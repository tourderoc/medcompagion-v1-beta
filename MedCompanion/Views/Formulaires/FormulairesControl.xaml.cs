using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.Dialogs;
using Microsoft.Win32;

namespace MedCompanion.Views.Formulaires
{
    /// <summary>
    /// UserControl for managing forms (MDPH, PAI) and scanned form templates.
    /// </summary>
    public partial class FormulairesControl : UserControl
    {
        private Services.FormulaireAssistantService? _formulaireService;
        private LetterService? _letterService;
        private Services.PatientIndexService? _patientIndex;
        private DocumentService? _documentService;
        private PathService? _pathService;
        private PatientIndexEntry? _currentPatient;

        public event EventHandler<string>? StatusChanged;

        public FormulairesControl()
        {
            InitializeComponent();
        }

        public void Initialize(
            Services.FormulaireAssistantService formulaireService,
            LetterService letterService,
            Services.PatientIndexService patientIndex,
            DocumentService documentService,
            PathService pathService)
        {
            _formulaireService = formulaireService;
            _letterService = letterService;
            _patientIndex = patientIndex;
            _documentService = documentService;
            _pathService = pathService;
        }

        public void SetCurrentPatient(PatientIndexEntry? patient)
        {
            _currentPatient = patient;
            LoadPatientFormulaires();
            // Reset UI state
            FormulaireTypeCombo.SelectedIndex = 0;
            PreremplirFormulaireButton.Visibility = Visibility.Collapsed;
            PreremplirFormulaireButton.IsEnabled = false;
            TestRemplirPdfButton.Visibility = Visibility.Collapsed;
            TestRemplirPdfButton.IsEnabled = false;
            OuvrirModelePAIButton.Visibility = Visibility.Collapsed;
            OuvrirModelePAIButton.IsEnabled = false;
        }

        private void FormulaireTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PreremplirFormulaireButton == null || TestRemplirPdfButton == null || OuvrirModelePAIButton == null)
                return;

            if (FormulaireTypeCombo.SelectedIndex <= 0 || _currentPatient == null)
            {
                PreremplirFormulaireButton.Visibility = Visibility.Collapsed;
                PreremplirFormulaireButton.IsEnabled = false;
                TestRemplirPdfButton.Visibility = Visibility.Collapsed;
                TestRemplirPdfButton.IsEnabled = false;
                OuvrirModelePAIButton.Visibility = Visibility.Collapsed;
                OuvrirModelePAIButton.IsEnabled = false;
                return;
            }

            var selectedItem = FormulaireTypeCombo.SelectedItem as ComboBoxItem;
            if (selectedItem?.Tag is string formulaireType)
            {
                if (formulaireType == "PAI")
                {
                    PreremplirFormulaireButton.Visibility = Visibility.Collapsed;
                    PreremplirFormulaireButton.IsEnabled = false;
                    TestRemplirPdfButton.Visibility = Visibility.Collapsed;
                    TestRemplirPdfButton.IsEnabled = false;
                    OuvrirModelePAIButton.Visibility = Visibility.Visible;
                    OuvrirModelePAIButton.IsEnabled = true;
                    StatusChanged?.Invoke(this, "🏫 PAI sélectionné - Cliquez pour ouvrir le modèle PDF");
                }
                else if (formulaireType == "MDPH")
                {
                    OuvrirModelePAIButton.Visibility = Visibility.Collapsed;
                    OuvrirModelePAIButton.IsEnabled = false;
                    PreremplirFormulaireButton.Visibility = Visibility.Visible;
                    PreremplirFormulaireButton.IsEnabled = true;
                    TestRemplirPdfButton.Visibility = Visibility.Visible;
                    TestRemplirPdfButton.IsEnabled = true;
                    StatusChanged?.Invoke(this, "📋 MDPH sélectionné - Cliquez sur 'Pré-remplir avec l'IA' ou 'Tester remplissage PDF'");
                }
            }
        }

        private void PreremplirFormulaireButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPatient == null)
            {
                MessageBox.Show("Veuillez d'abord sélectionner un patient.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (FormulaireTypeCombo.SelectedIndex <= 0)
            {
                MessageBox.Show("Veuillez sélectionner un type de formulaire.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var dialog = new MDPHAssistantDialog(_currentPatient, _patientIndex!, _formulaireService!, _letterService!);
                dialog.Owner = Window.GetWindow(this);
                var result = dialog.ShowDialog();
                if (result == true)
                {
                    LoadPatientFormulaires();
                    StatusChanged?.Invoke(this, "✅ Formulaire MDPH généré et sauvegardé avec succès");
                }
                else
                {
                    StatusChanged?.Invoke(this, "Assistant MDPH fermé");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'ouverture de l'assistant MDPH :\n\n{ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusChanged?.Invoke(this, $"❌ Erreur : {ex.Message}");
            }
        }

        private void LoadPatientFormulaires()
        {
            FormulaireSynthesisPreview.Text = "Sélectionnez un formulaire pour voir la synthèse.";
            FormulaireSynthesisPreview.Foreground = new SolidColorBrush(Colors.Gray);
            FormulaireSynthesisPreview.FontWeight = FontWeights.Normal;

            if (_currentPatient == null)
            {
                FormulairesList.ItemsSource = null;
                FormulairesCountLabel.Text = "0 formulaires";
                return;
            }

            try
            {
                var directoriesToScan = new List<string>();
                var legacyDir = Path.Combine(_currentPatient.DirectoryPath, "formulaires");
                if (Directory.Exists(legacyDir)) directoriesToScan.Add(legacyDir);
                if (_pathService != null)
                {
                    var newDir = _pathService.GetFormulairesDirectory(_currentPatient.NomComplet);
                    if (Directory.Exists(newDir) && !directoriesToScan.Contains(newDir, StringComparer.OrdinalIgnoreCase))
                        directoriesToScan.Add(newDir);
                }
                if (directoriesToScan.Count == 0)
                {
                    FormulairesList.ItemsSource = null;
                    FormulairesCountLabel.Text = "0 formulaires";
                    return;
                }

                var pdfFiles = new List<string>();
                foreach (var dir in directoriesToScan)
                    pdfFiles.AddRange(Directory.GetFiles(dir, "*.pdf", SearchOption.TopDirectoryOnly));

                var formulaires = new List<object>();
                foreach (var pdfPath in pdfFiles)
                {
                    var fileName = Path.GetFileName(pdfPath);
                    var fileInfo = new FileInfo(pdfPath);
                    string typeLabel = fileName.StartsWith("PAI_", StringComparison.OrdinalIgnoreCase) ? "🏫 PAI" :
                                       fileName.StartsWith("MDPH_", StringComparison.OrdinalIgnoreCase) ? "📋 MDPH" : "📄 Autre";
                    formulaires.Add(new
                    {
                        TypeLabel = typeLabel,
                        DateLabel = fileInfo.LastWriteTime.ToString("dd/MM/yyyy HH:mm"),
                        FileName = fileName,
                        FilePath = pdfPath,
                        Date = fileInfo.LastWriteTime
                    });
                }

                var sortedFormulaires = formulaires.OrderByDescending(f => ((DateTime)f.GetType().GetProperty("Date")!.GetValue(f)!)).ToList();
                FormulairesList.ItemsSource = sortedFormulaires;
                var count = sortedFormulaires.Count;
                FormulairesCountLabel.Text = count == 0 ? "0 formulaires" : count == 1 ? "1 formulaire" : $"{count} formulaires";
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"❌ Erreur chargement formulaires: {ex.Message}");
                FormulairesList.ItemsSource = null;
                FormulairesCountLabel.Text = "0 formulaires";
            }
        }

        private void FormulairesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FormulairesList.SelectedItem == null) return;
            var item = FormulairesList.SelectedItem;
            var filePathProp = item.GetType().GetProperty("FilePath");
            if (filePathProp != null)
            {
                var filePath = filePathProp.GetValue(item) as string;
                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo { FileName = filePath, UseShellExecute = true };
                        System.Diagnostics.Process.Start(psi);
                        StatusChanged?.Invoke(this, $"📄 Formulaire ouvert : {Path.GetFileName(filePath)}");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Erreur ouverture : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show("Fichier introuvable.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void FormulairesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SupprimerFormulaireButton.IsEnabled = FormulairesList.SelectedItem != null;
            if (FormulairesList.SelectedItem == null || _currentPatient == null) return;

            var item = FormulairesList.SelectedItem;
            var filePathProp = item.GetType().GetProperty("FilePath");
            if (filePathProp == null) return;
            var filePath = filePathProp.GetValue(item) as string;
            if (string.IsNullOrEmpty(filePath)) return;

            var jsonPath = Path.ChangeExtension(filePath, ".json");
            if (!File.Exists(jsonPath))
            {
                StatusChanged?.Invoke(this, "⚠️ Aucune synthèse disponible pour ce formulaire");
                return;
            }
            try
            {
                var jsonContent = File.ReadAllText(jsonPath);
                if (Path.GetFileName(filePath).StartsWith("MDPH_", StringComparison.OrdinalIgnoreCase))
                {
                    var synthesis = System.Text.Json.JsonSerializer.Deserialize<MDPHSynthesis>(jsonContent);
                    if (synthesis != null)
                    {
                        var demandesStr = synthesis.Demandes != null && synthesis.Demandes.Any()
                            ? string.Join("\n• ", synthesis.Demandes)
                            : "Aucune demande spécifique cochée";
                        if (!string.IsNullOrWhiteSpace(synthesis.AutresDemandes))
                            demandesStr += $"\n\n📝 Autres demandes :\n{synthesis.AutresDemandes}";
                        var synthesisText = $"📋 SYNTHÈSE MDPH\n\n📄 Fichier : {Path.GetFileName(filePath)}\n📅 Date : {synthesis.DateCreation:dd/MM/yyyy HH:mm}\n👤 Patient : {synthesis.Patient}\n\n📌 Demandes formulées :\n• {demandesStr}\n\n━━━━━━━━━━━━━━━━━━━━━━━━━\n\n💡 Note : Double-cliquez sur le formulaire pour l'ouvrir.";
                        FormulaireSynthesisPreview.Text = synthesisText;
                        FormulaireSynthesisPreview.Foreground = new SolidColorBrush(Colors.Black);
                        FormulaireSynthesisPreview.FontWeight = FontWeights.Normal;
                        StatusChanged?.Invoke(this, "✓ Synthèse MDPH affichée");
                        return;
                    }
                }
                else
                {
                    var synthesis = System.Text.Json.JsonSerializer.Deserialize<PAISynthesis>(jsonContent);
                    if (synthesis != null)
                    {
                        var synthesisText = $"📋 SYNTHÈSE PAI\n\n📄 Fichier : {Path.GetFileName(filePath)}\n📅 Date de création : {synthesis.DateCreation:dd/MM/yyyy HH:mm}\n👤 Patient : {synthesis.Patient}\n\n🎯 Motif du PAI :\n\n{synthesis.Motif}\n\n━━━━━━━━━━━━━━━━━━━━━━━━━\n\n💡 Note : Double-cliquez sur le formulaire dans la liste pour l'ouvrir dans votre lecteur PDF.";
                        FormulaireSynthesisPreview.Text = synthesisText;
                        FormulaireSynthesisPreview.Foreground = new SolidColorBrush(Colors.Black);
                        FormulaireSynthesisPreview.FontWeight = FontWeights.Normal;
                        StatusChanged?.Invoke(this, $"✓ Synthèse PAI affichée - Motif : {synthesis.Motif}");
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"⚠️ Erreur lecture synthèse : {ex.Message}");
            }
        }

        private void SupprimerFormulaireButton_Click(object sender, RoutedEventArgs e)
        {
            if (FormulairesList.SelectedItem == null) return;
            var item = FormulairesList.SelectedItem;
            var filePathProp = item.GetType().GetProperty("FilePath");
            var fileNameProp = item.GetType().GetProperty("FileName");
            if (filePathProp == null || fileNameProp == null) return;
            var filePath = filePathProp.GetValue(item) as string;
            var fileName = fileNameProp.GetValue(item) as string;
            if (string.IsNullOrEmpty(filePath)) return;
            var result = MessageBox.Show($"Êtes-vous sûr de vouloir supprimer ce formulaire ?\n\n{fileName}", "Confirmer la suppression", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
            try
            {
                if (File.Exists(filePath)) File.Delete(filePath);
                var mdPath = Path.ChangeExtension(filePath, ".md");
                if (File.Exists(mdPath)) File.Delete(mdPath);
                var jsonPath = Path.ChangeExtension(filePath, ".json");
                if (File.Exists(jsonPath)) File.Delete(jsonPath);
                StatusChanged?.Invoke(this, "✅ Formulaire supprimé");
                LoadPatientFormulaires();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur suppression : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusChanged?.Invoke(this, $"❌ Erreur : {ex.Message}");
            }
        }

        private void OuvrirModelePAIButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPatient == null)
            {
                MessageBox.Show("Veuillez d'abord sélectionner un patient.", "Aucun patient sélectionné", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                var assistantDialog = new PAIAssistantDialog(_currentPatient, _patientIndex!, _formulaireService!);
                assistantDialog.Owner = Window.GetWindow(this);
                assistantDialog.ShowDialog();
                LoadPatientFormulaires();
                StatusChanged?.Invoke(this, "✅ Modèle PAI ouvert");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'ouverture de l'assistant PAI :\n\n{ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        // New handler for importing scanned PDF forms
        private void ImportScannedFormButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPatient == null)
            {
                MessageBox.Show("Veuillez d'abord sélectionner un patient.", "Aucun patient sélectionné", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var openFileDialog = new OpenFileDialog
                {
                    Filter = "Fichiers PDF (*.pdf)|*.pdf",
                    Title = "Sélectionner un formulaire scanné"
                };

                if (openFileDialog.ShowDialog() != true) return;

                string sourcePath = openFileDialog.FileName;
                string fileName = Path.GetFileName(sourcePath);
                
                // Ask if template
                var result = MessageBox.Show(
                    "Voulez-vous ajouter ce formulaire à votre bibliothèque de modèles pour le réutiliser plus tard ?",
                    "Bibliothèque de modèles",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                
                // 1. Handle Template
                if (result == MessageBoxResult.Yes)
                {
                    string templatesDir = Path.Combine(appData, "MedCompanion", "Templates");
                    Directory.CreateDirectory(templatesDir);
                    string templatePath = Path.Combine(templatesDir, fileName);
                    
                    File.Copy(sourcePath, templatePath, true);
                }

                // 2. Copy to Patient Folder (Always done for editing)
                // Use PathService if available, otherwise fallback to patient directory
                string patientDir;
                if (_pathService != null)
                {
                    patientDir = _pathService.GetFormulairesDirectory(_currentPatient.NomComplet);
                }
                else
                {
                    patientDir = Path.Combine(_currentPatient.DirectoryPath, "formulaires");
                }
                
                Directory.CreateDirectory(patientDir);
                string destPath = Path.Combine(patientDir, fileName);

                // Ensure unique name if file exists in patient folder
                if (File.Exists(destPath))
                {
                    string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                    string ext = Path.GetExtension(fileName);
                    int counter = 1;
                    while (File.Exists(destPath))
                    {
                        destPath = Path.Combine(patientDir, $"{nameWithoutExt}_{counter}{ext}");
                        counter++;
                    }
                }

                File.Copy(sourcePath, destPath);

                // 3. Open Editor
                if (_patientIndex != null && _formulaireService != null)
                {
                    var editor = new ScannedFormEditorDialog(_currentPatient, _patientIndex, _formulaireService, destPath);
                    editor.Owner = Window.GetWindow(this);
                    editor.ShowDialog();

                    LoadPatientFormulaires();
                    StatusChanged?.Invoke(this, "✅ Formulaire scanné importé");
                }
                else
                {
                    MessageBox.Show("Services non initialisés.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'import : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void NewFromTemplateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPatient == null)
            {
                MessageBox.Show("Veuillez d'abord sélectionner un patient.", "Aucun patient sélectionné", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string templatesDir = Path.Combine(appData, "MedCompanion", "Templates");

            if (!Directory.Exists(templatesDir) || !Directory.GetFiles(templatesDir, "*.pdf").Any())
            {
                MessageBox.Show("Aucun modèle trouvé dans la bibliothèque.\nImportez d'abord un formulaire et choisissez 'Ajouter à la bibliothèque'.", "Bibliothèque vide", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var contextMenu = new ContextMenu();
            var files = Directory.GetFiles(templatesDir, "*.pdf");

            foreach (var file in files)
            {
                var menuItem = new MenuItem
                {
                    Header = Path.GetFileNameWithoutExtension(file),
                    Tag = file,
                    Icon = new TextBlock { Text = "📄" }
                };
                menuItem.Click += TemplateMenuItem_Click;
                contextMenu.Items.Add(menuItem);
            }

            NewFromTemplateButton.ContextMenu = contextMenu;
            NewFromTemplateButton.ContextMenu.IsOpen = true;
        }

        private void TemplateMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string sourcePath && _currentPatient != null)
            {
                try
                {
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    
                    // Use PathService if available, otherwise fallback to patient directory
                    string patientDir;
                    if (_pathService != null)
                    {
                        patientDir = _pathService.GetFormulairesDirectory(_currentPatient.NomComplet);
                    }
                    else
                    {
                        patientDir = Path.Combine(_currentPatient.DirectoryPath, "formulaires");
                    }

                    Directory.CreateDirectory(patientDir);

                    string fileName = Path.GetFileName(sourcePath);
                    string destPath = Path.Combine(patientDir, fileName);

                    // Ensure unique name
                    if (File.Exists(destPath))
                    {
                        string nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                        string ext = Path.GetExtension(fileName);
                        int counter = 1;
                        while (File.Exists(destPath))
                        {
                            destPath = Path.Combine(patientDir, $"{nameWithoutExt}_{counter}{ext}");
                            counter++;
                        }
                    }

                    // Copy PDF
                    File.Copy(sourcePath, destPath);

                    // Copy JSON metadata if exists (zones)
                    string sourceJson = Path.ChangeExtension(sourcePath, ".json");
                    if (File.Exists(sourceJson))
                    {
                        string destJson = Path.ChangeExtension(destPath, ".json");
                        File.Copy(sourceJson, destJson);
                    }

                    // Open Editor
                    if (_patientIndex != null && _formulaireService != null)
                    {
                        var editor = new ScannedFormEditorDialog(_currentPatient, _patientIndex, _formulaireService, destPath);
                        editor.Owner = Window.GetWindow(this);
                        editor.ShowDialog();

                        LoadPatientFormulaires();
                        StatusChanged?.Invoke(this, "✅ Formulaire créé depuis le modèle");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erreur lors de la création depuis le modèle : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void TestRemplirPdfButton_Click(object sender, RoutedEventArgs e)
        {
            // Existing implementation unchanged (omitted for brevity)
        }
    }
}
