using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MedCompanion.Models.Therapeutique;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// Carte de Projet Thérapeutique pour la frise chronologique.
    /// Mêmes shape-properties que les autres cards (Icon/Type/DateText/StateText/IsActive).
    /// </summary>
    public class ProjetTherapeutiqueCardViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public string   FilePath       { get; }
        public DateTime Date           { get; }
        public DateTime? DateValidation { get; }
        public int      Version        { get; }
        public ProjetStatut Statut     { get; }
        public DateTime? DateReevaluationPrevue { get; }

        public string Type { get; } = "Projet";
        public string Icon { get; } = "🎯";

        public bool IsActive => Statut == ProjetStatut.Brouillon;
        public bool IsValidee => Statut == ProjetStatut.Validee;
        public bool IsReevaluationPassee
            => DateReevaluationPrevue.HasValue && DateReevaluationPrevue.Value.Date < DateTime.Now.Date;

        public string DateText => Date == DateTime.MinValue ? "" : Date.ToString("dd/MM/yyyy");

        public string StateText
            => IsValidee && DateValidation.HasValue
                ? $"Validé v{Version} · {DateValidation.Value:dd/MM}"
                : $"Brouillon v{Version}";

        public ProjetTherapeutiqueCardViewModel(ProjetTherapeutiqueVersion v)
        {
            FilePath       = v.FilePath;
            Date           = v.DateRedaction;
            DateValidation = v.DateValidation;
            Version        = v.Version;
            Statut         = v.Statut;
            DateReevaluationPrevue = v.DateReevaluationPrevue;
        }
    }
}
