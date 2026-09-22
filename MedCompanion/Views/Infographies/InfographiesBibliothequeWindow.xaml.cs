using System.Windows;
using System.Windows.Input;
using MedCompanion.ViewModels.Infographies;

namespace MedCompanion.Views.Infographies
{
    /// <summary>
    /// Bibliothèque ouverte depuis la consultation : on choisit une fiche validée et on l'imprime.
    /// L'impression est notée dans le dossier du patient en cours, s'il y en a un.
    /// </summary>
    public partial class InfographiesBibliothequeWindow : Window
    {
        private readonly InfographiesViewModel _viewModel;

        public InfographiesBibliothequeWindow(string? patientDirectory, string? patientNom)
        {
            InitializeComponent();
            _viewModel = new InfographiesViewModel(seulementValidees: true, patientDirectory);
            DataContext = _viewModel;
            if (!string.IsNullOrWhiteSpace(patientNom))
                Title = $"Bibliothèque d'infographies — {patientNom}";

            _viewModel.Charger();
            Loaded += (s, e) => RechercheBox.Focus();
        }

        private void Imprimer_Click(object sender, RoutedEventArgs e) => _viewModel.Imprimer();

        private void Liste_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_viewModel.ASelection) _viewModel.Imprimer();
        }
    }
}
