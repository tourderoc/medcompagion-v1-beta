using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MedCompanion.Models.Evaluations;

namespace MedCompanion.Services.Evaluations
{
    /// <summary>
    /// Restitue, sous forme lisible par un modèle, ce que les deux séances d'évaluation ont produit :
    /// la Cartographie de l'enfant (séance 2) et l'Environnement &amp; évaluation ciblée (séance 3).
    ///
    /// POURQUOI CE SERVICE EXISTE — les moteurs d'aval (Synthèse Globale, Projet thérapeutique,
    /// Restitution) ne lisaient que les évaluations V1. Un enfant évalué par les deux nouveaux blocs
    /// obtenait donc une Synthèse Globale qui ignorait ses deux cartographies : le travail était
    /// dans le dossier, mais invisible de la chaîne qui en dépend. Ce n'était pas une dette de
    /// refonte, c'était un trou.
    ///
    /// CE QU'IL DONNE, ET CE QU'IL TAIT — les SYNTHÈSES d'abord, avec leurs fiabilités déclarées,
    /// puis un résumé structuré par axe et par nervure. Jamais les 156 items un par un : le prompt
    /// d'aval porte déjà tout le dossier clinique, et noyer une conclusion sous ses justificatifs
    /// la rend moins lisible, pas plus fondée. C'est le même parti que pour la V1, dont seul le
    /// Bilan Final était transmis.
    /// </summary>
    public class EvaluationV2ContextService
    {
        private readonly CartographieV2Service      _carto = new();
        private readonly SeanceEnvironnementService _env   = new();

        /// <summary>
        /// Le bloc à insérer dans un prompt, ou une chaîne vide si les deux séances n'ont rien
        /// produit. Vide et non « (aucune) » : c'est à l'appelant de décider comment nommer
        /// l'absence dans SON prompt.
        /// </summary>
        /// <param name="integral">
        /// Lecture INTÉGRALE : ajoute, sous chaque conclusion, les réponses item par item —
        /// les six dimensions de chaque axe parent, et les items de chaque nervure
        /// d'environnement avec leur source.
        ///
        /// Réservé aux blocs de SYNTHÈSE. Ailleurs, la conclusion suffit et le détail dilue.
        /// Mais la synthèse est précisément l'endroit où l'on croise : un 4/6 en Attachement ne
        /// dit pas la même chose selon que l'enfant achoppe sur la séparation ou sur le recours
        /// — c'est ce que la structure des six dimensions existe pour produire, et le score seul
        /// le jette. Un clinicien qui rédige sa synthèse relit ses observations, pas seulement
        /// ses conclusions.
        /// </param>
        public string PourPrompt(string patientDirectoryPath, bool integral = false)
        {
            if (string.IsNullOrWhiteSpace(patientDirectoryPath)) return "";

            var sb = new StringBuilder();

            EcrireSeance2(sb, patientDirectoryPath, integral);
            EcrireSeance3(sb, patientDirectoryPath, integral);

            return sb.ToString().TrimEnd();
        }

        /// <summary>Y a-t-il quelque chose à lire dans les deux séances ?</summary>
        public bool ADuContenu(string patientDirectoryPath)
            => !string.IsNullOrWhiteSpace(PourPrompt(patientDirectoryPath));

        // ── Séance 2 — Cartographie de l'enfant ───────────────────────────────

        private void EcrireSeance2(StringBuilder sb, string dir, bool integral = false)
        {
            List<CartographieV2> fiches;
            try { fiches = _carto.LoadAll(dir).Where(c => c.VerseeAuDossier).ToList(); }
            catch { return; }

            foreach (var c in fiches.OrderBy(c => c.Date))
            {
                sb.AppendLine($"■ CARTOGRAPHIE DE L'ENFANT — séance du {c.Date:dd/MM/yyyy}"
                            + (c.Age.HasValue ? $" ({c.Age} ans)" : ""));

                // La fiabilité d'abord : elle conditionne la lecture de tout ce qui suit.
                sb.AppendLine($"  Fiabilité — questionnaire parent : {FiabiliteCartographie.LabelDe(c.FiabiliteQuestionnaire)}"
                            + $" · axes observés : {FiabiliteCartographie.LabelDe(c.FiabiliteObservation)}");

                var qui = c.Informateur switch
                {
                    "mere" => "la mère", "pere" => "le père", "autre" => "un autre adulte", _ => null
                };
                if (qui != null)
                    sb.AppendLine($"  Questionnaire rempli par {qui}"
                                + (string.IsNullOrWhiteSpace(c.InformateurNom) ? "" : $" ({c.InformateurNom})"));

                if (c.ScoresQuestionnaire.Count > 0)
                {
                    sb.AppendLine("  Questionnaire parent (score sur 6, plus haut = plus favorable) :");
                    foreach (var kv in c.ScoresQuestionnaire)
                    {
                        sb.AppendLine($"    - {CartographieItemsV2.AxeLabel(kv.Key)} : {kv.Value}/6");

                        // Lecture intégrale : QUELLES dimensions accrochent, et non le seul score.
                        if (!integral) continue;
                        if (!c.ReponsesQuestionnaire.TryGetValue(kv.Key, out var reps) || reps.Length == 0) continue;
                        var dims = CartographieItemsV2.Dimensions(kv.Key);
                        for (int i = 0; i < reps.Length && i < dims.Count; i++)
                        {
                            var marque = reps[i] switch { "oui" => "oui", "non" => "NON", _ => "non renseigné" };
                            sb.AppendLine($"        [{marque}] {dims[i]}");
                        }
                    }
                    if (integral)
                        sb.AppendLine("    (« non renseigné » = case laissée vide par le parent — jamais un « non »)");
                }

                if (c.Axes.Count > 0)
                {
                    sb.AppendLine("  Axes observés par le médecin (sur 5) :");
                    foreach (var profil in ProfilsObservesV2.Profils)
                    {
                        foreach (var ax in profil.Axes)
                        {
                            if (!c.Axes.TryGetValue($"{profil.Key}.{ax.Key}", out var v) || v <= 0) continue;
                            // En intégral, on nomme le profil et les deux pôles : « 2/5 » n'a de
                            // sens que rapporté à ce que 1 et 5 veulent dire sur CET axe — et le
                            // Tempérament est un portrait, où aucun pôle n'est meilleur.
                            sb.AppendLine(integral
                                ? $"    - {profil.Label} · {ax.Label} : {v}/5  (1 = {ax.Pole1} ; 5 = {ax.Pole5})"
                                : $"    - {profil.Key}.{ax.Key} : {v}/5");
                        }
                    }
                    if (integral)
                        sb.AppendLine("    (Tempérament = portrait : aucun pôle n'y est meilleur que l'autre, rien n'y est pathologique)");
                }

                if (!string.IsNullOrWhiteSpace(c.SyntheseTexte))
                {
                    sb.AppendLine("  Synthèse de la cartographie (rédigée et relue par le médecin) :");
                    sb.AppendLine(Indenter(c.SyntheseTexte!));
                }

                sb.AppendLine();
            }
        }

        // ── Séance 3 — Environnement & évaluation ciblée ──────────────────────

        private void EcrireSeance3(StringBuilder sb, string dir, bool integral = false)
        {
            List<SeanceEnvironnement> fiches;
            try { fiches = _env.LoadAll(dir).ToList(); }
            catch { return; }

            foreach (var s in fiches.OrderBy(s => s.Date))
            {
                var aQuelqueChose = s.HasOrientation || s.HasEvaluation
                                 || s.HasCotationEnv || s.HasReponsesParent || s.HasSynthese;
                if (!aQuelqueChose) continue;

                sb.AppendLine($"■ ENVIRONNEMENT & ÉVALUATION CIBLÉE — séance du {s.Date:dd/MM/yyyy}"
                            + (s.EstCloturee ? " (clôturée)" : " (en cours)"));

                sb.AppendLine($"  Fiabilité — environnement : {FiabiliteCartographie.LabelDe(s.FiabiliteEnv)}"
                            + $" · évaluation ciblée : {FiabiliteCartographie.LabelDe(s.FiabiliteAxes)}");

                if (s.HasOrientation)
                {
                    // Rappelée comme ce qu'elle est — une mise au point de l'attention, pas un
                    // diagnostic. Sans cette précision, un modèle la lit comme une conclusion.
                    sb.AppendLine("  Orientation diagnostique posée avant la séance (mise au point de l'attention, PAS un diagnostic) :");
                    Liste(sb, "Hypothèses", s.HypothesesPrincipales);
                    Liste(sb, "Différentiels", s.Differentiels);
                    Liste(sb, "À éliminer", s.AEliminer);
                    Liste(sb, "Points de vigilance", s.PointsVigilance);
                    Liste(sb, "Questions cliniques", s.QuestionsCliniques);
                }

                if (s.HasCotationEnv || s.HasReponsesParent)
                {
                    var lues = LectureEnvironnementV2.Construire(s.CotationsEnv, s.ReponsesParent);
                    sb.AppendLine("  Cartographie de l'environnement — lecture par feuille :");
                    foreach (var f in lues)
                    {
                        sb.AppendLine(f.EstLisible
                            ? $"    - {f.Label} : {f.NbOui}/{f.NbTotal} favorables — {f.EtatText}"
                            : $"    - {f.Label} : NON LISIBLE — {f.EtatText}");

                        // Lecture intégrale : nervure par nervure, item par item, avec la source.
                        // Qui répond compte ici autant que la réponse — un item qui met en cause
                        // celui qui remplit la feuille revient au médecin, pas au parent.
                        if (!integral) continue;
                        foreach (var n in f.Nervures)
                        {
                            sb.AppendLine($"        ▸ {n.Label}{(n.IsCentrale ? " (centrale)" : "")} : "
                                        + (n.EstComplete ? $"{n.NbOui}/{n.NbTotal} → {n.NiveauLabel}" : $"non lisible ({n.EtatText})"));
                            foreach (var l in n.Lignes)
                            {
                                var marque = l.Reponse switch
                                {
                                    ReponseProposition.Oui => "oui",
                                    ReponseProposition.Non => "NON",
                                    _                      => "non renseigné"
                                };
                                sb.AppendLine($"            [{marque}] {l.Texte}  ({l.SourceLabel})");
                            }
                        }
                    }

                    // Une feuille non lisible est DITE et non omise : la taire laisserait croire
                    // que l'environnement a été exploré en entier.
                    sb.AppendLine("    (une feuille non lisible n'a pas assez de réponses ; ne pas l'interpréter)");
                }

                if (s.HasEvaluation)
                {
                    sb.AppendLine("  Évaluation ciblée — constats observés en séance :");
                    foreach (var a in s.Axes)
                    {
                        sb.AppendLine($"    ▸ {a.Intitule}"
                                    + (string.IsNullOrWhiteSpace(a.Rattachement) ? "" : $" — sert à trancher : {a.Rattachement}"));
                        foreach (var p in a.Propositions.Where(p => p.Reponse != ReponseProposition.NonObservee))
                            sb.AppendLine($"        [{(p.Reponse == ReponseProposition.Oui ? "oui" : "NON")}] {p.Texte}");
                        if (a.HasRemarques)
                            sb.AppendLine($"        Remarque du médecin : {a.Remarques.Replace("\n", " / ")}");
                    }
                    sb.AppendLine("    (une case non cochée signifie NON OBSERVÉ — jamais « non »)");
                }

                if (s.HasSynthese)
                {
                    sb.AppendLine("  Synthèse de la séance (rédigée et relue par le médecin) :");
                    sb.AppendLine(Indenter(s.SyntheseTexte!));
                }

                sb.AppendLine();
            }
        }

        // ── Rendu ─────────────────────────────────────────────────────────────

        private static void Liste(StringBuilder sb, string titre, List<string> items)
        {
            if (items.Count == 0) return;
            sb.AppendLine($"    {titre} : {string.Join(" · ", items)}");
        }

        private static string Indenter(string texte)
            => string.Join("\n", texte.Replace("\r\n", "\n").Split('\n').Select(l => "    " + l)).TrimEnd();
    }
}
