using System;

namespace MedCompanion.Models
{
    /// <summary>
    /// Praticien identifié comme auteur d'un document importé/scanné (bilan, courrier...).
    /// Extrait automatiquement par l'IA depuis l'en-tête/signature du document.
    /// </summary>
    public class Intervenant
    {
        public string Nom { get; set; } = string.Empty;
        public string? Profession { get; set; }
        public string? Adresse { get; set; }
        public string? CodePostal { get; set; }
        public string? Ville { get; set; }
        public string? Telephone { get; set; }
        public string? Email { get; set; }

        /// <summary>Nom du fichier document dont l'intervenant a été extrait.</summary>
        public string? SourceDocument { get; set; }

        /// <summary>Catégorie du document source (bilans, courriers...).</summary>
        public string? SourceCategory { get; set; }

        public DateTime DateAjout { get; set; } = DateTime.Now;
    }
}
