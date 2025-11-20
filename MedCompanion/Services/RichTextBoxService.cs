using System;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service centralisé pour la manipulation des RichTextBox et conversion Markdown
    /// Utilisable par tous les ViewModels (Notes, Attestations, Courriers, etc.)
    /// </summary>
    public class RichTextBoxService
    {
        /// <summary>
        /// Convertit du Markdown en FlowDocument
        /// </summary>
        public FlowDocument ConvertMarkdownToFlowDocument(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return new FlowDocument();
            }

            try
            {
                // Utiliser le converter statique existant du projet
                return MarkdownFlowDocumentConverter.MarkdownToFlowDocument(markdown);
            }
            catch (Exception ex)
            {
                // En cas d'erreur, retourner un document avec le texte brut
                var errorDoc = new FlowDocument();
                errorDoc.Blocks.Add(new Paragraph(new Run(markdown)));
                return errorDoc;
            }
        }

        /// <summary>
        /// Convertit du Markdown en FlowDocument (version manuelle de fallback)
        /// </summary>
        private FlowDocument ConvertMarkdownToFlowDocumentManual(string markdown)
        {
            try
            {
                var flowDocument = new FlowDocument
                {
                    PagePadding = new Thickness(20),
                    FontFamily = new FontFamily("Segoe UI"),
                    FontSize = 14
                };

                // Convertir HTML en FlowDocument (simplifiée)
                var paragraph = new Paragraph();
                
                // Parser les éléments basiques
                var lines = markdown.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    
                    if (string.IsNullOrWhiteSpace(trimmed))
                        continue;

                    // Titres
                    if (trimmed.StartsWith("# "))
                    {
                        var heading = new Paragraph(new Run(trimmed.Substring(2)))
                        {
                            FontSize = 24,
                            FontWeight = FontWeights.Bold,
                            Margin = new Thickness(0, 10, 0, 10)
                        };
                        flowDocument.Blocks.Add(heading);
                    }
                    else if (trimmed.StartsWith("## "))
                    {
                        var heading = new Paragraph(new Run(trimmed.Substring(3)))
                        {
                            FontSize = 20,
                            FontWeight = FontWeights.Bold,
                            Margin = new Thickness(0, 8, 0, 8)
                        };
                        flowDocument.Blocks.Add(heading);
                    }
                    else if (trimmed.StartsWith("### "))
                    {
                        var heading = new Paragraph(new Run(trimmed.Substring(4)))
                        {
                            FontSize = 16,
                            FontWeight = FontWeights.Bold,
                            Margin = new Thickness(0, 6, 0, 6)
                        };
                        flowDocument.Blocks.Add(heading);
                    }
                    // Listes à puces
                    else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                    {
                        var listItem = new Paragraph(new Run("• " + trimmed.Substring(2)))
                        {
                            Margin = new Thickness(20, 0, 0, 5)
                        };
                        flowDocument.Blocks.Add(listItem);
                    }
                    // Texte gras
                    else if (trimmed.Contains("**"))
                    {
                        var para = ProcessFormattedText(trimmed);
                        flowDocument.Blocks.Add(para);
                    }
                    // Texte normal
                    else
                    {
                        var para = new Paragraph(new Run(trimmed))
                        {
                            Margin = new Thickness(0, 0, 0, 10)
                        };
                        flowDocument.Blocks.Add(para);
                    }
                }

                return flowDocument;
            }
            catch (Exception ex)
            {
                // En cas d'erreur, retourner un document avec le texte brut
                var errorDoc = new FlowDocument();
                errorDoc.Blocks.Add(new Paragraph(new Run(markdown)));
                return errorDoc;
            }
        }

        /// <summary>
        /// Traite le texte avec formatage (gras, italique)
        /// </summary>
        private Paragraph ProcessFormattedText(string text)
        {
            var paragraph = new Paragraph();
            var parts = text.Split(new[] { "**" }, StringSplitOptions.None);
            
            for (int i = 0; i < parts.Length; i++)
            {
                if (i % 2 == 0)
                {
                    // Texte normal
                    paragraph.Inlines.Add(new Run(parts[i]));
                }
                else
                {
                    // Texte gras
                    paragraph.Inlines.Add(new Run(parts[i]) { FontWeight = FontWeights.Bold });
                }
            }
            
            paragraph.Margin = new Thickness(0, 0, 0, 10);
            return paragraph;
        }

        /// <summary>
        /// Convertit un FlowDocument en Markdown
        /// </summary>
        public string ConvertFlowDocumentToMarkdown(FlowDocument document)
        {
            if (document == null)
                return string.Empty;

            try
            {
                // Utiliser le converter statique existant du projet
                return MarkdownFlowDocumentConverter.FlowDocumentToMarkdown(document);
            }
            catch
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Définit le contenu d'un RichTextBox depuis du Markdown
        /// </summary>
        public void SetRichTextBoxContent(RichTextBox richTextBox, string markdown)
        {
            if (richTextBox == null)
                throw new ArgumentNullException(nameof(richTextBox));

            try
            {
                var flowDocument = ConvertMarkdownToFlowDocument(markdown);
                richTextBox.Document = flowDocument;
            }
            catch (Exception ex)
            {
                // En cas d'erreur, afficher le texte brut
                var doc = new FlowDocument();
                doc.Blocks.Add(new Paragraph(new Run(markdown ?? string.Empty)));
                richTextBox.Document = doc;
            }
        }

        /// <summary>
        /// Récupère le contenu d'un RichTextBox en Markdown
        /// </summary>
        public string GetRichTextBoxContent(RichTextBox richTextBox)
        {
            if (richTextBox == null)
                throw new ArgumentNullException(nameof(richTextBox));

            return ConvertFlowDocumentToMarkdown(richTextBox.Document);
        }

        /// <summary>
        /// Vide le contenu d'un RichTextBox
        /// </summary>
        public void ClearRichTextBox(RichTextBox richTextBox)
        {
            if (richTextBox == null)
                throw new ArgumentNullException(nameof(richTextBox));

            richTextBox.Document = new FlowDocument();
        }

        /// <summary>
        /// Obtient le texte brut d'un RichTextBox (sans formatage)
        /// </summary>
        public string GetPlainText(RichTextBox richTextBox)
        {
            if (richTextBox == null)
                return string.Empty;

            var textRange = new TextRange(
                richTextBox.Document.ContentStart,
                richTextBox.Document.ContentEnd
            );

            return textRange.Text;
        }

        /// <summary>
        /// Définit le texte brut d'un RichTextBox
        /// </summary>
        public void SetPlainText(RichTextBox richTextBox, string text)
        {
            if (richTextBox == null)
                throw new ArgumentNullException(nameof(richTextBox));

            var doc = new FlowDocument();
            doc.Blocks.Add(new Paragraph(new Run(text ?? string.Empty)));
            richTextBox.Document = doc;
        }

        /// <summary>
        /// Vérifie si un RichTextBox est vide
        /// </summary>
        public bool IsEmpty(RichTextBox richTextBox)
        {
            if (richTextBox == null)
                return true;

            var text = GetPlainText(richTextBox).Trim();
            return string.IsNullOrWhiteSpace(text);
        }
    }
}
