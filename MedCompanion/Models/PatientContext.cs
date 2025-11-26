using System.Collections.Generic;
using System.Text.RegularExpressions;
using MedCompanion.Services;

namespace MedCompanion.Models
{
    /// <summary>
    /// Contexte patient pour enrichir l'analyse IA des demandes de courriers
    /// </summary>
    public class PatientContext
    {
        public string NomComplet { get; set; } = "";
        public int? Age { get; set; }
        public string Sexe { get; set; } = "";
        public string DateNaissance { get; set; } = "";
        
        /// <summary>
        /// Liste des 3 notes les plus récentes (preview)
        /// </summary>
        public List<string> NotesRecentes { get; set; } = new List<string>();
        
        /// <summary>
        /// Diagnostics ou troubles mentionnés dans les notes
        /// </summary>
        public List<string> DiagnosticsConnus { get; set; } = new List<string>();

        /// <summary>
        /// Génère une représentation textuelle du contexte pour injection dans les prompts
        /// </summary>
        /// <param name="pseudonym">Pseudonyme optionnel pour anonymisation</param>
        /// <param name="anonContext">Contexte d'anonymisation pour anonymiser aussi le contenu</param>
        public string ToPromptText(string? pseudonym = null, AnonymizationContext? anonContext = null)
        {
            var lines = new List<string>();

            // ✅ Utiliser le pseudonyme si fourni
            var displayName = pseudonym ?? NomComplet;

            if (!string.IsNullOrEmpty(displayName))
                lines.Add($"Patient : {displayName}");
            
            if (Age.HasValue)
                lines.Add($"Âge : {Age} ans");
            
            if (!string.IsNullOrEmpty(Sexe))
                lines.Add($"Sexe : {Sexe}");
            
            if (!string.IsNullOrEmpty(DateNaissance))
                lines.Add($"Date de naissance : {DateNaissance}");
            
            if (DiagnosticsConnus != null && DiagnosticsConnus.Count > 0)
            {
                lines.Add("\nDiagnostics/Troubles :");
                foreach (var diag in DiagnosticsConnus)
                {
                    lines.Add($"- {diag}");
                }
            }
            
            if (NotesRecentes != null && NotesRecentes.Count > 0)
            {
                lines.Add("\nContexte clinique complet :");
                // ✅ Afficher le contenu complet sans limitation (synthèse ou notes)
                foreach (var note in NotesRecentes)
                {
                    // ✅ Anonymiser le contenu de la note si contexte fourni
                    var noteContent = note;
                    if (anonContext != null && !string.IsNullOrEmpty(anonContext.RealName))
                    {
                        noteContent = AnonymizeContent(noteContent, anonContext.RealName, anonContext.Pseudonym);
                    }
                    lines.Add(noteContent);
                }
            }

            return string.Join("\n", lines);
        }

        /// <summary>
        /// Anonymise le contenu en remplaçant le nom réel par le pseudonyme
        /// </summary>
        private string AnonymizeContent(string content, string realName, string pseudonym)
        {
            if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(realName))
            {
                return content;
            }

            // Nettoyer le nom réel (enlever M., Mme, etc.)
            string cleanRealName = Regex.Replace(realName, @"^(M\.|Mme|Monsieur|Madame)\s+", "", RegexOptions.IgnoreCase).Trim();

            var realParts = cleanRealName.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
            var pseudoParts = pseudonym.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);

            string anonymizedContent = content;

            // ✅ AMÉLIORATION : Remplacer toutes les variantes possibles du nom
            if (realParts.Length >= 2)
            {
                // Format 1 : "Nom Prénom" exact (ex: "RIOS Marie Astrid")
                anonymizedContent = Regex.Replace(anonymizedContent, Regex.Escape(cleanRealName), pseudonym, RegexOptions.IgnoreCase);

                // Format 2 : "Prénom Nom" inversé (ex: "Marie Astrid RIOS")
                string reversedName = string.Join(" ", realParts.Reverse());
                anonymizedContent = Regex.Replace(anonymizedContent, Regex.Escape(reversedName), pseudonym, RegexOptions.IgnoreCase);

                // Format 3 : Parties individuelles du nom (prénom et nom de famille)
                if (pseudoParts.Length >= 2)
                {
                    // Remplacer le nom de famille (dernier élément)
                    string realLastName = realParts[realParts.Length - 1];
                    string pseudoLastName = pseudoParts[pseudoParts.Length - 1];
                    anonymizedContent = Regex.Replace(anonymizedContent, $@"\b{Regex.Escape(realLastName)}\b", pseudoLastName, RegexOptions.IgnoreCase);

                    // Remplacer le prénom (premier élément)
                    string realFirstName = realParts[0];
                    string pseudoFirstName = pseudoParts[0];
                    anonymizedContent = Regex.Replace(anonymizedContent, $@"\b{Regex.Escape(realFirstName)}\b", pseudoFirstName, RegexOptions.IgnoreCase);

                    // Si nom composé (ex: "Marie Astrid"), remplacer aussi les prénoms intermédiaires
                    for (int i = 1; i < realParts.Length - 1; i++)
                    {
                        if (i < pseudoParts.Length - 1)
                        {
                            anonymizedContent = Regex.Replace(
                                anonymizedContent,
                                $@"\b{Regex.Escape(realParts[i])}\b",
                                pseudoParts.Length > i ? pseudoParts[i] : pseudoParts[0],
                                RegexOptions.IgnoreCase);
                        }
                    }
                }
            }
            else
            {
                // Nom simple (un seul mot)
                anonymizedContent = Regex.Replace(anonymizedContent, $@"\b{Regex.Escape(cleanRealName)}\b", pseudonym, RegexOptions.IgnoreCase);
            }

            return anonymizedContent;
        }
    }
}
