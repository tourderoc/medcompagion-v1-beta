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
            LoadSavedExchanges();
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

            _promptsDialog = new PromptsAnalysisDialog();
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
            var (success, result, relevanceWeight) = await _openAIService.StructurerNoteAsync(
                _selectedPatient.NomComplet,
                NotesControlPanel.RawNoteTextBox.Text.Trim()
            );

            if (success)
            {
                // Réinitialiser le fichier en cours (nouvelle note)
                _currentEditingFilePath = null;

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
    
    private async void OnNotesListRefreshRequested(object sender, EventArgs e)
    {
        // Recharger la liste des notes depuis le ViewModel
        if (_selectedPatient != null)
        {
            // IMPORTANT: Délai plus long pour s'assurer que le fichier est complètement écrit
            // (certains antivirus peuvent causer des délais)
            await Task.Delay(300);
            
            // Recharger les notes
            NoteViewModel.LoadNotes(_selectedPatient.NomComplet, _patientIndex);
            
            // FORCER une mise à jour complète de l'UI en déclenchant manuellement PropertyChanged
            // Ceci force WPF à reconstruire complètement le binding
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => 
            {
                // Forcer NotesList à se reconstruire en invalidant son ItemsSource
                if (NotesList != null)
                {
                    var currentSource = NotesList.ItemsSource;
                    NotesList.ItemsSource = null;
                    NotesList.ItemsSource = currentSource;
                    NotesList.Items.Refresh();
                }
            }, System.Windows.Threading.DispatcherPriority.Render);
        }
        // Le binding ItemsSource sur NoteViewModel.Notes se met à jour automatiquement
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
            NotesControlPanel.UpdateNotificationBadge();

            // NOUVEAU : Rafraîchir aussi la barre de progression de poids
            System.Diagnostics.Debug.WriteLine("[MainWindow.OnNoteSaved] Appel de UpdateWeightIndicator()");
            NotesControlPanel.UpdateWeightIndicator();
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

        // Réinitialiser fichier en cours
        _currentEditingFilePath = null;

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
     // ===== CHAT IA =====
    
    private void ChatInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ChatSendBtn_Click(sender, e);
            e.Handled = true;
        }
    }

    private async void ChatSendBtn_Click(object sender, RoutedEventArgs e)
    {
        var question = ChatInput.Text.Trim();

        if (string.IsNullOrWhiteSpace(question))
        {
            StatusTextBlock.Text = "⚠️ Veuillez saisir une question.";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
            return;
        }

        if (_selectedPatient == null)
        {
            StatusTextBlock.Text = "⚠️ Veuillez d'abord sélectionner un patient.";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
            return;
        }

        try
        {
            AddChatMessage("Vous", question, Colors.DarkBlue);
            ChatInput.Text = string.Empty;

            ChatSendBtn.IsEnabled = false;

            // ANCIEN SYSTÈME DÉSACTIVÉ - Utiliser le banner de suggestion à la place
            // Le nouveau système détecte automatiquement l'intent pendant la frappe
            // et affiche un banner avec bouton "Générer" pour confirmation
#if false // COURRIERS CHAT - MIGRÉ VERS CourriersControl
            if (_letterService.IsLetterIntent(question))
            {
                StatusTextBlock.Text = "⏳ Génération du courrier...";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);

                var (success, markdown, error) = await _letterService.GenerateLetterAsync(_selectedPatient.NomComplet, question);

                if (success)
                {
                    // Basculer vers l'onglet Courriers
                    AssistantTabControl.SelectedIndex = 1;

                    // Afficher le brouillon dans la zone courrier dédiée (éditable) avec conversion Markdown → FlowDocument
                    LetterEditText.IsReadOnly = false;
                    LetterEditText.Background = new SolidColorBrush(Colors.White);
                    LetterEditText.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(markdown);

                    // Réinitialiser _currentEditingFilePath (nouveau brouillon)
                    _currentEditingFilePath = null;

                    // Activer bouton sauvegarder courrier
                    ModifierLetterButton.Visibility = Visibility.Collapsed;
                    SupprimerLetterButton.Visibility = Visibility.Collapsed;
                    SauvegarderLetterButton.IsEnabled = true;

                    // DÉTECTER LES PLACEHOLDERS MANQUANTS (comme pour les templates)
                    var patientMetadata = _patientIndex.GetMetadata(_selectedPatient.Id);
                    var (hasMissing, missingFields, availableInfo) = _letterService.DetectMissingInfo(
                        "Courrier via chat",  // Nom générique
                        markdown,
                        patientMetadata
                    );

                    // Si des placeholders REQUIS sont détectés → Ouvrir dialogue
                    if (hasMissing && missingFields.Any(f => f.IsRequired))
                    {
                        StatusTextBlock.Text = "❓ Informations requises manquantes...";
                        StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);

                        var dialog = new MissingInfoDialog(missingFields);
                        dialog.Owner = this;

                        if (dialog.ShowDialog() == true && dialog.CollectedInfo != null)
                        {
                            // FUSIONNER infos disponibles (École/Classe depuis metadata) + infos collectées
                            var allInfo = new Dictionary<string, string>(availableInfo);
                            foreach (var kvp in dialog.CollectedInfo)
                            {
                                allInfo[kvp.Key] = kvp.Value;
                            }

                            // RÉ-ADAPTER LE COURRIER avec l'IA (au lieu de regex)
                            StatusTextBlock.Text = "⏳ Ré-adaptation avec infos complètes...";
                            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);

                            var (success2, updatedMarkdown, error2) =
                                await _letterService.AdaptTemplateWithMissingInfoAsync(
                                    _selectedPatient.NomComplet,
                                    "Courrier via chat",
                                    markdown,  // Markdown original
                                    allInfo    // Toutes les infos fusionnées
                                );

                            if (success2 && !string.IsNullOrEmpty(updatedMarkdown))
                            {
                                // Mettre à jour l'affichage avec le markdown ré-adapté par l'IA
                                LetterEditText.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(updatedMarkdown);

                                AddChatMessage("IA", "✅ Courrier ré-adapté avec vos informations. Vous pouvez le modifier puis sauvegarder.", Colors.Green);
                                StatusTextBlock.Text = "✅ Courrier complété - Vous pouvez sauvegarder";
                                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
                            }
                            else
                            {
                                // Ré-adaptation échouée → Garder version originale
                                AddChatMessage("IA", $"⚠️ Erreur ré-adaptation: {error2}. Brouillon original conservé.", Colors.Orange);
                                StatusTextBlock.Text = $"⚠️ Erreur ré-adaptation: {error2}";
                                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
                            }
                        }
                        else
                        {
                            // Utilisateur a annulé → Garder version avec placeholders
                            AddChatMessage("IA", "⚠️ Brouillon avec placeholders. Complétez-les manuellement puis sauvegardez.", Colors.Orange);
                            StatusTextBlock.Text = "⚠️ Complétez les placeholders manuellement";
                            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
                        }
                    }
                    else
                    {
                        // Pas de placeholders manquants
                        AddChatMessage("IA", "📄 Brouillon de courrier généré dans l'onglet Courriers. Vous pouvez le modifier puis sauvegarder.", Colors.Purple);
                        StatusTextBlock.Text = "✓ Brouillon généré dans onglet Courriers";
                        StatusTextBlock.Foreground = new SolidColorBrush(Colors.Purple);
                    }
                }
                else
                {
                    AddChatMessage("Erreur", $"❌ {error}", Colors.Red);
                    StatusTextBlock.Text = $"❌ {error}";
                    StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                }
            }
#endif // COURRIERS CHAT
            // else clause devient le code principal maintenant
            {
                // Chat normal
                StatusTextBlock.Text = "⏳ L'IA réfléchit...";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);

                var (hasContext, contextText, contextInfo) = _contextLoader.GetContextBundle(
                    _selectedPatient.NomComplet,
                    null  // Ne pas passer le contenu RichTextBox au chat
                );

                string contexte = hasContext ? contextText : string.Empty;

                if (!hasContext)
                {
                    AddChatMessage("Système", "⚠️ Aucune note disponible. L'IA répondra sans contexte patient.", Colors.Gray);
                }

                var (success, result) = await _openAIService.ChatAvecContexteAsync(contexte, question, _chatHistory, null);

                if (success)
                {
                    var reponse = result;
                    if (hasContext)
                    {
                        reponse += $"\n\n━━━━━━━━━━━━━━━━━━━━━━━━━\n📎 Contexte : {contextInfo}";
                    }

                    // Ajouter à l'historique temporaire AVANT d'afficher (pour que le bouton apparaisse)
                    _chatHistory.Add(new ChatExchange
                    {
                        Question = question,
                        Response = result,
                        Timestamp = DateTime.Now
                    });

                    // Limiter à 3 échanges (FIFO)
                    if (_chatHistory.Count > 3)
                    {
                        _chatHistory.RemoveAt(0);
                    }

                    // PUIS afficher le message avec le bouton 💾
                    AddChatMessage("IA", reponse, Colors.DarkGreen);

                    // Mettre à jour l'indicateur de mémoire
                    UpdateMemoryIndicator();

                    StatusTextBlock.Text = "✓ Réponse reçue";
                    StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
                }
                else
                {
                    AddChatMessage("Erreur", result, Colors.Red);
                    StatusTextBlock.Text = $"❌ {result}";
                    StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                }
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"❌ {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
        }
        finally
        {
            ChatSendBtn.IsEnabled = true;
        }
    }

    private void SaveExchangeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPatient == null || sender is not Button button || button.Tag is not int exchangeIndex)
            return;

        if (exchangeIndex < 0 || exchangeIndex >= _chatHistory.Count)
        {
            MessageBox.Show("Échange introuvable dans l'historique.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var exchange = _chatHistory[exchangeIndex];

        // NOUVEAU : Récupérer le texte modifié depuis le TextBox affiché
        try
        {
            // Remonter dans l'arbre visuel pour trouver le Grid parent
            var parent = VisualTreeHelper.GetParent(button);
            while (parent != null && parent is not Grid)
            {
                parent = VisualTreeHelper.GetParent(parent);
            }

            if (parent is Grid messageGrid)
            {
                // Trouver le TextBox dans le Grid (colonne 0)
                foreach (var child in messageGrid.Children)
                {
                    if (child is TextBox messageBox)
                    {
                        // Récupérer le texte modifié
                        var modifiedText = messageBox.Text;
                        
                        // Séparer l'en-tête (première ligne) du contenu
                        var lines = modifiedText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        if (lines.Length > 1)
                        {
                            // Mettre à jour la réponse avec le texte modifié (sans la première ligne qui est l'en-tête)
                            exchange.Response = string.Join("\n", lines.Skip(1));
                        }
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SaveExchange] Erreur récupération texte modifié: {ex.Message}");
            // En cas d'erreur, on garde le texte original
        }

        // Ouvrir le dialog pour saisir l'étiquette
        var dialog = new SaveChatDialog();
        dialog.Owner = this;

        if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.Etiquette))
        {
            exchange.Etiquette = dialog.Etiquette;

            // Sauvegarder l'échange (avec le texte potentiellement modifié)
            var (success, message, filePath) = _storageService.SaveChatExchange(_selectedPatient.NomComplet, exchange);

            if (success)
            {
                // Ajouter à la liste des échanges sauvegardés
                _savedChatExchanges.Add(exchange);
                RefreshSavedExchangesList();

                // Désactiver le bouton de sauvegarde (déjà sauvegardé)
                button.IsEnabled = false;
                button.Background = new SolidColorBrush(Colors.Gray);
                button.ToolTip = "Échange déjà sauvegardé";

                StatusTextBlock.Text = message;
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
            }
            else
            {
                MessageBox.Show(message, "Erreur de sauvegarde", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
    private void LoadSavedExchanges()
    {
        if (_selectedPatient == null)
        {
            _savedChatExchanges.Clear();
            RefreshSavedExchangesList();
            return;
        }

        var exchanges = _storageService.GetChatExchanges(_selectedPatient.NomComplet);
        _savedChatExchanges = exchanges.ToList();
        RefreshSavedExchangesList();
    }
     private void ViewSavedExchangeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SavedExchangesList.SelectedItem is not ChatExchange exchange)
        {
            MessageBox.Show("Veuillez sélectionner un échange.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        // NOUVEAU : Vérifier si l'échange est déjà affiché dans le chat
        Border? existingBorder = null;
        foreach (var child in ChatList.Children)
        {
            if (child is Border border && border.Tag is string borderId && borderId == exchange.Id)
            {
                existingBorder = border;
                break;
            }
        }
        
        if (existingBorder != null)
        {
            // L'échange existe déjà → Scroll vers lui au lieu de le re-ajouter
            existingBorder.BringIntoView();
            
            StatusTextBlock.Text = $"✓ Conversation déjà affichée - Scroll vers l'échange du {exchange.Timestamp:dd/MM/yyyy HH:mm}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);
            return;
        }
        
        // L'échange n'existe pas encore → L'ajouter (une seule fois)
        AddChatMessage("📖 Vous (archivé)", exchange.Question, Colors.DarkBlue, exchange.Id);
        AddChatMessage("📖 IA (archivé)", exchange.Response, Colors.DarkGreen, exchange.Id);
        
        StatusTextBlock.Text = $"✓ Échange du {exchange.Timestamp:dd/MM/yyyy HH:mm} affiché";
        StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);
    }
    
    /// <summary>
    /// Supprime un échange sauvegardé
    /// </summary>
    private void DeleteSavedExchangeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SavedExchangesList.SelectedItem is not ChatExchange exchange || _selectedPatient == null)
        {
            MessageBox.Show("Veuillez sélectionner un échange.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        var result = MessageBox.Show(
            $"Supprimer cet échange ?\n\nÉtiquette : {exchange.Etiquette}\nDate : {exchange.Timestamp:dd/MM/yyyy HH:mm}",
            "Confirmer",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );
        
        if (result == MessageBoxResult.Yes)
        {
            var (success, message) = _storageService.DeleteChatExchange(_selectedPatient.NomComplet, exchange.Id);
            
            if (success)
            {
                _savedChatExchanges.Remove(exchange);
                RefreshSavedExchangesList();
                
                StatusTextBlock.Text = message;
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
            }
            else
            {
                MessageBox.Show(message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
    
    /// <summary>
    /// Rafraîchit la liste des échanges sauvegardés
    /// </summary>
    private void RefreshSavedExchangesList()
    {
        SavedExchangesList.ItemsSource = null;
        SavedExchangesList.ItemsSource = _savedChatExchanges;
    }
    
    /// <summary>
    /// Gère la sélection dans la liste des échanges sauvegardés
    /// Affiche/masque les boutons selon qu'un échange est sélectionné ou non
    /// </summary>
    private void SavedExchangesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        bool hasSelection = SavedExchangesList.SelectedItem != null;
        
        // Afficher les boutons uniquement si un échange est sélectionné
        ViewSavedExchangeBtn.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        LetterFromChatBtn.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
        DeleteSavedExchangeBtn.Visibility = hasSelection ? Visibility.Visible : Visibility.Collapsed;
    }
    
    /// <summary>
    /// Génère un courrier à partir d'une conversation sauvegardée
    /// Version refactorisée - utilise CourriersControl.SetDraft()
    /// </summary>
    private async void LetterFromChatBtn_Click(object sender, RoutedEventArgs e)
    {
        if (SavedExchangesList.SelectedItem is not ChatExchange exchange || _selectedPatient == null)
        {
            MessageBox.Show("Veuillez sélectionner une conversation.", "Information",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            // Ouvrir le dialogue pour récupérer la demande de l'utilisateur
            var dialog = new Dialogs.LetterFromChatDialog(exchange);
            dialog.Owner = this;

            if (dialog.ShowDialog() != true || string.IsNullOrEmpty(dialog.UserRequest))
                return;

            // Désactiver le bouton pendant la génération
            LetterFromChatBtn.IsEnabled = false;
            LetterFromChatBtn.Content = "⏳ Génération...";

            StatusTextBlock.Text = "⏳ Génération du courrier depuis la conversation...";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);

            // Récupérer le contexte patient
            var (hasContext, contextText, contextInfo) = _contextLoader.GetContextBundle(
                _selectedPatient.NomComplet,
                null
            );

            // Construire le prompt pour l'IA
            var conversationContext = $"**Conversation précédente:**\n\n" +
                                     $"Question: {exchange.Question}\n\n" +
                                     $"Réponse: {exchange.Response}";

            var fullContext = hasContext
                ? $"{contextText}\n\n---\n\n{conversationContext}"
                : conversationContext;

            // Générer le courrier avec l'IA
            var (success, markdown, error) = await _letterService.GenerateLetterFromChatAsync(
                _selectedPatient.NomComplet,
                fullContext,
                dialog.UserRequest
            );

            if (success && !string.IsNullOrEmpty(markdown))
            {
                // Basculer vers l'onglet Courriers
                AssistantTabControl.SelectedIndex = 1;

                // Utiliser CourriersControl pour afficher le brouillon
                CourriersControlPanel.SetDraft(markdown);

                StatusTextBlock.Text = "✅ Courrier généré depuis la conversation - Vous pouvez le modifier puis sauvegarder";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);

                MessageBox.Show(
                    "✅ Courrier généré avec succès !\n\n" +
                    "Le brouillon est maintenant affiché dans l'onglet Courriers.\n" +
                    "Vous pouvez le modifier puis le sauvegarder.",
                    "Succès",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            else
            {
                MessageBox.Show(
                    $"❌ Erreur lors de la génération:\n\n{error}",
                    "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );

                StatusTextBlock.Text = $"❌ Erreur: {error}";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Erreur lors de la génération du courrier:\n\n{ex.Message}",
                "Erreur",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );

            StatusTextBlock.Text = $"❌ Erreur: {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
        }
        finally
        {
            // Réactiver le bouton
            LetterFromChatBtn.IsEnabled = true;
            LetterFromChatBtn.Content = "📝 Courrier";
        }
    }

#if false // ANCIEN CODE - CONSERVÉ POUR RÉFÉRENCE
    /// <summary>
    /// Génère un courrier à partir d'une conversation sauvegardée (ANCIEN)
    /// </summary>
    private async void LetterFromChatBtn_Click_OLD(object sender, RoutedEventArgs e)
    {
        if (SavedExchangesList.SelectedItem is not ChatExchange exchange || _selectedPatient == null)
        {
            MessageBox.Show("Veuillez sélectionner une conversation.", "Information",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            // Ouvrir le dialogue pour récupérer la demande de l'utilisateur
            var dialog = new Dialogs.LetterFromChatDialog(exchange);
            dialog.Owner = this;
            
            if (dialog.ShowDialog() != true || string.IsNullOrEmpty(dialog.UserRequest))
                return;
            
            // Désactiver le bouton pendant la génération
            LetterFromChatBtn.IsEnabled = false;
            LetterFromChatBtn.Content = "⏳ Génération...";
            
            StatusTextBlock.Text = "⏳ Génération du courrier depuis la conversation...";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);
            
            // Récupérer le contexte patient
            var (hasContext, contextText, contextInfo) = _contextLoader.GetContextBundle(
                _selectedPatient.NomComplet,
                null
            );
            
            // Construire le prompt pour l'IA
            var conversationContext = $"**Conversation précédente:**\n\n" +
                                     $"Question: {exchange.Question}\n\n" +
                                     $"Réponse: {exchange.Response}";
            
            var fullContext = hasContext 
                ? $"{contextText}\n\n---\n\n{conversationContext}" 
                : conversationContext;
            
            // Générer le courrier avec l'IA
            var (success, markdown, error) = await _letterService.GenerateLetterFromChatAsync(
                _selectedPatient.NomComplet,
                fullContext,
                dialog.UserRequest
            );
            
            if (success && !string.IsNullOrEmpty(markdown))
            {
                // Basculer vers l'onglet Courriers
                AssistantTabControl.SelectedIndex = 1;
                
                // Afficher le brouillon dans la zone courrier
                LetterEditText.IsReadOnly = false;
                LetterEditText.Background = new SolidColorBrush(Colors.White);
                LetterEditText.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(markdown);
                
            // Activer bouton sauvegarder
            ModifierLetterButton.Visibility = Visibility.Collapsed;
            SupprimerLetterButton.Visibility = Visibility.Collapsed;
            AnnulerLetterButton.Visibility = Visibility.Collapsed;
            SauvegarderLetterButton.Visibility = Visibility.Visible;
            SauvegarderLetterButton.IsEnabled = true;
            SauvegarderLetterButton.Background = new SolidColorBrush(Color.FromRgb(39, 174, 96)); // Vert
            ImprimerLetterButton.Visibility = Visibility.Collapsed;
                
                StatusTextBlock.Text = "✅ Courrier généré depuis la conversation - Vous pouvez le modifier puis sauvegarder";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
                
                MessageBox.Show(
                    "✅ Courrier généré avec succès !\n\n" +
                    "Le brouillon est maintenant affiché dans l'onglet Courriers.\n" +
                    "Vous pouvez le modifier puis le sauvegarder.",
                    "Succès",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            else
            {
                MessageBox.Show(
                    $"❌ Erreur lors de la génération:\n\n{error}",
                    "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                
                StatusTextBlock.Text = $"❌ Erreur: {error}";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Erreur lors de la génération du courrier:\n\n{ex.Message}",
                "Erreur",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            
            StatusTextBlock.Text = $"❌ Erreur: {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
        }
        finally
        {
            // Réactiver le bouton
            LetterFromChatBtn.IsEnabled = true;
            LetterFromChatBtn.Content = "📝 Courrier";
        }
    }
#endif // COURRIERS FROM CHAT

    private void UpdateMemoryIndicator()
    {
        // Pour l'instant, cette méthode est un placeholder
        // L'UI n'est pas encore ajoutée dans le XAML
        // TODO: Ajouter TextBlock MemoryIndicator dans MainWindow.xaml
    }


    // ===== BLOC NOTESCONTROL/SYNTHÈSE SUPPRIMÉ =====
    // Code migré vers Views/Notes/NotesControl.xaml.cs
    // Supprimé le 23/11/2025 après validation

}
