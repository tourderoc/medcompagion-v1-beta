using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service de gestion des numéros de dossier patient.
    /// Format: "YYYY-NNNN" (ex: "2025-0042")
    /// </summary>
    public class PatientIdService
    {
        private readonly PathService _pathService;
        private readonly string _counterFilePath;
        private DossierCounter _counter;

        public PatientIdService(PathService pathService)
        {
            _pathService = pathService;
            _counterFilePath = Path.Combine(_pathService.GetBasePatientsDirectory(), "dossier_counter.json");
            _counter = LoadCounter();
        }

        /// <summary>
        /// Génère un nouveau numéro de dossier pour l'année en cours
        /// </summary>
        public string GenerateNewNumeroDossier()
        {
            var year = DateTime.Now.Year.ToString();

            if (!_counter.Counters.ContainsKey(year))
            {
                _counter.Counters[year] = 0;
            }

            _counter.Counters[year]++;
            var numero = _counter.Counters[year];

            SaveCounter();

            return $"{year}-{numero:D4}";
        }

        /// <summary>
        /// Migre tous les patients existants qui n'ont pas de numéro de dossier
        /// </summary>
        /// <returns>Nombre de patients migrés</returns>
        public async Task<(int migrated, int total, string? error)> MigrateExistingPatientsAsync()
        {
            try
            {
                var patientsPath = _pathService.GetBasePatientsDirectory();
                if (!Directory.Exists(patientsPath))
                {
                    return (0, 0, null);
                }

                var patientDirs = Directory.GetDirectories(patientsPath);
                var patientsToMigrate = new List<(string path, DateTime created)>();

                // Collecter les patients sans numéro de dossier
                foreach (var dir in patientDirs)
                {
                    var patientJsonPath = Path.Combine(dir, "info_patient", "patient.json");
                    if (!File.Exists(patientJsonPath))
                        continue;

                    var json = await File.ReadAllTextAsync(patientJsonPath);
                    var patient = JsonSerializer.Deserialize<PatientMetadata>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (patient != null && string.IsNullOrEmpty(patient.NumeroDossier))
                    {
                        // Utiliser la date de création du dossier
                        var dirInfo = new DirectoryInfo(dir);
                        patientsToMigrate.Add((patientJsonPath, dirInfo.CreationTime));
                    }
                }

                if (patientsToMigrate.Count == 0)
                {
                    return (0, patientDirs.Length, null);
                }

                // Trier par date de création (les plus anciens d'abord)
                patientsToMigrate = patientsToMigrate.OrderBy(p => p.created).ToList();

                int migrated = 0;

                foreach (var (path, created) in patientsToMigrate)
                {
                    var json = await File.ReadAllTextAsync(path);
                    var patient = JsonSerializer.Deserialize<PatientMetadata>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (patient != null)
                    {
                        // Générer un numéro basé sur l'année de création du dossier
                        var year = created.Year.ToString();
                        if (!_counter.Counters.ContainsKey(year))
                        {
                            _counter.Counters[year] = 0;
                        }
                        _counter.Counters[year]++;
                        patient.NumeroDossier = $"{year}-{_counter.Counters[year]:D4}";

                        // Sauvegarder
                        var options = new JsonSerializerOptions
                        {
                            WriteIndented = true,
                            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                        };
                        var updatedJson = JsonSerializer.Serialize(patient, options);
                        await File.WriteAllTextAsync(path, updatedJson, System.Text.Encoding.UTF8);

                        migrated++;
                    }
                }

                // Enregistrer la date de migration
                _counter.LastMigration = DateTime.UtcNow.ToString("o");
                SaveCounter();

                return (migrated, patientDirs.Length, null);
            }
            catch (Exception ex)
            {
                return (0, 0, ex.Message);
            }
        }

        /// <summary>
        /// Vérifie si des patients ont besoin d'une migration
        /// </summary>
        public async Task<int> CountPatientsWithoutNumeroDossierAsync()
        {
            try
            {
                var patientsPath = _pathService.GetBasePatientsDirectory();
                if (!Directory.Exists(patientsPath))
                {
                    return 0;
                }

                int count = 0;
                var patientDirs = Directory.GetDirectories(patientsPath);

                foreach (var dir in patientDirs)
                {
                    var patientJsonPath = Path.Combine(dir, "info_patient", "patient.json");
                    if (!File.Exists(patientJsonPath))
                        continue;

                    var json = await File.ReadAllTextAsync(patientJsonPath);
                    var patient = JsonSerializer.Deserialize<PatientMetadata>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (patient != null && string.IsNullOrEmpty(patient.NumeroDossier))
                    {
                        count++;
                    }
                }

                return count;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Attribue un numéro de dossier à un patient spécifique
        /// </summary>
        public async Task<(bool success, string? numeroDossier, string? error)> AssignNumeroDossierAsync(string patientJsonPath)
        {
            try
            {
                if (!File.Exists(patientJsonPath))
                {
                    return (false, null, "Fichier patient.json introuvable");
                }

                var json = await File.ReadAllTextAsync(patientJsonPath);
                var patient = JsonSerializer.Deserialize<PatientMetadata>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (patient == null)
                {
                    return (false, null, "Impossible de lire les données patient");
                }

                if (!string.IsNullOrEmpty(patient.NumeroDossier))
                {
                    return (true, patient.NumeroDossier, null); // Déjà attribué
                }

                patient.NumeroDossier = GenerateNewNumeroDossier();

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                var updatedJson = JsonSerializer.Serialize(patient, options);
                await File.WriteAllTextAsync(patientJsonPath, updatedJson, System.Text.Encoding.UTF8);

                return (true, patient.NumeroDossier, null);
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        #region Private Methods

        private DossierCounter LoadCounter()
        {
            try
            {
                if (File.Exists(_counterFilePath))
                {
                    var json = File.ReadAllText(_counterFilePath);
                    return JsonSerializer.Deserialize<DossierCounter>(json) ?? new DossierCounter();
                }
            }
            catch
            {
                // Ignore errors, return default
            }

            return new DossierCounter();
        }

        private void SaveCounter()
        {
            try
            {
                var directory = Path.GetDirectoryName(_counterFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_counter, options);
                File.WriteAllText(_counterFilePath, json);
            }
            catch
            {
                // Log error but don't throw
            }
        }

        #endregion
    }

    /// <summary>
    /// Compteur de numéros de dossier par année
    /// </summary>
    public class DossierCounter
    {
        public Dictionary<string, int> Counters { get; set; } = new();
        public string? LastMigration { get; set; }
    }
}
