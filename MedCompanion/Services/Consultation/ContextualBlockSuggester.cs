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

namespace MedCompanion.Services.Consultation
{
    /// <summary>
    /// Phase D — ContextualBlockSuggester
    /// Un seul appel LLM après détection du motif.
    /// Reçoit motif + blocs actifs, retourne max 4 blockKey à suggérer en chips.
    /// </summary>
    public class ContextualBlockSuggester
    {
        private readonly BlockSetResolver _resolver;

        private readonly string _promptPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Resources", "Consultation", "prompt_suggest_blocks.txt");

        public ContextualBlockSuggester(BlockSetResolver resolver)
        {
            _resolver = resolver;
        }

        /// <summary>
        /// Suggère des blocs supplémentaires basés sur le motif détecté + l'âge du patient.
        /// Combine une heuristique locale (mots-clés) et un appel LLM optionnel.
        /// </summary>
        /// <param name="motif">Motif principal détecté</param>
        /// <param name="ageYears">Âge confirmé du patient</param>
        /// <param name="activeBlockKeys">Clés des blocs déjà actifs</param>
        /// <param name="llmService">Service LLM (optionnel, pour affinement)</param>
        /// <returns>Liste de BlockSuggestion (max 4)</returns>
        public async Task<List<BlockSuggestion>> SuggestAsync(
            string motif,
            int ageYears,
            List<string> activeBlockKeys,
            ILLMService? llmService = null)
        {
            // 1. Heuristique locale : matcher les mots-clés
            var localSuggestions = GetLocalSuggestions(motif, ageYears, activeBlockKeys);

            // 2. Si un LLM est disponible, affiner avec un appel unique
            if (llmService != null && llmService.IsConfigured())
            {
                try
                {
                    var llmSuggestions = await GetLLMSuggestionsAsync(
                        llmService, motif, ageYears, activeBlockKeys);

                    // Fusionner : priorité au LLM, enrichi par l'heuristique
                    return MergeSuggestions(llmSuggestions, localSuggestions, activeBlockKeys);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[BlockSuggester] LLM fallback: {ex.Message}");
                }
            }

            // Fallback : heuristique seule
            return localSuggestions.Take(4).ToList();
        }

        // ── Heuristique locale ─────────────────────────────────────────────────

        /// <summary>
        /// Suggestions basées sur les mots-clés du motif et l'éligibilité par âge.
        /// Pas d'appel LLM, instantané.
        /// </summary>
        private List<BlockSuggestion> GetLocalSuggestions(
            string motif, int ageYears, List<string> activeBlockKeys)
        {
            var eligible = _resolver.GetEligibleChipBlocks(ageYears);
            var motifLower = motif.ToLowerInvariant();

            var suggestions = new List<BlockSuggestion>();

            foreach (var block in eligible)
            {
                // Pas de doublon avec les blocs déjà actifs
                if (activeBlockKeys.Contains(block.Key))
                    continue;

                // Vérifier si un mot-clé match le motif
                var matchingKeyword = block.MotifKeywords
                    .FirstOrDefault(kw => motifLower.Contains(kw.ToLowerInvariant()));

                if (matchingKeyword != null)
                {
                    suggestions.Add(new BlockSuggestion
                    {
                        BlockKey = block.Key,
                        Title = block.Title,
                        Reason = $"Motif contient « {matchingKeyword} »"
                    });
                }
            }

            return suggestions.Take(4).ToList();
        }

        // ── Appel LLM ──────────────────────────────────────────────────────────

        /// <summary>
        /// Un seul appel LLM pour obtenir les suggestions contextuelles.
        /// Le LLM reçoit : motif, âge, blocs actifs, blocs chip disponibles.
        /// Il retourne max 4 blockKeys pertinents.
        /// </summary>
        private async Task<List<BlockSuggestion>> GetLLMSuggestionsAsync(
            ILLMService llmService,
            string motif,
            int ageYears,
            List<string> activeBlockKeys)
        {
            var eligible = _resolver.GetEligibleChipBlocks(ageYears)
                .Where(b => !activeBlockKeys.Contains(b.Key))
                .ToList();

            if (eligible.Count == 0)
                return new List<BlockSuggestion>();

            var prompt = BuildPrompt(motif, ageYears, activeBlockKeys, eligible);
            var (ok, raw, err) = await llmService.GenerateTextAsync(prompt, maxTokens: 500);

            if (!ok || string.IsNullOrWhiteSpace(raw))
            {
                Debug.WriteLine($"[BlockSuggester] LLM error: {err}");
                return new List<BlockSuggestion>();
            }

            return ParseLLMResponse(raw, eligible);
        }

        private string BuildPrompt(
            string motif, int ageYears,
            List<string> activeBlockKeys,
            List<BlockDefinition> eligible)
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

            var eligibleDesc = string.Join("\n", eligible.Select(b =>
                $"  - {b.Key}: {b.Title} (thèmes: {string.Join(", ", b.ExpectedThemes.Take(4))})"));

            return template
                .Replace("{MOTIF}", motif)
                .Replace("{AGE}", ageYears.ToString())
                .Replace("{BLOCS_ACTIFS}", string.Join(", ", activeBlockKeys))
                .Replace("{BLOCS_DISPONIBLES}", eligibleDesc);
        }

        private List<BlockSuggestion> ParseLLMResponse(string raw, List<BlockDefinition> eligible)
        {
            // Essaye de parser un JSON array de { blockKey, reason }
            var jsonBlock = ExtractJsonArray(raw);

            if (!string.IsNullOrWhiteSpace(jsonBlock))
            {
                try
                {
                    var items = JsonSerializer.Deserialize<List<LLMSuggestionItem>>(jsonBlock,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (items != null)
                    {
                        return items
                            .Where(item => eligible.Any(e => e.Key == item.BlockKey))
                            .Select(item =>
                            {
                                var def = eligible.First(e => e.Key == item.BlockKey);
                                return new BlockSuggestion
                                {
                                    BlockKey = item.BlockKey,
                                    Title = def.Title,
                                    Reason = item.Reason ?? "Suggéré par l'IA"
                                };
                            })
                            .Take(4)
                            .ToList();
                    }
                }
                catch (JsonException ex)
                {
                    Debug.WriteLine($"[BlockSuggester] JSON parse error: {ex.Message}");
                }
            }

            // Fallback : chercher des clés de blocs dans le texte brut
            return eligible
                .Where(e => raw.Contains(e.Key, StringComparison.OrdinalIgnoreCase))
                .Select(e => new BlockSuggestion
                {
                    BlockKey = e.Key,
                    Title = e.Title,
                    Reason = "Suggéré par l'IA"
                })
                .Take(4)
                .ToList();
        }

        // ── Fusion des suggestions ──────────────────────────────────────────────

        private static List<BlockSuggestion> MergeSuggestions(
            List<BlockSuggestion> llm,
            List<BlockSuggestion> local,
            List<string> activeBlockKeys)
        {
            var merged = new List<BlockSuggestion>(llm);
            var llmKeys = new HashSet<string>(llm.Select(s => s.BlockKey));

            // Ajouter les suggestions locales qui ne sont pas dans le LLM
            foreach (var localSugg in local)
            {
                if (!llmKeys.Contains(localSugg.BlockKey) && !activeBlockKeys.Contains(localSugg.BlockKey))
                    merged.Add(localSugg);
            }

            return merged.Take(4).ToList();
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static string ExtractJsonArray(string text)
        {
            var start = text.IndexOf('[');
            var end = text.LastIndexOf(']');
            return start < 0 || end < start ? "" : text[start..(end + 1)];
        }

        private static string GetFallbackPrompt() =>
            "Tu es un assistant médical spécialisé en pédopsychiatrie.\n" +
            "Patient : {AGE} ans. Motif : {MOTIF}\n" +
            "Blocs déjà actifs : {BLOCS_ACTIFS}\n\n" +
            "Blocs supplémentaires disponibles :\n{BLOCS_DISPONIBLES}\n\n" +
            "Retourne un JSON array (max 4 items) des blocs pertinents :\n" +
            "[{\"blockKey\": \"...\", \"reason\": \"...\"}]\n" +
            "Ne retourne QUE les blocs cliniquement pertinents pour ce motif et cet âge.";

        private class LLMSuggestionItem
        {
            public string BlockKey { get; set; } = "";
            public string? Reason { get; set; }
        }
    }
}
