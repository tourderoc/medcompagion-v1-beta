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
    /// Évaluation ciblée — 2ᵉ rubrique de la 3ᵉ séance.
    ///
    /// Med dérive des axes de l'orientation validée ; le médecin coche OUI ou NON pendant la séance
    /// et ajoute ses remarques axe par axe. La remarque reste attachée à l'axe dont elle parle : à
    /// la synthèse, une phrase orpheline ne dirait plus sur quoi elle portait.
    ///
    /// Med propose, le médecin dispose — chaque axe et chaque proposition peuvent être supprimés,
    /// et il peut en ajouter.
    /// </summary>
    public class EvaluationCibleeV2ViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public ObservableCollection<AxeCible> Axes { get; } = new();

        public EvaluationCibleeV2ViewModel()
        {
            Axes.CollectionChanged += (_, _) => Rafraichir();

            RepondreOuiCommand = new RelayCommand(p =>
            {
                if (p is PropositionObservable prop) prop.Basculer(ReponseProposition.Oui);
            });

            RepondreNonCommand = new RelayCommand(p =>
            {
                if (p is PropositionObservable prop) prop.Basculer(ReponseProposition.Non);
            });

            SupprimerAxeCommand = new RelayCommand(p =>
            {
                if (p is AxeCible axe) Axes.Remove(axe);
            });

            SupprimerPropositionCommand = new RelayCommand(p =>
            {
                if (p is not PropositionObservable prop) return;
                var axe = Axes.FirstOrDefault(a => a.Propositions.Contains(prop));
                axe?.Propositions.Remove(prop);
            });

            AjouterPropositionCommand = new RelayCommand(
                p => { if (p is AxeCible axe) axe.Ajouter(new PropositionObservable()); },
                p => p is AxeCible axe && axe.Propositions.Count < AxeCible.MaxPropositions);

            AjouterAxeCommand = new RelayCommand(
                _ => Ajouter(new AxeCible { Intitule = "" }),
                _ => PeutAjouterAxe);
        }

        public ICommand RepondreOuiCommand          { get; }
        public ICommand RepondreNonCommand          { get; }
        public ICommand SupprimerAxeCommand         { get; }
        public ICommand SupprimerPropositionCommand { get; }
        public ICommand AjouterPropositionCommand   { get; }
        public ICommand AjouterAxeCommand           { get; }

        public bool PeutAjouterAxe
            => Axes.Count < Services.Evaluations.AxesCiblesSuggesterService.MaxAxes;

        public string CompteurAxesText
            => $"{Axes.Count} / {Services.Evaluations.AxesCiblesSuggesterService.MaxAxes}";

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

        public bool HasAxes => Axes.Count > 0;

        public int NbRepondu => Axes.Sum(a => a.NbRepondu);
        public int NbTotal   => Axes.Sum(a => a.Propositions.Count);

        /// <summary>
        /// Avancement : ce qui est RENSEIGNÉ sur ce qui est proposé. Jamais un score — compter les
        /// oui donnerait un chiffre qui ressemble à une gravité alors qu'il ne mesure que le nombre
        /// d'items que le modèle a formulés à l'affirmative.
        /// </summary>
        public string AvancementText => !HasAxes
            ? "aucun axe construit"
            : $"{Axes.Count} axes — {NbRepondu}/{NbTotal} constats renseignés";

        // ── Chargement / ajout ────────────────────────────────────────────────

        public void Charger(Services.Evaluations.SeanceEnvironnement? fiche)
        {
            Axes.Clear();
            Status = "";
            if (fiche == null) return;
            foreach (var a in fiche.Axes) Ajouter(a);
        }

        /// <summary>
        /// Ajoute un axe en le branchant sur le rafraîchissement. Passer par ici et jamais par
        /// <c>Axes.Add</c> directement : sans le branchement, cocher une case ne mettrait plus à
        /// jour les compteurs.
        /// </summary>
        public void Ajouter(AxeCible axe)
        {
            axe.Changed = Rafraichir;
            axe.Rebrancher();
            Axes.Add(axe);
        }

        /// <summary>Convertit une proposition de Med en axe éditable.</summary>
        public void AjouterSuggestion(Services.Evaluations.AxesCiblesSuggesterService.AxeSuggere s)
        {
            var axe = new AxeCible { Intitule = s.Intitule, Rattachement = s.Rattachement };
            foreach (var texte in s.Propositions)
                axe.Ajouter(new PropositionObservable { Texte = texte });
            Ajouter(axe);
        }

        public List<AxeCible> ToList() => Axes
            .Where(a => !string.IsNullOrWhiteSpace(a.Intitule))
            .ToList();

        private void Rafraichir()
        {
            OnPropertyChanged(nameof(HasAxes));
            OnPropertyChanged(nameof(NbRepondu));
            OnPropertyChanged(nameof(NbTotal));
            OnPropertyChanged(nameof(AvancementText));
            OnPropertyChanged(nameof(PeutAjouterAxe));
            OnPropertyChanged(nameof(CompteurAxesText));
        }
    }
}
