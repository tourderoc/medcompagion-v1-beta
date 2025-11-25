using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MedCompanion
{
    public class ContextLoader
    {
        private readonly StorageService _storageService;

        public ContextLoader(StorageService storageService)
        {
            _storageService = storageService;
        }

        /// <summary>
        /// Extrait la date du header YAML ou du nom de fichier
        /// </summary>
        private DateTime ExtractDate(string filePath, string fileContent)
        {
            // Essayer d'extraire la date du header YAML
            var lines = fileContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            bool inYaml = false;
            
            foreach (var line in lines)
            {
                if (line.Trim() == "---")
                {
                    if (!inYaml)
                    {
                        inYaml = true;
                        continue;
                    }
                    else
                    {
                        break; // Fin du YAML
                    }
                }
                
                if (inYaml && line.StartsWith("date:"))
                {
                    var dateStr = line.Substring(5).Trim().Trim('"');
                    if (DateTime.TryParse(dateStr, out var yamlDate))
                    {
                        return yamlDate;
                    }
                }
            }
            
            // Sinon, extraire du nom de fichier (format: YYYY-MM-DD_HHmm_...)
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var match = Regex.Match(fileName, @"^(\d{4})-(\d{2})-(\d{2})_(\d{2})(\d{2})");
            
            if (match.Success)
            {
                int year = int.Parse(match.Groups[1].Value);
                int month = int.Parse(match.Groups[2].Value);
                int day = int.Parse(match.Groups[3].Value);
                int hour = int.Parse(match.Groups[4].Value);
                int minute = int.Parse(match.Groups[5].Value);
                
                return new DateTime(year, month, day, hour, minute, 0);
            }
            
            // Dernier recours: date de modification du fichier
            return File.GetLastWriteTime(filePath);
        }

        /// <summary>
        /// Récupère la première note structurée (la plus ancienne)
        /// </summary>
        public (DateTime date, string text)? GetFirstStructuredNote(string nomComplet)
        {
            try
            {
                var patientDir = _storageService.GetPatientDirectory(nomComplet);
                
                if (!Directory.Exists(patientDir))
                    return null;
                
                var allFiles = Directory.GetFiles(Path.GetDirectoryName(patientDir) ?? patientDir, "*.md", SearchOption.AllDirectories)
                    .Select(f => new
                    {
                        Path = f,
                        Content = File.ReadAllText(f, Encoding.UTF8),
                        Date = DateTime.MinValue
                    })
                    .Select(f => new
                    {
                        f.Path,
                        f.Content,
                        Date = ExtractDate(f.Path, f.Content)
                    })
                    .Where(f => f.Content.Contains("type: \"note-structuree\"") || f.Content.Contains("type: 'note-structuree'"))
                    .OrderBy(f => f.Date)
                    .FirstOrDefault();
                
                if (allFiles == null)
                    return null;
                
                var content = ExtractContentAfterYaml(allFiles.Content);
                return (allFiles.Date, content);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Récupère les N dernières notes structurées
        /// </summary>
        public List<(DateTime date, string text, string filePath)> GetLastStructuredNotes(string nomComplet, int count = 2)
        {
            try
            {
                var patientDir = _storageService.GetPatientDirectory(nomComplet);
                
                if (!Directory.Exists(patientDir))
                    return new List<(DateTime, string, string)>();
                
                var allFiles = Directory.GetFiles(Path.GetDirectoryName(patientDir) ?? patientDir, "*.md", SearchOption.AllDirectories)
                    .Select(f => new
                    {
                        Path = f,
                        Content = File.ReadAllText(f, Encoding.UTF8),
                        Date = DateTime.MinValue
                    })
                    .Select(f => new
                    {
                        f.Path,
                        f.Content,
                        Date = ExtractDate(f.Path, f.Content)
                    })
                    .Where(f => f.Content.Contains("type: \"note-structuree\"") || f.Content.Contains("type: 'note-structuree'"))
                    .OrderByDescending(f => f.Date)
                    .Take(count)
                    .ToList();
                
                return allFiles.Select(f => (f.Date, ExtractContentAfterYaml(f.Content), f.Path)).ToList();
            }
            catch
            {
                return new List<(DateTime, string, string)>();
            }
        }

        /// <summary>
        /// Tronque un texte Markdown sans casser les paragraphes
        /// </summary>
        public string TruncateMarkdown(string text, int maxWords)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
            
            // Séparer par paragraphes (lignes vides)
            var paragraphs = Regex.Split(text, @"\n\s*\n").Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
            
            var result = new StringBuilder();
            int wordCount = 0;
            
            foreach (var paragraph in paragraphs)
            {
                var words = paragraph.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                
                if (wordCount + words.Length <= maxWords)
                {
                    result.AppendLine(paragraph.Trim());
                    result.AppendLine();
                    wordCount += words.Length;
                }
                else
                {
                    // Ajouter les mots restants du dernier paragraphe
                    int remainingWords = maxWords - wordCount;
                    if (remainingWords > 0)
                    {
                        result.AppendLine(string.Join(" ", words.Take(remainingWords)) + "...");
                    }
                    break;
                }
            }
            
            return result.ToString().Trim();
        }

        /// <summary>
        /// Construit un contexte complet pour l'IA
        /// PRIORITÉ 1: SYNTHÈSE PATIENT (si disponible)
        /// FALLBACK: NOTE FONDATRICE + DERNIÈRES NOTES
        /// </summary>
        public (bool hasContext, string contextText, string contextInfo) GetContextBundle(string nomComplet, string? currentNote = null)
        {
            try
            {
                // NOUVEAU : Vérifier d'abord si une synthèse existe
                var patientDir = _storageService.GetPatientDirectory(nomComplet);
                var synthesisPath = Path.Combine(Path.GetDirectoryName(patientDir) ?? patientDir, "synthese", "synthese.md");

                if (File.Exists(synthesisPath))
                {
                    // ✅ SYNTHÈSE DISPONIBLE → Utiliser comme contexte prioritaire (SANS LIMITE)
                    try
                    {
                        var synthesisContent = File.ReadAllText(synthesisPath, Encoding.UTF8);
                        var cleanContent = ExtractContentAfterYaml(synthesisContent);
                        // ✅ MODIFICATION : Pas de troncature, envoyer la synthèse COMPLÈTE

                        var synthesisContext = new StringBuilder();
                        synthesisContext.AppendLine("📋 SYNTHÈSE PATIENT COMPLÈTE");
                        synthesisContext.AppendLine(cleanContent);

                        return (true, synthesisContext.ToString(), "synthèse complète");
                    }
                    catch
                    {
                        // Si erreur lecture synthèse, continuer vers fallback
                    }
                }

                // ⚠️ FALLBACK : Pas de synthèse ou erreur → Ancien système (note fondatrice + dernières)
                var first = GetFirstStructuredNote(nomComplet);
                var last = GetLastStructuredNotes(nomComplet, 2);

                if (first == null && last.Count == 0)
                {
                    return (false, string.Empty, "Aucune note disponible");
                }

                var context = new StringBuilder();
                int notesCount = 0;

                // NOTE FONDATRICE (COMPLÈTE)
                if (first.HasValue)
                {
                    // ✅ MODIFICATION : Pas de troncature, envoyer la note COMPLÈTE
                    context.AppendLine("NOTE FONDATRICE COMPLÈTE");
                    context.AppendLine($"{first.Value.date:yyyy-MM-dd} — {first.Value.text}");
                    context.AppendLine();
                    notesCount++;
                }

                // DERNIÈRES NOTES COMPLÈTES (avec déduplication)
                var lastNotes = last.Where(l =>
                    !first.HasValue ||
                    Math.Abs((l.date - first.Value.date).TotalMinutes) > 1 // Différence > 1 minute
                ).ToList();

                if (lastNotes.Count > 0)
                {
                    context.AppendLine("DERNIÈRES NOTES COMPLÈTES");
                    foreach (var note in lastNotes)
                    {
                        // ✅ MODIFICATION : Pas de troncature, envoyer les notes COMPLÈTES
                        context.AppendLine($"- {note.date:yyyy-MM-dd}: {note.text}");
                        context.AppendLine();
                        notesCount++;
                    }
                }

                string info = notesCount switch
                {
                    0 => "Aucune note",
                    1 => "note fondatrice",
                    2 => "note fondatrice + 1 dernière",
                    _ => "note fondatrice + 2 dernières"
                };

                return (true, context.ToString(), info);
            }
            catch
            {
                return (false, string.Empty, "Erreur chargement contexte");
            }
        }

        /// <summary>
        /// Charge les N dernières notes d'un patient
        /// </summary>
        public (bool success, string content, int notesFound) GetRecentNotes(string nomComplet, int count = 3)
        {
            try
            {
                var patientDir = _storageService.GetPatientDirectory(nomComplet);
                
                if (!Directory.Exists(patientDir))
                {
                    return (false, $"Aucune note trouvée pour {nomComplet}.", 0);
                }

                // Récupérer tous les fichiers .md du dossier patient
                var allFiles = Directory.GetFiles(patientDir, "*.md", SearchOption.AllDirectories)
                    .OrderByDescending(f => File.GetLastWriteTime(f))
                    .Take(count)
                    .ToList();

                if (!allFiles.Any())
                {
                    return (false, $"Aucune note trouvée pour {nomComplet}.", 0);
                }

                var result = new StringBuilder();
                result.AppendLine($"═══════════════════════════════════════");
                result.AppendLine($"CONTEXTE: {allFiles.Count} note(s) récente(s) pour {nomComplet}");
                result.AppendLine($"═══════════════════════════════════════");
                result.AppendLine();

                int noteNumber = 1;
                foreach (var filePath in allFiles)
                {
                    try
                    {
                        var fileContent = File.ReadAllText(filePath, Encoding.UTF8);
                        var fileName = Path.GetFileName(filePath);
                        var lastModified = File.GetLastWriteTime(filePath);

                        result.AppendLine($"─── Note {noteNumber}/{allFiles.Count} ───");
                        result.AppendLine($"Fichier: {fileName}");
                        result.AppendLine($"Date: {lastModified:yyyy-MM-dd HH:mm}");
                        result.AppendLine();

                        // Extraire le contenu après l'en-tête YAML
                        var content = ExtractContentAfterYaml(fileContent);
                        
                        // Limiter l'extrait si trop long
                        if (content.Length > 500)
                        {
                            content = content.Substring(0, 500) + "...";
                        }

                        result.AppendLine(content);
                        result.AppendLine();
                        result.AppendLine();

                        noteNumber++;
                    }
                    catch (Exception ex)
                    {
                        result.AppendLine($"⚠️ Erreur lecture {Path.GetFileName(filePath)}: {ex.Message}");
                        result.AppendLine();
                    }
                }

                result.AppendLine($"═══════════════════════════════════════");

                return (true, result.ToString(), allFiles.Count);
            }
            catch (Exception ex)
            {
                return (false, $"Erreur lors du chargement des notes: {ex.Message}", 0);
            }
        }

        /// <summary>
        /// Extrait le contenu après l'en-tête YAML
        /// </summary>
        private string ExtractContentAfterYaml(string fileContent)
        {
            // Chercher la fin de l'en-tête YAML (deuxième ligne "---")
            var lines = fileContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            
            int yamlEndIndex = -1;
            bool inYaml = false;

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() == "---")
                {
                    if (!inYaml)
                    {
                        inYaml = true; // Début du YAML
                    }
                    else
                    {
                        yamlEndIndex = i; // Fin du YAML
                        break;
                    }
                }
            }

            if (yamlEndIndex > 0 && yamlEndIndex < lines.Length - 1)
            {
                // Retourner tout après l'en-tête YAML
                return string.Join(Environment.NewLine, lines.Skip(yamlEndIndex + 1)).Trim();
            }

            // Si pas d'en-tête YAML trouvé, retourner le contenu tel quel
            return fileContent.Trim();
        }
    }
}
