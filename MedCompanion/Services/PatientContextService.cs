using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service centralisé pour collecter TOUT le contexte patient
    /// - TOUTES les métadonnées patient
    /// - Synthèse COMPLÈTE (ou notes complètes si pas de synthèse)
    /// - Demande utilisateur optionnelle
    /// AUCUNE LIMITE de mots
    /// </summary>
    public class PatientContextService
    {
        private readonly StorageService _storageService;
        private readonly PatientIndexService _patientIndex;

        public PatientContextService(
            StorageService storageService,
            PatientIndexService patientIndex)
        {
            _storageService = storageService;
            _patientIndex = patientIndex;
        }

        /// <summary>
        /// Collecte TOUT le contexte patient de manière centralisée
        /// </summary>
        /// <param name="nomComplet">Nom complet du patient</param>
        /// <param name="userRequest">Demande utilisateur optionnelle</param>
        /// <returns>Bundle contenant tout le contexte</returns>
        public PatientContextBundle GetCompleteContext(
            string nomComplet,
            string? userRequest = null)
        {
            var bundle = new PatientContextBundle
            {
                UserRequest = userRequest,
                GeneratedAt = DateTime.Now
            };

            // 1. Charger les métadonnées patient
            bundle.Metadata = LoadPatientMetadata(nomComplet);

            // 2. Charger le contexte clinique (synthèse OU notes)
            var (clinicalContent, contextType) = LoadSynthesisOrNotes(nomComplet);
            bundle.ClinicalContext = clinicalContent;
            bundle.ContextType = contextType;

            System.Diagnostics.Debug.WriteLine($"[PatientContextService] {bundle.ToDebugText()}");

            return bundle;
        }

        // === Méthodes Privées ===

        /// <summary>
        /// Charge TOUTES les métadonnées patient depuis patient.json
        /// </summary>
        private PatientMetadata? LoadPatientMetadata(string nomComplet)
        {
            try
            {
                var patientDir = _storageService.GetPatientDirectory(nomComplet);
                var patientJsonPath = Path.Combine(patientDir, "patient.json");

                if (File.Exists(patientJsonPath))
                {
                    var json = File.ReadAllText(patientJsonPath);
                    return System.Text.Json.JsonSerializer.Deserialize<PatientMetadata>(json);
                }

                // Fallback : créer un objet minimal
                var parts = nomComplet.Split(' ');
                return new PatientMetadata 
                { 
                    Prenom = parts.FirstOrDefault() ?? "", 
                    Nom = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : ""
                };
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PatientContextService] Erreur chargement métadonnées: {ex.Message}");
            }

            return new PatientMetadata { Prenom = nomComplet.Split(' ').FirstOrDefault() ?? "", Nom = nomComplet.Split(' ').LastOrDefault() ?? "" };
        }

        /// <summary>
        /// Charge le contexte clinique : Synthèse COMPLÈTE ou Notes COMPLÈTES
        /// PRIORITÉ 1: Synthèse patient
        /// FALLBACK: Note fondatrice + 2 dernières notes
        /// </summary>
        private (string content, string type) LoadSynthesisOrNotes(string nomComplet)
        {
            // PRIORITÉ 1 : Synthèse patient COMPLÈTE
            var patientDir = _storageService.GetPatientDirectory(nomComplet);
            var synthesisPath = Path.Combine(
                Path.GetDirectoryName(patientDir) ?? patientDir,
                "synthese",
                "synthese.md"
            );

            if (File.Exists(synthesisPath))
            {
                try
                {
                    var synthesisContent = File.ReadAllText(synthesisPath, Encoding.UTF8);
                    var cleanContent = ExtractContentAfterYaml(synthesisContent);
                    
                    System.Diagnostics.Debug.WriteLine($"[PatientContextService] Synthèse chargée: {cleanContent.Length} caractères");
                    
                    return (cleanContent, "synthèse");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PatientContextService] Erreur lecture synthèse: {ex.Message}");
                }
            }

            // FALLBACK : Note fondatrice + 2 dernières notes COMPLÈTES
            var notes = LoadFounderAndRecentNotes(nomComplet);
            return (notes, "notes");
        }

        /// <summary>
        /// Charge la note fondatrice + 2 dernières notes COMPLÈTES (SANS TRONCATURE)
        /// </summary>
        private string LoadFounderAndRecentNotes(string nomComplet)
        {
            var context = new StringBuilder();

            try
            {
                // Note fondatrice
                var first = GetFirstStructuredNote(nomComplet);
                if (first.HasValue)
                {
                    context.AppendLine("📝 NOTE FONDATRICE COMPLÈTE");
                    context.AppendLine($"Date: {first.Value.date:yyyy-MM-dd}");
                    context.AppendLine();
                    context.AppendLine(first.Value.text);
                    context.AppendLine();
                    context.AppendLine("─────────────────────────────────────");
                    context.AppendLine();
                }

                // 2 dernières notes
                var last = GetLastStructuredNotes(nomComplet, 2);
                
                // Déduplication (éviter de recharger la note fondatrice)
                var lastNotes = last.Where(l =>
                    !first.HasValue ||
                    Math.Abs((l.date - first.Value.date).TotalMinutes) > 1
                ).ToList();

                if (lastNotes.Count > 0)
                {
                    context.AppendLine("📝 DERNIÈRES NOTES COMPLÈTES");
                    context.AppendLine();
                    
                    foreach (var note in lastNotes)
                    {
                        context.AppendLine($"Date: {note.date:yyyy-MM-dd}");
                        context.AppendLine();
                        context.AppendLine(note.text);
                        context.AppendLine();
                        context.AppendLine("─────────────────────────────────────");
                        context.AppendLine();
                    }
                }

                if (context.Length == 0)
                {
                    context.AppendLine("⚠️ Aucune note disponible pour ce patient.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PatientContextService] Erreur chargement notes: {ex.Message}");
                context.AppendLine($"⚠️ Erreur chargement notes: {ex.Message}");
            }

            return context.ToString();
        }

        // === Méthodes utilitaires (réutilisées de ContextLoader) ===

        private (DateTime date, string text)? GetFirstStructuredNote(string nomComplet)
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

        private List<(DateTime date, string text)> GetLastStructuredNotes(string nomComplet, int count = 2)
        {
            try
            {
                var patientDir = _storageService.GetPatientDirectory(nomComplet);

                if (!Directory.Exists(patientDir))
                    return new List<(DateTime, string)>();

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

                return allFiles.Select(f => (f.Date, ExtractContentAfterYaml(f.Content))).ToList();
            }
            catch
            {
                return new List<(DateTime, string)>();
            }
        }

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
                        break;
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

            // Sinon, extraire du nom de fichier
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

            // Dernier recours
            return File.GetLastWriteTime(filePath);
        }

        private string ExtractContentAfterYaml(string fileContent)
        {
            var lines = fileContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            int yamlEndIndex = -1;
            bool inYaml = false;

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() == "---")
                {
                    if (!inYaml)
                    {
                        inYaml = true;
                    }
                    else
                    {
                        yamlEndIndex = i;
                        break;
                    }
                }
            }

            if (yamlEndIndex > 0 && yamlEndIndex < lines.Length - 1)
            {
                return string.Join(Environment.NewLine, lines.Skip(yamlEndIndex + 1)).Trim();
            }

            return fileContent.Trim();
        }
    }
}
