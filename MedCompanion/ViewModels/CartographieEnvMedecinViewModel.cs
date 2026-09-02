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
    /// Une ligne d'une nervure. Les items du parent y figurent aussi, en grisé et non cliquables :
    /// le médecin doit voir la nervure ENTIÈRE pendant qu'il cote sa part, sinon il coterait trois
    /// items en croyant qu'ils font toute la nervure — et il verrait mal pourquoi elle ne prend pas
    /// encore de couleur.
    /// </summary>
    public class LigneEnvViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public string Texte      { get; init; } = "";
        public bool   EstMedecin { get; init; }

        /// <summary>Vrai pour les items qui attendent la feuille parent — affichage seul.</summary>
        public bool EstParent => !EstMedecin;

        private ReponseProposition _reponse = ReponseProposition.NonObservee;
        public ReponseProposition Reponse
        {
            get => _reponse;
            set
            {
                if (_reponse == value) return;
                _reponse = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(EstOui));
                OnPropertyChanged(nameof(EstNon));
                Changed?.Invoke();
            }
        }

        public bool EstOui => _reponse == ReponseProposition.Oui;
        public bool EstNon => _reponse == ReponseProposition.Non;

        /// <summary>Coche, ou décoche si on reclique la même case.</summary>
        public void Basculer(ReponseProposition v)
            => Reponse = _reponse == v ? ReponseProposition.NonObservee : v;

        internal System.Action? Changed;
    }

    public class NervureEnvViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public string Label      { get; init; } = "";
        public bool   IsCentrale { get; init; }

        public ObservableCollection<LigneEnvViewModel> Lignes { get; } = new();

        public int NbMedecin => Lignes.Count(l => l.EstMedecin);
        public int NbCote    => Lignes.Count(l => l.EstMedecin && l.Reponse != ReponseProposition.NonObservee);
        public int NbParent  => Lignes.Count(l => l.EstParent);

        /// <summary>
        /// Ce que la nervure attend encore. On n'affiche PAS de couleur ici : une nervure se lit sur
        /// ses deux moitiés, et la moitié parent n'arrive qu'au dépouillement de la feuille. Colorer
        /// sur les seuls items du médecin donnerait une teinte qui a l'air d'un résultat.
        /// </summary>
        public string EtatText
        {
            get
            {
                if (NbMedecin == 0) return $"{NbParent} de la feuille parent";
                var attente = NbParent > 0 ? $" · {NbParent} de la feuille parent" : "";
                return $"{NbCote}/{NbMedecin} coté{(NbCote == 1 ? "" : "s")}{attente}";
            }
        }

        internal void Rafraichir()
        {
            OnPropertyChanged(nameof(NbCote));
            OnPropertyChanged(nameof(EtatText));
        }
    }

    public class FeuilleEnvViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public string Label     { get; init; } = "";
        public string SousTitre { get; init; } = "";

        public ObservableCollection<NervureEnvViewModel> Nervures { get; } = new();

        public int NbMedecin => Nervures.Sum(n => n.NbMedecin);
        public int NbCote    => Nervures.Sum(n => n.NbCote);

        public string AvancementText => $"{NbCote}/{NbMedecin}";

        internal void Rafraichir()
        {
            foreach (var n in Nervures) n.Rafraichir();
            OnPropertyChanged(nameof(NbCote));
            OnPropertyChanged(nameof(AvancementText));
        }
    }

    /// <summary>
    /// Cartographie de l'environnement, versant médecin — carte 4 de la 3ᵉ séance.
    ///
    /// Les 14 items que le médecin cote depuis l'entretien, dans les quatre feuilles et leurs
    /// nervures. Les 22 autres partent en salle d'attente sur la feuille parent.
    ///
    /// Le partage n'est pas celui de la séance 2. Là-bas le critère était l'accès à l'information ;
    /// ici, le parent décrit SA PROPRE famille, et sur les items qui comptent le plus, un parent en
    /// difficulté est précisément celui qui répondra « oui ». Le critère devient : l'item met-il en
    /// cause celui qui remplit.
    ///
    /// RIEN DE COCHÉ = NON RENSEIGNÉ, jamais « non » — ces items sont des affirmations favorables,
    /// et un « non » y est un signal. En faire le défaut peindrait en rouge tout ce que le médecin
    /// n'a pas eu le temps d'aborder.
    /// </summary>
    public class CartographieEnvMedecinViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public ObservableCollection<FeuilleEnvViewModel> Feuilles { get; } = new();

        public ICommand CoterOuiCommand { get; }
        public ICommand CoterNonCommand { get; }

        public CartographieEnvMedecinViewModel()
        {
            CoterOuiCommand = new RelayCommand(p =>
            {
                if (p is LigneEnvViewModel l && l.EstMedecin) l.Basculer(ReponseProposition.Oui);
            });
            CoterNonCommand = new RelayCommand(p =>
            {
                if (p is LigneEnvViewModel l && l.EstMedecin) l.Basculer(ReponseProposition.Non);
            });

            Construire();
        }

        private string _status = "";
        public string Status
        {
            get => _status;
            set { if (_status != value) { _status = value; OnPropertyChanged(); } }
        }

        public int NbCote  => Feuilles.Sum(f => f.NbCote);
        public int NbTotal => CartographieEnvironnementV2.NbItemsMedecin;

        public string AvancementText => NbCote == 0
            ? $"{NbTotal} items à coter"
            : $"{NbCote}/{NbTotal} items cotés";

        /// <summary>
        /// Bâtit l'arbre depuis le catalogue. Les items ne sont jamais éditables : ce sont des
        /// questions fixes, communes à tous les patients — c'est ce qui rend la lecture d'une
        /// feuille comparable d'un dossier à l'autre.
        /// </summary>
        private void Construire()
        {
            foreach (var feuille in CartographieEnvironnementV2.Feuilles)
            {
                var fvm = new FeuilleEnvViewModel { Label = feuille.Label, SousTitre = feuille.SousTitre };

                foreach (var nervure in feuille.Nervures)
                {
                    var nvm = new NervureEnvViewModel { Label = nervure.Label, IsCentrale = nervure.IsCentrale };

                    foreach (var item in nervure.Items)
                    {
                        var ligne = new LigneEnvViewModel
                        {
                            Texte      = item.Texte,
                            EstMedecin = item.Source == SourceItemEnv.Medecin
                        };
                        ligne.Changed = () => { nvm.Rafraichir(); fvm.Rafraichir(); Rafraichir(); };
                        nvm.Lignes.Add(ligne);
                    }

                    fvm.Nervures.Add(nvm);
                }

                Feuilles.Add(fvm);
            }
        }

        private IEnumerable<LigneEnvViewModel> LignesMedecin
            => Feuilles.SelectMany(f => f.Nervures).SelectMany(n => n.Lignes).Where(l => l.EstMedecin);

        public void Charger(Services.Evaluations.SeanceEnvironnement? fiche)
        {
            foreach (var l in LignesMedecin)
                l.Reponse = fiche != null && fiche.CotationsEnv.TryGetValue(l.Texte, out var r)
                    ? r
                    : ReponseProposition.NonObservee;

            Status = "";
            foreach (var f in Feuilles) f.Rafraichir();
            Rafraichir();
        }

        /// <summary>
        /// Ne renvoie que ce qui a été RÉPONDU. Écrire les non renseignés remplirait la fiche de
        /// lignes vides qui ressembleraient à un travail fait.
        /// </summary>
        public Dictionary<string, ReponseProposition> ToDictionary()
            => LignesMedecin
                .Where(l => l.Reponse != ReponseProposition.NonObservee)
                .ToDictionary(l => l.Texte, l => l.Reponse);

        private void Rafraichir()
        {
            OnPropertyChanged(nameof(NbCote));
            OnPropertyChanged(nameof(AvancementText));
        }
    }
}
