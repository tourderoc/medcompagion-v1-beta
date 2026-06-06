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
        return Task.FromResult((true, "Texte généré par le LLM.", (string?)null));
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
        var suggester = new RestitutionSuggesterService(mockLLM, syntheseService, ptService, null!);
        
        var bloc = new RestitutionBloc("projet_therapeutique", "Projet Thérapeutique Global", 7, "clinique");
        
        Console.WriteLine("Generating suggestion for bloc Projet Thérapeutique...");
        var result = await suggester.PrefillBlocAsync(patientName, bloc);
        
        Console.WriteLine("\n[Last Prompt Sent to LLM]\n");
        Console.WriteLine(mockLLM.LastPrompt);
        
        Console.WriteLine("\n[Result returned]\n");
        Console.WriteLine(result.Suggestion);

        // Cleanup
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Console.WriteLine("\nCleanup done. Test Passed.");
    }
}
