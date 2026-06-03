namespace MedCompanion.Models.Therapeutique
{
    /// <summary>
    /// Statut d'une action / objectif du Projet Thérapeutique. Pilier pour Med :
    /// alimente le suivi, les suggestions de transitions, et la mesure de progression.
    /// </summary>
    public enum ActionStatut
    {
        /// <summary>Décidée mais pas démarrée (RDV à prendre, ordonnance à rédiger).</summary>
        AVenir     = 0,
        /// <summary>Action en cours (traitement démarré, suivi orthophonie actif).</summary>
        EnCours    = 1,
        /// <summary>Action accomplie (examen passé, objectif atteint).</summary>
        Fait       = 2,
        /// <summary>Décision d'arrêt (intolérance, refus famille, devenu non pertinent).</summary>
        Abandonne  = 3
    }

    /// <summary>
    /// Statut global du document Projet Thérapeutique.
    /// </summary>
    public enum ProjetStatut
    {
        Brouillon = 0,
        Validee   = 1
    }
}
