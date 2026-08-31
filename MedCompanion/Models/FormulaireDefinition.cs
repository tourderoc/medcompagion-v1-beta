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
        };

        public static FormulaireDefinition? Par(string id) =>
            Tous.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>Jeton à imprimer sur un formulaire de cette définition.</summary>
        public static string JetonDe(FormulaireDefinition def) =>
            $"{PrefixeJeton}{def.Id}-V{def.VersionCourante}";

        /// <summary>
        /// Reconnaît un formulaire dans un texte extrait.
        ///
        /// Deux voies, dans cet ordre :
        ///  1. le JETON, qui donne le type ET la version sans ambiguïté ;
        ///  2. à défaut, le TITRE imprimé — cas des exemplaires antérieurs au jeton, dont on déduit
        ///     la version par <see cref="FormulaireDefinition.VersionSansJeton"/>.
        /// </summary>
        public static (FormulaireDefinition? definition, int version) Reconnaitre(string? texteNormalise)
        {
            if (string.IsNullOrWhiteSpace(texteNormalise)) return (null, 0);

            var jetonNormalise = PrefixeJeton.ToLowerInvariant().Replace("-", " ");

            foreach (var def in Tous)
            {
                // 1. Jeton : "medcomp form completion v2" après normalisation.
                var racine = jetonNormalise + def.Id.ToLowerInvariant() + " v";
                var i = texteNormalise.IndexOf(racine, StringComparison.Ordinal);
                if (i >= 0)
                {
                    var apres = texteNormalise[(i + racine.Length)..];
                    var chiffres = new string(apres.TakeWhile(char.IsDigit).ToArray());
                    if (int.TryParse(chiffres, out var v) && v > 0) return (def, v);
                }

                // 2. Titre seul : exemplaire imprimé avant le jeton.
                if (texteNormalise.Contains(def.TitreNormalise, StringComparison.Ordinal))
                    return (def, def.VersionSansJeton);
            }

            return (null, 0);
        }
    }
}
