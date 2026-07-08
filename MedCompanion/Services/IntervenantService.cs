using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    /// <summary>
    /// Persiste la liste des intervenants (praticiens auteurs de bilans/documents)
    /// extraits automatiquement à l'import/scan. Un patient peut avoir plusieurs
    /// intervenants (un par bilan issu d'un professionnel différent).
    /// </summary>
    public class IntervenantService
    {
        private static readonly JsonSerializerOptions _opts = new() { WriteIndented = true };

        private static string DataPath(string infoPatientDir)
            => Path.Combine(infoPatientDir, "intervenants.json");

        public List<Intervenant> Load(string infoPatientDir)
        {
            var path = DataPath(infoPatientDir);
            if (!File.Exists(path)) return new List<Intervenant>();
            try
            {
                var json = File.ReadAllText(path, Encoding.UTF8);
                return JsonSerializer.Deserialize<List<Intervenant>>(json) ?? new List<Intervenant>();
            }
            catch
            {
                return new List<Intervenant>();
            }
        }

        /// <summary>
        /// Ajoute un intervenant s'il n'est pas déjà connu pour ce patient
        /// (même nom + même téléphone = doublon ignoré).
        /// </summary>
        public void Add(string infoPatientDir, Intervenant intervenant)
        {
            if (string.IsNullOrWhiteSpace(intervenant.Nom)) return;

            try
            {
                Directory.CreateDirectory(infoPatientDir);
                var list = Load(infoPatientDir);

                bool doublon = list.Any(i =>
                    string.Equals(i.Nom.Trim(), intervenant.Nom.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((i.Telephone ?? "").Trim(), (intervenant.Telephone ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

                if (doublon) return;

                list.Add(intervenant);
                File.WriteAllText(DataPath(infoPatientDir), JsonSerializer.Serialize(list, _opts), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[IntervenantService] Erreur sauvegarde: {ex.Message}");
            }
        }
    }
}
