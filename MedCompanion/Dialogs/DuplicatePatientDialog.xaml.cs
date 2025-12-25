using System.Windows;
using MedCompanion.Models;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Résultat du dialogue de confirmation de doublon
    /// </summary>
    public enum DuplicateDialogResult
    {
        UseExisting,    // Utiliser le patient existant
        CreateAnyway,   // Créer quand même le nouveau patient
        Cancel          // Annuler l'opération
    }

    /// <summary>
    /// Dialogue affiché quand un patient similaire est détecté
    /// </summary>
    public partial class DuplicatePatientDialog : Window
    {
        public DuplicateDialogResult Result { get; private set; } = DuplicateDialogResult.Cancel;
        public string ExistingPatientId { get; private set; } = string.Empty;

        /// <summary>
        /// Constructeur avec informations des deux patients (existant et nouveau)
        /// </summary>
        public DuplicatePatientDialog(
            string existingId,
            string existingName,
            string existingDob,
            string newName,
            string newDob,
            string newId)
        {
            InitializeComponent();

            ExistingPatientId = existingId;

            // Remplir les informations du patient existant
            ExistingNameText.Text = existingName;
            ExistingDobText.Text = !string.IsNullOrEmpty(existingDob) ? existingDob : "Non renseignée";
            ExistingIdText.Text = existingId;

            // Remplir les informations du nouveau patient
            NewNameText.Text = newName;
            NewDobText.Text = !string.IsNullOrEmpty(newDob) ? newDob : "Non renseignée";
            NewIdText.Text = newId;
        }

        private void UseExistingButton_Click(object sender, RoutedEventArgs e)
        {
            Result = DuplicateDialogResult.UseExisting;
            DialogResult = true;
            Close();
        }

        private void CreateAnywayButton_Click(object sender, RoutedEventArgs e)
        {
            Result = DuplicateDialogResult.CreateAnyway;
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Result = DuplicateDialogResult.Cancel;
            DialogResult = false;
            Close();
        }
    }
}
