using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using MedCompanion.Models;
using MedCompanion.Models.StateMachine;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service de gestion de l'avatar Med (State Machine unifiée)
    /// Gère les transitions d'état et les animations MP4
    /// </summary>
    public class MedAvatarService : INotifyPropertyChanged
    {
        private static MedAvatarService? _instance;
        public static MedAvatarService Instance => _instance ??= new MedAvatarService();

        private MedAvatarConfig _config;
        private readonly MedAvatarEngine _engine = new();
        private AvatarState? _currentState;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Déclenché quand l'état change (avec l'AvatarState complet)
        /// </summary>
        public event EventHandler<AvatarState>? StateChanged;

        /// <summary>
        /// Déclenché quand une vidéo MP4 doit être jouée
        /// </summary>
        public event EventHandler<MediaItem>? MediaRequested;

        /// <summary>
        /// Profil State Machine actif
        /// </summary>
        public StateMachineProfile CurrentProfile { get; private set; } = null!;

        /// <summary>
        /// État actuel de l'avatar
        /// </summary>
        public AvatarState? CurrentState
        {
            get => _currentState;
            private set
            {
                if (_currentState != value)
                {
                    var oldState = _currentState;
                    _currentState = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentStateName));
                    OnPropertyChanged(nameof(CurrentStateDisplayName));

                    if (value != null)
                    {
                        StateChanged?.Invoke(this, value);
                        System.Diagnostics.Debug.WriteLine($"[MedAvatar] État: {oldState?.Name ?? "null"} → {value.Name}");
                    }
                }
            }
        }

        /// <summary>
        /// Nom de l'état actuel
        /// </summary>
        public string CurrentStateName => CurrentState?.Name ?? "Unknown";

        /// <summary>
        /// Nom d'affichage de l'état actuel
        /// </summary>
        public string CurrentStateDisplayName => CurrentState?.Name switch
        {
            "Idle" => "En attente",
            "Thinking" => "Réflexion...",
            "Speaking" => "Med parle...",
            _ => CurrentState?.Description ?? "Inconnu"
        };

        /// <summary>
        /// Configuration de l'avatar (taille, activé, etc.)
        /// </summary>
        public MedAvatarConfig Config
        {
            get => _config;
            private set
            {
                _config = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsEnabled));
                OnPropertyChanged(nameof(AvatarSize));
            }
        }

        /// <summary>
        /// Avatar activé
        /// </summary>
        public bool IsEnabled
        {
            get => Config.Enabled;
            set
            {
                if (Config.Enabled != value)
                {
                    Config.Enabled = value;
                    Config.Save();
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Taille de l'avatar
        /// </summary>
        public int AvatarSize
        {
            get => Config.AvatarSize;
            set
            {
                if (Config.AvatarSize != value)
                {
                    Config.AvatarSize = value;
                    Config.Save();
                    OnPropertyChanged();
                }
            }
        }

        private MedAvatarService()
        {
            _config = MedAvatarConfig.Load();
            InitializeEngine();
        }

        private void InitializeEngine()
        {
            try
            {
                // Charger ou créer le profil
                CurrentProfile = MedAvatarConfig.LoadOrCreateProfile();

                // Configurer le moteur
                _engine.LoadProfile(CurrentProfile);
                _engine.StateChanged += OnEngineStateChanged;
                _engine.MediaChanged += OnEngineMediaChanged;

                // Récupérer l'état initial
                CurrentState = _engine.CurrentState;

                System.Diagnostics.Debug.WriteLine($"[MedAvatarService] Engine initialisé avec {CurrentProfile.States.Count} états");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MedAvatarService] Engine Init Failed: {ex.Message}");

                // Créer un profil par défaut en mémoire
                CurrentProfile = MedAvatarConfig.CreateDefaultProfile();
                _engine.LoadProfile(CurrentProfile);
            }
        }

        private void OnEngineStateChanged(object? sender, AvatarState newState)
        {
            CurrentState = newState;

            // Jouer le premier média de l'état
            if (newState.MediaSequence.Any())
            {
                var media = newState.MediaSequence.First();
                if (media.FileExists)
                {
                    MediaRequested?.Invoke(this, media);
                }
            }
        }

        private void OnEngineMediaChanged(object? sender, string mediaPath)
        {
            // Trouver le MediaItem correspondant
            if (CurrentState != null)
            {
                var media = CurrentState.MediaSequence.FirstOrDefault(m => m.FilePath == mediaPath);
                if (media != null)
                {
                    MediaRequested?.Invoke(this, media);
                }
            }
        }

        #region State Machine Triggers

        /// <summary>
        /// Déclenche un trigger sur la state machine
        /// </summary>
        public void FireTrigger(string triggerName)
        {
            _engine.FireTrigger(triggerName);
        }

        /// <summary>
        /// Passe à l'état Idle (repos)
        /// </summary>
        public void SetIdle()
        {
            _engine.FireTrigger("GoIdle");
        }

        /// <summary>
        /// Passe à l'état Thinking (réflexion)
        /// </summary>
        public void SetThinking()
        {
            _engine.FireTrigger("StartThinking");
        }

        /// <summary>
        /// Passe à l'état Speaking (parole)
        /// </summary>
        public void SetSpeaking()
        {
            _engine.FireTrigger("StartSpeaking");
        }

        /// <summary>
        /// Va directement à un état par son nom
        /// </summary>
        public void GoToState(string stateName)
        {
            var state = CurrentProfile.States.FirstOrDefault(s =>
                s.Name.Equals(stateName, StringComparison.OrdinalIgnoreCase));

            if (state != null)
            {
                _engine.ForceState(state);
            }
        }

        #endregion

        #region Profile Management

        /// <summary>
        /// Recharge le profil depuis le fichier
        /// </summary>
        public void ReloadProfile()
        {
            CurrentProfile = MedAvatarConfig.LoadOrCreateProfile();
            _engine.LoadProfile(CurrentProfile);
            OnPropertyChanged(nameof(CurrentProfile));
        }

        /// <summary>
        /// Sauvegarde le profil actuel
        /// </summary>
        public bool SaveProfile()
        {
            try
            {
                CurrentProfile.Save(MedAvatarConfig.DefaultProfilePath);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MedAvatarService] Save Profile Failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Recharge la configuration (taille, activé)
        /// </summary>
        public void ReloadConfig()
        {
            Config = MedAvatarConfig.Load();
        }

        /// <summary>
        /// Sauvegarde la configuration
        /// </summary>
        public bool SaveConfig()
        {
            return Config.Save();
        }

        #endregion

        #region Media Management

        /// <summary>
        /// Ajoute une vidéo MP4 à un état
        /// </summary>
        public (bool success, string? error) AddMediaToState(string stateName, string sourcePath, bool loop = true)
        {
            var state = CurrentProfile.States.FirstOrDefault(s =>
                s.Name.Equals(stateName, StringComparison.OrdinalIgnoreCase));

            if (state == null)
            {
                return (false, $"État '{stateName}' non trouvé");
            }

            // Importer le fichier
            var (success, targetPath, error) = MedAvatarConfig.ImportMediaFile(sourcePath, stateName);
            if (!success)
            {
                return (false, error);
            }

            // Ajouter le média à l'état
            var media = new MediaItem
            {
                FilePath = targetPath,
                DisplayName = Path.GetFileNameWithoutExtension(sourcePath),
                Loop = loop
            };

            state.MediaSequence.Add(media);
            SaveProfile();

            return (true, null);
        }

        /// <summary>
        /// Définit la vidéo MP4 pour un état (remplace les existantes)
        /// </summary>
        public (bool success, string? error) SetMediaForState(string stateName, string sourcePath, bool loop = true)
        {
            var state = CurrentProfile.States.FirstOrDefault(s =>
                s.Name.Equals(stateName, StringComparison.OrdinalIgnoreCase));

            if (state == null)
            {
                return (false, $"État '{stateName}' non trouvé");
            }

            // Importer le fichier
            var (success, targetPath, error) = MedAvatarConfig.ImportMediaFile(sourcePath, stateName);
            if (!success)
            {
                return (false, error);
            }

            // Remplacer la séquence média
            state.MediaSequence.Clear();
            state.MediaSequence.Add(new MediaItem
            {
                FilePath = targetPath,
                DisplayName = Path.GetFileNameWithoutExtension(sourcePath),
                Loop = loop
            });

            state.IsLooping = loop;
            SaveProfile();

            // Si c'est l'état actuel, déclencher le changement
            if (CurrentState?.Id == state.Id)
            {
                var media = state.MediaSequence.First();
                MediaRequested?.Invoke(this, media);
            }

            return (true, null);
        }

        /// <summary>
        /// Supprime un média d'un état
        /// </summary>
        public bool RemoveMediaFromState(string stateName, Guid mediaId)
        {
            var state = CurrentProfile.States.FirstOrDefault(s =>
                s.Name.Equals(stateName, StringComparison.OrdinalIgnoreCase));

            if (state == null) return false;

            var media = state.MediaSequence.FirstOrDefault(m => m.Id == mediaId);
            if (media == null) return false;

            // Supprimer le fichier s'il est dans notre dossier
            if (media.FileExists && media.FilePath.StartsWith(MedAvatarConfig.DefaultMediaFolder))
            {
                try { File.Delete(media.FilePath); } catch { }
            }

            state.MediaSequence.Remove(media);
            SaveProfile();
            return true;
        }

        /// <summary>
        /// Vérifie si un état a des médias configurés
        /// </summary>
        public bool HasMedia(string stateName)
        {
            var state = CurrentProfile.States.FirstOrDefault(s =>
                s.Name.Equals(stateName, StringComparison.OrdinalIgnoreCase));

            return state?.MediaSequence.Any(m => m.FileExists) ?? false;
        }

        /// <summary>
        /// Liste tous les fichiers MP4 disponibles dans le dossier
        /// </summary>
        public string[] GetAvailableMediaFiles()
        {
            MedAvatarConfig.EnsureMediaFolderExists();
            return Directory.GetFiles(MedAvatarConfig.DefaultMediaFolder, "*.mp4");
        }

        #endregion

        #region State Management

        /// <summary>
        /// Ajoute un nouvel état au profil
        /// </summary>
        public AvatarState AddState(string name, string description = "")
        {
            var state = new AvatarState
            {
                Name = name,
                Description = description,
                GraphX = 100 + (CurrentProfile.States.Count * 150),
                GraphY = 200
            };

            CurrentProfile.States.Add(state);
            SaveProfile();

            return state;
        }

        /// <summary>
        /// Supprime un état du profil
        /// </summary>
        public bool RemoveState(Guid stateId)
        {
            var state = CurrentProfile.States.FirstOrDefault(s => s.Id == stateId);
            if (state == null) return false;

            // Supprimer les transitions liées
            var linkedTransitions = CurrentProfile.Transitions
                .Where(t => t.SourceStateId == stateId || t.TargetStateId == stateId)
                .ToList();

            foreach (var t in linkedTransitions)
            {
                CurrentProfile.Transitions.Remove(t);
            }

            CurrentProfile.States.Remove(state);
            SaveProfile();

            return true;
        }

        /// <summary>
        /// Ajoute une transition entre deux états
        /// </summary>
        public AvatarTransition? AddTransition(Guid sourceId, Guid targetId, string trigger)
        {
            var source = CurrentProfile.States.FirstOrDefault(s => s.Id == sourceId);
            var target = CurrentProfile.States.FirstOrDefault(s => s.Id == targetId);

            if (source == null || target == null) return null;

            var transition = new AvatarTransition
            {
                SourceStateId = sourceId,
                TargetStateId = targetId,
                Trigger = trigger
            };

            CurrentProfile.Transitions.Add(transition);
            SaveProfile();

            return transition;
        }

        /// <summary>
        /// Supprime une transition
        /// </summary>
        public bool RemoveTransition(Guid transitionId)
        {
            var transition = CurrentProfile.Transitions.FirstOrDefault(t => t.Id == transitionId);
            if (transition == null) return false;

            CurrentProfile.Transitions.Remove(transition);
            SaveProfile();

            return true;
        }

        #endregion

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
