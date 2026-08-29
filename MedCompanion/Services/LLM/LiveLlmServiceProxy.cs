using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MedCompanion.Services.LLM
{
    /// <summary>
    /// Wrapper <see cref="ILLMService"/> qui délègue chaque appel au provider ACTUELLEMENT actif de
    /// la factory, plutôt que de figer une référence au provider actif au moment de la construction.
    ///
    /// Plusieurs services (suggesters, extracteurs, détecteur de risque suicidaire...) sont construits
    /// une seule fois au démarrage de l'app avec un <see cref="ILLMService"/> "en dur" — constaté :
    /// après un changement de modèle dans le sélecteur principal, ces services continuaient
    /// silencieusement d'utiliser l'ancien provider (ex. llama.cpp resté chargé en arrière-plan après
    /// bascule vers Gemma, redémarré à leur insu par un appel resté sur l'ancienne référence).
    ///
    /// En leur injectant ce proxy au lieu d'une instance figée, ils suivent automatiquement tout
    /// changement de modèle sans qu'il faille les reconstruire ou leur pousser une mise à jour.
    /// </summary>
    public class LiveLlmServiceProxy : ILLMService, IStructuredOutputService
    {
        private readonly LLMServiceFactory _factory;

        public LiveLlmServiceProxy(LLMServiceFactory factory)
        {
            _factory = factory;
        }

        private ILLMService Current => _factory.GetCurrentProvider();

        public string GetProviderName() => Current.GetProviderName();
        public string GetModelName() => Current.GetModelName();
        public bool IsConfigured() => Current.IsConfigured();

        public Task<(bool isConnected, string message)> CheckConnectionAsync() => Current.CheckConnectionAsync();
        public Task<(bool success, string message)> WarmupAsync() => Current.WarmupAsync();
        public Task<(bool success, string message)> UnloadAsync() => Current.UnloadAsync();

        public Task<(bool success, string result, string? error)> GenerateTextAsync(
            string prompt, int maxTokens = 1500, CancellationToken cancellationToken = default, string? forceModel = null)
            => Current.GenerateTextAsync(prompt, maxTokens, cancellationToken, forceModel);

        public Task<(bool success, string result, string? error)> ChatAsync(
            string systemPrompt, List<(string role, string content)> messages, int maxTokens = 1500,
            CancellationToken cancellationToken = default, string? forceModel = null, int? numCtx = null)
            => Current.ChatAsync(systemPrompt, messages, maxTokens, cancellationToken, forceModel, numCtx);

        public Task<(bool success, string fullResponse, string? error)> ChatStreamAsync(
            string systemPrompt, List<(string role, string content)> messages, Action<string> onTokenReceived,
            int maxTokens = 1500, CancellationToken cancellationToken = default)
            => Current.ChatStreamAsync(systemPrompt, messages, onTokenReceived, maxTokens, cancellationToken);

        public Task<(bool success, string result, string? error)> AnalyzeImageAsync(
            string prompt, byte[] imageData, int maxTokens = 1500, CancellationToken cancellationToken = default)
            => Current.AnalyzeImageAsync(prompt, imageData, maxTokens, cancellationToken);

        // ── Sortie structurée ─────────────────────────────────────────────────
        // Interrogé à chaque appel et non mis en cache : la capacité suit le provider actif, qui
        // change avec le sélecteur de modèle. Un appelant qui aurait retenu la réponse tomberait à
        // côté après une bascule llama.cpp → OpenAI.

        public bool SupportsStructuredOutput
            => Current is IStructuredOutputService s && s.SupportsStructuredOutput;

        public Task<(bool success, string result, string? error)> GenerateJsonAsync(
            string prompt, string schemaName, string jsonSchema,
            int maxTokens = 1500, CancellationToken cancellationToken = default)
        {
            if (Current is IStructuredOutputService s && s.SupportsStructuredOutput)
                return s.GenerateJsonAsync(prompt, schemaName, jsonSchema, maxTokens, cancellationToken);

            // Repli : le provider actif ne sait pas contraindre. On génère en texte libre, et c'est
            // à l'appelant de parser — d'où l'intérêt de tester SupportsStructuredOutput avant.
            return Current.GenerateTextAsync(prompt, maxTokens, cancellationToken);
        }
    }
}
