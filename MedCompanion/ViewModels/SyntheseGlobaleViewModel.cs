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
        private SyntheseGlobale? _synthese;
        private CancellationTokenSource? _autoSaveCts;

        public SyntheseGlobaleViewModel(SyntheseGlobaleService service)
        {
            _service = service;

            ValiderCommand = new RelayCommand(_ => Valider(),         _ => CanValider);
            FermerCommand  = new RelayCommand(_ => FermerSansValider(), _ => Synthese != null);
        }

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
        public void OuvrirBrouillonOuCreer(string patientNomComplet, string psychiatre)
        {
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
