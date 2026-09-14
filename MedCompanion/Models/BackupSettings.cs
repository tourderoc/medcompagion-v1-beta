using System;
using System.IO;
using System.Text.Json;

namespace MedCompanion.Models
{
    /// <summary>
    /// Réglages du service de sauvegarde.
    ///
    /// Stockés dans leur propre fichier et non dans appsettings.json : le service écrit
    /// <see cref="LastRunUtc"/> en arrière-plan toutes les heures, et appsettings.json est réécrit
    /// en entier à chaque validation de la fenêtre Paramètres. Les mêler ferait perdre une écriture
    /// sur deux dès que les deux se produisent en même temps.
    /// </summary>
    public class BackupSettings
    {
        /// <summary>Interrupteur général. Faux tant que l'utilisateur n'a pas choisi une destination.</summary>
        public bool Enabled { get; set; } = false;

        /// <summary>Dossier racine des sauvegardes, ex. E:\Sauvegardes\MedCompanion.</summary>
        public string DestinationRoot { get; set; } = "";

        /// <summary>
        /// Nom de volume attendu pour le disque de destination. Vide = pas de contrôle.
        /// Renseigné, il empêche d'écrire sur un disque qui aurait récupéré la lettre
        /// du disque de sauvegarde (une clé USB branchée après coup, par exemple).
        /// </summary>
        public string ExpectedVolumeLabel { get; set; } = "";

        /// <summary>Délai minimum entre deux sauvegardes automatiques. 0 = uniquement au démarrage.</summary>
        public int IntervalMinutes { get; set; } = 60;

        /// <summary>Sauvegarde peu après l'ouverture de MedCompanion.</summary>
        public bool RunAtStartup { get; set; } = true;

        /// <summary>Dernière sauvegarde à la fermeture, pour ne pas perdre la fin de journée.</summary>
        public bool RunAtClose { get; set; } = true;

        /// <summary>
        /// Durée de conservation des versions précédentes d'un fichier écrasé.
        /// C'est ce qui distingue une sauvegarde d'une simple copie : si un fichier est abîmé
        /// à la source, sa dernière version correcte reste récupérable pendant ce délai.
        /// </summary>
        public int KeepVersionsDays { get; set; } = 60;

        /// <summary>
        /// On ignore les fichiers modifiés dans les toutes dernières secondes : ils ont des chances
        /// d'être encore en cours d'écriture par MedCompanion, et on récupérerait un document tronqué.
        /// Ils seront pris à la sauvegarde suivante.
        /// </summary>
        public int IgnoreFilesModifiedWithinSeconds { get; set; } = 10;

        /// <summary>Horodatage UTC de la dernière sauvegarde réussie. Écrit par le service.</summary>
        public DateTime? LastRunUtc { get; set; }

        /// <summary>Résumé lisible de la dernière sauvegarde, affiché dans les Paramètres.</summary>
        public string LastRunSummary { get; set; } = "";

        // ===== PERSISTANCE =====

        private static readonly string SettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MedCompanion",
            "backup_settings.json"
        );

        public static BackupSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    return JsonSerializer.Deserialize<BackupSettings>(json) ?? new BackupSettings();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BackupSettings] Erreur chargement : {ex.Message}");
            }
            return new BackupSettings();
        }

        public void Save()
        {
            var directory = Path.GetDirectoryName(SettingsFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
        }

        /// <summary>
        /// Enregistre les champs réglables par l'utilisateur en préservant l'horodatage de la
        /// dernière sauvegarde, que le service a pu écrire pendant que la fenêtre était ouverte.
        /// </summary>
        public void SaveUserFields()
        {
            var surDisque = Load();
            LastRunUtc = surDisque.LastRunUtc;
            LastRunSummary = surDisque.LastRunSummary;
            Save();
        }

        /// <summary>
        /// Relit le fichier, y inscrit le résultat de la sauvegarde qui vient de se terminer et
        /// réenregistre — sans écraser un réglage que l'utilisateur aurait changé entre-temps.
        /// </summary>
        public static void MarkRun(DateTime quandUtc, string resume)
        {
            try
            {
                var s = Load();
                s.LastRunUtc = quandUtc;
                s.LastRunSummary = resume;
                s.Save();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BackupSettings] Erreur MarkRun : {ex.Message}");
            }
        }

        /// <summary>Vrai si une sauvegarde automatique est due maintenant.</summary>
        public bool IsDue(DateTime maintenantUtc)
        {
            if (!Enabled || string.IsNullOrWhiteSpace(DestinationRoot)) return false;
            if (LastRunUtc == null) return true;
            if (IntervalMinutes <= 0) return false;
            return (maintenantUtc - LastRunUtc.Value).TotalMinutes >= IntervalMinutes;
        }
    }
}
