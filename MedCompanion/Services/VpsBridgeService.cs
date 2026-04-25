using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    /// <summary>
    /// Client HTTP pour le bridge VPS (tokens, notifications, messages).
    /// Dual-write avec FirebaseService pendant la migration.
    /// </summary>
    public class VpsBridgeService
    {
        private const string BASE_URL = "https://account.parentaile.fr";
        private const string API_KEY = "80aa57b95f53206d7380bfe1b2724bc82ef01cbbcdc278a0f9ac127ba9630828";

        private readonly HttpClient _http;
        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public VpsBridgeService()
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        private HttpRequestMessage Req(HttpMethod method, string path, object? body = null)
        {
            var req = new HttpRequestMessage(method, $"{BASE_URL}{path}");
            req.Headers.Add("X-Api-Key", API_KEY);
            if (body != null)
            {
                req.Content = new StringContent(
                    JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            }
            return req;
        }

        // ============================================
        // TOKENS
        // ============================================

        public async Task<(bool Ok, string? Error)> CreateTokenAsync(string tokenId, string doctorId, string patientId, string patientName)
        {
            try
            {
                var res = await _http.SendAsync(Req(HttpMethod.Post, "/bridge/tokens", new
                {
                    token_id = tokenId,
                    doctor_id = doctorId,
                    patient_id = patientId,
                    patient_name = patientName
                }));

                if (res.IsSuccessStatusCode || (int)res.StatusCode == 409)
                {
                    System.Diagnostics.Debug.WriteLine($"[VpsBridge] Token {tokenId} cree sur VPS");
                    return (true, null);
                }

                var err = await res.Content.ReadAsStringAsync();
                return (false, $"VPS {res.StatusCode}: {err}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VpsBridge] Erreur creation token: {ex.Message}");
                return (false, ex.Message);
            }
        }

        public async Task<(bool Ok, string? Error)> RevokeTokenAsync(string tokenId)
        {
            try
            {
                var res = await _http.SendAsync(Req(HttpMethod.Put, $"/bridge/tokens/{Uri.EscapeDataString(tokenId)}/revoke"));
                System.Diagnostics.Debug.WriteLine($"[VpsBridge] Token {tokenId} revoque: {res.StatusCode}");
                return (res.IsSuccessStatusCode, res.IsSuccessStatusCode ? null : $"VPS {res.StatusCode}");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public async Task<(bool Ok, string? Error)> DeleteTokenAsync(string tokenId)
        {
            try
            {
                var res = await _http.SendAsync(Req(HttpMethod.Delete, $"/bridge/tokens/{Uri.EscapeDataString(tokenId)}"));
                System.Diagnostics.Debug.WriteLine($"[VpsBridge] Token {tokenId} supprime: {res.StatusCode}");
                return (res.IsSuccessStatusCode || res.StatusCode == System.Net.HttpStatusCode.NotFound, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public async Task<(Dictionary<string, BridgeTokenInfo> Tokens, string? Error)> FetchAllTokensAsync()
        {
            try
            {
                var res = await _http.SendAsync(Req(HttpMethod.Get, "/bridge/tokens"));
                if (!res.IsSuccessStatusCode)
                    return (new(), $"VPS {res.StatusCode}");

                var json = await res.Content.ReadAsStringAsync();
                var list = JsonSerializer.Deserialize<List<BridgeTokenRaw>>(json, _jsonOpts);
                var dict = new Dictionary<string, BridgeTokenInfo>();

                if (list != null)
                {
                    foreach (var t in list)
                    {
                        dict[t.token_id ?? ""] = new BridgeTokenInfo
                        {
                            Status = t.status ?? "pending",
                            ParentUid = t.parent_uid,
                            Pseudo = t.pseudo,
                        };
                    }
                }

                return (dict, null);
            }
            catch (Exception ex)
            {
                return (new(), ex.Message);
            }
        }

        // ============================================
        // MESSAGES
        // ============================================

        public async Task<(List<BridgeMessage> Messages, string? Error)> FetchMessagesAsync()
        {
            try
            {
                var res = await _http.SendAsync(Req(HttpMethod.Get, "/bridge/messages"));
                if (!res.IsSuccessStatusCode)
                    return (new(), $"VPS {res.StatusCode}");

                var json = await res.Content.ReadAsStringAsync();
                var list = JsonSerializer.Deserialize<List<BridgeMessage>>(json, _jsonOpts) ?? new();
                return (list, null);
            }
            catch (Exception ex)
            {
                return (new(), ex.Message);
            }
        }

        public async Task<(bool Ok, string? Error)> ReplyToMessageAsync(string messageId, string replyContent, string senderName)
        {
            try
            {
                var res = await _http.SendAsync(Req(HttpMethod.Put,
                    $"/bridge/messages/{Uri.EscapeDataString(messageId)}/reply",
                    new { reply_content = replyContent, sender_name = senderName }));

                if (res.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[VpsBridge] Reponse envoyee pour message {messageId}");
                    return (true, null);
                }

                var err = await res.Content.ReadAsStringAsync();
                return (false, $"VPS {res.StatusCode}: {err}");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public async Task<(bool Ok, string? Error)> DeleteMessageAsync(string messageId)
        {
            try
            {
                var res = await _http.SendAsync(Req(HttpMethod.Delete,
                    $"/bridge/messages/{Uri.EscapeDataString(messageId)}"));
                return (res.IsSuccessStatusCode || res.StatusCode == System.Net.HttpStatusCode.NotFound, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        // ============================================
        // NOTIFICATIONS
        // ============================================

        public async Task<(bool Ok, string? Error)> SendNotificationAsync(PilotageNotification notif)
        {
            try
            {
                var res = await _http.SendAsync(Req(HttpMethod.Post, "/bridge/notifications", new
                {
                    type = notif.Type.ToString(),
                    title = notif.Title,
                    body = notif.Body,
                    target_parent_id = notif.TargetParentId,
                    token_id = notif.TokenId ?? "",
                    reply_to_message_id = notif.ReplyToMessageId ?? "",
                    sender_name = notif.SenderName
                }));

                if (res.IsSuccessStatusCode)
                {
                    System.Diagnostics.Debug.WriteLine($"[VpsBridge] Notification envoyee: {notif.Title}");
                    return (true, null);
                }

                var err = await res.Content.ReadAsStringAsync();
                return (false, $"VPS {res.StatusCode}: {err}");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public async Task<(bool Ok, string? Error)> SendBroadcastAsync(string title, string body, string senderName)
        {
            try
            {
                var (tokens, tokenErr) = await FetchAllTokensAsync();
                if (tokenErr != null) return (false, tokenErr);

                int sent = 0;
                foreach (var kvp in tokens)
                {
                    if (kvp.Value.Status != "used") continue;

                    var notif = new PilotageNotification
                    {
                        Type = NotificationType.Broadcast,
                        Title = title,
                        Body = body,
                        TargetParentId = "all",
                        TokenId = kvp.Key,
                        SenderName = senderName
                    };

                    var (ok, _) = await SendNotificationAsync(notif);
                    if (ok) sent++;
                }

                System.Diagnostics.Debug.WriteLine($"[VpsBridge] Broadcast: {sent} notifications envoyees");
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        // ============================================
        // DTOs
        // ============================================

        public class BridgeTokenRaw
        {
            public string? token_id { get; set; }
            public string? doctor_id { get; set; }
            public string? patient_id { get; set; }
            public string? patient_name { get; set; }
            public string? status { get; set; }
            public string? parent_uid { get; set; }
            public string? pseudo { get; set; }
            public string? fcm_token { get; set; }
        }

        public class BridgeTokenInfo
        {
            public string Status { get; set; } = "pending";
            public string? ParentUid { get; set; }
            public string? Pseudo { get; set; }
        }

        public class BridgeMessage
        {
            public string id { get; set; } = "";
            public string? token_id { get; set; }
            public string? doctor_id { get; set; }
            public string? parent_uid { get; set; }
            public string? parent_email { get; set; }
            public string? child_nickname { get; set; }
            public string? content { get; set; }
            public string? urgency { get; set; }
            public string? ai_summary { get; set; }
            public string? status { get; set; }
            public string? reply_content { get; set; }
            public string? replied_at { get; set; }
            public string? created_at { get; set; }
        }
    }
}
