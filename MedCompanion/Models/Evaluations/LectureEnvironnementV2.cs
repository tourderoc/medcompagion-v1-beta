using System.Collections.Generic;
using System.Linq;

namespace MedCompanion.Models.Evaluations
{
    /// <summary>Une ligne de nervure, une fois les deux moitiés réunies.</summary>
    public class LigneLue
    {
        public string             Texte   { get; init; } = "";
        public SourceItemEnv      Source  { get; init; }
        public ReponseProposition Reponse { get; init; }

        public bool EstRenseignee => Reponse != ReponseProposition.NonObservee;

        /// <summary>D'où vient la réponse — utile quand une nervure mêle les deux sources.</summary>
        public string SourceLabel => Source == SourceItemEnv.Parent ? "feuille parents" : "entretien";
    }

    /// <summary>
    /// Une nervure lue : ses items des deux sources, et sa couleur — s'il y a lieu.
    ///
    /// COULEUR SEULEMENT SI LA NERVURE EST COMPLÈTE. Une nervure porte 2 à 4 items ; il suffit
    /// qu'un seul manque pour déplacer la teinte d'un tiers. Colorer quand même reviendrait à
    /// afficher, du même gris au même vert, une nervure entièrement renseignée et une nervure
    /// devinée sur deux réponses.
    ///
    /// Le gris n'est donc pas un défaut d'affichage : c'est une information. Il dit qu'on ne sait
    /// pas encore — exactement ce que doit produire une feuille qui n'est pas revenue complète.
    /// </summary>
    public class NervureLue
    {
        public string Label      { get; init; } = "";
        public bool   IsCentrale { get; init; }

        public List<LigneLue> Lignes { get; init; } = new();

        public int NbTotal     => Lignes.Count;
        public int NbOui       => Lignes.Count(l => l.Reponse == ReponseProposition.Oui);
        public int NbNon       => Lignes.Count(l => l.Reponse == ReponseProposition.Non);
        public int NbManquants => Lignes.Count(l => !l.EstRenseignee);

        public bool EstComplete => NbTotal > 0 && NbManquants == 0;

        /// <summary>Part d'items favorables. N'a de sens QUE sur une nervure complète.</summary>
        public double Part => NbTotal == 0 ? 0 : (double)NbOui / NbTotal;

        public NiveauSegment? Niveau => EstComplete ? LectureEnvironnementV2.NiveauPourPart(Part) : null;

        public string Couleur => Niveau.HasValue
            ? CartographieContent.NiveauColor(Niveau.Value)
            : LectureEnvironnementV2.GrisIndetermine;

        public string EtatText => EstComplete
            ? $"{NbOui}/{NbTotal}"
            : $"— il manque {NbManquants} réponse{(NbManquants > 1 ? "s" : "")}";

        public string NiveauLabel => Niveau.HasValue
            ? CartographieContent.NiveauLabel(Niveau.Value)
            : "non lisible";
    }

    /// <summary>
    /// Une feuille lue. Elle ne prend sa couleur que si TOUTES ses nervures sont complètes : une
    /// feuille dont une nervure manque n'est pas une feuille un peu moins sûre, c'est une feuille
    /// dont on ne connaît pas une des tiges.
    /// </summary>
    public class FeuilleLue
    {
        public string Key       { get; init; } = "";
        public string Label     { get; init; } = "";
        public string SousTitre { get; init; } = "";

        public List<NervureLue> Nervures { get; init; } = new();

        public int NbNervures         => Nervures.Count;
        public int NbNervuresLisibles => Nervures.Count(n => n.EstComplete);

        public bool EstLisible => NbNervures > 0 && NbNervuresLisibles == NbNervures;

        public int NbTotal     => Nervures.Sum(n => n.NbTotal);
        public int NbOui       => Nervures.Sum(n => n.NbOui);
        public int NbNon       => Nervures.Sum(n => n.NbNon);
        public int NbManquants => Nervures.Sum(n => n.NbManquants);

        public NiveauSegment? Niveau => EstLisible
            ? LectureEnvironnementV2.NiveauPourPart(NbTotal == 0 ? 0 : (double)NbOui / NbTotal)
            : null;

        public string Couleur => Niveau.HasValue
            ? CartographieContent.NiveauColor(Niveau.Value)
            : LectureEnvironnementV2.GrisIndetermine;

        public string EtatText => EstLisible
            ? $"{NbOui}/{NbTotal} — {CartographieContent.NiveauLabel(Niveau!.Value)}"
            : $"{NbNervuresLisibles}/{NbNervures} nervures lisibles · {NbManquants} réponse{(NbManquants > 1 ? "s" : "")} manquante{(NbManquants > 1 ? "s" : "")}";
    }

    /// <summary>
    /// Réunit les deux moitiés de la cartographie de l'environnement : les 22 réponses de la
    /// feuille parents et les 14 items cotés par le médecin depuis l'entretien.
    ///
    /// C'est ici, et seulement ici, que les nervures prennent une couleur — pas plus tôt : sur les
    /// cartes 4 et 5, chaque moitié était affichée seule, et une teinte calculée sur une moitié
    /// aurait eu l'air d'un résultat.
    /// </summary>
    public static class LectureEnvironnementV2
    {
        /// <summary>Gris d'une nervure qu'on ne peut pas encore lire.</summary>
        public const string GrisIndetermine = "#C3CEDA";

        /// <summary>
        /// Part d'items favorables → couleur. Les seuils sont volontairement larges : avec deux à
        /// quatre items, une échelle fine donnerait des nuances que la donnée ne porte pas.
        /// </summary>
        public static NiveauSegment NiveauPourPart(double part) => part switch
        {
            >= 1.0  => NiveauSegment.VertFonce,
            >= 0.70 => NiveauSegment.VertClair,
            >= 0.50 => NiveauSegment.JauneClair,
            >= 0.30 => NiveauSegment.JauneFonce,
            > 0     => NiveauSegment.RougeClair,
            _       => NiveauSegment.RougeFonce
        };

        /// <param name="cotationsMedecin">Les 14 items du médecin, indexés par leur TEXTE.</param>
        /// <param name="reponsesParent">
        /// Les réponses de la feuille, par clé de feuille — dans l'ORDRE des items parents de
        /// cette feuille, toutes nervures confondues. C'est la numérotation de la page papier :
        /// le parent voit « 1 à 5 », sans les trous laissés par les items du médecin.
        /// </param>
        public static List<FeuilleLue> Construire(
            IReadOnlyDictionary<string, ReponseProposition>? cotationsMedecin,
            IReadOnlyDictionary<string, string[]>? reponsesParent)
        {
            var res = new List<FeuilleLue>();

            foreach (var feuille in CartographieEnvironnementV2.Feuilles)
            {
                string[]? repsP = null;
                reponsesParent?.TryGetValue(feuille.Key, out repsP);

                // Compteur PAR FEUILLE, incrémenté sur les seuls items parents : c'est ce qui fait
                // correspondre la n-ième case de la page papier au n-ième item parent.
                var iParent = 0;

                var nervures = new List<NervureLue>();
                foreach (var nervure in feuille.Nervures)
                {
                    var lignes = new List<LigneLue>();
                    foreach (var item in nervure.Items)
                    {
                        ReponseProposition r;

                        if (item.Source == SourceItemEnv.Parent)
                        {
                            var v = repsP != null && iParent < repsP.Length ? repsP[iParent] : "";
                            r = v switch
                            {
                                "oui" => ReponseProposition.Oui,
                                "non" => ReponseProposition.Non,
                                _     => ReponseProposition.NonObservee
                            };
                            iParent++;
                        }
                        else
                        {
                            r = cotationsMedecin != null && cotationsMedecin.TryGetValue(item.Texte, out var rm)
                                ? rm
                                : ReponseProposition.NonObservee;
                        }

                        lignes.Add(new LigneLue { Texte = item.Texte, Source = item.Source, Reponse = r });
                    }

                    nervures.Add(new NervureLue
                    {
                        Label = nervure.Label, IsCentrale = nervure.IsCentrale, Lignes = lignes
                    });
                }

                res.Add(new FeuilleLue
                {
                    Key = feuille.Key, Label = feuille.Label, SousTitre = feuille.SousTitre,
                    Nervures = nervures
                });
            }

            return res;
        }

        /// <summary>
        /// La lecture mise à plat pour le modèle. Les nervures non lisibles sont DITES comme telles
        /// et non omises : leur absence est une information clinique, et la taire laisserait croire
        /// que la feuille a été lue en entier.
        /// </summary>
        public static string PourPrompt(List<FeuilleLue> feuilles)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var f in feuilles)
            {
                sb.AppendLine($"■ {f.Label} — {f.SousTitre} : {f.EtatText}");
                foreach (var n in f.Nervures)
                {
                    sb.AppendLine(n.EstComplete
                        ? $"  • {n.Label} — {n.NbOui}/{n.NbTotal} favorables ({n.NiveauLabel})"
                        : $"  • {n.Label} — NON LISIBLE, {n.NbManquants} réponse(s) manquante(s) sur {n.NbTotal}");

                    foreach (var l in n.Lignes)
                    {
                        var marque = l.Reponse switch
                        {
                            ReponseProposition.Oui => "oui",
                            ReponseProposition.Non => "NON",
                            _                      => "?"
                        };
                        sb.AppendLine($"      [{marque}] {l.Texte}  ({l.SourceLabel})");
                    }
                }
                sb.AppendLine();
            }
            return sb.ToString();
        }
    }
}
