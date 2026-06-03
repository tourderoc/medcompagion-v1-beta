using System;

namespace MedCompanion.Models.Therapeutique
{
    /// <summary>
    /// Métadonnées d'une version de Projet Thérapeutique pour l'index et l'affichage
    /// de l'historique sans charger le contenu complet.
    /// </summary>
    public class ProjetTherapeutiqueVersion
    {
        public int      Version          { get; set; }
        public DateTime DateRedaction    { get; set; }
        public DateTime? DateValidation  { get; set; }
        public ProjetStatut Statut       { get; set; } = ProjetStatut.Brouillon;
        public string   Psychiatre       { get; set; } = "";
        public string   FilePath         { get; set; } = "";
        public string   FileName         { get; set; } = "";
        public DateTime? DateReevaluationPrevue { get; set; }

        public bool IsValidee  => Statut == ProjetStatut.Validee;
        public bool IsBrouillon => Statut == ProjetStatut.Brouillon;
        public bool IsReevaluationPassee
            => DateReevaluationPrevue.HasValue && DateReevaluationPrevue.Value.Date < DateTime.Now.Date;

        public string DisplayLabel
            => IsValidee && DateValidation.HasValue
                ? $"v{Version} — validé {DateValidation.Value:dd/MM/yyyy}"
                : $"v{Version} — brouillon {DateRedaction:dd/MM/yyyy}";
    }
}
