using System.Windows;
using MedCompanion.ViewModels;

namespace MedCompanion.Dialogs
{
    public partial class ProfileEvaluationDialog : Window
    {
        public ProfileEvaluationDialog(ProfileEvaluationViewModel vm)
        {
            InitializeComponent();
            Title = vm.SphereTitle;
            DataContext = vm;
        }

        private void Annuler_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Sauvegarder_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is ProfileEvaluationViewModel vm)
                vm.ApplyToProfile();
            DialogResult = true;
        }
    }
}
