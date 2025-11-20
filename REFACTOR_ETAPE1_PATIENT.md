# 🔧 ÉTAPE 1 : Créer MainWindow.Patient.cs

## 📝 Instructions

1. **Créer un nouveau fichier** : `MedCompanion/MainWindow.Patient.cs`
2. **Copier-coller le code ci-dessous** dans ce nouveau fichier
3. **Sauvegarder** le fichier

---

## 💾 Code à copier-coller

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MedCompanion.Commands;
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.Dialogs;

namespace MedCompanion;

/// <summary>
/// Partial class MainWindow - Gestion des patients et notes cliniques
/// </summary>
public partial class MainWindow : Window
{
    // ===== RECHERCHE PATIENT =====
    
    private void OnSearchBoxPaste(object sender, DataObjectPastingEventArgs e)
    {
        // Vérifier si le presse-papiers contient du texte
        if (e.DataObject.GetDataPresent(DataFormats.UnicodeText))
        {
            // Récupérer uniquement le texte brut (pas HTML, pas RTF)
            var text = e.DataObject.GetData(DataFormats.UnicodeText) as string;
            
            // Annuler le collage par défaut qui pourrait inclure du formatage
            e.CancelCommand();
            
            // Insérer le texte brut manuellement
            if (!string.IsNullOrEmpty(text))
            {
                // Obtenir la position actuelle du curseur
                int caretIndex = SearchBox.CaretIndex;
                
                // Insérer le texte à la position du curseur
                SearchBox.Text = SearchBox.Text.Insert(caretIndex, text);
                
                // Repositionner le curseur après le texte inséré
                SearchBox.CaretIndex = caretIndex + text.Length;
            }
        }
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(SearchBox.Text))
        {
            SearchPlaceholder.Visibility = Visibility.Collapsed;
        }
    }

    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(SearchBox.Text))
        {
            SearchPlaceholder.Visibility = Visibility.Visible;
        }
    }
    
    private void CreatePatientBorder_Click(object sender, MouseButtonEventArgs e)
    {
        PatientSearchViewModel?.CreatePatientCommand?.Execute(PatientSearchViewModel?.SearchText);
    }

    // ===== CHARGEMENT PATIENT =====
    
    /// <summary>
    /// Vérifie si le patient a des notes structurées enregistrées
    /// </summary>
    private bool PatientHasStructuredNotes(string nomComplet)
    {
        try
        {
            // Utiliser PathService pour obtenir le bon dossier de notes
            var notesDir = _pathService.GetNotesDirectory(nomComplet);
            
            if (!Directory.Exists(notesDir))
                return false;
            
            // Vérifier s'il existe des fichiers .md dans le dossier notes
            var mdFiles = Directory.GetFiles(notesDir, "*.md", SearchOption.TopDirectoryOnly);
            return mdFiles.Length > 0;
        }
        catch
        {
            return false;
        }
    }
    
    private void LoadPatientAsync(PatientIndexEntry patient)
    {
        try
        {
            // Réinitialiser l'interface pour le nouveau patient
            ResetPatientUI();
            
            _selectedPatient = patient;
            
            // Afficher carte
            PatientCardPanel.Visibility = Visibility.Visible;
            
            var metadata = _patientIndex.GetMetadata(patient.Id);
            if (metadata != null)
            {
                RenderPatientCard(metadata);
            }
            else
            {
                PatientNameLabel.Text = $"{patient.Nom} {patient.Prenom}";
                PatientAgeLabel.Text = patient.Age.HasValue ? $"{patient.Age} ans" : "";
                PatientDobLabel.Text = !string.IsNullOrEmpty(patient.DobFormatted) ? $"Né(e) le {patient.DobFormatted}" : "";
                PatientSexLabel.Text = patient.Sexe == "H" ? "Homme" : patient.Sexe == "F" ? "Femme" : "";
            }
            
            // Charger notes via le ViewModel, courriers, documents, formulaires, ordonnances, synthèse et échanges sauvegardés
            NoteViewModel.LoadNotes(patient.NomComplet, _patientIndex);
            RefreshLettersList();
            LoadSavedExchanges();
            RefreshAttestationsList();
            LoadPatientDocuments();
            LoadPatientFormulaires();
            OrdonnanceViewModel.LoadOrdonnances(_selectedPatient.NomComplet);
            LoadPatientSynthesis();
            
            // Vérifier si le patient a des notes structurées
            bool hasNotes = PatientHasStructuredNotes(patient.NomComplet);
            
            // Désactiver les contrôles de courrier si pas de notes
            TemplateLetterCombo.IsEnabled = hasNotes;
            AutoAdaptAIToggle.IsEnabled = hasNotes;
            
            if (!hasNotes)
            {
                // Reset sélection ComboBox
                TemplateLetterCombo.SelectedIndex = 0;
                
                // Message explicatif
                StatusTextBlock.Text = "⚠️ Patient sans notes - Fonctionnalité courriers désactivée. Structurez d'abord une note.";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
            }
            else
            {
                StatusTextBlock.Text = $"✓ Dossier chargé: {patient.NomComplet}";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
            }
            
            // Clear search et fermer popup
            SearchBox.Text = "";
            SearchPlaceholder.Visibility = Visibility.Visible;
            SuggestPopup.IsOpen = false;
            
            // Placer le focus sur la zone de note brute pour commencer à travailler
            RawNoteText.Focus();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"❌ Erreur chargement patient: {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            
            MessageBox.Show(
                $"Erreur lors du chargement du dossier patient :\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}",
                "Erreur critique",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    // ===== LISTE PATIENTS =====
    
    private void LoadPatientsInPanel()
    {
        try
        {
            var allPatients = _patientIndex.GetAllPatients();
            var patientDisplayList = new List<PatientDisplayInfo>();
            
            foreach (var patient in allPatients)
            {
                var metadata = _patientIndex.GetMetadata(patient.Id);
                var lastConsult = _patientIndex.GetLastConsultationDate(patient.Id);
                var creationDate = _patientIndex.GetCreationDate(patient.Id);
                
                patientDisplayList.Add(new PatientDisplayInfo
                {
                    Patient = patient,
                    AgeDisplay = metadata?.Age.HasValue == true ? $"{metadata.Age} ans" : "-",
                    LastConsultDisplay = lastConsult.HasValue ? lastConsult.Value.ToString("dd/MM/yyyy") : "Jamais",
                    CreationDisplay = creationDate.ToString("dd/MM/yyyy"),
                    LastConsultDate = lastConsult,
                    CreationDate = creationDate
                });
            }
            
            // Appliquer tri par défaut (Nom A→Z)
            ApplyPatientSorting("NomAsc", patientDisplayList);
            
            // Mettre à jour compteur
            UpdatePatientCount(patientDisplayList.Count);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"❌ Erreur chargement patients: {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
        }
    }
    
    // ===== TOGGLE LISTE PATIENTS =====
    
    private void TogglePatientsBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_isPatientsListVisible)
        {
            // MASQUER la liste
            PatientsPanelColumn.Width = new GridLength(0);
            TogglePatientsBtn.Content = "▶";
            _isPatientsListVisible = false;
            
            StatusTextBlock.Text = "📋 Liste patients masquée";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Gray);
        }
        else
        {
            // AFFICHER la liste
            PatientsPanelColumn.Width = new GridLength(220);
            TogglePatientsBtn.Content = "◀";
            _isPatientsListVisible = true;
            
            // Charger les patients si ce n'est pas déjà fait
            if (PatientsDataGrid.ItemsSource == null)
            {
                LoadPatientsInPanel();
            }
            
            StatusTextBlock.Text = "📋 Liste patients affichée";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Gray);
        }
    }
    
    private void ApplyPatientSorting(string sortMode, List<PatientDisplayInfo> patients)
    {
        IEnumerable<PatientDisplayInfo> sorted = patients;
        
        switch (sortMode)
        {
            case "NomAsc":
                sorted = patients.OrderBy(p => p.Patient.Nom).ThenBy(p => p.Patient.Prenom);
                break;
            case "NomDesc":
                sorted = patients.OrderByDescending(p => p.Patient.Nom).ThenByDescending(p => p.Patient.Prenom);
                break;
            case "ConsultDesc":
                sorted = patients.OrderByDescending(p => p.LastConsultDate ?? DateTime.MinValue);
                break;
            case "ConsultAsc":
                sorted = patients.OrderBy(p => p.LastConsultDate ?? DateTime.MaxValue);
                break;
        }
        
        PatientsDataGrid.ItemsSource = sorted.ToList();
    }
    
    private void UpdatePatientCount(int count)
    {
        PatientCountTextBlock.Text = count == 0 ? "Aucun patient" :
                                     count == 1 ? "1 patient" :
                                     $"{count} patients";
    }
    
    private void PatientsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool hasSelection = PatientsDataGrid.SelectedItem != null;
        DeletePatientButton.IsEnabled = hasSelection;
    }
    
    private void PatientsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PatientsDataGrid.SelectedItem is PatientDisplayInfo displayInfo)
        {
            LoadPatientAsync(displayInfo.Patient);
        }
    }
    
    private async void DeletePatientButton_Click(object sender, RoutedEventArgs e)
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
        
        var message = $"⚠️ Supprimer définitivement le dossier de {patient.NomComplet} ?\n\n" +
                     $"Contenu du dossier :\n" +
                     $"• {noteCount} note(s) clinique(s)\n" +
                     $"• {courrierCount} courrier(s)\n" +
                     $"• {attestationCount} attestation(s)\n" +
