using System;
using System.Collections.Generic;
using System.Linq;
using MedCompanion.Models.Evaluations;
using MedCompanion.Services.Evaluations;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// Bloc d'affichage d'une Cartographie de l'enfant (issue d'une évaluation clôturée),
    /// destiné à l'onglet BILANS du dossier bleu.
    /// Lecture seule : pour modifier, rouvrir l'évaluation depuis la frise.
    /// </summary>
    public class CartographieBilanCardViewModel
    {
        public string   FilePath     { get; }
        public DateTime DateCloture  { get; }
        public int?     AgeASaisie   { get; }
        public string   TitreCard    { get; }   // "🐛 Cartographie — 30/05/2026 (7 ans)"

        public List<CartographieBilanSegmentLine> Segments { get; }
        public List<CartographieBilanTemperamentLine> TemperamentLignes { get; }

        public bool HasTemperament { get; }

        public CartographieBilanCardViewModel(EvaluationPhase phase)
        {
            FilePath    = phase.FilePath ?? "";
            DateCloture = phase.DateCloture ?? phase.DateDerniereModif;
            AgeASaisie  = phase.CartographieEnfant.AgeAuMomentDeLaSaisie;

            var ageSuffix = AgeASaisie.HasValue ? $" ({AgeASaisie} ans)" : "";
            TitreCard = $"🐛 Cartographie — {DateCloture:dd/MM/yyyy}{ageSuffix}";

            Segments = new List<CartographieBilanSegmentLine>
            {
                BuildLine(phase.CartographieEnfant.Attachement,     AgeASaisie),
                BuildLine(phase.CartographieEnfant.Psychomotricite, AgeASaisie),
                BuildLine(phase.CartographieEnfant.Langage,         AgeASaisie),
                BuildLine(phase.CartographieEnfant.Emotions,        AgeASaisie),
                BuildLine(phase.CartographieEnfant.Imaginaire,      AgeASaisie),
                BuildLine(phase.CartographieEnfant.Pensee,          AgeASaisie),
            };

            var t = phase.CartographieEnfant.Temperament;
            HasTemperament = t.IsRenseigne;
            TemperamentLignes = new List<CartographieBilanTemperamentLine>
            {
                new("Niveau d'activité",       t.NiveauActivite),
                new("Rythme / Régularité",     t.Regularite),
                new("Réactivité sensorielle",  t.ReactiviteSensorielle),
                new("Intensité émotionnelle",  t.IntensiteEmotionnelle),
                new("Adaptabilité",            t.Adaptabilite),
                new("Temps de réaction",       t.TempsDeReaction),
            };
        }

        private static CartographieBilanSegmentLine BuildLine(ChenilleSegment segment, int? age)
        {
            var niveau = CartographieScoringService.Calculer(segment.Score, age);
            return new CartographieBilanSegmentLine
            {
                Label       = segment.Label,
                Score       = segment.Score,
                Niveau      = niveau,
                NiveauLabel = CartographieContent.NiveauLabel(niveau),
                NiveauColor = CartographieContent.NiveauColor(niveau),
            };
        }
    }

    public class CartographieBilanSegmentLine
    {
        public string         Label       { get; set; } = "";
        public int            Score       { get; set; }
        public NiveauSegment? Niveau      { get; set; }
        public string         NiveauLabel { get; set; } = "";
        public string         NiveauColor { get; set; } = "";
        public bool           HasNiveau   => Niveau.HasValue;
    }

    public class CartographieBilanTemperamentLine
    {
        public string Label { get; }
        public int    Value { get; }
        public CartographieBilanTemperamentLine(string label, int value) { Label = label; Value = value; }
    }
}
