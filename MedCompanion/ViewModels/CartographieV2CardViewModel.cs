using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Models.Evaluations;
using MedCompanion.Services.Evaluations;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// Bloc de synthèse de cartographie, pour l'onglet SYNTHESE du dossier bleu — à la suite de
    /// la Synthèse Initiale.
    ///
    /// Il ne reprend PAS les scores ni les axes : ils sont déjà dans BILANS, et les répéter ici
    /// ferait deux endroits à tenir à jour. Ce bloc porte le texte et ce qui le qualifie —
    /// l'informateur et les deux fiabilités — parce qu'une synthèse lue sans savoir ce qu'elle
    /// vaut est une synthèse mal lue.
    /// </summary>
    public class CartographieSyntheseBlocViewModel
    {
        public string FilePath  { get; }
        public string Titre     { get; }
        public string Texte     { get; }
        public string Qualification { get; }
        public bool   EstCloturee   { get; }

        public CartographieSyntheseBlocViewModel(CartographieV2 c)
        {
            FilePath = c.FilePath;
            var age  = c.Age.HasValue ? $" ({c.Age} ans)" : "";
            Titre    = $"Cartographie de l'enfant — {c.Date:dd/MM/yyyy}{age}";
            Texte    = c.SyntheseTexte ?? "";
            EstCloturee = c.EstCloturee;

            Qualification =
                $"Rempli par : {c.InformateurLisible}   ·   "
              + $"Questionnaire parent : {FiabiliteCartographie.LabelDe(c.FiabiliteQuestionnaire)}   ·   "
              + $"Profils observés : {FiabiliteCartographie.LabelDe(c.FiabiliteObservation)}";
        }
    }

    /// <summary>Une réponse du parent à un item, telle qu'affichée quand on déplie un axe.</summary>
    public class CartographieV2ReponseLigne
    {
        public string Texte   { get; init; } = "";
        public string Marque  { get; init; } = "";   // ✓ / ✗ / —
        public string Couleur { get; init; } = "";
    }

    /// <summary>Une ligne d'axe telle qu'affichée dans la carte du dossier bleu.</summary>
    public class CartographieV2AxeLigne : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string Label   { get; init; } = "";
        public int    Valeur  { get; init; }
        public string Couleur { get; init; } = ProfilsObservesV2.NeutreVide;
        public string Lecture { get; init; } = "";   // le pôle vers lequel penche la cotation

        /// <summary>Les profils se cotent sur 5, les questionnaires sur 6. Deux échelles, deux dénominateurs.</summary>
        public bool   SurSix     { get; init; }
        public string ValeurText => SurSix ? $"{Valeur}/6" : $"{Valeur}/5";

        /// <summary>
        /// Le détail des six réponses du parent. Vide pour un axe de profil, qui n'en a pas.
        /// </summary>
        public List<CartographieV2ReponseLigne> Reponses { get; init; } = new();
        public bool HasReponses => Reponses.Count > 0;

        private bool _isExpanded;
        /// <summary>
        /// Replié par défaut : la carte porte déjà 18 axes de profil et 5 scores. Le détail est
        /// à un clic — présent quand on en a besoin, absent quand on lit d'un coup d'œil.
        /// </summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Chevron)));
            }
        }

        public string Chevron => _isExpanded ? "▾" : "▸";

        public ICommand ToggleCommand { get; }

        public CartographieV2AxeLigne()
        {
            ToggleCommand = new RelayCommand(_ => { if (HasReponses) IsExpanded = !IsExpanded; });
        }
    }

    public class CartographieV2ProfilBloc
    {
        public string Label { get; init; } = "";
        public string Icon  { get; init; } = "";
        public List<CartographieV2AxeLigne> Lignes { get; init; } = new();
    }

    /// <summary>
    /// Carte d'une Cartographie de l'enfant V2, pour l'onglet BILANS du dossier bleu.
    /// Lecture seule : pour modifier, rouvrir le bloc Cartographie depuis la frise.
    ///
    /// Le titre ne porte PAS « V2 ». Dans le dossier d'un enfant, à côté de vrais bilans, un
    /// numéro de version ne veut rien dire pour qui le lit — et ce dossier peut être lu par
    /// quelqu'un d'autre que son auteur. La version vit dans le fichier, pas dans le libellé.
    /// </summary>
    public class CartographieV2CardViewModel
    {
        public string FilePath  { get; }
        public string TitreCard { get; }

        /// <summary>
        /// Ce qui manque est écrit. Sans cette ligne, une carte montrant dix-huit axes et aucun
        /// questionnaire se lirait comme une cartographie complète.
        /// </summary>
        public string EtatLigne  { get; }
        public bool   EstComplete { get; }

        public List<CartographieV2ProfilBloc> Blocs { get; }

        /// <summary>
        /// Les 5 scores du questionnaire parent, avec leur couleur issue de la grille unique.
        /// C'est la moitié « parent » de la cartographie : sans elle, la carte ne montrait que
        /// ce que le médecin avait observé, et la lecture croisée — qui est tout l'objet de
        /// l'outil — restait impossible depuis le dossier.
        /// </summary>
        public List<CartographieV2AxeLigne> Questionnaire { get; }
        public bool HasQuestionnaire => Questionnaire.Count > 0;

        /// <summary>
        /// Qui a rempli la feuille. Affiché sous le titre du questionnaire : les cinq scores qui
        /// suivent n'ont pas la même portée selon le regard dont ils viennent.
        /// </summary>
        public string InformateurLigne { get; }

        public CartographieV2CardViewModel(CartographieV2 c)
        {
            FilePath    = c.FilePath;
            EstComplete = c.EstComplete;
            EtatLigne   = c.EtatLisible;

            var age = c.Age.HasValue ? $" ({c.Age} ans)" : "";
            TitreCard = $"🧩 Cartographie de l'enfant — {c.Date:dd/MM/yyyy}{age}";

            // La tranche est celle de la feuille imprimée, lue dans la fiche — c'est elle qui dit
            // à quels énoncés les réponses correspondent, pas l'âge courant de l'enfant.
            var bande = c.BandeCode switch
            {
                "3-4"  => BandeAgeCarto.TroisQuatre,
                "5-6"  => BandeAgeCarto.CinqSix,
                "7-9"  => BandeAgeCarto.SeptNeuf,
                _      => BandeAgeCarto.DixOnze
            };

            InformateurLigne = $"Rempli par : {c.InformateurLisible}";

            Questionnaire = new List<CartographieV2AxeLigne>();
            foreach (var axeKey in CartographieItemsV2.AxeKeys)
            {
                if (!c.ScoresQuestionnaire.TryGetValue(axeKey, out var score)) continue;
                var niveau = CartographieItemsV2.NiveauPourScore(score);

                var detail  = new List<CartographieV2ReponseLigne>();
                var enonces = CartographieItemsV2.Items(axeKey, bande);
                c.ReponsesQuestionnaire.TryGetValue(axeKey, out var reps);

                for (int i = 0; i < enonces.Count; i++)
                {
                    var r = reps != null && i < reps.Length ? reps[i] : "";
                    detail.Add(new CartographieV2ReponseLigne
                    {
                        Texte   = enonces[i],
                        Marque  = r switch { "oui" => "✓", "non" => "✗", _ => "—" },
                        Couleur = r switch
                        {
                            "oui" => ProfilsObservesV2.Vert,
                            "non" => ProfilsObservesV2.Rouge,
                            _     => ProfilsObservesV2.NeutreCote
                        }
                    });
                }

                Questionnaire.Add(new CartographieV2AxeLigne
                {
                    Label    = CartographieItemsV2.AxeLabel(axeKey),
                    Valeur   = score,
                    Couleur  = CartographieContent.NiveauColor(niveau),
                    Lecture  = CartographieContent.NiveauLabel(niveau),
                    SurSix   = true,
                    Reponses = detail
                });
            }

            Blocs = new List<CartographieV2ProfilBloc>();
            foreach (var profil in ProfilsObservesV2.Profils)
            {
                var lignes = new List<CartographieV2AxeLigne>();
                foreach (var ax in profil.Axes)
                {
                    if (!c.Axes.TryGetValue($"{profil.Key}.{ax.Key}", out var v) || v <= 0) continue;
                    lignes.Add(new CartographieV2AxeLigne
                    {
                        Label   = ax.Label,
                        Valeur  = v,
                        Couleur = ProfilsObservesV2.CouleurValeur(profil.Nature, v),
                        Lecture = v >= 3 ? ax.Pole5 : ax.Pole1
                    });
                }
                if (lignes.Count == 0) continue;

                Blocs.Add(new CartographieV2ProfilBloc
                {
                    Label  = profil.Label,
                    Icon   = profil.Icon,
                    Lignes = lignes
                });
            }
        }
    }
}
