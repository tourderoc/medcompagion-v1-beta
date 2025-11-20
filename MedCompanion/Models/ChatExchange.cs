using System;

namespace MedCompanion.Models
{
    /// <summary>
    /// Représente un échange dans le chat IA (question + réponse)
    /// </summary>
    public class ChatExchange
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Question { get; set; } = string.Empty;
        public string Response { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string? Etiquette { get; set; }
        
        /// <summary>
        /// Label formaté pour l'affichage dans la liste (binding XAML)
        /// </summary>
        public string DisplayLabel => $"{Timestamp:dd/MM/yyyy HH:mm} - {(string.IsNullOrEmpty(Etiquette) ? "Chat" : Etiquette)}";
        
        /// <summary>
        /// Formatte l'échange pour l'affichage
        /// </summary>
        public string ToDisplayText()
        {
            return $"[{Timestamp:dd/MM/yyyy HH:mm}] {(string.IsNullOrEmpty(Etiquette) ? "Chat" : Etiquette)}";
        }
        
        /// <summary>
        /// Convertit l'échange en format Markdown pour la sauvegarde
        /// </summary>
        public string ToMarkdown()
        {
            return $@"---
date: {Timestamp:yyyy-MM-dd HH:mm:ss}
etiquette: {Etiquette ?? "Sans étiquette"}
---

**Vous :** {Question}

**IA :** {Response}";
        }
        
        /// <summary>
        /// Parse un fichier Markdown pour créer un ChatExchange
        /// </summary>
        public static ChatExchange? FromMarkdown(string markdown, string filePath)
        {
            try
            {
                var lines = markdown.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                
                // Parser le YAML header
                DateTime timestamp = DateTime.Now;
                string? etiquette = null;
                
                bool inYaml = false;
                int contentStartIndex = 0;
                
                for (int i = 0; i < lines.Length; i++)
                {
                    if (i == 0 && lines[i].Trim() == "---")
                    {
                        inYaml = true;
                        continue;
                    }
                    
                    if (inYaml && lines[i].Trim() == "---")
                    {
                        contentStartIndex = i + 1;
                        break;
                    }
                    
                    if (inYaml)
                    {
                        if (lines[i].StartsWith("date:"))
                        {
                            var dateStr = lines[i].Substring(5).Trim();
                            DateTime.TryParse(dateStr, out timestamp);
                        }
                        else if (lines[i].StartsWith("etiquette:"))
                        {
                            etiquette = lines[i].Substring(10).Trim();
                        }
                    }
                }
                
                // Extraire question et réponse
                var content = string.Join("\n", lines.Skip(contentStartIndex));
                var parts = content.Split(new[] { "**Vous :**", "**IA :**" }, StringSplitOptions.None);
                
                if (parts.Length >= 3)
                {
                    return new ChatExchange
                    {
                        Id = System.IO.Path.GetFileNameWithoutExtension(filePath),
                        Question = parts[1].Trim(),
                        Response = parts[2].Trim(),
                        Timestamp = timestamp,
                        Etiquette = etiquette
                    };
                }
                
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
