using System.Windows;

namespace MedCompanion.Dialogs
{
    public partial class SaveChatDialog : Window
    {
        public string? Etiquette { get; private set; }

        public SaveChatDialog()
        {
            InitializeComponent();
            EtiquetteTextBox.Focus();
            EtiquetteTextBox.SelectAll();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var etiquette = EtiquetteTextBox.Text.Trim();
            
            if (string.IsNullOrWhiteSpace(etiquette))
            {
                MessageBox.Show(
                    "Veuillez saisir une étiquette pour cet échange.",
                    "Étiquette requise",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                EtiquetteTextBox.Focus();
                return;
            }

            Etiquette = etiquette;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
