using System;

namespace MedCompanion.Models.Synthesis
{
    /// <summary>
    /// Métadonnées d'une version de Synthèse Globale (pour l'index et l'affichage
    /// d'historique sans avoir à charger tous les fichiers complets).
    ///
    /// Une instance par fichier `synthese_v{N}_{date}.md` archivé.
    /// </summary>
    public class SyntheseGlobaleVersion
    {
        public int      Version          { get; set; }
        public DateTime DateRedaction    { get; set; }
        public DateTime? DateValidation  { get; set; }
        public SyntheseStatut Statut     { get; set; } = SyntheseStatut.Brouillon;
        public string   Psychiatre       { get; set; } = "";
        public string   FilePath         { get; set; } = "";
        public string   FileName         { get; set; } = "";

        public bool IsValidee  => Statut == SyntheseStatut.Validee;
        public bool IsBrouillon => Statut == SyntheseStatut.Brouillon;

        /// <summary>Libellé court affichable (ex: "v2 — validée 15/09/2026").</summary>
        public string DisplayLabel
            => IsValidee && DateValidation.HasValue
                ? $"v{Version} — validée {DateValidation.Value:dd/MM/yyyy}"
                : $"v{Version} — brouillon {DateRedaction:dd/MM/yyyy}";
    }
}
