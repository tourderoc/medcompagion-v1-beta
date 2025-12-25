using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.Services.LLM;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Simplified dialog for importing scanned PDF forms
    /// </summary>
    public partial class ScannedFormImportDialog : UserControl
    {
        private string _selectedPdfPath = string.Empty;
        private readonly PathService _pathService;
        private readonly TemplateLibraryService _templateService;
        private PatientIndexEntry? _patient;

        public ScannedFormImportDialog()
        {
            InitializeComponent();
            _pathService = new PathService();
            _templateService = new TemplateLibraryService();
            
            // Get patient from DataContext when available
            this.DataContextChanged += (s, e) =>
            {
                if (DataContext is PatientIndexEntry patient)
                {
                    _patient = patient;
                }
            };
        }

        private void ImportPdfButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                Title = "Sélectionner le formulaire scanné"
            };
            if (dlg.ShowDialog() == true)
            {
                _selectedPdfPath = dlg.FileName;
                SelectedFileText.Text = Path.GetFileName(_selectedPdfPath);
                SaveButton.IsEnabled = true;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_selectedPdfPath))
            {
                MessageBox.Show("Aucun PDF sélectionné.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_patient == null)
            {
                MessageBox.Show("Aucun patient sélectionné.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Copy PDF to patient formulaires folder (not documents)
                var destDir = _pathService.GetFormulairesDirectory(_patient.NomComplet);
                Directory.CreateDirectory(destDir);
                var destPdfPath = Path.Combine(destDir, Path.GetFileName(_selectedPdfPath));
                File.Copy(_selectedPdfPath, destPdfPath, overwrite: true);

                // Create empty metadata for now (zones can be added later)
                var metadata = new ScannedFormMetadataContainer { Zones = new List<ScannedFormMetadata>() };
                var json = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                var metaPath = Path.ChangeExtension(destPdfPath, ".json");
                File.WriteAllText(metaPath, json);

                // Close the import window
                Window.GetWindow(this)?.Close();

                // AUTO-OPEN: Open the editor dialog to allow user to add zones
                // We need to get the required services
                var patientIndexService = new PatientIndexService();
                var appSettings = AppSettings.Load();
                var llmFactory = new LLMServiceFactory(appSettings);
                var promptConfigService = new PromptConfigService(); // ✅ Créer instance pour OpenAIService
                var anonymizationService = new AnonymizationService(appSettings); // ✅ MODIFIÉ : Passer AppSettings
                var storageService = new StorageService(_pathService);
                var patientContextService = new PatientContextService(storageService, patientIndexService);
                var promptTracker = new PromptTrackerService();
                var openAIService = new OpenAIService(llmFactory, promptConfigService, anonymizationService, promptTracker);
                
                // ✅ NOUVEAU : Initialiser LLMGatewayService
                var llmGatewayService = new LLMGatewayService(llmFactory, anonymizationService, openAIService, _pathService);

                var formulaireService = new FormulaireAssistantService(
                    llmGatewayService,
                    promptConfigService,
                    patientContextService,
                    anonymizationService,
                    llmFactory,
                    appSettings
                );
                
                // Initialize OcrService
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string tessDataPath = Path.Combine(appData, "MedCompanion", "tessdata");
                var ocrService = new OcrService(tessDataPath);

                var editorDialog = new ScannedFormEditorDialog(_patient, patientIndexService, formulaireService, destPdfPath, ocrService);
                editorDialog.Owner = Application.Current.MainWindow;
                editorDialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'import : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Window.GetWindow(this)?.Close();
        }
    }
}
