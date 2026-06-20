using System.ComponentModel;
using System.Runtime.CompilerServices;
using MedCompanion.Models.Evaluations;
using MedCompanion.Services.Evaluations;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// Wrapper bindable autour d'une Nervure (centrale ou secondaire) d'une feuille
    /// dimensionnelle. Expose la couleur calculée par EnvironnementScoringService et
    /// notifie le parent quand un item change (pour que la feuille puisse recalculer
    /// sa propre couleur globale).
    /// </summary>
    public class NervureViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public Nervure Modele { get; }

        public NervureViewModel(Nervure modele)
        {
            Modele = modele;
            Modele.PropertyChanged += OnModelChanged;
        }

        private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Nervure.Score))
            {
                OnPropertyChanged(nameof(Score));
                OnPropertyChanged(nameof(HasScore));
                OnPropertyChanged(nameof(Niveau));
                OnPropertyChanged(nameof(NiveauLabel));
                OnPropertyChanged(nameof(NiveauColor));
                OnPropertyChanged(nameof(DisplayLabel));
                OnPropertyChanged(nameof(DisplayColor));
                ScoreChanged?.Invoke();
            }
            else if (e.PropertyName == nameof(Nervure.AucunSigneNotable))
            {
                OnPropertyChanged(nameof(AucunSigneNotable));
                OnPropertyChanged(nameof(DisplayLabel));
                OnPropertyChanged(nameof(DisplayColor));
                ScoreChanged?.Invoke();
            }
        }

        /// <summary>Notifié à chaque coche/décoche ou changement AucunSigneNotable pour que la feuille parente recalcule.</summary>
        public event System.Action? ScoreChanged;

        // ── Passthrough ─────────────────────────────────────────────────────

        public string Label      => Modele.Label;
        public bool   IsCentrale => Modele.IsCentrale;
        public int    Score      => Modele.Score;
        public int    MaxScore   => Modele.MaxScore;

        /// <summary>Passthrough bindable pour la case "Rien de notable".</summary>
        public bool AucunSigneNotable
        {
            get => Modele.AucunSigneNotable;
            set => Modele.AucunSigneNotable = value;
        }

        // ── Calculés ────────────────────────────────────────────────────────

        /// <summary>True si au moins un item est coché — sinon la nervure est « non évaluée ».</summary>
        public bool HasScore => Modele.Score > 0;

        public NiveauFeuille Niveau      => EnvironnementScoringService.CalculerNervure(Modele);
        public string        NiveauLabel => CartographieEnvironnementContent.NiveauLabel(Niveau);
        public string        NiveauColor => CartographieEnvironnementContent.NiveauColor(Niveau);

        /// <summary>Libellé affiché : "Rien de notable" si coché, neutre si non évalué, sinon le niveau calculé.</summary>
        public string DisplayLabel => Modele.AucunSigneNotable && !HasScore
            ? "Rien de notable"
            : HasScore ? NiveauLabel : CartographieEnvironnementContent.NonEvalueLabel;

        /// <summary>Couleur affichée : bleu clair si "Rien de notable", grise si non évalué, sinon la couleur du niveau.</summary>
        public string DisplayColor => Modele.AucunSigneNotable && !HasScore
            ? "#5DADE2"
            : HasScore ? NiveauColor : CartographieEnvironnementContent.NonEvalueColor;
    }
}
