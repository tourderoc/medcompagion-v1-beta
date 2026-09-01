using System;
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

        /// <summary>
        /// Affectation modèle-par-étape, posée avant ou après Initialize selon l'ordre de
        /// construction de MainWindow. Conservée ici pour être transmise au ViewModel dès qu'il
        /// existe, plutôt que d'allonger encore la signature d'Initialize.
        /// </summary>
        private Services.LLM.EtapeModeleService? _etapeModelesEnAttente;

        public void SetEtapeModeles(Services.LLM.EtapeModeleService service)
        {
            _etapeModelesEnAttente = service;
            _viewModel ??= DataContext as ConsultationModeViewModel;
            _viewModel?.InjectEtapeModeles(service);
        }
        private DocumentService? _documentService;
        private ScannerService? _scannerService;

        /// <summary>
        /// Émis dès qu'une note du mode Consultation est sauvegardée dans le dossier patient.
        /// MainWindow s'abonne pour rafraîchir la liste de notes du mode Console.
        /// </summary>
        public event EventHandler? NoteSavedToPatient;

        /// <summary>
        /// Émis après qu'un PDF de restitution 1er entretien est sauvegardé.
        /// Payload : chemin complet du PDF. MainWindow l'enregistre dans le panel DOCUMENTS.
        /// </summary>
        public event EventHandler<string>? RestitutionPdfSavedToPatient;

        /// <summary>
        /// Émis quand un patient est choisi dans le drawer « Consultations récentes ».
        /// MainWindow s'abonne pour exécuter sa sélection patient complète (en-tête + panneaux).
        /// </summary>
        public event EventHandler<PatientIndexEntry>? PatientSwitchRequested;

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
            vm.RestitutionPdfSavedToPatient -= OnRestitutionPdfSaved;
            vm.RestitutionPdfSavedToPatient += OnRestitutionPdfSaved;
            vm.PatientSwitchRequested -= OnPatientSwitchRequested;
            vm.PatientSwitchRequested += OnPatientSwitchRequested;
        }

        private void OnViewModelNoteSaved(object? sender, EventArgs e)
            => NoteSavedToPatient?.Invoke(this, EventArgs.Empty);

        private void OnRestitutionPdfSaved(object? sender, string pdfPath)
            => RestitutionPdfSavedToPatient?.Invoke(this, pdfPath);

        private void OnPatientSwitchRequested(object? sender, PatientIndexEntry patient)
            => PatientSwitchRequested?.Invoke(this, patient);

        /// <summary>
        /// Note finale : referme le popup « + Ajouter une section » après le choix.
        /// L'ajout proprement dit est réalisé par AddFinalNoteSectionCommand (binding).
        /// </summary>
        private void AddSection_Click(object sender, RoutedEventArgs e)
        {
            if (AddSectionToggle != null) AddSectionToggle.IsChecked = false;
        }

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
                               ProjetTherapeutiquePilotageService? projetTherapeutiquePilotage = null,
                               ProjetTherapeutiqueRelectureService? projetTherapeutiqueRelecteur = null)
        {
            _viewModel ??= DataContext as ConsultationModeViewModel;
            _viewModel?.InjectServices(llmService, storageService, whisperService);
            if (_etapeModelesEnAttente != null)
                _viewModel?.InjectEtapeModeles(_etapeModelesEnAttente);
            if (patientIndex != null)
                _viewModel?.InjectPatientIndex(patientIndex);
            if (urgenceDispatcher != null && urgenceLogService != null)
                _viewModel?.InjectUrgenceDispatcher(urgenceDispatcher, urgenceLogService);
            if (evaluationPhaseService != null)
                _viewModel?.InjectEvaluationServices(evaluationPhaseService, preparationSuggester, axesSuggester, axisExtractor, bilanFinalSuggester, feuilleLecture, brancheLecture);
            if (syntheseGlobaleService != null)
                _viewModel?.InjectSyntheseGlobaleService(syntheseGlobaleService, syntheseGlobaleSuggester, synthesisWeightTracker, syntheseGlobaleRelecteur);
            if (projetTherapeutiqueService != null)
                _viewModel?.InjectProjetTherapeutiqueService(projetTherapeutiqueService, projetTherapeutiqueSuggester, projetTherapeutiquePilotage, projetTherapeutiqueRelecteur);
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

        // Rafraîchit la liste des micros détectés à l'ouverture du menu (un micro USB
        // peut avoir été branché/débranché depuis le dernier rafraîchissement).
        private void MicrophoneComboBox_DropDownOpened(object sender, EventArgs e)
        {
            _viewModel ??= DataContext as ConsultationModeViewModel;
            _viewModel?.RefreshAvailableMicrophones();
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
        private static void SaveDocumentSynthesisToDisk(string nomComplet, MedCompanion.Models.PatientDocument document, string synthesisText)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(synthesisText)) return;

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

{synthesisText}
";
                File.WriteAllText(synthesePath, syntheseContent, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MedDocSynthesis] Erreur sauvegarde synthèse: {ex.Message}");
            }
        }

        /// <summary>
        /// Génère la synthèse détaillée d'un document (même méthode que le bouton "Générer synthèse"
        /// du mode Console : GenerateSingleDocumentSynthesisAsync) afin que la synthèse produite
        /// automatiquement en mode Consultation soit de la même qualité qu'en mode Console.
        /// En cas d'échec, retombe sur le résumé court (document.Summary).
        /// </summary>
        /// <summary>
        /// Poids retenu quand le modèle n'a pas rendu d'évaluation exploitable. Valeur historique,
        /// qui était jusqu'ici appliquée à TOUS les documents.
        /// </summary>
        private const double PoidsDocumentParDefaut = 0.7;

        /// <returns>
        /// La synthèse ET le poids que le modèle lui a attribué (0,0 à 1,0).
        ///
        /// Ce poids était calculé puis jeté : le prompt demande explicitement une évaluation
        /// d'importance — 0,9 pour un bilan psychologique complet, 0,2 pour une pièce administrative
        /// — le service la parsait et la retournait, et l'appelant écrivait 0,7 en dur pour tout le
        /// monde. Un bilan neuropsychologique pesait donc autant qu'un courrier de rappel dans la
        /// Synthèse Initiale.
        /// </returns>
        private async Task<(string synthese, double poids)> GenerateRichDocumentSynthesisAsync(
            MedCompanion.Models.PatientDocument document)
        {
            try
            {
                MedCompanion.Models.PatientMetadata? patientData = _viewModel?.CurrentPatient == null ? null
                    : new MedCompanion.Models.PatientMetadata
                    {
                        Prenom = _viewModel.CurrentPatient.Prenom,
                        Nom = _viewModel.CurrentPatient.Nom
                    };

                var (synthesis, poids) = await _documentService!.GenerateSingleDocumentSynthesisAsync(document, patientData);

                // Un poids nul ou hors bornes signale une évaluation absente ou mal formée, pas un
                // document sans intérêt : on retombe alors sur la valeur par défaut plutôt que
                // d'annuler silencieusement l'influence du document.
                var poidsRetenu = poids > 0 && poids <= 1.0 ? poids : PoidsDocumentParDefaut;

                return (string.IsNullOrWhiteSpace(synthesis) ? (document.Summary ?? "") : synthesis, poidsRetenu);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MedDocSynthesis] Erreur génération synthèse détaillée: {ex.Message}");
                return (document.Summary ?? "", PoidsDocumentParDefaut);
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

                if (success && document != null && document.IsFormulaireCompletion)
                {
                    TraiterFormulaireCompletion(document);
                }
                else if (success && document != null)
                {
                    _viewModel.MedDocumentStatus = "⏳ Génération de la synthèse détaillée…";
                    var (richSynthesis, poidsDocument) = await GenerateRichDocumentSynthesisAsync(document);

                    // Auto-sauvegarde la synthèse du document (compatibilité Console)
                    SaveDocumentSynthesisToDisk(_viewModel.CurrentPatient.NomComplet, document, richSynthesis);

                    // Ajoute aussi à la liste in-memory utile si on entre en Synthèse Initiale après
                    _viewModel.ImportedDocuments.Add(new ImportedConsultationDocument
                    {
                        FileName = document.FileName,
                        FilePath = document.FilePath ?? dlg.FileName,
                        DocumentSynthesis = richSynthesis,
                        Category = document.Category ?? "Documents",
                        // Même évaluation par document que sur le chemin scanner : le mode d'entrée
                        // — import ou scan — ne dit rien de l'importance clinique de la pièce.
                        Weight = poidsDocument
                    });

                    // Rafraîchit les onglets BILANS et DOCS du dossier bleu
                    _viewModel.LoadPatientBilansFromDisk();
                    _viewModel.LoadPatientDocumentsFromDisk();
                    _viewModel.RefreshAdminInfoPublic();

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

        /// <summary>
        /// Le formulaire de complétion revient rempli : on l'archive et on enchaîne sur la lecture
        /// champ par champ, sans synthèse et sans l'ajouter aux documents pondérés.
        ///
        /// Les deux exclusions comptent autant l'une que l'autre. La synthèse ne pourrait décrire
        /// que le gabarit vierge, le manuscrit étant absent de la couche texte. Et la pondération à
        /// 0,7 faisait entrer une pièce purement administrative — téléphones, courriels,
        /// autorisations — dans le calcul de la Synthèse Initiale.
        /// </summary>
        private void TraiterFormulaireCompletion(PatientDocument document)
        {
            if (_viewModel?.CurrentPatient == null) return;

            // Deux feuilles portent le même préfixe de jeton et sont donc reconnues par la même
            // machinerie — mais elles ne se lisent pas pareil. Le questionnaire de cartographie
            // se dépouille par cases cochées ; l'ouvrir avec la saisie champ par champ du
            // formulaire de complétion ne rendrait rien.
            if (string.Equals(document.FormulaireId, "CARTO", StringComparison.OrdinalIgnoreCase))
            {
                _viewModel.MedDocumentStatus = "✅ Questionnaire de cartographie reconnu.";
                _viewModel.LoadPatientDocumentsFromDisk();
                _viewModel.OuvrirDepouillementCartographie(document.FilePath);
                return;
            }

            _viewModel.MedDocumentStatus = "✅ Formulaire reconnu — lecture des champs…";
            _viewModel.LoadPatientDocumentsFromDisk();

            // Le type ET la version viennent du jeton lu à l'import : c'est la version imprimée,
            // pas celle du gabarit courant, qui désigne la géométrie de lecture.
            var saisie = new MedCompanion.Dialogs.FormulaireSaisieDialog(
                document.FilePath, _viewModel.CurrentPatient.DirectoryPath,
                document.FormulaireId, document.FormulaireVersion, document.ExtractedText)
            {
                Owner = Window.GetWindow(this)
            };
            saisie.ShowDialog();

            _viewModel.RefreshAdminInfoPublic();
            _viewModel.MedDocumentStatus = "✅ Formulaire de complétion traité.";
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

                if (success && document != null && document.IsFormulaireCompletion)
                {
                    TraiterFormulaireCompletion(document);
                }
                else if (success && document != null)
                {
                    _viewModel.MedDocumentStatus = "⏳ Génération de la synthèse détaillée…";
                    var (richSynthesis, poidsDocument) = await GenerateRichDocumentSynthesisAsync(document);

                    // Auto-sauvegarde la synthèse du document (compatibilité Console)
                    SaveDocumentSynthesisToDisk(_viewModel.CurrentPatient.NomComplet, document, richSynthesis);

                    _viewModel.ImportedDocuments.Add(new ImportedConsultationDocument
                    {
                        FileName = document.FileName,
                        FilePath = document.FilePath ?? scanDialog.ScannedFilePath,
                        DocumentSynthesis = richSynthesis,
                        Category = document.Category ?? "Documents",
                        // Poids évalué par le modèle sur ce document précis, plutôt qu'un 0,7
                        // uniforme : un bilan psychologique complet et un courrier de rappel ne
                        // doivent pas peser pareil dans la Synthèse Initiale.
                        Weight = poidsDocument
                    });

                    // Rafraîchit les onglets BILANS et DOCS du dossier bleu
                    _viewModel.LoadPatientBilansFromDisk();
                    _viewModel.LoadPatientDocumentsFromDisk();
                    _viewModel.RefreshAdminInfoPublic();

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

        /// <summary>
        /// Carte 3 du bloc Cartographie : récupérer la feuille remplie par le parent.
        /// Reprend exactement le procédé du formulaire de complétion — <see cref="ScanDocumentDialog"/>,
        /// qui offre le scanner, l'import d'un fichier et la photo.
        ///
        /// Le geste s'accomplit avec la famille dans la pièce : aucun résultat n'est affiché,
        /// aucune analyse n'est lancée. On archive la feuille, on le dit, et c'est tout.
        /// La lecture des réponses viendra après la séance.
        /// </summary>
        private void CartoScanBtn_Click(object sender, RoutedEventArgs e)
        {
            _viewModel ??= DataContext as ConsultationModeViewModel;
            if (_viewModel == null || _scannerService == null || _viewModel.CurrentPatient == null)
            {
                MessageBox.Show("Scanner non disponible ou patient non sélectionné.",
                    "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new ScanDocumentDialog(_scannerService) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true || string.IsNullOrEmpty(dlg.ScannedFilePath)) return;

            // 1. La feuille est archivée à côté de la fiche de séance…
            _viewModel.EnregistrerScanQuestionnaire(dlg.ScannedFilePath);

            // 2. …et versée aux Documents du dossier bleu, comme le formulaire de complétion.
            //    Elle y devient consultable, et son crayon rouvre le dépouillement.
            _ = ImporterQuestionnaireDansDocumentsAsync(dlg.ScannedFilePath);
        }

        /// <summary>
        /// Verse la feuille scannée dans les Documents du patient, catégorie « Formulaires ».
        ///
        /// Le formulaire est DÉCLARÉ, pas reconnu : l'utilisateur vient de cliquer « Scanner la
        /// feuille remplie » sur la carte Cartographie, il n'y a aucune ambiguïté. Passer par la
        /// reconnaissance de contenu la ferait échouer — une feuille manuscrite scannée n'a pas de
        /// couche texte — et le document finirait classé « bilans », sans son crayon de saisie.
        /// </summary>
        private async System.Threading.Tasks.Task ImporterQuestionnaireDansDocumentsAsync(string scannedPath)
        {
            if (_viewModel?.CurrentPatient == null || _documentService == null) return;

            var def = MedCompanion.Models.FormulairesConnus.Par("CARTO");
            if (def == null) return;

            var (ok, _, message) = await _documentService.ImportFormulaireConnuAsync(
                scannedPath, _viewModel.CurrentPatient.NomComplet, def, def.VersionCourante);

            if (ok) _viewModel.LoadPatientDocumentsFromDisk();
            else    _viewModel.CartographieStatusMessage =
                        $"⚠ Feuille archivée, mais non ajoutée aux Documents : {message}";
        }

        private void CartoDepouillerBtn_Click(object sender, RoutedEventArgs e)
        {
            _viewModel ??= DataContext as ConsultationModeViewModel;
            _viewModel?.OuvrirDepouillementCartographie();
        }
    }
}
