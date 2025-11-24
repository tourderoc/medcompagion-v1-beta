using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MedCompanion.Models;
using MedCompanion.Services;

namespace MedCompanion.Views.Patients
{
    /// <summary>
    /// UserControl pour la liste des patients
    /// </summary>
    public partial class PatientListControl : UserControl
    {
        // Événements pour communiquer avec MainWindow
        public event EventHandler<PatientIndexEntry>? PatientSelected;
        public event EventHandler? PatientDeleted;
        public event EventHandler<string>? StatusChanged;

        private PatientIndexService? _patientIndex;
        private PathService? _pathService;

        public PatientListControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initialise le contrôle avec les services nécessaires
        /// </summary>
        public void Initialize(PatientIndexService patientIndex, PathService pathService)
        {
            _patientIndex = patientIndex;
            _pathService = pathService;
        }

        /// <summary>
        /// Charge et affiche la liste des patients
        /// Tri: Patients récemment ouverts en haut → Autres en ordre alphabétique
        /// Limite: 60 patients maximum pour performance
        /// </summary>
        public void LoadPatients()
        {
            if (_patientIndex == null)
                return;

            var patients = _patientIndex.GetAllPatients();
            var totalCount = patients.Count;
            
            var displayList = patients.Select(p =>
            {
                var lastConsult = _patientIndex.GetLastConsultationDate(p.Id);
                var recentOrder = _patientIndex.GetRecentOrder(p.Id); // -1 si pas récent, 0 = plus récent, 1 = 2e, etc.

                return new PatientDisplayInfo
                {
                    Patient = p,
                    NomComplet = p.NomComplet,
                    AgeDisplay = p.Age.HasValue ? $"{p.Age} ans" : "-",
                    LastConsultDisplay = lastConsult.HasValue
                        ? lastConsult.Value.ToString("dd/MM/yyyy")
                        : "Jamais",
                    LastConsultDate = lastConsult,
                    RecentOrder = recentOrder // Pour le tri par ordre d'ouverture
                };
            })
            // Tri intelligent: Patients récemment ouverts en priorité, puis alphabétique
            .OrderBy(p => p.RecentOrder == -1 ? int.MaxValue : p.RecentOrder) // Récents en premier (0, 1, 2...), puis autres (MaxValue)
            .ThenBy(p => p.NomComplet) // Ordre alphabétique pour les non-récents
            .Take(60) // Limiter à 60 patients pour performance
            .ToList();

            PatientsDataGrid.ItemsSource = displayList;
            
            // Afficher le compteur avec indication si limité
            if (displayList.Count < totalCount)
            {
                PatientCountTextBlock.Text = $"{displayList.Count} / {totalCount} patients (récents)";
            }
            else
            {
                PatientCountTextBlock.Text = $"{displayList.Count} patient{(displayList.Count > 1 ? "s" : "")}";
            }
        }

        #region Gestionnaires d'événements

        private void PatientsDataGrid_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Trouver la ligne DataGridRow sous le curseur
            var row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);

            if (row != null && row.Item != null)
            {
                // Forcer la sélection de la ligne
                PatientsDataGrid.SelectedItem = row.Item;
                row.IsSelected = true;

                // Donner le focus au DataGrid
                PatientsDataGrid.Focus();
            }
        }

        private void PatientsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            bool hasSelection = PatientsDataGrid.SelectedItem != null;
            InfoPatientButton.IsEnabled = hasSelection;
            DeletePatientButton.IsEnabled = hasSelection;
        }

        private void PatientsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (PatientsDataGrid.SelectedItem is PatientDisplayInfo displayInfo)
            {
                // Déclencher l'événement pour que MainWindow charge le patient
                PatientSelected?.Invoke(this, displayInfo.Patient);
            }
        }

        private void InfoPatientButton_Click(object sender, RoutedEventArgs e)
        {
            if (PatientsDataGrid.SelectedItem is not PatientDisplayInfo displayInfo)
            {
                MessageBox.Show("Veuillez sélectionner un patient.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_patientIndex == null)
                return;

            var patient = displayInfo.Patient;

            // Ouvrir le dialog d'informations patient
            var dialog = new Dialogs.PatientInfoDialog(patient, _patientIndex);
            dialog.Owner = FindVisualParent<Window>(this);
            var result = dialog.ShowDialog();

            if (result == true)
            {
                // Recharger la liste pour refléter les changements
                LoadPatients();
                StatusChanged?.Invoke(this, $"✅ Informations de {patient.NomComplet} mises à jour");
            }
        }

        private void DeletePatientButton_Click(object sender, RoutedEventArgs e)
        {
            if (PatientsDataGrid.SelectedItem is not PatientDisplayInfo displayInfo)
            {
                MessageBox.Show("Veuillez sélectionner un patient.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_patientIndex == null || _pathService == null)
                return;

            var patient = displayInfo.Patient;

            // Compter le contenu du dossier
            var (noteCount, courrierCount, attestationCount, chatCount) = CountPatientContent(patient.Id);

            var message = $"⚠️ Supprimer définitivement le dossier de {patient.NomComplet} ?\n\n" +
                         $"Contenu du dossier :\n" +
                         $"• {noteCount} note(s) clinique(s)\n" +
                         $"• {courrierCount} courrier(s)\n" +
                         $"• {attestationCount} attestation(s)\n" +
                         $"• {chatCount} échange(s) sauvegardé(s)\n\n" +
                         $"⚠️ Cette action est IRRÉVERSIBLE !\n\n" +
                         $"Êtes-vous sûr ?";

            var result = MessageBox.Show(message, "Confirmer la suppression",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);

            if (result == MessageBoxResult.Yes)
            {
                var (success, deleteMessage) = _patientIndex.DeletePatient(patient.Id);

                if (success)
                {
                    MessageBox.Show($"✅ {deleteMessage}", "Suppression réussie",
                        MessageBoxButton.OK, MessageBoxImage.Information);

                    // Recharger la liste
                    LoadPatients();

                    // Informer MainWindow
                    StatusChanged?.Invoke(this, "✅ Patient supprimé");
                    PatientDeleted?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    MessageBox.Show($"❌ {deleteMessage}", "Erreur",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Méthodes privées

        /// <summary>
        /// Compte le contenu du dossier patient
        /// </summary>
        private (int notes, int courriers, int attestations, int chats) CountPatientContent(string patientId)
        {
            try
            {
                if (_patientIndex == null || _pathService == null)
                    return (0, 0, 0, 0);

                var patient = _patientIndex.GetAllPatients().FirstOrDefault(p => p.Id == patientId);
                if (patient == null)
                    return (0, 0, 0, 0);

                var nomComplet = patient.NomComplet;
                int notes = 0, courriers = 0, attestations = 0, chats = 0;

                // Compter les notes
                var notesDir = _pathService.GetNotesDirectory(nomComplet);
                if (System.IO.Directory.Exists(notesDir))
                    notes = System.IO.Directory.GetFiles(notesDir, "*.md").Length;

                // Compter les courriers
                var courriersDir = _pathService.GetCourriersDirectory(nomComplet);
                if (System.IO.Directory.Exists(courriersDir))
                    courriers = System.IO.Directory.GetFiles(courriersDir, "*.md").Length;

                // Compter les attestations
                var attestationsDir = _pathService.GetAttestationsDirectory(nomComplet);
                if (System.IO.Directory.Exists(attestationsDir))
                    attestations = System.IO.Directory.GetFiles(attestationsDir, "*.md").Length;

                // Compter les échanges
                var chatsDir = _pathService.GetChatDirectory(nomComplet);
                if (System.IO.Directory.Exists(chatsDir))
                    chats = System.IO.Directory.GetFiles(chatsDir, "*.json").Length;

                return (notes, courriers, attestations, chats);
            }
            catch
            {
                return (0, 0, 0, 0);
            }
        }

        /// <summary>
        /// Trouve un parent visuel d'un type spécifique
        /// </summary>
        private T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
        {
            while (child != null)
            {
                if (child is T parent)
                    return parent;
                child = VisualTreeHelper.GetParent(child);
            }
            return null;
        }

        #endregion
    }

    /// <summary>
    /// Classe pour affichage dans le DataGrid
    /// </summary>
    public class PatientDisplayInfo
    {
        public PatientIndexEntry Patient { get; set; } = null!;
        public string NomComplet { get; set; } = string.Empty;
        public string AgeDisplay { get; set; } = string.Empty;
        public string LastConsultDisplay { get; set; } = string.Empty;
        public DateTime? LastConsultDate { get; set; } // Pour le tri par date de consultation
        public int RecentOrder { get; set; } // Pour le tri par ordre d'ouverture (-1 = pas récent, 0 = plus récent)
    }
}
