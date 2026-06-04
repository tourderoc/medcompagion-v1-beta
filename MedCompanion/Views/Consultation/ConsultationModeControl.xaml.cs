using System.IO;
using System.Windows;
using System.Windows.Controls;
using MedCompanion.Dialogs;
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.Services.Consultation;
using MedCompanion.Services.Evaluations;
using MedCompanion.Services.Synthesis;
using MedCompanion.Services.Therapeutique;
using MedCompanion.Services.LLM;
using MedCompanion.Services.Urgence;
using MedCompanion.ViewModels;
using Microsoft.Win32;

namespace MedCompanion.Views.Consultation
{
    public partial class ConsultationModeControl : UserControl
    {
        private ConsultationModeViewModel? _viewModel;
        private DocumentService? _documentService;
        private ScannerService? _scannerService;

        /// <summary>
        /// Émis dès qu'une note du mode Consultation est sauvegardée dans le dossier patient.
        /// MainWindow s'abonne pour rafraîchir la liste de notes du mode Console.
        /// </summary>
        public event EventHandler? NoteSavedToPatient;

        public ConsultationModeControl()
        {
            InitializeComponent();
            _viewModel = DataContext as ConsultationModeViewModel;
            DataContextChanged += (_, _) => WireViewModelEvents();
            WireViewModelEvents();
        }

        private void WireViewModelEvents()
        {
            var vm = DataContext as ConsultationModeViewModel;
            if (vm == null) return;
            vm.NoteSavedToPatient -= OnViewModelNoteSaved;
            vm.NoteSavedToPatient += OnViewModelNoteSaved;
        }

        private void OnViewModelNoteSaved(object? sender, EventArgs e)
            => NoteSavedToPatient?.Invoke(this, EventArgs.Empty);

        public void Initialize(ILLMService llmService, StorageService storageService,
                               WhisperStreamingService? whisperService = null,
                               DocumentService? documentService = null,
                               ScannerService? scannerService = null,
                               PatientIndexService? patientIndex = null,
                               UrgenceDispatcher? urgenceDispatcher = null,
                               UrgenceLogService? urgenceLogService = null,
                               EvaluationPhaseService? evaluationPhaseService = null,
                               PreparationSuggesterService? preparationSuggester = null,
                               AxesSuggesterService? axesSuggester = null,
                               AxisExtractorService? axisExtractor = null,
                               BilanFinalSuggesterService? bilanFinalSuggester = null,
                               FeuilleLectureService? feuilleLecture = null,
                               BrancheEnvironnementLectureService? brancheLecture = null,
                               SyntheseGlobaleService? syntheseGlobaleService = null,
                               SyntheseGlobaleSuggesterService? syntheseGlobaleSuggester = null,
                               SynthesisWeightTracker? synthesisWeightTracker = null,
                               SyntheseGlobaleRelectureService? syntheseGlobaleRelecteur = null,
                               ProjetTherapeutiqueService? projetTherapeutiqueService = null,
                               ProjetTherapeutiqueSuggesterService? projetTherapeutiqueSuggester = null,
                               ProjetTherapeutiquePilotageService? projetTherapeutiquePilotage = null)
        {
            _viewModel ??= DataContext as ConsultationModeViewModel;
            _viewModel?.InjectServices(llmService, storageService, whisperService);
            if (patientIndex != null)
                _viewModel?.InjectPatientIndex(patientIndex);
            if (urgenceDispatcher != null && urgenceLogService != null)
                _viewModel?.InjectUrgenceDispatcher(urgenceDispatcher, urgenceLogService);
            if (evaluationPhaseService != null)
                _viewModel?.InjectEvaluationServices(evaluationPhaseService, preparationSuggester, axesSuggester, axisExtractor, bilanFinalSuggester, feuilleLecture, brancheLecture);
            if (syntheseGlobaleService != null)
                _viewModel?.InjectSyntheseGlobaleService(syntheseGlobaleService, syntheseGlobaleSuggester, synthesisWeightTracker, syntheseGlobaleRelecteur);
            if (projetTherapeutiqueService != null)
                _viewModel?.InjectProjetTherapeutiqueService(projetTherapeutiqueService, projetTherapeutiqueSuggester, projetTherapeutiquePilotage);
            _documentService = documentService;
            _scannerService = scannerService;
        }

        public void LoadPatient(PatientIndexEntry patient)
        {
            _viewModel ??= DataContext as ConsultationModeViewModel;
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

        private void NewConsultationBtn_Click(object sender, RoutedEventArgs e)
        {
            _viewModel ??= DataContext as ConsultationModeViewModel;
            if (_viewModel == null) return;

            var menu = new ContextMenu();

            // "1ère consultation" n'apparaît que si le patient n'a AUCUNE note/consultation
            // (par définition la 1ère est unique : si le patient a déjà été vu, plus de 1ère possible)
            if (!_viewModel.HasConsultationNotes)
            {
                var premiere = new MenuItem { Header = "🩺  1ère consultation" };
                premiere.Click += (_, _) => _viewModel.NewConsultationCommand.Execute("premiere");
                menu.Items.Add(premiere);
            }

            var suivi = new MenuItem { Header = "🔄  Consultation de suivi" };
            suivi.Click += (_, _) => _viewModel.NewConsultationCommand.Execute("suivi");
            menu.Items.Add(suivi);

            // Phase d'évaluation — toujours visible (la zone Actions affiche Commencer / Poursuivre selon l'état)
            menu.Items.Add(new Separator());
            var evaluation = new MenuItem { Header = "📋  Phase d'évaluation" };
            evaluation.Click += (_, _) => _viewModel.NewConsultationCommand.Execute("evaluation");
            menu.Items.Add(evaluation);

            // Synthèse Globale — document de référence du patient, versionné, source de vérité
            var synthese = new MenuItem { Header = "🧭  Synthèse Globale" };
            synthese.Click += (_, _) => _viewModel.NewConsultationCommand.Execute("synthese_globale");
            menu.Items.Add(synthese);

            // Projet Thérapeutique — plan d'action structuré avec statuts par action
            var projet = new MenuItem { Header = "🎯  Projet Thérapeutique" };
            projet.Click += (_, _) => _viewModel.NewConsultationCommand.Execute("projet_therapeutique");
            menu.Items.Add(projet);

            menu.PlacementTarget = sender as UIElement;
            menu.Placement       = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen          = true;
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

        private void ImportSuiviTxtBtn_Click(object sender, RoutedEventArgs e)
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
                _viewModel.Suivi.Transcription = File.ReadAllText(dlg.FileName, System.Text.Encoding.UTF8);
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

        // Clinical Observations : sélection des chips gérée par MVVM via SelectOptionCommand
        // sur ClinicalObservationCard. Plus de code-behind fragile basé sur le visual tree.

        // ── Documents globaux via la zone Med (Suggestions) ──────────────────
        // Action transverse : disponible quel que soit le mode (1ère consult, suivi, hub).
        // L'IA classe automatiquement le document (Bilan vs Document) et le range
        // dans le bon sous-dossier patient.

        /// <summary>
        /// Auto-sauvegarde la synthèse du document importé sous forme de fichier markdown.
        /// Format identique à celui du mode Console (documents/syntheses_documents/{nom}_synthese_{stamp}.md)
        /// pour que les deux modes voient la même donnée.
        /// </summary>
        private static void SaveDocumentSynthesisToDisk(string nomComplet, MedCompanion.Models.PatientDocument document)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(document.Summary)) return;

                var pathService = new MedCompanion.Services.PathService();
                var documentsDir = pathService.GetDocumentsDirectory(nomComplet);
                var syntheseDir = Path.Combine(documentsDir, "syntheses_documents");
                Directory.CreateDirectory(syntheseDir);

                var originalFileName = Path.GetFileNameWithoutExtension(document.FileName);
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var syntheseFileName = $"{originalFileName}_synthese_{timestamp}.md";
                var synthesePath = Path.Combine(syntheseDir, syntheseFileName);

                var syntheseContent = $@"---
document_original: {document.FileName}
date_synthese: {DateTime.Now:dd/MM/yyyy HH:mm:ss}
patient: {nomComplet}
categorie: {document.Category ?? "Documents"}
---

# Synthèse — {document.FileName}

{document.Summary}
";
                File.WriteAllText(synthesePath, syntheseContent, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MedDocSynthesis] Erreur sauvegarde synthèse: {ex.Message}");
            }
        }

        private async void MedImportDocBtn_Click(object sender, RoutedEventArgs e)
        {
            _viewModel ??= DataContext as ConsultationModeViewModel;
            if (_viewModel == null || _documentService == null || _viewModel.CurrentPatient == null)
            {
                MessageBox.Show("Veuillez sélectionner un patient avant d'importer un document.",
                    "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new OpenFileDialog
            {
                Title  = "Importer un document du patient (bilan, rapport, courrier…)",
                Filter = "Tous les documents|*.pdf;*.docx;*.doc;*.jpg;*.jpeg;*.png;*.txt|" +
                         "PDF|*.pdf|Word|*.docx;*.doc|Images|*.jpg;*.jpeg;*.png|Texte|*.txt",
                Multiselect = false
            };
            if (dlg.ShowDialog() != true) return;

            _viewModel.MedDocumentStatus = "⏳ Analyse du document en cours…";
            _viewModel.IsImportingDocument = true;
            try
            {
                var (success, document, message) = await _documentService.ImportDocumentAsync(
                    dlg.FileName, _viewModel.CurrentPatient.NomComplet);

                if (success && document != null)
                {
                    // Auto-sauvegarde la synthèse du document (compatibilité Console)
                    SaveDocumentSynthesisToDisk(_viewModel.CurrentPatient.NomComplet, document);

                    // Ajoute aussi à la liste in-memory utile si on entre en Synthèse Initiale après
                    _viewModel.ImportedDocuments.Add(new ImportedConsultationDocument
                    {
                        FileName = document.FileName,
                        FilePath = document.FilePath ?? dlg.FileName,
                        DocumentSynthesis = document.Summary ?? "",
                        Category = document.Category ?? "Documents",
                        Weight = 0.6
                    });

                    // Rafraîchit les onglets BILANS et DOCS du dossier bleu
                    _viewModel.LoadPatientBilansFromDisk();
                    _viewModel.LoadPatientDocumentsFromDisk();

                    _viewModel.MedDocumentStatus = $"✅ {document.FileName} → {document.Category ?? "Documents"} (synthèse auto)";
                }
                else
                {
                    _viewModel.MedDocumentStatus = $"❌ Erreur : {message}";
                }
            }
            catch (Exception ex)
            {
                _viewModel.MedDocumentStatus = $"❌ Erreur : {ex.Message}";
            }
            finally
            {
                _viewModel.IsImportingDocument = false;
            }
        }

        private async void MedScannerBtn_Click(object sender, RoutedEventArgs e)
        {
            _viewModel ??= DataContext as ConsultationModeViewModel;
            if (_viewModel == null || _scannerService == null || _documentService == null || _viewModel.CurrentPatient == null)
            {
                MessageBox.Show("Scanner non disponible ou patient non sélectionné.",
                    "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var scanDialog = new ScanDocumentDialog(_scannerService) { Owner = Window.GetWindow(this) };
            if (scanDialog.ShowDialog() != true || string.IsNullOrEmpty(scanDialog.ScannedFilePath))
                return;

            _viewModel.MedDocumentStatus = "⏳ Analyse du document scanné…";
            _viewModel.IsImportingDocument = true;
            try
            {
                var (success, document, message) = await _documentService.ImportDocumentAsync(
                    scanDialog.ScannedFilePath, _viewModel.CurrentPatient.NomComplet);

                if (success && document != null)
                {
                    // Auto-sauvegarde la synthèse du document (compatibilité Console)
                    SaveDocumentSynthesisToDisk(_viewModel.CurrentPatient.NomComplet, document);

                    _viewModel.ImportedDocuments.Add(new ImportedConsultationDocument
                    {
                        FileName = document.FileName,
                        FilePath = document.FilePath ?? scanDialog.ScannedFilePath,
                        DocumentSynthesis = document.Summary ?? "",
                        Category = document.Category ?? "Documents",
                        Weight = 0.7
                    });

                    // Rafraîchit les onglets BILANS et DOCS du dossier bleu
                    _viewModel.LoadPatientBilansFromDisk();
                    _viewModel.LoadPatientDocumentsFromDisk();

                    _viewModel.MedDocumentStatus = $"✅ {document.FileName} → {document.Category ?? "Documents"} (synthèse auto)";

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
                    _viewModel.MedDocumentStatus = $"❌ Erreur scan : {message}";
                }
            }
            catch (Exception ex)
            {
                _viewModel.MedDocumentStatus = $"❌ Erreur : {ex.Message}";
            }
            finally
            {
                _viewModel.IsImportingDocument = false;
            }
        }
    }
}
