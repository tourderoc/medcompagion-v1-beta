using System.Collections.Generic;
using System.Linq;

namespace MedCompanion.Models.Evaluations
{
    /// <summary>
    /// Qui remplit un item de la cartographie de l'environnement.
    ///
    /// Le critère n'est PAS celui de la cartographie de l'enfant. Là-bas, c'était l'accès à
    /// l'information : le parent décrit son enfant, le médecin observe dans la pièce. Ici, le
    /// parent décrit SA PROPRE FAMILLE — et sur les items qui comptent le plus, un parent en
    /// difficulté est précisément celui qui répondra « oui ».
    ///
    /// Le critère devient donc : <b>l'item met-il en cause celui qui remplit ?</b> Un item qui
    /// met en cause l'autre adulte, l'école ou la situation se coche honnêtement — c'est souvent
    /// même ce que le parent vient dire. Un item qui le met en cause lui revient au médecin,
    /// qui le cote depuis l'entretien.
    /// </summary>
    public enum SourceItemEnv { Parent, Medecin }

    public class ItemEnvironnement
    {
        public string        Texte  { get; init; } = "";
        public SourceItemEnv Source { get; init; }
    }

    /// <summary>
    /// Une nervure : un sous-axe d'une feuille. Chaque nervure porte sa propre couleur — d'où
    /// l'importance qu'un même fait ne soit jamais compté dans deux nervures : il assombrirait
    /// deux fois le dessin, alors que c'est l'image de la feuille qui lit la dimension.
    /// </summary>
    public class NervureV2
    {
        public string Key        { get; init; } = "";
        public string Label      { get; init; } = "";
        public bool   IsCentrale { get; init; }
        public ItemEnvironnement[] Items { get; init; } = System.Array.Empty<ItemEnvironnement>();
    }

    public class FeuilleV2
    {
        public string Key       { get; init; } = "";
        public string Label     { get; init; } = "";
        public string SousTitre { get; init; } = "";
        public NervureV2[] Nervures { get; init; } = System.Array.Empty<NervureV2>();

        public IEnumerable<ItemEnvironnement> Items => Nervures.SelectMany(n => n.Items);
        public IEnumerable<ItemEnvironnement> ItemsParent  => Items.Where(i => i.Source == SourceItemEnv.Parent);
        public IEnumerable<ItemEnvironnement> ItemsMedecin => Items.Where(i => i.Source == SourceItemEnv.Medecin);
    }

    /// <summary>
    /// Cartographie de l'environnement, refondue — 4 feuilles, 36 items (V1 : 5 feuilles,
    /// 74 items). Rien de clinique n'a été retiré : tout ce qui a disparu était dit deux ou
    /// trois fois ailleurs.
    ///
    /// CE QUI A ÉTÉ CORRIGÉ, feuille par feuille :
    ///
    /// • FAMILLE — le conflit entre adultes était compté TROIS fois (« protégé des conflits »,
    ///   « circule sans être pris au milieu des conflits », « pas pris comme médiateur »), la
    ///   cohérence entre adultes deux fois, les règles expliquées deux fois. La nervure
    ///   « Messages éducatifs » était presque entièrement un doublon de la centrale : dissoute.
    ///   « Comportement de l'enfant » décrivait l'enfant dans une feuille sur son milieu, et
    ///   redisait la séance 2 : dissoute, sauf la parentification, qui est une propriété du
    ///   système familial.
    ///
    /// • ÉCOLE &amp; PAIRS — la feuille s'appelait « École &amp; Pairs » et ne contenait AUCUN item
    ///   sur les pairs. La nervure centrale « Position d'élève » décrivait l'enfant, et quatre
    ///   de ses cinq items existaient déjà en séance 2 (compréhension → Langage, attention →
    ///   profil Attention, frustration → Maintien de l'effort, solliciter de l'aide → Recours).
    ///   « Difficultés scolaires » redoublait la centrale. Les deux items « copains » retirés de
    ///   l'Attachement en séance 2 trouvent ici leur place.
    ///
    /// • ÉCRANS — les temps sans écran étaient comptés trois fois, et la quantité d'écran, donnée
    ///   la plus simple et la plus décisive, n'avait pas de place propre. La nervure centrale
    ///   était cinq items d'auto-évaluation parentale ; ici le partage parent/médecin ne protège
    ///   de rien (le médecin n'a aucune source indépendante sur ce qui se passe le soir à la
    ///   maison). Ce qui protège, c'est de poser des FAITS et non des jugements : un fait se
    ///   déclare, un jugement sur soi se maquille.
    ///
    /// • VALEURS SOCIÉTALES + CADRE ÉDUCATIF — fusionnées. Valeurs sociétales disait onze fois
    ///   la même idée (l'écart entre valeurs familiales et milieu) et était trop abstraite pour
    ///   un questionnaire. Et la nervure « Cadre à la maison » de Cadre éducatif ÉTAIT la
    ///   nervure centrale de Famille — doublon inter-feuilles, le pire de tous : un même fait
    ///   assombrissait deux feuilles.
    /// </summary>
    public static class CartographieEnvironnementV2
    {
        private static ItemEnvironnement P(string t) => new() { Texte = t, Source = SourceItemEnv.Parent };
        private static ItemEnvironnement M(string t) => new() { Texte = t, Source = SourceItemEnv.Medecin };

        public static readonly FeuilleV2[] Feuilles =
        {
            new FeuilleV2
            {
                Key = "famille", Label = "Famille", SousTitre = "Le Socle",
                Nervures = new[]
                {
                    new NervureV2
                    {
                        Key = "fonction_parentale", Label = "Fonction parentale effective", IsCentrale = true,
                        Items = new[]
                        {
                            M("Au moins un adulte tient un cadre stable et rassurant."),
                            M("Ce cadre est posé sans violence, ni menace, ni humiliation."),
                            P("Les règles et les attentes sont expliquées avec des mots que l'enfant comprend."),
                            // Met en cause l'AUTRE adulte : le parent y répond volontiers,
                            // c'est souvent le motif même de la consultation.
                            P("Les adultes qui l'élèvent restent cohérents entre eux."),
                        }
                    },
                    new NervureV2
                    {
                        Key = "liens_familiaux", Label = "Liens familiaux",
                        Items = new[]
                        {
                            P("L'enfant a au moins une personne de confiance vers qui aller."),
                            P("Les relations à la maison sont globalement apaisées."),
                            M("Il est tenu à l'écart des conflits entre adultes — il n'a ni à choisir, ni à arbitrer."),
                        }
                    },
                    new NervureV2
                    {
                        Key = "vecu_emotionnel", Label = "Vécu émotionnel",
                        Items = new[]
                        {
                            P("Il paraît détendu et en confiance à la maison."),
                            M("Il peut exprimer ce qu'il ressent sans crainte de rejet ou de punition."),
                            // La parentification ne se voit pas de l'intérieur : un parent qui
                            // s'appuie sur son enfant vit ça comme une complicité.
                            M("Il occupe une place d'enfant — il ne porte pas les soucis des adultes."),
                        }
                    },
                }
            },

            new FeuilleV2
            {
                Key = "ecole_pairs", Label = "École & Pairs", SousTitre = "L'Espace Social",
                Nervures = new[]
                {
                    new NervureV2
                    {
                        Key = "place_groupe", Label = "Place dans le groupe", IsCentrale = true,
                        Items = new[]
                        {
                            P("Il a au moins un ami avec qui il joue ou parle régulièrement."),
                            P("Il existe des liens hors de la classe — il invite, ou il est invité."),
                            // Réserve assumée : le parent est souvent le dernier informé d'un
                            // harcèlement. C'est la meilleure source disponible malgré tout —
                            // le médecin ne le voit pas davantage.
                            P("Il n'est ni mis à l'écart, ni pris pour cible."),
                        }
                    },
                    new NervureV2
                    {
                        Key = "vecu_scolaire", Label = "Vécu scolaire",
                        Items = new[]
                        {
                            P("Il va à l'école sans peur excessive ni maux de ventre du matin."),
                            P("Il peut dire ce qu'il vit à l'école, en bien comme en mal."),
                            P("Il garde une confiance minimale en ses capacités scolaires."),
                        }
                    },
                    new NervureV2
                    {
                        Key = "entourage_ecole", Label = "Entourage face à l'école",
                        Items = new[]
                        {
                            M("Au moins un adulte écoute son vécu scolaire sans juger."),
                            M("Les efforts sont reconnus, pas seulement les résultats."),
                            M("L'école et la famille se parlent sans que l'enfant soit pris entre les deux."),
                            // Reformulé depuis « comportement perçu comme un message » : ce
                            // n'était pas une observation sur l'enfant mais une posture de
                            // l'institution — donc enfin quelque chose d'environnemental.
                            M("L'école regarde son comportement comme un signal, pas comme son identité."),
                        }
                    },
                }
            },

            new FeuilleV2
            {
                Key = "ecrans", Label = "Écrans & Médias", SousTitre = "L'Influence Numérique",
                Nervures = new[]
                {
                    new NervureV2
                    {
                        Key = "cadre_quantite", Label = "Cadre et quantité", IsCentrale = true,
                        Items = new[]
                        {
                            P("Il existe une heure, ou une limite, à laquelle les écrans s'arrêtent."),
                            P("Il y a des moments de la semaine entièrement sans écran."),
                            P("Le temps d'écran quotidien reste dans ce qui est raisonnable pour son âge."),
                            // Ajout : le fait le plus prédictif du sommeil, purement vérifiable,
                            // et absent de la V1.
                            P("Il n'y a pas d'écran dans sa chambre la nuit."),
                        }
                    },
                    new NervureV2
                    {
                        Key = "usage", Label = "Ce qu'il en fait",
                        Items = new[]
                        {
                            P("Il fait aussi des choses actives : créer, apprendre, construire."),
                            P("Les écrans lui servent parfois de lien avec d'autres."),
                            P("Il peut parler de ce qu'il regarde ou de ce à quoi il joue."),
                        }
                    },
                    new NervureV2
                    {
                        Key = "effets", Label = "Ce que ça produit chez lui",
                        Items = new[]
                        {
                            P("Il reste apaisé quand l'écran s'arrête — la fin ne déclenche pas de crise."),
                            P("Il garde des activités et des intérêts hors écran."),
                            M("L'écran n'est pas ce vers quoi il va quand ça ne va pas."),
                        }
                    },
                }
            },

            new FeuilleV2
            {
                Key = "cadre_reperes", Label = "Cadre & repères", SousTitre = "La Structure Invisible",
                Nervures = new[]
                {
                    new NervureV2
                    {
                        Key = "tenir_cadre", Label = "Tenir le cadre", IsCentrale = true,
                        Items = new[]
                        {
                            M("Les adultes savent dire non et tenir, même quand c'est inconfortable."),
                            M("Une règle devenue inadaptée peut être révisée plutôt que subie."),
                        }
                    },
                    new NervureV2
                    {
                        Key = "monde_autour", Label = "Le monde autour",
                        Items = new[]
                        {
                            P("Les valeurs de la maison et celles du milieu de vie ne sont pas en opposition."),
                            P("La famille n'a pas à corriger en permanence ce que l'enfant reçoit du dehors."),
                            M("Les figures d'autorité extérieures — école, soignants — ne sont pas décrédibilisées devant lui."),
                            M("Il peut s'adapter au dehors sans se sentir en faute vis-à-vis des siens."),
                        }
                    },
                }
            },
        };

        public static FeuilleV2? Par(string key) => Feuilles.FirstOrDefault(f => f.Key == key);

        public static int NbItems        => Feuilles.Sum(f => f.Items.Count());
        public static int NbItemsParent  => Feuilles.Sum(f => f.ItemsParent.Count());
        public static int NbItemsMedecin => Feuilles.Sum(f => f.ItemsMedecin.Count());
    }
}
