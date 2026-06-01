namespace MedCompanion.Models.Evaluations
{
    /// <summary>
    /// Niveau couleur d'une nervure ou d'une feuille de la Cartographie de l'environnement
    /// (Étape 5). 5 niveaux progressifs, calculés par pourcentage de complétion uniforme
    /// (peu importe le nombre d'items de la nervure). Adaptation du Tome 3 décidée en
    /// session 2026-06-01 : le système 3 niveaux d'origine était trop binaire (4/5 ou
    /// 3/5 tombaient tous deux en « Fragile »).
    ///
    /// Ordre : du meilleur (VertFonce) au plus préoccupant (Rouge).
    /// </summary>
    public enum NiveauFeuille
    {
        VertFonce  = 0,
        VertClair  = 1,
        Jaune      = 2,
        Orange     = 3,
        Rouge      = 4
    }
}
