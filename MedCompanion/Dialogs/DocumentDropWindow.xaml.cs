using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MedCompanion.Services;

namespace MedCompanion.Dialogs;

public partial class DocumentDropWindow : Window
{
    private readonly string _patientName;
    private readonly string _patientDirectory;
    private readonly DocumentService _documentService;
    private readonly Action _onDocumentsImported;
    private int _totalImported = 0;
    
    public DocumentDropWindow(
        string patientName, 
        string patientDirectory, 
        DocumentService documentService,
        Action onDocumentsImported)
    {
        InitializeComponent();
        
        _patientName = patientName;
        _patientDirectory = patientDirectory;
        _documentService = documentService;
        _onDocumentsImported = onDocumentsImported;
        
        // Afficher le nom du patient
        PatientNameLabel.Text = $"Patient: {patientName}";
    }
    
    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            DropZone.Background = new SolidColorBrush(Color.FromRgb(187, 222, 251)); // Bleu clair
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }
    
    private void DropZone_DragLeave(object sender, DragEventArgs e)
    {
        DropZone.Background = new SolidColorBrush(Color.FromRgb(227, 242, 253)); // Bleu très clair
    }
    
    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        DropZone.Background = new SolidColorBrush(Color.FromRgb(227, 242, 253));
        
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            await ProcessFilesAsync(files);
        }
    }
    
    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Sélectionner des documents",
            Filter = "Tous les documents|*.pdf;*.docx;*.doc;*.jpg;*.jpeg;*.png;*.txt|" +
                    "PDF|*.pdf|" +
                    "Word|*.docx;*.doc|" +
                    "Images|*.jpg;*.jpeg;*.png|" +
                    "Texte|*.txt",
            Multiselect = true
        };
        
        if (openFileDialog.ShowDialog() == true)
        {
            await ProcessFilesAsync(openFileDialog.FileNames);
        }
    }
    
    private async Task ProcessFilesAsync(string[] filePaths)
    {
        if (filePaths == null || filePaths.Length == 0)
            return;
        
        // Créer une fenêtre de progression simple
        var progressWindow = new Window
        {
            Title = "Import en cours...",
            Width = 400,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow
        };
        
        var progressPanel = new StackPanel { Margin = new Thickness(20) };
        var progressText = new TextBlock 
        { 
            Text = "Traitement des documents...",
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var progressBar = new ProgressBar 
        { 
            Height = 25,
            IsIndeterminate = true
        };
        var detailText = new TextBlock 
        { 
            Text = "",
            FontSize = 12,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 10, 0, 0)
        };
        
        progressPanel.Children.Add(progressText);
        progressPanel.Children.Add(progressBar);
        progressPanel.Children.Add(detailText);
        progressWindow.Content = progressPanel;
        
        progressWindow.Show();
        
        int successCount = 0;
        int errorCount = 0;
        var errors = new System.Collections.Generic.List<string>();
        
        for (int i = 0; i < filePaths.Length; i++)
        {
            var filePath = filePaths[i];
            var fileName = Path.GetFileName(filePath);
            
            detailText.Text = $"Traitement: {fileName} ({i + 1}/{filePaths.Length})";
            await Task.Delay(100);
            
            try
            {
                var (success, document, message) = await _documentService.ImportDocumentAsync(
                    filePath, _patientDirectory);
                
                if (success)
                {
                    successCount++;
                    _totalImported++;
                }
                else
                {
                    errorCount++;
                    errors.Add($"{fileName}: {message}");
                }
            }
            catch (Exception ex)
            {
                errorCount++;
                errors.Add($"{fileName}: {ex.Message}");
            }
        }
        
        progressWindow.Close();
        
        // Mettre à jour le compteur
        UpdateCounter();
        
        // Afficher le résumé
        var summary = $"✅ {successCount} document(s) importé(s)";
        if (errorCount > 0)
        {
            summary += $"\n❌ {errorCount} erreur(s)";
            if (errors.Any())
            {
                summary += "\n\nDétails:\n" + string.Join("\n", errors.Take(5));
                if (errors.Count > 5)
                    summary += $"\n... et {errors.Count - 5} autre(s)";
            }
        }
        
        MessageBox.Show(summary, "Import terminé", 
            MessageBoxButton.OK, 
            errorCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        
        // Notifier MainWindow pour rafraîchir la liste
        _onDocumentsImported?.Invoke();
    }
    
    private void UpdateCounter()
    {
        if (_totalImported > 0)
        {
            CounterPanel.Visibility = Visibility.Visible;
            CounterText.Text = _totalImported == 1 
                ? "📊 1 document importé" 
                : $"📊 {_totalImported} documents importés";
        }
    }
    
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
