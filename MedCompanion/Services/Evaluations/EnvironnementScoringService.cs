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
        /// Couleur d'une nervure par pourcentage de complétion, uniforme quel que soit le max
        /// (3, 4 ou 5 items). Adaptation 5 niveaux décidée en session 2026-06-01.
        /// Seuils :
        ///   ≥ 90 % → VertFonce (Fluide)
        ///   ≥ 70 % → VertClair (Globalement fluide)
        ///   ≥ 50 % → Jaune     (Mitigé)
        ///   ≥ 30 % → Orange    (Fragile)
        ///   &lt; 30 % → Rouge     (Bloqué)
        /// Exemples sur centrale (5 items) : 5/5=VertFonce, 4/5=VertClair, 3/5=Jaune,
        /// 2/5=Orange, 1/5=Rouge, 0/5=Rouge.
        /// Exemples sur secondaire (3 items) : 3/3=VertFonce, 2/3=Jaune, 1/3=Orange, 0/3=Rouge.
        /// </summary>
        public static NiveauFeuille CalculerNervure(Nervure nervure)
        {
            if (nervure.MaxScore <= 0) return NiveauFeuille.Rouge;
            double pct = (double)nervure.Score / nervure.MaxScore;
            if (pct >= 0.90) return NiveauFeuille.VertFonce;
            if (pct >= 0.70) return NiveauFeuille.VertClair;
            if (pct >= 0.50) return NiveauFeuille.Jaune;
            if (pct >= 0.30) return NiveauFeuille.Orange;
            return NiveauFeuille.Rouge;
        }

        // ─── Feuille ──────────────────────────────────────────────────────────

        /// <summary>
        /// Couleur d'une feuille = pire couleur entre la nervure centrale et la pire des
        /// secondaires. Règle simplifiée et progressive sur 5 niveaux : la centrale n'a plus
        /// de priorité spéciale d'occultation, mais elle reste influente puisque les nervures
        /// participent à égalité au signal global. Cohérent avec la lecture clinique :
        /// si une dimension craque (centrale OU secondaire), la feuille reflète ce craquement.
        /// </summary>
        public static NiveauFeuille CalculerFeuille(FeuilleEnvironnement feuille)
        {
            var centrale = CalculerNervure(feuille.NervureCentrale);
            var pireSecondaire = feuille.NervuresSecondaires
                .Select(CalculerNervure)
                .DefaultIfEmpty(NiveauFeuille.VertFonce)
                .Max();   // enum croissant Vert→Rouge → Max = pire
            return centrale > pireSecondaire ? centrale : pireSecondaire;
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
