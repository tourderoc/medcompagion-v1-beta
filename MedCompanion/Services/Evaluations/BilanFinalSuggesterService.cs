using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MedCompanion.Models.Evaluations;
using MedCompanion.Services.LLM;

namespace MedCompanion.Services.Evaluations
{
    /// <summary>
    /// Génère une proposition de Bilan Final (Étape 5) qui croise les axes (Étape 2),
    /// la cartographie de l'enfant (Étape 3) et la cartographie de l'environnement
    /// (Étape 4). Sortie : diagnostics retenus, éléments en faveur, différentiels
    /// écartés, niveau de certitude, et un paragraphe synthèse intégrative qui croise
    /// les 3 sources cliniques.
    /// </summary>
    public class BilanFinalSuggesterService
    {
        private const int LlmTimeoutSeconds = 90;
        private const int MaxTokens         = 4500;
        // Caps anti-LLM-trop-large
        private const int MaxRetenus  = 3;
        private const int MaxEnFaveur = 8;
        private const int MaxEcartes  = 5;

        private readonly ILLMService _llm;

        public BilanFinalSuggesterService(ILLMService llm) { _llm = llm; }

        public class AxisInput
        {
            public string Label         { get; set; } = "";
            public string Justification { get; set; } = "";
            public int    State         { get; set; }   // 0=NonAborde, 1=Partiel, 2=Evoque
            public string Observation   { get; set; } = "";
        }

        public class BilanFinalSuggestion
        {
            public List<string>           DiagnosticsRetenus  = new();
            public List<string>           ElementsEnFaveur    = new();
            public List<EcarteSuggestion> DiagnosticsEcartes  = new();
            /// <summary>0=NonRenseigne 1=Hypothese 2=Probable 3=Certain</summary>
            public int Certitude;
            /// <summary>Paragraphe synthétique qui croise axes + cartographies.</summary>
            public string SyntheseIntegrative = "";
        }

        public class EcarteSuggestion
        {
            public string Label { get; set; } = "";
            public string Motif { get; set; } = "";
        }

        public async Task<(bool ok, BilanFinalSuggestion? suggestion, string? error)> SuggestAsync(
            int? age,
            string motif,
            List<AxisInput> axes,
            CartographieEnfant? cartoEnfant,
            CartographieEnvironnement? cartoEnv,
            CancellationToken ct = default)
        {
            var explored = axes.Where(a => a.State >= 1 && !string.IsNullOrWhiteSpace(a.Label)).ToList();
            var hasCartoEnfant = cartoEnfant != null && HasAnyChenilleScore(cartoEnfant);
            var hasCartoEnv    = cartoEnv != null && HasAnyEnvScore(cartoEnv);

            if (explored.Count == 0 && !hasCartoEnfant && !hasCartoEnv)
                return (false, null, "Aucune donnée d'évaluation (axes, cartographies) — rien à synthétiser.");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(LlmTimeoutSeconds));

            try
            {
                var prompt = BuildPrompt(age, motif, explored, cartoEnfant, cartoEnv);
                var (ok, raw, err) = await _llm.GenerateTextAsync(prompt, maxTokens: MaxTokens, cancellationToken: cts.Token);
                if (!ok || string.IsNullOrWhiteSpace(raw))
                {
                    var rawLen = raw?.Length ?? 0;
                    var preview = rawLen > 0 ? raw!.Substring(0, Math.Min(200, rawLen)).Replace("\r", " ").Replace("\n", " ") : "(vide)";
                    System.Diagnostics.Debug.WriteLine($"[BilanFinalSuggester] LLM réponse inutilisable. ok={ok}, err={err ?? "(null)"}, raw.Length={rawLen}, preview=\"{preview}\"");
                    return (false, null, err ?? (rawLen == 0
                        ? "Réponse LLM vide (le modèle n'a produit aucun contenu — possible filtrage harmony channel sur gpt-oss). Voir Sortie Debug."
                        : "Réponse LLM blanche (whitespace seulement)."));
                }

                var sug = ParseJson(raw);
                if (sug == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[BilanFinalSuggester] Parse failed. raw.Length={raw.Length}. Raw :");
                    System.Diagnostics.Debug.WriteLine(raw);
                    System.Diagnostics.Debug.WriteLine("[BilanFinalSuggester] FIN raw.");
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

        private static bool HasAnyChenilleScore(CartographieEnfant c)
            => c.Attachement.Score > 0     || c.Psychomotricite.Score > 0
            || c.Langage.Score > 0         || c.Emotions.Score > 0
            || c.Imaginaire.Score > 0      || c.Pensee.Score > 0;

        private static bool HasAnyEnvScore(CartographieEnvironnement c)
            => EnvFeuilleHasScore(c.Famille)        || EnvFeuilleHasScore(c.EcolePairs)
            || EnvFeuilleHasScore(c.EcransMedias)   || EnvFeuilleHasScore(c.ValeursSocietales)
            || EnvFeuilleHasScore(c.CadreEducatif);

        private static bool EnvFeuilleHasScore(FeuilleEnvironnement f)
        {
            if (f.NervureCentrale.Score > 0) return true;
            foreach (var s in f.NervuresSecondaires) if (s.Score > 0) return true;
            return false;
        }

        private static string BuildPrompt(
            int? age, string motif,
            List<AxisInput> explored,
            CartographieEnfant? cartoEnfant,
            CartographieEnvironnement? cartoEnv)
        {
            var ageInfo = age.HasValue ? $"{age.Value} ans" : "âge non confirmé";

            var sbAxes = new StringBuilder();
            foreach (var a in explored)
            {
                var state = a.State == 2 ? "Évoqué" : "Partiel";
                sbAxes.Append($"- {a.Label} ({state})");
                if (!string.IsNullOrWhiteSpace(a.Observation))
                    sbAxes.Append($" — Observation : {a.Observation.Trim()}");
                sbAxes.AppendLine();
            }
            if (explored.Count == 0) sbAxes.AppendLine("(aucun axe exploré)");

            var sbEnfant = new StringBuilder();
            if (cartoEnfant != null && HasAnyChenilleScore(cartoEnfant))
            {
                AppendChenilleSegment(sbEnfant, "Attachement",      cartoEnfant.Attachement);
                AppendChenilleSegment(sbEnfant, "Psychomotricité",  cartoEnfant.Psychomotricite);
                AppendChenilleSegment(sbEnfant, "Langage",          cartoEnfant.Langage);
                AppendChenilleSegment(sbEnfant, "Émotions",         cartoEnfant.Emotions);
                AppendChenilleSegment(sbEnfant, "Imaginaire",       cartoEnfant.Imaginaire);
                AppendChenilleSegment(sbEnfant, "Pensée",           cartoEnfant.Pensee);
                var temp = cartoEnfant.Temperament;
                if (temp != null && temp.IsRenseigne)
                    sbEnfant.AppendLine($"- Tempérament : activité={temp.NiveauActivite}/5, régularité={temp.Regularite}/5, réactivité={temp.ReactiviteSensorielle}/5, intensité={temp.IntensiteEmotionnelle}/5, adaptabilité={temp.Adaptabilite}/5, temps réaction={temp.TempsDeReaction}/5");
            }
            else
            {
                sbEnfant.AppendLine("(cartographie de l'enfant non renseignée)");
            }

            var sbEnv = new StringBuilder();
            if (cartoEnv != null && HasAnyEnvScore(cartoEnv))
            {
                AppendFeuille(sbEnv, cartoEnv.Famille);
                AppendFeuille(sbEnv, cartoEnv.EcolePairs);
                AppendFeuille(sbEnv, cartoEnv.EcransMedias);
                AppendFeuille(sbEnv, cartoEnv.ValeursSocietales);
                AppendFeuille(sbEnv, cartoEnv.CadreEducatif);
                if (!string.IsNullOrWhiteSpace(cartoEnv.LectureBrancheMed))
                {
                    sbEnv.AppendLine();
                    sbEnv.AppendLine($"Lecture globale de la branche environnement :");
                    sbEnv.AppendLine(cartoEnv.LectureBrancheMed.Trim());
                }
            }
            else
            {
                sbEnv.AppendLine("(cartographie de l'environnement non renseignée)");
            }

            return $@"Tu es pédopsychiatre. Tu produis un BILAN FINAL à partir d'une évaluation clinique structurée déjà menée à 3 niveaux : axes DSM, cartographie développementale de l'enfant (chenille), cartographie de son environnement (feuilles dimensionnelles).

OBJECTIF : mettre en cohérence le raisonnement clinique en 5 zones :
1. Diagnostic(s) retenu(s) : ce que tu poses cliniquement (un ou plusieurs si comorbidité).
2. Éléments cliniques en faveur : observations CONCRÈTES, puisées dans les 3 sources (axes, chenille, environnement), qui soutiennent le(s) diagnostic(s) retenu(s).
3. Diagnostics différentiels écartés : avec un MOTIF clinique précis d'élimination par dx écarté.
4. Niveau de certitude : 1 = hypothèse à confirmer, 2 = probable, 3 = certain.
5. Synthèse intégrative : 10-15 lignes structurées en 3 paragraphes courts :
   a) État global : ce qui tient, ce qui craque, à quel niveau (enfant, environnement, axes).
   b) Convergences : où plusieurs signaux se croisent (axes + chenille + feuilles fragiles).
   c) Point central à creuser ou orientation clinique.

CONTRAINTES STRICTES :
- Maximum 3 diagnostics retenus.
- Maximum 8 éléments en faveur (chacun court, 5-15 mots, factuel).
- Maximum 5 différentiels écartés (chacun avec motif court 5-15 mots).
- Pas de diagnostic non soutenu par au moins une observation des 3 sources.
- Pas d'inférence au-delà de ce que les sources disent.
- Pas de pistes thérapeutiques (réservé Projet Thérapeutique).
- Synthèse intégrative : ton clinique direct, pas de métaphores poétiques.
- Si rien ne peut être conclu, retourne des tableaux vides, synthèse_integrative vide, certitude=0.

DONNÉES :

Patient : {ageInfo}
Motif :
{(string.IsNullOrWhiteSpace(motif) ? "(non renseigné)" : motif.Trim())}

[1/3] Axes explorés (Étape 2) :
{sbAxes.ToString().TrimEnd()}

[2/3] Cartographie de l'enfant (Étape 3 — chenille développementale) :
{sbEnfant.ToString().TrimEnd()}

[3/3] Cartographie de l'environnement (Étape 4 — feuilles dimensionnelles) :
{sbEnv.ToString().TrimEnd()}

RÉPONDS UNIQUEMENT par un JSON valide :
{{
  ""diagnostics_retenus"": [""...""],
  ""elements_en_faveur"":  [""...""],
  ""diagnostics_ecartes"": [
    {{ ""label"": ""..."", ""motif"": ""..."" }}
  ],
  ""certitude"": 1,
  ""synthese_integrative"": ""...""
}}";
        }

        private static void AppendChenilleSegment(StringBuilder sb, string label, ChenilleSegment seg)
        {
            sb.AppendLine($"- {label} : {seg.Score}/6");
        }

        private static void AppendFeuille(StringBuilder sb, FeuilleEnvironnement f)
        {
            var couleurFeuille = EnvironnementScoringService.CalculerFeuille(f);
            sb.AppendLine($"- {f.Label} ({f.SousTitre}) : {couleurFeuille}");
            if (!string.IsNullOrWhiteSpace(f.LectureMed))
                sb.AppendLine($"  Lecture : {f.LectureMed.Replace("\r", "").Replace("\n", " ").Trim()}");
        }

        private static BilanFinalSuggestion? ParseJson(string raw)
        {
            var json = ExtractJson(raw);
            if (string.IsNullOrEmpty(json)) return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var sug = new BilanFinalSuggestion
                {
                    DiagnosticsRetenus  = ReadStringList(root, "diagnostics_retenus"),
                    ElementsEnFaveur    = ReadStringList(root, "elements_en_faveur"),
                    Certitude           = ReadInt(root, "certitude"),
                    SyntheseIntegrative = ReadString(root, "synthese_integrative")
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

        private static string ReadString(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out var v)) return "";
            if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? "";
            return "";
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
