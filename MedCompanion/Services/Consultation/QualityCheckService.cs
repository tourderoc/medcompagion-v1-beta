using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MedCompanion.Models;
using MedCompanion.Services.LLM;

namespace MedCompanion.Services.Consultation
{
    /// <summary>
    /// Passe qualité LLM après extraction : détecte médicaments mal orthographiés,
    /// incohérences logiques et termes ambigus. Retourne une liste de signalements
    /// que le médecin peut accepter ou ignorer avant sauvegarde.
    /// Conçu pour être réutilisé sur tout type de note structurée.
    /// </summary>
    public class QualityCheckService
    {
        private string? _promptTemplate;

        private string PromptTemplate
        {
            get
            {
                if (_promptTemplate != null) return _promptTemplate;

                var path = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "",
                    "Resources", "Consultation", "prompt_quality.txt");

                _promptTemplate = File.Exists(path)
                    ? File.ReadAllText(path, Encoding.UTF8)
                    : FallbackPrompt;

                return _promptTemplate;
            }
        }

        /// <summary>
        /// Lance la vérification qualité sur une note structurée en blocs.
        /// Retourne une liste de problèmes détectés (peut être vide).
        /// </summary>
        public async Task<List<QualityIssue>> CheckAsync(
            ILLMService llmService,
            string noteContent)
        {
            if (string.IsNullOrWhiteSpace(noteContent))
                return new List<QualityIssue>();

            var prompt = PromptTemplate.Replace("{NOTE}", noteContent);

            var (ok, raw, _) = await llmService.ChatAsync(
                prompt,
                new System.Collections.Generic.List<(string role, string content)>(),
                maxTokens: 1000);
            if (!ok || string.IsNullOrWhiteSpace(raw))
                return new List<QualityIssue>();

            return ParseIssues(raw);
        }

        private static List<QualityIssue> ParseIssues(string raw)
        {
            try
            {
                var json = ExtractJson(raw);
                if (string.IsNullOrWhiteSpace(json)) return new List<QualityIssue>();

                var items = JsonSerializer.Deserialize<List<JsonElement>>(json);
                if (items == null) return new List<QualityIssue>();

                var result = new List<QualityIssue>();
                foreach (var item in items)
                {
                    var issue = new QualityIssue
                    {
                        BlockKey   = item.TryGetProperty("blockKey",   out var bk) ? bk.GetString() ?? "" : "",
                        BlockTitle = item.TryGetProperty("blockTitle", out var bt) ? bt.GetString() ?? "" : "",
                        Original   = item.TryGetProperty("original",   out var or) ? or.GetString() ?? "" : "",
                        Suggestion = item.TryGetProperty("suggestion", out var su) ? su.GetString() ?? "" : "",
                        Reason     = item.TryGetProperty("reason",     out var re) ? re.GetString() ?? "" : "",
                        Type       = ParseType(item.TryGetProperty("type", out var ty) ? ty.GetString() : null)
                    };

                    if (!string.IsNullOrWhiteSpace(issue.Original) && !string.IsNullOrWhiteSpace(issue.Suggestion))
                        result.Add(issue);
                }
                return result;
            }
            catch
            {
                return new List<QualityIssue>();
            }
        }

        private static string? ExtractJson(string raw)
        {
            var start = raw.IndexOf('[');
            var end   = raw.LastIndexOf(']');
            if (start < 0 || end <= start) return null;
            return raw.Substring(start, end - start + 1);
        }

        private static QualityIssueType ParseType(string? s) => s switch
        {
            "Coherence" => QualityIssueType.Coherence,
            "Unclear"   => QualityIssueType.Unclear,
            _           => QualityIssueType.Medication
        };

        private const string FallbackPrompt =
            "Analyse cette note médicale et retourne [] si aucun problème, sinon un tableau JSON " +
            "avec les champs blockKey, blockTitle, original, suggestion, reason, type.\n\n{NOTE}";
    }
}
