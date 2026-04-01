using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.Services.Voice;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// ViewModel pour le Focus Med (Mode Consultation V2)
    /// Gère le chat avec Med et l'historique des conversations
    /// </summary>
    public class FocusMedViewModel : INotifyPropertyChanged
    {
        private MedAgentService? _medAgentService;
        private PiperTTSService? _ttsService;

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion

        #region Properties - Zone Active (Gauche)

        private string _inputText = string.Empty;
        public string InputText
        {
            get => _inputText;
            set => SetProperty(ref _inputText, value);
        }

        /// <summary>
        /// Messages de la conversation actuelle
        /// </summary>
        public ObservableCollection<ChatMessage> Messages { get; } = new();

        #endregion

        #region Properties - Bureau de Med (Centre)

        private bool _isMicrophoneEnabled = false;
        /// <summary>
        /// Contrôle si Med peut parler (micro central du Bureau de Med)
        /// Quand activé, Med lit automatiquement ses réponses
        /// </summary>
        public bool IsMicrophoneEnabled
        {
            get => _isMicrophoneEnabled;
            set
            {
                if (SetProperty(ref _isMicrophoneEnabled, value))
                {
                    // Synchroniser avec AutoReadResponses
                    AutoReadResponses = value;

                    // Si on désactive pendant que Med parle, arrêter la lecture
                    if (!value && IsSpeaking)
                    {
                        _ = StopSpeakingAsync();
                    }
                }
            }
        }

        private string _pendingActionText = string.Empty;
        public string PendingActionText
        {
            get => _pendingActionText;
            set => SetProperty(ref _pendingActionText, value);
        }

        private bool _hasPendingAction = false;
        public bool HasPendingAction
        {
            get => _hasPendingAction;
            set => SetProperty(ref _hasPendingAction, value);
        }

        private bool _isThinking = false;
        public bool IsThinking
        {
            get => _isThinking;
            set
            {
                if (SetProperty(ref _isThinking, value))
                {
                    OnPropertyChanged(nameof(MedStatusText));
                }
            }
        }

        private string _medStatusText = "En écoute";
        public string MedStatusText
        {
            get
            {
                if (IsSpeaking) return "Parle...";
                if (IsThinking) return "Réflexion...";
                return _medStatusText;
            }
            set => SetProperty(ref _medStatusText, value);
        }

        private string _llmInfo = "";
        /// <summary>
        /// Information sur le LLM utilisé (provider + modèle)
        /// </summary>
        public string LLMInfo
        {
            get => _llmInfo;
            set => SetProperty(ref _llmInfo, value);
        }

        private string _errorMessage = string.Empty;
        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        private bool _hasError = false;
        public bool HasError
        {
            get => _hasError;
            set => SetProperty(ref _hasError, value);
        }

        #endregion

        #region Properties - Voice Input

        private bool _isConversationModeEnabled = false;
        /// <summary>
        /// Mode conversation vocale: Espace déclenche Handy
        /// Séparé de IsMicrophoneEnabled qui contrôle le TTS de Med
        /// </summary>
        public bool IsConversationModeEnabled
        {
            get => _isConversationModeEnabled;
            set => SetProperty(ref _isConversationModeEnabled, value);
        }

        private bool _isVoiceRecording = false;
        /// <summary>
        /// Indique si l'enregistrement vocal est actif
        /// </summary>
        public bool IsVoiceRecording
        {
            get => _isVoiceRecording;
            set => SetProperty(ref _isVoiceRecording, value);
        }

        private string _voiceStatusText = string.Empty;
        /// <summary>
        /// Texte de statut pour l'enregistrement vocal
        /// </summary>
        public string VoiceStatusText
        {
            get => _voiceStatusText;
            set => SetProperty(ref _voiceStatusText, value);
        }

        private bool _autoSendVoiceMessage = true;
        /// <summary>
        /// Envoie automatiquement le message après détection du silence
        /// </summary>
        public bool AutoSendVoiceMessage
        {
            get => _autoSendVoiceMessage;
            set => SetProperty(ref _autoSendVoiceMessage, value);
        }

        #endregion

        #region Properties - Voice Output (TTS)

        private bool _isSpeaking = false;
        /// <summary>
        /// Indique si Med est en train de parler
        /// </summary>
        public bool IsSpeaking
        {
            get => _isSpeaking;
            set
            {
                if (SetProperty(ref _isSpeaking, value))
                {
                    OnPropertyChanged(nameof(MedStatusText));
                }
            }
        }

        private bool _autoReadResponses = false;
        /// <summary>
        /// Lecture automatique des réponses de Med
        /// Contrôlé par le micro central (IsMicrophoneEnabled)
        /// </summary>
        public bool AutoReadResponses
        {
            get => _autoReadResponses;
            set => SetProperty(ref _autoReadResponses, value);
        }

        private bool _ttsAvailable = false;
        /// <summary>
        /// Indique si le TTS est disponible (Piper configuré)
        /// </summary>
        public bool TTSAvailable
        {
            get => _ttsAvailable;
            set => SetProperty(ref _ttsAvailable, value);
        }

        #endregion

        #region Properties - Mémoire de Med (Droite)

        private MedMemoryService? _memoryService;

        /// <summary>
        /// Blocs mémoire de Med
        /// </summary>
        public ObservableCollection<MedMemoryBlock> MemoryBlocks { get; } = new();

        /// <summary>
        /// Bloc mémoire actuellement sélectionné pour édition
        /// </summary>
        private MedMemoryBlock? _selectedMemoryBlock;
        public MedMemoryBlock? SelectedMemoryBlock
        {
            get => _selectedMemoryBlock;
            set => SetProperty(ref _selectedMemoryBlock, value);
        }

        /// <summary>
        /// Texte en cours d'édition pour le bloc sélectionné
        /// </summary>
        private string _editingBlockContent = string.Empty;
        public string EditingBlockContent
        {
            get => _editingBlockContent;
            set => SetProperty(ref _editingBlockContent, value);
        }

        /// <summary>
        /// Indique si un bloc est en cours d'édition
        /// </summary>
        private bool _isEditingMemoryBlock = false;
        public bool IsEditingMemoryBlock
        {
            get => _isEditingMemoryBlock;
            set => SetProperty(ref _isEditingMemoryBlock, value);
        }

        // Conservé pour compatibilité (sera supprimé plus tard)
        public ObservableCollection<ArchivedConversation> ArchivedConversations { get; } = new();

        #endregion

        #region Commands

        public ICommand SendMessageCommand { get; }
        public ICommand ClearConversationCommand { get; }
        public ICommand GetSummaryCommand { get; }
        public ICommand StopSpeakingCommand { get; }
        public ICommand SpeakMessageCommand { get; }

        // Commandes Mémoire
        public ICommand EditMemoryBlockCommand { get; }
        public ICommand SaveMemoryBlockCommand { get; }
        public ICommand CancelEditMemoryBlockCommand { get; }

        #endregion

        #region Constructor

        public FocusMedViewModel()
        {
            // Commandes
            SendMessageCommand = new RelayCommand(async _ => await SendMessageAsync(), _ => CanSendMessage());
            ClearConversationCommand = new RelayCommand(_ => ClearConversation());
            GetSummaryCommand = new RelayCommand(async _ => await GetSummaryAsync());
            StopSpeakingCommand = new RelayCommand(async _ => await StopSpeakingAsync(), _ => IsSpeaking);
            SpeakMessageCommand = new RelayCommand(async param => await SpeakMessageAsync(param as string), _ => TTSAvailable && !IsSpeaking);

            // Commandes Mémoire
            EditMemoryBlockCommand = new RelayCommand(param => EditMemoryBlock(param as MedMemoryBlock));
            SaveMemoryBlockCommand = new RelayCommand(_ => SaveMemoryBlock(), _ => IsEditingMemoryBlock);
            CancelEditMemoryBlockCommand = new RelayCommand(_ => CancelEditMemoryBlock());

            // Initialiser le service TTS
            InitializeTTS();

            // Initialiser la mémoire
            InitializeMemory();

            // Données placeholder pour l'UI (temporaire)
            LoadPlaceholderData();
        }

        /// <summary>
        /// Initialise le service Piper TTS
        /// </summary>
        private void InitializeTTS()
        {
            _ttsService = new PiperTTSService();

            // Vérifier si Piper est disponible
            TTSAvailable = _ttsService.IsAvailable;

            if (TTSAvailable)
            {
                // S'abonner aux événements TTS
                _ttsService.SpeechStarted += (s, e) =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsSpeaking = true;
                    });
                };

                _ttsService.SpeechCompleted += (s, e) =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsSpeaking = false;
                    });
                };

                _ttsService.SpeechError += (s, error) =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsSpeaking = false;
                        ErrorMessage = $"Erreur TTS: {error}";
                        HasError = true;
                    });
                };
            }
        }

        // Message en cours de streaming (pour mise à jour en temps réel)
        private ChatMessage? _currentStreamingMessage;
        
        // Buffering pour le TTS en streaming
        private System.Text.StringBuilder _ttsBuffer = new();
        private List<char> _sentenceTerminators = new() { '.', '!', '?', '\n', ':', ';' };

        /// <summary>
        /// Initialise le service Med (doit être appelé après injection des dépendances)
        /// </summary>
        public void Initialize(MedAgentService medAgentService)
        {
            _medAgentService = medAgentService;

            // S'abonner aux événements
            _medAgentService.ThinkingStateChanged += (s, isThinking) =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    IsThinking = isThinking;
                });
            };

            _medAgentService.ErrorOccurred += (s, error) =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    ErrorMessage = error;
                    HasError = true;
                });
            };

            // S'abonner aux tokens pour le streaming
            _medAgentService.TokenReceived += (s, token) =>
            {
                // Capturer la référence au message actuel pour éviter la race condition
                // si _currentStreamingMessage devient null avant l'exécution du Dispatcher
                var messageToUpdate = _currentStreamingMessage;
                if (messageToUpdate == null) return;

                // Utiliser InvokeAsync avec priorité Render pour assurer un affichage fluide
                System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    messageToUpdate.Content += token;
                }, System.Windows.Threading.DispatcherPriority.Render);

                // Gestion du TTS en streaming
                if (AutoReadResponses && TTSAvailable)
                {
                    _ttsBuffer.Append(token);
                    string currentText = _ttsBuffer.ToString();
                    
                    // Vérifier si on a une phrase complète (se termine par un terminateur ou est assez longue)
                    if (currentText.Length > 0)
                    {
                        char lastChar = currentText[currentText.Length - 1];
                        if (_sentenceTerminators.Contains(lastChar) || 
                            (currentText.Length > 100 && char.IsWhiteSpace(lastChar)))
                        {
                            var phrase = currentText.Trim();
                            if (!string.IsNullOrEmpty(phrase) && _ttsService != null)
                            {
                                _ttsService.EnqueuePhraseAsync(phrase);
                            }
                            _ttsBuffer.Clear();
                        }
                    }
                }
            };

            // Afficher l'info du LLM
            LLMInfo = _medAgentService?.GetLLMInfo() ?? "Indisponible";

            // S'abonner aux changements de configuration LLM
            if (_medAgentService != null)
            {
                _medAgentService.ConfigChanged += (s, newLLMInfo) =>
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        LLMInfo = newLLMInfo;
                    });
                };
            }

            // Message d'accueil si le service est disponible
            if (_medAgentService != null && _medAgentService.IsEnabled)
            {
                MedStatusText = "En écoute";
                AddSystemMessage("Med est prêt. Posez-moi vos questions ou partagez vos réflexions.");
            }
            else if (_medAgentService != null)
            {
                MedStatusText = "Désactivé";
                AddSystemMessage("Med est désactivé. Activez-le dans Paramètres > Agents.");
            }
        }

        private void LoadPlaceholderData()
        {
            // Archives placeholder (conservé temporairement pour compatibilité)
            // Sera supprimé quand le panneau Historique sera complètement remplacé
        }

        /// <summary>
        /// Initialise le service de mémoire et charge les blocs
        /// </summary>
        private void InitializeMemory()
        {
            try
            {
                _memoryService = new MedMemoryService();
                var blocks = _memoryService.GetAllBlocks();

                MemoryBlocks.Clear();
                foreach (var block in blocks)
                {
                    MemoryBlocks.Add(block);
                }

                System.Diagnostics.Debug.WriteLine($"[FocusMedViewModel] {MemoryBlocks.Count} blocs mémoire chargés");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FocusMedViewModel] Erreur init mémoire: {ex.Message}");
            }
        }

        #endregion

        #region Memory Methods

        /// <summary>
        /// Ouvre un bloc mémoire pour édition
        /// </summary>
        private void EditMemoryBlock(MedMemoryBlock? block)
        {
            if (block == null) return;

            // Fermer l'édition précédente si elle existe
            if (SelectedMemoryBlock != null && SelectedMemoryBlock != block)
            {
                SelectedMemoryBlock.IsEditing = false;
            }

            SelectedMemoryBlock = block;
            EditingBlockContent = block.Content;
            IsEditingMemoryBlock = true;
            block.IsEditing = true;

            System.Diagnostics.Debug.WriteLine($"[FocusMedViewModel] Édition bloc: {block.BlockId}");
        }

        /// <summary>
        /// Sauvegarde les modifications du bloc mémoire
        /// </summary>
        private void SaveMemoryBlock()
        {
            if (SelectedMemoryBlock == null || _memoryService == null) return;

            // Mettre à jour le contenu
            SelectedMemoryBlock.Content = EditingBlockContent;
            _memoryService.UpdateBlockContent(SelectedMemoryBlock.BlockId, EditingBlockContent);
            _memoryService.Save();

            // Fermer l'édition
            SelectedMemoryBlock.IsEditing = false;
            IsEditingMemoryBlock = false;
            SelectedMemoryBlock = null;
            EditingBlockContent = string.Empty;

            System.Diagnostics.Debug.WriteLine("[FocusMedViewModel] Bloc mémoire sauvegardé");
        }

        /// <summary>
        /// Annule l'édition du bloc mémoire
        /// </summary>
        private void CancelEditMemoryBlock()
        {
            if (SelectedMemoryBlock != null)
            {
                SelectedMemoryBlock.IsEditing = false;
            }

            IsEditingMemoryBlock = false;
            SelectedMemoryBlock = null;
            EditingBlockContent = string.Empty;
        }

        /// <summary>
        /// Récupère le contexte mémoire pour le prompt
        /// </summary>
        public string GetMemoryContext()
        {
            return _memoryService?.GetContextForPrompt() ?? string.Empty;
        }

        #endregion

        #region Chat Methods

        private bool CanSendMessage()
        {
            return !string.IsNullOrWhiteSpace(InputText) && !IsThinking && _medAgentService?.IsEnabled == true;
        }

        private async Task SendMessageAsync()
        {
            if (_medAgentService == null || string.IsNullOrWhiteSpace(InputText))
                return;

            var userMessage = InputText.Trim();
            InputText = string.Empty;
            HasError = false;

            // Ajouter le message utilisateur à l'UI
            AddUserMessage(userMessage);

            // Créer un message vide pour Med (sera rempli en streaming)
            _currentStreamingMessage = new ChatMessage
            {
                Role = "assistant",
                Content = "",
                Timestamp = DateTime.Now
            };
            Messages.Add(_currentStreamingMessage);

            // Envoyer à Med avec streaming sur un thread de fond pour ne pas bloquer l'UI
            _ttsBuffer.Clear();
            if (AutoReadResponses && TTSAvailable && _ttsService != null)
            {
                await _ttsService.InitializeStreamAsync().ConfigureAwait(false);
            }

            var (success, response, error) = await Task.Run(async () => 
            {
                if (_medAgentService == null) return (false, "", "Service non initialisé");
                return await _medAgentService.SendMessageStreamAsync(userMessage);
            });

            // Finaliser le TTS stream
            if (AutoReadResponses && TTSAvailable && _ttsService != null)
            {
                // Envoyer le reste du buffer s'il n'est pas vide
                if (_ttsBuffer.Length > 0)
                {
                    await _ttsService.EnqueuePhraseAsync(_ttsBuffer.ToString()).ConfigureAwait(false);
                }
                await _ttsService.FinalizeStreamAsync().ConfigureAwait(false);
            }

            // Synchronisation finale par sécurité (si des tokens ont été manqués ou sont encore en file d'attente)
            if (success && _currentStreamingMessage != null && !string.IsNullOrEmpty(response))
            {
                _currentStreamingMessage.Content = response;
            }

            // Récupérer le contenu du message pour TTS
            var responseContent = _currentStreamingMessage?.Content ?? response;

            // Terminer le streaming
            _currentStreamingMessage = null;

            if (!success)
            {
                // En cas d'erreur, retirer le message vide et afficher l'erreur
                if (Messages.Count > 0 && Messages[Messages.Count - 1].Role == "assistant" && string.IsNullOrEmpty(Messages[Messages.Count - 1].Content))
                {
                    Messages.RemoveAt(Messages.Count - 1);
                }
                ErrorMessage = error ?? "Une erreur s'est produite.";
                HasError = true;
            }
            else
            {
                // La lecture est déjà gérée en streaming via TokenReceived
            }
        }

        private void ClearConversation()
        {
            Messages.Clear();
            _medAgentService?.ClearHistory();

            if (_medAgentService?.IsEnabled == true)
            {
                AddSystemMessage("Nouvelle conversation. Comment puis-je vous aider ?");
            }
        }

        private async Task GetSummaryAsync()
        {
            if (_medAgentService == null) return;

            var (success, summary, error) = await _medAgentService.GetConversationSummaryAsync();

            if (success)
            {
                // Proposer de copier le résumé
                PendingActionText = "Résumé généré. Copier dans le presse-papier ?";
                HasPendingAction = true;
                // Le résumé sera stocké temporairement pour la copie
                _lastSummary = summary;
            }
            else
            {
                ErrorMessage = error ?? "Impossible de générer le résumé.";
                HasError = true;
            }
        }

        private string _lastSummary = string.Empty;

        /// <summary>
        /// Copie le dernier résumé dans le presse-papier
        /// </summary>
        public void CopySummaryToClipboard()
        {
            if (!string.IsNullOrEmpty(_lastSummary))
            {
                System.Windows.Clipboard.SetText(_lastSummary);
                HasPendingAction = false;
                AddSystemMessage("Résumé copié dans le presse-papier.");
            }
        }

        /// <summary>
        /// Refuse l'action proposée
        /// </summary>
        public void DeclinePendingAction()
        {
            HasPendingAction = false;
            _lastSummary = string.Empty;
        }

        #endregion

        #region TTS Methods

        /// <summary>
        /// Lit un message à voix haute
        /// </summary>
        public async Task SpeakMessageAsync(string? text)
        {
            if (string.IsNullOrWhiteSpace(text) || _ttsService == null || !TTSAvailable)
                return;

            await _ttsService.SpeakAsync(text);
        }

        /// <summary>
        /// Arrête la lecture en cours
        /// </summary>
        public async Task StopSpeakingAsync()
        {
            if (_ttsService != null)
            {
                await _ttsService.StopAsync();
            }
        }

        #endregion

        #region Message Helpers

        private void AddUserMessage(string content)
        {
            Messages.Add(new ChatMessage
            {
                Role = "user",
                Content = content,
                Timestamp = DateTime.Now
            });
        }

        private void AddMedMessage(string content)
        {
            Messages.Add(new ChatMessage
            {
                Role = "assistant",
                Content = content,
                Timestamp = DateTime.Now
            });
        }

        private void AddSystemMessage(string content)
        {
            Messages.Add(new ChatMessage
            {
                Role = "system",
                Content = content,
                Timestamp = DateTime.Now
            });
        }

        #endregion
    }

    /// <summary>
    /// Message dans le chat Focus Med
    /// </summary>
    public class ChatMessage : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private string _role = "user";
        public string Role
        {
            get => _role;
            set { _role = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Role))); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsUser))); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMed))); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSystem))); }
        }

        public bool IsUser => Role == "user";
        public bool IsMed => Role == "assistant";
        public bool IsSystem => Role == "system";

        private string _content = string.Empty;
        public string Content
        {
            get => _content;
            set { _content = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Content))); }
        }

        private DateTime _timestamp;
        public DateTime Timestamp
        {
            get => _timestamp;
            set { _timestamp = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Timestamp))); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TimeLabel))); }
        }

        public string TimeLabel => Timestamp.ToString("HH:mm");
    }

    /// <summary>
    /// Modèle pour les conversations archivées
    /// </summary>
    public class ArchivedConversation : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private string _title = string.Empty;
        public string Title
        {
            get => _title;
            set { _title = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title))); }
        }

        private DateTime _dateTime;
        public DateTime DateTime
        {
            get => _dateTime;
            set
            {
                _dateTime = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DateTime)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DateTimeLabel)));
            }
        }

        public string DateTimeLabel
        {
            get
            {
                if (DateTime.Date == System.DateTime.Today)
                    return $"Aujourd'hui {DateTime:HH:mm}";
                if (DateTime.Date == System.DateTime.Today.AddDays(-1))
                    return $"Hier {DateTime:HH:mm}";
                return DateTime.ToString("dd/MM HH:mm");
            }
        }

        private string _preview = string.Empty;
        public string Preview
        {
            get => _preview;
            set { _preview = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Preview))); }
        }

        private bool _isExpanded = false;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded))); }
        }
    }
}
