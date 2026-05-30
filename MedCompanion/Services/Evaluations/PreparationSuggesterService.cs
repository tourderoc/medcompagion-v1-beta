using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MedCompanion.Services.LLM;

namespace MedCompanion.Services.Evaluations
{
    /// <summary>
    /// Génère une proposition de Préparation clinique (5 catégories) via le LLM.
    /// Sortie : structure éditable par le médecin avant validation.
    /// </summary>
    public class PreparationSuggesterService
    {
        private const int LlmTimeoutSeconds = 60;
        // Caps par catégorie (anti-LLM trop large)
        private const int MaxHypotheses    = 3;
        private const int MaxDifferentiels = 4;
        private const int MaxAEliminer     = 3;
        private const int MaxVigilance     = 4;
        private const int MaxQuestions     = 5;

        private readonly ILLMService _llm;

        public PreparationSuggesterService(ILLMService llm)
        {
            _llm = llm;
        }

        public class PreparationSuggestion
        {
            public List<string> HypothesesPrincipales { get; set; } = new();
            public List<string> Differentiels         { get; set; } = new();
            public List<string> AEliminer             { get; set; } = new();
            public List<string> PointsVigilance       { get; set; } = new();
            public List<string> QuestionsCliniques    { get; set; } = new();
        }

        public async Task<(bool ok, PreparationSuggestion? suggestion, string? error)> SuggestAsync(
            string patientName,
            int?   age,
            string motif,
            string synthese,
            string observationsRecentes,
            CancellationToken ct = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(LlmTimeoutSeconds));

            try
            {
                var prompt = BuildPrompt(patientName, age, motif, synthese, observationsRecentes);
                var (ok, raw, err) = await _llm.GenerateTextAsync(prompt, maxTokens: 2500, cancellationToken: cts.Token);
                if (!ok || string.IsNullOrWhiteSpace(raw))
                {
                    var rawLen = raw?.Length ?? 0;
                    var preview = rawLen > 0 ? raw!.Substring(0, Math.Min(200, rawLen)).Replace("\r", " ").Replace("\n", " ") : "(vide)";
                    System.Diagnostics.Debug.WriteLine($"[PreparationSuggester] LLM réponse inutilisable. ok={ok}, err={err ?? "(null)"}, raw.Length={rawLen}, preview=\"{preview}\"");
                    return (false, null, err ?? "Réponse LLM vide (voir Sortie Debug).");
                }

                var sug = ParseJson(raw, out var extracted);
                if (sug == null)
                {
                    var truncated = string.IsNullOrEmpty(extracted);
                    System.Diagnostics.Debug.WriteLine($"[PreparationSuggester] PARSING ÉCHEC. raw.Length={raw.Length}, JSON extrait = {(truncated ? "VIDE (pas d'accolade fermante ou pas de JSON dans la réponse)" : $"{extracted!.Length} chars mais Parse a échoué")}.");
                    System.Diagnostics.Debug.WriteLine("[PreparationSuggester] Réponse brute :");
                    System.Diagnostics.Debug.WriteLine(raw);
                    System.Diagnostics.Debug.WriteLine("[PreparationSuggester] FIN raw.");
                    return (false, null, truncated
                        ? "Aucun JSON détecté dans la réponse LLM (probable format markdown/tableau). Voir Sortie Debug."
                        : "Parsing JSON impossible (voir Sortie Debug).");
                }

                // Cap dur sur chaque liste
                sug.HypothesesPrincipales = Trim(sug.HypothesesPrincipales, MaxHypotheses);
                sug.Differentiels         = Trim(sug.Differentiels,         MaxDifferentiels);
                sug.AEliminer             = Trim(sug.AEliminer,             MaxAEliminer);
                sug.PointsVigilance       = Trim(sug.PointsVigilance,       MaxVigilance);
                sug.QuestionsCliniques    = Trim(sug.QuestionsCliniques,    MaxQuestions);

                return (true, sug, null);
            }
            catch (OperationCanceledException)
            {
                return (false, null, "Délai LLM dépassé.");
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        private static List<string> Trim(List<string> list, int max)
        {
            var clean = new List<string>();
            foreach (var s in list)
            {
                var t = (s ?? "").Trim();
                if (string.IsNullOrEmpty(t)) continue;
                if (clean.Count >= max) break;
                clean.Add(t);
            }
            return clean;
        }

        private static string BuildPrompt(string patientName, int? age, string motif, string synthese, string observationsRecentes)
        {
            var ageInfo = age.HasValue ? $"{age.Value} ans" : "âge non confirmé";
            return $@"Tu es pédopsychiatre. Tu prépares l'évaluation d'un patient — c'est l'étape de mise en pensée AVANT l'exploration clinique ciblée.

OBJECTIF : proposer au médecin, sur la base des données disponibles, une charpente clinique organisée en 5 catégories. Le médecin validera, modifiera, ou rejettera librement.

CONTRAINTES STRICTES :
- Maximum 3 hypothèses principales.
- Maximum 4 diagnostics différentiels.
- Maximum 3 diagnostics à éliminer prudemment.
- Maximum 4 points de vigilance.
- Maximum 5 questions cliniques à résoudre.
- Chaque item est COURT (3-8 mots), et CIBLÉ pour CET enfant — pas générique.
- Pas d'hypothèse 'par sécurité' sans justification clinique précise dans les données.
- Si une catégorie ne peut pas être renseignée raisonnablement, renvoie un tableau vide.

DONNÉES DISPONIBLES :

Patient : {patientName}, {ageInfo}

Motif de consultation :
{(string.IsNullOrWhiteSpace(motif) ? "(non renseigné)" : motif.Trim())}

Synthèse globale du patient :
{(string.IsNullOrWhiteSpace(synthese) ? "(aucune synthèse disponible)" : synthese.Trim())}

Observations récentes (dernière consultation au moins) :
{(string.IsNullOrWhiteSpace(observationsRecentes) ? "(aucune observation récente disponible)" : observationsRecentes.Trim())}

RÉPONDS UNIQUEMENT par un JSON valide :
{{
  ""hypotheses_principales"": [""...""],
  ""differentiels"":          [""...""],
  ""a_eliminer"":              [""...""],
  ""points_vigilance"":        [""...""],
  ""questions_cliniques"":     [""...""]
}}";
        }

        private static PreparationSuggestion? ParseJson(string raw, out string? extracted)
        {
            extracted = ExtractJson(raw);
            if (string.IsNullOrEmpty(extracted)) return null;

            try
            {
                using var doc = JsonDocument.Parse(extracted);
                var root = doc.RootElement;
                var sug = new PreparationSuggestion
                {
                    HypothesesPrincipales = ReadList(root, "hypotheses_principales"),
                    Differentiels         = ReadList(root, "differentiels"),
                    AEliminer             = ReadList(root, "a_eliminer"),
                    PointsVigilance       = ReadList(root, "points_vigilance"),
                    QuestionsCliniques    = ReadList(root, "questions_cliniques")
                };
                return sug;
            }
            catch
            {
                return null;
            }
        }

        private static List<string> ReadList(JsonElement root, string key)
        {
            var res = new List<string>();
            if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array) return res;
            foreach (var e in arr.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String)
                    res.Add(e.GetString() ?? "");
            return res;
        }

        private static string ExtractJson(string raw)
        {
            raw = Regex.Replace(raw, @"```(?:json)?\s*", "", RegexOptions.IgnoreCase).Replace("```", "");
            int start = raw.IndexOf('{');
            if (start < 0) return "";
            int depth = 0; bool inStr = false; bool esc = false;
            for (int i = start; i < raw.Length; i++)
            {
                var c = raw[i];
                if (esc) { esc = false; continue; }
                if (c == '\\') { esc = true; continue; }
                if (c == '"') { inStr = !inStr; continue; }
                if (inStr) continue;
                if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) return raw.Substring(start, i - start + 1); }
            }
            return "";
        }
    }
}
