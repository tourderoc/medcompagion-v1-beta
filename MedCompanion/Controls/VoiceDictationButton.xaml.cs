using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using MedCompanion.Services.Voice;

namespace MedCompanion.Controls
{
    /// <summary>
    /// UserControl réutilisable pour la dictée vocale via Handy
    /// Affiche un bouton microphone avec animation pendant l'enregistrement
    /// </summary>
    public partial class VoiceDictationButton : UserControl
    {
        private IVoiceInputService? _voiceService;
        private Storyboard? _recordingAnimation;

        /// <summary>
        /// Indique si l'enregistrement est actif
        /// </summary>
        public bool IsRecording => _voiceService?.IsRecording ?? false;

        /// <summary>
        /// TextBox cible où le texte dicté sera inséré.
        /// Le focus sera donné à ce TextBox AVANT de déclencher Handy.
        /// </summary>
        public TextBox? TargetTextBox { get; set; }

        /// <summary>
        /// Événement déclenché quand l'enregistrement démarre
        /// </summary>
        public event EventHandler? RecordingStarted;

        /// <summary>
        /// Événement déclenché quand l'enregistrement s'arrête
        /// </summary>
        public event EventHandler? RecordingStopped;

        public VoiceDictationButton()
        {
            InitializeComponent();
            _recordingAnimation = (Storyboard)FindResource("RecordingPulse");

            // Initialiser avec le service Handy par défaut
            InitializeVoiceService();
        }

        /// <summary>
        /// Initialise le service de transcription vocale
        /// </summary>
        private void InitializeVoiceService()
        {
            try
            {
                // Charger les paramètres pour le hotkey
                var settings = AppSettings.Load();
                var hotkey = settings.HandyHotkey ?? "Ctrl+Space";

                _voiceService = new HandyVoiceInputService { Hotkey = hotkey };

                // S'abonner aux événements
                _voiceService.RecordingStarted += OnRecordingStarted;
                _voiceService.RecordingStopped += OnRecordingStopped;

                System.Diagnostics.Debug.WriteLine($"[VoiceDictationButton] Service initialisé avec hotkey: {hotkey}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VoiceDictationButton] Erreur init: {ex.Message}");
            }
        }

        /// <summary>
        /// Définit un service de transcription personnalisé
        /// </summary>
        public void SetVoiceService(IVoiceInputService voiceService)
        {
            // Désabonner de l'ancien service
            if (_voiceService != null)
            {
                _voiceService.RecordingStarted -= OnRecordingStarted;
                _voiceService.RecordingStopped -= OnRecordingStopped;
            }

            _voiceService = voiceService;

            // S'abonner au nouveau service
            if (_voiceService != null)
            {
                _voiceService.RecordingStarted += OnRecordingStarted;
                _voiceService.RecordingStopped += OnRecordingStopped;
            }
        }

        /// <summary>
        /// Met à jour le hotkey utilisé
        /// </summary>
        public void UpdateHotkey(string hotkey)
        {
            if (_voiceService != null)
            {
                _voiceService.Hotkey = hotkey;
                System.Diagnostics.Debug.WriteLine($"[VoiceDictationButton] Hotkey mis à jour: {hotkey}");
            }
        }

        /// <summary>
        /// Click sur le bouton microphone
        /// </summary>
        private async void MicButton_Click(object sender, RoutedEventArgs e)
        {
            if (_voiceService == null)
            {
                MessageBox.Show(
                    "Service de transcription non disponible.\n\nVérifiez que Handy est installé et en cours d'exécution.",
                    "Transcription vocale",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                // IMPORTANT: Donner le focus au TextBox cible AVANT de déclencher Handy
                // pour que le texte dicté soit inséré au bon endroit
                if (TargetTextBox != null)
                {
                    TargetTextBox.Focus();
                    // Placer le curseur à la fin du texte existant
                    TargetTextBox.CaretIndex = TargetTextBox.Text?.Length ?? 0;
                    System.Diagnostics.Debug.WriteLine($"[VoiceDictationButton] Focus donné à TargetTextBox");
                }

                await _voiceService.ToggleRecordingAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VoiceDictationButton] Erreur toggle: {ex.Message}");
                MessageBox.Show(
                    $"Erreur lors de l'activation de la transcription:\n{ex.Message}",
                    "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Gestionnaire: enregistrement démarré
        /// </summary>
        private void OnRecordingStarted(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Afficher le bouton stop
                MicButton.Visibility = Visibility.Collapsed;
                StopButton.Visibility = Visibility.Visible;

                // Démarrer l'animation
                RecordingRing.Opacity = 1;
                _recordingAnimation?.Begin();

                // Propager l'événement
                RecordingStarted?.Invoke(this, EventArgs.Empty);

                System.Diagnostics.Debug.WriteLine("[VoiceDictationButton] Enregistrement démarré");
            });
        }

        /// <summary>
        /// Gestionnaire: enregistrement arrêté
        /// </summary>
        private void OnRecordingStopped(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // Afficher le bouton micro
                MicButton.Visibility = Visibility.Visible;
                StopButton.Visibility = Visibility.Collapsed;

                // Arrêter l'animation
                _recordingAnimation?.Stop();
                RecordingRing.Opacity = 0;

                // Propager l'événement
                RecordingStopped?.Invoke(this, EventArgs.Empty);

                System.Diagnostics.Debug.WriteLine("[VoiceDictationButton] Enregistrement arrêté");
            });
        }

        /// <summary>
        /// Force l'arrêt de l'enregistrement (si actif)
        /// </summary>
        public async void ForceStop()
        {
            if (_voiceService?.IsRecording == true)
            {
                await _voiceService.StopRecordingAsync();
            }
        }
    }
}
