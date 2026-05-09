using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
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

        #region Interrogatoire V0a

        private ILLMService? _llmService;
        private StorageService? _storageService;
        private readonly InterrogatoireExtractorService _extractor = new();

        public void InjectServices(ILLMService llmService, StorageService storageService)
        {
            _llmService = llmService;
            _storageService = storageService;
        }

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

        public bool IsInSaisieMode    => IsInterrogatoireMode && InterrogatoireState == InterrogatoireState.Saisie;
        public bool IsInExtractionMode => IsInterrogatoireMode && InterrogatoireState == InterrogatoireState.Extraction;
        public bool IsInFinalNoteMode  => IsInterrogatoireMode && InterrogatoireState == InterrogatoireState.FinalNote;

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

        // Blocs interrogatoire
        public ObservableCollection<ConsultationBlockViewModel> InterrogatoireBlocks { get; } = new();

        private void InitInterrogatoireBlocks()
        {
            InterrogatoireBlocks.Clear();
            var models = BlockDefinitionLoader.LoadAsBlocks();
            foreach (var m in models)
                InterrogatoireBlocks.Add(ConsultationBlockViewModel.FromModel(m));
            InterrogatoireState = InterrogatoireState.Saisie;
            TranscriptionInput = "";
            NoteContent = "";
            _noteSaved = false;
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
        }

        private async Task SaveInterrogatoireNoteAsync()
        {
            if (_storageService == null || CurrentPatient == null || string.IsNullOrWhiteSpace(NoteContent))
                return;

            var (ok, _, err) = _storageService.SaveStructuredNote(CurrentPatient.NomComplet, NoteContent);
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

        #endregion

        #region Constructor

        public ConsultationModeViewModel()
        {
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
            BackToSaisieCommand            = new RelayCommand(_ =>
            {
                foreach (var b in InterrogatoireBlocks) b.Reset();
                NoteContent = "";
                _noteSaved = false;
                InterrogatoireState = InterrogatoireState.Saisie;
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

        public ICommand NextSinglePageCommand { get; private set; } = null!;
        public ICommand PrevSinglePageCommand { get; private set; } = null!;
        public ICommand NextSpreadCommand     { get; private set; } = null!;
        public ICommand PrevSpreadCommand     { get; private set; } = null!;

        private void InitPaginationCommands()
        {
            NextSinglePageCommand = new RelayCommand(_ => { _singlePageIndex++; NotifyPageChange(); },   _ => HasNextSingle);
            PrevSinglePageCommand = new RelayCommand(_ => { _singlePageIndex--; NotifyPageChange(); },   _ => HasPrevSingle);
            NextSpreadCommand     = new RelayCommand(_ => { _spreadIndex++;     NotifySpreadChange(); }, _ => HasNextSpread);
            PrevSpreadCommand     = new RelayCommand(_ => { _spreadIndex--;     NotifySpreadChange(); }, _ => HasPrevSpread);
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

        private void NotifySpreadChange()
        {
            OnPropertyChanged(nameof(LeftPageNote));
            OnPropertyChanged(nameof(RightPageNote));
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
            NoteContent = "";

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
}
