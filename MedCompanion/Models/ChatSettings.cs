using System;
using System.IO;
using System.Text.Json;

namespace MedCompanion.Models
{
    /// <summary>
    /// Paramètres de configuration pour la mémoire intelligente du Chat
    /// </summary>
    public class ChatSettings
    {
        // ===== PARAMÈTRES DE COMPACTION =====

        /// <summary>
        /// Seuil de caractères pour déclencher la compaction (défaut: 20000)
        /// </summary>
        public int CompactionThreshold { get; set; } = 20000;

        /// <summary>
        /// Activer ou désactiver la compaction automatique (défaut: true)
        /// </summary>
        public bool EnableCompaction { get; set; } = true;

        // ===== MÉTHODES DE PERSISTANCE =====

        private static readonly string SettingsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MedCompanion",
            "chat_settings.json"
        );

        /// <summary>
        /// Charge les paramètres depuis le fichier JSON (ou retourne les valeurs par défaut)
        /// </summary>
        public static ChatSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<ChatSettings>(json);
                    return settings ?? new ChatSettings();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChatSettings] Erreur chargement: {ex.Message}");
            }

            return new ChatSettings();
        }

        /// <summary>
        /// Sauvegarde les paramètres dans le fichier JSON
        /// </summary>
        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsFilePath);
                if (directory != null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(SettingsFilePath, json);
                System.Diagnostics.Debug.WriteLine($"[ChatSettings] Sauvegardé: Threshold={CompactionThreshold}, Enabled={EnableCompaction}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChatSettings] Erreur sauvegarde: {ex.Message}");
            }
        }

        /// <summary>
        /// Valide et corrige les paramètres si nécessaire
        /// </summary>
        public void Validate()
        {
            // Limiter le seuil entre 5000 et 100000
            if (CompactionThreshold < 5000) CompactionThreshold = 5000;
            if (CompactionThreshold > 100000) CompactionThreshold = 100000;
        }
    }
}
