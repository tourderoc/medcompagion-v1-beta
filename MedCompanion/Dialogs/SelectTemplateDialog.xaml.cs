using System.Collections.Generic;
using System.Windows;

namespace MedCompanion.Dialogs
{
    public partial class SelectTemplateDialog : Window
    {
        public string? SelectedTemplate { get; private set; }

        public SelectTemplateDialog(List<string> availableTemplates)
        {
            InitializeComponent();
            
            // Remplir la liste avec tous les modèles disponibles
            TemplateListBox.ItemsSource = availableTemplates;
            
            // Activer le bouton OK quand un élément est sélectionné
            TemplateListBox.SelectionChanged += (s, e) =>
            {
                OkButton.IsEnabled = TemplateListBox.SelectedItem != null;
            };
            
            // Double-clic pour valider directement
            TemplateListBox.MouseDoubleClick += (s, e) =>
            {
                if (TemplateListBox.SelectedItem != null)
                {
                    OkButton_Click(s, e);
                }
            };
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (TemplateListBox.SelectedItem is string selectedTemplate)
            {
                SelectedTemplate = selectedTemplate;
                DialogResult = true;
                Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
