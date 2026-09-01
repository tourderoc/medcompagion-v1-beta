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
    /// Réponse à un item du questionnaire parent. Trois états, pas deux : la feuille porte
    /// DEUX cases (Oui et Non) précisément pour que « non » et « pas répondu » ne se confondent
    /// pas. Une ligne sautée par un parent ne doit pas produire un point perdu — donc une
    /// couleur plus sombre, donc potentiellement une orientation.
    /// </summary>
    public enum ReponseItem { NonRepondu = 0, Oui = 1, Non = 2 }

    public class ItemSaisieViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public int    Numero { get; init; }
        public string Texte  { get; init; } = "";

        private ReponseItem _reponse = ReponseItem.NonRepondu;
        public ReponseItem Reponse
        {
            get => _reponse;
            set
            {
                if (_reponse == value) return;
                _reponse = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EstOui));
                OnPropertyChanged(nameof(EstNon));
                OnPropertyChanged(nameof(EstVide));
                ReponseChanged?.Invoke();
            }
        }

        public bool EstOui  => _reponse == ReponseItem.Oui;
        public bool EstNon  => _reponse == ReponseItem.Non;
        public bool EstVide => _reponse == ReponseItem.NonRepondu;

        internal System.Action? ReponseChanged;

        public ICommand OuiCommand { get; }
        public ICommand NonCommand { get; }

        public ItemSaisieViewModel()
        {
            // Recliquer la réponse déjà posée la retire : c'est ainsi qu'on revient à
            // « pas répondu » quand on s'est trompé, sans quitter la ligne.
            OuiCommand = new RelayCommand(_ => Reponse = EstOui ? ReponseItem.NonRepondu : ReponseItem.Oui);
            NonCommand = new RelayCommand(_ => Reponse = EstNon ? ReponseItem.NonRepondu : ReponseItem.Non);
        }
    }

    public class AxeSaisieViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public string Key   { get; init; } = "";
        public string Label { get; init; } = "";
        public ObservableCollection<ItemSaisieViewModel> Items { get; } = new();

        /// <summary>Score brut : le nombre de « oui ». Les non-répondus ne comptent pas.</summary>
        public int Score => Items.Count(i => i.EstOui);

        public int NbRepondus => Items.Count(i => !i.EstVide);

        /// <summary>
        /// Vrai quand les six items ont une réponse. Un axe incomplet a un score qui sous-estime
        /// mécaniquement l'enfant : il est signalé plutôt que coloré comme les autres.
        /// </summary>
        public bool EstComplet => NbRepondus == Items.Count;

        public NiveauSegment Niveau => CartographieItemsV2.NiveauPourScore(Score);

        public string ScoreText   => $"{Score}/6";
        public string NiveauLabel => EstComplet
            ? CartographieContent.NiveauLabel(Niveau)
            : $"incomplet — {NbRepondus}/6 répondus";
        public string NiveauColor => EstComplet
            ? CartographieContent.NiveauColor(Niveau)
            : "#BDC3C7";

        public void Refresh()
        {
            OnPropertyChanged(nameof(Score));
            OnPropertyChanged(nameof(NbRepondus));
            OnPropertyChanged(nameof(EstComplet));
            OnPropertyChanged(nameof(ScoreText));
            OnPropertyChanged(nameof(NiveauLabel));
            OnPropertyChanged(nameof(NiveauColor));
        }
    }

    /// <summary>
    /// Dépouillement de la feuille parent : les 30 réponses lues sur l'image scannée, corrigeables
    /// une par une, et les 5 scores calculés en direct par la grille unique.
    ///
    /// Cet écran est la contrepartie de la règle « rien ne s'affiche pendant la séance ». Comme
    /// le résultat n'est jamais montré à la famille, le contrôle est déplacé ici — sans lui,
    /// archiver l'image ne servirait à rien puisqu'il n'y aurait aucun moyen de corriger.
    /// </summary>
    public class CartographieSaisieViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public string? ImagePath   { get; }
        public string  TrancheText { get; }

        public ObservableCollection<AxeSaisieViewModel> Axes { get; } = new();

        public CartographieSaisieViewModel(string? imagePath, BandeAgeCarto bande,
                                           IReadOnlyDictionary<string, int>? scoresExistants = null)
        {
            ImagePath   = imagePath;
            TrancheText = CartographieItemsV2.BandeLabel(bande);

            foreach (var axeKey in CartographieItemsV2.AxeKeys)
            {
                var axe = new AxeSaisieViewModel
                {
                    Key   = axeKey,
                    Label = CartographieItemsV2.AxeLabel(axeKey)
                };

                var enonces = CartographieItemsV2.Items(axeKey, bande);
                for (int i = 0; i < enonces.Count; i++)
                {
                    var item = new ItemSaisieViewModel { Numero = i + 1, Texte = enonces[i] };
                    item.ReponseChanged = () => { axe.Refresh(); OnPropertyChanged(nameof(AvancementText)); };
                    axe.Items.Add(item);
                }
                Axes.Add(axe);
            }

            // Reprise d'un dépouillement déjà fait : on ne repart pas de zéro. Faute de savoir
            // QUELS items étaient à oui, on restaure les N premiers — le médecin corrige. C'est
            // une reprise, pas une vérité : le score, lui, est exact.
            if (scoresExistants != null)
            {
                foreach (var axe in Axes)
                {
                    if (!scoresExistants.TryGetValue(axe.Key, out var score)) continue;
                    for (int i = 0; i < axe.Items.Count; i++)
                        axe.Items[i].Reponse = i < score ? ReponseItem.Oui : ReponseItem.Non;
                    axe.Refresh();
                }
            }
        }

        public int NbRepondus => Axes.Sum(a => a.NbRepondus);
        public int NbItems    => Axes.Sum(a => a.Items.Count);
        public string AvancementText => $"{NbRepondus} / {NbItems} réponses saisies";

        /// <summary>Les 5 scores, prêts à rejoindre les 18 axes observés dans la fiche de séance.</summary>
        public Dictionary<string, int> ToScores()
            => Axes.ToDictionary(a => a.Key, a => a.Score);

        /// <summary>Pré-remplit depuis une lecture automatique (clé = axe, valeur = 6 réponses).</summary>
        public void Prefill(IReadOnlyDictionary<string, ReponseItem[]> lecture)
        {
            foreach (var axe in Axes)
            {
                if (!lecture.TryGetValue(axe.Key, out var reps)) continue;
                for (int i = 0; i < axe.Items.Count && i < reps.Length; i++)
                    axe.Items[i].Reponse = reps[i];
                axe.Refresh();
            }
            OnPropertyChanged(nameof(AvancementText));
        }
    }
}
