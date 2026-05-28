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
    /// Reçoit une transcription Whisper complète (toute la session de dicte) + la liste des axes
    /// actifs, et demande au LLM de router le contenu vers les bonnes zones d'observation.
    /// Sortie : pour chaque axe concerné, du texte CLINIQUE à appender (pas du verbatim).
    /// Si la transcription est très longue (> seuil), elle est découpée en 2 passes puis fusionnée.
    /// </summary>
    public class AxisExtractorService
    {
        private const int LlmTimeoutSeconds = 60;
        // Au-delà de ~5000 mots (≈ 20-25 min de consultation), on découpe.
        private const int WordsThresholdForSplit = 5000;

        private readonly ILLMService _llm;

        public AxisExtractorService(ILLMService llm) { _llm = llm; }

        public class AxisContext
        {
            public string Label               { get; set; } = "";
            public string Justification       { get; set; } = "";
            public string ObservationActuelle { get; set; } = "";
        }

        public class ExtractionResult
        {
            /// <summary>Pour chaque label d'axe, le texte à appender dans son observation.</summary>
            public Dictionary<string, string> Updates { get; } = new();
        }

        public async Task<(bool ok, ExtractionResult? res, string? err)> ExtractAsync(
            string transcriptionComplete,
            int? patientAge,
            List<AxisContext> axes,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(transcriptionComplete) || axes.Count == 0)
                return (true, new ExtractionResult(), null);

            var words = transcriptionComplete.Split(new[] { ' ', '\n', '\r', '\t' },
                                                    StringSplitOptions.RemoveEmptyEntries);

            // Passe unique si la transcription tient dans le budget
            if (words.Length <= WordsThresholdForSplit)
                return await ExtractSinglePassAsync(transcriptionComplete, patientAge, axes, ct);

            // Découpage en 2 passes + merge
            var mid = words.Length / 2;
            var first  = string.Join(" ", words.Take(mid));
            var second = string.Join(" ", words.Skip(mid));

            var (ok1, r1, err1) = await ExtractSinglePassAsync(first,  patientAge, axes, ct);
            if (!ok1 || r1 == null) return (false, null, $"Passe 1 : {err1}");

            // 2e passe : on injecte les observations actualisées + ce que la passe 1 a déjà extrait
            var axesForPass2 = axes.Select(a => new AxisContext
            {
                Label               = a.Label,
                Justification       = a.Justification,
                ObservationActuelle = (r1.Updates.TryGetValue(a.Label, out var added)
                                       ? (a.ObservationActuelle + " " + added).Trim()
                                       : a.ObservationActuelle)
            }).ToList();
            var (ok2, r2, err2) = await ExtractSinglePassAsync(second, patientAge, axesForPass2, ct);
            if (!ok2 || r2 == null) return (false, null, $"Passe 2 : {err2}");

            var merged = new ExtractionResult();
            foreach (var kv in r1.Updates) merged.Updates[kv.Key] = kv.Value;
            foreach (var kv in r2.Updates)
            {
                if (merged.Updates.TryGetValue(kv.Key, out var existing))
                    merged.Updates[kv.Key] = (existing + " " + kv.Value).Trim();
                else
                    merged.Updates[kv.Key] = kv.Value;
            }
            return (true, merged, null);
        }

        private async Task<(bool ok, ExtractionResult? res, string? err)> ExtractSinglePassAsync(
            string transcription, int? age, List<AxisContext> axes, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(LlmTimeoutSeconds));
            try
            {
                var prompt = BuildPrompt(transcription, age, axes);
                var (ok, raw, err) = await _llm.GenerateTextAsync(prompt, maxTokens: 2200, cancellationToken: cts.Token);
                if (!ok || string.IsNullOrWhiteSpace(raw))
                    return (false, null, err ?? "Réponse LLM vide.");

                var res = ParseJson(raw);
                if (res == null)
                {
                    System.Diagnostics.Debug.WriteLine("[AxisExtractorService] Parsing JSON échec. Réponse brute :");
                    System.Diagnostics.Debug.WriteLine(raw);
                    return (false, null, "Parsing JSON impossible (voir Sortie Debug).");
                }
                return (true, res, null);
            }
            catch (OperationCanceledException) { return (false, null, "Délai LLM dépassé."); }
            catch (Exception ex)               { return (false, null, ex.Message); }
        }

        private static string BuildPrompt(string transcription, int? age, List<AxisContext> axes)
        {
            var ageInfo = age.HasValue ? $"{age.Value} ans" : "âge non confirmé";
            var sb = new StringBuilder();
            sb.AppendLine("Tu es pédopsychiatre. Tu reçois la transcription d'une consultation que tu viens de mener.");
            sb.AppendLine("Ta tâche : à partir des AXES D'ÉVALUATION ACTIFS, identifier ceux qui sont concernés par");
            sb.AppendLine("la consultation et proposer, pour CHAQUE axe concerné, une SYNTHÈSE CLINIQUE COURTE");
            sb.AppendLine("(2 à 5 phrases) à appender à la zone d'observation de cet axe.");
            sb.AppendLine();
            sb.AppendLine("RÈGLES STRICTES :");
            sb.AppendLine("- Tu ne fais PAS de transcription verbatim. Tu reformules en LANGUE CLINIQUE NEUTRE.");
            sb.AppendLine("- Tu écris comme le médecin l'écrirait dans ses notes (3e personne).");
            sb.AppendLine("  ❌ \"Je dors mal\"   ✅ \"Rapporte un sommeil de mauvaise qualité.\"");
            sb.AppendLine("- Tu n'updates QUE les axes pour lesquels il y a un contenu CLAIR dans la transcription.");
            sb.AppendLine("- Ne propose AUCUN diagnostic ni interprétation pesante. Tu rapportes des observations.");
            sb.AppendLine("- Ne reprends PAS l'observation actuelle d'un axe : ton texte sera AJOUTÉ à la suite.");
            sb.AppendLine("- Utilise EXACTEMENT le label d'axe (axis_label) tel que listé ci-dessous.");
            sb.AppendLine("- Si un même contenu pourrait aller dans 2 axes : choisis le PLUS pertinent, pas les deux.");
            sb.AppendLine();
            sb.AppendLine($"Patient : {ageInfo}");
            sb.AppendLine();
            sb.AppendLine("AXES ACTIFS :");
            foreach (var a in axes)
            {
                sb.AppendLine($"- \"{a.Label}\" — {a.Justification}");
                if (!string.IsNullOrWhiteSpace(a.ObservationActuelle))
                    sb.AppendLine($"  (déjà noté, ne pas reprendre : {Truncate(a.ObservationActuelle, 150)})");
            }
            sb.AppendLine();
            sb.AppendLine("TRANSCRIPTION DE LA CONSULTATION :");
            sb.AppendLine("───");
            sb.AppendLine(transcription.Trim());
            sb.AppendLine("───");
            sb.AppendLine();
            sb.AppendLine("RÉPONDS UNIQUEMENT par ce JSON :");
            sb.AppendLine("{");
            sb.AppendLine("  \"updates\": [");
            sb.AppendLine("    { \"axis_label\": \"<label exact>\", \"append\": \"<synthèse clinique>\" }");
            sb.AppendLine("  ]");
            sb.AppendLine("}");
            sb.AppendLine("Si rien à appender : { \"updates\": [] }");
            return sb.ToString();
        }

        private static string Truncate(string s, int n)
            => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n) + "…");

        private static ExtractionResult? ParseJson(string raw)
        {
            var json = ExtractJsonObject(raw);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var res = new ExtractionResult();
                if (!root.TryGetProperty("updates", out var arr) || arr.ValueKind != JsonValueKind.Array) return res;
                foreach (var el in arr.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;
                    if (!el.TryGetProperty("axis_label", out var labelEl) || labelEl.ValueKind != JsonValueKind.String) continue;
                    if (!el.TryGetProperty("append", out var appendEl)   || appendEl.ValueKind != JsonValueKind.String) continue;
                    var lbl = labelEl.GetString()?.Trim() ?? "";
                    var txt = appendEl.GetString()?.Trim() ?? "";
                    if (string.IsNullOrEmpty(lbl) || string.IsNullOrEmpty(txt)) continue;
                    if (res.Updates.TryGetValue(lbl, out var existing))
                        res.Updates[lbl] = (existing + " " + txt).Trim();
                    else
                        res.Updates[lbl] = txt;
                }
                return res;
            }
            catch { return null; }
        }

        private static string ExtractJsonObject(string raw)
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
