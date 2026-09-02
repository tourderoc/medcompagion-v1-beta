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
    /// Une pastille cliquable (1 à 5) sur la ligne d'un axe.
    /// Seule la pastille choisie porte une couleur : le reste de la ligne reste pâle, pour que
    /// la valeur cotée se lise d'un coup d'œil.
    /// </summary>
    public class PastilleViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public int Valeur { get; init; }

        private string _fill = ProfilsObservesV2.NeutreVide;
        public string Fill
        {
            get => _fill;
            set { if (_fill != value) { _fill = value; OnPropertyChanged(); } }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        public ICommand? ClickCommand { get; set; }
    }

    /// <summary>
    /// Une ligne du profil : un axe, ses deux pôles, et ses 5 pastilles.
    /// </summary>
    public class AxeProfilViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public AxeProfilDef Def    { get; }
        public ProfilNature Nature { get; }

        public string Label => Def.Label;
        public string Pole1 => Def.Pole1;
        public string Pole5 => Def.Pole5;

        public ObservableCollection<PastilleViewModel> Pastilles { get; } = new();

        /// <summary>
        /// Garde de lecture seule, fournie par l'écran : une séance clôturée ne se recote pas.
        /// Portée par la commande plutôt que par l'affichage — un axe grisé mais cliquable
        /// laisserait croire à une saisie enregistrée.
        /// </summary>
        internal System.Func<bool>? PeutEditer;

        private int _valeur = ProfilsObservesV2.NonRenseigne;
        /// <summary>0 = non renseigné. C'est l'état de départ : on ne note que ce qu'on a vu.</summary>
        public int Valeur
        {
            get => _valeur;
            set
            {
                if (_valeur == value) return;
                _valeur = value;
                RefreshPastilles();
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsRenseigne));
            }
        }

        public bool IsRenseigne => _valeur > 0;

        public AxeProfilViewModel(AxeProfilDef def, ProfilNature nature)
        {
            Def    = def;
            Nature = nature;

            for (int v = 1; v <= 5; v++)
            {
                var valeur = v;
                var p = new PastilleViewModel { Valeur = valeur };
                // Recliquer la valeur déjà choisie la retire : c'est le seul moyen de corriger
                // une cotation posée par erreur sans quitter l'écran, et ça ne coûte pas un clic
                // de plus que d'en choisir une autre.
                p.ClickCommand = new RelayCommand(
                    _ => Valeur = (Valeur == valeur ? 0 : valeur),
                    _ => PeutEditer?.Invoke() ?? true);
                Pastilles.Add(p);
            }
            RefreshPastilles();
        }

        private void RefreshPastilles()
        {
            foreach (var p in Pastilles)
            {
                p.IsSelected = p.Valeur == _valeur;
                p.Fill = p.IsSelected
                    ? ProfilsObservesV2.CouleurValeur(Nature, p.Valeur)
                    : ProfilsObservesV2.NeutreVide;
            }
        }
    }

    /// <summary>Un bloc de six axes.</summary>
    public class ProfilObserveViewModel
    {
        public ProfilDef Def { get; }

        public string       Label  => Def.Label;
        public string       Icon   => Def.Icon;
        public ProfilNature Nature => Def.Nature;

        /// <summary>
        /// Phrase de lecture affichée sous le titre du bloc. C'est elle qui dit au médecin
        /// pourquoi ce bloc est coloré et l'autre non — la règle est portée par l'écran, pas
        /// par une documentation qu'il faudrait avoir lue.
        /// </summary>
        public string ReglePhrase => Def.Nature == ProfilNature.Portrait
            ? "Portrait — aucun côté n'est meilleur que l'autre, donc pas de couleur."
            : "Compétence — 5 est toujours le bon côté.";

        public ObservableCollection<AxeProfilViewModel> Axes { get; } = new();

        public ProfilObserveViewModel(ProfilDef def)
        {
            Def = def;
            foreach (var a in def.Axes)
                Axes.Add(new AxeProfilViewModel(a, def.Nature));
        }
    }

    /// <summary>
    /// Écran « Profils observés » du bloc Cartographie de l'enfant.
    ///
    /// Les 5 questionnaires parents sont des actes séparés ; les 3 profils sont UN SEUL acte
    /// d'observation, tiré des mêmes trente minutes. Ils s'affichent donc ensemble.
    ///
    /// Rempli PENDANT la séance : un écran, aucune boîte de dialogue, un clic par axe, rien de
    /// coté au départ.
    /// </summary>
    public class ProfilsObservesViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public ObservableCollection<ProfilObserveViewModel> Profils { get; } = new();

        private bool _isReadOnly;
        /// <summary>Séance clôturée : les pastilles ne répondent plus.</summary>
        public bool IsReadOnly
        {
            get => _isReadOnly;
            set
            {
                if (_isReadOnly == value) return;
                _isReadOnly = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsEditable));
                System.Windows.Input.CommandManager.InvalidateRequerySuggested();
            }
        }
        public bool IsEditable => !_isReadOnly;

        public ProfilsObservesViewModel()
        {
            foreach (var def in ProfilsObservesV2.Profils)
                Profils.Add(new ProfilObserveViewModel(def));

            foreach (var axe in AllAxes) axe.PeutEditer = () => !_isReadOnly;

            foreach (var axe in AllAxes)
                axe.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(AxeProfilViewModel.Valeur))
                    {
                        OnPropertyChanged(nameof(NbRenseignes));
                        OnPropertyChanged(nameof(AvancementText));
                    }
                };
        }

        private IEnumerable<AxeProfilViewModel> AllAxes => Profils.SelectMany(p => p.Axes);

        public int NbAxes       => AllAxes.Count();
        public int NbRenseignes => AllAxes.Count(a => a.IsRenseigne);

        /// <summary>
        /// « 7 / 18 axes notés ». Un décompte, pas une barre de progression : rien n'oblige à
        /// tout coter — un axe non observé doit pouvoir rester vide sans que l'écran le réclame.
        /// </summary>
        public string AvancementText => $"{NbRenseignes} / {NbAxes} axes notés";

        /// <summary>Remet tous les axes à « non renseigné ».</summary>
        public void Reset()
        {
            foreach (var a in AllAxes) a.Valeur = ProfilsObservesV2.NonRenseigne;
        }

        /// <summary>
        /// Les axes cotés, sous la clé « profil.axe ». Les axes non observés ne sont pas
        /// exportés — absent et « 0 » disent la même chose, et le fichier reste lisible.
        /// </summary>
        public Dictionary<string, int> ToDictionary()
        {
            var d = new Dictionary<string, int>();
            foreach (var p in Profils)
                foreach (var a in p.Axes)
                    if (a.IsRenseigne) d[$"{p.Def.Key}.{a.Def.Key}"] = a.Valeur;
            return d;
        }

        /// <summary>Recharge les cotations depuis une fiche enregistrée.</summary>
        public void LoadFrom(IReadOnlyDictionary<string, int> axes)
        {
            foreach (var p in Profils)
                foreach (var a in p.Axes)
                    a.Valeur = axes.TryGetValue($"{p.Def.Key}.{a.Def.Key}", out var v)
                        ? v
                        : ProfilsObservesV2.NonRenseigne;
        }
    }
}
