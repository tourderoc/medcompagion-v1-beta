using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using MedCompanion.Models;
using MedCompanion.Services;

namespace MedCompanion
{
    public class StorageService
    {
        private readonly PathService _pathService;

        public StorageService(PathService pathService)
        {
            _pathService = pathService;
        }

        /// <summary>
        /// Obtient le chemin du dossier pour un patient (pour compatibilité)
        /// DEPRECATED: Utiliser PathService directement
        /// </summary>
        public string GetPatientDirectory(string nomComplet)
        {
            return _pathService.GetPatientYearDirectory(nomComplet);
        }

        /// <summary>
        /// Sauvegarde une note structurée
        /// </summary>
        public (bool success, string message, string? filePath) SaveStructuredNote(
            string nomComplet, 
            string noteStructuree)
        {
            try
            {
                var notesDir = _pathService.GetNotesDirectory(nomComplet);
                _pathService.EnsureDirectoryExists(notesDir);

                var now = DateTime.Now;
                var baseFileName = $"{now:yyyy-MM-dd_HHmm}_note.md";
                var filePath = Path.Combine(notesDir, baseFileName);

                // Gérer les doublons avec suffixes -v2, -v3, etc.
                int version = 2;
                while (File.Exists(filePath))
                {
                    var fileNameWithoutExt = Path.GetFileNameWithoutExtension(baseFileName);
                    filePath = Path.Combine(notesDir, $"{fileNameWithoutExt}-v{version}.md");
                    version++;
                }

                // Créer le contenu avec en-tête YAML
                var content = new StringBuilder();
                content.AppendLine("---");
                content.AppendLine($"patient: \"{nomComplet}\"");
                content.AppendLine($"date: \"{now:yyyy-MM-ddTHH:mm}\"");
                content.AppendLine("source: \"MedCompanion\"");
                content.AppendLine("type: \"note-structuree\"");
                content.AppendLine("version: \"1\"");
                content.AppendLine("---");
                content.AppendLine();
                content.AppendLine(noteStructuree);

                File.WriteAllText(filePath, content.ToString(), Encoding.UTF8);

                return (true, $"Enregistré → {filePath}", filePath);
            }
            catch (UnauthorizedAccessException)
            {
                return (false, "Erreur: Accès refusé. Vérifiez les permissions du dossier.", null);
            }
            catch (IOException ex)
            {
                return (false, $"Erreur d'écriture: {ex.Message}", null);
            }
            catch (Exception ex)
            {
                return (false, $"Erreur inattendue: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Met à jour une note structurée existante (écrase le fichier)
        /// </summary>
        public (bool success, string message) UpdateStructuredNote(string filePath, string noteStructuree)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return (false, "Le fichier n'existe plus");
                }

                // Lire le fichier existant pour conserver l'en-tête YAML
                var existingContent = File.ReadAllText(filePath, Encoding.UTF8);
                var lines = existingContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                
                // Trouver la fin du YAML header (deuxième ---)
                int yamlEndIndex = -1;
                int dashCount = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (lines[i].Trim() == "---")
                    {
                        dashCount++;
                        if (dashCount == 2)
                        {
                            yamlEndIndex = i;
                            break;
                        }
                    }
                }

                // Construire le nouveau contenu en gardant le YAML header
                var content = new StringBuilder();
                if (yamlEndIndex >= 0)
                {
                    // Garder le YAML header existant
                    for (int i = 0; i <= yamlEndIndex; i++)
                    {
                        content.AppendLine(lines[i]);
                    }
                }
                else
                {
                    // Pas de YAML header trouvé, en créer un minimal
                    content.AppendLine("---");
                    content.AppendLine($"date: \"{DateTime.Now:yyyy-MM-ddTHH:mm}\"");
                    content.AppendLine("type: \"note-structuree\"");
                    content.AppendLine("---");
                }
                
                content.AppendLine();
                content.AppendLine(noteStructuree);

                File.WriteAllText(filePath, content.ToString(), Encoding.UTF8);

                return (true, $"✓ Note mise à jour → {Path.GetFileName(filePath)}");
            }
            catch (UnauthorizedAccessException)
            {
                return (false, "Erreur: Accès refusé. Vérifiez les permissions du fichier.");
            }
            catch (IOException ex)
            {
                return (false, $"Erreur d'écriture: {ex.Message}");
            }
            catch (Exception ex)
            {
                return (false, $"Erreur inattendue: {ex.Message}");
            }
        }

        /// <summary>
        /// Supprime une note structurée
        /// </summary>
        public (bool success, string message) DeleteStructuredNote(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return (false, "Le fichier n'existe plus");
                }

                File.Delete(filePath);
                return (true, $"✓ Note supprimée → {Path.GetFileName(filePath)}");
            }
            catch (UnauthorizedAccessException)
            {
                return (false, "Erreur: Accès refusé. Vérifiez les permissions du fichier.");
            }
            catch (IOException ex)
            {
                return (false, $"Erreur de suppression: {ex.Message}");
            }
            catch (Exception ex)
            {
                return (false, $"Erreur inattendue: {ex.Message}");
            }
        }

        /// <summary>
        /// Ouvre le dossier du patient dans l'Explorateur Windows
        /// </summary>
        public (bool success, string message) OpenPatientDirectory(string nomComplet)
        {
            try
            {
                var patientDir = GetPatientDirectory(nomComplet);
                
                if (!Directory.Exists(patientDir))
                {
                    Directory.CreateDirectory(patientDir);
                }

                System.Diagnostics.Process.Start("explorer.exe", patientDir);
                return (true, "Dossier ouvert");
            }
            catch (Exception ex)
            {
                return (false, $"Erreur lors de l'ouverture du dossier: {ex.Message}");
            }
        }

        // ========== GESTION DES ÉCHANGES CHAT ==========


        /// <summary>
        /// Sauvegarde un échange de chat avec étiquette thématique
        /// </summary>
        public (bool success, string message, string? filePath) SaveChatExchange(
            string nomComplet, 
            ChatExchange exchange)
        {
            try
            {
                var chatDir = _pathService.GetChatDirectory(nomComplet);
                _pathService.EnsureDirectoryExists(chatDir);

                // Nettoyer l'étiquette pour le nom de fichier
                var etiquetteSafe = exchange.Etiquette?
                    .Replace(" ", "_")
                    .Replace("/", "-")
                    .Replace("\\", "-")
                    .Replace(":", "-") ?? "chat";

                var fileNameWithoutExt = $"chat_{exchange.Timestamp:yyyy-MM-dd_HHmmss}_{etiquetteSafe}";
                var fileName = $"{fileNameWithoutExt}.md";
                var filePath = Path.Combine(chatDir, fileName);

                // Gérer les doublons
                int version = 2;
                string finalFileNameWithoutExt = fileNameWithoutExt;
                while (File.Exists(filePath))
                {
                    finalFileNameWithoutExt = $"{fileNameWithoutExt}_v{version}";
                    filePath = Path.Combine(chatDir, $"{finalFileNameWithoutExt}.md");
                    version++;
                }

                // IMPORTANT: Définir l'ID basé sur le nom du fichier pour cohérence
                // Cela permet de retrouver facilement le fichier lors de la suppression
                exchange.Id = finalFileNameWithoutExt;

                // Sauvegarder au format Markdown
                File.WriteAllText(filePath, exchange.ToMarkdown(), Encoding.UTF8);

                return (true, $"💾 Échange sauvegardé → {Path.GetFileName(filePath)}", filePath);
            }
            catch (Exception ex)
            {
                return (false, $"❌ Erreur sauvegarde: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Récupère tous les échanges de chat sauvegardés pour un patient
        /// </summary>
        public List<ChatExchange> GetChatExchanges(string nomComplet)
        {
            var exchanges = new List<ChatExchange>();
            
            try
            {
                var chatDir = _pathService.GetChatDirectory(nomComplet);
                
                if (!Directory.Exists(chatDir))
                    return exchanges;

                var files = Directory.GetFiles(chatDir, "chat_*.md")
                    .OrderByDescending(f => File.GetLastWriteTime(f));

                foreach (var file in files)
                {
                    try
                    {
                        var markdown = File.ReadAllText(file, Encoding.UTF8);
                        var exchange = ChatExchange.FromMarkdown(markdown, file);
                        
                        if (exchange != null)
                        {
                            exchanges.Add(exchange);
                        }
                    }
                    catch
                    {
                        // Ignorer les fichiers mal formatés
                        continue;
                    }
                }
            }
            catch
            {
                // Retourner liste vide en cas d'erreur
            }

            return exchanges;
        }

        /// <summary>
        /// Supprime un échange de chat sauvegardé
        /// </summary>
        public (bool success, string message) DeleteChatExchange(string nomComplet, string exchangeId)
        {
            try
            {
                var chatDir = _pathService.GetChatDirectory(nomComplet);
                
                if (!Directory.Exists(chatDir))
                    return (false, "Dossier chat introuvable");

                // Chercher le fichier dont le nom (sans extension) correspond exactement à l'ID
                var file = Directory.GetFiles(chatDir, "*.md")
                    .FirstOrDefault(f => Path.GetFileNameWithoutExtension(f) == exchangeId);

                if (file == null || !File.Exists(file))
                    return (false, "Échange introuvable");

                File.Delete(file);
                return (true, $"🗑️ Échange supprimé");
            }
            catch (Exception ex)
            {
                return (false, $"❌ Erreur suppression: {ex.Message}");
            }
        }
    }
}
