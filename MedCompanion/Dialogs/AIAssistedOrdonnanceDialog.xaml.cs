using System.Windows;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Dialog pour créer une ordonnance assistée par IA
    /// </summary>
    public partial class AIAssistedOrdonnanceDialog : Window
    {
        public string Demande { get; private set; }

        public AIAssistedOrdonnanceDialog()
        {
            InitializeComponent();
            Demande = string.Empty;

            // Focus sur la zone de texte
            Loaded += (s, e) => DemandeTextBox.Focus();
        }

        private void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            var demande = DemandeTextBox.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(demande))
            {
                MessageBox.Show(
                    "Veuillez entrer une description de l'ordonnance souhaitée.",
                    "Demande vide",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            Demande = demande;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
