using System.Windows;
using MedCompanion.ViewModels;

namespace MedCompanion.Dialogs
{
    public partial class FeuilleEvaluationDialog : Window
    {
        public FeuilleEvaluationDialog(FeuilleEvaluationViewModel vm)
        {
            InitializeComponent();
            Title = vm.Title;
            DataContext = vm;
        }

        private void Annuler_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Sauvegarder_Click(object sender, RoutedEventArgs e) => DialogResult = true;
    }
}
