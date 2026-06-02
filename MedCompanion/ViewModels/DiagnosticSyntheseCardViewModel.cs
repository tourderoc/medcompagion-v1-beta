using System;
using System.Collections.Generic;
using System.Linq;
using MedCompanion.Models.Evaluations;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// Bloc d'affichage d'un Bilan Final (issu d'une évaluation clôturée), destiné à
    /// s'ajouter sous la synthèse globale du patient dans le dossier bleu (onglet SYNTHESE).
    /// Présentation lecture seule — pour modifier, rouvrir l'évaluation depuis la frise.
    /// </summary>
    public class DiagnosticSyntheseCardViewModel
    {
        public string   FilePath          { get; }
        public DateTime DateCloture       { get; }
        public string   DateClotureText   => DateCloture.ToString("dd/MM/yyyy");

        public List<string>            DiagnosticsRetenus { get; }
        public List<string>            ElementsEnFaveur   { get; }
        public List<DiagnosticEcarteText> DiagnosticsEcartes { get; }
        public string                  CertitudeLabel     { get; }
        public string                  CertitudeColor     { get; }

        /// <summary>Paragraphe synthèse intégrative (Étape 5). Vide si non générée.</summary>
        public string SyntheseIntegrative   { get; }
        public bool   HasSyntheseIntegrative => !string.IsNullOrWhiteSpace(SyntheseIntegrative);

        public bool HasDiagnosticsRetenus => DiagnosticsRetenus.Count > 0;
        public bool HasElementsEnFaveur   => ElementsEnFaveur.Count > 0;
        public bool HasDiagnosticsEcartes => DiagnosticsEcartes.Count > 0;

        public DiagnosticSyntheseCardViewModel(EvaluationPhase phase)
        {
            FilePath    = phase.FilePath ?? "";
            DateCloture = phase.DateCloture ?? phase.DateDerniereModif;

            DiagnosticsRetenus = phase.BilanFinal.DiagnosticsRetenus
                .Select(s => s?.Value ?? "")
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            ElementsEnFaveur = phase.BilanFinal.ElementsEnFaveur
                .Select(s => s?.Value ?? "")
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .ToList();

            DiagnosticsEcartes = phase.BilanFinal.DiagnosticsEcartes
                .Where(e => e != null && !string.IsNullOrWhiteSpace(e.Label))
                .Select(e => new DiagnosticEcarteText(e.Label, e.Motif ?? ""))
                .ToList();

            (CertitudeLabel, CertitudeColor) = phase.BilanFinal.Certitude switch
            {
                NiveauCertitude.HypotheseAConfirmer => ("Hypothèse à confirmer", "#F39C12"),
                NiveauCertitude.Probable            => ("Probable",              "#3498DB"),
                NiveauCertitude.Certain             => ("Certain",               "#27AE60"),
                _                                   => ("Non renseigné",         "#95A5A6"),
            };

            SyntheseIntegrative = phase.BilanFinal.SyntheseIntegrative ?? "";
        }
    }

    /// <summary>Tuple lisible pour le binding XAML d'un diagnostic écarté (label + motif).</summary>
    public class DiagnosticEcarteText
    {
        public string Label { get; }
        public string Motif { get; }
        public bool   HasMotif => !string.IsNullOrWhiteSpace(Motif);
        public DiagnosticEcarteText(string label, string motif)
        {
            Label = label;
            Motif = motif;
        }
    }
}
