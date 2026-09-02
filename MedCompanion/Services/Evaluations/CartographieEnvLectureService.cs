using System;
using System.Collections.Generic;
using System.IO;
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
    /// Lecture automatique de la feuille « Cartographie de l'environnement », remplie à la main
    /// par le parent en salle d'attente puis scannée.
    ///
    /// Même méthode que la feuille de l'enfant : géométrie LUE sur le gabarit, découpe bloc par
    /// bloc, lecture de chaque bloc par le modèle vision sous schéma contraint.
    ///
    /// UNE DIFFÉRENCE QUI COMPTE — les blocs ont ici des tailles inégales (5, 6, 9 et 2 items),
    /// parce que le partage parent/médecin ne tombe pas au même endroit dans chaque feuille. Le
    /// nombre de lignes d'un bloc est donc lu dans la carte de coordonnées (<c>nb</c>), jamais
    /// supposé. Coder « six lignes » comme pour la feuille de l'enfant ferait inventer au modèle
    /// quatre réponses dans « Cadre &amp; repères », qui n'en porte que deux.
    ///
    /// Rien de ce qui sort d'ici n'est réputé juste : le résultat pré-remplit le dépouillement,
    /// que le médecin vérifie sur l'image. Jamais un enregistrement direct.
    /// </summary>
    public class CartographieEnvLectureService
    {
        private readonly LlamaCppProvider       _vision = new();
        private readonly EdgeHeadlessPdfService _pdf    = new();

        /// <summary>
        /// Marge autour de chaque bloc découpé (mm). Absorbe un léger décalage de calage sans
        /// mordre sur le bloc voisin — les blocs sont espacés de 2 mm.
        /// </summary>
        private const double MargeBlocMm = 1.0;

        private static string TemplatePath => Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Resources", "Formulaires", "questionnaire_environnement.html");

        /// <summary>Bornes verticales ET nombre de lignes, mesurés sur le gabarit.</summary>
        private static Dictionary<string, (double y0, double y1, int nb)>? _blocsCache;

        public bool EstDisponible => _pdf.IsAvailable && File.Exists(TemplatePath);

        /// <summary>Qui a rempli la feuille, lu dans le bandeau du haut.</summary>
        public record Informateur(string? Qui, string? Nom);

        /// <summary>
        /// Lit les 22 réponses de la feuille parent, par clé de feuille.
        ///
        /// Chaque tableau a exactement la longueur du bloc correspondant, et vaut
        /// <see cref="ReponseItem.NonRepondu"/> partout où le modèle n'a rien vu de net. Une case
        /// douteuse est laissée vide plutôt que devinée : un « non » inventé sur un item favorable
        /// est un signal clinique qui n'existe pas.
        /// </summary>
        public async Task<(bool ok, Dictionary<string, ReponseItem[]> lecture, Informateur? informateur, string? error)> LireAsync(
            string scanPath)
        {
            var vide = new Dictionary<string, ReponseItem[]>();

            if (!File.Exists(scanPath))
                return (false, vide, null, "Feuille scannée introuvable.");

            var blocs = await ChargerBlocsAsync();
            if (blocs == null || blocs.Count == 0)
                return (false, vide, null, "Carte de coordonnées du gabarit illisible — lecture impossible.");

            byte[] page;
            try { page = ChargerPage(scanPath); }
            catch (Exception ex) { return (false, vide, null, $"Image illisible : {ex.Message}"); }

            var (_, hauteurPx) = ImageOps.GetSize(page);
            var mmToPx = hauteurPx / 297.0;

            var resultat = new Dictionary<string, ReponseItem[]>();
            var echecs   = new List<string>();

            // Qui a rempli la feuille — lu avant les blocs : c'est ce qui donne leur portée aux
            // réponses qui suivent.
            Informateur? informateur = null;
            if (blocs.TryGetValue("zone_informateur", out var zi))
                informateur = await LireInformateurAsync(Decouper(page, zi.y0, zi.y1, mmToPx));

            foreach (var feuille in CartographieEnvironnementV2.Feuilles)
            {
                var attendu = 0;
                foreach (var _ in feuille.ItemsParent) attendu++;
                if (attendu == 0) continue;   // feuille entièrement cotée par le médecin

                // Le motif d'échec est nommé, et pas seulement la feuille : quatre libellés nus ne
                // permettaient pas de distinguer un gabarit sans blocs d'un modèle qui ne répond
                // pas — deux pannes qui ne se réparent pas au même endroit.
                if (!blocs.TryGetValue($"bloc_{feuille.Key}", out var b))
                {
                    echecs.Add($"{feuille.Label} (bloc absent du gabarit)");
                    continue;
                }

                // Désaccord entre le gabarit et le catalogue : on ne devine pas. Un bloc mesuré à
                // 9 lignes alors que le catalogue en attend 5 signifie que l'un des deux a changé
                // sans l'autre — lire quand même décalerait toutes les réponses d'une feuille.
                if (b.nb != attendu)
                {
                    echecs.Add($"{feuille.Label} (gabarit {b.nb} lignes, catalogue {attendu})");
                    continue;
                }

                var crop = Decouper(page, b.y0, b.y1, mmToPx);
                var (ok, reponses, err) = await LireBlocAsync(crop, feuille.Label, b.nb);
                if (!ok) { echecs.Add($"{feuille.Label} ({err})"); continue; }

                resultat[feuille.Key] = reponses;
            }

            if (resultat.Count == 0)
                return (false, vide, informateur, "Aucun bloc n'a pu être lu. " + string.Join(" · ", echecs));

            var message = echecs.Count > 0
                ? "Blocs non lus : " + string.Join(" · ", echecs)
                : null;

            return (true, resultat, informateur, message);
        }

        /// <summary>
        /// Lit le bandeau « Qui remplit ce questionnaire ? ». Échec silencieux — un informateur
        /// non lu se saisit à la main, il ne doit pas faire tomber la lecture des réponses.
        /// </summary>
        private async Task<Informateur?> LireInformateurAsync(byte[] crop)
        {
            var prompt = new StringBuilder();
            prompt.AppendLine("Cette image est le bandeau d'un questionnaire papier : « Qui remplit ce questionnaire ? »");
            prompt.AppendLine("Il porte trois cases à cocher — Mère, Père, Autre — puis une ligne manuscrite « Prénom et lien ».");
            prompt.AppendLine();
            prompt.AppendLine("Indique quelle case porte une marque, et recopie exactement ce qui est écrit sur la ligne.");
            prompt.AppendLine("Utilise null si aucune case n'est marquée, ou si la ligne est vide.");
            prompt.AppendLine();
            prompt.AppendLine("Réponds UNIQUEMENT avec cet objet JSON :");
            prompt.AppendLine("""{ "qui": null, "nom": null }""");
            prompt.AppendLine("où \"qui\" vaut \"mere\", \"pere\", \"autre\" ou null.");

            try
            {
                var (ok, brut, _) = await _vision.AnalyzeImageAsync(prompt.ToString(), crop, maxTokens: 200);
                if (!ok) return null;

                var trouve = Regex.Match(brut ?? "", @"\{[\s\S]*?\}");
                if (!trouve.Success) return null;

                using var doc = JsonDocument.Parse(trouve.Value);
                // Le modele rend parfois la CHAINE "null" au lieu du litteral JSON null : sans ce
                // filtre, le champ « Prenom et lien » affichait le mot « null » a l'ecran, et
                // finissait recopie tel quel dans la fiche de seance.
                string? Lire(string k)
                {
                    if (!doc.RootElement.TryGetProperty(k, out var v)) return null;
                    if (v.ValueKind != JsonValueKind.String) return null;
                    var s = v.GetString()?.Trim();
                    if (string.IsNullOrEmpty(s)) return null;
                    return s.Equals("null", StringComparison.OrdinalIgnoreCase)
                        || s.Equals("none", StringComparison.OrdinalIgnoreCase)
                        || s.Equals("aucun", StringComparison.OrdinalIgnoreCase)
                        ? null : s;
                }

                var qui = Lire("qui")?.ToLowerInvariant();
                if (qui is not ("mere" or "pere" or "autre")) qui = null;
                return new Informateur(qui, Lire("nom"));
            }
            catch { return null; }
        }

        /// <summary>
        /// Bornes verticales (mm) et nombre de lignes de chaque bloc, mesurés sur le gabarit
        /// lui-même. Le gabarit se mesure ; il ne se suppose pas.
        /// </summary>
        private async Task<Dictionary<string, (double y0, double y1, int nb)>?> ChargerBlocsAsync()
        {
            if (_blocsCache != null) return _blocsCache;
            if (!_pdf.IsAvailable || !File.Exists(TemplatePath)) return null;

            // On mesure le gabarit REMPLI de ses blocs, pas le gabarit brut.
            //
            // Les blocs de cette feuille sont construits en C# et posés à la place de {{blocs}} :
            // le gabarit brut n'en contient aucun, et la carte de coordonnées extraite de lui ne
            // rapportait que le bandeau informateur. D'où « Aucun bloc n'a pu être lu » alors que
            // le modèle vision fonctionnait parfaitement — il n'avait rien à découper.
            var html = await QuestionnaireEnvironnementService.ConstruireGabaritMesurableAsync();
            if (html == null) return null;

            var mesurable = Path.Combine(Path.GetTempPath(), "cartoenv_coordmap.html");
            string json;
            try
            {
                await File.WriteAllTextAsync(mesurable, html, Encoding.UTF8);
                var (ok, brut, _) = await _pdf.ExtractCoordMapAsync(mesurable);
                if (!ok) return null;
                json = brut;
            }
            finally
            {
                try { File.Delete(mesurable); } catch { /* nettoyage best-effort */ }
            }

            try
            {
                using var doc = JsonDocument.Parse(json);
                var res = new Dictionary<string, (double, double, int)>();

                foreach (var f in doc.RootElement.GetProperty("fields").EnumerateArray())
                {
                    // Les blocs ET les zones (bandeau informateur) sont des unités de découpe ;
                    // les lignes, non — elles n'existent que pour une éventuelle lecture pixel
                    // par pixel.
                    var type = f.TryGetProperty("type", out var t) ? t.GetString() : null;
                    if (type != "bloc" && type != "zone") continue;

                    var name = f.GetProperty("name").GetString() ?? "";
                    var rect = f.GetProperty("rect");
                    var y    = rect.GetProperty("y").GetDouble();
                    var h    = rect.GetProperty("h").GetDouble();
                    var nb   = f.TryGetProperty("nb", out var n) && n.ValueKind == JsonValueKind.Number
                        ? n.GetInt32() : 0;

                    res[name] = (y, y + h, nb);
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
            byte[] crop, string label, int nb)
        {
            var gabarit = new StringBuilder("{ ");
            for (int i = 1; i <= nb; i++)
                gabarit.Append($"\"{i}\": null{(i < nb ? ", " : "")}");
            gabarit.Append(" }");

            var prompt = new StringBuilder();
            prompt.AppendLine("Cette image est un extrait de questionnaire papier rempli à la main par un parent.");
            prompt.AppendLine($"Il contient exactement {nb} ligne{(nb > 1 ? "s" : "")} numérotée{(nb > 1 ? "s" : "")} de 1 à {nb}.");
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
            prompt.AppendLine(gabarit.ToString());

            var (ok, brut, erreur) = await _vision.AnalyzeImageAsync(prompt.ToString(), crop, maxTokens: 60 + nb * 25);
            if (!ok) return (false, Array.Empty<ReponseItem>(), erreur ?? "échec du modèle");

            var trouve = Regex.Match(brut ?? "", @"\{[\s\S]*?\}");
            if (!trouve.Success) return (false, Array.Empty<ReponseItem>(), "aucun JSON en réponse");

            try
            {
                using var doc = JsonDocument.Parse(trouve.Value);
                var reps = new ReponseItem[nb];
                for (int i = 0; i < nb; i++)
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
