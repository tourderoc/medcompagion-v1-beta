using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MedCompanion.Models;
using MedCompanion.Services.LLM;

namespace MedCompanion.Services.Web
{
    /// <summary>
    /// Sub-Agent Web - Effectue des recherches web, lit des pages et synthétise les résultats
    /// Délégué par Med lorsque des mots-clés de recherche sont détectés
    /// </summary>
    public class WebAgentService
    {
        private readonly OllamaWebSearchService _webSearchService;
        private readonly AgentConfigService _agentConfigService;
        private readonly LLMServiceFactory _llmServiceFactory;
        private readonly SecureStorageService? _secureStorage;

        /// <summary>
        /// Mots-clés qui déclenchent la recherche web
        /// </summary>
        private static readonly string[] SearchKeywords = new[]
        {
            "cherche", "recherche", "vérifie", "vérifier",
            "voir sur le net", "sur le net", "sur internet", "regarde sur",
            "trouve", "trouver", "search", "look up"
        };

        public WebAgentService(
            OllamaWebSearchService webSearchService,
            AgentConfigService agentConfigService,
            LLMServiceFactory llmServiceFactory,
            SecureStorageService? secureStorage = null)
        {
            _webSearchService = webSearchService;
            _agentConfigService = agentConfigService;
            _llmServiceFactory = llmServiceFactory;
            _secureStorage = secureStorage;
        }

        /// <summary>
        /// Vérifie si l'agent Web est activé et configuré
        /// </summary>
        public bool IsAvailable()
        {
            var config = _agentConfigService.GetWebConfig();
            return config?.IsEnabled == true && _webSearchService.IsConfigured();
        }

        /// <summary>
        /// Détecte si un message nécessite une recherche web
        /// </summary>
        public bool ShouldSearch(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                System.Diagnostics.Debug.WriteLine("[WebAgent] ShouldSearch: message vide");
                return false;
            }

            if (!IsAvailable())
            {
                var config = _agentConfigService.GetWebConfig();
                var isEnabled = config?.IsEnabled ?? false;
                var isConfigured = _webSearchService.IsConfigured();
                System.Diagnostics.Debug.WriteLine($"[WebAgent] ShouldSearch: Non disponible - IsEnabled={isEnabled}, ApiConfigured={isConfigured}");
                return false;
            }

            var lowerMessage = message.ToLowerInvariant();
            var hasKeyword = SearchKeywords.Any(keyword => lowerMessage.Contains(keyword));
            System.Diagnostics.Debug.WriteLine($"[WebAgent] ShouldSearch: Message='{lowerMessage}', HasKeyword={hasKeyword}");
            return hasKeyword;
        }

        /// <summary>
        /// Extrait la requête de recherche du message utilisateur
        /// </summary>
        public string ExtractSearchQuery(string message)
        {
            var lowerMessage = message.ToLowerInvariant();

            foreach (var keyword in SearchKeywords)
            {
                var index = lowerMessage.IndexOf(keyword);
                if (index >= 0)
                {
                    // Prendre le texte après le mot-clé
                    var queryStart = index + keyword.Length;
                    if (queryStart < message.Length)
                    {
                        var query = message.Substring(queryStart).Trim();
                        // Nettoyer les prépositions courantes au début
                        query = query.TrimStart(' ', ':', '-', '>', 's', 'i');
                        if (query.StartsWith("ur ")) query = query.Substring(3);
                        if (query.StartsWith("les ")) query = query.Substring(4);
                        if (query.StartsWith("le ")) query = query.Substring(3);
                        if (query.StartsWith("la ")) query = query.Substring(3);
                        return query.Trim();
                    }
                }
            }

            // Si pas de mot-clé trouvé, utiliser le message entier
            return message;
        }

        /// <summary>
        /// Effectue une recherche web complète avec synthèse
        /// </summary>
        /// <param name="query">Requête de recherche</param>
        /// <returns>Tuple (succès, résultat, erreur)</returns>
        public async Task<(bool Success, WebSearchResult Result, string? Error)> SearchAndSynthesizeAsync(string query)
        {
            var config = _agentConfigService.GetWebConfig();
            if (config == null || !config.IsEnabled)
            {
                return (false, new WebSearchResult(), "Agent Web non activé");
            }

            if (!_webSearchService.IsConfigured())
            {
                return (false, new WebSearchResult(), "Clé API Ollama non configurée");
            }

            System.Diagnostics.Debug.WriteLine($"[WebAgent] Recherche: {query}");

            // 1. Effectuer la recherche web
            var (searchSuccess, sources, searchError) = await _webSearchService.SearchAsync(
                query,
                config.MaxSearchResults);

            if (!searchSuccess || sources.Count == 0)
            {
                return (false, new WebSearchResult(), searchError ?? "Aucun résultat trouvé");
            }

            // 2. Fetch le contenu des 2-3 premières sources pour plus de détails
            var sourcesToFetch = sources.Take(Math.Min(3, sources.Count)).ToList();
            foreach (var source in sourcesToFetch)
            {
                var (fetchSuccess, content, _) = await _webSearchService.FetchAsync(source.Url);
                if (fetchSuccess)
                {
                    source.FullContent = content;
                }
            }

            // 3. Synthétiser avec le LLM configuré
            var result = await SynthesizeResultsAsync(query, sources, config);

            return (true, result, null);
        }

        /// <summary>
        /// Recherche simple sans synthèse (retourne juste les sources)
        /// </summary>
        public async Task<(bool Success, List<WebSource> Sources, string? Error)> SearchAsync(string query)
        {
            var config = _agentConfigService.GetWebConfig();
            if (config == null || !config.IsEnabled)
            {
                return (false, new List<WebSource>(), "Agent Web non activé");
            }

            return await _webSearchService.SearchAsync(query, config.MaxSearchResults);
        }

        /// <summary>
        /// Lit le contenu d'une page web
        /// </summary>
        public async Task<(bool Success, string Content, string? Error)> FetchPageAsync(string url)
        {
            return await _webSearchService.FetchAsync(url);
        }

        /// <summary>
        /// Synthétise les résultats de recherche avec le LLM
        /// </summary>
        private async Task<WebSearchResult> SynthesizeResultsAsync(
            string query,
            List<WebSource> sources,
            AgentConfig config)
        {
            var result = new WebSearchResult
            {
                RawQuery = query,
                Sources = sources,
                SearchedAt = DateTime.Now
            };

            try
            {
                // Construire le contexte pour le LLM
                var contextBuilder = new StringBuilder();
                contextBuilder.AppendLine($"Requête de recherche: {query}");
                contextBuilder.AppendLine();
                contextBuilder.AppendLine("Résultats de recherche:");
                contextBuilder.AppendLine();

                for (int i = 0; i < sources.Count; i++)
                {
                    var source = sources[i];
                    contextBuilder.AppendLine($"[Source {i + 1}] {source.Title}");
                    contextBuilder.AppendLine($"URL: {source.Url}");
                    if (!string.IsNullOrEmpty(source.FullContent))
                    {
                        // Limiter le contenu à 3000 caractères par source
                        var content = source.FullContent.Length > 3000
                            ? source.FullContent.Substring(0, 3000) + "..."
                            : source.FullContent;
                        contextBuilder.AppendLine($"Contenu: {content}");
                    }
                    else if (!string.IsNullOrEmpty(source.Snippet))
                    {
                        contextBuilder.AppendLine($"Extrait: {source.Snippet}");
                    }
                    contextBuilder.AppendLine();
                }

                // Obtenir le service LLM approprié
                ILLMService? llmService = CreateLLMService(config);
                if (llmService == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[WebAgent] Provider LLM non disponible: {config.LLMProvider}");
                    result.Summary = "Synthèse non disponible (provider LLM non configuré)";
                    return result;
                }

                // Prompt pour la synthèse
                var synthesisPrompt = $@"Analyse les résultats de recherche suivants et produis une synthèse structurée.

{contextBuilder}

Réponds avec:
1. Un résumé concis en 2-3 phrases
2. Une liste de 3-5 points clés (commence chaque point par •)
3. Un niveau de confiance (high/medium/low) basé sur la concordance des sources

Format ta réponse ainsi:
RESUME:
[ton résumé ici]

POINTS CLES:
• [point 1]
• [point 2]
...

CONFIANCE: [high/medium/low]";

                // Appeler le LLM avec ChatAsync
                var messages = new List<(string role, string content)>
                {
                    ("user", synthesisPrompt)
                };

                var (success, response, error) = await llmService.ChatAsync(
                    config.Posture,
                    messages,
                    maxTokens: 1500);

                if (success && !string.IsNullOrEmpty(response))
                {
                    ParseSynthesisResponse(response, result);
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[WebAgent] Erreur LLM: {error}");
                    result.Summary = "Synthèse non disponible";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WebAgent] Exception synthèse: {ex.Message}");
                result.Summary = $"Erreur lors de la synthèse: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Parse la réponse du LLM pour extraire les éléments structurés
        /// </summary>
        private void ParseSynthesisResponse(string response, WebSearchResult result)
        {
            var lines = response.Split('\n');
            var currentSection = "";
            var keyPoints = new List<string>();
            var summaryLines = new List<string>();

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                if (trimmedLine.StartsWith("RESUME:", StringComparison.OrdinalIgnoreCase) ||
                    trimmedLine.StartsWith("RÉSUMÉ:", StringComparison.OrdinalIgnoreCase))
                {
                    currentSection = "resume";
                    var afterColon = trimmedLine.IndexOf(':');
                    if (afterColon >= 0 && afterColon < trimmedLine.Length - 1)
                    {
                        summaryLines.Add(trimmedLine.Substring(afterColon + 1).Trim());
                    }
                }
                else if (trimmedLine.StartsWith("POINTS CLES:", StringComparison.OrdinalIgnoreCase) ||
                         trimmedLine.StartsWith("POINTS CLÉS:", StringComparison.OrdinalIgnoreCase))
                {
                    currentSection = "points";
                }
                else if (trimmedLine.StartsWith("CONFIANCE:", StringComparison.OrdinalIgnoreCase))
                {
                    currentSection = "confiance";
                    var confidence = trimmedLine.Substring(10).Trim().ToLower();
                    if (confidence.Contains("high") || confidence.Contains("élevé"))
                        result.Confidence = "high";
                    else if (confidence.Contains("low") || confidence.Contains("faible"))
                        result.Confidence = "low";
                    else
                        result.Confidence = "medium";
                }
                else if (!string.IsNullOrWhiteSpace(trimmedLine))
                {
                    switch (currentSection)
                    {
                        case "resume":
                            summaryLines.Add(trimmedLine);
                            break;
                        case "points":
                            if (trimmedLine.StartsWith("•") || trimmedLine.StartsWith("-") || trimmedLine.StartsWith("*"))
                            {
                                keyPoints.Add(trimmedLine.TrimStart('•', '-', '*', ' '));
                            }
                            else
                            {
                                keyPoints.Add(trimmedLine);
                            }
                            break;
                    }
                }
            }

            result.Summary = string.Join(" ", summaryLines);
            result.KeyPoints = keyPoints;

            // Si pas de parsing réussi, utiliser la réponse brute
            if (string.IsNullOrWhiteSpace(result.Summary))
            {
                result.Summary = response;
            }
        }

        /// <summary>
        /// Crée le service LLM approprié selon la configuration
        /// </summary>
        private ILLMService? CreateLLMService(AgentConfig config)
        {
            try
            {
                if (config.LLMProvider == "OpenAI")
                {
                    // Récupérer la clé API OpenAI
                    var apiKey = _secureStorage?.GetApiKey("OpenAI");
                    if (string.IsNullOrEmpty(apiKey))
                    {
                        System.Diagnostics.Debug.WriteLine("[WebAgent] Clé API OpenAI non trouvée");
                        return null;
                    }
                    return new OpenAILLMProvider(apiKey, config.LLMModel);
                }
                else if (config.LLMProvider == "Ollama")
                {
                    // Utiliser Ollama local
                    return new OllamaLLMProvider("http://localhost:11434", config.LLMModel);
                }

                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WebAgent] Erreur création LLM: {ex.Message}");
                return null;
            }
        }
    }
}
