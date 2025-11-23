using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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
    // ===== BLOC COURRIERS SUPPRIMÉ =====
    // Code migré vers Views/Courriers/CourriersControl.xaml.cs
    // Supprimé le 23/11/2025 après validation
    // Méthodes supprimées : TemplateLetterCombo_SelectionChanged, LettersList_SelectionChanged,
    // LettersList_MouseDoubleClick, ImprimerLetterButton_Click, LetterEditText_TextChanged,
    // ModifierLetterButton_Click, AnnulerLetterButton_Click, SupprimerLetterButton_Click,
    // SauvegarderLetterButton_Click, RefreshLettersList, LoadPatientLetters

#if false // COURRIERS - MIGRÉ VERS CourriersControl
    // Métadonnées de génération pour évaluation
    private string? _lastGeneratedLetterMCCId = null;
    private string? _lastGeneratedLetterMCCName = null;
    private string? _lastGeneratedLetterRequest = null;

    private async void TemplateLetterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TemplateLetterCombo.SelectedIndex <= 0)
        {
            // "-- Sélectionner un modèle --" est sélectionné, ne rien faire
            return;
        }

        try
        {
            var selectedItem = TemplateLetterCombo.SelectedItem as ComboBoxItem;
            if (selectedItem == null)
                return;

            var templateName = selectedItem.Content as string;
            if (string.IsNullOrEmpty(templateName) || !_letterTemplates.ContainsKey(templateName))
                return;

            // 🆕 TRAÇABILITÉ MCC : Détecter si c'est un MCC et stocker les métadonnées
            if (templateName.StartsWith("[MCC]"))
            {
                var mccName = templateName.Substring(6).Trim(); // Enlever le préfixe "[MCC] "
                var mcc = _mccLibrary.GetAllMCCs().FirstOrDefault(m => m.Name == mccName);
                if (mcc != null)
                {
                    _lastGeneratedLetterMCCId = mcc.Id;
                    _lastGeneratedLetterMCCName = mcc.Name;
                    System.Diagnostics.Debug.WriteLine($"[MCC Tracking] MCC sélectionné: {mcc.Name} (ID: {mcc.Id})");
                }
            }
            else
            {
                // Réinitialiser si ce n'est pas un MCC
                _lastGeneratedLetterMCCId = null;
                _lastGeneratedLetterMCCName = null;
            }

            // Récupérer le modèle brut
            var templateMarkdown = _letterTemplates[templateName];

            // Basculer vers l'onglet Courriers
            AssistantTabControl.SelectedIndex = 1;

            // Afficher IMMÉDIATEMENT le modèle brut (état provisoire)
            LetterEditText.IsReadOnly = false;
            LetterEditText.Background = new SolidColorBrush(Colors.White);
            LetterEditText.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(templateMarkdown);

            // Réinitialiser le fichier (nouveau document)
            _currentEditingFilePath = null;

            // Masquer TOUS les boutons de lecture, activer Sauvegarder
            NoterLetterButton.Visibility = Visibility.Collapsed;
            ModifierLetterButton.Visibility = Visibility.Collapsed;
            SupprimerLetterButton.Visibility = Visibility.Collapsed;
            ImprimerLetterButton.Visibility = Visibility.Collapsed;
            SauvegarderLetterButton.Visibility = Visibility.Visible;
            SauvegarderLetterButton.IsEnabled = true;

            // Si toggle activé ET patient sélectionné → Adapter avec IA
            if (AutoAdaptAIToggle.IsChecked == true && _selectedPatient != null)
            {
                StatusTextBlock.Text = "⏳ Adaptation IA en cours...";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);

                // Désactiver ComboBox ET bouton de sauvegarde pendant génération
                TemplateLetterCombo.IsEnabled = false;
                SauvegarderLetterButton.IsEnabled = false;
                SauvegarderLetterButton.Background = new SolidColorBrush(Color.FromRgb(189, 195, 199)); // Gris

                try
                {
                    // Récupérer les métadonnées patient (nom, prénom, age, école, classe...)
                    var patientMetadata = _patientIndex.GetMetadata(_selectedPatient.Id);

                    // ÉTAPE 1 : Lancer l'adaptation IA D'ABORD (sans dialogue)
                    var (success, adaptedMarkdown, error) = await _letterService.AdaptTemplateWithAIAsync(
                        _selectedPatient.NomComplet,
                        templateName,
                        templateMarkdown
                    );

                    if (success && !string.IsNullOrEmpty(adaptedMarkdown))
                    {
                        // Afficher le résultat adapté
                        try
                        {
                            var newDocument = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(adaptedMarkdown);
                            LetterEditText.Document = newDocument;
                        }
                        catch (Exception displayEx)
                        {
                            StatusTextBlock.Text = $"⚠️ Erreur affichage, texte brut affiché : {displayEx.Message}";
                            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);

                            var fallbackDoc = new FlowDocument();
                            fallbackDoc.Blocks.Add(new Paragraph(new Run(adaptedMarkdown)));
                            LetterEditText.Document = fallbackDoc;
                        }

                        // ÉTAPE 2 : Détecter les informations manquantes APRÈS adaptation IA
                        var (hasMissing, missingFields, availableInfo) = _letterService.DetectMissingInfo(
                            templateName,
                            adaptedMarkdown,  // Sur le markdown ADAPTÉ par l'IA
                            patientMetadata
                        );

                        // ÉTAPE 3 : Si des infos manquent → Ouvrir dialogue
                        if (hasMissing)
                        {
                            StatusTextBlock.Text = "❓ Informations manquantes...";
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

                                // Relancer adaptation avec infos complètes
                                StatusTextBlock.Text = "⏳ Ré-adaptation avec infos complètes...";
                                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);

                                var (success2, updatedMarkdown, error2) =
                                    await _letterService.AdaptTemplateWithMissingInfoAsync(
                                        _selectedPatient.NomComplet,
                                        templateName,
                                        templateMarkdown,
                                        allInfo  // Toutes les infos fusionnées
                                    );

                                if (success2 && !string.IsNullOrEmpty(updatedMarkdown))
                                {
                                    LetterEditText.Document = MarkdownFlowDocumentConverter
                                        .MarkdownToFlowDocument(updatedMarkdown);

                                    // Réactiver le bouton de sauvegarde en VERT
                                    SauvegarderLetterButton.IsEnabled = true;
                                    SauvegarderLetterButton.Background = new SolidColorBrush(Color.FromRgb(39, 174, 96)); // Vert

                                    StatusTextBlock.Text = "✅ Courrier complété avec toutes les informations";
                                    StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
                                }
                                else
                                {
                                    // Ré-adaptation échouée → Réactiver quand même en vert
                                    SauvegarderLetterButton.IsEnabled = true;
                                    SauvegarderLetterButton.Background = new SolidColorBrush(Color.FromRgb(39, 174, 96)); // Vert

                                    StatusTextBlock.Text = $"⚠️ Erreur ré-adaptation : {error2}";
                                    StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
                                }
                            }
                            else
                            {
                                // Utilisateur a annulé → Garder version avec placeholders
                                // Réactiver le bouton de sauvegarde en VERT quand même
                                SauvegarderLetterButton.IsEnabled = true;
                                SauvegarderLetterButton.Background = new SolidColorBrush(Color.FromRgb(39, 174, 96)); // Vert

                                StatusTextBlock.Text = "⚠️ Infos manquantes - Complétez manuellement les placeholders";
                                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
                            }
                        }
                        else
                        {
                            // Pas d'infos manquantes OU seulement optionnelles
                            // Réactiver le bouton de sauvegarde en VERT
                            SauvegarderLetterButton.IsEnabled = true;
                            SauvegarderLetterButton.Background = new SolidColorBrush(Color.FromRgb(39, 174, 96)); // Vert

                            StatusTextBlock.Text = "✅ Adaptation IA terminée - Vous pouvez modifier puis sauvegarder";
                            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
                        }
                    }
                    else
                    {
                        // Adaptation échouée → Réactiver bouton en vert quand même
                        SauvegarderLetterButton.IsEnabled = true;
                        SauvegarderLetterButton.Background = new SolidColorBrush(Color.FromRgb(39, 174, 96)); // Vert

                        // Garder le modèle brut en cas d'échec
                        StatusTextBlock.Text = $"⚠️ Adaptation échouée - Modèle brut affiché : {error}";
                        StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
                    }
                }
                catch (Exception adaptEx)
                {
                    // En cas d'erreur, garder le modèle brut
                    StatusTextBlock.Text = $"⚠️ Erreur adaptation IA - Modèle brut affiché : {adaptEx.Message}";
                    StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
                }
                finally
                {
                    // Réactiver le ComboBox
                    TemplateLetterCombo.IsEnabled = true;
                }
            }
            else if (AutoAdaptAIToggle.IsChecked == true && _selectedPatient == null)
            {
                // Toggle activé mais pas de patient
                StatusTextBlock.Text = "⚠️ Modèle brut affiché - Sélectionnez un patient pour l'adaptation IA";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
            }
            else
            {
                // Toggle désactivé - modèle brut uniquement
                StatusTextBlock.Text = $"📝 Modèle '{templateName}' chargé - Modifiez-le puis sauvegardez";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);
            }

            // Remettre le ComboBox sur "-- Sélectionner un modèle --"
            TemplateLetterCombo.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"❌ Erreur chargement modèle: {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
        }
    }
    
    private void LettersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LettersList.SelectedItem != null)
        {
            var item = LettersList.SelectedItem;
            var filePathProp = item.GetType().GetProperty("FilePath");
            var filePath = (string)filePathProp!.GetValue(item)!;
            
            try
            {
                var markdown = File.ReadAllText(filePath);
                
                // Stocker le fichier en cours d'édition
                _currentEditingFilePath = filePath;
                
                // 🆕 RECHARGER LES MÉTADONNÉES MCC
                var metaPath = filePath + ".meta.json";
                if (File.Exists(metaPath))
                {
                    try
                    {
                        var metaJson = File.ReadAllText(metaPath);
                        var metadata = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(metaJson);
                        
                        if (metadata.TryGetProperty("mccId", out var mccIdProp))
                            _lastGeneratedLetterMCCId = mccIdProp.GetString();
                        
                        if (metadata.TryGetProperty("mccName", out var mccNameProp))
                            _lastGeneratedLetterMCCName = mccNameProp.GetString();
                        
                        System.Diagnostics.Debug.WriteLine($"[MCC Tracking] Métadonnées rechargées: {_lastGeneratedLetterMCCName} (ID: {_lastGeneratedLetterMCCId})");
                    }
                    catch (Exception metaEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MCC Tracking] Erreur rechargement métadonnées: {metaEx.Message}");
                        _lastGeneratedLetterMCCId = null;
                        _lastGeneratedLetterMCCName = null;
                    }
                }
                else
                {
                    // Pas de métadonnées MCC pour ce courrier
                    _lastGeneratedLetterMCCId = null;
                    _lastGeneratedLetterMCCName = null;
                }
                
                // Charger en mode lecture seule avec conversion Markdown → FlowDocument
                LetterEditText.IsReadOnly = true;
                LetterEditText.Background = new SolidColorBrush(Color.FromRgb(250, 250, 250));
                LetterEditText.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(markdown);
                
                // Afficher les 4 boutons (Noter + Modifier + Supprimer + Imprimer) et CACHER les boutons d'édition
                NoterLetterButton.Visibility = Visibility.Visible;
                NoterLetterButton.Tag = filePath; // Stocker le chemin pour RateLetterButton_Click
                ModifierLetterButton.Visibility = Visibility.Visible;
                SupprimerLetterButton.Visibility = Visibility.Visible;
                ImprimerLetterButton.Visibility = Visibility.Visible;
                SauvegarderLetterButton.Visibility = Visibility.Collapsed;
                AnnulerLetterButton.Visibility = Visibility.Collapsed;
                
                StatusTextBlock.Text = "📄 Courrier chargé (lecture seule)";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"❌ Erreur lecture: {ex.Message}";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            }
        }
        else
        {
            // Aucun courrier sélectionné → Cacher tous les boutons de lecture
            NoterLetterButton.Visibility = Visibility.Collapsed;
            ModifierLetterButton.Visibility = Visibility.Collapsed;
            SupprimerLetterButton.Visibility = Visibility.Collapsed;
            ImprimerLetterButton.Visibility = Visibility.Collapsed;
            SauvegarderLetterButton.Visibility = Visibility.Visible;
        }
    }
    
    private void LettersList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LettersList.SelectedItem != null)
        {
            var item = LettersList.SelectedItem;
            var filePathProp = item.GetType().GetProperty("FilePath");
            var filePath = (string)filePathProp!.GetValue(item)!;
            
            try
            {
                // Chercher le fichier .docx correspondant
                var docxPath = Path.ChangeExtension(filePath, ".docx");
                
                if (File.Exists(docxPath))
                {
                    // Ouvrir le fichier .docx avec le programme par défaut
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = docxPath,
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                    
                    StatusTextBlock.Text = "📄 Courrier ouvert dans LibreOffice";
                    StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
                }
                else
                {
                    StatusTextBlock.Text = "⚠️ Fichier .docx introuvable. Sauvegardez d'abord le courrier.";
                    StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"❌ Erreur ouverture: {ex.Message}";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            }
        }
    } private void ImprimerLetterButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentEditingFilePath == null)
        {
            StatusTextBlock.Text = "⚠️ Aucun courrier sélectionné";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
            return;
        }
        
        try
        {
            // Chercher le fichier .docx correspondant
            var docxPath = Path.ChangeExtension(_currentEditingFilePath, ".docx");
            
            if (!File.Exists(docxPath))
            {
                StatusTextBlock.Text = "⚠️ Fichier .docx introuvable. Sauvegardez d'abord le courrier.";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
                return;
            }
            
            // Imprimer directement avec le verbe "print" de Windows
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = docxPath,
                Verb = "print",
                UseShellExecute = true,
                CreateNoWindow = true
            };
            
            System.Diagnostics.Process.Start(psi);
            
            StatusTextBlock.Text = "🖨️ Document envoyé à l'imprimante par défaut";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"❌ Erreur impression: {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
        }
    }
    
    // ===== HANDLERS COURRIERS DÉDIÉS =====
    
    private void LetterEditText_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Activer sauvegarde si texte modifié en mode édition (RichTextBox vérifie si le document a du contenu)
        if (!LetterEditText.IsReadOnly && LetterEditText.Document != null && LetterEditText.Document.Blocks.Count > 0)
        {
            SauvegarderLetterButton.IsEnabled = true;
        }
    }
    
    private void ModifierLetterButton_Click(object sender, RoutedEventArgs e)
    {
        // Passer en mode édition
        LetterEditText.IsReadOnly = false;
        LetterEditText.Background = new SolidColorBrush(Colors.White);
        
        // Masquer les 4 boutons de lecture (Noter + Modifier + Supprimer + Imprimer)
        NoterLetterButton.Visibility = Visibility.Collapsed;
        ModifierLetterButton.Visibility = Visibility.Collapsed;
        SupprimerLetterButton.Visibility = Visibility.Collapsed;
        ImprimerLetterButton.Visibility = Visibility.Collapsed;
        
        // Afficher les 2 boutons d'édition
        AnnulerLetterButton.Visibility = Visibility.Visible;
        SauvegarderLetterButton.Visibility = Visibility.Visible;
        SauvegarderLetterButton.IsEnabled = true;
        
        LetterEditText.Focus();
        
        StatusTextBlock.Text = "✏️ Édition courrier activée";
        StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
    }
    
    private void AnnulerLetterButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Désélectionner le courrier dans la liste
            LettersList.SelectedItem = null;
            
            // Vider la zone d'édition
            LetterEditText.Document = new FlowDocument();
            
            // Repasser en mode lecture seule
            LetterEditText.IsReadOnly = true;
            LetterEditText.Background = new SolidColorBrush(Color.FromRgb(250, 250, 250));
            
            // Réinitialiser le fichier en cours d'édition
            _currentEditingFilePath = null;
            
            // Masquer les boutons d'édition
            AnnulerLetterButton.Visibility = Visibility.Collapsed;
            SauvegarderLetterButton.Visibility = Visibility.Collapsed;
            
            // Masquer aussi les boutons de lecture
            ModifierLetterButton.Visibility = Visibility.Collapsed;
            SupprimerLetterButton.Visibility = Visibility.Collapsed;
            ImprimerLetterButton.Visibility = Visibility.Collapsed;
            
            StatusTextBlock.Text = "❌ Modifications annulées";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Gray);
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"❌ Erreur: {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
        }
    }
    
    private void SupprimerLetterButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentEditingFilePath == null)
        {
            StatusTextBlock.Text = "⚠️ Aucun courrier sélectionné";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
            return;
        }
        
        var result = MessageBox.Show(
            $"Êtes-vous sûr de vouloir supprimer ce courrier ?\n\n{Path.GetFileName(_currentEditingFilePath)}",
            "Confirmer la suppression",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );
        
        if (result == MessageBoxResult.Yes)
        {
            try
            {
                // Supprimer le fichier .md
                File.Delete(_currentEditingFilePath);
                
                // Supprimer aussi le fichier .docx correspondant
                var docxPath = Path.ChangeExtension(_currentEditingFilePath, ".docx");
                if (File.Exists(docxPath))
                {
                    File.Delete(docxPath);
                }
                
                // Réinitialiser interface courrier (RichTextBox avec nouveau document vide)
                _currentEditingFilePath = null;
                LetterEditText.Document = new FlowDocument();
                LetterEditText.IsReadOnly = true;
                LetterEditText.Background = new SolidColorBrush(Color.FromRgb(250, 250, 250));
                
                ModifierLetterButton.Visibility = Visibility.Collapsed;
                SupprimerLetterButton.Visibility = Visibility.Collapsed;
                SauvegarderLetterButton.IsEnabled = false;
                
                StatusTextBlock.Text = "✅ Courrier supprimé (.md + .docx)";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
                
                // Recharger liste courriers
                RefreshLettersList();
                
                // Cacher le bouton Sauvegarder
                SauvegarderLetterButton.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"❌ Erreur: {ex.Message}";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            }
        }
    }

    private void SauvegarderLetterButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPatient == null || LetterEditText.Document == null || LetterEditText.Document.Blocks.Count == 0)
        {
            StatusTextBlock.Text = "⚠️ Rien à sauvegarder";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
            return;
        }

        try
        {
            // Convertir FlowDocument → Markdown
            var markdown = MarkdownFlowDocumentConverter.FlowDocumentToMarkdown(LetterEditText.Document);

            bool success;
            string message;
            string? mdFilePath = _currentEditingFilePath;

            if (_currentEditingFilePath != null)
            {
                // Mise à jour fichier existant
                File.WriteAllText(_currentEditingFilePath, markdown);
                success = true;
                message = "Courrier mis à jour";
            }
            else
            {
                // Nouveau brouillon → Sauvegarder
                (success, message, mdFilePath) = _letterService.SaveDraft(
                    _selectedPatient.NomComplet,
                    markdown
                );

                if (success && mdFilePath != null)
                {
                    _currentEditingFilePath = mdFilePath;
                }
            }

            // Exporter en .docx avec styles préservés
            if (success && mdFilePath != null)
            {
                var (exportSuccess, exportMessage, docxPath) = _letterService.ExportToDocx(
                    _selectedPatient.NomComplet,
                    markdown,
                    mdFilePath
                );

                if (exportSuccess)
                {
                    message = $"✅ Courrier sauvegardé et exporté (.docx)";
                }
                else
                {
                    message = $"⚠️ Sauvegardé mais erreur export: {exportMessage}";
                }
                
                // 🆕 PERSISTER LES MÉTADONNÉES MCC
                if (!string.IsNullOrEmpty(_lastGeneratedLetterMCCId))
                {
                    try
                    {
                        var metaPath = mdFilePath + ".meta.json";
                        var metadata = new
                        {
                            mccId = _lastGeneratedLetterMCCId,
                            mccName = _lastGeneratedLetterMCCName,
                            generatedDate = DateTime.Now
                        };
                        var metaJson = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(metaPath, metaJson);
                        System.Diagnostics.Debug.WriteLine($"[MCC Tracking] Métadonnées sauvegardées: {metaPath}");
                    }
                    catch (Exception metaEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MCC Tracking] Erreur sauvegarde métadonnées: {metaEx.Message}");
                    }
                }
            }

            StatusTextBlock.Text = message;
            StatusTextBlock.Foreground = new SolidColorBrush(success ? Colors.Green : Colors.Orange);

            // Retour mode lecture
            LetterEditText.IsReadOnly = true;
            LetterEditText.Background = new SolidColorBrush(Color.FromRgb(250, 250, 250));

            // Masquer les boutons d'édition
            AnnulerLetterButton.Visibility = Visibility.Collapsed;
            SauvegarderLetterButton.Visibility = Visibility.Collapsed;

            // Recharger liste courriers
            RefreshLettersList();
            
            // Désélectionner le courrier dans la liste
            LettersList.SelectedItem = null;
            
            // Masquer TOUS les boutons car aucun courrier n'est sélectionné
            NoterLetterButton.Visibility = Visibility.Collapsed;
            ModifierLetterButton.Visibility = Visibility.Collapsed;
            SupprimerLetterButton.Visibility = Visibility.Collapsed;
            ImprimerLetterButton.Visibility = Visibility.Collapsed;
            
            // Vider la zone d'édition
            LetterEditText.Document = new FlowDocument();
            
            // CORRECTION BUG: Réinitialiser pour éviter d'écraser le prochain courrier
            _currentEditingFilePath = null;
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"❌ {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
        }
    }
#endif // FIN BLOC COURRIERS

    private void ChatInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Fonction de détection automatique supprimée
        // Le chat fonctionne normalement sans détection de mots-clés
    }

#if false // COURRIERS TEMPLATES - MIGRÉ VERS CourriersControl
    /// <summary>
    /// Ouvre le dialogue de sélection de modèle avec TOUS les modèles disponibles
    /// </summary>
    private void OpenTemplateSelector()
    {
        if (_selectedPatient == null)
            return;
        
        // Récupérer TOUS les noms de modèles depuis le dictionnaire _letterTemplates
        var allTemplateNames = _letterTemplates.Keys.ToList();
        
        // Ouvrir le dialogue
        var dialog = new SelectTemplateDialog(allTemplateNames);
        dialog.Owner = this;
        
        if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.SelectedTemplate))
        {
            // L'utilisateur a choisi un modèle → Générer le courrier
            var selectedTemplate = dialog.SelectedTemplate;
            GenerateLetterFromTemplate(selectedTemplate);
        }
    }
    
    /// <summary>
    /// Génère un courrier à partir d'un modèle sélectionné
    /// AVEC EXTRACTION INTELLIGENTE des infos depuis les notes
    /// </summary>
    private async void GenerateLetterFromTemplate(string templateName)
    {
        if (_selectedPatient == null || !_letterTemplates.ContainsKey(templateName))
            return;
        
        try
        {
            AddChatMessage("Vous", $"Générer un courrier avec le modèle : {templateName}", Colors.DarkBlue);
            
            // 🆕 TRAÇABILITÉ MCC : Détecter si c'est un MCC et stocker les métadonnées
            if (templateName.StartsWith("[MCC]"))
            {
                var mccName = templateName.Substring(6).Trim();
                var mcc = _mccLibrary.GetAllMCCs().FirstOrDefault(m => m.Name == mccName);
                if (mcc != null)
                {
                    _lastGeneratedLetterMCCId = mcc.Id;
                    _lastGeneratedLetterMCCName = mcc.Name;
                    System.Diagnostics.Debug.WriteLine($"[MCC Tracking] MCC sélectionné via chat: {mcc.Name} (ID: {mcc.Id})");
                }
            }
            else
            {
                _lastGeneratedLetterMCCId = null;
                _lastGeneratedLetterMCCName = null;
            }
            
            StatusTextBlock.Text = $"⏳ Génération avec {templateName}...";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);
            
            // Basculer vers l'onglet Courriers
            AssistantTabControl.SelectedIndex = 1;
            
            var templateMarkdown = _letterTemplates[templateName];
            var patientMetadata = _patientIndex.GetMetadata(_selectedPatient.Id);
            
            // ÉTAPE 1 : Adapter le modèle avec l'IA
            var (success, adaptedMarkdown, error) = await _letterService.AdaptTemplateWithAIAsync(
                _selectedPatient.NomComplet,
                templateName,
                templateMarkdown
            );
            
            if (success && !string.IsNullOrEmpty(adaptedMarkdown))
            {
                LetterEditText.IsReadOnly = false;
                LetterEditText.Background = new SolidColorBrush(Colors.White);
                LetterEditText.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(adaptedMarkdown);
                
                _currentEditingFilePath = null;
                NoterLetterButton.Visibility = Visibility.Collapsed;
                ModifierLetterButton.Visibility = Visibility.Collapsed;
                SupprimerLetterButton.Visibility = Visibility.Collapsed;
                ImprimerLetterButton.Visibility = Visibility.Collapsed;
                
                // CORRECTION : Afficher et activer le bouton Sauvegarder
                SauvegarderLetterButton.Visibility = Visibility.Visible;
                SauvegarderLetterButton.IsEnabled = true;
                SauvegarderLetterButton.Background = new SolidColorBrush(Color.FromRgb(39, 174, 96)); // Vert
                
                // ÉTAPE 2 : Détecter les informations manquantes
                var (hasMissing, missingFields, availableInfo) = _letterService.DetectMissingInfo(
                    templateName,
                    adaptedMarkdown,
                    patientMetadata,
                    templateMarkdown
                );
                
                if (hasMissing)
                {
                    // ÉTAPE 3 : 🆕 EXTRACTION IA depuis les notes AVANT d'ouvrir le dialogue
                    StatusTextBlock.Text = "🔍 Extraction des informations depuis les notes...";
                    StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
                    
                    var extractedFromNotes = await _letterService.ExtractMissingInfoFromNotesAsync(
                        _selectedPatient.NomComplet,
                        missingFields
                    );
                    
                    // ÉTAPE 4 : Fusionner les infos (metadata + extraction IA)
                    var allInfo = new Dictionary<string, string>(availableInfo, StringComparer.OrdinalIgnoreCase);
                    foreach (var kvp in extractedFromNotes)
                    {
                        if (!allInfo.ContainsKey(kvp.Key))
                        {
                            allInfo[kvp.Key] = kvp.Value;
                            System.Diagnostics.Debug.WriteLine($"[GenerateLetter] ✓ Info extraite des notes: {kvp.Key} = {kvp.Value}");
                        }
                    }
                    
                    // ÉTAPE 5 : Filtrer les champs VRAIMENT manquants (non trouvés ni dans metadata ni dans notes)
                    var stillMissing = missingFields.Where(f => 
                        !allInfo.ContainsKey(f.FieldName)
                    ).ToList();
                    
                    System.Diagnostics.Debug.WriteLine($"[GenerateLetter] Champs manquants après extraction: {stillMissing.Count}/{missingFields.Count}");
                    
                    // ÉTAPE 6 : Si encore des infos manquantes → Ouvrir dialogue
                    if (stillMissing.Count > 0)
                    {
                        StatusTextBlock.Text = $"❓ {stillMissing.Count} information(s) manquante(s)...";
                        StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
                        
                        var missingDialog = new MissingInfoDialog(stillMissing);
                        missingDialog.Owner = this;
                        
                        if (missingDialog.ShowDialog() == true && missingDialog.CollectedInfo != null)
                        {
                            // Fusionner avec les infos du dialogue
                            foreach (var kvp in missingDialog.CollectedInfo)
                            {
                                allInfo[kvp.Key] = kvp.Value;
                            }
                        }
                    }
                    else
                    {
                        // TOUTES les infos ont été trouvées automatiquement !
                        System.Diagnostics.Debug.WriteLine("[GenerateLetter] ✅ Toutes les infos trouvées automatiquement");
                        StatusTextBlock.Text = "✅ Informations extraites automatiquement";
                        StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
                    }
                    
                    // ÉTAPE 7 : Ré-adapter avec TOUTES les infos
                    if (allInfo.Count > 0)
                    {
                        StatusTextBlock.Text = "⏳ Ré-adaptation avec informations complètes...";
                        StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);
                        
                        var (success2, updatedMarkdown, error2) = 
                            await _letterService.AdaptTemplateWithMissingInfoAsync(
                                _selectedPatient.NomComplet,
                                templateName,
                                templateMarkdown,
                                allInfo
                            );
                        
                        if (success2 && !string.IsNullOrEmpty(updatedMarkdown))
                        {
                            LetterEditText.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(updatedMarkdown);
                            AddChatMessage("IA", $"✅ {templateName} généré avec {allInfo.Count} info(s) complétée(s).", Colors.Green);
                            StatusTextBlock.Text = "✅ Document généré et complété";
                            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
                        }
                        else
                        {
                            AddChatMessage("Erreur", $"⚠️ Ré-adaptation échouée: {error2}", Colors.Orange);
                            StatusTextBlock.Text = "⚠️ Erreur ré-adaptation";
                            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
                        }
                    }
                }
                else
                {
                    // Pas d'infos manquantes
                    AddChatMessage("IA", $"✅ {templateName} généré.", Colors.Green);
                    StatusTextBlock.Text = "✅ Document généré";
                    StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
                }
            }
            else
            {
                AddChatMessage("Erreur", $"❌ {error}", Colors.Red);
                StatusTextBlock.Text = $"❌ {error}";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            }
        }
        catch (Exception ex)
        {
            AddChatMessage("Erreur", $"❌ {ex.Message}", Colors.Red);
            StatusTextBlock.Text = $"❌ {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
        }
    }
    
    private void ChooseTemplateBtn_Click(object sender, RoutedEventArgs e)
    {
        // Cette méthode n'est plus utilisée avec le nouveau système
        // mais on la garde pour éviter les erreurs de compilation
        OpenTemplateSelector();
    }
    
    private async void TemplateMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem menuItem || _selectedPatient == null)
            return;
        
        var templateName = menuItem.Tag as string;
        if (string.IsNullOrEmpty(templateName) || !_letterTemplates.ContainsKey(templateName))
            return;
        
        // Récupérer et vider la question
        var question = ChatInput.Text.Trim();
        ChatInput.Text = string.Empty;
        
        try
        {
            AddChatMessage("Vous", $"{question} (avec modèle: {templateName})", Colors.DarkBlue);
            
            StatusTextBlock.Text = $"⏳ Génération avec {templateName}...";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);
            
            // Basculer vers Courriers
            AssistantTabControl.SelectedIndex = 1;
            
            var templateMarkdown = _letterTemplates[templateName];
            var patientMetadata = _patientIndex.GetMetadata(_selectedPatient.Id);
            
            var (success, adaptedMarkdown, error) = await _letterService.AdaptTemplateWithAIAsync(
                _selectedPatient.NomComplet,
                templateName,
                templateMarkdown
            );
            
            if (success && !string.IsNullOrEmpty(adaptedMarkdown))
            {
                LetterEditText.IsReadOnly = false;
                LetterEditText.Background = new SolidColorBrush(Colors.White);
                LetterEditText.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(adaptedMarkdown);
                
                _currentEditingFilePath = null;
                ModifierLetterButton.Visibility = Visibility.Collapsed;
                SupprimerLetterButton.Visibility = Visibility.Collapsed;
                SauvegarderLetterButton.IsEnabled = true;
                
                // Gérer infos manquantes
                var (hasMissing, missingFields, availableInfo) = _letterService.DetectMissingInfo(
                    templateName,
                    adaptedMarkdown,
                    patientMetadata
                );
                
                if (hasMissing)
                {
                    var dialog = new MissingInfoDialog(missingFields);
                    dialog.Owner = this;
                    
                    if (dialog.ShowDialog() == true && dialog.CollectedInfo != null)
                    {
                        var allInfo = new Dictionary<string, string>(availableInfo);
                        foreach (var kvp in dialog.CollectedInfo)
                        {
                            allInfo[kvp.Key] = kvp.Value;
                        }
                        
                        var (success2, updatedMarkdown, error2) = 
                            await _letterService.AdaptTemplateWithMissingInfoAsync(
                                _selectedPatient.NomComplet,
                                templateName,
                                templateMarkdown,
                                allInfo
                            );
                        
                        if (success2 && !string.IsNullOrEmpty(updatedMarkdown))
                        {
                            LetterEditText.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(updatedMarkdown);
                        }
                    }
                }
                
                AddChatMessage("IA", $"✅ {templateName} généré.", Colors.Green);
                StatusTextBlock.Text = "✅ Document généré";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
            }
            else
            {
            AddChatMessage("Erreur", $"❌ {error}", Colors.Red);
            }
        }
        catch (Exception ex)
        {
            AddChatMessage("Erreur", $"❌ {ex.Message}", Colors.Red);
        }
    }
    private void LoadCustomTemplates()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[LoadCustomTemplates] Début du rechargement des MCC...");

            // Vider le dictionnaire des MCC
            var keysToRemove = _letterTemplates.Keys.Where(k => k.StartsWith("[MCC]")).ToList();
            foreach (var key in keysToRemove)
            {
                _letterTemplates.Remove(key);
            }
            System.Diagnostics.Debug.WriteLine($"[LoadCustomTemplates] ✓ Dictionnaire vidé : {keysToRemove.Count} entrées supprimées");

            // Compter les items NON-MCC dans le ComboBox (placeholder + modèles de base)
            int itemsToKeep = 0;
            for (int i = 0; i < TemplateLetterCombo.Items.Count; i++)
            {
                if (TemplateLetterCombo.Items[i] is ComboBoxItem item)
                {
                    var content = item.Content as string ?? "";
                    if (!content.StartsWith("[MCC]"))
                    {
                        itemsToKeep++;
                    }
                }
                else
                {
                    // Le premier item est juste un string "--"
                    itemsToKeep++;
                }
            }

            System.Diagnostics.Debug.WriteLine($"[LoadCustomTemplates] Items à conserver dans ComboBox : {itemsToKeep}");

            // Vider le ComboBox (garder seulement les items prédéfinis)
            while (TemplateLetterCombo.Items.Count > itemsToKeep)
            {
                TemplateLetterCombo.Items.RemoveAt(itemsToKeep);
            }

            System.Diagnostics.Debug.WriteLine($"[LoadCustomTemplates] ✓ ComboBox vidé, reste {TemplateLetterCombo.Items.Count} items");

            // Charger les MCC depuis MCCLibrary
            var allMCCs = _mccLibrary.GetAllMCCs();

            System.Diagnostics.Debug.WriteLine($"[LoadCustomTemplates] Chargement de {allMCCs.Count} MCC depuis MCCLibrary");

            foreach (var mcc in allMCCs)
            {
                // Préfixe [MCC] pour les distinguer
                var displayName = $"[MCC] {mcc.Name}";
                var comboItem = new ComboBoxItem { Content = displayName };
                TemplateLetterCombo.Items.Add(comboItem);

                // Ajouter au dictionnaire _letterTemplates
                _letterTemplates[displayName] = mcc.TemplateMarkdown;

                System.Diagnostics.Debug.WriteLine($"[LoadCustomTemplates] ✓ MCC ajouté: {displayName}");
            }

            System.Diagnostics.Debug.WriteLine($"[LoadCustomTemplates] ✅ Total rechargé: {allMCCs.Count} MCC");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadCustomTemplates] Erreur chargement: {ex.Message}");
        }
    }
#endif // FIN COURRIERS TEMPLATES

    private async void AnalyzeLetterBtn_Click(object sender, RoutedEventArgs e)
    {
        var exampleLetter = ExampleLetterTextBox.Text.Trim();
        
        if (string.IsNullOrEmpty(exampleLetter))
        {
            MessageBox.Show("Veuillez coller un exemple de courrier.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        try
        {
            AnalyzeLetterBtn.IsEnabled = false;
            
            // PHASE 1 : Analyse sémantique
            StatusTextBlock.Text = "⏳ Phase 1/3 : Analyse sémantique...";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);
            
            var (semanticSuccess, semantic, semanticError) = 
                await _templateExtractor.AnalyzeDocumentSemantic(exampleLetter);
            
            if (!semanticSuccess)
            {
                MessageBox.Show($"Erreur Phase 1 :\n\n{semanticError}", "Erreur", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusTextBlock.Text = $"❌ Erreur sémantique: {semanticError}";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                return;
            }
            
            // PHASE 2 : Extraction template
            StatusTextBlock.Text = "⏳ Phase 2/3 : Extraction template...";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);
            
            var (templateSuccess, template, name, variables, templateError) = 
                await _templateExtractor.ExtractTemplateFromExample(exampleLetter);
            
            if (!templateSuccess)
            {
                MessageBox.Show($"Erreur Phase 2 :\n\n{templateError}", "Erreur", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusTextBlock.Text = $"❌ Erreur template: {templateError}";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                return;
            }
            
            // PHASE 3 : Analyse structurelle
            StatusTextBlock.Text = "⏳ Phase 3/3 : Analyse structurelle...";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);
            
            var (structureSuccess, sections, structureError) = 
                await _templateExtractor.AnalyzeTemplateStructure(template);
            
            if (!structureSuccess)
            {
                MessageBox.Show($"Erreur Phase 3 :\n\n{structureError}", "Erreur", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusTextBlock.Text = $"❌ Erreur structure: {structureError}";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                return;
            }
            
            // FUSION : Combiner les résultats
            semantic.Sections = sections;
            
            // Créer le MCC complet
            var mcc = new MCCModel
            {
                Id = Guid.NewGuid().ToString("N").Substring(0, 16),
                Name = name,
                Version = 1,
                Created = DateTime.Now,
                LastModified = DateTime.Now,
                Semantic = semantic,
                TemplateMarkdown = template,
                PromptTemplate = "",
                Keywords = semantic.Keywords?.AUtiliser ?? new List<string>(),
                Status = MCCStatus.Active,
                UsageCount = 0,
                AverageRating = 0.0,
                TotalRatings = 0
            };
            
            if (mcc != null)
            {
                // ✅ CORRECTION : Stocker le MCC complet pour sauvegarde ultérieure
                _currentAnalyzedMCC = mcc;
                _currentExtractedTemplate = mcc.TemplateMarkdown;
                _currentExtractedVariables = mcc.Keywords;
                
                // ===== COLONNE GAUCHE : Template =====
                TemplateNameTextBox.Text = mcc.Name;
                TemplateVariablesTextBox.Text = string.Join(", ", mcc.Keywords.Select(v => $"{{{{{v}}}}}"));
                TemplatePreviewTextBox.Text = mcc.TemplateMarkdown;
                
                // ===== COLONNE DROITE : Analyse sémantique =====
                if (mcc.Semantic != null)
                {
                    SemanticTypeText.Text = mcc.Semantic.DocType ?? "-";
                    SemanticAudienceText.Text = mcc.Semantic.Audience ?? "-";
                    SemanticAgeGroupText.Text = mcc.Semantic.AgeGroup ?? "-";
                    SemanticToneText.Text = mcc.Semantic.Tone ?? "-";
                    
                    // Mots-clés cliniques (ItemsControl avec badges)
                    if (mcc.Semantic.ClinicalKeywords != null && mcc.Semantic.ClinicalKeywords.Count > 0)
                    {
                        SemanticKeywordsList.ItemsSource = mcc.Semantic.ClinicalKeywords;
                    }
                    else
                    {
                        SemanticKeywordsList.ItemsSource = new List<string> { "Aucun mot-clé détecté" };
                    }
                    
                    // Structure détectée (ItemsControl avec liste à puces)
                    if (mcc.Semantic.Sections != null && mcc.Semantic.Sections.Count > 0)
                    {
                        // CORRECTION: Convertir Dictionary en List<KeyValuePair> pour éviter l'erreur de binding TwoWay
                        SemanticStructureList.ItemsSource = mcc.Semantic.Sections.ToList();
                    }
                    else
                    {
                        SemanticStructureList.ItemsSource = new List<KeyValuePair<string, string>>
                        {
                            new KeyValuePair<string, string>("Structure", "Non analysée")
                        };
                    }
                }
                else
                {
                    // Pas d'analyse sémantique disponible
                    SemanticTypeText.Text = "Non disponible";
                    SemanticAudienceText.Text = "-";
                    SemanticAgeGroupText.Text = "-";
                    SemanticToneText.Text = "-";
                    SemanticKeywordsList.ItemsSource = new List<string> { "Analyse non disponible" };
                    SemanticStructureList.ItemsSource = new List<KeyValuePair<string, string>>();
                }
                
                // Afficher le panneau de résultats
                TemplateResultPanel.Visibility = Visibility.Visible;
                
                StatusTextBlock.Text = "✅ MCC extrait avec analyse sémantique complète";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusTextBlock.Text = $"❌ Erreur: {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
        }
        finally
        {
            AnalyzeLetterBtn.IsEnabled = true;
        }
    }
    
    private void SaveTemplateBtn_Click(object sender, RoutedEventArgs e)
    {
        var templateName = TemplateNameTextBox.Text.Trim();
        
        if (string.IsNullOrEmpty(templateName) || _currentAnalyzedMCC == null)
        {
            MessageBox.Show("Veuillez analyser un courrier d'abord.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        // ✅ CORRECTION : Mettre à jour le nom du MCC avec celui saisi par l'utilisateur
        _currentAnalyzedMCC.Name = templateName;
        
        // ✅ CORRECTION : Sauvegarder avec le système MCCLibrary (préserve l'analyse sémantique)
        var (success, message) = _mccLibrary.AddMCC(_currentAnalyzedMCC);
        
        if (success)
        {
            // Réinitialiser
            ExampleLetterTextBox.Text = string.Empty;
            TemplateResultPanel.Visibility = Visibility.Collapsed;
            _currentAnalyzedMCC = null;
            _currentExtractedTemplate = null;
            _currentExtractedVariables.Clear();
            
            // Recharger les templates dans CourriersControl
            CourriersControlPanel.ReloadTemplates();
            
            MessageBox.Show($"✅ {message}", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
            StatusTextBlock.Text = "✅ MCC sauvegardé avec analyse sémantique complète";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
        }
        else
        {
            MessageBox.Show($"❌ {message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusTextBlock.Text = $"❌ {message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
        }
    }
    
   



    // ===== BLOC ATTESTATIONS SUPPRIMÉ =====
    // Code migré vers Views/Attestations/AttestationsControl.xaml.cs
    // Supprimé le 23/11/2025 après validation

#if false // COURRIERS REFRESH/RATE - MIGRÉ VERS CourriersControl
    private void RefreshLettersList()
    {
        if (_selectedPatient == null)
        {
            LettersList.ItemsSource = null;
            return;
        }

        var lettresDir = _pathService.GetCourriersDirectory(_selectedPatient.NomComplet);
        
        if (!Directory.Exists(lettresDir))
        {
            LettersList.ItemsSource = null;
            return;
        }

        var letters = Directory.GetFiles(lettresDir, "*.md")
            .Select(f => new
            {
                Date = File.GetLastWriteTime(f),
                DateLabel = File.GetLastWriteTime(f).ToString("dd/MM/yyyy HH:mm"),
                Preview = Path.GetFileNameWithoutExtension(f),
                FilePath = f
            })
            .OrderByDescending(l => l.Date)
            .ToList();

        LettersList.ItemsSource = letters;
    }
    
    // ===== SYSTÈME D'ÉVALUATION DES COURRIERS =====
    
    /// <summary>
    /// Ouvre le dialogue d'évaluation pour un courrier
    /// </summary>
    private void RateLetterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string letterPath)
        {
            MessageBox.Show("Impossible de trouver le courrier à évaluer.", "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        
        if (!File.Exists(letterPath))
        {
            MessageBox.Show("Le fichier du courrier n'existe plus.", "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        try
        {
            ShowRateLetterDialog(letterPath, _lastGeneratedLetterMCCId, _lastGeneratedLetterMCCName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors de l'ouverture du dialogue d'évaluation:\n\n{ex.Message}",
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    /// <summary>
    /// Affiche le dialogue d'évaluation et traite le résultat
    /// </summary>
    private void ShowRateLetterDialog(string letterPath, string? mccId, string? mccName)
    {
        var dialog = new RateLetterDialog(letterPath, mccId, mccName)
        {
            Owner = this
        };
        
        if (dialog.ShowDialog() == true && dialog.Rating != null)
        {
            // Sauvegarder l'évaluation
            _letterRatingService.AddOrUpdateRating(dialog.Rating);
            
            // Traiter selon la note
            HandleRatingActions(dialog.Rating);
            
            // Mettre à jour l'affichage
            RefreshLettersList();
            
            StatusTextBlock.Text = $"✅ Évaluation enregistrée : {dialog.Rating.Rating}⭐";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
        }
    }
    
    /// <summary>
    /// Gère les actions automatiques selon la note
    /// </summary>
    private void HandleRatingActions(LetterRating rating)
    {
        // Mettre à jour les statistiques du MCC si un MCC a été utilisé
        if (!string.IsNullOrEmpty(rating.MCCId))
        {
            var (success, message) = _mccLibrary.UpdateMCCRatingStats(rating.MCCId, _letterRatingService);
            
            if (success)
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] Statistiques MCC mises à jour : {message}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[MainWindow] Erreur mise à jour stats MCC : {message}");
            }
        }
        
        // ✅ CORRECTION : Vérifier D'ABORD si le courrier est issu d'un MCC
        bool isFromMCC = !string.IsNullOrEmpty(rating.MCCId);
        
        // Note ≤ 3★ avec MCC → Message d'alerte
        if (rating.Rating <= 3 && isFromMCC)
        {
            MessageBox.Show(
                $"⚠️ Ce MCC a été noté {rating.Rating}★\n\n" +
                $"MCC : {rating.MCCName}\n\n" +
                $"Cette note indique que le MCC pourrait nécessiter une révision.\n" +
                $"Vous pouvez l'améliorer depuis la bibliothèque MCC.",
                "MCC à revoir",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
        }
        // Note 5★ AVEC MCC → Message de félicitation (NE PAS proposer de créer un nouveau MCC !)
        else if (rating.Rating == 5 && isFromMCC)
        {
            MessageBox.Show(
                $"🌟 Excellent ! Ce MCC a été noté 5★\n\n" +
                $"MCC : {rating.MCCName}\n\n" +
                $"Cette excellente note contribue à améliorer la qualité du MCC dans la bibliothèque.",
                "Excellente notation",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        // Note 5★ SANS MCC → Proposer de créer un MCC SEULEMENT si le courrier N'est PAS issu d'un MCC
        else if (rating.Rating == 5 && !isFromMCC)
        {
            var result = MessageBox.Show(
                $"🌟 Excellent courrier noté 5★ !\n\n" +
                $"Ce courrier n'utilise pas de MCC.\n" +
                $"Voulez-vous le transformer en MCC réutilisable ?",
                "Créer un MCC",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );
            
            if (result == MessageBoxResult.Yes)
            {
                // Basculer vers l'onglet Templates pour créer le MCC
                AssistantTabControl.SelectedIndex = 6; // Index de l'onglet Templates
                
                // Charger le contenu du courrier dans la zone d'analyse
                try
                {
                    var markdown = File.ReadAllText(rating.LetterPath);
                    ExampleLetterTextBox.Text = markdown;
                    
                    MessageBox.Show(
                        "Le courrier a été chargé dans l'onglet Templates.\n\n" +
                        "Cliquez sur 'Analyser' pour créer le MCC.",
                        "Prêt à créer le MCC",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erreur lors du chargement du courrier:\n\n{ex.Message}",
                        "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
#endif // FIN COURRIERS REFRESH/RATE
}
