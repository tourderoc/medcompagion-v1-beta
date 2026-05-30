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
    /// Génère une proposition de Synthèse Diagnostique (Étape 3) à partir des axes
    /// explorés en Étape 2 (état Partiel ou Évoqué + leurs observations).
    /// Sortie : diagnostics retenus, éléments en faveur, différentiels écartés, niveau de certitude.
    /// </summary>
    public class SyntheseSuggesterService
    {
        private const int LlmTimeoutSeconds = 60;
        // Caps anti-LLM-trop-large
        private const int MaxRetenus      = 3;
        private const int MaxEnFaveur     = 8;
        private const int MaxEcartes      = 5;

        private readonly ILLMService _llm;

        public SyntheseSuggesterService(ILLMService llm) { _llm = llm; }

        public class AxisInput
        {
            public string Label         { get; set; } = "";
            public string Justification { get; set; } = "";
            public int    State         { get; set; }   // 0=NonAborde, 1=Partiel, 2=Evoque
            public string Observation   { get; set; } = "";
        }

        public class SyntheseSuggestion
        {
            public List<string>           DiagnosticsRetenus = new();
            public List<string>           ElementsEnFaveur   = new();
            public List<EcarteSuggestion> DiagnosticsEcartes = new();
            /// <summary>0=NonRenseigne 1=Hypothese 2=Probable 3=Certain</summary>
            public int Certitude;
        }

        public class EcarteSuggestion
        {
            public string Label { get; set; } = "";
            public string Motif { get; set; } = "";
        }

        public async Task<(bool ok, SyntheseSuggestion? suggestion, string? error)> SuggestAsync(
            int? age,
            string motif,
            List<AxisInput> axes,
            CancellationToken ct = default)
        {
            // Filtrage : on ne garde que les axes explorés (Partiel ou Évoqué)
            var explored = axes.Where(a => a.State >= 1 && !string.IsNullOrWhiteSpace(a.Label)).ToList();
            if (explored.Count == 0)
                return (false, null, "Aucun axe exploré (Partiel ou Évoqué) — rien à synthétiser.");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(LlmTimeoutSeconds));

            try
            {
                var prompt = BuildPrompt(age, motif, explored);
                var (ok, raw, err) = await _llm.GenerateTextAsync(prompt, maxTokens: 3500, cancellationToken: cts.Token);
                if (!ok || string.IsNullOrWhiteSpace(raw))
                {
                    var rawLen = raw?.Length ?? 0;
                    var preview = rawLen > 0 ? raw!.Substring(0, Math.Min(200, rawLen)).Replace("\r", " ").Replace("\n", " ") : "(vide)";
                    System.Diagnostics.Debug.WriteLine($"[SyntheseSuggester] LLM réponse inutilisable. ok={ok}, err={err ?? "(null)"}, raw.Length={rawLen}, preview=\"{preview}\"");
                    return (false, null, err ?? (rawLen == 0
                        ? "Réponse LLM vide (le modèle n'a produit aucun contenu — possible filtrage harmony channel sur gpt-oss). Voir Sortie Debug."
                        : "Réponse LLM blanche (whitespace seulement)."));
                }

                var sug = ParseJson(raw);
                if (sug == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[SyntheseSuggester] Parse failed. raw.Length={raw.Length}. Raw :");
                    System.Diagnostics.Debug.WriteLine(raw);
                    System.Diagnostics.Debug.WriteLine("[SyntheseSuggester] FIN raw.");
                    return (false, null, "Parsing JSON impossible (voir Sortie Debug pour la réponse LLM brute).");
                }

                sug.DiagnosticsRetenus = TrimList(sug.DiagnosticsRetenus, MaxRetenus);
                sug.ElementsEnFaveur   = TrimList(sug.ElementsEnFaveur,   MaxEnFaveur);
                sug.DiagnosticsEcartes = sug.DiagnosticsEcartes
                    .Where(e => !string.IsNullOrWhiteSpace(e.Label))
                    .Take(MaxEcartes).ToList();
                if (sug.Certitude < 0 || sug.Certitude > 3) sug.Certitude = 0;

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

        private static List<string> TrimList(List<string> list, int max)
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

        private static string BuildPrompt(int? age, string motif, List<AxisInput> exploredAxes)
        {
            var ageInfo = age.HasValue ? $"{age.Value} ans" : "âge non confirmé";
            var sb = new StringBuilder();
            foreach (var a in exploredAxes)
            {
                var state = a.State == 2 ? "Évoqué" : "Partiel";
                sb.Append($"- {a.Label} ({state})");
                if (!string.IsNullOrWhiteSpace(a.Observation))
                    sb.Append($" — Observation : {a.Observation.Trim()}");
                sb.AppendLine();
            }

            return $@"Tu es pédopsychiatre. Tu produis une SYNTHÈSE DIAGNOSTIQUE à partir d'une évaluation clinique structurée déjà menée.

OBJECTIF : mettre en cohérence le raisonnement diagnostique en 4 zones :
1. Diagnostic(s) retenu(s) : ce que tu poses cliniquement (un ou plusieurs si comorbidité).
2. Éléments cliniques en faveur : observations CONCRÈTES extraites des axes explorés qui soutiennent le(s) diagnostic(s) retenu(s).
3. Diagnostics différentiels écartés : avec un MOTIF clinique précis d'élimination par dx écarté.
4. Niveau de certitude : 1 = hypothèse à confirmer, 2 = probable, 3 = certain.

CONTRAINTES STRICTES :
- Maximum 3 diagnostics retenus.
- Maximum 8 éléments en faveur (chacun court, 5-15 mots, factuel).
- Maximum 5 différentiels écartés (chacun avec motif court 5-15 mots).
- Pas de diagnostic non soutenu par au moins une observation des axes.
- Pas d'inférence au-delà de ce que les observations disent.
- Pas de pistes thérapeutiques (hors périmètre).
- Si rien ne peut être conclu, retourne des tableaux vides + certitude=0.

DONNÉES :

Patient : {ageInfo}
Motif :
{(string.IsNullOrWhiteSpace(motif) ? "(non renseigné)" : motif.Trim())}

Axes explorés (Étape 2) :
{sb.ToString().TrimEnd()}

RÉPONDS UNIQUEMENT par un JSON valide :
{{
  ""diagnostics_retenus"": [""...""],
  ""elements_en_faveur"":  [""...""],
  ""diagnostics_ecartes"": [
    {{ ""label"": ""..."", ""motif"": ""..."" }}
  ],
  ""certitude"": 1
}}";
        }

        private static SyntheseSuggestion? ParseJson(string raw)
        {
            var json = ExtractJson(raw);
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var sug = new SyntheseSuggestion
                {
                    DiagnosticsRetenus = ReadStringList(root, "diagnostics_retenus"),
                    ElementsEnFaveur   = ReadStringList(root, "elements_en_faveur"),
                    Certitude          = ReadInt(root, "certitude")
                };

                if (root.TryGetProperty("diagnostics_ecartes", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var e in arr.EnumerateArray())
                    {
                        if (e.ValueKind != JsonValueKind.Object) continue;
                        var label = e.TryGetProperty("label", out var l) && l.ValueKind == JsonValueKind.String ? l.GetString() ?? "" : "";
                        var motif = e.TryGetProperty("motif", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() ?? "" : "";
                        if (!string.IsNullOrWhiteSpace(label))
                            sug.DiagnosticsEcartes.Add(new EcarteSuggestion { Label = label, Motif = motif });
                    }
                }

                return sug;
            }
            catch
            {
                return null;
            }
        }

        private static List<string> ReadStringList(JsonElement root, string key)
        {
            var res = new List<string>();
            if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array) return res;
            foreach (var e in arr.EnumerateArray())
                if (e.ValueKind == JsonValueKind.String)
                    res.Add(e.GetString() ?? "");
            return res;
        }

        private static int ReadInt(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out var v)) return 0;
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
            if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var n2)) return n2;
            return 0;
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
