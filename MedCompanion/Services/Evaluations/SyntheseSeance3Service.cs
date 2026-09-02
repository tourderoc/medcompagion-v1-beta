using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MedCompanion.Models.Evaluations;
using MedCompanion.Services.LLM;

namespace MedCompanion.Services.Evaluations
{
    /// <summary>
    /// Synthèse de la 3ᵉ séance — deux blocs, deux fiabilités, un texte.
    ///
    /// CE QU'ELLE FAIT, ET CE QU'ELLE NE FAIT PAS. Elle PRÉSENTE et QUALIFIE ce que la séance a
    /// produit : la cartographie de l'environnement une fois ses deux moitiés réunies, et les
    /// constats de l'évaluation ciblée. Elle ne conclut pas, ne pose pas de diagnostic, et ne
    /// croise pas avec l'interrogatoire ni les bilans — ce croisement appartient à la Synthèse
    /// Globale, où toutes les sources sont réunies. Deux endroits où s'écrirait la même conclusion
    /// finiraient par diverger.
    ///
    /// DEUX BLOCS SÉPARÉS, ET DEUX FIABILITÉS. L'environnement et l'évaluation ciblée ne se
    /// valent pas et ne se pondèrent pas ensemble : le premier repose pour moitié sur une feuille
    /// remplie en salle d'attente, le second sur ce que le médecin a vu de ses yeux dans la pièce.
    /// Un seul curseur pour les deux traiterait implicitement l'un comme l'autre.
    /// </summary>
    public class SyntheseSeance3Service
    {
        // Deux appels courts plutôt qu'un long : chaque bloc est présenté séparément, puis un
        // dernier appel les met en regard. Même raison qu'ailleurs — un appel qui échoue n'emporte
        // pas tout, et le socle en tête est mis en cache par llama.cpp.
        private const int LlmTimeoutSeconds = 120;
        private const int MaxTokensBloc     = 700;
        private const int MaxTokensFinal    = 900;

        private readonly ILLMService _llm;

        public SyntheseSeance3Service(ILLMService llm) => _llm = llm;

        public class Entree
        {
            public string PatientNom { get; set; } = "";
            public int?   Age        { get; set; }

            public List<FeuilleLue> Environnement { get; set; } = new();
            public List<AxeCible>   Axes          { get; set; } = new();

            public string? FiabiliteEnv  { get; set; }
            public string? FiabiliteAxes { get; set; }

            public string? Informateur    { get; set; }
            public string? InformateurNom { get; set; }
        }

        /// <param name="onProgres">Libellé d'avancement pour le bandeau.</param>
        public async Task<(bool ok, string? texte, string? error)> RedigerAsync(
            Entree e, Action<string>? onProgres = null, CancellationToken ct = default)
        {
            var fEnv  = FiabiliteCartographie.Par(e.FiabiliteEnv);
            var fAxes = FiabiliteCartographie.Par(e.FiabiliteAxes);

            // Une source écartée n'est pas pesée à zéro : elle sort du texte, et son absence est
            // dite. C'est la même règle qu'à la séance 2.
            var envRetenu  = e.Environnement.Count > 0 && fEnv?.Poids  != null;
            var axesRetenu = e.Axes.Count          > 0 && fAxes?.Poids != null;

            if (!envRetenu && !axesRetenu)
                return (false, null,
                    "Rien à synthétiser : les deux blocs sont vides ou déclarés non exploitables.");

            var socle = BuildSocle(e, fEnv, fAxes);
            var parties = new List<string>();

            if (envRetenu)
            {
                onProgres?.Invoke("⏳ 1/3 — Cartographie de l'environnement…");
                var (ok, texte, err) = await AppelerAsync(
                    socle + BlocEnvironnement(e), MaxTokensBloc, ct);
                if (!ok) return (false, null, $"Environnement — {err}");
                parties.Add(texte!);
            }

            if (axesRetenu)
            {
                onProgres?.Invoke("⏳ 2/3 — Évaluation ciblée…");
                var (ok, texte, err) = await AppelerAsync(
                    socle + BlocAxes(e), MaxTokensBloc, ct);
                if (!ok) return (false, null, $"Évaluation ciblée — {err}");
                parties.Add(texte!);
            }

            // Un seul bloc retenu : il n'y a rien à mettre en regard, et un troisième appel ne
            // produirait qu'une paraphrase.
            if (parties.Count == 1)
                return (true, Assembler(e, fEnv, fAxes, parties), null);

            onProgres?.Invoke("⏳ 3/3 — Mise en regard…");
            var (okF, final, errF) = await AppelerAsync(
                socle + BlocMiseEnRegard(parties), MaxTokensFinal, ct);

            // La mise en regard est un plus, pas un prérequis : si elle échoue, les deux
            // présentations valent d'être gardées.
            if (okF) parties.Add(final!);
            else System.Diagnostics.Debug.WriteLine($"[SyntheseSeance3] mise en regard : {errF}");

            return (true, Assembler(e, fEnv, fAxes, parties), null);
        }

        private async Task<(bool ok, string? texte, string? err)> AppelerAsync(
            string prompt, int maxTokens, CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(LlmTimeoutSeconds));

            try
            {
                var (ok, brut, err) = await _llm.GenerateTextAsync(prompt, maxTokens, cts.Token);
                if (!ok || string.IsNullOrWhiteSpace(brut)) return (false, null, err ?? "réponse vide");
                return (true, Nettoyer(brut), null);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (OperationCanceledException) { return (false, null, $"délai dépassé ({LlmTimeoutSeconds} s)"); }
            catch (Exception ex) { return (false, null, ex.Message); }
        }

        /// <summary>
        /// Retire un éventuel bloc de réflexion et les clôtures bavardes. La synthèse est un texte
        /// libre — on ne peut pas la contraindre par un schéma — donc on nettoie après coup.
        /// </summary>
        private static string Nettoyer(string brut)
        {
            var t = System.Text.RegularExpressions.Regex.Replace(
                brut, @"<think>[\s\S]*?</think>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return t.Trim();
        }

        // ── Prompts ───────────────────────────────────────────────────────────

        private static string BuildSocle(Entree e, NiveauFiabilite? fEnv, NiveauFiabilite? fAxes)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Tu es pédopsychiatre. Tu rédiges la synthèse d'une séance d'évaluation.");
            sb.AppendLine();
            sb.AppendLine("RÈGLE PREMIÈRE — tu PRÉSENTES et tu QUALIFIES, tu ne conclus pas.");
            sb.AppendLine("Pas de diagnostic, pas de recommandation, pas de projet de soin. Ce texte n'est pas");
            sb.AppendLine("l'endroit où l'on tranche : le croisement avec l'interrogatoire et les bilans se fera");
            sb.AppendLine("plus tard, quand toutes les sources seront réunies.");
            sb.AppendLine();
            sb.AppendLine("Ton : clinique, sobre, à destination du médecin lui-même. Pas de listes à puces, pas de");
            sb.AppendLine("titres. Des paragraphes courts. Ne réécris pas les items un par un : dis ce qu'ils");
            sb.AppendLine("dessinent ensemble.");
            sb.AppendLine();
            sb.AppendLine($"Enfant : {(e.Age.HasValue ? $"{e.Age.Value} ans" : "âge non confirmé")}");
            sb.AppendLine();
            sb.AppendLine("FIABILITÉS DÉCLARÉES PAR LE MÉDECIN — elles qualifient les SOURCES, jamais les valeurs :");
            sb.AppendLine($"  · Cartographie de l'environnement : {Dire(fEnv)}");
            sb.AppendLine($"  · Évaluation ciblée : {Dire(fAxes)}");
            sb.AppendLine("Une source peu fiable ne doit pas fonder une affirmation à elle seule ; module la");
            sb.AppendLine("prudence de tes formulations en conséquence, sans jamais corriger un chiffre.");
            sb.AppendLine();

            return sb.ToString();

            static string Dire(NiveauFiabilite? f)
                => f == null ? "non renseignée" : $"{f.Label} — {f.Detail}";
        }

        private static string BlocEnvironnement(Entree e)
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══ BLOC 1 — CARTOGRAPHIE DE L'ENVIRONNEMENT ═══");
            sb.AppendLine();

            var qui = e.Informateur switch
            {
                "mere"  => "la mère", "pere" => "le père", "autre" => "un autre adulte", _ => null
            };
            sb.AppendLine(qui == null
                ? "Feuille parents : informateur non renseigné."
                : $"Feuille parents remplie par {qui}{(string.IsNullOrWhiteSpace(e.InformateurNom) ? "" : $" ({e.InformateurNom})")}.");
            sb.AppendLine("Les items marqués « entretien » ont été cotés par le médecin, ceux marqués");
            sb.AppendLine("« feuille parents » viennent de la feuille remplie en salle d'attente.");
            sb.AppendLine();
            sb.AppendLine(LectureEnvironnementV2.PourPrompt(e.Environnement));
            sb.AppendLine();
            sb.AppendLine("Une nervure NON LISIBLE n'a pas assez de réponses pour être lue. Ne l'interprète pas,");
            sb.AppendLine("et ne comble pas le manque : dis qu'elle reste ouverte. Un « ? » n'est jamais un « non ».");
            sb.AppendLine();
            sb.AppendLine("Écris 2 à 3 paragraphes présentant ce que dessine cet environnement — ce qui tient, ce");
            sb.AppendLine("qui est fragile, et ce qui n'a pas pu être lu. Rien d'autre.");

            return sb.ToString();
        }

        private static string BlocAxes(Entree e)
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══ BLOC 2 — ÉVALUATION CIBLÉE ═══");
            sb.AppendLine();
            sb.AppendLine("Constats observés pendant la séance, axe par axe. Chaque axe dit ce qu'il venait");
            sb.AppendLine("trancher. Une case vide signifie NON OBSERVÉ — jamais « non ».");
            sb.AppendLine();

            foreach (var axe in e.Axes)
            {
                sb.AppendLine($"■ {axe.Intitule} — sert à trancher : {axe.Rattachement}");
                foreach (var p in axe.Propositions)
                {
                    var marque = p.Reponse switch
                    {
                        ReponseProposition.Oui => "oui",
                        ReponseProposition.Non => "NON",
                        _                      => "?"
                    };
                    sb.AppendLine($"   [{marque}] {p.Texte}");
                }
                if (axe.HasRemarques)
                    sb.AppendLine($"   Remarques du médecin : {axe.Remarques.Replace("\n", " / ")}");
                sb.AppendLine();
            }

            sb.AppendLine("Les remarques du médecin sont ce qu'il a VU : elles pèsent plus qu'une case cochée,");
            sb.AppendLine("et tu les reprends au plus près de ses mots.");
            sb.AppendLine();
            sb.AppendLine("Écris 2 à 3 paragraphes disant ce que la séance a permis d'observer sur chaque axe, et");
            sb.AppendLine("ce qu'elle a laissé ouvert. Ne conclus sur aucune hypothèse.");

            return sb.ToString();
        }

        private static string BlocMiseEnRegard(List<string> parties)
        {
            var sb = new StringBuilder();
            sb.AppendLine("═══ TRAVAIL DEMANDÉ — MISE EN REGARD ═══");
            sb.AppendLine();
            sb.AppendLine("Voici les deux présentations déjà écrites :");
            sb.AppendLine();
            sb.AppendLine("--- Environnement ---");
            sb.AppendLine(parties[0]);
            sb.AppendLine();
            sb.AppendLine("--- Évaluation ciblée ---");
            sb.AppendLine(parties[1]);
            sb.AppendLine();
            sb.AppendLine("Écris UN paragraphe, court, qui met ces deux lectures en regard : ce qui converge, ce");
            sb.AppendLine("qui se contredit, ce qui reste sans réponse. Ne les résume pas — elles sont déjà là.");
            sb.AppendLine("Ne conclus toujours pas : nomme les questions que la suite devra trancher.");

            return sb.ToString();
        }

        /// <summary>
        /// Assemble le texte final. Les fiabilités sont écrites EN TÊTE, pas en note de bas de page :
        /// elles conditionnent la lecture de tout ce qui suit, et quelqu'un qui rouvre la fiche dans
        /// six mois doit les voir avant les affirmations, pas après.
        /// </summary>
        private static string Assembler(Entree e, NiveauFiabilite? fEnv, NiveauFiabilite? fAxes,
                                        List<string> parties)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"_Environnement : {Etat(fEnv, e.Environnement.Count > 0)}. "
                        + $"Évaluation ciblée : {Etat(fAxes, e.Axes.Count > 0)}._");
            sb.AppendLine();

            var titres = new List<string>();
            if (e.Environnement.Count > 0 && fEnv?.Poids != null) titres.Add("### Cartographie de l'environnement");
            if (e.Axes.Count > 0 && fAxes?.Poids != null)         titres.Add("### Évaluation ciblée");
            if (parties.Count > titres.Count)                     titres.Add("### Mise en regard");

            for (int i = 0; i < parties.Count; i++)
            {
                if (i < titres.Count) { sb.AppendLine(titres[i]); sb.AppendLine(); }
                sb.AppendLine(parties[i].Trim());
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();

            static string Etat(NiveauFiabilite? f, bool present)
            {
                if (!present) return "absente";
                if (f == null) return "fiabilité non renseignée";
                return f.Poids == null ? $"{f.Label} — écartée de cette synthèse" : f.Label.ToLowerInvariant();
            }
        }
    }
}
