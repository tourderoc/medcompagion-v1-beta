using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MedCompanion.Models;
using MedCompanion.Models.Evaluations;

namespace MedCompanion.Services.Evaluations
{
    /// <summary>
    /// Génère la feuille « Cartographie de l'environnement — questionnaire parent », remise au
    /// parent à l'accueil de la 3ᵉ séance et remplie en salle d'attente.
    ///
    /// Elle ne porte QUE les 22 items destinés au parent : les 14 autres mettent en cause celui
    /// qui remplit et sont cotés par le médecin depuis l'entretien.
    ///
    /// Deux différences avec la feuille de l'enfant :
    ///  • pas de déclinaison par tranche d'âge — ces items décrivent le milieu, pas le
    ///    développement, et ne changent pas entre 3 et 11 ans ;
    ///  • les blocs ont des tailles INÉGALES (5 / 6 / 9 / 2 items), au lieu de cinq blocs de six.
    ///    C'est pourquoi les blocs sont construits ici plutôt que figés dans le gabarit, et
    ///    pourquoi la carte de coordonnées les mesure au lieu de les supposer.
    /// </summary>
    public class QuestionnaireEnvironnementService
    {
        private readonly EdgeHeadlessPdfService _pdf = new();

        /// <summary>Version de mise en page. À incrémenter si la géométrie change.</summary>
        public const string Jeton = "MEDCOMP-FORM-CARTOENV-V1";

        public static string TemplatePath => Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Resources", "Formulaires", "questionnaire_environnement.html");

        /// <summary>Emplacement des blocs dans le gabarit — ils n'y sont pas écrits, ils y sont posés.</summary>
        public const string MarqueBlocs = "{{blocs}}";

        /// <summary>
        /// Le gabarit REMPLI de ses blocs, sans les champs patient.
        ///
        /// Existe pour la lecture automatique : les blocs de cette feuille sont construits en C#
        /// et non écrits dans le gabarit, si bien qu'une carte de coordonnées extraite du gabarit
        /// brut ne trouve AUCUN bloc à mesurer — c'est ce qui faisait échouer la lecture des quatre
        /// blocs d'un coup. La géométrie doit donc être mesurée sur la page telle qu'elle est
        /// imprimée, blocs posés.
        ///
        /// Les champs patient sont vidés et non remplis : ils vivent dans un en-tête de hauteur
        /// fixe, leur contenu ne déplace pas les blocs, et un faux nom rendrait la mesure
        /// dépendante d'un patient imaginaire.
        /// </summary>
        public static async Task<string?> ConstruireGabaritMesurableAsync()
        {
            if (!File.Exists(TemplatePath)) return null;

            var html = await File.ReadAllTextAsync(TemplatePath, Encoding.UTF8);
            html = html.Replace(MarqueBlocs, ConstruireBlocs());
            return Regex.Replace(html, @"\{\{\s*[a-zA-Z0-9_]+\s*\}\}", "");
        }

        public bool TemplateExists     => File.Exists(TemplatePath);
        public bool PdfEngineAvailable => _pdf.IsAvailable;

        public async Task<(bool ok, string? pdfPath, string? error)> GenerateAsync(
            PatientMetadata meta, int? age, string outputDir)
        {
            if (!File.Exists(TemplatePath))
                return (false, null,
                    $"Template introuvable. Déposez 'questionnaire_environnement.html' dans :\n{Path.GetDirectoryName(TemplatePath)}");

            if (!_pdf.IsAvailable)
                return (false, null, "Microsoft Edge introuvable — requis pour générer le PDF.");

            try
            {
                var html = await File.ReadAllTextAsync(TemplatePath, Encoding.UTF8);

                html = html.Replace(MarqueBlocs, ConstruireBlocs());
                html = FillPlaceholders(html, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["date_rdv"]      = DateTime.Now.ToString("dd/MM/yyyy"),
                    ["enfant_nom"]    = meta.Nom ?? "",
                    ["enfant_prenom"] = meta.Prenom ?? "",
                    ["enfant_dob"]    = meta.DobFormatted ?? "",
                    ["enfant_age"]    = age?.ToString() ?? "",
                });

                Directory.CreateDirectory(outputDir);
                var stamp   = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
                var tmpHtml = Path.Combine(Path.GetTempPath(), $"cartoenv_{stamp}.html");
                var pdfPath = Path.Combine(outputDir, $"{stamp}_questionnaire_environnement.pdf");

                await File.WriteAllTextAsync(tmpHtml, html, Encoding.UTF8);

                var ok = await _pdf.ConvertAsync(tmpHtml, pdfPath);
                try { File.Delete(tmpHtml); } catch { /* nettoyage best-effort */ }

                return ok
                    ? (true, pdfPath, null)
                    : (false, null, "Échec de la conversion PDF (Edge headless).");
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        /// <summary>
        /// Construit les blocs : une feuille dimensionnelle par bloc, avec ses seuls items parents.
        ///
        /// La numérotation des items suit l'ordre du bloc, pas celui de la feuille complète : le
        /// parent voit « 1 à 5 », pas les trous laissés par les items du médecin. C'est aussi ce
        /// qui rend la relecture simple — la n-ième case d'un bloc est le n-ième item parent de
        /// cette feuille, et rien d'autre.
        /// </summary>
        private static string ConstruireBlocs()
        {
            var sb = new StringBuilder();
            int indexBloc = 0;

            foreach (var feuille in CartographieEnvironnementV2.Feuilles)
            {
                var items = feuille.ItemsParent.ToList();
                if (items.Count == 0) continue;      // feuille entièrement cotée par le médecin
                indexBloc++;

                sb.AppendLine($"    <div class=\"bloc\" data-bloc=\"{feuille.Key}\" data-index=\"{indexBloc}\">");
                sb.AppendLine("      <div class=\"bloc-header\">");
                sb.AppendLine($"        <div class=\"bloc-numero\">{indexBloc}</div>");
                sb.AppendLine($"        <div class=\"bloc-titre\">{WebUtility.HtmlEncode(feuille.Label)}</div>");
                sb.AppendLine($"        <div class=\"bloc-soustitre\">{WebUtility.HtmlEncode(feuille.SousTitre)}</div>");
                sb.AppendLine("        <div class=\"bloc-colonnes\"><div>OUI</div><div>NON</div></div>");
                sb.AppendLine("      </div>");

                for (int i = 0; i < items.Count; i++)
                {
                    sb.AppendLine($"      <div class=\"item-row\" data-item=\"{feuille.Key}_{i + 1}\">");
                    sb.AppendLine($"        <div class=\"item-num\">{i + 1}</div>");
                    sb.AppendLine($"        <div class=\"item-texte\">{WebUtility.HtmlEncode(items[i].Texte)}</div>");
                    sb.AppendLine("        <div class=\"case-col\"><span class=\"case\" data-case=\"oui\"></span></div>");
                    sb.AppendLine("        <div class=\"case-col\"><span class=\"case\" data-case=\"non\"></span></div>");
                    sb.AppendLine("      </div>");
                }
                sb.AppendLine("    </div>");
            }
            return sb.ToString();
        }

        private static string FillPlaceholders(string html, IDictionary<string, string> values)
        {
            return Regex.Replace(html, @"\{\{\s*([a-zA-Z0-9_]+)\s*\}\}", match =>
            {
                var key = match.Groups[1].Value;
                return values.TryGetValue(key, out var v) ? WebUtility.HtmlEncode(v) : "";
            });
        }
    }
}
