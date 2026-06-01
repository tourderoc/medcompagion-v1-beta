using System.Linq;
using MedCompanion.Models.Evaluations;

namespace MedCompanion.Services.Evaluations
{
    /// <summary>
    /// Conversion score brut → niveau couleur pour la Cartographie de l'environnement
    /// (Étape 5). 100 % déterministe, transcription des seuils canoniques du Tome 3.
    ///
    /// Hiérarchie : nervure → feuille → synthèse globale.
    /// La nervure centrale a priorité pour déterminer la couleur de la feuille
    /// (règle explicite du Tome 3, §7).
    /// </summary>
    public static class EnvironnementScoringService
    {
        /// <summary>Borne basse (inclusive) de la tranche d'âge couverte.</summary>
        public const int AgeMin = 3;

        /// <summary>Borne haute (inclusive) de la tranche d'âge couverte.</summary>
        public const int AgeMax = 11;

        /// <summary>True si l'âge est dans la fourchette 3-11 où l'outil est applicable.</summary>
        public static bool IsApplicable(int? age)
            => age.HasValue && age.Value >= AgeMin && age.Value <= AgeMax;

        // ─── Nervure ──────────────────────────────────────────────────────────

        /// <summary>
        /// Couleur d'une nervure selon son score et son maximum.
        /// Centrale (max=5) : 5 → Vert ; 3-4 → Jaune ; &lt;3 → Rouge.
        /// Secondaire (max=3) : 3 → Vert ; 2 → Jaune ; &lt;2 → Rouge.
        /// Secondaire (max=4) : 4 → Vert ; 3 → Jaune ; &lt;3 → Rouge.
        /// </summary>
        public static NiveauFeuille CalculerNervure(Nervure nervure)
        {
            int score = nervure.Score;
            int max   = nervure.MaxScore;

            return max switch
            {
                5 => score >= 5 ? NiveauFeuille.Vert
                   : score >= 3 ? NiveauFeuille.Jaune
                   :              NiveauFeuille.Rouge,
                4 => score >= 4 ? NiveauFeuille.Vert
                   : score >= 3 ? NiveauFeuille.Jaune
                   :              NiveauFeuille.Rouge,
                _ => score >= 3 ? NiveauFeuille.Vert      // max == 3 par défaut
                   : score >= 2 ? NiveauFeuille.Jaune
                   :              NiveauFeuille.Rouge,
            };
        }

        // ─── Feuille ──────────────────────────────────────────────────────────

        /// <summary>
        /// Couleur d'une feuille selon les règles du Tome 3 :
        /// - Si la centrale est Rouge → feuille Rouge (priorité absolue).
        /// - Sinon, si la centrale est Vert ET toutes secondaires ≥ Jaune → feuille Vert.
        /// - Sinon → Jaune (centrale Jaune, ou centrale Vert mais une secondaire Rouge).
        /// </summary>
        public static NiveauFeuille CalculerFeuille(FeuilleEnvironnement feuille)
        {
            var centrale = CalculerNervure(feuille.NervureCentrale);
            if (centrale == NiveauFeuille.Rouge) return NiveauFeuille.Rouge;

            var pireSecondaire = feuille.NervuresSecondaires
                .Select(CalculerNervure)
                .DefaultIfEmpty(NiveauFeuille.Vert)
                .Max();   // enum Vert=0, Jaune=1, Rouge=2 → Max = pire

            if (centrale == NiveauFeuille.Vert && pireSecondaire != NiveauFeuille.Rouge)
                return NiveauFeuille.Vert;

            return NiveauFeuille.Jaune;
        }

        // ─── Synthèse globale ─────────────────────────────────────────────────

        /// <summary>
        /// Synthèse globale de l'étape 5 : pire couleur parmi les 5 feuilles
        /// (logique « feuille trouée » du Tome 3, §7).
        /// </summary>
        public static NiveauFeuille CalculerGlobal(CartographieEnvironnement carto)
        {
            var couleurs = new[]
            {
                CalculerFeuille(carto.Famille),
                CalculerFeuille(carto.EcolePairs),
                CalculerFeuille(carto.EcransMedias),
                CalculerFeuille(carto.ValeursSocietales),
                CalculerFeuille(carto.CadreEducatif),
            };
            return couleurs.Max();
        }
    }
}
