using System;

namespace MedCompanion.Models
{
    /// <summary>
    /// Entrée de log pour un prompt envoyé à l'IA
    /// </summary>
    public class PromptLogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Module { get; set; } = "";
        public string SystemPrompt { get; set; } = "";
        public string UserPrompt { get; set; } = "";
        public string AIResponse { get; set; } = "";
        public int TokensUsed { get; set; }
        public string LLMProvider { get; set; } = "";
        public string ModelName { get; set; } = "";
        public bool Success { get; set; }
        public string? Error { get; set; }
        
        /// <summary>
        /// Texte d'affichage pour la liste
        /// </summary>
        public string DisplayText => 
            $"{Timestamp:HH:mm:ss} - {Module} ({TokensUsed} tokens)";
    }
}
