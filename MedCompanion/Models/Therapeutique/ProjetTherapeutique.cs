using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace MedCompanion.Models.Therapeutique
{
    /// <summary>
    /// Un Projet Thérapeutique = document structuré, versionné, qui décrit le plan
    /// d'action thérapeutique pour un patient à partir de sa Synthèse Globale validée.
    ///
    /// Architecture :
    /// - 4 sections texte libre (objectifs, ressources, réévaluation, co-construction)
    /// - 4 sections d'actions structurées (médicales, psychologiques, développementales,
    ///   environnementales) — chaque action porte un statut (⚪🟡✅⛔) pilier pour Med
    /// - Lien explicite vers la Synthèse Globale qui a motivé ce projet
    /// - Date de réévaluation prévue (alerte si dépassée)
    ///
    /// Versions : v1, v2... immuables après validation. Patches incrémentaux entre versions.
    /// </summary>
    public class ProjetTherapeutique : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        // ── Métadonnées ──────────────────────────────────────────────────────

        public int Version { get; set; } = 1;
        public string PatientNomComplet { get; set; } = "";
        public DateTime DateRedaction { get; set; } = DateTime.Now;
        public DateTime? DateValidation { get; set; }
        public string Psychiatre { get; set; } = "";

        private ProjetStatut _statut = ProjetStatut.Brouillon;
        public ProjetStatut Statut
        {
            get => _statut;
            set
            {
                if (_statut != value)
                {
                    _statut = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsValidee));
                    OnPropertyChanged(nameof(IsBrouillon));
                }
            }
        }
        public bool IsValidee   => _statut == ProjetStatut.Validee;
        public bool IsBrouillon => _statut == ProjetStatut.Brouillon;

        /// <summary>Fichier de la version précédente (chaînage v1 → v2 → v3).</summary>
        public string? VersionPrecedenteFichier { get; set; }

        /// <summary>
        /// Référence au fichier de Synthèse Globale validée qui a motivé ce projet.
        /// Permet à Med de vérifier la cohérence Synthèse↔Projet (V1.4).
        /// </summary>
        public string? SyntheseGlobaleSourceFichier { get; set; }

        private DateTime? _dateReevaluationPrevue;
        /// <summary>Date globale de réévaluation. Si dépassée → badge ⚠️ Réévaluation passée.</summary>
        public DateTime? DateReevaluationPrevue
        {
            get => _dateReevaluationPrevue;
            set { if (_dateReevaluationPrevue != value) { _dateReevaluationPrevue = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsReevaluationPassee)); } }
        }

        public bool IsReevaluationPassee
            => _dateReevaluationPrevue.HasValue && _dateReevaluationPrevue.Value.Date < DateTime.Now.Date;

        public string FilePath { get; set; } = "";

        // ── Sections texte libre (sections 1, 6, 7 + co-construction) ────────

        private string _objectifsPrioritaires = "";
        public string ObjectifsPrioritaires
        {
            get => _objectifsPrioritaires;
            set { if (_objectifsPrioritaires != value) { _objectifsPrioritaires = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(HasAnyContenu)); } }
        }

        private string _ressourcesASoutenir = "";
        public string RessourcesASoutenir
        {
            get => _ressourcesASoutenir;
            set { if (_ressourcesASoutenir != value) { _ressourcesASoutenir = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(HasAnyContenu)); } }
        }

        private string _reevaluationChecklist = "";
        /// <summary>Checklist de ce qu'on vérifie spécifiquement à la réévaluation.</summary>
        public string ReevaluationChecklist
        {
            get => _reevaluationChecklist;
            set { if (_reevaluationChecklist != value) { _reevaluationChecklist = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(HasAnyContenu)); } }
        }

        private string _coConstructionFamille = "";
        /// <summary>Section transverse : accord et participation de la famille.</summary>
        public string CoConstructionFamille
        {
            get => _coConstructionFamille;
            set { if (_coConstructionFamille != value) { _coConstructionFamille = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(HasAnyContenu)); } }
        }

        // ── Sections actions structurées (sections 2-5) ──────────────────────

        public ObservableCollection<ProjetAction> ActionsMedicales         { get; } = new();
        public ObservableCollection<ProjetAction> ActionsPsychologiques    { get; } = new();
        public ObservableCollection<ProjetAction> ActionsDeveloppementales { get; } = new();
        public ObservableCollection<ProjetAction> ActionsEnvironnementales { get; } = new();

        public IEnumerable<ProjetAction> ToutesActions
            => ActionsMedicales
                .Concat(ActionsPsychologiques)
                .Concat(ActionsDeveloppementales)
                .Concat(ActionsEnvironnementales);

        /// <summary>True si au moins une section a du contenu (action ou texte).</summary>
        public bool HasAnyContenu
            => !string.IsNullOrWhiteSpace(_objectifsPrioritaires)
            || !string.IsNullOrWhiteSpace(_ressourcesASoutenir)
            || !string.IsNullOrWhiteSpace(_reevaluationChecklist)
            || !string.IsNullOrWhiteSpace(_coConstructionFamille)
            || ToutesActions.Any();

        // ── Progression (pilier UX V1.3) ─────────────────────────────────────

        /// <summary>Pourcentage d'actions au statut Fait (sur le total d'actions non abandonnées).</summary>
        public int ProgressionPct
        {
            get
            {
                var actives = ToutesActions.Where(a => a.Statut != ActionStatut.Abandonne).ToList();
                if (actives.Count == 0) return 0;
                var faites = actives.Count(a => a.Statut == ActionStatut.Fait);
                return (int)Math.Round(100.0 * faites / actives.Count);
            }
        }

        public int NbActionsAVenir    => ToutesActions.Count(a => a.Statut == ActionStatut.AVenir);
        public int NbActionsEnCours   => ToutesActions.Count(a => a.Statut == ActionStatut.EnCours);
        public int NbActionsFaites    => ToutesActions.Count(a => a.Statut == ActionStatut.Fait);
        public int NbActionsAbandon   => ToutesActions.Count(a => a.Statut == ActionStatut.Abandonne);
    }
}
