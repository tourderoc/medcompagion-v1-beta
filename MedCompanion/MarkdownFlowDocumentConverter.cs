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

            System.Diagnostics.Debug.WriteLine($"[MarkdownFlowDocumentConverter] Début conversion - Markdown reçu: {markdown?.Length ?? 0} caractères");

            if (string.IsNullOrWhiteSpace(markdown))
            {
                System.Diagnostics.Debug.WriteLine("[MarkdownFlowDocumentConverter] Markdown vide ou null!");
                return document;
            }

            // Debug: Afficher les 200 premiers caractères du markdown reçu
            var preview = markdown.Length > 200 ? markdown.Substring(0, 200) + "..." : markdown;
            System.Diagnostics.Debug.WriteLine($"[MarkdownFlowDocumentConverter] Contenu brut (200 premiers chars): {preview}");

            // Retirer l'en-tête YAML si présent
            var cleanMarkdown = RemoveYamlHeader(markdown);
            System.Diagnostics.Debug.WriteLine($"[MarkdownFlowDocumentConverter] Après RemoveYamlHeader: {cleanMarkdown?.Length ?? 0} caractères");

            // Debug: Vérifier si le contenu est vide après nettoyage YAML
            if (string.IsNullOrWhiteSpace(cleanMarkdown))
            {
                System.Diagnostics.Debug.WriteLine("[MarkdownFlowDocumentConverter] ATTENTION: Contenu vide après suppression YAML!");
                // Retourner le document avec le markdown original si le nettoyage a tout supprimé
                var fallbackPara = new Paragraph(new Run(markdown));
                document.Blocks.Add(fallbackPara);
                return document;
            }

            var cleanPreview = cleanMarkdown.Length > 200 ? cleanMarkdown.Substring(0, 200) + "..." : cleanMarkdown;
            System.Diagnostics.Debug.WriteLine($"[MarkdownFlowDocumentConverter] Contenu nettoyé (200 premiers chars): {cleanPreview}");

            var lines = cleanMarkdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            System.Diagnostics.Debug.WriteLine($"[MarkdownFlowDocumentConverter] Nombre de lignes à traiter: {lines.Length}");
            
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                
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
                
                // NOUVEAU : Détecter les tableaux Markdown
                if (line.TrimStart().StartsWith("|") && line.TrimEnd().EndsWith("|"))
                {
                    var (isTable, table, linesConsumed) = DetectAndParseTable(lines, i);
                    
                    if (isTable && table != null)
                    {
                        document.Blocks.Add(table);
                        i += linesConsumed - 1; // Sauter les lignes du tableau
                        continue;
                    }
                }
                
                // Ligne de séparation (---) - mais pas celle des tableaux
                if (line.Trim() == "---" || (line.Trim().All(c => c == '-') && !line.Contains("|")))
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

            System.Diagnostics.Debug.WriteLine($"[MarkdownFlowDocumentConverter] Conversion terminée - Document créé avec {document.Blocks.Count} blocs");
            return document;
        }
        
        /// <summary>
        /// Détecte et parse un tableau Markdown
        /// </summary>
        private static (bool isTable, Table? table, int linesConsumed) DetectAndParseTable(string[] lines, int startIndex)
        {
            if (startIndex >= lines.Length - 1)
                return (false, null, 0);
            
            var headerLine = lines[startIndex];
            var separatorLine = startIndex + 1 < lines.Length ? lines[startIndex + 1] : "";
            
            // Vérifier que la ligne suivante est un séparateur (|---|---|)
            if (!IsSeparatorLine(separatorLine))
                return (false, null, 0);
            
            // Parser les en-têtes
            var headers = ParseTableRow(headerLine);
            if (headers.Count == 0)
                return (false, null, 0);
            
            // Parser les lignes de données
            var rows = new System.Collections.Generic.List<System.Collections.Generic.List<string>>();
            int currentLine = startIndex + 2;
            
            while (currentLine < lines.Length)
            {
                var line = lines[currentLine];
                
                // Arrêter si la ligne n'est pas une ligne de tableau
                if (!line.TrimStart().StartsWith("|") || !line.TrimEnd().EndsWith("|"))
                    break;
                
                var rowData = ParseTableRow(line);
                if (rowData.Count > 0)
                {
                    rows.Add(rowData);
                    currentLine++;
                }
                else
                {
                    break;
                }
            }
            
            // Créer le tableau WPF
            var table = CreateWpfTable(headers, rows);
            int linesConsumed = currentLine - startIndex;
            
            return (true, table, linesConsumed);
        }
        
        /// <summary>
        /// Vérifie si une ligne est un séparateur de tableau (|---|---|)
        /// </summary>
        private static bool IsSeparatorLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;
            
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("|") || !trimmed.EndsWith("|"))
                return false;
            
            // Retirer les pipes et vérifier que tout est composé de - et espaces
            var content = trimmed.Trim('|');
            var cells = content.Split('|');
            
            foreach (var cell in cells)
            {
                var cellTrimmed = cell.Trim();
                if (string.IsNullOrEmpty(cellTrimmed) || !cellTrimmed.All(c => c == '-' || c == ':'))
                    return false;
            }
            
            return true;
        }
        
        /// <summary>
        /// Parse une ligne de tableau et retourne les cellules
        /// </summary>
        private static System.Collections.Generic.List<string> ParseTableRow(string line)
        {
            var cells = new System.Collections.Generic.List<string>();
            
            if (string.IsNullOrWhiteSpace(line))
                return cells;
            
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("|") || !trimmed.EndsWith("|"))
                return cells;
            
            // Retirer les pipes de début et fin
            var content = trimmed.Substring(1, trimmed.Length - 2);
            
            // Split par | et trim chaque cellule
            var parts = content.Split('|');
            foreach (var part in parts)
            {
                cells.Add(part.Trim());
            }
            
            return cells;
        }
        
        /// <summary>
        /// Crée un tableau WPF à partir des données parsées
        /// </summary>
        private static Table CreateWpfTable(System.Collections.Generic.List<string> headers, System.Collections.Generic.List<System.Collections.Generic.List<string>> rows)
        {
            var table = new Table
            {
                CellSpacing = 0,
                BorderBrush = new SolidColorBrush(Color.FromRgb(189, 195, 199)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 4, 0, 4)
            };
            
            // Définir les colonnes
            foreach (var header in headers)
            {
                table.Columns.Add(new TableColumn { Width = GridLength.Auto });
            }
            
            // Créer le groupe d'en-têtes
            var headerGroup = new TableRowGroup();
            var headerRow = new TableRow
            {
                Background = new SolidColorBrush(Color.FromRgb(236, 240, 241))
            };
            
            foreach (var header in headers)
            {
                var cell = new TableCell(new Paragraph(new Run(header))
                {
                    Margin = new Thickness(0),
                    FontSize = 12
                })
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(189, 195, 199)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8, 4, 8, 4),
                    FontWeight = FontWeights.Bold
                };
                headerRow.Cells.Add(cell);
            }
            
            headerGroup.Rows.Add(headerRow);
            table.RowGroups.Add(headerGroup);
            
            // Créer le groupe de données
            var dataGroup = new TableRowGroup();
            
            foreach (var row in rows)
            {
                var tableRow = new TableRow();
                
                // S'assurer qu'on a le bon nombre de cellules
                for (int i = 0; i < headers.Count; i++)
                {
                    var cellText = i < row.Count ? row[i] : "";
                    var cell = new TableCell(new Paragraph(new Run(cellText))
                    {
                        Margin = new Thickness(0),
                        FontSize = 12
                    })
                    {
                        BorderBrush = new SolidColorBrush(Color.FromRgb(189, 195, 199)),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(8, 4, 8, 4)
                    };
                    tableRow.Cells.Add(cell);
                }
                
                dataGroup.Rows.Add(tableRow);
            }
            
            table.RowGroups.Add(dataGroup);
            
            return table;
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
        /// Un header YAML valide doit :
        /// - Commencer par --- sur la première ligne
        /// - Se terminer par --- dans les 15 premières lignes (header YAML typique)
        /// - Contenir des lignes au format clé: valeur
        /// </summary>
        private static string RemoveYamlHeader(string markdown)
        {
            System.Diagnostics.Debug.WriteLine($"[RemoveYamlHeader] Entrée - Longueur: {markdown?.Length ?? 0}");

            if (!markdown.TrimStart().StartsWith("---"))
            {
                System.Diagnostics.Debug.WriteLine("[RemoveYamlHeader] Pas de YAML détecté (ne commence pas par ---)");
                return markdown;
            }

            var lines = markdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            System.Diagnostics.Debug.WriteLine($"[RemoveYamlHeader] Nombre total de lignes: {lines.Length}");

            // Limite de recherche : un header YAML ne dépasse jamais 15 lignes
            const int maxYamlHeaderLines = 15;
            int searchLimit = Math.Min(lines.Length, maxYamlHeaderLines);

            System.Diagnostics.Debug.WriteLine($"[RemoveYamlHeader] Recherche YAML limitée aux {searchLimit} premières lignes");

            bool inYaml = false;
            int yamlEndIndex = 0;
            bool hasValidYamlContent = false;

            for (int i = 0; i < searchLimit; i++)
            {
                var line = lines[i].Trim();

                if (i == 0 && line == "---")
                {
                    inYaml = true;
                    System.Diagnostics.Debug.WriteLine($"[RemoveYamlHeader] Début YAML trouvé à ligne {i}");
                    continue;
                }

                if (inYaml)
                {
                    // Vérifier si c'est la fin du YAML
                    if (line == "---")
                    {
                        yamlEndIndex = i + 1;
                        System.Diagnostics.Debug.WriteLine($"[RemoveYamlHeader] Fin YAML potentielle à ligne {i}, yamlEndIndex={yamlEndIndex}");
                        break;
                    }

                    // Vérifier si la ligne ressemble à du YAML (clé: valeur)
                    if (line.Contains(":") && !line.StartsWith("#") && !line.StartsWith("-"))
                    {
                        hasValidYamlContent = true;
                    }
                }
            }

            // Ne supprimer le YAML que si on a trouvé un header valide
            if (yamlEndIndex > 0 && hasValidYamlContent)
            {
                var result = string.Join("\n", lines.Skip(yamlEndIndex));
                System.Diagnostics.Debug.WriteLine($"[RemoveYamlHeader] YAML valide supprimé - Résultat: {result?.Length ?? 0} caractères");
                return result;
            }

            if (yamlEndIndex > 0 && !hasValidYamlContent)
            {
                System.Diagnostics.Debug.WriteLine("[RemoveYamlHeader] --- trouvé mais pas de contenu YAML valide (clé: valeur) - Retour du contenu original");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[RemoveYamlHeader] Pas de fin YAML dans les 15 premières lignes - Retour du contenu original");
            }

            return markdown;
        }
    }
}
