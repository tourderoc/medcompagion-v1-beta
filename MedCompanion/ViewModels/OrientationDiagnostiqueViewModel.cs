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
    /// <summary>
    /// Une rubrique de l'orientation diagnostique : un titre, une liste éditable, un cap.
    ///
    /// Le cap de 3 vaut aussi pour la saisie manuelle, pas seulement pour le LLM. Sinon la
    /// contrainte ne serait qu'un réglage du modèle, et la liste regonflerait à la main —
    /// alors que la raison du cap est clinique : neuf hypothèses ne s'observent pas en une
    /// consultation.
    /// </summary>
    public class RubriqueOrientationViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public const int Cap = 3;

        public string Titre     { get; init; } = "";
        public string SousTitre { get; init; } = "";

        public ObservableCollection<EditableString> Items { get; } = new();

        public ICommand AjouterCommand   { get; }
        public ICommand SupprimerCommand { get; }   // param : EditableString

        public bool PeutAjouter => Items.Count < Cap;
        public string CompteurText => $"{Items.Count} / {Cap}";

        public RubriqueOrientationViewModel()
        {
            Items.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(PeutAjouter));
                OnPropertyChanged(nameof(CompteurText));
                Changed?.Invoke();
            };

            AjouterCommand = new RelayCommand(
                _ => Items.Add(new EditableString("")),
                _ => PeutAjouter);

            SupprimerCommand = new RelayCommand(p =>
            {
                if (p is EditableString e) Items.Remove(e);
            });
        }

        internal System.Action? Changed;

        public void Remplacer(IEnumerable<string> valeurs)
        {
            Items.Clear();
            foreach (var v in valeurs.Take(Cap))
                if (!string.IsNullOrWhiteSpace(v)) Items.Add(new EditableString(v.Trim()));
        }

        public List<string> ToList()
            => Items.Select(i => i.Value.Trim()).Where(v => v.Length > 0).ToList();
    }

    /// <summary>
    /// Orientation diagnostique — 1ʳᵉ rubrique de la 3ᵉ séance.
    ///
    /// Ce n'est pas un diagnostic : c'est une mise au point de l'attention, faite sur un dossier
    /// volontairement incomplet, dont l'unique produit est ce que le médecin ira observer pendant
    /// la séance. Les axes d'observation eux-mêmes sont dérivés plus tard, à l'évaluation ciblée,
    /// depuis les hypothèses validées ici.
    ///
    /// Med propose, le médecin dispose : chaque item est éditable, supprimable, et il peut en
    /// ajouter — dans la limite de trois par rubrique.
    /// </summary>
    public class OrientationDiagnostiqueViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public RubriqueOrientationViewModel Hypotheses = new()
        {
            Titre = "Hypothèses principales",
            SousTitre = "Ce vers quoi les données penchent aujourd'hui"
        };
        public RubriqueOrientationViewModel Differentiels = new()
        {
            Titre = "Diagnostics différentiels",
            SousTitre = "Ce qui pourrait expliquer la même chose"
        };
        public RubriqueOrientationViewModel AEliminer = new()
        {
            Titre = "À éliminer prudemment",
            SousTitre = "Ce qu'il serait coûteux de manquer"
        };
        public RubriqueOrientationViewModel Vigilance = new()
        {
            Titre = "Points de vigilance",
            SousTitre = "Ce qui mérite l'œil sans être une hypothèse"
        };
        public RubriqueOrientationViewModel Questions = new()
        {
            Titre = "Questions cliniques",
            SousTitre = "Ce que cette séance doit permettre de trancher"
        };

        public ObservableCollection<RubriqueOrientationViewModel> Rubriques { get; } = new();

        private string _status = "";
        public string Status
        {
            get => _status;
            set { if (_status != value) { _status = value; OnPropertyChanged(); } }
        }

        private bool _isSuggesting;
        public bool IsSuggesting
        {
            get => _isSuggesting;
            set { if (_isSuggesting != value) { _isSuggesting = value; OnPropertyChanged(); } }
        }

        public int NbItems => Rubriques.Sum(r => r.ToList().Count);
        public string AvancementText => $"{NbItems} éléments posés";

        public OrientationDiagnostiqueViewModel()
        {
            foreach (var r in new[] { Hypotheses, Differentiels, AEliminer, Vigilance, Questions })
            {
                r.Changed = () => { OnPropertyChanged(nameof(NbItems)); OnPropertyChanged(nameof(AvancementText)); };
                Rubriques.Add(r);
            }
        }

        public void Charger(Services.Evaluations.SeanceEnvironnement? fiche)
        {
            if (fiche == null)
            {
                foreach (var r in Rubriques) r.Items.Clear();
                Status = "";
                return;
            }
            Hypotheses.Remplacer(fiche.HypothesesPrincipales);
            Differentiels.Remplacer(fiche.Differentiels);
            AEliminer.Remplacer(fiche.AEliminer);
            Vigilance.Remplacer(fiche.PointsVigilance);
            Questions.Remplacer(fiche.QuestionsCliniques);
        }

        // ── Remplissage au fil de l'eau ───────────────────────────────────────
        // La proposition arrive rubrique par rubrique : chaque bloc se remplit dès qu'il est prêt,
        // au lieu que l'écran reste figé jusqu'à la dernière. Les clés sont celles du service.

        private static class Cles
        {
            public const string Hypotheses    = Services.Evaluations.PreparationSuggesterService.Cles.Hypotheses;
            public const string Differentiels = Services.Evaluations.PreparationSuggesterService.Cles.Differentiels;
            public const string AEliminer     = Services.Evaluations.PreparationSuggesterService.Cles.AEliminer;
            public const string Vigilance     = Services.Evaluations.PreparationSuggesterService.Cles.Vigilance;
            public const string Questions     = Services.Evaluations.PreparationSuggesterService.Cles.Questions;
        }

        private RubriqueOrientationViewModel? ParCle(string cle) => cle switch
        {
            Cles.Hypotheses    => Hypotheses,
            Cles.Differentiels => Differentiels,
            Cles.AEliminer     => AEliminer,
            Cles.Vigilance     => Vigilance,
            Cles.Questions     => Questions,
            _ => null
        };

        /// <summary>
        /// Pose une rubrique proposée par Med, sauf si le médecin l'a déjà renseignée.
        /// À appeler sur le thread UI.
        /// </summary>
        public void Poser(string cle, IEnumerable<string> items)
        {
            var r = ParCle(cle);
            if (r == null || r.Items.Count > 0) return;
            r.Remplacer(items);
        }

        /// <summary>
        /// Ce qui est déjà à l'écran, par clé. Le service s'en sert deux fois : pour ne pas
        /// dépenser un appel sur une rubrique déjà remplie, et pour nourrir les rubriques
        /// suivantes — le travail du médecin oriente la proposition au lieu d'être ignoré.
        /// </summary>
        public Dictionary<string, List<string>> EtatCourant()
        {
            var d = new Dictionary<string, List<string>>();
            foreach (var cle in new[] { Cles.Hypotheses, Cles.Differentiels, Cles.AEliminer,
                                        Cles.Vigilance, Cles.Questions })
            {
                var items = ParCle(cle)?.ToList() ?? new List<string>();
                if (items.Count > 0) d[cle] = items;
            }
            return d;
        }
    }
}
