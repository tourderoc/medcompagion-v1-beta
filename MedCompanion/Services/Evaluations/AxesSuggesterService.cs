using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MedCompanion.Services.LLM;

namespace MedCompanion.Services.Evaluations
{
    /// <summary>
    /// Génère les axes d'attention clinique pour l'Étape 2 — Évaluation ciblée.
    /// Trois appels LLM séquentiels (un par catégorie) pour éviter le timeout.
    /// </summary>
    public class AxesSuggesterService
    {
        private const int LlmTimeoutSeconds     = 90;
        private const int MaxAxesPrincipaux     = 5;
        private const int MaxAxesDifferentiels  = 4;
        private const int MaxAxesSystemiques    = 4;
        private const int MaxQuestionsPerAxis   = 3;
        private const int MaxObservationsPerAxis = 5;

        private readonly ILLMService _llm;

        public AxesSuggesterService(ILLMService llm) => _llm = llm;

        public class AxesSuggestion
        {
            public List<SuggestedAxis> AxesPrincipaux    { get; set; } = new();
            public List<SuggestedAxis> AxesDifferentiels { get; set; } = new();
            public List<SuggestedAxis> AxesSystemiques   { get; set; } = new();
        }

        public class SuggestedAxis
        {
            public string       Label         { get; set; } = "";
            public string       Justification { get; set; } = "";
            public List<string> Questions     { get; set; } = new();
            public List<string> Observations  { get; set; } = new();
        }

        /// <summary>
        /// Génère les axes en 3 appels séquentiels. <paramref name="onPartialResult"/> est
        /// appelé après chaque catégorie pour mettre à jour l'UI progressivement.
        /// </summary>
        public async Task<(bool ok, AxesSuggestion? sug, string? err)> SuggestAsync(
            int? age,
            List<string> hypothesesPrincipales,
            List<string> differentiels,
            List<string> pointsVigilance,
            string motif,
            Action<AxesSuggestion>? onPartialResult = null,
            CancellationToken ct = default)
        {
            var result = new AxesSuggestion();

            // ── Appel 1 : Axes principaux ─────────────────────────────────────
            using var cts1 = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts1.CancelAfter(TimeSpan.FromSeconds(LlmTimeoutSeconds));
            try
            {
                var (ok, raw, err) = await _llm.GenerateTextAsync(
                    BuildPromptPrincipaux(age, hypothesesPrincipales, motif),
                    maxTokens: 1800, cancellationToken: cts1.Token);

                if (!ok || string.IsNullOrWhiteSpace(raw))
                    return (false, null, $"Axes principaux — {err ?? "réponse vide"}");

                result.AxesPrincipaux = TrimAxes(ParseAxes(raw, "axes_principaux"), MaxAxesPrincipaux);
                onPartialResult?.Invoke(result);
            }
            catch (OperationCanceledException) { return (false, null, "Délai LLM dépassé (axes principaux)."); }
            catch (Exception ex)               { return (false, null, ex.Message); }

            // ── Appel 2 : Différentiels ───────────────────────────────────────
            using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts2.CancelAfter(TimeSpan.FromSeconds(LlmTimeoutSeconds));
            try
            {
                var (ok, raw, err) = await _llm.GenerateTextAsync(
                    BuildPromptDifferentiels(age, hypothesesPrincipales, differentiels, motif),
                    maxTokens: 1500, cancellationToken: cts2.Token);

                if (ok && !string.IsNullOrWhiteSpace(raw))
                {
                    result.AxesDifferentiels = TrimAxes(ParseAxes(raw, "axes_differentiels"), MaxAxesDifferentiels);
                    onPartialResult?.Invoke(result);
                }
            }
            catch (OperationCanceledException) { /* on continue avec ce qu'on a */ }
            catch { /* best-effort */ }

            // ── Appel 3 : Systémiques ─────────────────────────────────────────
            using var cts3 = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts3.CancelAfter(TimeSpan.FromSeconds(LlmTimeoutSeconds));
            try
            {
                var (ok, raw, err) = await _llm.GenerateTextAsync(
                    BuildPromptSystemiques(age, pointsVigilance, motif),
                    maxTokens: 1500, cancellationToken: cts3.Token);

                if (ok && !string.IsNullOrWhiteSpace(raw))
                {
                    result.AxesSystemiques = TrimAxes(ParseAxes(raw, "axes_systemiques"), MaxAxesSystemiques);
                    onPartialResult?.Invoke(result);
                }
            }
            catch (OperationCanceledException) { /* on continue avec ce qu'on a */ }
            catch { /* best-effort */ }

            return (true, result, null);
        }

        // ── Prompts ───────────────────────────────────────────────────────────

        private static string BuildPromptPrincipaux(int? age, List<string> hypotheses, string motif)
        {
            var ageInfo = age.HasValue ? $"{age.Value} ans" : "âge non confirmé";
            var hypList = hypotheses.Any() ? string.Join(", ", hypotheses) : "(aucune)";

            return $@"Tu es pédopsychiatre. Génère les AXES PRINCIPAUX à explorer en Étape 2 d'évaluation.

Patient : {ageInfo}
Hypothèses principales : {hypList}
Motif : {(string.IsNullOrWhiteSpace(motif) ? "(non renseigné)" : motif.Trim())}

RÈGLES :
- Maximum {MaxAxesPrincipaux} axes, directement liés aux hypothèses.
- Label court (1-3 mots). Justification : 1 phrase. 3 questions ouvertes au clinicien.
- 3 à 5 observations cliniques à cocher (2-5 mots, constats naturels, pas de critères DSM bruts).

RÉPONDS UNIQUEMENT par ce JSON :
{{
  ""axes_principaux"": [{{ ""label"": ""..."", ""justification"": ""..."", ""questions"": [""...""], ""observations_proposees"": [""...""] }}]
}}";
        }

        private static string BuildPromptDifferentiels(int? age, List<string> hypotheses, List<string> differentiels, string motif)
        {
            var ageInfo  = age.HasValue ? $"{age.Value} ans" : "âge non confirmé";
            var hypList  = hypotheses.Any()    ? string.Join(", ", hypotheses)    : "(aucune)";
            var diffList = differentiels.Any() ? string.Join(", ", differentiels) : "(aucun)";

            return $@"Tu es pédopsychiatre. Génère les AXES DIFFÉRENTIELS à explorer pour confirmer ou écarter les différentiels.

Patient : {ageInfo}
Hypothèses : {hypList}
Différentiels à tester : {diffList}
Motif : {(string.IsNullOrWhiteSpace(motif) ? "(non renseigné)" : motif.Trim())}

RÈGLES :
- Maximum {MaxAxesDifferentiels} axes, centrés sur ce qui permettra d'écarter ou confirmer chaque différentiel.
- Label court (1-3 mots). Justification : 1 phrase. 3 questions ouvertes.
- 3 à 5 observations cliniques à cocher (2-5 mots, constats naturels, pas de critères DSM bruts).

RÉPONDS UNIQUEMENT par ce JSON :
{{
  ""axes_differentiels"": [{{ ""label"": ""..."", ""justification"": ""..."", ""questions"": [""...""], ""observations_proposees"": [""...""] }}]
}}";
        }

        private static string BuildPromptSystemiques(int? age, List<string> vigilance, string motif)
        {
            var ageInfo  = age.HasValue ? $"{age.Value} ans" : "âge non confirmé";
            var vigList  = vigilance.Any() ? string.Join(", ", vigilance) : "(aucun)";

            return $@"Tu es pédopsychiatre. Génère les AXES SYSTÉMIQUES à explorer (contexte familial, scolaire, sommeil, écrans, pairs, estime de soi).

Patient : {ageInfo}
Points de vigilance : {vigList}
Motif : {(string.IsNullOrWhiteSpace(motif) ? "(non renseigné)" : motif.Trim())}

RÈGLES :
- Maximum {MaxAxesSystemiques} axes contextuels (pas des hypothèses diagnostiques).
- Label court (1-3 mots). Justification : 1 phrase. 3 questions ouvertes.
- 3 à 5 observations cliniques à cocher (2-5 mots, constats naturels).

RÉPONDS UNIQUEMENT par ce JSON :
{{
  ""axes_systemiques"": [{{ ""label"": ""..."", ""justification"": ""..."", ""questions"": [""...""], ""observations_proposees"": [""...""] }}]
}}";
        }

        // ── Parsing ───────────────────────────────────────────────────────────

        private static List<SuggestedAxis> ParseAxes(string raw, string key)
        {
            var json = ExtractJson(raw);
            if (string.IsNullOrEmpty(json)) return new();
            try
            {
                using var doc = JsonDocument.Parse(json);
                return ReadAxes(doc.RootElement, key);
            }
            catch { return new(); }
        }

        private static List<SuggestedAxis> ReadAxes(JsonElement root, string key)
        {
            var list = new List<SuggestedAxis>();
            if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
                return list;

            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var a = new SuggestedAxis();
                if (el.TryGetProperty("label",         out var lab) && lab.ValueKind == JsonValueKind.String)
                    a.Label = lab.GetString() ?? "";
                if (el.TryGetProperty("justification", out var j)   && j.ValueKind   == JsonValueKind.String)
                    a.Justification = j.GetString() ?? "";
                if (el.TryGetProperty("questions",     out var qs)  && qs.ValueKind  == JsonValueKind.Array)
                    foreach (var q in qs.EnumerateArray())
                        if (q.ValueKind == JsonValueKind.String) a.Questions.Add(q.GetString() ?? "");
                if (el.TryGetProperty("observations_proposees", out var obs) && obs.ValueKind == JsonValueKind.Array)
                    foreach (var o in obs.EnumerateArray())
                        if (o.ValueKind == JsonValueKind.String) a.Observations.Add(o.GetString() ?? "");
                list.Add(a);
            }
            return list;
        }

        private static List<SuggestedAxis> TrimAxes(List<SuggestedAxis> axes, int max)
        {
            var clean = new List<SuggestedAxis>();
            foreach (var a in axes)
            {
                if (a == null || string.IsNullOrWhiteSpace(a.Label)) continue;
                a.Label         = a.Label.Trim();
                a.Justification = (a.Justification ?? "").Trim();
                a.Questions     = (a.Questions    ?? new()).Take(MaxQuestionsPerAxis)   .Select(q => q.Trim()).Where(q => !string.IsNullOrEmpty(q)).ToList();
                a.Observations  = (a.Observations ?? new()).Take(MaxObservationsPerAxis).Select(o => o.Trim()).Where(o => !string.IsNullOrEmpty(o)).ToList();
                clean.Add(a);
                if (clean.Count >= max) break;
            }
            return clean;
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
