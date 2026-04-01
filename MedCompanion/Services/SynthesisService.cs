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
    private readonly AnonymizationService _anonymizationService;
    private readonly PromptTrackerService? _promptTracker;  // ✅ NOUVEAU - Tracking des prompts

    public SynthesisService(
        OpenAIService openAIService,
        StorageService storageService,
        ContextLoader contextLoader,
        PathService pathService,
        PromptConfigService promptConfigService,
        SynthesisWeightTracker synthesisWeightTracker,
        AnonymizationService anonymizationService,
        PromptTrackerService? promptTracker = null)  // ✅ NOUVEAU
    {
        _openAIService = openAIService;
        _storageService = storageService;
        _contextLoader = contextLoader;
        _pathService = pathService;
        _promptConfigService = promptConfigService;
        _synthesisWeightTracker = synthesisWeightTracker;
        _anonymizationService = anonymizationService;
        _promptTracker = promptTracker;  // ✅ NOUVEAU - Stocker le tracker
    }

    /// <summary>
    /// Génère une synthèse complète à partir de tout le dossier patient
    /// Utilise Phase 1+2+3 pour anonymisation complète (PatientData + LLM extraction + Regex)
    /// </summary>
    public async Task<(bool success, string markdown, string? error)> GenerateCompleteSynthesisAsync(
        string patientName,
        string patientDirectory)
    {
        var busyService = BusyService.Instance;
        var cancellationToken = busyService.Start("Génération de la synthèse complète", canCancel: true);

        try
        {
            // ✅ ÉTAPE 1 : Charger les métadonnées complètes du patient
            busyService.UpdateStep("Chargement des métadonnées patient...");
            busyService.UpdateProgress(5);

            PatientMetadata? metadata = null;
            try
            {
                var patientJsonPath = System.IO.Path.Combine(patientDirectory, "info_patient", "patient.json");
                if (System.IO.File.Exists(patientJsonPath))
                {
                    var json = System.IO.File.ReadAllText(patientJsonPath);
                    System.Diagnostics.Debug.WriteLine($"[SynthesisService] JSON brut (premiers 500 chars): {json.Substring(0, Math.Min(500, json.Length))}");

                    // Options de désérialisation : ignorer la casse des noms de propriétés
                    var options = new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    metadata = System.Text.Json.JsonSerializer.Deserialize<PatientMetadata>(json, options);
                    System.Diagnostics.Debug.WriteLine($"[SynthesisService] Après déserialisation: Nom='{metadata?.Nom}', Prenom='{metadata?.Prenom}'");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[SynthesisService] patient.json NON TROUVÉ: {patientJsonPath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SynthesisService] Erreur chargement metadata: {ex.Message}");
            }

            // ✅ ÉTAPE 2 : Collecter tous les contenus du dossier (NON anonymisé)
            busyService.UpdateStep("Collecte de tous les documents patient...");
            busyService.UpdateProgress(10);

            var allContent = CollectAllPatientContent(patientDirectory);

            if (string.IsNullOrEmpty(allContent))
            {
                return (false, "", "Aucun contenu trouvé dans le dossier patient");
            }

            // ✅ Vérifier si on utilise un LLM local (pas besoin d'anonymisation)
            bool isLocalProvider = _openAIService.IsCurrentProviderLocal();
            System.Diagnostics.Debug.WriteLine($"[SynthesisService] Provider local: {isLocalProvider}");

            // ✅ ÉTAPE 3 : Construire le prompt
            busyService.UpdateStep("Construction du prompt...");
            busyService.UpdateProgress(25);

            var promptTemplate = _promptConfigService.GetActivePrompt("synthesis_complete");
            var prompt = promptTemplate
                .Replace("{{Patient_Name}}", patientName)
                .Replace("{{Patient_Content}}", allContent);

            string finalPrompt;
            AnonymizationContext? anonContext = null;

            if (isLocalProvider)
            {
                // ✅ MODE LOCAL : Pas d'anonymisation nécessaire (tout reste sur la machine)
                busyService.UpdateStep("Préparation du prompt (mode local)...");
                busyService.UpdateProgress(35);
                System.Diagnostics.Debug.WriteLine($"[SynthesisService] Mode local - Anonymisation bypassée");
                finalPrompt = prompt;
            }
            else
            {
                // ✅ MODE CLOUD : Anonymisation complète requise
                // Étape 3bis : Extraction des entités sensibles via LLM local (Phase 3)
                cancellationToken.ThrowIfCancellationRequested();
                busyService.UpdateStep("Extraction des entités sensibles (LLM local)...");
                busyService.UpdateProgress(25);

                System.Diagnostics.Debug.WriteLine($"[SynthesisService] Mode cloud - Extraction PII via LLM local...");
                PIIExtractionResult? extractedPii = null;
                try
                {
                    extractedPii = await _openAIService.ExtractPIIAsync(allContent, cancellationToken);
                    System.Diagnostics.Debug.WriteLine($"[SynthesisService] PII extraites: {extractedPii?.GetAllEntities().Count() ?? 0} entités");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SynthesisService] Erreur extraction PII: {ex.Message}");
                }

                // Étape 4 : Anonymisation hybride (Phase 1+2+3)
                busyService.UpdateStep("Anonymisation des données sensibles...");
                busyService.UpdateProgress(45);

                anonContext = new AnonymizationContext { WasAnonymized = true };
                finalPrompt = _anonymizationService.AnonymizeWithExtractedData(
                    prompt,
                    extractedPii,
                    metadata,
                    anonContext
                );
                System.Diagnostics.Debug.WriteLine($"[SynthesisService] Anonymisation terminée: {anonContext.Replacements.Count} remplacements");
            }

            // ✅ ÉTAPE 5 : Générer la synthèse via LLM
            cancellationToken.ThrowIfCancellationRequested();
            busyService.UpdateStep(isLocalProvider ? "Génération de la synthèse (LLM local)..." : "Génération de la synthèse (LLM cloud)...");
            busyService.UpdateProgress(55);

            var (success, synthesis, error) = await _openAIService.GenerateTextAsync(finalPrompt, maxTokens: 4000, cancellationToken: cancellationToken);

            if (!success || string.IsNullOrEmpty(synthesis))
            {
                return (false, "", error ?? "Erreur génération synthèse");
            }

            // ✅ ÉTAPE 6 : Désanonymiser le résultat (seulement si cloud)
            if (!isLocalProvider && anonContext != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                busyService.UpdateStep("Désanonymisation du résultat...");
                busyService.UpdateProgress(90);
                synthesis = _anonymizationService.Deanonymize(synthesis, anonContext);
            }

            // ✅ ÉTAPE 7 : Logger le prompt (si tracker disponible)
            busyService.UpdateStep("Finalisation...");
            busyService.UpdateProgress(95);

            LogPrompt("Synthèse", finalPrompt, synthesis, patientName);

            // Ajouter les métadonnées YAML
            var finalMarkdown = BuildSynthesisWithMetadata(synthesis, "complete");

            busyService.UpdateProgress(100);
            await System.Threading.Tasks.Task.Delay(300); // Petit délai pour voir 100%

            return (true, finalMarkdown, null);
        }
        catch (OperationCanceledException)
        {
            throw; // Propager pour gestion silencieuse dans le ViewModel
        }
        catch (Exception ex)
        {
            return (false, "", $"Erreur: {ex.Message}");
        }
        finally
        {
            busyService.Stop();
        }
    }

    /// <summary>
    /// Met à jour la synthèse de manière incrémentale
    /// Utilise Phase 1+2+3 pour anonymisation complète (PatientData + LLM extraction + Regex)
    /// </summary>
    public async Task<(bool success, string markdown, string? error)> UpdateSynthesisIncrementallyAsync(
        string patientName,
        string patientDirectory,
        string existingSynthesis,
        List<string> newItems)
    {
        var busyService = BusyService.Instance;
        var cancellationToken = busyService.Start("Mise à jour incrémentale de la synthèse", canCancel: true);

        try
        {
            if (newItems.Count == 0)
            {
                return (true, existingSynthesis, null); // Rien à ajouter
            }

            // ✅ ÉTAPE 1 : Charger les métadonnées complètes du patient
            busyService.UpdateStep("Chargement des métadonnées patient...");
            busyService.UpdateProgress(10);

            PatientMetadata? metadata = null;
            try
            {
                var patientJsonPath = System.IO.Path.Combine(patientDirectory, "info_patient", "patient.json");
                if (System.IO.File.Exists(patientJsonPath))
                {
                    var json = System.IO.File.ReadAllText(patientJsonPath);

                    // Options de désérialisation : ignorer la casse des noms de propriétés
                    var options = new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };
                    metadata = System.Text.Json.JsonSerializer.Deserialize<PatientMetadata>(json, options);
                }
            }
            catch { }

            // ✅ ÉTAPE 2 : Collecter uniquement le contenu des nouveaux éléments (NON anonymisé)
            busyService.UpdateStep($"Collecte des {newItems.Count} nouveau(x) élément(s)...");
            busyService.UpdateProgress(20);

            var newContent = CollectNewContent(patientDirectory, newItems);

            if (string.IsNullOrEmpty(newContent))
            {
                return (true, existingSynthesis, null);
            }

            // ✅ Vérifier si on utilise un LLM local (pas besoin d'anonymisation)
            bool isLocalProvider = _openAIService.IsCurrentProviderLocal();
            System.Diagnostics.Debug.WriteLine($"[SynthesisService] Provider local (incremental): {isLocalProvider}");

            // ✅ ÉTAPE 3 : Nettoyer les métadonnées YAML de l'ancienne synthèse
            busyService.UpdateStep("Préparation de la synthèse existante...");
            busyService.UpdateProgress(30);

            var cleanedSynthesis = RemoveYamlFrontMatter(existingSynthesis);

            // ✅ ÉTAPE 4 : Construire le prompt
            busyService.UpdateStep("Construction du prompt de mise à jour...");
            busyService.UpdateProgress(40);

            var promptTemplate = _promptConfigService.GetActivePrompt("synthesis_incremental");
            var prompt = promptTemplate
                .Replace("{{Existing_Synthesis}}", cleanedSynthesis)
                .Replace("{{New_Content}}", newContent);

            string finalPrompt;
            AnonymizationContext? anonContext = null;

            if (isLocalProvider)
            {
                // ✅ MODE LOCAL : Pas d'anonymisation nécessaire (tout reste sur la machine)
                busyService.UpdateStep("Préparation du prompt (mode local)...");
                busyService.UpdateProgress(50);
                System.Diagnostics.Debug.WriteLine($"[SynthesisService] Mode local - Anonymisation bypassée (incremental)");
                finalPrompt = prompt;
            }
            else
            {
                // ✅ MODE CLOUD : Anonymisation complète requise
                // Étape 4bis : Extraction des entités sensibles via LLM local (Phase 3)
                cancellationToken.ThrowIfCancellationRequested();
                busyService.UpdateStep("Extraction des entités sensibles (LLM local)...");
                busyService.UpdateProgress(40);

                System.Diagnostics.Debug.WriteLine($"[SynthesisService] Mode cloud - Extraction PII incrémentale via LLM local...");
                PIIExtractionResult? extractedPii = null;
                try
                {
                    extractedPii = await _openAIService.ExtractPIIAsync(newContent, cancellationToken);
                    System.Diagnostics.Debug.WriteLine($"[SynthesisService] PII extraites: {extractedPii?.GetAllEntities().Count() ?? 0} entités");
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[SynthesisService] Erreur extraction PII: {ex.Message}");
                }

                // Étape 5 : Anonymisation hybride (Phase 1+2+3)
                busyService.UpdateStep("Anonymisation des données sensibles...");
                busyService.UpdateProgress(60);

                anonContext = new AnonymizationContext { WasAnonymized = true };
                finalPrompt = _anonymizationService.AnonymizeWithExtractedData(
                    prompt,
                    extractedPii,
                    metadata,
                    anonContext
                );
                System.Diagnostics.Debug.WriteLine($"[SynthesisService] Anonymisation terminée: {anonContext.Replacements.Count} remplacements");
            }

            // ✅ ÉTAPE 6 : Générer la mise à jour via LLM
            cancellationToken.ThrowIfCancellationRequested();
            busyService.UpdateStep(isLocalProvider ? "Génération de la mise à jour (LLM local)..." : "Génération de la mise à jour (LLM cloud)...");
            busyService.UpdateProgress(70);

            var (success, updatedSynthesis, error) = await _openAIService.GenerateTextAsync(finalPrompt, maxTokens: 4000, cancellationToken: cancellationToken);

            if (!success || string.IsNullOrEmpty(updatedSynthesis))
            {
                return (false, "", error ?? "Erreur mise à jour synthèse");
            }

            // ✅ ÉTAPE 7 : Désanonymiser le résultat (seulement si cloud)
            if (!isLocalProvider && anonContext != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                busyService.UpdateStep("Désanonymisation du résultat...");
                busyService.UpdateProgress(90);
                updatedSynthesis = _anonymizationService.Deanonymize(updatedSynthesis, anonContext);
            }

            // ✅ ÉTAPE 8 : Logger le prompt (si tracker disponible)
            busyService.UpdateStep("Finalisation...");
            busyService.UpdateProgress(95);

            LogPrompt("Synthèse", finalPrompt, updatedSynthesis, patientName);

            // Ajouter les métadonnées YAML
            var finalMarkdown = BuildSynthesisWithMetadata(updatedSynthesis, "incremental");

            busyService.UpdateProgress(100);
            await System.Threading.Tasks.Task.Delay(300); // Petit délai pour voir 100%

            return (true, finalMarkdown, null);
        }
        catch (OperationCanceledException)
        {
            throw; // Propager pour gestion silencieuse dans le ViewModel
        }
        catch (Exception ex)
        {
            return (false, "", $"Erreur: {ex.Message}");
        }
        finally
        {
            busyService.Stop();
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
        var courrierFolders = _pathService.GetAllYearDirectories(patientName, "courriers");
        foreach (var dir in courrierFolders)
        {
            var courriers = Directory.GetFiles(dir, "*.md")
                .Where(f => File.GetLastWriteTime(f) > lastSynthesisDate.Value);
            newItems.AddRange(courriers.Select(f => $"Courrier: {Path.GetFileName(f)}"));
        }

        // Attestations
        var attestationFolders = _pathService.GetAllYearDirectories(patientName, "attestations");
        foreach (var dir in attestationFolders)
        {
            var attestations = Directory.GetFiles(dir, "*.md")
                .Where(f => File.GetLastWriteTime(f) > lastSynthesisDate.Value);
            newItems.AddRange(attestations.Select(f => $"Attestation: {Path.GetFileName(f)}"));
        }

        // Formulaires
        var formulaireFolders = _pathService.GetAllYearDirectories(patientName, "formulaires");
        foreach (var dir in formulaireFolders)
        {
            var formulaires = Directory.GetFiles(dir, "*.*")
                .Where(f => (f.EndsWith(".md") || f.EndsWith(".docx") || f.EndsWith(".json")) && 
                           File.GetLastWriteTime(f) > lastSynthesisDate.Value);
            newItems.AddRange(formulaires.Select(f => $"Formulaire: {Path.GetFileName(f)}"));
        }

        // Ordonnances
        var ordonnanceFolders = _pathService.GetAllYearDirectories(patientName, "ordonnances");
        foreach (var dir in ordonnanceFolders)
        {
            var ordonnances = Directory.GetFiles(dir, "*.md")
                .Where(f => File.GetLastWriteTime(f) > lastSynthesisDate.Value);
            newItems.AddRange(ordonnances.Select(f => $"Ordonnance: {Path.GetFileName(f)}"));
        }

        // Synthèses de documents
        var documentFolders = _pathService.GetAllYearDirectories(patientName, "documents");
        foreach (var dir in documentFolders)
        {
            var docSynthesesDir = Path.Combine(dir, "syntheses_documents"); // Note: Corrected subfolder name
            if (Directory.Exists(docSynthesesDir))
            {
                var docSyntheses = Directory.GetFiles(docSynthesesDir, "*.md")
                    .Where(f => File.GetLastWriteTime(f) > lastSynthesisDate.Value);
                newItems.AddRange(docSyntheses.Select(f => $"Synthèse doc: {Path.GetFileName(f)}"));
            }
        }

        // Chats sauvegardés
        var chatFolders = _pathService.GetAllYearDirectories(patientName, "chat");
        foreach (var dir in chatFolders)
        {
            var chats = Directory.GetFiles(dir, "*.md") // Note: Chats are .md now after StorageService update
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
        var courrierFolders = _pathService.GetAllYearDirectories(patientName, "courriers");
        if (courrierFolders.Any())
        {
            content.AppendLine("# COURRIERS\n");
            var allCourriers = new List<string>();
            foreach (var dir in courrierFolders)
            {
                allCourriers.AddRange(Directory.GetFiles(dir, "*.md"));
            }

            foreach (var courrier in allCourriers.OrderByDescending(f => File.GetLastWriteTime(f)))
            {
                content.AppendLine($"## {Path.GetFileName(courrier)}");
                content.AppendLine(RemoveYamlFrontMatter(File.ReadAllText(courrier)));
                content.AppendLine();
            }
        }

        // Attestations
        var attestationFolders = _pathService.GetAllYearDirectories(patientName, "attestations");
        if (attestationFolders.Any())
        {
            content.AppendLine("# ATTESTATIONS\n");
            var allAttestations = new List<string>();
            foreach (var dir in attestationFolders)
            {
                allAttestations.AddRange(Directory.GetFiles(dir, "*.md"));
            }

            foreach (var attestation in allAttestations.OrderByDescending(f => File.GetLastWriteTime(f)))
            {
                content.AppendLine($"## {Path.GetFileName(attestation)}");
                content.AppendLine(RemoveYamlFrontMatter(File.ReadAllText(attestation)));
                content.AppendLine();
            }
        }

        // Formulaires (MDPH, PAI, etc.)
        var formulaireFolders = _pathService.GetAllYearDirectories(patientName, "formulaires");
        if (formulaireFolders.Any())
        {
            content.AppendLine("# FORMULAIRES\n");
            var allFormulaires = new List<string>();
            foreach (var dir in formulaireFolders)
            {
                allFormulaires.AddRange(Directory.GetFiles(dir, "*.*")
                    .Where(f => f.EndsWith(".md") || f.EndsWith(".json")));
            }

            foreach (var formulaire in allFormulaires.OrderByDescending(f => File.GetLastWriteTime(f)))
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
        var ordonnanceFolders = _pathService.GetAllYearDirectories(patientName, "ordonnances");
        if (ordonnanceFolders.Any())
        {
            content.AppendLine("# ORDONNANCES\n");
            var allOrdonnances = new List<string>();
            foreach (var dir in ordonnanceFolders)
            {
                allOrdonnances.AddRange(Directory.GetFiles(dir, "*.md"));
            }

            foreach (var ordonnance in allOrdonnances.OrderByDescending(f => File.GetLastWriteTime(f)))
            {
                content.AppendLine($"## {Path.GetFileName(ordonnance)}");
                content.AppendLine(RemoveYamlFrontMatter(File.ReadAllText(ordonnance)));
                content.AppendLine();
            }
        }

        // Synthèses de documents
        var documentFolders = _pathService.GetAllYearDirectories(patientName, "documents");
        if (documentFolders.Any())
        {
            var allSyntheses = new List<string>();
            foreach (var dir in documentFolders)
            {
                var docSynthesesDir = Path.Combine(dir, "syntheses_documents");
                if (Directory.Exists(docSynthesesDir))
                {
                    allSyntheses.AddRange(Directory.GetFiles(docSynthesesDir, "*.md"));
                }
            }

            if (allSyntheses.Any())
            {
                content.AppendLine("# SYNTHÈSES DE DOCUMENTS MÉDICAUX\n");
                foreach (var synthese in allSyntheses.OrderByDescending(f => File.GetLastWriteTime(f)))
                {
                    content.AppendLine($"## {Path.GetFileName(synthese)}");
                    content.AppendLine(RemoveYamlFrontMatter(File.ReadAllText(synthese)));
                    content.AppendLine();
                }
            }
        }

        // Chats sauvegardés
        var chatFolders = _pathService.GetAllYearDirectories(patientName, "chat");
        if (chatFolders.Any())
        {
            var allChats = new List<string>();
            foreach (var dir in chatFolders)
            {
                allChats.AddRange(Directory.GetFiles(dir, "*.md"));
            }

            if (allChats.Any())
            {
                content.AppendLine("# DISCUSSIONS SAUVEGARDÉES\n");
                foreach (var chat in allChats.OrderByDescending(f => File.GetLastWriteTime(f)))
                {
                    try
                    {
                        var markdown = File.ReadAllText(chat);
                        var exchange = ChatExchange.FromMarkdown(markdown, chat);
                        if (exchange != null)
                        {
                            content.AppendLine($"## {exchange.Etiquette ?? "Discussion"}");
                            content.AppendLine($"**Question:** {exchange.Question}");
                            content.AppendLine($"**Réponse:** {exchange.Response}");
                            content.AppendLine();
                        }
                    }
                    catch { /* Ignorer les fichiers invalides */ }
                }
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
                    filePath = _pathService.GetAllYearDirectories(patientName, "courriers")
                        .Select(dir => Path.Combine(dir, fileName))
                        .FirstOrDefault(f => File.Exists(f));
                }
                else if (type == "Attestation")
                {
                    filePath = _pathService.GetAllYearDirectories(patientName, "attestations")
                        .Select(dir => Path.Combine(dir, fileName))
                        .FirstOrDefault(f => File.Exists(f));
                }
                else if (type == "Formulaire")
                {
                    filePath = _pathService.GetAllYearDirectories(patientName, "formulaires")
                        .Select(dir => Path.Combine(dir, fileName))
                        .FirstOrDefault(f => File.Exists(f));
                }
                else if (type == "Ordonnance")
                {
                    filePath = _pathService.GetAllYearDirectories(patientName, "ordonnances")
                        .Select(dir => Path.Combine(dir, fileName))
                        .FirstOrDefault(f => File.Exists(f));
                }
                else if (type == "Synthèse doc")
                {
                    filePath = _pathService.GetAllYearDirectories(patientName, "documents")
                        .Select(dir => Path.Combine(dir, "syntheses_documents", fileName))
                        .FirstOrDefault(f => File.Exists(f));
                }
                else if (type == "Discussion")
                {
                    filePath = _pathService.GetAllYearDirectories(patientName, "chat")
                        .Select(dir => Path.Combine(dir, fileName))
                        .FirstOrDefault(f => File.Exists(f));
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

    // NOTE: BuildCompleteSynthesisPrompt et BuildIncrementalUpdatePrompt supprimées
    // Car GetAnonymizedPromptAsync de PromptConfigService gère désormais :
    // - La récupération du template
    // - Le remplacement des placeholders
    // - L'anonymisation du prompt final

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

    /// <summary>
    /// Logger les prompts de synthèse (si tracker disponible)
    /// </summary>
    private void LogPrompt(string module, string userPrompt, string aiResponse, string patientName)
    {
        if (_promptTracker == null)
            return;

        try
        {
            // Récupérer le system prompt depuis le service de configuration
            var systemPrompt = _promptConfigService.GetActivePrompt("system_global");

            _promptTracker.LogPrompt(new PromptLogEntry
            {
                Timestamp = DateTime.Now,
                Module = module,  // "Synthèse" pour matcher le filtre dans l'UI
                SystemPrompt = systemPrompt,
                UserPrompt = userPrompt,  // ⚠️ Contient le PSEUDONYME (anonymisé)
                AIResponse = aiResponse,  // ✅ Réponse DÉSANONYMISÉE (vrai nom)
                TokensUsed = 0,  // TODO: récupérer depuis la réponse LLM si disponible
                LLMProvider = "OpenAI/Ollama",
                ModelName = "GPT-4",
                Success = true,
                Error = null
            });
        }
        catch (Exception ex)
        {
            // Ne pas bloquer la génération si le logging échoue
            System.Diagnostics.Debug.WriteLine($"[SynthesisService] Erreur logging prompt: {ex.Message}");
        }
    }
}
