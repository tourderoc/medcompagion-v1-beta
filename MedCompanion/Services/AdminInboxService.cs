using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    public class AdminInboxService
    {
        private const string BASE_URL = "https://account.parentaile.fr";
        private const string API_KEY = "80aa57b95f53206d7380bfe1b2724bc82ef01cbbcdc278a0f9ac127ba9630828";

        private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
        private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

        private HttpRequestMessage Req(HttpMethod method, string path, object? body = null)
        {
            var req = new HttpRequestMessage(method, $"{BASE_URL}{path}");
            req.Headers.Add("X-Api-Key", API_KEY);
            if (body != null)
                req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            return req;
        }

        public async Task<(List<AdminInboxItem> Items, string? Error)> GetInboxAsync(
            bool? reviewed = null, string? source = null, int limit = 100, int offset = 0)
        {
            try
            {
                var query = $"?limit={limit}&offset={offset}";
                if (reviewed.HasValue) query += $"&reviewed={reviewed.Value.ToString().ToLower()}";
                if (!string.IsNullOrEmpty(source)) query += $"&source={source}";

                var res = await _http.SendAsync(Req(HttpMethod.Get, $"/admin/inbox{query}"));
                var json = await res.Content.ReadAsStringAsync();
                var items = JsonSerializer.Deserialize<List<AdminInboxItem>>(json, _json) ?? new();
                return (items, null);
            }
            catch (Exception ex)
            {
                return (new(), ex.Message);
            }
        }

        public async Task<(AdminInboxStats? Stats, string? Error)> GetStatsAsync()
        {
            try
            {
                var res = await _http.SendAsync(Req(HttpMethod.Get, "/admin/inbox/stats"));
                var json = await res.Content.ReadAsStringAsync();
                var stats = JsonSerializer.Deserialize<AdminInboxStats>(json, _json);
                return (stats, null);
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }

        public async Task<(bool Ok, string? Error)> MarkReviewedAsync(string src, int id)
        {
            try
            {
                var path = src switch
                {
                    "feedback"   => $"/feedback/{id}/reviewed",
                    "ban_appeal" => $"/ban-appeals/{id}/reviewed",
                    "ban_report" => $"/ban-reports/{id}/reviewed",
                    _ => throw new ArgumentException($"src inconnu: {src}")
                };
                var res = await _http.SendAsync(Req(HttpMethod.Put, path, new { reviewed_by = "medcompanion" }));
                return (res.IsSuccessStatusCode, res.IsSuccessStatusCode ? null : $"HTTP {(int)res.StatusCode}");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public async Task<(bool Ok, string? Error)> DeleteAsync(string src, int id)
        {
            try
            {
                var path = src switch
                {
                    "feedback"   => $"/feedback/{id}",
                    "ban_appeal" => $"/ban-appeals/{id}",
                    "ban_report" => $"/ban-reports/{id}",
                    _ => throw new ArgumentException($"src inconnu: {src}")
                };
                var res = await _http.SendAsync(Req(HttpMethod.Delete, path));
                return (res.IsSuccessStatusCode, res.IsSuccessStatusCode ? null : $"HTTP {(int)res.StatusCode}");
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
