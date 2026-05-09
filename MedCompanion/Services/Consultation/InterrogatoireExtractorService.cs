using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MedCompanion.Models;
using MedCompanion.Services.LLM;

namespace MedCompanion.Services.Consultation
{
    public class InterrogatoireExtractorService
    {
        private readonly string _promptPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Resources", "Consultation", "prompt_system.txt");

        private string BuildPrompt(string transcription)
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

            return template.Replace("{TRANSCRIPTION}", transcription);
        }

        public async Task<(bool success, ExtractionResult? result, string? error)> ExtractAsync(
            ILLMService llmService,
            string transcription)
        {
            if (string.IsNullOrWhiteSpace(transcription))
                return (false, null, "Transcription vide.");

            var prompt = BuildPrompt(transcription);

            var (ok, raw, err) = await llmService.GenerateTextAsync(prompt, maxTokens: 3000);
            if (!ok)
                return (false, null, err);

            return ParseJson(raw);
        }

        private (bool, ExtractionResult?, string?) ParseJson(string raw)
        {
            // Extraire le JSON du texte (le LLM peut ajouter du texte autour)
            var json = ExtractJsonBlock(raw);
            if (string.IsNullOrWhiteSpace(json))
                return (false, null, "Aucun JSON trouvé dans la réponse LLM.");

            try
            {
                var result = JsonSerializer.Deserialize<ExtractionResult>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result == null)
                    return (false, null, "Désérialisation JSON échouée.");

                return (true, result, null);
            }
            catch (JsonException ex)
            {
                return (false, null, $"JSON invalide : {ex.Message}");
            }
        }

        private static string ExtractJsonBlock(string text)
        {
            // Cherche le premier { et le dernier } pour extraire l'objet JSON
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end < start)
                return "";
            return text[start..(end + 1)];
        }

        public static void ApplyUpdates(List<ConsultationBlock> blocks, ExtractionResult result)
        {
            foreach (var update in result.Updates)
            {
                var block = blocks.Find(b => b.Key == update.BlockKey);
                if (block == null) continue;

                if (!string.IsNullOrWhiteSpace(update.AppendText))
                {
                    block.FreeText = string.IsNullOrWhiteSpace(block.FreeText)
                        ? update.AppendText.Trim()
                        : block.FreeText + "\n" + update.AppendText.Trim();
                }

                foreach (var theme in update.NewThemes)
                {
                    if (!block.CoveredThemes.Contains(theme))
                        block.CoveredThemes.Add(theme);
                }
            }
        }

        public static string BuildFinalNote(List<ConsultationBlock> blocks, DateTime date)
        {
            var sb = new StringBuilder();

            foreach (var block in blocks)
            {
                if (string.IsNullOrWhiteSpace(block.FreeText)) continue;
                sb.AppendLine($"## {block.Title}");
                // Supprimer les lignes "Date : ..." redondantes injectées par le LLM
                var lines = block.FreeText
                    .Split('\n')
                    .Where(l => !Regex.IsMatch(l.Trim(), @"^\*?\*?Date\s*:.*", RegexOptions.IgnoreCase));
                sb.AppendLine(string.Join('\n', lines).Trim());
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }

        private static string GetFallbackPrompt() =>
            "Extrais les informations médicales de cette transcription et retourne un JSON avec la clé \"updates\". " +
            "Chaque update a : blockKey (parmi identite/motif/famille/fratrie/atcds/scolarite/activites/maison), appendText, newThemes.\n\n{TRANSCRIPTION}";
    }
}
