using System;
using System.Windows;
using MedCompanion.Models;
using MedCompanion.Services;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Dialog modal pour la création d'une ordonnance de médicaments.
    /// Encapsule le UserControl MedicamentsControl existant (réutilisation totale de la logique).
    /// </summary>
    public partial class OrdonnanceMedicamentsDialog : Window
    {
        public event EventHandler? OrdonnanceGenerated;

        public OrdonnanceMedicamentsDialog(PatientMetadata? patient)
        {
            InitializeComponent();

            // Initialiser les services nécessaires au MedicamentsControl hébergé (même init que la version inline d'OrdonnancesControl)
            var letterService     = new MedCompanion.LetterService(null!, null!, null!, null!, null!, null!, null!);
            var pathService       = new PathService();
            var storageService    = new MedCompanion.StorageService(pathService);
            var ordonnanceService = new OrdonnanceService(letterService, storageService, pathService);
            MedicamentsHostedControl.Initialize(ordonnanceService);

            if (patient != null)
            {
                MedicamentsHostedControl.SetCurrentPatient(patient);
                PatientHeaderText.Text = $"Patient : {patient.Prenom} {patient.Nom}";
            }

            // Écoute les events du contrôle hébergé pour fermer le dialog automatiquement
            MedicamentsHostedControl.OrdonnanceGenerated += (s, e) =>
            {
                OrdonnanceGenerated?.Invoke(this, EventArgs.Empty);
                DialogResult = true;
                Close();
            };

            MedicamentsHostedControl.CancelRequested += (s, e) =>
            {
                DialogResult = false;
                Close();
            };
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
