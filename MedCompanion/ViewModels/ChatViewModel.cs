using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Input;
using System.Windows.Media;
using MedCompanion.Commands;
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.Helpers;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// ViewModel pour la fonctionnalité Chat
    /// </summary>
    public class ChatViewModel : ViewModelBase
    {
        // ===== SERVICES =====
        private readonly OpenAIService _openAIService;
        private readonly StorageService _storageService;
        private readonly PatientContextService _patientContextService;
        private readonly AnonymizationService _anonymizationService;
        private readonly PromptConfigService _promptConfigService;
        private readonly LLMGatewayService _llmGatewayService;  // ✅ NOUVEAU - Gateway centralisé
        private readonly PromptTrackerService? _promptTracker;
        private readonly ChatMemoryService? _chatMemoryService;

        // ===== ÉTAT =====
        private PatientIndexEntry? _currentPatient;
        private List<ChatExchange> _chatHistory = new();  // Max 3 échanges temporaires

        // Mémoire compactée
        private string _compactedMemory = string.Empty;
        private List<ChatExchange> _recentSavedExchanges = new();

        // ===== COLLECTIONS (Binding UI) =====
        public ObservableCollection<ChatMessageViewModel> Messages { get; } = new();
        public ObservableCollection<ChatExchange> SavedExchanges { get; } = new();

        // ===== PROPRIÉTÉS =====

        private string _inputText = "";
        public string InputText
        {
            get => _inputText;
            set
            {
                if (SetProperty(ref _inputText, value))
                {
                    ((RelayCommand)SendMessageCommand).RaiseCanExecuteChanged();
                }
            }
        }

        private bool _isSending = false;
        public bool IsSending
        {
            get => _isSending;
            set
            {
                if (SetProperty(ref _isSending, value))
                {
                    ((RelayCommand)SendMessageCommand).RaiseCanExecuteChanged();
                }
            }
        }

        private ChatExchange? _selectedSavedExchange;
        public ChatExchange? SelectedSavedExchange
        {
            get => _selectedSavedExchange;
            set
            {
                if (SetProperty(ref _selectedSavedExchange, value))
                {
                    ((RelayCommand)ViewSavedExchangeCommand).RaiseCanExecuteChanged();
                    ((RelayCommand)DeleteSavedExchangeCommand).RaiseCanExecuteChanged();
                }
            }
        }

        // ===== PROPRIÉTÉS MÉMOIRE/COMPACTION =====

        private int _currentMemorySize;
        public int CurrentMemorySize
        {
            get => _currentMemorySize;
            set => SetProperty(ref _currentMemorySize, value);
        }

        private int _memoryThreshold;
        public int MemoryThreshold
        {
            get => _memoryThreshold;
            set => SetProperty(ref _memoryThreshold, value);
        }

        public int MemoryPercentage => MemoryThreshold > 0 ? Math.Min((int)((double)CurrentMemorySize / MemoryThreshold * 100), 100) : 0;

        public int RemainingMemoryPercentage => Math.Max(100 - MemoryPercentage, 0);

        public bool IsMemoryWarning => MemoryPercentage >= 80 && MemoryPercentage < 100;

        public bool IsMemoryCritical => MemoryPercentage >= 100;

        public bool ShowCompactButton => CurrentMemorySize >= MemoryThreshold && MemoryThreshold > 0;

        public string MemoryStatusText => $"📊 Mémoire : {CurrentMemorySize} / {MemoryThreshold} caractères ({MemoryPercentage}%)";

        // ===== COMMANDES =====

        public ICommand SendMessageCommand { get; }
        public ICommand SaveExchangeCommand { get; }
        public ICommand ViewSavedExchangeCommand { get; }
        public ICommand DeleteSavedExchangeCommand { get; }
        public ICommand CompactMemoryCommand { get; }

        // ===== ÉVÉNEMENTS =====

        public event EventHandler<string>? StatusChanged;
        public event EventHandler? ScrollToEndRequested;
        public event EventHandler<ChatExchange>? SaveExchangeRequested;  // Pour ouvrir le dialog

        // ===== CONSTRUCTEUR =====

        public ChatViewModel(
            OpenAIService openAIService,
            StorageService storageService,
            PatientContextService patientContextService,
            AnonymizationService anonymizationService,
            PromptConfigService promptConfigService,
            LLMGatewayService llmGatewayService,  // ✅ NOUVEAU - Gateway centralisé
            PromptTrackerService? promptTracker = null,
            ChatMemoryService? chatMemoryService = null)
        {
            _openAIService = openAIService ?? throw new ArgumentNullException(nameof(openAIService));
            _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
            _patientContextService = patientContextService ?? throw new ArgumentNullException(nameof(patientContextService));
            _anonymizationService = anonymizationService ?? throw new ArgumentNullException(nameof(anonymizationService));
            _promptConfigService = promptConfigService ?? throw new ArgumentNullException(nameof(promptConfigService));
            _llmGatewayService = llmGatewayService ?? throw new ArgumentNullException(nameof(llmGatewayService));  // ✅ NOUVEAU
            _promptTracker = promptTracker;
            _chatMemoryService = chatMemoryService;

            // Initialiser les commandes
            SendMessageCommand = new RelayCommand(
                execute: _ => SendMessageAsync(),
                canExecute: _ => !string.IsNullOrWhiteSpace(InputText) && !IsSending && _currentPatient != null
            );

            SaveExchangeCommand = new RelayCommand(
                execute: param => SaveExchange(param),
                canExecute: _ => true
            );

            ViewSavedExchangeCommand = new RelayCommand(
                execute: _ => ViewSavedExchange(),
                canExecute: _ => SelectedSavedExchange != null
            );

            DeleteSavedExchangeCommand = new RelayCommand(
                execute: _ => DeleteSavedExchange(),
                canExecute: _ => SelectedSavedExchange != null
            );

            CompactMemoryCommand = new RelayCommand(
                execute: _ => CompactMemoryAsync(),
                canExecute: _ => ShowCompactButton && _currentPatient != null
            );

            // Charger les paramètres de compaction
            var settings = ChatSettings.Load();
            settings.Validate();
            MemoryThreshold = settings.CompactionThreshold;
        }

        // ===== MÉTHODES PUBLIQUES =====

        /// <summary>
        /// Définit le patient courant et charge ses échanges sauvegardés
        /// </summary>
        public void SetCurrentPatient(PatientIndexEntry? patient)
        {
            _currentPatient = patient;
            LoadSavedExchanges();

            // ✅ NOUVEAU : Calculer la taille mémoire au lieu de compacter automatiquement
            if (patient != null)
            {
                var allExchanges = _storageService.GetChatExchanges(patient.NomComplet);
                CurrentMemorySize = allExchanges.Sum(e => (e.Question?.Length ?? 0) + (e.Response?.Length ?? 0));

                // Notifier les changements pour la barre de progression
                OnPropertyChanged(nameof(MemoryPercentage));
                OnPropertyChanged(nameof(RemainingMemoryPercentage));
                OnPropertyChanged(nameof(IsMemoryWarning));
                OnPropertyChanged(nameof(IsMemoryCritical));
                OnPropertyChanged(nameof(ShowCompactButton));
                OnPropertyChanged(nameof(MemoryStatusText));
                ((RelayCommand)CompactMemoryCommand).RaiseCanExecuteChanged();

                RaiseStatusMessage($"✓ Patient chargé - {allExchanges.Count} échanges ({MemoryPercentage}% mémoire)");
            }

            ((RelayCommand)SendMessageCommand).RaiseCanExecuteChanged();
        }

        /// <summary>
        /// Réinitialise le chat (changement de patient ou fermeture)
        /// </summary>
        public void Reset()
        {
            _chatHistory.Clear();
            Messages.Clear();
            SavedExchanges.Clear();
            InputText = "";
            _currentPatient = null;
            SelectedSavedExchange = null;

            // Réinitialiser la mémoire compactée
            _compactedMemory = string.Empty;
            _recentSavedExchanges.Clear();
        }

        // ===== MÉTHODES PRIVÉES =====

        // Message IA en cours de streaming
        private ChatMessageViewModel? _currentStreamingMessage;
        private CancellationTokenSource? _streamingCts;

        /// <summary>
        /// Envoie un message à l'IA via LLMGatewayService avec STREAMING
        /// ✅ REFACTORISÉ : Streaming temps réel sans BusyService
        /// - Provider local (Ollama) : Pas d'anonymisation
        /// - Provider cloud (OpenAI) : Anonymisation 3 phases automatique
        /// </summary>
        private async void SendMessageAsync()
        {
            if (_currentPatient == null || string.IsNullOrWhiteSpace(InputText))
                return;

            var question = InputText.Trim();
            InputText = "";  // Vider immédiatement

            var providerName = _llmGatewayService.GetActiveProviderName();
            var isLocal = _llmGatewayService.IsLocalProvider();

            // CancellationToken pour pouvoir annuler
            _streamingCts?.Cancel();
            _streamingCts = new CancellationTokenSource();
            var cancellationToken = _streamingCts.Token;

            try
            {
                IsSending = true;

                // Ajouter le message utilisateur
                AddMessage("Vous", question, Colors.DarkBlue, isFromAI: false);

                // Afficher le type de provider
                RaiseStatusMessage($"⏳ L'IA réfléchit... ({providerName}{(isLocal ? " - local" : " - cloud")})");

                // Récupérer le contexte patient
                var contextBundle = _patientContextService.GetCompleteContext(
                    _currentPatient.NomComplet,
                    userRequest: null,
                    pseudonym: null
                );

                cancellationToken.ThrowIfCancellationRequested();

                var contextText = contextBundle.ToPromptText(_currentPatient.NomComplet, null);
                var contextInfo = $"{contextBundle.ContextType} ({contextText.Length} caractères)";

                if (string.IsNullOrWhiteSpace(contextBundle.ClinicalContext))
                {
                    AddMessage("Système", "⚠️ Aucune note disponible. L'IA répondra sans contexte patient.", Colors.Gray, isFromAI: false);
                }

                // Récupérer le prompt système
                var chatPrompt = _promptConfigService?.GetActivePrompt("chat_interaction") ?? "";
                var systemPrompt = _promptConfigService?.GetActivePrompt("system_global") ?? "";
                var fullSystemPrompt = $"{systemPrompt}\n\n{chatPrompt}";

                // Construire les messages
                var messages = new List<(string role, string content)>();

                if (!string.IsNullOrWhiteSpace(contextText))
                {
                    messages.Add(("system", $"CONTEXTE PATIENT:\n{contextText}"));
                }

                if (!string.IsNullOrWhiteSpace(_compactedMemory))
                {
                    messages.Add(("system", $"HISTORIQUE COMPACTÉ:\n{_compactedMemory}"));
                }

                foreach (var exchange in _chatHistory)
                {
                    messages.Add(("user", exchange.Question));
                    messages.Add(("assistant", exchange.Response));
                }

                messages.Add(("user", question));

                cancellationToken.ThrowIfCancellationRequested();

                // ✅ STREAMING : Créer un message IA vide AVANT le streaming
                _currentStreamingMessage = new ChatMessageViewModel
                {
                    Author = "IA",
                    Content = "",
                    BorderColor = Colors.DarkGreen,
                    IsFromAI = true,
                    PlainText = "IA\n"  // Texte brut pendant le streaming
                };
                Messages.Add(_currentStreamingMessage);
                ScrollToEndRequested?.Invoke(this, EventArgs.Empty);

                // ✅ STREAMING : Callback pour chaque token
                void OnTokenReceived(string token)
                {
                    var msg = _currentStreamingMessage;
                    if (msg == null) return;

                    // Utiliser InvokeAsync avec priorité Render pour fluidité
                    System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        msg.Content += token;
                        msg.PlainText = $"IA\n{msg.Content}";
                    }, System.Windows.Threading.DispatcherPriority.Render);
                }

                // ✅ APPEL STREAMING VIA GATEWAY
                var (success, result, error) = await _llmGatewayService.ChatStreamAsync(
                    systemPrompt: fullSystemPrompt,
                    messages: messages,
                    onTokenReceived: OnTokenReceived,
                    patientName: _currentPatient.NomComplet,
                    maxTokens: 2000,
                    cancellationToken: cancellationToken
                );

                cancellationToken.ThrowIfCancellationRequested();

                if (success && _currentStreamingMessage != null)
                {
                    // ✅ Synchronisation finale du contenu (si des tokens manqués)
                    var reponse = result;

                    // Logger le prompt
                    if (_promptTracker != null)
                    {
                        _promptTracker.LogPrompt(new PromptLogEntry
                        {
                            Timestamp = DateTime.Now,
                            Module = "Chat",
                            SystemPrompt = fullSystemPrompt,
                            UserPrompt = $"CONTEXTE: {contextText}\n\nQUESTION: {question}",
                            AIResponse = reponse,
                            TokensUsed = 0,
                            LLMProvider = providerName,
                            ModelName = "auto",
                            Success = true,
                            Error = null
                        });
                    }

                    // Ajouter info contexte à la fin
                    if (!string.IsNullOrWhiteSpace(contextBundle.ClinicalContext))
                    {
                        reponse += $"\n\n━━━━━━━━━━━━━━━━━━━━━━━━━\n📎 Contexte : {contextInfo}";
                    }

                    // ✅ Mettre à jour le message final avec Markdown
                    _currentStreamingMessage.Content = reponse;
                    try
                    {
                        _currentStreamingMessage.RichContent = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(reponse);
                        _currentStreamingMessage.PlainText = null;  // Basculer vers RichContent
                    }
                    catch
                    {
                        _currentStreamingMessage.PlainText = $"IA\n{reponse}";
                    }

                    // Ajouter à l'historique temporaire
                    _chatHistory.Add(new ChatExchange
                    {
                        Question = question,
                        Response = result,  // Réponse sans info contexte
                        Timestamp = DateTime.Now
                    });

                    if (_chatHistory.Count > 3)
                    {
                        _chatHistory.RemoveAt(0);
                    }

                    // Assigner l'index pour le bouton 💾
                    _currentStreamingMessage.ExchangeIndex = _chatHistory.Count - 1;

                    RaiseStatusMessage($"✓ Réponse reçue ({providerName})");
                }
                else if (!success)
                {
                    // Supprimer le message vide en cas d'erreur
                    if (_currentStreamingMessage != null && Messages.Contains(_currentStreamingMessage))
                    {
                        Messages.Remove(_currentStreamingMessage);
                    }
                    AddMessage("Erreur", error ?? result, Colors.Red, isFromAI: false);
                    RaiseStatusMessage($"❌ {error ?? result}");
                }
            }
            catch (OperationCanceledException)
            {
                // Supprimer ou marquer le message comme annulé
                if (_currentStreamingMessage != null)
                {
                    if (string.IsNullOrWhiteSpace(_currentStreamingMessage.Content))
                    {
                        Messages.Remove(_currentStreamingMessage);
                    }
                    else
                    {
                        _currentStreamingMessage.Content += "\n\n⚠️ [Réponse interrompue]";
                        _currentStreamingMessage.PlainText = $"IA\n{_currentStreamingMessage.Content}";
                    }
                }
                RaiseStatusMessage("⚠️ Requête annulée");
            }
            catch (Exception ex)
            {
                if (_currentStreamingMessage != null && Messages.Contains(_currentStreamingMessage))
                {
                    Messages.Remove(_currentStreamingMessage);
                }
                AddMessage("Erreur", $"Erreur inattendue: {ex.Message}", Colors.Red, isFromAI: false);
                RaiseStatusMessage($"❌ {ex.Message}");
            }
            finally
            {
                _currentStreamingMessage = null;
                IsSending = false;
            }
        }

        /// <summary>
        /// Ajoute un message dans la collection Messages
        /// </summary>
        private ChatMessageViewModel AddMessage(string author, string content, Color borderColor, bool isFromAI, int? exchangeIndex = null, string? exchangeId = null)
        {
            var message = new ChatMessageViewModel
            {
                Author = author,
                Content = content,
                BorderColor = borderColor,
                IsFromAI = isFromAI,
                ExchangeIndex = exchangeIndex,
                ExchangeId = exchangeId
            };

            // Si message IA : générer le FlowDocument avec Markdown
            if (isFromAI)
            {
                try
                {
                    message.RichContent = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(content);
                }
                catch
                {
                    // Fallback : texte brut
                    message.PlainText = $"{author}\n{content}";
                }
            }
            else
            {
                // Messages utilisateur/système : texte simple
                message.PlainText = $"{author}\n{content}";
            }

            Messages.Add(message);

            // Scroll vers la fin
            ScrollToEndRequested?.Invoke(this, EventArgs.Empty);

            return message;
        }

        /// <summary>
        /// Ajoute un message système (public, pour les appels externes)
        /// </summary>
        public void AddSystemMessage(string author, string content, Color borderColor, bool isFromAI = false)
        {
            AddMessage(author, content, borderColor, isFromAI);
        }

        /// <summary>
        /// Sauvegarde un échange (ouvre le dialog pour saisir l'étiquette)
        /// </summary>
        private void SaveExchange(object? parameter)
        {
            if (parameter is not int exchangeIndex || _currentPatient == null)
                return;

            if (exchangeIndex < 0 || exchangeIndex >= _chatHistory.Count)
            {
                RaiseStatusMessage("❌ Échange introuvable dans l'historique");
                return;
            }

            var exchange = _chatHistory[exchangeIndex];

            // Déclencher l'événement pour que MainWindow ouvre le dialog
            SaveExchangeRequested?.Invoke(this, exchange);
        }

        /// <summary>
        /// Sauvegarde réellement l'échange après saisie de l'étiquette (appelé depuis MainWindow)
        /// </summary>
        public void CompleteSaveExchange(ChatExchange exchange, string etiquette)
        {
            if (_currentPatient == null)
                return;

            exchange.Etiquette = etiquette;

            var (success, message, filePath) = _storageService.SaveChatExchange(_currentPatient.NomComplet, exchange);

            if (success)
            {
                RaiseStatusMessage($"✅ {message}");
                LoadSavedExchanges();  // Recharger la liste
                UpdateMemorySize();     // ✅ NOUVEAU : Mettre à jour la barre de progression
            }
            else
            {
                RaiseStatusMessage($"❌ {message}");
            }
        }

        /// <summary>
        /// Charge les échanges sauvegardés du patient
        /// </summary>
        private void LoadSavedExchanges()
        {
            SavedExchanges.Clear();

            if (_currentPatient == null)
                return;

            // ✅ NOUVEAU : Charger d'abord le résumé compacté s'il existe
            var chatDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "MedCompanion",
                "patients",
                _currentPatient.NomComplet,
                DateTime.Now.Year.ToString(),
                "chat"
            );

            var summaryPath = System.IO.Path.Combine(chatDir, "_compacted_summary.md");

            if (System.IO.File.Exists(summaryPath))
            {
                try
                {
                    var summaryContent = System.IO.File.ReadAllText(summaryPath, Encoding.UTF8);
                    var summaryExchange = new ChatExchange
                    {
                        Id = "_compacted_summary",
                        Timestamp = System.IO.File.GetLastWriteTime(summaryPath),
                        Question = "📚 Historique compacté",
                        Response = summaryContent,
                        Etiquette = "Résumé compacté"
                    };
                    SavedExchanges.Add(summaryExchange);
                }
                catch
                {
                    // Ignorer si impossible de lire
                }
            }

            // Charger les échanges individuels (chat_*.md)
            var exchanges = _storageService.GetChatExchanges(_currentPatient.NomComplet);
            foreach (var exchange in exchanges)
            {
                SavedExchanges.Add(exchange);
            }
        }

        /// <summary>
        /// Affiche un échange sauvegardé dans le chat
        /// </summary>
        private void ViewSavedExchange()
        {
            if (SelectedSavedExchange == null)
                return;

            var exchange = SelectedSavedExchange;

            // Vérifier si l'échange est déjà affiché
            if (Messages.Any(m => m.ExchangeId == exchange.Id))
            {
                RaiseStatusMessage($"✓ Conversation déjà affichée - Scroll vers l'échange du {exchange.Timestamp:dd/MM/yyyy HH:mm}");
                // TODO: Scroll vers le message existant
                return;
            }

            // ✅ NOUVEAU : Vérifier si c'est le résumé compacté
            bool isCompactedSummary = exchange.Id == "_compacted_summary";

            if (isCompactedSummary)
            {
                // Afficher uniquement la synthèse (pas de question)
                var message = AddMessage("📚 Synthèse clinique compactée", exchange.Response, Colors.DarkOrange, isFromAI: true, exchangeId: exchange.Id);
                message.IsArchived = true;  // ✅ Marquer comme archivé pour cacher le bouton 💾
            }
            else
            {
                // Ajouter l'échange normal dans le chat
                var questionMsg = AddMessage("📖 Vous (archivé)", exchange.Question, Colors.DarkBlue, isFromAI: false, exchangeId: exchange.Id);
                var responseMsg = AddMessage("📖 IA (archivé)", exchange.Response, Colors.DarkGreen, isFromAI: true, exchangeId: exchange.Id);
                questionMsg.IsArchived = true;
                responseMsg.IsArchived = true;
            }

            RaiseStatusMessage($"✓ Échange du {exchange.Timestamp:dd/MM/yyyy HH:mm} affiché");
        }

        /// <summary>
        /// Supprime un échange sauvegardé (ou le résumé compacté)
        /// </summary>
        private void DeleteSavedExchange()
        {
            if (SelectedSavedExchange == null || _currentPatient == null)
                return;

            var exchange = SelectedSavedExchange;

            // ✅ NOUVEAU : Vérifier si c'est le résumé compacté
            bool isCompactedSummary = exchange.Id == "_compacted_summary";

            // Message de confirmation adapté
            var confirmMessage = isCompactedSummary
                ? "⚠️ Supprimer la synthèse clinique compactée ?\n\nCette action est irréversible."
                : $"Supprimer cet échange ?\n\nÉtiquette : {exchange.Etiquette}\nDate : {exchange.Timestamp:dd/MM/yyyy HH:mm}";

            var confirmed = System.Windows.MessageBox.Show(
                confirmMessage,
                "Confirmer",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Warning
            );

            if (confirmed == System.Windows.MessageBoxResult.Yes)
            {
                bool success;
                string message;

                if (isCompactedSummary)
                {
                    // ✅ Suppression du résumé compacté
                    var chatDir = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "MedCompanion",
                        "patients",
                        _currentPatient.NomComplet,
                        DateTime.Now.Year.ToString(),
                        "chat"
                    );

                    var summaryPath = System.IO.Path.Combine(chatDir, "_compacted_summary.md");

                    try
                    {
                        if (System.IO.File.Exists(summaryPath))
                        {
                            System.IO.File.Delete(summaryPath);
                            success = true;
                            message = "Synthèse compactée supprimée";
                        }
                        else
                        {
                            success = false;
                            message = "Fichier de synthèse introuvable";
                        }
                    }
                    catch (Exception ex)
                    {
                        success = false;
                        message = $"Erreur lors de la suppression : {ex.Message}";
                    }
                }
                else
                {
                    // Suppression d'un échange normal
                    (success, message) = _storageService.DeleteChatExchange(_currentPatient.NomComplet, exchange.Id);
                }

                if (success)
                {
                    RaiseStatusMessage($"✅ {message}");
                    LoadSavedExchanges();  // Recharger la liste
                    UpdateMemorySize();     // Mettre à jour la barre de progression
                }
                else
                {
                    RaiseStatusMessage($"❌ {message}");
                }
            }
        }

        /// <summary>
        /// Compacte manuellement la mémoire du chat
        /// LOGIQUE SIMPLIFIÉE : Supprime TOUS les échanges et garde uniquement le résumé
        /// </summary>
        private async void CompactMemoryAsync()
        {
            if (_currentPatient == null || _chatMemoryService == null)
                return;

            try
            {
                RaiseStatusMessage("⏳ Compactage de la mémoire en cours...");

                var settings = ChatSettings.Load();
                settings.Validate();

                var allExchanges = _storageService.GetChatExchanges(_currentPatient.NomComplet);

                if (allExchanges.Count == 0)
                {
                    RaiseStatusMessage("⚠️ Aucun échange à compacter");
                    return;
                }

                // Charger le résumé existant s'il y a lieu
                var chatDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "MedCompanion",
                    "patients",
                    _currentPatient.NomComplet,
                    DateTime.Now.Year.ToString(),
                    "chat"
                );
                var summaryPath = System.IO.Path.Combine(chatDir, "_compacted_summary.md");
                
                if (System.IO.File.Exists(summaryPath))
                {
                    try
                    {
                        var summaryContent = await System.Threading.Tasks.Task.Run(() => System.IO.File.ReadAllText(summaryPath, Encoding.UTF8));
                        if (!string.IsNullOrWhiteSpace(summaryContent))
                        {
                            // Créer un échange fictif pour le résumé existant
                            var summaryExchange = new ChatExchange
                            {
                                Id = "_compacted_summary", // ID spécial
                                Timestamp = DateTime.MinValue, // Pour qu'il soit le premier après tri
                                Question = "Résumé précédent",
                                Response = summaryContent,
                                Etiquette = "Résumé"
                            };
                            allExchanges.Insert(0, summaryExchange);
                        }
                    }
                    catch { /* Ignorer erreur lecture */ }
                }

                // Compacter tous les échanges (y compris l'ancien résumé)
                var (wasCompacted, compactedSummary, _) = await _chatMemoryService.CompactIfNeededAsync(
                    allExchanges,
                    settings.CompactionThreshold
                );

                if (wasCompacted)
                {
                    // ✅ NOUVEAU : Supprimer TOUS les fichiers chat_*.md
                    foreach (var exchange in allExchanges)
                    {
                        // Ne pas essayer de supprimer le résumé via StorageService (il est géré à part)
                        if (exchange.Id == "_compacted_summary") continue;

                        _storageService.DeleteChatExchange(_currentPatient.NomComplet, exchange.Id);
                    }

                    // Sauvegarder le résumé compacté dans un fichier _compacted_summary.md
                    // Utiliser le chemin déjà récupéré plus haut
                    var directory = System.IO.Path.GetDirectoryName(summaryPath);
                    if (directory != null && !System.IO.Directory.Exists(directory))
                    {
                        System.IO.Directory.CreateDirectory(directory);
                    }

                    await System.Threading.Tasks.Task.Run(() => System.IO.File.WriteAllText(summaryPath, compactedSummary, Encoding.UTF8));

                    // Recharger et mettre à jour la taille mémoire
                    LoadSavedExchanges();
                    UpdateMemorySize();

                    // Calculer la nouvelle taille du résumé
                    var summarySize = compactedSummary.Length;

                    System.Diagnostics.Debug.WriteLine($"[CompactMemory] {allExchanges.Count} échanges supprimés → 1 résumé ({summarySize} chars)");
                    System.Diagnostics.Debug.WriteLine($"[CompactMemory] Nouvelle taille mémoire: {CurrentMemorySize} / {MemoryThreshold} ({MemoryPercentage}%)");

                    RaiseStatusMessage($"✅ Mémoire compactée : {allExchanges.Count} échanges → 1 résumé ({summarySize} caractères)");
                }
                else
                {
                    RaiseStatusMessage("⚠️ Compactage non nécessaire (mémoire sous le seuil)");
                }
            }
            catch (Exception ex)
            {
                RaiseStatusMessage($"❌ Erreur compactage : {ex.Message}");
            }
        }

        /// <summary>
        /// Met à jour la taille mémoire actuelle
        /// Compte les échanges individuels + le résumé compacté s'il existe
        /// </summary>
        private void UpdateMemorySize()
        {
            if (_currentPatient != null)
            {
                var allExchanges = _storageService.GetChatExchanges(_currentPatient.NomComplet);
                var exchangesSize = allExchanges.Sum(e => (e.Question?.Length ?? 0) + (e.Response?.Length ?? 0));

                // ✅ NOUVEAU : Compter aussi le résumé compacté s'il existe
                var chatDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "MedCompanion",
                    "patients",
                    _currentPatient.NomComplet,
                    DateTime.Now.Year.ToString(),
                    "chat"
                );

                var summaryPath = System.IO.Path.Combine(chatDir, "_compacted_summary.md");
                var summarySize = 0;

                if (System.IO.File.Exists(summaryPath))
                {
                    try
                    {
                        var summaryContent = System.IO.File.ReadAllText(summaryPath, Encoding.UTF8);
                        summarySize = summaryContent.Length;
                    }
                    catch
                    {
                        // Ignorer si impossible de lire
                    }
                }

                CurrentMemorySize = exchangesSize + summarySize;

                OnPropertyChanged(nameof(MemoryPercentage));
                OnPropertyChanged(nameof(RemainingMemoryPercentage));
                OnPropertyChanged(nameof(IsMemoryWarning));
                OnPropertyChanged(nameof(IsMemoryCritical));
                OnPropertyChanged(nameof(ShowCompactButton));
                OnPropertyChanged(nameof(MemoryStatusText));
                ((RelayCommand)CompactMemoryCommand).RaiseCanExecuteChanged();
            }
        }

        /// <summary>
        /// Déclenche l'événement StatusChanged
        /// </summary>
        private void RaiseStatusMessage(string message)
        {
            StatusChanged?.Invoke(this, message);
        }
    }
}
