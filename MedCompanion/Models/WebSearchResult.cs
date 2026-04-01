using System;
using System.Collections.Generic;

namespace MedCompanion.Models
{
    /// <summary>
    /// Résultat d'une recherche web effectuée par le Sub-Agent Web
    /// </summary>
    public class WebSearchResult
    {
        /// <summary>
        /// Synthèse des résultats de recherche
        /// </summary>
        public string Summary { get; set; } = string.Empty;

        /// <summary>
        /// Points clés extraits des résultats
        /// </summary>
        public List<string> KeyPoints { get; set; } = new();

        /// <summary>
        /// Sources citées dans la recherche
        /// </summary>
        public List<WebSource> Sources { get; set; } = new();

        /// <summary>
        /// Niveau de confiance basé sur la concordance des sources
        /// Valeurs: "high", "medium", "low"
        /// </summary>
        public string Confidence { get; set; } = "medium";

        /// <summary>
        /// Requête originale de l'utilisateur
        /// </summary>
        public string RawQuery { get; set; } = string.Empty;

        /// <summary>
        /// Date et heure de la recherche
        /// </summary>
        public DateTime SearchedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Formate le résultat pour injection dans le contexte de Med
        /// </summary>
        public string ToContextString()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"**Résultats de recherche pour:** {RawQuery}");
            sb.AppendLine();
            sb.AppendLine("**Synthèse:**");
            sb.AppendLine(Summary);
            sb.AppendLine();

            if (KeyPoints.Count > 0)
            {
                sb.AppendLine("**Points clés:**");
                foreach (var point in KeyPoints)
                {
                    sb.AppendLine($"• {point}");
                }
                sb.AppendLine();
            }

            if (Sources.Count > 0)
            {
                sb.AppendLine("**Sources:**");
                foreach (var source in Sources)
                {
                    sb.AppendLine($"• [{source.Title}]({source.Url})");
                }
            }

            sb.AppendLine($"\n_Confiance: {Confidence} | Recherché le {SearchedAt:dd/MM/yyyy HH:mm}_");

            return sb.ToString();
        }
    }

    /// <summary>
    /// Source web citée dans les résultats de recherche
    /// </summary>
    public class WebSource
    {
        /// <summary>
        /// Titre de la page
        /// </summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// URL de la source
        /// </summary>
        public string Url { get; set; } = string.Empty;

        /// <summary>
        /// Extrait pertinent de la page
        /// </summary>
        public string Snippet { get; set; } = string.Empty;

        /// <summary>
        /// Contenu complet de la page (si fetch effectué)
        /// </summary>
        public string? FullContent { get; set; }
    }
}
