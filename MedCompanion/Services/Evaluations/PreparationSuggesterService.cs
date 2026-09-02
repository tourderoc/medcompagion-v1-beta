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
    /// Génère une proposition d'ORIENTATION DIAGNOSTIQUE — cinq rubriques, une par appel LLM,
    /// dans l'ordre du raisonnement clinique.
    ///
    /// POURQUOI SÉQUENTIEL, ET PAS UN SEUL APPEL — trois raisons, dont deux sont cliniques :
    ///
    /// 1. Un différentiel n'existe que RELATIVEMENT à une hypothèse. Demander les cinq rubriques
    ///    d'un coup, c'est demander au modèle de poser ses différentiels en même temps que les
    ///    hypothèses qu'ils sont censés discuter. En les enchaînant, chaque rubrique voit ce qui a
    ///    déjà été posé — y compris ce que le MÉDECIN avait écrit lui-même avant de cliquer.
    ///
    /// 2. Une rubrique manquée n'emporte plus les autres. Auparavant, un JSON tronqué au troisième
    ///    accolade perdait les cinq listes d'un coup.
    ///
    /// 3. Chaque appel devient court, donc le délai cesse d'être une loterie : le budget de sortie
    ///    est de trois items brefs, pas de quinze.
    ///
    /// Le coût attendu — cinq passes de prompt au lieu d'une — n'est pas payé : le dossier est placé
    /// en TÊTE de chaque prompt, identique d'un appel à l'autre, et llama.cpp réutilise le cache de
    /// préfixe. Seule la consigne finale change. C'est la raison de l'ordre des blocs dans
    /// <see cref="BuildPrompt"/>, à ne pas inverser.
    /// </summary>
    public class PreparationSuggesterService
    {
        // Par appel, et non plus pour l'ensemble. Une rubrique, c'est au plus trois phrases
        // courtes : 90 s couvrent largement, y compris la longue passe de prompt du premier appel,
        // qui est le seul à la payer en entier.
        private const int LlmTimeoutSeconds = 90;

        // Budget de sortie d'UNE rubrique. Sous contrainte de schéma il n'y a pas de bloc de
        // réflexion à absorber : trois items de 3-8 mots tiennent très largement dedans.
        private const int MaxTokens = 300;

        // Cap uniforme à 3 par rubrique.
        //
        // Ce n'est pas une préférence esthétique : la préparation ne pose pas un diagnostic, elle
        // ORIENTE ce que le médecin ira observer dans la séance. Neuf hypothèses ne s'observent
        // pas en une consultation — au-delà de trois par rubrique, la liste cesse de cibler et
        // redevient un inventaire, que le médecin devra trier lui-même au mauvais moment.
        public const int MaxParRubrique = 3;

        private readonly ILLMService _llm;

        public PreparationSuggesterService(ILLMService llm)
        {
            _llm = llm;
        }

        // ── Clés de rubriques ─────────────────────────────────────────────────
        // Mêmes clés côté service et côté ViewModel : elles servent d'adresse au callback qui
        // remplit l'écran au fil de l'eau.

        public static class Cles
        {
            public const string Hypotheses    = "hypotheses_principales";
            public const string Differentiels = "differentiels";
            public const string AEliminer     = "a_eliminer";
            public const string Vigilance     = "points_vigilance";
            public const string Questions     = "questions_cliniques";
        }

        public class PreparationSuggestion
        {
            public List<string> HypothesesPrincipales { get; set; } = new();
            public List<string> Differentiels         { get; set; } = new();
            public List<string> AEliminer             { get; set; } = new();
            public List<string> PointsVigilance       { get; set; } = new();
            public List<string> QuestionsCliniques    { get; set; } = new();

            /// <summary>Rubriques dont l'appel a échoué. Le reste est utilisable tel quel.</summary>
            public List<string> Echecs { get; set; } = new();

            public int Total => HypothesesPrincipales.Count + Differentiels.Count + AEliminer.Count
                              + PointsVigilance.Count + QuestionsCliniques.Count;
        }

        /// <summary>Une rubrique : son titre à l'écran, et ce qu'on demande précisément au modèle.</summary>
        private sealed record Rubrique(string Cle, string Titre, string Consigne);

        /// <summary>
        /// L'ordre est celui du raisonnement, pas celui de l'affichage : chaque rubrique est
        /// nourrie par les précédentes. Les questions cliniques viennent en dernier parce qu'elles
        /// portent sur ce que tout le reste a laissé ouvert.
        /// </summary>
        private static readonly Rubrique[] Sequence =
        {
            new(Cles.Hypotheses, "Hypothèses principales",
                "Vers quoi les données penchent aujourd'hui. Chaque hypothèse doit s'appuyer sur un "
              + "élément précis et identifiable du dossier ci-dessus."),

            new(Cles.Differentiels, "Diagnostics différentiels",
                "Ce qui pourrait expliquer LE MÊME tableau que les hypothèses déjà posées. Ne "
              + "propose rien qui ne discute pas réellement une de ces hypothèses."),

            new(Cles.AEliminer, "À éliminer prudemment",
                "Ce qu'il serait COÛTEUX de manquer chez cet enfant — gravité, urgence, "
              + "irréversibilité. Pas une liste de sécurité : uniquement ce que le dossier rend "
              + "plausible."),

            new(Cles.Vigilance, "Points de vigilance",
                "Ce qui mérite l'œil sans constituer une hypothèse diagnostique : signal isolé, "
              + "élément de contexte, discordance entre deux sources du dossier."),

            new(Cles.Questions, "Questions cliniques",
                "Ce que CETTE séance doit permettre de trancher. Formule des questions auxquelles "
              + "une observation directe de l'enfant peut répondre — pas des questions d'examen "
              + "complémentaire."),
        };

        /// <param name="synthesesBilans">
        /// Synthèses des bilans du dossier — leur SYNTHÈSE seulement, jamais le rapport entier :
        /// c'est ce que le médecin lit lui-même quand il prépare.
        /// </param>
        /// <param name="cartographie">
        /// Cartographie de l'enfant (2ᵉ séance), avec les fiabilités déclarées. Source la plus
        /// fraîche et la plus structurée du dossier — mais dont la portée dépend de ce que le
        /// médecin a jugé de ses deux moitiés.
        /// </param>
        /// <param name="dejaPosees">
        /// Ce qui figure déjà à l'écran, par clé de rubrique. Sert deux fois : une rubrique déjà
        /// renseignée n'est PAS régénérée (on ne dépense pas un appel pour un résultat qui sera
        /// jeté), et son contenu nourrit les rubriques suivantes — le travail du médecin oriente
        /// la proposition, au lieu d'être ignoré puis écrasé.
        /// </param>
        /// <param name="onRubrique">
        /// Appelé dès qu'une rubrique est prête, avec sa clé et ses items. C'est ce qui fait
        /// apparaître les blocs l'un après l'autre au lieu d'un écran figé pendant une minute.
        /// </param>
        /// <param name="onProgres">Libellé d'avancement pour le bandeau de statut.</param>
        public async Task<(bool ok, PreparationSuggestion? suggestion, string? error)> SuggestAsync(
            string patientName,
            int?   age,
            string motif,
            string synthese,
            string observationsRecentes,
            string synthesesBilans = "",
            string cartographie    = "",
            IReadOnlyDictionary<string, List<string>>? dejaPosees = null,
            Action<string, List<string>>? onRubrique = null,
            Action<string>? onProgres = null,
            CancellationToken ct = default)
        {
            var dossier = BuildDossier(patientName, age, motif, synthese, observationsRecentes,
                                       synthesesBilans, cartographie);

            var res = new PreparationSuggestion();

            // Tout ce qui est posé — par le médecin ou par les appels précédents — et que la
            // rubrique suivante doit voir.
            var acquis = new List<(string titre, List<string> items)>();

            foreach (var kv in dejaPosees ?? new Dictionary<string, List<string>>())
            {
                var r = Sequence.FirstOrDefault(x => x.Cle == kv.Key);
                if (r != null && kv.Value.Count > 0) acquis.Add((r.Titre, kv.Value));
            }

            var rang = 0;
            foreach (var rubrique in Sequence)
            {
                rang++;
                ct.ThrowIfCancellationRequested();

                // Rubrique déjà remplie par le médecin : on passe, sans dépenser d'appel.
                if (dejaPosees != null && dejaPosees.TryGetValue(rubrique.Cle, out var existant)
                    && existant.Count > 0)
                    continue;

                onProgres?.Invoke($"⏳ {rang}/{Sequence.Length} — {rubrique.Titre}…");

                var (ok, items, err) = await DemanderRubriqueAsync(dossier, rubrique, acquis, ct);

                if (!ok)
                {
                    // Une rubrique manquée n'emporte plus les autres : on note et on continue.
                    // Sauf annulation explicite du médecin, qui, elle, doit tout arrêter.
                    if (ct.IsCancellationRequested) throw new OperationCanceledException(ct);
                    res.Echecs.Add(rubrique.Titre);
                    System.Diagnostics.Debug.WriteLine($"[Orientation] {rubrique.Cle} : {err}");
                    continue;
                }

                Affecter(res, rubrique.Cle, items);
                if (items.Count > 0) acquis.Add((rubrique.Titre, items));
                onRubrique?.Invoke(rubrique.Cle, items);
            }

            // Échec de bout en bout : c'est le moteur qui ne répond pas, pas la clinique qui est
            // pauvre. Le distinguer d'un dossier qui ne porte réellement rien.
            if (res.Echecs.Count == Sequence.Length)
                return (false, null, "Aucune rubrique n'a abouti — le moteur ne répond pas. "
                                   + "Voir « Affectation par étape » dans le moteur local.");

            return (true, res, null);
        }

        // ── Un appel = une rubrique ───────────────────────────────────────────

        private async Task<(bool ok, List<string> items, string? err)> DemanderRubriqueAsync(
            string dossier, Rubrique rubrique,
            List<(string titre, List<string> items)> acquis,
            CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(LlmTimeoutSeconds));

            try
            {
                var prompt = BuildPrompt(dossier, rubrique, acquis);

                // Chemin contraint quand le moteur le permet. Ce n'est pas un raffinement : sur un
                // modèle à réflexion (Qwen), une demande de JSON en prose part dans un long bloc de
                // raisonnement que le serveur range dans `reasoning_content` ; `content` revient
                // vide ou tronqué, et l'étape échoue — au parsing ou au délai. Le décodage contraint
                // coupe la réflexion et rend le JSON valide par construction. Le cap de 3 devient
                // alors structurel : la grammaire refuse un quatrième item.
                var (ok, raw, err) = _llm is IStructuredOutputService s && s.SupportsStructuredOutput
                    ? await s.GenerateJsonAsync(prompt, "rubrique_orientation", SchemaRubrique, MaxTokens, cts.Token)
                    : await _llm.GenerateTextAsync(prompt, maxTokens: MaxTokens, cancellationToken: cts.Token);

                if (!ok || string.IsNullOrWhiteSpace(raw))
                    return (false, new List<string>(), err ?? "réponse vide");

                var items = ParseItems(raw);
                if (items == null) return (false, new List<string>(), "JSON illisible");

                return (true, Trim(items), null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;   // annulation du médecin : remonte
            }
            catch (OperationCanceledException)
            {
                return (false, new List<string>(), $"délai dépassé ({LlmTimeoutSeconds} s)");
            }
            catch (Exception ex)
            {
                return (false, new List<string>(), ex.Message);
            }
        }

        private static void Affecter(PreparationSuggestion res, string cle, List<string> items)
        {
            switch (cle)
            {
                case Cles.Hypotheses:    res.HypothesesPrincipales = items; break;
                case Cles.Differentiels: res.Differentiels         = items; break;
                case Cles.AEliminer:     res.AEliminer             = items; break;
                case Cles.Vigilance:     res.PointsVigilance       = items; break;
                case Cles.Questions:     res.QuestionsCliniques    = items; break;
            }
        }

        private static List<string> Trim(List<string> list)
        {
            var clean = new List<string>();
            foreach (var s in list)
            {
                var t = (s ?? "").Trim();
                if (string.IsNullOrEmpty(t)) continue;
                if (clean.Count >= MaxParRubrique) break;
                clean.Add(t);
            }
            return clean;
        }

        // ── Prompts ───────────────────────────────────────────────────────────

        /// <summary>
        /// Le dossier, construit UNE fois et réutilisé mot pour mot dans les cinq prompts. C'est ce
        /// qui rend les appels 2 à 5 quasi gratuits en passe de prompt (cache de préfixe llama.cpp).
        /// </summary>
        private static string BuildDossier(string patientName, int? age, string motif, string synthese,
                                           string observationsRecentes, string synthesesBilans,
                                           string cartographie)
        {
            var ageInfo = age.HasValue ? $"{age.Value} ans" : "âge non confirmé";
            var sb = new StringBuilder();

            sb.AppendLine("Tu es pédopsychiatre. Tu prépares une ORIENTATION DIAGNOSTIQUE — pas un diagnostic.");
            sb.AppendLine();
            sb.AppendLine("Son unique but : donner au médecin des éléments PRÉCIS À OBSERVER pendant la séance qui");
            sb.AppendLine("vient. Ce n'est pas une conclusion, c'est une mise au point de l'attention. Le dossier est");
            sb.AppendLine("incomplet à ce stade, et c'est assumé : la synthèse, plus tard, aura à répondre de la");
            sb.AppendLine("complétude. Ici on cible. Le médecin validera, modifiera ou supprimera chaque proposition.");
            sb.AppendLine();
            sb.AppendLine("═══ DOSSIER ═══");
            sb.AppendLine();
            sb.AppendLine($"Patient : {patientName}, {ageInfo}");
            sb.AppendLine();
            sb.AppendLine("Motif de consultation :");
            sb.AppendLine(Ou(motif, "(non renseigné)"));
            sb.AppendLine();
            sb.AppendLine("Synthèse globale du patient :");
            sb.AppendLine(Ou(synthese, "(aucune synthèse disponible)"));
            sb.AppendLine();
            sb.AppendLine("Observations récentes (dernière consultation au moins) :");
            sb.AppendLine(Ou(observationsRecentes, "(aucune observation récente disponible)"));
            sb.AppendLine();
            sb.AppendLine("Synthèses des bilans du dossier :");
            sb.AppendLine(Ou(synthesesBilans, "(aucun bilan au dossier)"));
            sb.AppendLine();
            sb.AppendLine("Cartographie de l'enfant :");
            sb.AppendLine(Ou(cartographie, "(cartographie non réalisée)"));
            sb.AppendLine();
            sb.AppendLine("IMPORTANT — la cartographie porte des FIABILITÉS déclarées par le médecin pour chacune de");
            sb.AppendLine("ses deux moitiés. Une moitié jugée peu fiable ne doit pas fonder une hypothèse à elle");
            sb.AppendLine("seule ; une moitié déclarée non exploitable ne doit pas être utilisée du tout.");

            return sb.ToString();

            static string Ou(string v, string defaut) => string.IsNullOrWhiteSpace(v) ? defaut : v.Trim();
        }

        /// <summary>
        /// Dossier d'abord (identique à chaque appel, donc mis en cache par le serveur), puis ce qui
        /// est déjà posé, puis la consigne de LA rubrique demandée. Inverser cet ordre ferait payer
        /// la passe de prompt complète cinq fois.
        /// </summary>
        private static string BuildPrompt(string dossier, Rubrique rubrique,
                                          List<(string titre, List<string> items)> acquis)
        {
            var sb = new StringBuilder(dossier);
            sb.AppendLine();

            if (acquis.Count > 0)
            {
                sb.AppendLine("═══ DÉJÀ POSÉ ═══");
                sb.AppendLine();
                sb.AppendLine("Ces éléments sont acquis — certains ont été écrits par le médecin lui-même.");
                sb.AppendLine("Appuie-toi dessus, et ne les répète pas.");
                sb.AppendLine();
                foreach (var (titre, items) in acquis)
                {
                    sb.AppendLine($"{titre} :");
                    foreach (var i in items) sb.AppendLine($"  - {i}");
                }
                sb.AppendLine();
            }

            sb.AppendLine($"═══ RUBRIQUE DEMANDÉE : {rubrique.Titre.ToUpperInvariant()} ═══");
            sb.AppendLine();
            sb.AppendLine(rubrique.Consigne);
            sb.AppendLine();
            sb.AppendLine("RÈGLES :");
            sb.AppendLine($"- Au maximum {MaxParRubrique} items. Moins vaut mieux que du remplissage.");
            sb.AppendLine("- Chaque item est COURT (3-8 mots) et CIBLÉ pour CET enfant — jamais générique.");
            sb.AppendLine("- Rien qui ne s'appuie sur une donnée présente ci-dessus. N'invente pas.");
            sb.AppendLine("- Si cette rubrique ne peut pas être renseignée honnêtement, renvoie une liste VIDE.");
            sb.AppendLine("  Une rubrique vide est un résultat ; une rubrique remplie pour la remplir est du bruit.");
            sb.AppendLine("- Ne réponds QUE pour cette rubrique. Ne conclus pas, ne recommande aucun bilan.");
            sb.AppendLine();
            sb.AppendLine("Réponds uniquement par : {\"items\": [\"...\"]}");

            return sb.ToString();
        }

        /// <summary>
        /// Schéma d'UNE rubrique. Le cap est dans la grammaire : llama-server compile
        /// <c>maxItems</c> en GBNF, si bien que le modèle ne PEUT pas produire un quatrième item —
        /// la contrainte cesse d'être une consigne qu'il faut espérer voir respectée.
        /// </summary>
        private const string SchemaRubrique = """
        {
          "type": "object",
          "properties": {
            "items": { "type": "array", "maxItems": 3, "items": { "type": "string" } }
          },
          "required": ["items"],
          "additionalProperties": false
        }
        """;

        // ── Parsing ───────────────────────────────────────────────────────────

        private static List<string>? ParseItems(string raw)
        {
            var extracted = ExtractJson(raw);
            if (string.IsNullOrEmpty(extracted)) return null;

            try
            {
                using var doc = JsonDocument.Parse(extracted);
                if (!doc.RootElement.TryGetProperty("items", out var arr)
                    || arr.ValueKind != JsonValueKind.Array)
                    return null;

                var res = new List<string>();
                foreach (var e in arr.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.String)
                        res.Add(e.GetString() ?? "");
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
