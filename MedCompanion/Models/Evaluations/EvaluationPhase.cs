using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MedCompanion.Models.Evaluations
{
    public enum EvaluationStep { Preparation = 1, EvaluationCiblee = 2, Synthese = 3, Cartographie = 4 }

    public enum AxisExplorationState { NonAborde = 0, Partiel = 1, Evoque = 2 }

    public enum AxisCategory { Principal = 0, Differentiel = 1, Systemique = 2 }

    /// <summary>
    /// Une phase d'évaluation pédopsy pour un patient. Persistante sur plusieurs séances.
    /// Modèle "chapitre linéaire avec marque-page" : étape courante + flags de validation.
    /// </summary>
    public class EvaluationPhase
    {
        public string   PatientNomComplet   { get; set; } = "";
        public string   FilePath            { get; set; } = "";     // chemin du fichier .md persistant
        public DateTime DateDebut           { get; set; } = DateTime.Now;
        public DateTime DateDerniereModif   { get; set; } = DateTime.Now;
        public DateTime? DateCloture        { get; set; }            // null = active, sinon = clôturée immuable

        public EvaluationStep EtapeCourante { get; set; } = EvaluationStep.Preparation;

        public EvaluationPreparation Preparation     { get; set; } = new();
        public EvaluationCiblee      EvaluationCiblee { get; set; } = new();
        public SyntheseDiagnostique  Synthese         { get; set; } = new();
        // V0.3 ajoutera Cartographie enfant (3-11 ans)

        public bool IsActive                    => !DateCloture.HasValue;
        public bool IsPreparationValidated      => Preparation.IsValidated;
        public bool IsEvaluationCibleeValidated => EvaluationCiblee.IsValidated;
        public bool IsSyntheseValidated         => Synthese.IsValidated;
    }

    /// <summary>
    /// Données de l'Étape 1 — Préparation clinique.
    /// 5 catégories de listes éditables + date de validation médecin.
    /// </summary>
    public class EvaluationPreparation : INotifyPropertyChanged
    {
        public ObservableCollection<EditableString> HypothesesPrincipales { get; } = new();
        public ObservableCollection<EditableString> Differentiels         { get; } = new();
        public ObservableCollection<EditableString> AEliminer             { get; } = new();
        public ObservableCollection<EditableString> PointsVigilance       { get; } = new();
        public ObservableCollection<EditableString> QuestionsCliniques    { get; } = new();

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
}
