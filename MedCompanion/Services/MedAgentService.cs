using System.Collections.ObjectModel;
using MedCompanion.Models;
using MedCompanion.Services.LLM;
using MedCompanion.Services.Web;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service pour gérer les interactions avec l'agent Med
    /// Utilise le LLM configuré dans AgentConfig et gère l'historique de conversation
    /// </summary>
    public class MedAgentService
    {
        private readonly AgentConfigService _agentConfigService;
        private readonly LLMServiceFactory _llmFactory;
        private readonly SecureStorageService _secureStorage;
        private ILLMService? _currentLLMService;
        private WebAgentService? _webAgentService;
        private List<(string role, string content)> _conversationHistory = new();

        public MedAgentService(
            AgentConfigService agentConfigService,
            LLMServiceFactory llmFactory,
            SecureStorageService secureStorage)
        {
            _agentConfigService = agentConfigService;
            _llmFactory = llmFactory;
            _secureStorage = secureStorage;

            // Initialiser le Sub-Agent Web
            InitializeWebAgent();
        }

        /// <summary>
        /// Initialise le Sub-Agent Web pour la recherche
        /// </summary>
        private void InitializeWebAgent()
        {
            try
            {
                var webSearchService = new OllamaWebSearchService(_secureStorage);
                _webAgentService = new WebAgentService(webSearchService, _agentConfigService, _llmFactory, _secureStorage);
                System.Diagnostics.Debug.WriteLine("[MedAgentService] Sub-Agent Web initialisé");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MedAgentService] Erreur init Web Agent : {ex.Message}");
            }
        }

        /// <summary>
        /// Événement déclenché lors d'un message de Med
        /// </summary>
        public event EventHandler<string>? MessageReceived;

        /// <summary>
        /// Événement déclenché lors d'une erreur
        /// </summary>
        public event EventHandler<string>? ErrorOccurred;

        /// <summary>
        /// Événement déclenché quand Med est en train de réfléchir
        /// </summary>
        public event EventHandler<bool>? ThinkingStateChanged;

        /// <summary>
        /// Historique de la conversation courante
        /// </summary>
        public IReadOnlyList<(string role, string content)> ConversationHistory => _conversationHistory.AsReadOnly();

        /// <summary>
        /// Indique si Med est activé
        /// </summary>
        public bool IsEnabled
        {
            get
            {
                var config = _agentConfigService.GetMedConfig();
                return config?.IsEnabled ?? false;
            }
        }

        /// <summary>
        /// Obtient les informations du LLM configuré pour Med (provider + modèle)
        /// </summary>
        public string GetLLMInfo()
        {
            var config = _agentConfigService.GetMedConfig();
            if (config == null) return "Non configuré";
            return $"{config.LLMProvider} - {config.LLMModel}";
        }

        /// <summary>
        /// Obtient le provider LLM de Med configuré
        /// </summary>
        private async Task<ILLMService?> GetLLMServiceAsync()
        {
            var config = _agentConfigService.GetMedConfig();
            if (config == null) return null;

            // Vérifier si on doit recréer le service
            if (_currentLLMService == null)
            {
                _currentLLMService = await CreateLLMServiceAsync(config).ConfigureAwait(false);
            }

            return _currentLLMService;
        }

        /// <summary>
        /// Crée le service LLM approprié selon la configuration de Med
        /// </summary>
        private Task<ILLMService?> CreateLLMServiceAsync(AgentConfig config)
        {
            try
            {
                if (config.LLMProvider == "OpenAI")
                {
                    var apiKey = _secureStorage.GetApiKey("OpenAI");
                    if (string.IsNullOrEmpty(apiKey))
                    {
                        ErrorOccurred?.Invoke(this, "Clé API OpenAI non configurée. Veuillez la définir dans Paramètres > API.");
                        return Task.FromResult<ILLMService?>(null);
                    }

                    return Task.FromResult<ILLMService?>(new OpenAILLMProvider(apiKey, config.LLMModel));
                }
                else if (config.LLMProvider == "Ollama")
                {
                    // Le constructeur OllamaLLMProvider prend (baseUrl, model)
                    // On utilise l'URL par défaut localhost:11434 et le modèle configuré
                    return Task.FromResult<ILLMService?>(new OllamaLLMProvider("http://localhost:11434", config.LLMModel));
                }

                return Task.FromResult<ILLMService?>(null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MedAgentService] Erreur création LLM : {ex.Message}");
                ErrorOccurred?.Invoke(this, $"Erreur de connexion au LLM : {ex.Message}");
                return Task.FromResult<ILLMService?>(null);
            }
        }

        /// <summary>
        /// Événement déclenché à chaque token reçu (pour le streaming)
        /// </summary>
        public event EventHandler<string>? TokenReceived;

        /// <summary>
        /// Événement déclenché quand la configuration LLM change
        /// </summary>
        public event EventHandler<string>? ConfigChanged;

        /// <summary>
        /// Événement déclenché lors d'une recherche web
        /// </summary>
        public event EventHandler<string>? WebSearchStarted;

        /// <summary>
        /// Envoie un message à Med et retourne sa réponse
        /// </summary>
        public async Task<(bool success, string response, string? error)> SendMessageAsync(string userMessage)
        {
            var config = _agentConfigService.GetMedConfig();
            if (config == null || !config.IsEnabled)
            {
                return (false, "", "Med n'est pas activé.");
            }

            var llmService = await GetLLMServiceAsync().ConfigureAwait(false);
            if (llmService == null)
            {
                return (false, "", "Service LLM non disponible.");
            }

            try
            {
                ThinkingStateChanged?.Invoke(this, true);

                // Vérifier si une recherche web est demandée
                string enrichedMessage = userMessage;
                if (_webAgentService != null && _webAgentService.ShouldSearch(userMessage))
                {
                    enrichedMessage = await EnrichWithWebSearchAsync(userMessage).ConfigureAwait(false);
                }

                // Ajouter le message utilisateur à l'historique
                _conversationHistory.Add(("user", enrichedMessage));

                // Préparer les messages pour l'API
                var messages = new List<(string role, string content)>(_conversationHistory);

                // Appeler le LLM avec la posture comme system prompt
                var (success, result, error) = await llmService.ChatAsync(
                    config.Posture,
                    messages,
                    maxTokens: 2000
                ).ConfigureAwait(false);

                if (success)
                {
                    // Ajouter la réponse à l'historique
                    _conversationHistory.Add(("assistant", result));
                    MessageReceived?.Invoke(this, result);
                    return (true, result, null);
                }
                else
                {
                    // Retirer le message utilisateur en cas d'erreur
                    _conversationHistory.RemoveAt(_conversationHistory.Count - 1);
                    ErrorOccurred?.Invoke(this, error ?? "Erreur inconnue");
                    return (false, "", error);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MedAgentService] Exception : {ex.Message}");
                ErrorOccurred?.Invoke(this, ex.Message);
                return (false, "", ex.Message);
            }
            finally
            {
                ThinkingStateChanged?.Invoke(this, false);
            }
        }

        /// <summary>
        /// Enrichit le message utilisateur avec les résultats de recherche web
        /// </summary>
        private async Task<string> EnrichWithWebSearchAsync(string userMessage)
        {
            if (_webAgentService == null) return userMessage;

            try
            {
                var searchQuery = _webAgentService.ExtractSearchQuery(userMessage);
                System.Diagnostics.Debug.WriteLine($"[MedAgentService] Délégation recherche: {searchQuery}");

                WebSearchStarted?.Invoke(this, $"Recherche web: {searchQuery}");

                var (success, result, error) = await _webAgentService.SearchAndSynthesizeAsync(searchQuery).ConfigureAwait(false);

                if (success && result != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[MedAgentService] Recherche réussie: {result.Sources.Count} sources");

                    // Enrichir le message avec les résultats
                    var enrichedMessage = $"{userMessage}\n\n[Résultats de recherche Web]\n{result.ToContextString()}";
                    return enrichedMessage;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[MedAgentService] Recherche échouée: {error}");
                    // Retourner le message original si la recherche échoue
                    return $"{userMessage}\n\n[Note: La recherche web n'a pas abouti: {error}]";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MedAgentService] Exception recherche: {ex.Message}");
                return userMessage;
            }
        }

        /// <summary>
        /// Envoie un message à Med avec streaming (les tokens arrivent au fur et à mesure)
        /// </summary>
        public async Task<(bool success, string response, string? error)> SendMessageStreamAsync(string userMessage)
        {
            var config = _agentConfigService.GetMedConfig();
            if (config == null || !config.IsEnabled)
            {
                return (false, "", "Med n'est pas activé.");
            }

            var llmService = await GetLLMServiceAsync().ConfigureAwait(false);
            if (llmService == null)
            {
                return (false, "", "Service LLM non disponible.");
            }

            try
            {
                ThinkingStateChanged?.Invoke(this, true);

                // Vérifier si une recherche web est demandée
                string enrichedMessage = userMessage;
                if (_webAgentService != null && _webAgentService.ShouldSearch(userMessage))
                {
                    enrichedMessage = await EnrichWithWebSearchAsync(userMessage).ConfigureAwait(false);
                }

                // Ajouter le message utilisateur à l'historique
                _conversationHistory.Add(("user", enrichedMessage));

                // Préparer les messages pour l'API
                var messages = new List<(string role, string content)>(_conversationHistory);

                // Callback pour chaque token reçu
                void OnToken(string token)
                {
                    TokenReceived?.Invoke(this, token);
                }

                // Appeler le LLM avec streaming
                var (success, result, error) = await llmService.ChatStreamAsync(
                    config.Posture,
                    messages,
                    OnToken,
                    maxTokens: 2000
                ).ConfigureAwait(false);

                if (success)
                {
                    // Ajouter la réponse complète à l'historique
                    _conversationHistory.Add(("assistant", result));
                    MessageReceived?.Invoke(this, result);
                    return (true, result, null);
                }
                else
                {
                    // Retirer le message utilisateur en cas d'erreur
                    _conversationHistory.RemoveAt(_conversationHistory.Count - 1);
                    ErrorOccurred?.Invoke(this, error ?? "Erreur inconnue");
                    return (false, "", error);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MedAgentService] Exception : {ex.Message}");
                ErrorOccurred?.Invoke(this, ex.Message);
                return (false, "", ex.Message);
            }
            finally
            {
                ThinkingStateChanged?.Invoke(this, false);
            }
        }

        /// <summary>
        /// Efface l'historique de conversation
        /// </summary>
        public void ClearHistory()
        {
            _conversationHistory.Clear();
            System.Diagnostics.Debug.WriteLine("[MedAgentService] Historique effacé");
        }

        /// <summary>
        /// Réinitialise le service LLM (utile après changement de configuration)
        /// </summary>
        public void ResetLLMService()
        {
            _currentLLMService = null;
            System.Diagnostics.Debug.WriteLine("[MedAgentService] Service LLM réinitialisé");

            // Notifier le changement de configuration
            var newLLMInfo = GetLLMInfo();
            ConfigChanged?.Invoke(this, newLLMInfo);
        }

        /// <summary>
        /// Recharge le Sub-Agent Web (utile après changement de configuration)
        /// </summary>
        public void ReloadWebAgent()
        {
            // Recharger la configuration des agents depuis le fichier
            _agentConfigService.Reload();

            // Recréer le Web Agent avec la nouvelle configuration
            InitializeWebAgent();

            System.Diagnostics.Debug.WriteLine("[MedAgentService] Sub-Agent Web rechargé");
        }

        /// <summary>
        /// Obtient un résumé de la conversation actuelle
        /// </summary>
        public async Task<(bool success, string summary, string? error)> GetConversationSummaryAsync()
        {
            if (_conversationHistory.Count == 0)
            {
                return (true, "Aucune conversation en cours.", null);
            }

            var llmService = await GetLLMServiceAsync().ConfigureAwait(false);
            if (llmService == null)
            {
                return (false, "", "Service LLM non disponible.");
            }

            try
            {
                ThinkingStateChanged?.Invoke(this, true);

                var summaryPrompt = @"Tu es un assistant qui résume des conversations médicales.
Résume la conversation suivante de manière concise et professionnelle, en préservant les points clés et les décisions importantes.
Le résumé doit être utile pour un médecin qui voudrait se remémorer cette discussion.";

                var conversationText = string.Join("\n", _conversationHistory.Select(h =>
                    $"{(h.role == "user" ? "Médecin" : "Med")}: {h.content}"));

                var messages = new List<(string role, string content)>
                {
                    ("user", $"Voici la conversation à résumer :\n\n{conversationText}")
                };

                var (success, result, error) = await llmService.ChatAsync(
                    summaryPrompt,
                    messages,
                    maxTokens: 1000
                ).ConfigureAwait(false);

                return (success, result, error);
            }
            finally
            {
                ThinkingStateChanged?.Invoke(this, false);
            }
        }

        /// <summary>
        /// Analyse une image avec un prompt spécifique
        /// </summary>
        public async Task<string> ProcessVisionRequestAsync(string prompt, byte[] imageBytes)
        {
            var llmService = await GetLLMServiceAsync().ConfigureAwait(false);
            if (llmService == null) return "Service LLM non disponible.";

            try
            {
                ThinkingStateChanged?.Invoke(this, true);
                var (success, result, error) = await llmService.AnalyzeImageAsync(prompt, imageBytes).ConfigureAwait(false);
                
                if (success) return result;
                return $"Erreur d'analyse : {error}";
            }
            catch (Exception ex)
            {
                return $"Échec : {ex.Message}";
            }
            finally
            {
                ThinkingStateChanged?.Invoke(this, false);
            }
        }
    }
}
