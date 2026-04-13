using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    public class VpsAccountService
    {
        private readonly AppSettings _settings;
        private readonly HttpClient _http;

        public VpsAccountService(AppSettings settings)
        {
            _settings = settings;
            _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        private void ConfigureAuth()
        {
            _http.DefaultRequestHeaders.Remove("X-Api-Key");
            // On utilise la clé configurée pour le service account
            // Dans votre .env VPS c'est ACCOUNT_API_KEY
            // Dans MedCompanion, on s'assure d'utiliser l'URL correcte (port 8001)
            var apiKey = "80aa57b95f53206d7380bfe1b2724bc82ef01cbbcdc278a0f9ac127ba9630828"; 
            _http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        }

        public async Task<(bool success, List<VpsAccount>? accounts, string? error)> GetAllAccountsAsync(int limit = 200)
        {
            try
            {
                ConfigureAuth();
                // L'URL de base pour l'account service (par défaut sur le port 8001)
                var baseUrl = "https://account.parentaile.fr"; 
                var url = $"{baseUrl}/accounts?limit={limit}";

                var response = await _http.GetAsync(url);
                if (!response.IsSuccessStatusCode)
                {
                    var errBody = await response.Content.ReadAsStringAsync();
                    return (false, null, $"Erreur API ({response.StatusCode}): {errBody}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var accounts = JsonSerializer.Deserialize<List<VpsAccount>>(json, options);

                return (true, accounts, null);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }
    }
}
