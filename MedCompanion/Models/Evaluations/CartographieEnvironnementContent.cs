namespace MedCompanion.Models.Evaluations
{
    /// <summary>
    /// Contenu canonique (texte) des 5 feuilles dimensionnelles de la Cartographie de
    /// l'environnement. Transcription verbatim du Tome 3 (Grilles de Cotation
    /// Canoniques — Les Feuilles Dimensionnelles), à une exception près :
    /// l'item "médiateur" de la feuille Famille / Vécu émotionnel est reformulé en
    /// positif pour respecter la règle universelle Oui = 1 point.
    /// Source verrouillée par l'auteur.
    /// </summary>
    public static class CartographieEnvironnementContent
    {
        // ─── 1. Famille — Le Socle ────────────────────────────────────────────
        public static FeuilleEnvironnement NewFamille() => new(
            "famille", "Famille", "Le Socle",
            centrale: new Nervure(
                "fonction_parentale_effective",
                "Fonction parentale effective",
                isCentrale: true,
                "Au moins un adulte tient un cadre stable et rassurant ?",
                "Ce cadre est posé sans violence ni passages brutaux d'un extrême à l'autre ?",
                "L'enfant est protégé des conflits entre adultes ?",
                "L'enfant comprend clairement ce qu'on attend de lui (attentes expliquées) ?",
                "Les adultes parviennent à garder une certaine cohérence entre eux ?"),
            new Nervure(
                "liens_familiaux",
                "Liens familiaux",
                isCentrale: false,
                "Présence d'au moins une personne de confiance (écouté/soutenu) ?",
                "Relations globales apaisées et respectueuses (sans tensions lourdes) ?",
                "L'enfant circule librement sans être pris au milieu de conflits ?"),
            new Nervure(
                "vecu_emotionnel",
                "Vécu émotionnel",
                isCentrale: false,
                "L'enfant semble détendu et en confiance dans l'espace familial ?",
                "Peut-il exprimer ses émotions sans crainte de rejet ou de punition ?",
                "L'enfant n'est pas pris comme médiateur des tensions adultes ?"),
            new Nervure(
                "messages_educatifs",
                "Messages éducatifs",
                isCentrale: false,
                "Règles expliquées avec des mots adaptés ?",
                "Cohérence globale entre les différents éducateurs ?",
                "Attentes réalistes (sans menace, chantage ou humiliation) ?"),
            new Nervure(
                "comportement_enfant",
                "Comportement de l'enfant",
                isCentrale: false,
                "Expression des besoins sans crises ou retrait systématiques ?",
                "Place ajustée dans la famille (ni trop effacé, ni parentifié) ?",
                "Réaction ajustée aux tensions (pose des limites, cherche du soutien) ?"));

        // ─── 2. École & Pairs — L'Espace Social ───────────────────────────────
        public static FeuilleEnvironnement NewEcolePairs() => new(
            "ecole_pairs", "École & Pairs", "L'Espace Social",
            centrale: new Nervure(
                "position_eleve",
                "Position d'élève",
                isCentrale: true,
                "Compréhension des règles, consignes et exercices ?",
                "Capacité d'attention adaptée à l'âge sans se perdre ?",
                "Gestion de la frustration face à la difficulté sans s'effondrer ?",
                "Capacité à solliciter de l'aide en cas de blocage ?",
                "Gain progressif en autonomie adapté à son âge ?"),
            new Nervure(
                "attitude_parentale",
                "Attitude parentale",
                isCentrale: false,
                "Écoute régulière du vécu scolaire sans jugement ?",
                "Compréhension des attentes de l'institution (même si désaccord) ?",
                "Valorisation des efforts plutôt que des seuls résultats ?",
                "Capacité à parler des tensions scolaires sans reproches ?"),
            new Nervure(
                "vecu_emotionnel",
                "Vécu émotionnel",
                isCentrale: false,
                "Fréquentation de l'école sans peur excessive ni somatisation ?",
                "Possibilité de nommer ses émotions liées à l'école ?",
                "Maintien d'une confiance en soi minimale dans le cadre scolaire ?"),
            new Nervure(
                "difficultes_scolaires",
                "Difficultés scolaires",
                isCentrale: false,
                "Compréhension réelle du travail demandé ?",
                "Organisation du travail (savoir par où commencer) ?",
                "Persévérance face à l'échec initial ?"),
            new Nervure(
                "comportement",
                "Comportement",
                isCentrale: false,
                "Place adaptée dans la classe (ni invisible, ni envahissant) ?",
                "Expression des désaccords sans comportement extrême ?",
                "Comportement perçu comme un message et non comme l'identité ?"));

        // ─── 3. Écrans & Médias — L'Influence Numérique ───────────────────────
        public static FeuilleEnvironnement NewEcransMedias() => new(
            "ecrans_medias", "Écrans & Médias", "L'Influence Numérique",
            centrale: new Nervure(
                "vigilance_parentale",
                "Vigilance parentale",
                isCentrale: true,
                "Connaissance des contenus consultés par l'enfant ?",
                "Existence de règles claires à la maison ?",
                "Dialogue ouvert sur le vécu numérique ?",
                "Capacité à poser des limites sans conflit systématique ?",
                "Offre d'espaces réguliers « sans écran » ?"),
            new Nervure(
                "vecu_emotionnel",
                "Vécu émotionnel",
                isCentrale: false,
                "Enfant apaisé (non agité/triste) après l'usage ?",
                "Capacité à nommer ses ressentis (plaisir, frustration) ?",
                "L'usage n'est pas une fuite face au stress/solitude ?"),
            new Nervure(
                "qualite_contenus",
                "Qualité des contenus",
                isCentrale: false,
                "Accès à des contenus variés, créatifs et adaptés ?",
                "Usage actif (apprendre, créer) et non seulement passif ?",
                "Supervision globale de l'autonomie numérique ?"),
            new Nervure(
                "gestion_temps",
                "Gestion du temps",
                isCentrale: false,
                "Préservation des temps de jeu libre et de discussion ?",
                "Moments « débranchés » organisés dans la semaine ?",
                "Régulation raisonnable du temps global ?"),
            new Nervure(
                "impact_social",
                "Impact social",
                isCentrale: false,
                "Usage servant de lien social (projets, discussions) ?",
                "Absence d'isolement vis-à-vis de la vie familiale ?",
                "Maintien des capacités d'interaction réelle ?"));

        // ─── 4. Valeurs sociétales — L'Alignement au Monde ────────────────────
        public static FeuilleEnvironnement NewValeursSocietales() => new(
            "valeurs_societales", "Valeurs sociétales", "L'Alignement au Monde",
            centrale: new Nervure(
                "harmonie_valeurs",
                "Harmonie valeurs familiales / sociétales",
                isCentrale: true,
                "Accord global entre les valeurs transmises et l'environnement ?",
                "Aisance éducative malgré les pressions extérieures ?",
                "Absence de conflit intérieur majeur entre convictions et société ?",
                "Pas de besoin de « traduction » permanente des messages du monde ?",
                "Sentiment de cohérence globale pour l'enfant ?"),
            new Nervure(
                "adaptation_enfant",
                "Adaptation de l'enfant",
                isCentrale: false,
                "Compréhension des attentes du monde extérieur ?",
                "Capacité à s'adapter sans se renier ?",
                "Maintien d'une liberté intérieure malgré la conformité ?"),
            new Nervure(
                "message_culturel_milieu",
                "Message culturel du milieu",
                isCentrale: false,
                "Repères éducatifs du quartier/milieu clairs et cohérents ?",
                "L'entourage soutient indirectement les valeurs familiales ?",
                "Absence de contradiction permanente avec le milieu de vie ?"));

        // ─── 5. Cadre éducatif — La Structure Invisible ───────────────────────
        public static FeuilleEnvironnement NewCadreEducatif() => new(
            "cadre_educatif", "Cadre éducatif", "La Structure Invisible",
            centrale: new Nervure(
                "positionnement_parental",
                "Positionnement parental face aux règles",
                isCentrale: true,
                "Connaissance et acceptation des lois actuelles ?",
                "Capacité à expliquer une règle gênante sans agressivité ?",
                "Capacité à dire « non » malgré l'inconfort ?",
                "Refus de laisser l'enfant décider pour éviter les crises ?",
                "Capacité à réviser une règle devenue inadaptée ?"),
            new Nervure(
                "cadre_maison",
                "Cadre à la maison",
                isCentrale: false,
                "Règles connues, claires et régulièrement rappelées ?",
                "Réaction ferme mais sans violence en cas de transgression ?",
                "L'enfant comprend le « pourquoi » de l'interdit ?"),
            new Nervure(
                "autorite_exterieure",
                "Rapport à l'autorité extérieure",
                isCentrale: false,
                "Cohérence des propos tenus sur les institutions ?",
                "Pas de décrédibilisation des figures d'autorité devant l'enfant ?",
                "Aide à l'insertion sociale sans renier ses propres opinions ?"));

        // ─── Lexique d'interprétation pour binding XAML ───────────────────────

        /// <summary>Libellé d'un niveau non encore évalué (aucun item coché).</summary>
        public const string NonEvalueLabel = "Non évalué";

        /// <summary>Couleur grise pour un niveau non encore évalué.</summary>
        public const string NonEvalueColor = "#BDC3C7";

        public static string NiveauLabel(NiveauFeuille? niveau) => niveau switch
        {
            NiveauFeuille.Vert  => "Fluide",
            NiveauFeuille.Jaune => "Fragile",
            NiveauFeuille.Rouge => "Bloqué",
            _                   => "—"
        };

        public static string NiveauDescription(NiveauFeuille? niveau) => niveau switch
        {
            NiveauFeuille.Vert  => "Circulation harmonieuse ; l'enfant dispose d'un oxygène psychologique suffisant.",
            NiveauFeuille.Jaune => "Circulation fluctuante ; présence de tensions ou d'incohérences nécessitant un étayage.",
            NiveauFeuille.Rouge => "Contradictions majeures ou absences nettes ; zone de surcharge ou de confusion clinique.",
            _                   => ""
        };

        /// <summary>Code couleur hex pour binding XAML (badge, fond, etc).</summary>
        public static string NiveauColor(NiveauFeuille? niveau) => niveau switch
        {
            NiveauFeuille.Vert  => "#27AE60",
            NiveauFeuille.Jaune => "#F1C40F",
            NiveauFeuille.Rouge => "#C0392B",
            _                   => "#BDC3C7"
        };
    }
}
