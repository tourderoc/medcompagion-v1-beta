using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using MedCompanion.Models.Infographies;

namespace MedCompanion.Services.Infographies
{
    /// <summary>
    /// Bibliothèque d'infographies remises aux familles.
    /// Structure disque :
    /// Documents/MedCompanion/bibliotheque/infographies/&lt;id&gt;/
    /// ├── fiche.json   (titre, type, sujets, public, statut de relecture)
    /// └── image.png    (l'infographie telle qu'exportée de NotebookLM)
    /// Les fiches supprimées partent dans _corbeille/, jamais effacées.
    /// La sauvegarde vers E: couvre ce dossier (tout Documents/MedCompanion hors patients est copié).
    /// </summary>
    public class InfographieService
    {
        /// <summary>
        /// Un PDF est converti en PNG (première page, 300 ppp) : miniatures, aperçu, impression et
        /// futur HTML de la Restitution travaillent tous sur une image. Le PDF est gardé à côté.
        /// </summary>
        public static readonly string[] ExtensionsAcceptees = { ".pdf", ".png", ".jpg", ".jpeg", ".bmp" };

        public static readonly string[] TypesParDefaut =
            { "Trouble", "Traitement", "Conseils pratiques", "Démarches", "Hygiène de vie" };

        public static readonly string[] PublicsParDefaut =
            { "Parents", "Enfant", "Adolescent", "École" };

        private const string FicheJson = "fiche.json";
        private const string Corbeille = "_corbeille";
        private const string RemisesJson = "infographies_remises.json";
        private const string OriginalPdf = "original.pdf";

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly string _racine;

        public InfographieService()
        {
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _racine = Path.Combine(documentsPath, "MedCompanion", "bibliotheque", "infographies");
        }

        public string Racine => _racine;

        // ── Fiches ──────────────────────────────────────────────────────────

        public (bool success, List<Infographie> fiches, string? error) ListFiches()
        {
            try
            {
                Directory.CreateDirectory(_racine);
                var fiches = new List<Infographie>();

                foreach (var dir in Directory.GetDirectories(_racine))
                {
                    if (Path.GetFileName(dir).StartsWith("_")) continue;
                    var jsonPath = Path.Combine(dir, FicheJson);
                    if (!File.Exists(jsonPath)) continue;

                    try
                    {
                        var fiche = JsonSerializer.Deserialize<Infographie>(File.ReadAllText(jsonPath, Encoding.UTF8));
                        if (fiche == null) continue;
                        fiche.Id = Path.GetFileName(dir);
                        fiche.DossierPath = dir;
                        fiches.Add(fiche);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[InfographieService] fiche.json illisible dans {dir} : {ex.Message}");
                    }
                }

                return (true, fiches.OrderBy(f => f.Titre, StringComparer.CurrentCultureIgnoreCase).ToList(), null);
            }
            catch (Exception ex)
            {
                return (false, new List<Infographie>(), $"Erreur lecture bibliothèque : {ex.Message}");
            }
        }

        /// <summary>
        /// Copie l'image dans la bibliothèque et crée sa fiche, statut « à relire ».
        /// Le titre de départ est tiré du nom du fichier.
        /// </summary>
        public (bool success, Infographie? fiche, string? error) Importer(string cheminImage)
        {
            try
            {
                var ext = Path.GetExtension(cheminImage).ToLowerInvariant();
                if (!ExtensionsAcceptees.Contains(ext))
                    return (false, null, $"Format non pris en charge : {Path.GetFileName(cheminImage)} (PDF, PNG ou JPG attendu).");

                var titre = TitreDepuisNomFichier(cheminImage);
                var slug = Slugify(titre);
                var dir = Path.Combine(_racine, slug);
                int i = 2;
                while (Directory.Exists(dir))
                    dir = Path.Combine(_racine, $"{slug}_{i++}");

                Directory.CreateDirectory(dir);
                string fichier, remarque;
                try
                {
                    (fichier, remarque) = CopierDansFiche(cheminImage, dir);
                }
                catch
                {
                    Directory.Delete(dir, recursive: true); // pas de fiche à moitié créée
                    throw;
                }

                var fiche = new Infographie
                {
                    Titre = titre,
                    Fichier = fichier,
                    Notes = remarque,
                    Id = Path.GetFileName(dir),
                    DossierPath = dir
                };

                var (ok, err) = Enregistrer(fiche);
                return ok ? (true, fiche, null) : (false, null, err);
            }
            catch (Exception ex)
            {
                return (false, null, $"Erreur import : {ex.Message}");
            }
        }

        public (bool success, string? error) Enregistrer(Infographie fiche)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fiche.DossierPath))
                    return (false, "Dossier de la fiche non défini.");

                fiche.DateModification = DateTime.Now;
                Directory.CreateDirectory(fiche.DossierPath);
                File.WriteAllText(
                    Path.Combine(fiche.DossierPath, FicheJson),
                    JsonSerializer.Serialize(fiche, _jsonOptions),
                    Encoding.UTF8);
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, $"Erreur sauvegarde fiche : {ex.Message}");
            }
        }

        /// <summary>
        /// Remplace l'image (version corrigée dans NotebookLM) en gardant le classement.
        /// La fiche repasse « à relire » : ce qui a été validé, c'est l'ancienne image.
        /// </summary>
        public (bool success, string? error) RemplacerImage(Infographie fiche, string cheminImage)
        {
            try
            {
                var ext = Path.GetExtension(cheminImage).ToLowerInvariant();
                if (!ExtensionsAcceptees.Contains(ext))
                    return (false, $"Format non pris en charge : {Path.GetFileName(cheminImage)} (PDF, PNG ou JPG attendu).");

                var ancienne = fiche.ImagePath;
                var (fichier, remarque) = CopierDansFiche(cheminImage, fiche.DossierPath);
                if (!string.Equals(fichier, fiche.Fichier, StringComparison.OrdinalIgnoreCase) && File.Exists(ancienne))
                    File.Delete(ancienne);

                // Un ancien PDF d'origine ne correspond plus à une nouvelle image
                var original = Path.Combine(fiche.DossierPath, OriginalPdf);
                if (ext != ".pdf" && File.Exists(original))
                    File.Delete(original);

                if (remarque.Length > 0 && !fiche.Notes.Contains(remarque))
                    fiche.Notes = string.IsNullOrWhiteSpace(fiche.Notes) ? remarque : fiche.Notes + "\n" + remarque;

                fiche.Fichier = fichier;
                fiche.DateValidation = null;
                return Enregistrer(fiche);
            }
            catch (Exception ex)
            {
                return (false, $"Erreur remplacement image : {ex.Message}");
            }
        }

        /// <summary>Déplace la fiche dans _corbeille/ (récupérable à la main).</summary>
        public (bool success, string? error) Supprimer(Infographie fiche)
        {
            try
            {
                var corbeille = Path.Combine(_racine, Corbeille);
                Directory.CreateDirectory(corbeille);
                var cible = Path.Combine(corbeille, $"{fiche.Id}_{DateTime.Now:yyyyMMdd_HHmmss}");
                Directory.Move(fiche.DossierPath, cible);
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, $"Erreur suppression : {ex.Message}");
            }
        }

        // ── Remises aux patients ────────────────────────────────────────────

        public List<RemiseInfographie> ListRemises(string patientDirectory)
        {
            try
            {
                var path = Path.Combine(patientDirectory, "info_patient", RemisesJson);
                if (!File.Exists(path)) return new List<RemiseInfographie>();
                return JsonSerializer.Deserialize<List<RemiseInfographie>>(File.ReadAllText(path, Encoding.UTF8))
                       ?? new List<RemiseInfographie>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[InfographieService] remises illisibles : {ex.Message}");
                return new List<RemiseInfographie>();
            }
        }

        public (bool success, string? error) EnregistrerRemise(string patientDirectory, Infographie fiche)
        {
            try
            {
                var remises = ListRemises(patientDirectory);
                remises.Add(new RemiseInfographie { Id = fiche.Id, Titre = fiche.Titre, Date = DateTime.Now });

                var dir = Path.Combine(patientDirectory, "info_patient");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, RemisesJson),
                    JsonSerializer.Serialize(remises, _jsonOptions), Encoding.UTF8);
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, $"Erreur enregistrement de la remise : {ex.Message}");
            }
        }

        // ── Utilitaires ─────────────────────────────────────────────────────

        /// <summary>
        /// Place l'image dans le dossier de la fiche et retourne son nom. Un PDF est rendu en PNG
        /// (première page) et gardé sous original.pdf. La remarque signale les pages ignorées.
        /// </summary>
        private static (string fichier, string remarque) CopierDansFiche(string source, string dir)
        {
            var ext = Path.GetExtension(source).ToLowerInvariant();
            if (ext != ".pdf")
            {
                var fichier = "image" + ext;
                File.Copy(source, Path.Combine(dir, fichier), overwrite: true);
                return (fichier, "");
            }

            var pdf = File.ReadAllBytes(source);
            int pages = PDFtoImage.Conversion.GetPageCount(pdf);
            using (var bitmap = PDFtoImage.Conversion.ToImage(pdf, 0))
            using (var png = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100))
            using (var flux = File.Create(Path.Combine(dir, "image.png")))
                png.SaveTo(flux);

            File.Copy(source, Path.Combine(dir, OriginalPdf), overwrite: true);
            var remarque = pages > 1
                ? $"PDF de {pages} pages : seule la première est gardée comme infographie."
                : "";
            return ("image.png", remarque);
        }

        private static string TitreDepuisNomFichier(string chemin)
        {
            var nom = Path.GetFileNameWithoutExtension(chemin).Replace('_', ' ').Replace('-', ' ').Trim();
            nom = System.Text.RegularExpressions.Regex.Replace(nom, " {2,}", " ");
            return string.IsNullOrEmpty(nom) ? "Infographie" : char.ToUpper(nom[0]) + nom[1..];
        }

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
            if (slug.Length > 60) slug = slug[..60].Trim('-');
            return string.IsNullOrEmpty(slug) ? "infographie" : slug;
        }
    }
}
