using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Models;
using MedCompanion.Models.Evaluations;
using MedCompanion.Services.Consultation;
using MedCompanion.Services.Evaluations;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// VM du panneau "Phase d'évaluation" affiché dans la zone Actions du Mode Consultation.
    /// Gère les 3 états : pas commencée / en cours de séance / suspendue.
    /// V0 = Étape 1 (Préparation clinique).
    /// </summary>
    public class EvaluationPhaseViewModel : INotifyPropertyChanged
    {
        private readonly EvaluationPhaseService _phaseService;
        private readonly PreparationSuggesterService? _suggester;
        private readonly AxesSuggesterService? _axesSuggester;
        private readonly AxisExtractorService? _axisExtractor;
        private readonly BilanFinalSuggesterService? _bilanFinalSuggester;
        private readonly FeuilleLectureService? _feuilleLecture;
        private readonly BrancheEnvironnementLectureService? _brancheLecture;
        private readonly WhisperStreamingService? _whisper;
        private PatientIndexEntry? _patient;

        // Dicte d'évaluation (Étape A) : tampon de transcription + handler éphémère
        private readonly System.Text.StringBuilder _dicteBuffer = new();
        private Action<string>? _whisperTextHandler;
        private Action<float>?  _whisperLevelHandler;

        public EvaluationPhaseViewModel(EvaluationPhaseService phaseService,
                                        PreparationSuggesterService? suggester,
                                        AxesSuggesterService? axesSuggester = null,
                                        AxisExtractorService?  axisExtractor = null,
                                        WhisperStreamingService? whisper = null,
                                        BilanFinalSuggesterService? bilanFinalSuggester = null,
                                        FeuilleLectureService? feuilleLecture = null,
                                        BrancheEnvironnementLectureService? brancheLecture = null)
        {
            _phaseService      = phaseService;
            _suggester         = suggester;
            _axesSuggester     = axesSuggester;
            _axisExtractor     = axisExtractor;
            _bilanFinalSuggester = bilanFinalSuggester;
            _feuilleLecture    = feuilleLecture;
            _brancheLecture    = brancheLecture;
            _whisper           = whisper;

            StartCommand                  = new RelayCommand(_ => StartNew(),                _ => CanStart);
            ResumeCommand                 = new RelayCommand(_ => Resume(),                  _ => CanResume);
            SuggestPreparationCommand     = new RelayCommand(async _ => await SuggestPreparationAsync(), _ => IsWorkingPreparation && !IsBusy);
            ValidatePreparationCommand    = new RelayCommand(_ => ValidatePreparation(),    _ => CanValidatePreparation);
            TerminateSessionCommand       = new RelayCommand(_ => TerminateSession(),       _ => IsWorkingPreparation || IsWorkingEvaluation || IsWorkingBilanFinal || IsWorkingCartographieEnfant || IsWorkingCartographieEnvironnement);
            CancelEvaluationCommand       = new RelayCommand(_ => CancelEvaluation(),       _ => IsWorkingPreparation || IsWorkingEvaluation || IsWorkingBilanFinal || IsWorkingCartographieEnfant || IsWorkingCartographieEnvironnement);

            AddItemCommand    = new RelayCommand(param => AddItem(param as string));
            RemoveItemCommand = new RelayCommand(param => RemoveItem(param));

            // Étape 2
            SuggestAxesCommand          = new RelayCommand(async _ => await SuggestAxesAsync(),       _ => IsWorkingEvaluation && !IsBusy && _axesSuggester != null);
            ValidateEvaluationCommand   = new RelayCommand(_ => ValidateEvaluationCiblee(),         _ => CanValidateEvaluationCiblee);
            BackToPreparationCommand    = new RelayCommand(_ => BackToPreparation(),                _ => IsWorkingEvaluation);
            SetAxisStateCommand         = new RelayCommand(param => SetAxisState(param));

            // Dicte évaluation (Étape A)
            StartDicteCommand        = new RelayCommand(async _ => await StartDicteAsync(),       _ => CanStartDicte);
            StopDicteCommand         = new RelayCommand(async _ => await StopDicteAsync(),        _ => IsDicteActive);
            AcceptMedTextCommand     = new RelayCommand(param => AcceptMedText(param as EvaluationAxis));
            RejectMedTextCommand     = new RelayCommand(param => RejectMedText(param as EvaluationAxis));
            OpenRawTranscriptCommand = new RelayCommand(_ => OpenRawTranscript(),                _ => !string.IsNullOrEmpty(LastRawTranscriptPath));

            // Étape 3 — Synthèse diagnostique
            SuggestBilanFinalCommand        = new RelayCommand(async _ => await SuggestBilanFinalAsync(), _ => IsWorkingBilanFinal && !IsBusy && _bilanFinalSuggester != null);
            ValidateBilanFinalCommand       = new RelayCommand(_ => ValidateBilanFinal(),                _ => CanValidateBilanFinal);
            BackToCartographieEnvironnementCommand = new RelayCommand(_ => BackToCartographieEnvironnementFromBilan(), _ => IsWorkingBilanFinal);
            AddBilanFinalItemCommand        = new RelayCommand(param => AddBilanFinalItem(param as string));
            RemoveBilanFinalItemCommand     = new RelayCommand(param => RemoveBilanFinalItem(param));
            AddDiagnosticEcarteCommand    = new RelayCommand(_ => AddDiagnosticEcarte());
            RemoveDiagnosticEcarteCommand = new RelayCommand(param => RemoveDiagnosticEcarte(param as DiagnosticEcarte));
            SetCertitudeCommand           = new RelayCommand(param => SetCertitude(param));

            // Lecture seule — navigation entre étapes sans modifier la phase ni sauvegarder
            ViewStepCommand           = new RelayCommand(param => ViewStep(param as string), _ => IsReadOnly);
            CloseReadOnlyCommand      = new RelayCommand(_ => ReturnToActiveContext(),       _ => IsReadOnly);

            // Étape 4 — Cartographie de l'enfant
            ValidateCartographieEnfantCommand = new RelayCommand(_ => ValidateCartographieEnfant(), _ => CanValidateCartographieEnfant);
            BackToEvaluationCibleeCommand     = new RelayCommand(_ => BackToEvaluationCibleeFromCarto(), _ => IsWorkingCartographieEnfant);

            // Étape 5 — Cartographie de l'environnement
            ValidateCartographieEnvironnementCommand = new RelayCommand(_ => ValidateCartographieEnvironnement(), _ => CanValidateCartographieEnvironnement);
            BackToCartographieEnfantCommand          = new RelayCommand(_ => BackToCartographieEnfant(),          _ => IsWorkingCartographieEnvironnement);
            LireBrancheCommand                       = new RelayCommand(async _ => await LireBrancheAsync(),       _ => CanLireBranche);
            EffacerLectureBrancheCommand             = new RelayCommand(_ => EffacerLectureBranche(),              _ => HasLectureBranche && !IsLectureBrancheEnCours);
        }

        /// <summary>
        /// Notifié à chaque changement notable d'état d'une évaluation (création, clôture).
        /// L'orchestrateur (ConsultationModeViewModel) s'y abonne pour rafraîchir la frise et
        /// les blocs de synthèse du dossier bleu.
        /// </summary>
        public event Action? PhaseStateChanged;

        /// <summary>
        /// Notifié quand le médecin demande à fermer la vue lecture seule (bouton « ✕ Fermer la vue »).
        /// L'orchestrateur (ConsultationModeViewModel) s'y abonne pour basculer IsEvaluationPhaseMode
        /// à false, sinon on reste bloqué sur l'écran "Aucune évaluation en cours — Commencer".
        /// </summary>
        public event Action? ReadOnlyViewClosed;

        // ── Patient courant ─────────────────────────────────────────────────

        /// <summary>
        /// Définit le patient courant et recharge l'état de la phase (active / suspendue / aucune).
        /// </summary>
        public void SetCurrentPatient(PatientIndexEntry? patient)
        {
            _patient = patient;
            ReloadState();
        }

        private void ReloadState()
        {
            Phase = (_patient != null && !string.IsNullOrEmpty(_patient.DirectoryPath))
                ? _phaseService.LoadActive(_patient.DirectoryPath)
                : null;

            // Hook auto-save sur les axes chargés du disque (uniquement après load — sinon les
            // axes créés par ReplaceAxes auront déjà eu leur handler ajouté).
            if (Phase != null)
            {
                HookAxisHandlers(Phase.EvaluationCiblee.AxesPrincipaux);
                HookAxisHandlers(Phase.EvaluationCiblee.AxesDifferentiels);
                HookAxisHandlers(Phase.EvaluationCiblee.AxesSystemiques);
                HookBilanFinalHandlers(Phase.BilanFinal);
            }

            RebuildCartographieWrappers();
            IsWorkingPreparation               = false;
            IsWorkingEvaluation                = false;
            IsWorkingBilanFinal                  = false;
            IsWorkingCartographieEnfant        = false;
            IsWorkingCartographieEnvironnement = false;
            StatusMessage = "";
            NotifyAllStates();
        }

        private void HookBilanFinalHandlers(BilanFinal s)
        {
            s.PropertyChanged -= OnBilanFinalChanged;
            s.PropertyChanged += OnBilanFinalChanged;
            HookEditableList(s.DiagnosticsRetenus);
            HookEditableList(s.ElementsEnFaveur);
            foreach (var e in s.DiagnosticsEcartes)
            {
                e.PropertyChanged -= OnEcarteChanged;
                e.PropertyChanged += OnEcarteChanged;
            }
        }

        private void HookEditableList(ObservableCollection<EditableString> list)
        {
            foreach (var it in list)
            {
                it.PropertyChanged -= OnEditableStringChanged;
                it.PropertyChanged += OnEditableStringChanged;
            }
        }

        private void OnEditableStringChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (Phase == null) return;
            if (e.PropertyName == nameof(EditableString.Value))
            { try { _phaseService.Save(Phase); } catch { /* meilleur effort */ } }
        }

        private void OnBilanFinalChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (Phase == null) return;
            if (e.PropertyName == nameof(BilanFinal.Certitude))
            { try { _phaseService.Save(Phase); } catch { /* meilleur effort */ } }
        }

        private void OnEcarteChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (Phase == null) return;
            if (e.PropertyName == nameof(DiagnosticEcarte.Label) ||
                e.PropertyName == nameof(DiagnosticEcarte.Motif))
            { try { _phaseService.Save(Phase); } catch { /* meilleur effort */ } }
        }

        private void HookAxisHandlers(ObservableCollection<EvaluationAxis> axes)
        {
            foreach (var ax in axes)
            {
                ax.PropertyChanged -= OnAxisChanged;   // évite double abonnement
                ax.PropertyChanged += OnAxisChanged;
                foreach (var obs in ax.ObservationsProposees)
                {
                    obs.PropertyChanged -= OnObservationChanged;
                    obs.PropertyChanged += OnObservationChanged;
                }
            }
        }

        // ── État courant ────────────────────────────────────────────────────

        private EvaluationPhase? _phase;
        public EvaluationPhase? Phase
        {
            get => _phase;
            private set { _phase = value; OnPropertyChanged(); }
        }

        private bool _isWorkingPreparation;
        /// <summary>
        /// True quand le médecin est en train de travailler l'Étape 1 dans la séance courante.
        /// </summary>
        public bool IsWorkingPreparation
        {
            get => _isWorkingPreparation;
            set { if (_isWorkingPreparation != value) { _isWorkingPreparation = value; OnPropertyChanged(); NotifyAllStates(); } }
        }

        private bool _isWorkingEvaluation;
        /// <summary>
        /// True quand le médecin est en train de travailler l'Étape 2 dans la séance courante.
        /// </summary>
        public bool IsWorkingEvaluation
        {
            get => _isWorkingEvaluation;
            set { if (_isWorkingEvaluation != value) { _isWorkingEvaluation = value; OnPropertyChanged(); NotifyAllStates(); } }
        }

        private bool _isWorkingBilanFinal;
        /// <summary>
        /// True quand le médecin est en train de travailler l'Étape 3 (Synthèse) dans la séance courante.
        /// </summary>
        public bool IsWorkingBilanFinal
        {
            get => _isWorkingBilanFinal;
            set { if (_isWorkingBilanFinal != value) { _isWorkingBilanFinal = value; OnPropertyChanged(); NotifyAllStates(); } }
        }

        private bool _isWorkingCartographieEnfant;
        /// <summary>
        /// True quand le médecin est en train de remplir l'Étape 4 (Cartographie de l'enfant) dans la séance courante.
        /// </summary>
        public bool IsWorkingCartographieEnfant
        {
            get => _isWorkingCartographieEnfant;
            set { if (_isWorkingCartographieEnfant != value) { _isWorkingCartographieEnfant = value; OnPropertyChanged(); NotifyAllStates(); } }
        }

        private bool _isWorkingCartographieEnvironnement;
        /// <summary>
        /// True quand le médecin est en train de remplir l'Étape 5 (Cartographie de l'environnement) dans la séance courante.
        /// </summary>
        public bool IsWorkingCartographieEnvironnement
        {
            get => _isWorkingCartographieEnvironnement;
            set { if (_isWorkingCartographieEnvironnement != value) { _isWorkingCartographieEnvironnement = value; OnPropertyChanged(); NotifyAllStates(); } }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            set { if (_isBusy != value) { _isBusy = value; OnPropertyChanged(); } }
        }

        private string _statusMessage = "";
        public string StatusMessage
        {
            get => _statusMessage;
            set { if (_statusMessage != value) { _statusMessage = value; OnPropertyChanged(); } }
        }

        private bool _isReadOnly;
        /// <summary>
        /// True quand l'évaluation est affichée en lecture seule (consultation d'une évaluation
        /// clôturée du passé). Les actions de modification doivent être désactivées par la View.
        /// </summary>
        public bool IsReadOnly
        {
            get => _isReadOnly;
            private set { if (_isReadOnly != value) { _isReadOnly = value; OnPropertyChanged(); NotifyAllStates(); } }
        }

        // ── États dérivés pour la View (3 cas + lecture seule) ──────────────

        public bool CanStart  => !IsReadOnly && _patient != null && Phase == null && !IsWorkingPreparation && !IsWorkingEvaluation && !IsWorkingBilanFinal && !IsWorkingCartographieEnfant && !IsWorkingCartographieEnvironnement;
        public bool CanResume => !IsReadOnly && _patient != null && Phase != null && Phase.IsActive && !IsWorkingPreparation && !IsWorkingEvaluation && !IsWorkingBilanFinal && !IsWorkingCartographieEnfant && !IsWorkingCartographieEnvironnement;

        /// <summary>
        /// True si l'âge du patient est dans la fourchette 3-11 ans où la Cartographie de
        /// l'enfant est applicable. Si false, l'étape 4 est sautée et l'étape 3 mène directement
        /// à la clôture.
        /// </summary>
        public bool IsCartographieDisponible => CartographieScoringService.IsApplicable(_patient?.Age);

        // ── Étape 4 — Cartographie de l'enfant ──────────────────────────────

        public CartographieSegmentViewModel? AttachementVM { get; private set; }
        public CartographieSegmentViewModel? LangageVM    { get; private set; }
        public CartographieSegmentViewModel? EmotionsVM   { get; private set; }
        public CartographieSegmentViewModel? ImaginaireVM { get; private set; }
        public CartographieSegmentViewModel? PenseeVM     { get; private set; }

        public TemperamentProfile?       Temperament     => Phase?.CartographieEnfant.Temperament;
        public PsychomotriciteProfile?   Psychomotricite => Phase?.CartographieEnfant.Psychomotricite;
        public AttentionProfile?         Attention       => Phase?.CartographieEnfant.Attention;

        // ── Étape 5 — Cartographie de l'environnement ───────────────────────

        public FeuilleEnvironnementViewModel? FamilleVM           { get; private set; }
        public FeuilleEnvironnementViewModel? EcolePairsVM        { get; private set; }
        public FeuilleEnvironnementViewModel? EcransMediasVM      { get; private set; }
        public FeuilleEnvironnementViewModel? ValeursSocietalesVM { get; private set; }
        public FeuilleEnvironnementViewModel? CadreEducatifVM     { get; private set; }

        /// <summary>Couleur synthèse globale de l'étape 5 (pire parmi les feuilles évaluées).</summary>
        public NiveauFeuille EnvironnementSynthese
            => Phase != null
                ? EnvironnementScoringService.CalculerGlobal(Phase.CartographieEnvironnement)
                : NiveauFeuille.VertFonce;

        /// <summary>True si au moins une feuille a au moins un item coché.</summary>
        public bool HasAnyEnvironnementScore
            => (FamilleVM?.HasAnyScore ?? false)
            || (EcolePairsVM?.HasAnyScore ?? false)
            || (EcransMediasVM?.HasAnyScore ?? false)
            || (ValeursSocietalesVM?.HasAnyScore ?? false)
            || (CadreEducatifVM?.HasAnyScore ?? false);

        public string EnvironnementSyntheseLabel
            => HasAnyEnvironnementScore
                ? CartographieEnvironnementContent.NiveauLabel(EnvironnementSynthese)
                : CartographieEnvironnementContent.NonEvalueLabel;

        public string EnvironnementSyntheseColor
            => HasAnyEnvironnementScore
                ? CartographieEnvironnementContent.NiveauColor(EnvironnementSynthese)
                : CartographieEnvironnementContent.NonEvalueColor;

        /// <summary>
        /// (Re)construit les wrappers segments (étape 4) et feuilles (étape 5) en pointant
        /// vers la Phase active. Appelé chaque fois que Phase change.
        /// </summary>
        private void RebuildCartographieWrappers()
        {
            if (Phase == null)
            {
                AttachementVM = LangageVM = EmotionsVM = ImaginaireVM = PenseeVM = null;
                FamilleVM = EcolePairsVM = EcransMediasVM = ValeursSocietalesVM = CadreEducatifVM = null;
            }
            else
            {
                Func<int?> ageProvider = () => Phase?.CartographieEnfant.AgeAuMomentDeLaSaisie ?? _patient?.Age;
                AttachementVM = new CartographieSegmentViewModel(Phase.CartographieEnfant.Attachement, ageProvider);
                LangageVM     = new CartographieSegmentViewModel(Phase.CartographieEnfant.Langage,     ageProvider);
                EmotionsVM    = new CartographieSegmentViewModel(Phase.CartographieEnfant.Emotions,    ageProvider);
                ImaginaireVM  = new CartographieSegmentViewModel(Phase.CartographieEnfant.Imaginaire,  ageProvider);
                PenseeVM      = new CartographieSegmentViewModel(Phase.CartographieEnfant.Pensee,      ageProvider);

                FamilleVM           = BuildFeuilleVM(Phase.CartographieEnvironnement.Famille);
                EcolePairsVM        = BuildFeuilleVM(Phase.CartographieEnvironnement.EcolePairs);
                EcransMediasVM      = BuildFeuilleVM(Phase.CartographieEnvironnement.EcransMedias);
                ValeursSocietalesVM = BuildFeuilleVM(Phase.CartographieEnvironnement.ValeursSocietales);
                CadreEducatifVM     = BuildFeuilleVM(Phase.CartographieEnvironnement.CadreEducatif);
            }
            OnPropertyChanged(nameof(AttachementVM));
            OnPropertyChanged(nameof(LangageVM));
            OnPropertyChanged(nameof(EmotionsVM));
            OnPropertyChanged(nameof(ImaginaireVM));
            OnPropertyChanged(nameof(PenseeVM));
            OnPropertyChanged(nameof(Temperament));
            OnPropertyChanged(nameof(Psychomotricite));
            OnPropertyChanged(nameof(Attention));

            OnPropertyChanged(nameof(FamilleVM));
            OnPropertyChanged(nameof(EcolePairsVM));
            OnPropertyChanged(nameof(EcransMediasVM));
            OnPropertyChanged(nameof(ValeursSocietalesVM));
            OnPropertyChanged(nameof(CadreEducatifVM));
            NotifyEnvironnementSynthese();
            NotifyLectureBranche();
        }

        private FeuilleEnvironnementViewModel BuildFeuilleVM(FeuilleEnvironnement modele)
        {
            var vm = new FeuilleEnvironnementViewModel(modele);
            vm.CouleurChanged += NotifyEnvironnementSynthese;

            // V0.3 — branchement lecture LLM par feuille
            if (_feuilleLecture != null)
            {
                vm.LectureCallback = async (feuilleVm, ct) =>
                {
                    var age   = Phase?.CartographieEnvironnement.AgeAuMomentDeLaSaisie ?? _patient?.Age;
                    var motif = ""; // V0.3 : pas de motif structuré pour l'instant
                    return await _feuilleLecture.ReadFeuilleAsync(feuilleVm.Modele, age, motif, ct);
                };
            }

            // Auto-save à chaque mise à jour de la lecture (texte ou édition manuelle)
            vm.LectureChanged += () =>
            {
                if (Phase != null)
                {
                    try { _phaseService.Save(Phase); } catch { /* meilleur effort */ }
                }
            };

            return vm;
        }

        private void NotifyEnvironnementSynthese()
        {
            OnPropertyChanged(nameof(HasAnyEnvironnementScore));
            OnPropertyChanged(nameof(EnvironnementSynthese));
            OnPropertyChanged(nameof(EnvironnementSyntheseLabel));
            OnPropertyChanged(nameof(EnvironnementSyntheseColor));
        }

        // ── V0.4 — Lecture LLM de la branche globale ─────────────────────────

        public string? LectureBrancheMed
        {
            get => Phase?.CartographieEnvironnement.LectureBrancheMed;
            set
            {
                if (Phase == null) return;
                if (Phase.CartographieEnvironnement.LectureBrancheMed != value)
                {
                    Phase.CartographieEnvironnement.LectureBrancheMed = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasLectureBranche));
                    (LireBrancheCommand            as RelayCommand)?.RaiseCanExecuteChanged();
                    (EffacerLectureBrancheCommand  as RelayCommand)?.RaiseCanExecuteChanged();
                    try { _phaseService.Save(Phase); } catch { /* meilleur effort */ }
                }
            }
        }

        public DateTime? LectureBrancheDate
        {
            get => Phase?.CartographieEnvironnement.LectureBrancheDate;
            set
            {
                if (Phase == null) return;
                if (Phase.CartographieEnvironnement.LectureBrancheDate != value)
                {
                    Phase.CartographieEnvironnement.LectureBrancheDate = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool HasLectureBranche => !string.IsNullOrWhiteSpace(LectureBrancheMed);

        private bool _isLectureBrancheEnCours;
        public bool IsLectureBrancheEnCours
        {
            get => _isLectureBrancheEnCours;
            private set
            {
                if (_isLectureBrancheEnCours != value)
                {
                    _isLectureBrancheEnCours = value;
                    OnPropertyChanged();
                    (LireBrancheCommand            as RelayCommand)?.RaiseCanExecuteChanged();
                    (EffacerLectureBrancheCommand  as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private string? _lectureBrancheError;
        public string? LectureBrancheError
        {
            get => _lectureBrancheError;
            private set
            {
                if (_lectureBrancheError != value)
                {
                    _lectureBrancheError = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasLectureBrancheError));
                }
            }
        }
        public bool HasLectureBrancheError => !string.IsNullOrWhiteSpace(_lectureBrancheError);

        private bool CanLireBranche
            => IsWorkingCartographieEnvironnement
            && Phase != null
            && _brancheLecture != null
            && !IsLectureBrancheEnCours;

        private async Task LireBrancheAsync()
        {
            if (Phase == null || _brancheLecture == null) return;
            IsLectureBrancheEnCours = true;
            LectureBrancheError = null;
            try
            {
                var age   = Phase.CartographieEnvironnement.AgeAuMomentDeLaSaisie ?? _patient?.Age;
                var motif = ""; // V0.4 : pas de motif structuré pour l'instant
                var (ok, lecture, error) = await _brancheLecture.ReadBrancheAsync(
                    Phase.CartographieEnvironnement, age, motif, CancellationToken.None);
                if (ok && !string.IsNullOrWhiteSpace(lecture))
                {
                    LectureBrancheMed  = lecture;
                    LectureBrancheDate = DateTime.Now;
                }
                else
                {
                    LectureBrancheError = error ?? "Lecture branche impossible.";
                }
            }
            catch (Exception ex)
            {
                LectureBrancheError = ex.Message;
            }
            finally
            {
                IsLectureBrancheEnCours = false;
            }
        }

        private void EffacerLectureBranche()
        {
            LectureBrancheMed  = null;
            LectureBrancheDate = null;
            LectureBrancheError = null;
        }

        private void NotifyLectureBranche()
        {
            OnPropertyChanged(nameof(LectureBrancheMed));
            OnPropertyChanged(nameof(LectureBrancheDate));
            OnPropertyChanged(nameof(HasLectureBranche));
            OnPropertyChanged(nameof(IsLectureBrancheEnCours));
            OnPropertyChanged(nameof(LectureBrancheError));
            OnPropertyChanged(nameof(HasLectureBrancheError));
        }

        private bool CanValidateCartographieEnfant
            => IsWorkingCartographieEnfant && Phase != null;

        private bool CanValidateCartographieEnvironnement
            => IsWorkingCartographieEnvironnement && Phase != null;

        public string SuspendedMarqueLabel
        {
            get
            {
                if (Phase == null) return "";
                var step = Phase.EtapeCourante switch
                {
                    EvaluationStep.Preparation                => "Étape 1 — Préparation",
                    EvaluationStep.EvaluationCiblee           => "Étape 2 — Évaluation ciblée",
                    EvaluationStep.BilanFinal                   => "Étape 3 — Synthèse diagnostique",
                    EvaluationStep.CartographieEnfant         => "Étape 4 — Cartographie de l'enfant",
                    EvaluationStep.CartographieEnvironnement  => "Étape 5 — Cartographie de l'environnement",
                    _                                         => "?"
                };
                return $"Marque-page : {step}";
            }
        }

        public string SuspendedAgoLabel
        {
            get
            {
                if (Phase == null) return "";
                var days = (DateTime.Now - Phase.DateDerniereModif).TotalDays;
                if (days < 1) return "Dernière session : aujourd'hui";
                if (days < 2) return "Dernière session : hier";
                return $"Dernière session : il y a {(int)days} jours";
            }
        }

        // ── Étape 1 — Préparation ──────────────────────────────────────────

        public ObservableCollection<EditableString> HypothesesPrincipales => Phase?.Preparation.HypothesesPrincipales ?? new();
        public ObservableCollection<EditableString> Differentiels         => Phase?.Preparation.Differentiels         ?? new();
        public ObservableCollection<EditableString> AEliminer             => Phase?.Preparation.AEliminer             ?? new();
        public ObservableCollection<EditableString> PointsVigilance       => Phase?.Preparation.PointsVigilance       ?? new();
        public ObservableCollection<EditableString> QuestionsCliniques    => Phase?.Preparation.QuestionsCliniques    ?? new();

        private bool CanValidatePreparation
            => IsWorkingPreparation && Phase != null && AnyPreparationContent();

        private bool AnyPreparationContent()
            => Phase != null && (
                Phase.Preparation.HypothesesPrincipales.Any(NonEmpty) ||
                Phase.Preparation.Differentiels.Any(NonEmpty) ||
                Phase.Preparation.AEliminer.Any(NonEmpty) ||
                Phase.Preparation.PointsVigilance.Any(NonEmpty) ||
                Phase.Preparation.QuestionsCliniques.Any(NonEmpty));

        private static bool NonEmpty(EditableString s) => s != null && !string.IsNullOrWhiteSpace(s.Value);

        // ── Commandes ──────────────────────────────────────────────────────

        public ICommand StartCommand                  { get; }
        public ICommand ResumeCommand                 { get; }
        public ICommand SuggestPreparationCommand     { get; }
        public ICommand ValidatePreparationCommand    { get; }
        public ICommand TerminateSessionCommand       { get; }
        public ICommand CancelEvaluationCommand       { get; }
        public ICommand AddItemCommand                { get; }
        public ICommand RemoveItemCommand             { get; }

        private void StartNew()
        {
            if (_patient == null || string.IsNullOrEmpty(_patient.DirectoryPath)) return;
            try
            {
                Phase = _phaseService.Create(_patient.NomComplet, _patient.DirectoryPath);
                RebuildCartographieWrappers();
                IsWorkingPreparation = true;
                StatusMessage = "Évaluation démarrée — Étape 1 Préparation clinique.";
                NotifyAllStates();
                NotifyPreparationCollections();
                PhaseStateChanged?.Invoke();   // nouvelle card "active" à ajouter à la frise
            }
            catch (Exception ex)
            {
                StatusMessage = $"Impossible de démarrer : {ex.Message}";
            }
        }

        private void Resume()
        {
            if (Phase == null) return;
            // Route selon l'étape courante (marque-page)
            switch (Phase.EtapeCourante)
            {
                case EvaluationStep.EvaluationCiblee:
                    IsWorkingEvaluation = true;
                    StatusMessage = "Reprise de l'évaluation — Étape 2.";
                    NotifyEvaluationCollections();
                    break;
                case EvaluationStep.BilanFinal:
                    IsWorkingBilanFinal = true;
                    StatusMessage = "Reprise de l'évaluation — Étape 3 Synthèse diagnostique.";
                    NotifyBilanFinalCollections();
                    break;
                case EvaluationStep.CartographieEnfant:
                    if (IsCartographieDisponible)
                    {
                        IsWorkingCartographieEnfant = true;
                        StatusMessage = "Reprise de l'évaluation — Étape 4 Cartographie de l'enfant.";
                        NotifyCartographieCollections();
                    }
                    else
                    {
                        // Patient hors fourchette : retour Synthèse pour clôturer manuellement
                        IsWorkingBilanFinal = true;
                        StatusMessage = "Cartographie de l'enfant non applicable (3-11 ans). Vous pouvez clôturer depuis l'Étape 3.";
                        NotifyBilanFinalCollections();
                    }
                    break;
                case EvaluationStep.CartographieEnvironnement:
                    if (IsCartographieDisponible)
                    {
                        IsWorkingCartographieEnvironnement = true;
                        StatusMessage = "Reprise de l'évaluation — Étape 5 Cartographie de l'environnement.";
                        NotifyEnvironnementSynthese();
                    }
                    else
                    {
                        IsWorkingBilanFinal = true;
                        StatusMessage = "Cartographie de l'environnement non applicable (3-11 ans). Vous pouvez clôturer depuis l'Étape 3.";
                        NotifyBilanFinalCollections();
                    }
                    break;
                default:
                    IsWorkingPreparation = true;
                    StatusMessage = "Reprise de l'évaluation — Étape 1.";
                    NotifyPreparationCollections();
                    break;
            }
            NotifyAllStates();
        }

        /// <summary>
        /// Affiche une phase donnée dans le panneau, éventuellement en lecture seule (utilisé pour
        /// rouvrir une évaluation clôturée depuis la frise).
        /// Route vers l'étape correspondant au marque-page (EtapeCourante).
        /// </summary>
        public void ShowPhase(EvaluationPhase phaseToShow, bool readOnly)
        {
            if (phaseToShow == null) return;

            // Reset des working states (on va router vers le bon en fonction de l'étape)
            IsWorkingPreparation               = false;
            IsWorkingEvaluation                = false;
            IsWorkingBilanFinal                  = false;
            IsWorkingCartographieEnfant        = false;
            IsWorkingCartographieEnvironnement = false;

            Phase = phaseToShow;
            IsReadOnly = readOnly;
            RebuildCartographieWrappers();

            // Hook auto-save uniquement si éditable — en lecture seule c'est sans effet utile
            if (!readOnly)
            {
                HookAxisHandlers(Phase.EvaluationCiblee.AxesPrincipaux);
                HookAxisHandlers(Phase.EvaluationCiblee.AxesDifferentiels);
                HookAxisHandlers(Phase.EvaluationCiblee.AxesSystemiques);
                HookBilanFinalHandlers(Phase.BilanFinal);
            }

            // Router selon le marque-page
            switch (Phase.EtapeCourante)
            {
                case EvaluationStep.EvaluationCiblee:
                    IsWorkingEvaluation = true;
                    NotifyEvaluationCollections();
                    break;
                case EvaluationStep.BilanFinal:
                    IsWorkingBilanFinal = true;
                    NotifyBilanFinalCollections();
                    break;
                case EvaluationStep.CartographieEnfant:
                    IsWorkingCartographieEnfant = true;
                    NotifyCartographieCollections();
                    break;
                case EvaluationStep.CartographieEnvironnement:
                    IsWorkingCartographieEnvironnement = true;
                    NotifyEnvironnementSynthese();
                    break;
                default:
                    IsWorkingPreparation = true;
                    NotifyPreparationCollections();
                    break;
            }

            StatusMessage = readOnly
                ? $"Évaluation clôturée le {Phase.DateCloture:dd/MM/yyyy} — lecture seule."
                : "Reprise de l'évaluation.";
            NotifyAllStates();
        }

        /// <summary>
        /// Lecture seule : navigation entre les étapes sans modifier la phase ni écrire sur disque.
        /// Param attendu : "1", "2", "3", "4" ou "5".
        /// </summary>
        private void ViewStep(string? step)
        {
            if (!IsReadOnly || Phase == null) return;
            IsWorkingPreparation               = false;
            IsWorkingEvaluation                = false;
            IsWorkingBilanFinal                  = false;
            IsWorkingCartographieEnfant        = false;
            IsWorkingCartographieEnvironnement = false;
            switch (step)
            {
                case "1": IsWorkingPreparation               = true; NotifyPreparationCollections(); break;
                case "2": IsWorkingEvaluation                = true; NotifyEvaluationCollections();  break;
                case "3": IsWorkingCartographieEnfant        = true; NotifyCartographieCollections(); break;
                case "4": IsWorkingCartographieEnvironnement = true; NotifyEnvironnementSynthese();   break;
                case "5": IsWorkingBilanFinal                = true; NotifyBilanFinalCollections();   break;
            }
            NotifyAllStates();
        }

        /// <summary>
        /// Sort du mode lecture seule et restaure l'évaluation active du patient (ou Phase=null
        /// s'il n'y en a pas). Utilisé quand le médecin quitte une vue clôturée pour revenir
        /// au contexte de travail courant.
        /// </summary>
        public void ReturnToActiveContext()
        {
            IsReadOnly = false;
            IsWorkingPreparation               = false;
            IsWorkingEvaluation                = false;
            IsWorkingBilanFinal                  = false;
            IsWorkingCartographieEnfant        = false;
            IsWorkingCartographieEnvironnement = false;
            StatusMessage = "";
            // ReloadState charge l'évaluation active s'il y en a une, sinon Phase = null
            ReloadState();
            // Notifie l'orchestrateur pour qu'il bascule IsEvaluationPhaseMode à false et
            // affiche à nouveau la frise des consultations / la liste des patients.
            ReadOnlyViewClosed?.Invoke();
        }

        public async Task SuggestPreparationAsync()
        {
            if (Phase == null || _suggester == null) return;
            IsBusy = true;
            StatusMessage = "Génération IA en cours...";
            try
            {
                var ctx = await BuildContextAsync();
                var (ok, sug, err) = await _suggester.SuggestAsync(
                    _patient?.NomComplet ?? "",
                    ctx.Age,
                    ctx.Motif,
                    ctx.Synthese,
                    ctx.ObservationsRecentes);

                if (!ok || sug == null)
                {
                    StatusMessage = $"Suggestion IA indisponible : {err}";
                    return;
                }

                // On REMPLACE le contenu actuel par la suggestion (les listes étaient vides ou
                // partiellement remplies — le médecin peut éditer librement après).
                ReplaceList(Phase.Preparation.HypothesesPrincipales, sug.HypothesesPrincipales);
                ReplaceList(Phase.Preparation.Differentiels,          sug.Differentiels);
                ReplaceList(Phase.Preparation.AEliminer,              sug.AEliminer);
                ReplaceList(Phase.Preparation.PointsVigilance,        sug.PointsVigilance);
                ReplaceList(Phase.Preparation.QuestionsCliniques,     sug.QuestionsCliniques);

                _phaseService.Save(Phase);
                StatusMessage = "Suggestion IA reçue — vous pouvez éditer librement.";
                NotifyPreparationCollections();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur IA : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static void ReplaceList(ObservableCollection<EditableString> target, System.Collections.Generic.List<string> source)
        {
            target.Clear();
            foreach (var s in source)
                if (!string.IsNullOrWhiteSpace(s)) target.Add(new EditableString(s));
        }

        private void ValidatePreparation()
        {
            if (Phase == null) return;
            // Nettoyer les entrées vides
            CleanEmpty(Phase.Preparation.HypothesesPrincipales);
            CleanEmpty(Phase.Preparation.Differentiels);
            CleanEmpty(Phase.Preparation.AEliminer);
            CleanEmpty(Phase.Preparation.PointsVigilance);
            CleanEmpty(Phase.Preparation.QuestionsCliniques);

            Phase.Preparation.ValidationDate = DateTime.Now;
            Phase.EtapeCourante = EvaluationStep.EvaluationCiblee;
            _phaseService.Save(Phase);

            // Transition directe vers Étape 2 dans la même séance
            IsWorkingPreparation = false;
            IsWorkingEvaluation  = true;
            StatusMessage = "Étape 1 validée. Vous pouvez maintenant générer les axes d'évaluation.";
            NotifyAllStates();
            NotifyEvaluationCollections();
        }

        private void TerminateSession()
        {
            if (Phase == null) return;

            // Étape 1 : cleanup
            CleanEmpty(Phase.Preparation.HypothesesPrincipales);
            CleanEmpty(Phase.Preparation.Differentiels);
            CleanEmpty(Phase.Preparation.AEliminer);
            CleanEmpty(Phase.Preparation.PointsVigilance);
            CleanEmpty(Phase.Preparation.QuestionsCliniques);

            // Étape 3 : cleanup
            CleanEmpty(Phase.BilanFinal.DiagnosticsRetenus);
            CleanEmpty(Phase.BilanFinal.ElementsEnFaveur);
            CleanEmptyEcartes(Phase.BilanFinal.DiagnosticsEcartes);

            _phaseService.Save(Phase);
            IsWorkingPreparation               = false;
            IsWorkingEvaluation                = false;
            IsWorkingBilanFinal                  = false;
            IsWorkingCartographieEnfant        = false;
            IsWorkingCartographieEnvironnement = false;
            StatusMessage = "Séance suspendue. Vous reprendrez exactement ici la prochaine fois.";
            NotifyAllStates();
        }

        private static void CleanEmptyEcartes(ObservableCollection<DiagnosticEcarte> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i] == null || string.IsNullOrWhiteSpace(list[i].Label)) list.RemoveAt(i);
        }

        private void CancelEvaluation()
        {
            // V0 : pas de suppression de fichier, juste retour à l'état "suspendue"
            // (la suppression sera un geste explicite via le dossier, plus tard)
            TerminateSession();
        }

        private static void CleanEmpty(ObservableCollection<EditableString> list)
        {
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i] == null || string.IsNullOrWhiteSpace(list[i].Value)) list.RemoveAt(i);
        }

        // ── Add / Remove items ─────────────────────────────────────────────

        private void AddItem(string? category)
        {
            var list = ListForCategory(category);
            list?.Add(new EditableString(""));
        }

        private void RemoveItem(object? param)
        {
            if (param is EditableString item)
            {
                // Préparation (Étape 1)
                foreach (var cat in new[] { "hypotheses", "differentiels", "a_eliminer", "vigilance", "questions" })
                {
                    var list = ListForCategory(cat);
                    if (list != null && list.Remove(item)) return;
                }
                // Synthèse diagnostique (Étape 3)
                if (Phase != null)
                {
                    if (Phase.BilanFinal.DiagnosticsRetenus.Remove(item)) { TrySaveBilanFinal(); return; }
                    if (Phase.BilanFinal.ElementsEnFaveur.Remove(item))   { TrySaveBilanFinal(); return; }
                }
            }
        }

        private ObservableCollection<EditableString>? ListForCategory(string? cat) => cat switch
        {
            "hypotheses" => Phase?.Preparation.HypothesesPrincipales,
            "differentiels" => Phase?.Preparation.Differentiels,
            "a_eliminer" => Phase?.Preparation.AEliminer,
            "vigilance" => Phase?.Preparation.PointsVigilance,
            "questions" => Phase?.Preparation.QuestionsCliniques,
            _ => null
        };

        // ═══════════════════════════════════════════════════════════════════
        // ÉTAPE 2 — Évaluation ciblée (V0.1)
        // ═══════════════════════════════════════════════════════════════════

        public ObservableCollection<EvaluationAxis> AxesPrincipaux     => Phase?.EvaluationCiblee.AxesPrincipaux     ?? new();
        public ObservableCollection<EvaluationAxis> AxesDifferentiels  => Phase?.EvaluationCiblee.AxesDifferentiels  ?? new();
        public ObservableCollection<EvaluationAxis> AxesSystemiques    => Phase?.EvaluationCiblee.AxesSystemiques    ?? new();

        public bool HasAnyAxes
            => Phase != null && (
                Phase.EvaluationCiblee.AxesPrincipaux.Count > 0 ||
                Phase.EvaluationCiblee.AxesDifferentiels.Count > 0 ||
                Phase.EvaluationCiblee.AxesSystemiques.Count > 0);

        public ICommand SuggestAxesCommand        { get; private set; } = null!;
        public ICommand ValidateEvaluationCommand { get; private set; } = null!;
        public ICommand BackToPreparationCommand  { get; private set; } = null!;
        public ICommand SetAxisStateCommand       { get; private set; } = null!;

        private bool CanValidateEvaluationCiblee
            => IsWorkingEvaluation && Phase != null && HasAnyAxes;

        public async Task SuggestAxesAsync()
        {
            if (Phase == null || _axesSuggester == null) return;

            IsBusy = true;
            StatusMessage = "Génération IA des axes en cours...";
            try
            {
                var hypotheses    = Phase.Preparation.HypothesesPrincipales.Select(e => e.Value).Where(NonEmptyStr).ToList();
                var differentiels = Phase.Preparation.Differentiels.Select(e => e.Value).Where(NonEmptyStr).ToList();
                var vigilance     = Phase.Preparation.PointsVigilance.Select(e => e.Value).Where(NonEmptyStr).ToList();
                var motif         = (await BuildContextAsync()).Motif;

                var (ok, sug, err) = await _axesSuggester.SuggestAsync(_patient?.Age, hypotheses, differentiels, vigilance, motif);
                if (!ok || sug == null)
                {
                    StatusMessage = $"Suggestion IA indisponible : {err}";
                    return;
                }

                // On REMPLACE — le médecin pourra ré-éditer librement
                ReplaceAxes(Phase.EvaluationCiblee.AxesPrincipaux,    sug.AxesPrincipaux,    AxisCategory.Principal);
                ReplaceAxes(Phase.EvaluationCiblee.AxesDifferentiels, sug.AxesDifferentiels, AxisCategory.Differentiel);
                ReplaceAxes(Phase.EvaluationCiblee.AxesSystemiques,   sug.AxesSystemiques,   AxisCategory.Systemique);

                _phaseService.Save(Phase);
                StatusMessage = "Axes proposés — explorez librement, prenez des notes par axe.";
                NotifyEvaluationCollections();
                OnPropertyChanged(nameof(HasAnyAxes));
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur IA : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private static bool NonEmptyStr(string s) => !string.IsNullOrWhiteSpace(s);

        private void ReplaceAxes(ObservableCollection<EvaluationAxis> target,
                                 System.Collections.Generic.List<AxesSuggesterService.SuggestedAxis> source,
                                 AxisCategory cat)
        {
            target.Clear();
            foreach (var s in source)
            {
                if (string.IsNullOrWhiteSpace(s.Label)) continue;
                var ax = new EvaluationAxis
                {
                    Label         = s.Label,
                    Justification = s.Justification ?? "",
                    Category      = cat,
                    State         = AxisExplorationState.NonAborde
                };
                foreach (var q in s.Questions ?? new())
                    if (!string.IsNullOrWhiteSpace(q))
                        ax.SuggestedQuestions.Add(new EditableString(q));
                foreach (var o in s.Observations ?? new())
                {
                    if (string.IsNullOrWhiteSpace(o)) continue;
                    var obs = new AxisObservationItem { Label = o, IsChecked = false };
                    obs.PropertyChanged += OnObservationChanged;
                    ax.ObservationsProposees.Add(obs);
                }
                // À chaque modif d'observation/état → save (auto-save)
                ax.PropertyChanged += OnAxisChanged;
                target.Add(ax);
            }
        }

        private void OnObservationChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (Phase == null) return;
            if (e.PropertyName != nameof(AxisObservationItem.IsChecked)) return;

            // Quand l'utilisateur coche une observation, on l'ajoute automatiquement au
            // champ texte libre de l'axe parent — sauf si déjà présent. Ça rend le check
            // utile pour étoffer la narration. Décocher ne touche pas le texte (le médecin
            // l'a peut-être intégré dans une phrase).
            if (sender is AxisObservationItem obs && obs.IsChecked)
            {
                var parent = FindParentAxis(obs);
                if (parent != null)
                    AppendObservationLabel(parent, obs.Label);
            }

            try { _phaseService.Save(Phase); } catch { /* meilleur effort */ }
        }

        private EvaluationAxis? FindParentAxis(AxisObservationItem obs)
        {
            if (Phase == null) return null;
            foreach (var ax in Phase.EvaluationCiblee.AxesPrincipaux)
                if (ax.ObservationsProposees.Contains(obs)) return ax;
            foreach (var ax in Phase.EvaluationCiblee.AxesDifferentiels)
                if (ax.ObservationsProposees.Contains(obs)) return ax;
            foreach (var ax in Phase.EvaluationCiblee.AxesSystemiques)
                if (ax.ObservationsProposees.Contains(obs)) return ax;
            return null;
        }

        private static void AppendObservationLabel(EvaluationAxis axis, string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return;
            var current = axis.Observation ?? "";

            // Déjà présent dans le texte (insensible casse) → on ne duplique pas.
            if (current.IndexOf(label, StringComparison.OrdinalIgnoreCase) >= 0) return;

            axis.Observation = string.IsNullOrWhiteSpace(current)
                ? label
                : current.TrimEnd().TrimEnd(',') + ", " + label;
        }

        private void OnAxisChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (Phase == null) return;
            if (e.PropertyName == nameof(EvaluationAxis.Observation) ||
                e.PropertyName == nameof(EvaluationAxis.State))
            {
                try { _phaseService.Save(Phase); } catch { /* meilleur effort */ }
            }
        }

        /// <summary>
        /// Param attendu : Tuple&lt;EvaluationAxis, AxisExplorationState&gt;.
        /// </summary>
        private void SetAxisState(object? param)
        {
            if (param is not Tuple<EvaluationAxis, AxisExplorationState> t) return;
            t.Item1.State = t.Item2;
        }

        private void ValidateEvaluationCiblee()
        {
            if (Phase == null) return;
            Phase.EvaluationCiblee.ValidationDate = DateTime.Now;

            // Nouveau flow : Évaluation Ciblée → Cartographie de l'enfant (si 3-11) → Cartographie
            // de l'environnement → Bilan Final. Si âge hors 3-11, skip direct au Bilan Final.
            if (IsCartographieDisponible)
            {
                Phase.EtapeCourante = EvaluationStep.CartographieEnfant;
                Phase.CartographieEnfant.AgeAuMomentDeLaSaisie = _patient?.Age;
                _phaseService.Save(Phase);

                IsWorkingEvaluation = false;
                IsWorkingCartographieEnfant = true;
                StatusMessage = "Étape 2 validée. Passage à l'Étape 3 — Cartographie de l'enfant.";
                NotifyAllStates();
            }
            else
            {
                Phase.EtapeCourante = EvaluationStep.BilanFinal;
                _phaseService.Save(Phase);

                IsWorkingEvaluation = false;
                IsWorkingBilanFinal  = true;
                StatusMessage = "Étape 2 validée. Cartographies non applicables (3-11 ans) — passage direct au Bilan Final.";
                NotifyAllStates();
                NotifyBilanFinalCollections();
            }
        }

        private void BackToPreparation()
        {
            // Re-anchorage : retour à l'Étape 1 SANS perdre l'Étape 2 déjà saisie
            if (Phase == null) return;
            Phase.Preparation.ValidationDate = null;  // ré-éditable
            Phase.EtapeCourante = EvaluationStep.Preparation;
            _phaseService.Save(Phase);

            IsWorkingEvaluation = false;
            IsWorkingPreparation = true;
            StatusMessage = "Retour à l'Étape 1 — vous pouvez modifier les hypothèses. L'Étape 2 reste conservée.";
            NotifyAllStates();
            NotifyPreparationCollections();
        }

        private void NotifyEvaluationCollections()
        {
            OnPropertyChanged(nameof(AxesPrincipaux));
            OnPropertyChanged(nameof(AxesDifferentiels));
            OnPropertyChanged(nameof(AxesSystemiques));
            OnPropertyChanged(nameof(HasAnyAxes));
        }

        // ═══════════════════════════════════════════════════════════════════
        // ÉTAPE A — Dicte évaluation + extraction LLM vers les axes
        // ═══════════════════════════════════════════════════════════════════

        private bool _isDicteActive;
        public bool IsDicteActive
        {
            get => _isDicteActive;
            set { if (_isDicteActive != value) { _isDicteActive = value; OnPropertyChanged(); NotifyAllStates(); } }
        }

        private float _dicteAudioLevel;
        public float DicteAudioLevel
        {
            get => _dicteAudioLevel;
            set { if (Math.Abs(_dicteAudioLevel - value) > 0.001f) { _dicteAudioLevel = value; OnPropertyChanged(); OnPropertyChanged(nameof(DicteAudioLevelPct)); } }
        }
        public int DicteAudioLevelPct => Math.Min(100, (int)(_dicteAudioLevel * 600));

        private string _dicteStatus = "";
        public string DicteStatus
        {
            get => _dicteStatus;
            set { if (_dicteStatus != value) { _dicteStatus = value; OnPropertyChanged(); } }
        }

        private string _lastRawTranscriptPath = "";
        public string LastRawTranscriptPath
        {
            get => _lastRawTranscriptPath;
            private set { if (_lastRawTranscriptPath != value) { _lastRawTranscriptPath = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasRawTranscript)); } }
        }
        public bool HasRawTranscript => !string.IsNullOrEmpty(LastRawTranscriptPath) && System.IO.File.Exists(LastRawTranscriptPath);

        public bool CanStartDicte
            => IsWorkingEvaluation && !IsDicteActive && !IsBusy
               && _whisper != null && _axisExtractor != null
               && HasAnyAxes;

        public ICommand StartDicteCommand        { get; private set; } = null!;
        public ICommand StopDicteCommand         { get; private set; } = null!;
        public ICommand AcceptMedTextCommand     { get; private set; } = null!;
        public ICommand RejectMedTextCommand     { get; private set; } = null!;
        public ICommand OpenRawTranscriptCommand { get; private set; } = null!;

        private async Task StartDicteAsync()
        {
            if (_whisper == null) return;
            if (_whisper.IsActive)
            {
                DicteStatus = "Whisper est déjà actif (utilisé par un autre mode ?). Réessaie.";
                return;
            }

            _dicteBuffer.Clear();
            DicteStatus = "Préparation du modèle...";

            // Handlers éphémères : seuls actifs pendant la dicte évaluation
            _whisperTextHandler = txt =>
            {
                if (string.IsNullOrEmpty(txt)) return;
                lock (_dicteBuffer) _dicteBuffer.Append(txt);
            };
            _whisperLevelHandler = lvl =>
                System.Windows.Application.Current?.Dispatcher.InvokeAsync(() => DicteAudioLevel = lvl);

            _whisper.TextAppended      += _whisperTextHandler;
            _whisper.AudioLevelChanged += _whisperLevelHandler;

            try
            {
                _whisper.Mode = RecordingMode.Batch;
                _whisper.BatchDurationSeconds = 90;
                var modelManager = new WhisperModelManager();
                await _whisper.StartAsync(modelManager);
                IsDicteActive = true;
                DicteStatus = "🔴 Med écoute — concentrez-vous sur le patient.";
            }
            catch (Exception ex)
            {
                DicteStatus = $"Erreur démarrage micro : {ex.Message}";
                UnsubscribeWhisperHandlers();
            }
        }

        private async Task StopDicteAsync()
        {
            if (_whisper == null) return;

            DicteStatus = "⏳ Finalisation de la transcription...";
            try { await _whisper.StopAsync(); } catch { /* meilleur effort */ }
            IsDicteActive = false;
            UnsubscribeWhisperHandlers();

            string transcript;
            lock (_dicteBuffer) transcript = _dicteBuffer.ToString().Trim();
            if (string.IsNullOrWhiteSpace(transcript))
            {
                DicteStatus = "Aucun texte transcrit.";
                return;
            }

            // Sauvegarde brute (médico-légal / debug)
            try { SaveRawTranscript(transcript); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Dicte] Save raw échec : {ex.Message}"); }

            // Extraction LLM → routage vers axes
            await RunExtractionAsync(transcript);
        }

        private void UnsubscribeWhisperHandlers()
        {
            if (_whisper == null) return;
            if (_whisperTextHandler  != null) { _whisper.TextAppended      -= _whisperTextHandler;  _whisperTextHandler  = null; }
            if (_whisperLevelHandler != null) { _whisper.AudioLevelChanged -= _whisperLevelHandler; _whisperLevelHandler = null; }
        }

        private void SaveRawTranscript(string text)
        {
            if (_patient == null || string.IsNullOrEmpty(_patient.DirectoryPath)) return;
            var dir = System.IO.Path.Combine(_patient.DirectoryPath, DateTime.Now.Year.ToString(), "transcriptions");
            System.IO.Directory.CreateDirectory(dir);
            var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
            var path  = System.IO.Path.Combine(dir, $"{stamp}_evaluation_dicte.txt");
            System.IO.File.WriteAllText(path, text, System.Text.Encoding.UTF8);
            LastRawTranscriptPath = path;
        }

        private void OpenRawTranscript()
        {
            if (!HasRawTranscript) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = LastRawTranscriptPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                DicteStatus = $"Impossible d'ouvrir la transcription : {ex.Message}";
            }
        }

        private async Task RunExtractionAsync(string transcript)
        {
            if (Phase == null || _axisExtractor == null) return;
            IsBusy = true;
            DicteStatus = "✨ Med analyse et répartit...";
            try
            {
                var allAxes = AxesPrincipaux.Concat(AxesDifferentiels).Concat(AxesSystemiques).ToList();
                var axesCtx = allAxes.Select(a => new AxisExtractorService.AxisContext
                {
                    Label               = a.Label,
                    Justification       = a.Justification,
                    ObservationActuelle = a.Observation
                }).ToList();

                var (ok, res, err) = await _axisExtractor.ExtractAsync(transcript, _patient?.Age, axesCtx);
                if (!ok || res == null)
                {
                    DicteStatus = $"Extraction impossible : {err}";
                    return;
                }

                int affected = 0;
                foreach (var ax in allAxes)
                {
                    if (!res.Updates.TryGetValue(ax.Label, out var addition) || string.IsNullOrWhiteSpace(addition)) continue;
                    // Append au PendingMedText (en concaténant si déjà du pending d'une session précédente)
                    if (string.IsNullOrWhiteSpace(ax.PendingMedText))
                        ax.PendingMedText = addition.Trim();
                    else
                        ax.PendingMedText = (ax.PendingMedText + " " + addition.Trim()).Trim();
                    affected++;
                }

                _phaseService.Save(Phase);
                DicteStatus = affected == 0
                    ? "Med n'a rien trouvé à répartir. Tu peux relire la transcription brute."
                    : $"Med a proposé du contenu pour {affected} axe(s). Vérifie et accepte ou ignore.";
            }
            catch (Exception ex)
            {
                DicteStatus = $"Erreur extraction : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ── Validation chips par axe ────────────────────────────────────────

        private void AcceptMedText(EvaluationAxis? axis)
        {
            if (axis == null || string.IsNullOrWhiteSpace(axis.PendingMedText) || Phase == null) return;
            var addition = axis.PendingMedText.Trim();
            if (string.IsNullOrWhiteSpace(axis.Observation))
                axis.Observation = addition;
            else
                axis.Observation = (axis.Observation.TrimEnd() + "\n" + addition).Trim();
            axis.PendingMedText = "";
            _phaseService.Save(Phase);
        }

        private void RejectMedText(EvaluationAxis? axis)
        {
            if (axis == null || Phase == null) return;
            axis.PendingMedText = "";
            _phaseService.Save(Phase);
        }

        // ── Construction du contexte LLM ────────────────────────────────────

        private class ContextData
        {
            public int? Age;
            public string Motif = "";
            public string Synthese = "";
            public string ObservationsRecentes = "";
        }

        private Task<ContextData> BuildContextAsync()
        {
            // V0 : remplissage minimal depuis _patient et lecture rapide du disque
            var ctx = new ContextData();
            if (_patient == null) return Task.FromResult(ctx);

            ctx.Age = _patient.Age;

            // Synthèse : on lit synthese/synthese.md s'il existe
            try
            {
                if (!string.IsNullOrEmpty(_patient.DirectoryPath))
                {
                    var synthesisDir = System.IO.Path.Combine(_patient.DirectoryPath, "synthese");
                    if (System.IO.Directory.Exists(synthesisDir))
                    {
                        var files = System.IO.Directory.GetFiles(synthesisDir, "*.md");
                        if (files.Length > 0)
                        {
                            var content = System.IO.File.ReadAllText(files[0], System.Text.Encoding.UTF8);
                            ctx.Synthese = StripYaml(content);
                        }
                    }

                    // Observations récentes : dernière note de l'année courante
                    var notesDir = System.IO.Path.Combine(_patient.DirectoryPath, DateTime.Now.Year.ToString(), "notes");
                    if (System.IO.Directory.Exists(notesDir))
                    {
                        var noteFiles = System.IO.Directory.GetFiles(notesDir, "*.md")
                            .Where(f => !System.IO.Path.GetFileName(f).Contains("signal") &&
                                        !System.IO.Path.GetFileName(f).Contains("evaluation") &&
                                        !System.IO.Path.GetFileName(f).Contains("restitution"))
                            .OrderByDescending(f => f)
                            .ToList();
                        if (noteFiles.Count > 0)
                            ctx.ObservationsRecentes = StripYaml(System.IO.File.ReadAllText(noteFiles[0], System.Text.Encoding.UTF8));
                    }
                }
            }
            catch { /* contexte facultatif */ }

            return Task.FromResult(ctx);
        }

        private static string StripYaml(string content)
        {
            if (string.IsNullOrEmpty(content) || !content.TrimStart().StartsWith("---")) return content;
            var first = content.IndexOf("---", StringComparison.Ordinal);
            var second = content.IndexOf("---", first + 3, StringComparison.Ordinal);
            if (second < 0) return content;
            return content.Substring(second + 3).TrimStart('\r', '\n');
        }

        // ═══════════════════════════════════════════════════════════════════
        // ÉTAPE 3 — Synthèse diagnostique (V0.2)
        // ═══════════════════════════════════════════════════════════════════

        public ObservableCollection<EditableString>   DiagnosticsRetenus  => Phase?.BilanFinal.DiagnosticsRetenus ?? new();
        public ObservableCollection<EditableString>   ElementsEnFaveur    => Phase?.BilanFinal.ElementsEnFaveur   ?? new();
        public ObservableCollection<DiagnosticEcarte> DiagnosticsEcartes  => Phase?.BilanFinal.DiagnosticsEcartes ?? new();

        public NiveauCertitude BilanFinalCertitude
        {
            get => Phase?.BilanFinal.Certitude ?? NiveauCertitude.NonRenseigne;
            set { if (Phase != null) Phase.BilanFinal.Certitude = value; }
        }
        public bool IsCertitudeHypothese => BilanFinalCertitude == NiveauCertitude.HypotheseAConfirmer;

        /// <summary>
        /// Paragraphe synthèse intégrative (Étape 5). Éditable manuellement par le psy
        /// après génération LLM. Auto-save à chaque édition.
        /// </summary>
        public string? SyntheseIntegrative
        {
            get => Phase?.BilanFinal.SyntheseIntegrative;
            set
            {
                if (Phase == null) return;
                if (Phase.BilanFinal.SyntheseIntegrative != value)
                {
                    Phase.BilanFinal.SyntheseIntegrative = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasSyntheseIntegrative));
                    try { _phaseService.Save(Phase); } catch { /* meilleur effort */ }
                }
            }
        }
        public bool HasSyntheseIntegrative => !string.IsNullOrWhiteSpace(SyntheseIntegrative);

        public ICommand EffacerSyntheseIntegrativeCommand
            => new RelayCommand(_ => { SyntheseIntegrative = null; }, _ => HasSyntheseIntegrative);
        public bool IsCertitudeProbable  => BilanFinalCertitude == NiveauCertitude.Probable;
        public bool IsCertitudeCertain   => BilanFinalCertitude == NiveauCertitude.Certain;

        public ICommand SuggestBilanFinalCommand        { get; private set; } = null!;
        public ICommand ValidateBilanFinalCommand       { get; private set; } = null!;
        public ICommand BackToCartographieEnvironnementCommand { get; private set; } = null!;
        public ICommand AddBilanFinalItemCommand        { get; private set; } = null!;
        public ICommand RemoveBilanFinalItemCommand     { get; private set; } = null!;
        public ICommand AddDiagnosticEcarteCommand    { get; private set; } = null!;
        public ICommand RemoveDiagnosticEcarteCommand { get; private set; } = null!;
        public ICommand SetCertitudeCommand           { get; private set; } = null!;

        // Lecture seule
        public ICommand ViewStepCommand               { get; private set; } = null!;
        public ICommand CloseReadOnlyCommand          { get; private set; } = null!;

        // Étape 4 — Cartographie de l'enfant
        public ICommand ValidateCartographieEnfantCommand { get; private set; } = null!;
        public ICommand BackToEvaluationCibleeCommand     { get; private set; } = null!;

        // Étape 5 — Cartographie de l'environnement
        public ICommand ValidateCartographieEnvironnementCommand { get; private set; } = null!;
        public ICommand BackToCartographieEnfantCommand          { get; private set; } = null!;
        public ICommand LireBrancheCommand                       { get; private set; } = null!;
        public ICommand EffacerLectureBrancheCommand             { get; private set; } = null!;

        private bool CanValidateBilanFinal
            => IsWorkingBilanFinal && Phase != null && (
                Phase.BilanFinal.DiagnosticsRetenus.Any(NonEmpty) ||
                Phase.BilanFinal.ElementsEnFaveur.Any(NonEmpty) ||
                Phase.BilanFinal.DiagnosticsEcartes.Any(e => !string.IsNullOrWhiteSpace(e?.Label)) ||
                Phase.BilanFinal.Certitude != NiveauCertitude.NonRenseigne);

        public async Task SuggestBilanFinalAsync()
        {
            if (Phase == null || _bilanFinalSuggester == null) return;

            IsBusy = true;
            StatusMessage = "Génération IA de la synthèse en cours...";
            try
            {
                var allAxes = Phase.EvaluationCiblee.AxesPrincipaux
                    .Concat(Phase.EvaluationCiblee.AxesDifferentiels)
                    .Concat(Phase.EvaluationCiblee.AxesSystemiques)
                    .ToList();
                var axes = allAxes.Select(a => new BilanFinalSuggesterService.AxisInput
                {
                    Label         = a.Label,
                    Justification = a.Justification,
                    State         = (int)a.State,
                    Observation   = a.Observation
                }).ToList();

                var motif = (await BuildContextAsync()).Motif;
                var (ok, sug, err) = await _bilanFinalSuggester.SuggestAsync(
                    _patient?.Age, motif, axes,
                    Phase.CartographieEnfant, Phase.CartographieEnvironnement);
                if (!ok || sug == null)
                {
                    StatusMessage = $"Suggestion IA indisponible : {err}";
                    return;
                }

                // On REMPLACE le contenu — le médecin pourra éditer librement après
                ReplaceList(Phase.BilanFinal.DiagnosticsRetenus, sug.DiagnosticsRetenus);
                ReplaceList(Phase.BilanFinal.ElementsEnFaveur,   sug.ElementsEnFaveur);
                ReplaceEcartes(Phase.BilanFinal.DiagnosticsEcartes, sug.DiagnosticsEcartes);
                if (sug.Certitude >= 0 && sug.Certitude <= 3)
                    Phase.BilanFinal.Certitude = (NiveauCertitude)sug.Certitude;
                if (!string.IsNullOrWhiteSpace(sug.SyntheseIntegrative))
                {
                    Phase.BilanFinal.SyntheseIntegrative     = sug.SyntheseIntegrative.Trim();
                    Phase.BilanFinal.SyntheseIntegrativeDate = DateTime.Now;
                }

                HookBilanFinalHandlers(Phase.BilanFinal);
                _phaseService.Save(Phase);
                StatusMessage = "Bilan Final proposé — vous pouvez éditer librement.";
                NotifyBilanFinalCollections();
                OnPropertyChanged(nameof(SyntheseIntegrative));
                OnPropertyChanged(nameof(HasSyntheseIntegrative));
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur IA : {ex.Message}";
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ReplaceEcartes(ObservableCollection<DiagnosticEcarte> target,
                                    System.Collections.Generic.List<BilanFinalSuggesterService.EcarteSuggestion> source)
        {
            target.Clear();
            foreach (var s in source)
            {
                if (string.IsNullOrWhiteSpace(s.Label)) continue;
                var e = new DiagnosticEcarte(s.Label, s.Motif ?? "");
                e.PropertyChanged += OnEcarteChanged;
                target.Add(e);
            }
        }

        private void ValidateBilanFinal()
        {
            if (Phase == null) return;
            CleanEmpty(Phase.BilanFinal.DiagnosticsRetenus);
            CleanEmpty(Phase.BilanFinal.ElementsEnFaveur);
            CleanEmptyEcartes(Phase.BilanFinal.DiagnosticsEcartes);

            Phase.BilanFinal.ValidationDate = DateTime.Now;

            // Étape 5 = dernière étape — la validation clôture l'évaluation.
            _phaseService.Save(Phase);
            _phaseService.Close(Phase);
            IsWorkingBilanFinal = false;
            Phase = null;
            StatusMessage = "Bilan Final validé. Évaluation clôturée.";
            NotifyAllStates();
            PhaseStateChanged?.Invoke();
        }

        /// <summary>
        /// Valide l'Étape 4 (Cartographie de l'enfant) et passe à l'Étape 5
        /// (Cartographie de l'environnement) si patient 3-11 ans, sinon clôture directe.
        /// </summary>
        private void ValidateCartographieEnfant()
        {
            if (Phase == null) return;
            Phase.CartographieEnfant.ValidationDate = DateTime.Now;
            if (!Phase.CartographieEnfant.AgeAuMomentDeLaSaisie.HasValue)
                Phase.CartographieEnfant.AgeAuMomentDeLaSaisie = _patient?.Age;

            if (IsCartographieDisponible)
            {
                Phase.EtapeCourante = EvaluationStep.CartographieEnvironnement;
                Phase.CartographieEnvironnement.AgeAuMomentDeLaSaisie = _patient?.Age;
                _phaseService.Save(Phase);

                IsWorkingCartographieEnfant = false;
                IsWorkingCartographieEnvironnement = true;
                StatusMessage = "Étape 4 validée. Passage à l'Étape 5 — Cartographie de l'environnement.";
                NotifyAllStates();
            }
            else
            {
                _phaseService.Save(Phase);
                _phaseService.Close(Phase);
                IsWorkingCartographieEnfant = false;
                Phase = null;
                StatusMessage = "Évaluation clôturée. Cartographie de l'environnement non applicable (3-11 ans).";
                NotifyAllStates();
                PhaseStateChanged?.Invoke();
            }
        }

        /// <summary>
        /// Valide l'Étape 4 (Cartographie de l'environnement) et passe à l'Étape 5 (Bilan Final).
        /// </summary>
        private void ValidateCartographieEnvironnement()
        {
            if (Phase == null) return;
            Phase.CartographieEnvironnement.ValidationDate = DateTime.Now;
            if (!Phase.CartographieEnvironnement.AgeAuMomentDeLaSaisie.HasValue)
                Phase.CartographieEnvironnement.AgeAuMomentDeLaSaisie = _patient?.Age;

            Phase.EtapeCourante = EvaluationStep.BilanFinal;
            _phaseService.Save(Phase);

            IsWorkingCartographieEnvironnement = false;
            IsWorkingBilanFinal = true;
            StatusMessage = "Étape 4 validée. Passage à l'Étape 5 — Bilan Final.";
            NotifyAllStates();
            NotifyBilanFinalCollections();
        }

        /// <summary>
        /// Retour à l'Étape 4 depuis l'Étape 5. Réinitialise la validation de l'étape 4
        /// pour la rendre éditable, conserve la cartographie de l'environnement.
        /// </summary>
        private void BackToCartographieEnfant()
        {
            if (Phase == null) return;
            Phase.CartographieEnfant.ValidationDate = null;
            Phase.EtapeCourante = EvaluationStep.CartographieEnfant;
            _phaseService.Save(Phase);

            IsWorkingCartographieEnvironnement = false;
            IsWorkingCartographieEnfant = true;
            StatusMessage = "Retour à l'Étape 4 — la Cartographie de l'environnement reste conservée.";
            NotifyAllStates();
            NotifyCartographieCollections();
        }

        /// <summary>
        /// Retour à l'Étape 3 depuis l'Étape 4 (le médecin veut modifier la synthèse).
        /// Réinitialise la validation de l'étape 3 pour la rendre éditable, conserve la cartographie.
        /// </summary>
        /// <summary>
        /// Retour à l'Étape 2 (Évaluation Ciblée) depuis l'Étape 3 (Cartographie Enfant).
        /// Réinitialise la validation de l'étape 2, conserve la cartographie déjà cotée.
        /// </summary>
        private void BackToEvaluationCibleeFromCarto()
        {
            if (Phase == null) return;
            Phase.EvaluationCiblee.ValidationDate = null;
            Phase.EtapeCourante = EvaluationStep.EvaluationCiblee;
            _phaseService.Save(Phase);

            IsWorkingCartographieEnfant = false;
            IsWorkingEvaluation = true;
            StatusMessage = "Retour à l'Étape 2 — les cartographies restent conservées si déjà ébauchées.";
            NotifyAllStates();
            NotifyEvaluationCollections();
        }

        /// <summary>
        /// Retour à l'Étape 4 (Cartographie Environnement) depuis l'Étape 5 (Bilan Final).
        /// Réinitialise la validation de l'étape 4, conserve le Bilan Final déjà ébauché.
        /// </summary>
        private void BackToCartographieEnvironnementFromBilan()
        {
            if (Phase == null) return;
            Phase.CartographieEnvironnement.ValidationDate = null;
            Phase.EtapeCourante = EvaluationStep.CartographieEnvironnement;
            _phaseService.Save(Phase);

            IsWorkingBilanFinal = false;
            IsWorkingCartographieEnvironnement = true;
            StatusMessage = "Retour à l'Étape 4 — le Bilan Final reste conservé si déjà ébauché.";
            NotifyAllStates();
            NotifyEnvironnementSynthese();
        }

        private void AddBilanFinalItem(string? category)
        {
            var list = SyntheseListForCategory(category);
            if (list == null) return;
            var newItem = new EditableString("");
            newItem.PropertyChanged += OnEditableStringChanged;
            list.Add(newItem);
        }

        private void RemoveBilanFinalItem(object? param)
        {
            if (param is EditableString item && Phase != null)
            {
                if (Phase.BilanFinal.DiagnosticsRetenus.Remove(item)) { TrySaveBilanFinal(); return; }
                if (Phase.BilanFinal.ElementsEnFaveur.Remove(item))   { TrySaveBilanFinal(); return; }
            }
        }

        private ObservableCollection<EditableString>? SyntheseListForCategory(string? cat) => cat switch
        {
            "retenus"     => Phase?.BilanFinal.DiagnosticsRetenus,
            "en_faveur"   => Phase?.BilanFinal.ElementsEnFaveur,
            _             => null
        };

        private void AddDiagnosticEcarte()
        {
            if (Phase == null) return;
            var e = new DiagnosticEcarte();
            e.PropertyChanged += OnEcarteChanged;
            Phase.BilanFinal.DiagnosticsEcartes.Add(e);
        }

        private void RemoveDiagnosticEcarte(DiagnosticEcarte? e)
        {
            if (e == null || Phase == null) return;
            if (Phase.BilanFinal.DiagnosticsEcartes.Remove(e)) TrySaveBilanFinal();
        }

        private void SetCertitude(object? param)
        {
            if (Phase == null) return;
            NiveauCertitude target;
            if (param is NiveauCertitude n) target = n;
            else if (param is int i && i >= 0 && i <= 3) target = (NiveauCertitude)i;
            else if (param is string s && int.TryParse(s, out var k) && k >= 0 && k <= 3) target = (NiveauCertitude)k;
            else return;
            Phase.BilanFinal.Certitude = target;
            OnPropertyChanged(nameof(BilanFinalCertitude));
            OnPropertyChanged(nameof(IsCertitudeHypothese));
            OnPropertyChanged(nameof(IsCertitudeProbable));
            OnPropertyChanged(nameof(IsCertitudeCertain));
        }

        private void TrySaveBilanFinal()
        {
            if (Phase == null) return;
            try { _phaseService.Save(Phase); } catch { /* meilleur effort */ }
        }

        private void NotifyBilanFinalCollections()
        {
            OnPropertyChanged(nameof(DiagnosticsRetenus));
            OnPropertyChanged(nameof(ElementsEnFaveur));
            OnPropertyChanged(nameof(DiagnosticsEcartes));
            OnPropertyChanged(nameof(BilanFinalCertitude));
            OnPropertyChanged(nameof(IsCertitudeHypothese));
            OnPropertyChanged(nameof(IsCertitudeProbable));
            OnPropertyChanged(nameof(IsCertitudeCertain));
        }

        // ── INPC ──────────────────────────────────────────────────────────

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? prop = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));

        private void NotifyAllStates()
        {
            OnPropertyChanged(nameof(Phase));
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanResume));
            OnPropertyChanged(nameof(IsWorkingPreparation));
            OnPropertyChanged(nameof(IsWorkingEvaluation));
            OnPropertyChanged(nameof(IsWorkingBilanFinal));
            OnPropertyChanged(nameof(IsWorkingCartographieEnfant));
            OnPropertyChanged(nameof(IsWorkingCartographieEnvironnement));
            OnPropertyChanged(nameof(IsCartographieDisponible));
            OnPropertyChanged(nameof(SuspendedMarqueLabel));
            OnPropertyChanged(nameof(SuspendedAgoLabel));
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                System.Windows.Input.CommandManager.InvalidateRequerySuggested);
        }

        private void NotifyCartographieCollections()
        {
            // Notifie les wrappers que l'âge peut avoir changé (rare, mais possible si _patient change)
            AttachementVM?.NotifyAgeChanged();
            LangageVM?.NotifyAgeChanged();
            EmotionsVM?.NotifyAgeChanged();
            ImaginaireVM?.NotifyAgeChanged();
            PenseeVM?.NotifyAgeChanged();
        }

        private void NotifyPreparationCollections()
        {
            OnPropertyChanged(nameof(HypothesesPrincipales));
            OnPropertyChanged(nameof(Differentiels));
            OnPropertyChanged(nameof(AEliminer));
            OnPropertyChanged(nameof(PointsVigilance));
            OnPropertyChanged(nameof(QuestionsCliniques));
        }
    }
}
