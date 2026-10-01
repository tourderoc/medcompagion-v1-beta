using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using MedCompanion.Dialogs;
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

        /// <summary>
        /// Le médecin veut créer le dossier d'un patient vu à l'agenda. Med passe le nom, le
        /// prénom et la date de naissance lus à l'écran — la fenêtre de création et sa garde
        /// contre les doublons font le reste.
        /// </summary>
        public event Action<Models.Agenda.RendezVous>? CreationDossierDemandee;

        public AgendaControl()
        {
            InitializeComponent();
            DataContext = _viewModel;

            // À chaque rafraîchissement, on remesure la zone disponible APRÈS la mise en page.
            // Sans ça, l'échelle restait calculée sur une hauteur périmée — prise avant que la
            // grille ait sa taille définitive — et la journée se retrouvait coupée en plein
            // après-midi (constaté le 28/09/2026 : grille arrêtée à 12h30). Converge tout seul :
            // remesurer avec la même valeur ne déclenche pas de nouveau rafraîchissement.
            _viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName != nameof(AgendaViewModel.Titre)) return;
                Dispatcher.BeginInvoke(new Action(Mesurer), DispatcherPriority.Loaded);
            };
        }

        private void Mesurer()
            => _viewModel.DefinirHauteurDisponible(CorpsScroll.ViewportHeight > 0
                ? CorpsScroll.ViewportHeight
                : CorpsScroll.ActualHeight);

        private void Bloc_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement el || el.DataContext is not BlocRendezVous bloc) return;

            if (bloc.PeutOuvrir)
            {
                DossierDemande?.Invoke(bloc.Dossier);
                return;
            }

            // Plusieurs dossiers possibles : Med ne tranche pas
            if (bloc.Rdv.Certitude == "ambigu")
            {
                var dlgAmbigu = new CustomChoiceDialog(
                    "Plusieurs dossiers possibles",
                    $"Plusieurs dossiers peuvent correspondre à {bloc.NomComplet}.\n\n" +
                    "Med ne choisit pas à votre place — ouvrez le bon dossier par la recherche.",
                    "Ouvrir la recherche",
                    "🗑 Supprimer ce rdv",
                    "Fermer");
                dlgAmbigu.Owner = Window.GetWindow(this);
                if (dlgAmbigu.ShowDialog() == true && dlgAmbigu.UserChoice == CustomChoiceDialog.Choice.Option2)
                {
                    ConfirmerEtSupprimer(bloc);
                }
                return;
            }

            // Aucun dossier Med associé (consultation parents, dossier à créer, ou rdv erroné/halluciné)
            var naissance = bloc.Rdv.DateNaissance.HasValue
                ? $"\nNé(e) le {bloc.Rdv.DateNaissance:dd/MM/yyyy} ({bloc.Age} ans)."
                : "\nAucune date de naissance n'a été lue.";

            var dlg = new CustomChoiceDialog(
                "Rendez-vous sans dossier",
                $"Rendez-vous de {bloc.Heure} : {bloc.NomComplet}{naissance}\n\n" +
                "Aucun dossier Med n'est associé à ce rendez-vous.\n\n" +
                "Que souhaitez-vous faire ?",
                "Créer son dossier",
                "🗑 Supprimer ce rdv",
                "Annuler");
            dlg.Owner = Window.GetWindow(this);

            if (dlg.ShowDialog() == true)
            {
                if (dlg.UserChoice == CustomChoiceDialog.Choice.Option1)
                {
                    CreationDossierDemandee?.Invoke(bloc.Rdv);
                }
                else if (dlg.UserChoice == CustomChoiceDialog.Choice.Option2)
                {
                    ConfirmerEtSupprimer(bloc);
                }
            }
        }

        private void SupprimerRdvMenu_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem item) return;
            var bloc = (item.DataContext as BlocRendezVous)
                    ?? ((item.Parent as ContextMenu)?.PlacementTarget as FrameworkElement)?.DataContext as BlocRendezVous;
            if (bloc != null)
            {
                ConfirmerEtSupprimer(bloc);
            }
        }

        private void ConfirmerEtSupprimer(BlocRendezVous bloc)
        {
            var detail = $"{bloc.Heure} — {bloc.NomComplet}";
            if (!string.IsNullOrWhiteSpace(bloc.Motif))
                detail += $"\n{bloc.Motif}";

            var r = MessageBox.Show(
                $"Supprimer ce rendez-vous de l'agenda Med ?\n\n" +
                $"{detail}\n\n" +
                "Doctolib n'est pas modifié : seule la copie locale de Med est mise à jour.",
                "Supprimer ce rendez-vous",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (r == MessageBoxResult.Yes)
            {
                _viewModel.SupprimerRendezVous(bloc.Rdv);
            }
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
        private void CorpsScroll_SizeChanged(object sender, SizeChangedEventArgs e) => Mesurer();

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
            if (dlg.ShowDialog() != true) return;

            var (ok, message) = _viewModel.Importer(dlg.FileName);

            // Le résultat se disait dans la ligne de statut, en petit et en bas de l'écran :
            // un import raté passait pour « il ne se passe rien » (constaté le 28/09/2026).
            MessageBox.Show(message, ok ? "Import terminé" : "Import impossible",
                MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
    }
}
