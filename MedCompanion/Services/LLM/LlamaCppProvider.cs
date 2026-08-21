using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace MedCompanion.Services.LLM
{
    /// <summary>
    /// Provider LLM pour llama-server (API compatible OpenAI), utilisé pour
    /// les modèles décrits par <see cref="LlamaCppProfiles"/> (Qwen pour le raisonnement, Gemma 4 pour
    /// le volume). Voir <see cref="LlamaCppServerManager"/> pour la gestion du process.
    /// </summary>
    public class LlamaCppProvider : ILLMService
    {
        /// <summary>Profil servi actuellement — c'est lui qui porte le nom, les chemins et les
        /// capacités du modèle. Voir <see cref="LlamaCppProfiles"/>.</summary>
        private LlamaCppModelProfile Profile => LlamaCppServerManager.CurrentProfile;

        private string ModelDisplayName => Profile.DisplayName;

        /// <summary>
        /// Sélectionne le modèle à servir. Sans effet immédiat : le serveur redémarre au prochain
        /// appel, quand EnsureRunningAsync constate le changement de profil (~6-7 s).
        /// </summary>
        public void SetProfile(LlamaCppModelProfile profile)
        {
            LlamaCppServerManager.CurrentProfile = profile;

            // Un modèle qui ne raisonne pas ne doit pas se voir imposer un niveau hérité du
            // précédent : le template rejetterait la requête (erreur 500).
            if (!profile.SupportsReasoning)
                _reasoningEffort = null;
        }

        private readonly HttpClient _httpClient;

        private string? _reasoningEffort;

        /// <summary>
        /// Niveau de réflexion : "minimal" | "low" | "medium" | "high", transmis par requête via le
        /// champ OpenAI standard "reasoning_effort" que le chat template sait interpréter — ou
        /// <see cref="ReasoningLevels.Off"/> pour couper la réflexion.
        ///
        /// Couper n'est PAS un niveau : c'est un drapeau de démarrage du serveur. Le passage à "off"
        /// (ou le retour) impose donc un redémarrage de llama-server, déclenché automatiquement au
        /// prochain appel par EnsureRunningAsync qui détecte le changement de mode.
        /// </summary>
        public string? ReasoningEffort
        {
            get => _reasoningEffort;
            set
            {
                _reasoningEffort = value;
                LlamaCppServerManager.ReasoningEnabled = value != ReasoningLevels.Off;
            }
        }

        public LlamaCppProvider()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(6) };
        }

        public string GetProviderName() => "LlamaCpp";
        public string GetModelName() => ModelDisplayName;
        public bool IsConfigured() => true;

        public async Task<(bool isConnected, string message)> CheckConnectionAsync()
        {
            var (ok, msg) = await LlamaCppServerManager.EnsureRunningAsync();
            return (ok, ok ? $"llama.cpp connecté ({Profile.DisplayName})" : msg);
        }

        public async Task<(bool success, string message)> WarmupAsync()
            => await LlamaCppServerManager.EnsureRunningAsync();

        /// <summary>
        /// Publie le débit de génération à partir du bloc <c>usage</c> de la réponse. La durée
        /// mesurée côté client inclut le traitement du prompt, qu'on retranche via <c>timings</c>
        /// quand llama-server le fournit — sinon le débit affiché serait sous-estimé sur les longs
        /// contextes (une synthèse avec 3500 tokens de prompt perd ~5 s avant le premier token).
        /// </summary>
        private void ReportThroughput(JsonElement root, double elapsedSeconds)
        {
            try
            {
                if (!root.TryGetProperty("usage", out var usage)) return;
                if (!usage.TryGetProperty("completion_tokens", out var ct)) return;
                var tokens = ct.GetInt32();

                var genSeconds = elapsedSeconds;
                if (root.TryGetProperty("timings", out var timings) &&
                    timings.TryGetProperty("predicted_ms", out var predictedMs))
                {
                    genSeconds = predictedMs.GetDouble() / 1000.0;
                }

                LlmThroughputMonitor.Report(ModelDisplayName, tokens, genSeconds);
            }
            catch { /* métrique d'affichage : jamais bloquant */ }
        }

        /// <summary>Décharger = arrêter llama-server : c'est le seul moyen de rendre la VRAM ici
        /// (contrairement à Ollama, le serveur garde le modèle résident tant qu'il tourne). Utilisé
        /// avant une dictée pour laisser toute la carte à Whisper. Le serveur redémarre tout seul
        /// au prochain appel via EnsureRunningAsync.</summary>
        public async Task<(bool success, string message)> UnloadAsync()
        {
            // Off UI thread : Stop() attend le verrou puis la mort du process (jusqu'à quelques secondes).
            await Task.Run(() => LlamaCppServerManager.Stop());
            return (true, "llama.cpp arrêté, VRAM libérée (redémarrage auto au prochain appel).");
        }

        public async Task<(bool success, string result, string? error)> GenerateTextAsync(
            string prompt,
            int maxTokens = 1500,
            System.Threading.CancellationToken cancellationToken = default,
            string? forceModel = null)
            => await ChatAsync("", new List<(string role, string content)> { ("user", prompt) }, maxTokens, cancellationToken);

        public async Task<(bool success, string result, string? error)> ChatAsync(
            string systemPrompt,
            List<(string role, string content)> messages,
            int maxTokens = 1500,
            System.Threading.CancellationToken cancellationToken = default,
            string? forceModel = null,
            int? numCtx = null) // Contexte fixé côté serveur (32768) — non ajustable par requête ici.
        {
            var (ready, readyMsg) = await LlamaCppServerManager.EnsureRunningAsync();
            if (!ready) return (false, "", readyMsg);

            try
            {
                var messagesList = new List<object>();
                if (!string.IsNullOrWhiteSpace(systemPrompt))
                    messagesList.Add(new { role = "system", content = systemPrompt });
                foreach (var (role, content) in messages)
                    messagesList.Add(new { role, content });

                var bodyDict = BuildRequestBody(messagesList, maxTokens, stream: false);

                var json = JsonSerializer.Serialize(bodyDict);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                var sw = System.Diagnostics.Stopwatch.StartNew();
                var response = await _httpClient.PostAsync($"{LlamaCppServerManager.BaseUrl}/v1/chat/completions", httpContent, cancellationToken).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                sw.Stop();

                if (!response.IsSuccessStatusCode)
                    return (false, "", $"Erreur {(int)response.StatusCode}: {responseBody}");

                using var document = JsonDocument.Parse(responseBody);
                var root = document.RootElement;

                ReportThroughput(root, sw.Elapsed.TotalSeconds);

                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var messageContent = choices[0].GetProperty("message").GetProperty("content").GetString();
                    return (true, messageContent ?? "", null);
                }

                return (false, "", "Format de réponse inattendu");
            }
            catch (HttpRequestException ex)
            {
                return (false, "", $"Erreur réseau : {ex.Message}");
            }
            catch (TaskCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("Chat annulé", ex, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                return (false, "", "Timeout - la génération a pris trop de temps");
            }
            catch (Exception ex)
            {
                return (false, "", $"Erreur inattendue : {ex.Message}");
            }
        }

        public async Task<(bool success, string fullResponse, string? error)> ChatStreamAsync(
            string systemPrompt,
            List<(string role, string content)> messages,
            Action<string> onTokenReceived,
            int maxTokens = 1500,
            System.Threading.CancellationToken cancellationToken = default)
        {
            var (ready, readyMsg) = await LlamaCppServerManager.EnsureRunningAsync();
            if (!ready) return (false, "", readyMsg);

            try
            {
                var messagesList = new List<object>();
                if (!string.IsNullOrWhiteSpace(systemPrompt))
                    messagesList.Add(new { role = "system", content = systemPrompt });
                foreach (var (role, content) in messages)
                    messagesList.Add(new { role, content });

                var bodyDict = BuildRequestBody(messagesList, maxTokens, stream: true);

                var json = JsonSerializer.Serialize(bodyDict);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, $"{LlamaCppServerManager.BaseUrl}/v1/chat/completions") { Content = httpContent };
                var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                    return (false, "", $"Erreur {(int)response.StatusCode}");

                var fullResponse = new StringBuilder();
                using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                using var reader = new System.IO.StreamReader(stream);

                while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (string.IsNullOrEmpty(line) || !line.StartsWith("data: ")) continue;

                    var data = line.Substring(6);
                    if (data == "[DONE]") break;

                    try
                    {
                        var doc = JsonDocument.Parse(data);
                        if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                        {
                            var choice = choices[0];
                            if (choice.TryGetProperty("delta", out var delta) && delta.TryGetProperty("content", out var contentProp))
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
                    catch (JsonException)
                    {
                        continue;
                    }
                }

                return (true, fullResponse.ToString(), null);
            }
            catch (HttpRequestException ex)
            {
                return (false, "", $"Erreur réseau : {ex.Message}");
            }
            catch (TaskCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("Streaming annulé", ex, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                return (false, "", "Timeout - le streaming a pris trop de temps");
            }
            catch (Exception ex)
            {
                return (false, "", $"Erreur inattendue : {ex.Message}");
            }
        }

        /// <summary>
        /// Analyse une image via le fichier vision (mmproj) chargé aux côtés du GGUF texte.
        /// Testé fiable sur la lecture de cases à cocher d'un formulaire scanné (8/8 correctes sur
        /// un cas réel), contrairement à GLM-OCR qui échoue sur cette tâche.
        /// </summary>
        public async Task<(bool success, string result, string? error)> AnalyzeImageAsync(
            string prompt,
            byte[] imageData,
            int maxTokens = 1500,
            System.Threading.CancellationToken cancellationToken = default)
        {
            // forVision: true — le MTP (actif par défaut pour le texte) rend le traitement d'image
            // catastrophique sur ce modèle (mesuré : ~280s au lieu de quelques secondes). Le serveur
            // redémarre donc sans MTP le temps de cette requête.
            var (ready, readyMsg) = await LlamaCppServerManager.EnsureRunningAsync(forVision: true);
            if (!ready) return (false, "", readyMsg);

            try
            {
                var base64Image = Convert.ToBase64String(imageData);
                var content = new object[]
                {
                    new { type = "text", text = prompt },
                    new { type = "image_url", image_url = new { url = $"data:image/png;base64,{base64Image}" } }
                };
                var messagesList = new List<object> { new { role = "user", content } };

                var bodyDict = BuildRequestBody(messagesList, maxTokens, stream: false);

                var json = JsonSerializer.Serialize(bodyDict);
                var httpContent = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{LlamaCppServerManager.BaseUrl}/v1/chat/completions", httpContent, cancellationToken).ConfigureAwait(false);
                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                    return (false, "", $"Erreur {(int)response.StatusCode}: {responseBody}");

                using var document = JsonDocument.Parse(responseBody);
                var root = document.RootElement;

                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var messageContent = choices[0].GetProperty("message").GetProperty("content").GetString();
                    return (true, messageContent ?? "", null);
                }

                return (false, "", "Format de réponse inattendu");
            }
            catch (HttpRequestException ex)
            {
                return (false, "", $"Erreur réseau : {ex.Message}");
            }
            catch (TaskCanceledException ex) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException("Analyse image annulée", ex, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                return (false, "", "Timeout - l'analyse a pris trop de temps");
            }
            catch (Exception ex)
            {
                return (false, "", $"Erreur inattendue : {ex.Message}");
            }
        }

        // max_tokens plafonne le TOTAL généré, raisonnement compris — or Qwen3.8 raisonne toujours.
        // Un budget calibré pour la seule réponse (ex. 400 pour l'extraction de puces) était donc
        // intégralement consommé par le bloc de réflexion : le serveur s'arrêtait pile au plafond et
        // renvoyait un content VIDE, que l'appelant signalait par une « Erreur LLM » sans message.
        // Même réserve que OllamaLLMProvider.ReasoningHeadroomTokens.
        private const int ReasoningHeadroomTokens = 2000;

        private Dictionary<string, object> BuildRequestBody(List<object> messagesList, int maxTokens, bool stream)
        {
            var body = new Dictionary<string, object>
            {
                ["model"] = ModelDisplayName,
                ["messages"] = messagesList.ToArray(),
                ["temperature"] = 0.3,
                ["max_tokens"] = maxTokens > 0 ? maxTokens + ReasoningHeadroomTokens : maxTokens,
                ["stream"] = stream
            };
            // "off" n'est pas un niveau de reasoning_effort (les niveaux vont de 'minimal' à 'max') :
            // c'est l'interrupteur serveur --reasoning off, appliqué au démarrage du process. On
            // s'abstient donc d'envoyer le champ, sinon le template recevrait un niveau invalide.
            if (!string.IsNullOrEmpty(ReasoningEffort) && ReasoningEffort != ReasoningLevels.Off)
                body["reasoning_effort"] = ToTemplateEffort(ReasoningEffort!);
            return body;
        }

        /// <summary>
        /// Traduit le niveau logique de l'interface vers le vocabulaire du chat template de ce
        /// modèle, qui n'accepte que <c>low</c>, <c>medium</c> et <c>xhigh</c>. Un niveau inconnu
        /// n'est pas ignoré par le serveur : il lève une exception Jinja et renvoie une erreur 500
        /// ("Unexpected reasoning effort ..."), donc la traduction doit être exhaustive.
        /// Le niveau haut de gpt-oss ("high") correspond ici à "xhigh".
        /// </summary>
        private static string ToTemplateEffort(string logicalLevel) => logicalLevel switch
        {
            ReasoningLevels.High => "xhigh",
            ReasoningLevels.Low or ReasoningLevels.Medium => logicalLevel,
            _ => ReasoningLevels.Medium   // repli sûr pour tout niveau non reconnu
        };

    }
}
