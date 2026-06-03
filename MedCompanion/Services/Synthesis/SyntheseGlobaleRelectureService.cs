using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MedCompanion.Models.Evaluations;
using MedCompanion.Models.Synthesis;
using MedCompanion.Services.Evaluations;
using MedCompanion.Services.LLM;

namespace MedCompanion.Services.Synthesis
{
    /// <summary>
    /// V0.5 — Relecture critique d'une Synthèse Globale par Med. Détecte 4 types de
    /// problèmes :
    /// 1. Contradictions internes entre sections
    /// 2. Affirmations non sourcées (risque d'hallucination)
    /// 3. Sections incomplètes vs ce que les sources permettent
    /// 4. Suggestions éditoriales (mineures)
    ///
    /// Garde-fou : en mode incrémental, les contradictions sont plus probables entre
    /// l'ancien (sections inchangées de v(N)) et le nouveau (sections patchées en v(N+1)).
    /// La validation peut être bloquée tant que la relecture n'a pas été lancée au moins
    /// une fois sur la version courante.
    /// </summary>
    public class SyntheseGlobaleRelectureService
    {
        private const int LlmTimeoutSeconds = 90;
        private const int MaxTokens         = 3500;

        private readonly ILLMService _llm;
        private readonly PatientContextService _patientContext;
        private readonly EvaluationPhaseService _evaluationPhaseService;

        public SyntheseGlobaleRelectureService(
            ILLMService llm,
            PatientContextService patientContext,
            EvaluationPhaseService evaluationPhaseService)
        {
            _llm                    = llm;
            _patientContext         = patientContext;
            _evaluationPhaseService = evaluationPhaseService;
        }

        public async Task<(bool ok, List<RelectureFlag>? flags, string? error)> ReleireAsync(
            SyntheseGlobale synthese,
            string patientDirectoryPath,
            CancellationToken ct = default)
        {
            if (synthese == null) return (false, null, "Synthèse nulle.");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(LlmTimeoutSeconds));

            try
            {
                var bundle      = _patientContext.GetCompleteContext(synthese.PatientNomComplet);
                var clinical    = bundle?.ClinicalContext ?? "";
                var evaluations = !string.IsNullOrEmpty(patientDirectoryPath)
                    ? _evaluationPhaseService.LoadAll(patientDirectoryPath)
                        .Where(p => !p.IsActive)
                        .OrderBy(p => p.DateDebut)
                        .ToList()
                    : new List<EvaluationPhase>();

                var prompt = BuildPrompt(synthese, clinical, evaluations);
                var (ok, raw, err) = await _llm.GenerateTextAsync(prompt, maxTokens: MaxTokens, cancellationToken: cts.Token);
                if (!ok || string.IsNullOrWhiteSpace(raw))
                    return (false, null, err ?? "Réponse LLM vide.");

                var flags = ParseJson(raw);
                if (flags == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[SyntheseGlobaleRelecture] Parse failed. Raw : {raw}");
                    return (false, null, "Parsing JSON impossible (voir Sortie Debug).");
                }
                return (true, flags, null);
            }
            catch (OperationCanceledException) { return (false, null, "Délai LLM dépassé."); }
            catch (Exception ex)                { return (false, null, ex.Message); }
        }

        private static string BuildPrompt(
            SyntheseGlobale synthese,
            string clinicalContent,
            List<EvaluationPhase> evaluations)
        {
            var sbSynthese = new StringBuilder();
            foreach (var s in synthese.Sections)
            {
                sbSynthese.AppendLine($"### {s.Titre} [clé: {s.Key}]");
                sbSynthese.AppendLine(string.IsNullOrWhiteSpace(s.Contenu) ? "(vide)" : s.Contenu.Trim());
                sbSynthese.AppendLine();
            }

            var sbEval = new StringBuilder();
            if (evaluations.Count > 0)
            {
                foreach (var ev in evaluations)
                {
                    sbEval.AppendLine($"### Évaluation clôturée {ev.DateCloture:yyyy-MM-dd}");
                    if (ev.BilanFinal.DiagnosticsRetenus.Count > 0)
                    {
                        sbEval.AppendLine("Diagnostics retenus :");
                        foreach (var d in ev.BilanFinal.DiagnosticsRetenus)
                            if (!string.IsNullOrWhiteSpace(d?.Value)) sbEval.AppendLine($"- {d.Value}");
                    }
                    if (!string.IsNullOrWhiteSpace(ev.BilanFinal.SyntheseIntegrative))
                    {
                        sbEval.AppendLine("Synthèse intégrative :");
                        sbEval.AppendLine(ev.BilanFinal.SyntheseIntegrative.Trim());
                    }
                    sbEval.AppendLine();
                }
            }

            return $@"Tu es pédopsychiatre. Tu RELIS CRITIQUEMENT une Synthèse Globale rédigée et tu signales 4 types de problèmes potentiels :

1. CONTRADICTIONS INTERNES — Deux sections disent des choses incompatibles
   Ex : ""Anxiété de séparation"" dans Hypothèses, mais Conclusion parle de ""séparations sans difficulté"".
   Type: ""contradiction"". Renseigne section_cle ET section_cle_secondaire.

2. AFFIRMATIONS NON SOURCÉES — Une phrase n'a pas d'appui dans le dossier source fourni
   Ex : ""L'enfant a un retard de langage majeur"" mais aucune évaluation langage dans les sources.
   Type: ""non_source"". section_cle = section où l'affirmation se trouve.

3. SECTIONS INCOMPLÈTES — Une section est trop courte ou silencieuse alors que les sources offrent de la matière
   Ex : Section ""Environnement"" vide alors qu'une cartographie environnement est présente dans les sources.
   Type: ""incomplete"". section_cle = section à enrichir.

4. SUGGESTIONS ÉDITORIALES — Ton, structure, formulation à améliorer (non bloquant)
   Type: ""suggestion"".

SÉVÉRITÉ :
- ""critique"" : contradiction majeure, hallucination flagrante → à corriger AVANT validation
- ""moyenne""  : à vérifier mais non bloquant
- ""mineure""  : information / suggestion d'amélioration

CONTRAINTES :
- Si tu ne trouves rien, retourne un tableau vide. NE FORCE PAS de signalements artificiels.
- Maximum 10 flags au total (priorise les critiques).
- Sois concret : cite la phrase concernée dans ""detail"".
- Propose une correction concrète quand possible dans ""suggestion"".

[A] SYNTHÈSE GLOBALE À RELIRE :
{sbSynthese.ToString().TrimEnd()}

[B] SOURCES DU DOSSIER (pour vérifier les affirmations) :
{(string.IsNullOrWhiteSpace(clinicalContent) ? "(notes/synthèse vides)" : clinicalContent.Trim())}

[C] ÉVALUATIONS CLÔTURÉES (pour vérifier) :
{(sbEval.Length > 0 ? sbEval.ToString().TrimEnd() : "(aucune)")}

RÉPONDS UNIQUEMENT par un JSON valide :
{{
  ""flags"": [
    {{
      ""type"": ""contradiction|non_source|incomplete|suggestion"",
      ""severite"": ""critique|moyenne|mineure"",
      ""section_cle"": ""hypotheses|enfant|environnement|articulation|conclusion|evolution"",
      ""section_cle_secondaire"": ""..."",
      ""detail"": ""..."",
      ""suggestion"": ""...""
    }}
  ]
}}";
        }

        private static List<RelectureFlag>? ParseJson(string raw)
        {
            var json = ExtractJson(raw);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("flags", out var arr) || arr.ValueKind != JsonValueKind.Array)
                    return new List<RelectureFlag>();

                var flags = new List<RelectureFlag>();
                foreach (var e in arr.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    var f = new RelectureFlag
                    {
                        Type                 = ParseType(ReadString(e, "type")),
                        Severite             = ParseSeverite(ReadString(e, "severite")),
                        SectionCle           = ReadString(e, "section_cle"),
                        SectionCleSecondaire = NullIfEmpty(ReadString(e, "section_cle_secondaire")),
                        Detail               = ReadString(e, "detail"),
                        Suggestion           = ReadString(e, "suggestion"),
                    };
                    if (!string.IsNullOrWhiteSpace(f.Detail))
                        flags.Add(f);
                }
                return flags.Take(10).ToList();
            }
            catch { return null; }
        }

        private static FlagType ParseType(string s) => s switch
        {
            "contradiction" => FlagType.Contradiction,
            "non_source"    => FlagType.NonSource,
            "incomplete"    => FlagType.Incomplete,
            _               => FlagType.Suggestion
        };

        private static FlagSeverite ParseSeverite(string s) => s switch
        {
            "critique" => FlagSeverite.Critique,
            "moyenne"  => FlagSeverite.Moyenne,
            _          => FlagSeverite.Mineure
        };

        private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

        private static string ReadString(JsonElement root, string key)
        {
            if (!root.TryGetProperty(key, out var v)) return "";
            return v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "") : "";
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
