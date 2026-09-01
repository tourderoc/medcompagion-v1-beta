using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using MedCompanion.Models.Evaluations;
using MedCompanion.Services.LLM;
using MedCompanion.Services.Vision;
using MedCompanion.ViewModels;
using PDFtoImage;
using SkiaSharp;

namespace MedCompanion.Services.Evaluations
{
    /// <summary>
    /// Lecture automatique de la feuille du questionnaire parent, remplie à la main puis scannée.
    ///
    /// MÊME MÉTHODE QUE LE FORMULAIRE DE COMPLÉTION, et volontairement :
    ///  1. la géométrie est LUE sur le gabarit (carte de coordonnées extraite par Edge), jamais
    ///     codée en dur — un énoncé qui passerait sur deux lignes décalerait tout ce qui suit ;
    ///  2. la page scannée est découpée bloc par bloc ;
    ///  3. chaque bloc est lu par le modèle vision, sous schéma JSON contraint.
    ///
    /// Un bloc à la fois plutôt que la page entière : sur le formulaire, la lecture pleine page
    /// confondait des champs voisins. Ici, six lignes et deux colonnes suffisent au modèle pour
    /// répondre sans se perdre, et une erreur reste contenue à un axe.
    ///
    /// Rien de tout cela n'est réputé juste : le résultat pré-remplit le dépouillement, que le
    /// médecin vérifie sur l'image. C'est le seul usage prévu — jamais un enregistrement direct.
    /// </summary>
    public class CartographieLectureService
    {
        private readonly LlamaCppProvider      _vision = new();
        private readonly EdgeHeadlessPdfService _pdf   = new();

        /// <summary>
        /// Marge autour de chaque bloc découpé (mm). Absorbe un léger décalage de calage sans
        /// mordre sur le bloc voisin — les blocs sont espacés de 2 mm.
        /// </summary>
        private const double MargeBlocMm = 1.0;

        private static string TemplatePath => Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Resources", "Formulaires", "questionnaire_cartographie.html");

        /// <summary>Carte de coordonnées du gabarit, calculée une fois par session.</summary>
        private static Dictionary<string, (double y0, double y1)>? _blocsCache;

        public bool EstDisponible => _pdf.IsAvailable && File.Exists(TemplatePath);

        /// <summary>
        /// Lit les 30 réponses d'une feuille scannée.
        /// Retourne, par axe, six réponses — <see cref="ReponseItem.NonRepondu"/> partout où le
        /// modèle n'a rien vu de net. Une case douteuse est laissée vide plutôt que devinée :
        /// un « non » inventé coûte un point, donc une couleur, donc peut-être une orientation.
        /// </summary>
        public async Task<(bool ok, Dictionary<string, ReponseItem[]> lecture, string? error)> LireAsync(
            string scanPath)
        {
            var vide = new Dictionary<string, ReponseItem[]>();

            if (!File.Exists(scanPath))
                return (false, vide, "Feuille scannée introuvable.");

            var blocs = await ChargerBlocsAsync();
            if (blocs == null || blocs.Count == 0)
                return (false, vide, "Carte de coordonnées du gabarit illisible — lecture impossible.");

            byte[] page;
            try { page = ChargerPage(scanPath); }
            catch (Exception ex) { return (false, vide, $"Image illisible : {ex.Message}"); }

            var (_, hauteurPx) = ImageOps.GetSize(page);
            var mmToPx = hauteurPx / 297.0;

            var resultat = new Dictionary<string, ReponseItem[]>();
            var echecs   = new List<string>();

            foreach (var axe in CartographieItemsV2.AxeKeys)
            {
                if (!blocs.TryGetValue($"bloc_{axe}", out var bornes)) { echecs.Add(axe); continue; }

                var crop = Decouper(page, bornes.y0, bornes.y1, mmToPx);
                var (ok, reponses, err) = await LireBlocAsync(crop, axe);
                if (!ok) { echecs.Add($"{CartographieItemsV2.AxeLabel(axe)} ({err})"); continue; }

                resultat[axe] = reponses;
            }

            if (resultat.Count == 0)
                return (false, vide, "Aucun bloc n'a pu être lu. " + string.Join(" · ", echecs));

            var message = echecs.Count > 0
                ? "Blocs non lus : " + string.Join(" · ", echecs)
                : null;

            return (true, resultat, message);
        }

        /// <summary>
        /// Bornes verticales (mm) de chaque bloc, mesurées sur le gabarit lui-même.
        /// Elles ne dépendent pas de la tranche d'âge : la géométrie est identique pour les
        /// quatre versions, seul le texte des énoncés change.
        /// </summary>
        private async Task<Dictionary<string, (double y0, double y1)>?> ChargerBlocsAsync()
        {
            if (_blocsCache != null) return _blocsCache;
            if (!_pdf.IsAvailable || !File.Exists(TemplatePath)) return null;

            var (ok, json, _) = await _pdf.ExtractCoordMapAsync(TemplatePath);
            if (!ok) return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var res = new Dictionary<string, (double, double)>();

                foreach (var f in doc.RootElement.GetProperty("fields").EnumerateArray())
                {
                    if (f.TryGetProperty("type", out var t) && t.GetString() != "bloc") continue;
                    var name = f.GetProperty("name").GetString() ?? "";
                    var rect = f.GetProperty("rect");
                    var y    = rect.GetProperty("y").GetDouble();
                    var h    = rect.GetProperty("h").GetDouble();
                    res[name] = (y, y + h);
                }

                _blocsCache = res;
                return res;
            }
            catch { return null; }
        }

        /// <summary>PDF → première page en PNG ; image → telle quelle.</summary>
        private static byte[] ChargerPage(string path)
        {
            if (!path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                return File.ReadAllBytes(path);

            var pdfBytes = File.ReadAllBytes(path);
            using var bitmap = Conversion.ToImage(pdfBytes, 0);
            using var data   = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        private static byte[] Decouper(byte[] page, double yMm0, double yMm1, double mmToPx)
        {
            var (largeur, hauteur) = ImageOps.GetSize(page);
            int y0 = Math.Max(0, (int)((yMm0 - MargeBlocMm) * mmToPx));
            int y1 = Math.Min(hauteur, (int)((yMm1 + MargeBlocMm) * mmToPx));
            return ImageOps.Crop(page, new Int32Rect(0, y0, largeur, Math.Max(1, y1 - y0)));
        }

        private async Task<(bool ok, ReponseItem[] reponses, string? error)> LireBlocAsync(
            byte[] crop, string axeKey)
        {
            var prompt = new StringBuilder();
            prompt.AppendLine("Cette image est un extrait de questionnaire papier rempli à la main par un parent.");
            prompt.AppendLine("Il contient exactement 6 lignes numérotées de 1 à 6.");
            prompt.AppendLine("Chaque ligne se termine par DEUX cases carrées : celle de gauche est OUI, celle de droite est NON.");
            prompt.AppendLine();
            prompt.AppendLine("Pour chaque ligne, indique laquelle des deux cases porte une marque manuscrite");
            prompt.AppendLine("(croix, coche, trait, remplissage) :");
            prompt.AppendLine("  \"oui\"  si la case de GAUCHE est marquée,");
            prompt.AppendLine("  \"non\"  si la case de DROITE est marquée,");
            prompt.AppendLine("  null   si aucune des deux n'est marquée, si les deux le sont, ou en cas de doute.");
            prompt.AppendLine();
            prompt.AppendLine("N'interprète PAS le sens des phrases : regarde uniquement les cases.");
            prompt.AppendLine("Dans le doute, réponds null — une case laissée vide est corrigée à la main,");
            prompt.AppendLine("une case devinée passe inaperçue.");
            prompt.AppendLine();
            prompt.AppendLine("Réponds UNIQUEMENT avec cet objet JSON, sans commentaire ni texte autour :");
            prompt.AppendLine("""{ "1": null, "2": null, "3": null, "4": null, "5": null, "6": null }""");

            var (ok, brut, erreur) = await _vision.AnalyzeImageAsync(prompt.ToString(), crop, maxTokens: 300);
            if (!ok) return (false, Array.Empty<ReponseItem>(), erreur ?? "échec du modèle");

            var trouve = Regex.Match(brut ?? "", @"\{[\s\S]*?\}");
            if (!trouve.Success) return (false, Array.Empty<ReponseItem>(), "aucun JSON en réponse");

            try
            {
                using var doc = JsonDocument.Parse(trouve.Value);
                var reps = new ReponseItem[6];
                for (int i = 0; i < 6; i++)
                {
                    reps[i] = ReponseItem.NonRepondu;
                    if (!doc.RootElement.TryGetProperty((i + 1).ToString(), out var v)) continue;
                    if (v.ValueKind != JsonValueKind.String) continue;

                    var s = v.GetString()?.Trim().ToLowerInvariant();
                    if (s == "oui") reps[i] = ReponseItem.Oui;
                    else if (s == "non") reps[i] = ReponseItem.Non;
                }
                return (true, reps, null);
            }
            catch (Exception ex)
            {
                return (false, Array.Empty<ReponseItem>(), $"JSON illisible : {ex.Message}");
            }
        }
    }
}
