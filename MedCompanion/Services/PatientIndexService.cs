using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service d'indexation et de recherche des patients
    /// </summary>
    public class PatientIndexService
    {
        private readonly string _patientsRoot;
        private readonly string _recentPatientsPath;
        private readonly PathService? _pathService;
        private List<PatientIndexEntry> _index = new();
        private FileSystemWatcher? _watcher;
        private List<string> _recentPatientIds = new();
        private const int MaxRecentPatients = 20; // Augmenté de 5 à 20 pour avoir plus d'historique

        public PatientIndexService(PathService? pathService = null)
        {
            _pathService = pathService;
            
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _patientsRoot = Path.Combine(documentsPath, "MedCompanion", "patients");
            _recentPatientsPath = Path.Combine(documentsPath, "MedCompanion", "recent-patients.json");
            
            if (!Directory.Exists(_patientsRoot))
            {
                Directory.CreateDirectory(_patientsRoot);
            }
            
            // Charger les patients récents
            LoadRecentPatients();
        }

        /// <summary>
        /// Scanne tous les dossiers patients et construit l'index
        /// </summary>
        public async Task ScanAsync()
        {
            await Task.Run(() =>
            {
                var newIndex = new List<PatientIndexEntry>();
                
                if (!Directory.Exists(_patientsRoot))
                    return;

                foreach (var patientDir in Directory.GetDirectories(_patientsRoot))
                {
                    try
                    {
                        // Chercher d'abord dans le nouveau dossier info_patient/
                        string? patientJsonPath = null;
                        var dirName = Path.GetFileName(patientDir);
                        
                        if (_pathService != null)
                        {
                            // Utiliser PathService pour la nouvelle structure
                            var parts = dirName.Split('_');
                            if (parts.Length >= 2)
                            {
                                var nom = parts[0];
                                var prenom = string.Join(" ", parts.Skip(1));
                                var nomComplet = $"{prenom} {nom}";
                                patientJsonPath = _pathService.GetPatientJsonPath(nomComplet);
                            }
                        }
                        
                        // Fallback : ancienne structure à la racine
                        if (patientJsonPath == null || !File.Exists(patientJsonPath))
                        {
                            patientJsonPath = Path.Combine(patientDir, "patient.json");
                        }
                        
                        if (File.Exists(patientJsonPath))
                        {
                            // Lire depuis patient.json
                            var json = File.ReadAllText(patientJsonPath);
                            var metadata = JsonSerializer.Deserialize<PatientMetadata>(json, new JsonSerializerOptions
                            {
                                PropertyNameCaseInsensitive = true
                            });
                            
                            if (metadata != null && !string.IsNullOrEmpty(metadata.Prenom) && !string.IsNullOrEmpty(metadata.Nom))
                            {
                                newIndex.Add(new PatientIndexEntry
                                {
                                    Id = Path.GetFileName(patientDir),
                                    Prenom = metadata.Prenom,
                                    Nom = metadata.Nom,
                                    Dob = metadata.Dob,
                                    Sexe = metadata.Sexe,
                                    DirectoryPath = patientDir
                                });
                            }
                        }
                        else
                        {
                            // Inférer depuis le nom du dossier (format: Nom_Prenom)
                            var parts = dirName.Split('_');
                            
                            if (parts.Length >= 2)
                            {
                                newIndex.Add(new PatientIndexEntry
                                {
                                    Id = dirName,
                                    Nom = parts[0],
                                    Prenom = string.Join(" ", parts.Skip(1)),
                                    DirectoryPath = patientDir
                                });
                            }
                        }
                    }
                    catch
                    {
                        // Ignorer les dossiers problématiques
                    }
                }
                
                _index = newIndex;
            });
        }

        /// <summary>
        /// Recherche des patients par nom/prénom
        /// </summary>
        public List<PatientIndexEntry> Search(string query, int maxResults = 10)
        {
            if (string.IsNullOrWhiteSpace(query))
                return new List<PatientIndexEntry>();

            var normalizedQuery = Normalize(query.Trim());
            
            var results = _index
                .Where(p =>
                {
                    var normalizedNom = Normalize(p.Nom);
                    var normalizedPrenom = Normalize(p.Prenom);
                    var normalizedComplet = Normalize(p.NomComplet); // Format: "NOM Prenom"
                    var normalizedInverse = Normalize($"{p.Prenom} {p.Nom}"); // Format: "Prenom NOM" (Doctolib)
                    
                    return normalizedNom.Contains(normalizedQuery) ||
                           normalizedPrenom.Contains(normalizedQuery) ||
                           normalizedComplet.Contains(normalizedQuery) ||
                           normalizedInverse.Contains(normalizedQuery); // Supporter les deux ordres !
                })
                .OrderBy(p =>
                {
                    // Priorité: match exact sur nom > prénom > nom complet
                    var normalizedNom = Normalize(p.Nom);
                    var normalizedPrenom = Normalize(p.Prenom);
                    
                    if (normalizedNom.StartsWith(normalizedQuery))
                        return 0;
                    if (normalizedPrenom.StartsWith(normalizedQuery))
                        return 1;
                    return 2;
                })
                .ThenBy(p => p.Nom)
                .ThenBy(p => p.Prenom)
                .Take(maxResults)
                .ToList();

            return results;
        }

        /// <summary>
        /// Crée ou met à jour un patient
        /// </summary>
        public (bool success, string message, string? id, string? path) Upsert(PatientMetadata metadata)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(metadata.Prenom) || string.IsNullOrWhiteSpace(metadata.Nom))
                {
                    return (false, "Prénom et nom requis", null, null);
                }

                // Créer l'ID (Nom_Prenom)
                var id = $"{metadata.Nom}_{metadata.Prenom.Replace(" ", "_")}";
                var patientDir = Path.Combine(_patientsRoot, id);

                // 🐛 DEBUG: Logger la création
                System.Diagnostics.Debug.WriteLine($"[Upsert] Création patient: ID='{id}', Nom='{metadata.Nom}', Prenom='{metadata.Prenom}'");

                // ✅ AMÉLIORATION: Détection de doublons AVANCÉE avec comparaison mot-à-mot et date de naissance
                var duplicateResult = DetectDuplicates(metadata, id);

                // Si doublon détecté avec score élevé (≥80%) → BLOQUER et demander confirmation utilisateur
                if (duplicateResult.isDuplicate && duplicateResult.similarPatient != null)
                {
                    var existingPatient = duplicateResult.similarPatient;
                    var existingMetadata = GetMetadata(existingPatient.Id);

                    // Formater le message d'alerte pour l'utilisateur
                    var alertMessage = $"DUPLICATE_DETECTED|{existingPatient.Id}|{existingPatient.NomComplet}";
                    if (existingMetadata != null && !string.IsNullOrEmpty(existingMetadata.Dob))
                    {
                        alertMessage += $"|{existingMetadata.DobFormatted}";
                    }

                    // ❌ NE PAS CRÉER - Retourner une alerte
                    return (false, alertMessage, existingPatient.Id, existingPatient.DirectoryPath);
                }
                
                // Créer le dossier si nécessaire
                if (!Directory.Exists(patientDir))
                {
                    Directory.CreateDirectory(patientDir);
                }
                
                // Créer le sous-dossier de l'année courante
                var yearDir = Path.Combine(patientDir, DateTime.Now.Year.ToString());
                if (!Directory.Exists(yearDir))
                {
                    Directory.CreateDirectory(yearDir);
                }

                // Écrire patient.json (utiliser PathService si disponible, sinon fallback ancien chemin)
                string patientJsonPath;
                if (_pathService != null)
                {
                    var nomComplet = $"{metadata.Prenom} {metadata.Nom}";
                    patientJsonPath = _pathService.GetPatientJsonPath(nomComplet);
                    // Créer le dossier info_patient si nécessaire
                    var infoDir = _pathService.GetInfoPatientDirectory(nomComplet);
                    if (!Directory.Exists(infoDir))
                    {
                        Directory.CreateDirectory(infoDir);
                    }
                }
                else
                {
                    // Fallback : ancienne structure à la racine
                    patientJsonPath = Path.Combine(patientDir, "patient.json");
                }
                
                var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                File.WriteAllText(patientJsonPath, json, Encoding.UTF8);

                // Mettre à jour l'index
                var existingIndex = _index.FirstOrDefault(p => p.Id == id);
                if (existingIndex != null)
                {
                    _index.Remove(existingIndex);
                }
                
                _index.Add(new PatientIndexEntry
                {
                    Id = id,
                    Prenom = metadata.Prenom,
                    Nom = metadata.Nom,
                    Dob = metadata.Dob,
                    Sexe = metadata.Sexe,
                    DirectoryPath = patientDir
                });

                return (true, "Patient créé/mis à jour avec succès", id, patientDir);
            }
            catch (Exception ex)
            {
                return (false, $"Erreur: {ex.Message}", null, null);
            }
        }

        /// <summary>
        /// Détecte les doublons potentiels avec analyse mot-à-mot et date de naissance
        /// Retourne (isDuplicate, similarPatient, similarityScore)
        /// </summary>
        private (bool isDuplicate, PatientIndexEntry? similarPatient, int score) DetectDuplicates(PatientMetadata newPatient, string newId)
        {
            var bestMatch = (isDuplicate: false, patient: (PatientIndexEntry?)null, score: 0);

            foreach (var existingPatient in _index.Where(p => p.Id != newId))
            {
                int score = 0;
                var existingMetadata = GetMetadata(existingPatient.Id);

                // 1. Comparer les dates de naissance (si identiques = +50 points)
                if (!string.IsNullOrEmpty(newPatient.Dob) &&
                    !string.IsNullOrEmpty(existingMetadata?.Dob) &&
                    newPatient.Dob == existingMetadata.Dob)
                {
                    score += 50;
                }

                // 2. Comparer les noms mot-à-mot (noms composés)
                var newNomParts = newPatient.Nom.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => Normalize(p)).ToList();
                var existingNomParts = existingPatient.Nom.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => Normalize(p)).ToList();

                // Compter les mots en commun dans le nom
                int commonNomWords = newNomParts.Count(w => existingNomParts.Contains(w));
                if (commonNomWords > 0)
                {
                    score += (commonNomWords * 20); // +20 points par mot commun
                }

                // 3. Comparer les prénoms mot-à-mot (prénoms composés)
                var newPrenomParts = newPatient.Prenom.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => Normalize(p)).ToList();
                var existingPrenomParts = existingPatient.Prenom.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => Normalize(p)).ToList();

                // Compter les mots en commun dans le prénom
                int commonPrenomWords = newPrenomParts.Count(w => existingPrenomParts.Contains(w));
                if (commonPrenomWords > 0)
                {
                    score += (commonPrenomWords * 15); // +15 points par mot commun
                }

                // 4. Si le score dépasse 80 → Doublon très probable
                if (score >= 80 && score > bestMatch.score)
                {
                    bestMatch = (true, existingPatient, score);
                }
            }

            return bestMatch;
        }

        /// <summary>
        /// Récupère les métadonnées d'un patient par ID
        /// </summary>
        public PatientMetadata? GetMetadata(string id)
        {
            var entry = _index.FirstOrDefault(p => p.Id == id);
            if (entry == null)
                return null;

            // Chercher d'abord dans le nouveau chemin info_patient/patient.json
            string patientJsonPath;
            if (_pathService != null)
            {
                var nomComplet = $"{entry.Prenom} {entry.Nom}";
                patientJsonPath = _pathService.GetPatientJsonPath(nomComplet);
                
                // Si pas trouvé, fallback sur l'ancien chemin
                if (!File.Exists(patientJsonPath))
                {
                    patientJsonPath = Path.Combine(entry.DirectoryPath, "patient.json");
                }
            }
            else
            {
                // Fallback : ancienne structure à la racine
                patientJsonPath = Path.Combine(entry.DirectoryPath, "patient.json");
            }
            
            // Si le fichier patient.json existe, le charger
            if (File.Exists(patientJsonPath))
            {
                try
                {
                    var json = File.ReadAllText(patientJsonPath);
                    return JsonSerializer.Deserialize<PatientMetadata>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch
                {
                    // Si erreur de lecture, fallback sur l'entrée d'index
                }
            }
            
            // Si pas de fichier JSON OU erreur de lecture → Construire depuis l'index
            return new PatientMetadata
            {
                Prenom = entry.Prenom,
                Nom = entry.Nom,
                Dob = entry.Dob,
                Sexe = entry.Sexe
            };
        }

        /// <summary>
        /// Active le monitoring des changements de fichiers
        /// </summary>
        public void StartWatching()
        {
            if (_watcher != null)
                return;

            _watcher = new FileSystemWatcher(_patientsRoot)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                Filter = "patient.json",
                IncludeSubdirectories = true,
                EnableRaisingEvents = true
            };

            _watcher.Changed += async (s, e) => await ScanAsync();
            _watcher.Created += async (s, e) => await ScanAsync();
            _watcher.Deleted += async (s, e) => await ScanAsync();
            _watcher.Renamed += async (s, e) => await ScanAsync();
        }

        /// <summary>
        /// Arrête le monitoring
        /// </summary>
        public void StopWatching()
        {
            _watcher?.Dispose();
            _watcher = null;
        }

        /// <summary>
        /// Normalise une chaîne pour la recherche (supprime les accents et met en minuscules)
        /// </summary>
        private string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            var normalized = text.Normalize(NormalizationForm.FormD);
            var result = new StringBuilder();

            foreach (var c in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category != UnicodeCategory.NonSpacingMark)
                {
                    result.Append(c);
                }
            }

            return result.ToString().Normalize(NormalizationForm.FormC).ToLowerInvariant();
        }

        /// <summary>
        /// Récupère tous les patients (pour la liste complète)
        /// </summary>
        public List<PatientIndexEntry> GetAllPatients()
        {
            return _index.OrderBy(p => p.Nom).ThenBy(p => p.Prenom).ToList();
        }
        
        /// <summary>
        /// Récupère la date de création du dossier patient
        /// </summary>
        public DateTime GetCreationDate(string patientId)
        {
            var entry = _index.FirstOrDefault(p => p.Id == patientId);
            if (entry == null)
                return DateTime.MinValue;
            
            try
            {
                return Directory.GetCreationTime(entry.DirectoryPath);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }
        
        /// <summary>
        /// Récupère la date de la dernière consultation (note la plus récente)
        /// </summary>
        public DateTime? GetLastConsultationDate(string patientId)
        {
            var notes = GetPatientNotes(patientId);
            return notes.Count > 0 ? notes.Max(n => n.date) : null;
        }
        
        /// <summary>
        /// Supprime un patient et tout son dossier
        /// </summary>
        public (bool success, string message) DeletePatient(string patientId)
        {
            try
            {
                var entry = _index.FirstOrDefault(p => p.Id == patientId);
                if (entry == null)
                {
                    return (false, "Patient introuvable");
                }
                
                // Supprimer le dossier complet
                if (Directory.Exists(entry.DirectoryPath))
                {
                    Directory.Delete(entry.DirectoryPath, recursive: true);
                }
                
                // Retirer de l'index
                _index.Remove(entry);
                
                return (true, $"Dossier de {entry.NomComplet} supprimé avec succès");
            }
            catch (Exception ex)
            {
                return (false, $"Erreur suppression : {ex.Message}");
            }
        }
        
        /// <summary>
        /// Récupère toutes les notes d'un patient triées par date (décroissante)
        /// </summary>
        public List<(string filePath, DateTime date, string preview)> GetPatientNotes(string patientId)
        {
            var entry = _index.FirstOrDefault(p => p.Id == patientId);
            if (entry == null)
                return new List<(string, DateTime, string)>();

            var notes = new List<(string filePath, DateTime date, string preview)>();

            try
            {
                // Si PathService est disponible, utiliser la nouvelle structure /notes/
                if (_pathService != null)
                {
                    // Convertir l'ID en nom complet pour PathService
                    var patientName = entry.NomComplet;
                    var notesFolder = _pathService.GetNotesDirectory(patientName);
                    if (Directory.Exists(notesFolder))
                    {
                        foreach (var mdFile in Directory.GetFiles(notesFolder, "*.md"))
                        {
                            AddNoteToList(mdFile, notes);
                        }
                    }
                }
                else
                {
                    // Fallback : ancienne structure /2025/*.md pour compatibilité
                    foreach (var yearDir in Directory.GetDirectories(entry.DirectoryPath).Where(d => int.TryParse(Path.GetFileName(d), out _)))
                    {
                        foreach (var mdFile in Directory.GetFiles(yearDir, "*.md"))
                        {
                            AddNoteToList(mdFile, notes);
                        }
                    }
                }
            }
            catch
            {
                // Ignorer les erreurs de lecture
            }

            return notes.OrderByDescending(n => n.date).ToList();
        }

        /// <summary>
        /// Méthode helper pour extraire les informations d'une note et l'ajouter à la liste
        /// </summary>
        private void AddNoteToList(string mdFile, List<(string filePath, DateTime date, string preview)> notes)
        {
            try
            {
                var content = File.ReadAllText(mdFile);
                var lines = content.Split('\n');
                
                // Extraire la date depuis le nom du fichier (format: YYYY-MM-DD_HHmm_...)
                var fileName = Path.GetFileNameWithoutExtension(mdFile);
                var parts = fileName.Split('_');
                
                // Extraire date + heure (2 premières parties : "2025-10-24" + "1448")
                var datePart = parts.Length >= 2 
                    ? $"{parts[0]}_{parts[1]}"  // "2025-10-24_1448"
                    : parts[0];  // Fallback si format ancien
                
                // Parser avec format personnalisé pour supporter "yyyy-MM-dd_HHmm"
                DateTime fileDate;
                if (DateTime.TryParseExact(datePart, "yyyy-MM-dd_HHmm", 
                    System.Globalization.CultureInfo.InvariantCulture, 
                    System.Globalization.DateTimeStyles.None, out fileDate))
                {
                    // Parsing réussi avec heure/minutes
                }
                else if (DateTime.TryParse(parts[0], out fileDate))
                {
                    // Fallback : parsing simple de la date seule (ancien format)
                }
                else
                {
                    return; // Skip ce fichier si impossible à parser
                }
                
                // Extraire un aperçu (première ligne non-YAML)
                var preview = "Note structurée";
                bool inYaml = false;
                foreach (var line in lines)
                {
                    if (line.Trim() == "---")
                    {
                        inYaml = !inYaml;
                        continue;
                    }
                    if (!inYaml && !string.IsNullOrWhiteSpace(line))
                    {
                        preview = line.Trim();
                        if (preview.Length > 60)
                            preview = preview.Substring(0, 60) + "...";
                        break;
                    }
                }
                
                notes.Add((mdFile, fileDate, preview));
            }
            catch
            {
                // Ignorer les fichiers problématiques
            }
        }
        
        /// <summary>
        /// Charge la liste des patients récents depuis le fichier JSON
        /// </summary>
        private void LoadRecentPatients()
        {
            try
            {
                if (File.Exists(_recentPatientsPath))
                {
                    var json = File.ReadAllText(_recentPatientsPath);
                    _recentPatientIds = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
                }
            }
            catch
            {
                _recentPatientIds = new List<string>();
            }
        }
        
        /// <summary>
        /// Sauvegarde la liste des patients récents dans le fichier JSON
        /// </summary>
        private void SaveRecentPatients()
        {
            try
            {
                var json = JsonSerializer.Serialize(_recentPatientIds, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_recentPatientsPath, json, Encoding.UTF8);
            }
            catch
            {
                // Ignorer les erreurs de sauvegarde
            }
        }
        
        /// <summary>
        /// Ajoute un patient à la liste des récents (FIFO - max 5)
        /// </summary>
        public void AddRecentPatient(string patientId)
        {
            if (string.IsNullOrEmpty(patientId))
                return;
            
            // Retirer le patient s'il existe déjà (pour le remettre en premier)
            _recentPatientIds.Remove(patientId);
            
            // Ajouter en premier
            _recentPatientIds.Insert(0, patientId);
            
            // Limiter à MaxRecentPatients
            if (_recentPatientIds.Count > MaxRecentPatients)
            {
                _recentPatientIds = _recentPatientIds.Take(MaxRecentPatients).ToList();
            }
            
            // Sauvegarder
            SaveRecentPatients();
        }
        
        /// <summary>
        /// Récupère la liste des patients récents (max 20, dans l'ordre)
        /// </summary>
        public List<PatientIndexEntry> GetRecentPatients()
        {
            var recentPatients = new List<PatientIndexEntry>();

            foreach (var patientId in _recentPatientIds)
            {
                var patient = _index.FirstOrDefault(p => p.Id == patientId);
                if (patient != null)
                {
                    recentPatients.Add(patient);
                }
            }

            return recentPatients;
        }

        /// <summary>
        /// Récupère l'ordre de récence d'un patient (0 = plus récent, -1 = pas dans l'historique)
        /// </summary>
        public int GetRecentOrder(string patientId)
        {
            var index = _recentPatientIds.IndexOf(patientId);
            return index; // 0 = plus récent, 1 = second plus récent, etc. -1 = pas dans l'historique
        }

        /// <summary>
        /// Vérifie si un patient a des doublons potentiels
        /// Retourne (hasDuplicates, duplicatePatient, similarityScore)
        /// </summary>
        public (bool hasDuplicates, PatientIndexEntry? duplicatePatient, int score) CheckForDuplicates(string patientId)
        {
            var metadata = GetMetadata(patientId);
            if (metadata == null)
                return (false, null, 0);

            return DetectDuplicates(metadata, patientId);
        }
    }
}
