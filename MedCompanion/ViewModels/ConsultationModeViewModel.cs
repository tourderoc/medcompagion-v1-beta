using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.Services.Consultation;
using MedCompanion.Services.LLM;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// ViewModel pour le Mode Consultation
    /// Gère l'état de l'interface adaptative (travail/dossier) et Med assistant
    /// </summary>
    public class ConsultationModeViewModel : INotifyPropertyChanged
    {
        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion

        #region Layout State

        private ConsultationViewState _currentState = ConsultationViewState.Consultation;
        /// <summary>
        /// État actuel de l'affichage (FocusTravail, Consultation, FocusDossier)
        /// </summary>
        public ConsultationViewState CurrentState
        {
            get => _currentState;
            set
            {
                if (SetProperty(ref _currentState, value))
                {
                    UpdateLayoutProportions();
                    OnPropertyChanged(nameof(IsFocusTravail));
                    OnPropertyChanged(nameof(IsConsultation));
                    OnPropertyChanged(nameof(IsFocusDossier));
                }
            }
        }

        public bool IsFocusTravail => CurrentState == ConsultationViewState.FocusTravail;
        public bool IsConsultation => CurrentState == ConsultationViewState.Consultation;
        public bool IsFocusDossier => CurrentState == ConsultationViewState.FocusDossier;

        private double _workspaceWidth = 0.67;
        /// <summary>
        /// Proportion de l'espace de travail (0 à 1)
        /// </summary>
        public double WorkspaceWidth
        {
            get => _workspaceWidth;
            set => SetProperty(ref _workspaceWidth, value);
        }

        private double _dossierWidth = 0.33;
        /// <summary>
        /// Proportion du dossier patient (0 à 1)
        /// </summary>
        public double DossierWidth
        {
            get => _dossierWidth;
            set => SetProperty(ref _dossierWidth, value);
        }

        /// <summary>
        /// Met à jour les proportions selon l'état actuel
        /// </summary>
        private void UpdateLayoutProportions()
        {
            switch (CurrentState)
            {
                case ConsultationViewState.FocusTravail:
                    WorkspaceWidth = 1.0;
                    DossierWidth = 0.0;
                    break;
                case ConsultationViewState.Consultation:
                    WorkspaceWidth = 0.67;
                    DossierWidth = 0.33;
                    break;
                case ConsultationViewState.FocusDossier:
                    WorkspaceWidth = 0.0;
                    DossierWidth = 1.0;
                    break;
            }
        }

        #endregion

        #region Med Assistant State

        private MedConsultationMode _medMode = MedConsultationMode.Suggestions;
        /// <summary>
        /// Mode de comportement de Med (Silencieux, Suggestions, Checklist)
        /// </summary>
        public MedConsultationMode MedMode
        {
            get => _medMode;
            set
            {
                if (SetProperty(ref _medMode, value))
                {
                    OnPropertyChanged(nameof(IsMedSilencieux));
                    OnPropertyChanged(nameof(IsMedSuggestions));
                    OnPropertyChanged(nameof(IsMedChecklist));
                }
            }
        }

        public bool IsMedSilencieux => MedMode == MedConsultationMode.Silencieux;
        public bool IsMedSuggestions => MedMode == MedConsultationMode.Suggestions;
        public bool IsMedChecklist => MedMode == MedConsultationMode.Checklist;

        private bool _isMedExpanded = true;
        /// <summary>
        /// Indique si le panneau Med est étendu ou réduit
        /// </summary>
        public bool IsMedExpanded
        {
            get => _isMedExpanded;
            set
            {
                if (SetProperty(ref _isMedExpanded, value))
                {
                    OnPropertyChanged(nameof(IsMedCollapsed));
                }
            }
        }

        /// <summary>
        /// Inverse de IsMedExpanded pour le binding
        /// </summary>
        public bool IsMedCollapsed => !IsMedExpanded;

        /// <summary>
        /// Suggestions contextuelles de Med
        /// </summary>
        public ObservableCollection<MedSuggestion> Suggestions { get; } = new();

        /// <summary>
        /// Items de la checklist
        /// </summary>
        public ObservableCollection<ChecklistItem> ChecklistItems { get; } = new();

        #endregion

        #region Dossier Patient State

        private DossierTab _activeDossierTab = DossierTab.Synthese;
        /// <summary>
        /// Intercalaire actif du dossier
        /// </summary>
        public DossierTab ActiveDossierTab
        {
            get => _activeDossierTab;
            set
            {
                if (SetProperty(ref _activeDossierTab, value))
                {
                    OnPropertyChanged(nameof(IsCouvertureActive));
                    OnPropertyChanged(nameof(IsSyntheseActive));
                    OnPropertyChanged(nameof(IsAdminActive));
                    OnPropertyChanged(nameof(IsConsultationsActive));
                    OnPropertyChanged(nameof(IsProjetActive));
                    OnPropertyChanged(nameof(IsBilansActive));
                    OnPropertyChanged(nameof(IsDocumentsActive));
                }
            }
        }

        public bool IsCouvertureActive => ActiveDossierTab == DossierTab.Couverture;
        public bool IsSyntheseActive => ActiveDossierTab == DossierTab.Synthese;
        public bool IsAdminActive => ActiveDossierTab == DossierTab.Administratif;
        public bool IsConsultationsActive => ActiveDossierTab == DossierTab.Consultations;
        public bool IsProjetActive => ActiveDossierTab == DossierTab.ProjetTherapeutique;
        public bool IsBilansActive => ActiveDossierTab == DossierTab.Bilans;
        public bool IsDocumentsActive => ActiveDossierTab == DossierTab.Documents;

        private PatientIndexEntry? _currentPatient;
        /// <summary>
        /// Patient actuellement en consultation
        /// </summary>
        public PatientIndexEntry? CurrentPatient
        {
            get => _currentPatient;
            set
            {
                if (SetProperty(ref _currentPatient, value))
                {
                    OnPropertyChanged(nameof(HasPatient));
                    OnPropertyChanged(nameof(PatientDisplayName));
                    OnPropertyChanged(nameof(PatientAge));
                }
            }
        }

        public bool HasPatient => CurrentPatient != null;
        public string PatientDisplayName => CurrentPatient != null
            ? $"{CurrentPatient.Nom} {CurrentPatient.Prenom}"
            : "Aucun patient";
        public string PatientAge => !string.IsNullOrEmpty(CurrentPatient?.Dob)
            ? $"Né(e) le {CurrentPatient.Dob}"
            : "";

        #endregion

        #region Note de Consultation

        private string _noteContent = "";
        /// <summary>
        /// Contenu de la note de consultation en cours
        /// </summary>
        public string NoteContent
        {
            get => _noteContent;
            set
            {
                if (SetProperty(ref _noteContent, value))
                {
                    OnPropertyChanged(nameof(HasNoteContent));
                    // Auto-save trigger pourrait être ajouté ici
                }
            }
        }

        public bool HasNoteContent => !string.IsNullOrWhiteSpace(NoteContent);

        private DateTime _consultationDate = DateTime.Now;
        /// <summary>
        /// Date de la consultation en cours
        /// </summary>
        public DateTime ConsultationDate
        {
            get => _consultationDate;
            set => SetProperty(ref _consultationDate, value);
        }

        #endregion

        #region Interrogatoire V0b (Blocs Adaptatifs)

        private ILLMService? _llmService;
        private StorageService? _storageService;
        private readonly InterrogatoireExtractorService  _extractor            = new();
        private readonly IncrementalExtractorService     _incrementalExtractor = new();
        private readonly QualityCheckService             _qualityChecker       = new();
        private WhisperStreamingService? _whisperService;

        // ── V0b : services adaptatifs ──────────────────────────────────────────
        private readonly BlockSetResolver            _blockSetResolver     = new();
        private readonly MotifPrincipalDetector       _motifDetector        = new();
        private readonly ContextualBlockSuggester     _blockSuggester;
        private readonly BlockPrefiller               _blockPrefiller       = new();

        /// <summary>Âge confirmé du patient (null si pas encore confirmé)</summary>
        private int? _confirmedAge;
        public int? ConfirmedAge
        {
            get => _confirmedAge;
            set
            {
                if (SetProperty(ref _confirmedAge, value))
                {
                    OnPropertyChanged(nameof(HasConfirmedAge));
                    if (value.HasValue)
                        OnAgeConfirmed(value.Value);
                }
            }
        }

        private bool _isAgeConfirmed;
        public bool HasConfirmedAge => _isAgeConfirmed && _confirmedAge.HasValue;

        /// <summary>Motif principal détecté</summary>
        private string _detectedMotif = "";
        public string DetectedMotif
        {
            get => _detectedMotif;
            set
            {
                if (SetProperty(ref _detectedMotif, value))
                    OnPropertyChanged(nameof(HasDetectedMotif));
            }
        }
        public bool HasDetectedMotif => !string.IsNullOrWhiteSpace(_detectedMotif);

        /// <summary>Suggestions de blocs supplémentaires (chips)</summary>
        public ObservableCollection<BlockSuggestionViewModel> BlockSuggestions { get; } = new();
        public bool HasBlockSuggestions => BlockSuggestions.Count > 0;

        /// <summary>Indique si la structure est gelée (après ~90s)</summary>
        private bool _isStructureFrozen;
        public bool IsStructureFrozen
        {
            get => _isStructureFrozen;
            set => SetProperty(ref _isStructureFrozen, value);
        }

        // File d'extraction : jamais deux appels LLM simultanés
        private readonly SemaphoreSlim _extractionLock    = new(1, 1);
        private string                 _pendingSegment    = "";

        public void InjectServices(ILLMService llmService, StorageService storageService,
                                   WhisperStreamingService? whisperService = null)
        {
            _llmService     = llmService;
            _storageService = storageService;

            if (whisperService != null)
            {
                _whisperService = whisperService;
                _whisperService.StatusChanged         += msg  => Dispatch(() => ExtractionStatus = msg);
                _whisperService.TextAppended          += text => Dispatch(() => TranscriptionInput += text);
                _whisperService.SegmentReady          += seg  => _ = EnqueueExtractionAsync(seg);
                _whisperService.SessionResetRequested += ()   => Dispatch(ResetDictationSession);
            }
        }

        /// <summary>
        /// Réinitialise complètement la session de dictée :
        /// - vide la transcription
        /// - vide le segment en attente d'extraction
        /// - réinitialise les blocs (texte + thèmes)
        /// </summary>
        private void ResetDictationSession()
        {
            TranscriptionInput = "";
            _pendingSegment    = "";

            foreach (var block in InterrogatoireBlocks)
                block.Reset();

            // V0b : réinitialiser les détecteurs
            _motifDetector.Reset();
            DetectedMotif = "";
            _isAgeConfirmed = false;
            OnPropertyChanged(nameof(HasConfirmedAge));
            BlockSuggestions.Clear();
            OnPropertyChanged(nameof(HasBlockSuggestions));
            IsStructureFrozen = false;

            // Repasse en mode Saisie si on était dans un autre état
            if (IsInterrogatoireMode)
                InterrogatoireState = InterrogatoireState.Saisie;
        }

        /// <summary>
        /// Accumule les segments et déclenche une extraction incrémentale dès que le LLM est libre.
        /// </summary>
        private async Task EnqueueExtractionAsync(string newSegment)
        {
            _pendingSegment += " " + newSegment;

            if (!await _extractionLock.WaitAsync(0)) return;

            try
            {
                while (!string.IsNullOrWhiteSpace(_pendingSegment))
                {
                    var segment = _pendingSegment.Trim();
                    _pendingSegment = "";
                    await ExtractIncrementalAsync(segment);
                }
            }
            finally
            {
                _extractionLock.Release();
            }
        }

        private async Task ExtractIncrementalAsync(string segment)
        {
            if (_llmService == null || string.IsNullOrWhiteSpace(segment)) return;

            Dispatch(() => ExtractionStatus = "⟳ Extraction en cours...");

            var currentBlocks = InterrogatoireBlocks
                .Select(vm => new ConsultationBlock
                {
                    Key           = vm.Key,
                    Title         = vm.Title,
                    FreeText      = vm.FreeText,
                    ExpectedThemes = new List<string>(vm.ExpectedThemes),
                    CoveredThemes = new List<string>(vm.CoveredThemes)
                }).ToList();

            var (ok, result, err) = await _incrementalExtractor.ExtractAsync(
                _llmService, segment, currentBlocks);

            if (!ok || result == null)
            {
                Dispatch(() => ExtractionStatus = $"Erreur extraction : {err}");
                return;
            }

            Dispatch(() =>
            {
                foreach (var update in result.Updates)
                {
                    var blockVm = InterrogatoireBlocks.FirstOrDefault(b => b.Key == update.BlockKey);
                    if (blockVm == null) continue;

                    if (!string.IsNullOrWhiteSpace(update.AppendText))
                        blockVm.AppendText(update.AppendText);

                    foreach (var theme in update.NewThemes)
                        blockVm.AddTheme(theme);
                }
                UpdateBlockCollections(); // V0c : mettre à jour Active/Completed
                ExtractionStatus = "● Enregistrement...";

                // V0b : vérifier si l'âge est confirmé (thème "age" dans le bloc "identite")
                if (!_isAgeConfirmed)
                {
                    var identiteBlock = InterrogatoireBlocks.FirstOrDefault(b => b.Key == "identite");
                    if (identiteBlock != null && identiteBlock.CoveredThemes.Contains("age"))
                    {
                        _isAgeConfirmed = true;
                        OnPropertyChanged(nameof(HasConfirmedAge));
                        System.Diagnostics.Debug.WriteLine("[V0b] Âge confirmé via l'interrogatoire.");
                    }
                }

                // V0b : vérifier si le motif principal est détecté
                _motifDetector.CheckForMotif(InterrogatoireBlocks);
            });
        }

        private static void Dispatch(Action a)
            => System.Windows.Application.Current?.Dispatcher.Invoke(a);

        // Type de consultation
        private ConsultationType _consultationType = ConsultationType.Normal;
        public ConsultationType ConsultationType
        {
            get => _consultationType;
            set
            {
                if (SetProperty(ref _consultationType, value))
                {
                    OnPropertyChanged(nameof(IsInterrogatoireMode));
                    OnPropertyChanged(nameof(IsNormalMode));
                    if (value == ConsultationType.PremiereConsultation)
                        InitInterrogatoireBlocks();
                    // Forcer la notification même si InterrogatoireState n'a pas changé de valeur
                    OnPropertyChanged(nameof(IsInSaisieMode));
                    OnPropertyChanged(nameof(IsInExtractionMode));
                    OnPropertyChanged(nameof(IsInFinalNoteMode));
                    OnPropertyChanged(nameof(CanExtract));
                }
            }
        }

        public bool IsInterrogatoireMode => ConsultationType == ConsultationType.PremiereConsultation;
        public bool IsNormalMode => !IsInterrogatoireMode;

        // État interne de l'interrogatoire
        private InterrogatoireState _interrogatoireState = InterrogatoireState.Saisie;
        public InterrogatoireState InterrogatoireState
        {
            get => _interrogatoireState;
            set
            {
                if (SetProperty(ref _interrogatoireState, value))
                {
                    OnPropertyChanged(nameof(IsInSaisieMode));
                    OnPropertyChanged(nameof(IsInExtractionMode));
                    OnPropertyChanged(nameof(IsInFinalNoteMode));
                }
            }
        }

        public bool IsInSaisieMode    => IsInterrogatoireMode && InterrogatoireState == InterrogatoireState.Saisie && !IsInClinicalMode && !IsSynthesisMode;
        public bool IsInExtractionMode => IsInterrogatoireMode && InterrogatoireState == InterrogatoireState.Extraction && !IsInClinicalMode && !IsSynthesisMode;
        public bool IsInFinalNoteMode  => IsInterrogatoireMode && InterrogatoireState == InterrogatoireState.FinalNote && !IsInClinicalMode && !IsSynthesisMode;

        // Transcription
        private string _transcriptionInput = "";
        public string TranscriptionInput
        {
            get => _transcriptionInput;
            set
            {
                if (SetProperty(ref _transcriptionInput, value))
                    OnPropertyChanged(nameof(CanExtract));
            }
        }

        public bool CanExtract => IsInSaisieMode && !string.IsNullOrWhiteSpace(TranscriptionInput) && _llmService != null;

        // Mode de dictée (Batch recommandé pour consultation, Streaming en option pour test)
        private bool _useBatchMode = true;
        public bool UseBatchMode
        {
            get => _useBatchMode;
            set => SetProperty(ref _useBatchMode, value);
        }

        // Durée du chunk en mode Batch (60s / 90s / 120s)
        private int _batchDurationSeconds = 90;
        public int BatchDurationSeconds
        {
            get => _batchDurationSeconds;
            set => SetProperty(ref _batchDurationSeconds, value);
        }

        // Enregistrement continu
        private bool _isRecording;
        public bool IsRecording
        {
            get => _isRecording;
            set
            {
                if (SetProperty(ref _isRecording, value))
                {
                    OnPropertyChanged(nameof(IsNotRecording));
                    OnPropertyChanged(nameof(RecordingStatusColor));
                    System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                        System.Windows.Input.CommandManager.InvalidateRequerySuggested);
                }
            }
        }
        public bool IsNotRecording => !_isRecording;

        public string RecordingStatusColor => IsRecording ? "#27AE60" : "#AAAAAA";

        // Blocs interrogatoire
        public ObservableCollection<ConsultationBlockViewModel> InterrogatoireBlocks { get; } = new();

        // V0c : Blocs séparés (actifs vs complétés)
        private ObservableCollection<ConsultationBlockViewModel> _activeBlocks = new();
        public IReadOnlyList<ConsultationBlockViewModel> ActiveBlocks => _activeBlocks;

        private ObservableCollection<ConsultationBlockViewModel> _completedBlocks = new();
        public IReadOnlyList<ConsultationBlockViewModel> CompletedBlocks => _completedBlocks;

        private void InitInterrogatoireBlocks()
        {
            InterrogatoireBlocks.Clear();
            BlockSuggestions.Clear();
            OnPropertyChanged(nameof(HasBlockSuggestions));

            // V0b : résoudre les blocs selon l'âge confirmé
            List<BlockDefinition> definitions;
            if (_confirmedAge.HasValue)
            {
                definitions = _blockSetResolver.Resolve(_confirmedAge.Value);
            }
            else
            {
                // Âge pas encore confirmé → noyau fixe uniquement
                definitions = _blockSetResolver.ResolveWithoutAge();
            }

            foreach (var d in definitions)
                InterrogatoireBlocks.Add(ConsultationBlockViewModel.FromDefinition(d));

            // Réinitialiser les détecteurs V0b
            _motifDetector.Reset();
            DetectedMotif = "";
            IsStructureFrozen = false;

            InterrogatoireState = InterrogatoireState.Saisie;
            TranscriptionInput = "";
            NoteContent = "";
            _noteSaved = false;
            IsInClinicalMode = false;
            IsSynthesisMode = false;

            UpdateBlockCollections();
        }

        /// <summary>
        /// V0c : Met à jour les collections Active/Completed
        /// appelée après chaque changement de bloc (progression, masquage)
        /// </summary>
        private void UpdateBlockCollections()
        {
            _activeBlocks.Clear();
            _completedBlocks.Clear();

            foreach (var block in InterrogatoireBlocks.Where(b => !b.IsHidden))
            {
                if (block.IsCompleted)
                    _completedBlocks.Add(block);
                else
                    _activeBlocks.Add(block);
            }

            OnPropertyChanged(nameof(ActiveBlocks));
            OnPropertyChanged(nameof(CompletedBlocks));

            // V0c : Geler structure quand age ET motif à 100%
            CheckAndFreezeStructureIfReady();
        }

        /// <summary>
        /// V0c : Gèle la structure dès que les deux blocs clés (age + motif) sont à 100%
        /// plutôt que d'attendre un timer arbitraire.
        /// </summary>
        private void CheckAndFreezeStructureIfReady()
        {
            if (IsStructureFrozen) return;

            var ageBlock = InterrogatoireBlocks.FirstOrDefault(b => b.Key == "age");
            var motifBlock = InterrogatoireBlocks.FirstOrDefault(b => b.Key == "motif");

            if (ageBlock?.IsCompleted == true && motifBlock?.IsCompleted == true)
            {
                IsStructureFrozen = true;
                System.Diagnostics.Debug.WriteLine("[V0c] Structure gelée : age + motif à 100%");
            }
        }

        // ── V0b : Gestion de l'âge confirmé ──────────────────────────────────

        /// <summary>
        /// Appelé quand l'âge du patient est confirmé.
        /// Ajoute silencieusement le macro-bloc contextuel par âge.
        /// </summary>
        private void OnAgeConfirmed(int ageYears)
        {
            // Ne pas ajouter si le macro-bloc est déjà présent
            var existingKeys = InterrogatoireBlocks.Select(b => b.Key).ToHashSet();
            var ageBlock = _blockSetResolver.GetAgeBlock(ageYears);

            if (ageBlock != null && !existingKeys.Contains(ageBlock.Key))
            {
                InterrogatoireBlocks.Add(ConsultationBlockViewModel.FromDefinition(ageBlock));
                System.Diagnostics.Debug.WriteLine($"[V0b] Macro-bloc ajouté : {ageBlock.Title} (âge={ageYears})");
            }
        }

        // ── V0b : Gestion du motif détecté ────────────────────────────────────

        /// <summary>
        /// Appelé quand le motif principal est détecté (one-shot).
        /// Lance la suggestion de blocs supplémentaires.
        /// </summary>
        private async void OnMotifDetected(string motif)
        {
            Dispatch(() => DetectedMotif = motif);

            if (!_confirmedAge.HasValue) return;

            var activeKeys = InterrogatoireBlocks.Select(b => b.Key).ToList();
            var suggestions = await _blockSuggester.SuggestAsync(
                motif, _confirmedAge.Value, activeKeys, _llmService);

            Dispatch(() =>
            {
                BlockSuggestions.Clear();
                foreach (var s in suggestions)
                    BlockSuggestions.Add(new BlockSuggestionViewModel(s, this));
                OnPropertyChanged(nameof(HasBlockSuggestions));

                // Geler la structure
                IsStructureFrozen = true;
                System.Diagnostics.Debug.WriteLine($"[V0b] Structure gelée. {suggestions.Count} chips suggérés.");
            });
        }

        // ── V0b : Accept/Dismiss chips ────────────────────────────────────────

        /// <summary>
        /// Accepte un chip : ajoute le bloc et pré-remplit avec le contexte existant.
        /// </summary>
        internal async Task AcceptBlockSuggestionAsync(BlockSuggestion suggestion)
        {
            var definition = _blockSetResolver.GetByKey(suggestion.BlockKey);
            if (definition == null) return;

            suggestion.IsAccepted = true;

            // Ajouter le bloc
            var newBlock = ConsultationBlockViewModel.FromDefinition(definition);
            InterrogatoireBlocks.Add(newBlock);

            // Pré-remplir si un LLM est disponible
            if (_llmService != null)
            {
                ExtractionStatus = $"⟳ Pré-remplissage {definition.Title}...";
                var (ok, prefillText, themes) = await _blockPrefiller.PrefillAsync(
                    _llmService, definition, InterrogatoireBlocks.Where(b => b.Key != definition.Key));

                if (ok && !string.IsNullOrWhiteSpace(prefillText))
                {
                    Dispatch(() =>
                    {
                        newBlock.AppendText(prefillText);
                        foreach (var theme in themes)
                            newBlock.AddTheme(theme);
                        ExtractionStatus = $"✓ {definition.Title} pré-rempli.";
                    });
                }
                else
                {
                    Dispatch(() => ExtractionStatus = $"✓ {definition.Title} ajouté.");
                }
            }

            // Retirer le chip de la liste
            var chipVm = BlockSuggestions.FirstOrDefault(c => c.Suggestion.BlockKey == suggestion.BlockKey);
            if (chipVm != null)
                BlockSuggestions.Remove(chipVm);
            OnPropertyChanged(nameof(HasBlockSuggestions));
        }

        /// <summary>
        /// Rejette un chip : le retire de la liste sans ajouter le bloc.
        /// </summary>
        internal void DismissBlockSuggestion(BlockSuggestion suggestion)
        {
            suggestion.IsDismissed = true;
            var chipVm = BlockSuggestions.FirstOrDefault(c => c.Suggestion.BlockKey == suggestion.BlockKey);
            if (chipVm != null)
                BlockSuggestions.Remove(chipVm);
            OnPropertyChanged(nameof(HasBlockSuggestions));
        }

        private bool _noteSaved = false;

        // Extraction
        private string _extractionStatus = "";
        public string ExtractionStatus
        {
            get => _extractionStatus;
            set => SetProperty(ref _extractionStatus, value);
        }

        private async Task ExtractInterrogatoireAsync()
        {
            if (_llmService == null || string.IsNullOrWhiteSpace(TranscriptionInput))
                return;

            InterrogatoireState = InterrogatoireState.Extraction;
            ExtractionStatus = "Extraction en cours...";

            var (ok, result, err) = await _extractor.ExtractAsync(_llmService, TranscriptionInput);

            if (!ok || result == null)
            {
                ExtractionStatus = $"Erreur : {err}";
                InterrogatoireState = InterrogatoireState.Saisie;
                return;
            }

            // Appliquer les mises à jour aux ViewModels des blocs
            foreach (var update in result.Updates)
            {
                var blockVm = InterrogatoireBlocks.FirstOrDefault(b => b.Key == update.BlockKey);
                if (blockVm == null) continue;

                if (!string.IsNullOrWhiteSpace(update.AppendText))
                    blockVm.AppendText(update.AppendText);

                foreach (var theme in update.NewThemes)
                    blockVm.AddTheme(theme);
            }
            UpdateBlockCollections(); // V0c : mettre à jour Active/Completed

            // Construire la note finale
            var blocks = InterrogatoireBlocks.Select(vm => new ConsultationBlock
            {
                Key = vm.Key, Title = vm.Title, FreeText = vm.FreeText,
                ExpectedThemes = vm.ExpectedThemes, CoveredThemes = vm.CoveredThemes
            }).ToList();

            NoteContent = InterrogatoireExtractorService.BuildFinalNote(blocks, ConsultationDate);
            ExtractionStatus = "Extraction terminée — vérifiez et complétez.";
            InterrogatoireState = InterrogatoireState.FinalNote;
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                System.Windows.Input.CommandManager.InvalidateRequerySuggested);

            // Passe qualité en arrière-plan (non bloquante)
            _ = RunQualityCheckAsync(NoteContent);
        }

        // ── Contrôle qualité ─────────────────────────────────────────────────

        public ObservableCollection<QualityIssueViewModel> QualityIssues { get; } = new();

        private bool _isRunningQualityCheck;
        public bool IsRunningQualityCheck
        {
            get => _isRunningQualityCheck;
            set { if (SetProperty(ref _isRunningQualityCheck, value)) OnPropertyChanged(nameof(HasQualityIssues)); }
        }

        public bool HasQualityIssues => QualityIssues.Count > 0 || IsRunningQualityCheck;

        private async Task RunQualityCheckAsync(string noteContent)
        {
            if (_llmService == null) return;
            Dispatch(() => { IsRunningQualityCheck = true; QualityIssues.Clear(); });

            var issues = await _qualityChecker.CheckAsync(_llmService, noteContent);

            Dispatch(() =>
            {
                IsRunningQualityCheck = false;
                QualityIssues.Clear();
                foreach (var issue in issues)
                    QualityIssues.Add(new QualityIssueViewModel(issue, this));
                OnPropertyChanged(nameof(HasQualityIssues));
            });
        }

        internal void ApplyQualityIssue(QualityIssue issue)
        {
            NoteContent = NoteContent.Replace(issue.Original, issue.Suggestion,
                System.StringComparison.OrdinalIgnoreCase);
        }

        internal void DismissQualityIssue(QualityIssueViewModel vm)
        {
            QualityIssues.Remove(vm);
            OnPropertyChanged(nameof(HasQualityIssues));
        }

        private async Task SaveInterrogatoireNoteAsync()
        {
            if (_storageService == null || CurrentPatient == null || string.IsNullOrWhiteSpace(NoteContent))
                return;

            var noteTitle = $"Interrogatoire – {ConsultationDate:dd/MM/yyyy}";
            var (ok, _, err) = _storageService.SaveStructuredNote(CurrentPatient.NomComplet, NoteContent, noteTitle);
            if (ok)
            {
                _noteSaved = true;
                ExtractionStatus = "Note sauvegardée dans le dossier patient.";
                await RefreshConsultationNotesAsync();
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                    System.Windows.Input.CommandManager.InvalidateRequerySuggested);
            }
            else
            {
                ExtractionStatus = $"Erreur sauvegarde : {err}";
            }
        }

        #endregion

        #region Observations Cliniques V0c

        private ClinicalObservationsSession _clinicalObservations = new();
        public ClinicalObservationsSession ClinicalObservations
        {
            get => _clinicalObservations;
            set => SetProperty(ref _clinicalObservations, value);
        }

        private bool _isInClinicalMode = false;
        public bool IsInClinicalMode
        {
            get => _isInClinicalMode;
            set
            {
                if (SetProperty(ref _isInClinicalMode, value))
                {
                    OnPropertyChanged(nameof(IsInSaisieMode));
                    OnPropertyChanged(nameof(IsInExtractionMode));
                    OnPropertyChanged(nameof(IsInFinalNoteMode));
                }
            }
        }

        /// <summary>
        /// Initialise les 10 cartes d'observation clinique avec leurs options
        /// </summary>
        private void InitializeClinicalObservations()
        {
            _clinicalObservations.Cards.Clear();
            _clinicalObservations.CreatedAt = DateTime.Now;

            AddClinicalCard(ClinicalObservationBranch.Contact, "Contact/Rapport",
                new[] { "Bon", "Distant", "Fuyant", "Adhésif", "Instable" });

            AddClinicalCard(ClinicalObservationBranch.Langage, "Langage",
                new[] { "Adapté", "Riche", "Pauvre/Immaturité", "Inexistant" });

            AddClinicalCard(ClinicalObservationBranch.Comprehension, "Compréhension",
                new[] { "Adaptée", "Limitée", "Consignes simples uniquement" });

            AddClinicalCard(ClinicalObservationBranch.Psychomotricite, "Psychomotricité",
                new[] { "Harmonieuse", "Instabilité motrice", "Inhibition", "Maladresse" });

            AddClinicalCard(ClinicalObservationBranch.MimiquRegard, "Mimique & Regard",
                new[] { "Expressive", "Faciès figé", "Regard fuyant", "Pauvreté des mimiques" });

            AddClinicalCard(ClinicalObservationBranch.ProfilCognitif, "Profil Cognitif estimé",
                new[] { "Harmonieux", "Dysharmonieux", "Supérieur", "Retard suspecté" });

            AddClinicalCard(ClinicalObservationBranch.HumeurAnxiete, "Humeur / Anxiété",
                new[] { "Stable", "Triste", "Irritable", "Angoissé" });

            AddClinicalCard(ClinicalObservationBranch.ImaginaireJeu, "Imaginaire / Jeu",
                new[] { "Riche", "Pauvre", "Bizarre", "Stéréotypé" });

            AddClinicalCard(ClinicalObservationBranch.RapportCadre, "Rapport au cadre",
                new[] { "Respecté", "Opposition", "Désinhibé", "Passif" });

            AddClinicalCard(ClinicalObservationBranch.Vigilance, "Vigilance",
                new[] { "R.A.S", "Signes de négligence", "Signes de maltraitance" });

            OnPropertyChanged(nameof(ClinicalObservations));
        }

        private void AddClinicalCard(ClinicalObservationBranch branch, string title, string[] options)
        {
            var card = new ClinicalObservationCard
            {
                Branch = branch,
                Title = title,
                Options = new List<string>(options)
            };
            _clinicalObservations.Cards.Add(card);
        }

        public void SelectObservationOption(ClinicalObservationCard card, string option)
        {
            card.SelectedOption = option;
            OnPropertyChanged(nameof(ClinicalObservations));
        }

        public void ToggleCardExpand(ClinicalObservationCard card)
        {
            card.IsExpanded = !card.IsExpanded;
            OnPropertyChanged(nameof(ClinicalObservations));
        }

        public async Task TerminateClinicalObservationsAsync()
        {
            if (_llmService == null) return;

            var narrative = await GenerateClinicalNarrativeAsync(_clinicalObservations);
            _clinicalObservations.GeneratedClinicalNarrative = narrative;
            OnPropertyChanged(nameof(ClinicalObservations));

            // Basculer vers la note finale
            IsInClinicalMode = false;
            InterrogatoireState = InterrogatoireState.FinalNote;
        }

        private async Task<string> GenerateClinicalNarrativeAsync(ClinicalObservationsSession obs)
        {
            if (_llmService == null) return "";

            var prompt = "Génère un paragraphe clinique cohérent et bien structuré à partir de ces observations cliniques de l'enfant:\n\n";

            foreach (var card in obs.Cards)
            {
                if (card.SelectedOption != null)
                {
                    prompt += $"- {card.Title}: {card.SelectedOption}";
                    if (!string.IsNullOrWhiteSpace(card.FreeText))
                        prompt += $" ({card.FreeText})";
                    prompt += "\n";
                }
            }

            prompt += "\nGénère un seul paragraphe cohérent, cliniquement pertinent, prêt à être intégré dans la note de consultation.";

            var (ok, result, _) = await _llmService.ChatAsync(prompt, new(), maxTokens: 500);
            return ok ? result : "";
        }

        #endregion

        #region Synthèse Initiale V0d

        private InitialSynthesisWeights _synthesisWeights = new();
        public InitialSynthesisWeights SynthesisWeights
        {
            get => _synthesisWeights;
            set => SetProperty(ref _synthesisWeights, value);
        }

        private bool _isSynthesisMode = false;
        public bool IsSynthesisMode
        {
            get => _isSynthesisMode;
            set
            {
                if (SetProperty(ref _isSynthesisMode, value))
                {
                    OnPropertyChanged(nameof(IsInSaisieMode));
                    OnPropertyChanged(nameof(IsInExtractionMode));
                    OnPropertyChanged(nameof(IsInFinalNoteMode));
                }
            }
        }

        private string _synthesisContent = "";
        public string SynthesisContent
        {
            get => _synthesisContent;
            set => SetProperty(ref _synthesisContent, value);
        }

        private bool _areWeightsLoading = false;
        public bool AreWeightsLoading
        {
            get => _areWeightsLoading;
            set => SetProperty(ref _areWeightsLoading, value);
        }

        private bool _isGeneratingSynthesis = false;
        public bool IsGeneratingSynthesis
        {
            get => _isGeneratingSynthesis;
            set => SetProperty(ref _isGeneratingSynthesis, value);
        }

        private string _synthesisStatusMessage = "";
        public string SynthesisStatusMessage
        {
            get => _synthesisStatusMessage;
            set => SetProperty(ref _synthesisStatusMessage, value);
        }

        // V0d : Documents importés
        public ObservableCollection<ImportedConsultationDocument> ImportedDocuments { get; } = new();

        private bool _isImportingDocument = false;
        public bool IsImportingDocument
        {
            get => _isImportingDocument;
            set => SetProperty(ref _isImportingDocument, value);
        }

        private void SwitchToSynthesis()
        {
            IsInClinicalMode = false;
            IsSynthesisMode = true;
        }

        private async Task ProposeWeightsAsync()
        {
            AreWeightsLoading = true;
            SynthesisStatusMessage = "⏳ Analyse des données...";

            try
            {
                if (_llmService == null) return;

                // Construire le prompt pour proposer les poids
                var prompt = BuildWeightProposalPrompt();
                var (success, response, _) = await _llmService.ChatAsync(prompt, new(), maxTokens: 500);

                if (success)
                {
                    // Parser la réponse JSON pour extraire les poids proposés
                    var weights = ExtractWeightsFromResponse(response);

                    SynthesisWeights.InterrogatoireWeight = weights.ContainsKey("interrogatoire")
                        ? weights["interrogatoire"]
                        : 0.5;

                    SynthesisWeights.ObservationsWeight = weights.ContainsKey("observations")
                        ? weights["observations"]
                        : 0.8;

                    SynthesisWeights.DocumentWeights = weights
                        .Where(kv => !kv.Key.StartsWith("interrogatoire") && !kv.Key.StartsWith("observations"))
                        .ToDictionary(kv => kv.Key, kv => kv.Value);

                    SynthesisWeights.LLMJustification = ExtractJustificationFromResponse(response);

                    SynthesisStatusMessage = "✅ Poids proposés (ajustable via sliders)";
                }
                else
                {
                    SynthesisStatusMessage = "❌ Erreur: " + response;
                }
            }
            catch (Exception ex)
            {
                SynthesisStatusMessage = $"❌ Erreur: {ex.Message}";
            }
            finally
            {
                AreWeightsLoading = false;
            }
        }

        private async Task GenerateSynthesisAsync()
        {
            IsGeneratingSynthesis = true;
            SynthesisStatusMessage = "⏳ Génération synthèse pondérée...";

            try
            {
                if (_llmService == null) return;

                // Construire le JSON structuré avec données + poids validés
                var synthesisJSON = BuildInitialSynthesisJSON(SynthesisWeights);

                var prompt = BuildSynthesisGenerationPrompt(synthesisJSON);

                var (success, synthesis, _) = await _llmService.ChatAsync(prompt, new(), maxTokens: 2000);

                if (success)
                {
                    SynthesisContent = synthesis;
                    SynthesisStatusMessage = "✅ Synthèse générée avec succès";
                }
                else
                {
                    SynthesisStatusMessage = "❌ Erreur génération: " + synthesis;
                }
            }
            catch (Exception ex)
            {
                SynthesisStatusMessage = $"❌ Erreur: {ex.Message}";
            }
            finally
            {
                IsGeneratingSynthesis = false;
            }
        }

        private Task SaveSynthesisAsync()
        {
            if (string.IsNullOrEmpty(SynthesisContent))
            {
                SynthesisStatusMessage = "❌ Aucune synthèse à sauvegarder";
                return Task.CompletedTask;
            }

            if (_storageService == null || _currentPatient == null)
            {
                SynthesisStatusMessage = "❌ Services non disponibles";
                return Task.CompletedTask;
            }

            try
            {
                // Construire le Markdown avec métadonnées YAML
                var now = DateTime.Now;
                var synthesisWithMetadata = $@"---
date_synthese: {now:yyyy-MM-ddTHH:mm:ss}
type: initial_consultation
weights:
  interrogatoire: {SynthesisWeights.InterrogatoireWeight:F1}
  observations: {SynthesisWeights.ObservationsWeight:F1}
  moyenne: {SynthesisWeights.AverageWeight:F1}
justification: {SynthesisWeights.LLMJustification ?? ""}
---

{SynthesisContent}";

                // Sauvegarder le fichier via SaveStructuredNote
                var (success, message, filePath) = _storageService.SaveStructuredNote(
                    _currentPatient.NomComplet,
                    SynthesisContent,
                    "Synthèse Initiale - Première Consultation");

                if (success && !string.IsNullOrEmpty(filePath))
                {
                    // Réécrire avec le fichier pour ajouter les métadonnées de poids
                    try
                    {
                        File.WriteAllText(filePath, synthesisWithMetadata, System.Text.Encoding.UTF8);
                    }
                    catch { }

                    SynthesisStatusMessage = $"✅ Synthèse sauvegardée: {Path.GetFileName(filePath)}";
                }
                else
                {
                    SynthesisStatusMessage = "⚠️ Synthèse créée mais: " + message;
                }
            }
            catch (Exception ex)
            {
                SynthesisStatusMessage = $"❌ Erreur sauvegarde: {ex.Message}";
            }

            return Task.CompletedTask;
        }

        private async Task ImportDocumentAsync()
        {
            if (_llmService == null) return;

            IsImportingDocument = true;
            SynthesisStatusMessage = "📄 Sélectionner un document...";

            try
            {
                // Dialog pour sélectionner fichier (via code-behind)
                // TODO: Phase D - Implémenter dialog dans ConsultationModeControl.xaml.cs
                // Pour maintenant, placeholder pour tester workflow

                SynthesisStatusMessage = "⏳ Génération synthèse document...";

                // Exemple: créer un document test pour démonstration
                var doc = new ImportedConsultationDocument
                {
                    FileName = "Document_test.pdf",
                    FilePath = "test_path",
                    Category = "Documents",
                    Weight = 0.6
                };

                // Générer synthèse du document via LLM
                var prompt = $@"Génère une synthèse courte (3-4 lignes) de ce document médical:

Fichier: {doc.FileName}
Catégorie: {doc.Category}

Synthèse (format markdown, concis, factuels uniquement):";

                var (success, synthesis, _) = await _llmService.ChatAsync(prompt, new(), maxTokens: 300);

                if (success)
                {
                    doc.DocumentSynthesis = synthesis;
                    ImportedDocuments.Add(doc);
                    SynthesisStatusMessage = $"✅ Document importé: {doc.FileName}";
                }
                else
                {
                    SynthesisStatusMessage = "❌ Erreur génération synthèse doc";
                }
            }
            catch (Exception ex)
            {
                SynthesisStatusMessage = $"❌ Erreur: {ex.Message}";
            }
            finally
            {
                IsImportingDocument = false;
            }
        }

        // Helpers

        private string BuildWeightProposalPrompt()
        {
            var interrogatoireData = BuildInterrogatoireJSON();
            var observationsData = BuildObservationsJSON();
            var documentsSection = "";

            if (ImportedDocuments.Count > 0)
            {
                documentsSection = @"

DOCUMENTS IMPORTÉS (bilans, rapports, etc.):
" + string.Join("\n", ImportedDocuments.Select(d =>
    $@"- {d.FileName} (Catégorie: {d.Category})
  Synthèse: {d.DocumentSynthesis}"));
            }

            return $@"Tu es un clinicien expérimenté en pédopsychiatrie évaluant la qualité des données d'une 1ère consultation.

DONNÉES COLLECTÉES:

INTERROGATOIRE (anamnèse parentale):
{interrogatoireData}

OBSERVATIONS CLINIQUES DIRECTES (examen enfant):
{observationsData}
{documentsSection}

TÂCHE: Évalue la FIABILITÉ et PERTINENCE diagnostique de chaque source de données pour cette synthèse initiale.

Critères d'évaluation:
- COMPLÉTUDE: Toutes les zones ont-elles été explorées?
- COHÉRENCE: Les informations sont-elles cohérentes/compatibles?
- CLARTÉ: Le contenu est-il précis et non ambigu?
- RELEVANCE: Les informations sont-elles pertinentes au motif?
- DÉTAIL CLINIQUE: Suffisant pour une première évaluation?

Échelle de poids (0.1-1.0):
- 0.9-1.0: Très complet, cohérent, cliniquement riche
- 0.7-0.8: Bon coverage, fiable, quelques détails manquants
- 0.5-0.6: Moyen, partiellement complet
- 0.3-0.4: Limité, incomplet
- 0.1-0.2: Très partiel, peu fiable pour synthèse

Réponds en JSON valide:" + (ImportedDocuments.Count > 0 ? @"
{{
  ""weights"": {{
    ""interrogatoire"": 0.X,
    ""observations"": 0.X,
    ""documents"": {{
      // Pour chaque document: ""document_id"": 0.X
    }}
  }},
  ""justification"": ""Courte analyse de la fiabilité de chaque composant""
}}" : @"
{{
  ""weights"": {{
    ""interrogatoire"": 0.X,
    ""observations"": 0.X
  }},
  ""justification"": ""Courte analyse de la fiabilité de chaque composant""
}}");
        }

        private string BuildInterrogatoireJSON()
        {
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                transcription = TranscriptionInput ?? "",
                blocks_count = InterrogatoireBlocks?.Count ?? 0,
                blocks_filled = InterrogatoireBlocks?.Count(b => b.ProgressPct > 0) ?? 0
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        private string BuildObservationsJSON()
        {
            return System.Text.Json.JsonSerializer.Serialize(new
            {
                total_cards = _clinicalObservations.Cards.Count,
                filled_cards = _clinicalObservations.Cards.Count(c => c.SelectedOption != null),
                observations = _clinicalObservations.Cards.Select(c => new
                {
                    branch = c.Branch.ToString(),
                    title = c.Title,
                    selected = c.SelectedOption
                }).ToList()
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        private Dictionary<string, double> ExtractWeightsFromResponse(string response)
        {
            try
            {
                var json = System.Text.Json.JsonDocument.Parse(response);
                var weights = new Dictionary<string, double>();

                if (json.RootElement.TryGetProperty("weights", out var weightsObj))
                {
                    foreach (var prop in weightsObj.EnumerateObject())
                    {
                        if (double.TryParse(prop.Value.ToString(), out var weight))
                        {
                            weights[prop.Name] = Math.Clamp(weight, 0.1, 1.0);
                        }
                    }
                }

                return weights;
            }
            catch
            {
                return new Dictionary<string, double>();
            }
        }

        private string ExtractJustificationFromResponse(string response)
        {
            try
            {
                var json = System.Text.Json.JsonDocument.Parse(response);
                if (json.RootElement.TryGetProperty("justification", out var justif))
                {
                    return justif.GetString() ?? "";
                }
            }
            catch { }

            return "";
        }

        private string BuildInitialSynthesisJSON(InitialSynthesisWeights weights)
        {
            var data = new
            {
                interrogatoire = BuildInterrogatoireJSON(),
                observations = BuildObservationsJSON()
            };

            // Inclure les documents importés s'il y en a
            var documentsList = ImportedDocuments.Select(d => new
            {
                document_id = d.DocumentId,
                file_name = d.FileName,
                category = d.Category,
                synthesis = d.DocumentSynthesis
            }).ToList();

            return System.Text.Json.JsonSerializer.Serialize(new
            {
                consultation_type = "premiere_consultation",
                weights = new
                {
                    interrogatoire = weights.InterrogatoireWeight,
                    observations = weights.ObservationsWeight,
                    documents = ImportedDocuments.ToDictionary(d => d.DocumentId, d => d.Weight),
                    moyenne = weights.AverageWeight
                },
                data = new
                {
                    interrogatoire = BuildInterrogatoireJSON(),
                    observations = BuildObservationsJSON(),
                    documents = documentsList
                }
            }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        }

        private string BuildSynthesisGenerationPrompt(string synthesisJSON)
        {
            return $@"Tu es un clinicien pédopsychiatre expert en synthèse d'évaluation. Voici les données structurées d'une 1ère consultation avec leurs poids de fiabilité:

{synthesisJSON}

IMPORTANT: Les poids indiquent le degré de fiabilité et doivent orienter ton analyse:
- Poids 0.9-1.0: Données très fiables → détailler, valoriser, donner du poids décisionnel
- Poids 0.7-0.8: Données bonnes → inclure normalement
- Poids 0.5-0.6: Données moyennes → inclure avec nuance, noter les limites potentielles
- Poids 0.3-0.4: Données limitées → mentionner sans surinterprétation
- Poids <0.3: Données fragmentaires → noter avec prudence

RÈGLES DE SYNTHÈSE:
✅ INCLURE: Description factuelles, observations cliniques, données anamnestiques
✅ ADOPTER: Ton neutre, clinique, basé sur les données présentes
✅ PONDÉRER: Équilibrer les sources selon leurs poids respectifs

❌ EXCLURE ABSOLUMENT:
- Aucune proposition d'axes diagnostiques
- Aucune recommandation thérapeutique
- Aucun projet de soin
- Aucune interprétation spéculative au-delà des données
- Aucun diagnostic provisoire ou hypothèse

STRUCTURE (Markdown):

# Synthèse Initiale - Première Consultation

## Contexte
[Présentation: âge, motif principal de consultation, date]

## Données Anamnestiques - Parents (Poids: {SynthesisWeights.InterrogatoireWeight:F1})
[Synthèse structurée de ce que les parents rapportent: antécédents, contexte développemental, symptômes rapportés. Si poids bas, noter les limitations de fiabilité de ces données.]

## Observations Cliniques Directes (Poids: {SynthesisWeights.ObservationsWeight:F1})
[Observations précises et factuelles de l'enfant lors de l'examen: comportement, contact, langage, psychomotricité, etc. Si poids élevé, valoriser cette source.]

## Synthèse Descriptive Pondérée
[Fusion intelligente et logique: Ce qu'on observe de cet enfant, intégrant les données parentales ET cliniques directes, pondérées par leur fiabilité respective. Décrire l'enfant tel qu'on le comprend après cette évaluation, sans interpréter au-delà.]";
        }

        #endregion

        #region Commands

        public ICommand SwitchToFocusTravailCommand { get; }
        public ICommand SwitchToConsultationCommand { get; }
        public ICommand SwitchToDossierCommand { get; }

        public ICommand SetMedSilencieuxCommand { get; }
        public ICommand SetMedSuggestionsCommand { get; }
        public ICommand SetMedChecklistCommand { get; }
        public ICommand ToggleMedExpandedCommand { get; }

        public ICommand SelectDossierTabCommand { get; }

        public ICommand SaveNoteCommand { get; }

        public ICommand ExtractInterrogatoireCommand { get; }
        public ICommand SaveInterrogatoireNoteCommand { get; }
        public ICommand BackToSaisieCommand { get; }
        public ICommand StartRecordingCommand { get; }
        public ICommand StopRecordingCommand { get; }

        // V0b : Commande pour confirmer l'âge manuellement
        public ICommand ConfirmAgeCommand { get; }

        // V0b : Commande pour saisir le motif manuellement
        public ICommand SetMotifManuallyCommand { get; }

        // V0c : Commande pour basculer la visibilité d'un bloc
        public ICommand ToggleBlockVisibilityCommand { get; }

        // V0c : Commandes Observations Cliniques
        public ICommand SwitchToInterrogatoireCommand { get; }
        public ICommand SwitchToClinicalCommand { get; }
        public ICommand SelectObservationCommand { get; }
        public ICommand ToggleCardExpandCommand { get; }
        public ICommand TerminateClinicalObservationsCommand { get; }

        // V0d : Commandes Synthèse Initiale
        public ICommand SwitchToSynthesisCommand { get; }
        public ICommand ProposeWeightsCommand { get; }
        public ICommand GenerateSynthesisCommand { get; }
        public ICommand SaveSynthesisCommand { get; }

        // V0d : Commandes Documents Importés
        public ICommand ImportDocumentCommand { get; }
        public ICommand RemoveDocumentCommand { get; }

        #endregion

        #region Constructor

        public ConsultationModeViewModel()
        {
            // V0b : initialiser le suggester avec le resolver
            _blockSuggester = new ContextualBlockSuggester(_blockSetResolver);
            _motifDetector.MotifDetected += OnMotifDetected;
            // Par défaut, ouvrir sur la couverture
            _activeDossierTab = DossierTab.Couverture;

            // Commands Layout
            SwitchToFocusTravailCommand = new RelayCommand(_ => CurrentState = ConsultationViewState.FocusTravail);
            SwitchToConsultationCommand = new RelayCommand(_ => CurrentState = ConsultationViewState.Consultation);
            SwitchToDossierCommand = new RelayCommand(_ => CurrentState = ConsultationViewState.FocusDossier);

            // Commands Med
            SetMedSilencieuxCommand = new RelayCommand(_ => MedMode = MedConsultationMode.Silencieux);
            SetMedSuggestionsCommand = new RelayCommand(_ => MedMode = MedConsultationMode.Suggestions);
            SetMedChecklistCommand = new RelayCommand(_ => MedMode = MedConsultationMode.Checklist);
            ToggleMedExpandedCommand = new RelayCommand(_ => IsMedExpanded = !IsMedExpanded);

            // Command Dossier Tab
            SelectDossierTabCommand = new RelayCommand(param =>
            {
                if (param is DossierTab tab)
                    ActiveDossierTab = tab;
                else if (param is string tabName && Enum.TryParse<DossierTab>(tabName, out var parsedTab))
                    ActiveDossierTab = parsedTab;
            });

            // Command Save Note
            SaveNoteCommand = new RelayCommand(async _ => await SaveNoteAsync(), _ => HasPatient && HasNoteContent);

            // Commands interrogatoire
            ExtractInterrogatoireCommand   = new RelayCommand(async _ => await ExtractInterrogatoireAsync(), _ => CanExtract);
            SaveInterrogatoireNoteCommand  = new RelayCommand(async _ => await SaveInterrogatoireNoteAsync(), _ => IsInFinalNoteMode && HasPatient && HasNoteContent && !_noteSaved);
            BackToSaisieCommand = new RelayCommand(_ =>
            {
                foreach (var b in InterrogatoireBlocks) b.Reset();
                NoteContent = "";
                _noteSaved = false;
                InterrogatoireState = InterrogatoireState.Saisie;
            });

            StartRecordingCommand = new RelayCommand(
                async _ =>
                {
                    if (_whisperService == null) return;
                    _whisperService.Mode                 = UseBatchMode ? RecordingMode.Batch : RecordingMode.Streaming;
                    _whisperService.BatchDurationSeconds = BatchDurationSeconds;
                    var modelManager = new WhisperModelManager();
                    await _whisperService.StartAsync(modelManager);
                    IsRecording = true;
                },
                _ => IsInSaisieMode && !IsRecording && _whisperService != null);

            StopRecordingCommand = new RelayCommand(
                async _ =>
                {
                    if (_whisperService == null) return;
                    await _whisperService.StopAsync();
                    IsRecording = false;
                },
                _ => IsRecording);

            // V0b : Commande confirmation âge
            ConfirmAgeCommand = new RelayCommand(param =>
            {
                if (param is int age)
                    ConfirmedAge = age;
                else if (param is string ageStr && int.TryParse(ageStr, out var parsedAge))
                    ConfirmedAge = parsedAge;
            }, _ => IsInterrogatoireMode);

            // V0b : Commande motif manuel
            SetMotifManuallyCommand = new RelayCommand(param =>
            {
                if (param is string motif && !string.IsNullOrWhiteSpace(motif))
                    _motifDetector.SetMotifManually(motif);
            }, _ => IsInterrogatoireMode && !HasDetectedMotif);

            // V0c : Commande basculer visibilité bloc
            ToggleBlockVisibilityCommand = new RelayCommand(param =>
            {
                if (param is ConsultationBlockViewModel vm)
                {
                    vm.ToggleHidden();
                    UpdateBlockCollections();
                }
            }, _ => IsInterrogatoireMode);

            // V0c : Commandes Observations Cliniques
            SwitchToInterrogatoireCommand = new RelayCommand(_ =>
            {
                IsInClinicalMode = false;
                IsSynthesisMode = false;
            }, _ => IsInterrogatoireMode);

            SwitchToClinicalCommand = new RelayCommand(_ =>
            {
                if (_clinicalObservations.Cards.Count == 0)
                    InitializeClinicalObservations();
                IsSynthesisMode = false;
                IsInClinicalMode = true;
            }, _ => IsInterrogatoireMode);

            SelectObservationCommand = new RelayCommand(param =>
            {
                // Format attendu du paramètre depuis le XAML : object[] avec [card, option]
                // Ou directement un tuple ValueTuple<ClinicalObservationCard, string>
                if (param is object[] arr && arr.Length >= 2)
                {
                    if (arr[0] is ClinicalObservationCard card && arr[1] is string option)
                        SelectObservationOption(card, option);
                }
                else if (param is ValueTuple<ClinicalObservationCard, string> tuple)
                {
                    SelectObservationOption(tuple.Item1, tuple.Item2);
                }
            }, _ => IsInClinicalMode);

            ToggleCardExpandCommand = new RelayCommand(param =>
            {
                if (param is ClinicalObservationCard card)
                    ToggleCardExpand(card);
            }, _ => IsInClinicalMode);

            TerminateClinicalObservationsCommand = new RelayCommand(async _ =>
            {
                await TerminateClinicalObservationsAsync();
            }, _ => IsInClinicalMode);

            // Commands Synthèse Initiale V0d
            // TODO: Phase D — retourner à: IsInterrogatoireMode && IsStructureFrozen
            // Pour tests: juste IsInterrogatoireMode
            SwitchToSynthesisCommand = new RelayCommand(_ =>
            {
                SwitchToSynthesis();
            }, _ => IsInterrogatoireMode);

            ProposeWeightsCommand = new RelayCommand(async _ =>
            {
                await ProposeWeightsAsync();
            }, _ => IsSynthesisMode && !AreWeightsLoading);

            GenerateSynthesisCommand = new RelayCommand(async _ =>
            {
                await GenerateSynthesisAsync();
            }, _ => IsSynthesisMode && !IsGeneratingSynthesis);

            SaveSynthesisCommand = new RelayCommand(async _ =>
            {
                await SaveSynthesisAsync();
            }, _ => IsSynthesisMode && !string.IsNullOrEmpty(SynthesisContent));

            // V0d : Commands Documents Importés
            ImportDocumentCommand = new RelayCommand(async _ =>
            {
                await ImportDocumentAsync();
            }, _ => IsSynthesisMode && !IsImportingDocument);

            RemoveDocumentCommand = new RelayCommand(param =>
            {
                if (param is ImportedConsultationDocument doc)
                {
                    ImportedDocuments.Remove(doc);
                }
            });

            // Commands pagination dossier
            InitPaginationCommands();

            // Charger des données placeholder pour tester l'UI
            LoadPlaceholderData();
        }

        #endregion

        #region Methods

        public void SetDossierDataService(DossierDataService service)
        {
            _dossierDataService = service;
        }

        private DossierDataService _dossierDataService = new DossierDataService(new PathService());

        public ObservableCollection<ConsultationNoteViewModel> ConsultationNotes { get; } = new();

        public bool HasNoConsultationNotes => ConsultationNotes.Count == 0;
        public bool HasConsultationNotes   => ConsultationNotes.Count > 0;

        private async Task RefreshConsultationNotesAsync()
        {
            if (CurrentPatient == null) return;
            var patientSnapshot = CurrentPatient;
            var pages = await _dossierDataService.LoadConsultationsAsync(patientSnapshot.NomComplet);
            if (CurrentPatient != patientSnapshot) return; // patient changé pendant l'attente IO
            ConsultationNotes.Clear();
            foreach (var p in pages)
            {
                var noteVm = new ConsultationNoteViewModel(p.Title, p.FilePath ?? "", p.Content ?? "");
                noteVm.DeleteRequested += vm =>
                {
                    ConsultationNotes.Remove(vm);
                    OnPropertyChanged(nameof(HasNoConsultationNotes));
                    OnPropertyChanged(nameof(HasConsultationNotes));
                    _singlePageIndex = Math.Max(0, Math.Min(_singlePageIndex, ConsultationNotes.Count - 1));
                    _spreadIndex     = Math.Max(0, Math.Min(_spreadIndex,     SpreadCount - 1));
                    NotifyPageChange();
                    NotifySpreadChange();
                };
                ConsultationNotes.Add(noteVm);
            }
            OnPropertyChanged(nameof(HasNoConsultationNotes));
            OnPropertyChanged(nameof(HasConsultationNotes));
            ResetPagination();
        }

        #endregion

        #region Pagination CONSULT

        private int _singlePageIndex = 0;
        private int _spreadIndex     = 0;

        private int SpreadCount =>
            ConsultationNotes.Count == 0 ? 0 : (int)Math.Ceiling(ConsultationNotes.Count / 2.0);

        public ConsultationNoteViewModel? SinglePageNote =>
            ConsultationNotes.Count > 0 ? ConsultationNotes[Math.Clamp(_singlePageIndex, 0, ConsultationNotes.Count - 1)] : null;

        public ConsultationNoteViewModel? LeftPageNote  =>
            ConsultationNotes.ElementAtOrDefault(_spreadIndex * 2);

        public ConsultationNoteViewModel? RightPageNote =>
            ConsultationNotes.ElementAtOrDefault(_spreadIndex * 2 + 1);

        public bool   HasPrevSingle  => _singlePageIndex > 0;
        public bool   HasNextSingle  => _singlePageIndex < ConsultationNotes.Count - 1;
        public string SinglePageText => ConsultationNotes.Count == 0 ? "–" : $"{_singlePageIndex + 1}/{ConsultationNotes.Count}";

        public bool   HasPrevSpread  => _spreadIndex > 0;
        public bool   HasNextSpread  => _spreadIndex < SpreadCount - 1;
        public string SpreadText
        {
            get
            {
                if (ConsultationNotes.Count == 0) return "–";
                int l = _spreadIndex * 2 + 1;
                int r = Math.Min(l + 1, ConsultationNotes.Count);
                return r == l ? $"{l}/{ConsultationNotes.Count}" : $"{l}–{r}/{ConsultationNotes.Count}";
            }
        }

        public ICommand NextSinglePageCommand  { get; private set; } = null!;
        public ICommand PrevSinglePageCommand  { get; private set; } = null!;
        public ICommand NextSpreadCommand      { get; private set; } = null!;
        public ICommand PrevSpreadCommand      { get; private set; } = null!;
        public ICommand IncreaseFontSizeCommand { get; private set; } = null!;
        public ICommand DecreaseFontSizeCommand { get; private set; } = null!;

        private double _noteFontSize = 12;
        public double NoteFontSize
        {
            get => _noteFontSize;
            set => SetProperty(ref _noteFontSize, Math.Clamp(value, 10, 28));
        }

        private void InitPaginationCommands()
        {
            NextSinglePageCommand   = new RelayCommand(_ => { _singlePageIndex++; NotifyPageChange(); },   _ => HasNextSingle);
            PrevSinglePageCommand   = new RelayCommand(_ => { _singlePageIndex--; NotifyPageChange(); },   _ => HasPrevSingle);
            NextSpreadCommand       = new RelayCommand(_ => { _spreadIndex++;     NotifySpreadChange(); }, _ => HasNextSpread);
            PrevSpreadCommand       = new RelayCommand(_ => { _spreadIndex--;     NotifySpreadChange(); }, _ => HasPrevSpread);
            IncreaseFontSizeCommand = new RelayCommand(_ => NoteFontSize += 2, _ => NoteFontSize < 28);
            DecreaseFontSizeCommand = new RelayCommand(_ => NoteFontSize -= 2, _ => NoteFontSize > 10);
        }

        private void NotifyPageChange()
        {
            OnPropertyChanged(nameof(SinglePageNote));
            OnPropertyChanged(nameof(SinglePageText));
            OnPropertyChanged(nameof(HasPrevSingle));
            OnPropertyChanged(nameof(HasNextSingle));
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                System.Windows.Input.CommandManager.InvalidateRequerySuggested);
        }

        public bool HasRightPageNote => RightPageNote != null;

        private void NotifySpreadChange()
        {
            OnPropertyChanged(nameof(LeftPageNote));
            OnPropertyChanged(nameof(RightPageNote));
            OnPropertyChanged(nameof(HasRightPageNote));
            OnPropertyChanged(nameof(SpreadText));
            OnPropertyChanged(nameof(HasPrevSpread));
            OnPropertyChanged(nameof(HasNextSpread));
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                System.Windows.Input.CommandManager.InvalidateRequerySuggested);
        }

        private void ResetPagination()
        {
            _singlePageIndex = 0;
            _spreadIndex     = 0;
            NotifyPageChange();
            NotifySpreadChange();
        }

        #endregion

        #region Methods

        /// <summary>
        /// Charge le patient pour la consultation
        /// </summary>
        public void LoadPatient(PatientIndexEntry patient)
        {
            CurrentPatient = patient;
            ConsultationDate = DateTime.Now;

            // Réinitialiser l'interrogatoire (blocs, état, textes)
            ConsultationType = ConsultationType.Normal;   // remet les flags IsInterrogatoireMode etc.
            InterrogatoireBlocks.Clear();
            _interrogatoireState = InterrogatoireState.Saisie;
            _noteSaved = false;
            TranscriptionInput = "";
            NoteContent = "";
            ExtractionStatus = "";

            // V0b : réinitialiser l'état adaptatif
            _confirmedAge = null;
            OnPropertyChanged(nameof(ConfirmedAge));
            OnPropertyChanged(nameof(HasConfirmedAge));
            _motifDetector.Reset();
            DetectedMotif = "";
            _isAgeConfirmed = false;
            OnPropertyChanged(nameof(HasConfirmedAge));
            BlockSuggestions.Clear();
            OnPropertyChanged(nameof(HasBlockSuggestions));
            IsStructureFrozen = false;

            // V0b : pré-calculer l'âge si Dob disponible (sera confirmé en consultation)
            if (!string.IsNullOrEmpty(patient.Dob) &&
                DateTime.TryParse(patient.Dob, out var dob))
            {
                var age = DateTime.Now.Year - dob.Year;
                if (DateTime.Now.DayOfYear < dob.DayOfYear) age--;
                _confirmedAge = age; // Pré-rempli, sera confirmé
                OnPropertyChanged(nameof(ConfirmedAge));
                OnPropertyChanged(nameof(HasConfirmedAge));
            }

            // Vider immédiatement pour ne pas afficher les notes du patient précédent
            ConsultationNotes.Clear();
            OnPropertyChanged(nameof(HasNoConsultationNotes));
            OnPropertyChanged(nameof(HasConsultationNotes));
            ResetPagination();

            ActiveDossierTab = DossierTab.Couverture;
            LoadSuggestionsForPatient(patient);
            _ = RefreshConsultationNotesAsync();
        }

        /// <summary>
        /// Charge les suggestions de Med pour le patient
        /// </summary>
        private void LoadSuggestionsForPatient(PatientIndexEntry patient)
        {
            Suggestions.Clear();

            // Placeholder - sera connecté au service Med plus tard
            Suggestions.Add(new MedSuggestion
            {
                Icon = "📝",
                Title = "Points à évoquer",
                Content = "Suivi scolaire, RDV bilan prévu",
                Category = "PointAEvoquer"
            });
        }

        /// <summary>
        /// Sauvegarde la note de consultation
        /// </summary>
        private System.Threading.Tasks.Task SaveNoteAsync()
        {
            if (CurrentPatient == null || string.IsNullOrWhiteSpace(NoteContent))
                return System.Threading.Tasks.Task.CompletedTask;

            // TODO: Connecter au service de stockage
            System.Diagnostics.Debug.WriteLine($"[ConsultationMode] Sauvegarde note pour {PatientDisplayName}");
            return System.Threading.Tasks.Task.CompletedTask;
        }

        /// <summary>
        /// Données placeholder pour tester l'UI
        /// </summary>
        private void LoadPlaceholderData()
        {
            // Suggestions de test
            Suggestions.Add(new MedSuggestion
            {
                Icon = "💊",
                Title = "Interactions détectées",
                Content = "Aucune interaction connue",
                Category = "Interaction"
            });

            Suggestions.Add(new MedSuggestion
            {
                Icon = "📝",
                Title = "Points à évoquer",
                Content = "Suivi scolaire, sommeil",
                Category = "PointAEvoquer"
            });

            Suggestions.Add(new MedSuggestion
            {
                Icon = "🎯",
                Title = "Rappel",
                Content = "Dernier RDV: fatigue mentionnée",
                Category = "Rappel"
            });

            // Checklist de test
            ChecklistItems.Add(new ChecklistItem { Text = "Évolution depuis dernier RDV", Source = "auto" });
            ChecklistItems.Add(new ChecklistItem { Text = "Tolérance traitement", Source = "auto" });
            ChecklistItems.Add(new ChecklistItem { Text = "Situation scolaire", Source = "auto" });
            ChecklistItems.Add(new ChecklistItem { Text = "Qualité du sommeil", Source = "auto" });
        }

        #endregion
    }

    public class QualityIssueViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly ConsultationModeViewModel _parent;
        public QualityIssue Issue { get; }

        public string Original   => Issue.Original;
        public string Suggestion => Issue.Suggestion;
        public string Reason     => Issue.Reason;
        public string BlockTitle => Issue.BlockTitle;

        public string TypeIcon => Issue.Type switch
        {
            QualityIssueType.Medication => "💊",
            QualityIssueType.Coherence  => "⚠",
            _                           => "❓"
        };

        public ICommand AcceptCommand  { get; }
        public ICommand DismissCommand { get; }

        public QualityIssueViewModel(QualityIssue issue, ConsultationModeViewModel parent)
        {
            Issue   = issue;
            _parent = parent;

            AcceptCommand = new RelayCommand(_ =>
            {
                _parent.ApplyQualityIssue(Issue);
                _parent.DismissQualityIssue(this);
            });

            DismissCommand = new RelayCommand(_ => _parent.DismissQualityIssue(this));
        }
    }
}
