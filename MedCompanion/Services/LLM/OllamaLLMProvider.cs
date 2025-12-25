using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MedCompanion.Services.LLM
{
    /// <summary>
    /// Provider LLM pour Ollama (modèles locaux)
    /// </summary>
    public class OllamaLLMProvider : ILLMService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private string _currentModel;
        private List<string> _availableModels = new();

        public OllamaLLMProvider(string baseUrl = "http://localhost:11434", string defaultModel = "llama3.2:latest")
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _currentModel = defaultModel;
            _httpClient = new HttpClient
            {
                // Timeout de 3 minutes pour supporter les modèles 8B/12B
                // Au-delà, le modèle est probablement trop lent pour l'usage interactif
                Timeout = TimeSpan.FromMinutes(3)
            };
        }

        public string GetProviderName() => "Ollama";

        public string GetModelName() => _currentModel;

        public bool IsConfigured() => true; // Ollama n'a pas besoin de clé API

        /// <summary>
        /// Détecte automatiquement les modèles Ollama disponibles
        /// </summary>
        public async Task<List<string>> DetectAvailableModelsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/tags");
                
                if (!response.IsSuccessStatusCode)
                {
                    return new List<string>();
                }

                var json = await response.Content.ReadAsStringAsync();
                var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("models", out var modelsArray))
                {
                    _availableModels = modelsArray.EnumerateArray()
                        .Select(m => m.GetProperty("name").GetString())
                        .Where(name => !string.IsNullOrEmpty(name))
                        .Select(name => name!)
                        .OrderBy(name => name)
                        .ToList();

                    return _availableModels;
                }

                return new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Change le modèle actif
        /// </summary>
        public void SetModel(string modelName)
        {
            _currentModel = modelName;
        }

        public async Task<(bool isConnected, string message)> CheckConnectionAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_baseUrl}/api/tags");
                
                if (response.IsSuccessStatusCode)
                {
                    var models = await DetectAvailableModelsAsync();
                    var count = models.Count;
                    return (true, $"Ollama connecté - {count} modèle(s) disponible(s)");
                }

                return (false, $"Ollama non accessible sur {_baseUrl}");
            }
            catch (HttpRequestException ex)
            {
                return (false, $"Erreur connexion Ollama: {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                return (false, "Timeout - Ollama ne répond pas");
            }
            catch (Exception ex)
            {
                return (false, $"Erreur inattendue: {ex.Message}");
            }
        }

        public async Task<(bool success, string message)> WarmupAsync()
        {
            try
            {
                var requestBody = new
                {
                    model = _currentModel,
                    prompt = "Bonjour",
                    stream = false
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/generate", content);

                if (response.IsSuccessStatusCode)
                {
                    return (true, $"Warm-up réussi - {_currentModel} prêt");
                }

                return (false, $"Échec warm-up: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                return (false, $"Erreur warm-up: {ex.Message}");
            }
        }

        public async Task<(bool success, string result, string? error)> GenerateTextAsync(
            string prompt, 
            int maxTokens = 1500)
        {
            try
            {
                // Créer les options selon maxTokens
                object requestBody;

                if (maxTokens <= 0)
                {
                    // Pas de limite de tokens
                    requestBody = new
                    {
                        model = _currentModel,
                        prompt = prompt,
                        stream = false,
                        options = new
                        {
                            temperature = 0.3
                        }
                    };
                }
                else
                {
                    // Limite spécifiée
                    requestBody = new
                    {
                        model = _currentModel,
                        prompt = prompt,
                        stream = false,
                        options = new
                        {
                            num_predict = maxTokens,
                            temperature = 0.3
                        }
                    };
                }

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/generate", content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return (false, "", $"Erreur {response.StatusCode}: {response.ReasonPhrase}");
                }

                var doc = JsonDocument.Parse(responseBody);
                
                if (doc.RootElement.TryGetProperty("response", out var responseText))
                {
                    var text = responseText.GetString() ?? "";
                    return (true, text, null);
                }

                return (false, "", "Format de réponse inattendu");
            }
            catch (HttpRequestException ex)
            {
                return (false, "", $"Erreur réseau: {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                return (false, "", "Timeout - La génération a pris trop de temps");
            }
            catch (Exception ex)
            {
                return (false, "", $"Erreur inattendue: {ex.Message}");
            }
        }

        public async Task<(bool success, string result, string? error)> ChatAsync(
            string systemPrompt,
            List<(string role, string content)> messages,
            int maxTokens = 1500)
        {
            try
            {
                // Construire le tableau de messages pour Ollama
                var ollamaMessages = new List<object>
                {
                    new { role = "system", content = systemPrompt }
                };

                foreach (var (role, content) in messages)
                {
                    ollamaMessages.Add(new { role = role, content = content });
                }

                var requestBody = new
                {
                    model = _currentModel,
                    messages = ollamaMessages.ToArray(),
                    stream = false,
                    options = new
                    {
                        num_predict = maxTokens,
                        temperature = 0.3
                    }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/chat", httpContent);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return (false, "", $"Erreur {response.StatusCode}: {response.ReasonPhrase}");
                }

                var doc = JsonDocument.Parse(responseBody);
                
                if (doc.RootElement.TryGetProperty("message", out var messageObj))
                {
                    if (messageObj.TryGetProperty("content", out var contentProp))
                    {
                        var text = contentProp.GetString() ?? "";
                        return (true, text, null);
                    }
                }

                return (false, "", "Format de réponse inattendu");
            }
            catch (HttpRequestException ex)
            {
                return (false, "", $"Erreur réseau: {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                return (false, "", "Timeout - Le chat a pris trop de temps");
            }
            catch (Exception ex)
            {
                return (false, "", $"Erreur inattendue: {ex.Message}");
            }
        }
    }
}
