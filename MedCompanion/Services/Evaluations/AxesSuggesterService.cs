using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MedCompanion.Services.LLM;

namespace MedCompanion.Services.Evaluations
{
    /// <summary>
    /// Génère les axes d'attention clinique pour l'Étape 2 — Évaluation ciblée.
    /// Trois catégories : principaux (hypothèses), différentiels (à explorer pour écarter),
    /// systémiques (familial, scolaire, écrans, etc.).
    /// </summary>
    public class AxesSuggesterService
    {
        private const int LlmTimeoutSeconds = 35;
        private const int MaxAxesPrincipaux    = 5;
        private const int MaxAxesDifferentiels = 4;
        private const int MaxAxesSystemiques   = 4;
        private const int MaxQuestionsPerAxis     = 3;
        private const int MaxObservationsPerAxis  = 5;

        private readonly ILLMService _llm;

        public AxesSuggesterService(ILLMService llm)
        {
            _llm = llm;
        }

        public class AxesSuggestion
        {
            public List<SuggestedAxis> AxesPrincipaux     { get; set; } = new();
            public List<SuggestedAxis> AxesDifferentiels  { get; set; } = new();
            public List<SuggestedAxis> AxesSystemiques    { get; set; } = new();
        }

        public class SuggestedAxis
        {
            public string Label         { get; set; } = "";
            public string Justification { get; set; } = "";
            public List<string> Questions    { get; set; } = new();
            public List<string> Observations { get; set; } = new();
        }

        public async Task<(bool ok, AxesSuggestion? sug, string? err)> SuggestAsync(
            int? age,
            List<string> hypothesesPrincipales,
            List<string> differentiels,
            List<string> pointsVigilance,
            string motif,
            CancellationToken ct = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(LlmTimeoutSeconds));

            try
            {
                var prompt = BuildPrompt(age, hypothesesPrincipales, differentiels, pointsVigilance, motif);
                var (ok, raw, err) = await _llm.GenerateTextAsync(prompt, maxTokens: 4500, cancellationToken: cts.Token);
                if (!ok || string.IsNullOrWhiteSpace(raw))
                    return (false, null, err ?? "Réponse LLM vide.");

                var sug = ParseJson(raw, out var extracted);
                if (sug == null)
                {
                    var truncated = string.IsNullOrEmpty(extracted);
                    System.Diagnostics.Debug.WriteLine($"[AxesSuggesterService] PARSING ÉCHEC. Longueur réponse = {raw.Length}. JSON extrait = {(truncated ? "VIDE (probable troncature : pas d'accolade fermante)" : $"{extracted!.Length} chars mais Parse a échoué")}.");
                    System.Diagnostics.Debug.WriteLine("[AxesSuggesterService] Réponse brute :");
                    System.Diagnostics.Debug.WriteLine(raw);
                    System.Diagnostics.Debug.WriteLine("[AxesSuggesterService] FIN réponse brute.");
                    return (false, null, truncated
                        ? "Réponse LLM tronquée (limite tokens atteinte). Voir Sortie Debug."
                        : "Parsing JSON impossible (voir Sortie Debug pour la réponse LLM brute).");
                }

                sug.AxesPrincipaux    = TrimAxes(sug.AxesPrincipaux,    MaxAxesPrincipaux);
                sug.AxesDifferentiels = TrimAxes(sug.AxesDifferentiels, MaxAxesDifferentiels);
                sug.AxesSystemiques   = TrimAxes(sug.AxesSystemiques,   MaxAxesSystemiques);

                return (true, sug, null);
            }
            catch (OperationCanceledException) { return (false, null, "Délai LLM dépassé."); }
            catch (Exception ex)               { return (false, null, ex.Message); }
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

        private static string BuildPrompt(int? age, List<string> hypotheses, List<string> differentiels, List<string> vigilance, string motif)
        {
            var ageInfo = age.HasValue ? $"{age.Value} ans" : "âge non confirmé";
            var hypList = string.Join(", ", hypotheses);
            var diffList = string.Join(", ", differentiels);
            var vigList  = string.Join(", ", vigilance);

            return $@"Tu es pédopsychiatre. Tu prépares l'Étape 2 d'une évaluation : la liste des axes cliniques à explorer pendant les prochaines consultations.

Le médecin a validé en Étape 1 :
- Hypothèses principales : {(string.IsNullOrEmpty(hypList) ? "(aucune)" : hypList)}
- Diagnostics différentiels : {(string.IsNullOrEmpty(diffList) ? "(aucun)" : diffList)}
- Points de vigilance : {(string.IsNullOrEmpty(vigList) ? "(aucun)" : vigList)}
- Motif de consultation : {(string.IsNullOrWhiteSpace(motif) ? "(non renseigné)" : motif.Trim())}
- Âge du patient : {ageInfo}

OBJECTIF : produire 3 listes d'axes d'attention clinique organisés en catégories :
1. AXES PRINCIPAUX — dimensions à explorer directement liées aux hypothèses principales.
2. DIFFÉRENTIELS — axes à explorer pour écarter ou confirmer les différentiels.
3. SYSTÉMIQUES — facteurs contextuels (familial, scolaire, sommeil, écrans, estime de soi, pairs).

CONTRAINTES STRICTES :
- Maximum 5 axes principaux, 4 différentiels, 4 systémiques.
- Chaque axe = LABEL COURT (1-3 mots, ex: ""Attention"", ""Sommeil"", ""Fonctionnement familial"").
- Chaque axe a une JUSTIFICATION courte (1 phrase, pourquoi pour CE patient).
- Chaque axe a 2-3 QUESTIONS OUVERTES proposées au clinicien (à intégrer librement en consultation, pas un script).
- Chaque axe a 3-5 OBSERVATIONS PROPOSÉES, courtes (2-5 mots), formulées comme des CONSTATS CLINIQUES NATURELS que le médecin pourra cocher si présents. Exemples : ""Humeur fluctuante"", ""Endormissement difficile"", ""Repli social"". JAMAIS de critères DSM bruts.
- Pas d'axes ""par sécurité"" non justifiés par les hypothèses ou le motif.
- Pas de checklist DSM : les observations sont des AIDES AU CONSTAT, pas des critères à valider.

RÉPONDS UNIQUEMENT par un JSON valide :
{{
  ""axes_principaux"":     [{{ ""label"": ""..."", ""justification"": ""..."", ""questions"": [""...""], ""observations_proposees"": [""...""] }}],
  ""axes_differentiels"":  [{{ ""label"": ""..."", ""justification"": ""..."", ""questions"": [""...""], ""observations_proposees"": [""...""] }}],
  ""axes_systemiques"":    [{{ ""label"": ""..."", ""justification"": ""..."", ""questions"": [""...""], ""observations_proposees"": [""...""] }}]
}}";
        }

        private static AxesSuggestion? ParseJson(string raw, out string? extracted)
        {
            extracted = ExtractJson(raw);
            if (string.IsNullOrEmpty(extracted)) return null;

            try
            {
                using var doc = JsonDocument.Parse(extracted);
                var root = doc.RootElement;
                return new AxesSuggestion
                {
                    AxesPrincipaux    = ReadAxes(root, "axes_principaux"),
                    AxesDifferentiels = ReadAxes(root, "axes_differentiels"),
                    AxesSystemiques   = ReadAxes(root, "axes_systemiques")
                };
            }
            catch { return null; }
        }

        private static List<SuggestedAxis> ReadAxes(JsonElement root, string key)
        {
            var list = new List<SuggestedAxis>();
            if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;

            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object) continue;
                var a = new SuggestedAxis();
                if (el.TryGetProperty("label", out var lab) && lab.ValueKind == JsonValueKind.String)
                    a.Label = lab.GetString() ?? "";
                if (el.TryGetProperty("justification", out var j) && j.ValueKind == JsonValueKind.String)
                    a.Justification = j.GetString() ?? "";
                if (el.TryGetProperty("questions", out var qs) && qs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var q in qs.EnumerateArray())
                        if (q.ValueKind == JsonValueKind.String)
                            a.Questions.Add(q.GetString() ?? "");
                }
                if (el.TryGetProperty("observations_proposees", out var obs) && obs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var o in obs.EnumerateArray())
                        if (o.ValueKind == JsonValueKind.String)
                            a.Observations.Add(o.GetString() ?? "");
                }
                list.Add(a);
            }
            return list;
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
