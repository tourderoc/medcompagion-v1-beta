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
            int maxTokens = 1500,
            System.Threading.CancellationToken cancellationToken = default,
            string? forceModel = null)
        {
            if (!IsConfigured())
            {
                return (false, "", "Clé API OpenAI non configurée");
            }

            try
            {
                var requestBody = new
                {
                    model = forceModel ?? _model,
                    messages = new[]
                    {
                        new { role = "user", content = prompt }
                    },
                    temperature = 0.3,
                    max_tokens = maxTokens
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(ENDPOINT, content, cancellationToken).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

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
            catch (TaskCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                // Annulation demandée par l'utilisateur - propager l'exception
                throw new OperationCanceledException("Génération annulée", ex, cancellationToken);
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
            int maxTokens = 1500,
            System.Threading.CancellationToken cancellationToken = default,
            string? forceModel = null)
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
                    model = forceModel ?? _model,
                    messages = messagesList.ToArray(),
                    temperature = 0.3,
                    max_tokens = maxTokens
                };

                var json = JsonSerializer.Serialize(requestBody);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(ENDPOINT, httpContent, cancellationToken).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

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
            catch (TaskCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                // Annulation demandée par l'utilisateur - propager l'exception
                throw new OperationCanceledException("Chat annulé", ex, cancellationToken);
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

        public async Task<(bool success, string result, string? error)> AnalyzeImageAsync(
            string prompt, 
            byte[] imageData, 
            int maxTokens = 1500,
            System.Threading.CancellationToken cancellationToken = default)
        {
            if (!IsConfigured())
            {
                return (false, "", "Clé API OpenAI non configurée");
            }

            try
            {
                string base64Image = Convert.ToBase64String(imageData);
                
                var requestBody = new
                {
                    model = _model.Contains("gpt-4") ? _model : "gpt-4o", // Forcer un modèle vision si besoin
                    messages = new[]
                    {
                        new 
                        { 
                            role = "user", 
                            content = new object[]
                            {
                                new { type = "text", text = prompt },
                                new { type = "image_url", image_url = new { url = $"data:image/png;base64,{base64Image}" } }
                            }
                        }
                    },
                    max_tokens = maxTokens
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(ENDPOINT, content, cancellationToken).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    return (false, "", $"Erreur {(int)response.StatusCode}: {responseBody}");
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
            catch (Exception ex)
            {
                return (false, "", $"Erreur vision: {ex.Message}");
            }
        }

        public async Task<(bool success, string fullResponse, string? error)> ChatStreamAsync(
            string systemPrompt,
            List<(string role, string content)> messages,
            Action<string> onTokenReceived,
            int maxTokens = 1500,
            System.Threading.CancellationToken cancellationToken = default)
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
                    max_tokens = maxTokens,
                    stream = true // Activer le streaming
                };

                var json = JsonSerializer.Serialize(requestBody);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                // Utiliser SendAsync pour avoir accès au stream de réponse
                var request = new HttpRequestMessage(HttpMethod.Post, ENDPOINT)
                {
                    Content = httpContent
                };

                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

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

                var fullResponse = new StringBuilder();

                // Lire le stream SSE ligne par ligne
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new System.IO.StreamReader(stream);

                while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (string.IsNullOrEmpty(line)) continue;

                    // Les lignes SSE commencent par "data: "
                    if (!line.StartsWith("data: ")) continue;

                    var data = line.Substring(6); // Enlever "data: "

                    // Fin du stream
                    if (data == "[DONE]") break;

                    try
                    {
                        var doc = JsonDocument.Parse(data);

                        if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                        {
                            var choice = choices[0];
                            if (choice.TryGetProperty("delta", out var delta))
                            {
                                if (delta.TryGetProperty("content", out var contentProp))
                                {
                                    var token = contentProp.GetString() ?? "";
                                    if (!string.IsNullOrEmpty(token))
                                    {
                                        fullResponse.Append(token);
                                        onTokenReceived(token);
                                    }
                                }
                            }
                        }
                    }
                    catch (JsonException)
                    {
                        // Ignorer les lignes mal formatées
                        continue;
                    }
                }

                return (true, fullResponse.ToString(), null);
            }
            catch (HttpRequestException ex)
            {
                return (false, "", $"Erreur réseau: {ex.Message}");
            }
            catch (TaskCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                // Annulation demandée par l'utilisateur - propager l'exception
                throw new OperationCanceledException("Streaming annulé", ex, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                return (false, "", "Timeout - Le streaming a pris trop de temps");
            }
            catch (Exception ex)
            {
                return (false, "", $"Erreur inattendue: {ex.Message}");
            }
        }
    }
}
