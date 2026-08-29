using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace MedCompanion.Services
{
    /// <summary>
    /// Ce que l'usage apprend et que l'annuaire officiel ne saura jamais.
    ///
    /// Deux choses seulement, et c'est délibéré — tout le reste vient du socle officiel
    /// (<see cref="EcoleLocaleService"/>) qu'il ne faut pas dupliquer :
    ///
    ///  • Les NOMS D'USAGE. « L'école du centre » ne figure dans aucun jeu de données ; seule la
    ///    consultation apprend qu'elle désigne l'UAI 0831077V. C'est le cas que ni la recherche
    ///    officielle ni une recherche web ne règlent.
    ///
    ///  • Les CORRECTIONS. Quand une adresse mail officielle est périmée et que le médecin la
    ///    rectifie, la rectification doit survivre au prochain téléchargement de l'annuaire.
    ///    Stockée par UAI, elle se réapplique par-dessus la fiche officielle rafraîchie.
    ///
    /// Fichier séparé de l'annuaire à dessein : le socle se remplace en bloc à chaque
    /// actualisation, la surcouche ne se perd jamais.
    /// </summary>
    public class EcoleSurcoucheService
    {
        private readonly string _fichier;
        private Surcouche _donnees = new();

        private static readonly JsonSerializerOptions _opts = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public EcoleSurcoucheService()
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MedCompanion");
            Directory.CreateDirectory(appData);
            _fichier = Path.Combine(appData, "ecoles-perso.json");
            Charger();
        }

        private void Charger()
        {
            try
            {
                if (!File.Exists(_fichier)) return;
                _donnees = JsonSerializer.Deserialize<Surcouche>(
                    File.ReadAllText(_fichier), _opts) ?? new Surcouche();
            }
            catch { _donnees = new Surcouche(); }
        }

        private void Sauver()
        {
            try { File.WriteAllText(_fichier, JsonSerializer.Serialize(_donnees, _opts)); }
            catch { /* la persistance ne doit jamais empêcher la recherche en cours */ }
        }

        // ── Noms d'usage ──────────────────────────────────────────────────────

        /// <summary>
        /// UAI associé à une saisie, ou null. La comparaison passe par la même normalisation que la
        /// recherche : un alias enregistré « école du centre » doit répondre à « Ecole du Centre ».
        /// </summary>
        public string? ResoudreAlias(string saisie)
        {
            var cle = EcoleLocaleService.Normaliser(saisie);
            if (cle.Length == 0) return null;
            return _donnees.Alias.TryGetValue(cle, out var uai) ? uai : null;
        }

        /// <summary>Retient qu'une façon de nommer l'école désigne cet établissement.</summary>
        public void EnregistrerAlias(string saisie, string uai)
        {
            var cle = EcoleLocaleService.Normaliser(saisie);
            if (cle.Length == 0 || string.IsNullOrWhiteSpace(uai)) return;
            _donnees.Alias[cle] = uai;
            Sauver();
        }

        public IReadOnlyDictionary<string, string> Alias => _donnees.Alias;

        public void SupprimerAlias(string saisie)
        {
            var cle = EcoleLocaleService.Normaliser(saisie);
            if (_donnees.Alias.Remove(cle)) Sauver();
        }

        // ── Corrections ───────────────────────────────────────────────────────

        /// <summary>
        /// Rend une COPIE de la fiche officielle avec les corrections appliquées.
        ///
        /// Une copie et non l'original : les fiches viennent du cache mémoire de
        /// <see cref="EcoleLocaleService"/>, partagé par toutes les recherches. Les modifier sur
        /// place corromprait durablement l'annuaire en mémoire, et la fiche officielle ne serait
        /// plus récupérable pour comparer ce que le médecin a réellement changé.
        ///
        /// Seuls les champs effectivement corrigés sont remplacés : une correction du mail ne doit
        /// pas figer un téléphone que l'annuaire aurait mis à jour entre-temps.
        /// </summary>
        public EcoleAnnuaireResult Appliquer(EcoleAnnuaireResult officiel)
        {
            if (string.IsNullOrWhiteSpace(officiel.Uai)) return officiel;
            if (!_donnees.Corrections.TryGetValue(officiel.Uai, out var c)) return officiel;

            return new EcoleAnnuaireResult
            {
                Uai        = officiel.Uai,
                Nom        = officiel.Nom,
                Type       = officiel.Type,
                Statut     = officiel.Statut,
                CodePostal = officiel.CodePostal,
                Commune    = officiel.Commune,
                Adresse    = string.IsNullOrWhiteSpace(c.Adresse)   ? officiel.Adresse   : c.Adresse!,
                Telephone  = string.IsNullOrWhiteSpace(c.Telephone) ? officiel.Telephone : c.Telephone!,
                Email      = string.IsNullOrWhiteSpace(c.Email)     ? officiel.Email     : c.Email!,
                Web        = string.IsNullOrWhiteSpace(c.Web)       ? officiel.Web       : c.Web!
            };
        }

        /// <summary>
        /// Enregistre les écarts entre la fiche officielle et ce que le médecin a saisi.
        /// Seuls les champs RÉELLEMENT différents sont retenus : sans cette comparaison, on
        /// figerait la valeur officielle du jour comme une correction, et un rafraîchissement
        /// ultérieur de l'annuaire n'aurait plus aucun effet sur cette école.
        /// </summary>
        public bool EnregistrerCorrections(
            EcoleAnnuaireResult officiel, string? email, string? telephone, string? adresse, string? web)
        {
            if (string.IsNullOrWhiteSpace(officiel.Uai)) return false;

            var c = new Correction();
            var quelqueChose = false;

            if (Different(email, officiel.Email))         { c.Email     = email?.Trim();     quelqueChose = true; }
            if (Different(telephone, officiel.Telephone)) { c.Telephone = telephone?.Trim(); quelqueChose = true; }
            if (Different(adresse, officiel.Adresse))     { c.Adresse   = adresse?.Trim();   quelqueChose = true; }
            if (Different(web, officiel.Web))             { c.Web       = web?.Trim();       quelqueChose = true; }

            if (quelqueChose) _donnees.Corrections[officiel.Uai] = c;
            else              _donnees.Corrections.Remove(officiel.Uai);   // le médecin est revenu à l'officiel

            Sauver();
            return quelqueChose;
        }

        private static bool Different(string? saisi, string? officiel)
        {
            if (string.IsNullOrWhiteSpace(saisi)) return false;   // champ vide = pas une correction
            return !string.Equals(saisi.Trim(), (officiel ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public bool ADesCorrections(string uai) => _donnees.Corrections.ContainsKey(uai);

        public int NombreAlias       => _donnees.Alias.Count;
        public int NombreCorrections => _donnees.Corrections.Count;

        // ── Stockage ──────────────────────────────────────────────────────────

        private class Surcouche
        {
            /// <summary>Saisie normalisée → UAI.</summary>
            public Dictionary<string, string> Alias { get; set; } = new();

            /// <summary>UAI → champs rectifiés.</summary>
            public Dictionary<string, Correction> Corrections { get; set; } = new();
        }

        private class Correction
        {
            public string? Email { get; set; }
            public string? Telephone { get; set; }
            public string? Adresse { get; set; }
            public string? Web { get; set; }
        }
    }
}
