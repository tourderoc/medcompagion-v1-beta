using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using MedCompanion.Models.Evaluations;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// Synthèse de la 3ᵉ séance — carte 6.
    ///
    /// TROIS TEMPS, dans cet ordre et pas un autre :
    ///  1. les réponses sont RÉUNIES et montrées — la cartographie de l'environnement avec ses
    ///     deux moitiés enfin ensemble, l'évaluation ciblée dans un bloc à part ;
    ///  2. le médecin pose une fiabilité sur CHACUN des deux blocs ;
    ///  3. le texte est rédigé en tenant compte de ces deux poids.
    ///
    /// Les deux blocs restent séparés jusqu'au bout. L'environnement repose pour moitié sur une
    /// feuille remplie en salle d'attente ; l'évaluation ciblée sur ce que le médecin a vu de ses
    /// yeux. Un seul curseur pour les deux traiterait implicitement l'un comme l'autre.
    /// </summary>
    public class SyntheseSeance3ViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        // ── Bloc 1 : la cartographie réunie ───────────────────────────────────

        public ObservableCollection<FeuilleLue> Feuilles { get; } = new();

        public int NbNervures         => Feuilles.Sum(f => f.NbNervures);
        public int NbNervuresLisibles => Feuilles.Sum(f => f.NbNervuresLisibles);
        public int NbManquants        => Feuilles.Sum(f => f.NbManquants);

        public bool HasEnvironnement => Feuilles.Any(f => f.NbTotal - f.NbManquants > 0);

        /// <summary>
        /// Dit d'abord ce qui est LISIBLE, ensuite ce qui manque. Une feuille à moitié revenue est
        /// le cas courant, pas une anomalie : l'écran doit le présenter comme un état, pas comme
        /// une erreur.
        /// </summary>
        public string EnvironnementEtatText => !HasEnvironnement
            ? "aucune réponse — ni feuille parents, ni cotation"
            : $"{NbNervuresLisibles}/{NbNervures} nervures lisibles"
              + (NbManquants > 0 ? $" · {NbManquants} réponse{(NbManquants > 1 ? "s" : "")} manquante{(NbManquants > 1 ? "s" : "")}" : "");

        private string _informateurLigne = "";
        public string InformateurLigne
        {
            get => _informateurLigne;
            set { if (_informateurLigne != value) { _informateurLigne = value; OnPropertyChanged(); } }
        }

        // ── Bloc 2 : l'évaluation ciblée, à part ──────────────────────────────

        public ObservableCollection<AxeCible> Axes { get; } = new();

        public bool HasAxes => Axes.Count > 0;

        public int NbConstats => Axes.Sum(a => a.Propositions.Count);
        public int NbRepondus => Axes.Sum(a => a.NbRepondu);

        public string AxesEtatText => !HasAxes
            ? "aucun axe construit"
            : $"{Axes.Count} axe{(Axes.Count > 1 ? "s" : "")} · {NbRepondus}/{NbConstats} constats renseignés";

        // ── Les deux fiabilités ───────────────────────────────────────────────

        public CurseurFiabiliteViewModel FiabiliteEnv { get; } = new()
        {
            Titre     = "Cartographie de l'environnement",
            SousTitre = "Feuille des parents + vos items cotés depuis l'entretien"
        };

        public CurseurFiabiliteViewModel FiabiliteAxes { get; } = new()
        {
            Titre     = "Évaluation ciblée",
            SousTitre = "Ce que vous avez observé vous-même pendant la séance"
        };

        // ── Le texte ──────────────────────────────────────────────────────────

        private string _texte = "";
        public string Texte
        {
            get => _texte;
            set
            {
                if (_texte == value) return;
                _texte = value ?? "";
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasTexte));
            }
        }

        public bool HasTexte => !string.IsNullOrWhiteSpace(_texte);

        private string _status = "";
        public string Status
        {
            get => _status;
            set { if (_status != value) { _status = value; OnPropertyChanged(); } }
        }

        private bool _isRedaction;
        public bool IsRedaction
        {
            get => _isRedaction;
            set { if (_isRedaction != value) { _isRedaction = value; OnPropertyChanged(); } }
        }

        /// <summary>
        /// La rédaction n'est offerte qu'une fois les deux fiabilités posées. Ce n'est pas une
        /// formalité : le poids conditionne la prudence du texte, et laisser rédiger sans lui
        /// produirait une synthèse qui affirme au même ton une feuille remplie avec soin et une
        /// feuille griffonnée dans le couloir.
        /// </summary>
        public bool PeutRediger =>
            (HasEnvironnement || HasAxes)
            && (!HasEnvironnement || FiabiliteEnv.Selection != null)
            && (!HasAxes || FiabiliteAxes.Selection != null);

        public string BlocageText
        {
            get
            {
                if (!HasEnvironnement && !HasAxes) return "Rien à synthétiser pour l'instant.";
                var manque = new List<string>();
                if (HasEnvironnement && FiabiliteEnv.Selection == null)  manque.Add("l'environnement");
                if (HasAxes && FiabiliteAxes.Selection == null)          manque.Add("l'évaluation ciblée");
                return manque.Count == 0
                    ? ""
                    : $"Posez la fiabilité de {string.Join(" et ", manque)} avant de rédiger.";
            }
        }

        public SyntheseSeance3ViewModel()
        {
            FiabiliteEnv.PropertyChanged  += (_, _) => RafraichirBlocage();
            FiabiliteAxes.PropertyChanged += (_, _) => RafraichirBlocage();
        }

        private void RafraichirBlocage()
        {
            OnPropertyChanged(nameof(PeutRediger));
            OnPropertyChanged(nameof(BlocageText));
        }

        // ── Chargement ────────────────────────────────────────────────────────

        public void Charger(Services.Evaluations.SeanceEnvironnement? fiche)
        {
            Feuilles.Clear();
            Axes.Clear();
            Status = "";

            if (fiche == null)
            {
                Texte = "";
                InformateurLigne = "";
                FiabiliteEnv.Selection = null;
                FiabiliteAxes.Selection = null;
                RafraichirTout();
                return;
            }

            foreach (var f in LectureEnvironnementV2.Construire(fiche.CotationsEnv, fiche.ReponsesParent))
                Feuilles.Add(f);

            foreach (var a in fiche.Axes) Axes.Add(a);

            Texte = fiche.SyntheseTexte ?? "";
            FiabiliteEnv.Selection  = fiche.FiabiliteEnv;
            FiabiliteAxes.Selection = fiche.FiabiliteAxes;

            var qui = fiche.InformateurEnv switch
            {
                "mere" => "la mère", "pere" => "le père", "autre" => "un autre adulte", _ => null
            };
            InformateurLigne = qui == null
                ? (fiche.HasReponsesParent ? "Feuille parents — informateur non renseigné." : "")
                : $"Feuille parents remplie par {qui}"
                  + (string.IsNullOrWhiteSpace(fiche.InformateurEnvNom) ? "" : $" ({fiche.InformateurEnvNom})") + ".";

            RafraichirTout();
        }

        public void RafraichirTout()
        {
            OnPropertyChanged(nameof(NbNervures));
            OnPropertyChanged(nameof(NbNervuresLisibles));
            OnPropertyChanged(nameof(NbManquants));
            OnPropertyChanged(nameof(HasEnvironnement));
            OnPropertyChanged(nameof(EnvironnementEtatText));
            OnPropertyChanged(nameof(HasAxes));
            OnPropertyChanged(nameof(NbConstats));
            OnPropertyChanged(nameof(NbRepondus));
            OnPropertyChanged(nameof(AxesEtatText));
            RafraichirBlocage();
        }
    }
}
