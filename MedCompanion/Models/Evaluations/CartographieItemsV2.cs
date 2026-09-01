using System;
using System.Collections.Generic;

namespace MedCompanion.Models.Evaluations
{
    /// <summary>
    /// Bandes d'âge de la Cartographie de l'enfant V2. Inchangées par rapport à la V1 :
    /// elles suivent les ruptures développementales (avant école / GS-CP / élémentaire / préado).
    /// </summary>
    public enum BandeAgeCarto
    {
        TroisQuatre = 0,   // 3-4 ans
        CinqSix     = 1,   // 5-6 ans
        SeptNeuf    = 2,   // 7-9 ans
        DixOnze     = 3    // 10-11 ans
    }

    /// <summary>
    /// Contenu canonique V2 des 5 questionnaires parents de la Cartographie de l'enfant.
    ///
    /// DÉCISION STRUCTURANTE (cf. PLAN_CARTOGRAPHIE_ENFANT_V2.md) : la grille de conversion
    /// score → couleur est UNIQUE et indépendante de l'âge ; ce sont les ITEMS qui changent
    /// selon la bande d'âge. La V1 faisait l'inverse — d'où un plafond à 10-11 ans (4/6 = rouge
    /// foncé) et un plancher à 3-4 ans (items « copains d'école » impossibles avant l'école).
    ///
    /// L'item n°i mesure la MÊME dimension dans les 4 bandes (cf. <see cref="Dimensions"/>).
    /// Cette stabilité disciplinera l'écriture, garde une géométrie de feuille constante, et
    /// dit qualitativement CE QUI accroche quand un enfant perd un point.
    ///
    /// Calibration : dans chaque bande, un enfant qui va bien à cet âge-là doit en cocher 5 ou 6.
    /// Un item que tout le monde réussit est un défaut d'écriture, pas quelque chose qu'on
    /// rattrape en durcissant la conversion.
    ///
    /// Aucune dimension n'appartient à deux sphères : la V1 en comptait dix en double
    /// (verbalisation des affects en Langage ET Émotions, réconfort en Attachement ET Émotions,
    /// compréhension des consignes en Langage ET Pensée, concentration en Pensée ET profil
    /// Attention…). Chaque doublon faisait perdre deux points pour une seule difficulté.
    /// Toute évolution des items doit repasser par ce contrôle.
    ///
    /// L'attention, la psychomotricité et le tempérament sont ABSENTS d'ici : ce sont les
    /// 3 profils remplis par le médecin en observant l'enfant, et l'attention est la sphère
    /// qui déclenche la demande de bilan attentionnel standardisé — l'avis des parents ne
    /// doit pas la pré-empter.
    /// </summary>
    public static class CartographieItemsV2
    {
        public const int AgeMin = 3;
        public const int AgeMax = 11;

        /// <summary>Clés des 5 axes, dans l'ordre de lecture séquentielle de la feuille.</summary>
        public static readonly string[] AxeKeys =
            { "attachement", "langage", "emotions", "imaginaire", "pensee" };

        public static bool IsApplicable(int? age)
            => age.HasValue && age.Value >= AgeMin && age.Value <= AgeMax;

        /// <summary>Bande d'âge applicable, ou null hors fourchette 3-11.</summary>
        public static BandeAgeCarto? Bande(int? age)
        {
            if (!IsApplicable(age)) return null;
            return age!.Value switch
            {
                <= 4 => BandeAgeCarto.TroisQuatre,
                <= 6 => BandeAgeCarto.CinqSix,
                <= 9 => BandeAgeCarto.SeptNeuf,
                _    => BandeAgeCarto.DixOnze
            };
        }

        /// <summary>Libellé imprimé dans le bandeau de la feuille et estampillé dans le YAML.</summary>
        public static string BandeLabel(BandeAgeCarto b) => b switch
        {
            BandeAgeCarto.TroisQuatre => "3-4 ans",
            BandeAgeCarto.CinqSix     => "5-6 ans",
            BandeAgeCarto.SeptNeuf    => "7-9 ans",
            _                         => "10-11 ans"
        };

        /// <summary>Code court et stable, destiné à être écrit dans les fichiers d'évaluation.</summary>
        public static string BandeCode(BandeAgeCarto b) => b switch
        {
            BandeAgeCarto.TroisQuatre => "3-4",
            BandeAgeCarto.CinqSix     => "5-6",
            BandeAgeCarto.SeptNeuf    => "7-9",
            _                         => "10-11"
        };

        /// <summary>
        /// LA grille unique, indépendante de l'âge. C'est la grille 5-6 ans de la V1 — la seule
        /// des quatre qui fût bien formée : strictement monotone, un niveau par score, les six
        /// niveaux utilisés. Les trois autres en étaient des déformations destinées à rattraper
        /// des items mal calibrés pour leur âge ; ce sont les items qui changent désormais.
        /// </summary>
        public static NiveauSegment NiveauPourScore(int score) => score switch
        {
            >= 6 => NiveauSegment.VertFonce,
            5    => NiveauSegment.VertClair,
            4    => NiveauSegment.JauneClair,
            3    => NiveauSegment.JauneFonce,
            2    => NiveauSegment.RougeClair,
            _    => NiveauSegment.RougeFonce
        };

        public static string AxeLabel(string axeKey) => axeKey switch
        {
            "attachement" => "Attachement & sécurité intérieure",
            "langage"     => "Langage & communication",
            "emotions"    => "Émotions",
            "imaginaire"  => "Imaginaire & monde intérieur",
            "pensee"      => "Pensée & organisation",
            _             => axeKey
        };

        /// <summary>
        /// Les 6 dimensions stables de chaque axe. L'item n°i d'une bande mesure la dimension
        /// n°i, quelle que soit la bande — seule son expression développementale change.
        /// </summary>
        public static IReadOnlyList<string> Dimensions(string axeKey) => axeKey switch
        {
            "attachement" => new[] { "Séparation", "Recours", "Consolabilité", "Reprise du lien", "Confiance en la disponibilité", "Prudence avec l'inconnu" },
            "langage"     => new[] { "Compréhension", "Se faire comprendre", "Récit", "Conversation", "Réparation", "Le langage comme outil" },
            "emotions"    => new[] { "Expressivité", "Nommer", "Proportion", "Retour au calme", "Moyens propres", "Émotions d'autrui" },
            "imaginaire"  => new[] { "Vie intérieure", "Symboliser & créer", "Accès", "Élaboration", "Frontière", "Questionnement existentiel" },
            "pensee"      => new[] { "Curiosité causale", "Apprentissage", "Mémoire du vécu", "Raisonnement", "Résolution de problème", "Repérage dans le temps" },
            _             => Array.Empty<string>()
        };

        /// <summary>
        /// Les 6 affirmations d'un axe pour une bande donnée. Cotation binaire, « oui » = 1 point.
        /// Toutes les formulations sont positives : « oui » est toujours la réponse favorable.
        /// </summary>
        public static IReadOnlyList<string> Items(string axeKey, BandeAgeCarto bande)
            => Grille.TryGetValue((axeKey, bande), out var items) ? items : Array.Empty<string>();

        private static readonly Dictionary<(string, BandeAgeCarto), string[]> Grille = new()
        {
            // ══ AXE 1 — ATTACHEMENT ═══════════════════════════════════════════════════
            // Les items « copains d'école » de la V1 mesuraient la sociabilité entre pairs,
            // pas l'attachement : deux construits additionnés dans un même score, et cause
            // du plancher à 3 ans. Retirés.
            [("attachement", BandeAgeCarto.TroisQuatre)] = new[]
            {
                "Il accepte de rester sans moi dans un lieu qu'il connaît (crèche, école, chez ses grands-parents).",
                "Quand il a mal ou qu'il a peur, il vient me chercher.",
                "Quand il est bouleversé, il se calme dans mes bras ou avec ma voix.",
                "Quand je reviens le chercher, il vient vers moi, content.",
                "Il s'éloigne pour jouer et revient vers moi de temps en temps.",
                "Il garde une réserve avec les adultes qu'il ne connaît pas.",
            },
            [("attachement", BandeAgeCarto.CinqSix)] = new[]
            {
                "Il passe la journée à l'école sans que la séparation soit un problème.",
                "Quand quelque chose l'a blessé ou inquiété dans sa journée, il finit par m'en parler.",
                "Quand il est en colère ou triste, il accepte que je l'aide à se calmer.",
                "Quand on se retrouve le soir, il vient vers moi et me raconte.",
                "Il se rassure quand je lui dis à l'avance ce qui va se passer (qui vient le chercher, à quelle heure).",
                "Il ne part pas facilement avec un adulte qu'il ne connaît pas.",
            },
            [("attachement", BandeAgeCarto.SeptNeuf)] = new[]
            {
                "Il peut passer une journée entière chez quelqu'un d'autre sans avoir besoin de m'appeler.",
                "Quand il a un problème trop gros pour lui, il vient me le dire plutôt que de le garder.",
                "Quand il est débordé, il accepte encore d'être réconforté par un adulte.",
                "Après une absence ou une journée difficile, il revient vers moi de lui-même.",
                "Une parole de ma part suffit à le rassurer quand il s'inquiète.",
                "Il garde la bonne distance avec les adultes qu'il connaît peu.",
            },
            [("attachement", BandeAgeCarto.DixOnze)] = new[]
            {
                "Il peut partir plusieurs jours (colonie, classe verte, chez un ami) sans que ça tourne mal.",
                "Quand quelque chose de grave ou d'embarrassant lui arrive, il finit par m'en parler, ou à un adulte de confiance.",
                "Quand il va mal, il accepte encore un geste ou une parole de réconfort, même s'il fait le grand.",
                "Après une dispute entre nous, c'est réparable — on se reparle.",
                "Il me fait confiance quand je lui dis que je serai là.",
                "Il garde une réserve appropriée avec les adultes qu'il connaît peu, y compris en ligne.",
            },

            // ══ AXE 2 — LANGAGE & COMMUNICATION ═══════════════════════════════════════
            // Deux items V1 ne mesuraient pas le langage : « exprime ses besoins sans crier »
            // (régulation émotionnelle) et « utilise des mots pour décrire ses émotions »
            // (doublon exact avec Émotions n°2). La verbalisation des affects est rendue
            // exclusivement aux Émotions. Ajout de l'item le plus utile à 3-4 ans, absent
            // de la V1 : est-ce que les inconnus le comprennent ? (trouble d'articulation).
            [("langage", BandeAgeCarto.TroisQuatre)] = new[]
            {
                "Il comprend une consigne simple sans qu'on ait besoin de lui montrer.",
                "Les gens qui ne le connaissent pas comprennent ce qu'il dit.",
                "Il raconte un petit bout de ce qu'il a fait (un moment, un événement).",
                "Il répond quand on lui parle et reste un moment dans l'échange.",
                "Quand il n'a pas compris, ça se voit — il redemande ou il montre.",
                "Il demande avec des mots ce qu'il veut, plutôt que de le prendre ou de pleurer.",
            },
            [("langage", BandeAgeCarto.CinqSix)] = new[]
            {
                "Il comprend une consigne en deux temps (« range tes chaussures et va te laver les mains »).",
                "Il parle en phrases complètes, sans que j'aie besoin de traduire pour les autres.",
                "Il raconte sa journée dans l'ordre, et on comprend.",
                "Il attend son tour pour parler, sans couper tout le temps.",
                "Il dit quand il n'a pas compris, plutôt que de faire semblant.",
                "Il explique ce qu'il veut, ou pourquoi il n'est pas d'accord.",
            },
            [("langage", BandeAgeCarto.SeptNeuf)] = new[]
            {
                "Il comprend une explication un peu longue sans qu'on ait à la redécouper.",
                "Il trouve ses mots sans tourner longtemps autour.",
                "Il raconte une histoire ou un film avec assez de détails pour qu'on suive.",
                "Il tient une conversation : il relance, il pose des questions à l'autre.",
                "Il pose une question précise sur ce qu'il n'a pas compris.",
                "Il défend son point de vue avec des arguments.",
            },
            [("langage", BandeAgeCarto.DixOnze)] = new[]
            {
                "Il comprend ce qui n'est pas dit directement — l'allusion, l'ironie, le second degré.",
                "Il arrive à expliquer clairement quelque chose de compliqué.",
                "Il raconte un événement en tenant compte de ce que je sais déjà et de ce que j'ignore.",
                "Il adapte sa façon de parler selon la personne (un copain, un adulte, un professeur).",
                "Il reformule ou fait préciser, plutôt que de partir sur un malentendu.",
                "Il négocie, il argumente, et il peut changer d'avis en discutant.",
            },

            // ══ AXE 3 — ÉMOTIONS ══════════════════════════════════════════════════════
            // Retiré : « accepte d'être réconforté » (= consolabilité, appartient à
            // l'Attachement) et la double formulation du retour au calme, qui comptait
            // deux fois. Ajouté : la PROPORTION, dimension la plus discriminante en clinique
            // et absente de la V1 — une dysrégulation n'est pas de ressentir fort, c'est que
            // la réaction soit sans commune mesure avec l'événement.
            // Ligne de partage : accepter le réconfort de l'autre = Attachement ;
            // avoir ses propres moyens de s'apaiser = Émotions.
            [("emotions", BandeAgeCarto.TroisQuatre)] = new[]
            {
                "On voit tout de suite quand il est content, triste ou fâché.",
                "Il dit des mots simples pour ce qu'il ressent (« content », « peur », « pas content »).",
                "Ses colères et ses chagrins restent à la mesure de ce qui vient de se passer.",
                "Après une grosse colère ou un gros chagrin, il redescend en quelques minutes.",
                "Il a des moyens à lui pour se rassurer (un doudou, un coin, un geste).",
                "Il remarque quand quelqu'un pleure ou est triste.",
            },
            [("emotions", BandeAgeCarto.CinqSix)] = new[]
            {
                "Je sais lire sur son visage ce qu'il ressent, même s'il ne le dit pas.",
                "Il peut dire ce qu'il ressent avec ses mots (triste, en colère, jaloux).",
                "Il ne s'effondre pas pour une contrariété ordinaire.",
                "Quand c'est passé, c'est passé — il repart sur autre chose.",
                "Quand ça monte, il sait s'isoler ou faire quelque chose qui le calme.",
                "Il reconnaît quand un autre enfant est triste ou en colère.",
            },
            [("emotions", BandeAgeCarto.SeptNeuf)] = new[]
            {
                "Quand quelque chose l'a touché, ça finit par se voir — il ne masque pas tout.",
                "Il distingue des émotions proches : déçu, vexé, en colère, ce n'est pas pareil pour lui.",
                "Sa réaction est proportionnée — il ne part pas très haut pour peu de chose.",
                "Il retrouve son calme sans que la journée entière en soit gâchée.",
                "Il a des façons à lui de se calmer, sans qu'un adulte ait à intervenir.",
                "Il tient compte de ce que ressent l'autre : il s'arrête, il console, il s'excuse.",
            },
            // Dimension 1 à 10-11 ans : cacher ses émotions à ses parents est NORMAL à cet âge.
            // L'item ne peut donc pas être « il montre ce qu'il ressent » — un préado sain
            // répondrait non. Il porte sur le fait qu'un canal reste ouvert malgré la pudeur.
            [("emotions", BandeAgeCarto.DixOnze)] = new[]
            {
                "Même quand il cache, je finis par savoir que ça ne va pas — il ne reste pas hermétique.",
                "Il peut expliquer pourquoi il se sent comme ça, pas seulement ce qu'il ressent.",
                "Il encaisse une déception ou une injustice sans que ça prenne des proportions.",
                "Après une contrariété, il revient à lui-même dans la journée — il ne rumine pas des jours.",
                "Il sait ce qui lui fait du bien quand ça ne va pas, et il y a recours.",
                "Il perçoit quand quelqu'un va mal, même sans que ce soit dit.",
            },

            // ══ AXE 4 — IMAGINAIRE & MONDE INTÉRIEUR ══════════════════════════════════
            // Seul axe où un item V1 ne perdait pas seulement du pouvoir discriminant avec
            // l'âge, mais VOYAIT SA VALENCE S'INVERSER : le faire-semblant culmine vers 4-6 ans
            // et décline normalement ensuite. « Il a des personnages imaginaires » coché OUI
            // chez un enfant de 11 ans n'est pas une bonne nouvelle. Les dimensions sont donc
            // définies au niveau qui survit à ce déclin : la symbolisation, dont le véhicule
            // change (jeu → récit → fiction et création).
            // Dimension 5 (frontière réel/imaginaire) est nouvelle : elle distingue
            // l'imaginaire ressource de l'imaginaire qui envahit.
            // Dimension 4 bornée pour ne pas empiéter sur Émotions n°5 : là-bas s'apaiser,
            // ici élaborer (rejouer, mettre en récit, digérer).
            [("imaginaire", BandeAgeCarto.TroisQuatre)] = new[]
            {
                "Il joue seul en se racontant des choses à voix haute.",
                "Il transforme les objets pour jouer : un bâton devient une épée, un carton une maison.",
                "Il me montre ou me raconte un bout de ce qu'il est en train de jouer.",
                "Il rejoue dans ses jeux des choses qu'il a vécues (le docteur, l'école, une dispute).",
                "Quand un jeu ou une histoire lui fait peur, il se rassure si on lui dit que ce n'est pas pour de vrai.",
                "Il pose des questions sur les grandes choses (d'où viennent les bébés, où sont les gens qui ne sont plus là).",
            },
            [("imaginaire", BandeAgeCarto.CinqSix)] = new[]
            {
                "Il s'invente des histoires dans sa tête, il rêvasse.",
                "Il invente des scénarios de jeu élaborés, avec des rôles et des règles.",
                "Il me raconte ce qu'il imagine, ou ce dont il a rêvé.",
                "Quand quelque chose l'a marqué, ça se retrouve dans ses jeux ou ses dessins.",
                "Il sait faire la différence entre ce qu'il invente et ce qui est arrivé pour de vrai.",
                "Il pose des questions sur la mort, la naissance, le temps.",
            },
            [("imaginaire", BandeAgeCarto.SeptNeuf)] = new[]
            {
                "Il a un monde à lui — des histoires, des univers, des choses qu'il se raconte.",
                "Il invente des histoires, des mondes ou des personnages, ou il les dessine.",
                "Il me raconte ses idées, ses histoires, ce qu'il invente.",
                "Son imaginaire lui fait du bien : il s'y ressource sans s'y perdre.",
                "Il fait clairement la part entre l'imaginaire et le réel.",
                "Il s'interroge sur des choses qui le dépassent — la mort, l'infini, l'injustice.",
            },
            // Dimension 3 : « un peu » est volontaire — un préado qui donne un accès complet
            // à son monde intérieur n'est pas plus sain qu'un autre.
            [("imaginaire", BandeAgeCarto.DixOnze)] = new[]
            {
                "Il a une vie intérieure à lui, où il se retire parfois (pensées, projets, rêveries).",
                "Il crée ou s'investit dans des univers de fiction : écrire, dessiner, jouer, construire, lire.",
                "Il me laisse entrer un peu dans ce qui l'occupe ou ce qu'il pense.",
                "Ses histoires, ses lectures ou ses jeux l'aident à digérer ce qu'il traverse.",
                "Son imaginaire reste à sa place — il ne déborde pas sur sa vie de tous les jours.",
                "Il se pose des questions sur le sens, sur lui-même, sur ce qu'il deviendra.",
            },

            // ══ AXE 5 — PENSÉE & ORGANISATION COGNITIVE ═══════════════════════════════
            // Sphère reconstruite intégralement : 4 des 6 items V1 appartenaient ailleurs
            // (2 au Langage, 2 aux profils Attention/Tempérament). Elle ne mesurait pas la
            // pensée mais du langage, de l'attention et du tempérament — un enfant TDAH y
            // perdait des points déjà comptés dans le profil Attention.
            // Dimension 2 (apprentissage) est celle qui manquait le plus : « ce qu'on lui
            // apprend reste-t-il acquis ? » est le marqueur parental par excellence d'un
            // trouble des apprentissages, et la V1 ne l'interrogeait nulle part.
            // Dimension 4 formulée en « faire des liens et prévoir », JAMAIS en « réfléchir
            // avant d'agir » — sinon on remesure l'inhibition, donc l'Attention.
            [("pensee", BandeAgeCarto.TroisQuatre)] = new[]
            {
                "Il demande comment ça marche, pourquoi ça fait ça.",
                "Ce qu'on lui montre, il l'attrape et il arrive à le refaire.",
                "Quand on lui reparle de quelque chose qu'il a vécu, il s'en souvient.",
                "Il comprend que si on fait ceci, il arrive cela (si je lâche, ça tombe).",
                "Devant un petit obstacle, il essaie quelque chose de lui-même.",
                "Il comprend « après », « tout à l'heure », « demain ».",
            },
            [("pensee", BandeAgeCarto.CinqSix)] = new[]
            {
                "Il veut savoir comment les choses fonctionnent, et la réponse l'intéresse vraiment.",
                "Ce qu'il apprend à l'école ou à la maison finit par tenir.",
                "Il se souvient d'événements d'il y a plusieurs semaines.",
                "Il comprend les conséquences simples de ce qu'il fait.",
                "Il trouve des solutions à de petits problèmes du quotidien.",
                "Il se repère dans la journée et dans la semaine (l'école, le week-end, les jours).",
            },
            [("pensee", BandeAgeCarto.SeptNeuf)] = new[]
            {
                "Il creuse ce qui l'intéresse : il pose des questions, il cherche à savoir.",
                "Une notion apprise reste acquise — on ne repart pas de zéro à chaque fois.",
                "Il se souvient de choses qui se sont passées il y a des mois, avec des détails justes.",
                "Il fait des liens : il voit ce qui va arriver s'il continue comme ça.",
                "Devant un problème concret, il trouve comment s'en sortir.",
                "Il se repère dans le mois et l'année, il situe les événements dans le temps.",
            },
            [("pensee", BandeAgeCarto.DixOnze)] = new[]
            {
                "Quand quelque chose l'intrigue, il va chercher la réponse lui-même.",
                "Il apprend de nouvelles choses sans que ça lui demande un effort disproportionné.",
                "Il a des souvenirs construits de son enfance, il peut y revenir.",
                "Il raisonne et il pèse — il voit les conséquences un peu à distance.",
                "Face à une situation nouvelle, il trouve une solution qui tient la route.",
                "Il se projette : la semaine prochaine, les vacances, l'année d'après.",
            },
        };
    }
}
