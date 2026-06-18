using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    /// <summary>
    /// Génère le « Formulaire de complétion — 1ère consultation » pré-rempli à remettre aux parents.
    ///
    /// Approche (cf. PLAN_FORMULAIRE_COMPLETION.md) : un template HTML/CSS A4 dont les
    /// placeholders {{cle}} sont remplacés par les valeurs connues du dossier, puis converti en
    /// PDF via Microsoft Edge headless (Chromium, rendu CSS fidèle, 100% local, aucune dépendance).
    ///
    /// Le template est attendu dans : Resources/Formulaires/formulaire_completion.html
    /// </summary>
    public class FormulaireCompletionService
    {
        private readonly EdgeHeadlessPdfService _pdf = new();

        private static string TemplatePath => Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Resources", "Formulaires", "formulaire_completion.html");

        /// <summary>Le template HTML est-il présent ?</summary>
        public bool TemplateExists => File.Exists(TemplatePath);

        /// <summary>Edge (moteur de conversion) est-il disponible ?</summary>
        public bool PdfEngineAvailable => _pdf.IsAvailable;

        /// <summary>
        /// Génère le PDF pré-rempli dans <paramref name="outputDir"/>.
        /// <paramref name="perePrenom"/> et <paramref name="merePrenom"/> sont extraits par le LLM
        /// depuis le bloc famille de l'interrogatoire avant d'appeler cette méthode.
        /// </summary>
        public async Task<(bool ok, string? pdfPath, string? error)> GenerateAsync(
            PatientMetadata meta, string outputDir,
            string? perePrenom = null, string? merePrenom = null)
        {
            if (!File.Exists(TemplatePath))
                return (false, null,
                    $"Template introuvable. Déposez 'formulaire_completion.html' dans :\n{Path.GetDirectoryName(TemplatePath)}");

            if (!_pdf.IsAvailable)
                return (false, null, "Microsoft Edge introuvable — requis pour générer le PDF.");

            try
            {
                var html = await File.ReadAllTextAsync(TemplatePath, Encoding.UTF8);
                html = FillPlaceholders(html, BuildValues(meta, perePrenom, merePrenom));

                Directory.CreateDirectory(outputDir);
                var stamp   = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
                var tmpHtml = Path.Combine(Path.GetTempPath(), $"formulaire_{stamp}.html");
                var pdfPath = Path.Combine(outputDir, $"{stamp}_formulaire_completion.pdf");

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
        /// Valeurs pré-remplies. Seuls le bandeau enfant, la date et les prénoms des parents
        /// sont injectés. Tout le reste est laissé vide — rempli à la main par les parents.
        /// </summary>
        private static Dictionary<string, string> BuildValues(
            PatientMetadata m, string? perePrenom, string? merePrenom) =>
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["date_rdv"]      = DateTime.Now.ToString("dd/MM/yyyy"),
                ["enfant_nom"]    = m.Nom ?? "",
                ["enfant_prenom"] = m.Prenom ?? "",
                ["enfant_dob"]    = m.DobFormatted ?? "",
                ["ecole"]         = m.Ecole ?? "",
                ["classe"]        = m.Classe ?? "",
                ["pere_prenom"]   = perePrenom ?? "",
                ["mere_prenom"]   = merePrenom ?? "",
            };

        /// <summary>
        /// Remplace les placeholders {{cle}} par leur valeur (HTML-encodée).
        /// Les placeholders sans valeur connue sont vidés (rendu blanc dans le formulaire).
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
