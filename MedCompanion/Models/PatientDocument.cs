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
        /// <summary>
        /// Ce document est le formulaire de complétion rempli par les parents, reconnu à son titre
        /// imprimé lors de l'import.
        ///
        /// Il ne doit être ni analysé ni synthétisé — son contenu utile est manuscrit, donc invisible
        /// à l'extraction de texte — et surtout il ne doit PAS entrer dans la pondération de la
        /// Synthèse Initiale : c'est une pièce administrative, pas un élément clinique.
        /// </summary>
        public bool IsFormulaireCompletion { get; set; }

        /// <summary>Type de formulaire reconnu (voir <see cref="FormulairesConnus"/>), ou vide.</summary>
        public string FormulaireId { get; set; } = string.Empty;

        /// <summary>
        /// Version de la mise en page avec laquelle ce formulaire a été IMPRIMÉ — pas celle du
        /// gabarit actuel. C'est elle qui désigne la géométrie à utiliser pour le relire.
        /// </summary>
        public int FormulaireVersion { get; set; }

        public long FileSizeBytes { get; set; }
        public string FileExtension { get; set; } = string.Empty;
        
        /// <summary>
        /// Nom d'affichage formaté
        /// </summary>
        public string DisplayName => $"{FileName} ({Category})";

        /// <summary>
        /// Document à lire champ par champ — seul cas où le crayon de saisie a un sens.
        /// Le drapeau seul ne suffit pas : il n'existe que depuis l'ajout de la reconnaissance
        /// automatique, et les formulaires importés avant dorment dans « autres » sans lui. On
        /// retombe donc sur la catégorie puis sur le nom du fichier.
        /// </summary>
        public bool IsFormulaire =>
            IsFormulaireCompletion ||
            Category.Equals("Formulaires", StringComparison.OrdinalIgnoreCase) ||
            FileName.Contains("formulaire", StringComparison.OrdinalIgnoreCase);
        
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
