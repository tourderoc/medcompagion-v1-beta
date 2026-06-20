using System;
using System.IO;
using System.Text;
using System.Text.Json;
using MedCompanion.Models;

namespace MedCompanion.Services.Consultation
{
    public class FormulaireDataService
    {
        private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

        private static string DataPath(string patientDir)
            => Path.Combine(patientDir, "notes", "formulaire_data.json");

        public bool HasData(string? patientDir)
            => !string.IsNullOrEmpty(patientDir) && File.Exists(DataPath(patientDir));

        public FormulaireData Load(string? patientDir)
        {
            if (!HasData(patientDir)) return new FormulaireData();
            try
            {
                var json = File.ReadAllText(DataPath(patientDir!), Encoding.UTF8);
                return JsonSerializer.Deserialize<FormulaireData>(json) ?? new FormulaireData();
            }
            catch { return new FormulaireData(); }
        }

        public void Save(string patientDir, FormulaireData data)
        {
            try
            {
                data.DateSaisie = DateTime.Now;
                Directory.CreateDirectory(Path.Combine(patientDir, "notes"));
                File.WriteAllText(DataPath(patientDir), JsonSerializer.Serialize(data, _opts), Encoding.UTF8);
            }
            catch { }
        }
    }
}
