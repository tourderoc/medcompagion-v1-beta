namespace MedCompanion.Models.Evaluations
{
    /// <summary>
    /// Contenu canonique (texte) de la Chenille Universelle : affirmations des questionnaires
    /// et phrases-boussoles de chaque segment.
    /// Source : GRILLES_CARTOGRAPHIE_CANONIQUES.md (verrouillé par l'auteur).
    /// </summary>
    public static class CartographieContent
    {
        public static ChenilleSegment NewAttachement() => new(
            "attachement",
            "Attachement & sécurité intérieure",
            "Il a besoin de sentir qu'il peut s'éloigner… sans me perdre.",
            "Mon enfant accepte de se séparer de moi dans un lieu connu.",
            "Il accepte que d'autres adultes prennent soin de lui sans panique.",
            "Il revient vers moi (ou un autre adulte) quand il vit une émotion forte.",
            "Il cherche mon regard ou un contact dans un contexte incertain.",
            "Il a au moins un copain à l'école, et m'en parle.",
            "Il demande à voir ou inviter ses copains en dehors de l'école.");

        public static ChenilleSegment NewPsychomotricite() => new(
            "psychomotricite",
            "Psychomotricité & exploration",
            "Son corps est son premier terrain de jeu… et de découverte.",
            "Mon enfant aime grimper, courir, sauter ou ramper.",
            "Il manipule des objets sans tout casser ni les lâcher sans cesse.",
            "Il peut rester concentré sur une activité physique pendant quelques minutes.",
            "Il supporte la majorité des textures (pâte, sable, herbe, habits…).",
            "Il se repère facilement dans l'espace (dedans/dehors, gauche/droite).",
            "Il alterne naturellement mouvement et pause sans qu'on le force.");

        public static ChenilleSegment NewLangage() => new(
            "langage",
            "Langage & communication",
            "Parler, ce n'est pas juste dire des mots. C'est entrer en relation.",
            "Mon enfant raconte ce qu'il a vécu dans sa journée.",
            "Il comprend ce qu'on lui dit sans qu'il faille toujours reformuler.",
            "Il pose des questions ou reformule quand il ne comprend pas.",
            "Il exprime ses besoins sans forcément crier ou se fâcher.",
            "Il utilise des mots pour décrire ses émotions ou ses pensées.",
            "Il peut écouter ou attendre son tour quand quelqu'un parle.");

        public static ChenilleSegment NewEmotions() => new(
            "emotions",
            "Émotions",
            "Il ne sait pas encore gérer ses émotions. Il apprend à les traverser.",
            "Mon enfant montre clairement quand il est triste, fâché ou joyeux.",
            "Il peut mettre des mots sur ce qu'il ressent.",
            "Quand il est débordé, il accepte d'être réconforté ou calmé.",
            "Il retrouve son calme en quelques minutes, avec ou sans aide.",
            "Il sait reconnaître quand un autre enfant vit une émotion.",
            "Il vit ses émotions intensément mais revient à l'équilibre sans blocage.");

        public static ChenilleSegment NewImaginaire() => new(
            "imaginaire",
            "Imaginaire & monde intérieur",
            "Son monde imaginaire n'est pas une fuite. C'est une passerelle vers son monde intérieur.",
            "Mon enfant invente des histoires ou se raconte des choses dans sa tête.",
            "Il transforme facilement les objets ou les lieux pour jouer \"comme si\".",
            "Il a des personnages imaginaires ou fait \"comme si\" dans ses jeux.",
            "Il peut raconter ce qu'il pense, imagine ou rêve.",
            "Il pose parfois des questions existentielles (vie, mort, pourquoi…).",
            "Il semble trouver du réconfort dans son imaginaire ou ses jeux intérieurs.");

        public static ChenilleSegment NewPensee() => new(
            "pensee",
            "Pensée & organisation cognitive",
            "Penser, c'est apprendre à faire du tri dans ce qu'on vit.",
            "Mon enfant pose des questions sur le pourquoi des choses.",
            "Il comprend les consignes sans qu'il faille toujours les répéter.",
            "Il peut se concentrer quelques minutes sur une tâche sans se disperser.",
            "Il arrive à expliquer ce qu'il pense, même simplement.",
            "Il trouve des solutions à de petits problèmes du quotidien.",
            "Il s'adapte quand on change d'avis ou d'activité.");

        // ── Lexique d'interprétation clinique des couleurs (canonique) ──────────

        public static string LectureEmotionnelle(NiveauSegment? niveau) => niveau switch
        {
            NiveauSegment.VertFonce  => "Compétence intégrée — sécurité intérieure solide, lien stable, corps investi avec plaisir, langage fluide et relation bien installée.",
            NiveauSegment.VertClair  => "Base sécurisante présente — organisation cognitive fonctionnelle, corps actif et bien intégré, bonne capacité d'expression.",
            NiveauSegment.JauneClair => "Ambivalence — équilibre encore fragile, vérification fréquente du lien, début de verbalisation ou de jeu symbolique irrégulier.",
            NiveauSegment.JauneFonce => "Fragilité marquée — instabilité dans l'investissement corporel, difficulté à symboliser, émotions intenses difficilement canalisées.",
            NiveauSegment.RougeClair => "Anxiété et tension — communication détournée, corps souvent en retrait, blocages ou débordements fréquents constituant un appel au lien.",
            NiveauSegment.RougeFonce => "Difficulté majeure — insécurité affective marquée, isolement verbal, désorganisation cognitive, tempêtes émotionnelles ou repli persistant.",
            _                        => ""
        };

        public static string NiveauLabel(NiveauSegment? niveau) => niveau switch
        {
            NiveauSegment.VertFonce  => "Vert foncé",
            NiveauSegment.VertClair  => "Vert clair",
            NiveauSegment.JauneClair => "Jaune clair",
            NiveauSegment.JauneFonce => "Jaune foncé",
            NiveauSegment.RougeClair => "Rouge clair",
            NiveauSegment.RougeFonce => "Rouge foncé",
            _                        => "—"
        };

        /// <summary>Code couleur hex pour binding XAML (badge, fond, etc).</summary>
        public static string NiveauColor(NiveauSegment? niveau) => niveau switch
        {
            NiveauSegment.VertFonce  => "#27AE60",
            NiveauSegment.VertClair  => "#82E0AA",
            NiveauSegment.JauneClair => "#F9E79F",
            NiveauSegment.JauneFonce => "#F1C40F",
            NiveauSegment.RougeClair => "#F1948A",
            NiveauSegment.RougeFonce => "#C0392B",
            _                        => "#BDC3C7"
        };
    }
}
