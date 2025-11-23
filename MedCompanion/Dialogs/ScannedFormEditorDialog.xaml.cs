using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using MedCompanion.Models;
using MedCompanion.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using PdfSharp.Pdf;
using PdfSharp.Drawing;
using SD = System.Drawing; // Alias for System.Drawing to avoid conflicts with WPF
using SkiaSharp;

namespace MedCompanion.Dialogs
{
    public partial class ScannedFormEditorDialog : Window
    {
        #region Inner Classes

        /// <summary>
        /// Represents an editable zone with its visual elements (rectangle, resize handles, text)
        /// </summary>
        private class EditableZone
        {
            public ScannedFormMetadata Metadata { get; set; }
            public Rectangle MainRectangle { get; set; }
            public TextBlock TextDisplay { get; set; }
            public List<Rectangle> ResizeHandles { get; set; } = new List<Rectangle>();
            public bool IsSelected { get; set; }
            
            // In-place editing support
            public TextBox? EditTextBox { get; set; }
            public bool IsEditing { get; set; }

            public EditableZone(ScannedFormMetadata metadata, Rectangle rectangle, TextBlock textBlock)
            {
                Metadata = metadata;
                MainRectangle = rectangle;
                TextDisplay = textBlock;
            }

            public void ShowHandles()
            {
                foreach (var handle in ResizeHandles)
                {
                    handle.Visibility = Visibility.Visible;
                }
            }

            public void HideHandles()
            {
                foreach (var handle in ResizeHandles)
                {
                    handle.Visibility = Visibility.Collapsed;
                }
            }

            public void UpdateSelection(bool selected)
            {
                IsSelected = selected;
                if (selected)
                {
                    MainRectangle.Stroke = Brushes.Blue;
                    MainRectangle.StrokeThickness = 3;
                    MainRectangle.Fill = new SolidColorBrush(Color.FromArgb(50, 0, 0, 255));
                    ShowHandles();
                }
                else
                {
                    MainRectangle.Stroke = Brushes.Red;
                    MainRectangle.StrokeThickness = 2;
                    MainRectangle.Fill = new SolidColorBrush(Color.FromArgb(30, 255, 0, 0));
                    HideHandles();
                }
            }

            public void UpdateText()
            {
                TextDisplay.Text = Metadata.FilledText;
            }

            public void UpdateTextPosition()
            {
                // Sync TextBlock position and size with rectangle
                Canvas.SetLeft(TextDisplay, Canvas.GetLeft(MainRectangle));
                Canvas.SetTop(TextDisplay, Canvas.GetTop(MainRectangle));
                TextDisplay.Width = MainRectangle.Width;
                TextDisplay.Height = MainRectangle.Height;
            }
        }

        #endregion

        #region Fields

        private readonly PatientIndexEntry _selectedPatient;
        private readonly PatientIndexService _patientIndex;
        private readonly FormulaireAssistantService _formulaireService;
        private readonly PathService _pathService = new PathService();
        private readonly string _pdfPath;

        private WebView2? _webView;
        private Canvas? _zoneCanvas;
        private bool _webViewInitialized = false;
        private double _currentZoom = 1.0;
        private double _offsetX = 0;
        private double _offsetY = 0;

        // Drawing mode
        private bool _isDrawingMode = false;
        private Point _dragStartPoint;
        private Rectangle? _currentRect;

        // Zone management
        private readonly List<EditableZone> _editableZones = new List<EditableZone>();
        private EditableZone? _selectedZone;

        // Drag & Drop
        private bool _isDragging = false;
        private Point _dragOffset;

        // Resizing
        private bool _isResizing = false;
        private Rectangle? _activeResizeHandle;
        private Point _resizeAnchor;
        private string _resizeDirection = "";

        private const double HandleSize = 8;
        private const double MinZoneSize = 20;

        #endregion

        #region Constructor & Initialization

        public ScannedFormEditorDialog(
            PatientIndexEntry selectedPatient,
            PatientIndexService patientIndex,
            FormulaireAssistantService formulaireService,
            string pdfPath)
        {
            InitializeComponent();

            _selectedPatient = selectedPatient;
            _patientIndex = patientIndex;
            _formulaireService = formulaireService;
            _pdfPath = pdfPath;

            Loaded += ScannedFormEditorDialog_Loaded;
            KeyDown += Window_KeyDown;
        }

        private async void ScannedFormEditorDialog_Loaded(object sender, RoutedEventArgs e)
        {
            LoadPatientInfo();
            await InitializePdfViewerAsync();
            LoadExistingZones();
            UpdateZoneCounter();
        }

        private void LoadPatientInfo()
        {
            var metadata = _patientIndex.GetMetadata(_selectedPatient.Id);
            if (metadata != null)
            {
                PatientPrenomText.Text = metadata.Prenom ?? "Non renseigné";
                PatientNomText.Text = metadata.Nom ?? "Non renseigné";

                if (!string.IsNullOrEmpty(metadata.Dob) && DateTime.TryParse(metadata.Dob, out var dob))
                {
                    PatientDobText.Text = dob.ToString("dd/MM/yyyy");
                }
                else
                {
                    PatientDobText.Text = "Non renseignée";
                }
            }
            else
            {
                PatientPrenomText.Text = _selectedPatient.Prenom;
                PatientNomText.Text = _selectedPatient.Nom;
                PatientDobText.Text = "Non renseignée";
            }
        }

        private async Task InitializePdfViewerAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(_pdfPath))
                    {
                        Dispatcher.Invoke(() =>
                        {
                            PdfFallbackMessage.Text = "❌ PDF introuvable";
                            PdfFallbackMessage.Foreground = Brushes.Red;
                        });
                        return;
                    }

                    // Render PDF to images using PDFtoImage v5.0
                    // Read PDF file as bytes
                    byte[] pdfBytes = File.ReadAllBytes(_pdfPath);
                    
                    // Get total page count
                    int pageCount = PDFtoImage.Conversion.GetPageCount(pdfBytes);
                    System.Diagnostics.Debug.WriteLine($"[PDF] Total pages: {pageCount}");

                    Dispatcher.Invoke(() =>
                    {
                        // Create a StackPanel to hold all pages vertically
                        var pagesStack = new StackPanel
                        {
                            Orientation = Orientation.Vertical,
                            HorizontalAlignment = HorizontalAlignment.Center
                        };

                        double currentY = 0; // Track vertical offset for zones

                        // Render each page
                        for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
                        {
                            try
                            {
                                // Convert page to SkiaSharp bitmap
                                var skBitmap = PDFtoImage.Conversion.ToImage(pdfBytes, pageIndex);
                                
                                if (skBitmap != null)
                                {
                                    // Convert SkiaSharp bitmap to WPF BitmapSource
                                    var bitmapSource = ConvertSkBitmapToBitmapSource(skBitmap);

                                    // Create Image control to display the page
                                    var pageImage = new System.Windows.Controls.Image
                                    {
                                        Source = bitmapSource,
                                        Stretch = Stretch.Uniform,
                                        HorizontalAlignment = HorizontalAlignment.Center,
                                        Margin = new Thickness(0, pageIndex > 0 ? 10 : 0, 0, 0), // Add spacing between pages
                                        Tag = new { PageIndex = pageIndex, OffsetY = currentY } // Store page info
                                    };

                                    pagesStack.Children.Add(pageImage);

                                    // Update offset for next page (add image height + margin)
                                    currentY += bitmapSource.Height + (pageIndex > 0 ? 10 : 0);

                                    skBitmap.Dispose();
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[PDF] Error rendering page {pageIndex}: {ex.Message}");
                            }
                        }

                        // Add the pages stack to the container
                        Panel.SetZIndex(pagesStack, 0);
                        PdfViewerContainer.Children.Add(pagesStack);

                        // Create Canvas overlay for zones - must stretch to cover entire PDF area
                        _zoneCanvas = new Canvas
                        {
                            Background = Brushes.Transparent,
                            IsHitTestVisible = true,
                            HorizontalAlignment = HorizontalAlignment.Stretch,
                            VerticalAlignment = VerticalAlignment.Stretch
                        };
                        Panel.SetZIndex(_zoneCanvas, 100); // Canvas on top layer to capture mouse events
                        PdfViewerContainer.Children.Add(_zoneCanvas);

                        // Debug: Log when canvas size changes
                        _zoneCanvas.SizeChanged += (s, args) =>
                        {
                            System.Diagnostics.Debug.WriteLine($"[Canvas] Size changed: {args.NewSize.Width}x{args.NewSize.Height}");
                        };

                        // Attach mouse events
                        _zoneCanvas.MouseLeftButtonDown += ZoneCanvas_MouseLeftButtonDown;
                        _zoneCanvas.MouseMove += ZoneCanvas_MouseMove;
                        _zoneCanvas.MouseLeftButtonUp += ZoneCanvas_MouseLeftButtonUp;

                        System.Diagnostics.Debug.WriteLine($"[ScannedFormEditor] {pageCount} pages rendered, Canvas created and attached");

                        _webViewInitialized = true;

                        PdfFallbackMessage.Visibility = Visibility.Collapsed;
                        PdfZoomInButton.IsEnabled = true;
                        PdfZoomOutButton.IsEnabled = true;

                        // Calculate initial zoom to fit width
                        // We need to wait for layout update to get actual width
                        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(() =>
                        {
                            var firstPage = pagesStack.Children.OfType<Image>().FirstOrDefault();
                            if (firstPage != null && firstPage.Source != null)
                            {
                                double pageWidth = firstPage.Source.Width;
                                double containerWidth = PdfScrollViewer.ActualWidth;
                                
                                if (containerWidth > 0 && pageWidth > 0)
                                {
                                    // Calculate zoom to fit width with some margin (e.g. 40px)
                                    double fitZoom = (containerWidth - 40) / pageWidth;
                                    
                                    // User requested one step less than fit width (approx -0.15 for comfort)
                                    fitZoom -= 0.15;

                                    // Ensure reasonable bounds (e.g. not too small)
                                    if (fitZoom < 0.3) fitZoom = 0.3;
                                    if (fitZoom > 1.0) fitZoom = 1.0; // Don't zoom in more than 100% initially
                                    
                                    SetZoom(fitZoom);
                                }
                                else
                                {
                                    // Fallback if dimensions not available
                                    SetZoom(0.75);
                                }
                            }
                        }));
                    });
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() =>
                    {
                        PdfFallbackMessage.Text = $"⚠ Erreur chargement PDF : {ex.Message}";
                        PdfFallbackMessage.Foreground = Brushes.Orange;
                        System.Diagnostics.Debug.WriteLine($"[PDF Error] {ex}");
                    });
                }
            });
        }

        private System.Windows.Media.Imaging.BitmapSource ConvertSkBitmapToBitmapSource(SkiaSharp.SKBitmap skBitmap)
        {
            using (var image = SkiaSharp.SKImage.FromBitmap(skBitmap))
            using (var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100))
            {
                var memoryStream = new MemoryStream();
                data.SaveTo(memoryStream);
                memoryStream.Seek(0, SeekOrigin.Begin);

                var bitmapImage = new System.Windows.Media.Imaging.BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmapImage.StreamSource = memoryStream;
                bitmapImage.EndInit();
                bitmapImage.Freeze();

                return bitmapImage;
            }
        }

        #endregion

        #region Zone Loading & Creation

        private void LoadExistingZones()
        {
            try
            {
                var jsonPath = System.IO.Path.ChangeExtension(_pdfPath, ".json");
                if (!File.Exists(jsonPath)) return;

                var json = File.ReadAllText(jsonPath);
                var container = System.Text.Json.JsonSerializer.Deserialize<ScannedFormMetadataContainer>(json);

                if (container?.Zones != null)
                {
                    foreach (var zone in container.Zones)
                    {
                        CreateEditableZone(zone);
                    }
                    
                    // Load offsets and update TextBoxes
                    _offsetX = container.OffsetX;
                    _offsetY = container.OffsetY;
                    
                    if (OffsetXTextBox != null)
                    {
                        // Convert pixels to cm for display
                        double cmX = _offsetX / 37.8;
                        OffsetXTextBox.Text = cmX.ToString("F2");
                    }
                    if (OffsetYTextBox != null)
                    {
                        // Convert pixels to cm for display
                        double cmY = _offsetY / 37.8;
                        OffsetYTextBox.Text = cmY.ToString("F2");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du chargement des zones : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private EditableZone CreateEditableZone(ScannedFormMetadata metadata)
        {
            if (_zoneCanvas == null) return null!;

            var rect = new Rectangle
            {
                Stroke = Brushes.Red,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(30, 255, 0, 0)),
                Width = metadata.Rectangle.Width,
                Height = metadata.Rectangle.Height,
                Cursor = Cursors.Hand
            };

            Canvas.SetLeft(rect, metadata.Rectangle.X);
            Canvas.SetTop(rect, metadata.Rectangle.Y);

            // Create TextBlock to display text inside the zone
            var textBlock = new TextBlock
            {
                Text = metadata.FilledText,
                Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 139)), // DarkBlue
                FontSize = metadata.FontSize,
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(5),
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = metadata.Rectangle.Width,
                Height = metadata.Rectangle.Height,
                IsHitTestVisible = false // Don't capture mouse events
            };

            Canvas.SetLeft(textBlock, metadata.Rectangle.X);
            Canvas.SetTop(textBlock, metadata.Rectangle.Y);

            var editableZone = new EditableZone(metadata, rect, textBlock);

            // Create resize handles
            CreateResizeHandles(editableZone);

            // Add to canvas (rectangle first, then text on top)
            _zoneCanvas.Children.Add(rect);
            _zoneCanvas.Children.Add(textBlock);

            // Add event handlers
            rect.MouseLeftButtonDown += (s, e) => Zone_MouseLeftButtonDown(editableZone, e);
            rect.MouseEnter += (s, e) => { if (!_isDrawingMode) rect.Cursor = Cursors.Hand; };
            
            // Add context menu
            rect.ContextMenu = CreateZoneContextMenu(editableZone);

            _editableZones.Add(editableZone);

            return editableZone;
        }
        
        private ContextMenu CreateZoneContextMenu(EditableZone zone)
        {
            var menu = new ContextMenu();
            
            // Font size submenu
            var fontSizeMenu = new MenuItem { Header = "📏 Taille de police" };
            foreach (var size in new[] { 12, 18, 24, 36, 48 })
            {
                var item = new MenuItem { Header = $"{size} pt" };
                item.Click += (s, e) =>
                {
                    zone.Metadata.FontSize = size;
                    if (zone.TextDisplay != null)
                    {
                        zone.TextDisplay.FontSize = size;
                    }
                    UpdateSelectedZoneInfo();
                };
                fontSizeMenu.Items.Add(item);
            }
            menu.Items.Add(fontSizeMenu);
            
            menu.Items.Add(new Separator());
            
            // Edit text
            var editItem = new MenuItem { Header = "✏️ Éditer le texte" };
            editItem.Click += (s, e) => EditZoneText(zone);
            menu.Items.Add(editItem);
            
            // Paste
            var pasteItem = new MenuItem { Header = "📋 Coller (Ctrl+V)" };
            pasteItem.Click += (s, e) => PasteIntoZone(zone);
            menu.Items.Add(pasteItem);
            
            menu.Items.Add(new Separator());
            
            // Delete zone
            var deleteItem = new MenuItem { Header = "🗑️ Supprimer" };
            deleteItem.Click += (s, e) =>
            {
                _selectedZone = zone;
                DeleteSelectedZone();
            };
            menu.Items.Add(deleteItem);
            
            return menu;
        }
        
        private void PasteIntoZone(EditableZone zone)
        {
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                zone.Metadata.FilledText = text;
                zone.UpdateText();
                UpdateSelectedZoneInfo();
            }
        }

        private void CreateResizeHandles(EditableZone zone)
        {
            if (_zoneCanvas == null) return;

            var positions = new[] { "NW", "N", "NE", "E", "SE", "S", "SW", "W" };
            var cursors = new Dictionary<string, Cursor>
            {
                { "NW", Cursors.SizeNWSE }, { "SE", Cursors.SizeNWSE },
                { "NE", Cursors.SizeNESW }, { "SW", Cursors.SizeNESW },
                { "N", Cursors.SizeNS }, { "S", Cursors.SizeNS },
                { "E", Cursors.SizeWE }, { "W", Cursors.SizeWE }
            };

            foreach (var pos in positions)
            {
                var handle = new Rectangle
                {
                    Width = HandleSize,
                    Height = HandleSize,
                    Fill = Brushes.White,
                    Stroke = Brushes.Blue,
                    StrokeThickness = 2,
                    Cursor = cursors[pos],
                    Visibility = Visibility.Collapsed,
                    Tag = pos
                };

                handle.MouseLeftButtonDown += (s, e) => ResizeHandle_MouseLeftButtonDown(zone, handle, e);

                _zoneCanvas.Children.Add(handle);
                zone.ResizeHandles.Add(handle);
            }

            UpdateResizeHandles(zone);
        }

        private void UpdateResizeHandles(EditableZone zone)
        {
            if (zone.ResizeHandles.Count != 8) return;

            var rect = zone.MainRectangle;
            var left = Canvas.GetLeft(rect);
            var top = Canvas.GetTop(rect);
            var right = left + rect.Width;
            var bottom = top + rect.Height;
            var centerX = left + rect.Width / 2;
            var centerY = top + rect.Height / 2;

            var positions = new Dictionary<string, Point>
            {
                { "NW", new Point(left - HandleSize/2, top - HandleSize/2) },
                { "N", new Point(centerX - HandleSize/2, top - HandleSize/2) },
                { "NE", new Point(right - HandleSize/2, top - HandleSize/2) },
                { "E", new Point(right - HandleSize/2, centerY - HandleSize/2) },
                { "SE", new Point(right - HandleSize/2, bottom - HandleSize/2) },
                { "S", new Point(centerX - HandleSize/2, bottom - HandleSize/2) },
                { "SW", new Point(left - HandleSize/2, bottom - HandleSize/2) },
                { "W", new Point(left - HandleSize/2, centerY - HandleSize/2) }
            };

            foreach (var handle in zone.ResizeHandles)
            {
                var pos = handle.Tag as string;
                if (pos != null && positions.ContainsKey(pos))
                {
                    Canvas.SetLeft(handle, positions[pos].X);
                    Canvas.SetTop(handle, positions[pos].Y);
                }
            }
        }

        #endregion

        #region Zone Selection & Deletion

        private void SelectZone(EditableZone zone)
        {
            // Deselect previous zone
            if (_selectedZone != null && _selectedZone != zone)
            {
                _selectedZone.UpdateSelection(false);
            }

            _selectedZone = zone;
            zone.UpdateSelection(true);

            // Update info panel
            UpdateSelectedZoneInfo();
        }

        private void DeselectZone()
        {
            if (_selectedZone != null)
            {
                _selectedZone.UpdateSelection(false);
                _selectedZone = null;
            }

            // Clear info panel
            SelectedZonePanel.Visibility = Visibility.Collapsed;
        }

        private void UpdateSelectedZoneInfo()
        {
            if (_selectedZone == null)
            {
                SelectedZonePanel.Visibility = Visibility.Collapsed;
                return;
            }

            SelectedZonePanel.Visibility = Visibility.Visible;
            var rect = _selectedZone.Metadata.Rectangle;
            ZonePositionText.Text = $"X: {rect.X:F0}, Y: {rect.Y:F0}";
            ZoneSizeText.Text = $"L: {rect.Width:F0}, H: {rect.Height:F0}";
            ZoneTextPreview.Text = string.IsNullOrEmpty(_selectedZone.Metadata.FilledText) 
                ? "(vide)" 
                : _selectedZone.Metadata.FilledText;

            // Update Font Size ComboBox
            if (ZoneFontSizeCombo != null)
            {
                // Temporarily remove handler to avoid triggering change event
                ZoneFontSizeCombo.SelectionChanged -= ZoneFontSizeCombo_SelectionChanged;

                foreach (ComboBoxItem item in ZoneFontSizeCombo.Items)
                {
                    if (item.Tag != null && double.TryParse(item.Tag.ToString(), out double size))
                    {
                        if (Math.Abs(size - _selectedZone.Metadata.FontSize) < 0.1)
                        {
                            ZoneFontSizeCombo.SelectedItem = item;
                            break;
                        }
                    }
                }

                ZoneFontSizeCombo.SelectionChanged += ZoneFontSizeCombo_SelectionChanged;
            }
        }

        private void ZoneFontSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_selectedZone == null || ZoneFontSizeCombo.SelectedItem == null) return;

            if (ZoneFontSizeCombo.SelectedItem is ComboBoxItem item && item.Tag != null && double.TryParse(item.Tag.ToString(), out double newSize))
            {
                _selectedZone.Metadata.FontSize = newSize;
                
                // Update TextBlock if accessible
                if (_selectedZone.TextDisplay != null)
                {
                    _selectedZone.TextDisplay.FontSize = newSize;
                }
            }
        }

        private void DeleteSelectedZone()
        {
            if (_selectedZone == null || _zoneCanvas == null) return;

            var result = MessageBox.Show(
                "Êtes-vous sûr de vouloir supprimer cette zone ?",
                "Confirmer la suppression",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            // Remove from canvas
            _zoneCanvas.Children.Remove(_selectedZone.MainRectangle);
            foreach (var handle in _selectedZone.ResizeHandles)
            {
                _zoneCanvas.Children.Remove(handle);
            }

            // Remove from list
            _editableZones.Remove(_selectedZone);
            _selectedZone = null;

            // Update UI
            SelectedZonePanel.Visibility = Visibility.Collapsed;
            UpdateZoneCounter();
        }

        private void UpdateZoneCounter()
        {
            var count = _editableZones.Count;
            var text = count == 0 ? "Aucune zone" : count == 1 ? "1 zone" : $"{count} zones";
            
            // Update left panel counter
            if (ZoneCountText != null)
            {
                ZoneCountText.Text = text;
            }
            
            // Update PDF toolbar counter
            if (PdfZoneCountText != null)
            {
                PdfZoneCountText.Text = text;
            }
        }

        #endregion

        #region Mouse Event Handlers

        private void Zone_MouseLeftButtonDown(EditableZone zone, MouseButtonEventArgs e)
        {
            if (_isDrawingMode) return;

            e.Handled = true;

            // Double-click to edit text
            if (e.ClickCount == 2)
            {
                EditZoneText(zone);
                return;
            }

            // Single click to select and start dragging
            SelectZone(zone);
            _isDragging = true;
            _dragOffset = e.GetPosition(zone.MainRectangle);
            zone.MainRectangle.CaptureMouse();
        }

        private void ResizeHandle_MouseLeftButtonDown(EditableZone zone, Rectangle handle, MouseButtonEventArgs e)
        {
            if (_isDrawingMode) return;

            e.Handled = true;

            SelectZone(zone);
            _isResizing = true;
            _activeResizeHandle = handle;
            _resizeDirection = handle.Tag as string ?? "";

            var rect = zone.MainRectangle;
            var left = Canvas.GetLeft(rect);
            var top = Canvas.GetTop(rect);

            // Set anchor point (opposite corner/side)
            _resizeAnchor = _resizeDirection switch
            {
                "NW" => new Point(left + rect.Width, top + rect.Height),
                "N" => new Point(left, top + rect.Height),
                "NE" => new Point(left, top + rect.Height),
                "E" => new Point(left, top),
                "SE" => new Point(left, top),
                "S" => new Point(left, top),
                "SW" => new Point(left + rect.Width, top),
                "W" => new Point(left + rect.Width, top),
                _ => new Point(left, top)
            };

            handle.CaptureMouse();
        }

        private void ZoneCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isDrawingMode || _zoneCanvas == null) 
            {
                System.Diagnostics.Debug.WriteLine($"[MouseDown] Ignored - DrawingMode: {_isDrawingMode}, Canvas: {_zoneCanvas != null}");
                return;
            }

            System.Diagnostics.Debug.WriteLine("[MouseDown] Starting to draw zone");

            // Deselect any selected zone when starting to draw
            DeselectZone();

            _dragStartPoint = e.GetPosition(_zoneCanvas);
            System.Diagnostics.Debug.WriteLine($"[MouseDown] Start point: {_dragStartPoint.X}, {_dragStartPoint.Y}");
            
            _currentRect = new Rectangle
            {
                Stroke = Brushes.Red,
                StrokeThickness = 2,
                Fill = new SolidColorBrush(Color.FromArgb(30, 255, 0, 0))
            };

            Canvas.SetLeft(_currentRect, _dragStartPoint.X);
            Canvas.SetTop(_currentRect, _dragStartPoint.Y);
            _zoneCanvas.Children.Add(_currentRect);
            _zoneCanvas.CaptureMouse();
            
            System.Diagnostics.Debug.WriteLine("[MouseDown] Rectangle created and added to canvas");
        }

        private void ZoneCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_zoneCanvas == null) return;

            var pos = e.GetPosition(_zoneCanvas);

            // Handle drawing new zone
            if (_currentRect != null)
            {
                var x = Math.Min(pos.X, _dragStartPoint.X);
                var y = Math.Min(pos.Y, _dragStartPoint.Y);
                var w = Math.Abs(pos.X - _dragStartPoint.X);
                var h = Math.Abs(pos.Y - _dragStartPoint.Y);

                Canvas.SetLeft(_currentRect, x);
                Canvas.SetTop(_currentRect, y);
                _currentRect.Width = w;
                _currentRect.Height = h;
                return;
            }

            // Handle dragging zone
            if (_isDragging && _selectedZone != null)
            {
                var rect = _selectedZone.MainRectangle;
                var newLeft = pos.X - _dragOffset.X;
                var newTop = pos.Y - _dragOffset.Y;

                // Keep within canvas bounds
                newLeft = Math.Max(0, Math.Min(newLeft, _zoneCanvas.ActualWidth - rect.Width));
                newTop = Math.Max(0, Math.Min(newTop, _zoneCanvas.ActualHeight - rect.Height));

                Canvas.SetLeft(rect, newLeft);
                Canvas.SetTop(rect, newTop);

                _selectedZone.UpdateTextPosition(); // Sync TextBlock position
                UpdateResizeHandles(_selectedZone);
                UpdateSelectedZoneInfo();
                return;
            }

            // Handle resizing zone
            if (_isResizing && _selectedZone != null)
            {
                var rect = _selectedZone.MainRectangle;
                
                double newLeft = Canvas.GetLeft(rect);
                double newTop = Canvas.GetTop(rect);
                double newWidth = rect.Width;
                double newHeight = rect.Height;

                switch (_resizeDirection)
                {
                    case "NW":
                        newLeft = Math.Min(pos.X, _resizeAnchor.X - MinZoneSize);
                        newTop = Math.Min(pos.Y, _resizeAnchor.Y - MinZoneSize);
                        newWidth = _resizeAnchor.X - newLeft;
                        newHeight = _resizeAnchor.Y - newTop;
                        break;
                    case "N":
                        newTop = Math.Min(pos.Y, _resizeAnchor.Y - MinZoneSize);
                        newHeight = _resizeAnchor.Y - newTop;
                        break;
                    case "NE":
                        newTop = Math.Min(pos.Y, _resizeAnchor.Y - MinZoneSize);
                        newWidth = Math.Max(MinZoneSize, pos.X - _resizeAnchor.X);
                        newHeight = _resizeAnchor.Y - newTop;
                        break;
                    case "E":
                        newWidth = Math.Max(MinZoneSize, pos.X - _resizeAnchor.X);
                        break;
                    case "SE":
                        newWidth = Math.Max(MinZoneSize, pos.X - _resizeAnchor.X);
                        newHeight = Math.Max(MinZoneSize, pos.Y - _resizeAnchor.Y);
                        break;
                    case "S":
                        newHeight = Math.Max(MinZoneSize, pos.Y - _resizeAnchor.Y);
                        break;
                    case "SW":
                        newLeft = Math.Min(pos.X, _resizeAnchor.X - MinZoneSize);
                        newWidth = _resizeAnchor.X - newLeft;
                        newHeight = Math.Max(MinZoneSize, pos.Y - _resizeAnchor.Y);
                        break;
                    case "W":
                        newLeft = Math.Min(pos.X, _resizeAnchor.X - MinZoneSize);
                        newWidth = _resizeAnchor.X - newLeft;
                        break;
                }

                Canvas.SetLeft(rect, newLeft);
                Canvas.SetTop(rect, newTop);
                rect.Width = newWidth;
                rect.Height = newHeight;

                _selectedZone.UpdateTextPosition(); // Sync TextBlock size and position
                UpdateResizeHandles(_selectedZone);
                UpdateSelectedZoneInfo();
            }
        }

        private void ZoneCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_zoneCanvas == null) return;

            // Handle drawing completion
            if (_currentRect != null)
            {
                _zoneCanvas.ReleaseMouseCapture();

                // Only create zone if it has meaningful size
                if (_currentRect.Width > 10 && _currentRect.Height > 10)
                {
                    var metadata = new ScannedFormMetadata
                    {
                        Page = 1,
                        Rectangle = new Rect(
                            Canvas.GetLeft(_currentRect),
                            Canvas.GetTop(_currentRect),
                            _currentRect.Width,
                            _currentRect.Height
                        ),
                        PlaceholderText = "",
                        FilledText = ""
                    };

                    // Remove temporary rectangle
                    _zoneCanvas.Children.Remove(_currentRect);

                    // Create editable zone
                    var editableZone = CreateEditableZone(metadata);
                    SelectZone(editableZone);

                    UpdateZoneCounter();
                }
                else
                {
                    _zoneCanvas.Children.Remove(_currentRect);
                }

                _currentRect = null;

                // Exit drawing mode
                _isDrawingMode = false;
                AddZoneButton.Content = "➕ Ajouter zone texte";
                AddZoneButton.Background = new SolidColorBrush(Color.FromRgb(155, 89, 182));
                _zoneCanvas.Cursor = Cursors.Arrow;
                return;
            }

            // Handle drag completion
            if (_isDragging && _selectedZone != null)
            {
                _selectedZone.MainRectangle.ReleaseMouseCapture();
                
                // Update metadata
                var rect = _selectedZone.MainRectangle;
                _selectedZone.Metadata.Rectangle = new Rect(
                    Canvas.GetLeft(rect),
                    Canvas.GetTop(rect),
                    rect.Width,
                    rect.Height
                );

                _isDragging = false;
                return;
            }

            // Handle resize completion
            if (_isResizing && _selectedZone != null && _activeResizeHandle != null)
            {
                _activeResizeHandle.ReleaseMouseCapture();

                // Update metadata
                var rect = _selectedZone.MainRectangle;
                _selectedZone.Metadata.Rectangle = new Rect(
                    Canvas.GetLeft(rect),
                    Canvas.GetTop(rect),
                    rect.Width,
                    rect.Height
                );

                _isResizing = false;
                _activeResizeHandle = null;
            }
        }

        #endregion

        #region Zone Editing

        private void EditZoneText(EditableZone zone)
        {
            if (_zoneCanvas == null || zone.IsEditing) return;

            // Enter edit mode
            zone.IsEditing = true;

            // Create TextBox overlay
            var editBox = new TextBox
            {
                Text = zone.Metadata.FilledText,
                FontSize = zone.Metadata.FontSize,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brushes.White,
                BorderBrush = Brushes.Blue,
                BorderThickness = new Thickness(2),
                Padding = new Thickness(2)
            };

            // Position and size to match zone
            Canvas.SetLeft(editBox, Canvas.GetLeft(zone.MainRectangle));
            Canvas.SetTop(editBox, Canvas.GetTop(zone.MainRectangle));
            editBox.Width = zone.MainRectangle.Width;
            editBox.Height = zone.MainRectangle.Height;

            // Store reference
            zone.EditTextBox = editBox;

            // Hide TextBlock, show TextBox
            zone.TextDisplay.Visibility = Visibility.Collapsed;
            _zoneCanvas.Children.Add(editBox);

            // Focus and select all
            editBox.Focus();
            editBox.SelectAll();

            // Handle exit edit mode
            editBox.LostFocus += (s, e) => ExitEditMode(zone);
            editBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    // Cancel editing
                    zone.Metadata.FilledText = zone.Metadata.FilledText; // Keep original
                    ExitEditMode(zone);
                    e.Handled = true;
                }
                else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
                {
                    // Save and exit (Shift+Enter for new line)
                    zone.Metadata.FilledText = editBox.Text;
                    ExitEditMode(zone);
                    e.Handled = true;
                }
            };
        }

        private void ExitEditMode(EditableZone zone)
        {
            if (!zone.IsEditing || zone.EditTextBox == null || _zoneCanvas == null) return;

            // Save text
            zone.Metadata.FilledText = zone.EditTextBox.Text;

            // Remove TextBox
            _zoneCanvas.Children.Remove(zone.EditTextBox);
            zone.EditTextBox = null;
            zone.IsEditing = false;

            // Show TextBlock
            zone.TextDisplay.Visibility = Visibility.Visible;
            zone.UpdateText();
            UpdateSelectedZoneInfo();
        }

        #endregion

        #region Keyboard Handlers

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && _selectedZone != null)
            {
                DeleteSelectedZone();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && _selectedZone != null)
            {
                DeselectZone();
                e.Handled = true;
            }
            else if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) != 0 && _selectedZone != null)
            {
                // Ctrl+V to paste into selected zone
                PasteIntoZone(_selectedZone);
                e.Handled = true;
            }
        }

        #endregion

        #region Button Click Handlers

        private void AddZoneButton_Click(object sender, RoutedEventArgs e)
        {
            _isDrawingMode = !_isDrawingMode;
            
            // Update button in toolbar
            if (PdfAddZoneButton != null)
            {
                PdfAddZoneButton.Content = _isDrawingMode ? "✅ Mode dessin activé" : "➕ Ajouter zone texte";
                PdfAddZoneButton.Background = _isDrawingMode 
                    ? new SolidColorBrush(Color.FromRgb(39, 174, 96)) 
                    : new SolidColorBrush(Color.FromRgb(155, 89, 182));
            }
            
            // Also update the button in left panel if it exists
            if (AddZoneButton != null)
            {
                AddZoneButton.Content = _isDrawingMode ? "✅ Mode dessin activé" : "➕ Ajouter zone texte";
                AddZoneButton.Background = _isDrawingMode 
                    ? new SolidColorBrush(Color.FromRgb(39, 174, 96)) 
                    : new SolidColorBrush(Color.FromRgb(155, 89, 182));
            }
            
            if (_zoneCanvas != null)
            {
                _zoneCanvas.Cursor = _isDrawingMode ? Cursors.Cross : Cursors.Arrow;
                System.Diagnostics.Debug.WriteLine($"[DrawingMode] Mode: {_isDrawingMode}, Canvas cursor: {(_isDrawingMode ? "Cross" : "Arrow")}");
            }

            // Deselect zone when entering drawing mode
            if (_isDrawingMode)
            {
                DeselectZone();
            }
        }

        private void EditZoneButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedZone != null)
            {
                EditZoneText(_selectedZone);
            }
        }

        private void DeleteZoneButton_Click(object sender, RoutedEventArgs e)
        {
            DeleteSelectedZone();
        }

        private async void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            var instruction = InstructionTextBox.Text.Trim();
            if (string.IsNullOrEmpty(instruction))
            {
                MessageBox.Show("Veuillez entrer une instruction pour l'IA.", "Instruction manquante", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                GenerateButton.IsEnabled = false;
                GenerateButton.Content = "⏳ Génération en cours...";
                StatusText.Text = "";
                ResponseTextBox.Text = "";

                var style = (StyleComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Standard";
                var length = (LengthComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Moyen";

                var metadata = _patientIndex.GetMetadata(_selectedPatient.Id);
                if (metadata == null)
                {
                    MessageBox.Show("Impossible de charger les données du patient.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var response = await _formulaireService.GenerateCustomContent(metadata, instruction, style, length);

                ResponseTextBox.Text = response;
                CopyResponseButton.IsEnabled = true;
                StatusText.Text = "✅ Réponse générée !";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la génération : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                GenerateButton.IsEnabled = true;
                GenerateButton.Content = "✨ Générer avec l'IA";
            }
        }

        private struct PageLayoutInfo
        {
            public double Width;
            public double Height;
            public double OffsetY;
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveButton.IsEnabled = false;
                SaveButton.Content = "⏳ Sauvegarde...";

                // Capture UI data on UI thread
                var stackPanel = PdfViewerContainer.Children.OfType<StackPanel>().FirstOrDefault();
                if (stackPanel == null) throw new Exception("Impossible de retrouver les pages affichées.");

                var pageLayouts = new List<PageLayoutInfo>();
                foreach (var child in stackPanel.Children.OfType<Image>())
                {
                    dynamic tag = child.Tag;
                    pageLayouts.Add(new PageLayoutInfo 
                    { 
                        Width = child.ActualWidth, 
                        Height = child.ActualHeight, 
                        OffsetY = tag.OffsetY 
                    });
                }

                // 1. Save JSON (Metadata)
                var jsonPath = System.IO.Path.ChangeExtension(_pdfPath, ".json");
                var zones = _editableZones.Select(z => z.Metadata).ToList();
                var container = new ScannedFormMetadataContainer 
                { 
                    Zones = zones,
                    OffsetX = _offsetX,
                    OffsetY = _offsetY
                };
                var json = System.Text.Json.JsonSerializer.Serialize(container, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(jsonPath, json);

                // 2. Generate Final PDF (Async)
                await Task.Run(() => GenerateFinalPdf(zones, pageLayouts));

                MessageBox.Show($"✅ Formulaire enregistré avec succès !\n\nFichier généré : {System.IO.Path.GetFileNameWithoutExtension(_pdfPath)}_rempli.pdf", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la sauvegarde : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                SaveButton.IsEnabled = true;
                SaveButton.Content = "💾 Enregistrer";
            }
        }

        private void GenerateFinalPdf(List<ScannedFormMetadata> zones, List<PageLayoutInfo> pageLayouts)
        {
            string outputPath = System.IO.Path.ChangeExtension(_pdfPath, "_rempli.pdf");
            byte[] pdfBytes = File.ReadAllBytes(_pdfPath);
            int pageCount = PDFtoImage.Conversion.GetPageCount(pdfBytes);

            // Create new PDF document
            using var document = new PdfDocument();

            for (int i = 0; i < pageCount; i++)
            {
                // 1. Render page to image
                using var skBitmap = PDFtoImage.Conversion.ToImage(pdfBytes, i);
                
                // Convert SkiaSharp bitmap to System.Drawing.Bitmap
                using var ms = new MemoryStream();
                skBitmap.Encode(ms, SkiaSharp.SKEncodedImageFormat.Png, 100);
                ms.Seek(0, SeekOrigin.Begin);
                using var bitmap = new SD.Bitmap(ms);
                
                using var graphics = SD.Graphics.FromImage(bitmap);
                graphics.SmoothingMode = SD.Drawing2D.SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = SD.Text.TextRenderingHint.AntiAlias;

                // 2. Find zones for this page using pre-captured layout info
                if (i < pageLayouts.Count)
                {
                    var layout = pageLayouts[i];
                    double displayWidth = layout.Width;
                    double displayHeight = layout.Height;
                    double pageOffsetY = layout.OffsetY;

                    double scaleX = bitmap.Width / displayWidth;
                    double scaleY = bitmap.Height / displayHeight;

                    // Filter zones that overlap with this page
                    foreach (var zone in zones)
                    {
                        // Check if zone center is on this page
                        double zoneCenterY = zone.Rectangle.Y + (zone.Rectangle.Height / 2);
                        
                        if (zoneCenterY >= pageOffsetY && zoneCenterY < (pageOffsetY + displayHeight))
                        {
                            // Calculate relative position
                            double relX = zone.Rectangle.X;
                            double relY = zone.Rectangle.Y - pageOffsetY;
                            
                            // Apply automatic default offset (12.7 cm = ~480 pixels)
                            // This compensates for the systematic offset observed in PDF rendering
                            const double DEFAULT_OFFSET_CM = 12.7;
                            const double PIXELS_PER_CM = 37.8; // 96 DPI standard
                            double defaultOffsetPixels = DEFAULT_OFFSET_CM * PIXELS_PER_CM;
                            
                            // Apply default offset + user adjustments
                            relX -= (defaultOffsetPixels + _offsetX);
                            relY -= _offsetY;

                            // Scale to image coordinates
                            float imgX = (float)(relX * scaleX);
                            float imgY = (float)(relY * scaleY);
                            float imgW = (float)(zone.Rectangle.Width * scaleX);
                            float imgH = (float)(zone.Rectangle.Height * scaleY);

                            // Draw text
                            if (!string.IsNullOrEmpty(zone.FilledText))
                            {
                                // Font scaling
                                float fontSize = (float)(zone.FontSize * scaleX); // Scale font size
                                using var font = new SD.Font("Arial", fontSize, SD.FontStyle.Regular);
                                using var brush = new SD.SolidBrush(SD.Color.DarkBlue);
                                
                                var rect = new SD.RectangleF(imgX, imgY, imgW, imgH);
                                graphics.DrawString(zone.FilledText, font, brush, rect);
                            }
                        }
                    }
                }

                // 3. Add to PDF
                // Save modified bitmap to stream
                using var pageMs = new MemoryStream();
                bitmap.Save(pageMs, SD.Imaging.ImageFormat.Png);
                pageMs.Seek(0, SeekOrigin.Begin);

                var page = document.AddPage();
                using var xImage = XImage.FromStream(pageMs);
                
                // Set page size to match image
                page.Width = xImage.PointWidth;
                page.Height = xImage.PointHeight;

                using var xGraphics = XGraphics.FromPdfPage(page);
                xGraphics.DrawImage(xImage, 0, 0);
            }

            document.Save(outputPath);
        }

        private void CopyResponseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(ResponseTextBox.Text))
            {
                Clipboard.SetText(ResponseTextBox.Text);
                StatusText.Text = "✅ Copié dans le presse-papier !";

                Task.Delay(2000).ContinueWith(_ =>
                {
                    Dispatcher.Invoke(() => StatusText.Text = "✅ Réponse générée !");
                });
            }
        }

        private void CopyPrenomButton_Click(object sender, RoutedEventArgs e) => Clipboard.SetText(PatientPrenomText.Text);
        private void CopyNomButton_Click(object sender, RoutedEventArgs e) => Clipboard.SetText(PatientNomText.Text);
        private void CopyDobButton_Click(object sender, RoutedEventArgs e) => Clipboard.SetText(PatientDobText.Text);
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        #region Zoom Handling

        private void PdfZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            SetZoom(_currentZoom + 0.1);
        }

        private void PdfZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            SetZoom(_currentZoom - 0.1);
        }

        private void SetZoom(double newZoom)
        {
            if (PdfViewerContainer == null) return;

            _currentZoom = newZoom;
            
            // Clamp zoom level
            if (_currentZoom < 0.2) _currentZoom = 0.2;
            if (_currentZoom > 5.0) _currentZoom = 5.0;

            // Apply scale transform
            var scaleTransform = new ScaleTransform(_currentZoom, _currentZoom);
            PdfViewerContainer.LayoutTransform = scaleTransform;
        }
        
        private void OffsetXTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (double.TryParse(OffsetXTextBox.Text, out double cm))
            {
                // Convert cm to pixels (37.8 pixels per cm at 96 DPI)
                _offsetX = cm * 37.8;
            }
        }
        
        private void OffsetYTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (double.TryParse(OffsetYTextBox.Text, out double cm))
            {
                // Convert cm to pixels (37.8 pixels per cm at 96 DPI)
                _offsetY = cm * 37.8;
            }
        }
        
        private void NumericTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Allow digits, minus sign, and decimal point
            var textBox = sender as TextBox;
            if (textBox == null) return;
            
            // Check if input is valid
            string newText = textBox.Text.Insert(textBox.SelectionStart, e.Text);
            
            // Allow minus only at the beginning
            if (e.Text == "-" && textBox.SelectionStart != 0)
            {
                e.Handled = true;
                return;
            }
            
            // Allow only one decimal point
            if (e.Text == "." && textBox.Text.Contains("."))
            {
                e.Handled = true;
                return;
            }
            
            // Check if it's a valid number character
            if (!char.IsDigit(e.Text, 0) && e.Text != "." && e.Text != "-")
            {
                e.Handled = true;
            }
        }

        #endregion

        #endregion
    }
}
