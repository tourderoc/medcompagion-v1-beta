namespace MedCompanion.Models.Synthesis
{
    /// <summary>
    /// Statut diff d'une section au sein d'une proposition de mise à jour incrémentale
    /// de la Synthèse Globale (v(n) → v(n+1)).
    ///
    /// - Inchangee : Med n'a pas modifié cette section, on garde le contenu de v(n).
    /// - Modifiee  : Med a retouché — affichage orange, contenu nouveau, diff visible.
    /// - Nouvelle  : section vide dans v(n), Med propose un contenu — affichage vert.
    /// - Supprimee : Med propose de retirer la section (rare) — affichage rouge barré.
    ///
    /// Pour une version VALIDÉE (figée), toutes les sections sont en statut Inchangee.
    /// </summary>
    public enum SectionUpdateStatus
    {
        Inchangee = 0,
        Modifiee  = 1,
        Nouvelle  = 2,
        Supprimee = 3
    }
}
