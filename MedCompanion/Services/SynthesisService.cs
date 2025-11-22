using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MedCompanion.Models;

namespace MedCompanion.Services;

/// <summary>
/// Service de génération et mise à jour de synthèses patient intelligentes
/// </summary>
public class SynthesisService
{
    private readonly OpenAIService _openAIService;
    private readonly StorageService _storageService;
    private readonly ContextLoader _contextLoader;
    private readonly PathService _pathService;
    private readonly PromptConfigService _promptConfigService;
    private readonly SynthesisWeightTracker _synthesisWeightTracker;

    public SynthesisService(
        OpenAIService openAIService,
        StorageService storageService,
        ContextLoader contextLoader,
        PathService pathService,
        PromptConfigService promptConfigService,
        SynthesisWeightTracker synthesisWeightTracker)
    {
        _openAIService = openAIService;
        _storageService = storageService;
        _contextLoader = contextLoader;
        _pathService = pathService;
        _promptConfigService = promptConfigService;
        _synthesisWeightTracker = synthesisWeightTracker;
    }

    /// <summary>
    /// Génère une synthèse complète à partir de tout le dossier patient
    /// </summary>
    public async Task<(bool success, string markdown, string? error)> GenerateCompleteSynthesisAsync(
        string patientName,
        string patientDirectory)
    {
        try
        {
            // Collecter tous les contenus du dossier
            var allContent = CollectAllPatientContent(patientDirectory);

            if (string.IsNullOrEmpty(allContent))
            {
                return (false, "", "Aucun contenu trouvé dans le dossier patient");
            }

            // Générer la synthèse avec l'IA
            var prompt = BuildCompleteSynthesisPrompt(patientName, allContent);
            var (success, synthesis, error) = await _openAIService.GenerateTextAsync(prompt);

            if (!success || string.IsNullOrEmpty(synthesis))
            {
                return (false, "", error ?? "Erreur génération synthèse");
            }

            // Ajouter les métadonnées YAML
            var finalMarkdown = BuildSynthesisWithMetadata(synthesis, "complete");

            return (true, finalMarkdown, null);
        }
        catch (Exception ex)
        {
            return (false, "", $"Erreur: {ex.Message}");
        }
    }

    /// <summary>
    /// Met à jour la synthèse de manière incrémentale
    /// </summary>
    public async Task<(bool success, string markdown, string? error)> UpdateSynthesisIncrementallyAsync(
        string patientName,
        string patientDirectory,
        string existingSynthesis,
        List<string> newItems)
    {
        try
        {
            if (newItems.Count == 0)
            {
                return (true, existingSynthesis, null); // Rien à ajouter
            }

            // Collecter uniquement le contenu des nouveaux éléments
            var newContent = CollectNewContent(patientDirectory, newItems);

            if (string.IsNullOrEmpty(newContent))
            {
                return (true, existingSynthesis, null);
            }

            // Nettoyer les métadonnées YAML de l'ancienne synthèse
            var cleanedSynthesis = RemoveYamlFrontMatter(existingSynthesis);

            // Générer la mise à jour avec l'IA
            var prompt = BuildIncrementalUpdatePrompt(patientName, cleanedSynthesis, newContent);
            var (success, updatedSynthesis, error) = await _openAIService.GenerateTextAsync(prompt);

            if (!success || string.IsNullOrEmpty(updatedSynthesis))
            {
                return (false, "", error ?? "Erreur mise à jour synthèse");
            }

            // Ajouter les métadonnées YAML
            var finalMarkdown = BuildSynthesisWithMetadata(updatedSynthesis, "incremental");

            return (true, finalMarkdown, null);
        }
        catch (Exception ex)
        {
            return (false, "", $"Erreur: {ex.Message}");
        }
    }

    /// <summary>
    /// Vérifie si la synthèse nécessite une mise à jour
    /// </summary>
    public (bool needsUpdate, List<string> newItems, DateTime? lastSynthesisDate) CheckForUpdates(
        string patientDirectory)
    {
        var patientName = Path.GetFileName(patientDirectory);
        var syntheseDir = _pathService.GetSyntheseDirectory(patientName);
        var synthesisPath = Path.Combine(syntheseDir, "synthese.md");

        if (!File.Exists(synthesisPath))
        {
            return (true, new List<string>(), null); // Pas de synthèse = génération complète
        }

        // Lire la date de dernière synthèse depuis le YAML
        var lastSynthesisDate = GetLastSynthesisDate(synthesisPath);
        if (!lastSynthesisDate.HasValue)
        {
            return (true, new List<string>(), null);
        }

        // Vérifier tous les types de fichiers
        var newItems = new List<string>();

        // Notes
        foreach (var yearDir in Directory.GetDirectories(patientDirectory)
            .Where(d => int.TryParse(Path.GetFileName(d), out _)))
        {
            var notes = Directory.GetFiles(yearDir, "*.md", SearchOption.AllDirectories)
                .Where(f => File.GetLastWriteTime(f) > lastSynthesisDate.Value);
            newItems.AddRange(notes.Select(f => $"Note: {Path.GetFileName(f)}"));
        }

        // Courriers
        var courriersDir = _pathService.GetCourriersDirectory(patientName);
        if (Directory.Exists(courriersDir))
        {
            var courriers = Directory.GetFiles(courriersDir, "*.md")
                .Where(f => File.GetLastWriteTime(f) > lastSynthesisDate.Value);
            newItems.AddRange(courriers.Select(f => $"Courrier: {Path.GetFileName(f)}"));
        }

        // Attestations
        var attestationsDir = _pathService.GetAttestationsDirectory(patientName);
        if (Directory.Exists(attestationsDir))
        {
            var attestations = Directory.GetFiles(attestationsDir, "*.md")
                .Where(f => File.GetLastWriteTime(f) > lastSynthesisDate.Value);
            newItems.AddRange(attestations.Select(f => $"Attestation: {Path.GetFileName(f)}"));
        }

        // Formulaires
        var formulairesDir = _pathService.GetFormulairesDirectory(patientName);
        if (Directory.Exists(formulairesDir))
        {
            var formulaires = Directory.GetFiles(formulairesDir, "*.*")
                .Where(f => (f.EndsWith(".md") || f.EndsWith(".docx") || f.EndsWith(".json")) && 
                           File.GetLastWriteTime(f) > lastSynthesisDate.Value);
            newItems.AddRange(formulaires.Select(f => $"Formulaire: {Path.GetFileName(f)}"));
        }

        // Ordonnances
        var ordonnancesDir = _pathService.GetOrdonnancesDirectory(patientName);
        if (Directory.Exists(ordonnancesDir))
        {
            var ordonnances = Directory.GetFiles(ordonnancesDir, "*.md")
                .Where(f => File.GetLastWriteTime(f) > lastSynthesisDate.Value);
            newItems.AddRange(ordonnances.Select(f => $"Ordonnance: {Path.GetFileName(f)}"));
        }

        // Synthèses de documents
        var documentsDir = _pathService.GetDocumentsDirectory(patientName);
        var docSynthesesDir = Path.Combine(documentsDir, "syntheses");
        if (Directory.Exists(docSynthesesDir))
        {
            var docSyntheses = Directory.GetFiles(docSynthesesDir, "*.md")
                .Where(f => File.GetLastWriteTime(f) > lastSynthesisDate.Value);
            newItems.AddRange(docSyntheses.Select(f => $"Synthèse doc: {Path.GetFileName(f)}"));
        }

        // Chats sauvegardés
        var chatDir = _pathService.GetChatDirectory(patientName);
        if (Directory.Exists(chatDir))
        {
            var chats = Directory.GetFiles(chatDir, "*.json")
                .Where(f => File.GetLastWriteTime(f) > lastSynthesisDate.Value);
            newItems.AddRange(chats.Select(f => $"Discussion: {Path.GetFileName(f)}"));
        }

        return (newItems.Count > 0, newItems, lastSynthesisDate);
    }

    /// <summary>
    /// Sauvegarde la synthèse dans le dossier patient
    /// </summary>
    public (bool success, string message) SaveSynthesis(
        string patientDirectory,
        string markdown)
    {
        try
        {
            var patientName = Path.GetFileName(patientDirectory);
            var syntheseDir = _pathService.GetSyntheseDirectory(patientName);

            // S'assurer que le dossier existe
            _pathService.EnsureDirectoryExists(syntheseDir);

            var synthesisPath = Path.Combine(syntheseDir, "synthese.md");

            // Backup de l'ancienne synthèse si elle existe
            if (File.Exists(synthesisPath))
            {
                var backupPath = Path.Combine(syntheseDir, $"synthese_backup_{DateTime.Now:yyyyMMdd_HHmmss}.md");
                File.Copy(synthesisPath, backupPath, true);
            }

            File.WriteAllText(synthesisPath, markdown, Encoding.UTF8);

            // NOUVEAU : Réinitialiser le poids accumulé après mise à jour de la synthèse
            _synthesisWeightTracker.ResetAfterSynthesisUpdate(patientName);

            return (true, "Synthèse sauvegardée avec succès");
        }
        catch (Exception ex)
        {
            return (false, $"Erreur sauvegarde: {ex.Message}");
        }
    }

    // === MÉTHODES PRIVÉES ===

    private string CollectAllPatientContent(string patientDirectory)
    {
        var content = new StringBuilder();
        var patientName = Path.GetFileName(patientDirectory);

        // Notes cliniques
        content.AppendLine("# NOTES CLINIQUES\n");
        foreach (var yearDir in Directory.GetDirectories(patientDirectory)
            .Where(d => int.TryParse(Path.GetFileName(d), out _))
            .OrderByDescending(d => d))
        {
            var notes = Directory.GetFiles(yearDir, "*.md", SearchOption.AllDirectories)
                .OrderByDescending(f => File.GetLastWriteTime(f));

            foreach (var note in notes)
            {
                content.AppendLine($"## {Path.GetFileName(note)}");
                content.AppendLine(RemoveYamlFrontMatter(File.ReadAllText(note)));
                content.AppendLine();
            }
        }

        // Courriers
        var courriersDir = _pathService.GetCourriersDirectory(patientName);
        if (Directory.Exists(courriersDir))
        {
            content.AppendLine("# COURRIERS\n");
            var courriers = Directory.GetFiles(courriersDir, "*.md")
                .OrderByDescending(f => File.GetLastWriteTime(f));

            foreach (var courrier in courriers)
            {
                content.AppendLine($"## {Path.GetFileName(courrier)}");
                content.AppendLine(RemoveYamlFrontMatter(File.ReadAllText(courrier)));
                content.AppendLine();
            }
        }

        // Attestations
        var attestationsDir = _pathService.GetAttestationsDirectory(patientName);
        if (Directory.Exists(attestationsDir))
        {
            content.AppendLine("# ATTESTATIONS\n");
            var attestations = Directory.GetFiles(attestationsDir, "*.md")
                .OrderByDescending(f => File.GetLastWriteTime(f));

            foreach (var attestation in attestations)
            {
                content.AppendLine($"## {Path.GetFileName(attestation)}");
                content.AppendLine(RemoveYamlFrontMatter(File.ReadAllText(attestation)));
                content.AppendLine();
            }
        }

        // Formulaires (MDPH, PAI, etc.)
        var formulairesDir = _pathService.GetFormulairesDirectory(patientName);
        if (Directory.Exists(formulairesDir))
        {
            content.AppendLine("# FORMULAIRES\n");
            var formulaires = Directory.GetFiles(formulairesDir, "*.*")
                .Where(f => f.EndsWith(".md") || f.EndsWith(".json"))
                .OrderByDescending(f => File.GetLastWriteTime(f));

            foreach (var formulaire in formulaires)
            {
                if (formulaire.EndsWith(".json"))
                {
                    try
                    {
                        var jsonContent = File.ReadAllText(formulaire);
                        var fileName = Path.GetFileName(formulaire);

                        if (fileName.StartsWith("MDPH_", StringComparison.OrdinalIgnoreCase))
                        {
                            var synthesis = System.Text.Json.JsonSerializer.Deserialize<MDPHSynthesis>(jsonContent);
                            if (synthesis != null)
                            {
                                content.AppendLine($"## Dossier MDPH du {synthesis.DateCreation:dd/MM/yyyy}");
                                content.AppendLine($"Demandes : {string.Join(", ", synthesis.Demandes)}");
                                if (!string.IsNullOrWhiteSpace(synthesis.AutresDemandes))
                                    content.AppendLine($"Autres : {synthesis.AutresDemandes}");
                                content.AppendLine();
                            }
                        }
                        else if (fileName.StartsWith("PAI_", StringComparison.OrdinalIgnoreCase))
                        {
                            var synthesis = System.Text.Json.JsonSerializer.Deserialize<PAISynthesis>(jsonContent);
                            if (synthesis != null)
                            {
                                content.AppendLine($"## PAI du {synthesis.DateCreation:dd/MM/yyyy}");
                                content.AppendLine($"Motif : {synthesis.Motif}");
                                content.AppendLine();
                            }
                        }
                    }
                    catch { /* Ignorer JSON malformé */ }
                }
                else
                {
                    content.AppendLine($"## {Path.GetFileName(formulaire)}");
                    content.AppendLine(RemoveYamlFrontMatter(File.ReadAllText(formulaire)));
                    content.AppendLine();
                }
            }
        }

        // Ordonnances
        var ordonnancesDir = _pathService.GetOrdonnancesDirectory(patientName);
        if (Directory.Exists(ordonnancesDir))
        {
            content.AppendLine("# ORDONNANCES\n");
            var ordonnances = Directory.GetFiles(ordonnancesDir, "*.md")
                .OrderByDescending(f => File.GetLastWriteTime(f));

            foreach (var ordonnance in ordonnances)
            {
                content.AppendLine($"## {Path.GetFileName(ordonnance)}");
                content.AppendLine(RemoveYamlFrontMatter(File.ReadAllText(ordonnance)));
                content.AppendLine();
            }
        }

        // Synthèses de documents
        var documentsDir = _pathService.GetDocumentsDirectory(patientName);
        var docSynthesesDir = Path.Combine(documentsDir, "syntheses");
        if (Directory.Exists(docSynthesesDir))
        {
            content.AppendLine("# SYNTHÈSES DE DOCUMENTS MÉDICAUX\n");
            var syntheses = Directory.GetFiles(docSynthesesDir, "*.md")
                .OrderByDescending(f => File.GetLastWriteTime(f));

            foreach (var synthese in syntheses)
            {
                content.AppendLine($"## {Path.GetFileName(synthese)}");
                content.AppendLine(RemoveYamlFrontMatter(File.ReadAllText(synthese)));
                content.AppendLine();
            }
        }

        // Chats sauvegardés
        var chatDir = _pathService.GetChatDirectory(patientName);
        if (Directory.Exists(chatDir))
        {
            content.AppendLine("# DISCUSSIONS SAUVEGARDÉES\n");
            var chats = Directory.GetFiles(chatDir, "*.json")
                .OrderByDescending(f => File.GetLastWriteTime(f));

            foreach (var chat in chats)
            {
                try
                {
                    var jsonContent = File.ReadAllText(chat);
                    var exchange = System.Text.Json.JsonSerializer.Deserialize<ChatExchange>(jsonContent);
                    if (exchange != null)
                    {
                        content.AppendLine($"## {exchange.Etiquette ?? "Discussion"}");
                        content.AppendLine($"**Question:** {exchange.Question}");
                        content.AppendLine($"**Réponse:** {exchange.Response}");
                        content.AppendLine();
                    }
                }
                catch { /* Ignorer les fichiers JSON invalides */ }
            }
        }

        return content.ToString();
    }

    private string CollectNewContent(string patientDirectory, List<string> newItems)
    {
        var content = new StringBuilder();
        content.AppendLine("# NOUVEAUX ÉLÉMENTS\n");
        
        var patientName = Path.GetFileName(patientDirectory);

        foreach (var item in newItems)
        {
            try
            {
                // Extraire le type et le nom du fichier
                var parts = item.Split(new[] { ": " }, 2, StringSplitOptions.None);
                if (parts.Length != 2) continue;

                var type = parts[0];
                var fileName = parts[1];

                // Trouver le fichier correspondant
                string? filePath = null;

                if (type == "Note")
                {
                    foreach (var yearDir in Directory.GetDirectories(patientDirectory)
                        .Where(d => int.TryParse(Path.GetFileName(d), out _)))
                    {
                        var found = Directory.GetFiles(yearDir, fileName, SearchOption.AllDirectories).FirstOrDefault();
                        if (found != null)
                        {
                            filePath = found;
                            break;
                        }
                    }
                }
                else if (type == "Courrier")
                {
                    filePath = Path.Combine(_pathService.GetCourriersDirectory(patientName), fileName);
                }
                else if (type == "Attestation")
                {
                    filePath = Path.Combine(_pathService.GetAttestationsDirectory(patientName), fileName);
                }
                else if (type == "Formulaire")
                {
                    filePath = Path.Combine(_pathService.GetFormulairesDirectory(patientName), fileName);
                }
                else if (type == "Ordonnance")
                {
                    filePath = Path.Combine(_pathService.GetOrdonnancesDirectory(patientName), fileName);
                }
                else if (type == "Synthèse doc")
                {
                    var documentsDir = _pathService.GetDocumentsDirectory(patientName);
                    filePath = Path.Combine(documentsDir, "syntheses", fileName);
                }
                else if (type == "Discussion")
                {
                    filePath = Path.Combine(_pathService.GetChatDirectory(patientName), fileName);
                }

                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    content.AppendLine($"## {type}: {fileName}");

                    if (filePath.EndsWith(".json"))
                    {
                        var jsonContent = File.ReadAllText(filePath);
                        
                        if (type == "Discussion")
                        {
                            var exchange = System.Text.Json.JsonSerializer.Deserialize<ChatExchange>(jsonContent);
                            if (exchange != null)
                            {
                                content.AppendLine($"**Question:** {exchange.Question}");
                                content.AppendLine($"**Réponse:** {exchange.Response}");
                            }
                        }
                        else if (type == "Formulaire")
                        {
                            if (fileName.StartsWith("MDPH_", StringComparison.OrdinalIgnoreCase))
                            {
                                var synthesis = System.Text.Json.JsonSerializer.Deserialize<MDPHSynthesis>(jsonContent);
                                if (synthesis != null)
                                {
                                    content.AppendLine($"Dossier MDPH du {synthesis.DateCreation:dd/MM/yyyy}");
                                    content.AppendLine($"Demandes : {string.Join(", ", synthesis.Demandes)}");
                                    if (!string.IsNullOrWhiteSpace(synthesis.AutresDemandes))
                                        content.AppendLine($"Autres : {synthesis.AutresDemandes}");
                                }
                            }
                            else if (fileName.StartsWith("PAI_", StringComparison.OrdinalIgnoreCase))
                            {
                                var synthesis = System.Text.Json.JsonSerializer.Deserialize<PAISynthesis>(jsonContent);
                                if (synthesis != null)
                                {
                                    content.AppendLine($"PAI du {synthesis.DateCreation:dd/MM/yyyy}");
                                    content.AppendLine($"Motif : {synthesis.Motif}");
                                }
                            }
                        }
                    }
                    else if (filePath.EndsWith(".md"))
                    {
                        // Markdown
                        content.AppendLine(RemoveYamlFrontMatter(File.ReadAllText(filePath)));
                    }

                    content.AppendLine();
                }
            }
            catch { /* Ignorer les erreurs de lecture */ }
        }

        return content.ToString();
    }

    private string BuildCompleteSynthesisPrompt(string patientName, string allContent)
    {
        // Récupérer le prompt depuis la configuration
        var promptConfig = _promptConfigService.GetPrompt("synthesis_complete");
        var promptTemplate = promptConfig?.ActivePrompt ?? "";

        // Remplacer les placeholders
        return promptTemplate
            .Replace("{{Patient_Name}}", patientName)
            .Replace("{{Patient_Content}}", allContent);
    }

    private string BuildIncrementalUpdatePrompt(string patientName, string existingSynthesis, string newContent)
    {
        // Récupérer le prompt depuis la configuration
        var promptConfig = _promptConfigService.GetPrompt("synthesis_incremental");
        var promptTemplate = promptConfig?.ActivePrompt ?? "";

        // Remplacer les placeholders
        return promptTemplate
            .Replace("{{Existing_Synthesis}}", existingSynthesis)
            .Replace("{{New_Content}}", newContent);
    }

    private string BuildSynthesisWithMetadata(string synthesis, string type)
    {
        var yaml = $@"---
date_synthese: {DateTime.Now:yyyy-MM-ddTHH:mm:ss}
type: {type}
---

{synthesis}";
        return yaml;
    }

    private string RemoveYamlFrontMatter(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return content;

        if (!content.TrimStart().StartsWith("---"))
            return content;

        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
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

        if (yamlEndIndex > 0 && yamlEndIndex < lines.Length)
        {
            return string.Join("\n", lines.Skip(yamlEndIndex)).TrimStart();
        }

        return content;
    }

    private DateTime? GetLastSynthesisDate(string synthesisPath)
    {
        try
        {
            var content = File.ReadAllText(synthesisPath);
            var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            if (lines.Length < 3 || lines[0].Trim() != "---")
                return null;

            for (int i = 1; i < lines.Length; i++)
            {
                if (lines[i].Trim() == "---")
                    break;

                if (lines[i].StartsWith("date_synthese:"))
                {
                    var dateStr = lines[i].Substring("date_synthese:".Length).Trim();
                    if (DateTime.TryParse(dateStr, out var date))
                    {
                        return date;
                    }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
