using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MedCompanion.Models;
using MedCompanion.Services.LLM;
using MedCompanion.ViewModels;

namespace MedCompanion.Services.Consultation
{
    /// <summary>
    /// Phase E — BlockPrefiller
    /// Quand un chip est accepté (✓), pré-remplit le nouveau bloc
    /// avec le contexte des blocs existants via un appel LLM.
    /// </summary>
    public class BlockPrefiller
    {
        private readonly string _promptPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Resources", "Consultation", "prompt_prefill.txt");

        /// <summary>
        /// Pré-remplit un bloc supplémentaire avec les informations déjà recueillies
        /// dans les blocs existants. Un seul appel LLM.
        /// </summary>
        /// <param name="llmService">Service LLM</param>
        /// <param name="newBlock">Définition du nouveau bloc à pré-remplir</param>
        /// <param name="existingBlocks">Blocs existants avec leur contenu</param>
        /// <returns>Texte pré-rempli, ou vide si rien de pertinent</returns>
        public async Task<(bool success, string prefillText, List<string> coveredThemes)> PrefillAsync(
            ILLMService llmService,
            BlockDefinition newBlock,
            IEnumerable<ConsultationBlockViewModel> existingBlocks)
        {
            // Ne pré-remplir que si des blocs existants ont du contenu
            var blocksWithContent = existingBlocks
                .Where(b => !string.IsNullOrWhiteSpace(b.FreeText))
                .ToList();

            if (blocksWithContent.Count == 0)
                return (true, "", new List<string>());

            var prompt = BuildPrompt(newBlock, blocksWithContent);
            var (ok, raw, err) = await llmService.GenerateTextAsync(prompt, maxTokens: 1000);

            if (!ok || string.IsNullOrWhiteSpace(raw))
            {
                Debug.WriteLine($"[BlockPrefiller] LLM error: {err}");
                return (false, "", new List<string>());
            }

            return ParseResponse(raw, newBlock);
        }

        private string BuildPrompt(BlockDefinition newBlock, List<ConsultationBlockViewModel> existingBlocks)
        {
            string template;
            try
            {
                template = File.Exists(_promptPath)
                    ? File.ReadAllText(_promptPath, Encoding.UTF8)
                    : GetFallbackPrompt();
            }
            catch
            {
                template = GetFallbackPrompt();
            }

            var contextSb = new StringBuilder();
            foreach (var block in existingBlocks)
            {
                contextSb.AppendLine($"[{block.Key} — {block.Title}]");
                contextSb.AppendLine(block.FreeText.Trim());
                contextSb.AppendLine();
            }

            return template
                .Replace("{BLOC_KEY}", newBlock.Key)
                .Replace("{BLOC_TITLE}", newBlock.Title)
                .Replace("{THEMES_ATTENDUS}", string.Join(", ", newBlock.ExpectedThemes))
                .Replace("{CONTEXTE_EXISTANT}", contextSb.ToString().TrimEnd());
        }

        private static (bool, string, List<string>) ParseResponse(string raw, BlockDefinition newBlock)
        {
            // Essaye de parser un JSON { "prefillText": "...", "coveredThemes": [...] }
            var jsonBlock = ExtractJsonBlock(raw);

            if (!string.IsNullOrWhiteSpace(jsonBlock))
            {
                try
                {
                    var result = JsonSerializer.Deserialize<PrefillResult>(jsonBlock,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (result != null && !string.IsNullOrWhiteSpace(result.PrefillText))
                    {
                        return (true, result.PrefillText.Trim(), result.CoveredThemes ?? new List<string>());
                    }
                }
                catch (JsonException ex)
                {
                    Debug.WriteLine($"[BlockPrefiller] JSON parse error: {ex.Message}");
                }
            }

            // Fallback : utilise le texte brut comme pré-remplissage
            var text = raw.Trim();
            // Supprimer les blocs de code markdown si présents
            if (text.StartsWith("```"))
            {
                var lines = text.Split('\n').ToList();
                if (lines.Count > 2)
                {
                    lines.RemoveAt(0); // Première ligne ```
                    if (lines.Last().Trim() == "```")
                        lines.RemoveAt(lines.Count - 1);
                    text = string.Join('\n', lines).Trim();
                }
            }

            return (true, text, new List<string>());
        }

        private static string ExtractJsonBlock(string text)
        {
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            return start < 0 || end < start ? "" : text[start..(end + 1)];
        }

        private static string GetFallbackPrompt() =>
            "Tu es un assistant médical en pédopsychiatrie.\n" +
            "Le médecin vient d'ajouter le bloc « {BLOC_TITLE} » ({BLOC_KEY}).\n" +
            "Thèmes attendus : {THEMES_ATTENDUS}\n\n" +
            "Contexte existant des autres blocs :\n{CONTEXTE_EXISTANT}\n\n" +
            "Extrais du contexte existant UNIQUEMENT les informations pertinentes pour ce nouveau bloc.\n" +
            "Retourne un JSON : {\"prefillText\": \"...\", \"coveredThemes\": [\"theme1\", ...]}\n" +
            "Si aucune info pertinente, retourne {\"prefillText\": \"\", \"coveredThemes\": []}";

        private class PrefillResult
        {
            public string PrefillText { get; set; } = "";
            public List<string> CoveredThemes { get; set; } = new();
        }
    }
}
