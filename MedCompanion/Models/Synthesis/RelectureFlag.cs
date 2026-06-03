namespace MedCompanion.Models.Synthesis
{
    /// <summary>
    /// Type de signalement émis par la relecture critique de Med (V0.5).
    /// </summary>
    public enum FlagType
    {
        /// <summary>Deux sections se contredisent (ex: hypothèses dit X, conclusion dit Y).</summary>
        Contradiction,
        /// <summary>Affirmation sans appui dans le dossier source (risque d'hallucination).</summary>
        NonSource,
        /// <summary>Section trop courte ou trop lacunaire vs ce que les sources permettent.</summary>
        Incomplete,
        /// <summary>Suggestion d'amélioration éditoriale (ton, structure).</summary>
        Suggestion
    }

    /// <summary>
    /// Sévérité d'un flag de relecture critique. Le bouton "Valider" peut être bloqué tant
    /// qu'il reste des Critiques non traités (selon configuration).
    /// </summary>
    public enum FlagSeverite
    {
        /// <summary>À traiter impérativement avant validation (contradiction majeure).</summary>
        Critique,
        /// <summary>À examiner mais non bloquant (affirmation à vérifier).</summary>
        Moyenne,
        /// <summary>Information / amélioration possible.</summary>
        Mineure
    }

    /// <summary>
    /// Un flag de relecture critique produit par Med sur une version brouillon de la
    /// Synthèse Globale. Chaque flag est affiché à côté de la section concernée (chip)
    /// et listé dans un panneau récap. Le psy peut le marquer comme traité.
    /// </summary>
    public class RelectureFlag
    {
        /// <summary>Clé de la section principale concernée (hypotheses, enfant, …).</summary>
        public string SectionCle { get; set; } = "";

        /// <summary>Clé d'une seconde section si le flag est une contradiction entre deux.</summary>
        public string? SectionCleSecondaire { get; set; }

        public FlagType     Type     { get; set; } = FlagType.Suggestion;
        public FlagSeverite Severite { get; set; } = FlagSeverite.Mineure;

        /// <summary>Description courte du problème (1-2 phrases).</summary>
        public string Detail { get; set; } = "";

        /// <summary>Suggestion concrète de correction (optionnelle).</summary>
        public string Suggestion { get; set; } = "";

        /// <summary>True si le psy a marqué ce flag comme traité (lu, corrigé ou ignoré).</summary>
        public bool Traite { get; set; }

        public string SeveriteLabel => Severite switch
        {
            FlagSeverite.Critique => "Critique",
            FlagSeverite.Moyenne  => "Moyenne",
            _                     => "Mineure"
        };

        public string TypeLabel => Type switch
        {
            FlagType.Contradiction => "Contradiction",
            FlagType.NonSource     => "Non sourcé",
            FlagType.Incomplete    => "Incomplet",
            _                      => "Suggestion"
        };

        /// <summary>Code couleur hex pour binding XAML (chip / bordure).</summary>
        public string SeveriteColor => Severite switch
        {
            FlagSeverite.Critique => "#C0392B",
            FlagSeverite.Moyenne  => "#E67E22",
            _                     => "#7F8C8D"
        };

        /// <summary>Icône emoji pour la sévérité.</summary>
        public string SeveriteIcon => Severite switch
        {
            FlagSeverite.Critique => "🔴",
            FlagSeverite.Moyenne  => "🟠",
            _                     => "🟡"
        };
    }
}
