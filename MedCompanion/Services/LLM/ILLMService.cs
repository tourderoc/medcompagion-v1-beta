using System.Collections.Generic;
using System.Threading.Tasks;

namespace MedCompanion.Services.LLM
{
    /// <summary>
    /// Interface commune pour tous les providers LLM (OpenAI, Ollama, etc.)
    /// </summary>
    public interface ILLMService
    {
        /// <summary>
        /// Nom du provider (ex: "OpenAI", "Ollama")
        /// </summary>
        string GetProviderName();

        /// <summary>
        /// Nom du modèle actuel (ex: "gpt-4o-mini", "llama3.2:latest")
        /// </summary>
        string GetModelName();

        /// <summary>
        /// Vérifie si le service est connecté et fonctionnel
        /// </summary>
        Task<(bool isConnected, string message)> CheckConnectionAsync();

        /// <summary>
        /// Effectue un warm-up du modèle avec une requête simple
        /// </summary>
        Task<(bool success, string message)> WarmupAsync();

        /// <summary>
        /// Génère du texte à partir d'un prompt simple
        /// </summary>
        Task<(bool success, string result, string? error)> GenerateTextAsync(
            string prompt, 
            int maxTokens = 1500);

        /// <summary>
        /// Chat avec système prompt + historique de messages
        /// </summary>
        Task<(bool success, string result, string? error)> ChatAsync(
            string systemPrompt,
            List<(string role, string content)> messages,
            int maxTokens = 1500);

        /// <summary>
        /// Indique si le service est configuré et prêt à l'emploi
        /// </summary>
        bool IsConfigured();
    }
}
