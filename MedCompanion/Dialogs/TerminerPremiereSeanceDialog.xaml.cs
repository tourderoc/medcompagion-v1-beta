using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace MedCompanion.Dialogs
{
    public enum TerminerPremiereAction
    {
        Annuler,
        Suspendre,
        Cloturer
    }

    /// <summary>
    /// Logique d'interaction pour TerminerPremiereSeanceDialog.xaml
    /// Permet au médecin de choisir entre Suspendre (Garder en cours) et Clôturer définitivement.
    /// </summary>
    public partial class TerminerPremiereSeanceDialog : Window
    {
        public TerminerPremiereAction ActionChoisie { get; private set; } = TerminerPremiereAction.Annuler;

        public TerminerPremiereSeanceDialog(string patientNom, IReadOnlyList<string> etapesManquantes)
        {
            InitializeComponent();

            PatientTextBlock.Text = $"Patient : {patientNom}";

            if (etapesManquantes != null && etapesManquantes.Count > 0)
            {
                ManquantsItemsControl.ItemsSource = etapesManquantes;
                StatusTitleTextBlock.Text = "⚠️ Des étapes restent à finaliser :";
                StatusCardBorder.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7)); // #FEF3C7
                StatusCardBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B)); // #F59E0B
                StatusTitleTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E)); // #92400E
                StatusSubtextBlock.Text = "Vous pouvez suspendre la séance pour la reprendre plus tard (le dossier reste « En cours » sur la frise), ou la clôturer définitivement dès maintenant.";
            }
            else
            {
                ManquantsItemsControl.Visibility = Visibility.Collapsed;
                StatusTitleTextBlock.Text = "✅ Toutes les étapes de la 1ère consultation sont complétées !";
                StatusCardBorder.Background = new SolidColorBrush(Color.FromRgb(0xEC, 0xFD, 0xF5)); // #ECFDF5
                StatusCardBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x10, 0xB9, 0x81)); // #10B981
                StatusTitleTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x06, 0x5F, 0x46)); // #065F46
                StatusSubtextBlock.Text = "Interrogatoire, observations cliniques, synthèse initiale et restitution sont prêts. Vous pouvez clôturer la séance.";

                // Mettre en valeur le bouton Clôturer
                BtnCloturer.Background = new SolidColorBrush(Color.FromRgb(0xD1, 0xFA, 0xE5));
                BtnCloturer.BorderBrush = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69));
                TitleCloture.Text = "✅ Clôturer la 1ère consultation";
                TitleCloture.Foreground = new SolidColorBrush(Color.FromRgb(0x06, 0x5F, 0x46));
                IconCloture.Text = "✅";
            }
        }

        private void BtnSuspendre_Click(object sender, RoutedEventArgs e)
        {
            ActionChoisie = TerminerPremiereAction.Suspendre;
            DialogResult = true;
            Close();
        }

        private void BtnCloturer_Click(object sender, RoutedEventArgs e)
        {
            ActionChoisie = TerminerPremiereAction.Cloturer;
            DialogResult = true;
            Close();
        }

        private void BtnAnnuler_Click(object sender, RoutedEventArgs e)
        {
            ActionChoisie = TerminerPremiereAction.Annuler;
            DialogResult = false;
            Close();
        }
    }
}
