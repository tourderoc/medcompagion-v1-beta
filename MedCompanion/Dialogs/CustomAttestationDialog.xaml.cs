using System.Windows;

namespace MedCompanion.Dialogs
{
    public partial class CustomAttestationDialog : Window
    {
        public string? Consigne { get; private set; }

        public CustomAttestationDialog()
        {
            InitializeComponent();
            ConsigneTextBox.Focus();
        }

        private void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            var consigne = ConsigneTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(consigne))
            {
                MessageBox.Show(
                    "Veuillez décrire l'attestation souhaitée.",
                    "Consigne manquante",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            Consigne = consigne;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Consigne = null;
            DialogResult = false;
            Close();
        }
    }
}
