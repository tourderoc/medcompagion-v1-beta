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
    /// Provider LLM pour OpenAI (cloud)
    /// </summary>
    public class OpenAILLMProvider : ILLMService
    {
        private const string ENDPOINT = "https://api.openai.com/v1/chat/completions";
        
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _model;

        public OpenAILLMProvider(string? apiKey = null, string model = "gpt-4o-mini")
        {
            _apiKey = apiKey ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? string.Empty;
            _model = model;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
            
            if (!string.IsNullOrEmpty(_apiKey))
            {
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
            }
        }

        public string GetProviderName() => "OpenAI";

        public string GetModelName() => _model;

        public bool IsConfigured() => !string.IsNullOrEmpty(_apiKey);

        public async Task<(bool isConnected, string message)> CheckConnectionAsync()
        {
            if (!IsConfigured())
            {
                return (false, "Clé API OpenAI non configurée");
            }

            try
            {
                // Test simple avec un prompt minimal
                var requestBody = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "user", content = "test" }
                    },
                    max_tokens = 5
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(ENDPOINT, content);

                if (response.IsSuccessStatusCode)
                {
                    return (true, $"OpenAI connecté - {_model} disponible");
                }

                return response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => (false, "Clé API invalide"),
                    System.Net.HttpStatusCode.TooManyRequests => (false, "Quota dépassé"),
                    _ => (false, $"Erreur {(int)response.StatusCode}")
                };
            }
            catch (HttpRequestException ex)
            {
                return (false, $"Erreur réseau: {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                return (false, "Timeout - OpenAI ne répond pas");
            }
            catch (Exception ex)
            {
                return (false, $"Erreur inattendue: {ex.Message}");
            }
        }

        public async Task<(bool success, string message)> WarmupAsync()
        {
            if (!IsConfigured())
            {
                return (false, "Clé API non configurée");
            }

            try
            {
                var requestBody = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "user", content = "Bonjour" }
                    },
                    max_tokens = 10
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(ENDPOINT, content);

                if (response.IsSuccessStatusCode)
                {
                    return (true, $"Warm-up réussi - {_model} prêt");
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
            if (!IsConfigured())
            {
                return (false, "", "Clé API OpenAI non configurée");
            }

            try
            {
                var requestBody = new
                {
                    model = _model,
                    messages = new[]
                    {
                        new { role = "user", content = prompt }
                    },
                    temperature = 0.3,
                    max_tokens = maxTokens
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(ENDPOINT, content);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return response.StatusCode switch
                    {
                        System.Net.HttpStatusCode.Unauthorized => (false, "", "Erreur 401: Clé API invalide"),
                        System.Net.HttpStatusCode.TooManyRequests => (false, "", "Erreur 429: Trop de requêtes"),
                        System.Net.HttpStatusCode.InternalServerError => (false, "", "Erreur 500: Erreur serveur OpenAI"),
                        _ => (false, "", $"Erreur {(int)response.StatusCode}: {response.ReasonPhrase}")
                    };
                }

                using var document = JsonDocument.Parse(responseBody);
                var root = document.RootElement;
                
                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var messageContent = choices[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString();
                    
                    return (true, messageContent ?? "", null);
                }

                return (false, "", "Format de réponse inattendu");
            }
            catch (HttpRequestException ex)
            {
                return (false, "", $"Erreur réseau: {ex.Message}");
            }
            catch (JsonException ex)
            {
                return (false, "", $"Erreur de traitement: {ex.Message}");
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
            if (!IsConfigured())
            {
                return (false, "", "Clé API OpenAI non configurée");
            }

            try
            {
                var messagesList = new List<object>
                {
                    new { role = "system", content = systemPrompt }
                };

                foreach (var (role, content) in messages)
                {
                    messagesList.Add(new { role = role, content = content });
                }

                var requestBody = new
                {
                    model = _model,
                    messages = messagesList.ToArray(),
                    temperature = 0.3,
                    max_tokens = maxTokens
                };

                var json = JsonSerializer.Serialize(requestBody);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(ENDPOINT, httpContent);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    return response.StatusCode switch
                    {
                        System.Net.HttpStatusCode.Unauthorized => (false, "", "Erreur 401: Clé API invalide"),
                        System.Net.HttpStatusCode.TooManyRequests => (false, "", "Erreur 429: Trop de requêtes"),
                        System.Net.HttpStatusCode.InternalServerError => (false, "", "Erreur 500: Erreur serveur OpenAI"),
                        _ => (false, "", $"Erreur {(int)response.StatusCode}: {response.ReasonPhrase}")
                    };
                }

                using var document = JsonDocument.Parse(responseBody);
                var root = document.RootElement;
                
                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var messageContent = choices[0]
                        .GetProperty("message")
                        .GetProperty("content")
                        .GetString();
                    
                    return (true, messageContent ?? "", null);
                }

                return (false, "", "Format de réponse inattendu");
            }
            catch (HttpRequestException ex)
            {
                return (false, "", $"Erreur réseau: {ex.Message}");
            }
            catch (JsonException ex)
            {
                return (false, "", $"Erreur de traitement: {ex.Message}");
            }
            catch (Exception ex)
            {
                return (false, "", $"Erreur inattendue: {ex.Message}");
            }
        }
    }
}
