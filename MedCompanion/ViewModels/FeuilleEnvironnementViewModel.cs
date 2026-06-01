using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Models.Evaluations;
using MedCompanion.Services.Evaluations;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// Wrapper bindable autour d'une FeuilleEnvironnement. Expose la couleur globale
    /// de la feuille (calculée selon la règle « centrale prioritaire » du Tome 3) et
    /// notifie le VM parent à chaque changement pour recalcul de la synthèse globale.
    /// Porte aussi la lecture LLM par feuille (V0.3) et ses commandes.
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

        // ── Lecture LLM par feuille (V0.3) ──────────────────────────────────

        /// <summary>
        /// Délégué injecté par le VM parent : reçoit la feuille à lire + token et
        /// produit la lecture. Si null, les commandes sont désactivées.
        /// </summary>
        public Func<FeuilleEnvironnementViewModel, CancellationToken, Task<(bool ok, string? lecture, string? error)>>? LectureCallback { get; set; }

        public string? LectureMed
        {
            get => Modele.LectureMed;
            set
            {
                if (Modele.LectureMed != value)
                {
                    Modele.LectureMed = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasLecture));
                    LectureChanged?.Invoke();
                }
            }
        }

        public DateTime? LectureDate
        {
            get => Modele.LectureDate;
            set
            {
                if (Modele.LectureDate != value)
                {
                    Modele.LectureDate = value;
                    OnPropertyChanged();
                    LectureChanged?.Invoke();
                }
            }
        }

        public bool HasLecture => !string.IsNullOrWhiteSpace(LectureMed);

        private bool _isLectureEnCours;
        public bool IsLectureEnCours
        {
            get => _isLectureEnCours;
            set
            {
                if (_isLectureEnCours != value)
                {
                    _isLectureEnCours = value;
                    OnPropertyChanged();
                    (LireFeuilleCommand     as RelayCommand)?.RaiseCanExecuteChanged();
                    (EffacerLectureCommand  as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private string? _lectureError;
        public string? LectureError
        {
            get => _lectureError;
            set
            {
                if (_lectureError != value)
                {
                    _lectureError = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasLectureError));
                }
            }
        }
        public bool HasLectureError => !string.IsNullOrWhiteSpace(_lectureError);

        public ICommand LireFeuilleCommand    { get; }
        public ICommand EffacerLectureCommand { get; }

        /// <summary>Notifié à chaque changement de la lecture (pour auto-save côté parent).</summary>
        public event System.Action? LectureChanged;

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

            LireFeuilleCommand = new RelayCommand(
                async _ => await LancerLectureAsync(),
                _ => LectureCallback != null && !IsLectureEnCours);

            EffacerLectureCommand = new RelayCommand(
                _ => { LectureMed = null; LectureDate = null; LectureError = null; },
                _ => HasLecture && !IsLectureEnCours);

            // Propager les changements de lecture pour rafraîchir CanExecute des commandes
            LectureChanged += () =>
            {
                (EffacerLectureCommand as RelayCommand)?.RaiseCanExecuteChanged();
            };
        }

        private async Task LancerLectureAsync()
        {
            if (LectureCallback == null) return;
            IsLectureEnCours = true;
            LectureError = null;
            try
            {
                var (ok, lecture, error) = await LectureCallback(this, CancellationToken.None);
                if (ok && !string.IsNullOrWhiteSpace(lecture))
                {
                    LectureMed  = lecture;
                    LectureDate = DateTime.Now;
                }
                else
                {
                    LectureError = error ?? "Lecture impossible.";
                }
            }
            catch (Exception ex)
            {
                LectureError = ex.Message;
            }
            finally
            {
                IsLectureEnCours = false;
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
