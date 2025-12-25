using System;

namespace MedCompanion.Models
{
    /// <summary>
    /// Modèle pour un élément de la liste des courriers
    /// Utilisé pour le binding XAML dans CourriersControl
    /// </summary>
    public class LetterListItem
    {
        /// <summary>
        /// Date de création du courrier
        /// </summary>
        public DateTime Date { get; set; }

        /// <summary>
        /// Type de courrier (ex: "Courrier école", "Courrier médecin", etc.)
        /// </summary>
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// Aperçu du contenu (premières lignes)
        /// </summary>
        public string Preview { get; set; } = string.Empty;

        /// <summary>
        /// Chemin complet vers le fichier .md
        /// </summary>
        public string MdPath { get; set; } = string.Empty;

        /// <summary>
        /// Chemin complet vers le fichier .docx (si exporté)
        /// </summary>
        public string DocxPath { get; set; } = string.Empty;

        /// <summary>
        /// Chemin vers le fichier actuellement édité (utilisé pour FilePath binding)
        /// </summary>
        public string FilePath => MdPath;

        // ✅ Propriétés formatées pour l'affichage (compatibilité XAML bindings)

        /// <summary>
        /// Date formatée pour affichage dans la liste
        /// </summary>
        public string DateLabel => Date.ToString("dd/MM/yyyy HH:mm");

        /// <summary>
        /// Type formaté pour affichage dans la liste
        /// </summary>
        public string TypeLabel => Type;

        /// <summary>
        /// Texte complet pour affichage dans la liste
        /// Format: [dd/MM/yyyy] Type - Preview
        /// </summary>
        public string DisplayText => $"[{Date:dd/MM/yyyy}] {Type} - {Preview}";

        /// <summary>
        /// ID du MCC utilisé (si généré depuis un MCC)
        /// </summary>
        public string? MCCId { get; set; }

        /// <summary>
        /// Nom du MCC utilisé (si généré depuis un MCC)
        /// </summary>
        public string? MCCName { get; set; }

        /// <summary>
        /// Note de qualité donnée au MCC (1-5 étoiles) si disponible
        /// </summary>
        public int? Rating { get; set; }
    }
}
