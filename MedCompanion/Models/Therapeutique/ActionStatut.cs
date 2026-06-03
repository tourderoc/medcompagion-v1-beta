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

    /// <summary>
    /// V1.2 — Statut diff d'une action au sein d'une proposition de patch incrémental
    /// (revue de v(N+1) qui hérite de v(N)).
    /// </summary>
    public enum ActionDiffStatut
    {
        /// <summary>Med ne propose pas de changement (action déjà bonne).</summary>
        Inchangee  = 0,
        /// <summary>Med propose une modification (nouveau libellé / description / indicateur).</summary>
        Modifiee   = 1,
        /// <summary>Action ajoutée par Med (n'existait pas en v(N)).</summary>
        Nouvelle   = 2,
        /// <summary>Med propose d'archiver / retirer cette action.</summary>
        AArchiver  = 3
    }
}
