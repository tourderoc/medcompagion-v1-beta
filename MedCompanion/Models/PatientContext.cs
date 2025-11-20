using System.Collections.Generic;

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
        public string ToPromptText()
        {
            var lines = new List<string>();
            
            if (!string.IsNullOrEmpty(NomComplet))
                lines.Add($"Patient : {NomComplet}");
            
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
                lines.Add("\nNotes récentes (contexte) :");
                for (int i = 0; i < NotesRecentes.Count && i < 3; i++)
                {
                    var preview = NotesRecentes[i].Length > 150 
                        ? NotesRecentes[i].Substring(0, 150) + "..." 
                        : NotesRecentes[i];
                    lines.Add($"{i + 1}. {preview}");
                }
            }
            
            return string.Join("\n", lines);
        }
    }
}
