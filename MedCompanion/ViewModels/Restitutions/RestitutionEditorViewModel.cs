using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Models.Restitutions;
using MedCompanion.Services.Restitutions;

namespace MedCompanion.ViewModels.Restitutions
{
    public class RestitutionBlocViewModel : INotifyPropertyChanged
    {
        public RestitutionBloc Model { get; }

        public string Title => Model.Titre;
        public string SectionType => Model.Key;

        public string Contenu
        {
            get => Model.ContenuValide;
            set
            {
                if (Model.ContenuValide != value)
                {
                    Model.ContenuValide = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isGenerating;
        public bool IsGenerating
        {
            get => _isGenerating;
            set
            {
                if (_isGenerating != value)
                {
                    _isGenerating = value;
                    OnPropertyChanged();
                    (GenerateCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public ICommand GenerateCommand { get; }

        public RestitutionBlocViewModel(RestitutionBloc model, Func<RestitutionBlocViewModel, Task> generateAction)
        {
            Model = model;
            GenerateCommand = new RelayCommand(async _ => await generateAction(this), _ => !IsGenerating);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RestitutionEditorViewModel : INotifyPropertyChanged
    {
        private readonly RestitutionService _restitutionService;
        private readonly RestitutionSuggesterService _suggesterService;
        private readonly DossierReaderService _dossierReader;
        private readonly RestitutionHtmlPreviewService? _previewService;
        private readonly string _patientName;
        private DossierRestitutionInitial _dossier;

        // Debounce des refreshs de la preview HTML quand le médecin tape.
        private System.Windows.Threading.DispatcherTimer? _previewDebounceTimer;

        // CTS pour annulation : créé à chaque lancement de GenerateAll, annulé via StopCommand.
        private CancellationTokenSource? _generationCts;

        // DossierReading lu en début de GenerateAll, partagé entre les 8 blocs pour cohérence.
        private DossierReading? _currentReading;

        public ObservableCollection<RestitutionBlocViewModel> Blocs { get; } = new();

        public string PatientName => _patientName;

        private string _statusMessage = "";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        private bool _isGeneratingAll;
        /// <summary>True pendant le préremplissage séquentiel des 8 blocs. Pilote l'affichage du bouton Stop.</summary>
        public bool IsGeneratingAll
        {
            get => _isGeneratingAll;
            set
            {
                if (_isGeneratingAll != value)
                {
                    _isGeneratingAll = value;
                    OnPropertyChanged();
                    (GenerateAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (StopGenerationCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private int _progressIndex;
        /// <summary>Numéro du bloc en cours (1-based) pendant le préremplissage. 0 quand inactif.</summary>
        public int ProgressIndex
        {
            get => _progressIndex;
            private set { if (_progressIndex != value) { _progressIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressLabel)); } }
        }

        public int ProgressTotal => Blocs.Count;

        public string ProgressLabel
            => IsGeneratingAll && ProgressIndex > 0
                ? $"Bloc {ProgressIndex}/{ProgressTotal}"
                : "";

        public ICommand SaveCommand            { get; }
        public ICommand GenerateAllCommand     { get; }
        public ICommand StopGenerationCommand  { get; }
        public ICommand CloseCommand           { get; }

        public event Action? RequestClose;

        /// <summary>
        /// Notifié quand l'aperçu HTML doit être rafraîchi (à la fin du debounce 500 ms après
        /// la dernière édition). Le code-behind du View s'y abonne pour rafraîchir le WebView2.
        /// </summary>
        public event Action? PreviewRefreshRequested;

        public RestitutionEditorViewModel(
            DossierRestitutionInitial dossier,
            string patientName,
            RestitutionService restitutionService,
            RestitutionSuggesterService suggesterService,
            DossierReaderService dossierReader,
            RestitutionHtmlPreviewService? previewService = null)
        {
            _dossier            = dossier;
            _patientName        = patientName;
            _restitutionService = restitutionService;
            _suggesterService   = suggesterService;
            _dossierReader      = dossierReader;
            _previewService     = previewService;

            foreach (var bloc in _dossier.Blocs)
            {
                var vm = new RestitutionBlocViewModel(bloc, GenerateBlocAsync);
                // Quand le médecin tape dans un bloc, on déclenche un refresh de l'aperçu (debounce).
                vm.PropertyChanged += OnBlocPropertyChanged;
                Blocs.Add(vm);
            }

            SaveCommand           = new RelayCommand(async _ => await SaveAsync());
            GenerateAllCommand    = new RelayCommand(async _ => await GenerateAllAsync(), _ => !IsGeneratingAll);
            StopGenerationCommand = new RelayCommand(_ => StopGeneration(),               _ =>  IsGeneratingAll);
            CloseCommand          = new RelayCommand(_ => RequestClose?.Invoke());
        }

        /// <summary>
        /// Construit le HTML d'aperçu complet du dossier en l'état actuel. Appelée par le
        /// code-behind du View pour alimenter le WebView2.
        /// </summary>
        public string BuildPreviewHtml()
            => _previewService?.BuildPreviewHtml(_dossier, _patientName) ?? "<html><body><p>Aperçu indisponible.</p></body></html>";

        private void OnBlocPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(RestitutionBlocViewModel.Contenu)) return;

            // Debounce 500ms : on évite de re-rendre à chaque touche tapée par le médecin.
            if (_previewDebounceTimer == null)
            {
                _previewDebounceTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                _previewDebounceTimer.Tick += (s, _) =>
                {
                    _previewDebounceTimer.Stop();
                    PreviewRefreshRequested?.Invoke();
                };
            }
            _previewDebounceTimer.Stop();
            _previewDebounceTimer.Start();
        }

        // ── Génération d'un seul bloc (déclenchée par le bouton Suggérer du bloc) ──

        private async Task GenerateBlocAsync(RestitutionBlocViewModel blocVm)
        {
            blocVm.IsGenerating = true;
            try
            {
                _currentReading ??= await _dossierReader.ReadAsync(_patientName);
                var ct = _generationCts?.Token ?? CancellationToken.None;

                // Certains blocs sont composites : ils sont produits en plusieurs appels LLM
                // séquentiels (un par sous-section) pour fiabiliser le rendu et respecter les
                // limites de tokens. On les achemine vers la méthode progressive idoine.
                switch (blocVm.Model.Key)
                {
                    case "couverture":
                    {
                        // Couverture déterministe (identité, scolarité, dates) + petit appel LLM
                        // en fallback uniquement pour le champ « Motif de consultation » quand
                        // les notes ne sont pas structurées par sections labellisées.
                        StatusMessage = "✍ Couverture — extraction des informations...";
                        var content = await _suggesterService.BuildCouvertureFromDataAsync(_currentReading!, ct);
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            blocVm.Contenu = content;
                            await SaveAsync();
                            StatusMessage = "✓ Couverture renseignée depuis le dossier.";
                        }
                        else
                            StatusMessage = "⚠ Couverture : données insuffisantes — remplissez manuellement.";
                        break;
                    }

                    case "restitution_1page":
                        await RunProgressiveAsync(blocVm, "Restitution parents", 6,
                            (cb, c) => _suggesterService.SuggestRestitution1PageProgressiveAsync(_currentReading!, cb, c), ct);
                        break;

                    case "patient_contexte_familial":
                        await RunProgressiveAsync(blocVm, "Contexte familial", 6,
                            (cb, c) => _suggesterService.SuggestContexteFamilialProgressiveAsync(_currentReading!, cb, c), ct);
                        break;

                    case "patient_antecedents":
                        await RunProgressiveAsync(blocVm, "Antécédents", 6,
                            (cb, c) => _suggesterService.SuggestAntecedentsProgressiveAsync(_currentReading!, cb, c), ct);
                        break;

                    case "patient_situation_actuelle":
                        await RunProgressiveAsync(blocVm, "Situation actuelle", 5,
                            (cb, c) => _suggesterService.SuggestSituationActuelleProgressiveAsync(_currentReading!, cb, c), ct);
                        break;

                    case "carto_enfant":
                        // V0.2 : génération sphère 1 (Attachement) uniquement. Le total
                        // de sections croît au fur et à mesure qu'on câble les sphères 2-8.
                        await RunProgressiveAsync(blocVm, "Cartographie de l'enfant", 1,
                            (cb, c) => _suggesterService.SuggestCartoEnfantProgressiveAsync(_currentReading!, cb, c), ct);
                        break;

                    default:
                    {
                        var result = await _suggesterService.PrefillBlocAsync(blocVm.Model, _currentReading, ct);
                        if (!result.Suggestion.StartsWith("(Erreur"))
                        {
                            blocVm.Contenu = result.Suggestion;
                            await SaveAsync();
                        }
                        else
                        {
                            StatusMessage = $"Erreur gén. {blocVm.Title} : {result.Suggestion}";
                        }
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = $"⏸ Génération du bloc « {blocVm.Title} » annulée.";
            }
            finally
            {
                blocVm.IsGenerating = false;
            }
        }

        /// <summary>
        /// Exécute une génération progressive (multi-LLM séquentielles) sur un bloc,
        /// en remettant à zéro le contenu puis en rafraîchissant l'UI à chaque section
        /// produite. Met aussi à jour le statut "section N/total".
        /// </summary>
        private async Task RunProgressiveAsync(
            RestitutionBlocViewModel blocVm,
            string label,
            int totalSections,
            Func<Action<string>, CancellationToken, Task> runner,
            CancellationToken ct)
        {
            blocVm.Contenu = "";
            int done = 0;
            StatusMessage = $"✍ {label} — section 1/{totalSections}...";

            await runner(accumulated =>
            {
                done++;
                blocVm.Contenu = accumulated;
                StatusMessage = done < totalSections
                    ? $"✍ {label} — section {done + 1}/{totalSections}..."
                    : $"✓ {label} complète.";
            }, ct);

            if (!string.IsNullOrWhiteSpace(blocVm.Contenu))
                await SaveAsync();
        }

        // ── Génération séquentielle de tous les blocs ───────────────────────

        private async Task GenerateAllAsync()
        {
            if (IsGeneratingAll) return;

            _generationCts?.Dispose();
            _generationCts = new CancellationTokenSource();
            var ct = _generationCts.Token;

            IsGeneratingAll = true;
            ProgressIndex   = 0;

            try
            {
                StatusMessage = "📖 Lecture du dossier patient...";
                _currentReading = await _dossierReader.ReadAsync(_patientName);

                int i = 0;
                foreach (var blocVm in Blocs)
                {
                    i++;
                    if (ct.IsCancellationRequested) break;

                    // Ne pas écraser un bloc déjà rempli (le médecin l'a peut-être édité à la main).
                    if (!string.IsNullOrWhiteSpace(blocVm.Contenu)) continue;

                    ProgressIndex = i;
                    StatusMessage = $"✍ Bloc {i}/{Blocs.Count} — {blocVm.Title}...";
                    blocVm.IsGenerating = true;

                    try
                    {
                        // Blocs composites : on délègue à la méthode progressive qui en interne
                        // fait N appels LLM séquentiels. Garantit que les sous-sections sont
                        // toutes présentes et fiables (chacune dans sa propre fenêtre de tokens).
                        switch (blocVm.Model.Key)
                        {
                            case "couverture":
                            {
                                // Voir GenerateBlocAsync : déterministe + LLM-fallback motif uniquement.
                                var content = await _suggesterService.BuildCouvertureFromDataAsync(_currentReading!, ct);
                                if (!string.IsNullOrWhiteSpace(content))
                                {
                                    blocVm.Contenu = content;
                                    await SaveAsync();
                                }
                                else
                                    StatusMessage = "⚠ Couverture : données insuffisantes — remplissez manuellement.";
                                break;
                            }

                            case "restitution_1page":
                                await RunProgressiveAsync(blocVm, "Restitution parents", 6,
                                    (cb, c) => _suggesterService.SuggestRestitution1PageProgressiveAsync(_currentReading, cb, c), ct);
                                break;

                            case "patient_contexte_familial":
                                await RunProgressiveAsync(blocVm, "Contexte familial", 6,
                                    (cb, c) => _suggesterService.SuggestContexteFamilialProgressiveAsync(_currentReading, cb, c), ct);
                                break;

                            case "patient_antecedents":
                                await RunProgressiveAsync(blocVm, "Antécédents", 6,
                                    (cb, c) => _suggesterService.SuggestAntecedentsProgressiveAsync(_currentReading, cb, c), ct);
                                break;

                            case "patient_situation_actuelle":
                                await RunProgressiveAsync(blocVm, "Situation actuelle", 5,
                                    (cb, c) => _suggesterService.SuggestSituationActuelleProgressiveAsync(_currentReading, cb, c), ct);
                                break;

                            case "carto_enfant":
                                await RunProgressiveAsync(blocVm, "Cartographie de l'enfant", 1,
                                    (cb, c) => _suggesterService.SuggestCartoEnfantProgressiveAsync(_currentReading, cb, c), ct);
                                break;

                            default:
                            {
                                var result = await _suggesterService.PrefillBlocAsync(blocVm.Model, _currentReading, ct);
                                if (ct.IsCancellationRequested) break;

                                if (!result.Suggestion.StartsWith("(Erreur"))
                                {
                                    blocVm.Contenu = result.Suggestion;
                                    await SaveAsync();  // auto-save après chaque bloc → reprise possible
                                }
                                else
                                {
                                    StatusMessage = $"⚠ Bloc {i} échoué : {result.Suggestion}. Passage au suivant.";
                                }
                                break;
                            }
                        }
                    }
                    finally
                    {
                        blocVm.IsGenerating = false;
                    }
                }

                StatusMessage = ct.IsCancellationRequested
                    ? $"⏸ Arrêté au bloc {ProgressIndex}/{Blocs.Count}. Les blocs déjà rédigés sont sauvegardés."
                    : "✓ Préremplissage terminé. À vous de relire et ajuster.";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = $"⏸ Génération annulée au bloc {ProgressIndex}/{Blocs.Count}.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Erreur : {ex.Message}";
            }
            finally
            {
                IsGeneratingAll = false;
                ProgressIndex   = 0;
            }
        }

        private void StopGeneration()
        {
            try { _generationCts?.Cancel(); }
            catch { /* meilleur effort */ }
            StatusMessage = "⏸ Arrêt demandé...";
        }

        // ── Sauvegarde brouillon ────────────────────────────────────────────

        private async Task SaveAsync()
        {
            try
            {
                await Task.Run(() => _restitutionService.SaveBrouillon(_dossier));
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur sauvegarde : {ex.Message}";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
