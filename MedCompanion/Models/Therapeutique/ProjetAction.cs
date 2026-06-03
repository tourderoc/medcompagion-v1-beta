using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MedCompanion.Models.Therapeutique
{
    /// <summary>
    /// Une action structurée d'une section actions du Projet Thérapeutique
    /// (sections 2-5 : Médicale / Psychologique / Développementale / Environnementale).
    /// Identifiée par un GUID stable pour permettre le suivi inter-versions et le
    /// pilotage par Med.
    ///
    /// V1.0 — Édition manuelle uniquement.
    /// V1.3 — Med proposera des transitions de statut à partir des notes récentes.
    /// </summary>
    public class ProjetAction : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        /// <summary>Identifiant stable (GUID) pour retrouver l'action entre versions.</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        private string _libelle = "";
        /// <summary>Libellé court de l'action (ex: "Démarrer méthylphénidate 5 mg").</summary>
        public string Libelle
        {
            get => _libelle;
            set { if (_libelle != value) { _libelle = value ?? ""; OnPropertyChanged(); } }
        }

        private string _description = "";
        /// <summary>Description optionnelle (détail clinique).</summary>
        public string Description
        {
            get => _description;
            set { if (_description != value) { _description = value ?? ""; OnPropertyChanged(); } }
        }

        private ActionStatut _statut = ActionStatut.AVenir;
        public ActionStatut Statut
        {
            get => _statut;
            set
            {
                if (_statut != value)
                {
                    _statut = value;
                    DateDernierStatut = DateTime.Now;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(DateDernierStatut));
                    OnPropertyChanged(nameof(StatutIcon));
                    OnPropertyChanged(nameof(StatutLabel));
                    OnPropertyChanged(nameof(StatutColor));
                }
            }
        }

        /// <summary>Date de la décision (création de l'action). Immuable.</summary>
        public DateTime DateDecision { get; set; } = DateTime.Now;

        /// <summary>Date du dernier changement de statut. Auto-mise à jour.</summary>
        public DateTime DateDernierStatut { get; set; } = DateTime.Now;

        private string _motifDernierChangement = "";
        /// <summary>Motif optionnel du dernier changement de statut.</summary>
        public string MotifDernierChangement
        {
            get => _motifDernierChangement;
            set { if (_motifDernierChangement != value) { _motifDernierChangement = value ?? ""; OnPropertyChanged(); } }
        }

        private string _lienSyntheseSection = "";
        /// <summary>
        /// Clé de la section de la Synthèse Globale qui motive cette action
        /// (hypotheses / enfant / environnement / articulation / conclusion).
        /// Optionnel mais encouragé (V1.4 flag les actions sans justification).
        /// </summary>
        public string LienSyntheseSection
        {
            get => _lienSyntheseSection;
            set { if (_lienSyntheseSection != value) { _lienSyntheseSection = value ?? ""; OnPropertyChanged(); } }
        }

        private string _indicateurReussite = "";
        /// <summary>
        /// Critère mesurable de réussite (qualitatif ou quantitatif).
        /// Ex: "Amélioration attention en classe (cotation Conners)".
        /// </summary>
        public string IndicateurReussite
        {
            get => _indicateurReussite;
            set { if (_indicateurReussite != value) { _indicateurReussite = value ?? ""; OnPropertyChanged(); } }
        }

        // ── Affichage ────────────────────────────────────────────────────────

        public string StatutIcon => Statut switch
        {
            ActionStatut.AVenir    => "⚪",
            ActionStatut.EnCours   => "🟡",
            ActionStatut.Fait      => "✅",
            ActionStatut.Abandonne => "⛔",
            _                      => "⚪"
        };

        public string StatutLabel => Statut switch
        {
            ActionStatut.AVenir    => "À venir",
            ActionStatut.EnCours   => "En cours",
            ActionStatut.Fait      => "Fait",
            ActionStatut.Abandonne => "Abandonné",
            _                      => "À venir"
        };

        public string StatutColor => Statut switch
        {
            ActionStatut.AVenir    => "#95A5A6",
            ActionStatut.EnCours   => "#F39C12",
            ActionStatut.Fait      => "#27AE60",
            ActionStatut.Abandonne => "#7F8C8D",
            _                      => "#95A5A6"
        };
    }
}
