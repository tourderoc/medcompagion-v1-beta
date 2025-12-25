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
public partial class MainWindow : Window
{
    // NOTE: OnSearchBoxPaste, SearchBox_GotFocus, SearchBox_LostFocus
    // Ces méthodes sont maintenant gérées par PatientSearchControl


    private void CreatePatientBorder_Click(object sender, MouseButtonEventArgs e)
    {
        PatientSearchViewModel?.CreatePatientCommand?.Execute(PatientSearchViewModel?.SearchText);
    }

    private void DuplicateIndicator_MainWindow_Click(object sender, MouseButtonEventArgs e)
    {
        if (_selectedPatient == null || _currentPatientDuplicate == null)
            return;

        var result = MessageBox.Show(
            $"Ce patient a un doublon détecté (score: {_currentPatientDuplicateScore}/210):\n\n" +
            $"Patient actuel:\n{_selectedPatient.Nom} {_selectedPatient.Prenom}\n" +
            $"Né(e) le: {_selectedPatient.DobFormatted}\n\n" +
            $"Doublon détecté:\n{_currentPatientDuplicate.Nom} {_currentPatientDuplicate.Prenom}\n" +
            $"Né(e) le: {_currentPatientDuplicate.DobFormatted}\n\n" +
            $"Voulez-vous charger le doublon pour vérification?",
            "Doublon détecté",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            LoadPatientAsync(_currentPatientDuplicate);
        }
    }
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

            // Ajouter à l'historique des patients récemment ouverts
            _patientIndex.AddRecentPatient(patient.Id);

            // Afficher carte
            PatientCardPanel.Visibility = Visibility.Visible;

            var metadata = _patientIndex.GetMetadata(patient.Id);
            if (metadata != null)
            {
                RenderPatientCard(metadata);
                
                // Initialiser le AttestationViewModel avec les métadonnées du patient
                AttestationViewModel.CurrentPatient = metadata;
            }
            else
            {
                PatientNameLabel.Text = $"{patient.Nom} {patient.Prenom}";
                PatientAgeLabel.Text = patient.Age.HasValue ? $"{patient.Age} ans" : "";
                PatientDobLabel.Text = !string.IsNullOrEmpty(patient.DobFormatted) ? $"Né(e) le {patient.DobFormatted}" : "";
                PatientSexLabel.Text = patient.Sexe == "H" ? "Homme" : patient.Sexe == "F" ? "Femme" : "";
                
                // Initialiser quand même le ViewModel avec les données disponibles
                AttestationViewModel.CurrentPatient = new PatientMetadata
                {
                    Nom = patient.Nom,
                    Prenom = patient.Prenom,
                    Dob = patient.Dob,
                    Sexe = patient.Sexe
                };
            }

            // Charger notes via le ViewModel, courriers, documents, formulaires, ordonnances, synthèse et échanges sauvegardés
            NoteViewModel.LoadNotes(patient.NomComplet, _patientIndex);
            // RefreshLettersList(); // MIGRÉ vers CourriersControl - appelé via SetCurrentPatient
            // LoadSavedExchanges(); // MIGRÉ vers ChatControl - appelé via SetCurrentPatient
            // RefreshAttestationsList(); // MIGRÉ vers AttestationsControl - géré par le ViewModel
            // LoadPatientDocuments(); // MIGRÉ vers DocumentsControl - appelé via SetCurrentPatient

            // MIGRÉ vers DocumentsControl - Charger les documents via SetCurrentPatient
            DocumentsControlPanel.SetCurrentPatient(_selectedPatient);

            // MIGRÉ vers FormulairesControl - Charger les formulaires via SetCurrentPatient
            FormulairesControlPanel.SetCurrentPatient(_selectedPatient);

            // MIGRÉ vers CourriersControl - Charger les courriers via SetCurrentPatient
            CourriersControlPanel.SetCurrentPatient(_selectedPatient);

            // Mettre à jour le patient sélectionné dans OrdonnanceViewModel
            OrdonnanceViewModel.SelectedPatient = metadata ?? new PatientMetadata
            {
                Nom = patient.Nom,
                Prenom = patient.Prenom,
                Dob = patient.Dob,
                Sexe = patient.Sexe
            };
            OrdonnanceViewModel.LoadOrdonnances(_selectedPatient.NomComplet);

            // MIGRÉ vers NotesControl - Charger la synthèse via SetCurrentPatient
            NotesControlPanel.SetCurrentPatient(_selectedPatient);

            // Initialiser ChatControl avec le patient courant
            ChatControlPanel.SetCurrentPatient(_selectedPatient);

            // Vérifier si le patient a un doublon
            var (hasDuplicates, duplicatePatient, score) = _patientIndex.CheckForDuplicates(patient.Id);
            if (hasDuplicates && duplicatePatient != null)
            {
                _currentPatientDuplicate = duplicatePatient;
                _currentPatientDuplicateScore = score;
                DuplicateIndicator.Visibility = Visibility.Visible;
            }
            else
            {
                _currentPatientDuplicate = null;
                _currentPatientDuplicateScore = 0;
                DuplicateIndicator.Visibility = Visibility.Collapsed;
            }

            // Vérifier si le patient a des notes structurées
            bool hasNotes = PatientHasStructuredNotes(patient.NomComplet);

            if (!hasNotes)
            {
                // Message explicatif
                StatusTextBlock.Text = "⚠️ Patient sans notes - Structurez d'abord une note.";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
            }
            else
            {
                StatusTextBlock.Text = $"✓ Dossier chargé: {patient.NomComplet}";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
            }

            // Clear search (géré par le ViewModel maintenant)
            if (PatientSearchViewModel != null)
            {
                PatientSearchViewModel.SearchText = "";
            }

            // Placer le focus sur la zone de note brute pour commencer à travailler
            NotesControlPanel.RawNoteTextBox.Focus();
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
    private void LoadPatientsInPanel()
    {
        // MIGRÉ vers PatientListControl
        PatientListControlPanel.LoadPatients();
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

            // Charger les patients
            LoadPatientsInPanel();

            StatusTextBlock.Text = "📋 Liste patients affichée";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Gray);
        }
    }


    // ===== BLOC PATIENTLISTCONTROL SUPPRIMÉ =====
    // Code migré vers Views/Patients/PatientListControl.xaml.cs
    // Supprimé le 23/11/2025 après validation

    private void AnalysePromptsBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Vérifier si la fenêtre existe déjà
            if (_promptsDialog != null)
            {
                // Fenêtre déjà ouverte → La mettre au premier plan
                _promptsDialog.Activate();
                _promptsDialog.Focus();

                StatusTextBlock.Text = "✓ Fenêtre Prompts déjà ouverte - Mise au premier plan";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);
                return;
            }

            StatusTextBlock.Text = "⏳ Ouverture...";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);

            _promptsDialog = new PromptsAnalysisDialog(_promptConfigService, _promptReformulationService); // ✅ Passer les instances partagées
            _promptsDialog.Owner = this;

            // Nettoyer la référence quand la fenêtre est fermée
            _promptsDialog.Closed += (s, args) => _promptsDialog = null;

            _promptsDialog.Show();  // Non-modal: permet d'utiliser l'app principale en même temps

            StatusTextBlock.Text = "✓ Dialogue ouvert (non-modal)";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
        }
        catch (Exception ex)
        {
            // Nettoyer la référence en cas d'erreur
            _promptsDialog = null;

            MessageBox.Show(
                $"Erreur lors de l'ouverture du dialogue :\n\n{ex.Message}\n\nStack Trace:\n{ex.StackTrace}",
                "Erreur d'initialisation",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );

            StatusTextBlock.Text = $"❌ Erreur: {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
        }
    }
    private void OpenPatientFolderBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPatient != null)
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", _selectedPatient.DirectoryPath);
                StatusTextBlock.Text = "📁 Dossier ouvert";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"❌ {ex.Message}";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            }
        }
    }
    private async void StructurerButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPatient == null)
        {
            StatusTextBlock.Text = "⚠️ Veuillez d'abord sélectionner un patient.";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
            return;
        }

        if (string.IsNullOrWhiteSpace(NotesControlPanel.RawNoteTextBox.Text))
        {
            StatusTextBlock.Text = "⚠️ Veuillez saisir une note brute.";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
            return;
        }

        // Désactiver boutons pendant génération
        NotesControlPanel.StructurerBtn.IsEnabled = false;
        NotesControlPanel.ValiderSauvegarderBtn.IsEnabled = false;
        NotesControlPanel.ValiderSauvegarderBtn.Background = new SolidColorBrush(Color.FromRgb(189, 195, 199)); // Gris
        
        StatusTextBlock.Text = "⏳ Structuration en cours...";
        StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);
        NotesControlPanel.StructuredNoteTextBox.Document = new FlowDocument();

        try
        {
            // ✅ Récupérer le sexe du patient
            var sexe = _selectedPatient.Sexe ?? "M";

            // ✅ APPEL MODIFIÉ : Passer le sexe
            var (success, result, relevanceWeight) = await _openAIService.StructurerNoteAsync(
                _selectedPatient.NomComplet,
                sexe,  // ✅ NOUVEAU paramètre
                NotesControlPanel.RawNoteTextBox.Text.Trim()
            );

            if (success)
            {
                // NOUVEAU : Stocker le poids de pertinence pour l'utiliser lors de la sauvegarde
                _lastNoteRelevanceWeight = relevanceWeight;

                // Nouvelle note structurée → Mode édition direct avec formatage
                NotesControlPanel.StructuredNoteTextBox.IsReadOnly = false;
                NotesControlPanel.StructuredNoteTextBox.Background = new SolidColorBrush(Colors.White);
                
                try
                {
                    // Convertir Markdown en FlowDocument formaté
                    NotesControlPanel.StructuredNoteTextBox.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(result);
                }
                catch
                {
                    // Si erreur, utiliser texte brut
                    var doc = new FlowDocument();
                    doc.Blocks.Add(new Paragraph(new Run(result)));
                    NotesControlPanel.StructuredNoteTextBox.Document = doc;
                }
                
                // NE PAS contrôler manuellement la visibilité - le binding MVVM s'en charge !
                NotesControlPanel.ValiderSauvegarderBtn.Background = new SolidColorBrush(Color.FromRgb(39, 174, 96)); // Vert
                
                StatusTextBlock.Text = "✓ Note structurée avec succès";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
            }
            else
            {
                StatusTextBlock.Text = $"❌ {result}";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"❌ {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
        }
        finally
        {
            NotesControlPanel.StructurerBtn.IsEnabled = true;
        }
    }
private void OnNoteStatusChanged(object sender, string message)
    {
        StatusTextBlock.Text = message;
        
        // Gérer les couleurs selon le préfixe du message
        if (message.StartsWith("✅") || message.StartsWith("✓"))
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
        else if (message.StartsWith("❌"))
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
        else if (message.StartsWith("⏳"))
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);
        else if (message.StartsWith("⚠️") || message.StartsWith("❓"))
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
        else if (message.StartsWith("✏️"))
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
        else
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Gray);
    }
  private void OnNoteContentLoaded(object sender, (string filePath, string content) data)
    {
        var (filePath, content) = data;
        
        try
        {
            // Convertir Markdown → FlowDocument
            NotesControlPanel.StructuredNoteTextBox.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(content);
            
            // Mode lecture seule
            NotesControlPanel.StructuredNoteTextBox.IsReadOnly = true;
            NotesControlPanel.StructuredNoteTextBox.Background = new SolidColorBrush(Color.FromRgb(250, 250, 250));
            
            // NE PAS contrôler manuellement la visibilité - le binding MVVM s'en charge !
        }
        catch (Exception ex)
        {
            // En cas d'erreur, afficher texte brut
            var fallbackDoc = new FlowDocument();
            fallbackDoc.Blocks.Add(new Paragraph(new Run(content)));
            NotesControlPanel.StructuredNoteTextBox.Document = fallbackDoc;
            
            StatusTextBlock.Text = $"⚠️ Erreur formatage: {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
        }
    }
    
    private void OnNoteStructured(object sender, string markdown)
    {
        try
        {
            // Convertir Markdown → FlowDocument
            NotesControlPanel.StructuredNoteTextBox.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(markdown);
            
            // Mode édition (nouvelle note)
            NotesControlPanel.StructuredNoteTextBox.IsReadOnly = false;
            NotesControlPanel.StructuredNoteTextBox.Background = new SolidColorBrush(Colors.White);
            
            // NE PAS contrôler manuellement la visibilité - le binding MVVM s'en charge !
            NotesControlPanel.ValiderSauvegarderBtn.Background = new SolidColorBrush(Color.FromRgb(39, 174, 96)); // Vert
        }
        catch (Exception ex)
        {
            // Si erreur, utiliser texte brut
            var doc = new FlowDocument();
            doc.Blocks.Add(new Paragraph(new Run(markdown)));
            NotesControlPanel.StructuredNoteTextBox.Document = doc;
            
            StatusTextBlock.Text = $"⚠️ Erreur formatage: {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
        }
    }
    
    private void OnNoteSaveRequested(object sender, string? filePath)
    {
        // Convertir FlowDocument → Markdown
        var markdown = MarkdownFlowDocumentConverter.FlowDocumentToMarkdown(NotesControlPanel.StructuredNoteTextBox.Document);
        
        // Appeler CompleteSave du ViewModel
        NoteViewModel.CompleteSave(markdown);
    }
    
    private void OnNoteDeleteRequested(object sender, string filePath)
    {
        // Afficher dialogue de confirmation
        var result = MessageBox.Show(
            $"Êtes-vous sûr de vouloir supprimer cette note ?\n\n{Path.GetFileName(filePath)}",
            "Confirmer la suppression",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );
        
        if (result == MessageBoxResult.Yes)
        {
            NoteViewModel.CompleteDelete();
        }
    }
    
    /// <summary>
    /// Handler pour rafraîchir la liste des PATIENTS après sauvegarde d'une note
    /// Cela met à jour la colonne "Dernière note" dans la liste des patients
    /// </summary>
    private void OnPatientListRefreshRequested(object sender, EventArgs e)
    {
        // Recharger la liste des patients pour mettre à jour la colonne "Dernière note"
        LoadPatientsInPanel();
    }
    
    /// <summary>
    /// Handler appelé après sauvegarde d'une note
    /// Re-vérifie si le patient a des notes pour activer le menu courriers
    /// </summary>
    private void OnNoteSaved(object sender, EventArgs e)
    {
        // Re-vérifier si le patient a des notes après sauvegarde
        if (_selectedPatient != null)
        {
            bool hasNotes = PatientHasStructuredNotes(_selectedPatient.NomComplet);

            if (hasNotes)
            {
                StatusTextBlock.Text += " - Courriers disponibles";
            }

            // NOUVEAU : Rafraîchir le badge de notification de synthèse
            NotesControlPanel.SynthesisViewModel?.UpdateNotificationBadge();

            // NOUVEAU : La barre de progression de poids est automatiquement mise à jour par le ViewModel
            System.Diagnostics.Debug.WriteLine("[MainWindow.OnNoteSaved] Badge et poids mis à jour via ViewModel");
        }
    }
    
    /// <summary>
    /// Handler pour vider le RichTextBox après sauvegarde
    /// </summary>
    private void OnNoteClearedAfterSave(object sender, EventArgs e)
    {
        // Forcer le vidage du RichTextBox en créant un nouveau FlowDocument vide
        NotesControlPanel.StructuredNoteTextBox.Document = new FlowDocument();
    }

    /// <summary>
    /// Handler pour forcer la synchronisation du RichTextBox avec le ViewModel
    /// Le binding WPF standard ne fonctionne pas bien avec RichTextBox.IsReadOnly
    /// </summary>
   
    private void NoteViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NoteViewModel.IsStructuredNoteReadOnly))
        {
            // Forcer la mise à jour du RichTextBox
            NotesControlPanel.StructuredNoteTextBox.IsReadOnly = NoteViewModel.IsStructuredNoteReadOnly;

            // Changer aussi le Background pour indiquer visuellement le mode
            if (NoteViewModel.IsStructuredNoteReadOnly)
            {
                NotesControlPanel.StructuredNoteTextBox.Background = new SolidColorBrush(Color.FromRgb(250, 250, 250)); // Gris clair (lecture seule)
            }
            else
            {
                NotesControlPanel.StructuredNoteTextBox.Background = new SolidColorBrush(Colors.White); // Blanc (édition)

                // IMPORTANT: Quand on passe en mode édition, activer immédiatement le bouton Sauvegarder
                // Le handler TextChanged ne se déclenche pas au changement de ReadOnly
                if (NoteViewModel != null)
                {
                    NoteViewModel.IsSauvegarderButtonEnabled = true;
                    ((RelayCommand)NoteViewModel.SaveCommand).RaiseCanExecuteChanged();
                }
            }
        }
    }
    private void StructuredNoteText_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Si on est en mode édition (pas readonly), activer le bouton Sauvegarder
        if (!NotesControlPanel.StructuredNoteTextBox.IsReadOnly && NoteViewModel != null)
        {
            NoteViewModel.IsSauvegarderButtonEnabled = true;

            // IMPORTANT: Forcer la réévaluation du Command, même si la valeur était déjà true
            // (car le setter ne déclenche pas de notification si la valeur ne change pas)
            ((RelayCommand)NoteViewModel.SaveCommand).RaiseCanExecuteChanged();
        }
    }
    private void EnterConsultationMode(string content)
    {

        // Trouver le Grid parent pour modifier les RowDefinitions
        var parentGrid = FindParentGrid(NotesControlPanel.StructuredNoteTextBox);
        if (parentGrid != null)
        {
            // Sauvegarder la référence
            _notesGrid = parentGrid;

            // Masquer Row 0 (Synthèse), Row 1 (séparateur), Row 2 (Actions), Row 3 (Note brute) et Row 4 (séparateur)
            parentGrid.RowDefinitions[0].Height = new GridLength(0); // Synthèse
            parentGrid.RowDefinitions[1].Height = new GridLength(0); // Séparateur
            parentGrid.RowDefinitions[2].Height = new GridLength(0); // Actions
            parentGrid.RowDefinitions[3].Height = new GridLength(0); // Note brute
            parentGrid.RowDefinitions[4].Height = new GridLength(0); // Séparateur
        }

        // Masquer le label "Note brute" et le label "Note structurée"
        NotesControlPanel.RawNoteLabelBlock.Visibility = Visibility.Collapsed;
        NotesControlPanel.StructuredNoteLabelBlock.Visibility = Visibility.Collapsed;

        // Masquer bouton Structurer
        NotesControlPanel.StructurerBtn.Visibility = Visibility.Collapsed;

        // Afficher bouton Fermer
        NotesControlPanel.FermerConsultationBtn.Visibility = Visibility.Visible;

        // Charger en mode lecture seule avec contenu formaté (Markdown → FlowDocument)
        NotesControlPanel.StructuredNoteTextBox.IsReadOnly = true;
        NotesControlPanel.StructuredNoteTextBox.Background = new SolidColorBrush(Color.FromRgb(250, 250, 250));

        try
        {
            // Convertir Markdown en FlowDocument formaté
            NotesControlPanel.StructuredNoteTextBox.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(content);
        }
        catch (Exception ex)
        {
            // En cas d'erreur, afficher le texte brut
            var fallbackDoc = new FlowDocument();
            fallbackDoc.Blocks.Add(new Paragraph(new Run(content)));
            NotesControlPanel.StructuredNoteTextBox.Document = fallbackDoc;

            StatusTextBlock.Text = $"⚠️ Erreur formatage: {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
        }

        // NE PAS contrôler manuellement la visibilité - le binding MVVM s'en charge !
    }
      private void FermerConsultationButton_Click(object sender, RoutedEventArgs e)
    {
        ExitConsultationMode();
    }

    /// <summary>
    /// Désactive le mode consultation (retour à l'affichage normal)
    /// </summary>
    private void ExitConsultationMode()
    {
        // Restaurer les RowDefinitions
        if (_notesGrid != null)
        {
            // Row 0: Synthèse (Auto)
            _notesGrid.RowDefinitions[0].Height = GridLength.Auto;

            // Row 1: Séparateur (10px)
            _notesGrid.RowDefinitions[1].Height = new GridLength(10);

            // Row 2: Actions (Auto)
            _notesGrid.RowDefinitions[2].Height = GridLength.Auto;

            // Row 3: Note brute (MinHeight=80, MaxHeight=120)
            _notesGrid.RowDefinitions[3].Height = new GridLength(1, GridUnitType.Star);
            _notesGrid.RowDefinitions[3].MinHeight = 80;
            _notesGrid.RowDefinitions[3].MaxHeight = 120;

            // Row 4: Séparateur (3px)
            _notesGrid.RowDefinitions[4].Height = new GridLength(3);

            // Row 5: Note structurée (*)
            _notesGrid.RowDefinitions[5].Height = new GridLength(1, GridUnitType.Star);
        }

        // Réafficher les labels
        NotesControlPanel.RawNoteLabelBlock.Visibility = Visibility.Visible;
        NotesControlPanel.StructuredNoteLabelBlock.Visibility = Visibility.Visible;

        // Réafficher bouton Structurer
        NotesControlPanel.StructurerBtn.Visibility = Visibility.Visible;

        // Masquer bouton Fermer
        NotesControlPanel.FermerConsultationBtn.Visibility = Visibility.Collapsed;

        // Vider les zones
        NotesControlPanel.RawNoteTextBox.Text = string.Empty;
        NotesControlPanel.StructuredNoteTextBox.Document = new FlowDocument(); // RichTextBox utilise Document

        // NE PAS contrôler manuellement la visibilité - le binding MVVM s'en charge !

        // Désélectionner la note dans la liste
        NotesList.SelectedItem = null;

        StatusTextBlock.Text = "✓ Mode normal rétabli";
        StatusTextBlock.Foreground = new SolidColorBrush(Colors.Gray);
    }
    private Grid? FindParentGrid(DependencyObject child)
    {
        var parent = System.Windows.Media.VisualTreeHelper.GetParent(child);

        while (parent != null)
        {
            if (parent is Grid grid && grid.RowDefinitions.Count == 6)
            {
                return grid;
            }
            parent = System.Windows.Media.VisualTreeHelper.GetParent(parent);
        }

        return null;
    }
    

}
