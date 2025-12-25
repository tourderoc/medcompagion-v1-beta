using System;

namespace MedCompanion.Models
{
    /// <summary>
    /// Modèle pour un élément de la liste des attestations
    /// </summary>
    public class AttestationListItem
    {
        public DateTime Date { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Preview { get; set; } = string.Empty;
        public string MdPath { get; set; } = string.Empty;
        public string DocxPath { get; set; } = string.Empty;

        // ✅ Propriétés formatées pour l'affichage (comme les courriers)
        public string DateLabel => Date.ToString("dd/MM/yyyy HH:mm");
        public string TypeLabel => Type;

        public string DisplayText => $"[{Date:dd/MM/yyyy}] {Type} - {Preview}";
    }
}
