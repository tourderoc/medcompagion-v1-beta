using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MedCompanion.Dialogs;
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.Services.Voice;
using MedCompanion.ViewModels;

namespace MedCompanion.Views.Consultation
{
    /// <summary>
    /// Focus Med - Interface principale du Mode Consultation (V2)
    /// Layout 45% / 24% / 31% : Zone Active / Bureau de Med / Zone Archive
    /// </summary>
    public partial class FocusMedControl : UserControl
    {
        private FocusMedViewModel? _viewModel;
        private HandyVoiceInputService? _voiceInputService;
        private SilenceDetector? _silenceDetector;
        private MedAvatarService? _avatarService;

        public FocusMedControl()
        {
            InitializeComponent();
            _viewModel = DataContext as FocusMedViewModel;

            // Gérer Enter pour envoyer le message
            this.PreviewKeyDown += FocusMedControl_PreviewKeyDown;

            // Initialiser les services vocaux
            InitializeVoiceServices();

            // Initialiser le service Avatar
            InitializeAvatarService();
        }

        /// <summary>
        /// Initialise le service Avatar et lie les états du ViewModel
        /// </summary>
        private void InitializeAvatarService()
        {
            _avatarService = MedAvatarService.Instance;

            // Observer les changements du ViewModel pour mettre à jour l'avatar
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            }

            // État initial
            _avatarService.SetIdle();
        }

        /// <summary>
        /// Met à jour l'état de l'avatar selon le ViewModel
        /// </summary>
        private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_avatarService == null || _viewModel == null) return;

            // Synchroniser les états ViewModel -> Avatar
            if (e.PropertyName == nameof(FocusMedViewModel.IsThinking) ||
                e.PropertyName == nameof(FocusMedViewModel.IsSpeaking))
            {
                Dispatcher.Invoke(() =>
                {
                    if (_viewModel.IsSpeaking)
                    {
                        _avatarService.SetSpeaking();
                    }
                    else if (_viewModel.IsThinking)
                    {
                        _avatarService.SetThinking();
                    }
                    else
                    {
                        _avatarService.SetIdle();
                    }
                });
            }
        }

        /// <summary>
        /// Ouvre le dialog de configuration de l'avatar
        /// </summary>
        private void ConfigAvatar_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new MedAvatarConfigDialog
                {
                    Owner = Window.GetWindow(this)
                };
                dialog.ShowDialog();

                // Rafraîchir l'avatar après configuration
                MedAvatar?.Refresh();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur ouverture config: {ex.Message}\n\n{ex.StackTrace}",
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Initialise les services de saisie vocale (Handy + détection silence)
        /// </summary>
        private void InitializeVoiceServices()
        {
            // Service Handy pour STT
            _voiceInputService = new HandyVoiceInputService();
            _voiceInputService.Hotkey = "Ctrl+Shift+H"; // Raccourci par défaut, configurable

            _voiceInputService.RecordingStarted += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (_viewModel != null)
                    {
                        _viewModel.IsVoiceRecording = true;
                        _viewModel.VoiceStatusText = "Parlez...";
                    }
                    // Démarrer la détection de silence
                    _silenceDetector?.Start(_viewModel?.InputText ?? "");
                });
            };

            _voiceInputService.RecordingStopped += (s, e) =>
            {
                Dispatcher.Invoke(async () =>
                {
                    if (_viewModel != null)
                    {
                        _viewModel.IsVoiceRecording = false;
                        _viewModel.VoiceStatusText = "";

                        // Si l'envoi auto est activé et qu'on est en mode conversation
                        if (_viewModel.AutoSendVoiceMessage && _viewModel.IsConversationModeEnabled)
                        {
                            await WaitForTextStabilityAndSendAsync();
                        }
                    }
                    _silenceDetector?.Stop();
                });
            };

            // Détecteur de silence
            _silenceDetector = new SilenceDetector();
            _silenceDetector.SilenceDelayMs = 2000; // 2 secondes de silence

            _silenceDetector.SilenceDetected += async (s, text) =>
            {
                await Dispatcher.InvokeAsync(async () =>
                {
                    // Arrêter l'enregistrement Handy
                    if (_viewModel?.IsVoiceRecording == true)
                    {
                        await _voiceInputService.StopRecordingAsync();
                    }

                    // On ne déclenche plus l'envoi ici directement car RecordingStopped 
                    // ou ToggleHandyForConversationAsync vont s'en charger avec une attente de stabilité
                });
            };
        }

        /// <summary>
        /// Initialise le contrôle avec le service Med
        /// Doit être appelé par MainWindow après création
        /// </summary>
        public void Initialize(MedAgentService medAgentService)
        {
            _viewModel = DataContext as FocusMedViewModel;
            _viewModel?.Initialize(medAgentService);

            // S'abonner aux changements de collection pour auto-scroll
            if (_viewModel != null)
            {
                _viewModel.Messages.CollectionChanged += Messages_CollectionChanged;

                // S'abonner aux changements d'état pour l'avatar
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            }

            // S'assurer que l'avatar est en état idle
            _avatarService?.SetIdle();
        }

        /// <summary>
        /// Permet d'injecter un ViewModel externe si nécessaire
        /// </summary>
        public void SetViewModel(FocusMedViewModel viewModel)
        {
            DataContext = viewModel;
            _viewModel = viewModel;
        }

        /// <summary>
        /// Gère les raccourcis clavier (Enter pour envoyer, Espace pour conversation vocale)
        /// </summary>
        private async void FocusMedControl_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Mode conversation vocale: Espace toggle Handy si mode conversation activé
            if (e.Key == Key.Space && _viewModel?.IsConversationModeEnabled == true)
            {
                var isRecording = _viewModel.IsVoiceRecording;
                var isSpeaking = _viewModel.IsSpeaking;

                // Si Med parle: l'interrompre et démarrer l'enregistrement (prise de parole)
                if (isSpeaking && !isRecording)
                {
                    e.Handled = true;
                    await _viewModel.StopSpeakingAsync();
                    await ToggleHandyForConversationAsync(); // Démarre Handy
                    return;
                }

                // Si en enregistrement: toujours arrêter et envoyer (priorité absolue)
                if (isRecording)
                {
                    e.Handled = true;
                    await ToggleHandyForConversationAsync();
                    return;
                }

                // Si pas en enregistrement: démarrer (le focus est normalement déjà géré)
                e.Handled = true;
                await ToggleHandyForConversationAsync();
                return;
            }

            // Ctrl+Enter ou Enter seul pour envoyer
            if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                if (_viewModel?.SendMessageCommand.CanExecute(null) == true)
                {
                    _viewModel.SendMessageCommand.Execute(null);
                    e.Handled = true;
                }
            }
        }

        /// <summary>
        /// Toggle Handy pour le mode conversation vocale
        /// Si arrêt -> attend la transcription et envoie automatiquement
        /// </summary>
        private async Task ToggleHandyForConversationAsync()
        {
            if (_voiceInputService == null || _viewModel == null) return;

            try
            {
                var wasRecording = _viewModel.IsVoiceRecording;

                // Focus sur le TextBox pour Handy
                InputTextBox.Focus();
                await Task.Delay(50);

                // Toggle Handy
                await _voiceInputService.ToggleRecordingAsync();

                // L'envoi automatique est maintenant géré par l'événement RecordingStopped
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Conversation] Erreur: {ex.Message}");
            }
        }

        /// <summary>
        /// Attend que le texte dans le TextBox ne change plus (injection Handy finie)
        /// puis envoie le message automatiquement
        /// </summary>
        private async Task WaitForTextStabilityAndSendAsync()
        {
            if (_viewModel == null) return;

            try
            {
                string lastText = _viewModel.InputText;
                int stableCount = 0;
                int maxWaitIterations = 20; // Max 4 secondes (20 * 200ms)

                // Attendre que le texte se stabilise
                for (int i = 0; i < maxWaitIterations; i++)
                {
                    await Task.Delay(200);
                    
                    if (_viewModel.InputText == lastText && !string.IsNullOrWhiteSpace(lastText))
                    {
                        stableCount++;
                        // Si stable pendant 2 itérations (400ms) sans changement
                        if (stableCount >= 2) break;
                    }
                    else
                    {
                        lastText = _viewModel.InputText;
                        stableCount = 0;
                    }
                }

                // Envoyer automatiquement si du texte est présent
                if (!string.IsNullOrWhiteSpace(_viewModel.InputText))
                {
                    if (_viewModel.SendMessageCommand.CanExecute(null))
                    {
                        System.Diagnostics.Debug.WriteLine($"[Conversation] Envoi auto (stable): \"{_viewModel.InputText}\"");
                        _viewModel.SendMessageCommand.Execute(null);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Stability] Erreur: {ex.Message}");
            }
        }

        /// <summary>
        /// Auto-scroll vers le bas quand un nouveau message arrive
        /// </summary>
        private void Messages_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                // Utiliser Dispatcher pour s'assurer que le scroll se fait après le rendu
                Dispatcher.InvokeAsync(() =>
                {
                    MessagesScrollViewer.ScrollToEnd();
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        /// <summary>
        /// Accepte l'action proposée par Med
        /// </summary>
        private void AcceptAction_Click(object sender, RoutedEventArgs e)
        {
            _viewModel?.CopySummaryToClipboard();
        }

        /// <summary>
        /// Refuse l'action proposée par Med
        /// </summary>
        private void DeclineAction_Click(object sender, RoutedEventArgs e)
        {
            _viewModel?.DeclinePendingAction();
        }

        /// <summary>
        /// Gère le clic sur le bouton micro chat (active/désactive le mode conversation)
        /// Déclenche aussi Handy directement si on l'active (One-Click)
        /// </summary>
        private async void VoiceInputButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;

            // Toggle le mode conversation
            _viewModel.IsConversationModeEnabled = !_viewModel.IsConversationModeEnabled;

            if (_viewModel.IsConversationModeEnabled)
            {
                // Donner le focus au TextBox
                InputTextBox.Focus();
                _viewModel.VoiceStatusText = "Appuyez sur Espace pour parler";
            }
            else
            {
                _viewModel.VoiceStatusText = "";
                // Si on désactive alors qu'on enregistrait, on arrête
                if (_viewModel.IsVoiceRecording)
                {
                    await ToggleHandyForConversationAsync();
                }
            }
        }

        /// <summary>
        /// Gère les changements de texte (pour la détection de silence)
        /// </summary>
        private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Mettre à jour le détecteur de silence avec le nouveau texte
            if (_silenceDetector?.IsActive == true && sender is TextBox textBox)
            {
                _silenceDetector.UpdateText(textBox.Text);
            }
        }

        /// <summary>
        /// Configure le raccourci clavier Handy
        /// </summary>
        public void SetHandyHotkey(string hotkey)
        {
            if (_voiceInputService != null)
            {
                _voiceInputService.Hotkey = hotkey;
            }
        }

        /// <summary>
        /// Configure le délai de silence
        /// </summary>
        public void SetSilenceDelay(int delayMs)
        {
            if (_silenceDetector != null)
            {
                _silenceDetector.SilenceDelayMs = delayMs;
            }
        }

        /// <summary>
        /// Gère le clic sur le bouton de lecture d'un message
        /// </summary>
        private async void SpeakMessage_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string content && _viewModel != null)
            {
                await _viewModel.SpeakMessageAsync(content);
            }
        }

        /// <summary>
        /// Gère le clic sur le bouton de contrôle vocal (Bureau de Med)
        /// - Si Med parle : arrête la lecture
        /// - Sinon : toggle activation/désactivation de la voix
        /// </summary>
        private async void VoiceControlButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;

            try
            {
                if (_viewModel.IsSpeaking)
                {
                    // Med parle -> arrêter
                    await _viewModel.StopSpeakingAsync();
                }
                else
                {
                    // Toggle l'activation de la voix
                    _viewModel.IsMicrophoneEnabled = !_viewModel.IsMicrophoneEnabled;
                }
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[VoiceControl] Erreur: {ex.Message}");
            }
        }
    }
}
