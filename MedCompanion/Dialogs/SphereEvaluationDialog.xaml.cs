using System.Windows;
using MedCompanion.ViewModels;

namespace MedCompanion.Dialogs
{
    public partial class SphereEvaluationDialog : Window
    {
        public SphereEvaluationDialog(CartographieSegmentViewModel segVm)
        {
            InitializeComponent();
            Title = $"🐛 {segVm.Label}";
            DataContext = segVm;
        }

        private void Annuler_Click(object sender, RoutedEventArgs e) => DialogResult = false;

        private void Sauvegarder_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is CartographieSegmentViewModel vm)
                vm.Segment.IsEvaluated = true;
            DialogResult = true;
        }
    }
}
