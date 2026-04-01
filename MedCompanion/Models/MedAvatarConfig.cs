using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MedCompanion.Models.StateMachine;

namespace MedCompanion.Models
{
    /// <summary>
    /// Configuration complète de l'avatar Med
    /// Utilise StateMachineProfile pour gérer les états et transitions
    /// </summary>
    public class MedAvatarConfig
    {
        /// <summary>Version du fichier de config</summary>
        public int Version { get; set; } = 2;

        /// <summary>Avatar activé ou non</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Taille de l'avatar en pixels (largeur = hauteur)</summary>
        public int AvatarSize { get; set; } = 120;

        /// <summary>
        /// Chemin par défaut du fichier de configuration
        /// </summary>
        public static string DefaultConfigPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "MedCompanion",
            "config",
            "avatar_config.json"
        );

        /// <summary>
        /// Chemin du profil State Machine
        /// </summary>
        public static string DefaultProfilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "MedCompanion",
            "config",
            "avatar_profile.json"
        );

        /// <summary>
        /// Dossier par défaut pour les fichiers vidéo MP4
        /// </summary>
        public static string DefaultMediaFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "MedCompanion",
            "config",
            "avatars"
        );

        /// <summary>
        /// Charge la configuration depuis le fichier
        /// </summary>
        public static MedAvatarConfig Load()
        {
            try
            {
                if (File.Exists(DefaultConfigPath))
                {
                    var json = File.ReadAllText(DefaultConfigPath);
                    var config = JsonSerializer.Deserialize<MedAvatarConfig>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        Converters = { new JsonStringEnumConverter() }
                    });
                    return config ?? new MedAvatarConfig();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MedAvatarConfig] Erreur chargement: {ex.Message}");
            }

            return new MedAvatarConfig();
        }

        /// <summary>
        /// Sauvegarde la configuration
        /// </summary>
        public bool Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(DefaultConfigPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Converters = { new JsonStringEnumConverter() }
                });

                File.WriteAllText(DefaultConfigPath, json);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MedAvatarConfig] Erreur sauvegarde: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// S'assure que le dossier des médias existe
        /// </summary>
        public static void EnsureMediaFolderExists()
        {
            if (!Directory.Exists(DefaultMediaFolder))
            {
                Directory.CreateDirectory(DefaultMediaFolder);
            }
        }

        /// <summary>
        /// Charge ou crée le profil State Machine par défaut
        /// </summary>
        public static StateMachineProfile LoadOrCreateProfile()
        {
            EnsureMediaFolderExists();

            if (File.Exists(DefaultProfilePath))
            {
                var profile = StateMachineProfile.Load(DefaultProfilePath);
                if (profile != null)
                {
                    return profile;
                }
            }

            // Créer un profil par défaut avec 3 états
            var defaultProfile = CreateDefaultProfile();
            defaultProfile.Save(DefaultProfilePath);
            return defaultProfile;
        }

        /// <summary>
        /// Crée un profil par défaut avec les états Idle, Thinking, Speaking
        /// </summary>
        public static StateMachineProfile CreateDefaultProfile()
        {
            var profile = new StateMachineProfile { Name = "Default Avatar Profile" };

            // Créer les 3 états de base
            var idle = new AvatarState
            {
                Name = "Idle",
                Description = "État de repos - En attente",
                IsLooping = true,
                GraphX = 100,
                GraphY = 200
            };

            var thinking = new AvatarState
            {
                Name = "Thinking",
                Description = "État de réflexion - Traitement en cours",
                IsLooping = true,
                GraphX = 400,
                GraphY = 100
            };

            var speaking = new AvatarState
            {
                Name = "Speaking",
                Description = "État de parole - Génération de texte",
                IsLooping = true,
                GraphX = 400,
                GraphY = 300
            };

            profile.States.Add(idle);
            profile.States.Add(thinking);
            profile.States.Add(speaking);
            profile.InitialStateId = idle.Id;

            // Créer les transitions
            // Idle -> Thinking
            profile.Transitions.Add(new AvatarTransition
            {
                SourceStateId = idle.Id,
                TargetStateId = thinking.Id,
                Trigger = "StartThinking"
            });

            // Idle -> Speaking
            profile.Transitions.Add(new AvatarTransition
            {
                SourceStateId = idle.Id,
                TargetStateId = speaking.Id,
                Trigger = "StartSpeaking"
            });

            // Thinking -> Idle
            profile.Transitions.Add(new AvatarTransition
            {
                SourceStateId = thinking.Id,
                TargetStateId = idle.Id,
                Trigger = "GoIdle"
            });

            // Thinking -> Speaking
            profile.Transitions.Add(new AvatarTransition
            {
                SourceStateId = thinking.Id,
                TargetStateId = speaking.Id,
                Trigger = "StartSpeaking"
            });

            // Speaking -> Idle
            profile.Transitions.Add(new AvatarTransition
            {
                SourceStateId = speaking.Id,
                TargetStateId = idle.Id,
                Trigger = "GoIdle"
            });

            // Speaking -> Thinking
            profile.Transitions.Add(new AvatarTransition
            {
                SourceStateId = speaking.Id,
                TargetStateId = thinking.Id,
                Trigger = "StartThinking"
            });

            return profile;
        }

        /// <summary>
        /// Importe un fichier MP4 dans le dossier des médias
        /// </summary>
        public static (bool success, string targetPath, string? error) ImportMediaFile(string sourcePath, string stateName)
        {
            try
            {
                if (!File.Exists(sourcePath))
                {
                    return (false, "", "Le fichier source n'existe pas");
                }

                var ext = Path.GetExtension(sourcePath).ToLower();
                if (ext != ".mp4")
                {
                    return (false, "", "Seuls les fichiers MP4 sont supportés");
                }

                EnsureMediaFolderExists();

                var fileName = $"{stateName.ToLower()}_{DateTime.Now:yyyyMMddHHmmss}.mp4";
                var targetPath = Path.Combine(DefaultMediaFolder, fileName);

                File.Copy(sourcePath, targetPath, overwrite: true);

                System.Diagnostics.Debug.WriteLine($"[MedAvatarConfig] Importé: {sourcePath} → {targetPath}");

                return (true, targetPath, null);
            }
            catch (Exception ex)
            {
                return (false, "", ex.Message);
            }
        }
    }
}
