using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MedCompanion.Models.Synthesis;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// Carte de Synthèse Globale pour la frise chronologique du patient.
    /// Présente les mêmes "shape properties" que ConsultationCardViewModel/EvaluationCardViewModel
    /// (Icon, Type, DateText, IsActive) pour que la frise puisse les afficher dans le même ItemsControl.
    /// </summary>
    public class SyntheseGlobaleCardViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public string   FilePath       { get; }
        public DateTime Date           { get; }    // = DateRedaction pour le tri chronologique
        public DateTime? DateValidation { get; }
        public int      Version        { get; }
        public SyntheseStatut Statut   { get; }

        public string Type { get; } = "Synthèse";
        public string Icon { get; } = "🧭";

        public bool IsActive => Statut == SyntheseStatut.Brouillon;   // ≈ "en cours"
        public bool IsValidee => Statut == SyntheseStatut.Validee;

        public string DateText => Date == DateTime.MinValue ? "" : Date.ToString("dd/MM/yyyy");

        /// <summary>Sous-titre court : "Brouillon v2" ou "Validée v1 · 02/06".</summary>
        public string StateText
            => IsValidee && DateValidation.HasValue
                ? $"Validée v{Version} · {DateValidation.Value:dd/MM}"
                : $"Brouillon v{Version}";

        public SyntheseGlobaleCardViewModel(SyntheseGlobaleVersion v)
        {
            FilePath       = v.FilePath;
            Date           = v.DateRedaction;
            DateValidation = v.DateValidation;
            Version        = v.Version;
            Statut         = v.Statut;
        }
    }
}
