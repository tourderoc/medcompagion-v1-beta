using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace MedCompanion.ViewModels
{
    public enum FriseStageStatus
    {
        Locked,     // étape précédente non validée
        Available,  // accessible, pas encore commencée
        InProgress, // brouillon actif
        Completed   // validée / clôturée
    }

    public class FriseStageViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public string Key        { get; init; } = "";
        public string Label      { get; init; } = "";
        public string Icon       { get; init; } = "";
        public bool   ShowArrow  { get; set; } = true;   // false pour le dernier jalon

        private FriseStageStatus _status = FriseStageStatus.Locked;
        public FriseStageStatus Status
        {
            get => _status;
            set
            {
                if (_status == value) return;
                _status = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(StatusText));
                OnPropertyChanged(nameof(IsClickable));
            }
        }

        private DateTime? _date;
        public DateTime? Date
        {
            get => _date;
            set { _date = value; OnPropertyChanged(); OnPropertyChanged(nameof(StatusText)); }
        }

        public string StatusText => Status switch
        {
            FriseStageStatus.Locked     => "À venir",
            FriseStageStatus.Available  => "Disponible",
            FriseStageStatus.InProgress => _date.HasValue ? $"En cours · {_date.Value:dd/MM}" : "En cours",
            FriseStageStatus.Completed  => _date.HasValue ? $"Clôturée · {_date.Value:dd/MM}" : "Clôturée",
            _                           => ""
        };

        public bool IsClickable => Status != FriseStageStatus.Locked;

        public ICommand? ActivateCommand { get; set; }
    }
}
