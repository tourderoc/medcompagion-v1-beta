using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MedCompanion.Models.Therapeutique
{
    /// <summary>
    /// V1.3 — Suggestion de transition de statut sur une action du Projet Thérapeutique,
    /// produite par Med à partir des notes récentes et de l'évolution du dossier.
    ///
    /// Exemple : action "ECG préalable" actuellement ⚪ A venir → Med voit dans une note
    /// "ECG réalisé le 30/05, RAS" → propose ✅ Fait avec motif "Confirmé dans note du 30/05".
    /// </summary>
    public class TransitionSuggestion : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        /// <summary>ID de l'action concernée (matching avec ProjetAction.Id).</summary>
        public string ActionId { get; set; } = "";

        /// <summary>Libellé court de l'action (affichage).</summary>
        public string ActionLibelle { get; set; } = "";

        public ActionStatut StatutActuel  { get; set; }
        public ActionStatut StatutPropose { get; set; }

        /// <summary>Justification clinique : ce que Med a vu dans le dossier qui motive la transition.</summary>
        public string Justification { get; set; } = "";

        /// <summary>Source (ex: "Note du 30/05/2026", "Évaluation clôturée 12/04").</summary>
        public string Source { get; set; } = "";

        private bool _traite;
        /// <summary>True si le psy a accepté OU rejeté cette suggestion.</summary>
        public bool Traite
        {
            get => _traite;
            set { if (_traite != value) { _traite = value; OnPropertyChanged(); } }
        }

        // ── Affichage ────────────────────────────────────────────────────────

        public string StatutActuelIcon  => IconForStatut(StatutActuel);
        public string StatutProposeIcon => IconForStatut(StatutPropose);
        public string StatutActuelLabel  => LabelForStatut(StatutActuel);
        public string StatutProposeLabel => LabelForStatut(StatutPropose);
        public string TransitionLabel    => $"{StatutActuelIcon} {StatutActuelLabel} → {StatutProposeIcon} {StatutProposeLabel}";

        private static string IconForStatut(ActionStatut s) => s switch
        {
            ActionStatut.AVenir    => "⚪",
            ActionStatut.EnCours   => "🟡",
            ActionStatut.Fait      => "✅",
            ActionStatut.Abandonne => "⛔",
            _                      => "⚪"
        };

        private static string LabelForStatut(ActionStatut s) => s switch
        {
            ActionStatut.AVenir    => "À venir",
            ActionStatut.EnCours   => "En cours",
            ActionStatut.Fait      => "Fait",
            ActionStatut.Abandonne => "Abandonné",
            _                      => "À venir"
        };
    }
}
