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
    /// Construit les axes d'observation de la 3ᵉ séance à partir de l'ORIENTATION DIAGNOSTIQUE
    /// validée par le médecin.
    ///
    /// DEUX TEMPS, ET NON UN SEUL APPEL :
    ///
    /// 1. Un appel pose les axes eux-mêmes — intitulé + ce que l'axe vient trancher. Sortie très
    ///    courte, donc rapide, et le médecin voit la charpente avant que le détail arrive.
    /// 2. Un appel par axe produit ses propositions observables. Chacun sait ce que SON axe sert,
    ///    ce qu'un appel global ne permettrait pas : les propositions seraient formulées avant que
    ///    le rattachement de l'axe soit décidé.
    ///
    /// Même bénéfice que pour l'orientation : un axe manqué n'emporte pas les autres, chaque appel
    /// est court, et le dossier placé en tête de tous les prompts est mis en cache par llama.cpp —
    /// les appels 2..n ne repaient pas la passe de prompt.
    /// </summary>
    public class AxesCiblesSuggesterService
    {
        private const int LlmTimeoutSeconds = 90;
        private const int MaxTokensAxes     = 400;   // 5 axes : intitulé + une ligne
        private const int MaxTokensItems    = 350;   // 6 constats courts

        /// <summary>
        /// Au plus 5 axes. Même raison clinique que le cap de 3 de l'orientation : un axe, c'est du
        /// temps d'observation dans une séance qui en a peu. Au-delà, la liste cesse de cibler.
        /// </summary>
        public const int MaxAxes = 5;

        public const int MaxPropositions = Models.Evaluations.AxeCible.MaxPropositions;

        private readonly ILLMService _llm;

        public AxesCiblesSuggesterService(ILLMService llm) => _llm = llm;

        public class AxeSuggere
        {
            public string       Intitule     { get; set; } = "";
            public string       Rattachement { get; set; } = "";
            public List<string> Propositions { get; set; } = new();
        }

        /// <summary>Ce que le médecin a validé à l'orientation — la matière première des axes.</summary>
        public class Orientation
        {
            public List<string> Hypotheses    { get; set; } = new();
            public List<string> Differentiels { get; set; } = new();
            public List<string> AEliminer     { get; set; } = new();
            public List<string> Vigilance     { get; set; } = new();
            public List<string> Questions     { get; set; } = new();

            public bool EstVide => Hypotheses.Count + Differentiels.Count + AEliminer.Count
                                 + Vigilance.Count + Questions.Count == 0;
        }

        /// <param name="onAxe">
        /// Appelé dès qu'un axe est complet (intitulé + propositions). C'est ce qui fait apparaître
        /// les axes l'un après l'autre au lieu d'un écran figé.
        /// </param>
        /// <param name="onProgres">Libellé d'avancement pour le bandeau.</param>
        public async Task<(bool ok, List<AxeSuggere>? axes, string? error)> SuggestAsync(
            int? age,
            string motif,
            Orientation orientation,
            string cartographie = "",
            Action<AxeSuggere>? onAxe = null,
            Action<string>? onProgres = null,
            CancellationToken ct = default)
        {
            // Sans orientation, il n'y a rien à cibler. Le dire plutôt que produire des axes
            // génériques, qui seraient exactement l'inventaire que cette étape cherche à éviter.
            if (orientation.EstVide)
                return (false, null, "Aucune orientation diagnostique posée — commencez par la rubrique précédente.");

            var socle = BuildSocle(age, motif, orientation, cartographie);

            onProgres?.Invoke("⏳ Construction des axes…");

            var (okAxes, axes, errAxes) = await DemanderAxesAsync(socle, ct);
            if (!okAxes || axes == null || axes.Count == 0)
                return (false, null, errAxes ?? "Aucun axe n'a pu être construit.");

            var echecs = new List<string>();
            for (var i = 0; i < axes.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var axe = axes[i];

                onProgres?.Invoke($"⏳ {i + 1}/{axes.Count} — {axe.Intitule}…");

                var (ok, items, err) = await DemanderPropositionsAsync(socle, axe, axes.Take(i).ToList(), ct);
                if (ok) axe.Propositions = items;
                else
                {
                    // L'axe reste, sans ses propositions : sa charpente vaut mieux que rien, et le
                    // médecin peut l'observer quand même. On note pour le dire.
                    if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                    echecs.Add(axe.Intitule);
                    System.Diagnostics.Debug.WriteLine($"[AxesCibles] {axe.Intitule} : {err}");
                }

                onAxe?.Invoke(axe);
            }

            return (true, axes, echecs.Count == 0
                ? null
                : $"Propositions non générées pour : {string.Join(", ", echecs)}.");
        }

        // ── Temps 1 : les axes ────────────────────────────────────────────────

        private async Task<(bool ok, List<AxeSuggere>? axes, string? err)> DemanderAxesAsync(
            string socle, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(LlmTimeoutSeconds));

            var sb = new StringBuilder(socle);
            sb.AppendLine();
            sb.AppendLine("═══ TRAVAIL DEMANDÉ : LES AXES ═══");
            sb.AppendLine();
            sb.AppendLine($"Construis au maximum {MaxAxes} AXES D'OBSERVATION pour la séance qui vient.");
            sb.AppendLine();
            sb.AppendLine("RÈGLES :");
            sb.AppendLine("- Chaque axe doit se rattacher EXPLICITEMENT à un élément de l'orientation ci-dessus");
            sb.AppendLine("  (une hypothèse, un différentiel, un élément à éliminer, un point de vigilance ou");
            sb.AppendLine("  une question clinique). Reprends cet élément mot pour mot dans « rattachement ».");
            sb.AppendLine("  Un axe qui ne se rattache à rien n'est pas un axe : ne le propose pas.");
            sb.AppendLine("- Intitulé COURT : 1 à 3 mots (ex. « Attention soutenue », « Séparation »).");
            sb.AppendLine("- Deux axes ne doivent pas explorer la même chose. Une difficulté observée une fois");
            sb.AppendLine("  ne doit pas pouvoir être cochée deux fois : elle paraîtrait deux fois plus lourde.");
            sb.AppendLine("- Moins d'axes vaut mieux qu'un axe de remplissage.");
            sb.AppendLine();
            sb.AppendLine("Réponds uniquement par : {\"axes\": [{\"intitule\": \"...\", \"rattachement\": \"...\"}]}");

            try
            {
                var (ok, raw, err) = await AppelerAsync(sb.ToString(), "axes_cibles", SchemaAxes, MaxTokensAxes, cts.Token);
                if (!ok || string.IsNullOrWhiteSpace(raw)) return (false, null, err ?? "réponse vide");

                var axes = ParseAxes(raw);
                if (axes == null) return (false, null, "JSON illisible");

                return (true, axes.Take(MaxAxes).ToList(), null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (OperationCanceledException) { return (false, null, $"délai dépassé ({LlmTimeoutSeconds} s)"); }
            catch (Exception ex) { return (false, null, ex.Message); }
        }

        // ── Temps 2 : les propositions d'un axe ───────────────────────────────

        private async Task<(bool ok, List<string> items, string? err)> DemanderPropositionsAsync(
            string socle, AxeSuggere axe, List<AxeSuggere> precedents, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(LlmTimeoutSeconds));

            var sb = new StringBuilder(socle);
            sb.AppendLine();

            if (precedents.Count > 0)
            {
                sb.AppendLine("═══ AXES DÉJÀ TRAITÉS ═══");
                sb.AppendLine();
                sb.AppendLine("Ne reprends AUCUNE de ces propositions, même reformulée : un même constat coché");
                sb.AppendLine("sous deux axes compterait deux fois.");
                sb.AppendLine();
                foreach (var p in precedents)
                {
                    sb.AppendLine($"{p.Intitule} :");
                    foreach (var i in p.Propositions) sb.AppendLine($"  - {i}");
                }
                sb.AppendLine();
            }

            sb.AppendLine($"═══ TRAVAIL DEMANDÉ : PROPOSITIONS DE L'AXE « {axe.Intitule} » ═══");
            sb.AppendLine();
            sb.AppendLine($"Cet axe sert à trancher : {axe.Rattachement}");
            sb.AppendLine();
            sb.AppendLine($"Formule 4 à {MaxPropositions} CONSTATS que le médecin cochera OUI ou NON pendant la séance.");
            sb.AppendLine();
            sb.AppendLine("RÈGLES — la plus importante d'abord :");
            sb.AppendLine("- Un CONSTAT, jamais une inférence. « Se retourne quand on entre dans la pièce » se");
            sb.AppendLine("  coche ; « trouble de l'attention » ne se coche pas, il se conclut. Si répondre à ta");
            sb.AppendLine("  proposition demande d'interpréter, elle est mal formulée.");
            sb.AppendLine("- Observable DANS LE CABINET, pendant cette séance. Rien qui suppose l'école, la");
            sb.AppendLine("  maison, ou le récit d'un tiers.");
            sb.AppendLine("- Court : 4 à 10 mots. Affirmatif, au présent.");
            sb.AppendLine("- Adapté à l'âge indiqué dans le dossier ci-dessus.");
            sb.AppendLine("- Une proposition à laquelle on peut répondre OUI **et** NON est mal découpée : n'en");
            sb.AppendLine("  garde qu'une seule idée par ligne.");
            sb.AppendLine();
            sb.AppendLine("Réponds uniquement par : {\"propositions\": [\"...\"]}");

            try
            {
                var (ok, raw, err) = await AppelerAsync(sb.ToString(), "propositions_axe", SchemaPropositions, MaxTokensItems, cts.Token);
                if (!ok || string.IsNullOrWhiteSpace(raw)) return (false, new List<string>(), err ?? "réponse vide");

                var items = ParseListe(raw, "propositions");
                if (items == null) return (false, new List<string>(), "JSON illisible");

                return (true, Nettoyer(items, MaxPropositions), null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (OperationCanceledException) { return (false, new List<string>(), $"délai dépassé ({LlmTimeoutSeconds} s)"); }
            catch (Exception ex) { return (false, new List<string>(), ex.Message); }
        }

        // ── Socle commun ──────────────────────────────────────────────────────

        /// <summary>
        /// Identique et EN TÊTE de tous les prompts : c'est ce qui rend les appels suivants quasi
        /// gratuits en passe de prompt (cache de préfixe llama.cpp). Ne pas déplacer après la
        /// consigne.
        /// </summary>
        private static string BuildSocle(int? age, string motif, Orientation o, string cartographie)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Tu es pédopsychiatre. Tu prépares l'ÉVALUATION CIBLÉE d'une séance d'évaluation.");
            sb.AppendLine();
            sb.AppendLine("Le médecin a déjà posé son orientation diagnostique. Ton travail est d'en dériver ce");
            sb.AppendLine("qu'il ira OBSERVER dans la séance — pas de rediscuter l'orientation, pas de conclure.");
            sb.AppendLine();
            sb.AppendLine("═══ DOSSIER ═══");
            sb.AppendLine();
            sb.AppendLine($"Enfant : {(age.HasValue ? $"{age.Value} ans" : "âge non confirmé")}");
            sb.AppendLine($"Motif : {(string.IsNullOrWhiteSpace(motif) ? "(non renseigné)" : motif.Trim())}");
            sb.AppendLine();
            sb.AppendLine("─ Orientation diagnostique validée par le médecin ─");
            Liste(sb, "Hypothèses principales", o.Hypotheses);
            Liste(sb, "Diagnostics différentiels", o.Differentiels);
            Liste(sb, "À éliminer prudemment", o.AEliminer);
            Liste(sb, "Points de vigilance", o.Vigilance);
            Liste(sb, "Questions cliniques", o.Questions);
            sb.AppendLine();
            sb.AppendLine("─ Cartographie de l'enfant ─");
            sb.AppendLine(string.IsNullOrWhiteSpace(cartographie) ? "(non réalisée)" : cartographie.Trim());

            return sb.ToString();

            static void Liste(StringBuilder sb, string titre, List<string> items)
            {
                if (items.Count == 0) return;
                sb.AppendLine($"{titre} :");
                foreach (var i in items) sb.AppendLine($"  - {i}");
            }
        }

        // ── Appel ─────────────────────────────────────────────────────────────

        private Task<(bool success, string result, string? error)> AppelerAsync(
            string prompt, string nom, string schema, int maxTokens, CancellationToken ct)
            => _llm is IStructuredOutputService s && s.SupportsStructuredOutput
                ? s.GenerateJsonAsync(prompt, nom, schema, maxTokens, ct)
                : _llm.GenerateTextAsync(prompt, maxTokens, ct);

        private const string SchemaAxes = """
        {
          "type": "object",
          "properties": {
            "axes": {
              "type": "array",
              "maxItems": 5,
              "items": {
                "type": "object",
                "properties": {
                  "intitule":     { "type": "string" },
                  "rattachement": { "type": "string" }
                },
                "required": ["intitule", "rattachement"],
                "additionalProperties": false
              }
            }
          },
          "required": ["axes"],
          "additionalProperties": false
        }
        """;

        private const string SchemaPropositions = """
        {
          "type": "object",
          "properties": {
            "propositions": { "type": "array", "maxItems": 6, "items": { "type": "string" } }
          },
          "required": ["propositions"],
          "additionalProperties": false
        }
        """;

        // ── Parsing ───────────────────────────────────────────────────────────

        private static List<string> Nettoyer(List<string> items, int max)
        {
            var res = new List<string>();
            foreach (var s in items)
            {
                var t = (s ?? "").Trim();
                if (t.Length == 0) continue;
                if (res.Count >= max) break;
                res.Add(t);
            }
            return res;
        }

        private static List<AxeSuggere>? ParseAxes(string raw)
        {
            var json = ExtractJson(raw);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("axes", out var arr) || arr.ValueKind != JsonValueKind.Array)
                    return null;

                var res = new List<AxeSuggere>();
                foreach (var e in arr.EnumerateArray())
                {
                    var intitule = Lire(e, "intitule");
                    if (string.IsNullOrWhiteSpace(intitule)) continue;
                    res.Add(new AxeSuggere { Intitule = intitule, Rattachement = Lire(e, "rattachement") });
                }
                return res;
            }
            catch { return null; }

            static string Lire(JsonElement e, string k)
                => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";
        }

        private static List<string>? ParseListe(string raw, string cle)
        {
            var json = ExtractJson(raw);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty(cle, out var arr) || arr.ValueKind != JsonValueKind.Array)
                    return null;

                var res = new List<string>();
                foreach (var e in arr.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String) res.Add(e.GetString() ?? "");
                return res;
            }
            catch { return null; }
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
