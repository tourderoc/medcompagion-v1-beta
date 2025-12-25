using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.Dialogs;

namespace MedCompanion.Views.Documents
{
    public partial class DocumentsControl : UserControl
    {
        // Services
        private DocumentService? _documentService;
        private PathService? _pathService;
        private PatientIndexService? _patientIndexService;
        private SynthesisWeightTracker? _synthesisWeightTracker;
        private ScannerService? _scannerService;
        private RegenerationService? _regenerationService;

        // État
        private PatientIndexEntry? _currentPatient;
        private List<PatientDocument> _allDocuments = new();
        private string _currentDocumentFilter = "all";
        private PatientDocument? _currentSynthesizedDocument;
        private string? _currentSynthesisPath;

        // Poids de pertinence de la dernière synthèse de document (pour mise à jour synthèse patient)
        private double _lastDocumentSynthesisWeight = 0.0;

        // Événement pour communication avec MainWindow
        public event EventHandler<string>? StatusChanged;
        public event EventHandler? DocumentSynthesisSaved; // NOUVEAU : Pour rafraîchir le badge de synthèse

        public DocumentsControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initialise le contrôle avec les services nécessaires
        /// </summary>
        public void Initialize(
            DocumentService documentService,
            PathService pathService,
            PatientIndexService patientIndexService,
            SynthesisWeightTracker synthesisWeightTracker,
            ScannerService scannerService,
            RegenerationService? regenerationService = null)
        {
            _documentService = documentService;
            _pathService = pathService;
            _patientIndexService = patientIndexService;
            _synthesisWeightTracker = synthesisWeightTracker;
            _scannerService = scannerService;
            _regenerationService = regenerationService;
        }

        /// <summary>
        /// Charge les données pour le patient sélectionné
        /// </summary>
        public void SetCurrentPatient(PatientIndexEntry? patient)
        {
            _currentPatient = patient;
            LoadPatientDocuments();
        }

        /// <summary>
        /// Charge la liste des documents du patient courant
        /// </summary>
        private async void LoadPatientDocuments()
        {
            if (_currentPatient == null || _documentService == null)
            {
                _allDocuments.Clear();
                ApplyDocumentFilter("all");
                return;
            }

            try
            {
                _allDocuments = await _documentService.GetAllDocumentsAsync(_currentPatient.NomComplet);
                ApplyDocumentFilter(_currentDocumentFilter);

                StatusChanged?.Invoke(this, $"✓ {_allDocuments.Count} document(s) chargé(s)");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"❌ Erreur chargement documents: {ex.Message}");
            }
        }

        private void ApplyDocumentFilter(string filter)
        {
            _currentDocumentFilter = filter;

            var filtered = filter == "all"
                ? _allDocuments
                : _allDocuments.Where(d => d.Category == filter).ToList();

            if (DocumentsDataGrid != null)
            {
                DocumentsDataGrid.ItemsSource = filtered;
            }

            if (DocCountLabel != null)
            {
                var count = filtered.Count;
                DocCountLabel.Text = count == 0 ? "Aucun document" :
                                     count == 1 ? "1 document" :
                                     $"{count} documents";
            }
        }

        private void DocCategoriesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DocCategoriesListBox.SelectedItem is ListBoxItem item && item.Tag is string tag)
            {
                ApplyDocumentFilter(tag);
            }
        }

        #region Browse Files Documents

        private async void DocBrowseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPatient == null)
            {
                MessageBox.Show("Veuillez d'abord sélectionner un patient.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

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
                await ProcessDocumentFilesAsync(openFileDialog.FileNames);
            }
        }

        #endregion

        #region Process Files Documents

        private async Task ProcessDocumentFilesAsync(string[] filePaths)
        {
            if (filePaths == null || filePaths.Length == 0 || _currentPatient == null)
                return;

            // Obtenir la fenêtre parente de manière sécurisée
            var ownerWindow = Window.GetWindow(this);

            var progressWindow = new Window
            {
                Title = "Import en cours...",
                Width = 400,
                Height = 150,
                WindowStartupLocation = ownerWindow != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Owner = ownerWindow,
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

            try
            {
                progressWindow.Show();
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"❌ Erreur ouverture fenêtre progression: {ex.Message}");
                // Continuer sans fenêtre de progression
            }

            int successCount = 0;
            int errorCount = 0;
            var errors = new List<string>();

            for (int i = 0; i < filePaths.Length; i++)
            {
                var filePath = filePaths[i];
                var fileName = Path.GetFileName(filePath);

                // Mettre à jour le texte seulement si la fenêtre est toujours ouverte
                if (progressWindow.IsLoaded)
                {
                    detailText.Text = $"Traitement: {fileName} ({i + 1}/{filePaths.Length})";
                }
                await Task.Delay(100);

                try
                {
                    var (success, document, message) = await _documentService!.ImportDocumentAsync(
                        filePath, _currentPatient.DirectoryPath);

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

            // Fermer la fenêtre seulement si elle est toujours ouverte
            try
            {
                if (progressWindow.IsLoaded)
                {
                    progressWindow.Close();
                }
            }
            catch
            {
                // Ignorer les erreurs de fermeture
            }

            // Recharger la liste
            LoadPatientDocuments();

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

        private async void DocumentsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var isDocumentSelected = DocumentsDataGrid.SelectedItem != null;

            if (DeleteDocumentButton != null)
            {
                DeleteDocumentButton.IsEnabled = isDocumentSelected;
            }

            // Reset de l'aperçu et des boutons lors du changement de sélection
            if (isDocumentSelected && DocumentsDataGrid.SelectedItem is PatientDocument selectedDocument)
            {
                await UpdateDocumentSynthesisState(selectedDocument);
            }
            else
            {
                // Aucun document sélectionné → Reset complet
                ResetSynthesisPreview();
                if (DocSynthesisButton != null)
                {
                    DocSynthesisButton.IsEnabled = false;
                }
            }
        }

        /// <summary>
        /// Met à jour l'état de la zone de synthèse en fonction du document sélectionné
        /// </summary>
        private async Task UpdateDocumentSynthesisState(PatientDocument document)
        {
            if (_currentPatient == null || _documentService == null)
                return;

            try
            {
                // Vérifier si une synthèse existe déjà
                var (exists, synthesisPath) = _documentService.GetExistingSynthesis(document, _currentPatient.NomComplet);

                if (exists && !string.IsNullOrEmpty(synthesisPath))
                {
                    // SYNTHÈSE EXISTE → Charger automatiquement
                    var synthesisContent = await _documentService.LoadSynthesisContentAsync(synthesisPath);

                    // Convertir Markdown en FlowDocument formaté
                    try
                    {
                        DocSynthesisPreview.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(synthesisContent);
                    }
                    catch
                    {
                        // En cas d'erreur, afficher texte brut
                        var doc = new FlowDocument();
                        doc.Blocks.Add(new Paragraph(new Run(synthesisContent)));
                        DocSynthesisPreview.Document = doc;
                    }

                    // Stocker le document et le chemin pour suppression
                    _currentSynthesizedDocument = document;
                    _currentSynthesisPath = synthesisPath;

                    // Bouton Synthèse INACTIF (gris)
                    DocSynthesisButton.IsEnabled = false;
                    DocSynthesisButton.Background = new SolidColorBrush(Color.FromRgb(189, 195, 199)); // Gris

                    // Afficher boutons Supprimer et Vue Détaillée, masquer Enregistrer
                    DeleteSynthesisBtn.Visibility = Visibility.Visible;
                    SaveSynthesisBtn.Visibility = Visibility.Collapsed;
                    CloseSynthesisPreviewBtn.Visibility = Visibility.Visible;
                    ViewDocumentSynthesisButton.Visibility = Visibility.Visible;

                    StatusChanged?.Invoke(this, $"✓ Synthèse chargée depuis {Path.GetFileName(synthesisPath)}");
                }
                else
                {
                    // PAS DE SYNTHÈSE → État par défaut
                    ResetSynthesisPreview();

                    // Bouton Synthèse ACTIF (vert)
                    DocSynthesisButton.IsEnabled = true;
                    DocSynthesisButton.Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Vert

                    _currentSynthesizedDocument = null;
                    _currentSynthesisPath = null;
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"❌ Erreur vérification synthèse: {ex.Message}");
            }
        }

        /// <summary>
        /// Réinitialise la zone d'aperçu de synthèse
        /// </summary>
        private void ResetSynthesisPreview()
        {
            // Créer un FlowDocument avec un message par défaut
            var doc = new FlowDocument();
            var paragraph = new Paragraph(new Run("Sélectionnez un document et cliquez sur 'Synthèse' pour voir l'aperçu..."))
            {
                Foreground = new SolidColorBrush(Color.FromRgb(127, 140, 141)), // Gris
                FontSize = 13
            };
            doc.Blocks.Add(paragraph);
            DocSynthesisPreview.Document = doc;

            DeleteSynthesisBtn.Visibility = Visibility.Collapsed;
            SaveSynthesisBtn.Visibility = Visibility.Collapsed;
            CloseSynthesisPreviewBtn.Visibility = Visibility.Collapsed;
            ViewDocumentSynthesisButton.Visibility = Visibility.Collapsed;
        }

        private void OpenDocument(PatientDocument document)
        {
            try
            {
                if (File.Exists(document.FilePath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
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

            if (_currentPatient == null || _documentService == null)
            {
                MessageBox.Show("Erreur: Patient ou service non initialisé.",
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

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
                StatusChanged?.Invoke(this, "⏳ Suppression en cours...");

                // Utiliser la méthode du service
                var (success, message) = await _documentService.DeleteDocumentAsync(document, _currentPatient.NomComplet);

                if (success)
                {
                    // Recharger la liste des documents
                    LoadPatientDocuments();

                    MessageBox.Show("✅ Document supprimé avec succès.", "Succès",
                        MessageBoxButton.OK, MessageBoxImage.Information);

                    StatusChanged?.Invoke(this, "✅ Document supprimé");
                }
                else
                {
                    MessageBox.Show($"❌ {message}", "Erreur",
                        MessageBoxButton.OK, MessageBoxImage.Error);

                    StatusChanged?.Invoke(this, $"❌ {message}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la suppression:\n\n{ex.Message}",
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);

                StatusChanged?.Invoke(this, $"❌ Erreur: {ex.Message}");
            }
        }

        private async void DocSynthesisButton_Click(object sender, RoutedEventArgs e)
        {
            if (DocumentsDataGrid.SelectedItem is not PatientDocument selectedDocument)
            {
                MessageBox.Show("Veuillez sélectionner un document pour générer sa synthèse.",
                    "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // Afficher message de chargement dans la zone d'aperçu
                var loadingDoc = new FlowDocument();
                var loadingPara = new Paragraph(new Run($"⏳ Génération de la synthèse en cours...\n\nAnalyse du document: {selectedDocument.FileName}"))
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(52, 152, 219)), // Bleu
                    FontSize = 13
                };
                loadingDoc.Blocks.Add(loadingPara);
                DocSynthesisPreview.Document = loadingDoc;

                // Masquer le bouton Enregistrer pendant la génération
                SaveSynthesisBtn.Visibility = Visibility.Collapsed;
                CloseSynthesisPreviewBtn.Visibility = Visibility.Visible;

                StatusChanged?.Invoke(this, "⏳ Génération de la synthèse en cours...");

                // Récupérer les métadonnées du patient pour l'anonymisation (si disponible)
                PatientMetadata? patientData = null;
                if (_patientIndexService != null && _currentPatient != null)
                {
                    patientData = _patientIndexService.GetMetadata(_currentPatient.Id);
                }

                // Générer la synthèse avec extraction du poids
                var (synthesis, relevanceWeight) = await _documentService!.GenerateSingleDocumentSynthesisAsync(selectedDocument, patientData);

                // Stocker le document, la synthèse et le poids pour pouvoir les sauvegarder
                _currentSynthesizedDocument = selectedDocument;
                _lastDocumentSynthesisWeight = relevanceWeight;

                // Afficher la synthèse dans la zone d'aperçu avec formatage
                try
                {
                    DocSynthesisPreview.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(synthesis);
                }
                catch
                {
                    // En cas d'erreur de conversion, afficher texte brut
                    var doc = new FlowDocument();
                    doc.Blocks.Add(new Paragraph(new Run(synthesis)));
                    DocSynthesisPreview.Document = doc;
                }

                // Afficher ET activer le bouton Enregistrer
                SaveSynthesisBtn.Visibility = Visibility.Visible;
                SaveSynthesisBtn.IsEnabled = true;
                SaveSynthesisBtn.Background = new SolidColorBrush(Color.FromRgb(39, 174, 96)); // Vert

                StatusChanged?.Invoke(this, "✅ Synthèse générée avec succès");
            }
            catch (Exception ex)
            {
                _currentSynthesizedDocument = null;
                var errorDoc = new FlowDocument();
                var errorPara = new Paragraph(new Run($"❌ Erreur lors de la génération:\n\n{ex.Message}"))
                {
                    Foreground = new SolidColorBrush(Colors.Red)
                };
                errorDoc.Blocks.Add(errorPara);
                DocSynthesisPreview.Document = errorDoc;

                StatusChanged?.Invoke(this, $"❌ Erreur: {ex.Message}");
            }
        }

        private void SaveSynthesisBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_currentSynthesizedDocument == null || _currentPatient == null)
            {
                MessageBox.Show("Aucune synthèse à sauvegarder.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // CORRECTION: Utiliser PathService pour obtenir le bon chemin (2025/documents/)
                var documentsDir = _pathService!.GetDocumentsDirectory(_currentPatient.NomComplet);
                var syntheseDir = Path.Combine(documentsDir, "syntheses_documents");

                if (!Directory.Exists(syntheseDir))
                {
                    Directory.CreateDirectory(syntheseDir);
                }

                // Générer le nom du fichier
                var originalFileName = Path.GetFileNameWithoutExtension(_currentSynthesizedDocument.FileName);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var syntheseFileName = $"{originalFileName}_synthese_{timestamp}.md";
                var synthesePath = Path.Combine(syntheseDir, syntheseFileName);

                // Extraire le texte Markdown du FlowDocument
                var synthesisMarkdown = MarkdownFlowDocumentConverter.FlowDocumentToMarkdown(DocSynthesisPreview.Document);

                // Créer le contenu avec métadonnées
                var syntheseContent = $@"---
document_original: {_currentSynthesizedDocument.FileName}
date_synthese: {DateTime.Now:dd/MM/yyyy HH:mm:ss}
patient: {_currentPatient.NomComplet}
---

{synthesisMarkdown}
";

                // Sauvegarder
                File.WriteAllText(synthesePath, syntheseContent, Encoding.UTF8);

                // Mettre à jour le chemin actuel pour permettre la prévisualisation/suppression immédiate
                _currentSynthesisPath = synthesePath;

                // NOUVEAU : Enregistrer le poids de pertinence pour la synthèse patient
                if (_synthesisWeightTracker != null)
                {
                    _synthesisWeightTracker.RecordContentWeight(
                        _currentPatient.NomComplet,
                        "synthese_document",
                        synthesePath,
                        _lastDocumentSynthesisWeight,
                        $"Synthèse document '{_currentSynthesizedDocument.FileName}' (poids IA: {_lastDocumentSynthesisWeight:F1})"
                    );

                    System.Diagnostics.Debug.WriteLine(
                        $"[DocumentsControl] Poids enregistré: {_lastDocumentSynthesisWeight:F1} pour synthèse {syntheseFileName}");
                }

                // Message de succès
                MessageBox.Show(
                    $"✅ Synthèse sauvegardée avec succès !\n\n" +
                    $"Fichier : {syntheseFileName}\n" +
                    $"Emplacement : documents/syntheses/",
                    "Sauvegarde réussie",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                StatusChanged?.Invoke(this, $"✅ Synthèse sauvegardée : {syntheseFileName}");

                // METRIQUE ERGONOMIE : Mettre à jour l'interface immédiatement
                // 1. Masquer le bouton Enregistrer
                SaveSynthesisBtn.Visibility = Visibility.Collapsed;
                SaveSynthesisBtn.IsEnabled = false;

                // 2. Afficher les boutons de gestion (Oeil et Poubelle)
                ViewDocumentSynthesisButton.Visibility = Visibility.Visible;
                DeleteSynthesisBtn.Visibility = Visibility.Visible;
                CloseSynthesisPreviewBtn.Visibility = Visibility.Visible;

                // 3. Griser le bouton de génération principal (comme si elle existait déjà au chargement)
                if (DocSynthesisButton != null)
                {
                    DocSynthesisButton.IsEnabled = false;
                    DocSynthesisButton.Background = new SolidColorBrush(Color.FromRgb(189, 195, 199)); // Gris
                }

                // NOUVEAU : Déclencher l'événement pour rafraîchir le badge de synthèse
                DocumentSynthesisSaved?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la sauvegarde:\n\n{ex.Message}",
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);

                StatusChanged?.Invoke(this, $"❌ Erreur sauvegarde: {ex.Message}");
            }
        }

        private void CloseSynthesisPreviewBtn_Click(object sender, RoutedEventArgs e)
        {
            // Masquer les boutons et réinitialiser l'aperçu
            ResetSynthesisPreview();
        }

        private async void ViewDocumentSynthesisButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentSynthesisPath) || _currentPatient == null)
            {
                MessageBox.Show("Aucune synthèse à afficher.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var dialog = new DetailedViewDialog();
                dialog.LoadContent(_currentSynthesisPath, ContentType.Synthesis, _currentPatient.NomComplet);

                // ✅ NOUVEAU : Initialiser le service de régénération IA si disponible
                if (_regenerationService != null)
                {
                    // Créer PatientMetadata depuis le patient courant pour l'anonymisation
                    PatientMetadata? patientMetadata = null;
                    if (_patientIndexService != null)
                    {
                        patientMetadata = _patientIndexService.GetMetadata(_currentPatient.Id);
                    }
                    else
                    {
                        // Fallback minimal
                        patientMetadata = new PatientMetadata
                        {
                            Nom = _currentPatient.Nom ?? "",
                            Prenom = _currentPatient.Prenom ?? "",
                            Sexe = _currentPatient.Sexe ?? ""
                        };
                    }
                    
                    dialog.InitializeRegenerationService(_regenerationService, patientMetadata);
                }

                // S'abonner à l'événement de sauvegarde pour rafraîchir l'aperçu
                dialog.ContentSaved += async (s, args) =>
                {
                    // Recharger le contenu de la synthèse dans l'aperçu
                    if (_documentService != null && !string.IsNullOrEmpty(_currentSynthesisPath))
                    {
                        try
                        {
                            var synthesisContent = await _documentService.LoadSynthesisContentAsync(_currentSynthesisPath);
                            
                            // Convertir Markdown en FlowDocument formaté
                            try
                            {
                                DocSynthesisPreview.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(synthesisContent);
                            }
                            catch
                            {
                                // En cas d'erreur, afficher texte brut
                                var doc = new FlowDocument();
                                doc.Blocks.Add(new Paragraph(new Run(synthesisContent)));
                                DocSynthesisPreview.Document = doc;
                            }

                            StatusChanged?.Invoke(this, "✅ Aperçu de la synthèse mis à jour");
                        }
                        catch (Exception ex)
                        {
                            StatusChanged?.Invoke(this, $"❌ Erreur lors du rafraîchissement: {ex.Message}");
                        }
                    }
                };

                dialog.Owner = Window.GetWindow(this);
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'ouverture de la vue détaillée:\n\n{ex.Message}",
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusChanged?.Invoke(this, $"❌ Erreur: {ex.Message}");
            }
        }

        private void DeleteSynthesisBtn_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentSynthesisPath) || _currentSynthesizedDocument == null)
            {
                MessageBox.Show("Aucune synthèse à supprimer.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Êtes-vous sûr de vouloir supprimer cette synthèse ?\n\n" +
                $"Document : {_currentSynthesizedDocument.FileName}\n" +
                $"Fichier : {Path.GetFileName(_currentSynthesisPath)}",
                "Confirmer la suppression",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                var (success, message) = _documentService!.DeleteSynthesis(_currentSynthesisPath);

                if (success)
                {
                    // Réinitialiser l'aperçu
                    ResetSynthesisPreview();

                    // Réactiver le bouton Synthèse (vert)
                    if (DocSynthesisButton != null)
                    {
                        DocSynthesisButton.IsEnabled = true;
                        DocSynthesisButton.Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Vert
                    }

                    // Réinitialiser les variables
                    _currentSynthesizedDocument = null;
                    _currentSynthesisPath = null;

                    MessageBox.Show("✅ Synthèse supprimée avec succès.", "Succès",
                        MessageBoxButton.OK, MessageBoxImage.Information);

                    StatusChanged?.Invoke(this, "✅ Synthèse supprimée - Le bouton Synthèse est à nouveau actif");
                }
                else
                {
                    MessageBox.Show($"Erreur lors de la suppression:\n\n{message}",
                        "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);

                    StatusChanged?.Invoke(this, $"❌ Erreur suppression: {message}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la suppression:\n\n{ex.Message}",
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);

                StatusChanged?.Invoke(this, $"❌ Erreur: {ex.Message}");
            }
        }

        private void OpenDropWindowButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPatient == null)
            {
                MessageBox.Show("Veuillez d'abord sélectionner un patient.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_documentService == null)
            {
                MessageBox.Show("Erreur: Service de documents non initialisé.", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                var mainWindow = Window.GetWindow(this);

                // Ouvrir la petite fenêtre flottante de drag & drop
                var dropWindow = new DocumentDropWindow(
                    _currentPatient.NomComplet,
                    _currentPatient.DirectoryPath,
                    _documentService,
                    () => LoadPatientDocuments() // Callback pour rafraîchir la liste
                );

                // Minimiser la fenêtre principale
                if (mainWindow != null)
                {
                    mainWindow.WindowState = WindowState.Minimized;
                }

                // Restaurer la fenêtre principale à la fermeture de la fenêtre Drag & Drop
                dropWindow.Closed += (s, args) =>
                {
                    if (mainWindow != null && mainWindow.WindowState == WindowState.Minimized)
                    {
                        mainWindow.WindowState = WindowState.Normal;
                        mainWindow.Activate(); // Mettre au premier plan
                    }
                };

                dropWindow.Show();

                StatusChanged?.Invoke(this, "🪟 Fenêtre drag & drop ouverte - Fenêtre principale minimisée");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'ouverture de la fenêtre:\n\n{ex.Message}",
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);

                StatusChanged?.Invoke(this, $"❌ Erreur: {ex.Message}");
            }
        }

        #endregion

        #region Scanner Integration

        private async void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPatient == null)
            {
                MessageBox.Show("Veuillez d'abord sélectionner un patient.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_scannerService == null)
            {
                MessageBox.Show("Le service de scanner n'est pas initialisé.", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (_documentService == null)
            {
                MessageBox.Show("Le service de documents n'est pas initialisé.", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                // Ouvrir la boîte de dialogue de scan
                var scanDialog = new ScanDocumentDialog(_scannerService)
                {
                    Owner = Window.GetWindow(this)
                };

                var result = scanDialog.ShowDialog();

                if (result == true && !string.IsNullOrEmpty(scanDialog.ScannedFilePath))
                {
                    // L'utilisateur a scanné et sélectionné un fichier
                    var scannedFile = scanDialog.ScannedFilePath;

                    StatusChanged?.Invoke(this, "⏳ Import du document scanné en cours...");

                    // Importer le document scanné
                    await ProcessDocumentFilesAsync(new[] { scannedFile });

                    StatusChanged?.Invoke(this, "✅ Document scanné importé avec succès");

                    // Nettoyer le dossier temporaire après import réussi
                    try
                    {
                        if (File.Exists(scannedFile))
                        {
                            var tempFolder = Path.GetDirectoryName(scannedFile);
                            if (tempFolder != null && tempFolder.Contains("MedCompanion_Scans"))
                            {
                                Directory.Delete(tempFolder, true);
                            }
                        }
                    }
                    catch
                    {
                        // Ignorer les erreurs de nettoyage
                    }
                }
                else
                {
                    StatusChanged?.Invoke(this, "Scan annulé");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du scan:\n\n{ex.Message}",
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusChanged?.Invoke(this, $"❌ Erreur scan: {ex.Message}");
            }
        }

        #endregion
    }
}
