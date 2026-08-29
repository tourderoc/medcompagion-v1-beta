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
    public class OllamaLLMProvider : ILLMService, IStructuredOutputService
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private string _currentModel;
        private List<string> _availableModels = new();

        // Les modèles à raisonnement (gpt-oss, format harmony) consomment une partie du budget
        // de génération en « thinking » (canal analysis) AVANT de produire la réponse finale.
        // Comme num_predict plafonne le TOTAL (raisonnement + réponse), un budget calibré pour la
        // seule réponse (ex. 450) est épuisé par le raisonnement → réponse tronquée ou vide. On
        // ajoute donc cette réserve à num_predict uniquement quand think=true, pour que la réponse
        // finale ne soit jamais affamée. Sans effet sur les modèles non-raisonnants.
        private const int ReasoningHeadroomTokens = 2000;

        // Gemma 4 12B (et les modèles modernes) supportent 128K tokens.
        // 16384 était trop court pour les transcriptions longues (>145 min ≈ 30 000+ tokens).
        // 65536 couvre ~300 min de transcription et reste raisonnable en VRAM (~4-6 GB de KV cache).
        private const int DefaultNumCtx = 65536;

        /// <summary>
        /// Niveau de réflexion souhaité ("low" | "medium" | "high"), pour les modèles dont le
        /// template expose une variable "reasoning_effort" graduée (gpt-oss, et certains hybrides
        /// communautaires comme les quantizations Qwen3 calquées sur ce même format). Null/vide =
        /// comportement par défaut (think on/off simple selon <see cref="IsGptOssModel"/>).
        /// Ignoré silencieusement si le modèle actif ne supporte pas ce réglage.
        /// </summary>
        public string? ReasoningEffort { get; set; }

        /// <summary>
        /// Vrai si le modèle expose un niveau de réflexion graduable (low/medium/high) via le champ
        /// "think" de l'API Ollama, plutôt qu'un simple on/off.
        /// </summary>
        public static bool SupportsReasoningEffort(string? modelName)
        {
            if (string.IsNullOrEmpty(modelName)) return false;
            return IsGptOssModel(modelName) ||
                   modelName.Contains("Qwen3.8-27B", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>num_predict effectif : ajoute la réserve de raisonnement pour les modèles think=true.</summary>
        private static int EffectiveNumPredict(int maxTokens, bool thinkMode)
            => (thinkMode && maxTokens > 0) ? maxTokens + ReasoningHeadroomTokens : maxTokens;

        // Ce modèle pèse ~12,7 Go rien qu'en poids. À 65536 tokens de contexte, le cache KV ne tient
        // plus dans les 16 Go de VRAM disponibles et force un débordement (mesuré : ~5 t/s au lieu
        // d'une vitesse GPU normale). 16384 reste largement suffisant pour une synthèse ou une
        // conversation Med (quelques milliers de tokens) et libère assez de VRAM pour que le modèle
        // tienne entièrement sur GPU. N'affecte aucun autre modèle.
        private const int Qwen38ReducedNumCtx = 16384;

        private static int EffectiveNumCtx(string? modelName, int requestedCtx)
        {
            if (!string.IsNullOrEmpty(modelName) && modelName.Contains("Qwen3.8-27B", StringComparison.OrdinalIgnoreCase))
                return Math.Min(requestedCtx, Qwen38ReducedNumCtx);
            return requestedCtx;
        }

        public OllamaLLMProvider(string baseUrl = "http://localhost:11434", string defaultModel = "llama3.2:latest")
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _currentModel = defaultModel;
            _httpClient = new HttpClient
            {
                // Timeout de 6 minutes : contexte 64K sur Gemma 4 12B peut prendre plus de 3 min
                Timeout = TimeSpan.FromMinutes(6)
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

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
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
                    var models = await DetectAvailableModelsAsync().ConfigureAwait(false);
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

        /// <summary>
        /// Décharge le modèle de la VRAM. Wipe le KV cache et libère la mémoire GPU.
        /// Le prochain appel paiera 5-10s de rechargement (selon taille du modèle).
        /// Mécanisme Ollama : POST /api/generate avec keep_alive=0 → décharge immédiat.
        /// </summary>
        /// <summary>
        /// Publie le débit de génération. Ollama renvoie les compteurs exacts du moteur —
        /// <c>eval_count</c> (tokens générés) et <c>eval_duration</c> (durée de génération seule, en
        /// nanosecondes) — donc aucune mesure côté client n'est nécessaire, et le temps de traitement
        /// du prompt est déjà exclu.
        /// </summary>
        private static void ReportThroughput(JsonElement root, string model)
        {
            try
            {
                if (!root.TryGetProperty("eval_count", out var evalCount)) return;
                if (!root.TryGetProperty("eval_duration", out var evalDuration)) return;

                LlmThroughputMonitor.Report(model, evalCount.GetInt32(), evalDuration.GetInt64() / 1_000_000_000.0);
            }
            catch { /* métrique d'affichage : jamais bloquant */ }
        }

        public async Task<(bool success, string message)> UnloadAsync()
        {
            try
            {
                var requestBody = new
                {
                    model      = _currentModel,
                    keep_alive = 0,  // 0 = décharge immédiatement après la requête
                    prompt     = "",
                    stream     = false
                };

                var json    = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/generate", content).ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                    return (true, $"Med déchargé ({_currentModel}). Premier appel : ~5-10s de réveil.");

                return (false, $"Échec déchargement : {response.StatusCode}");
            }
            catch (Exception ex)
            {
                return (false, $"Erreur déchargement : {ex.Message}");
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
                    stream = false,
                    think = false
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/generate", content).ConfigureAwait(false);

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
            int maxTokens = 1500,
            System.Threading.CancellationToken cancellationToken = default,
            string? forceModel = null)
        {
            try
            {
                var activeModel = forceModel ?? _currentModel;
                // gpt-oss = format harmony : DOIT raisonner (think=true) sinon channel "final" reste vide.
                // Autres modèles (Gemma 4, etc.) : think=false pour aller plus vite.
                var thinkMode = IsGptOssModel(activeModel);

                // Créer les options selon maxTokens
                object requestBody;

                if (maxTokens <= 0)
                {
                    // Pas de limite de tokens
                    requestBody = new
                    {
                        model = activeModel,
                        prompt = prompt,
                        stream = false,
                        think = thinkMode,
                        options = new
                        {
                            num_ctx = EffectiveNumCtx(activeModel, DefaultNumCtx),
                            temperature = 0.3,
                            num_gpu = 99  // Forcer le maximum de layers sur GPU
                        }
                    };
                }
                else
                {
                    // Limite spécifiée
                    requestBody = new
                    {
                        model = activeModel,
                        prompt = prompt,
                        stream = false,
                        think = thinkMode,
                        options = new
                        {
                            num_predict = EffectiveNumPredict(maxTokens, thinkMode),
                            num_ctx = EffectiveNumCtx(activeModel, DefaultNumCtx),
                            temperature = 0.3,
                            num_gpu = 99  // Forcer le maximum de layers sur GPU
                        }
                    };
                }

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/generate", content, cancellationToken).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

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
            catch (TaskCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                // Annulation demandée par l'utilisateur - propager l'exception
                throw new OperationCanceledException("Génération annulée", ex, cancellationToken);
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

        // ── Sortie structurée (IStructuredOutputService) ──────────────────────

        /// <summary>
        /// Ollama compile le schéma passé dans <c>format</c> en grammaire, comme llama.cpp — c'est
        /// le même moteur en dessous. Disponible sur toutes les versions que l'app cible.
        /// </summary>
        public bool SupportsStructuredOutput => true;

        public async Task<(bool success, string result, string? error)> GenerateJsonAsync(
            string prompt,
            string schemaName,
            string jsonSchema,
            int maxTokens = 1500,
            System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                var activeModel = _currentModel;

                // Le schéma doit partir en objet JSON, pas en chaîne échappée.
                using var schemaDoc = JsonDocument.Parse(jsonSchema);

                var requestBody = new
                {
                    model    = activeModel,
                    messages = new object[] { new { role = "user", content = prompt } },
                    stream   = false,
                    // Pas de réflexion sous contrainte : elle ne pourrait pas être exprimée dans le
                    // schéma, et le budget de tokens n'a donc rien à absorber.
                    think    = false,
                    format   = schemaDoc.RootElement.Clone(),
                    options  = new
                    {
                        num_predict = maxTokens,
                        // On réutilise la fenêtre déjà en place : demander une autre taille forcerait
                        // Ollama à recharger le modèle (plusieurs secondes et un pic de VRAM).
                        num_ctx     = EffectiveNumCtx(activeModel, DefaultNumCtx),
                        temperature = 0.0,
                        num_gpu     = 99
                    }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/chat", httpContent, cancellationToken).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                    return (false, "", $"Erreur {response.StatusCode}: {responseBody}");

                using var doc = JsonDocument.Parse(responseBody);
                ReportThroughput(doc.RootElement, activeModel);

                if (doc.RootElement.TryGetProperty("message", out var messageObj) &&
                    messageObj.TryGetProperty("content", out var contentProp))
                {
                    var content = contentProp.GetString() ?? "";

                    // Ollama signale par done_reason="length" une génération arrêtée au plafond.
                    // Le JSON est alors tronqué : le dire ici évite de faire croire à un problème
                    // de format côté appelant.
                    if (doc.RootElement.TryGetProperty("done_reason", out var reason) &&
                        reason.GetString() == "length")
                    {
                        return (false, content,
                            $"Réponse coupée : le modèle a atteint le plafond de {maxTokens} tokens sans terminer.");
                    }

                    return (true, content, null);
                }

                return (false, "", "Format de réponse inattendu");
            }
            catch (TaskCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("Génération annulée", ex, cancellationToken);
            }
            catch (Exception ex)
            {
                return (false, "", $"Erreur : {ex.Message}");
            }
        }

        public async Task<(bool success, string result, string? error)> ChatAsync(
            string systemPrompt,
            List<(string role, string content)> messages,
            int maxTokens = 1500,
            System.Threading.CancellationToken cancellationToken = default,
            string? forceModel = null,
            int? numCtx = null)
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

                var activeModel = forceModel ?? _currentModel;
                var thinkMode = IsGptOssModel(activeModel);  // gpt-oss : harmony nécessite think=true ; autres : false
                object thinkValue = (!string.IsNullOrEmpty(ReasoningEffort) && ReasoningEffort != ReasoningLevels.Off && SupportsReasoningEffort(activeModel))
                    ? ReasoningEffort
                    : thinkMode;
                var requestBody = new
                {
                    model = activeModel,
                    messages = ollamaMessages.ToArray(),
                    stream = false,
                    think = thinkValue,
                    options = new
                    {
                        num_predict = EffectiveNumPredict(maxTokens, thinkMode),
                        num_ctx = EffectiveNumCtx(activeModel, numCtx ?? DefaultNumCtx),
                        temperature = 0.3,
                        num_gpu = 99  // Forcer le maximum de layers sur GPU
                    }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/chat", httpContent, cancellationToken).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    return (false, "", $"Erreur {response.StatusCode}: {response.ReasonPhrase}");
                }

                var doc = JsonDocument.Parse(responseBody);

                ReportThroughput(doc.RootElement, activeModel);

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
            catch (TaskCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                // Annulation demandée par l'utilisateur - propager l'exception
                throw new OperationCanceledException("Chat annulé", ex, cancellationToken);
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

        public async Task<(bool success, string result, string? error)> AnalyzeImageAsync(
            string prompt, 
            byte[] imageData, 
            int maxTokens = 1500,
            System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                string base64Image = Convert.ToBase64String(imageData);
                
                var requestBody = new
                {
                    model = _currentModel, // S'assurer que le modèle supporte la vision (ex: llava)
                    prompt = prompt,
                    stream = false,
                    think = false,
                    images = new[] { base64Image },
                    options = new
                    {
                        num_predict = maxTokens,
                        temperature = 0.3
                    }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_baseUrl}/api/generate", content, cancellationToken).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    return (false, "", $"Erreur {response.StatusCode}: {responseBody}");
                }

                var doc = JsonDocument.Parse(responseBody);
                
                if (doc.RootElement.TryGetProperty("response", out var responseText))
                {
                    return (true, responseText.GetString() ?? "", null);
                }

                return (false, "", "Format de réponse inattendu");
            }
            catch (Exception ex)
            {
                return (false, "", $"Erreur vision Ollama: {ex.Message}");
            }
        }

        public async Task<(bool success, string fullResponse, string? error)> ChatStreamAsync(
            string systemPrompt,
            List<(string role, string content)> messages,
            Action<string> onTokenReceived,
            int maxTokens = 1500,
            System.Threading.CancellationToken cancellationToken = default)
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

                var thinkMode = IsGptOssModel(_currentModel);  // gpt-oss : harmony nécessite think=true
                object thinkValue = (!string.IsNullOrEmpty(ReasoningEffort) && ReasoningEffort != ReasoningLevels.Off && SupportsReasoningEffort(_currentModel))
                    ? ReasoningEffort
                    : thinkMode;
                var requestBody = new
                {
                    model = _currentModel,
                    messages = ollamaMessages.ToArray(),
                    stream = true, // Activer le streaming
                    think = thinkValue,
                    options = new
                    {
                        num_predict = EffectiveNumPredict(maxTokens, thinkMode),
                        num_ctx = EffectiveNumCtx(_currentModel, DefaultNumCtx),
                        temperature = 0.3,
                        num_gpu = 99  // Forcer le maximum de layers sur GPU
                    }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                // Utiliser SendAsync pour avoir accès au stream de réponse
                var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/chat")
                {
                    Content = httpContent
                };

                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    return (false, "", $"Erreur {response.StatusCode}: {response.ReasonPhrase}");
                }

                var fullResponse = new StringBuilder();

                // Lire le stream ligne par ligne
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var reader = new System.IO.StreamReader(stream);

                while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                {
                var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(line)) continue;

                    try
                    {
                        var doc = JsonDocument.Parse(line);

                        if (doc.RootElement.TryGetProperty("message", out var messageObj))
                        {
                            if (messageObj.TryGetProperty("content", out var contentProp))
                            {
                                var token = contentProp.GetString() ?? "";
                                if (!string.IsNullOrEmpty(token))
                                {
                                    fullResponse.Append(token);
                                    
                                    // Notifier immédiatement le token
                                    onTokenReceived(token);
                                    
                                    // Debug optionnel (très verbeux)
                                    // System.Diagnostics.Debug.Write(token);
                                }
                            }
                        }

                        // Vérifier si c'est le dernier message
                        if (doc.RootElement.TryGetProperty("done", out var doneProp) && doneProp.GetBoolean())
                        {
                            break;
                        }
                    }
                    catch (JsonException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[OllamaStream] Erreur JSON ligne: {ex.Message}");
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

        /// <summary>
        /// gpt-oss utilise le format harmony (channels analysis/final). Il DOIT raisonner :
        /// avec think=false, le channel "final" reste vide → réponse vide côté client.
        /// Couvre gpt-oss:20b, gpt-oss:120b, gpt-oss:*-cloud, etc.
        /// </summary>
        private static bool IsGptOssModel(string? modelName)
            => !string.IsNullOrEmpty(modelName)
               && modelName.StartsWith("gpt-oss", StringComparison.OrdinalIgnoreCase);
    }
}
