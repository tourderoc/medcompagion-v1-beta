using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace MedCompanion
{
    /// <summary>
    /// Convertit le Markdown en FlowDocument (pour RichTextBox) et vice versa
    /// </summary>
    public class MarkdownFlowDocumentConverter
    {
        /// <summary>
        /// Convertit du Markdown en FlowDocument pour affichage dans RichTextBox
        /// </summary>
        public static FlowDocument MarkdownToFlowDocument(string markdown)
        {
            var document = new FlowDocument();
            
            if (string.IsNullOrWhiteSpace(markdown))
                return document;
            
            // Retirer l'en-tête YAML si présent
            var cleanMarkdown = RemoveYamlHeader(markdown);
            
            var lines = cleanMarkdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    // Ligne vide → Petit espace (seulement 2px)
                    var emptyPara = new Paragraph
                    {
                        Margin = new Thickness(0, 0, 0, 2)
                    };
                    document.Blocks.Add(emptyPara);
                    continue;
                }
                
                // Ligne de séparation (---)
                if (line.Trim() == "---" || line.Trim().All(c => c == '-'))
                {
                    // Ignorer les lignes de séparation (elles ne sont pas nécessaires visuellement)
                    continue;
                }
                
                // Titre H1 (# Titre)
                if (line.StartsWith("# ") && !line.StartsWith("## "))
                {
                    var titleText = line.Substring(2).Trim();
                    var para = new Paragraph(new Run(titleText))
                    {
                        FontSize = 16,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(44, 62, 80)),
                        Margin = new Thickness(0, 4, 0, 2)
                    };
                    document.Blocks.Add(para);
                    continue;
                }
                
                // Titre H2 (## Sous-titre)
                if (line.StartsWith("## ") && !line.StartsWith("### "))
                {
                    var subtitleText = line.Substring(3).Trim();
                    var para = new Paragraph(new Run(subtitleText))
                    {
                        FontSize = 14,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(52, 73, 94)),
                        Margin = new Thickness(0, 3, 0, 2)
                    };
                    document.Blocks.Add(para);
                    continue;
                }
                
                // Titre H3 (### Sous-sous-titre)
                if (line.StartsWith("### ") && !line.StartsWith("#### "))
                {
                    var h3Text = line.Substring(4).Trim();
                    var para = new Paragraph(new Run(h3Text))
                    {
                        FontSize = 13,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromRgb(52, 73, 94)),
                        Margin = new Thickness(0, 2, 0, 1)
                    };
                    document.Blocks.Add(para);
                    continue;
                }
                
                // Titre H4 (#### Sous-sous-sous-titre)
                if (line.StartsWith("#### "))
                {
                    var h4Text = line.Substring(5).Trim();
                    var para = new Paragraph(new Run(h4Text))
                    {
                        FontSize = 12,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Color.FromRgb(52, 73, 94)),
                        Margin = new Thickness(0, 2, 0, 1)
                    };
                    document.Blocks.Add(para);
                    continue;
                }
                
                // Liste à puces (- Item)
                if (line.TrimStart().StartsWith("- "))
                {
                    var indent = line.Length - line.TrimStart().Length;
                    var bulletText = line.TrimStart().Substring(2).Trim();
                    
                    var para = new Paragraph
                    {
                        Margin = new Thickness(15 + (indent * 8), 0, 0, 1),
                        TextIndent = -15
                    };
                    
                    // Ajouter la puce
                    para.Inlines.Add(new Run("• ") 
                    { 
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(52, 152, 219))
                    });
                    
                    // Ajouter le texte avec styles inline
                    ParseInlineMarkdown(bulletText, para);
                    document.Blocks.Add(para);
                    continue;
                }
                
                // Paragraphe normal avec styles inline (espacement minimal)
                var paragraph = new Paragraph
                {
                    Margin = new Thickness(0, 0, 0, 1),
                    TextIndent = 0
                };
                ParseInlineMarkdown(line, paragraph);
                document.Blocks.Add(paragraph);
            }
            
            return document;
        }
        
        /// <summary>
        /// Parse les styles inline (**gras**, *italique*)
        /// </summary>
        private static void ParseInlineMarkdown(string text, Paragraph paragraph)
        {
            // Pattern pour **gras** et *italique*
            var pattern = @"(\*\*[^*]+\*\*)|(\*[^*]+\*)";
            var regex = new Regex(pattern);
            
            int lastIndex = 0;
            
            foreach (Match match in regex.Matches(text))
            {
                // Texte avant le match (normal)
                if (match.Index > lastIndex)
                {
                    var normalText = text.Substring(lastIndex, match.Index - lastIndex);
                    paragraph.Inlines.Add(new Run(normalText));
                }
                
                // Texte avec style
                var matchedText = match.Value;
                
                if (matchedText.StartsWith("**") && matchedText.EndsWith("**"))
                {
                    // Gras
                    var boldText = matchedText.Substring(2, matchedText.Length - 4);
                    paragraph.Inlines.Add(new Run(boldText) { FontWeight = FontWeights.Bold });
                }
                else if (matchedText.StartsWith("*") && matchedText.EndsWith("*"))
                {
                    // Italique
                    var italicText = matchedText.Substring(1, matchedText.Length - 2);
                    paragraph.Inlines.Add(new Run(italicText) { FontStyle = FontStyles.Italic });
                }
                
                lastIndex = match.Index + match.Length;
            }
            
            // Texte restant après le dernier match
            if (lastIndex < text.Length)
            {
                var remainingText = text.Substring(lastIndex);
                paragraph.Inlines.Add(new Run(remainingText));
            }
            
            // Si aucun match, ajouter tout le texte normalement
            if (!regex.IsMatch(text))
            {
                paragraph.Inlines.Clear();
                paragraph.Inlines.Add(new Run(text));
            }
        }
        
        /// <summary>
        /// Convertit un FlowDocument en Markdown
        /// </summary>
        public static string FlowDocumentToMarkdown(FlowDocument document)
        {
            var markdown = new StringBuilder();
            
            foreach (var block in document.Blocks)
            {
                if (block is Paragraph paragraph)
                {
                    var paragraphText = ParagraphToMarkdown(paragraph);
                    
                    if (!string.IsNullOrWhiteSpace(paragraphText))
                    {
                        markdown.AppendLine(paragraphText);
                    }
                    else
                    {
                        markdown.AppendLine(); // Ligne vide
                    }
                }
            }
            
            return markdown.ToString().TrimEnd();
        }
        
        /// <summary>
        /// Convertit un paragraphe en Markdown
        /// </summary>
        private static string ParagraphToMarkdown(Paragraph paragraph)
        {
            var sb = new StringBuilder();
            
            // Détecter si c'est un titre
            bool isTitle = paragraph.FontSize > 15 && paragraph.FontWeight == FontWeights.Bold;
            bool isSubtitle = paragraph.FontSize > 13 && paragraph.FontSize <= 15 && paragraph.FontWeight == FontWeights.Bold;
            
            if (isTitle)
            {
                sb.Append("# ");
            }
            else if (isSubtitle)
            {
                sb.Append("## ");
            }
            
            // Extraire le texte avec styles inline
            foreach (var inline in paragraph.Inlines)
            {
                if (inline is Run run)
                {
                    var text = run.Text;
                    
                    if (run.FontWeight == FontWeights.Bold && !isTitle && !isSubtitle)
                    {
                        sb.Append($"**{text}**");
                    }
                    else if (run.FontStyle == FontStyles.Italic)
                    {
                        sb.Append($"*{text}*");
                    }
                    else
                    {
                        sb.Append(text);
                    }
                }
            }
            
            return sb.ToString();
        }
        
        /// <summary>
        /// Retire l'en-tête YAML d'un Markdown
        /// </summary>
        private static string RemoveYamlHeader(string markdown)
        {
            if (!markdown.TrimStart().StartsWith("---"))
                return markdown;
            
            var lines = markdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            bool inYaml = false;
            int yamlEndIndex = 0;
            
            for (int i = 0; i < lines.Length; i++)
            {
                if (i == 0 && lines[i].Trim() == "---")
                {
                    inYaml = true;
                    continue;
                }
                if (inYaml && lines[i].Trim() == "---")
                {
                    yamlEndIndex = i + 1;
                    break;
                }
            }
            
            if (yamlEndIndex > 0)
            {
                return string.Join("\n", lines.Skip(yamlEndIndex));
            }
            
            return markdown;
        }
    }
}
