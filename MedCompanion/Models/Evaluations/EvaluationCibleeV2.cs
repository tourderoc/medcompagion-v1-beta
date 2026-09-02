using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace MedCompanion.Models.Evaluations
{
    /// <summary>
    /// Réponse à une proposition observable.
    ///
    /// <see cref="NonObservee"/> est la valeur PAR DÉFAUT, et ce n'est pas un détail d'implémentation :
    /// dans une séance, la plupart des propositions ne seront tout simplement pas rencontrées. Un
    /// item non coché ne dit RIEN — surtout pas « non ». Toute lecture ultérieure (synthèse,
    /// compteurs) doit traiter les trois valeurs séparément et ne jamais assimiler une absence à
    /// une négation : c'est ainsi qu'on fabrique un dossier qui affirme ce que personne n'a observé.
    /// </summary>
    public enum ReponseProposition
    {
        NonObservee = 0,
        Oui         = 1,
        Non         = 2
    }

    /// <summary>
    /// Un constat observable pendant la séance, coché OUI ou NON.
    ///
    /// La formulation doit rester au niveau du CONSTAT (« se retourne quand on entre »), jamais de
    /// l'inférence (« trouble de l'attention ») : un item qui demande une interprétation ne se coche
    /// pas honnêtement au moment où on le coche.
    /// </summary>
    public class PropositionObservable : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public string Texte { get; set; } = "";

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
                OnPropertyChanged(nameof(EstNonObservee));
                Changed?.Invoke();
            }
        }

        public bool EstOui         => _reponse == ReponseProposition.Oui;
        public bool EstNon         => _reponse == ReponseProposition.Non;
        public bool EstNonObservee => _reponse == ReponseProposition.NonObservee;

        /// <summary>
        /// Coche, ou DÉCOCHE si on reclique la même case. Se dédire doit être aussi facile que
        /// répondre : sans ce retour en arrière, une erreur de clic en séance devient une donnée
        /// définitive qu'on ne peut plus qu'inverser en mentant.
        /// </summary>
        public void Basculer(ReponseProposition valeur)
            => Reponse = _reponse == valeur ? ReponseProposition.NonObservee : valeur;

        internal System.Action? Changed;
    }

    /// <summary>
    /// Un axe d'observation de la 3ᵉ séance, dérivé de l'orientation diagnostique.
    ///
    /// <see cref="Rattachement"/> n'est pas décoratif : un axe qui ne se rattache à rien de
    /// l'orientation n'est pas un axe, c'est un inventaire qui revient par la fenêtre. C'est aussi
    /// ce qui permettra, à la synthèse, de dire SUR QUOI portent les oui et les non.
    /// </summary>
    public class AxeCible : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        /// <summary>Au plus 6 propositions : au-delà, l'axe cesse d'être observable en séance.</summary>
        public const int MaxPropositions = 6;

        public string Intitule     { get; set; } = "";
        public string Rattachement { get; set; } = "";

        public ObservableCollection<PropositionObservable> Propositions { get; } = new();

        private string _remarques = "";
        public string Remarques
        {
            get => _remarques;
            set
            {
                if (_remarques == value) return;
                _remarques = value ?? "";
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasRemarques));
                Changed?.Invoke();
            }
        }

        public bool HasRemarques => !string.IsNullOrWhiteSpace(_remarques);

        public AxeCible()
        {
            Propositions.CollectionChanged += (_, _) => { Rafraichir(); Changed?.Invoke(); };
        }

        internal System.Action? Changed;

        public void Ajouter(PropositionObservable p)
        {
            if (Propositions.Count >= MaxPropositions) return;
            p.Changed = Rafraichir;
            Propositions.Add(p);
        }

        public int NbOui        => Propositions.Count(p => p.EstOui);
        public int NbNon        => Propositions.Count(p => p.EstNon);
        public int NbNonObserve => Propositions.Count(p => p.EstNonObservee);
        public int NbRepondu    => NbOui + NbNon;

        /// <summary>
        /// Avancement lisible. On affiche le nombre de propositions RENSEIGNÉES, jamais un score :
        /// compter les oui produirait un chiffre qui ressemble à une gravité alors qu'il ne mesure
        /// que le nombre d'items formulés à l'affirmative par le modèle.
        /// </summary>
        public string AvancementText => Propositions.Count == 0
            ? "aucune proposition"
            : $"{NbRepondu}/{Propositions.Count} renseignées";

        public bool EstRenseigne => NbRepondu > 0 || HasRemarques;

        private void Rafraichir()
        {
            OnPropertyChanged(nameof(NbOui));
            OnPropertyChanged(nameof(NbNon));
            OnPropertyChanged(nameof(NbNonObserve));
            OnPropertyChanged(nameof(NbRepondu));
            OnPropertyChanged(nameof(AvancementText));
            OnPropertyChanged(nameof(EstRenseigne));
            Changed?.Invoke();
        }

        /// <summary>Rebranche les rappels après une désérialisation.</summary>
        public void Rebrancher()
        {
            foreach (var p in Propositions) p.Changed = Rafraichir;
            Rafraichir();
        }
    }
}
