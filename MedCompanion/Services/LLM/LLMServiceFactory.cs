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
        public async Task<ILLMService> InitializeAsync()
        {
            // Tenter d'initialiser Ollama si sélectionné
            if (_settings.LLMProvider == "Ollama")
            {
                _ollamaProvider = new OllamaLLMProvider(
                    _settings.OllamaBaseUrl,
                    _settings.OllamaModel
                );

                var (isConnected, _) = await _ollamaProvider.CheckConnectionAsync();
                
                if (isConnected)
                {
                    _currentProvider = _ollamaProvider;
                    return _currentProvider;
                }
                
                // Ollama indisponible → fallback vers OpenAI
                _settings.LLMProvider = "OpenAI";
            }

            // Charger la clé OpenAI depuis le stockage sécurisé ou variable d'environnement
            string? apiKey = GetOpenAIApiKey();

            // Initialiser OpenAI (par défaut ou fallback)
            _openAIProvider = new OpenAILLMProvider(
                apiKey: apiKey,
                model: _settings.OpenAIModel
            );

            _currentProvider = _openAIProvider;
            return _currentProvider;
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
                if (providerName == "Ollama")
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
