using System.Windows;
using System.Windows.Controls;

namespace MedCompanion.Dialogs
{
    public partial class AnonymizationPhase3Dialog : Window
    {
        public bool UsePhase3 { get; private set; } = false;
        public string SelectedModel { get; private set; } = "llama3";

        public AnonymizationPhase3Dialog()
        {
            InitializeComponent();
        }

        private void YesButton_Click(object sender, RoutedEventArgs e)
        {
            UsePhase3 = true;

            // Récupérer le modèle sélectionné
            if (ModelComboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                SelectedModel = selectedItem.Tag?.ToString() ?? "llama3";
            }

            DialogResult = true;
            Close();
        }

        private void NoButton_Click(object sender, RoutedEventArgs e)
        {
            UsePhase3 = false;
            DialogResult = true;
            Close();
        }
    }
}
