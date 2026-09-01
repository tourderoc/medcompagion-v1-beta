using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MedCompanion.Models;
using MedCompanion.Models.Evaluations;

namespace MedCompanion.Services.Evaluations
{
    /// <summary>
    /// Génère la feuille « Cartographie de l'enfant — questionnaire parent », remise au parent
    /// à l'accueil et remplie en salle d'attente pendant que le médecin voit l'enfant.
    ///
    /// Même mécanique que <see cref="MedCompanion.Services.FormulaireCompletionService"/> :
    /// un template HTML/CSS A4 dont les placeholders {{cle}} sont remplacés, converti en PDF
    /// par Edge headless (100 % local).
    ///
    /// UN SEUL GABARIT POUR LES 4 TRANCHES. La géométrie — repères de calage, blocs, colonnes
    /// de cases — est rigoureusement identique quelle que soit la bande d'âge : seul le TEXTE
    /// des 30 énoncés change. C'est pourquoi le jeton d'en-tête ne porte pas la tranche : il
    /// désigne la mise en page, donc la façon de relire la feuille. La tranche imprimée dans le
    /// bandeau sert à autre chose — dire à quels énoncés les cases lues correspondent. Elle doit
    /// donc être conservée avec les réponses, jamais recalculée depuis l'âge courant de l'enfant.
    /// </summary>
    public class QuestionnaireCartographieService
    {
        private readonly EdgeHeadlessPdfService _pdf = new();

        /// <summary>Version de mise en page. À incrémenter si la géométrie change — jamais pour un
        /// simple changement de libellé d'item, qui ne déplace aucune case.</summary>
        public const string Jeton = "MEDCOMP-FORM-CARTO-V1";

        private static string TemplatePath => Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Resources", "Formulaires", "questionnaire_cartographie.html");

        public bool TemplateExists    => File.Exists(TemplatePath);
        public bool PdfEngineAvailable => _pdf.IsAvailable;

        /// <summary>
        /// Génère le PDF de la feuille pour un enfant et son âge.
        /// </summary>
        /// <param name="age">Âge confirmé. Hors 3-11, l'outil ne s'applique pas et rien n'est généré.</param>
        /// <param name="outputDir">
        /// Dossier de sortie. Une feuille VIERGE n'est pas un document du dossier médical : elle
        /// est générée en temporaire, comme le formulaire de complétion. C'est le scan de la
        /// feuille REMPLIE qui sera archivé.
        /// </param>
        public async Task<(bool ok, string? pdfPath, string? error)> GenerateAsync(
            PatientMetadata meta, int? age, string outputDir)
        {
            var bande = CartographieItemsV2.Bande(age);
            if (bande == null)
                return (false, null,
                    $"La Cartographie de l'enfant couvre {CartographieItemsV2.AgeMin}-{CartographieItemsV2.AgeMax} ans. " +
                    $"Âge renseigné : {(age.HasValue ? age.Value + " ans" : "inconnu")}.");

            if (!File.Exists(TemplatePath))
                return (false, null,
                    $"Template introuvable. Déposez 'questionnaire_cartographie.html' dans :\n{Path.GetDirectoryName(TemplatePath)}");

            if (!_pdf.IsAvailable)
                return (false, null, "Microsoft Edge introuvable — requis pour générer le PDF.");

            try
            {
                var html = await File.ReadAllTextAsync(TemplatePath, Encoding.UTF8);
                html = FillPlaceholders(html, BuildValues(meta, age!.Value, bande.Value));

                Directory.CreateDirectory(outputDir);
                var stamp   = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
                var tmpHtml = Path.Combine(Path.GetTempPath(), $"carto_{stamp}.html");
                var pdfPath = Path.Combine(outputDir, $"{stamp}_questionnaire_cartographie.pdf");

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
        /// Bandeau enfant + les 30 énoncés de la bande. Aucun rappel du 1er entretien : les
        /// parents en ont déjà reçu la restitution à l'issue de cette consultation.
        /// </summary>
        private static Dictionary<string, string> BuildValues(PatientMetadata m, int age, BandeAgeCarto bande)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["date_rdv"]      = DateTime.Now.ToString("dd/MM/yyyy"),
                ["enfant_nom"]    = m.Nom ?? "",
                ["enfant_prenom"] = m.Prenom ?? "",
                ["enfant_dob"]    = m.DobFormatted ?? "",
                ["enfant_age"]    = age.ToString(),
                ["tranche"]       = CartographieItemsV2.BandeLabel(bande),
            };

            foreach (var axe in CartographieItemsV2.AxeKeys)
            {
                var items = CartographieItemsV2.Items(axe, bande);
                for (int i = 0; i < 6; i++)
                    values[$"{axe}_{i + 1}"] = i < items.Count ? items[i] : "";
            }

            return values;
        }

        /// <summary>
        /// Remplace les placeholders {{cle}} par leur valeur, HTML-encodée. Un placeholder sans
        /// valeur est vidé — une ligne blanche se voit à l'impression, une balise brute non.
        /// </summary>
        private static string FillPlaceholders(string html, IDictionary<string, string> values)
        {
            return Regex.Replace(html, @"\{\{\s*([a-zA-Z0-9_]+)\s*\}\}", match =>
            {
                var key = match.Groups[1].Value;
                return values.TryGetValue(key, out var v)
                    ? System.Net.WebUtility.HtmlEncode(v)
                    : "";
            });
        }
    }
}
