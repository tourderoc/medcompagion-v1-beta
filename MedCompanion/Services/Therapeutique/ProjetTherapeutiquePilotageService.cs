using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MedCompanion.Models.Therapeutique;
using MedCompanion.Services.LLM;

namespace MedCompanion.Services.Therapeutique
{
    /// <summary>
    /// V1.3 — Pilotage de Med sur les statuts des actions du Projet Thérapeutique.
    ///
    /// À partir du projet courant + notes récentes, Med propose des transitions de
    /// statut (⚪→🟡, 🟡→✅, etc.) en justifiant chaque suggestion par ce qu'il a vu
    /// dans le dossier. Le psy accepte ou rejette chaque transition.
    ///
    /// Ne touche PAS au contenu des actions (libellé, description). Pour ça, utiliser
    /// le mode patch (V1.2).
    /// </summary>
    public class ProjetTherapeutiquePilotageService
    {
        private const int LlmTimeoutSeconds = 60;
        private const int MaxTokens         = 2500;

        private readonly ILLMService _llm;
        private readonly PatientContextService _patientContext;

        public ProjetTherapeutiquePilotageService(
            ILLMService llm,
            PatientContextService patientContext)
        {
            _llm            = llm;
            _patientContext = patientContext;
        }

        public class TransitionRaw
        {
            public string ActionId       = "";
            public string StatutPropose  = "";
            public string Justification  = "";
            public string Source         = "";
        }

        public async Task<(bool ok, List<TransitionSuggestion>? suggestions, string? error)> SuggerTransitionsAsync(
            ProjetTherapeutique projet,
            CancellationToken ct = default)
        {
            if (projet == null) return (false, null, "Projet nul.");

            var allActions = projet.ToutesActions.ToList();
            if (allActions.Count == 0)
                return (true, new List<TransitionSuggestion>(), null);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(LlmTimeoutSeconds));

            try
            {
                var bundle = _patientContext.GetCompleteContext(projet.PatientNomComplet);
                var clinicalContent = bundle?.ClinicalContext ?? "";

                if (string.IsNullOrWhiteSpace(clinicalContent))
                    return (true, new List<TransitionSuggestion>(), null);   // pas de matière nouvelle

                var prompt = BuildPrompt(projet, allActions, clinicalContent);
                var (ok, raw, err) = await _llm.GenerateTextAsync(prompt, maxTokens: MaxTokens, cancellationToken: cts.Token);
                if (!ok || string.IsNullOrWhiteSpace(raw))
                    return (false, null, err ?? "Réponse LLM vide.");

                var rawList = ParseJson(raw);
                if (rawList == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[ProjetPilotage] Parse failed. Raw : {raw}");
                    return (false, null, "Parsing JSON impossible (voir Sortie Debug).");
                }

                // Convertir en suggestions enrichies (libellé + statut actuel depuis le projet)
                var suggestions = new List<TransitionSuggestion>();
                foreach (var r in rawList)
                {
                    var action = allActions.FirstOrDefault(a => a.Id == r.ActionId);
                    if (action == null) continue;
                    var statutProp = MapStatut(r.StatutPropose);
                    if (statutProp == action.Statut) continue;   // pas de transition réelle
                    suggestions.Add(new TransitionSuggestion
                    {
                        ActionId       = action.Id,
                        ActionLibelle  = action.Libelle,
                        StatutActuel   = action.Statut,
                        StatutPropose  = statutProp,
                        Justification  = r.Justification,
                        Source         = r.Source,
                    });
                }
                return (true, suggestions, null);
            }
            catch (OperationCanceledException) { return (false, null, "Délai LLM dépassé."); }
            catch (Exception ex)                { return (false, null, ex.Message); }
        }

        private static ActionStatut MapStatut(string s) => s?.ToLowerInvariant() switch
        {
            "a_venir"   => ActionStatut.AVenir,
            "avenir"    => ActionStatut.AVenir,
            "en_cours"  => ActionStatut.EnCours,
            "encours"   => ActionStatut.EnCours,
            "fait"      => ActionStatut.Fait,
            "abandonne" => ActionStatut.Abandonne,
            "abandon"   => ActionStatut.Abandonne,
            _           => ActionStatut.AVenir
        };

        private static string BuildPrompt(
            ProjetTherapeutique projet,
            List<ProjetAction> actions,
            string clinicalContent)
        {
            var sbActions = new StringBuilder();
            foreach (var a in actions)
            {
                sbActions.AppendLine($"- [id={a.Id}] [statut_actuel={StatutKey(a.Statut)}] {a.Libelle}");
                if (!string.IsNullOrWhiteSpace(a.Description))
                    sbActions.AppendLine($"  Description : {a.Description}");
                if (!string.IsNullOrWhiteSpace(a.IndicateurReussite))
                    sbActions.AppendLine($"  Indicateur : {a.IndicateurReussite}");
                sbActions.AppendLine($"  Dernière mise à jour de statut : {a.DateDernierStatut:yyyy-MM-dd}");
            }

            return $@"Tu es pédopsychiatre. Tu PILOTES le suivi d'un Projet Thérapeutique à partir des notes récentes du dossier.

OBJECTIF : pour chaque action du projet, dire si tu vois dans les notes récentes une preuve que son statut devrait évoluer.

STATUTS POSSIBLES :
- ""a_venir""   : décidée, pas encore démarrée
- ""en_cours""  : action démarrée / en train de se faire
- ""fait""      : action accomplie (examen passé, RDV honoré, objectif atteint)
- ""abandonne"" : décision d'arrêt (intolérance, refus famille, devenu non pertinent)

CONTRAINTES STRICTES :
- Tu ne signales QUE les actions où tu as une PREUVE EXPLICITE dans les notes/sources.
- Si rien n'a bougé pour une action, NE LA MENTIONNE PAS (liste vide acceptée).
- Cite la source (date de la note, type de document).
- Justification courte (1-2 phrases) : ce que tu as vu.
- N'invente pas. Si tu n'es pas sûr, ne propose pas.
- Pas de suggestion de modification du libellé / description : seulement le statut.

PROJET COURANT (v{projet.Version}) — actions :
{sbActions.ToString().TrimEnd()}

SOURCES (notes / synthèse hors-ce-dossier) :
{clinicalContent.Trim()}

RÉPONDS UNIQUEMENT par un JSON valide :
{{
  ""transitions"": [
    {{
      ""action_id"": ""..."",
      ""statut_propose"": ""a_venir|en_cours|fait|abandonne"",
      ""justification"": ""..."",
      ""source"": ""...""
    }}
  ]
}}";
        }

        private static string StatutKey(ActionStatut s) => s switch
        {
            ActionStatut.AVenir    => "a_venir",
            ActionStatut.EnCours   => "en_cours",
            ActionStatut.Fait      => "fait",
            ActionStatut.Abandonne => "abandonne",
            _                      => "a_venir"
        };

        private static List<TransitionRaw>? ParseJson(string raw)
        {
            var json = ExtractJson(raw);
            if (string.IsNullOrEmpty(json)) return null;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (!root.TryGetProperty("transitions", out var arr) || arr.ValueKind != JsonValueKind.Array)
                    return new List<TransitionRaw>();
                var list = new List<TransitionRaw>();
                foreach (var e in arr.EnumerateArray())
                {
                    if (e.ValueKind != JsonValueKind.Object) continue;
                    var t = new TransitionRaw
                    {
                        ActionId      = ReadString(e, "action_id"),
                        StatutPropose = ReadString(e, "statut_propose"),
                        Justification = ReadString(e, "justification"),
                        Source        = ReadString(e, "source"),
                    };
                    if (!string.IsNullOrWhiteSpace(t.ActionId) && !string.IsNullOrWhiteSpace(t.StatutPropose))
                        list.Add(t);
                }
                return list;
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
