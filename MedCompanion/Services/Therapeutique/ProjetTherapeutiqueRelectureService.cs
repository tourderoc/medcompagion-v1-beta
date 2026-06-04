using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MedCompanion.Models.Synthesis;
using MedCompanion.Models.Therapeutique;
using MedCompanion.Services.LLM;
using MedCompanion.Services.Synthesis;

namespace MedCompanion.Services.Therapeutique
{
    /// <summary>
    /// V1.4 — Relecture critique d'un Projet Thérapeutique par Med. Vérifie la
    /// cohérence Synthèse ↔ Projet et la qualité opérationnelle des actions :
    ///
    /// 1. Action sans justification (lien_synthese_section absent ou pointant ailleurs)
    /// 2. Dimension critique de la Synthèse non couverte par une action
    /// 3. Contradiction entre actions ou entre action et synthèse
    /// 4. Action sans indicateur de réussite mesurable
    /// 5. Objectif prioritaire sans action correspondante
    /// 6. Suggestion éditoriale (mineure)
    ///
    /// Garde-fou : les flags critiques bloquent la validation tant que non traités.
    /// </summary>
    public class ProjetTherapeutiqueRelectureService
    {
        private const int LlmTimeoutSeconds = 90;
        private const int MaxTokens         = 3500;

        private readonly ILLMService _llm;
        private readonly SyntheseGlobaleService _syntheseGlobaleService;

        public ProjetTherapeutiqueRelectureService(ILLMService llm, SyntheseGlobaleService syntheseGlobaleService)
        {
            _llm                    = llm;
            _syntheseGlobaleService = syntheseGlobaleService;
        }

        public async Task<(bool ok, List<ProjetRelectureFlag>? flags, string? error)> ReleireAsync(
            ProjetTherapeutique projet,
            CancellationToken ct = default)
        {
            if (projet == null) return (false, null, "Projet nul.");
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(LlmTimeoutSeconds));

            try
            {
                // Charger la synthèse de référence (celle qui a motivé ce projet)
                SyntheseGlobale? synthese = null;
                if (!string.IsNullOrWhiteSpace(projet.SyntheseGlobaleSourceFichier))
                {
                    var dir = _syntheseGlobaleService.GetRootDirectory(projet.PatientNomComplet);
                    var path = System.IO.Path.Combine(dir, projet.SyntheseGlobaleSourceFichier);
                    if (System.IO.File.Exists(path)) synthese = _syntheseGlobaleService.Load(path);
                }
                // Fallback : dernière validée
                if (synthese == null)
                {
                    var derniere = _syntheseGlobaleService.GetDerniereValidee(projet.PatientNomComplet);
                    if (derniere != null) synthese = _syntheseGlobaleService.Load(derniere.FilePath);
                }

                var prompt = BuildPrompt(projet, synthese);
                var (ok, raw, err) = await _llm.GenerateTextAsync(prompt, maxTokens: MaxTokens, cancellationToken: cts.Token);
                if (!ok || string.IsNullOrWhiteSpace(raw))
                    return (false, null, err ?? "Réponse LLM vide.");

                var flags = ParseJson(raw, projet);
                if (flags == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[ProjetRelecture] Parse failed. Raw : {raw}");
                    return (false, null, "Parsing JSON impossible (voir Sortie Debug).");
                }
                return (true, flags, null);
            }
            catch (OperationCanceledException) { return (false, null, "Délai LLM dépassé."); }
            catch (Exception ex)                { return (false, null, ex.Message); }
        }

        private static string BuildPrompt(ProjetTherapeutique projet, SyntheseGlobale? synthese)
        {
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
                sbSynth.AppendLine("(aucune synthèse de référence — base ta relecture sur le projet lui-même)");
            }

            var sbProjet = new StringBuilder();
            sbProjet.AppendLine("### Objectifs prioritaires");
            sbProjet.AppendLine(string.IsNullOrWhiteSpace(projet.ObjectifsPrioritaires) ? "(vide)" : projet.ObjectifsPrioritaires.Trim());
            sbProjet.AppendLine();
            AppendActions(sbProjet, "Prise en charge médicale",        projet.ActionsMedicales);
            AppendActions(sbProjet, "Prise en charge psychologique",   projet.ActionsPsychologiques);
            AppendActions(sbProjet, "Accompagnement développemental",  projet.ActionsDeveloppementales);
            AppendActions(sbProjet, "Actions sur l'environnement",     projet.ActionsEnvironnementales);
            sbProjet.AppendLine("### Ressources à soutenir");
            sbProjet.AppendLine(string.IsNullOrWhiteSpace(projet.RessourcesASoutenir) ? "(vide)" : projet.RessourcesASoutenir.Trim());
            sbProjet.AppendLine();
            sbProjet.AppendLine("### Réévaluation (checklist)");
            sbProjet.AppendLine(string.IsNullOrWhiteSpace(projet.ReevaluationChecklist) ? "(vide)" : projet.ReevaluationChecklist.Trim());
            sbProjet.AppendLine();
            sbProjet.AppendLine("### Co-construction famille");
            sbProjet.AppendLine(string.IsNullOrWhiteSpace(projet.CoConstructionFamille) ? "(vide)" : projet.CoConstructionFamille.Trim());

            return $@"Tu es pédopsychiatre. Tu RELIS CRITIQUEMENT un Projet Thérapeutique pour signaler 6 types de problèmes potentiels :

1. ACTION SANS JUSTIFICATION — Une action n'a pas de lien_synthese_section, ou son lien pointe vers une section vide. L'action n'est pas appuyée par la synthèse.
   Type: ""action_sans_justification"" — action_id requis.

2. DIMENSION NON ADRESSÉE — Une dimension fragile / critique de la Synthèse n'a pas d'action correspondante.
   Ex : Synthèse dit ""environnement Bloqué"" mais aucune action dans Actions sur l'environnement.
   Type: ""dimension_non_adressee"" — cible = nom de la dimension non couverte.

3. CONTRADICTION — Une action contredit ce que la synthèse soutient, OU deux actions sont contradictoires.
   Type: ""contradiction"".

4. ACTION SANS INDICATEUR — Une action sans indicateur de réussite mesurable (pas de critère d'évaluation).
   Type: ""action_sans_indicateur"" — action_id requis.

5. OBJECTIF SANS ACTION — Un objectif prioritaire évoqué dans la section ""Objectifs prioritaires"" n'a pas d'action concrète qui l'adresse.
   Type: ""objectif_sans_action"".

6. SUGGESTION ÉDITORIALE — Ton, formulation, structure.
   Type: ""suggestion"".

SÉVÉRITÉ :
- ""critique"" : action sans justification claire OU dimension critique de la synthèse non adressée → bloque la validation
- ""moyenne""  : action sans indicateur, contradiction mineure
- ""mineure""  : suggestion d'amélioration

CONTRAINTES :
- Si tu ne trouves rien, retourne un tableau vide. NE FORCE PAS de signalements artificiels.
- Maximum 10 flags. Priorise les critiques.
- Sois concret : cite l'action ou la section concernée.
- Propose une correction concrète dans ""suggestion"" quand possible.

[A] SYNTHÈSE GLOBALE DE RÉFÉRENCE :
{sbSynth.ToString().TrimEnd()}

[B] PROJET THÉRAPEUTIQUE À RELIRE :
{sbProjet.ToString().TrimEnd()}

RÉPONDS UNIQUEMENT par un JSON valide :
{{
  ""flags"": [
    {{
      ""type"": ""action_sans_justification|dimension_non_adressee|contradiction|action_sans_indicateur|objectif_sans_action|suggestion"",
      ""severite"": ""critique|moyenne|mineure"",
      ""action_id"": ""..."",
      ""cible"": ""..."",
      ""detail"": ""..."",
      ""suggestion"": ""...""
    }}
  ]
}}";
        }

        private static void AppendActions(StringBuilder sb, string section, IEnumerable<ProjetAction> actions)
        {
            sb.AppendLine($"### Actions — {section}");
            var list = actions.ToList();
            if (list.Count == 0) { sb.AppendLine("(aucune)"); sb.AppendLine(); return; }
            foreach (var a in list)
            {
                sb.AppendLine($"- [id={a.Id}] {a.Libelle}");
                if (!string.IsNullOrWhiteSpace(a.Description))
                    sb.AppendLine($"  Description : {a.Description}");
                if (!string.IsNullOrWhiteSpace(a.IndicateurReussite))
                    sb.AppendLine($"  Indicateur : {a.IndicateurReussite}");
                else
                    sb.AppendLine($"  Indicateur : (manquant)");
                if (!string.IsNullOrWhiteSpace(a.LienSyntheseSection))
                    sb.AppendLine($"  Lien synthèse : {a.LienSyntheseSection}");
                else
                    sb.AppendLine($"  Lien synthèse : (manquant)");
            }
            sb.AppendLine();
        }

        private static List<ProjetRelectureFlag>? ParseJson(string raw, ProjetTherapeutique projet)
        {
            var json = ExtractJson(raw);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("flags", out var arr) || arr.ValueKind != JsonValueKind.Array)
                    return new List<ProjetRelectureFlag>();

                var flags = new List<ProjetRelectureFlag>();
                foreach (var e in arr.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    var actionId = NullIfEmpty(ReadString(e, "action_id"));
                    var f = new ProjetRelectureFlag
                    {
                        Type       = ParseType(ReadString(e, "type")),
                        Severite   = ParseSeverite(ReadString(e, "severite")),
                        ActionId   = actionId,
                        Cible      = ReadString(e, "cible"),
                        Detail     = ReadString(e, "detail"),
                        Suggestion = ReadString(e, "suggestion"),
                    };
                    // Enrichir la cible : si action_id, prendre le libellé de l'action
                    if (!string.IsNullOrWhiteSpace(actionId))
                    {
                        var action = projet.ToutesActions.FirstOrDefault(a => a.Id == actionId);
                        if (action != null) f.Cible = action.Libelle;
                    }
                    if (!string.IsNullOrWhiteSpace(f.Detail)) flags.Add(f);
                }
                return flags.Take(10).ToList();
            }
            catch { return null; }
        }

        private static ProjetFlagType ParseType(string s) => s?.ToLowerInvariant() switch
        {
            "action_sans_justification" => ProjetFlagType.ActionSansJustification,
            "dimension_non_adressee"    => ProjetFlagType.DimensionNonAdressee,
            "contradiction"             => ProjetFlagType.Contradiction,
            "action_sans_indicateur"    => ProjetFlagType.ActionSansIndicateur,
            "objectif_sans_action"      => ProjetFlagType.ObjectifSansAction,
            _                           => ProjetFlagType.Suggestion
        };

        private static ProjetFlagSeverite ParseSeverite(string s) => s?.ToLowerInvariant() switch
        {
            "critique" => ProjetFlagSeverite.Critique,
            "moyenne"  => ProjetFlagSeverite.Moyenne,
            _          => ProjetFlagSeverite.Mineure
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
