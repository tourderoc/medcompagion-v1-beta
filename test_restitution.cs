using System;
using System.IO;
using System.Linq;
using MedCompanion.Models;
using MedCompanion.Models.Restitutions;
using MedCompanion.Services;
using MedCompanion.Services.Restitutions;

class Program
{
    static void Main()
    {
        var basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "MedCompanion", "patients");
        var pathService = new PathService();
        var service = new RestitutionService(pathService);
        
        var patientName = "TEST_Restitution_Phase3";
        var r = new DossierRestitutionInitial();
        r.PatientNomComplet = patientName;
        r.Blocs[0].ContenuPreremplit = "Contenu test 1";
        r.Blocs[0].ContenuValide = "Contenu validé 1";
        r.Blocs[0].IsValidated = true;
        
        // Save
        service.SaveBrouillon(r);
        Console.WriteLine($"Brouillon saved at: {r.Id}");
        
        // Load
        var loaded = service.Load(r.Id);
        if (loaded == null) {
            Console.WriteLine("Failed to load");
            return;
        }
        Console.WriteLine($"Loaded type: {loaded.Type}");
        Console.WriteLine($"Loaded block 1 valid content: {loaded.Blocs[0].ContenuValide}");
        Console.WriteLine($"Loaded block 1 is validated: {loaded.Blocs[0].IsValidated}");
        
        // Validate
        var finalPath = service.Validate(loaded);
        Console.WriteLine($"Validated saved at: {finalPath}");
        
        // List
        var list = service.ListRestitutionsAsync(patientName).Result;
        Console.WriteLine($"Found {list.Count} restitutions");
        
        // Cleanup
        var dir = pathService.GetPatientRootDirectory(patientName);
        if (Directory.Exists(dir))
            Directory.Delete(dir, true);
        Console.WriteLine("Cleanup done");
    }
}
