using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MedCompanion.Models;

namespace MedCompanion.Services.Web
{
    /// <summary>
    /// Client API pour les fonctionnalités de recherche web Ollama
    /// Utilise web_search et web_fetch de l'API Ollama
    /// </summary>
    public class OllamaWebSearchService
    {
        private readonly HttpClient _httpClient;
        private readonly SecureStorageService _secureStorage;

        private const string BASE_URL = "https://ollama.com/api";
        private const string STORAGE_KEY = "ollama_web_api_key";

        public OllamaWebSearchService(SecureStorageService secureStorage)
        {
            _secureStorage = secureStorage;
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        /// <summary>
        /// Vérifie si la clé API est configurée
        /// </summary>
        public bool IsConfigured()
        {
            var apiKey = _secureStorage.GetApiKey(STORAGE_KEY);
            return !string.IsNullOrWhiteSpace(apiKey);
        }

        /// <summary>
        /// Configure l'en-tête d'authentification
        /// </summary>
        private void ConfigureAuth()
        {
            var apiKey = _secureStorage.GetApiKey(STORAGE_KEY);
            if (!string.IsNullOrEmpty(apiKey))
            {
                _httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", apiKey);
            }
        }

        /// <summary>
        /// Effectue une recherche web via l'API Ollama
        /// </summary>
        /// <param name="query">Requête de recherche</param>
        /// <param name="maxResults">Nombre maximum de résultats (défaut: 5)</param>
        /// <returns>Tuple (succès, sources, erreur)</returns>
        public async Task<(bool Success, List<WebSource> Sources, string? Error)> SearchAsync(
            string query,
            int maxResults = 5)
        {
            if (!IsConfigured())
            {
                return (false, new List<WebSource>(), "Clé API Ollama non configurée. Allez dans Paramètres > API.");
            }

            try
            {
                ConfigureAuth();

                var requestBody = new
                {
                    query = query,
                    max_results = maxResults
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                System.Diagnostics.Debug.WriteLine($"[OllamaWebSearch] Recherche: {query}");

                var response = await _httpClient.PostAsync($"{BASE_URL}/web_search", content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"[OllamaWebSearch] Erreur {response.StatusCode}: {errorContent}");
                    return (false, new List<WebSource>(), $"Erreur API Ollama: {response.StatusCode}");
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"[OllamaWebSearch] Réponse: {responseJson}");

                var result = JsonSerializer.Deserialize<OllamaSearchResponse>(responseJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Results == null)
                {
                    return (false, new List<WebSource>(), "Format de réponse invalide");
                }

                var sources = new List<WebSource>();
                foreach (var item in result.Results)
                {
                    sources.Add(new WebSource
                    {
                        Title = item.Title ?? "",
                        Url = item.Url ?? "",
                        Snippet = item.Snippet ?? item.Description ?? ""
                    });
                }

                System.Diagnostics.Debug.WriteLine($"[OllamaWebSearch] {sources.Count} résultats trouvés");
                return (true, sources, null);
            }
            catch (TaskCanceledException)
            {
                return (false, new List<WebSource>(), "Timeout: la recherche a pris trop de temps");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OllamaWebSearch] Exception: {ex.Message}");
                return (false, new List<WebSource>(), $"Erreur: {ex.Message}");
            }
        }

        /// <summary>
        /// Récupère le contenu d'une page web via l'API Ollama
        /// </summary>
        /// <param name="url">URL de la page à lire</param>
        /// <returns>Tuple (succès, contenu, erreur)</returns>
        public async Task<(bool Success, string Content, string? Error)> FetchAsync(string url)
        {
            if (!IsConfigured())
            {
                return (false, "", "Clé API Ollama non configurée. Allez dans Paramètres > API.");
            }

            try
            {
                ConfigureAuth();

                var requestBody = new { url = url };
                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                System.Diagnostics.Debug.WriteLine($"[OllamaWebFetch] Lecture: {url}");

                var response = await _httpClient.PostAsync($"{BASE_URL}/web_fetch", content);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"[OllamaWebFetch] Erreur {response.StatusCode}: {errorContent}");
                    return (false, "", $"Erreur API Ollama: {response.StatusCode}");
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<OllamaFetchResponse>(responseJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result == null)
                {
                    return (false, "", "Format de réponse invalide");
                }

                var pageContent = result.Content ?? result.Text ?? "";

                // Limiter la taille du contenu pour éviter les tokens excessifs
                if (pageContent.Length > 15000)
                {
                    pageContent = pageContent.Substring(0, 15000) + "\n\n[... contenu tronqué ...]";
                }

                System.Diagnostics.Debug.WriteLine($"[OllamaWebFetch] Contenu récupéré: {pageContent.Length} caractères");
                return (true, pageContent, null);
            }
            catch (TaskCanceledException)
            {
                return (false, "", "Timeout: la lecture de la page a pris trop de temps");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OllamaWebFetch] Exception: {ex.Message}");
                return (false, "", $"Erreur: {ex.Message}");
            }
        }

        /// <summary>
        /// Teste la connexion à l'API Ollama
        /// </summary>
        public async Task<(bool Success, string Message)> TestConnectionAsync()
        {
            if (!IsConfigured())
            {
                return (false, "Clé API non configurée");
            }

            try
            {
                // Faire une petite recherche de test
                var (success, sources, error) = await SearchAsync("test", 1);
                if (success)
                {
                    return (true, "Connexion réussie");
                }
                return (false, error ?? "Échec de la connexion");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        #region Response Models

        private class OllamaSearchResponse
        {
            public List<OllamaSearchResult>? Results { get; set; }
        }

        private class OllamaSearchResult
        {
            public string? Title { get; set; }
            public string? Url { get; set; }
            public string? Snippet { get; set; }
            public string? Description { get; set; }
        }

        private class OllamaFetchResponse
        {
            public string? Content { get; set; }
            public string? Text { get; set; }
            public string? Title { get; set; }
        }

        #endregion
    }
}
