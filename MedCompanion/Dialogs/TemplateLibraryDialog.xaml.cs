using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace MedCompanion.Dialogs
{
    public partial class TemplateLibraryDialog : Window
    {
        #region Inner Classes

        public class TemplateInfo
        {
            public string Name { get; set; } = string.Empty;
            public string FullPath { get; set; } = string.Empty;
            public DateTime DateCreated { get; set; }
            public string DateCreatedFormatted => DateCreated.ToString("dd/MM/yyyy HH:mm");
        }

        #endregion

        #region Properties

        public string? SelectedTemplatePath { get; private set; }

        #endregion

        #region Fields

        private readonly string _templatesDirectory;
        private List<TemplateInfo> _templates = new List<TemplateInfo>();

        #endregion

        #region Constructor

        public TemplateLibraryDialog()
        {
            InitializeComponent();

            // Get templates directory
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _templatesDirectory = Path.Combine(appDataPath, "MedCompanion", "Templates");

            // Ensure directory exists
            if (!Directory.Exists(_templatesDirectory))
            {
                Directory.CreateDirectory(_templatesDirectory);
            }

            Loaded += TemplateLibraryDialog_Loaded;
        }

        #endregion

        #region Event Handlers

        private void TemplateLibraryDialog_Loaded(object sender, RoutedEventArgs e)
        {
            LoadTemplates();
        }

        private void TemplateListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedTemplate = TemplateListBox.SelectedItem as TemplateInfo;
            
            if (selectedTemplate != null)
            {
                // Enable action buttons
                UseButton.IsEnabled = true;
                DeleteButton.IsEnabled = true;
                
                // Show selected template info
                SelectedInfoPanel.Visibility = Visibility.Visible;
                SelectedTemplateNameText.Text = selectedTemplate.Name;
            }
            else
            {
                // Disable action buttons
                UseButton.IsEnabled = false;
                DeleteButton.IsEnabled = false;
                
                // Hide selected template info
                SelectedInfoPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void UseButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedTemplate = TemplateListBox.SelectedItem as TemplateInfo;
            
            if (selectedTemplate != null)
            {
                SelectedTemplatePath = selectedTemplate.FullPath;
                DialogResult = true;
                Close();
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedTemplate = TemplateListBox.SelectedItem as TemplateInfo;
            
            if (selectedTemplate == null) return;

            // Confirmation dialog
            var result = MessageBox.Show(
                $"Êtes-vous sûr de vouloir supprimer le modèle \"{selectedTemplate.Name}\" ?\\n\\nCette action est irréversible.",
                "Confirmer la suppression",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // Delete PDF file
                    if (File.Exists(selectedTemplate.FullPath))
                    {
                        File.Delete(selectedTemplate.FullPath);
                    }

                    // Delete associated JSON file if exists
                    var jsonPath = Path.ChangeExtension(selectedTemplate.FullPath, ".json");
                    if (File.Exists(jsonPath))
                    {
                        File.Delete(jsonPath);
                    }

                    // Reload templates
                    LoadTemplates();

                    MessageBox.Show(
                        "Modèle supprimé avec succès.",
                        "Succès",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Erreur lors de la suppression du modèle : {ex.Message}",
                        "Erreur",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        #endregion

        #region Methods

        private void LoadTemplates()
        {
            try
            {
                _templates.Clear();

                if (!Directory.Exists(_templatesDirectory))
                {
                    UpdateUI();
                    return;
                }

                // Get all PDF files in templates directory
                var pdfFiles = Directory.GetFiles(_templatesDirectory, "*.pdf");

                foreach (var pdfFile in pdfFiles)
                {
                    var fileInfo = new FileInfo(pdfFile);
                    
                    _templates.Add(new TemplateInfo
                    {
                        Name = Path.GetFileNameWithoutExtension(pdfFile),
                        FullPath = pdfFile,
                        DateCreated = fileInfo.CreationTime
                    });
                }

                // Sort by date (most recent first)
                _templates = _templates.OrderByDescending(t => t.DateCreated).ToList();

                UpdateUI();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Erreur lors du chargement des modèles : {ex.Message}",
                    "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void UpdateUI()
        {
            // Update list
            TemplateListBox.ItemsSource = _templates;

            // Update count
            TemplateCountText.Text = $"{_templates.Count} modèle(s)";

            // Show/hide empty state
            if (_templates.Count == 0)
            {
                TemplateListBox.Visibility = Visibility.Collapsed;
                EmptyStatePanel.Visibility = Visibility.Visible;
                UseButton.IsEnabled = false;
                DeleteButton.IsEnabled = false;
                SelectedInfoPanel.Visibility = Visibility.Collapsed;
            }
            else
            {
                TemplateListBox.Visibility = Visibility.Visible;
                EmptyStatePanel.Visibility = Visibility.Collapsed;
            }
        }

        #endregion
    }
}
