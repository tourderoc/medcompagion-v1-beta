using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MedCompanion.Models.Urgences;
using MedCompanion.Services.LLM;

namespace MedCompanion.Services.Urgence.Detectors
{
    /// <summary>
    /// Détecteur LLM de risque suicidaire — analyse sémantique d'une note de consultation pédopsy.
    /// Renvoie un signal si une évaluation structurée par le médecin est justifiée.
    /// JAMAIS un niveau de risque (Faible/Modéré/Élevé) — c'est le médecin qui statue.
    /// </summary>
    public class SuicideRiskDetector : IUrgenceDetector
    {
        public string UrgenceType => "risque_suicidaire";
        public string Name        => "SuicideRiskDetector_v1";

        // Seuil de confidence en dessous duquel on n'émet PAS de signal (anti-faux-positif).
        private const double MinConfidence = 0.40;

        // Délai max alloué à l'appel LLM avant fallback (en secondes).
        private const int LlmTimeoutSeconds = 25;

        private readonly ILLMService _llm;
        private readonly IUrgenceDetector? _fallback;

        public SuicideRiskDetector(ILLMService llmService, IUrgenceDetector? fallback = null)
        {
            _llm      = llmService;
            _fallback = fallback;
        }

        public async Task<UrgenceSignal?> DetectAsync(UrgenceNoteContext context, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(context.NoteContent)) return null;

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(LlmTimeoutSeconds));

            try
            {
                var prompt = BuildPrompt(context);
                var (ok, result, err) = await _llm.GenerateTextAsync(prompt, maxTokens: 600, cancellationToken: cts.Token);

                if (!ok || string.IsNullOrWhiteSpace(result))
                {
                    System.Diagnostics.Debug.WriteLine($"[SuicideRiskDetector] LLM échec : {err}. Fallback keyword.");
                    return _fallback != null ? await _fallback.DetectAsync(context, ct) : null;
                }

                var parsed = ParseJsonResponse(result);
                if (parsed == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[SuicideRiskDetector] Parsing JSON échoué. Fallback keyword. Réponse: {Truncate(result, 200)}");
                    return _fallback != null ? await _fallback.DetectAsync(context, ct) : null;
                }

                if (!parsed.Alert || parsed.Confidence < MinConfidence)
                {
                    System.Diagnostics.Debug.WriteLine($"[SuicideRiskDetector] Pas d'alerte (alert={parsed.Alert}, conf={parsed.Confidence:0.00}).");
                    return null;
                }

                return new UrgenceSignal
                {
                    Type              = UrgenceType,
                    PatientNomComplet = context.PatientNomComplet,
                    DetecteurName     = Name,
                    DetectionDate     = DateTime.Now,
                    Confidence        = parsed.Confidence,
                    Passages          = parsed.Passages.Take(8).ToList(),
                    Motif             = parsed.MotifPedopsy ?? "",
                    NoteSourcePath    = context.NoteFilePath
                };
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine("[SuicideRiskDetector] Timeout LLM. Fallback keyword.");
                return _fallback != null ? await _fallback.DetectAsync(context, ct) : null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SuicideRiskDetector] Exception : {ex.Message}. Fallback keyword.");
                return _fallback != null ? await _fallback.DetectAsync(context, ct) : null;
            }
        }

        // ── Prompt ────────────────────────────────────────────────────────────

        private static string BuildPrompt(UrgenceNoteContext ctx)
        {
            var ageInfo = ctx.PatientAge.HasValue ? $"{ctx.PatientAge.Value} ans" : "âge non confirmé";
            var typeInfo = ctx.ConsultationType switch
            {
                "consultation-premiere" => "première consultation",
                "consultation-suivi"    => "consultation de suivi",
                _                       => ctx.ConsultationType
            };

            return $@"Tu es un assistant clinique spécialisé en pédopsychiatrie. Ta tâche : analyser cette note de consultation et déterminer s'il y a des signaux de risque suicidaire qui justifient que le médecin ouvre une évaluation structurée.

RÈGLE ABSOLUE : tu NE poses PAS un niveau de risque. Tu identifies SEULEMENT si une évaluation par le médecin est pertinente. Le médecin décidera ensuite Faible/Modéré/Élevé.

═══════════════════════════════════════════════════════════════
SIGNAUX À CONSIDÉRER COMME ALERTE (alert=true)
═══════════════════════════════════════════════════════════════

Idéation suicidaire directe :
  • ""envie de mourir"", ""en finir"", ""me suicider"", ""me tuer""
  • ""idées suicidaires"", ""idées noires"" structurées

Idéation indirecte mais préoccupante :
  • ""tout le monde serait mieux sans moi""
  • ""j'aimerais juste dormir et plus me réveiller""
  • ""ça sert plus à rien"", ""à quoi bon""
  • ""disparaître"", ""ne plus être là""

Comportements/contenus à risque :
  • Mention d'un scénario, d'un moyen, d'une date, d'un lieu (pont, médicaments, pendaison)
  • Recherches internet de moyens
  • Préparation de matériel
  • Antécédents de tentative de suicide (TS) — récent ou ancien
  • Scarifications, automutilations
  • Adieux, don d'objets personnels
  • Désinvestissement massif récent + anhédonie sévère + désespoir
  • Publication inquiétante réseaux sociaux après harcèlement
  • Accumulation de médicaments au domicile signalée

═══════════════════════════════════════════════════════════════
SIGNAUX À NE PAS CONSIDÉRER COMME ALERTE (alert=false)
═══════════════════════════════════════════════════════════════

  • Tristesse ou pleurs sans contenu suicidaire
  • Mention de la mort d'un proche (deuil) SANS idéation propre du patient
    → ex: ""propos de mort après décès du grand-père"" = DEUIL, pas alerte
  • Métaphores : ""je suis mort de fatigue"", ""ça me tue""
  • Propos suicidaires expressifs en colère SANS intentionnalité ni scénario,
    chez un jeune enfant en crise de frustration
    → ex: ""propos suicidaires lors de colères, sans intentionnalité structurée""
       = expression émotionnelle, pas alerte
  • Idées de fuite ou de fugue sans dimension auto-agressive
  • Conduites à risque (alcool, conduite dangereuse) SEULES sans idéation
    associée — note possible en signal indirect mais confidence basse (~0.4)

═══════════════════════════════════════════════════════════════
SPÉCIFICITÉS PÉDOPSYCHIATRIQUES PAR ÂGE
═══════════════════════════════════════════════════════════════

  • < 12 ans : passage à l'acte impulsif possible, accès aux moyens
    (médicaments parents) = facteur aggravant. Mais propos suicidaires
    en crise de colère SANS structuration ≠ alerte.
  • 12-15 ans : harcèlement scolaire + isolement = facteur de risque majeur.
  • Adolescent (≥ 15 ans) : scénario plus structuré possible, rupture
    sentimentale = déclencheur fréquent, antécédent de TS très significatif.

═══════════════════════════════════════════════════════════════
FORMAT DE RÉPONSE OBLIGATOIRE
═══════════════════════════════════════════════════════════════

Réponds UNIQUEMENT par un objet JSON valide, rien d'autre :

{{
  ""alert"": true ou false,
  ""confidence"": nombre entre 0.0 et 1.0,
  ""passages"": [""citation exacte 1"", ""citation exacte 2""],
  ""motif_pedopsy"": ""explication brève (1-2 phrases) du raisonnement""
}}

- Si alert=false → confidence reflète la certitude que ce N'EST PAS une alerte (peut être 0.9 pour un deuil clair)
- Si alert=true → confidence reflète la pertinence d'une évaluation par le médecin
- passages = citations TEXTUELLES extraites de la note (préserve les guillemets, l'orthographe). 2 à 5 passages max.
- motif_pedopsy = pourquoi tu alertes ou pas, en tenant compte de l'âge

═══════════════════════════════════════════════════════════════
NOTE À ANALYSER
═══════════════════════════════════════════════════════════════

Patient : {ageInfo}
Type de consultation : {typeInfo}
{(string.IsNullOrWhiteSpace(ctx.MotifConsultation) ? "" : $"Motif détecté : {ctx.MotifConsultation}\n")}
─── Contenu de la note ───
{ctx.NoteContent.Trim()}
─── Fin de note ───

Réponds par le JSON uniquement.";
        }

        // ── Parser JSON robuste ───────────────────────────────────────────────

        private class LlmResponse
        {
            public bool         Alert        { get; set; }
            public double       Confidence   { get; set; }
            public List<string> Passages     { get; set; } = new();
            public string?      MotifPedopsy { get; set; }
        }

        private static LlmResponse? ParseJsonResponse(string raw)
        {
            // Le LLM peut entourer le JSON de texte. On extrait le 1er { ... } équilibré.
            var json = ExtractJsonObject(raw);
            if (string.IsNullOrWhiteSpace(json)) return null;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var res = new LlmResponse();

                if (root.TryGetProperty("alert", out var alertEl))
                {
                    res.Alert = alertEl.ValueKind switch
                    {
                        JsonValueKind.True  => true,
                        JsonValueKind.False => false,
                        JsonValueKind.String => alertEl.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false,
                        _ => false
                    };
                }

                if (root.TryGetProperty("confidence", out var confEl))
                {
                    res.Confidence = confEl.ValueKind switch
                    {
                        JsonValueKind.Number => confEl.GetDouble(),
                        JsonValueKind.String when double.TryParse(confEl.GetString(),
                                                  System.Globalization.NumberStyles.Float,
                                                  System.Globalization.CultureInfo.InvariantCulture,
                                                  out var v) => v,
                        _ => 0.0
                    };
                }

                if (root.TryGetProperty("passages", out var passagesEl) && passagesEl.ValueKind == JsonValueKind.Array)
                {
                    foreach (var p in passagesEl.EnumerateArray())
                        if (p.ValueKind == JsonValueKind.String)
                            res.Passages.Add(p.GetString() ?? "");
                }

                if (root.TryGetProperty("motif_pedopsy", out var motifEl) && motifEl.ValueKind == JsonValueKind.String)
                    res.MotifPedopsy = motifEl.GetString();

                return res;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Extrait le premier objet JSON équilibré de la chaîne (gère le texte autour, les ```json fences).
        /// </summary>
        private static string ExtractJsonObject(string raw)
        {
            // Retirer les fences markdown ```json ... ```
            raw = Regex.Replace(raw, @"```(?:json)?\s*", "", RegexOptions.IgnoreCase);
            raw = raw.Replace("```", "");

            int start = raw.IndexOf('{');
            if (start < 0) return "";
            int depth = 0;
            bool inString = false;
            bool escape   = false;
            for (int i = start; i < raw.Length; i++)
            {
                var c = raw[i];
                if (escape) { escape = false; continue; }
                if (c == '\\') { escape = true; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (inString) continue;
                if (c == '{') depth++;
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0) return raw.Substring(start, i - start + 1);
                }
            }
            return "";
        }

        private static string Truncate(string s, int n) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n) + "…");
    }
}
