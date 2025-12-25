using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.ViewModels;

namespace MedCompanion.Views.Chat
{
    /// <summary>
    /// UserControl pour la gestion du Chat avec l'IA
    /// </summary>
    public partial class ChatControl : UserControl
    {
        public ChatViewModel? ChatViewModel { get; private set; }

        public event EventHandler<string>? StatusChanged;
        public event EventHandler<ChatExchange>? SaveExchangeRequested;

        public ChatControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initialise le contrôle avec les services nécessaires
        /// </summary>
        public void Initialize(
            OpenAIService openAIService,
            StorageService storageService,
            PatientContextService patientContextService,
            AnonymizationService anonymizationService,
            PromptConfigService promptConfigService,
            LLMGatewayService llmGatewayService,  // ✅ NOUVEAU - Gateway centralisé
            PromptTrackerService? promptTracker = null,
            ChatMemoryService? chatMemoryService = null)
        {
            ChatViewModel = new ChatViewModel(openAIService, storageService, patientContextService, anonymizationService, promptConfigService, llmGatewayService, promptTracker, chatMemoryService);

            // Connecter les événements du ViewModel
            ChatViewModel.StatusChanged += (s, msg) => StatusChanged?.Invoke(this, msg);
            ChatViewModel.ScrollToEndRequested += (s, e) => ChatScrollViewer.ScrollToEnd();
            ChatViewModel.SaveExchangeRequested += (s, exchange) => SaveExchangeRequested?.Invoke(this, exchange);

            // Raccourci clavier Ctrl+Enter pour envoyer
            ChatInput.KeyDown += ChatInput_KeyDown;

            // Définir le DataContext
            DataContext = ChatViewModel;
        }

        /// <summary>
        /// Définit le patient courant
        /// </summary>
        public void SetCurrentPatient(PatientIndexEntry? patient)
        {
            ChatViewModel?.SetCurrentPatient(patient);
        }

        /// <summary>
        /// Réinitialise le chat (changement de patient)
        /// </summary>
        public void Reset()
        {
            ChatViewModel?.Reset();
        }

        /// <summary>
        /// Complète la sauvegarde d'un échange après saisie de l'étiquette
        /// </summary>
        public void CompleteSaveExchange(ChatExchange exchange, string etiquette)
        {
            ChatViewModel?.CompleteSaveExchange(exchange, etiquette);
        }

        /// <summary>
        /// Raccourci Ctrl+Enter pour envoyer
        /// </summary>
        private void ChatInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                if (ChatViewModel?.SendMessageCommand.CanExecute(null) == true)
                {
                    ChatViewModel.SendMessageCommand.Execute(null);
                }
                e.Handled = true;
            }
        }
    }
}
