using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using MedCompanion.Models.Livres;

namespace MedCompanion.Services.Livres
{
    /// <summary>
    /// CRUD des livres de l'Atelier d'écriture.
    /// Structure disque :
    /// Documents/MedCompanion/livres/&lt;slug&gt;/
    /// ├── livre.json      (métadonnées + mise en page + liste chapitres)
    /// ├── memoire.md      (mémoire narrative utilisée par Med)
    /// └── chapitres/      (un .md par chapitre)
    /// </summary>
    public class LivreService
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly string _livresRoot;

        public LivreService()
        {
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _livresRoot = Path.Combine(documentsPath, "MedCompanion", "livres");
        }

        public string LivresRoot => _livresRoot;

        // ── Livres ──────────────────────────────────────────────────────────

        public (bool success, List<Livre> livres, string? error) ListLivres()
        {
            try
            {
                Directory.CreateDirectory(_livresRoot);
                var livres = new List<Livre>();

                foreach (var dir in Directory.GetDirectories(_livresRoot))
                {
                    var jsonPath = Path.Combine(dir, "livre.json");
                    if (!File.Exists(jsonPath)) continue;

                    try
                    {
                        var livre = JsonSerializer.Deserialize<Livre>(File.ReadAllText(jsonPath, Encoding.UTF8));
                        if (livre == null) continue;
                        livre.DossierPath = dir;
                        livres.Add(livre);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LivreService] livre.json illisible dans {dir} : {ex.Message}");
                    }
                }

                return (true, livres.OrderByDescending(l => l.DateModification).ToList(), null);
            }
            catch (Exception ex)
            {
                return (false, new List<Livre>(), $"Erreur lecture bibliothèque : {ex.Message}");
            }
        }

        public (bool success, Livre? livre, string? error) CreateLivre(string titre, string auteur)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(titre))
                    return (false, null, "Le titre du livre est requis.");

                var slug = Slugify(titre);
                var dir = Path.Combine(_livresRoot, slug);

                // Suffixe numérique si un livre du même nom existe déjà
                int i = 2;
                while (Directory.Exists(dir))
                    dir = Path.Combine(_livresRoot, $"{slug}_{i++}");

                Directory.CreateDirectory(Path.Combine(dir, "chapitres"));

                var livre = new Livre
                {
                    Titre = titre.Trim(),
                    Auteur = auteur?.Trim() ?? "",
                    DossierPath = dir
                };

                var (ok, err) = SaveLivre(livre);
                return ok ? (true, livre, null) : (false, null, err);
            }
            catch (Exception ex)
            {
                return (false, null, $"Erreur création livre : {ex.Message}");
            }
        }

        public (bool success, string? error) SaveLivre(Livre livre)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(livre.DossierPath))
                    return (false, "Chemin du livre non défini.");

                livre.DateModification = DateTime.Now;
                Directory.CreateDirectory(livre.DossierPath);
                File.WriteAllText(
                    Path.Combine(livre.DossierPath, "livre.json"),
                    JsonSerializer.Serialize(livre, _jsonOptions),
                    Encoding.UTF8);
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, $"Erreur sauvegarde livre : {ex.Message}");
            }
        }

        // ── Chapitres ───────────────────────────────────────────────────────

        public (bool success, ChapitreLivre? chapitre, string? error) AddChapitre(Livre livre, string titre)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(titre))
                    return (false, null, "Le titre du chapitre est requis.");

                int ordre = livre.Chapitres.Count == 0 ? 1 : livre.Chapitres.Max(c => c.Ordre) + 1;
                var chapitre = new ChapitreLivre
                {
                    Titre = titre.Trim(),
                    Ordre = ordre,
                    Fichier = $"{ordre:D2}_{Slugify(titre)}.md"
                };

                var chapPath = GetChapitrePath(livre, chapitre);
                Directory.CreateDirectory(Path.GetDirectoryName(chapPath)!);
                if (!File.Exists(chapPath))
                    File.WriteAllText(chapPath, "", Encoding.UTF8);

                livre.Chapitres.Add(chapitre);
                var (ok, err) = SaveLivre(livre);
                return ok ? (true, chapitre, null) : (false, null, err);
            }
            catch (Exception ex)
            {
                return (false, null, $"Erreur création chapitre : {ex.Message}");
            }
        }

        public (bool success, string contenu, string? error) LoadChapitre(Livre livre, ChapitreLivre chapitre)
        {
            try
            {
                var path = GetChapitrePath(livre, chapitre);
                return (true, File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "", null);
            }
            catch (Exception ex)
            {
                return (false, "", $"Erreur lecture chapitre : {ex.Message}");
            }
        }

        public (bool success, string? error) SaveChapitre(Livre livre, ChapitreLivre chapitre, string contenu)
        {
            try
            {
                var path = GetChapitrePath(livre, chapitre);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, contenu ?? "", Encoding.UTF8);
                SaveLivre(livre); // met à jour DateModification
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, $"Erreur sauvegarde chapitre : {ex.Message}");
            }
        }

        public (bool success, string? error) DeleteChapitre(Livre livre, ChapitreLivre chapitre)
        {
            try
            {
                var path = GetChapitrePath(livre, chapitre);
                if (File.Exists(path)) File.Delete(path);
                livre.Chapitres.Remove(chapitre);
                return SaveLivre(livre);
            }
            catch (Exception ex)
            {
                return (false, $"Erreur suppression chapitre : {ex.Message}");
            }
        }

        // ── Mémoire du livre ────────────────────────────────────────────────

        public string LoadMemoire(Livre livre)
        {
            try
            {
                var path = Path.Combine(livre.DossierPath, "memoire.md");
                return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
            }
            catch { return ""; }
        }

        public (bool success, string? error) SaveMemoire(Livre livre, string memoire)
        {
            try
            {
                File.WriteAllText(Path.Combine(livre.DossierPath, "memoire.md"), memoire ?? "", Encoding.UTF8);
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, $"Erreur sauvegarde mémoire : {ex.Message}");
            }
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private string GetChapitrePath(Livre livre, ChapitreLivre chapitre)
            => Path.Combine(livre.DossierPath, "chapitres", chapitre.Fichier);

        /// <summary>"Le Mur" → "le-mur" (sans accents ni caractères interdits).</summary>
        private static string Slugify(string input)
        {
            var normalized = input.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in normalized)
            {
                var cat = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (cat == System.Globalization.UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (c == ' ' || c == '-' || c == '_' || c == '\'') sb.Append('-');
            }
            var slug = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "-{2,}", "-").Trim('-');
            return string.IsNullOrEmpty(slug) ? "livre" : slug;
        }
    }
}
