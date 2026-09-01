using System.Collections.Generic;
using System.Linq;
using MedCompanion.Models.Evaluations;
using MedCompanion.Services.Evaluations;

namespace MedCompanion.ViewModels
{
    /// <summary>Une ligne d'axe telle qu'affichée dans la carte du dossier bleu.</summary>
    public class CartographieV2AxeLigne
    {
        public string Label   { get; init; } = "";
        public int    Valeur  { get; init; }
        public string Couleur { get; init; } = ProfilsObservesV2.NeutreVide;
        public string Lecture { get; init; } = "";   // le pôle vers lequel penche la cotation

        /// <summary>Les profils se cotent sur 5, les questionnaires sur 6. Deux échelles, deux dénominateurs.</summary>
        public bool   SurSix     { get; init; }
        public string ValeurText => SurSix ? $"{Valeur}/6" : $"{Valeur}/5";
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

        public CartographieV2CardViewModel(CartographieV2 c)
        {
            FilePath    = c.FilePath;
            EstComplete = c.EstComplete;
            EtatLigne   = c.EtatLisible;

            var age = c.Age.HasValue ? $" ({c.Age} ans)" : "";
            TitreCard = $"🧩 Cartographie de l'enfant — {c.Date:dd/MM/yyyy}{age}";

            Questionnaire = new List<CartographieV2AxeLigne>();
            foreach (var axeKey in CartographieItemsV2.AxeKeys)
            {
                if (!c.ScoresQuestionnaire.TryGetValue(axeKey, out var score)) continue;
                var niveau = CartographieItemsV2.NiveauPourScore(score);
                Questionnaire.Add(new CartographieV2AxeLigne
                {
                    Label   = CartographieItemsV2.AxeLabel(axeKey),
                    Valeur  = score,
                    Couleur = CartographieContent.NiveauColor(niveau),
                    Lecture = CartographieContent.NiveauLabel(niveau),
                    SurSix  = true
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
