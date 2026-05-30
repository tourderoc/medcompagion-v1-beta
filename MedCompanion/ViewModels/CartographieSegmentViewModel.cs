using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MedCompanion.Models.Evaluations;
using MedCompanion.Services.Evaluations;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// Wrapper bindable autour d'un ChenilleSegment + âge patient.
    /// Expose les valeurs dérivées (Niveau, couleur, lecture émotionnelle) qui se
    /// recalculent automatiquement quand les items du segment changent ou quand l'âge change.
    /// </summary>
    public class CartographieSegmentViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        private readonly Func<int?> _ageProvider;

        public ChenilleSegment Segment { get; }

        public CartographieSegmentViewModel(ChenilleSegment segment, Func<int?> ageProvider)
        {
            Segment      = segment;
            _ageProvider = ageProvider;
            Segment.PropertyChanged += OnSegmentChanged;
        }

        private void OnSegmentChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChenilleSegment.Score))
            {
                OnPropertyChanged(nameof(Score));
                NotifyComputed();
            }
        }

        /// <summary>Appelé par le ViewModel parent quand l'âge devient connu/change.</summary>
        public void NotifyAgeChanged() => NotifyComputed();

        private void NotifyComputed()
        {
            OnPropertyChanged(nameof(Niveau));
            OnPropertyChanged(nameof(NiveauLabel));
            OnPropertyChanged(nameof(NiveauColor));
            OnPropertyChanged(nameof(LectureEmotionnelle));
            OnPropertyChanged(nameof(HasNiveau));
        }

        // ── Passthrough ─────────────────────────────────────────────────────

        public string Label          => Segment.Label;
        public string PhraseBoussole => Segment.PhraseBoussole;
        public int    Score          => Segment.Score;

        // ── Calculés ────────────────────────────────────────────────────────

        public NiveauSegment? Niveau
            => CartographieScoringService.Calculer(Segment.Score, _ageProvider());

        public bool HasNiveau => Niveau.HasValue;

        public string NiveauLabel        => CartographieContent.NiveauLabel(Niveau);
        public string NiveauColor        => CartographieContent.NiveauColor(Niveau);
        public string LectureEmotionnelle => CartographieContent.LectureEmotionnelle(Niveau);
    }
}
