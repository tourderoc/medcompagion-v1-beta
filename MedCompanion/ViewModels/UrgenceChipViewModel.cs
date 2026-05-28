using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Models.Urgences;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// État du chip "signal d'urgence détecté" affiché en haut de la zone Note.
    /// Trois actions au médecin : Ouvrir évaluation / Voir passages / Écarter.
    /// </summary>
    public class UrgenceChipViewModel : INotifyPropertyChanged
    {
        public UrgenceSignal Signal { get; }

        public string HeaderText { get; }

        public ObservableCollection<string> Passages { get; } = new();

        public ICommand OpenEvaluationCommand { get; }
        public ICommand ShowPassagesCommand   { get; }
        public ICommand DismissCommand        { get; }
        public ICommand CloseShowPassagesCommand { get; }

        private bool _showPassagesPopupOpen;
        public bool ShowPassagesPopupOpen
        {
            get => _showPassagesPopupOpen;
            set => SetProperty(ref _showPassagesPopupOpen, value);
        }

        private bool _isVisible = true;
        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        public UrgenceChipViewModel(
            UrgenceSignal signal,
            Action onOpenEvaluation,
            Action<string> onDismiss)
        {
            Signal     = signal;
            HeaderText = BuildHeader(signal.Type);

            foreach (var p in signal.Passages) Passages.Add(p);

            OpenEvaluationCommand    = new RelayCommand(_ => { onOpenEvaluation(); IsVisible = false; });
            ShowPassagesCommand      = new RelayCommand(_ => ShowPassagesPopupOpen = !ShowPassagesPopupOpen);
            CloseShowPassagesCommand = new RelayCommand(_ => ShowPassagesPopupOpen = false);
            DismissCommand           = new RelayCommand(motif =>
            {
                onDismiss(motif as string ?? "");
                IsVisible = false;
            });
        }

        private static string BuildHeader(string urgenceType) => urgenceType switch
        {
            "risque_suicidaire" => "Signal d'alerte détecté — évaluer le risque suicidaire ?",
            _                   => $"Signal d'urgence détecté ({urgenceType})"
        };

        public event PropertyChangedEventHandler? PropertyChanged;
        private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? prop = null)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }
    }
}
