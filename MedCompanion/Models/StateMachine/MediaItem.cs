using System;
using System.IO;
using System.Text.Json.Serialization;

namespace MedCompanion.Models.StateMachine
{
    /// <summary>
    /// Représente un fichier vidéo MP4 dans une séquence d'animation
    /// </summary>
    public class MediaItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>
        /// Chemin vers le fichier MP4
        /// </summary>
        public string FilePath { get; set; } = "";

        /// <summary>
        /// Nom d'affichage pour l'UI
        /// </summary>
        public string DisplayName { get; set; } = "";

        /// <summary>
        /// Durée de la vidéo (auto-détectée ou manuelle)
        /// </summary>
        public TimeSpan Duration { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// Jouer en boucle
        /// </summary>
        public bool Loop { get; set; } = true;

        /// <summary>
        /// Vitesse de lecture (1.0 = normale)
        /// </summary>
        public double Speed { get; set; } = 1.0;

        [JsonIgnore]
        public bool IsValid => !string.IsNullOrWhiteSpace(FilePath) &&
                               Path.GetExtension(FilePath).Equals(".mp4", StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public bool FileExists => IsValid && File.Exists(FilePath);

        [JsonIgnore]
        public string FileName => string.IsNullOrEmpty(FilePath) ? "" : Path.GetFileName(FilePath);
    }
}
