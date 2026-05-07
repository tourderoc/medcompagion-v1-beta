using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MedCompanion.Models;

namespace MedCompanion.Services.Consultation
{
    public static class BlockDefinitionLoader
    {
        private static readonly string _jsonPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Resources", "Consultation", "interrogatoire_blocks.json");

        public static List<BlockDefinition> Load()
        {
            try
            {
                if (!File.Exists(_jsonPath))
                    return GetDefaults();

                var json = File.ReadAllText(_jsonPath, System.Text.Encoding.UTF8);
                return JsonSerializer.Deserialize<List<BlockDefinition>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? GetDefaults();
            }
            catch
            {
                return GetDefaults();
            }
        }

        public static List<ConsultationBlock> LoadAsBlocks()
        {
            var defs = Load();
            var blocks = new List<ConsultationBlock>();
            foreach (var d in defs)
            {
                blocks.Add(new ConsultationBlock
                {
                    Key = d.Key,
                    Title = d.Title,
                    ExpectedThemes = new List<string>(d.ExpectedThemes)
                });
            }
            return blocks;
        }

        private static List<BlockDefinition> GetDefaults() => new()
        {
            new() { Key = "identite",  Title = "Identité",               ExpectedThemes = new() { "age", "accompagnant", "classe", "ecole" } },
            new() { Key = "motif",     Title = "Motif & histoire",        ExpectedThemes = new() { "motif_principal", "anciennete", "retentissement" } },
            new() { Key = "famille",   Title = "Famille",                 ExpectedThemes = new() { "statut_parental", "pere", "mere" } },
            new() { Key = "fratrie",   Title = "Fratrie",                 ExpectedThemes = new() { "presence", "noms_ages" } },
            new() { Key = "atcds",     Title = "ATCDs médicaux",          ExpectedThemes = new() { "medical", "allergies" } },
            new() { Key = "scolarite", Title = "Scolarité",               ExpectedThemes = new() { "rapport_ecole", "niveau_ou_copains" } },
            new() { Key = "activites", Title = "Activités",               ExpectedThemes = new() { "pratique_ou_absence" } },
            new() { Key = "maison",    Title = "Maison (écrans/sommeil)", ExpectedThemes = new() { "ecrans", "sommeil" } }
        };
    }
}
