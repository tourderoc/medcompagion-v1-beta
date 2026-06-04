using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MedCompanion.Models.Therapeutique
{
    /// <summary>
    /// V1.4 — Type de signalement de relecture critique d'un Projet Thérapeutique.
    /// Vérifie la cohérence Synthèse ↔ Projet et la qualité opérationnelle des actions.
    /// </summary>
    public enum ProjetFlagType
    {
        /// <summary>Action sans LienSyntheseSection ou pointant vers une section vide.</summary>
        ActionSansJustification,
        /// <summary>Dimension fragile/critique de la Synthèse non couverte par une action.</summary>
        DimensionNonAdressee,
        /// <summary>Une action contredit une autre OU contredit la Synthèse.</summary>
        Contradiction,
        /// <summary>Action sans indicateur de réussite mesurable.</summary>
        ActionSansIndicateur,
        /// <summary>Objectif prioritaire sans action concrète qui l'adresse.</summary>
        ObjectifSansAction,
        /// <summary>Suggestion éditoriale (ton, structure).</summary>
        Suggestion
    }

    public enum ProjetFlagSeverite
    {
        /// <summary>À traiter avant validation (incohérence majeure, dimension critique non adressée).</summary>
        Critique,
        /// <summary>À examiner mais non bloquant (action sans indicateur).</summary>
        Moyenne,
        /// <summary>Information / amélioration possible.</summary>
        Mineure
    }

    /// <summary>
    /// Un flag de relecture critique produit par Med sur un brouillon de Projet
    /// Thérapeutique. Le psy peut le marquer comme traité.
    /// </summary>
    public class ProjetRelectureFlag : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public ProjetFlagType     Type     { get; set; } = ProjetFlagType.Suggestion;
        public ProjetFlagSeverite Severite { get; set; } = ProjetFlagSeverite.Mineure;

        /// <summary>ID de l'action concernée si applicable (sinon vide).</summary>
        public string? ActionId { get; set; }

        /// <summary>Libellé court de l'action ou nom de section ciblée.</summary>
        public string Cible { get; set; } = "";

        public string Detail     { get; set; } = "";
        public string Suggestion { get; set; } = "";

        private bool _traite;
        public bool Traite
        {
            get => _traite;
            set { if (_traite != value) { _traite = value; OnPropertyChanged(); } }
        }

        // ── Affichage ────────────────────────────────────────────────────────

        public string SeveriteLabel => Severite switch
        {
            ProjetFlagSeverite.Critique => "Critique",
            ProjetFlagSeverite.Moyenne  => "Moyenne",
            _                           => "Mineure"
        };

        public string TypeLabel => Type switch
        {
            ProjetFlagType.ActionSansJustification => "Action sans justification",
            ProjetFlagType.DimensionNonAdressee    => "Dimension non adressée",
            ProjetFlagType.Contradiction           => "Contradiction",
            ProjetFlagType.ActionSansIndicateur    => "Action sans indicateur",
            ProjetFlagType.ObjectifSansAction      => "Objectif sans action",
            _                                      => "Suggestion"
        };

        public string SeveriteColor => Severite switch
        {
            ProjetFlagSeverite.Critique => "#C0392B",
            ProjetFlagSeverite.Moyenne  => "#E67E22",
            _                           => "#7F8C8D"
        };

        public string SeveriteIcon => Severite switch
        {
            ProjetFlagSeverite.Critique => "🔴",
            ProjetFlagSeverite.Moyenne  => "🟠",
            _                           => "🟡"
        };
    }
}
