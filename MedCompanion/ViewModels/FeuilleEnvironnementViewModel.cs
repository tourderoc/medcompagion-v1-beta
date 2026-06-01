using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MedCompanion.Models.Evaluations;
using MedCompanion.Services.Evaluations;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// Wrapper bindable autour d'une FeuilleEnvironnement. Expose la couleur globale
    /// de la feuille (calculée selon la règle « centrale prioritaire » du Tome 3) et
    /// notifie le VM parent à chaque changement pour recalcul de la synthèse globale.
    /// </summary>
    public class FeuilleEnvironnementViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public FeuilleEnvironnement Modele { get; }

        public NervureViewModel Centrale { get; }
        public ObservableCollection<NervureViewModel> Secondaires { get; } = new();

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(); } }
        }

        public FeuilleEnvironnementViewModel(FeuilleEnvironnement modele)
        {
            Modele = modele;

            Centrale = new NervureViewModel(modele.NervureCentrale);
            Centrale.ScoreChanged += NotifyCouleur;

            foreach (var n in modele.NervuresSecondaires)
            {
                var vm = new NervureViewModel(n);
                vm.ScoreChanged += NotifyCouleur;
                Secondaires.Add(vm);
            }
        }

        private void NotifyCouleur()
        {
            OnPropertyChanged(nameof(HasAnyScore));
            OnPropertyChanged(nameof(Couleur));
            OnPropertyChanged(nameof(NiveauLabel));
            OnPropertyChanged(nameof(NiveauColor));
            OnPropertyChanged(nameof(NiveauDescription));
            OnPropertyChanged(nameof(DisplayLabel));
            OnPropertyChanged(nameof(DisplayColor));
            OnPropertyChanged(nameof(DisplayDescription));
            CouleurChanged?.Invoke();
        }

        /// <summary>Notifié à chaque changement de couleur de la feuille (pour synthèse globale).</summary>
        public event System.Action? CouleurChanged;

        // ── Passthrough ─────────────────────────────────────────────────────

        public string Label     => Modele.Label;
        public string SousTitre => Modele.SousTitre;

        // ── Calculés ────────────────────────────────────────────────────────

        /// <summary>True si au moins un item de la feuille a été coché — sinon feuille « non évaluée ».</summary>
        public bool HasAnyScore
        {
            get
            {
                if (Centrale.HasScore) return true;
                foreach (var s in Secondaires) if (s.HasScore) return true;
                return false;
            }
        }

        public NiveauFeuille Couleur           => EnvironnementScoringService.CalculerFeuille(Modele);
        public string        NiveauLabel       => CartographieEnvironnementContent.NiveauLabel(Couleur);
        public string        NiveauColor       => CartographieEnvironnementContent.NiveauColor(Couleur);
        public string        NiveauDescription => CartographieEnvironnementContent.NiveauDescription(Couleur);

        /// <summary>Libellé affiché : neutre tant qu'aucun item de la feuille n'est coché.</summary>
        public string DisplayLabel => HasAnyScore ? NiveauLabel : CartographieEnvironnementContent.NonEvalueLabel;

        /// <summary>Couleur affichée : grise tant qu'aucun item de la feuille n'est coché.</summary>
        public string DisplayColor => HasAnyScore ? NiveauColor : CartographieEnvironnementContent.NonEvalueColor;

        /// <summary>Description clinique : vide tant qu'aucun item n'est coché.</summary>
        public string DisplayDescription => HasAnyScore ? NiveauDescription : "";
    }
}
