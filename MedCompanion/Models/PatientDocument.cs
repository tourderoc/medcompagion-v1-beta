using System;

namespace MedCompanion.Models
{
    /// <summary>
    /// Représente un document patient (bilan, courrier, ordonnance, etc.)
    /// </summary>
    public class PatientDocument
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty; // bilans, courriers, ordonnances, etc.
        public DateTime DateAdded { get; set; } = DateTime.Now;
        public string Summary { get; set; } = string.Empty; // Synthèse IA du document
        public string ExtractedText { get; set; } = string.Empty; // Texte extrait (OCR si nécessaire)
        public long FileSizeBytes { get; set; }
        public string FileExtension { get; set; } = string.Empty;
        
        /// <summary>
        /// Nom d'affichage formaté
        /// </summary>
        public string DisplayName => $"{FileName} ({Category})";
        
        /// <summary>
        /// Date formatée pour l'affichage
        /// </summary>
        public string DateAddedDisplay => DateAdded.ToString("dd/MM/yyyy HH:mm");
        
        /// <summary>
        /// Taille formatée pour l'affichage
        /// </summary>
        public string FileSizeDisplay
        {
            get
            {
                if (FileSizeBytes < 1024)
                    return $"{FileSizeBytes} B";
                else if (FileSizeBytes < 1024 * 1024)
                    return $"{FileSizeBytes / 1024:F1} KB";
                else
                    return $"{FileSizeBytes / (1024 * 1024):F1} MB";
            }
        }
    }
    
    /// <summary>
    /// Catégories de documents prédéfinies
    /// </summary>
    public static class DocumentCategories
    {
        public const string Bilans = "bilans";
        public const string Courriers = "courriers";
        public const string Ordonnances = "ordonnances";
        public const string Attestations = "attestations";
        public const string Radiologies = "radiologies";
        public const string Analyses = "analyses";
        public const string Autres = "autres";
        
        public static string[] All => new[]
        {
            Bilans,
            Courriers,
            Ordonnances,
            Attestations,
            Radiologies,
            Analyses,
            Autres
        };
        
        public static string GetDisplayName(string category)
        {
            return category switch
            {
                Bilans => "📋 Bilans",
                Courriers => "📝 Courriers",
                Ordonnances => "⚕️ Ordonnances",
                Attestations => "📑 Attestations",
                Radiologies => "🔬 Radiologies",
                Analyses => "🧪 Analyses",
                Autres => "📄 Autres",
                _ => category
            };
        }
    }
}
