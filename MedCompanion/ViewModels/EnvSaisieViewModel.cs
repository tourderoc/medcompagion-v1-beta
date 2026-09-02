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
    /// Un bloc de la feuille parent : une feuille dimensionnelle et ses seuls items parents.
    ///
    /// PAS DE SCORE NI DE COULEUR ICI, contrairement au dépouillement de la feuille de l'enfant.
    /// Là-bas, un axe était six items du parent et se lisait seul. Ici, une feuille se lit sur ses
    /// deux moitiés — celle du parent et celle que le médecin cote depuis l'entretien — et afficher
    /// un « 4/5 » sur cet écran donnerait un chiffre qui a l'air d'un résultat alors qu'il ne
    /// couvre qu'une partie de la feuille.
    /// </summary>
    public class BlocEnvSaisieViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public string Key       { get; init; } = "";
        public string Label     { get; init; } = "";
        public string SousTitre { get; init; } = "";

        public ObservableCollection<ItemSaisieViewModel> Items { get; } = new();

        public int NbRepondus => Items.Count(i => !i.EstVide);
        public bool EstComplet => NbRepondus == Items.Count;

        public string AvancementText => $"{NbRepondus}/{Items.Count}";

        /// <summary>
        /// Ce que la feuille attend encore du médecin. Affiché ici pour que le dépouillement ne
        /// laisse pas croire qu'un bloc complet est une feuille complète.
        /// </summary>
        public string AttenteMedecinText
        {
            get
            {
                var n = CartographieEnvironnementV2.Par(Key)?.ItemsMedecin.Count() ?? 0;
                return n == 0 ? "" : $"+ {n} coté{(n > 1 ? "s" : "")} par vous";
            }
        }

        public void Refresh()
        {
            OnPropertyChanged(nameof(NbRepondus));
            OnPropertyChanged(nameof(EstComplet));
            OnPropertyChanged(nameof(AvancementText));
        }
    }

    /// <summary>
    /// Dépouillement de la feuille « Cartographie de l'environnement », ouvert APRÈS la séance.
    ///
    /// Contrepartie de la règle « rien ne s'affiche pendant la séance » : comme le résultat n'est
    /// jamais montré à la famille au moment du scan, le contrôle est déplacé ici. Sans cet écran,
    /// archiver l'image ne servirait à rien — il n'y aurait aucun moyen de corriger une lecture
    /// fausse.
    /// </summary>
    public class EnvSaisieViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public string? ImagePath { get; }

        public ObservableCollection<BlocEnvSaisieViewModel> Blocs { get; } = new();

        // ── Informateur ───────────────────────────────────────────────────────
        // Qui a rempli la feuille : c'est ce qui donne leur portée aux réponses. Un questionnaire
        // rempli par le parent qui voit l'enfant un week-end sur deux ne dit pas la même chose.

        private string? _informateur;
        public string? Informateur
        {
            get => _informateur;
            set
            {
                if (_informateur == value) return;
                _informateur = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EstMere));
                OnPropertyChanged(nameof(EstPere));
                OnPropertyChanged(nameof(EstAutre));
            }
        }

        public bool EstMere  => _informateur == "mere";
        public bool EstPere  => _informateur == "pere";
        public bool EstAutre => _informateur == "autre";

        private string _informateurNom = "";
        public string InformateurNom
        {
            get => _informateurNom;
            set { if (_informateurNom != value) { _informateurNom = value ?? ""; OnPropertyChanged(); } }
        }

        public ICommand ChoisirInformateurCommand { get; }

        public EnvSaisieViewModel(string? imagePath,
                                  IReadOnlyDictionary<string, string[]>? reponsesExistantes = null)
        {
            ImagePath = imagePath;

            // Recliquer le même choix l'annule — on peut se dédire sans avoir à recharger l'écran.
            ChoisirInformateurCommand = new RelayCommand(p =>
            {
                var v = p as string;
                Informateur = _informateur == v ? null : v;
            });

            foreach (var feuille in CartographieEnvironnementV2.Feuilles)
            {
                var items = feuille.ItemsParent.ToList();
                if (items.Count == 0) continue;   // feuille entièrement cotée par le médecin

                var bloc = new BlocEnvSaisieViewModel
                {
                    Key = feuille.Key, Label = feuille.Label, SousTitre = feuille.SousTitre
                };

                string[]? deja = null;
                reponsesExistantes?.TryGetValue(feuille.Key, out deja);

                for (int i = 0; i < items.Count; i++)
                {
                    var item = new ItemSaisieViewModel { Numero = i + 1, Texte = items[i].Texte };

                    if (deja != null && i < deja.Length)
                        item.Reponse = deja[i] switch
                        {
                            "oui" => ReponseItem.Oui,
                            "non" => ReponseItem.Non,
                            _     => ReponseItem.NonRepondu
                        };

                    item.PropertyChanged += (_, e) =>
                    {
                        if (e.PropertyName != nameof(ItemSaisieViewModel.Reponse)) return;
                        bloc.Refresh();
                        Rafraichir();
                    };

                    bloc.Items.Add(item);
                }

                Blocs.Add(bloc);
            }
        }

        public int NbRepondus => Blocs.Sum(b => b.NbRepondus);
        public int NbItems    => Blocs.Sum(b => b.Items.Count);

        public string AvancementText => $"{NbRepondus} / {NbItems} réponses saisies";

        /// <summary>
        /// Les réponses par feuille, sous une forme qui se relit sans l'application : « oui »,
        /// « non », ou chaîne vide. On garde le DÉTAIL, pas un score — c'est le détail qui sert
        /// l'analyse, et le score s'en déduit quand on en a besoin.
        /// </summary>
        public Dictionary<string, string[]> ToReponses()
        {
            var res = new Dictionary<string, string[]>();
            foreach (var b in Blocs)
                res[b.Key] = b.Items.Select(i => i.Reponse switch
                {
                    ReponseItem.Oui => "oui",
                    ReponseItem.Non => "non",
                    _               => ""
                }).ToArray();
            return res;
        }

        public void PrefillInformateur(string? qui, string? nom)
        {
            if (!string.IsNullOrWhiteSpace(qui)) Informateur = qui;
            if (!string.IsNullOrWhiteSpace(nom)) InformateurNom = nom!.Trim();
        }

        /// <summary>
        /// Pré-remplit depuis la lecture automatique. Ne touche QUE les cases encore vides : si le
        /// médecin a déjà corrigé une ligne à la main, une seconde lecture ne doit pas la reprendre.
        /// </summary>
        public void Prefill(IReadOnlyDictionary<string, ReponseItem[]> lecture)
        {
            foreach (var bloc in Blocs)
            {
                if (!lecture.TryGetValue(bloc.Key, out var reps)) continue;
                for (int i = 0; i < bloc.Items.Count && i < reps.Length; i++)
                    if (bloc.Items[i].EstVide) bloc.Items[i].Reponse = reps[i];
                bloc.Refresh();
            }
            Rafraichir();
        }

        private void Rafraichir()
        {
            OnPropertyChanged(nameof(NbRepondus));
            OnPropertyChanged(nameof(AvancementText));
        }
    }
}
