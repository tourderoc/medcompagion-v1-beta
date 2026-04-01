using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Models;
using MedCompanion.Services;

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

            // Charger des données placeholder pour tester l'UI
            LoadPlaceholderData();
        }

        #endregion

        #region Methods

        public void SetDossierDataService(DossierDataService service)
        {
            _dossierDataService = service;
        }

        private DossierDataService? _dossierDataService;

        /// <summary>
        /// Charge le patient pour la consultation
        /// </summary>
        public void LoadPatient(PatientIndexEntry patient)
        {
            CurrentPatient = patient;
            ConsultationDate = DateTime.Now;
            NoteContent = "";

            // Réinitialiser sur l'onglet Couverture
            ActiveDossierTab = DossierTab.Couverture;

            // Charger les suggestions contextuelles
            LoadSuggestionsForPatient(patient);
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
