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
        private SynthesisWeightTracker? _synthesisWeightTracker;
        private PatientIndexEntry? _currentPatient;

        private OcrService? _ocrService; // ✅ OCR Service reference

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
            PathService pathService,
            SynthesisWeightTracker synthesisWeightTracker,
            OcrService ocrService) // ✅ Added OcrService parameter
        {
            _formulaireService = formulaireService;
            _letterService = letterService;
            _patientIndex = patientIndex;
            _documentService = documentService;
            _pathService = pathService;
            _synthesisWeightTracker = synthesisWeightTracker;
            _ocrService = ocrService; // ✅ Store it
        }

        public void SetCurrentPatient(PatientIndexEntry? patient)
        {
            _currentPatient = patient;
            LoadPatientFormulaires();
            // Reset UI state
            FormulaireTypeCombo.SelectedIndex = 0;
            PreremplirFormulaireButton.Visibility = Visibility.Collapsed;
            PreremplirFormulaireButton.IsEnabled = false;
            // TestRemplirPdfButton removed
            // OuvrirModelePAIButton.Visibility = Visibility.Collapsed;
            // OuvrirModelePAIButton.IsEnabled = false;
        }

        private void FormulaireTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PreremplirFormulaireButton == null || OuvrirModelePAIButton == null)
                return;

            if (FormulaireTypeCombo.SelectedIndex <= 0 || _currentPatient == null)
            {
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
                    PreremplirFormulaireButton.Visibility = Visibility.Collapsed;
                    PreremplirFormulaireButton.IsEnabled = false;
                    OuvrirModelePAIButton.Visibility = Visibility.Visible;
                    OuvrirModelePAIButton.IsEnabled = true;
                    StatusChanged?.Invoke(this, "🏫 PAI sélectionné - Cliquez pour ouvrir le modèle PDF");
                }
                else if (formulaireType == "MDPH")
                {
                    PreremplirFormulaireButton.Visibility = Visibility.Visible;
                    PreremplirFormulaireButton.IsEnabled = true;
                    OuvrirModelePAIButton.Visibility = Visibility.Collapsed;
                    OuvrirModelePAIButton.IsEnabled = false;
                    StatusChanged?.Invoke(this, "📋 MDPH sélectionné - Cliquez sur 'Pré-remplir avec l'IA'");
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
                var dialog = new MDPHAssistantDialog(_currentPatient, _patientIndex!, _formulaireService!, _letterService!, _synthesisWeightTracker!);
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
                
                // Group files by their "base name" (ignoring _rempli suffix)
                // Example: "Form.pdf" and "Form._rempli.pdf" -> Base: "Form"
                var groupedFiles = pdfFiles.GroupBy(f => 
                {
                    string name = Path.GetFileNameWithoutExtension(f);
                    if (name.EndsWith("_rempli", StringComparison.OrdinalIgnoreCase))
                    {
                        name = name.Substring(0, name.Length - 7); // Remove _rempli
                    }
                    // Also remove trailing dot if present (e.g. "Form." -> "Form")
                    // This happens if the file was "Form._rempli.pdf"
                    if (name.EndsWith("."))
                    {
                        name = name.Substring(0, name.Length - 1);
                    }
                    return name;
                });

                foreach (var group in groupedFiles)
                {
                    // For each group, prefer the _rempli version if it exists
                    string bestFile = group.FirstOrDefault(f => f.EndsWith("_rempli.pdf", StringComparison.OrdinalIgnoreCase)) 
                                      ?? group.First();

                    var fileName = Path.GetFileName(bestFile);
                    var fileInfo = new FileInfo(bestFile);
                    string typeLabel = fileName.StartsWith("PAI_", StringComparison.OrdinalIgnoreCase) ? "🏫 PAI" :
                                       fileName.StartsWith("MDPH_", StringComparison.OrdinalIgnoreCase) ? "📋 MDPH" : "📄 Autre";
                    
                    formulaires.Add(new
                    {
                        TypeLabel = typeLabel,
                        DateLabel = fileInfo.LastWriteTime.ToString("dd/MM/yyyy HH:mm"),
                        FileName = fileName,
                        FilePath = bestFile,
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
                        OpenPdfWithPreferredBrowser(filePath);
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

            // Handle _rempli.pdf files: look for JSON on the source file
            string jsonPath;
            if (filePath.EndsWith("_rempli.pdf", StringComparison.OrdinalIgnoreCase))
            {
                string sourcePath = filePath.Replace("_rempli.pdf", ".pdf", StringComparison.OrdinalIgnoreCase);
                jsonPath = Path.ChangeExtension(sourcePath, ".json");
            }
            else
            {
                jsonPath = Path.ChangeExtension(filePath, ".json");
            }

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

            // Analyse pour exclusion en cascade
            var directory = Path.GetDirectoryName(filePath);
            var filesToDelete = new List<string>();
            filesToDelete.Add(filePath);

            // LOGIQUE DE SUPPRESSION EN CASCADE POUR MDPH
            // Format attendu : MDPH_YYYYMMDD_HHMMSS ou MDPH_Rempli_YYYYMMDD_HHMMSS
            var fileNameNoExt = Path.GetFileNameWithoutExtension(filePath);
            bool isMdph = fileNameNoExt.StartsWith("MDPH_");

            if (isMdph && directory != null)
            {
                try 
                {
                    // Extraction du timestamp
                    // Regex pour capturer YYYYMMDD_HHMMSS (15 caractères)
                    var match = System.Text.RegularExpressions.Regex.Match(fileNameNoExt, @"(\d{8}_\d{6})");
                    if (match.Success)
                    {
                        var timestampStr = match.Groups[1].Value;
                        if (DateTime.TryParseExact(timestampStr, "yyyyMMdd_HHmmss", null, System.Globalization.DateTimeStyles.None, out var fileDate))
                        {
                            // Chercher tous les fichiers du dossier qui semblent liés (même créneau à +/- 2 minutes)
                            var allFiles = Directory.GetFiles(directory);
                            foreach (var f in allFiles)
                            {
                                var fName = Path.GetFileName(f);
                                if (f == filePath) continue; // Déjà ajouté

                                // Si c'est le log global des champs
                                if (fName.Equals("MDPH_Champs_PDF.txt", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (!filesToDelete.Contains(f)) filesToDelete.Add(f);
                                    continue;
                                }

                                // Vérifier si le fichier contient un timestamp similaire
                                var fMatch = System.Text.RegularExpressions.Regex.Match(fName, @"(\d{8}_\d{6})");
                                if (fMatch.Success)
                                {
                                    var fTimestampStr = fMatch.Groups[1].Value;
                                    if (DateTime.TryParseExact(fTimestampStr, "yyyyMMdd_HHmmss", null, System.Globalization.DateTimeStyles.None, out var fDate))
                                    {
                                        // Si l'écart est < 2 minutes, on considère que c'est le même lot
                                        if (Math.Abs((fDate - fileDate).TotalMinutes) < 2)
                                        {
                                            if (!filesToDelete.Contains(f)) filesToDelete.Add(f);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Erreur analyse cascade: {ex.Message}");
                }
            }
            else
            {
                // Logique standard pour autres fichiers
                // Essayer de trouver le JSON associé
                if (filePath.EndsWith("_rempli.pdf", StringComparison.OrdinalIgnoreCase))
                {
                    var relatedJson = filePath.Replace("_rempli.pdf", "_rempli.json"); 
                    if (File.Exists(relatedJson) && !filesToDelete.Contains(relatedJson)) filesToDelete.Add(relatedJson);
                }
                else
                {
                     var relatedJson = Path.ChangeExtension(filePath, ".json");
                     if (File.Exists(relatedJson) && !filesToDelete.Contains(relatedJson)) filesToDelete.Add(relatedJson);
                }
            }

            // Message de confirmation adapté
            string message = $"Voulez-vous vraiment supprimer ce formulaire ?\n{fileName}";
            if (filesToDelete.Count > 1)
            {
                message += $"\n\n⚠️ Cela supprimera {filesToDelete.Count} fichiers associés (sources, logs, exports Word, JSON...) du même lot.";
            }

            if (MessageBox.Show(message, "Confirmation de suppression", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            try
            {
                int deletedCount = 0;
                foreach (var file in filesToDelete)
                {
                    if (File.Exists(file))
                    {
                        File.Delete(file);
                        deletedCount++;
                        System.Diagnostics.Debug.WriteLine($"[Suppression] Fichier supprimé : {file}");
                    }
                }

                StatusChanged?.Invoke(this, $"✅ {deletedCount} document(s) supprimé(s)");
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
                var assistantDialog = new PAIAssistantDialog(_currentPatient, _patientIndex!, _formulaireService!, _synthesisWeightTracker!);
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
                if (_patientIndex != null && _formulaireService != null && _ocrService != null)
                {
                    var editor = new ScannedFormEditorDialog(_currentPatient, _patientIndex, _formulaireService, destPath, _ocrService);
                    editor.Owner = Window.GetWindow(this);
                    var editorResult = editor.ShowDialog();

                    // If user cancelled (closed without saving), delete the copied PDF
                    if (editorResult != true)
                    {
                        try
                        {
                            if (File.Exists(destPath))
                            {
                                File.Delete(destPath);
                            }
                            // Also delete JSON if it exists
                            var jsonPath = System.IO.Path.ChangeExtension(destPath, ".json");
                            if (File.Exists(jsonPath))
                            {
                                File.Delete(jsonPath);
                            }
                        }
                        catch (Exception deleteEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error deleting cancelled import: {deleteEx.Message}");
                        }
                    }

                    LoadPatientFormulaires();
                    StatusChanged?.Invoke(this, editorResult == true ? "✅ Formulaire scanné importé" : "❌ Import annulé");
                }
                else
                {
                    MessageBox.Show("Services non initialisés (OCR requis).", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
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

            // Open template library dialog
            var libraryDialog = new TemplateLibraryDialog();
            libraryDialog.Owner = Window.GetWindow(this);
            
            if (libraryDialog.ShowDialog() == true && !string.IsNullOrEmpty(libraryDialog.SelectedTemplatePath))
            {
                CreateFormFromTemplate(libraryDialog.SelectedTemplatePath);
            }
        }

        private void CreateFormFromTemplate(string sourcePath)
        {
            if (_currentPatient == null) return;

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
                if (_patientIndex != null && _formulaireService != null && _ocrService != null)
                {
                    var editor = new ScannedFormEditorDialog(_currentPatient, _patientIndex, _formulaireService, destPath, _ocrService);
                    editor.Owner = Window.GetWindow(this);
                    var result = editor.ShowDialog();

                    // If user cancelled (closed without saving), delete the copied files
                    if (result != true)
                    {
                        try
                        {
                            if (File.Exists(destPath))
                            {
                                File.Delete(destPath);
                            }
                            // Also delete JSON if it exists
                            var jsonPath = System.IO.Path.ChangeExtension(destPath, ".json");
                            if (File.Exists(jsonPath))
                            {
                                File.Delete(jsonPath);
                            }
                        }
                        catch (Exception deleteEx)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error deleting cancelled template: {deleteEx.Message}");
                        }
                    }

                    LoadPatientFormulaires();
                    StatusChanged?.Invoke(this, result == true ? "✅ Formulaire créé depuis le modèle" : "❌ Création annulée");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la création depuis le modèle : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        /// <summary>
        /// Ouvre un fichier PDF avec Firefox (si installé) ou avec le navigateur par défaut
        /// </summary>
        private void OpenPdfWithPreferredBrowser(string pdfPath)
        {
            try
            {
                // Liste des chemins Firefox possibles
                var firefoxPaths = new[]
                {
                    @"C:\Program Files\Mozilla Firefox\firefox.exe",
                    @"C:\Program Files (x86)\Mozilla Firefox\firefox.exe",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Mozilla Firefox\firefox.exe"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Mozilla Firefox\firefox.exe")
                };

                // Chercher Firefox
                string? firefoxExe = firefoxPaths.FirstOrDefault(File.Exists);

                if (firefoxExe != null)
                {
                    // Ouvrir avec Firefox
                    System.Diagnostics.Debug.WriteLine($"[FormulairesControl] Ouverture du PDF avec Firefox: {firefoxExe}");
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = firefoxExe,
                        Arguments = $"\"{pdfPath}\"",
                        UseShellExecute = false
                    };
                    System.Diagnostics.Process.Start(psi);
                }
                else
                {
                    // Firefox non trouvé, essayer Chrome
                    var chromePaths = new[]
                    {
                        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                        @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
                        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\Application\chrome.exe")
                    };

                    string? chromeExe = chromePaths.FirstOrDefault(File.Exists);

                    if (chromeExe != null)
                    {
                        // Ouvrir avec Chrome
                        System.Diagnostics.Debug.WriteLine($"[FormulairesControl] Firefox non trouvé, ouverture avec Chrome: {chromeExe}");
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = chromeExe,
                            Arguments = $"\"{pdfPath}\"",
                            UseShellExecute = false
                        };
                        System.Diagnostics.Process.Start(psi);
                    }
                    else
                    {
                        // Ni Firefox ni Chrome, utiliser le navigateur par défaut (probablement Edge)
                        System.Diagnostics.Debug.WriteLine($"[FormulairesControl] Firefox et Chrome non trouvés, ouverture avec le navigateur par défaut");
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = pdfPath,
                            UseShellExecute = true
                        };
                        System.Diagnostics.Process.Start(psi);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FormulairesControl] Erreur lors de l'ouverture du PDF: {ex.Message}");
                MessageBox.Show($"Impossible d'ouvrir le PDF:\n{ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
