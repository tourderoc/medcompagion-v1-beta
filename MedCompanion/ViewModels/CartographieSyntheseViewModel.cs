using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Models.Evaluations;

namespace MedCompanion.ViewModels
{
    /// <summary>Un choix de fiabilité, affiché comme un bouton nommé.</summary>
    public class ChoixFiabiliteViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public NiveauFiabilite Niveau { get; init; } = null!;
        public string Label   => Niveau.Label;
        public string Detail  => Niveau.Detail;
        public string Couleur => Niveau.Couleur;

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public ICommand? ClickCommand { get; set; }
    }

    /// <summary>
    /// Une des deux moitiés à qualifier : le questionnaire parent, ou les profils observés.
    /// </summary>
    public class CurseurFiabiliteViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public string Titre      { get; init; } = "";
        public string SousTitre  { get; init; } = "";

        public ObservableCollection<ChoixFiabiliteViewModel> Choix { get; } = new();

        private string? _selection;
        public string? Selection
        {
            get => _selection;
            set
            {
                if (_selection == value) return;
                _selection = value;
                foreach (var c in Choix) c.IsSelected = c.Niveau.Key == value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectionLabel));
                SelectionChanged?.Invoke();
            }
        }

        public string SelectionLabel => FiabiliteCartographie.LabelDe(_selection);

        internal System.Action? SelectionChanged;

        public CurseurFiabiliteViewModel()
        {
            foreach (var n in FiabiliteCartographie.Niveaux)
            {
                var choix = new ChoixFiabiliteViewModel { Niveau = n };
                // Recliquer le niveau déjà choisi le retire : on revient à « non renseignée »
                // sans avoir à choisir un niveau qu'on ne pense pas.
                choix.ClickCommand = new RelayCommand(_ =>
                    Selection = (Selection == n.Key) ? null : n.Key);
                Choix.Add(choix);
            }
        }
    }

    /// <summary>
    /// Carte 4 du bloc Cartographie : qualifier les deux moitiés, puis produire une synthèse
    /// qui les PRÉSENTE — elle ne conclut pas.
    ///
    /// La ligne est fine et volontaire : cette synthèse dit « voici les deux moitiés, voilà ce
    /// qu'elles valent, prêtes à être croisées », pas « cet enfant présente un trouble de X ».
    /// Le jour où elle conclurait, il y aurait deux endroits où s'écrit le même diagnostic, et
    /// ils divergeraient.
    /// </summary>
    public class CartographieSyntheseViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public CurseurFiabiliteViewModel Questionnaire { get; } = new()
        {
            Titre     = "Questionnaire parent",
            SousTitre = "Quelle confiance accordez-vous à la façon dont la feuille a été remplie ?"
        };

        public CurseurFiabiliteViewModel Observation { get; } = new()
        {
            Titre     = "Profils observés",
            SousTitre = "Et à votre propre observation, dans les conditions de cette séance ?"
        };

        private string _informateurLigne = "";
        public string InformateurLigne
        {
            get => _informateurLigne;
            set { if (_informateurLigne != value) { _informateurLigne = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// Axes du questionnaire dont des items sont restés sans réponse. Signal de fiabilité
        /// OBJECTIF et par axe — il ne dépend d'aucun jugement, et complète les deux curseurs
        /// au lieu de les répéter.
        /// </summary>
        public ObservableCollection<string> AxesIncomplets { get; } = new();
        public bool HasAxesIncomplets => AxesIncomplets.Count > 0;

        private string _texte = "";
        public string Texte
        {
            get => _texte;
            set { if (_texte != value) { _texte = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasTexte)); } }
        }
        public bool HasTexte => !string.IsNullOrWhiteSpace(_texte);

        private string _status = "";
        public string Status
        {
            get => _status;
            set { if (_status != value) { _status = value; OnPropertyChanged(); } }
        }

        private bool _isGenerating;
        public bool IsGenerating
        {
            get => _isGenerating;
            set { if (_isGenerating != value) { _isGenerating = value; OnPropertyChanged(); OnPropertyChanged(nameof(PeutGenerer)); } }
        }

        /// <summary>
        /// Les deux fiabilités doivent être posées avant de générer : c'est le sens même de
        /// cette carte. Une synthèse produite sans elles serait une synthèse non qualifiée,
        /// c'est-à-dire celle qu'on voulait éviter.
        /// </summary>
        public bool PeutGenerer =>
            !IsGenerating && Questionnaire.Selection != null && Observation.Selection != null;

        public CartographieSyntheseViewModel()
        {
            Questionnaire.SelectionChanged = () => OnPropertyChanged(nameof(PeutGenerer));
            Observation.SelectionChanged   = () => OnPropertyChanged(nameof(PeutGenerer));
        }

        public void Charger(Services.Evaluations.CartographieV2? fiche)
        {
            AxesIncomplets.Clear();
            if (fiche == null)
            {
                InformateurLigne = "informateur non renseigné";
                return;
            }

            InformateurLigne          = fiche.InformateurLisible;
            Questionnaire.Selection   = fiche.FiabiliteQuestionnaire;
            Observation.Selection     = fiche.FiabiliteObservation;
            Texte                     = fiche.SyntheseTexte ?? "";

            foreach (var axe in CartographieItemsV2.AxeKeys)
            {
                if (!fiche.ReponsesQuestionnaire.TryGetValue(axe, out var reps)) continue;
                var vides = reps.Count(r => r != "oui" && r != "non");
                if (vides > 0)
                    AxesIncomplets.Add($"{CartographieItemsV2.AxeLabel(axe)} — {vides} item(s) sans réponse");
            }
            OnPropertyChanged(nameof(HasAxesIncomplets));
        }
    }
}
