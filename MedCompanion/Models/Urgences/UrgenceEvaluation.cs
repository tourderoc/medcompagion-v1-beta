using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MedCompanion.Models.Urgences
{
    public enum UrgenceRiskLevel { NonRenseigne, Faible, Modere, Eleve }

    /// <summary>
    /// Une option de réponse (radio) pour une question d'une section d'évaluation.
    /// </summary>
    public class UrgenceChoice
    {
        public string Code  { get; set; } = ""; // valeur stockée (ex: "precise")
        public string Label { get; set; } = ""; // texte affiché (ex: "Précise")
    }

    /// <summary>
    /// Une section du questionnaire (idéation, intentionnalité, scénario, ...).
    /// Contient une question principale (radio) + un champ texte libre pour précisions cliniques.
    /// </summary>
    public class UrgenceEvaluationSection : INotifyPropertyChanged
    {
        public string Key         { get; set; } = "";    // identifiant stable (ex: "ideation_suicidaire")
        public string Title       { get; set; } = "";
        public string HelpText    { get; set; } = "";    // courte aide contextuelle adaptée à l'âge
        public List<UrgenceChoice> Choices { get; set; } = new();

        private string? _selectedChoiceCode;
        public string? SelectedChoiceCode
        {
            get => _selectedChoiceCode;
            set => SetProperty(ref _selectedChoiceCode, value);
        }

        private string _freeText = "";
        public string FreeText
        {
            get => _freeText;
            set => SetProperty(ref _freeText, value);
        }

        private bool _isExpanded = true;
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void SetProperty<T>(ref T field, T value, [CallerMemberName] string? prop = null)
        {
            if (Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        }
    }

    /// <summary>
    /// Une case à cocher du plan d'action (Information parents, Contrat sécurité, ...).
    /// </summary>
    public class UrgenceActionItem : INotifyPropertyChanged
    {
        public string Key   { get; set; } = "";
        public string Label { get; set; } = "";

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked != value) { _isChecked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); } }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
