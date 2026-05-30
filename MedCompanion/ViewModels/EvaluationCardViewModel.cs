using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MedCompanion.Models.Evaluations;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// Carte d'évaluation pour la frise chronologique.
    /// Présente les mêmes "shape properties" que ConsultationCardViewModel
    /// (Icon, Type, DateText, IsActive) pour que la frise puisse les afficher dans le même ItemsControl.
    /// </summary>
    public class EvaluationCardViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public string   FilePath        { get; }
        public DateTime Date            { get; }    // = DateDebut, pour l'ordre chronologique
        public DateTime? DateCloture    { get; }
        public EvaluationStep EtapeCourante { get; }
        public string   Type            { get; } = "Évaluation";
        public string   Icon            { get; } = "🧠";

        public bool IsActive => !DateCloture.HasValue;
        public bool IsClosed =>  DateCloture.HasValue;

        public string DateText => Date == DateTime.MinValue ? "" : Date.ToString("dd/MM/yyyy");

        /// <summary>
        /// Sous-titre court : "En cours · Étape 2" ou "Clôturée 30/05".
        /// </summary>
        public string StateText
        {
            get
            {
                if (IsClosed)
                    return $"Clôturée {DateCloture!.Value:dd/MM}";
                var step = EtapeCourante switch
                {
                    EvaluationStep.Preparation      => "Étape 1",
                    EvaluationStep.EvaluationCiblee => "Étape 2",
                    EvaluationStep.Synthese         => "Étape 3",
                    EvaluationStep.CartographieEnfant         => "Étape 4",
                    EvaluationStep.CartographieEnvironnement  => "Étape 5",
                    _                               => ""
                };
                return $"En cours · {step}";
            }
        }

        public EvaluationCardViewModel(EvaluationPhase phase)
        {
            FilePath      = phase.FilePath ?? "";
            Date          = phase.DateDebut;
            DateCloture   = phase.DateCloture;
            EtapeCourante = phase.EtapeCourante;
        }
    }
}
