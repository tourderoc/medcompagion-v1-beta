using System;
using System.Text.Json.Serialization;

namespace MedCompanion.Models
{
    /// <summary>
    /// Représente un modèle de courrier (par défaut ou personnalisé)
    /// </summary>
    public class LetterTemplate
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// Contenu du template en Markdown
        /// Supporte les deux noms pour compatibilité : "Markdown" et "TemplateMarkdown"
        /// </summary>
        [JsonPropertyName("Markdown")]
        public string Markdown { get; set; } = string.Empty;
        
        /// <summary>
        /// Alias pour TemplateMarkdown (utilisé dans MCCModel)
        /// </summary>
        [JsonPropertyName("TemplateMarkdown")]
        public string TemplateMarkdown 
        { 
            get => Markdown; 
            set => Markdown = value; 
        }
        
        public DateTime CreatedDate { get; set; }
        public int UsageCount { get; set; }
        public bool IsCustom { get; set; }
        
        /// <summary>
        /// Liste des variables extraites du template (ex: ["Nom_Prenom", "Age", "Ecole"])
        /// </summary>
        public List<string> Variables { get; set; } = new();
        
        /// <summary>
        /// Description courte du template (optionnel)
        /// </summary>
        public string? Description { get; set; }
    }
    
    /// <summary>
    /// Conteneur pour la sérialisation JSON des templates personnalisés
    /// </summary>
    public class TemplateCollection
    {
        public List<LetterTemplate> CustomTemplates { get; set; } = new();
    }
}
