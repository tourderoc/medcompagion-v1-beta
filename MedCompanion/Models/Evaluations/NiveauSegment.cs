namespace MedCompanion.Models.Evaluations
{
    /// <summary>
    /// Niveau couleur d'un segment de la Chenille Universelle, après conversion
    /// score brut → couleur via la grille conditionnée par l'âge.
    /// Ordre : du meilleur (VertFonce) au plus préoccupant (RougeFonce).
    /// </summary>
    public enum NiveauSegment
    {
        VertFonce,
        VertClair,
        JauneClair,
        JauneFonce,
        RougeClair,
        RougeFonce
    }
}
