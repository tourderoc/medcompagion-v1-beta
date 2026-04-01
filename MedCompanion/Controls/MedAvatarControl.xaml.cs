using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MedCompanion.Models.StateMachine;
using MedCompanion.Services;

namespace MedCompanion.Controls
{
    /// <summary>
    /// Contrôle d'avatar Med avec support MP4 et fallback WPF
    /// </summary>
    public partial class MedAvatarControl : UserControl
    {
        private readonly MedAvatarService _avatarService;
        private Storyboard? _thinkingStoryboard;
        private Storyboard? _speakingStoryboard;
        private bool _isVideoLoaded;
        private string? _currentVideoPath;

        #region Dependency Properties

        public static readonly DependencyProperty AvatarSizeProperty =
            DependencyProperty.Register(nameof(AvatarSize), typeof(double), typeof(MedAvatarControl),
                new PropertyMetadata(120.0));

        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(MedAvatarControl),
                new PropertyMetadata(new CornerRadius(60)));

        public double AvatarSize
        {
            get => (double)GetValue(AvatarSizeProperty);
            set => SetValue(AvatarSizeProperty, value);
        }

        public CornerRadius CornerRadius
        {
            get => (CornerRadius)GetValue(CornerRadiusProperty);
            set => SetValue(CornerRadiusProperty, value);
        }

        #endregion

        public MedAvatarControl()
        {
            InitializeComponent();

            _avatarService = MedAvatarService.Instance;

            // S'abonner aux changements d'état
            _avatarService.StateChanged += OnStateChanged;
            _avatarService.PropertyChanged += OnServicePropertyChanged;
            _avatarService.MediaRequested += OnMediaRequested;

            Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Charger les storyboards
                _thinkingStoryboard = (Storyboard)FindResource("ThinkingAnimation");
                _speakingStoryboard = (Storyboard)FindResource("SpeakingAnimation");

                // Charger la taille depuis la config
                AvatarSize = _avatarService.AvatarSize;
                CornerRadius = new CornerRadius(AvatarSize / 2);

                // Essayer de charger la vidéo pour l'état actuel
                TryLoadMediaForState(_avatarService.CurrentState);

                // Appliquer l'état actuel
                UpdateStateVisuals(_avatarService.CurrentState);

                System.Diagnostics.Debug.WriteLine($"[MedAvatarControl] Loaded - State: {_avatarService.CurrentStateName}, Size: {AvatarSize}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MedAvatarControl] Erreur OnLoaded: {ex.Message}");
            }
        }

        private void OnStateChanged(object? sender, AvatarState newState)
        {
            Dispatcher.Invoke(() =>
            {
                TryLoadMediaForState(newState);
                UpdateStateVisuals(newState);
            });
        }

        private void OnServicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MedAvatarService.AvatarSize))
            {
                Dispatcher.Invoke(() =>
                {
                    AvatarSize = _avatarService.AvatarSize;
                    CornerRadius = new CornerRadius(AvatarSize / 2);
                });
            }
        }

        private void OnMediaRequested(object? sender, MediaItem media)
        {
            Dispatcher.Invoke(() =>
            {
                LoadVideoAnimation(media.FilePath, media.Loop);
            });
        }

        /// <summary>
        /// Essaie de charger le média pour un état donné
        /// </summary>
        private void TryLoadMediaForState(AvatarState? state)
        {
            if (state == null)
            {
                ShowFallback();
                return;
            }

            try
            {
                var media = state.MediaSequence.FirstOrDefault(m => m.FileExists);
                if (media != null)
                {
                    LoadVideoAnimation(media.FilePath, media.Loop);
                }
                else
                {
                    ShowFallback();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MedAvatarControl] Erreur chargement média: {ex.Message}");
                ShowFallback();
            }
        }

        /// <summary>
        /// Charge et joue une animation Video MP4
        /// </summary>
        private void LoadVideoAnimation(string filePath, bool loop)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    ShowFallback();
                    return;
                }

                // Éviter de recharger la même vidéo
                if (_currentVideoPath == filePath && _isVideoLoaded && VideoPlayer.Visibility == Visibility.Visible)
                {
                    return;
                }

                // Masquer Fallback
                FallbackContainer.Visibility = Visibility.Collapsed;

                // Configurer VideoPlayer
                VideoPlayer.Visibility = Visibility.Visible;
                VideoPlayer.Source = new Uri(filePath);

                // Gérer la boucle
                VideoPlayer.MediaEnded -= OnVideoEnded;
                if (loop)
                {
                    VideoPlayer.MediaEnded += OnVideoEnded;
                }

                VideoPlayer.Play();
                _isVideoLoaded = true;
                _currentVideoPath = filePath;

                System.Diagnostics.Debug.WriteLine($"[MedAvatarControl] Vidéo chargée: {filePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MedAvatarControl] Erreur LoadVideo: {ex.Message}");
                ShowFallback();
            }
        }

        private void OnVideoEnded(object sender, RoutedEventArgs e)
        {
            VideoPlayer.Position = TimeSpan.Zero;
            VideoPlayer.Play();
        }

        /// <summary>
        /// Affiche le fallback (animation WPF)
        /// </summary>
        private void ShowFallback()
        {
            VideoPlayer.Visibility = Visibility.Collapsed;
            VideoPlayer.Stop();
            FallbackContainer.Visibility = Visibility.Visible;
            _isVideoLoaded = false;
            _currentVideoPath = null;
        }

        /// <summary>
        /// Met à jour les visuels selon l'état
        /// </summary>
        private void UpdateStateVisuals(AvatarState? state)
        {
            StopFallbackAnimations();

            if (_isVideoLoaded)
            {
                UpdateStateBadge(state?.Name ?? "Idle");
                return;
            }

            var stateName = state?.Name ?? "Idle";

            // Fallback: animations WPF natives
            switch (stateName)
            {
                case "Idle":
                    StateLabel.Text = "En attente";
                    IconText.Text = "M";
                    IconText.Foreground = new SolidColorBrush(Color.FromRgb(0x19, 0x76, 0xD2));
                    GradientStart.Color = Color.FromRgb(0xE3, 0xF2, 0xFD);
                    GradientEnd.Color = Color.FromRgb(0xBB, 0xDE, 0xFB);
                    CircleStroke.Color = Color.FromRgb(0x90, 0xCA, 0xF9);
                    StateBadge.Background = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                    break;

                case "Thinking":
                    StateLabel.Text = "Réflexion...";
                    IconText.Text = "?";
                    IconText.Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0x7C, 0x00));
                    GradientStart.Color = Color.FromRgb(0xFF, 0xF3, 0xE0);
                    GradientEnd.Color = Color.FromRgb(0xFF, 0xE0, 0xB2);
                    CircleStroke.Color = Color.FromRgb(0xFF, 0xB7, 0x4D);
                    StateBadge.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                    _thinkingStoryboard?.Begin(this, true);
                    break;

                case "Speaking":
                    StateLabel.Text = "Parle...";
                    IconText.Text = "M";
                    IconText.Foreground = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));
                    GradientStart.Color = Color.FromRgb(0xE3, 0xF2, 0xFD);
                    GradientEnd.Color = Color.FromRgb(0x90, 0xCA, 0xF9);
                    CircleStroke.Color = Color.FromRgb(0x21, 0x96, 0xF3);
                    StateBadge.Background = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));
                    _speakingStoryboard?.Begin(this, true);
                    break;

                default:
                    StateLabel.Text = state?.Description ?? stateName;
                    IconText.Text = stateName.Length > 0 ? stateName[0].ToString().ToUpper() : "?";
                    IconText.Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x7D, 0x8B));
                    GradientStart.Color = Color.FromRgb(0xEC, 0xEF, 0xF1);
                    GradientEnd.Color = Color.FromRgb(0xCF, 0xD8, 0xDC);
                    CircleStroke.Color = Color.FromRgb(0x90, 0xA4, 0xAE);
                    StateBadge.Background = new SolidColorBrush(Color.FromRgb(0x78, 0x90, 0x9C));
                    break;
            }
        }

        /// <summary>
        /// Met à jour seulement le badge d'état
        /// </summary>
        private void UpdateStateBadge(string stateName)
        {
            switch (stateName)
            {
                case "Idle":
                    StateBadge.Background = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
                    break;
                case "Thinking":
                    StateBadge.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0x98, 0x00));
                    break;
                case "Speaking":
                    StateBadge.Background = new SolidColorBrush(Color.FromRgb(0x21, 0x96, 0xF3));
                    break;
                default:
                    StateBadge.Background = new SolidColorBrush(Color.FromRgb(0x78, 0x90, 0x9C));
                    break;
            }
        }

        /// <summary>
        /// Arrête les animations WPF
        /// </summary>
        private void StopFallbackAnimations()
        {
            try
            {
                _thinkingStoryboard?.Stop(this);
                _speakingStoryboard?.Stop(this);

                PulseCircle.Opacity = 1.0;
                SpeakingRing.Opacity = 0;
            }
            catch
            {
                // Ignorer
            }
        }

        /// <summary>
        /// Force le rechargement
        /// </summary>
        public void Refresh()
        {
            _currentVideoPath = null;
            _isVideoLoaded = false;
            TryLoadMediaForState(_avatarService.CurrentState);
            UpdateStateVisuals(_avatarService.CurrentState);
        }

        /// <summary>
        /// Arrête l'animation
        /// </summary>
        public void Stop()
        {
            StopFallbackAnimations();
            try
            {
                if (_isVideoLoaded)
                {
                    VideoPlayer.Stop();
                }
            }
            catch { }
        }
    }
}
