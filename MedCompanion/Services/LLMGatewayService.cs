using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MedCompanion.Models;
using MedCompanion.Services.LLM;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service passerelle centralisé pour tous les appels LLM.
    /// Gère automatiquement l'anonymisation selon le type de provider :
    /// - Local (Ollama) : Pas d'anonymisation
    /// - Cloud (OpenAI) : Anonymisation 3 phases + Désanonymisation
    /// </summary>
    public class LLMGatewayService
    {
        private readonly LLMServiceFactory _llmFactory;
        private readonly AnonymizationService _anonymizationService;
        private readonly OpenAIService _openAIService; // Pour ExtractPIIAsync (Phase 3)
        private readonly PathService _pathService;

        /// <summary>
        /// Événement déclenché pour informer du statut
        /// </summary>
        public event EventHandler<string>? StatusChanged;

        public LLMGatewayService(
            LLMServiceFactory llmFactory,
            AnonymizationService anonymizationService,
            OpenAIService openAIService,
            PathService pathService)
        {
            _llmFactory = llmFactory ?? throw new ArgumentNullException(nameof(llmFactory));
            _anonymizationService = anonymizationService ?? throw new ArgumentNullException(nameof(anonymizationService));
            _openAIService = openAIService ?? throw new ArgumentNullException(nameof(openAIService));
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
        }

        #region Détection Provider

        /// <summary>
        /// Vérifie si le provider actuel est local (Ollama)
        /// </summary>
        public bool IsLocalProvider()
        {
            var providerName = _llmFactory.GetActiveProviderName();
            return providerName.Equals("Ollama", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Vérifie si le provider actuel est cloud (OpenAI, etc.)
        /// </summary>
        public bool IsCloudProvider()
        {
            return !IsLocalProvider();
        }

        /// <summary>
        /// Retourne le nom du provider actif
        /// </summary>
        public string GetActiveProviderName()
        {
            return _llmFactory.GetActiveProviderName();
        }

        #endregion

        #region Méthodes principales

        /// <summary>
        /// Génère du texte avec anonymisation automatique si nécessaire.
        /// Pipeline : [Anonymisation 3 phases si Cloud] → LLM → [Désanonymisation si Cloud]
        /// </summary>
        /// <param name="prompt">Le prompt à envoyer</param>
        /// <param name="patientName">Nom complet du patient (pour charger ses métadonnées)</param>
        /// <param name="maxTokens">Nombre maximum de tokens</param>
        /// <param name="cancellationToken">Token d'annulation</param>
        /// <returns>Tuple (success, result, error)</returns>
        public async Task<(bool success, string result, string? error)> GenerateTextAsync(
            string prompt,
            string? patientName = null,
            int maxTokens = 1500,
            CancellationToken cancellationToken = default)
        {
            System.Diagnostics.Debug.WriteLine($"[LLMGateway] ════════════════════════════════════════");
            System.Diagnostics.Debug.WriteLine($"[LLMGateway] GenerateTextAsync - Début");
            System.Diagnostics.Debug.WriteLine($"[LLMGateway] Patient: {patientName ?? "(aucun)"}");
            System.Diagnostics.Debug.WriteLine($"[LLMGateway] Prompt: {prompt?.Length ?? 0} caractères");

            if (string.IsNullOrWhiteSpace(prompt))
                return (false, "", "Le prompt est vide");

            try
            {
                var llm = _llmFactory.GetCurrentProvider();
                var providerName = _llmFactory.GetActiveProviderName();
                var modelName = _llmFactory.GetActiveModelName();

                System.Diagnostics.Debug.WriteLine($"[LLMGateway] Provider: {providerName} | Model: {modelName}");

                if (!llm.IsConfigured())
                    return (false, "", "LLM non configuré");

                // Si provider local → Pas d'anonymisation
                if (IsLocalProvider())
                {
                    LogStatus("🖥️ Provider local détecté - Pas d'anonymisation");
                    System.Diagnostics.Debug.WriteLine($"[LLMGateway] → Appel direct LLM (pas d'anonymisation)");
                    var localResult = await llm.GenerateTextAsync(prompt, maxTokens);
                    System.Diagnostics.Debug.WriteLine($"[LLMGateway] → Réponse: {localResult.result?.Length ?? 0} caractères");
                    return localResult;
                }

                // Provider cloud → Anonymisation 3 phases
                LogStatus("☁️ Provider cloud détecté - Anonymisation activée");
                System.Diagnostics.Debug.WriteLine($"[LLMGateway] → Anonymisation 3 phases activée");

                var (anonymizedPrompt, context) = await AnonymizeInputAsync(prompt, patientName);

                // Appel LLM avec prompt anonymisé
                var (success, result, error) = await llm.GenerateTextAsync(anonymizedPrompt, maxTokens);

                if (!success)
                    return (false, result, error);

                // Désanonymisation de la réponse
                var deanonymizedResult = DeanonymizeOutput(result, context);

                return (true, deanonymizedResult, null);
            }
            catch (Exception ex)
            {
                return (false, "", $"Erreur LLMGateway: {ex.Message}");
            }
        }

        /// <summary>
        /// Chat avec historique et anonymisation automatique si nécessaire.
        /// Pipeline : [Anonymisation 3 phases si Cloud] → LLM → [Désanonymisation si Cloud]
        /// </summary>
        /// <param name="systemPrompt">Prompt système</param>
        /// <param name="messages">Historique des messages (role, content)</param>
        /// <param name="patientName">Nom complet du patient (pour charger ses métadonnées)</param>
        /// <param name="maxTokens">Nombre maximum de tokens</param>
        /// <param name="cancellationToken">Token d'annulation</param>
        /// <returns>Tuple (success, result, error)</returns>
        public async Task<(bool success, string result, string? error)> ChatAsync(
            string systemPrompt,
            List<(string role, string content)> messages,
            string? patientName = null,
            int maxTokens = 1500,
            CancellationToken cancellationToken = default)
        {
            System.Diagnostics.Debug.WriteLine($"[LLMGateway] ════════════════════════════════════════");
            System.Diagnostics.Debug.WriteLine($"[LLMGateway] ChatAsync - Début");
            System.Diagnostics.Debug.WriteLine($"[LLMGateway] Patient: {patientName ?? "(aucun)"}");
            System.Diagnostics.Debug.WriteLine($"[LLMGateway] SystemPrompt: {systemPrompt?.Length ?? 0} caractères");
            System.Diagnostics.Debug.WriteLine($"[LLMGateway] Messages: {messages?.Count ?? 0} messages");

            if (messages == null || messages.Count == 0)
                return (false, "", "Aucun message fourni");

            try
            {
                // Vérifier l'annulation dès le début
                if (cancellationToken.IsCancellationRequested)
                    return (false, "", "Opération annulée par l'utilisateur");

                var llm = _llmFactory.GetCurrentProvider();
                var providerName = _llmFactory.GetActiveProviderName();
                var modelName = _llmFactory.GetActiveModelName();

                System.Diagnostics.Debug.WriteLine($"[LLMGateway] Provider: {providerName} | Model: {modelName}");
                System.Diagnostics.Debug.WriteLine($"[LLMGateway] IsLocal: {IsLocalProvider()}");

                if (!llm.IsConfigured())
                    return (false, "", "LLM non configuré");

                // Si provider local → Pas d'anonymisation
                if (IsLocalProvider())
                {
                    LogStatus("🖥️ Provider local détecté - Pas d'anonymisation");
                    System.Diagnostics.Debug.WriteLine($"[LLMGateway] → Appel direct LLM (pas d'anonymisation)");

                    // Vérifier l'annulation avant l'appel LLM
                    if (cancellationToken.IsCancellationRequested)
                        return (false, "", "Opération annulée par l'utilisateur");

                    var localResult = await llm.ChatAsync(systemPrompt ?? string.Empty, messages, maxTokens, cancellationToken);
                    System.Diagnostics.Debug.WriteLine($"[LLMGateway] → Réponse locale: {localResult.result?.Length ?? 0} caractères");
                    System.Diagnostics.Debug.WriteLine($"[LLMGateway] ════════════════════════════════════════");
                    return localResult;
                }

                // Provider cloud → Anonymisation 3 phases
                LogStatus("☁️ Provider cloud détecté - Anonymisation activée");
                System.Diagnostics.Debug.WriteLine($"[LLMGateway] → Anonymisation 3 phases activée");

                // Vérifier l'annulation avant l'anonymisation
                if (cancellationToken.IsCancellationRequested)
                    return (false, "", "Opération annulée par l'utilisateur");

                // Anonymiser le system prompt
                System.Diagnostics.Debug.WriteLine($"[LLMGateway] → Anonymisation du system prompt...");
                var (anonymizedSystemPrompt, systemContext) = await AnonymizeInputAsync(systemPrompt ?? string.Empty, patientName);
                System.Diagnostics.Debug.WriteLine($"[LLMGateway]   SystemPrompt: {systemContext?.Replacements?.Count ?? 0} remplacements");

                // Vérifier l'annulation après l'anonymisation du system prompt
                if (cancellationToken.IsCancellationRequested)
                    return (false, "", "Opération annulée par l'utilisateur");

                // Anonymiser tous les messages
                System.Diagnostics.Debug.WriteLine($"[LLMGateway] → Anonymisation de {messages.Count} messages...");
                var anonymizedMessages = new List<(string role, string content)>();
                var messagesContexts = new List<AnonymizationContext>();
                int totalReplacements = 0;

                foreach (var (role, content) in messages)
                {
                    // Vérifier l'annulation pendant l'anonymisation des messages
                    if (cancellationToken.IsCancellationRequested)
                        return (false, "", "Opération annulée par l'utilisateur");

                    var (anonymizedContent, msgContext) = await AnonymizeInputAsync(content, patientName);
                    anonymizedMessages.Add((role, anonymizedContent));
                    messagesContexts.Add(msgContext);
                    totalReplacements += msgContext?.Replacements?.Count ?? 0;
                }
                System.Diagnostics.Debug.WriteLine($"[LLMGateway]   Messages: {totalReplacements} remplacements au total");

                // Vérifier l'annulation avant l'appel LLM cloud
                if (cancellationToken.IsCancellationRequested)
                    return (false, "", "Opération annulée par l'utilisateur");

                // Appel LLM avec données anonymisées
                System.Diagnostics.Debug.WriteLine($"[LLMGateway] → Appel LLM cloud avec données anonymisées...");
                var (success, result, error) = await llm.ChatAsync(anonymizedSystemPrompt, anonymizedMessages, maxTokens, cancellationToken);

                // Vérifier l'annulation après l'appel LLM
                if (cancellationToken.IsCancellationRequested)
                    return (false, "", "Opération annulée par l'utilisateur");

                if (!success)
                {
                    System.Diagnostics.Debug.WriteLine($"[LLMGateway] ✗ Erreur LLM: {error}");
                    return (false, result, error);
                }

                System.Diagnostics.Debug.WriteLine($"[LLMGateway] ✓ Réponse cloud: {result?.Length ?? 0} caractères");

                // Désanonymisation de la réponse (utiliser le contexte combiné)
                System.Diagnostics.Debug.WriteLine($"[LLMGateway] → Désanonymisation de la réponse...");
                var combinedContext = CombineContexts(systemContext, messagesContexts);
                System.Diagnostics.Debug.WriteLine($"[LLMGateway]   Contexte combiné: {combinedContext?.Replacements?.Count ?? 0} remplacements");
                var deanonymizedResult = DeanonymizeOutput(result ?? string.Empty, combinedContext);
                System.Diagnostics.Debug.WriteLine($"[LLMGateway] ✓ Désanonymisation terminée");
                System.Diagnostics.Debug.WriteLine($"[LLMGateway] ════════════════════════════════════════");

                return (true, deanonymizedResult, null);
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine($"[LLMGateway] ✗ Opération annulée");
                return (false, "", "Opération annulée par l'utilisateur");
            }
            catch (Exception ex)
            {
                return (false, "", $"Erreur LLMGateway: {ex.Message}");
            }
        }

        /// <summary>
        /// Chat avec streaming et anonymisation automatique si nécessaire.
        /// Les tokens sont envoyés via le callback onTokenReceived.
        /// Note: Les tokens sont envoyés AVANT désanonymisation pour fluidité.
        /// La réponse finale retournée est désanonymisée.
        /// </summary>
        public async Task<(bool success, string result, string? error)> ChatStreamAsync(
            string systemPrompt,
            List<(string role, string content)> messages,
            Action<string> onTokenReceived,
            string? patientName = null,
            int maxTokens = 2000,
            CancellationToken cancellationToken = default)
        {
            System.Diagnostics.Debug.WriteLine($"[LLMGateway] ════════════════════════════════════════");
            System.Diagnostics.Debug.WriteLine($"[LLMGateway] ChatStreamAsync - Début");
            System.Diagnostics.Debug.WriteLine($"[LLMGateway] Patient: {patientName ?? "(aucun)"}");

            if (messages == null || messages.Count == 0)
                return (false, "", "Aucun message fourni");

            try
            {
                if (cancellationToken.IsCancellationRequested)
                    return (false, "", "Opération annulée");

                var llm = _llmFactory.GetCurrentProvider();
                var providerName = _llmFactory.GetActiveProviderName();

                if (!llm.IsConfigured())
                    return (false, "", "LLM non configuré");

                // Si provider local → Pas d'anonymisation, streaming direct
                if (IsLocalProvider())
                {
                    LogStatus("🖥️ Provider local - Streaming direct");

                    var (success, fullResponse, error) = await llm.ChatStreamAsync(
                        systemPrompt ?? string.Empty,
                        messages,
                        onTokenReceived,
                        maxTokens,
                        cancellationToken);

                    return (success, fullResponse, error);
                }

                // Provider cloud → Anonymisation puis streaming
                LogStatus("☁️ Provider cloud - Anonymisation + Streaming");

                // Anonymiser le system prompt
                var (anonymizedSystemPrompt, systemContext) = await AnonymizeInputAsync(systemPrompt ?? string.Empty, patientName);

                if (cancellationToken.IsCancellationRequested)
                    return (false, "", "Opération annulée");

                // Anonymiser tous les messages
                var anonymizedMessages = new List<(string role, string content)>();
                var messagesContexts = new List<AnonymizationContext>();

                foreach (var (role, content) in messages)
                {
                    if (cancellationToken.IsCancellationRequested)
                        return (false, "", "Opération annulée");

                    var (anonymizedContent, msgContext) = await AnonymizeInputAsync(content, patientName);
                    anonymizedMessages.Add((role, anonymizedContent));
                    messagesContexts.Add(msgContext);
                }

                // Streaming avec données anonymisées
                // Note: Les tokens sont envoyés tels quels (anonymisés), la désanonymisation se fait sur la réponse finale
                var (streamSuccess, streamResponse, streamError) = await llm.ChatStreamAsync(
                    anonymizedSystemPrompt,
                    anonymizedMessages,
                    onTokenReceived,  // Les tokens arrivent anonymisés
                    maxTokens,
                    cancellationToken);

                if (!streamSuccess)
                    return (false, streamResponse, streamError);

                // Désanonymiser la réponse complète pour le retour final
                var combinedContext = CombineContexts(systemContext, messagesContexts);
                var deanonymizedResult = DeanonymizeOutput(streamResponse, combinedContext);

                return (true, deanonymizedResult, null);
            }
            catch (OperationCanceledException)
            {
                return (false, "", "Opération annulée");
            }
            catch (Exception ex)
            {
                return (false, "", $"Erreur streaming: {ex.Message}");
            }
        }

        #endregion

        #region Anonymisation 3 phases

        /// <summary>
        /// Anonymise le texte avec les 3 phases :
        /// Phase 1 : Données patient.json
        /// Phase 2 : Patterns regex
        /// Phase 3 : Extraction LLM local (si disponible)
        /// </summary>
        private async Task<(string anonymizedText, AnonymizationContext context)> AnonymizeInputAsync(
            string text,
            string? patientName)
        {
            if (string.IsNullOrWhiteSpace(text))
                return (text ?? "", new AnonymizationContext());

            var context = new AnonymizationContext { WasAnonymized = true };

            // Charger les métadonnées patient si disponible
            PatientMetadata? patientData = null;
            if (!string.IsNullOrWhiteSpace(patientName))
            {
                patientData = LoadPatientMetadata(patientName);
            }

            // Phase 3 : Extraction PII via LLM local (si Ollama disponible)
            PIIExtractionResult? extractedPii = null;
            try
            {
                // Vérifier si Ollama est disponible pour l'extraction
                if (await _llmFactory.IsOllamaAvailableAsync())
                {
                    LogStatus("🔍 Phase 3 - Extraction PII via LLM local...");
                    extractedPii = await _openAIService.ExtractPIIAsync(text);

                    if (extractedPii != null && extractedPii.GetAllEntities().Count > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LLMGateway] Phase 3 - {extractedPii.GetAllEntities().Count} entités extraites");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LLMGateway] Phase 3 échouée (non bloquant): {ex.Message}");
            }

            // Appliquer l'anonymisation avec les 3 phases
            string anonymizedText;

            if (extractedPii != null || patientData != null)
            {
                // Utiliser AnonymizeWithExtractedData qui combine les 3 phases
                anonymizedText = _anonymizationService.AnonymizeWithExtractedData(
                    text,
                    extractedPii,   // Phase 3
                    patientData,    // Phase 1
                    context         // Phase 2 inclus
                );
            }
            else
            {
                // Fallback : Phase 1+2 seulement
                var (anonText, anonContext) = await _anonymizationService.AnonymizeAsync(text, patientData);
                anonymizedText = anonText;
                context = anonContext;
            }

            LogStatus($"✓ Anonymisation terminée ({context.Replacements.Count} remplacements)");

            return (anonymizedText, context);
        }

        /// <summary>
        /// Charge les métadonnées patient depuis patient.json
        /// </summary>
        private PatientMetadata? LoadPatientMetadata(string patientName)
        {
            try
            {
                var patientJsonPath = _pathService.GetPatientJsonPath(patientName);

                if (System.IO.File.Exists(patientJsonPath))
                {
                    var json = System.IO.File.ReadAllText(patientJsonPath, System.Text.Encoding.UTF8);
                    return System.Text.Json.JsonSerializer.Deserialize<PatientMetadata>(json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LLMGateway] Erreur chargement patient.json: {ex.Message}");
            }

            return null;
        }

        #endregion

        #region Désanonymisation

        /// <summary>
        /// Désanonymise la réponse du LLM
        /// </summary>
        private string DeanonymizeOutput(string text, AnonymizationContext? context)
        {
            if (string.IsNullOrWhiteSpace(text) || context == null || !context.WasAnonymized)
                return text;

            return _anonymizationService.Deanonymize(text, context);
        }

        /// <summary>
        /// Combine plusieurs contextes d'anonymisation en un seul
        /// </summary>
        private AnonymizationContext CombineContexts(AnonymizationContext? systemContext, List<AnonymizationContext> messagesContexts)
        {
            var combined = new AnonymizationContext { WasAnonymized = true };

            // Ajouter les remplacements du system prompt
            if (systemContext?.Replacements != null)
            {
                foreach (var kvp in systemContext.Replacements)
                {
                    if (!combined.Replacements.ContainsKey(kvp.Key))
                        combined.Replacements[kvp.Key] = kvp.Value;
                }
                combined.Pseudonym = systemContext.Pseudonym;
            }

            // Ajouter les remplacements de chaque message
            foreach (var msgContext in messagesContexts)
            {
                if (msgContext?.Replacements != null)
                {
                    foreach (var kvp in msgContext.Replacements)
                    {
                        if (!combined.Replacements.ContainsKey(kvp.Key))
                            combined.Replacements[kvp.Key] = kvp.Value;
                    }

                    // Garder le pseudonyme s'il existe
                    if (!string.IsNullOrEmpty(msgContext.Pseudonym))
                        combined.Pseudonym = msgContext.Pseudonym;
                }
            }

            return combined;
        }

        #endregion

        #region Helpers

        private void LogStatus(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[LLMGateway] {message}");
            StatusChanged?.Invoke(this, message);
        }

        #endregion
    }
}
