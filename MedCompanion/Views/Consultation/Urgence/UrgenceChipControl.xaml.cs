using System.Windows;
using System.Windows.Controls;
using MedCompanion.ViewModels;

namespace MedCompanion.Views.Consultation.Urgence
{
    public partial class UrgenceChipControl : UserControl
    {
        public UrgenceChipControl()
        {
            InitializeComponent();
        }

        private void DismissButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not UrgenceChipViewModel vm) return;

            var dlg = new Dialogs.UrgenceEcartDialog { Owner = Window.GetWindow(this) };
            var ok = dlg.ShowDialog();
            if (ok != true) return; // utilisateur a annulé → on n'écarte pas

            var motif = dlg.Motif ?? "";
            if (vm.DismissCommand.CanExecute(motif))
                vm.DismissCommand.Execute(motif);
        }
    }
}
