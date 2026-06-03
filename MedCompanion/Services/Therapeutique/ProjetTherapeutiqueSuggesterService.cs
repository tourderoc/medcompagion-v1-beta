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
using MedCompanion.Models.Therapeutique;
using MedCompanion.Services.Evaluations;
using MedCompanion.Services.LLM;
using MedCompanion.Services.Synthesis;

namespace MedCompanion.Services.Therapeutique
{
    /// <summary>
    /// V1.1 — Propose une version v0 du Projet Thérapeutique à partir de la dernière
    /// Synthèse Globale validée du patient, complétée par les évaluations clôturées
    /// et le contexte clinique (notes).
    ///
    /// Sortie structurée :
    /// - Texte pour les sections 1 (Objectifs), 6 (Ressources), 7 (Réévaluation +
    ///   checklist) et Co-construction famille
    /// - Listes d'actions structurées pour les sections 2-5 (médical / psy /
    ///   développemental / environnement) avec libellé, description, indicateur de
    ///   réussite et lien vers la section de la Synthèse qui motive l'action
    ///
    /// Contrainte stricte : ne cite QUE ce qui figure dans la Synthèse et les sources.
    /// Pas d'invention de diagnostic ou de prescription non motivée.
    /// </summary>
    public class ProjetTherapeutiqueSuggesterService
    {
        private const int LlmTimeoutSeconds = 120;
        private const int MaxTokens         = 5500;
        // Caps anti-LLM-trop-large : 3-5 actions par section, formulation courte
        private const int MaxActionsParSection = 5;

        private readonly ILLMService _llm;
        private readonly PatientContextService _patientContext;
        private readonly EvaluationPhaseService _evaluationPhaseService;
        private readonly SyntheseGlobaleService _syntheseGlobaleService;

        public ProjetTherapeutiqueSuggesterService(
            ILLMService llm,
            PatientContextService patientContext,
            EvaluationPhaseService evaluationPhaseService,
            SyntheseGlobaleService syntheseGlobaleService)
        {
            _llm                    = llm;
            _patientContext         = patientContext;
            _evaluationPhaseService = evaluationPhaseService;
            _syntheseGlobaleService = syntheseGlobaleService;
        }

        public class ActionSuggestion
        {
            public string Libelle            = "";
            public string Description        = "";
            public string IndicateurReussite = "";
            public string LienSyntheseSection = "";   // hypotheses|enfant|environnement|articulation|conclusion
        }

        public class ProjetSuggestion
        {
            public string ObjectifsPrioritaires { get; set; } = "";
            public string RessourcesASoutenir   { get; set; } = "";
            public string ReevaluationChecklist { get; set; } = "";
            public string CoConstructionFamille { get; set; } = "";
            public List<ActionSuggestion> ActionsMedicales         { get; set; } = new();
            public List<ActionSuggestion> ActionsPsychologiques    { get; set; } = new();
            public List<ActionSuggestion> ActionsDeveloppementales { get; set; } = new();
            public List<ActionSuggestion> ActionsEnvironnementales { get; set; } = new();

            public string? SyntheseGlobaleSourceFichier { get; set; }
            public int SyntheseGlobaleSourceVersion { get; set; }
        }

        public async Task<(bool ok, ProjetSuggestion? suggestion, string? error)> GenerateInitialAsync(
            string patientNomComplet,
            string patientDirectoryPath,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(patientNomComplet))
                return (false, null, "Nom patient vide.");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(LlmTimeoutSeconds));

            try
            {
                // 1. Dernière Synthèse Globale validée (source principale)
                var derniereSynthVersion = _syntheseGlobaleService.GetDerniereValidee(patientNomComplet);
                SyntheseGlobale? synthese = null;
                if (derniereSynthVersion != null)
                    synthese = _syntheseGlobaleService.Load(derniereSynthVersion.FilePath);

                // 2. Évaluations clôturées (pour diagnostics + cartographies)
                var evaluations = !string.IsNullOrEmpty(patientDirectoryPath)
                    ? _evaluationPhaseService.LoadAll(patientDirectoryPath)
                        .Where(p => !p.IsActive)
                        .OrderBy(p => p.DateDebut)
                        .ToList()
                    : new List<EvaluationPhase>();

                // 3. Notes / contexte
                var bundle = _patientContext.GetCompleteContext(patientNomComplet);
                var clinicalContent = bundle?.ClinicalContext ?? "";

                if (synthese == null && evaluations.Count == 0 && string.IsNullOrWhiteSpace(clinicalContent))
                    return (false, null, "Aucune donnée clinique (synthèse, évaluation, notes) — proposition impossible.");

                var prompt = BuildPrompt(patientNomComplet, bundle?.Metadata, synthese, evaluations, clinicalContent);
                var (ok, raw, err) = await _llm.GenerateTextAsync(prompt, maxTokens: MaxTokens, cancellationToken: cts.Token);
                if (!ok || string.IsNullOrWhiteSpace(raw))
                    return (false, null, err ?? "Réponse LLM vide.");

                var sug = ParseJson(raw);
                if (sug == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[ProjetSuggester] Parse failed. Raw : {raw}");
                    return (false, null, "Parsing JSON impossible (voir Sortie Debug).");
                }

                sug.SyntheseGlobaleSourceFichier = derniereSynthVersion?.FileName;
                sug.SyntheseGlobaleSourceVersion = derniereSynthVersion?.Version ?? 0;

                // Cap : 5 actions max par section
                sug.ActionsMedicales         = sug.ActionsMedicales.Take(MaxActionsParSection).ToList();
                sug.ActionsPsychologiques    = sug.ActionsPsychologiques.Take(MaxActionsParSection).ToList();
                sug.ActionsDeveloppementales = sug.ActionsDeveloppementales.Take(MaxActionsParSection).ToList();
                sug.ActionsEnvironnementales = sug.ActionsEnvironnementales.Take(MaxActionsParSection).ToList();

                return (true, sug, null);
            }
            catch (OperationCanceledException) { return (false, null, "Délai LLM dépassé."); }
            catch (Exception ex)                { return (false, null, ex.Message); }
        }

        private static string BuildPrompt(
            string patientNomComplet,
            Models.PatientMetadata? meta,
            SyntheseGlobale? synthese,
            List<EvaluationPhase> evaluations,
            string clinicalContent)
        {
            var ageInfo = !string.IsNullOrWhiteSpace(meta?.Dob)
                ? $"né(e) le {meta!.Dob}"
                : "âge non précisé";

            var sbSynth = new StringBuilder();
            if (synthese != null)
            {
                foreach (var s in synthese.Sections)
                {
                    if (string.IsNullOrWhiteSpace(s.Contenu)) continue;
                    sbSynth.AppendLine($"### {s.Titre} [clé: {s.Key}]");
                    sbSynth.AppendLine(s.Contenu.Trim());
                    sbSynth.AppendLine();
                }
            }
            else
            {
                sbSynth.AppendLine("(aucune synthèse globale validée — base sur les évaluations et les notes)");
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

            return $@"Tu es pédopsychiatre. Tu produis une PROPOSITION DE PROJET THÉRAPEUTIQUE STRUCTURÉ à partir de la dernière Synthèse Globale validée et du dossier.

OBJECTIF : produire un plan d'action concret, sourcé, prudent, qui sera validé / modifié / complété par le clinicien.

ARCHITECTURE DU PROJET :
- 7 sections fixes :
  1. Objectifs prioritaires (texte libre, 100-200 mots, 3-5 objectifs hiérarchisés)
  2. Prise en charge médicale (liste d'actions structurées)
  3. Prise en charge psychologique (liste d'actions structurées)
  4. Accompagnement développemental (liste d'actions structurées — orthophonie, psychomot, ergo, neuropsy, etc.)
  5. Actions sur l'environnement (liste d'actions structurées — famille, école, écrans, cadre)
  6. Ressources à soutenir (texte libre, 80-150 mots — forces de l'enfant, alliés, ressources familiales)
  7. Réévaluation prévue (checklist texte libre — points à revérifier dans 3-6 mois)
- Section transverse : Co-construction avec la famille (texte libre 50-100 mots)

CONTRAINTES STRICTES :
- Tu cites UNIQUEMENT ce que la synthèse ou les sources soutiennent. AUCUNE invention.
- Pas de prescription médicamenteuse précise sans appui dans la synthèse. Préfère ""évaluer indication X"" plutôt que ""démarrer X mg""
- 3-5 actions MAXIMUM par section actions (pas une liste exhaustive)
- Pour chaque action :
  • libelle : court (50-80 caractères max), action concrète au verbe d'action
  • description : 1-2 phrases de précision si nécessaire
  • indicateur_reussite : critère mesurable (qualitatif ou quantitatif)
  • lien_synthese_section : clé de la section de la synthèse qui motive cette action
    (hypotheses | enfant | environnement | articulation | conclusion)
- Si une section n'a pas matière à action concrète, retourne une liste vide.
- Si pas de synthèse globale disponible, sois encore plus prudent et ne propose que des actions évidentes.
- Ton clinique direct, prudent.

DONNÉES :

Patient : {patientNomComplet} ({ageInfo})

[A] SYNTHÈSE GLOBALE (source principale) :
{sbSynth.ToString().TrimEnd()}

[B] Évaluations clôturées :
{(sbEval.Length > 0 ? sbEval.ToString().TrimEnd() : "(aucune)")}

[C] Notes cliniques / synthèse hors-ce-dossier :
{(string.IsNullOrWhiteSpace(clinicalContent) ? "(aucune)" : clinicalContent.Trim())}

RÉPONDS UNIQUEMENT par un JSON valide :
{{
  ""objectifs_prioritaires"":  ""..."",
  ""ressources_a_soutenir"":   ""..."",
  ""reevaluation_checklist"":  ""..."",
  ""co_construction_famille"": ""..."",
  ""actions_medicales"":         [ {{ ""libelle"": ""..."", ""description"": ""..."", ""indicateur_reussite"": ""..."", ""lien_synthese_section"": ""..."" }} ],
  ""actions_psychologiques"":    [ {{ ""libelle"": ""..."", ""description"": ""..."", ""indicateur_reussite"": ""..."", ""lien_synthese_section"": ""..."" }} ],
  ""actions_developpementales"": [ {{ ""libelle"": ""..."", ""description"": ""..."", ""indicateur_reussite"": ""..."", ""lien_synthese_section"": ""..."" }} ],
  ""actions_environnementales"": [ {{ ""libelle"": ""..."", ""description"": ""..."", ""indicateur_reussite"": ""..."", ""lien_synthese_section"": ""..."" }} ]
}}";
        }

        private static ProjetSuggestion? ParseJson(string raw)
        {
            var json = ExtractJson(raw);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var sug = new ProjetSuggestion
                {
                    ObjectifsPrioritaires = ReadString(root, "objectifs_prioritaires"),
                    RessourcesASoutenir   = ReadString(root, "ressources_a_soutenir"),
                    ReevaluationChecklist = ReadString(root, "reevaluation_checklist"),
                    CoConstructionFamille = ReadString(root, "co_construction_famille"),
                    ActionsMedicales         = ReadActions(root, "actions_medicales"),
                    ActionsPsychologiques    = ReadActions(root, "actions_psychologiques"),
                    ActionsDeveloppementales = ReadActions(root, "actions_developpementales"),
                    ActionsEnvironnementales = ReadActions(root, "actions_environnementales"),
                };
                return sug;
            }
            catch { return null; }
        }

        private static List<ActionSuggestion> ReadActions(JsonElement root, string key)
        {
            var list = new List<ActionSuggestion>();
            if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
            foreach (var e in arr.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                var a = new ActionSuggestion
                {
                    Libelle             = ReadString(e, "libelle"),
                    Description         = ReadString(e, "description"),
                    IndicateurReussite  = ReadString(e, "indicateur_reussite"),
                    LienSyntheseSection = ReadString(e, "lien_synthese_section"),
                };
                if (!string.IsNullOrWhiteSpace(a.Libelle)) list.Add(a);
            }
            return list;
        }

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
