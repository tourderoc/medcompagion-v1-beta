using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Models.Synthesis;
using MedCompanion.Services.Synthesis;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// ViewModel d'édition d'une Synthèse Globale (V0.1 — édition manuelle).
    /// Gère l'auto-save du brouillon avec debounce 500 ms, la validation et la
    /// fermeture sans validation.
    ///
    /// Une seule instance partagée à la fois dans ConsultationModeViewModel —
    /// nettoyée à la fermeture du panneau.
    /// </summary>
    public class SyntheseGlobaleViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        private readonly SyntheseGlobaleService _service;
        private readonly SyntheseGlobaleSuggesterService? _suggester;
        private SyntheseGlobale? _synthese;
        private CancellationTokenSource? _autoSaveCts;
        private string _patientDirectoryPath = "";

        public SyntheseGlobaleViewModel(SyntheseGlobaleService service, SyntheseGlobaleSuggesterService? suggester = null)
        {
            _service   = service;
            _suggester = suggester;

            ValiderCommand   = new RelayCommand(_ => Valider(),              _ => CanValider);
            FermerCommand    = new RelayCommand(_ => FermerSansValider(),    _ => Synthese != null);
            ProposerCommand  = new RelayCommand(async _ => await ProposerAsync(),
                                                _ => CanProposer);
        }

        public ICommand ProposerCommand { get; }

        private bool _isProposerEnCours;
        public bool IsProposerEnCours
        {
            get => _isProposerEnCours;
            private set { if (_isProposerEnCours != value) { _isProposerEnCours = value; OnPropertyChanged();
                (ProposerCommand as RelayCommand)?.RaiseCanExecuteChanged(); } }
        }

        private bool CanProposer
            => _suggester != null
            && Synthese != null
            && Synthese.IsBrouillon
            && !IsProposerEnCours;

        public bool ProposerVisible => _suggester != null;

        /// <summary>Synthèse en cours d'édition (brouillon).</summary>
        public SyntheseGlobale? Synthese
        {
            get => _synthese;
            set
            {
                if (_synthese != value)
                {
                    DetachSectionHandlers();
                    _synthese = value;
                    AttachSectionHandlers();
                    OnPropertyChanged();
                    NotifyAll();
                }
            }
        }

        /// <summary>Notifié à la fermeture du panneau (validation OU annulation).</summary>
        public event Action? Closed;

        /// <summary>Notifié à la création d'un nouveau brouillon (pour rafraîchir la frise).</summary>
        public event Action? BrouillonCreated;

        // ── Affichage ────────────────────────────────────────────────────────

        public string TitreVue
            => Synthese == null
                ? ""
                : Synthese.IsValidee
                    ? $"🧭 Synthèse Globale v{Synthese.Version} — validée {Synthese.DateValidation:dd/MM/yyyy}"
                    : $"🧭 Synthèse Globale v{Synthese.Version} — brouillon";

        public string StatutLabel
            => Synthese == null ? ""
             : Synthese.IsValidee ? "Validée (lecture seule)"
             : "Brouillon (édition libre, auto-save)";

        public bool IsReadOnly => Synthese?.IsValidee ?? false;
        public bool IsBrouillon => Synthese?.IsBrouillon ?? false;

        private string _statusMessage = "";
        public string StatusMessage
        {
            get => _statusMessage;
            set { if (_statusMessage != value) { _statusMessage = value ?? ""; OnPropertyChanged(); } }
        }

        // ── Commandes ────────────────────────────────────────────────────────

        public ICommand ValiderCommand { get; }
        public ICommand FermerCommand  { get; }

        private bool CanValider
            => Synthese != null && Synthese.IsBrouillon && Synthese.HasAnyContenu;

        // ── Cycle de vie ─────────────────────────────────────────────────────

        /// <summary>
        /// Ouvre une synthèse pour un patient : reprend le brouillon courant s'il existe,
        /// sinon crée un nouveau brouillon vide v(N+1) où N = dernière version validée.
        /// </summary>
        public void OuvrirBrouillonOuCreer(string patientNomComplet, string psychiatre, string patientDirectoryPath = "")
        {
            _patientDirectoryPath = patientDirectoryPath ?? "";
            if (string.IsNullOrWhiteSpace(patientNomComplet)) return;

            var brouillonMeta = _service.GetBrouillon(patientNomComplet);
            if (brouillonMeta != null)
            {
                var loaded = _service.Load(brouillonMeta.FilePath);
                if (loaded != null)
                {
                    Synthese = loaded;
                    StatusMessage = $"Brouillon v{loaded.Version} repris.";
                    return;
                }
            }

            // Nouveau brouillon : v(N+1) où N = dernière validée
            var derniere = _service.GetDerniereValidee(patientNomComplet);
            var nouvelleVersion = (derniere?.Version ?? 0) + 1;

            var nouveau = new SyntheseGlobale
            {
                Version           = nouvelleVersion,
                PatientNomComplet = patientNomComplet,
                Psychiatre        = psychiatre,
                Statut            = SyntheseStatut.Brouillon,
                DateRedaction     = DateTime.Now,
                VersionPrecedenteFichier = derniere?.FileName
            };
            _service.SaveBrouillon(nouveau);
            Synthese = nouveau;
            StatusMessage = $"Nouveau brouillon v{nouvelleVersion} créé.";
            BrouillonCreated?.Invoke();
        }

        /// <summary>
        /// Demande à Med de proposer une v1 à partir du dossier complet (V0.2 — génération
        /// initiale). REMPLACE le contenu du brouillon. Le psy peut ensuite éditer librement.
        /// </summary>
        private async Task ProposerAsync()
        {
            if (_suggester == null || Synthese == null || Synthese.IsValidee) return;

            // Confirmation si du contenu existe déjà
            if (Synthese.HasAnyContenu)
            {
                var r = System.Windows.MessageBox.Show(
                    "Le brouillon contient déjà du contenu. La proposition de Med va le REMPLACER.\n\nContinuer ?",
                    "Proposer (Med)",
                    System.Windows.MessageBoxButton.YesNo,
                    System.Windows.MessageBoxImage.Warning);
                if (r != System.Windows.MessageBoxResult.Yes) return;
            }

            IsProposerEnCours = true;
            StatusMessage = "⏳ Med analyse le dossier complet...";

            try
            {
                var (ok, sug, error) = await _suggester.GenerateInitialAsync(
                    Synthese.PatientNomComplet, _patientDirectoryPath, CancellationToken.None);
                if (!ok || sug == null)
                {
                    StatusMessage = $"❌ Proposition impossible : {error ?? "réponse vide"}";
                    return;
                }

                Synthese.Hypotheses.Contenu    = sug.Hypotheses;
                Synthese.Enfant.Contenu        = sug.Enfant;
                Synthese.Environnement.Contenu = sug.Environnement;
                Synthese.Articulation.Contenu  = sug.Articulation;
                Synthese.Conclusion.Contenu    = sug.Conclusion;
                // Évolution reste vide en v1 (se remplit à partir de v2 en mode patch)

                Synthese.SourcesEvaluations    = sug.EvaluationsSources;
                Synthese.SourcesNombreNotes    = sug.NotesUtilisees;

                _service.SaveBrouillon(Synthese);
                StatusMessage = $"✓ Proposition Med insérée — {sug.EvaluationsSources.Count} évaluation(s) consultée(s). Éditez librement.";
                (ValiderCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Erreur : {ex.Message}";
            }
            finally
            {
                IsProposerEnCours = false;
            }
        }

        private void Valider()
        {
            if (Synthese == null) return;
            try
            {
                _service.Validate(Synthese);
                StatusMessage = $"✓ Synthèse v{Synthese.Version} validée — nouvelle source de vérité.";
                NotifyAll();
                Closed?.Invoke();
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Erreur validation : {ex.Message}";
            }
        }

        private void FermerSansValider()
        {
            DetachSectionHandlers();
            Synthese = null;
            Closed?.Invoke();
        }

        // ── Auto-save debounced ──────────────────────────────────────────────

        private void AttachSectionHandlers()
        {
            if (Synthese == null) return;
            foreach (var s in Synthese.Sections)
                s.PropertyChanged += OnSectionChanged;
        }

        private void DetachSectionHandlers()
        {
            if (Synthese == null) return;
            foreach (var s in Synthese.Sections)
                s.PropertyChanged -= OnSectionChanged;
        }

        private void OnSectionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(SyntheseSection.Contenu)) return;
            if (Synthese == null || Synthese.IsValidee) return;
            ScheduleAutoSave();
            (ValiderCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void ScheduleAutoSave()
        {
            _autoSaveCts?.Cancel();
            _autoSaveCts = new CancellationTokenSource();
            var ct = _autoSaveCts.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(500, ct);
                    if (ct.IsCancellationRequested || Synthese == null || Synthese.IsValidee) return;
                    _service.SaveBrouillon(Synthese);
                }
                catch { /* meilleur effort */ }
            });
        }

        private void NotifyAll()
        {
            OnPropertyChanged(nameof(Synthese));
            OnPropertyChanged(nameof(TitreVue));
            OnPropertyChanged(nameof(StatutLabel));
            OnPropertyChanged(nameof(IsReadOnly));
            OnPropertyChanged(nameof(IsBrouillon));
            (ValiderCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (FermerCommand  as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }
}
