using System.Collections.Generic;

namespace MedCompanion.Models
{
    /// <summary>
    /// Configuration d'un prompt (3 niveaux : Original/Default/Custom)
    /// - Original : Version d'usine, jamais modifiée (restauration possible)
    /// - Default : Version de référence (peut évoluer via promotion)
    /// - Custom : Version personnalisée de test
    /// </summary>
    public class PromptConfig
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Module { get; set; } = string.Empty; // "OpenAI", "Letter", "Attestation", etc.
        
        /// <summary>
        /// Prompt original d'usine (jamais modifié, pour restauration)
        /// </summary>
        public string OriginalPrompt { get; set; } = string.Empty;
        
        /// <summary>
        /// Prompt par défaut (peut évoluer via promotion du custom)
        /// </summary>
        public string DefaultPrompt { get; set; } = string.Empty;
        
        /// <summary>
        /// Prompt personnalisé (vos tests/améliorations)
        /// </summary>
        public string? CustomPrompt { get; set; }
        
        /// <summary>
        /// Indique si le prompt personnalisé est actif
        /// </summary>
        public bool IsCustomActive { get; set; } = false;
        
        /// <summary>
        /// Retourne le prompt actif (custom si activé, sinon default)
        /// </summary>
        public string ActivePrompt => IsCustomActive && !string.IsNullOrEmpty(CustomPrompt) 
            ? CustomPrompt 
            : DefaultPrompt;
    }
    
    /// <summary>
    /// Configuration complète de tous les prompts
    /// </summary>
    public class PromptsConfiguration
    {
        public Dictionary<string, PromptConfig> Prompts { get; set; } = new();
    }
}
