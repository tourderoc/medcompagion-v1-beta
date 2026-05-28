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
                                        WhisperStreamingService? whisper = null)
        {
            _phaseService  = phaseService;
            _suggester     = suggester;
            _axesSuggester = axesSuggester;
            _axisExtractor = axisExtractor;
            _whisper       = whisper;

            StartCommand                  = new RelayCommand(_ => StartNew(),                _ => CanStart);
            ResumeCommand                 = new RelayCommand(_ => Resume(),                  _ => CanResume);
            SuggestPreparationCommand     = new RelayCommand(async _ => await SuggestPreparationAsync(), _ => IsWorkingPreparation && !IsBusy);
            ValidatePreparationCommand    = new RelayCommand(_ => ValidatePreparation(),    _ => CanValidatePreparation);
            TerminateSessionCommand       = new RelayCommand(_ => TerminateSession(),       _ => IsWorkingPreparation || IsWorkingEvaluation);
            CancelEvaluationCommand       = new RelayCommand(_ => CancelEvaluation(),       _ => IsWorkingPreparation || IsWorkingEvaluation);

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
        }

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
            }

            IsWorkingPreparation = false;
            IsWorkingEvaluation  = false;
            StatusMessage = "";
            NotifyAllStates();
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

        // ── États dérivés pour la View (3 cas) ──────────────────────────────

        public bool CanStart  => _patient != null && Phase == null && !IsWorkingPreparation && !IsWorkingEvaluation;
        public bool CanResume => _patient != null && Phase != null && Phase.IsActive && !IsWorkingPreparation && !IsWorkingEvaluation;

        public string SuspendedMarqueLabel
        {
            get
            {
                if (Phase == null) return "";
                var step = Phase.EtapeCourante switch
                {
                    EvaluationStep.Preparation       => "Étape 1 — Préparation",
                    EvaluationStep.EvaluationCiblee  => "Étape 2 — Évaluation ciblée",
                    EvaluationStep.Cartographie      => "Étape 3 — Cartographie",
                    _                                 => "?"
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
                IsWorkingPreparation = true;
                StatusMessage = "Évaluation démarrée — Étape 1 Préparation clinique.";
                NotifyAllStates();
                NotifyPreparationCollections();
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
                case EvaluationStep.Cartographie:
                    // V0.2 : pour l'instant on retombe en Étape 2 si elle existe
                    IsWorkingEvaluation = true;
                    StatusMessage = "Reprise — Étape 3 (Cartographie) bientôt disponible.";
                    NotifyEvaluationCollections();
                    break;
                default:
                    IsWorkingPreparation = true;
                    StatusMessage = "Reprise de l'évaluation — Étape 1.";
                    NotifyPreparationCollections();
                    break;
            }
            NotifyAllStates();
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

            _phaseService.Save(Phase);
            IsWorkingPreparation = false;
            IsWorkingEvaluation  = false;
            StatusMessage = "Séance suspendue. Vous reprendrez exactement ici la prochaine fois.";
            NotifyAllStates();
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
                // Cherche dans toutes les listes
                foreach (var cat in new[] { "hypotheses", "differentiels", "a_eliminer", "vigilance", "questions" })
                {
                    var list = ListForCategory(cat);
                    if (list != null && list.Remove(item)) return;
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
            if (e.PropertyName == nameof(AxisObservationItem.IsChecked))
            {
                try { _phaseService.Save(Phase); } catch { /* meilleur effort */ }
            }
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
            Phase.EtapeCourante = EvaluationStep.Cartographie;
            _phaseService.Save(Phase);

            IsWorkingEvaluation = false;
            StatusMessage = "Étape 2 validée. Étape 3 (Cartographie globale) disponible prochainement.";
            NotifyAllStates();
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
            OnPropertyChanged(nameof(SuspendedMarqueLabel));
            OnPropertyChanged(nameof(SuspendedAgoLabel));
            System.Windows.Application.Current?.Dispatcher.InvokeAsync(
                System.Windows.Input.CommandManager.InvalidateRequerySuggested);
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
