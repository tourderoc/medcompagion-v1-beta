using System;
using System.Collections.Generic;

namespace MedCompanion.Models
{
    /// <summary>
    /// Représente la synthèse d'un dossier MDPH pour l'affichage dans la liste des formulaires.
    /// </summary>
    public class MDPHSynthesis
    {
        public string Patient { get; set; } = string.Empty;
        public DateTime DateCreation { get; set; }
        public List<string> Demandes { get; set; } = new List<string>();
        public string AutresDemandes { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
    }
}
