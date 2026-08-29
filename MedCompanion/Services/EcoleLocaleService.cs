using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MedCompanion.Services
{
    /// <summary>
    /// Copie locale de l'Annuaire de l'Éducation Nationale, interrogée hors ligne.
    ///
    /// POURQUOI EN LOCAL PLUTÔT QU'EN LIGNE — l'API distante filtre la commune par comparaison
    /// littérale : « Vidaubant » au lieu de « Vidauban » fait passer de 1 résultat à 0, et aucune
    /// requête ne rattrape ça. Mesuré le 29/08/2026 sur le cas réel. Avec les données en mémoire on
    /// compare en approximatif (distance d'édition) sur le nom ET sur la commune, ce que l'API ne
    /// sait pas faire. Le problème disparaît en tant que classe, pas au cas par cas.
    ///
    /// POURQUOI EN BLOC PLUTÔT QU'AU FIL DE L'EAU — un cache qui se remplit à l'usage garde un
    /// démarrage à froid permanent : la première école jamais vue exige encore le réseau. Or tout
    /// l'annuaire tient en 25 Mo et se télécharge en un appel. Mesuré : le Var seul = 812
    /// établissements, 298 Ko, 0,6 s. Il n'y a rien à accumuler patiemment.
    ///
    /// Ce que l'usage apprend et que le jeu de données ignore — noms d'usage et corrections du
    /// médecin — vit dans <see cref="EcoleSurcoucheService"/>, par-dessus.
    /// </summary>
    public class EcoleLocaleService
    {
        /// <summary>
        /// Export complet en un appel. Le filtre `select` réduit la charge aux champs exploités :
        /// l'annuaire complet en compte une centaine, on en garde onze.
        /// </summary>
        private const string ExportUrl =
            "https://data.education.gouv.fr/api/explore/v2.1/catalog/datasets/" +
            "fr-en-annuaire-education/exports/json";

        private const string Champs =
            "identifiant_de_l_etablissement,nom_etablissement,type_etablissement,statut_public_prive," +
            "adresse_1,adresse_2,code_postal,nom_commune,telephone,mail,web,code_departement";

        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };

        private readonly string _fichier;
        private readonly string _fichierMeta;

        private List<EcoleAnnuaireResult>? _cache;
        private List<string>? _cacheNormNom;     // index parallèle, normalisé une fois pour toutes
        private List<string>? _cacheNormCommune;

        public EcoleLocaleService()
        {
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MedCompanion");
            Directory.CreateDirectory(appData);

            _fichier     = Path.Combine(appData, "annuaire-education.json");
            _fichierMeta = Path.Combine(appData, "annuaire-education.meta.json");
        }

        public bool EstDisponible => File.Exists(_fichier);

        /// <summary>
        /// Date du téléchargement, ou null si l'annuaire n'a jamais été récupéré. À AFFICHER :
        /// une copie locale sert silencieusement une donnée ancienne, et les coordonnées d'école
        /// bougent (fusions, réorganisations de RPI). Sans cette date, rien ne le signale.
        /// </summary>
        public DateTime? DateTelechargement
        {
            get
            {
                try
                {
                    if (!File.Exists(_fichierMeta)) return null;
                    var meta = JsonSerializer.Deserialize<Meta>(File.ReadAllText(_fichierMeta));
                    return meta?.Date;
                }
                catch { return null; }
            }
        }

        public int NombreEtablissements => _cache?.Count ?? 0;

        // ── Téléchargement ────────────────────────────────────────────────────

        /// <summary>
        /// Récupère l'annuaire complet et remplace la copie locale.
        /// </summary>
        /// <param name="progression">Octets reçus, pour un retour visuel sur les 25 Mo.</param>
        public async Task<(bool ok, int nombre, string? erreur)> TelechargerAsync(
            Action<long>? progression = null)
        {
            var url = $"{ExportUrl}?select={Uri.EscapeDataString(Champs)}";
            var temporaire = _fichier + ".tmp";

            try
            {
                using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                if (!resp.IsSuccessStatusCode)
                    return (false, 0, $"Annuaire indisponible (HTTP {(int)resp.StatusCode}).");

                // Écriture dans un fichier temporaire puis bascule : une coupure réseau en cours de
                // route laisserait sinon un annuaire tronqué à la place d'un annuaire valide.
                await using (var source = await resp.Content.ReadAsStreamAsync())
                await using (var cible = File.Create(temporaire))
                {
                    var tampon = new byte[81920];
                    long total = 0;
                    int lus;
                    while ((lus = await source.ReadAsync(tampon)) > 0)
                    {
                        await cible.WriteAsync(tampon.AsMemory(0, lus));
                        total += lus;
                        progression?.Invoke(total);
                    }
                }

                // Valider le contenu AVANT de remplacer : un JSON illisible remplacerait une copie
                // saine par une copie inutilisable.
                var liste = Charger(temporaire);
                if (liste.Count == 0)
                {
                    File.Delete(temporaire);
                    return (false, 0, "Le fichier reçu ne contient aucun établissement.");
                }

                File.Move(temporaire, _fichier, overwrite: true);
                File.WriteAllText(_fichierMeta,
                    JsonSerializer.Serialize(new Meta { Date = DateTime.Now, Nombre = liste.Count }));

                IndexerEnMemoire(liste);
                return (true, liste.Count, null);
            }
            catch (Exception ex)
            {
                try { if (File.Exists(temporaire)) File.Delete(temporaire); } catch { }
                return (false, 0, $"Téléchargement impossible : {ex.Message}");
            }
        }

        // ── Chargement et index ───────────────────────────────────────────────

        private static List<EcoleAnnuaireResult> Charger(string chemin)
        {
            try
            {
                var json = File.ReadAllText(chemin, Encoding.UTF8);
                var brut = JsonSerializer.Deserialize<List<AnnuaireRecord>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return brut?.Select(Map).Where(e => !string.IsNullOrWhiteSpace(e.Nom)).ToList()
                       ?? new List<EcoleAnnuaireResult>();
            }
            catch { return new List<EcoleAnnuaireResult>(); }
        }

        /// <summary>
        /// Charge l'annuaire en mémoire si ce n'est pas déjà fait. Les formes normalisées sont
        /// calculées ICI, une fois : les recalculer à chaque comparaison ferait ~69 000
        /// normalisations par frappe.
        /// </summary>
        public bool AssurerCharge()
        {
            if (_cache != null) return true;
            if (!File.Exists(_fichier)) return false;

            var liste = Charger(_fichier);
            if (liste.Count == 0) return false;

            IndexerEnMemoire(liste);
            return true;
        }

        private void IndexerEnMemoire(List<EcoleAnnuaireResult> liste)
        {
            _cache            = liste;
            _cacheNormNom     = liste.Select(e => Normaliser(RetirerPrefixe(e.Nom))).ToList();
            _cacheNormCommune = liste.Select(e => Normaliser(e.Commune)).ToList();
        }

        /// <summary>Fiche d'un établissement par son UAI, ou null. Sert la résolution des alias.</summary>
        public EcoleAnnuaireResult? TrouverParUai(string uai)
        {
            if (!AssurerCharge() || _cache == null || string.IsNullOrWhiteSpace(uai)) return null;
            return _cache.FirstOrDefault(e => string.Equals(e.Uai, uai, StringComparison.OrdinalIgnoreCase));
        }

        // ── Recherche approximative ───────────────────────────────────────────

        /// <summary>
        /// Recherche tolérante aux fautes sur le nom ET sur la commune.
        ///
        /// La commune, quand elle est fournie, RESTREINT mais n'EXCLUT jamais : c'est précisément
        /// l'exclusion stricte de l'API distante qui faisait perdre la recherche entière sur une
        /// lettre. Ici une commune fautive dégrade le score, elle ne supprime pas le résultat.
        /// </summary>
        public List<EcoleAnnuaireResult> Rechercher(string nom, string? commune, int limite = 15)
        {
            if (!AssurerCharge() || _cache == null) return new List<EcoleAnnuaireResult>();
            if (string.IsNullOrWhiteSpace(nom)) return new List<EcoleAnnuaireResult>();

            var nomN     = Normaliser(RetirerPrefixe(nom));
            var communeN = Normaliser(commune ?? "");
            if (nomN.Length == 0) return new List<EcoleAnnuaireResult>();

            var scores = new List<(double score, int index)>();

            for (int i = 0; i < _cache.Count; i++)
            {
                var scoreNom = ScoreTexte(nomN, _cacheNormNom![i]);
                if (scoreNom < 0.55) continue;   // le nom reste le critère éliminatoire

                var score = scoreNom;
                if (communeN.Length > 0)
                {
                    // Pondération volontairement asymétrique : une commune qui colle remonte
                    // fortement le résultat, une commune qui ne colle pas ne l'écarte pas.
                    var scoreCommune = ScoreTexte(communeN, _cacheNormCommune![i]);
                    score = scoreNom + scoreCommune * 1.5;
                }

                scores.Add((score, i));
            }

            return scores
                .OrderByDescending(s => s.score)
                .Take(limite)
                .Select(s => _cache[s.index])
                .ToList();
        }

        /// <summary>
        /// Score de 0 à 1 entre une saisie et une valeur de référence.
        /// Un mot de la saisie qui se retrouve tel quel dans la référence vaut 1 (« Michel » dans
        /// « Henri Michel ») ; sinon on tombe sur la similarité d'édition, qui encaisse la faute
        /// de frappe (« Vidaubant » ≈ « Vidauban »).
        /// </summary>
        private static double ScoreTexte(string saisie, string reference)
        {
            if (saisie.Length == 0 || reference.Length == 0) return 0;
            if (reference.Contains(saisie, StringComparison.Ordinal)) return 1.0;

            var motsSaisie = saisie.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var motsRef    = reference.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (motsSaisie.Length == 0 || motsRef.Length == 0) return 0;

            // Chaque mot saisi cherche son meilleur correspondant dans la référence ; la moyenne
            // évite qu'un mot parasite (« école », « groupe ») écrase un nom par ailleurs juste.
            double total = 0;
            foreach (var mot in motsSaisie)
            {
                double meilleur = 0;
                foreach (var refMot in motsRef)
                {
                    var s = SimilariteMot(mot, refMot);
                    if (s > meilleur) meilleur = s;
                }
                total += meilleur;
            }
            return total / motsSaisie.Length;
        }

        private static double SimilariteMot(string a, string b)
        {
            if (a == b) return 1.0;
            if (b.StartsWith(a, StringComparison.Ordinal)) return 0.95;   // saisie tronquée
            if (a.StartsWith(b, StringComparison.Ordinal)) return 0.90;

            var max = Math.Max(a.Length, b.Length);
            if (max == 0) return 0;

            // Au-delà d'un tiers d'écart de longueur, ce ne sont plus les mêmes mots : couper ici
            // évite d'appeler Levenshtein 69 000 fois pour rien.
            if (Math.Abs(a.Length - b.Length) > max / 3 + 1) return 0;

            var distance = Levenshtein(a, b);
            return 1.0 - (double)distance / max;
        }

        private static int Levenshtein(string a, string b)
        {
            var precedent = new int[b.Length + 1];
            var courant   = new int[b.Length + 1];

            for (int j = 0; j <= b.Length; j++) precedent[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                courant[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    var cout = a[i - 1] == b[j - 1] ? 0 : 1;
                    courant[j] = Math.Min(Math.Min(courant[j - 1] + 1, precedent[j] + 1),
                                          precedent[j - 1] + cout);
                }
                (precedent, courant) = (courant, precedent);
            }
            return precedent[b.Length];
        }

        // ── Normalisation ─────────────────────────────────────────────────────

        /// <summary>
        /// Minuscules, sans accent, sans ponctuation. « Saint-Étienne » et « saint etienne »
        /// doivent se comparer comme identiques avant même de parler de faute de frappe.
        /// </summary>
        public static string Normaliser(string? texte)
        {
            if (string.IsNullOrWhiteSpace(texte)) return "";

            var decompose = texte.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decompose.Length);
            var espacePrecedent = false;

            foreach (var c in decompose)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;

                if (char.IsLetterOrDigit(c)) { sb.Append(c); espacePrecedent = false; }
                else if (!espacePrecedent)   { sb.Append(' '); espacePrecedent = true; }
            }
            return sb.ToString().Trim();
        }

        private static readonly string[] Prefixes =
        {
            "ecole primaire", "ecole elementaire", "ecole maternelle", "groupe scolaire",
            "ecole", "college", "lycee", "lpo", "lp"
        };

        /// <summary>
        /// Retire le type d'établissement du nom. Sans ça, « école Henri Michel » comparé à
        /// « Ecole primaire Henri Michel » dilue le score sur des mots qui ne distinguent rien —
        /// et ce sont les mots distinctifs qui doivent décider.
        /// </summary>
        private static string RetirerPrefixe(string? nom)
        {
            var n = Normaliser(nom);
            foreach (var p in Prefixes)
            {
                if (n.StartsWith(p + " ", StringComparison.Ordinal))
                    return n[(p.Length + 1)..];
            }
            return n;
        }

        // ── Correspondance API ────────────────────────────────────────────────

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

        private class Meta
        {
            public DateTime Date { get; set; }
            public int Nombre { get; set; }
        }
    }
}
