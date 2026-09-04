using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MedCompanion.Models.Evaluations;
using MedCompanion.Services.Evaluations;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Édition des six réponses d'UN axe parent de la cartographie V2 (feuille parents),
    /// depuis le bouton « Compléter » d'une sphère du Dossier de Restitution.
    ///
    /// Trois états par item — oui / non / non renseigné — parce que la feuille elle-même les
    /// distingue : une case laissée vide par le parent n'est pas un non, et l'effacer ici
    /// fausserait le score. Le score reste le nombre de « oui », comme au dépouillement.
    /// La fiche n'est modifiée qu'à la sauvegarde ; Annuler ne touche à rien.
    /// </summary>
    public partial class AxeV2EvaluationDialog : Window
    {
        private readonly CartographieV2 _carto;
        private readonly string _axeKey;
        private readonly List<CheckBox> _cases = new();

        public AxeV2EvaluationDialog(CartographieV2 carto, string axeKey)
        {
            InitializeComponent();
            _carto  = carto;
            _axeKey = axeKey;

            var label = CartographieItemsV2.AxeLabel(axeKey);
            Title = $"🧩 {label}";
            TitreText.Text = label;
            InformateurText.Text = $"Feuille parents du {carto.Date:dd/MM/yyyy} — remplie par : {carto.InformateurLisible}";

            // La tranche est celle de la feuille imprimée, lue dans la fiche — c'est elle qui
            // dit à quels énoncés les réponses correspondent, pas l'âge courant de l'enfant.
            var bande = carto.BandeCode switch
            {
                "3-4" => BandeAgeCarto.TroisQuatre,
                "5-6" => BandeAgeCarto.CinqSix,
                "7-9" => BandeAgeCarto.SeptNeuf,
                _     => BandeAgeCarto.DixOnze
            };
            var enonces = CartographieItemsV2.Items(axeKey, bande);
            carto.ReponsesQuestionnaire.TryGetValue(axeKey, out var reps);

            for (int i = 0; i < enonces.Count; i++)
            {
                var r = reps != null && i < reps.Length ? reps[i] : "";
                var cb = new CheckBox
                {
                    IsThreeState = true,
                    IsChecked    = r switch { "oui" => true, "non" => false, _ => (bool?)null },
                    Margin       = new Thickness(0, 5, 0, 5),
                    FontSize     = 12,
                    Cursor       = System.Windows.Input.Cursors.Hand,
                    Content      = new TextBlock { Text = enonces[i], TextWrapping = TextWrapping.Wrap, FontSize = 12 }
                };
                cb.Checked       += (_, _) => MettreAJourScore();
                cb.Unchecked     += (_, _) => MettreAJourScore();
                cb.Indeterminate += (_, _) => MettreAJourScore();
                _cases.Add(cb);
                ItemsHost.Children.Add(cb);
            }
            MettreAJourScore();
        }

        private int ScoreCourant()
        {
            int n = 0;
            foreach (var c in _cases) if (c.IsChecked == true) n++;
            return n;
        }

        private void MettreAJourScore()
        {
            var score  = ScoreCourant();
            var niveau = CartographieItemsV2.NiveauPourScore(score);
            ScoreText.Text  = $"Score : {score}/6";
            NiveauText.Text = CartographieContent.NiveauLabel(niveau);
            try
            {
                NiveauBadge.Background = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString(CartographieContent.NiveauColor(niveau)));
            }
            catch { /* couleur non critique */ }

            int vides = 0;
            foreach (var c in _cases) if (c.IsChecked == null) vides++;
            VidesText.Text = vides > 0 ? $"{vides} non renseigné(s)" : "";
        }

        private void Annuler_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Sauvegarder_Click(object sender, RoutedEventArgs e)
        {
            var reps = new string[_cases.Count];
            for (int i = 0; i < _cases.Count; i++)
                reps[i] = _cases[i].IsChecked switch { true => "oui", false => "non", _ => "vide" };

            _carto.ReponsesQuestionnaire[_axeKey] = reps;
            _carto.ScoresQuestionnaire[_axeKey]   = ScoreCourant();
            _carto.DerniereModif = DateTime.Now;
            DialogResult = true;
        }
    }
}
