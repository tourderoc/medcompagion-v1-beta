using System;
using System.Collections.Generic;
using System.Linq;

namespace MedCompanion.Models
{
    /// <summary>
    /// Un type de formulaire remis aux parents, remli à la main puis relu champ par champ.
    ///
    /// Le registre existe parce qu'un second type est prévu (la part parents de la cartographie
    /// enfant, notamment) : sans lui, la reconnaissance et le choix de la géométrie de lecture
    /// resteraient câblés sur le seul formulaire de complétion, et chaque nouveau document
    /// demanderait de rouvrir le même code.
    /// </summary>
    public sealed class FormulaireDefinition
    {
        /// <summary>Identifiant stable, repris dans le jeton imprimé. Majuscules, sans accent.</summary>
        public required string Id { get; init; }

        public required string Libelle { get; init; }

        /// <summary>Version de la MISE EN PAGE. À incrémenter dès qu'un bloc bouge.</summary>
        public required int VersionCourante { get; init; }

        /// <summary>Gabarit de la version courante, dans Resources/Formulaires.</summary>
        public required string Template { get; init; }

        /// <summary>
        /// Gabarits des versions antérieures, par numéro de version.
        ///
        /// Indispensable et non optionnel : la carte de coordonnées qui pilote le découpage de la
        /// lecture est calculée à partir du gabarit. Un formulaire imprimé il y a trois semaines,
        /// rempli et scanné aujourd'hui, doit être relu avec la géométrie de SON époque — sinon on
        /// cherche l'adresse là où se trouve désormais la situation familiale, et la lecture est
        /// fausse sans que rien ne le signale.
        /// </summary>
        public Dictionary<int, string> TemplatesAnterieurs { get; init; } = new();

        /// <summary>
        /// Titre imprimé, servant de reconnaissance de repli pour les exemplaires antérieurs au
        /// jeton. Comparé après normalisation (minuscules, sans accent).
        /// </summary>
        public required string TitreNormalise { get; init; }

        /// <summary>
        /// Version à supposer quand le titre est reconnu mais qu'AUCUN jeton n'est présent.
        ///
        /// Un formulaire sans jeton a nécessairement été imprimé avant l'introduction du jeton :
        /// il porte donc la mise en page de cette époque. C'est ce qui permet de relire correctement
        /// les exemplaires déjà remis aux familles.
        /// </summary>
        public required int VersionSansJeton { get; init; }

        /// <summary>Gabarit correspondant à une version donnée, ou null si elle n'est pas archivée.</summary>
        public string? TemplatePourVersion(int version)
        {
            if (version == VersionCourante) return Template;
            return TemplatesAnterieurs.TryGetValue(version, out var t) ? t : null;
        }
    }

    /// <summary>Formulaires connus et lecture du jeton imprimé.</summary>
    public static class FormulairesConnus
    {
        /// <summary>
        /// Jeton imprimé sur le formulaire, par exemple <c>MEDCOMP-FORM-COMPLETION-V2</c>.
        ///
        /// Du texte et non un QR code : la couche texte du PDF transporte déjà le contenu imprimé —
        /// c'est ce qui fait fonctionner la reconnaissance par le titre — alors qu'un QR imposerait
        /// une bibliothèque de décodage, l'extraction de l'image de la page, et une lecture sensible
        /// au biais et à la qualité du scan.
        /// </summary>
        public const string PrefixeJeton = "MEDCOMP-FORM-";

        public static readonly IReadOnlyList<FormulaireDefinition> Tous = new[]
        {
            new FormulaireDefinition
            {
                Id               = "COMPLETION",
                Libelle          = "Formulaire de complétion — 1re consultation",
                VersionCourante  = 2,
                Template         = "formulaire_completion.html",
                // v1 : adresse en bloc 3, situation familiale en bloc 4 — ordre inversé
                // le 30/08/2026 pour que la situation, qui décide du nombre d'adresses, précède.
                TemplatesAnterieurs = new() { [1] = "formulaire_completion_v1.html" },
                TitreNormalise   = "formulaire de completion",
                VersionSansJeton = 1,
            },

            // Feuille du questionnaire parent de la Cartographie de l'enfant. Elle porte le même
            // préfixe de jeton que le formulaire de complétion pour hériter de la reconnaissance
            // tolérante à l'OCR — mesurée sur exemplaire réel, un jeton peut ressortir déformé
            // (« MEDCOMP-FORN-COMPLETION-VS »), et la distance d'édition le rattrape.
            //
            // UNE SEULE VERSION POUR LES QUATRE TRANCHES D'ÂGE : la géométrie est identique,
            // seul le texte des trente énoncés change. La tranche est donc lue dans la fiche de
            // séance, pas dans le jeton.
            new FormulaireDefinition
            {
                Id               = "CARTO",
                Libelle          = "Cartographie de l'enfant — questionnaire parent",
                VersionCourante  = 1,
                Template         = "questionnaire_cartographie.html",
                TitreNormalise   = "cartographie de l enfant",
                VersionSansJeton = 1,
            },
            new FormulaireDefinition
            {
                Id               = "CARTOENV",
                Libelle          = "Cartographie de l'environnement — questionnaire parent",
                VersionCourante  = 1,
                Template         = "questionnaire_environnement.html",
                TitreNormalise   = "cartographie de l environnement",
                VersionSansJeton = 1,
            },
        };

        public static FormulaireDefinition? Par(string id) =>
            Tous.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>Jeton à imprimer sur un formulaire de cette définition.</summary>
        public static string JetonDe(FormulaireDefinition def) =>
            $"{PrefixeJeton}{def.Id}-V{def.VersionCourante}";

        /// <summary>
        /// Minuscules, accents retirés, ponctuation ramenée à des espaces simples.
        ///
        /// Vit ici et non dans le service d'import : la reconnaissance est aussi rejouée à
        /// l'ouverture de la saisie, et deux normalisations distinctes finiraient par diverger —
        /// c'est-à-dire par reconnaître un formulaire à l'import et plus à la relecture.
        /// </summary>
        public static string NormaliserPourComparaison(string texte)
        {
            var decompose = texte.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder(decompose.Length);
            var espacePrecedent = false;

            foreach (var c in decompose)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                    == System.Globalization.UnicodeCategory.NonSpacingMark) continue;

                if (char.IsLetterOrDigit(c)) { sb.Append(c); espacePrecedent = false; }
                else if (!espacePrecedent)   { sb.Append(' '); espacePrecedent = true; }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Reconnaît un formulaire dans un texte extrait.
        ///
        /// Trois voies, dans cet ordre :
        ///  1. le JETON exact — cas d'une couche texte propre ;
        ///  2. le JETON approché — cas d'un formulaire RENDU PAR LES PARENTS, donc scanné, donc
        ///     océrisé. Mesuré sur un exemplaire réel : « MEDCOMP-FORM-COMPLETION-V2 » est ressorti
        ///     « MEDCOMP-FORN-COMPLETION-VS ». La lecture exacte échouait, le repli par le titre
        ///     annonçait v1, et le découpage cherchait l'adresse là où se trouve la situation
        ///     familiale : des champs plausibles et faux, sans la moindre alerte ;
        ///  3. à défaut, le TITRE imprimé — exemplaires antérieurs au jeton, dont on déduit la
        ///     version par <see cref="FormulaireDefinition.VersionSansJeton"/>.
        /// </summary>
        public static (FormulaireDefinition? definition, int version) Reconnaitre(string? texteNormalise)
        {
            if (string.IsNullOrWhiteSpace(texteNormalise)) return (null, 0);

            var jetonNormalise = PrefixeJeton.ToLowerInvariant().Replace("-", " ");

            // TROIS PASSES COMPLÈTES, et non trois essais par définition.
            //
            // Un identifiant peut être le PRÉFIXE d'un autre — « CARTO » et « CARTOENV ». En
            // parcourant les définitions d'abord, la reconnaissance approchée de CARTO était
            // tentée avant le jeton exact de CARTOENV : une lettre d'écart suffisait alors à
            // faire passer la feuille de l'environnement pour celle de l'enfant. Un jeton lu
            // exactement doit toujours l'emporter sur un jeton deviné, quelle que soit la
            // définition à laquelle il appartient.

            // 1. Jeton exact : « medcomp form completion v2 » après normalisation.
            foreach (var def in Tous)
            {
                var racine = jetonNormalise + def.Id.ToLowerInvariant() + " v";
                var i = texteNormalise.IndexOf(racine, StringComparison.Ordinal);
                if (i < 0) continue;

                var apres = texteNormalise[(i + racine.Length)..];
                var chiffres = new string(apres.TakeWhile(char.IsDigit).ToArray());
                if (int.TryParse(chiffres, out var v) && v > 0) return (def, v);
            }

            // 2. Jeton approché, pour un scan océrisé.
            foreach (var def in Tous)
            {
                var versionApprochee = VersionParJetonApproche(texteNormalise, def);
                if (versionApprochee > 0) return (def, versionApprochee);
            }

            // 3. Titre seul : exemplaire imprimé avant que le jeton existe.
            foreach (var def in Tous)
                if (texteNormalise.Contains(def.TitreNormalise, StringComparison.Ordinal))
                    return (def, def.VersionSansJeton);

            return (null, 0);
        }

        /// <summary>Distance d'édition tolérée sur les 22 caractères du jeton, hors chiffre.</summary>
        private const int ToleranceJeton = 3;

        /// <summary>
        /// Caractères que l'OCR rend à la place d'un chiffre. Une lettre peut correspondre à
        /// plusieurs chiffres (s → 5 ou 2) : la levée d'ambiguïté se fait plus bas, en ne gardant
        /// que les versions réellement connues du formulaire.
        /// </summary>
        private static readonly Dictionary<char, int[]> ChiffresConfondus = new()
        {
            ['o'] = new[] { 0 }, ['q'] = new[] { 0 }, ['d'] = new[] { 0 },
            ['i'] = new[] { 1 }, ['l'] = new[] { 1 }, ['t'] = new[] { 1 },
            ['z'] = new[] { 2 }, ['s'] = new[] { 5, 2 }, ['g'] = new[] { 6, 9 },
            ['b'] = new[] { 8, 6 }, ['a'] = new[] { 4 }, ['e'] = new[] { 8 },
        };

        /// <summary>
        /// Cherche le jeton à quelques caractères près, puis décode le chiffre de version.
        /// Renvoie 0 si le jeton est absent ou si le chiffre reste ambigu — on préfère alors la
        /// voie du titre, qui se trompe de façon prévisible, à une version devinée.
        /// </summary>
        private static int VersionParJetonApproche(string texteNormalise, FormulaireDefinition def)
        {
            var compact = new string(texteNormalise.Where(char.IsLetterOrDigit).ToArray());
            var aiguille = "medcompform" + def.Id.ToLowerInvariant() + "v";
            if (compact.Length < aiguille.Length) return 0;

            // Toutes les fenêtres acceptables, la meilleure d'abord. Trier importe : sur un cas
            // réel, la fenêtre décalée d'un caractère passait aussi le seuil (distance 3) et
            // tombait sur un « v » indécodable, alors que la bonne fenêtre était juste à côté à
            // distance 1. Prendre la première venue aurait fait renoncer à un jeton lisible.
            var fenetres = Enumerable
                .Range(0, compact.Length - aiguille.Length + 1)
                .Select(debut => (debut, ecart: Distance(compact.Substring(debut, aiguille.Length), aiguille)))
                .Where(f => f.ecart <= ToleranceJeton)
                .OrderBy(f => f.ecart);

            var connues = VersionsConnues(def);

            foreach (var (debut, _) in fenetres)
            {
                // Le caractère qui suit porte la version. Un vrai chiffre tranche seul.
                var suivant = debut + aiguille.Length < compact.Length
                    ? compact[debut + aiguille.Length]
                    : '\0';

                if (char.IsDigit(suivant)) return suivant - '0';
                if (!ChiffresConfondus.TryGetValue(suivant, out var candidats)) continue;

                var retenues = candidats.Where(connues.Contains).Distinct().ToList();
                if (retenues.Count == 1) return retenues[0];
            }

            return 0;
        }

        private static HashSet<int> VersionsConnues(FormulaireDefinition def)
        {
            var set = new HashSet<int>(def.TemplatesAnterieurs.Keys) { def.VersionCourante };
            return set;
        }

        /// <summary>Distance de Levenshtein, sur des chaînes de quelques dizaines de caractères.</summary>
        private static int Distance(string a, string b)
        {
            var precedent = Enumerable.Range(0, b.Length + 1).ToArray();
            var courant = new int[b.Length + 1];

            for (int i = 1; i <= a.Length; i++)
            {
                courant[0] = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    var cout = a[i - 1] == b[j - 1] ? 0 : 1;
                    courant[j] = Math.Min(Math.Min(courant[j - 1] + 1, precedent[j] + 1),
                                          precedent[j - 1] + cout);
                }
                (precedent, courant) = (courant, precedent);
            }

            return precedent[b.Length];
        }
    }
}
