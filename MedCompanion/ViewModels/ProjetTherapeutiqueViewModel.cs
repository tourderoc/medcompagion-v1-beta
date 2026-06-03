using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Models.Therapeutique;
using MedCompanion.Services.Therapeutique;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// V1.0 — ViewModel d'édition manuelle d'un Projet Thérapeutique.
    ///
    /// Architecture parallèle à SyntheseGlobaleViewModel :
    /// - Auto-save debounce 500 ms
    /// - Commandes Valider / Fermer
    /// - Commandes ajout/suppression d'actions structurées par section
    /// - Commande de cycle de statut sur une action (clic = ⚪→🟡→✅→⛔→⚪)
    /// </summary>
    public class ProjetTherapeutiqueViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        private readonly ProjetTherapeutiqueService _service;
        private ProjetTherapeutique? _projet;
        private CancellationTokenSource? _autoSaveCts;

        public ProjetTherapeutiqueViewModel(ProjetTherapeutiqueService service)
        {
            _service = service;

            ValiderCommand    = new RelayCommand(_ => Valider(),                _ => CanValider);
            FermerCommand     = new RelayCommand(_ => FermerSansValider(),      _ => Projet != null);

            AjouterActionMedicaleCommand         = new RelayCommand(_ => AjouterAction("medicale"),         _ => Projet?.IsBrouillon == true);
            AjouterActionPsychologiqueCommand    = new RelayCommand(_ => AjouterAction("psychologique"),    _ => Projet?.IsBrouillon == true);
            AjouterActionDeveloppementaleCommand = new RelayCommand(_ => AjouterAction("developpementale"), _ => Projet?.IsBrouillon == true);
            AjouterActionEnvironnementaleCommand = new RelayCommand(_ => AjouterAction("environnementale"), _ => Projet?.IsBrouillon == true);

            SupprimerActionCommand = new RelayCommand(param => SupprimerAction(param as ProjetAction),
                                                      param => param is ProjetAction && Projet?.IsBrouillon == true);

            CyclerStatutCommand = new RelayCommand(param => CyclerStatut(param as ProjetAction),
                                                   param => param is ProjetAction && Projet?.IsBrouillon == true);
        }

        public ProjetTherapeutique? Projet
        {
            get => _projet;
            set
            {
                if (_projet != value)
                {
                    DetachHandlers();
                    _projet = value;
                    AttachHandlers();
                    OnPropertyChanged();
                    NotifyAll();
                }
            }
        }

        public event Action? Closed;
        public event Action? BrouillonCreated;
        public event Action<string>? ProjetValidated;

        // ── Affichage ────────────────────────────────────────────────────────

        public string TitreVue
            => Projet == null ? ""
             : Projet.IsValidee ? $"🎯 Projet Thérapeutique v{Projet.Version} — validé {Projet.DateValidation:dd/MM/yyyy}"
             : $"🎯 Projet Thérapeutique v{Projet.Version} — brouillon";

        public string StatutLabel
            => Projet == null ? ""
             : Projet.IsValidee ? "Validé (lecture seule)"
             : "Brouillon (édition libre, auto-save)";

        public bool IsReadOnly  => Projet?.IsValidee ?? false;
        public bool IsBrouillon => Projet?.IsBrouillon ?? false;

        public bool IsReevaluationPassee => Projet?.IsReevaluationPassee ?? false;

        private string _statusMessage = "";
        public string StatusMessage
        {
            get => _statusMessage;
            set { if (_statusMessage != value) { _statusMessage = value ?? ""; OnPropertyChanged(); } }
        }

        // ── Commandes ────────────────────────────────────────────────────────

        public ICommand ValiderCommand { get; }
        public ICommand FermerCommand  { get; }

        public ICommand AjouterActionMedicaleCommand         { get; }
        public ICommand AjouterActionPsychologiqueCommand    { get; }
        public ICommand AjouterActionDeveloppementaleCommand { get; }
        public ICommand AjouterActionEnvironnementaleCommand { get; }

        public ICommand SupprimerActionCommand { get; }
        public ICommand CyclerStatutCommand    { get; }

        private bool CanValider
            => Projet != null
            && Projet.IsBrouillon
            && Projet.HasAnyContenu;

        // ── Cycle de vie ─────────────────────────────────────────────────────

        public void OuvrirBrouillonOuCreer(string patientNomComplet, string psychiatre)
        {
            if (string.IsNullOrWhiteSpace(patientNomComplet)) return;

            var brouillonMeta = _service.GetBrouillon(patientNomComplet);
            if (brouillonMeta != null)
            {
                var loaded = _service.Load(brouillonMeta.FilePath);
                if (loaded != null)
                {
                    Projet = loaded;
                    StatusMessage = $"Brouillon v{loaded.Version} repris.";
                    return;
                }
            }

            var derniere = _service.GetDerniereValidee(patientNomComplet);
            var nouvelleVersion = (derniere?.Version ?? 0) + 1;

            var nouveau = new ProjetTherapeutique
            {
                Version           = nouvelleVersion,
                PatientNomComplet = patientNomComplet,
                Psychiatre        = psychiatre,
                Statut            = ProjetStatut.Brouillon,
                DateRedaction     = DateTime.Now,
                VersionPrecedenteFichier = derniere?.FileName
            };
            _service.SaveBrouillon(nouveau);
            Projet = nouveau;
            StatusMessage = $"Nouveau brouillon v{nouvelleVersion} créé.";
            BrouillonCreated?.Invoke();
        }

        private void Valider()
        {
            if (Projet == null) return;
            try
            {
                var patientNom = Projet.PatientNomComplet;
                _service.Validate(Projet);
                StatusMessage = $"✓ Projet Thérapeutique v{Projet.Version} validé.";
                NotifyAll();
                ProjetValidated?.Invoke(patientNom);
                Closed?.Invoke();
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Erreur validation : {ex.Message}";
            }
        }

        private void FermerSansValider()
        {
            DetachHandlers();
            Projet = null;
            Closed?.Invoke();
        }

        // ── Actions structurées ──────────────────────────────────────────────

        private void AjouterAction(string section)
        {
            if (Projet == null || Projet.IsValidee) return;
            var action = new ProjetAction { Libelle = "(nouvelle action)" };
            switch (section)
            {
                case "medicale":         Projet.ActionsMedicales.Add(action); break;
                case "psychologique":    Projet.ActionsPsychologiques.Add(action); break;
                case "developpementale": Projet.ActionsDeveloppementales.Add(action); break;
                case "environnementale": Projet.ActionsEnvironnementales.Add(action); break;
            }
            AttachActionHandlers(action);
            NotifyProgression();
            ScheduleAutoSave();
        }

        private void SupprimerAction(ProjetAction? a)
        {
            if (a == null || Projet == null || Projet.IsValidee) return;
            Projet.ActionsMedicales.Remove(a);
            Projet.ActionsPsychologiques.Remove(a);
            Projet.ActionsDeveloppementales.Remove(a);
            Projet.ActionsEnvironnementales.Remove(a);
            DetachActionHandlers(a);
            NotifyProgression();
            ScheduleAutoSave();
        }

        /// <summary>
        /// Clic sur le chip statut → cycle ⚪ → 🟡 → ✅ → ⛔ → ⚪.
        /// La date est auto-mise à jour dans le setter de Statut.
        /// </summary>
        private void CyclerStatut(ProjetAction? a)
        {
            if (a == null || Projet == null || Projet.IsValidee) return;
            a.Statut = a.Statut switch
            {
                ActionStatut.AVenir    => ActionStatut.EnCours,
                ActionStatut.EnCours   => ActionStatut.Fait,
                ActionStatut.Fait      => ActionStatut.Abandonne,
                ActionStatut.Abandonne => ActionStatut.AVenir,
                _                      => ActionStatut.AVenir
            };
            NotifyProgression();
            ScheduleAutoSave();
        }

        // ── Auto-save debounced ──────────────────────────────────────────────

        private void AttachHandlers()
        {
            if (Projet == null) return;
            Projet.PropertyChanged += OnProjetChanged;
            foreach (var a in Projet.ToutesActions) AttachActionHandlers(a);
            // Collection changes
            Projet.ActionsMedicales.CollectionChanged         += OnActionsCollectionChanged;
            Projet.ActionsPsychologiques.CollectionChanged    += OnActionsCollectionChanged;
            Projet.ActionsDeveloppementales.CollectionChanged += OnActionsCollectionChanged;
            Projet.ActionsEnvironnementales.CollectionChanged += OnActionsCollectionChanged;
        }

        private void DetachHandlers()
        {
            if (Projet == null) return;
            Projet.PropertyChanged -= OnProjetChanged;
            foreach (var a in Projet.ToutesActions) DetachActionHandlers(a);
            Projet.ActionsMedicales.CollectionChanged         -= OnActionsCollectionChanged;
            Projet.ActionsPsychologiques.CollectionChanged    -= OnActionsCollectionChanged;
            Projet.ActionsDeveloppementales.CollectionChanged -= OnActionsCollectionChanged;
            Projet.ActionsEnvironnementales.CollectionChanged -= OnActionsCollectionChanged;
        }

        private void AttachActionHandlers(ProjetAction a) => a.PropertyChanged += OnActionChanged;
        private void DetachActionHandlers(ProjetAction a) => a.PropertyChanged -= OnActionChanged;

        private void OnProjetChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (Projet?.IsValidee == true) return;
            ScheduleAutoSave();
        }

        private void OnActionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (Projet?.IsValidee == true) return;
            ScheduleAutoSave();
            (ValiderCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void OnActionsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (Projet?.IsValidee == true) return;
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
                    if (ct.IsCancellationRequested || Projet == null || Projet.IsValidee) return;
                    _service.SaveBrouillon(Projet);
                }
                catch { /* meilleur effort */ }
            });
        }

        private void NotifyProgression()
        {
            OnPropertyChanged(nameof(Projet));
            if (Projet == null) return;
            // Propager les calculs de progression au binding
            typeof(ProjetTherapeutique).GetMethod("OnPropertyChanged", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            // Force notification des propriétés agrégées
            // Note: le modèle ne notifie pas ses props calculées (ProgressionPct etc.) — on
            // se contente de notifier l'objet entier ici, le binding {Binding Projet.ProgressionPct}
            // sera réévalué.
        }

        private void NotifyAll()
        {
            OnPropertyChanged(nameof(Projet));
            OnPropertyChanged(nameof(TitreVue));
            OnPropertyChanged(nameof(StatutLabel));
            OnPropertyChanged(nameof(IsReadOnly));
            OnPropertyChanged(nameof(IsBrouillon));
            OnPropertyChanged(nameof(IsReevaluationPassee));
            (ValiderCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (FermerCommand  as RelayCommand)?.RaiseCanExecuteChanged();
        }
    }
}
