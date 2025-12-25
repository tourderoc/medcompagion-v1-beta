using System;

namespace MedCompanion.Models
{
    /// <summary>
    /// Classe auxiliaire pour l'affichage des MCC dans la bibliothèque
    /// Contient le modèle MCC + propriétés formatées pour l'UI
    /// </summary>
    public class MCCDisplayItem
    {
        public MCCModel MCC { get; set; } = null!;
        public string Name { get; set; } = string.Empty;
        public string CreatedDisplay { get; set; } = string.Empty;
        public int UsageCount { get; set; }
        public string KeywordsPreview { get; set; } = string.Empty;
        public SemanticAnalysis Semantic { get; set; } = new();

        // Propriétés de notation
        public double AverageRating { get; set; }
        public int RatingCount { get; set; }
        public string RatingDisplay { get; set; } = string.Empty;
        public string RatingColor { get; set; } = "#999999";
    }
}
