using System;
using System.IO;
using System.Windows;
using MedCompanion.Models;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Fenêtre modale pour visualiser et éditer la synthèse IA d'un document patient (bilan, courrier, etc.).
    /// </summary>
    public partial class DocumentDetailDialog : Window
    {
        private readonly PatientDocumentItem _item;

        public DocumentDetailDialog(PatientDocumentItem item)
        {
            InitializeComponent();
            _item = item;

            // En-tête
            HeaderFileName.Text = $"📄 {item.FileName}";
            HeaderMeta.Text     = $"{Capitalize(item.Category)} • Ajouté le {item.DateFormatted}";

            // Rendu Markdown formaté en lecture
            RefreshReadonlyView();

            // Pas de synthèse → message + on cache "Modifier" (rien à modifier)
            if (string.IsNullOrWhiteSpace(item.SynthesisContent))
            {
                EditButton.IsEnabled = false;
                StatusText.Text = "ℹ️ Pas de synthèse IA générée pour ce document.";
            }
        }

        private void RefreshReadonlyView()
        {
            ContentRichTextBox.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(
                string.IsNullOrWhiteSpace(_item.SynthesisContent)
                    ? "*Pas de synthèse pour ce document.*"
                    : _item.SynthesisContent);
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            // Passer en mode édition
            ContentEditTextBox.Text       = _item.SynthesisContent;
            ContentRichTextBox.Visibility = Visibility.Collapsed;
            ContentEditTextBox.Visibility = Visibility.Visible;
            EditButton.Visibility         = Visibility.Collapsed;
            SaveButton.Visibility         = Visibility.Visible;
            CancelEditButton.Visibility   = Visibility.Visible;
            StatusText.Text               = "✏️ Mode édition — modifie le Markdown brut puis enregistre.";
        }

        private void CancelEditButton_Click(object sender, RoutedEventArgs e)
        {
            BackToReadonly();
            StatusText.Text = "";
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_item.SynthesisFilePath))
                {
                    StatusText.Text = "❌ Pas de fichier de synthèse associé — impossible d'enregistrer.";
                    return;
                }

                var newContent = ContentEditTextBox.Text;

                // Backup
                try
                {
                    var dir    = Path.GetDirectoryName(_item.SynthesisFilePath) ?? "";
                    var name   = Path.GetFileNameWithoutExtension(_item.SynthesisFilePath);
                    var backup = Path.Combine(dir, $"{name}_backup_{DateTime.Now:yyyyMMdd_HHmmss}.md");
                    File.Copy(_item.SynthesisFilePath, backup, true);
                }
                catch { }

                // Préserver le YAML header existant s'il est présent
                string yamlHeader = "";
                if (File.Exists(_item.SynthesisFilePath))
                {
                    var existing = File.ReadAllText(_item.SynthesisFilePath, System.Text.Encoding.UTF8);
                    if (existing.TrimStart().StartsWith("---"))
                    {
                        var first  = existing.IndexOf("---", StringComparison.Ordinal);
                        var second = existing.IndexOf("---", first + 3, StringComparison.Ordinal);
                        if (second > 0) yamlHeader = existing.Substring(0, second + 3) + "\n\n";
                    }
                }
                if (string.IsNullOrEmpty(yamlHeader))
                {
                    yamlHeader = $"---\ndocument_original: {_item.FileName}\ndate_synthese: {DateTime.Now:dd/MM/yyyy HH:mm:ss}\n---\n\n";
                }

                File.WriteAllText(_item.SynthesisFilePath, yamlHeader + newContent, System.Text.Encoding.UTF8);
                _item.SynthesisContent = newContent;
                RefreshReadonlyView();
                BackToReadonly();
                StatusText.Text = "✅ Synthèse mise à jour (ancienne version sauvegardée à côté).";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"❌ Erreur sauvegarde : {ex.Message}";
            }
        }

        private void BackToReadonly()
        {
            ContentRichTextBox.Visibility = Visibility.Visible;
            ContentEditTextBox.Visibility = Visibility.Collapsed;
            EditButton.Visibility         = Visibility.Visible;
            SaveButton.Visibility         = Visibility.Collapsed;
            CancelEditButton.Visibility   = Visibility.Collapsed;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private static string Capitalize(string s)
            => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
