using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MedCompanion.Models.Evaluations
{
    public enum NiveauCertitude
    {
        NonRenseigne        = 0,
        HypotheseAConfirmer = 1,
        Probable            = 2,
        Certain             = 3
    }

    /// <summary>
    /// Données de l'Étape 5 — Bilan Final (ex-Synthèse Diagnostique).
    /// Mise en cohérence du raisonnement APRÈS toutes les étapes : axes (Étape 2),
    /// cartographie enfant (Étape 3), cartographie environnement (Étape 4).
    /// Contient diagnostic(s) retenu(s), éléments en faveur, différentiels écartés,
    /// niveau de certitude et une synthèse intégrative libre (générée par Med +
    /// éditable) qui croise les 3 sources cliniques.
    /// </summary>
    public class BilanFinal : INotifyPropertyChanged
    {
        public ObservableCollection<EditableString>     DiagnosticsRetenus  { get; } = new();
        public ObservableCollection<EditableString>     ElementsEnFaveur    { get; } = new();
        public ObservableCollection<DiagnosticEcarte>   DiagnosticsEcartes  { get; } = new();

        private string? _syntheseIntegrative;
        /// <summary>
        /// Paragraphe synthétique généré par Med qui croise axes + cartographie enfant +
        /// cartographie environnement. Éditable manuellement par le psy après génération.
        /// </summary>
        public string? SyntheseIntegrative
        {
            get => _syntheseIntegrative;
            set
            {
                if (_syntheseIntegrative != value)
                {
                    _syntheseIntegrative = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasSyntheseIntegrative));
                }
            }
        }
        public bool HasSyntheseIntegrative => !string.IsNullOrWhiteSpace(_syntheseIntegrative);

        private DateTime? _syntheseIntegrativeDate;
        public DateTime? SyntheseIntegrativeDate
        {
            get => _syntheseIntegrativeDate;
            set { if (_syntheseIntegrativeDate != value) { _syntheseIntegrativeDate = value; OnPropertyChanged(); } }
        }

        private NiveauCertitude _certitude = NiveauCertitude.NonRenseigne;
        public NiveauCertitude Certitude
        {
            get => _certitude;
            set
            {
                if (_certitude != value)
                {
                    _certitude = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsHypotheseAConfirmer));
                    OnPropertyChanged(nameof(IsProbable));
                    OnPropertyChanged(nameof(IsCertain));
                }
            }
        }

        public bool IsHypotheseAConfirmer => Certitude == NiveauCertitude.HypotheseAConfirmer;
        public bool IsProbable            => Certitude == NiveauCertitude.Probable;
        public bool IsCertain             => Certitude == NiveauCertitude.Certain;

        private DateTime? _validationDate;
        public DateTime? ValidationDate
        {
            get => _validationDate;
            set
            {
                if (_validationDate != value)
                {
                    _validationDate = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsValidated));
                }
            }
        }
        public bool IsValidated => ValidationDate.HasValue;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? prop = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }

    /// <summary>
    /// Un diagnostic différentiel écarté, avec son motif d'élimination.
    /// </summary>
    public class DiagnosticEcarte : INotifyPropertyChanged
    {
        private string _label = "";
        public string Label
        {
            get => _label;
            set { if (_label != value) { _label = value ?? ""; OnPropertyChanged(); } }
        }

        private string _motif = "";
        public string Motif
        {
            get => _motif;
            set { if (_motif != value) { _motif = value ?? ""; OnPropertyChanged(); } }
        }

        public DiagnosticEcarte() { }
        public DiagnosticEcarte(string label, string motif) { _label = label ?? ""; _motif = motif ?? ""; }

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? prop = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
    }
}
