using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MedCompanion.ViewModels.Agenda;

namespace MedCompanion.Views.Agenda
{
    /// <summary>Grise le bouton pendant la lecture.</summary>
    public class InverseBoolConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value is bool b && !b;

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
            => value is bool b && !b;
    }

    /// <summary>
    /// Agenda du Bureau : copie locale de l'agenda Doctolib, en lecture seule.
    /// Écran d'accueil du mode Bureau.
    /// </summary>
    public partial class AgendaControl : UserControl
    {
        private readonly AgendaViewModel _viewModel = new();

        /// <summary>
        /// Le médecin a cliqué un rendez-vous rapproché : le Bureau remonte la demande à la
        /// fenêtre, qui ouvre le dossier en mode Consultation. L'agenda ne connaît pas les modes,
        /// il dit seulement quel dossier est demandé.
        /// </summary>
        public event Action<string>? DossierDemande;

        public AgendaControl()
        {
            InitializeComponent();
            DataContext = _viewModel;
        }

        private void Bloc_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement el || el.DataContext is not BlocRendezVous bloc) return;

            if (bloc.PeutOuvrir)
            {
                DossierDemande?.Invoke(bloc.Dossier);
                return;
            }

            // Pas de dossier : on explique plutôt que de ne rien faire. La création viendra
            // ensuite ; ici, un rendez-vous sans dossier n'est pas forcément une anomalie —
            // une consultation parents n'a pas de dossier enfant.
            var quoi = bloc.Rdv.Certitude == "ambigu"
                ? $"Plusieurs dossiers peuvent correspondre à {bloc.NomComplet}.\n\n" +
                  "Med ne choisit pas à votre place — c'est le cas des frères et sœurs nés le même jour. " +
                  "Ouvrez le dossier par la recherche cette fois-ci."
                : $"Aucun dossier Med ne correspond à {bloc.NomComplet}.\n\n" +
                  "C'est normal pour une consultation parents, ou pour un patient dont le dossier " +
                  "n'existe pas encore. La création du dossier depuis l'agenda viendra plus tard.";

            MessageBox.Show(quoi, "Pas de dossier à ouvrir", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>Appelé à chaque ouverture : revient au jour même et relit le disque.</summary>
        public void Recharger()
        {
            _viewModel.Aujourdhui();
            _viewModel.DefinirHauteurDisponible(CorpsScroll.ActualHeight);
            _viewModel.Rafraichir();
        }

        /// <summary>
        /// La grille se cale sur la hauteur disponible : une journée tient à l'écran sans
        /// défilement, quelle que soit la taille de la fenêtre.
        /// </summary>
        private void CorpsScroll_SizeChanged(object sender, SizeChangedEventArgs e)
            => _viewModel.DefinirHauteurDisponible(CorpsScroll.ViewportHeight > 0
                ? CorpsScroll.ViewportHeight
                : CorpsScroll.ActualHeight);

        private void Precedent_Click(object sender, RoutedEventArgs e)  => _viewModel.Precedent();
        private void Suivant_Click(object sender, RoutedEventArgs e)    => _viewModel.Suivant();
        private void Aujourdhui_Click(object sender, RoutedEventArgs e) => _viewModel.Aujourdhui();

        private void Vider_Click(object sender, RoutedEventArgs e)
        {
            var (du, au) = _viewModel.PeriodeAffichee;
            var periode = du == au ? $"la journée du {du:dd/MM/yyyy}" : $"la semaine du {du:dd/MM} au {au:dd/MM/yyyy}";
            var r = MessageBox.Show(
                $"Vider {periode} dans l'agenda de Med ?\n\nSeule la copie locale est vidée : Doctolib n'est pas touché, " +
                "et un nouvel import ou une nouvelle lecture d'écran la remplira de nouveau.",
                "Vider l'agenda", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r == MessageBoxResult.Yes) _viewModel.ViderPeriodeAffichee();
        }

        private async void MettreAJour_Click(object sender, RoutedEventArgs e)
            => await _viewModel.MettreAJourDepuisCapturesAsync();

        private void FermerLecture_Click(object sender, RoutedEventArgs e) => _viewModel.EffacerLecture();

        private void Importer_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title  = "Importer un export d'agenda Doctolib",
                Filter = "Export Doctolib (CSV)|*.csv"
            };
            if (dlg.ShowDialog() == true)
                _viewModel.Importer(dlg.FileName);
        }
    }
}
