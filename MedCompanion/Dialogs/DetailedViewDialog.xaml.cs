using System;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using MedCompanion.Models;
using MedCompanion.Services;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Dialogue pour afficher et éditer du contenu en plein écran
    /// Supporte la régénération via IA avec le RegenerationService
    /// </summary>
    public partial class DetailedViewDialog : Window
    {
        // Événement déclenché après sauvegarde
        public event EventHandler? ContentSaved;

        private ContentType _contentType;
        private string _filePath = string.Empty;
        private string _originalContent = string.Empty;
        private bool _isModified = false;
        private bool _isEditMode = false;

        // Services pour la régénération
        private RegenerationService? _regenerationService;
        private PatientMetadata? _patientMetadata;

        public DetailedViewDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initialise le service de régénération (doit être appelé avant d'utiliser Régénérer)
        /// </summary>
        public void InitializeRegenerationService(RegenerationService regenerationService, PatientMetadata? patientMetadata = null)
        {
            _regenerationService = regenerationService;
            _patientMetadata = patientMetadata;
        }

        /// <summary>
        /// Charge et affiche le contenu
        /// </summary>
        public void LoadContent(string filePath, ContentType contentType, string patientName)
        {
            _filePath = filePath;
            _contentType = contentType;

            // Définir le titre selon le type
            string icon = contentType switch
            {
                ContentType.Synthesis => "📊",
                ContentType.Note => "📝",
                ContentType.Letter => "📄",
                _ => "📄"
            };

            string typeName = contentType switch
            {
                ContentType.Synthesis => "Synthèse",
                ContentType.Note => "Note",
                ContentType.Letter => "Courrier",
                _ => "Document"
            };

            TitleTextBlock.Text = $"{icon} {typeName} - {patientName}";

            // Charger le contenu du fichier
            if (File.Exists(filePath))
            {
                _originalContent = File.ReadAllText(filePath);
                DisplayContent(_originalContent);
            }
            else
            {
                _originalContent = string.Empty;
                DisplayContent("Aucun contenu disponible.");
            }
        }

        /// <summary>
        /// Affiche le contenu dans le RichTextBox en mode lecture
        /// </summary>
        private void DisplayContent(string markdownContent)
        {
            try
            {
                // Convertir Markdown en FlowDocument via le convertisseur centralisé
                // Cela gère le nettoyage du YAML et le formatage
                var flowDocument = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(markdownContent);
                ReadOnlyContent.Document = flowDocument;
            }
            catch
            {
                // Fallback: affichage texte brut
                var flowDocument = new FlowDocument(new Paragraph(new Run(markdownContent)));
                ReadOnlyContent.Document = flowDocument;
            }
        }

        /// <summary>
        /// Bascule en mode édition
        /// </summary>
        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            SwitchToEditMode();
        }

        /// <summary>
        /// Ouvre le dialogue de régénération
        /// </summary>
        private void RegenerateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_regenerationService == null)
            {
                MessageBox.Show(
                    "Le service de régénération n'est pas initialisé.\nVeuillez contacter le support.",
                    "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(_originalContent))
            {
                MessageBox.Show(
                    "Aucun contenu à régénérer.",
                    "Information",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Convertir ContentType en string pour le service
            string contentTypeStr = _contentType switch
            {
                ContentType.Note => "Note",
                ContentType.Synthesis => "Synthesis",
                ContentType.Letter => "Letter",
                _ => "Document"
            };

            // Ouvrir le dialogue de régénération
            var dialog = new RegenerationDialog(
                _regenerationService,
                _originalContent,
                contentTypeStr,
                _patientMetadata);

            dialog.Owner = this;

            if (dialog.ShowDialog() == true && dialog.IsSuccess && !string.IsNullOrEmpty(dialog.RegeneratedContent))
            {
                // Mettre à jour le contenu avec la version régénérée
                _originalContent = dialog.RegeneratedContent;
                _isModified = true;

                // Afficher le nouveau contenu
                DisplayContent(_originalContent);

                // Sauvegarder automatiquement le fichier
                try
                {
                    File.WriteAllText(_filePath, _originalContent);
                    _isModified = false;

                    StatusTextBlock.Text = "✅ Contenu régénéré et sauvegardé";
                    StatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(39, 174, 96));

                    // Notifier la MainWindow
                    ContentSaved?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Le contenu a été régénéré mais n'a pas pu être sauvegardé:\n{ex.Message}",
                        "Avertissement",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }
        }

        private void SwitchToEditMode()
        {
            _isEditMode = true;

            // En mode édition, on affiche le contenu brut (Markdown + YAML)
            // pour permettre l'édition de la structure et des métadonnées
            var editDoc = new FlowDocument();
            var para = new Paragraph(new Run(_originalContent));
            // Utiliser une police à chasse fixe pour l'édition de code/markdown
            para.FontFamily = new FontFamily("Consolas, Courier New");
            editDoc.Blocks.Add(para);

            EditableContent.Document = editDoc;

            // Basculer l'affichage
            ReadOnlyContent.Visibility = Visibility.Collapsed;
            EditableContent.Visibility = Visibility.Visible;
            ReadModeButtonsPanel.Visibility = Visibility.Collapsed;
            EditButtonsPanel.Visibility = Visibility.Visible;

            StatusTextBlock.Text = "Mode édition (Markdown brut)";
            StatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(231, 76, 60));

            EditableContent.Focus();
        }

        /// <summary>
        /// Annule l'édition et revient en mode lecture
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isModified)
            {
                var result = MessageBox.Show(
                    "Annuler les modifications ?",
                    "Confirmation",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );

                if (result != MessageBoxResult.Yes)
                    return;
            }

            SwitchToReadMode();
        }

        private void SwitchToReadMode()
        {
            _isEditMode = false;
            _isModified = false;

            // Recharger le contenu original
            DisplayContent(_originalContent);

            // Basculer l'affichage
            ReadOnlyContent.Visibility = Visibility.Visible;
            EditableContent.Visibility = Visibility.Collapsed;
            ReadModeButtonsPanel.Visibility = Visibility.Visible;
            EditButtonsPanel.Visibility = Visibility.Collapsed;

            StatusTextBlock.Text = "Mode lecture";
            StatusTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(127, 140, 141));
        }

        /// <summary>
        /// Sauvegarde les modifications
        /// </summary>
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Extraire le texte brut du RichTextBox (Markdown)
                var textRange = new TextRange(EditableContent.Document.ContentStart, EditableContent.Document.ContentEnd);
                var content = textRange.Text;

                // Sauvegarder dans le fichier
                File.WriteAllText(_filePath, content);

                _originalContent = content;
                _isModified = false;

                MessageBox.Show(
                    "✅ Contenu sauvegardé avec succès !",
                    "Sauvegarde",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                // Déclencher l'événement pour rafraîchir l'app principale
                ContentSaved?.Invoke(this, EventArgs.Empty);

                // Revenir en mode lecture
                SwitchToReadMode();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"❌ Erreur lors de la sauvegarde :\n{ex.Message}",
                    "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        /// <summary>
        /// Gère la fermeture de la fenêtre
        /// </summary>
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_isEditMode && _isModified)
            {
                var result = MessageBox.Show(
                    "Vous avez des modifications non sauvegardées.\n\nVoulez-vous les sauvegarder avant de fermer ?",
                    "Modifications non sauvegardées",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning
                );

                if (result == MessageBoxResult.Yes)
                {
                    SaveButton_Click(sender, new RoutedEventArgs());
                    // Si la sauvegarde a échoué, annuler la fermeture
                    if (_isModified)
                        e.Cancel = true;
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                }
            }
        }
    }

    /// <summary>
    /// Type de contenu affiché
    /// </summary>
    public enum ContentType
    {
        Synthesis,
        Note,
        Letter
    }
}
