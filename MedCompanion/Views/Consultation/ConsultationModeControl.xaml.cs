using System.IO;
using System.Windows;
using System.Windows.Controls;
using MedCompanion.Dialogs;
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.Services.Consultation;
using MedCompanion.Services.LLM;
using MedCompanion.ViewModels;
using Microsoft.Win32;

namespace MedCompanion.Views.Consultation
{
    public partial class ConsultationModeControl : UserControl
    {
        private ConsultationModeViewModel? _viewModel;
        private DocumentService? _documentService;
        private ScannerService? _scannerService;

        public ConsultationModeControl()
        {
            InitializeComponent();
            _viewModel = DataContext as ConsultationModeViewModel;
        }

        public void Initialize(ILLMService llmService, StorageService storageService,
                               WhisperStreamingService? whisperService = null,
                               DocumentService? documentService = null,
                               ScannerService? scannerService = null)
        {
            _viewModel ??= DataContext as ConsultationModeViewModel;
            _viewModel?.InjectServices(llmService, storageService, whisperService);
            _documentService = documentService;
            _scannerService = scannerService;
        }

        public void LoadPatient(PatientIndexEntry patient)
        {
            _viewModel ??= DataContext as ConsultationModeViewModel;
            if (ConsultationTypeCombo.Items.Count > 0)
                ConsultationTypeCombo.SelectedIndex = 0;
            _viewModel?.LoadPatient(patient);
        }

        public void SetViewState(ConsultationViewState state)
        {
            _viewModel ??= DataContext as ConsultationModeViewModel;
            if (_viewModel != null)
                _viewModel.CurrentState = state;
        }

        public ConsultationModeViewModel? GetViewModel() => _viewModel;

        // ── Handlers ─────────────────────────────────────────────────────────

        private void ConsultationTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _viewModel ??= DataContext as ConsultationModeViewModel;
            if (_viewModel == null) return;

            var item = ConsultationTypeCombo.SelectedItem as ComboBoxItem;
            var tag  = item?.Tag?.ToString() ?? "Normal";

            _viewModel.ConsultationType = tag switch
            {
                "PremiereConsultation" => ConsultationType.PremiereConsultation,
                _                      => ConsultationType.Normal
            };
        }

        private void ImportTxtBtn_Click(object sender, RoutedEventArgs e)
        {
            _viewModel ??= DataContext as ConsultationModeViewModel;
            if (_viewModel == null) return;

            var dlg = new OpenFileDialog
            {
                Title       = "Importer une transcription",
                Filter      = "Fichiers texte (*.txt)|*.txt|Tous les fichiers (*.*)|*.*",
                Multiselect = false
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                _viewModel.TranscriptionInput = File.ReadAllText(dlg.FileName, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lecture fichier : {ex.Message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void VocabBtn_Click(object sender, RoutedEventArgs e)
        {
            var vocabService = new WhisperVocabService();
            var dialog = new WhisperVocabDialog(vocabService)
            {
                Owner = Window.GetWindow(this)
            };
            dialog.ShowDialog();
        }

        // ── Documents Importés (V0d) ──────────────────────────────────────────

        private async void ImportDocBtn_Click(object sender, RoutedEventArgs e)
        {
            _viewModel ??= DataContext as ConsultationModeViewModel;
            if (_viewModel == null || _documentService == null || _viewModel.CurrentPatient == null)
            {
                MessageBox.Show("Services non disponibles ou aucun patient sélectionné.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new OpenFileDialog
            {
                Title = "Importer un document (bilan, rapport...)",
                Filter = "Tous les documents|*.pdf;*.docx;*.doc;*.jpg;*.jpeg;*.png;*.txt|" +
                         "PDF|*.pdf|Word|*.docx;*.doc|Images|*.jpg;*.jpeg;*.png|Texte|*.txt",
                Multiselect = false
            };

            if (dlg.ShowDialog() != true) return;

            _viewModel.SynthesisStatusMessage = "⏳ Import et analyse du document...";
            _viewModel.IsImportingDocument = true;

            try
            {
                var (success, document, message) = await _documentService.ImportDocumentAsync(
                    dlg.FileName,
                    _viewModel.CurrentPatient.NomComplet);

                if (success && document != null)
                {
                    var importedDoc = new ImportedConsultationDocument
                    {
                        FileName = document.FileName,
                        FilePath = document.FilePath ?? dlg.FileName,
                        DocumentSynthesis = document.Summary ?? "",
                        Category = document.Category ?? "Documents",
                        Weight = 0.6
                    };

                    _viewModel.ImportedDocuments.Add(importedDoc);
                    _viewModel.SynthesisStatusMessage = $"✅ Document importé: {document.FileName}";
                }
                else
                {
                    _viewModel.SynthesisStatusMessage = $"❌ Erreur import: {message}";
                }
            }
            catch (Exception ex)
            {
                _viewModel.SynthesisStatusMessage = $"❌ Erreur: {ex.Message}";
            }
            finally
            {
                _viewModel.IsImportingDocument = false;
            }
        }

        private async void ScannerBtn_Click(object sender, RoutedEventArgs e)
        {
            _viewModel ??= DataContext as ConsultationModeViewModel;
            if (_viewModel == null || _scannerService == null || _documentService == null || _viewModel.CurrentPatient == null)
            {
                MessageBox.Show("Services de scan non disponibles ou aucun patient sélectionné.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var scanDialog = new ScanDocumentDialog(_scannerService)
            {
                Owner = Window.GetWindow(this)
            };

            if (scanDialog.ShowDialog() != true || string.IsNullOrEmpty(scanDialog.ScannedFilePath))
                return;

            _viewModel.SynthesisStatusMessage = "⏳ Analyse du document scanné...";
            _viewModel.IsImportingDocument = true;

            try
            {
                var (success, document, message) = await _documentService.ImportDocumentAsync(
                    scanDialog.ScannedFilePath,
                    _viewModel.CurrentPatient.NomComplet);

                if (success && document != null)
                {
                    var importedDoc = new ImportedConsultationDocument
                    {
                        FileName = document.FileName,
                        FilePath = document.FilePath ?? scanDialog.ScannedFilePath,
                        DocumentSynthesis = document.Summary ?? "",
                        Category = document.Category ?? "Documents",
                        Weight = 0.7
                    };

                    _viewModel.ImportedDocuments.Add(importedDoc);
                    _viewModel.SynthesisStatusMessage = $"✅ Document scanné: {document.FileName}";

                    // Nettoyer le fichier temporaire de scan
                    try
                    {
                        var tempFolder = Path.GetDirectoryName(scanDialog.ScannedFilePath);
                        if (tempFolder != null && tempFolder.Contains("MedCompanion_Scans"))
                            Directory.Delete(tempFolder, true);
                    }
                    catch { }
                }
                else
                {
                    _viewModel.SynthesisStatusMessage = $"❌ Erreur scan: {message}";
                }
            }
            catch (Exception ex)
            {
                _viewModel.SynthesisStatusMessage = $"❌ Erreur: {ex.Message}";
            }
            finally
            {
                _viewModel.IsImportingDocument = false;
            }
        }

        // ── Clinical Observations (V0c) ──────────────────────────────────────

        private void ObservationOptionButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            _viewModel ??= DataContext as ConsultationModeViewModel;
            if (_viewModel == null) return;

            var option = btn.Content?.ToString();
            if (string.IsNullOrWhiteSpace(option)) return;

            var visual = btn.Parent as WrapPanel;
            var border = visual?.Parent as StackPanel;
            var grid = border?.Parent as Grid;
            var template = grid?.Parent as Border;
            var itemsControl = template?.Parent as ItemsControl;
            var card = itemsControl?.DataContext as ClinicalObservationCard;

            if (card != null)
            {
                _viewModel.SelectObservationOption(card, option);

                ResetOtherButtonsInGroup(visual, btn);
                btn.Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(52, 152, 219));
                btn.Foreground = System.Windows.Media.Brushes.White;
            }
        }

        private void ResetOtherButtonsInGroup(WrapPanel? panel, Button selectedBtn)
        {
            if (panel == null) return;
            var whiteBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(255, 255, 255));
            var darkBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(44, 62, 80));

            foreach (var child in panel.Children.OfType<Button>())
            {
                if (child != selectedBtn)
                {
                    child.Background = whiteBrush;
                    child.Foreground = darkBrush;
                }
            }
        }
    }
}
