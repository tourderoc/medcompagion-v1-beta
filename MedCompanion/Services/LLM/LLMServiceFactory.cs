using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MedCompanion.Services.LLM
{
    /// <summary>
    /// Factory pour créer et gérer les providers LLM
    /// </summary>
    public class LLMServiceFactory
    {
        private ILLMService? _currentProvider;
        private OllamaLLMProvider? _ollamaProvider;
        private OpenAILLMProvider? _openAIProvider;
        private LlamaCppProvider? _llamaCppProvider;
        
        private readonly AppSettings _settings;
        private readonly SecureStorageService? _secureStorage;
        public event EventHandler<string>? ApiKeyMigrationDetected;

        public LLMServiceFactory(AppSettings settings, SecureStorageService? secureStorage = null)
        {
            _settings = settings;
            _secureStorage = secureStorage;
        }

        /// <summary>
        /// Initialise les providers et retourne le provider actif
        /// </summary>
        public Task<ILLMService> InitializeAsync()
        {
            // Initialiser llama.cpp (Qwen) si c'était le dernier modèle sélectionné. Pas de connexion
            // à vérifier ici : le process llama-server démarre à la demande au premier appel.
            if (_settings.LLMProvider == "LlamaCpp")
            {
                _llamaCppProvider ??= new LlamaCppProvider();
                _currentProvider = _llamaCppProvider;
                return Task.FromResult(_currentProvider);
            }

            // Initialiser Ollama si sélectionné
            if (_settings.LLMProvider == "Ollama")
            {
                _ollamaProvider = new OllamaLLMProvider(
                    _settings.OllamaBaseUrl,
                    _settings.OllamaModel
                );
                
                _currentProvider = _ollamaProvider;
                return Task.FromResult(_currentProvider);
            }

            // Charger la clé OpenAI depuis le stockage sécurisé ou variable d'environnement
            string? apiKey = GetOpenAIApiKey();

            // Initialiser OpenAI (par défaut ou fallback)
            _openAIProvider = new OpenAILLMProvider(
                apiKey: apiKey,
                model: _settings.OpenAIModel
            );

            _currentProvider = _openAIProvider;
            return Task.FromResult((ILLMService)_currentProvider);
        }

        /// <summary>
        /// Retourne le provider actuellement actif
        /// </summary>
        public ILLMService GetCurrentProvider()
        {
            if (_currentProvider == null)
            {
                throw new InvalidOperationException("LLMServiceFactory n'a pas été initialisé. Appelez InitializeAsync() d'abord.");
            }
            return _currentProvider;
        }

        /// <summary>
        /// Change le provider actif (Ollama ou OpenAI)
        /// </summary>
        public async Task<(bool success, string message)> SwitchProviderAsync(string providerName, string? modelName = null)
        {
            try
            {
                // On quitte llama.cpp : libérer sa VRAM pour que les modèles Ollama en aient
                // à nouveau la pleine disposition sur la même carte graphique.
                //
                // Volontairement SANS condition sur _currentProvider : llama-server peut tourner
                // alors que le provider actif est Ollama, car la lecture des formulaires par vision
                // (FormulaireSaisieDialog) instancie son propre LlamaCppProvider et démarre le
                // serveur en dehors de cette fabrique. Le test "_currentProvider == _llamaCppProvider"
                // laissait alors Qwen résident (14,6 Go) pendant qu'Ollama chargeait son modèle
                // par-dessus. Stop() est peu coûteux et sans effet si rien ne tourne.
                if (providerName != "LlamaCpp")
                {
                    LlamaCppServerManager.Stop();
                }

                if (providerName == "LlamaCpp")
                {
                    // Libère proactivement le modèle Ollama actif (s'il y en a un) avant de charger
                    // Qwen sur llama.cpp : sans ça, Ollama garde son modèle en VRAM/RAM plusieurs
                    // minutes après le dernier appel (keep_alive par défaut), et les deux modèles
                    // tournent en même temps le temps que le timeout expire — VRAM/RAM saturées,
                    // ralentissements observés. Best-effort : une erreur ici ne doit pas bloquer le
                    // basculement vers llama.cpp.
                    if (_ollamaProvider != null)
                    {
                        try { await _ollamaProvider.UnloadAsync(); }
                        catch { /* best-effort */ }
                    }

                    _llamaCppProvider ??= new LlamaCppProvider();

                    // Sélectionner le profil AVANT la connexion : c'est lui qui détermine le modèle
                    // que le serveur va charger. Un nom inconnu ne devrait pas arriver (le routage
                    // n'envoie ici que les modèles résolus), mais on garde le profil courant plutôt
                    // que d'échouer.
                    var previousProfile = LlamaCppServerManager.CurrentProfile;
                    var profile = LlamaCppProfiles.Resolve(modelName ?? _settings.OllamaModel);
                    if (profile != null)
                        _llamaCppProvider.SetProfile(profile);

                    var (isConnected, message) = await _llamaCppProvider.CheckConnectionAsync();
                    if (!isConnected)
                    {
                        // Restaurer le profil précédent : sinon le gestionnaire resterait pointé sur
                        // un modèle qui n'a pas pu être chargé, et le prochain appel LLM retenterait
                        // ce modèle en échec alors que le sélecteur affiche l'ancien.
                        _llamaCppProvider.SetProfile(previousProfile);
                        return (false, message);
                    }

                    _currentProvider = _llamaCppProvider;
                    _settings.LLMProvider = "LlamaCpp";
                    // Conserve le nom Ollama brut (celui utilisé pour retrouver l'item dans le
                    // sélecteur au redémarrage — voir SelectCurrentModel), pas le nom d'affichage.
                    _settings.OllamaModel = modelName ?? _settings.OllamaModel;

                    return (true, $"🖥️ Basculé vers llama.cpp ({_llamaCppProvider.GetModelName()})");
                }
                else if (providerName == "Ollama")
                {
                    // Créer ou réutiliser le provider Ollama
                    if (_ollamaProvider == null || (modelName != null && _ollamaProvider.GetModelName() != modelName))
                    {
                        _ollamaProvider = new OllamaLLMProvider(
                            _settings.OllamaBaseUrl,
                            modelName ?? _settings.OllamaModel
                        );
                    }
                    else if (modelName != null)
                    {
                        _ollamaProvider.SetModel(modelName);
                    }

                    // Vérifier la connexion
                    var (isConnected, message) = await _ollamaProvider.CheckConnectionAsync();
                    
                    if (!isConnected)
                    {
                        return (false, message);
                    }

                    // Effectuer le warm-up
                    var (warmupSuccess, warmupMessage) = await _ollamaProvider.WarmupAsync();
                    
                    if (!warmupSuccess)
                    {
                        return (false, $"Warm-up échoué: {warmupMessage}");
                    }

                    _currentProvider = _ollamaProvider;
                    _settings.LLMProvider = "Ollama";
                    _settings.OllamaModel = _ollamaProvider.GetModelName();

                    return (true, $"🖥️ Basculé vers Ollama ({_ollamaProvider.GetModelName()})");
                }
                else if (providerName == "OpenAI")
                {
                    // Charger la clé OpenAI
                    string? apiKey = GetOpenAIApiKey();

                    // Créer ou réutiliser le provider OpenAI
                    if (_openAIProvider == null)
                    {
                        _openAIProvider = new OpenAILLMProvider(
                            apiKey: apiKey,
                            model: modelName ?? _settings.OpenAIModel
                        );
                    }

                    // Vérifier la connexion
                    var (isConnected, message) = await _openAIProvider.CheckConnectionAsync();
                    
                    if (!isConnected)
                    {
                        return (false, message);
                    }

                    // Effectuer le warm-up
                    var (warmupSuccess, warmupMessage) = await _openAIProvider.WarmupAsync();
                    
                    if (!warmupSuccess)
                    {
                        return (false, $"Warm-up échoué: {warmupMessage}");
                    }

                    _currentProvider = _openAIProvider;
                    _settings.LLMProvider = "OpenAI";
                    
                    if (modelName != null)
                    {
                        _settings.OpenAIModel = modelName;
                    }

                    return (true, $"☁️ Basculé vers OpenAI ({_openAIProvider.GetModelName()})");
                }

                return (false, $"Provider inconnu: {providerName}");
            }
            catch (Exception ex)
            {
                return (false, $"Erreur lors du changement de provider: {ex.Message}");
            }
        }

        /// <summary>
        /// Détecte les modèles Ollama disponibles
        /// </summary>
        public async Task<List<string>> GetAvailableOllamaModelsAsync()
        {
            try
            {
                var tempProvider = new OllamaLLMProvider(_settings.OllamaBaseUrl);
                return await tempProvider.DetectAvailableModelsAsync();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Vérifie si Ollama est disponible
        /// </summary>
        public async Task<bool> IsOllamaAvailableAsync()
        {
            try
            {
                var tempProvider = new OllamaLLMProvider(_settings.OllamaBaseUrl);
                var (isConnected, _) = await tempProvider.CheckConnectionAsync();
                return isConnected;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Retourne le nom du provider actif
        /// </summary>
        public string GetActiveProviderName()
        {
            return _currentProvider?.GetProviderName() ?? "Aucun";
        }

        /// <summary>
        /// Retourne le nom du modèle actif
        /// </summary>
        public string GetActiveModelName()
        {
            return _currentProvider?.GetModelName() ?? "Aucun";
        }

        /// <summary>
        /// Vrai si le provider actif (Ollama ou llama.cpp) expose un niveau de réflexion graduable
        /// (low/medium/high) pour son modèle courant.
        /// </summary>
        public bool CurrentModelSupportsReasoningEffort()
        {
            // Tous les modèles routés vers llama.cpp ne raisonnent pas : Gemma 4 n'a pas de niveau
            // de réflexion, et lui en envoyer un fait échouer le rendu de son template (erreur 500).
            if (_currentProvider == _llamaCppProvider && _llamaCppProvider != null)
                return LlamaCppServerManager.CurrentProfile.SupportsReasoning;

            return _ollamaProvider != null && _currentProvider == _ollamaProvider &&
                   OllamaLLMProvider.SupportsReasoningEffort(_ollamaProvider.GetModelName());
        }

        /// <summary>
        /// Définit le niveau de réflexion ("low" | "medium" | "high") pour le provider actif
        /// (Ollama ou llama.cpp). Sans effet si le provider/modèle actif ne supporte pas ce réglage.
        /// </summary>
        public void SetReasoningEffort(string? level)
        {
            if (_ollamaProvider != null)
                _ollamaProvider.ReasoningEffort = level;
            if (_llamaCppProvider != null)
                _llamaCppProvider.ReasoningEffort = level;
        }

        /// <summary>
        /// Récupère la clé API OpenAI depuis le stockage sécurisé ou variable d'environnement
        /// Gère aussi la migration depuis les variables d'environnement
        /// </summary>
        private string? GetOpenAIApiKey()
        {
            // 1. Essayer de charger depuis le stockage sécurisé
            if (_secureStorage != null && _secureStorage.HasApiKey("OpenAI"))
            {
                return _secureStorage.GetApiKey("OpenAI");
            }

            // 2. Fallback vers variable d'environnement
            var envKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            
            if (!string.IsNullOrEmpty(envKey))
            {
                // Migration détectée : notifier pour proposer l'import
                ApiKeyMigrationDetected?.Invoke(this, envKey);
                return envKey;
            }

            return null;
        }
    }
}
