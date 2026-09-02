using System.Linq;

namespace MedCompanion.Models.Evaluations
{
    /// <summary>
    /// Un niveau de fiabilité d'une source d'observation.
    ///
    /// Le médecin choisit un MOT, le système tient un NOMBRE : on ne distingue pas 0,6 de 0,7
    /// de façon reproductible, mais « fiable » de « peu fiable », oui. Le poids reste sur
    /// l'échelle 0-1 déjà utilisée par <c>SynthesisWeightTracker</c> et par les documents
    /// importés — le modèle ne voit ainsi qu'une seule notion de poids.
    /// </summary>
    public class NiveauFiabilite
    {
        public string  Key     { get; init; } = "";
        public string  Label   { get; init; } = "";
        public string  Detail  { get; init; } = "";
        /// <summary>Poids 0-1, ou null quand la source est écartée.</summary>
        public double? Poids   { get; init; }
        public string  Couleur { get; init; } = "";
    }

    /// <summary>
    /// Fiabilité déclarée des deux moitiés de la cartographie.
    ///
    /// DEUX CURSEURS, pas un. Pondérer le seul questionnaire parent reviendrait à traiter
    /// implicitement les dix-huit axes observés comme certains — or un enfant vu vingt minutes,
    /// malade ou figé lors d'une première rencontre, ça se pondère aussi. La dissymétrie serait
    /// un jugement caché.
    ///
    /// La fiabilité qualifie la SOURCE, elle ne corrige jamais une VALEUR : un 4/6 reste un 4/6
    /// et sa couleur ne bouge pas. Sans quoi on obtiendrait des scores « ajustés » que plus
    /// personne ne pourrait retracer.
    /// </summary>
    public static class FiabiliteCartographie
    {
        public static readonly NiveauFiabilite[] Niveaux =
        {
            new() { Key = "fiable",  Label = "Fiable",              Poids = 1.00, Couleur = "#27AE60",
                    Detail = "Rempli avec attention, par quelqu'un qui connaît bien l'enfant" },
            new() { Key = "moyenne", Label = "Moyennement fiable",  Poids = 0.65, Couleur = "#F1C40F",
                    Detail = "Quelques doutes sur l'attention portée ou la connaissance du quotidien" },
            new() { Key = "faible",  Label = "Peu fiable",          Poids = 0.30, Couleur = "#E67E22",
                    Detail = "Rempli vite, ou par quelqu'un qui voit peu l'enfant" },
            // Zéro n'est pas un poids, c'est un état : une source non exploitable ne doit pas
            // peser zéro dans un calcul, elle doit être écartée et dite comme telle.
            new() { Key = "non_exploitable", Label = "Non exploitable", Poids = null, Couleur = "#95A5A6",
                    Detail = "À écarter du croisement — dit explicitement, pas pesé à zéro" },
        };

        public static NiveauFiabilite? Par(string? key)
            => string.IsNullOrEmpty(key) ? null : Niveaux.FirstOrDefault(n => n.Key == key);

        public static string LabelDe(string? key) => Par(key)?.Label ?? "non renseignée";
    }
}
