using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MedCompanion.Models;
using MedCompanion.Services;
using Microsoft.Win32;

namespace MedCompanion.Dialogs
{
    public partial class DocumentManagerDialog : Window
    {
        private readonly DocumentService _documentService;
        private readonly string _patientFolderPath;
        private List<PatientDocument> _allDocuments = new();
        private string _currentFilter = "all";
        
        public DocumentManagerDialog(DocumentService documentService, string patientFolderPath)
        {
            InitializeComponent();
            _documentService = documentService;
            _patientFolderPath = patientFolderPath;
            
            Loaded += DocumentManagerDialog_Loaded;
        }
        
        private async void DocumentManagerDialog_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadDocumentsAsync();
        }
        
        private async Task LoadDocumentsAsync()
        {
            try
            {
                _allDocuments = await _documentService.GetAllDocumentsAsync(_patientFolderPath);
                ApplyFilter(_currentFilter);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du chargement des documents: {ex.Message}", 
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void ApplyFilter(string filter)
        {
            _currentFilter = filter;
            
            var filtered = filter == "all" 
                ? _allDocuments 
                : _allDocuments.Where(d => d.Category == filter).ToList();
            
            if (DocumentsDataGrid != null)
            {
                DocumentsDataGrid.ItemsSource = filtered;
            }
            
            if (DocumentCountLabel != null)
            {
                var count = filtered.Count;
                DocumentCountLabel.Text = count == 0 ? "Aucun document" :
                                         count == 1 ? "1 document" :
                                         $"{count} documents";
            }
        }
        
        private void CategoriesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Ignorer si les contrôles ne sont pas encore chargés
            if (!IsLoaded)
                return;
                
            if (CategoriesListBox.SelectedItem is ListBoxItem item && item.Tag is string tag)
            {
                ApplyFilter(tag);
            }
        }
        
        #region Drag & Drop
        
        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                DropZone.Background = new SolidColorBrush(Color.FromRgb(187, 222, 251)); // Couleur plus claire
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }
        
        private void DropZone_DragLeave(object sender, DragEventArgs e)
        {
            DropZone.Background = new SolidColorBrush(Color.FromRgb(227, 242, 253)); // Couleur normale
        }
        
        private async void DropZone_Drop(object sender, DragEventArgs e)
        {
            DropZone.Background = new SolidColorBrush(Color.FromRgb(227, 242, 253)); // Couleur normale
            
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                await ProcessFilesAsync(files);
            }
        }
        
        #endregion
        
        #region Browse Files
        
        private async void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
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
        
        #endregion
        
        #region Process Files
        
        private async Task ProcessFilesAsync(string[] filePaths)
        {
            if (filePaths == null || filePaths.Length == 0)
                return;
            
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
            var errors = new List<string>();
            
            for (int i = 0; i < filePaths.Length; i++)
            {
                var filePath = filePaths[i];
                var fileName = Path.GetFileName(filePath);
                
                detailText.Text = $"Traitement: {fileName} ({i + 1}/{filePaths.Length})";
                await Task.Delay(100); // Laisser l'UI se mettre à jour
                
                try
                {
                    var (success, document, message) = await _documentService.ImportDocumentAsync(filePath, _patientFolderPath);
                    
                    if (success)
                    {
                        successCount++;
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
            
            // Recharger la liste
            await LoadDocumentsAsync();
            
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
        }
        
        #endregion
        
        #region Document Actions
        
        private void DocumentsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DocumentsDataGrid.SelectedItem is PatientDocument document)
            {
                OpenDocument(document);
            }
        }
        
        private void DocumentsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Activer les boutons si un document est sélectionné
            var isDocumentSelected = DocumentsDataGrid.SelectedItem != null;
            
            if (DeleteDocumentButton != null)
            {
                DeleteDocumentButton.IsEnabled = isDocumentSelected;
            }
            
            if (SynthesisButton != null)
            {
                SynthesisButton.IsEnabled = isDocumentSelected;
            }
        }
        
        private void OpenDocument(PatientDocument document)
        {
            try
            {
                if (File.Exists(document.FilePath))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = document.FilePath,
                        UseShellExecute = true
                    });
                }
                else
                {
                    MessageBox.Show("Le fichier n'existe plus à cet emplacement.", 
                        "Fichier introuvable", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Impossible d'ouvrir le document: {ex.Message}", 
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private async void DeleteDocumentButton_Click(object sender, RoutedEventArgs e)
        {
            if (DocumentsDataGrid.SelectedItem is not PatientDocument document)
            {
                MessageBox.Show("Veuillez sélectionner un document à supprimer.", 
                    "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            // Demander confirmation
            var result = MessageBox.Show(
                $"Êtes-vous sûr de vouloir supprimer ce document ?\n\n" +
                $"Nom: {document.FileName}\n" +
                $"Catégorie: {document.Category}\n" +
                $"Date: {document.DateAddedDisplay}\n\n" +
                $"Cette action est irréversible.",
                "Confirmer la suppression",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );
            
            if (result != MessageBoxResult.Yes)
                return;
            
            try
            {
                // Supprimer le fichier physique
                if (File.Exists(document.FilePath))
                {
                    File.Delete(document.FilePath);
                }
                
                // Retirer de l'index JSON
                var indexPath = Path.Combine(_patientFolderPath, "documents", "documents-index.json");
                if (File.Exists(indexPath))
                {
                    var json = await File.ReadAllTextAsync(indexPath);
                    var documents = System.Text.Json.JsonSerializer.Deserialize<List<PatientDocument>>(json) ?? new List<PatientDocument>();
                    
                    // Retirer le document
                    documents.RemoveAll(d => d.FilePath == document.FilePath);
                    
                    // Réécrire l'index
                    var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                    var updatedJson = System.Text.Json.JsonSerializer.Serialize(documents, options);
                    await File.WriteAllTextAsync(indexPath, updatedJson);
                }
                
                // Recharger la liste
                await LoadDocumentsAsync();
                
                MessageBox.Show("Document supprimé avec succès.", "Succès", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la suppression:\n\n{ex.Message}", 
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        #endregion
        
        #region Synthesis
        
        private async void SynthesisButton_Click(object sender, RoutedEventArgs e)
        {
            // Vérifier qu'un document est sélectionné
            if (DocumentsDataGrid.SelectedItem is not PatientDocument selectedDocument)
            {
                MessageBox.Show("Veuillez sélectionner un document pour générer sa synthèse.", 
                    "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            // Créer la fenêtre de progression
            var progressWindow = new Window
            {
                Title = "Génération en cours...",
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
                Text = "⏳ Génération de la synthèse en cours...",
                FontSize = 14,
                Margin = new Thickness(0, 0, 0, 10),
                TextAlignment = TextAlignment.Center
            };
            var progressBar = new ProgressBar 
            { 
                Height = 25,
                IsIndeterminate = true
            };
            var detailText = new TextBlock 
            { 
                Text = $"Analyse du document:\n{selectedDocument.FileName}",
                FontSize = 12,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 10, 0, 0),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            
            progressPanel.Children.Add(progressText);
            progressPanel.Children.Add(progressBar);
            progressPanel.Children.Add(detailText);
            progressWindow.Content = progressPanel;
            
            // Afficher la fenêtre de progression
            progressWindow.Show();
            
            try
            {
                // Générer la synthèse uniquement de ce document (ignorer le poids dans ce contexte)
                var (synthesis, _) = await _documentService.GenerateSingleDocumentSynthesisAsync(selectedDocument);
                
                // Fermer la fenêtre de progression
                progressWindow.Close();
                
                // Afficher la synthèse dans une nouvelle fenêtre
                var synthesisWindow = new Window
                {
                    Title = $"📄 Synthèse de {selectedDocument.FileName}",
                    Width = 800,
                    Height = 600,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = this
                };
                
                var scrollViewer = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Padding = new Thickness(20)
                };
                
                var textBlock = new TextBlock
                {
                    Text = synthesis,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 13
                };
                
                scrollViewer.Content = textBlock;
                synthesisWindow.Content = scrollViewer;
                synthesisWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                progressWindow.Close();
                MessageBox.Show($"Erreur lors de la génération de la synthèse:\n\n{ex.Message}", 
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        #endregion
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
