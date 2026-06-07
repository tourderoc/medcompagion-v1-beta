using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace MedCompanion.Models.Evaluations
{
    public enum EvaluationStep
    {
        Preparation               = 1,
        EvaluationCiblee          = 2,
        CartographieEnfant        = 3,
        CartographieEnvironnement = 4,
        BilanFinal                = 5
    }

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

        /// <summary>
        /// Liste des dates des séances pendant lesquelles le médecin a travaillé sur cette
        /// évaluation. Auto-alimentée à chaque Save (1 entrée par jour calendaire, sans
        /// doublon). Utilisé pour afficher "Dates d'évaluation" sur la couverture du
        /// Dossier de Restitution.
        /// </summary>
        public List<DateTime> SessionDates { get; set; } = new();

        /// <summary>
        /// Ajoute la date du jour (calendaire) aux SessionDates si elle n'y est pas déjà.
        /// Tri chronologique automatique. Appelé par EvaluationPhaseService.Save.
        /// </summary>
        public void RecordSessionDate(DateTime when)
        {
            var date = when.Date;
            if (SessionDates.Any(d => d.Date == date)) return;
            SessionDates.Add(date);
            SessionDates.Sort();
        }

        public EvaluationStep EtapeCourante { get; set; } = EvaluationStep.Preparation;

        public EvaluationPreparation      Preparation                { get; set; } = new();
        public EvaluationCiblee           EvaluationCiblee           { get; set; } = new();
        public CartographieEnfant         CartographieEnfant         { get; set; } = new();
        public CartographieEnvironnement  CartographieEnvironnement  { get; set; } = new();
        public BilanFinal                 BilanFinal                 { get; set; } = new();

        public bool IsActive                              => !DateCloture.HasValue;
        public bool IsPreparationValidated                => Preparation.IsValidated;
        public bool IsEvaluationCibleeValidated           => EvaluationCiblee.IsValidated;
        public bool IsCartographieEnfantValidated         => CartographieEnfant.IsValidated;
        public bool IsCartographieEnvironnementValidated  => CartographieEnvironnement.IsValidated;
        public bool IsBilanFinalValidated                 => BilanFinal.IsValidated;
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
