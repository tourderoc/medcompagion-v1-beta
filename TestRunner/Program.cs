using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MedCompanion.Models;
using MedCompanion.Models.Restitutions;
using MedCompanion.Services;
using MedCompanion.Services.Restitutions;
using MedCompanion.Services.Synthesis;
using MedCompanion.Services.Therapeutique;
using MedCompanion.Services.LLM;

public class MockLLM : ILLMService
{
    public string LastPrompt { get; set; }
    public string ResultToReturn { get; set; } = "Texte généré par le LLM.";
    
    public bool IsConfigured() => true;
    public string GetModelName() => "MockLLM";
    public string GetProviderName() => "MockProvider";
    public Task<(bool isConnected, string message)> CheckConnectionAsync() => Task.FromResult((true, "OK"));
    public Task<(bool success, string message)> WarmupAsync() => Task.FromResult((true, "OK"));
    public Task<(bool success, string message)> UnloadAsync() => Task.FromResult((true, "OK"));

    public Task<(bool success, string result, string? error)> GenerateTextAsync(string prompt, int maxTokens = 1500, CancellationToken cancellationToken = default, string? forceModel = null) => Task.FromResult((true, "Generated", (string?)null));

    public Task<(bool success, string result, string? error)> ChatAsync(string systemPrompt, List<(string role, string content)> messages, int maxTokens = 1500, CancellationToken cancellationToken = default, string? forceModel = null)
    {
        LastPrompt = $"=== System Prompt ===\n{systemPrompt}\n\n=== User Prompt ===\n{messages[0].content}";
        return Task.FromResult((true, ResultToReturn, (string?)null));
    }

    public Task<(bool success, string fullResponse, string? error)> ChatStreamAsync(string systemPrompt, List<(string role, string content)> messages, Action<string> onChunkReceived, int maxTokens = 1500, CancellationToken cancellationToken = default) => Task.FromResult((true, "Streamed", (string?)null));

    public Task<(bool success, string result, string? error)> AnalyzeImageAsync(string prompt, byte[] imageBytes, int maxTokens = 1500, CancellationToken cancellationToken = default) => Task.FromResult((true, "Image analyzed", (string?)null));
}

class Program
{
    static async Task Main()
    {
        var pathService = new PathService();
        var patientName = "TEST_Restitution_Phase4";
        
        // Setup environment
        var dir = pathService.GetPatientRootDirectory(patientName);
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        pathService.EnsurePatientStructure(patientName);

        // Create a Projet Therapeutique
        var ptService = new ProjetTherapeutiqueService(pathService);
        var pt = new MedCompanion.Models.Therapeutique.ProjetTherapeutique { PatientNomComplet = patientName };
        pt.ObjectifsPrioritaires = "Objectif test 1\nObjectif test 2";
        pt.ActionsMedicales.Add(new MedCompanion.Models.Therapeutique.ProjetAction { Libelle = "Action Med 1", Description = "Desc 1" });
        ptService.Validate(pt);

        // Instantiate Suggester Service
        var mockLLM = new MockLLM();
        var syntheseService = new SyntheseGlobaleService(pathService);
        var readerService = new DossierReaderService(pathService, syntheseService, ptService);
        var suggester = new RestitutionSuggesterService(mockLLM, readerService, syntheseService, ptService);
        
        var bloc = new RestitutionBloc("projet_therapeutique", "Projet Thérapeutique Global", 7, "clinique");
        
        var reading = await readerService.ReadAsync(patientName);
        Console.WriteLine("\nGenerating progressive suggestion for Contexte Familial...");
        await suggester.SuggestContexteFamilialProgressiveAsync(reading, _ => {});
        Console.WriteLine("\n[Last Progressive Prompt Sent to LLM]\n");
        Console.WriteLine(mockLLM.LastPrompt);

        // Test PatientContextAuditService
        Console.WriteLine("\nTesting PatientContextAuditService...");
        var auditService = new PatientContextAuditService();
        mockLLM.ResultToReturn = @"{
            ""ecole"": ""Ecole Primaire Centre"",
            ""classe"": ""CE2"",
            ""mereNom"": ""Alice"",
            ""mereAge"": ""39 ans"",
            ""mereJob"": ""Infirmière"",
            ""pereNom"": ""Bob"",
            ""pereAge"": ""41 ans"",
            ""pereJob"": ""Ingénieur"",
            ""fratrie"": ""un grand frère de 10 ans"",
            ""marcheAge"": ""12 mois"",
            ""langageAcq"": ""normal"",
            ""propreteAcq"": ""acquise""
        }";
        var details = await auditService.ExtractContextAsync(mockLLM, "Le patient va à l'école primaire centre en CE2. Sa mère Alice, 39 ans, est infirmière et son père Bob, 41 ans, est ingénieur. Marche acquise à 12 mois.");
        if (details.Ecole == "Ecole Primaire Centre" && 
            details.Classe == "CE2" &&
            details.MereNom == "Alice" &&
            details.MereAge == "39 ans" &&
            details.MereJob == "Infirmière" &&
            details.PereNom == "Bob" &&
            details.PereAge == "41 ans" &&
            details.PereJob == "Ingénieur" &&
            details.Fratrie == "un grand frère de 10 ans" &&
            details.MarcheAge == "12 mois" &&
            details.LangageAcq == "normal" &&
            details.PropreteAcq == "acquise")
        {
            Console.WriteLine("PatientContextAuditService Test Passed.");
        }
        else
        {
            Console.Error.WriteLine("PatientContextAuditService Test Failed: extracted details do not match expectation!");
            Environment.Exit(1);
        }

        // Test with Markdown blocks around JSON
        mockLLM.ResultToReturn = @"```json
{
    ""ecole"": ""Ecole Primaire Centre"",
    ""classe"": ""CE2""
}
```";
        var detailsMd = await auditService.ExtractContextAsync(mockLLM, "...");
        if (detailsMd.Ecole == "Ecole Primaire Centre" && detailsMd.Classe == "CE2")
        {
            Console.WriteLine("PatientContextAuditService Markdown JSON Test Passed.");
        }
        else
        {
            Console.Error.WriteLine("PatientContextAuditService Markdown JSON Test Failed!");
            Environment.Exit(1);
        }

        // Cleanup
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Console.WriteLine("\nCleanup done. Test Passed.");
    }
}
