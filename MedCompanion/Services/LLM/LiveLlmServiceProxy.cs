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
    public class LiveLlmServiceProxy : ILLMService
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
    }
}
