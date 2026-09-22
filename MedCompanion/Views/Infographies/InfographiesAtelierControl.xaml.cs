using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MedCompanion.Services.Infographies;
using MedCompanion.ViewModels.Infographies;

namespace MedCompanion.Views.Infographies
{
    /// <summary>
    /// Mode Bureau « Infographies » : import, classement et validation des fiches.
    /// Le travail de préparation se fait ici, hors consultation.
    /// </summary>
    public partial class InfographiesAtelierControl : UserControl
    {
        private readonly InfographiesViewModel _viewModel = new(seulementValidees: false);

        private static readonly string FiltreImages =
            "Infographies (PDF, PNG, JPG)|" + string.Join(";", InfographieService.ExtensionsAcceptees.Select(e => "*" + e));

        public InfographiesAtelierControl()
        {
            InitializeComponent();
            DataContext = _viewModel;
        }

        /// <summary>Appelé à chaque ouverture du mode : relit la bibliothèque sur disque.</summary>
        public void Recharger() => _viewModel.Charger();

        private void Importer_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Importer des infographies",
                Filter = FiltreImages,
                Multiselect = true
            };
            if (dlg.ShowDialog() == true)
                _viewModel.Importer(dlg.FileNames);
        }

        private void Remplacer_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.Selection == null) return;
            var dlg = new OpenFileDialog
            {
                Title = $"Nouvelle version de « {_viewModel.Selection.Titre} »",
                Filter = FiltreImages
            };
            if (dlg.ShowDialog() == true)
                _viewModel.RemplacerImage(dlg.FileName);
        }

        private void Valider_Click(object sender, RoutedEventArgs e) => _viewModel.BasculerValidation();

        private async void Proposer_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.Selection == null) return;
            await _viewModel.ProposerAsync(new[] { _viewModel.Selection.Fiche.Id });
        }

        private void Supprimer_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.Selection == null) return;
            var r = MessageBox.Show(
                $"Retirer « {_viewModel.Selection.Titre} » de la bibliothèque ?\n\n" +
                "La fiche est déplacée dans le dossier _corbeille de la bibliothèque, pas effacée.",
                "Supprimer l'infographie", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r == MessageBoxResult.Yes)
                _viewModel.Supprimer();
        }

        private void Zone_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Zone_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetData(DataFormats.FileDrop) is not string[] fichiers) return;
            var images = fichiers
                .Where(f => InfographieService.ExtensionsAcceptees.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToList();
            if (images.Count == 0)
            {
                _viewModel.Statut = "Seuls les fichiers PDF, PNG et JPG peuvent être importés.";
                return;
            }
            _viewModel.Importer(images);
        }
    }
}
