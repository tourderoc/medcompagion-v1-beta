using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    public class VpsGroupService
    {
        private const string ApiKey = "80aa57b95f53206d7380bfe1b2724bc82ef01cbbcdc278a0f9ac127ba9630828";
        private const string BaseUrl = "https://account.parentaile.fr";

        private readonly HttpClient _http;
        private readonly JsonSerializerOptions _jsonOpts = new() { PropertyNameCaseInsensitive = true };

        public VpsGroupService(AppSettings settings)
        {
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            _http.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        }

        public async Task<(bool success, List<VpsGroup>? groups, string? error)> GetAllGroupsAsync(bool includeEnded = true, int limit = 200)
        {
            try
            {
                var url = $"{BaseUrl}/groupes?include_ended={(includeEnded ? "true" : "false")}&limit={limit}";
                var res = await _http.GetAsync(url);
                if (!res.IsSuccessStatusCode)
                {
                    var body = await res.Content.ReadAsStringAsync();
                    return (false, null, $"Erreur API ({res.StatusCode}): {body}");
                }
                var json = await res.Content.ReadAsStringAsync();
                var list = JsonSerializer.Deserialize<List<VpsGroup>>(json, _jsonOpts);
                return (true, list, null);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        public async Task<(bool success, string? error)> UpdateGroupAsync(string id, Dictionary<string, object?> patch)
        {
            try
            {
                var url = $"{BaseUrl}/groupes/{Uri.EscapeDataString(id)}";
                var body = JsonSerializer.Serialize(patch);
                var req = new HttpRequestMessage(HttpMethod.Put, url)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                var res = await _http.SendAsync(req);
                if (!res.IsSuccessStatusCode)
                {
                    var err = await res.Content.ReadAsStringAsync();
                    return (false, $"Erreur API ({res.StatusCode}): {err}");
                }
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        public async Task<(bool success, string? error)> DeleteGroupAsync(string id)
        {
            try
            {
                var url = $"{BaseUrl}/groupes/{Uri.EscapeDataString(id)}";
                var res = await _http.DeleteAsync(url);
                if (!res.IsSuccessStatusCode)
                {
                    var err = await res.Content.ReadAsStringAsync();
                    return (false, $"Erreur API ({res.StatusCode}): {err}");
                }
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }
    }
}
