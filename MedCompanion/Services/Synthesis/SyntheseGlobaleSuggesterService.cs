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
    /// V0.2 — Génération initiale d'une Synthèse Globale par Med, à partir du
    /// dossier patient complet. Lit :
    /// - Métadonnées patient (via PatientContextService)
    /// - Synthèse / notes cliniques existantes (via PatientContextService)
    /// - Évaluations clôturées (Bilan Final + cartographies) via EvaluationPhaseService
    ///
    /// Sortie : 5 sections cliniques (Hypothèses, Enfant, Environnement, Articulation,
    /// Conclusion). La 6e section "Évolution" reste vide pour une v1 — elle se remplira
    /// à partir de la v2 (patch incrémental — voir V0.4).
    ///
    /// CONTRAINTE ABSOLUE : ne cite QUE ce qui figure dans les sources. Si une section
    /// n'a pas de matière, le texte renvoyé est "Données insuffisantes — à compléter
    /// par l'observation clinique".
    /// </summary>
    public class SyntheseGlobaleSuggesterService
    {
        private const int LlmTimeoutSeconds = 120;
        private const int MaxTokens         = 4500;
        private const int MaxNotesIncluses  = 5;     // dernières notes (anti-saturation prompt)

        private readonly ILLMService _llm;
        private readonly PatientContextService _patientContext;
        private readonly EvaluationPhaseService _evaluationPhaseService;

        public SyntheseGlobaleSuggesterService(
            ILLMService llm,
            PatientContextService patientContext,
            EvaluationPhaseService evaluationPhaseService)
        {
            _llm                    = llm;
            _patientContext         = patientContext;
            _evaluationPhaseService = evaluationPhaseService;
        }

        public class SyntheseSuggestion
        {
            public string Hypotheses     = "";
            public string Enfant         = "";
            public string Environnement  = "";
            public string Articulation   = "";
            public string Conclusion     = "";
            public List<string> EvaluationsSources = new();
            public int NotesUtilisees;
        }

        /// <summary>
        /// Proposition de patch pour une section : statut diff + nouveau contenu + résumé court.
        /// </summary>
        public class SectionPatch
        {
            public string Cle           = "";   // hypotheses, enfant, environnement, articulation, conclusion, evolution
            public string Statut        = "";   // "inchangee" | "modifiee" | "nouvelle" | "supprimee"
            public string Contenu       = "";
            public string DiffResume    = "";
        }

        public class PatchSuggestion
        {
            public List<SectionPatch> Sections = new();
        }

        /// <summary>
        /// Produit une proposition v0 pour la Synthèse Globale d'un patient.
        /// </summary>
        public async Task<(bool ok, SyntheseSuggestion? suggestion, string? error)>
            GenerateInitialAsync(string patientNomComplet, string patientDirectoryPath, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(patientNomComplet))
                return (false, null, "Nom patient vide.");

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(LlmTimeoutSeconds));

            try
            {
                // 1. Sources cliniques (synthèse existante OU notes)
                var bundle = _patientContext.GetCompleteContext(patientNomComplet);
                var clinicalContent = bundle?.ClinicalContext ?? "";

                // 2. Évaluations clôturées
                var evaluations = !string.IsNullOrEmpty(patientDirectoryPath)
                    ? _evaluationPhaseService.LoadAll(patientDirectoryPath)
                        .Where(p => !p.IsActive)
                        .OrderBy(p => p.DateDebut)
                        .ToList()
                    : new List<EvaluationPhase>();

                if (string.IsNullOrWhiteSpace(clinicalContent) && evaluations.Count == 0)
                    return (false, null, "Aucune donnée clinique au dossier — synthèse impossible.");

                var prompt = BuildPrompt(patientNomComplet, bundle?.Metadata, clinicalContent, evaluations);
                var (ok, raw, err) = await _llm.GenerateTextAsync(prompt, maxTokens: MaxTokens, cancellationToken: cts.Token);
                if (!ok || string.IsNullOrWhiteSpace(raw))
                    return (false, null, err ?? "Réponse LLM vide.");

                var sug = ParseJson(raw);
                if (sug == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[SyntheseGlobaleSuggester] Parse failed. Raw : {raw}");
                    return (false, null, "Parsing JSON impossible (voir Sortie Debug).");
                }

                // Métadonnées sources (traçabilité)
                sug.EvaluationsSources = evaluations
                    .Where(e => e.DateCloture.HasValue)
                    .Select(e => e.DateCloture!.Value.ToString("yyyy-MM-dd"))
                    .ToList();
                sug.NotesUtilisees = string.IsNullOrWhiteSpace(clinicalContent) ? 0 : 1;

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

        /// <summary>
        /// V0.4 — Propose un PATCH d'une synthèse existante : pour chaque section, décide
        /// si elle reste Inchangée, doit être Modifiée, ou est Nouvelle. Reçoit la version
        /// actuelle complète + les évaluations nouvelles depuis. Sortie : statut par section
        /// + contenu nouveau (si Modifiée/Nouvelle) + résumé court du changement.
        /// </summary>
        public async Task<(bool ok, PatchSuggestion? patch, string? error)> SuggestPatchAsync(
            Models.Synthesis.SyntheseGlobale synthese,
            string patientDirectoryPath,
            CancellationToken ct = default)
        {
            if (synthese == null) return (false, null, "Synthèse nulle.");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(LlmTimeoutSeconds));

            try
            {
                // Sources : contexte clinique + évaluations clôturées
                var bundle = _patientContext.GetCompleteContext(synthese.PatientNomComplet);
                var clinicalContent = bundle?.ClinicalContext ?? "";
                var evaluations = !string.IsNullOrEmpty(patientDirectoryPath)
                    ? _evaluationPhaseService.LoadAll(patientDirectoryPath)
                        .Where(p => !p.IsActive)
                        .OrderBy(p => p.DateDebut)
                        .ToList()
                    : new List<EvaluationPhase>();

                var prompt = BuildPatchPrompt(synthese, bundle?.Metadata, clinicalContent, evaluations);
                var (ok, raw, err) = await _llm.GenerateTextAsync(prompt, maxTokens: MaxTokens, cancellationToken: cts.Token);
                if (!ok || string.IsNullOrWhiteSpace(raw))
                    return (false, null, err ?? "Réponse LLM vide.");

                var patch = ParsePatchJson(raw);
                if (patch == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[SyntheseGlobalePatch] Parse failed. Raw : {raw}");
                    return (false, null, "Parsing JSON impossible (voir Sortie Debug).");
                }
                return (true, patch, null);
            }
            catch (OperationCanceledException) { return (false, null, "Délai LLM dépassé."); }
            catch (Exception ex)                { return (false, null, ex.Message); }
        }

        private static string BuildPatchPrompt(
            Models.Synthesis.SyntheseGlobale synthese,
            Models.PatientMetadata? meta,
            string clinicalContent,
            List<EvaluationPhase> evaluations)
        {
            var ageInfo = !string.IsNullOrWhiteSpace(meta?.Dob)
                ? $"né(e) le {meta!.Dob}"
                : "âge non précisé";

            var sbCurrent = new StringBuilder();
            foreach (var s in synthese.Sections)
            {
                sbCurrent.AppendLine($"### {s.Titre} [clé: {s.Key}]");
                sbCurrent.AppendLine(string.IsNullOrWhiteSpace(s.Contenu) ? "(vide)" : s.Contenu.Trim());
                sbCurrent.AppendLine();
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

            return $@"Tu es pédopsychiatre. Tu PROPOSES UN PATCH d'une Synthèse Globale existante en intégrant uniquement les nouveaux éléments du dossier (évaluations clôturées depuis, notes récentes).

PRINCIPE FONDAMENTAL : NE PAS RÉÉCRIRE CE QUI NE CHANGE PAS.

Pour chaque section, décide :
- ""inchangee"" : aucune modification nécessaire (renvoie quand même ""contenu"" identique pour audit)
- ""modifiee""  : retouche nécessaire (renvoie le nouveau contenu COMPLET de la section)
- ""nouvelle""  : la section était vide ou très lacunaire dans la version actuelle, et tu y mets du contenu
- ""supprimee"" : à retirer (rare, à n'utiliser que si la section n'a plus de matière)

Ton clinique direct, prudent. Pas de pistes thérapeutiques. Cite uniquement les sources fournies.

Patient : {synthese.PatientNomComplet} ({ageInfo})
Version actuelle : v{synthese.Version}

[A] SYNTHÈSE ACTUELLE (à patcher) :
{sbCurrent.ToString().TrimEnd()}

[B] Contexte clinique existant (synthèse hors ce dossier, notes) :
{(string.IsNullOrWhiteSpace(clinicalContent) ? "(aucun)" : clinicalContent.Trim())}

[C] Évaluations clôturées du dossier :
{(sbEval.Length > 0 ? sbEval.ToString().TrimEnd() : "(aucune)")}

PARTICULIER POUR LA SECTION ""evolution"" :
- Elle décrit ce qui change DEPUIS la version précédente.
- Si v > 1 et qu'il y a vraiment du nouveau, mets ""statut: nouvelle"" ou ""modifiee"" avec quelques lignes.
- Sinon laisse-la inchangée (vide ou contenu antérieur).

RÉPONDS UNIQUEMENT par un JSON valide, en respectant l'ordre des clés :
{{
  ""sections"": [
    {{ ""cle"": ""hypotheses"",    ""statut"": ""..."", ""contenu"": ""..."", ""diff_resume"": ""..."" }},
    {{ ""cle"": ""enfant"",        ""statut"": ""..."", ""contenu"": ""..."", ""diff_resume"": ""..."" }},
    {{ ""cle"": ""environnement"", ""statut"": ""..."", ""contenu"": ""..."", ""diff_resume"": ""..."" }},
    {{ ""cle"": ""articulation"",  ""statut"": ""..."", ""contenu"": ""..."", ""diff_resume"": ""..."" }},
    {{ ""cle"": ""conclusion"",    ""statut"": ""..."", ""contenu"": ""..."", ""diff_resume"": ""..."" }},
    {{ ""cle"": ""evolution"",     ""statut"": ""..."", ""contenu"": ""..."", ""diff_resume"": ""..."" }}
  ]
}}";
        }

        private static PatchSuggestion? ParsePatchJson(string raw)
        {
            var json = ExtractJson(raw);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("sections", out var arr) || arr.ValueKind != JsonValueKind.Array) return null;

                var patch = new PatchSuggestion();
                foreach (var e in arr.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    var sp = new SectionPatch
                    {
                        Cle        = ReadString(e, "cle"),
                        Statut     = ReadString(e, "statut").ToLowerInvariant(),
                        Contenu    = ReadString(e, "contenu"),
                        DiffResume = ReadString(e, "diff_resume")
                    };
                    if (!string.IsNullOrWhiteSpace(sp.Cle)) patch.Sections.Add(sp);
                }
                return patch.Sections.Count == 0 ? null : patch;
            }
            catch { return null; }
        }

        private static string BuildPrompt(
            string patientNomComplet,
            Models.PatientMetadata? meta,
            string clinicalContent,
            List<EvaluationPhase> evaluations)
        {
            var sbEval = new StringBuilder();
            if (evaluations.Count > 0)
            {
                foreach (var ev in evaluations)
                {
                    sbEval.AppendLine($"### Évaluation clôturée {ev.DateCloture:yyyy-MM-dd}");
                    // Bilan Final
                    if (ev.BilanFinal.DiagnosticsRetenus.Count > 0)
                    {
                        sbEval.AppendLine("Diagnostics retenus :");
                        foreach (var d in ev.BilanFinal.DiagnosticsRetenus)
                            if (!string.IsNullOrWhiteSpace(d?.Value)) sbEval.AppendLine($"- {d.Value}");
                    }
                    if (ev.BilanFinal.ElementsEnFaveur.Count > 0)
                    {
                        sbEval.AppendLine("Éléments cliniques en faveur :");
                        foreach (var e in ev.BilanFinal.ElementsEnFaveur)
                            if (!string.IsNullOrWhiteSpace(e?.Value)) sbEval.AppendLine($"- {e.Value}");
                    }
                    if (ev.BilanFinal.DiagnosticsEcartes.Count > 0)
                    {
                        sbEval.AppendLine("Diagnostics différentiels écartés :");
                        foreach (var ec in ev.BilanFinal.DiagnosticsEcartes)
                            if (!string.IsNullOrWhiteSpace(ec?.Label)) sbEval.AppendLine($"- {ec.Label} ({ec.Motif})");
                    }
                    if (!string.IsNullOrWhiteSpace(ev.BilanFinal.SyntheseIntegrative))
                    {
                        sbEval.AppendLine("Synthèse intégrative du bilan :");
                        sbEval.AppendLine(ev.BilanFinal.SyntheseIntegrative.Trim());
                    }
                    // Cartographie enfant (chenille)
                    var c = ev.CartographieEnfant;
                    if (c.IsValidated || c.Attachement.Score > 0 || c.Langage.Score > 0)
                    {
                        sbEval.AppendLine("Cartographie de l'enfant (chenille) :");
                        sbEval.AppendLine($"- Attachement : {c.Attachement.Score}/6");
                        if (c.Psychomotricite.IsRenseigne)
                            sbEval.AppendLine($"- Psychomotricité : globale={c.Psychomotricite.MotriciteGlobale}/5, fine={c.Psychomotricite.MotriciteFine}/5, tonus={c.Psychomotricite.Tonus}/5, dextérité={c.Psychomotricite.Dexterite}/5, coordination={c.Psychomotricite.Coordination}/5, impulsivité={c.Psychomotricite.ImpulsiviteMotrice}/5");
                        sbEval.AppendLine($"- Langage : {c.Langage.Score}/6");
                        sbEval.AppendLine($"- Émotions : {c.Emotions.Score}/6");
                        sbEval.AppendLine($"- Imaginaire : {c.Imaginaire.Score}/6");
                        sbEval.AppendLine($"- Pensée : {c.Pensee.Score}/6");
                        if (c.Temperament.IsRenseigne)
                            sbEval.AppendLine($"- Tempérament : activité={c.Temperament.NiveauActivite}/5, régularité={c.Temperament.Regularite}/5, réactivité={c.Temperament.ReactiviteSensorielle}/5, intensité={c.Temperament.IntensiteEmotionnelle}/5, adaptabilité={c.Temperament.Adaptabilite}/5");
                        if (c.Attention.IsRenseigne)
                            sbEval.AppendLine($"- Attention & FE : soutenue={c.Attention.AttentionSoutenue}/5, sélective={c.Attention.AttentionSelective}/5, divisée={c.Attention.AttentionDivisee}/5, inhibition={c.Attention.Inhibition}/5, planification={c.Attention.Planification}/5, flexibilité={c.Attention.FlexibiliteAttentionnelle}/5");
                    }
                    // Cartographie environnement (feuilles)
                    var e2 = ev.CartographieEnvironnement;
                    if (e2.IsValidated || HasEnvScore(e2))
                    {
                        sbEval.AppendLine("Cartographie de l'environnement (feuilles) :");
                        AppendFeuille(sbEval, "Famille",            e2.Famille);
                        AppendFeuille(sbEval, "École & Pairs",       e2.EcolePairs);
                        AppendFeuille(sbEval, "Écrans & Médias",     e2.EcransMedias);
                        AppendFeuille(sbEval, "Valeurs sociétales",  e2.ValeursSocietales);
                        AppendFeuille(sbEval, "Cadre éducatif",      e2.CadreEducatif);
                        if (!string.IsNullOrWhiteSpace(e2.LectureBrancheMed))
                        {
                            sbEval.AppendLine("Lecture globale environnement :");
                            sbEval.AppendLine(e2.LectureBrancheMed.Trim());
                        }
                    }
                    sbEval.AppendLine();
                }
            }
            else
            {
                sbEval.AppendLine("(aucune évaluation clôturée disponible)");
            }

            var ageInfo = !string.IsNullOrWhiteSpace(meta?.Dob)
                ? $"né(e) le {meta!.Dob}"
                : "âge non précisé";

            return $@"Tu es pédopsychiatre. Tu produis la PREMIÈRE VERSION (v1) d'une SYNTHÈSE GLOBALE STRUCTURÉE pour un enfant suivi en consultation, à partir du dossier complet.

OBJECTIF : produire un document de référence de ~700-1200 mots, structuré en 5 sections cliniques, qui servira de SOURCE DE VÉRITÉ pour les futures consultations.

CONTRAINTE ABSOLUE :
- Tu cites UNIQUEMENT ce qui figure dans les sources fournies ci-dessous. Aucune inférence au-delà.
- Si une section n'a pas de matière dans les sources, tu retournes EXACTEMENT le texte : ""Données insuffisantes — à compléter par l'observation clinique.""
- Ton clinique direct, prudent (« compatible avec », « semble », « tendance à »).
- Pas de pistes thérapeutiques (réservé au Projet Thérapeutique).
- Pas de métaphores poétiques (le destinataire est un confrère).

SECTIONS À PRODUIRE :
1. Hypothèses diagnostiques retenues (100-200 mots) — diagnostics évoqués/retenus avec leur fondement
2. Compréhension du fonctionnement de l'enfant (200-300 mots) — capacités, sensibilités, vulnérabilités
3. Compréhension de l'environnement (200-300 mots) — famille, école, écrans, valeurs, cadre
4. Articulation clinique (150-200 mots) — comment ces éléments interagissent
5. Conclusion intégrative globale (150-250 mots) — vue d'ensemble, fil rouge clinique

DONNÉES :

Patient : {patientNomComplet} ({ageInfo})

[A] Contexte clinique existant (synthèse ou notes) :
{(string.IsNullOrWhiteSpace(clinicalContent) ? "(aucun)" : clinicalContent.Trim())}

[B] Évaluations clôturées :
{sbEval.ToString().TrimEnd()}

RÉPONDS UNIQUEMENT par un JSON valide :
{{
  ""hypotheses"":    ""..."",
  ""enfant"":        ""..."",
  ""environnement"": ""..."",
  ""articulation"":  ""..."",
  ""conclusion"":    ""...""
}}";
        }

        private static bool HasEnvScore(CartographieEnvironnement c)
            => c.Famille.NervureCentrale.Score > 0
            || c.EcolePairs.NervureCentrale.Score > 0
            || c.EcransMedias.NervureCentrale.Score > 0
            || c.ValeursSocietales.NervureCentrale.Score > 0
            || c.CadreEducatif.NervureCentrale.Score > 0;

        private static void AppendFeuille(StringBuilder sb, string label, FeuilleEnvironnement f)
        {
            var couleur = EnvironnementScoringService.CalculerFeuille(f);
            sb.AppendLine($"- {label} : {couleur}");
            if (!string.IsNullOrWhiteSpace(f.LectureMed))
                sb.AppendLine($"  Lecture : {f.LectureMed.Replace("\r", "").Replace("\n", " ").Trim()}");
        }

        private static SyntheseSuggestion? ParseJson(string raw)
        {
            var json = ExtractJson(raw);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                return new SyntheseSuggestion
                {
                    Hypotheses    = ReadString(root, "hypotheses"),
                    Enfant        = ReadString(root, "enfant"),
                    Environnement = ReadString(root, "environnement"),
                    Articulation  = ReadString(root, "articulation"),
                    Conclusion    = ReadString(root, "conclusion"),
                };
            }
            catch { return null; }
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
