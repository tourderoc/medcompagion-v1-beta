using System;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using MedCompanion.Services;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Dialogue pour afficher et éditer du contenu en plein écran
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

        public DetailedViewDialog()
        {
            InitializeComponent();
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
                // Convertir Markdown en FlowDocument
                var flowDocument = MarkdownToFlowDocument(markdownContent);
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
        /// Convertit Markdown simple en FlowDocument
        /// </summary>
        private FlowDocument MarkdownToFlowDocument(string markdown)
        {
            var flowDoc = new FlowDocument();
            flowDoc.PagePadding = new Thickness(0);

            var lines = markdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            Paragraph? currentParagraph = null;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    if (currentParagraph != null)
                    {
                        flowDoc.Blocks.Add(currentParagraph);
                        currentParagraph = null;
                    }
                    continue;
                }

                // Headers
                if (line.StartsWith("### "))
                {
                    if (currentParagraph != null)
                    {
                        flowDoc.Blocks.Add(currentParagraph);
                        currentParagraph = null;
                    }
                    var para = new Paragraph(new Run(line.Substring(4)))
                    {
                        FontSize = 16,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromRgb(44, 62, 80)),
                        Margin = new Thickness(0, 10, 0, 5)
                    };
                    flowDoc.Blocks.Add(para);
                }
                else if (line.StartsWith("## "))
                {
                    if (currentParagraph != null)
                    {
                        flowDoc.Blocks.Add(currentParagraph);
                        currentParagraph = null;
                    }
                    var para = new Paragraph(new Run(line.Substring(3)))
                    {
                        FontSize = 18,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(26, 188, 156)),
                        Margin = new Thickness(0, 15, 0, 8)
                    };
                    flowDoc.Blocks.Add(para);
                }
                else if (line.StartsWith("# "))
                {
                    if (currentParagraph != null)
                    {
                        flowDoc.Blocks.Add(currentParagraph);
                        currentParagraph = null;
                    }
                    var para = new Paragraph(new Run(line.Substring(2)))
                    {
                        FontSize = 20,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(52, 73, 94)),
                        Margin = new Thickness(0, 20, 0, 10)
                    };
                    flowDoc.Blocks.Add(para);
                }
                // Liste à puces
                else if (line.TrimStart().StartsWith("• ") || line.TrimStart().StartsWith("- "))
                {
                    if (currentParagraph != null)
                    {
                        flowDoc.Blocks.Add(currentParagraph);
                        currentParagraph = null;
                    }
                    var text = line.TrimStart().Substring(2);
                    var para = new Paragraph(new Run("  • " + text))
                    {
                        Margin = new Thickness(20, 2, 0, 2)
                    };
                    flowDoc.Blocks.Add(para);
                }
                // Texte normal
                else
                {
                    if (currentParagraph == null)
                    {
                        currentParagraph = new Paragraph();
                        currentParagraph.Margin = new Thickness(0, 0, 0, 10);
                    }
                    currentParagraph.Inlines.Add(new Run(line + " "));
                }
            }

            if (currentParagraph != null)
            {
                flowDoc.Blocks.Add(currentParagraph);
            }

            return flowDoc;
        }

        /// <summary>
        /// Bascule en mode édition
        /// </summary>
        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            SwitchToEditMode();
        }

        private void SwitchToEditMode()
        {
            _isEditMode = true;

            // Copier le contenu en mode édition
            var textRange = new TextRange(ReadOnlyContent.Document.ContentStart, ReadOnlyContent.Document.ContentEnd);
            var editDoc = new FlowDocument();
            using (var stream = new MemoryStream())
            {
                textRange.Save(stream, DataFormats.Xaml);
                stream.Position = 0;
                var editRange = new TextRange(editDoc.ContentStart, editDoc.ContentEnd);
                editRange.Load(stream, DataFormats.Xaml);
            }

            EditableContent.Document = editDoc;

            // Basculer l'affichage
            ReadOnlyContent.Visibility = Visibility.Collapsed;
            EditableContent.Visibility = Visibility.Visible;
            EditButton.Visibility = Visibility.Collapsed;
            EditButtonsPanel.Visibility = Visibility.Visible;

            StatusTextBlock.Text = "Mode édition";
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
            EditButton.Visibility = Visibility.Visible;
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
                // Extraire le texte du RichTextBox
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
