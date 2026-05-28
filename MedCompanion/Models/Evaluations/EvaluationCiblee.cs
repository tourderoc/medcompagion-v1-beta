using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MedCompanion.Models.Evaluations
{
    /// <summary>
    /// Données de l'Étape 2 — Évaluation ciblée.
    /// 3 catégories d'axes (principaux / différentiels / systémiques), exploration libre par axe.
    /// </summary>
    public class EvaluationCiblee : INotifyPropertyChanged
    {
        public ObservableCollection<EvaluationAxis> AxesPrincipaux     { get; } = new();
        public ObservableCollection<EvaluationAxis> AxesDifferentiels  { get; } = new();
        public ObservableCollection<EvaluationAxis> AxesSystemiques    { get; } = new();

        private DateTime? _validationDate;
        public DateTime? ValidationDate
        {
            get => _validationDate;
            set
            {
                if (_validationDate != value)
                {
                    _validationDate = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ValidationDate)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsValidated)));
                }
            }
        }
        public bool IsValidated => ValidationDate.HasValue;

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    /// <summary>
    /// Un axe d'attention clinique à explorer (ex: "Attention", "Anxiété", "Écrans").
    /// État visuel + observation narrative libre du clinicien + questions ouvertes proposées.
    /// </summary>
    public class EvaluationAxis : INotifyPropertyChanged
    {
        public string Label         { get; set; } = "";   // ex: "Attention"
        public string Justification { get; set; } = "";   // courte phrase clinique (proposée par LLM)
        public AxisCategory Category { get; set; }        // pour persistance

        private AxisExplorationState _state = AxisExplorationState.NonAborde;
        public AxisExplorationState State
        {
            get => _state;
            set
            {
                if (_state != value)
                {
                    _state = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsNonAborde));
                    OnPropertyChanged(nameof(IsPartiel));
                    OnPropertyChanged(nameof(IsEvoque));
                }
            }
        }

        public bool IsNonAborde => State == AxisExplorationState.NonAborde;
        public bool IsPartiel   => State == AxisExplorationState.Partiel;
        public bool IsEvoque    => State == AxisExplorationState.Evoque;

        private string _observation = "";
        public string Observation
        {
            get => _observation;
            set
            {
                if (_observation != value)
                {
                    _observation = value ?? "";
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Texte rédigé par Med en attente de validation par le médecin (après une dicte).
        /// Affiché dans un encart distinct avec boutons ✓ Accepter / ✖ Ignorer.
        /// Vide tant que Med n'a rien à proposer pour cet axe.
        /// </summary>
        private string _pendingMedText = "";
        public string PendingMedText
        {
            get => _pendingMedText;
            set
            {
                if (_pendingMedText != value)
                {
                    _pendingMedText = value ?? "";
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasPendingMedText));
                }
            }
        }
        public bool HasPendingMedText => !string.IsNullOrWhiteSpace(_pendingMedText);

        public ObservableCollection<EditableString>      SuggestedQuestions    { get; } = new();
        public ObservableCollection<AxisObservationItem> ObservationsProposees { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? prop = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    /// <summary>
    /// Observation clinique proposée par le LLM, cochable par le médecin.
    /// Complète le texte narratif libre (ne le remplace pas).
    /// </summary>
    public class AxisObservationItem : INotifyPropertyChanged
    {
        public string Label { get; set; } = "";

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked != value)
                {
                    _isChecked = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
