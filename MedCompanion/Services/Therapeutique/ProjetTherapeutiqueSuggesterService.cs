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

        /// <summary>
        /// V1.2 — Patch sur une action existante OU nouvelle action proposée.
        /// </summary>
        public class ActionPatch
        {
            public string Id              = "";   // vide → nouvelle action ; sinon = ID existant
            public string Statut          = "";   // "inchangee" | "modifiee" | "nouvelle" | "a_archiver"
            public string Libelle         = "";
            public string Description     = "";
            public string IndicateurReussite = "";
            public string LienSyntheseSection = "";
            public string DiffResume      = "";
        }

        public class SectionTextPatch
        {
            public string Statut     = "";   // "inchangee" | "modifiee"
            public string Contenu    = "";
            public string DiffResume = "";
        }

        public class ProjetPatchSuggestion
        {
            public SectionTextPatch Objectifs       = new();
            public SectionTextPatch Ressources      = new();
            public SectionTextPatch Reevaluation    = new();
            public SectionTextPatch CoConstruction  = new();

            public List<ActionPatch> ActionsMedicales         = new();
            public List<ActionPatch> ActionsPsychologiques    = new();
            public List<ActionPatch> ActionsDeveloppementales = new();
            public List<ActionPatch> ActionsEnvironnementales = new();
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

        /// <summary>
        /// V1.2 — Propose un PATCH d'un Projet Thérapeutique existant : pour chaque
        /// section et chaque action, statut Inchangee/Modifiee/Nouvelle/AArchiver +
        /// contenu nouveau (si Modifiee/Nouvelle) + résumé court du changement.
        /// </summary>
        public async Task<(bool ok, ProjetPatchSuggestion? patch, string? error)> SuggestPatchAsync(
            ProjetTherapeutique projet,
            string patientDirectoryPath,
            CancellationToken ct = default)
        {
            if (projet == null) return (false, null, "Projet nul.");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(LlmTimeoutSeconds));

            try
            {
                var derniereSynth = _syntheseGlobaleService.GetDerniereValidee(projet.PatientNomComplet);
                SyntheseGlobale? synthese = null;
                if (derniereSynth != null) synthese = _syntheseGlobaleService.Load(derniereSynth.FilePath);

                var evaluations = !string.IsNullOrEmpty(patientDirectoryPath)
                    ? _evaluationPhaseService.LoadAll(patientDirectoryPath)
                        .Where(p => !p.IsActive)
                        .OrderBy(p => p.DateDebut)
                        .ToList()
                    : new List<EvaluationPhase>();

                var bundle = _patientContext.GetCompleteContext(projet.PatientNomComplet);
                var clinicalContent = bundle?.ClinicalContext ?? "";

                var prompt = BuildPatchPrompt(projet, synthese, evaluations, clinicalContent);
                var (ok, raw, err) = await _llm.GenerateTextAsync(prompt, maxTokens: MaxTokens, cancellationToken: cts.Token);
                if (!ok || string.IsNullOrWhiteSpace(raw))
                    return (false, null, err ?? "Réponse LLM vide.");

                var patch = ParsePatchJson(raw);
                if (patch == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[ProjetPatch] Parse failed. Raw : {raw}");
                    return (false, null, "Parsing JSON impossible (voir Sortie Debug).");
                }
                return (true, patch, null);
            }
            catch (OperationCanceledException) { return (false, null, "Délai LLM dépassé."); }
            catch (Exception ex)                { return (false, null, ex.Message); }
        }

        private static string BuildPatchPrompt(
            ProjetTherapeutique projet,
            SyntheseGlobale? synthese,
            List<EvaluationPhase> evaluations,
            string clinicalContent)
        {
            var sbCurrent = new StringBuilder();
            sbCurrent.AppendLine($"### Objectifs prioritaires");
            sbCurrent.AppendLine(string.IsNullOrWhiteSpace(projet.ObjectifsPrioritaires) ? "(vide)" : projet.ObjectifsPrioritaires.Trim());
            sbCurrent.AppendLine();
            sbCurrent.AppendLine($"### Ressources à soutenir");
            sbCurrent.AppendLine(string.IsNullOrWhiteSpace(projet.RessourcesASoutenir) ? "(vide)" : projet.RessourcesASoutenir.Trim());
            sbCurrent.AppendLine();
            sbCurrent.AppendLine($"### Réévaluation prévue (checklist)");
            sbCurrent.AppendLine(string.IsNullOrWhiteSpace(projet.ReevaluationChecklist) ? "(vide)" : projet.ReevaluationChecklist.Trim());
            sbCurrent.AppendLine();
            sbCurrent.AppendLine($"### Co-construction famille");
            sbCurrent.AppendLine(string.IsNullOrWhiteSpace(projet.CoConstructionFamille) ? "(vide)" : projet.CoConstructionFamille.Trim());
            sbCurrent.AppendLine();

            AppendActions(sbCurrent, "Prise en charge médicale",        projet.ActionsMedicales);
            AppendActions(sbCurrent, "Prise en charge psychologique",   projet.ActionsPsychologiques);
            AppendActions(sbCurrent, "Accompagnement développemental",  projet.ActionsDeveloppementales);
            AppendActions(sbCurrent, "Actions sur l'environnement",     projet.ActionsEnvironnementales);

            var sbSynth = new StringBuilder();
            if (synthese != null)
            {
                foreach (var s in synthese.Sections)
                {
                    if (string.IsNullOrWhiteSpace(s.Contenu)) continue;
                    sbSynth.AppendLine($"### {s.Titre}");
                    sbSynth.AppendLine(s.Contenu.Trim());
                    sbSynth.AppendLine();
                }
            }

            return $@"Tu es pédopsychiatre. Tu PROPOSES UN PATCH d'un Projet Thérapeutique existant en intégrant uniquement les nouveaux éléments (nouvelles évaluations clôturées, notes récentes, ou évolution de la Synthèse Globale).

PRINCIPE FONDAMENTAL : NE PAS RÉÉCRIRE CE QUI NE CHANGE PAS.

Pour chaque section TEXTE LIBRE (objectifs / ressources / réévaluation / co-construction), choisis :
- ""inchangee"" : pas de modif (renvoie contenu vide)
- ""modifiee""  : retoucher (renvoie contenu nouveau COMPLET + diff_resume)

Pour chaque ACTION existante, choisis :
- ""inchangee""  : action toujours pertinente telle quelle
- ""modifiee""   : ajuster libellé / description / indicateur (renvoie le nouveau contenu complet)
- ""a_archiver"" : suggérer de retirer cette action (n'est plus pertinente)
- ""nouvelle""   : interdit pour les actions existantes (réservé aux ajouts)

Pour AJOUTER UNE NOUVELLE ACTION : statut ""nouvelle"" avec id vide.

CONTRAINTES :
- Ton clinique direct, prudent.
- Cite uniquement ce que la synthèse / les sources soutiennent.
- Pas de prescription précise sans appui.
- Max 2 actions nouvelles par section (rester chirurgical).

Patient : {projet.PatientNomComplet}
Version actuelle du projet : v{projet.Version}

[A] PROJET ACTUEL (à patcher) :
{sbCurrent.ToString().TrimEnd()}

[B] SYNTHÈSE GLOBALE actuelle :
{(sbSynth.Length > 0 ? sbSynth.ToString().TrimEnd() : "(aucune)")}

[C] Évaluations clôturées : {evaluations.Count}

[D] Notes / contexte :
{(string.IsNullOrWhiteSpace(clinicalContent) ? "(aucun)" : clinicalContent.Trim())}

RÉPONDS UNIQUEMENT par un JSON valide (les listes d'actions DOIVENT inclure une entrée par action existante, avec son id) :
{{
  ""objectifs"":       {{ ""statut"": ""..."", ""contenu"": ""..."", ""diff_resume"": ""..."" }},
  ""ressources"":      {{ ""statut"": ""..."", ""contenu"": ""..."", ""diff_resume"": ""..."" }},
  ""reevaluation"":    {{ ""statut"": ""..."", ""contenu"": ""..."", ""diff_resume"": ""..."" }},
  ""co_construction"": {{ ""statut"": ""..."", ""contenu"": ""..."", ""diff_resume"": ""..."" }},
  ""actions_medicales"": [
    {{ ""id"": ""..."", ""statut"": ""..."", ""libelle"": ""..."", ""description"": ""..."", ""indicateur_reussite"": ""..."", ""lien_synthese_section"": ""..."", ""diff_resume"": ""..."" }}
  ],
  ""actions_psychologiques"":    [ {{ ""id"": ""..."", ""statut"": ""..."", ""libelle"": ""..."", ""description"": ""..."", ""indicateur_reussite"": ""..."", ""lien_synthese_section"": ""..."", ""diff_resume"": ""..."" }} ],
  ""actions_developpementales"": [ {{ ""id"": ""..."", ""statut"": ""..."", ""libelle"": ""..."", ""description"": ""..."", ""indicateur_reussite"": ""..."", ""lien_synthese_section"": ""..."", ""diff_resume"": ""..."" }} ],
  ""actions_environnementales"": [ {{ ""id"": ""..."", ""statut"": ""..."", ""libelle"": ""..."", ""description"": ""..."", ""indicateur_reussite"": ""..."", ""lien_synthese_section"": ""..."", ""diff_resume"": ""..."" }} ]
}}";
        }

        private static void AppendActions(StringBuilder sb, string section, IEnumerable<ProjetAction> actions)
        {
            sb.AppendLine($"### Actions — {section}");
            var list = actions.ToList();
            if (list.Count == 0) { sb.AppendLine("(aucune)"); sb.AppendLine(); return; }
            foreach (var a in list)
            {
                sb.AppendLine($"- [id={a.Id}] [statut={a.Statut}] {a.Libelle}");
                if (!string.IsNullOrWhiteSpace(a.Description))
                    sb.AppendLine($"  Description : {a.Description}");
                if (!string.IsNullOrWhiteSpace(a.IndicateurReussite))
                    sb.AppendLine($"  Indicateur : {a.IndicateurReussite}");
            }
            sb.AppendLine();
        }

        private static ProjetPatchSuggestion? ParsePatchJson(string raw)
        {
            var json = ExtractJson(raw);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var patch = new ProjetPatchSuggestion
                {
                    Objectifs      = ReadTextPatch(root, "objectifs"),
                    Ressources     = ReadTextPatch(root, "ressources"),
                    Reevaluation   = ReadTextPatch(root, "reevaluation"),
                    CoConstruction = ReadTextPatch(root, "co_construction"),
                    ActionsMedicales         = ReadActionPatches(root, "actions_medicales"),
                    ActionsPsychologiques    = ReadActionPatches(root, "actions_psychologiques"),
                    ActionsDeveloppementales = ReadActionPatches(root, "actions_developpementales"),
                    ActionsEnvironnementales = ReadActionPatches(root, "actions_environnementales"),
                };
                return patch;
            }
            catch { return null; }
        }

        private static SectionTextPatch ReadTextPatch(JsonElement root, string key)
        {
            var p = new SectionTextPatch();
            if (!root.TryGetProperty(key, out var e) || e.ValueKind != JsonValueKind.Object) return p;
            p.Statut     = ReadString(e, "statut").ToLowerInvariant();
            p.Contenu    = ReadString(e, "contenu");
            p.DiffResume = ReadString(e, "diff_resume");
            return p;
        }

        private static List<ActionPatch> ReadActionPatches(JsonElement root, string key)
        {
            var list = new List<ActionPatch>();
            if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array) return list;
            foreach (var e in arr.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                list.Add(new ActionPatch
                {
                    Id                  = ReadString(e, "id"),
                    Statut              = ReadString(e, "statut").ToLowerInvariant(),
                    Libelle             = ReadString(e, "libelle"),
                    Description         = ReadString(e, "description"),
                    IndicateurReussite  = ReadString(e, "indicateur_reussite"),
                    LienSyntheseSection = ReadString(e, "lien_synthese_section"),
                    DiffResume          = ReadString(e, "diff_resume"),
                });
            }
            return list;
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
