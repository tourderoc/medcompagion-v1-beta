using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MedCompanion.Models;
using MedCompanion.Dialogs;

namespace MedCompanion;

public partial class MainWindow : Window
{
#if false // ===== OBSOLÈTE : Migré vers FormulairesControl =====

    // ===== SECTION FORMULAIRES =====

    // Collez ICI les 8 méthodes coupées
    private void FormulaireTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FormulaireTypeCombo.SelectedIndex <= 0 || _selectedPatient == null)
        {
            // Aucun formulaire sélectionné ou pas de patient → Tout masquer
            PreremplirFormulaireButton.Visibility = Visibility.Collapsed;
            PreremplirFormulaireButton.IsEnabled = false;
            OuvrirModelePAIButton.Visibility = Visibility.Collapsed;
            OuvrirModelePAIButton.IsEnabled = false;
            return;
        }
        
        var selectedItem = FormulaireTypeCombo.SelectedItem as ComboBoxItem;
        if (selectedItem?.Tag is string formulaireType)
        {
            if (formulaireType == "PAI")
            {
                // PAI sélectionné → Afficher bouton "Ouvrir modèle", masquer bouton IA
                PreremplirFormulaireButton.Visibility = Visibility.Collapsed;
                PreremplirFormulaireButton.IsEnabled = false;
                
                OuvrirModelePAIButton.Visibility = Visibility.Visible;
                OuvrirModelePAIButton.IsEnabled = true;
                
                StatusTextBlock.Text = "🏫 PAI sélectionné - Cliquez pour ouvrir le modèle PDF";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
            }
            else if (formulaireType == "MDPH")
            {
                // MDPH sélectionné → Afficher bouton IA, masquer bouton Ouvrir
                OuvrirModelePAIButton.Visibility = Visibility.Collapsed;
                OuvrirModelePAIButton.IsEnabled = false;
                
                PreremplirFormulaireButton.Visibility = Visibility.Visible;
                PreremplirFormulaireButton.IsEnabled = true;
                
                StatusTextBlock.Text = "📋 MDPH sélectionné - Cliquez sur 'Pré-remplir avec l'IA' pour générer";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);
            }
        }
    }
    
    private async void PreremplirFormulaireButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPatient == null)
        {
            MessageBox.Show("Veuillez d'abord sélectionner un patient.", "Information",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        if (FormulaireTypeCombo.SelectedIndex <= 0)
        {
            MessageBox.Show("Veuillez sélectionner un type de formulaire.", "Information",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        var selectedItem = FormulaireTypeCombo.SelectedItem as ComboBoxItem;
        if (selectedItem?.Tag is not string formulaireType)
            return;
        
        try
        {
            // Désactiver le bouton pendant la génération
            PreremplirFormulaireButton.IsEnabled = false;
            PreremplirFormulaireButton.Content = "⏳ Génération en cours...";
            
            StatusTextBlock.Text = $"⏳ Génération IA du formulaire {formulaireType} en cours...";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);
            
            var metadata = _patientIndex.GetMetadata(_selectedPatient.Id);
            if (metadata == null)
            {
                MessageBox.Show("Impossible de récupérer les métadonnées du patient.", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            
            // Générer toutes les sections avec l'IA
            var sections = await _formulaireService.GenerateAllSections(metadata);
            
            if (sections == null || sections.Count == 0)
            {
                MessageBox.Show("Erreur lors de la génération des sections.", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            
            // Construire le document Markdown complet
            var markdownContent = new StringBuilder();
            markdownContent.AppendLine($"# Formulaire {formulaireType} - {metadata.Prenom} {metadata.Nom}");
            markdownContent.AppendLine();
            markdownContent.AppendLine($"**Date de génération :** {DateTime.Now:dd/MM/yyyy HH:mm}");
            markdownContent.AppendLine();
            markdownContent.AppendLine("---");
            markdownContent.AppendLine();
            
            // Ajouter chaque section
            if (sections.ContainsKey("pathologie"))
            {
                markdownContent.AppendLine("## 📋 PATHOLOGIE MOTIVANT LA DEMANDE");
                markdownContent.AppendLine();
                markdownContent.AppendLine(sections["pathologie"]);
                markdownContent.AppendLine();
            }
            
            if (sections.ContainsKey("histoire"))
            {
                markdownContent.AppendLine("## 📖 HISTOIRE DE LA PATHOLOGIE");
                markdownContent.AppendLine();
                markdownContent.AppendLine(sections["histoire"]);
                markdownContent.AppendLine();
            }
            
            if (sections.ContainsKey("clinique"))
            {
                markdownContent.AppendLine("## 🩺 DESCRIPTION CLINIQUE ACTUELLE");
                markdownContent.AppendLine();
                markdownContent.AppendLine(sections["clinique"]);
                markdownContent.AppendLine();
            }
            
            if (sections.ContainsKey("traitements"))
            {
                markdownContent.AppendLine("## 💊 TRAITEMENTS ET PRISES EN CHARGE");
                markdownContent.AppendLine();
                markdownContent.AppendLine(sections["traitements"]);
                markdownContent.AppendLine();
            }
            
            if (sections.ContainsKey("mobilite"))
            {
                markdownContent.AppendLine("## 🚶 RETENTISSEMENT FONCTIONNEL - MOBILITÉ");
                markdownContent.AppendLine();
                markdownContent.AppendLine(sections["mobilite"]);
                markdownContent.AppendLine();
            }
            
            if (sections.ContainsKey("communication"))
            {
                markdownContent.AppendLine("## 💬 RETENTISSEMENT FONCTIONNEL - COMMUNICATION");
                markdownContent.AppendLine();
                markdownContent.AppendLine(sections["communication"]);
                markdownContent.AppendLine();
            }
            
            if (sections.ContainsKey("cognition"))
            {
                markdownContent.AppendLine("## 🧠 RETENTISSEMENT FONCTIONNEL - COGNITION");
                markdownContent.AppendLine();
                markdownContent.AppendLine(sections["cognition"]);
                markdownContent.AppendLine();
            }
            
            if (sections.ContainsKey("autonomie"))
            {
                markdownContent.AppendLine("## 🛁 RETENTISSEMENT FONCTIONNEL - ENTRETIEN PERSONNEL");
                markdownContent.AppendLine();
                markdownContent.AppendLine(sections["autonomie"]);
                markdownContent.AppendLine();
            }
            
            if (sections.ContainsKey("vieQuotidienne"))
            {
                markdownContent.AppendLine("## 🏠 RETENTISSEMENT FONCTIONNEL - VIE QUOTIDIENNE");
                markdownContent.AppendLine();
                markdownContent.AppendLine(sections["vieQuotidienne"]);
                markdownContent.AppendLine();
            }
            
            if (sections.ContainsKey("socialScolaire"))
            {
                markdownContent.AppendLine("## 🏫 RETENTISSEMENT SUR VIE SOCIALE ET SCOLAIRE");
                markdownContent.AppendLine();
                markdownContent.AppendLine(sections["socialScolaire"]);
                markdownContent.AppendLine();
            }
            
            if (sections.ContainsKey("remarques"))
            {
                markdownContent.AppendLine("## 📝 REMARQUES OU OBSERVATIONS COMPLÉMENTAIRES");
                markdownContent.AppendLine();
                markdownContent.AppendLine(sections["remarques"]);
                markdownContent.AppendLine();
            }
            
            var finalMarkdown = markdownContent.ToString();
            
            // Sauvegarder le formulaire dans le dossier patient
            var formulairesDir = Path.Combine(_selectedPatient.DirectoryPath, "formulaires");
            if (!Directory.Exists(formulairesDir))
            {
                Directory.CreateDirectory(formulairesDir);
            }
            
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"{formulaireType}_{timestamp}.md";
            var filePath = Path.Combine(formulairesDir, fileName);
            
            File.WriteAllText(filePath, finalMarkdown, Encoding.UTF8);
            
            // Exporter en DOCX
            var (exportSuccess, exportMessage, docxPath) = _letterService.ExportToDocx(
                _selectedPatient.NomComplet,
                finalMarkdown,
                filePath
            );
            
            if (exportSuccess && !string.IsNullOrEmpty(docxPath))
            {
                var result = MessageBox.Show(
                    $"✅ Formulaire {formulaireType} généré avec succès !\n\n" +
                    $"📄 Fichier : {fileName}\n" +
                    $"📁 Emplacement : formulaires/\n\n" +
                    $"Voulez-vous ouvrir le document maintenant ?",
                    "Génération réussie",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information
                );
                
                if (result == MessageBoxResult.Yes && File.Exists(docxPath))
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = docxPath,
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                }
                
                StatusTextBlock.Text = $"✅ Formulaire {formulaireType} généré et exporté en DOCX";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
            }
            else
            {
                MessageBox.Show(
                    $"⚠️ Formulaire généré mais erreur lors de l'export DOCX :\n\n{exportMessage}",
                    "Avertissement",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                
                StatusTextBlock.Text = $"⚠️ Formulaire généré mais erreur export DOCX";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Erreur lors de la génération du formulaire :\n\n{ex.Message}",
                "Erreur",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            
            StatusTextBlock.Text = $"❌ Erreur : {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
        }
        finally
        {
            // Réactiver le bouton
            PreremplirFormulaireButton.IsEnabled = true;
            PreremplirFormulaireButton.Content = "🤖 Pré-remplir avec l'IA";
        }
    }
    
    /// <summary>
    /// Charge la liste des formulaires sauvegardés du patient
    /// </summary>
    private void LoadPatientFormulaires()
    {
        if (_selectedPatient == null)
        {
            FormulairesList.ItemsSource = null;
            FormulairesCountLabel.Text = "0 formulaires";
            return;
        }
        
        try
        {
            var formulairesDir = Path.Combine(_selectedPatient.DirectoryPath, "formulaires");
            
            if (!Directory.Exists(formulairesDir))
            {
                FormulairesList.ItemsSource = null;
                FormulairesCountLabel.Text = "0 formulaires";
                return;
            }
            
            // Récupérer tous les fichiers PDF et DOCX
            var pdfFiles = Directory.GetFiles(formulairesDir, "*.pdf", SearchOption.TopDirectoryOnly);
            var docxFiles = Directory.GetFiles(formulairesDir, "*.docx", SearchOption.TopDirectoryOnly);
            
            var formulaires = new List<object>();
            
            // Traiter les PDF
            foreach (var pdfPath in pdfFiles)
            {
                var fileName = Path.GetFileName(pdfPath);
                var fileInfo = new FileInfo(pdfPath);
                
                // Détecter le type (PAI ou MDPH)
                string typeLabel;
                if (fileName.StartsWith("PAI_", StringComparison.OrdinalIgnoreCase))
                {
                    typeLabel = "🏫 PAI";
                }
                else if (fileName.StartsWith("MDPH_", StringComparison.OrdinalIgnoreCase))
                {
                    typeLabel = "📋 MDPH";
                }
                else
                {
                    typeLabel = "📄 Autre";
                }
                
                formulaires.Add(new
                {
                    TypeLabel = typeLabel,
                    DateLabel = fileInfo.LastWriteTime.ToString("dd/MM/yyyy HH:mm"),
                    FileName = fileName,
                    FilePath = pdfPath,
                    Date = fileInfo.LastWriteTime
                });
            }
            
            // Traiter les DOCX
            foreach (var docxPath in docxFiles)
            {
                var fileName = Path.GetFileName(docxPath);
                var fileInfo = new FileInfo(docxPath);
                
                // Détecter le type
                string typeLabel;
                if (fileName.StartsWith("PAI_", StringComparison.OrdinalIgnoreCase))
                {
                    typeLabel = "🏫 PAI";
                }
                else if (fileName.StartsWith("MDPH_", StringComparison.OrdinalIgnoreCase))
                {
                    typeLabel = "📋 MDPH";
                }
                else
                {
                    typeLabel = "📄 Autre";
                }
                
                formulaires.Add(new
                {
                    TypeLabel = typeLabel,
                    DateLabel = fileInfo.LastWriteTime.ToString("dd/MM/yyyy HH:mm"),
                    FileName = fileName,
                    FilePath = docxPath,
                    Date = fileInfo.LastWriteTime
                });
            }
            
            // Trier par date décroissante
            var sortedFormulaires = formulaires.OrderByDescending(f => 
                f.GetType().GetProperty("Date")?.GetValue(f) as DateTime?
            ).ToList();
            
            FormulairesList.ItemsSource = sortedFormulaires;
            
            // Mettre à jour le compteur
            var count = sortedFormulaires.Count;
            FormulairesCountLabel.Text = count == 0 ? "0 formulaires" :
                                        count == 1 ? "1 formulaire" :
                                        $"{count} formulaires";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"❌ Erreur chargement formulaires: {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            FormulairesList.ItemsSource = null;
            FormulairesCountLabel.Text = "0 formulaires";
        }
    }
    
    /// <summary>
    /// Gestionnaire du double-clic sur un formulaire pour l'ouvrir
    /// </summary>
    private void FormulairesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FormulairesList.SelectedItem == null)
            return;
        
        var item = FormulairesList.SelectedItem;
        var filePathProp = item.GetType().GetProperty("FilePath");
        
        if (filePathProp != null)
        {
            var filePath = filePathProp.GetValue(item) as string;
            
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                    
                    StatusTextBlock.Text = $"📄 Formulaire ouvert : {Path.GetFileName(filePath)}";
                    StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erreur ouverture : {ex.Message}", "Erreur", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("Fichier introuvable.", "Erreur", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
    
    /// <summary>
    /// Gestionnaire de sélection pour activer/désactiver le bouton Supprimer et afficher la synthèse
    /// </summary>
    private void FormulairesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        SupprimerFormulaireButton.IsEnabled = FormulairesList.SelectedItem != null;
        
        if (FormulairesList.SelectedItem == null || _selectedPatient == null)
        {
            return;
        }
        
        var item = FormulairesList.SelectedItem;
        var filePathProp = item.GetType().GetProperty("FilePath");
        
        if (filePathProp != null)
        {
            var filePath = filePathProp.GetValue(item) as string;
            
            if (!string.IsNullOrEmpty(filePath))
            {
                // Chercher le fichier .json correspondant
                var jsonPath = Path.ChangeExtension(filePath, ".json");
                
                if (File.Exists(jsonPath))
                {
                    try
                    {
                        // Charger et désérialiser le JSON
                        var jsonContent = File.ReadAllText(jsonPath);
                        var synthesis = System.Text.Json.JsonSerializer.Deserialize<PAISynthesis>(jsonContent);
                        
                        if (synthesis != null)
                        {
                            // Construire la synthèse formatée avec emojis et formatage
                            var synthesisText = $"📋 SYNTHÈSE PAI\n\n" +
                                              $"📄 Fichier : {Path.GetFileName(filePath)}\n" +
                                              $"📅 Date de création : {synthesis.DateCreation:dd/MM/yyyy HH:mm}\n" +
                                              $"👤 Patient : {synthesis.Patient}\n\n" +
                                              $"🎯 Motif du PAI :\n\n" +
                                              $"{synthesis.Motif}\n\n" +
                                              $"━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
                                              $"💡 Note : Double-cliquez sur le formulaire dans la liste pour l'ouvrir dans votre lecteur PDF.";
                            
                            // Afficher dans le TextBlock d'aperçu
                            if (FormulaireSynthesisPreview != null)
                            {
                                FormulaireSynthesisPreview.Text = synthesisText;
                                FormulaireSynthesisPreview.Foreground = new SolidColorBrush(Colors.Black);
                                FormulaireSynthesisPreview.FontWeight = FontWeights.Normal;
                                
                                StatusTextBlock.Text = $"✓ Synthèse PAI affichée - Motif : {synthesis.Motif}";
                                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        StatusTextBlock.Text = $"⚠️ Erreur lecture synthèse : {ex.Message}";
                        StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
                    }
                }
                else
                {
                    // Pas de synthèse disponible
                    StatusTextBlock.Text = "⚠️ Aucune synthèse disponible pour ce formulaire";
                    StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
                }
            }
        }
    }
    
    /// <summary>
    /// Supprime un formulaire sélectionné
    /// </summary>
    private void SupprimerFormulaireButton_Click(object sender, RoutedEventArgs e)
    {
        if (FormulairesList.SelectedItem == null)
        {
            MessageBox.Show("Veuillez sélectionner un formulaire à supprimer.", "Information",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        var item = FormulairesList.SelectedItem;
        var filePathProp = item.GetType().GetProperty("FilePath");
        var fileNameProp = item.GetType().GetProperty("FileName");
        
        if (filePathProp != null && fileNameProp != null)
        {
            var filePath = filePathProp.GetValue(item) as string;
            var fileName = fileNameProp.GetValue(item) as string;
            
            if (string.IsNullOrEmpty(filePath))
                return;
            
            var result = MessageBox.Show(
                $"Êtes-vous sûr de vouloir supprimer ce formulaire ?\n\n{fileName}",
                "Confirmer la suppression",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );
            
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // Supprimer le fichier
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }
                    
                    // Si c'est un PDF, chercher le .md correspondant et le supprimer aussi
                    if (filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                    {
                        var mdPath = Path.ChangeExtension(filePath, ".md");
                        if (File.Exists(mdPath))
                        {
                            File.Delete(mdPath);
                        }
                    }
                    
                    // Si c'est un DOCX, chercher le .md correspondant et le supprimer aussi
                    if (filePath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                    {
                        var mdPath = Path.ChangeExtension(filePath, ".md");
                        if (File.Exists(mdPath))
                        {
                            File.Delete(mdPath);
                        }
                    }
                    
                    StatusTextBlock.Text = "✅ Formulaire supprimé";
                    StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
                    
                    // Recharger la liste
                    LoadPatientFormulaires();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erreur suppression : {ex.Message}", "Erreur", 
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    
                    StatusTextBlock.Text = $"❌ Erreur : {ex.Message}";
                    StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                }
            }
        }
    }
    
    private void OuvrirModelePAIButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPatient == null)
        {
            MessageBox.Show(
                "Veuillez d'abord sélectionner un patient.",
                "Aucun patient sélectionné",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
            return;
        }
        
        try
        {
            // Construire le chemin vers le PDF modèle
            var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var paiModelPath = Path.Combine(appDirectory, "Assets", "Formulaires", "Dossier PAI.pdf");
            
            // Vérifier que le fichier modèle existe
            if (!File.Exists(paiModelPath))
            {
                MessageBox.Show(
                    $"Le modèle PAI est introuvable :\n\n{paiModelPath}\n\n" +
                    "Veuillez vérifier que le fichier existe dans le dossier Assets/Formulaires/",
                    "Fichier introuvable",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                
                StatusTextBlock.Text = "❌ Modèle PAI introuvable";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                return;
            }
            
            // Créer le dossier formulaires dans le dossier du patient
            var formulairesDir = Path.Combine(_selectedPatient.DirectoryPath, "formulaires");
            if (!Directory.Exists(formulairesDir))
            {
                Directory.CreateDirectory(formulairesDir);
            }
            
            // Générer le nom du fichier avec timestamp
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var paiFileName = $"PAI_{_selectedPatient.Prenom}_{_selectedPatient.Nom}_{timestamp}.pdf";
            var paiDestPath = Path.Combine(formulairesDir, paiFileName);
            
            // Copier le modèle vers le dossier du patient
            File.Copy(paiModelPath, paiDestPath, overwrite: false);
            
            // Ouvrir le PDF copié avec le lecteur par défaut
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = paiDestPath,
                UseShellExecute = true
            };
            System.Diagnostics.Process.Start(psi);
            
            // NOUVEAU : Ouvrir le dialogue pour renseigner le motif du PAI
            var motifDialog = new PAIMotifDialog();
            motifDialog.Owner = this;
            
            if (motifDialog.ShowDialog() == true && !string.IsNullOrEmpty(motifDialog.Motif))
            {
                // Créer et sauvegarder la synthèse en JSON
                var synthesis = new PAISynthesis
                {
                    Type = "PAI",
                    DateCreation = DateTime.Now,
                    Patient = _selectedPatient.NomComplet,
                    Motif = motifDialog.Motif,
                    FileName = paiFileName
                };
                
                // Sauvegarder le JSON à côté du PDF
                var jsonPath = Path.ChangeExtension(paiDestPath, ".json");
                var jsonContent = System.Text.Json.JsonSerializer.Serialize(synthesis, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(jsonPath, jsonContent, Encoding.UTF8);
                
                StatusTextBlock.Text = $"✅ PAI créé avec motif : {motifDialog.Motif}";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
            }
            else
            {
                // L'utilisateur a annulé ou n'a pas renseigné de motif
                StatusTextBlock.Text = $"⚠️ PAI créé sans motif enregistré";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
            }
            
            // Rafraîchir la liste des formulaires
            LoadPatientFormulaires();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Erreur lors de la création du formulaire PAI :\n\n{ex.Message}",
                "Erreur",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
            
            StatusTextBlock.Text = $"❌ Erreur : {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
        }
    }

#endif // ===== FIN OBSOLÈTE : Formulaires migré vers FormulairesControl =====

    // NOTE: OpenDropWindowButton_Click MIGRÉ vers DocumentsControl.xaml.cs
#if false
    private void OpenDropWindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPatient == null)
        {
            MessageBox.Show("Veuillez d'abord sélectionner un patient.", "Information",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var dropWindow = new Dialogs.DocumentDropWindow(
                _selectedPatient.NomComplet,
                _selectedPatient.DirectoryPath,
                _documentService,
                () => LoadPatientDocuments() // Callback pour rafraîchir la liste
            );

            dropWindow.Show();

            StatusTextBlock.Text = "🪟 Fenêtre drag & drop ouverte - Vous pouvez minimiser l'application principale";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors de l'ouverture de la fenêtre:\n\n{ex.Message}",
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);

            StatusTextBlock.Text = $"❌ Erreur: {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
        }
    }
#endif

}
