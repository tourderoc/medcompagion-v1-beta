using System.Collections.Generic;

namespace MedCompanion.Models.Evaluations
{
    /// <summary>
    /// Nature d'un profil observé, qui décide de sa règle de lecture ET de sa couleur.
    /// </summary>
    public enum ProfilNature
    {
        /// <summary>
        /// Portrait : axes bipolaires, aucun pôle n'est meilleur que l'autre
        /// (« il n'est pas trop ou pas assez, il est lui »). Jamais de rouge ni de vert.
        /// </summary>
        Portrait,

        /// <summary>
        /// Compétence : axes unipolaires, 5 est TOUJOURS le bon côté. Rouge 1-2, neutre 3, vert 4-5.
        /// </summary>
        Competence
    }

    /// <summary>Un axe d'un profil observé : un libellé et ses deux extrémités.</summary>
    public class AxeProfilDef
    {
        public string Key   { get; init; } = "";
        public string Label { get; init; } = "";
        public string Pole1 { get; init; } = "";
        public string Pole5 { get; init; } = "";
    }

    public class ProfilDef
    {
        public string       Key    { get; init; } = "";
        public string       Label  { get; init; } = "";
        public string       Icon   { get; init; } = "";
        public ProfilNature Nature { get; init; }
        public AxeProfilDef[] Axes { get; init; } = System.Array.Empty<AxeProfilDef>();
    }

    /// <summary>
    /// Les 3 profils remplis par le MÉDECIN en observant l'enfant pendant la séance, pendant
    /// que le parent remplit sa feuille en salle d'attente.
    ///
    /// Partage par ACCÈS À L'INFORMATION : les 5 questionnaires portent sur ce que seul le
    /// parent voit (la durée, le quotidien, le lien) ; ces 3 profils sur ce que seul le
    /// clinicien voit, dans la pièce, en trente minutes. Aucun des deux ne peut coter la
    /// moitié de l'autre.
    ///
    /// DEUX NATURES ASSUMÉES (cf. PLAN_CARTOGRAPHIE_ENFANT_V2.md §10.2). Le Tempérament est un
    /// portrait : ses pôles sont deux façons d'être, il ne reçoit donc aucune couleur. La
    /// Psychomotricité et l'Attention sont des compétences : 5 est toujours favorable, elles
    /// sont colorées. La conséquence heureuse est que la couleur rend la distinction visible
    /// sans un mot d'explication.
    ///
    /// COROLLAIRE OBLIGATOIRE : dans les deux profils colorés, aucun axe ne peut être inversé.
    /// La V1 mélangeait les polarités DANS un même profil (« Motricité fine » à 5 = bon,
    /// « Impulsivité motrice » à 5 = mauvais) : on aurait vu du vert à 5 sur une ligne et du
    /// rouge à 5 sur la suivante, au moment précis où le médecin a trois secondes pour regarder.
    /// Tout axe inversé a donc été reformulé (« Impulsivité motrice » → « Contrôle moteur »).
    ///
    /// Cinq axes sur dix-huit faisaient double emploi dans la V1. Aucun score n'était gonflé —
    /// il n'y en a pas — mais une seule observation se retrouvait notée trois ou quatre fois
    /// sous des noms différents, et se relisait ensuite comme une convergence. Un enfant agité
    /// produisait un signal sur quatre axes répartis sur trois profils : ça ressemble à une
    /// corroboration, c'est une seule chose vue une fois.
    /// </summary>
    public static class ProfilsObservesV2
    {
        /// <summary>Valeur d'un axe non renseigné. État de départ : on ne note que ce qu'on a vu.</summary>
        public const int NonRenseigne = 0;

        public static readonly ProfilDef[] Profils =
        {
            // ══ TEMPÉRAMENT ═══════════════════════════════════════════════════════════
            // Portrait, sans couleur.
            // Retiré : « Temps de réaction » (retouchait le niveau d'activité et l'impulsivité,
            // qui appartiennent aux deux autres blocs) et « Rythme / Régularité » — le rythme
            // (sommeil, appétit) NE S'OBSERVE PAS dans la pièce en trente minutes : c'est un
            // savoir de parent, il relève de l'anamnèse.
            // Ajouté : « Approche / retrait », la chose la plus observable des trois premières
            // minutes d'une consultation avec un enfant, et « Humeur de fond ».
            new ProfilDef
            {
                Key = "temperament", Label = "Tempérament", Icon = "🌡",
                Nature = ProfilNature.Portrait,
                Axes = new[]
                {
                    new AxeProfilDef { Key = "activite",   Label = "Niveau d'activité",
                        Pole1 = "Posé, économe de ses mouvements", Pole5 = "En mouvement permanent" },
                    // Approche = PREMIÈRE réaction ; Adaptabilité = ajustement DANS LA DURÉE.
                    // Un enfant qui se retire d'abord puis s'adapte très bien est un profil
                    // fréquent et parlant — un axe unique l'écrase.
                    new AxeProfilDef { Key = "approche",   Label = "Approche / retrait",
                        Pole1 = "Observe longtemps avant d'entrer", Pole5 = "Va vers la nouveauté d'emblée" },
                    new AxeProfilDef { Key = "sensoriel",  Label = "Réactivité sensorielle",
                        Pole1 = "Peu réactif aux bruits, textures, lumières", Pole5 = "Très sensible aux stimulations" },
                    new AxeProfilDef { Key = "intensite",  Label = "Intensité émotionnelle",
                        Pole1 = "Émotions discrètes, peu visibles", Pole5 = "Émotions fortes et démonstratives" },
                    // Reformulé en deux façons d'être : la V1 disait « change difficilement » →
                    // « s'adapte facilement », ce qui donnait un bon côté à un axe de portrait.
                    // Un enfant qui s'ajuste instantanément à tout n'est pas forcément celui qui
                    // va le mieux — il ne signale rien.
                    new AxeProfilDef { Key = "adaptabilite", Label = "Adaptabilité",
                        Pole1 = "A besoin de temps pour accepter le changement", Pole5 = "S'ajuste immédiatement" },
                    new AxeProfilDef { Key = "humeur",     Label = "Humeur de fond",
                        Pole1 = "Sérieux, grave, peu souriant", Pole5 = "Enjoué, souriant d'emblée" },
                }
            },

            // ══ PSYCHOMOTRICITÉ ═══════════════════════════════════════════════════════
            // Compétence, colorée, 5 toujours favorable.
            // Corrigé : « Motricité fine » ≡ « Dextérité » (doublon INTERNE, fusionnés) ;
            // « Motricité globale » vs « Coordination » (superposés, fusionnés) ;
            // « Impulsivité motrice » inversé → « Contrôle moteur » ;
            // « Tonus » était un axe bipolaire déguisé — hypotonie et hypertonie sont toutes
            // deux anormales, le milieu est le bon : coté 1-5 avec 5 = vert, un enfant raide
            // comme un piquet serait ressorti en vert. Devient « Régulation du tonus ».
            new ProfilDef
            {
                Key = "psychomotricite", Label = "Psychomotricité", Icon = "🏃",
                Nature = ProfilNature.Competence,
                Axes = new[]
                {
                    new AxeProfilDef { Key = "aisance",   Label = "Aisance motrice globale",
                        Pole1 = "Malhabile, se cogne, trébuche", Pole5 = "Se déplace et se pose avec aisance" },
                    new AxeProfilDef { Key = "gestes_fins", Label = "Précision des gestes fins",
                        Pole1 = "Gestes imprécis, tenue du crayon difficile", Pole5 = "Gestes précis et ajustés" },
                    new AxeProfilDef { Key = "tonus",     Label = "Régulation du tonus",
                        Pole1 = "Tonus mal ajusté : avachi ou raide", Pole5 = "Tonus ajusté à la situation" },
                    // Frontière avec l'Attention : ICI le contrôle du CORPS (tenir en place,
                    // freiner un geste) ; là-bas l'inhibition COGNITIVE (attendre son tour de
                    // parole). Deux observations réellement différentes dans une pièce.
                    new AxeProfilDef { Key = "controle",  Label = "Contrôle moteur",
                        Pole1 = "Ne tient pas en place, touche tout, ne freine pas", Pole5 = "Peut rester posé, peut retenir un geste" },
                    new AxeProfilDef { Key = "reperage",  Label = "Repérage corporel et spatial",
                        Pole1 = "Se repère mal dans l'espace et par rapport à son corps", Pole5 = "Se repère bien" },
                    // Retenu contre « Investissement du corps », qui frôlait Tempérament n°1
                    // « Niveau d'activité ». Praxies est plus étroit, sans aucun recouvrement,
                    // et pointe directement vers une demande de bilan psychomoteur.
                    new AxeProfilDef { Key = "praxies",   Label = "Praxies / imitation gestuelle",
                        Pole1 = "Ne reproduit pas un geste, perd la séquence", Pole5 = "Reproduit un geste et enchaîne une séquence" },
                }
            },

            // ══ ATTENTION & FONCTIONS EXÉCUTIVES ══════════════════════════════════════
            // Compétence, colorée, 5 toujours favorable.
            // SEUL PROFIL AUQUEL UNE DÉCISION EST ATTACHÉE : il déclenche la demande de bilan
            // attentionnel standardisé. Ses six axes sont donc les six choses qui, vues dans
            // une pièce, font penser « il faut un bilan ».
            // Retiré : « Flexibilité attentionnelle » → Tempérament (dans un bureau,
            // l'observable est le même événement : on change d'activité, on regarde si l'enfant
            // suit — pas de tri de cartes) ; « Attention divisée » → construit de laboratoire
            // jamais testé en consultation, et un axe qu'on ne remplit pas honnêtement finit
            // rempli au jugé.
            new ProfilDef
            {
                Key = "attention", Label = "Attention & fonctions exécutives", Icon = "🧠",
                Nature = ProfilNature.Competence,
                Axes = new[]
                {
                    new AxeProfilDef { Key = "soutenue",   Label = "Attention soutenue",
                        Pole1 = "Décroche au bout de quelques secondes", Pole5 = "Reste plusieurs minutes sur une activité" },
                    new AxeProfilDef { Key = "distraction", Label = "Résistance à la distraction",
                        Pole1 = "Le moindre bruit ou objet le détourne", Pole5 = "Reste sur ce qu'il fait malgré ce qui se passe autour" },
                    // À ne pas confondre avec « Mémoire du vécu » (sphère Pensée), qui est
                    // épisodique et remplie par le parent — formulée exprès pour laisser la
                    // place libre ici.
                    new AxeProfilDef { Key = "memoire_travail", Label = "Mémoire de travail",
                        Pole1 = "Perd la consigne en cours de route", Pole5 = "Garde la consigne en tête pendant qu'il fait" },
                    new AxeProfilDef { Key = "inhibition", Label = "Inhibition",
                        Pole1 = "Répond avant la fin, coupe, ne peut pas attendre", Pole5 = "Attend son tour, laisse finir la question" },
                    new AxeProfilDef { Key = "planification", Label = "Planification",
                        Pole1 = "Se lance sans savoir par où", Pole5 = "S'y prend dans un ordre, sait par où commencer" },
                    // Réserve signalée : cet axe touche autant à la motivation qu'à l'attention.
                    // Un enfant qui abandonne peut être déficitaire, découragé ou déprimé.
                    // Il reste ici — classique de la clinique attentionnelle — mais c'est le
                    // seul des six qui ne se lit pas tout seul.
                    new AxeProfilDef { Key = "effort",     Label = "Maintien de l'effort",
                        Pole1 = "Abandonne dès que ça résiste", Pole5 = "Persévère quand c'est difficile" },
                }
            },
        };

        /// <summary>
        /// Couleur d'une valeur cotée, selon la nature du profil.
        /// Portrait → toujours neutre. Compétence → 1-2 rouge, 3 neutre, 4-5 vert.
        /// Une valeur non renseignée n'a pas de couleur.
        /// </summary>
        public static string CouleurValeur(ProfilNature nature, int valeur)
        {
            if (valeur <= 0) return NeutreVide;
            if (nature == ProfilNature.Portrait) return Portraitte;
            return valeur switch
            {
                <= 2 => Rouge,
                3    => NeutreCote,
                _    => Vert
            };
        }

        public const string Rouge      = "#E74C3C";
        public const string Vert       = "#27AE60";
        public const string NeutreCote = "#93A7BD";  // coté à 3, ni favorable ni défavorable
        public const string Portraitte = "#4F6D8A";  // portrait : coté, mais jamais jugé
        public const string NeutreVide = "#E8EEF5";  // pastille non choisie
    }
}
