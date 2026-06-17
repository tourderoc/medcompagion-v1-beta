using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MedCompanion.Services
{
    /// <summary>
    /// Un établissement renvoyé par l'Annuaire de l'Éducation Nationale.
    /// </summary>
    public class EcoleAnnuaireResult
    {
        public string Uai { get; set; } = "";
        public string Nom { get; set; } = "";
        public string Type { get; set; } = "";
        public string Statut { get; set; } = "";
        public string Adresse { get; set; } = "";
        public string CodePostal { get; set; } = "";
        public string Commune { get; set; } = "";
        public string Telephone { get; set; } = "";
        public string Email { get; set; } = "";
        public string Web { get; set; } = "";

        /// <summary>Libellé lisible pour distinguer les résultats dans un sélecteur.</summary>
        public string DisplayLabel
        {
            get
            {
                var loc = string.Join(" ", new[] { CodePostal, Commune }.Where(s => !string.IsNullOrWhiteSpace(s)));
                var addr = string.Join(", ", new[] { Adresse, loc }.Where(s => !string.IsNullOrWhiteSpace(s)));
                return string.IsNullOrWhiteSpace(addr) ? Nom : $"{Nom} — {addr}";
            }
        }
    }

    /// <summary>
    /// Interroge l'Annuaire de l'Éducation Nationale (open data officiel, sans clé) pour
    /// récupérer les coordonnées fiables d'un établissement à partir de son nom et de sa commune.
    /// Source : data.education.gouv.fr / dataset fr-en-annuaire-education.
    /// </summary>
    public class EcoleAnnuaireService
    {
        private const string BaseUrl =
            "https://data.education.gouv.fr/api/explore/v2.1/catalog/datasets/fr-en-annuaire-education/records";

        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Recherche des établissements par nom (obligatoire) et commune (optionnelle).
        /// </summary>
        public async Task<(bool ok, List<EcoleAnnuaireResult> results, string? error)> SearchAsync(
            string nom, string? commune, int limit = 15)
        {
            if (string.IsNullOrWhiteSpace(nom))
                return (false, new List<EcoleAnnuaireResult>(), "Indiquez au moins le nom de l'école.");

            // Neutraliser les guillemets qui casseraient la requête ODSQL
            var safeNom = nom.Replace("\"", " ").Trim();
            var where   = $"search(nom_etablissement,\"{safeNom}\")";

            if (!string.IsNullOrWhiteSpace(commune))
            {
                var safeCommune = commune.Replace("\"", " ").Trim();
                where += $" and nom_commune like \"{safeCommune}\"";
            }

            var url = $"{BaseUrl}?where={Uri.EscapeDataString(where)}&limit={limit}";

            try
            {
                using var resp = await _http.GetAsync(url);
                if (!resp.IsSuccessStatusCode)
                    return (false, new List<EcoleAnnuaireResult>(),
                        $"Annuaire indisponible (HTTP {(int)resp.StatusCode}).");

                var json   = await resp.Content.ReadAsStringAsync();
                var parsed = JsonSerializer.Deserialize<AnnuaireResponse>(json, _jsonOpts);

                var list = parsed?.Results?.Select(Map).ToList() ?? new List<EcoleAnnuaireResult>();
                return (true, list, null);
            }
            catch (Exception ex)
            {
                return (false, new List<EcoleAnnuaireResult>(),
                    $"Erreur réseau : {ex.Message}");
            }
        }

        private static EcoleAnnuaireResult Map(AnnuaireRecord r)
        {
            var adresse = string.Join(", ",
                new[] { r.Adresse1, r.Adresse2 }
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s!.Trim()));

            return new EcoleAnnuaireResult
            {
                Uai        = r.Uai ?? "",
                Nom        = r.Nom ?? "",
                Type       = r.Type ?? "",
                Statut     = r.Statut ?? "",
                Adresse    = adresse,
                CodePostal = r.CodePostal ?? "",
                Commune    = r.Commune ?? "",
                Telephone  = r.Telephone ?? "",
                Email      = r.Mail ?? "",
                Web        = r.Web ?? ""
            };
        }

        // ── DTO de désérialisation (champs exacts de l'API) ──────────────────
        private class AnnuaireResponse
        {
            [JsonPropertyName("total_count")] public int TotalCount { get; set; }
            [JsonPropertyName("results")]     public List<AnnuaireRecord> Results { get; set; } = new();
        }

        private class AnnuaireRecord
        {
            [JsonPropertyName("identifiant_de_l_etablissement")] public string? Uai { get; set; }
            [JsonPropertyName("nom_etablissement")]              public string? Nom { get; set; }
            [JsonPropertyName("type_etablissement")]             public string? Type { get; set; }
            [JsonPropertyName("statut_public_prive")]            public string? Statut { get; set; }
            [JsonPropertyName("adresse_1")]                      public string? Adresse1 { get; set; }
            [JsonPropertyName("adresse_2")]                      public string? Adresse2 { get; set; }
            [JsonPropertyName("code_postal")]                    public string? CodePostal { get; set; }
            [JsonPropertyName("nom_commune")]                    public string? Commune { get; set; }
            [JsonPropertyName("telephone")]                      public string? Telephone { get; set; }
            [JsonPropertyName("mail")]                           public string? Mail { get; set; }
            [JsonPropertyName("web")]                            public string? Web { get; set; }
        }
    }
}
