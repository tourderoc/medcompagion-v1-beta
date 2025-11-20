using System.Windows;

namespace MedCompanion.Dialogs
{
    public partial class SelectGenderDialog : Window
    {
        public string? SelectedGender { get; private set; }

        public SelectGenderDialog()
        {
            InitializeComponent();
        }

        private void ValidateButton_Click(object sender, RoutedEventArgs e)
        {
            // Déterminer le sexe sélectionné
            if (FemininRadio.IsChecked == true)
            {
                SelectedGender = "F";
            }
            else
            {
                SelectedGender = "H"; // Par défaut masculin
            }

            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedGender = null;
            DialogResult = false;
            Close();
        }
    }
}
