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


    private void ChatInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Fonction de détection automatique supprimée
        // Le chat fonctionne normalement sans détection de mots-clés
    }


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

}
