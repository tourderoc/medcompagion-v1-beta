using System;
using System.Collections.Generic;
using MedCompanion.Models.Evaluations;
using MedCompanion.Services.Evaluations;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// Bloc d'affichage d'une Cartographie de l'environnement (issue d'une évaluation
    /// clôturée), destiné à l'onglet BILANS du dossier bleu.
    /// Lecture seule : pour modifier, rouvrir l'évaluation depuis la frise.
    /// </summary>
    public class CartographieEnvironnementBilanCardViewModel
    {
        public string   FilePath        { get; }
        public DateTime DateCloture     { get; }
        public int?     AgeASaisie      { get; }
        public string   TitreCard       { get; }
        public string   SyntheseLabel   { get; }
        public string   SyntheseColor   { get; }

        public List<CartographieEnvironnementBilanLine> Feuilles { get; }

        public CartographieEnvironnementBilanCardViewModel(EvaluationPhase phase)
        {
            FilePath    = phase.FilePath ?? "";
            DateCloture = phase.DateCloture ?? phase.DateDerniereModif;
            AgeASaisie  = phase.CartographieEnvironnement.AgeAuMomentDeLaSaisie;

            var ageSuffix = AgeASaisie.HasValue ? $" ({AgeASaisie} ans)" : "";
            TitreCard = $"🌿 Cartographie environnement — {DateCloture:dd/MM/yyyy}{ageSuffix}";

            var hasAnyScore = HasAnyScore(phase.CartographieEnvironnement);
            var synth = EnvironnementScoringService.CalculerGlobal(phase.CartographieEnvironnement);
            SyntheseLabel = hasAnyScore
                ? CartographieEnvironnementContent.NiveauLabel(synth)
                : CartographieEnvironnementContent.NonEvalueLabel;
            SyntheseColor = hasAnyScore
                ? CartographieEnvironnementContent.NiveauColor(synth)
                : CartographieEnvironnementContent.NonEvalueColor;

            Feuilles = new List<CartographieEnvironnementBilanLine>
            {
                BuildLine(phase.CartographieEnvironnement.Famille),
                BuildLine(phase.CartographieEnvironnement.EcolePairs),
                BuildLine(phase.CartographieEnvironnement.EcransMedias),
                BuildLine(phase.CartographieEnvironnement.ValeursSocietales),
                BuildLine(phase.CartographieEnvironnement.CadreEducatif),
            };
        }

        private static bool HasAnyScore(CartographieEnvironnement c)
            => FeuilleHasScore(c.Famille)           || FeuilleHasScore(c.EcolePairs)
            || FeuilleHasScore(c.EcransMedias)      || FeuilleHasScore(c.ValeursSocietales)
            || FeuilleHasScore(c.CadreEducatif);

        private static bool FeuilleHasScore(FeuilleEnvironnement f)
        {
            if (f.NervureCentrale.Score > 0) return true;
            foreach (var s in f.NervuresSecondaires) if (s.Score > 0) return true;
            return false;
        }

        private static CartographieEnvironnementBilanLine BuildLine(FeuilleEnvironnement f)
        {
            var hasScore = FeuilleHasScore(f);
            var couleur = EnvironnementScoringService.CalculerFeuille(f);
            return new CartographieEnvironnementBilanLine
            {
                Label       = f.Label,
                SousTitre   = f.SousTitre,
                NiveauLabel = hasScore ? CartographieEnvironnementContent.NiveauLabel(couleur) : CartographieEnvironnementContent.NonEvalueLabel,
                NiveauColor = hasScore ? CartographieEnvironnementContent.NiveauColor(couleur) : CartographieEnvironnementContent.NonEvalueColor,
            };
        }
    }

    public class CartographieEnvironnementBilanLine
    {
        public string Label       { get; set; } = "";
        public string SousTitre   { get; set; } = "";
        public string NiveauLabel { get; set; } = "";
        public string NiveauColor { get; set; } = "";
    }
}
