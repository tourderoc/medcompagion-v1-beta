using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MedCompanion.Models;
using MedCompanion.Services.LLM;

namespace MedCompanion.Services.Consultation
{
    /// <summary>
    /// Extraction LLM incrémentale : envoie uniquement le nouveau segment + état actuel des blocs.
    /// Évite la duplication en montrant au LLM ce qui est déjà extrait.
    /// </summary>
    public class IncrementalExtractorService
    {
        private readonly string _promptPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Resources", "Consultation", "prompt_incremental.txt");

        public async Task<(bool success, ExtractionResult? result, string? error)> ExtractAsync(
            ILLMService llmService,
            string newSegment,
            List<ConsultationBlock> currentBlocks)
        {
            if (string.IsNullOrWhiteSpace(newSegment))
                return (false, null, "Segment vide.");

            var prompt = BuildPrompt(newSegment, currentBlocks);
            var (ok, raw, err) = await llmService.GenerateTextAsync(prompt, maxTokens: 2000);
            if (!ok)
                return (false, null, err);

            return ParseJson(raw);
        }

        private string BuildPrompt(string newSegment, List<ConsultationBlock> currentBlocks)
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

            return template
                .Replace("{BLOCS_ACTUELS}", SerializeCurrentBlocks(currentBlocks))
                .Replace("{NOUVEAU_SEGMENT}", newSegment);
        }

        private static string SerializeCurrentBlocks(List<ConsultationBlock> blocks)
        {
            var sb = new StringBuilder();
            foreach (var block in blocks)
            {
                sb.Append($"[{block.Key}] ");
                sb.AppendLine(string.IsNullOrWhiteSpace(block.FreeText)
                    ? "(vide)"
                    : block.FreeText.Trim());
            }
            return sb.ToString().TrimEnd();
        }

        private static (bool, ExtractionResult?, string?) ParseJson(string raw)
        {
            var json = ExtractJsonBlock(raw);
            if (string.IsNullOrWhiteSpace(json))
                return (false, null, "Aucun JSON dans la réponse LLM.");

            try
            {
                var result = JsonSerializer.Deserialize<ExtractionResult>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                return result == null
                    ? (false, null, "Désérialisation JSON échouée.")
                    : (true, result, null);
            }
            catch (JsonException ex)
            {
                return (false, null, $"JSON invalide : {ex.Message}");
            }
        }

        private static string ExtractJsonBlock(string text)
        {
            var start = text.IndexOf('{');
            var end   = text.LastIndexOf('}');
            return start < 0 || end < start ? "" : text[start..(end + 1)];
        }

        private static string GetFallbackPrompt() =>
            "Extrais uniquement les nouvelles informations de ce segment.\n" +
            "État actuel des blocs:\n{BLOCS_ACTUELS}\n\n" +
            "Nouveau segment:\n{NOUVEAU_SEGMENT}\n\n" +
            "Retourne un JSON {\"updates\":[{\"blockKey\":\"...\",\"appendText\":\"...\",\"newThemes\":[]}]}";
    }
}
