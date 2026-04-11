using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Google.Cloud.Firestore.V1;
using System.Diagnostics;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service de communication avec Firebase Firestore
    /// Utilise l'API REST pour écrire/lire des données sans Admin SDK
    /// </summary>
    public class FirebaseService
    {
        private readonly HttpClient _httpClient;
        private string? _projectId;
        private string? _apiKey;
        private readonly string _configPath;
        private bool _isConfigured;

        // SDK Firestore pour listeners temps réel
        private FirestoreDb? _firestoreDb;
        private bool _isListenerConfigured;

        public bool IsListenerConfigured => _isListenerConfigured;

        public FirebaseService()
        {
            _httpClient = new HttpClient();

            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _configPath = System.IO.Path.Combine(documentsPath, "MedCompanion", "pilotage", "firebase_config.json");

            LoadConfiguration();
            LoadServiceAccount();
        }

        /// <summary>
        /// Charge la configuration Firebase depuis le fichier local
        /// </summary>
        private void LoadConfiguration()
        {
            try
            {
                if (System.IO.File.Exists(_configPath))
                {
                    var json = System.IO.File.ReadAllText(_configPath);
                    var config = JsonSerializer.Deserialize<FirebaseConfig>(json);

                    if (config != null && !string.IsNullOrEmpty(config.ProjectId) && !string.IsNullOrEmpty(config.ApiKey))
                    {
                        _projectId = config.ProjectId;
                        _apiKey = config.ApiKey;
                        _isConfigured = true;
                        System.Diagnostics.Debug.WriteLine($"[FirebaseService] Configuration chargée: {_projectId}");
                    }
                }
                else
                {
                    // Créer un fichier de config exemple
                    CreateExampleConfig();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FirebaseService] Erreur chargement config: {ex.Message}");
                _isConfigured = false;
            }
        }

        /// <summary>
        /// Crée un fichier de configuration exemple
        /// </summary>
        private void CreateExampleConfig()
        {
            try
            {
                var dir = System.IO.Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                {
                    System.IO.Directory.CreateDirectory(dir);
                }

                var exampleConfig = new FirebaseConfig
                {
                    ProjectId = "votre-projet-firebase",
                    ApiKey = "votre-api-key-firebase"
                };

                var json = JsonSerializer.Serialize(exampleConfig, new JsonSerializerOptions { WriteIndented = true });
                System.IO.File.WriteAllText(_configPath, json);

                System.Diagnostics.Debug.WriteLine($"[FirebaseService] Config exemple créée: {_configPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FirebaseService] Erreur création config: {ex.Message}");
            }
        }

        /// <summary>
        /// Vérifie si Firebase REST est configuré
        /// </summary>
        public bool IsConfigured => _isConfigured;

        /// <summary>
        /// Initialise le SDK Firestore depuis le fichier compte de service.
        /// Silencieux si le fichier est absent — l'app bascule sur le polling.
        /// </summary>
        private void LoadServiceAccount()
        {
            try
            {
                var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var saPath = Path.Combine(documentsPath, "MedCompanion", "pilotage", "firebase_service_account.json");

                if (!File.Exists(saPath) || string.IsNullOrEmpty(_projectId)) return;

                var credential = GoogleCredential
                    .FromFile(saPath)
                    .CreateScoped(FirestoreClient.DefaultScopes);

                var builder = new FirestoreClientBuilder { Credential = credential };
                _firestoreDb = FirestoreDb.Create(_projectId, builder.Build());
                _isListenerConfigured = true;

                Debug.WriteLine("[FirebaseService] ✅ Listener Firestore SDK initialisé");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[FirebaseService] ⚠️ SDK non disponible, mode REST: {ex.Message}");
                _isListenerConfigured = false;
            }
        }

        /// <summary>
        /// Parse un DocumentSnapshot Firestore SDK → PatientMessage (mêmes champs que le parsing REST)
        /// </summary>
        private Models.PatientMessage ParseSnapshotDocument(DocumentSnapshot doc)
        {
            return new Models.PatientMessage
            {
                Id = doc.Id,
                TokenId = doc.ContainsField("tokenId") ? doc.GetValue<string>("tokenId") : "",
                Content = doc.ContainsField("content") ? doc.GetValue<string>("content") : "",
                ParentEmail = doc.ContainsField("parentEmail") ? doc.GetValue<string>("parentEmail") : null,
                ChildNickname = doc.ContainsField("childNickname") ? doc.GetValue<string>("childNickname") : "",
                Status = doc.ContainsField("status") ? doc.GetValue<string>("status") : "sent",
                CreatedAt = doc.ContainsField("createdAt")
                    ? doc.GetValue<Timestamp>("createdAt").ToDateTime()
                    : doc.CreateTime?.ToDateTime() ?? DateTime.UtcNow,
                ReplyContent = doc.ContainsField("replyContent") ? doc.GetValue<string>("replyContent") : null,
                RepliedAt = doc.ContainsField("repliedAt")
                    ? doc.GetValue<Timestamp>("repliedAt").ToDateTime()
                    : (DateTime?)null
            };
        }

        /// <summary>
        /// Démarre un listener temps réel sur la collection 'messages'.
        /// Utilise snapshot.Changes pour ne traiter que les ajouts/modifications.
        /// Le premier appel retourne tous les documents existants comme "Added".
        /// </summary>
        public FirestoreChangeListener ListenToMessages(
            Action<List<Models.PatientMessage>> onNewMessages,
            Action<Exception>? onError = null)
        {
            if (_firestoreDb == null) throw new InvalidOperationException("FirestoreDb non initialisé");

            return _firestoreDb.Collection("messages").Listen(
                async (snapshot, ct) =>
                {
                    await System.Threading.Tasks.Task.Yield();
                    try
                    {
                        var messages = new List<Models.PatientMessage>();
                        foreach (var change in snapshot.Changes)
                        {
                            if (change.ChangeType == Google.Cloud.Firestore.DocumentChange.Type.Removed) continue;
                            messages.Add(ParseSnapshotDocument(change.Document));
                        }
                        if (messages.Count > 0) onNewMessages(messages);
                    }
                    catch (Exception ex) { onError?.Invoke(ex); }
                });
        }

        /// <summary>
        /// Démarre un listener temps réel sur la collection 'tokens'.
        /// Retourne le dictionnaire complet tokenId → status à chaque changement.
        /// </summary>
        public FirestoreChangeListener ListenToTokens(
            Action<Dictionary<string, string>> onStatusChange,
            Action<Exception>? onError = null)
        {
            if (_firestoreDb == null) throw new InvalidOperationException("FirestoreDb non initialisé");

            return _firestoreDb.Collection("tokens").Listen(
                async (snapshot, ct) =>
                {
                    await System.Threading.Tasks.Task.Yield();
                    try
                    {
                        var statuses = new Dictionary<string, string>();
                        foreach (var doc in snapshot.Documents)
                            statuses[doc.Id] = doc.ContainsField("status") ? doc.GetValue<string>("status") : "pending";
                        onStatusChange(statuses);
                    }
                    catch (Exception ex) { onError?.Invoke(ex); }
                });
        }

        /// <summary>
        /// Obtient le chemin du fichier de configuration
        /// </summary>
        public string GetConfigPath() => _configPath;

        /// <summary>
        /// Écrit un token dans Firestore pour validation côté Parent'aile
        /// Collection: tokens/{tokenId}
        /// </summary>
        public async Task<(bool Success, string? Error)> WriteTokenAsync(string tokenId, DateTime createdAt)
        {
            if (!_isConfigured)
            {
                return (false, "Firebase non configuré. Modifiez le fichier firebase_config.json");
            }

            try
            {
                var url = $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents/tokens/{tokenId}?key={_apiKey}";

                var document = new
                {
                    fields = new
                    {
                        createdAt = new { timestampValue = createdAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                        status = new { stringValue = "pending" }
                    }
                };

                var json = JsonSerializer.Serialize(document);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // PATCH pour créer ou mettre à jour
                var request = new HttpRequestMessage(new HttpMethod("PATCH"), url)
                {
                    Content = content
                };

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[FirebaseService] ✓ Token {tokenId} écrit sur Firebase");
                    return (true, null);
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"[FirebaseService] ✗ Erreur Firebase: {error}");
                    return (false, $"Erreur Firebase: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FirebaseService] ✗ Exception: {ex.Message}");
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Met à jour le statut d'un token (used, revoked)
        /// </summary>
        public async Task<(bool Success, string? Error)> UpdateTokenStatusAsync(string tokenId, string status)
        {
            if (!_isConfigured)
            {
                return (false, "Firebase non configuré");
            }

            try
            {
                var url = $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents/tokens/{tokenId}?updateMask.fieldPaths=status&key={_apiKey}";

                var document = new
                {
                    fields = new
                    {
                        status = new { stringValue = status }
                    }
                };

                var json = JsonSerializer.Serialize(document);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(new HttpMethod("PATCH"), url)
                {
                    Content = content
                };

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[FirebaseService] ✓ Token {tokenId} status → {status}");
                    return (true, null);
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    return (false, $"Erreur Firebase: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Supprime un token de Firestore
        /// </summary>
        public async Task<(bool Success, string? Error)> DeleteTokenAsync(string tokenId)
        {
            if (!_isConfigured)
            {
                return (false, "Firebase non configuré");
            }

            try
            {
                var url = $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents/tokens/{tokenId}?key={_apiKey}";

                var response = await _httpClient.DeleteAsync(url);

                if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    System.Diagnostics.Debug.WriteLine($"[FirebaseService] ✓ Token {tokenId} supprimé de Firebase");
                    return (true, null);
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    return (false, $"Erreur Firebase: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Récupère les messages de la collection 'messages'.
        /// Si <paramref name="since"/> est fourni, seuls les messages créés après cette date sont retournés (sync incrémentale).
        /// Passer null pour récupérer tous les messages (refresh manuel).
        /// </summary>
        public async Task<(List<Models.PatientMessage> Messages, string? Error)> FetchMessagesAsync(DateTime? since = null)
        {
            if (!_isConfigured) return (new(), "Firebase non configuré");

            try
            {
                // Utilisation de runQuery pour pouvoir trier et filtrer proprement via les index
                var url = $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents:runQuery?key={_apiKey}";

                object query;
                if (since.HasValue)
                {
                    var sinceUtc = since.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
                    query = new
                    {
                        structuredQuery = new
                        {
                            from = new[] { new { collectionId = "messages" } },
                            where = new
                            {
                                fieldFilter = new
                                {
                                    field = new { fieldPath = "createdAt" },
                                    op = "GREATER_THAN_OR_EQUAL",
                                    value = new { timestampValue = sinceUtc }
                                }
                            },
                            orderBy = new[]
                            {
                                new { field = new { fieldPath = "createdAt" }, direction = "DESCENDING" }
                            }
                        }
                    };
                }
                else
                {
                    query = new
                    {
                        structuredQuery = new
                        {
                            from = new[] { new { collectionId = "messages" } },
                            orderBy = new[]
                            {
                                new { field = new { fieldPath = "createdAt" }, direction = "DESCENDING" }
                            }
                        }
                    };
                }

                var jsonQuery = JsonSerializer.Serialize(query);
                var contentQuery = new StringContent(jsonQuery, System.Text.Encoding.UTF8, "application/json");

                System.Diagnostics.Debug.WriteLine($"[FirebaseService] Fetching messages from: {url}");
                var response = await _httpClient.PostAsync(url, contentQuery);

                if (!response.IsSuccessStatusCode)
                {
                    var err = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"[FirebaseService] Error: {response.StatusCode} - {err}");
                    return (new(), $"Erreur Firebase ({response.StatusCode}): {err}");
                }

                var json = await response.Content.ReadAsStringAsync();
                System.Diagnostics.Debug.WriteLine($"[FirebaseService] Response received: {json.Length} bytes");

                // runQuery retourne un tableau d'objets contenant un champ 'document'
                var queryResults = JsonSerializer.Deserialize<List<FirebaseQueryResult>>(json);
                
                var result = new List<Models.PatientMessage>();
                if (queryResults != null)
                {
                    foreach (var item in queryResults)
                    {
                        var doc = item.document;
                        if (doc == null) continue;

                        var msg = new Models.PatientMessage
                        {
                            Id = doc.name.Substring(doc.name.LastIndexOf('/') + 1),
                            TokenId = doc.fields.tokenId?.stringValue ?? "",
                            Content = doc.fields.content?.stringValue ?? "",
                            ParentEmail = doc.fields.parentEmail?.stringValue,
                            ChildNickname = doc.fields.childNickname?.stringValue ?? "",
                            Status = doc.fields.status?.stringValue ?? "sent",
                            CreatedAt = doc.createTime
                        };

                        if (!string.IsNullOrEmpty(doc.fields.createdAt?.timestampValue) &&
                            DateTime.TryParse(doc.fields.createdAt.timestampValue, out var dt))
                        {
                            msg.CreatedAt = dt;
                        }

                        // Réponse du médecin
                        msg.ReplyContent = doc.fields.replyContent?.stringValue;
                        if (!string.IsNullOrEmpty(doc.fields.repliedAt?.timestampValue) &&
                            DateTime.TryParse(doc.fields.repliedAt.timestampValue, out var repliedDt))
                        {
                            msg.RepliedAt = repliedDt;
                        }

                        result.Add(msg);
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[FirebaseService] {result.Count} messages parsés avec succès");
                return (result, null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FirebaseService] Exception: {ex.Message}");
                return (new(), ex.Message);
            }
        }

        // Classe de support pour runQuery
        private class FirebaseQueryResult
        {
            public Models.FirebaseDocument? document { get; set; }
            public string? readTime { get; set; }
        }

        /// <summary>
        /// Met à jour un message avec une réponse et change son statut
        /// </summary>
        public async Task<(bool Success, string? Error)> UpdateMessageReplyAsync(string messageId, string replyContent)
        {
            if (!_isConfigured) return (false, "Firebase non configuré");

            try
            {
                // On met à jour le statut et on ajoute le contenu de la réponse (pour historique côté parent)
                var url = $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents/messages/{messageId}?updateMask.fieldPaths=status&updateMask.fieldPaths=replyContent&updateMask.fieldPaths=repliedAt&key={_apiKey}";

                var document = new
                {
                    fields = new
                    {
                        status = new { stringValue = "replied" },
                        replyContent = new { stringValue = replyContent },
                        repliedAt = new { timestampValue = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") }
                    }
                };

                var json = JsonSerializer.Serialize(document);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(new HttpMethod("PATCH"), url) { Content = content };
                var response = await _httpClient.SendAsync(request);

                return (response.IsSuccessStatusCode, response.IsSuccessStatusCode ? null : await response.Content.ReadAsStringAsync());
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Supprime un message de la collection 'messages' dans Firestore
        /// </summary>
        public async Task<(bool Success, string? Error)> DeleteMessageAsync(string messageId)
        {
            if (!_isConfigured) return (false, "Firebase non configuré");

            try
            {
                var url = $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents/messages/{messageId}?key={_apiKey}";

                var response = await _httpClient.DeleteAsync(url);

                if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    System.Diagnostics.Debug.WriteLine($"[FirebaseService] ✓ Message {messageId} supprimé de Firebase");
                    return (true, null);
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    return (false, $"Erreur Firebase: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Écrit une notification dans la collection 'notifications' pour les parents
        /// </summary>
        public async Task<(bool Success, string? Error)> WriteNotificationAsync(Models.PilotageNotification notification)
        {
            if (!_isConfigured) return (false, "Firebase non configuré");

            try
            {
                var url = $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents/notifications/{notification.Id}?key={_apiKey}";

                var document = new
                {
                    fields = new
                    {
                        type = new { stringValue = notification.Type.ToString() },
                        title = new { stringValue = notification.Title },
                        body = new { stringValue = notification.Body },
                        targetParentId = new { stringValue = notification.TargetParentId },
                        tokenId = new { stringValue = notification.TokenId ?? "" },
                        replyToMessageId = new { stringValue = notification.ReplyToMessageId ?? "" },
                        createdAt = new { timestampValue = notification.CreatedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ") },
                        read = new { booleanValue = false },
                        senderName = new { stringValue = notification.SenderName }
                    }
                };

                var json = JsonSerializer.Serialize(document);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(new HttpMethod("PATCH"), url) { Content = content };
                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[FirebaseService] ✓ Notification {notification.Id} envoyée");
                    return (true, null);
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"[FirebaseService] ✗ Erreur notification: {error}");
                    return (false, $"Erreur Firebase: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FirebaseService] ✗ Exception notification: {ex.Message}");
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Envoie une notification broadcast à tous les parents
        /// Récupère tous les tokens actifs et crée une notification pour chacun
        /// </summary>
        public async Task<(int Sent, int Failed, string? Error)> SendBroadcastNotificationAsync(string title, string body, string senderName)
        {
            if (!_isConfigured) return (0, 0, "Firebase non configuré");

            try
            {
                // Récupérer uniquement les tokens status="used" via runQuery (filtre côté Firestore)
                var queryUrl = $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents:runQuery?key={_apiKey}";
                var query = new
                {
                    structuredQuery = new
                    {
                        from = new[] { new { collectionId = "tokens" } },
                        where = new
                        {
                            fieldFilter = new
                            {
                                field = new { fieldPath = "status" },
                                op = "EQUAL",
                                value = new { stringValue = "used" }
                            }
                        }
                    }
                };

                var queryJson = JsonSerializer.Serialize(query);
                var queryContent = new StringContent(queryJson, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(queryUrl, queryContent);

                if (!response.IsSuccessStatusCode)
                {
                    return (0, 0, "Impossible de récupérer les tokens");
                }

                var json = await response.Content.ReadAsStringAsync();
                var queryResults = JsonSerializer.Deserialize<List<FirebaseQueryResult>>(json);

                int sent = 0, failed = 0;

                if (queryResults != null)
                {
                    foreach (var item in queryResults)
                    {
                        var doc = item.document;
                        if (doc == null) continue;

                        var tokenId = doc.name.Substring(doc.name.LastIndexOf('/') + 1);

                        var notification = new Models.PilotageNotification
                        {
                            Type = Models.NotificationType.Broadcast,
                            Title = title,
                            Body = body,
                            TargetParentId = "all",
                            TokenId = tokenId,
                            SenderName = senderName
                        };

                        var (success, _) = await WriteNotificationAsync(notification);
                        if (success) sent++; else failed++;
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[FirebaseService] Broadcast envoyé: {sent} succès, {failed} échecs");
                return (sent, failed, null);
            }
            catch (Exception ex)
            {
                return (0, 0, ex.Message);
            }
        }

        // Classes de support pour le parsing
        private class FirebaseDocumentList
        {
            public List<FirebaseDocumentItem>? documents { get; set; }
        }

        private class FirebaseDocumentItem
        {
            public string name { get; set; } = "";
            public FirebaseFields? fields { get; set; }
        }

        private class FirebaseFields
        {
            public FirebaseStringValue? status { get; set; }
        }

        private class FirebaseStringValue
        {
            public string? stringValue { get; set; }
        }

        /// <summary>
        /// Récupère les statuts de tous les tokens depuis Firestore
        /// Retourne un dictionnaire tokenId → status ("pending", "used", "revoked")
        /// </summary>
        public async Task<(Dictionary<string, string> TokenStatuses, string? Error)> FetchTokenStatusesAsync(List<string>? knownTokenIds = null)
        {
            if (!_isConfigured) return (new(), "Firebase non configuré");

            try
            {
                var result = new Dictionary<string, string>();

                // Stratégie 1 : lister la collection entière (limitée à 100 documents)
                var url = $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents/tokens?pageSize=100&key={_apiKey}";
                System.Diagnostics.Debug.WriteLine($"[FirebaseService] FetchTokenStatuses: tentative listing collection...");
                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"[FirebaseService] Listing réponse: {json.Length} bytes");
                    var tokensDoc = JsonSerializer.Deserialize<FirebaseDocumentList>(json);

                    if (tokensDoc?.documents != null)
                    {
                        foreach (var doc in tokensDoc.documents)
                        {
                            var tokenId = doc.name.Substring(doc.name.LastIndexOf('/') + 1);
                            var status = doc.fields?.status?.stringValue ?? "pending";
                            result[tokenId] = status;
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"[FirebaseService] {result.Count} token statuses récupérés (listing)");
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    System.Diagnostics.Debug.WriteLine($"[FirebaseService] Listing tokens échoué ({response.StatusCode}): {errorBody}");
                }

                // Stratégie 2 : lire individuellement les tokens manquants
                if (knownTokenIds != null)
                {
                    var missingIds = knownTokenIds.Where(id => !result.ContainsKey(id)).ToList();
                    if (missingIds.Count > 0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[FirebaseService] {missingIds.Count} tokens manquants après listing, lecture individuelle: {string.Join(", ", missingIds)}");

                        foreach (var tokenId in missingIds)
                        {
                            try
                            {
                                var tokenUrl = $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents/tokens/{tokenId}?key={_apiKey}";
                                var tokenResponse = await _httpClient.GetAsync(tokenUrl);

                                if (tokenResponse.IsSuccessStatusCode)
                                {
                                    var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
                                    System.Diagnostics.Debug.WriteLine($"[FirebaseService] Token {tokenId} individuel: {tokenJson}");
                                    var tokenDoc = JsonSerializer.Deserialize<FirebaseDocumentItem>(tokenJson);
                                    var status = tokenDoc?.fields?.status?.stringValue ?? "pending";
                                    result[tokenId] = status;
                                    System.Diagnostics.Debug.WriteLine($"[FirebaseService] Token {tokenId} → status={status}");
                                }
                                else
                                {
                                    System.Diagnostics.Debug.WriteLine($"[FirebaseService] Token {tokenId} lecture échouée: {tokenResponse.StatusCode}");
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[FirebaseService] Token {tokenId} exception: {ex.Message}");
                            }
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[FirebaseService] Total final: {result.Count} token statuses");
                return (result, null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FirebaseService] Erreur FetchTokenStatuses: {ex.Message}");
                return (new(), ex.Message);
            }
        }

        /// <summary>
        /// Récupère les pseudos des parents depuis la collection accounts
        /// Retourne un dictionnaire tokenId → nickname
        /// Parcourt accounts/{uid}/children/{tokenId} pour trouver le lien
        /// </summary>
        public async Task<(Dictionary<string, string> Nicknames, string? Error)> FetchParentNicknamesAsync()
        {
            if (!_isConfigured) return (new(), "Firebase non configuré");

            try
            {
                // Lire tous les comptes parents (limité à 100)
                var url = $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents/accounts?pageSize=100&key={_apiKey}";
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                {
                    return (new(), $"Erreur Firebase ({response.StatusCode})");
                }

                var json = await response.Content.ReadAsStringAsync();
                var accountsDoc = JsonSerializer.Deserialize<FirebaseAccountList>(json);

                var result = new Dictionary<string, string>();
                if (accountsDoc?.documents != null)
                {
                    foreach (var doc in accountsDoc.documents)
                    {
                        var uid = doc.name.Substring(doc.name.LastIndexOf('/') + 1);
                        var pseudo = doc.fields?.pseudo?.stringValue;

                        if (string.IsNullOrEmpty(pseudo)) continue;

                        // Lire les enfants liés à ce compte
                        var childrenUrl = $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents/accounts/{uid}/children?key={_apiKey}";
                        var childrenResponse = await _httpClient.GetAsync(childrenUrl);

                        if (childrenResponse.IsSuccessStatusCode)
                        {
                            var childrenJson = await childrenResponse.Content.ReadAsStringAsync();
                            var childrenDoc = JsonSerializer.Deserialize<FirebaseChildrenList>(childrenJson);

                            if (childrenDoc?.documents != null)
                            {
                                foreach (var child in childrenDoc.documents)
                                {
                                    // Le document ID est le tokenId
                                    var tokenId = child.name.Substring(child.name.LastIndexOf('/') + 1);
                                    var nickname = child.fields?.nickname?.stringValue ?? pseudo;
                                    result[tokenId] = nickname;
                                }
                            }
                        }
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[FirebaseService] {result.Count} parent nicknames récupérés");
                return (result, null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FirebaseService] Erreur FetchParentNicknames: {ex.Message}");
                return (new(), ex.Message);
            }
        }

        // Classes de support pour accounts
        private class FirebaseAccountList
        {
            public List<FirebaseAccountItem>? documents { get; set; }
        }

        private class FirebaseAccountItem
        {
            public string name { get; set; } = "";
            public FirebaseAccountFields? fields { get; set; }
        }

        private class FirebaseAccountFields
        {
            public FirebaseStringValue? pseudo { get; set; }
            public FirebaseStringValue? email { get; set; }
        }

        private class FirebaseChildrenList
        {
            public List<FirebaseChildItem>? documents { get; set; }
        }

        private class FirebaseChildItem
        {
            public string name { get; set; } = "";
            public FirebaseChildFields? fields { get; set; }
        }

        private class FirebaseChildFields
        {
            public FirebaseStringValue? nickname { get; set; }
        }

        /// <summary>
        /// Récupère tous les URLs d'avatars IA configurés par les parents
        /// </summary>
        public async Task<(Dictionary<string, string> AiUrls, string? Error)> FetchAIUrlsAsync()
        {
            if (!_isConfigured) return (new(), "Firebase non configuré");

            try
            {
                var url = $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents/accounts?pageSize=100&key={_apiKey}";
                var response = await _httpClient.GetAsync(url);
                
                if (!response.IsSuccessStatusCode)
                    return (new(), $"Erreur Firebase: {response.StatusCode}");

                var json = await response.Content.ReadAsStringAsync();
                var accountsDoc = JsonSerializer.Deserialize<JsonElement>(json);

                var result = new Dictionary<string, string>();
                if (accountsDoc.TryGetProperty("documents", out var documents))
                {
                    foreach (var doc in documents.EnumerateArray())
                    {
                        var name = doc.GetProperty("name").GetString();
                        var uid = name.Substring(name.LastIndexOf('/') + 1);
                        
                        // Chercher fields -> avatar -> mapValue -> fields -> aiUrl -> stringValue
                        if (doc.TryGetProperty("fields", out var fields) && 
                            fields.TryGetProperty("avatar", out var avatar) &&
                            avatar.TryGetProperty("mapValue", out var mapValue) &&
                            mapValue.TryGetProperty("fields", out var avatarFields) &&
                            avatarFields.TryGetProperty("aiUrl", out var aiUrl))
                        {
                            var urlString = aiUrl.GetProperty("stringValue").GetString();
                            if (!string.IsNullOrEmpty(urlString))
                            {
                                result[uid] = urlString;
                            }
                        }
                    }
                }

                return (result, null);
            }
            catch (Exception ex)
            {
                return (new(), ex.Message);
            }
        }

        /// <summary>
        /// Teste la connexion à Firebase
        /// </summary>
        public async Task<(bool Success, string Message)> TestConnectionAsync()
        {
            if (!_isConfigured)
            {
                return (false, "Firebase non configuré. Modifiez le fichier:\n" + _configPath);
            }

            try
            {
                // Essayer de lire la collection tokens (même vide)
                var url = $"https://firestore.googleapis.com/v1/projects/{_projectId}/databases/(default)/documents/tokens?pageSize=1&key={_apiKey}";

                var response = await _httpClient.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    return (true, $"Connexion Firebase OK\nProjet: {_projectId}");
                }
                else
                {
                    var error = await response.Content.ReadAsStringAsync();
                    return (false, $"Erreur: {response.StatusCode}\n{error}");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Exception: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Configuration Firebase
    /// </summary>
    public class FirebaseConfig
    {
        public string? ProjectId { get; set; }
        public string? ApiKey { get; set; }
    }
}
