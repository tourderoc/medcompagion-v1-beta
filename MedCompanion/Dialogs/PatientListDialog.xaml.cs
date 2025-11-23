using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using MedCompanion.Models;
using MedCompanion.Services;

namespace MedCompanion.Dialogs
{
    public partial class PatientListDialog : Window
    {
        private readonly PatientIndexService _patientIndex;
        private List<PatientDisplayInfo> _allPatients = new();
        
        public PatientIndexEntry? SelectedPatient { get; private set; }
        
        public PatientListDialog(PatientIndexService patientIndex)
        {
            InitializeComponent();
            _patientIndex = patientIndex;
            
            Loaded += PatientListDialog_Loaded;
            PatientsDataGrid.SelectionChanged += PatientsDataGrid_SelectionChanged;
            
            // Handler pour la touche Escape (fermeture rapide)
            this.KeyDown += PatientListDialog_KeyDown;
        }
        
        private void PatientListDialog_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                e.Handled = true;
            }
            else if (e.Key == Key.Space)
            {
                // Bloquer la barre espace pour qu'elle ne ferme pas la fenêtre
                e.Handled = true;
            }
        }
        
    private void PatientListDialog_Loaded(object sender, RoutedEventArgs e)
    {
        LoadPatients();

        // Forcer le focus sur la fenêtre pour éviter le problème du double-clic
        this.Focus();
        this.Focusable = true;
    }
        
        private void LoadPatients()
        {
            try
            {
                // Récupérer tous les patients
                var allPatients = _patientIndex.GetAllPatients();
                
                // Convertir en PatientDisplayInfo avec calculs
                _allPatients = new List<PatientDisplayInfo>();
                
                foreach (var patient in allPatients)
                {
                    var metadata = _patientIndex.GetMetadata(patient.Id);
                    var lastConsult = _patientIndex.GetLastConsultationDate(patient.Id);
                    var creationDate = _patientIndex.GetCreationDate(patient.Id);
                    
                    _allPatients.Add(new PatientDisplayInfo
                    {
                        Patient = patient,
                        AgeDisplay = metadata?.Age.HasValue == true ? $"{metadata.Age} ans" : "-",
                        LastConsultDisplay = lastConsult.HasValue ? lastConsult.Value.ToString("dd/MM/yyyy") : "Jamais",
                        CreationDisplay = creationDate.ToString("dd/MM/yyyy"),
                        LastConsultDate = lastConsult,
                        CreationDate = creationDate
                    });
                }
                
                // Trier par défaut (Nom A→Z)
                ApplySorting("NomAsc");
                
                // Mettre à jour le compteur
                UpdatePatientCount();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur chargement patients : {ex.Message}", "Erreur", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void SortComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (SortComboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item && 
                item.Tag is string sortMode)
            {
                ApplySorting(sortMode);
            }
        }
        
        private void ApplySorting(string sortMode)
        {
            IEnumerable<PatientDisplayInfo> sorted = _allPatients;
            
            switch (sortMode)
            {
                case "NomAsc":
                    sorted = _allPatients.OrderBy(p => p.Patient.Nom).ThenBy(p => p.Patient.Prenom);
                    break;
                    
                case "NomDesc":
                    sorted = _allPatients.OrderByDescending(p => p.Patient.Nom).ThenByDescending(p => p.Patient.Prenom);
                    break;
                    
                case "ConsultDesc":
                    sorted = _allPatients.OrderByDescending(p => p.LastConsultDate ?? DateTime.MinValue);
                    break;
                    
                case "ConsultAsc":
                    sorted = _allPatients.OrderBy(p => p.LastConsultDate ?? DateTime.MaxValue);
                    break;
                    
                case "CreationDesc":
                    sorted = _allPatients.OrderByDescending(p => p.CreationDate);
                    break;
                    
                case "CreationAsc":
                    sorted = _allPatients.OrderBy(p => p.CreationDate);
                    break;
            }
            
            PatientsDataGrid.ItemsSource = sorted.ToList();
        }
        
        private void PatientsDataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            bool hasSelection = PatientsDataGrid.SelectedItem != null;
            OpenButton.IsEnabled = hasSelection;
            DeleteButton.IsEnabled = hasSelection;
        }
        
        private void OpenButton_Click(object sender, RoutedEventArgs e)
        {
            if (PatientsDataGrid.SelectedItem is PatientDisplayInfo displayInfo)
            {
                SelectedPatient = displayInfo.Patient;
                DialogResult = true;
            }
        }
        
        private async void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (PatientsDataGrid.SelectedItem is not PatientDisplayInfo displayInfo)
            {
                MessageBox.Show("Veuillez sélectionner un patient.", "Information", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            
            var patient = displayInfo.Patient;
            
            // Compter le contenu du dossier
            var (noteCount, courrierCount, attestationCount, chatCount) = CountPatientContent(patient.Id);
            
            // Message de confirmation détaillé
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
                }
                else
                {
                    MessageBox.Show($"❌ {deleteMessage}", "Erreur", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        
        private (int notes, int courriers, int attestations, int chats) CountPatientContent(string patientId)
        {
            try
            {
                var patient = _patientIndex.GetAllPatients().FirstOrDefault(p => p.Id == patientId);
                if (patient == null)
                    return (0, 0, 0, 0);
                
                int noteCount = 0;
                int courrierCount = 0;
                int attestationCount = 0;
                int chatCount = 0;
                
                // Compter notes (tous les .md dans les dossiers année)
                foreach (var yearDir in Directory.GetDirectories(patient.DirectoryPath)
                    .Where(d => int.TryParse(Path.GetFileName(d), out _)))
                {
                    noteCount += Directory.GetFiles(yearDir, "*.md", SearchOption.AllDirectories).Length;
                }
                
                // Compter courriers
                var courriersDir = Path.Combine(patient.DirectoryPath, "courriers");
                if (Directory.Exists(courriersDir))
                {
                    courrierCount = Directory.GetFiles(courriersDir, "*.md", SearchOption.TopDirectoryOnly).Length;
                }
                
                // Compter attestations
                var attestationsDir = Path.Combine(patient.DirectoryPath, "attestations");
                if (Directory.Exists(attestationsDir))
                {
                    attestationCount = Directory.GetFiles(attestationsDir, "*.md", SearchOption.TopDirectoryOnly).Length;
                }
                
                // Compter chats
                var chatDir = Path.Combine(patient.DirectoryPath, "chat");
                if (Directory.Exists(chatDir))
                {
                    chatCount = Directory.GetFiles(chatDir, "*.json", SearchOption.TopDirectoryOnly).Length;
                }
                
                return (noteCount, courrierCount, attestationCount, chatCount);
            }
            catch
            {
                return (0, 0, 0, 0);
            }
        }
        
        private void UpdatePatientCount()
        {
            var count = _allPatients.Count;
            PatientCountLabel.Text = count == 0 ? "Aucun patient" :
                                    count == 1 ? "1 patient" :
                                    $"{count} patients";
        }
        
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
    
    /// <summary>
    /// Classe pour l'affichage dans le DataGrid
    /// </summary>
    public class PatientDisplayInfo
    {
        public PatientIndexEntry Patient { get; set; } = null!;
        public string NomComplet => Patient.NomComplet;
        public string AgeDisplay { get; set; } = string.Empty;
        public string LastConsultDisplay { get; set; } = string.Empty;
        public string CreationDisplay { get; set; } = string.Empty;
        public DateTime? LastConsultDate { get; set; }
        public DateTime CreationDate { get; set; }
    }
}
