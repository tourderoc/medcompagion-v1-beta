using System.Collections.Generic;
using MedCompanion.Models.Evaluations;

namespace MedCompanion.Services.Evaluations
{
    /// <summary>
    /// Conversion score brut → niveau couleur pour la Chenille Universelle (3-11 ans).
    /// 100 % déterministe : UNE seule grille par tranche d'âge, applicable identiquement
    /// aux 6 segments de questionnaire (Attachement, Psychomotricité, Langage, Émotions,
    /// Imaginaire, Pensée). Source canonique : GRILLES_CARTOGRAPHIE_CANONIQUES.md.
    ///
    /// Le segment Tempérament n'utilise pas ce service (pas de score, pas de couleur).
    /// </summary>
    public static class CartographieScoringService
    {
        /// <summary>
        /// Borne basse (inclusive) de la tranche d'âge couverte par l'outil.
        /// </summary>
        public const int AgeMin = 3;

        /// <summary>
        /// Borne haute (inclusive) de la tranche d'âge couverte par l'outil.
        /// </summary>
        public const int AgeMax = 11;

        /// <summary>
        /// True si l'âge est dans la fourchette 3-11 où l'outil est applicable.
        /// </summary>
        public static bool IsApplicable(int? age)
            => age.HasValue && age.Value >= AgeMin && age.Value <= AgeMax;

        /// <summary>
        /// Convertit un score brut (0-6) en niveau couleur selon l'âge.
        /// Retourne null si l'âge est hors fourchette ou si le score est invalide.
        /// </summary>
        public static NiveauSegment? Calculer(int score, int? age)
        {
            if (!IsApplicable(age)) return null;
            if (score < 0 || score > 6) return null;
            return Grille[ChoisirBande(age!.Value)][score];
        }

        private enum BandeAge { ThreeFour, FiveSix, SevenNine, TenEleven }

        private static BandeAge ChoisirBande(int age) => age switch
        {
            <= 4 => BandeAge.ThreeFour,
            <= 6 => BandeAge.FiveSix,
            <= 9 => BandeAge.SevenNine,
            _    => BandeAge.TenEleven
        };

        // Indice du tableau = score (0..6). Tables transcrites de
        // GRILLES_CARTOGRAPHIE_CANONIQUES.md section 3.
        private static readonly Dictionary<BandeAge, NiveauSegment[]> Grille = new()
        {
            [BandeAge.ThreeFour] = new[]
            {
                /* 0 */ NiveauSegment.RougeFonce,
                /* 1 */ NiveauSegment.RougeClair,
                /* 2 */ NiveauSegment.JauneFonce,
                /* 3 */ NiveauSegment.JauneClair,
                /* 4 */ NiveauSegment.VertClair,
                /* 5 */ NiveauSegment.VertClair,
                /* 6 */ NiveauSegment.VertFonce,
            },
            [BandeAge.FiveSix] = new[]
            {
                /* 0 */ NiveauSegment.RougeFonce,
                /* 1 */ NiveauSegment.RougeFonce,
                /* 2 */ NiveauSegment.RougeClair,
                /* 3 */ NiveauSegment.JauneFonce,
                /* 4 */ NiveauSegment.JauneClair,
                /* 5 */ NiveauSegment.VertClair,
                /* 6 */ NiveauSegment.VertFonce,
            },
            [BandeAge.SevenNine] = new[]
            {
                /* 0 */ NiveauSegment.RougeFonce,
                /* 1 */ NiveauSegment.RougeFonce,
                /* 2 */ NiveauSegment.RougeFonce,
                /* 3 */ NiveauSegment.RougeFonce,
                /* 4 */ NiveauSegment.JauneClair,
                /* 5 */ NiveauSegment.VertClair,
                /* 6 */ NiveauSegment.VertFonce,
            },
            [BandeAge.TenEleven] = new[]
            {
                /* 0 */ NiveauSegment.RougeFonce,
                /* 1 */ NiveauSegment.RougeFonce,
                /* 2 */ NiveauSegment.RougeFonce,
                /* 3 */ NiveauSegment.RougeFonce,
                /* 4 */ NiveauSegment.RougeFonce,
                /* 5 */ NiveauSegment.JauneFonce,
                /* 6 */ NiveauSegment.VertFonce,
            },
        };
    }
}
