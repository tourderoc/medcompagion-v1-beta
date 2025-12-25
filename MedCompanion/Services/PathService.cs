using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service centralisé de gestion des chemins de fichiers patients
    /// Garantit une structure cohérente et facilite la maintenance
    /// </summary>
    public class PathService
    {
        private readonly string _baseDirectory;

        public PathService()
        {
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _baseDirectory = Path.Combine(documentsPath, "MedCompanion", "patients");
        }

        /// <summary>
        /// Normalise le nom complet pour créer un nom de dossier valide
        /// Ex: "Yanis Dupont" -> "Dupont_Yanis"
        /// </summary>
        private string NormalizePatientName(string nomComplet)
        {
            var parts = nomComplet.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            
            if (parts.Length >= 2)
            {
                // Dernier mot = nom de famille, le reste = prénom
                var prenom = string.Join("_", parts.Take(parts.Length - 1));
                var nom = parts.Last();
                return $"{nom}_{prenom}";
            }
            
            // Si un seul mot, on l'utilise tel quel
            return parts[0].Replace(" ", "_");
        }

        /// <summary>
        /// Obtient le chemin du dossier racine d'un patient
        /// Ex: Documents/MedCompanion/patients/DUPONT_Yanis
        /// </summary>
        public string GetPatientRootDirectory(string nomComplet)
        {
            var normalizedName = NormalizePatientName(nomComplet);
            return Path.Combine(_baseDirectory, normalizedName);
        }

        /// <summary>
        /// Obtient le chemin du dossier d'une année spécifique pour un patient
        /// Ex: Documents/MedCompanion/patients/DUPONT_Yanis/2025
        /// </summary>
        public string GetPatientYearDirectory(string nomComplet, int? year = null)
        {
            var yearStr = (year ?? DateTime.Now.Year).ToString();
            return Path.Combine(GetPatientRootDirectory(nomComplet), yearStr);
        }

        /// <summary>
        /// Obtient le chemin du dossier des notes cliniques
        /// Ex: Documents/MedCompanion/patients/DUPONT_Yanis/2025/notes
        /// </summary>
        public string GetNotesDirectory(string nomComplet, int? year = null)
        {
            return Path.Combine(GetPatientYearDirectory(nomComplet, year), "notes");
        }

        /// <summary>
        /// Obtient le chemin du dossier des échanges chat
        /// Ex: Documents/MedCompanion/patients/DUPONT_Yanis/2025/chat
        /// </summary>
        public string GetChatDirectory(string nomComplet, int? year = null)
        {
            return Path.Combine(GetPatientYearDirectory(nomComplet, year), "chat");
        }

        /// <summary>
        /// Obtient le chemin du dossier des courriers
        /// Ex: Documents/MedCompanion/patients/DUPONT_Yanis/2025/courriers
        /// </summary>
        public string GetCourriersDirectory(string nomComplet, int? year = null)
        {
            return Path.Combine(GetPatientYearDirectory(nomComplet, year), "courriers");
        }

        /// <summary>
        /// Obtient le chemin du dossier des ordonnances
        /// Ex: Documents/MedCompanion/patients/DUPONT_Yanis/2025/ordonnances
        /// </summary>
        public string GetOrdonnancesDirectory(string nomComplet, int? year = null)
        {
            return Path.Combine(GetPatientYearDirectory(nomComplet, year), "ordonnances");
        }

        /// <summary>
        /// Obtient le chemin du dossier des attestations
        /// Ex: Documents/MedCompanion/patients/DUPONT_Yanis/2025/attestations
        /// </summary>
        public string GetAttestationsDirectory(string nomComplet, int? year = null)
        {
            return Path.Combine(GetPatientYearDirectory(nomComplet, year), "attestations");
        }

        /// <summary>
        /// Obtient le chemin du dossier des documents importés
        /// Ex: Documents/MedCompanion/patients/DUPONT_Yanis/2025/documents
        /// </summary>
        public string GetDocumentsDirectory(string nomComplet, int? year = null)
        {
            return Path.Combine(GetPatientYearDirectory(nomComplet, year), "documents");
        }

        /// <summary>
        /// Obtient le chemin du dossier des formulaires (PAI, MDPH, etc.)
        /// Ex: Documents/MedCompanion/patients/DUPONT_Yanis/2025/formulaires
        /// </summary>
        public string GetFormulairesDirectory(string nomComplet, int? year = null)
        {
            return Path.Combine(GetPatientYearDirectory(nomComplet, year), "formulaires");
        }

        /// <summary>
        /// Obtient le chemin du dossier des synthèses
        /// Ex: Documents/MedCompanion/patients/DUPONT_Yanis/synthese
        /// Note: La synthèse est transversale (couvre toutes les années), donc à la racine du patient
        /// </summary>
        public string GetSyntheseDirectory(string nomComplet)
        {
            return Path.Combine(GetPatientRootDirectory(nomComplet), "synthese");
        }

        /// <summary>
        /// Obtient le chemin du dossier des informations administratives du patient
        /// Ex: Documents/MedCompanion/patients/DUPONT_Yanis/info_patient
        /// Note: Dossier pour les données administratives (patient.json, adresse, contacts, etc.)
        /// </summary>
        public string GetInfoPatientDirectory(string nomComplet)
        {
            return Path.Combine(GetPatientRootDirectory(nomComplet), "info_patient");
        }

        /// <summary>
        /// Obtient le chemin complet du fichier patient.json
        /// Ex: Documents/MedCompanion/patients/DUPONT_Yanis/info_patient/patient.json
        /// </summary>
        public string GetPatientJsonPath(string nomComplet)
        {
            return Path.Combine(GetInfoPatientDirectory(nomComplet), "patient.json");
        }

        /// <summary>
        /// Crée un répertoire s'il n'existe pas déjà
        /// </summary>
        public void EnsureDirectoryExists(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        /// <summary>
        /// Crée toute la structure de dossiers pour un patient
        /// </summary>
        public void EnsurePatientStructure(string nomComplet, int? year = null)
        {
            // Créer le dossier année
            var yearDir = GetPatientYearDirectory(nomComplet, year);
            EnsureDirectoryExists(yearDir);

            // Créer tous les sous-dossiers
            EnsureDirectoryExists(GetNotesDirectory(nomComplet, year));
            EnsureDirectoryExists(GetChatDirectory(nomComplet, year));
            EnsureDirectoryExists(GetCourriersDirectory(nomComplet, year));
            EnsureDirectoryExists(GetOrdonnancesDirectory(nomComplet, year));
            EnsureDirectoryExists(GetAttestationsDirectory(nomComplet, year));
            EnsureDirectoryExists(GetDocumentsDirectory(nomComplet, year));
            EnsureDirectoryExists(GetFormulairesDirectory(nomComplet, year));

            // Créer les sous-dossiers de documents
            var documentsDir = GetDocumentsDirectory(nomComplet, year);
            EnsureDirectoryExists(Path.Combine(documentsDir, "bilans"));
            EnsureDirectoryExists(Path.Combine(documentsDir, "courriers"));
            EnsureDirectoryExists(Path.Combine(documentsDir, "ordonnances"));
            EnsureDirectoryExists(Path.Combine(documentsDir, "radiologies"));
            EnsureDirectoryExists(Path.Combine(documentsDir, "analyses"));
            EnsureDirectoryExists(Path.Combine(documentsDir, "autres"));

            // Créer le dossier de synthèse (à la racine du patient, pas dans l'année)
            EnsureDirectoryExists(GetSyntheseDirectory(nomComplet));

            // Créer le dossier info_patient (à la racine du patient)
            EnsureDirectoryExists(GetInfoPatientDirectory(nomComplet));
        }

        /// <summary>
        /// Récupère la liste de tous les patients
        /// </summary>
        public List<string> GetAllPatients()
        {
            if (!Directory.Exists(_baseDirectory))
                return new List<string>();

            try
            {
                return Directory.GetDirectories(_baseDirectory)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .ToList()!;
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Récupère les années disponibles pour un patient
        /// </summary>
        public List<int> GetAvailableYears(string nomComplet)
        {
            var patientRoot = GetPatientRootDirectory(nomComplet);
            
            if (!Directory.Exists(patientRoot))
                return new List<int>();

            try
            {
                return Directory.GetDirectories(patientRoot)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrEmpty(name) && int.TryParse(name, out _))
                    .Select(name => int.Parse(name!))
                    .OrderByDescending(year => year)
                    .ToList();
            }
            catch
            {
                return new List<int>();
            }
        }

        /// <summary>
        /// Vérifie si un patient existe
        /// </summary>
        public bool PatientExists(string nomComplet)
        {
            var patientRoot = GetPatientRootDirectory(nomComplet);
            return Directory.Exists(patientRoot);
        }

        /// <summary>
        /// Obtient le chemin de base des données d'application (AppData/Roaming/MedCompanion)
        /// </summary>
        public string GetAppDataPath()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "MedCompanion");
        }

        /// <summary>
        /// Obtient le chemin de base de tous les patients
        /// </summary>
        public string GetBasePatientsDirectory()
        {
            return _baseDirectory;
        }
    }
}
