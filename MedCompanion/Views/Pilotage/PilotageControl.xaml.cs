using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using MedCompanion.Models;
using MedCompanion.Services;

namespace MedCompanion.Views.Pilotage
{
    /// <summary>
    /// Mode Pilotage - Centre de commande Parent'aile
    /// V3 : 3 onglets (Messages / Patients / Utilisateurs) + auto-archivage + pression temporelle
    /// </summary>
    public partial class PilotageControl : UserControl
    {
        private TokenService? _tokenService;
        private QRCodeService? _qrCodeService;
        private PatientIndexService? _patientIndexService;
        private PilotageAgentService? _pilotageService;
        private FirebaseService? _firebaseService;
        private PilotageAttachmentService? _attachmentService;
        private PilotageMessageCacheService? _messageCache;
        private PilotageEmailService? _emailService;
        private OpenAIService? _openAIService;
        private AppSettings? _settings;

        // Timer pour le polling
        private DispatcherTimer? _pollingTimer;
        private const int POLLING_INTERVAL_MINUTES = 30;

        // Collections
        private List<PatientMessage> _allMessages = new();
        public ObservableCollection<PatientMessage> Messages { get; } = new();
        public ObservableCollection<PilotageAttachment> Attachments { get; } = new();

        // Message sélectionné
        private PatientMessage? _selectedMessage;

        // Compteur nouveaux messages
        private int _newMessagesCount = 0;

        // Onglet Patients : données agrégées
        private List<PatientAggregation> _allPatientAggregations = new();
        public ObservableCollection<PatientAggregation> PatientAggregations { get; } = new();

        // Onglet Utilisateurs : tokens
        public ObservableCollection<PatientToken> Tokens { get; } = new();
        private PatientToken? _selectedToken;

        // Dernière synchro
        private DateTime? _lastSyncTime;

        /// <summary>
        /// Event pour notifier les changements de statut
        /// </summary>
        public event EventHandler<string>? StatusChanged;

        /// <summary>
        /// Event pour demander la navigation vers un dossier patient
        /// </summary>
        public event EventHandler<string>? NavigateToPatientRequested;

        /// <summary>
        /// Event quand de nouveaux messages sont détectés
        /// </summary>
        public event EventHandler<int>? NewMessagesDetected;

        /// <summary>
        /// Déclenché quand un token est créé, révoqué ou modifié (pour rafraîchir le header MainWindow)
        /// </summary>
        public event EventHandler? TokenModified;

        public PilotageControl()
        {
            InitializeComponent();
            MessagesListBox.ItemsSource = Messages;
            AttachmentsListBox.ItemsSource = Attachments;
            PatientsListBox.ItemsSource = PatientAggregations;
            TokensListBox.ItemsSource = Tokens;

            // Rafraîchir l'onglet actif quand le contrôle redevient visible
            IsVisibleChanged += PilotageControl_IsVisibleChanged;
        }

        private void PilotageControl_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is true && _patientIndexService != null)
            {
                // Rafraîchir l'onglet actif quand on revient sur Pilotage
                switch (MainTabControl.SelectedIndex)
                {
                    case 0: RefreshMessages(); break;
                    case 1: RefreshPatientsTab(); break;
                    case 2: RefreshTokensTab(); break;
                }
            }
        }

        /// <summary>
        /// Initialise le contrôle avec les services nécessaires
        /// </summary>
        public void Initialize(
            PatientIndexService patientIndexService,
            PilotageAgentService pilotageService,
            FirebaseService firebaseService,
            OpenAIService openAIService,
            AppSettings settings,
            PathService pathService,
            PilotageAttachmentService? attachmentService = null)
        {
            _tokenService ??= new TokenService();
            _qrCodeService ??= new QRCodeService();
            _patientIndexService = patientIndexService;
            _pilotageService = pilotageService;
            _firebaseService = firebaseService;
            _openAIService = openAIService;
            _settings = settings;
            _attachmentService = attachmentService ?? new PilotageAttachmentService(pathService);
            _messageCache = new PilotageMessageCacheService(pathService);
            _emailService = new PilotageEmailService(settings);

            // Nettoyer les vieux messages (> 90 jours)
            _messageCache.CleanOldMessages(90);

            // Démarrer le polling
            StartPolling();

            OnStatusChanged("Mode Pilotage prêt");
            RefreshMessages();
        }

        /// <summary>
        /// Surcharge pour rétrocompatibilité
        /// </summary>
        public void Initialize(
            PatientIndexService patientIndexService,
            PilotageAgentService pilotageService,
            FirebaseService firebaseService,
            OpenAIService openAIService,
            AppSettings settings)
        {
            _tokenService ??= new TokenService();
            _qrCodeService ??= new QRCodeService();
            _patientIndexService = patientIndexService;
            _pilotageService = pilotageService;
            _firebaseService = firebaseService;
            _openAIService = openAIService;
            _settings = settings;

            OnStatusChanged("Mode Pilotage prêt (sans cache)");
            RefreshMessages();
        }

        /// <summary>
        /// Permet d'injecter le PathService après initialisation
        /// </summary>
        public void SetPathService(PathService pathService)
        {
            _attachmentService = new PilotageAttachmentService(pathService);
            _messageCache = new PilotageMessageCacheService(pathService);
            if (_settings != null)
            {
                _emailService = new PilotageEmailService(_settings);
            }
        }

        #region Polling

        /// <summary>
        /// Démarre le timer de polling (30 min)
        /// </summary>
        private void StartPolling()
        {
            _pollingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(POLLING_INTERVAL_MINUTES)
            };
            _pollingTimer.Tick += async (s, e) => await CheckForNewMessagesAsync();
            _pollingTimer.Start();

            System.Diagnostics.Debug.WriteLine($"[Pilotage] ⏰ Polling démarré: toutes les {POLLING_INTERVAL_MINUTES} minutes");
        }

        /// <summary>
        /// Arrête le polling
        /// </summary>
        public void StopPolling()
        {
            _pollingTimer?.Stop();
            System.Diagnostics.Debug.WriteLine("[Pilotage] ⏹️ Polling arrêté");
        }

        /// <summary>
        /// Vérifie s'il y a de nouveaux messages (appelé par le timer)
        /// </summary>
        private async System.Threading.Tasks.Task CheckForNewMessagesAsync()
        {
            if (_firebaseService == null || _messageCache == null) return;

            try
            {
                System.Diagnostics.Debug.WriteLine("[Pilotage] 🔍 Vérification nouveaux messages...");

                var (firebaseMessages, error) = await _firebaseService.FetchMessagesAsync();

                if (!string.IsNullOrEmpty(error))
                {
                    System.Diagnostics.Debug.WriteLine($"[Pilotage] ⚠️ Erreur polling: {error}");
                    return;
                }

                // Compter les nouveaux messages (pas dans le cache)
                var cachedIds = _messageCache.GetCachedMessageIds();
                var newMessages = firebaseMessages.Where(m => !cachedIds.Contains(m.Id)).ToList();

                if (newMessages.Count > 0)
                {
                    _newMessagesCount = newMessages.Count;

                    // Notifier via l'event
                    NewMessagesDetected?.Invoke(this, _newMessagesCount);

                    // Mettre à jour le statut
                    await Dispatcher.InvokeAsync(() =>
                    {
                        OnStatusChanged($"📬 {_newMessagesCount} nouveau(x) message(s) !");
                    });

                    System.Diagnostics.Debug.WriteLine($"[Pilotage] 📬 {_newMessagesCount} nouveaux messages détectés");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[Pilotage] ✓ Aucun nouveau message");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Pilotage] ❌ Erreur polling: {ex.Message}");
            }
        }

        #endregion

        #region Tab Control

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != MainTabControl) return;

            switch (MainTabControl.SelectedIndex)
            {
                case 0: // Messages
                    SelectionInfoText.Text = _selectedMessage != null
                        ? $"Message de {_selectedMessage.PatientName}"
                        : "Sélectionnez un message pour répondre";
                    break;
                case 1: // Patients
                    RefreshPatientsTab();
                    SelectionInfoText.Text = "Vue longitudinale des patients";
                    break;
                case 2: // Utilisateurs
                    RefreshTokensTab();
                    SelectionInfoText.Text = "Gestion des utilisateurs Parent'aile";
                    break;
            }
        }

        #endregion

        #region Messages Management

        /// <summary>
        /// Rafraîchit la liste des messages (avec cache)
        /// </summary>
        public async void RefreshMessages()
        {
            if (_firebaseService == null || _pilotageService == null) return;

            LoadingOverlay.Visibility = Visibility.Visible;
            OnStatusChanged("Recherche de nouveaux messages...");

            try
            {
                // Synchroniser les tokens avec Firebase (pseudo, statut activé)
                if (_tokenService != null)
                {
                    var synced = await _tokenService.SyncFromFirebaseAsync();
                    if (synced > 0)
                        System.Diagnostics.Debug.WriteLine($"[Pilotage] {synced} token(s) synchronisé(s) depuis Firebase");
                }

                var (firebaseMessages, error) = await _firebaseService.FetchMessagesAsync();

                if (!string.IsNullOrEmpty(error))
                {
                    OnStatusChanged($"Erreur Firebase : {error}");
                }

                // Récupérer les tokens pour faire le lien avec les patients
                var tokens = _tokenService != null ? await _tokenService.GetAllTokensAsync() : new List<PatientToken>();
                var tokenMap = tokens.ToDictionary(t => t.TokenId, t => t);

                _allMessages.Clear();
                int newCount = 0;
                int cachedCount = 0;

                foreach (var msg in firebaseMessages.OrderByDescending(m => m.ReceivedAt))
                {
                    // Lien avec le patient local
                    if (tokenMap.TryGetValue(msg.TokenId, out var token))
                    {
                        msg.PatientId = token.PatientId;
                        msg.PatientName = token.PatientDisplayName;

                        if (string.IsNullOrEmpty(msg.ChildNickname) && !string.IsNullOrEmpty(token.Pseudo))
                        {
                            msg.ChildNickname = token.Pseudo;
                        }
                    }

                    // Vérifier si le message est dans le cache
                    if (_messageCache != null && _messageCache.IsMessageCached(msg.Id))
                    {
                        // ✅ Utiliser les données du cache (évite le traitement LLM)
                        var cachedMsg = _messageCache.GetCachedMessage(msg.Id);
                        if (cachedMsg != null)
                        {
                            // Mettre à jour les champs qui peuvent changer (status)
                            cachedMsg.Status = msg.Status;
                            cachedMsg.PatientId = msg.PatientId;
                            cachedMsg.PatientName = msg.PatientName;

                            _allMessages.Add(cachedMsg);
                            cachedCount++;
                            continue;
                        }
                    }

                    // ⚡ Nouveau message : analyse LLM nécessaire
                    var patientContext = "";
                    await _pilotageService.ProcessMessageAsync(msg, patientContext);

                    // Sauvegarder dans le cache
                    _messageCache?.CacheMessage(msg);

                    _allMessages.Add(msg);
                    newCount++;
                }

                // Appliquer le filtre actuel
                ApplyFilter();

                // Mettre à jour les compteurs
                UpdateCounters();

                // Mettre à jour le badge Messages
                UpdateMessagesBadge();

                // Reset du compteur de nouveaux messages
                _newMessagesCount = 0;

                // Mettre à jour la dernière synchro
                _lastSyncTime = DateTime.Now;
                LastSyncText.Text = $"Dernière synchro: {_lastSyncTime:HH:mm}";

                EmptyStatePanel.Visibility = Messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

                var statusMsg = $"{_allMessages.Count} messages ({cachedCount} en cache, {newCount} nouveaux analysés)";
                OnStatusChanged(statusMsg);
                System.Diagnostics.Debug.WriteLine($"[Pilotage] {statusMsg}");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du rafraîchissement : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                OnStatusChanged("Erreur de rafraîchissement");
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Applique le filtre sélectionné avec tri FIFO intelligent
        /// </summary>
        private void ApplyFilter()
        {
            Messages.Clear();

            var selectedItem = FilterCombo.SelectedItem as ComboBoxItem;
            var filterTag = selectedItem?.Tag?.ToString() ?? "active";

            IEnumerable<PatientMessage> filtered = filterTag switch
            {
                "active" => _allMessages.Where(m => m.Status != "replied"),
                "urgent" => _allMessages.Where(m => m.Urgency == MessageUrgency.Critical || m.Urgency == MessageUrgency.Urgent),
                "moderate" => _allMessages.Where(m => m.Urgency == MessageUrgency.Moderate),
                "replied" => _allMessages.Where(m => m.Status == "replied"),
                _ => _allMessages // "all"
            };

            // Tri FIFO intelligent : urgence décroissante, puis date croissante (plus ancien en premier)
            var sorted = filtered
                .OrderByDescending(m => GetUrgencyWeight(m.Urgency))
                .ThenBy(m => m.CreatedAt);

            foreach (var msg in sorted)
            {
                Messages.Add(msg);
            }

            // Mettre à jour le label d'attente
            var activeCount = _allMessages.Count(m => m.Status != "replied");
            ActiveCountLabel.Text = $"{activeCount} en attente";
        }

        private static int GetUrgencyWeight(MessageUrgency urgency)
        {
            return urgency switch
            {
                MessageUrgency.Critical => 4,
                MessageUrgency.Urgent => 3,
                MessageUrgency.Moderate => 2,
                MessageUrgency.Low => 1,
                _ => 0
            };
        }

        /// <summary>
        /// Met à jour le badge compteur dans l'onglet Messages
        /// </summary>
        private void UpdateMessagesBadge()
        {
            var activeCount = _allMessages.Count(m => m.Status != "replied");
            if (activeCount > 0)
            {
                MessagesBadge.Visibility = Visibility.Visible;
                MessagesBadgeText.Text = activeCount.ToString();
            }
            else
            {
                MessagesBadge.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Met à jour les compteurs dans le header
        /// </summary>
        private void UpdateCounters()
        {
            var urgentCount = _allMessages.Count(m => (m.Urgency == MessageUrgency.Critical || m.Urgency == MessageUrgency.Urgent) && m.Status != "replied");
            var moderateCount = _allMessages.Count(m => m.Urgency == MessageUrgency.Moderate && m.Status != "replied");
            var totalCount = _allMessages.Count(m => m.Status != "replied");

            UrgentCountText.Text = urgentCount.ToString();
            ToSeeCountText.Text = moderateCount.ToString();
            TotalCountText.Text = totalCount.ToString();
        }

        private void FilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_allMessages.Count > 0 || Messages.Count > 0)
                ApplyFilter();
        }

        private void MessagesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MessagesListBox.SelectedItem is PatientMessage selected)
            {
                _selectedMessage = selected;
                ShowMessage(selected);
                LoadAttachmentsForPatient(selected.PatientId);
                UpdateSendButtonsState();
            }
            else
            {
                _selectedMessage = null;
                HideMessageDetail();
                ClearAttachments();
                UpdateSendButtonsState();
            }
        }

        public void ShowMessage(PatientMessage message)
        {
            NoSelectionPanel.Visibility = Visibility.Collapsed;
            MessageContentPanel.Visibility = Visibility.Visible;

            PatientNameText.Text = message.PatientName ?? "Patient inconnu";
            PatientPseudoText.Text = $" ({message.ParentPseudo})";
            MessageDateText.Text = $"Reçu le {message.ReceivedAt:dd/MM/yyyy} à {message.ReceivedAt:HH:mm}";
            MessageContentText.Text = message.Content;

            // Indicateur de pression temporelle dans le header du message
            var hours = (DateTime.Now - message.CreatedAt).TotalHours;
            if (hours < 2)
            {
                TimePressureText.Text = "";
            }
            else if (hours < 8)
            {
                TimePressureText.Text = $"⏰ {(int)hours}h d'attente";
                TimePressureText.Foreground = new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12));
            }
            else
            {
                TimePressureText.Text = $"🔥 {(int)hours}h d'attente";
                TimePressureText.Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
            }

            ParentEmailText.Text = !string.IsNullOrEmpty(message.ParentEmail) ? message.ParentEmail : "Non renseigné";
            AttachmentPatientText.Text = $"Patient: {message.PatientName ?? "—"}";
            SelectionInfoText.Text = $"Message de {message.PatientName}";

            ReplyTextBox.Text = "";

            OnStatusChanged($"Lecture : {message.PatientName}");
        }

        public void HideMessageDetail()
        {
            NoSelectionPanel.Visibility = Visibility.Visible;
            MessageContentPanel.Visibility = Visibility.Collapsed;
            AttachmentPatientText.Text = "Patient: —";
            ParentEmailText.Text = "—";
            TimePressureText.Text = "";
            SelectionInfoText.Text = "Sélectionnez un message pour répondre";
        }

        #endregion

        #region Patients Tab

        /// <summary>
        /// Modèle d'agrégation par patient pour l'onglet Patients
        /// Contient : info patient + token + messages
        /// </summary>
        public class PatientAggregation
        {
            public string PatientId { get; set; } = "";
            public string PatientName { get; set; } = "Inconnu";

            // Token info
            public PatientToken? Token { get; set; }
            public string TokenStatus => Token == null ? "Aucun"
                : Token.Active ? (Token.IsActivated ? "Actif" : "En attente")
                : "Révoqué";
            public string TokenStatusLabel => Token == null ? "Pas de token"
                : Token.Active ? (Token.IsActivated ? $"Actif · {Token.Pseudo}" : "En attente d'activation")
                : "Token révoqué";

            // Messages info
            public int TotalMessages { get; set; }
            public int PendingCount { get; set; }
            public DateTime? LastMessageDate { get; set; }
            public List<PatientMessage> Messages { get; set; } = new();

            public string MessagesSummary => TotalMessages > 0
                ? $"· {TotalMessages} msg" + (PendingCount > 0 ? $" ({PendingCount} en attente)" : "")
                : "";

            public string LastActivityRelative
            {
                get
                {
                    // Utiliser la dernière activité du token ou du dernier message
                    var lastDate = LastMessageDate ?? Token?.LastActivity ?? Token?.CreatedAt;
                    if (lastDate == null) return "";
                    var diff = DateTime.Now - lastDate.Value;
                    if (diff.TotalMinutes < 60) return $"Il y a {(int)diff.TotalMinutes}m";
                    if (diff.TotalHours < 24) return $"Il y a {(int)diff.TotalHours}h";
                    return lastDate.Value.ToString("dd/MM");
                }
            }
        }

        // Patient sélectionné dans l'onglet Patients
        private PatientAggregation? _selectedPatientAgg;

        /// <summary>
        /// Rafraîchit l'onglet Patients avec TOUS les patients MedCompanion
        /// </summary>
        private async void RefreshPatientsTab()
        {
            if (_patientIndexService == null) return;

            _allPatientAggregations.Clear();
            PatientAggregations.Clear();

            try
            {
                // 0. Re-scanner l'index patients (nouveaux patients ajoutés depuis Console)
                await _patientIndexService.ScanAsync();

                // 0b. Synchroniser les tokens avec Firebase
                if (_tokenService != null)
                {
                    await _tokenService.SyncFromFirebaseAsync();
                }

                // 1. Charger TOUS les patients de MedCompanion
                var allPatients = _patientIndexService.GetAllPatients();

                // 2. Charger tous les tokens
                var allTokens = _tokenService != null
                    ? await _tokenService.GetAllTokensAsync()
                    : new List<PatientToken>();
                var tokenByPatientId = allTokens
                    .GroupBy(t => t.PatientId)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.Active).ThenByDescending(t => t.CreatedAt).First());

                // 3. Grouper les messages par PatientId (via TokenId -> PatientId)
                var tokenMap = allTokens.ToDictionary(t => t.TokenId, t => t);
                var messagesByPatientId = new Dictionary<string, List<PatientMessage>>();
                foreach (var msg in _allMessages)
                {
                    var patientId = msg.PatientId;
                    if (string.IsNullOrEmpty(patientId) && !string.IsNullOrEmpty(msg.TokenId) && tokenMap.TryGetValue(msg.TokenId, out var tok))
                    {
                        patientId = tok.PatientId;
                    }
                    if (!string.IsNullOrEmpty(patientId))
                    {
                        if (!messagesByPatientId.ContainsKey(patientId))
                            messagesByPatientId[patientId] = new();
                        messagesByPatientId[patientId].Add(msg);
                    }
                }

                // 4. Créer un PatientAggregation pour chaque patient
                foreach (var patient in allPatients)
                {
                    tokenByPatientId.TryGetValue(patient.Id, out var token);
                    messagesByPatientId.TryGetValue(patient.Id, out var messages);
                    messages ??= new List<PatientMessage>();

                    var agg = new PatientAggregation
                    {
                        PatientId = patient.Id,
                        PatientName = $"{patient.Nom.ToUpperInvariant()} {patient.Prenom}",
                        Token = token,
                        TotalMessages = messages.Count,
                        PendingCount = messages.Count(m => m.Status != "replied"),
                        LastMessageDate = messages.Count > 0 ? messages.Max(m => m.CreatedAt) : null,
                        Messages = messages.OrderByDescending(m => m.CreatedAt).ToList()
                    };

                    _allPatientAggregations.Add(agg);
                }

                // Tri : patients avec messages en attente d'abord, puis token actif, puis alpha
                var sorted = _allPatientAggregations
                    .OrderByDescending(p => p.PendingCount > 0)
                    .ThenByDescending(p => p.Token != null && p.Token.Active)
                    .ThenBy(p => p.PatientName);

                foreach (var p in sorted)
                {
                    PatientAggregations.Add(p);
                }

                NoPatientsPanel.Visibility = PatientAggregations.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Pilotage] Erreur RefreshPatientsTab: {ex.Message}");
            }
        }

        private void PatientSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var searchText = PatientSearchBox.Text?.Trim().ToLowerInvariant() ?? "";

            PatientAggregations.Clear();

            var filtered = string.IsNullOrEmpty(searchText)
                ? _allPatientAggregations
                : _allPatientAggregations.Where(p =>
                    p.PatientName.ToLowerInvariant().Contains(searchText) ||
                    (p.Token?.Pseudo ?? "").ToLowerInvariant().Contains(searchText));

            foreach (var p in filtered
                .OrderByDescending(p => p.PendingCount > 0)
                .ThenByDescending(p => p.Token != null && p.Token.Active)
                .ThenBy(p => p.PatientName))
            {
                PatientAggregations.Add(p);
            }
        }

        private void PatientsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PatientsListBox.SelectedItem is PatientAggregation selected)
            {
                _selectedPatientAgg = selected;
                ShowPatientDetail(selected);
            }
            else
            {
                _selectedPatientAgg = null;
                HidePatientDetail();
            }
        }

        private void ShowPatientDetail(PatientAggregation patient)
        {
            NoPatientSelectedPanel.Visibility = Visibility.Collapsed;
            PatientContentScroll.Visibility = Visibility.Visible;

            PatientDetailName.Text = patient.PatientName;

            // Section Token
            if (patient.Token != null)
            {
                NoTokenPanel.Visibility = Visibility.Collapsed;
                TokenInfoPanel.Visibility = Visibility.Visible;

                // QR Code aperçu
                if (_qrCodeService != null && patient.Token.Active)
                {
                    try
                    {
                        PatientQRCodeImage.Source = _qrCodeService.GenerateQRCode(patient.Token.TokenId);
                    }
                    catch { PatientQRCodeImage.Source = null; }
                }
                else
                {
                    PatientQRCodeImage.Source = null;
                }

                // Infos token
                PatientTokenStatus.Text = patient.Token.StatusDisplay;
                PatientTokenStatus.Foreground = patient.Token.StatusDisplay switch
                {
                    "Actif" => new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60)),
                    "En attente d'activation" => new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12)),
                    "Révoqué" => new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)),
                    _ => new SolidColorBrush(Colors.Gray)
                };
                PatientTokenId.Text = patient.Token.TokenId;
                PatientTokenPseudo.Text = patient.Token.Pseudo ?? "—";
                PatientTokenCreatedAt.Text = patient.Token.CreatedAt.ToString("dd/MM/yyyy HH:mm");

                // Boutons
                PrintTokenBtn.IsEnabled = patient.Token.Active;
                ShowPatientQRBtn.IsEnabled = patient.Token.Active;
                RevokePatientTokenBtn.IsEnabled = patient.Token.Active;

                // Bouton régénération visible uniquement si token révoqué
                RegenerateTokenBtn.Visibility = !patient.Token.Active
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                PatientDetailPseudo.Text = !string.IsNullOrEmpty(patient.Token.Pseudo)
                    ? $"Pseudo Parent'aile: {patient.Token.Pseudo}"
                    : "Token généré, en attente d'activation par le parent";
            }
            else
            {
                NoTokenPanel.Visibility = Visibility.Visible;
                TokenInfoPanel.Visibility = Visibility.Collapsed;
                PatientDetailPseudo.Text = "Pas de token Parent'aile";
            }

            // Stats
            PatientStatTotal.Text = patient.TotalMessages.ToString();
            PatientStatPending.Text = patient.PendingCount.ToString();
            PatientStatLastContact.Text = patient.LastMessageDate?.ToString("dd/MM/yyyy HH:mm") ?? "—";

            // Historique messages
            if (patient.Messages.Count > 0)
            {
                NoPatientMessagesPanel.Visibility = Visibility.Collapsed;
                PatientMessagesListBox.Visibility = Visibility.Visible;
                PatientMessagesListBox.ItemsSource = patient.Messages;
            }
            else
            {
                NoPatientMessagesPanel.Visibility = Visibility.Visible;
                PatientMessagesListBox.Visibility = Visibility.Collapsed;
                PatientMessagesListBox.ItemsSource = null;
            }

            SelectionInfoText.Text = $"Patient: {patient.PatientName}" +
                (patient.TotalMessages > 0 ? $" — {patient.TotalMessages} messages" : "") +
                (patient.Token != null ? $" — Token: {patient.Token.StatusDisplay}" : " — Pas de token");
        }

        private void HidePatientDetail()
        {
            NoPatientSelectedPanel.Visibility = Visibility.Visible;
            PatientContentScroll.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Affiche le détail d'un message sélectionné dans l'historique patient
        /// </summary>
        private void PatientMessagesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PatientMessagesListBox.SelectedItem is PatientMessage msg)
            {
                // Date
                PatientMsgDetailDate.Text = $"Reçu le {msg.CreatedAt:dd/MM/yyyy} à {msg.CreatedAt:HH:mm}";

                // Contenu complet du message parent
                PatientMsgDetailContent.Text = msg.Content;

                // Réponse du médecin
                if (msg.HasReply)
                {
                    PatientMsgReplySection.Visibility = Visibility.Visible;
                    PatientMsgNoReplySection.Visibility = Visibility.Collapsed;
                    PatientMsgReplyContent.Text = msg.ReplyContent;
                    PatientMsgReplyDate.Text = msg.RepliedAt.HasValue
                        ? $"Répondu le {msg.RepliedAt.Value:dd/MM/yyyy} à {msg.RepliedAt.Value:HH:mm}"
                        : "Répondu";
                }
                else if (msg.Status == "replied")
                {
                    // Statut replied mais pas de contenu de réponse stocké
                    PatientMsgReplySection.Visibility = Visibility.Collapsed;
                    PatientMsgNoReplySection.Visibility = Visibility.Visible;
                    PatientMsgNoReplySection.Children.Clear();
                    PatientMsgNoReplySection.Children.Add(new System.Windows.Controls.TextBlock
                    {
                        Text = "Réponse envoyée (contenu non disponible)",
                        FontSize = 12,
                        FontStyle = FontStyles.Italic,
                        Foreground = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0x27, 0xAE, 0x60))
                    });
                }
                else
                {
                    PatientMsgReplySection.Visibility = Visibility.Collapsed;
                    PatientMsgNoReplySection.Visibility = Visibility.Visible;
                }

                PatientMsgDetailPanel.Visibility = Visibility.Visible;
            }
            else
            {
                PatientMsgDetailPanel.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Ferme le panneau de détail du message
        /// </summary>
        private void ClosePatientMsgDetail_Click(object sender, RoutedEventArgs e)
        {
            PatientMsgDetailPanel.Visibility = Visibility.Collapsed;
            PatientMessagesListBox.SelectedItem = null;
        }

        private void PatientViewDossier_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPatientAgg != null)
            {
                NavigateToPatientRequested?.Invoke(this, _selectedPatientAgg.PatientName);
            }
        }

        /// <summary>
        /// Générer un token pour le patient sélectionné
        /// </summary>
        private async void GenerateTokenBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPatientAgg == null || _tokenService == null) return;

            // Vérifier qu'il n'y a pas déjà un token actif
            if (await _tokenService.HasActiveTokenAsync(_selectedPatientAgg.PatientId))
            {
                MessageBox.Show("Ce patient a déjà un token actif.", "Token existant", MessageBoxButton.OK, MessageBoxImage.Information);
                RefreshPatientsTab();
                return;
            }

            try
            {
                GenerateTokenBtn.IsEnabled = false;
                OnStatusChanged($"Génération du token pour {_selectedPatientAgg.PatientName}...");

                var (token, firebaseOk, firebaseError) = await _tokenService.CreateTokenAsync(
                    _selectedPatientAgg.PatientId,
                    _selectedPatientAgg.PatientName);

                if (!firebaseOk)
                {
                    MessageBox.Show(
                        $"Token créé localement mais Firebase a échoué :\n{firebaseError}\n\nLe token fonctionnera quand Firebase sera accessible.",
                        "Attention",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    // Proposer d'imprimer directement
                    var result = MessageBox.Show(
                        $"Token généré avec succès !\n\nToken: {token.TokenId}\n\nVoulez-vous l'imprimer maintenant ?",
                        "Token créé",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        PrintToken(token);
                    }
                }

                // Sauvegarder l'ID avant refresh (RefreshPatientsTab vide la sélection)
                var patientIdToReselect = _selectedPatientAgg.PatientId;
                var patientName = _selectedPatientAgg.PatientName;

                OnStatusChanged($"Token généré pour {patientName}");
                TokenModified?.Invoke(this, EventArgs.Empty);
                RefreshPatientsTab();

                // Re-sélectionner le patient après refresh
                var reselect = PatientAggregations.FirstOrDefault(p => p.PatientId == patientIdToReselect);
                if (reselect != null)
                {
                    PatientsListBox.SelectedItem = reselect;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur: {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                GenerateTokenBtn.IsEnabled = true;
            }
        }

        /// <summary>
        /// Imprimer le token du patient sélectionné (génère PDF et l'ouvre)
        /// </summary>
        private void PrintTokenBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPatientAgg?.Token == null) return;
            PrintToken(_selectedPatientAgg.Token);
        }

        /// <summary>
        /// Génère le PDF du token et l'ouvre pour impression
        /// </summary>
        private void PrintToken(PatientToken token)
        {
            try
            {
                var pdfService = new TokenPdfService();

                if (pdfService.TemplateExists())
                {
                    var pdfPath = pdfService.GenerateTokenPdf(
                        token.TokenId,
                        token.PatientDisplayName,
                        token.CreatedAt);

                    // Ouvrir le PDF avec l'application par défaut
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = pdfPath,
                        UseShellExecute = true
                    });

                    OnStatusChanged($"PDF du token généré : {System.IO.Path.GetFileName(pdfPath)}");
                }
                else
                {
                    // Fallback : impression simple via WPF PrintDialog
                    PrintTokenSimple(token);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur d'impression : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Impression simple (fallback si pas de template PDF)
        /// </summary>
        private void PrintTokenSimple(PatientToken token)
        {
            if (_qrCodeService == null) return;

            try
            {
                var printDialog = new System.Windows.Controls.PrintDialog();
                if (printDialog.ShowDialog() == true)
                {
                    var doc = new System.Windows.Documents.FlowDocument();
                    doc.PagePadding = new Thickness(50);

                    var titlePara = new System.Windows.Documents.Paragraph(
                        new System.Windows.Documents.Run("Parent'aile"))
                    {
                        FontSize = 28,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22)),
                        TextAlignment = TextAlignment.Center
                    };
                    doc.Blocks.Add(titlePara);

                    var qrImage = _qrCodeService.GenerateQRCode(token.TokenId);
                    var image = new System.Windows.Controls.Image { Source = qrImage, Width = 250, Height = 250 };
                    var imageContainer = new System.Windows.Documents.BlockUIContainer(image);
                    doc.Blocks.Add(imageContainer);

                    var infoPara = new System.Windows.Documents.Paragraph();
                    infoPara.TextAlignment = TextAlignment.Center;
                    infoPara.Inlines.Add(new System.Windows.Documents.Run($"\nPatient: {token.PatientDisplayName}\n") { FontSize = 14, FontWeight = FontWeights.Bold });
                    infoPara.Inlines.Add(new System.Windows.Documents.Run($"Token: {token.TokenId}\n") { FontSize = 12, FontFamily = new FontFamily("Consolas") });
                    infoPara.Inlines.Add(new System.Windows.Documents.Run($"\nScannez ce QR code avec votre téléphone\npour accéder à l'espace Parent'aile") { FontSize = 11 });
                    doc.Blocks.Add(infoPara);

                    var writer = (System.Windows.Documents.IDocumentPaginatorSource)doc;
                    printDialog.PrintDocument(writer.DocumentPaginator, $"Token - {token.PatientDisplayName}");

                    OnStatusChanged($"Token imprimé pour {token.PatientDisplayName}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur d'impression : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Afficher le QR Code en grand dans une fenêtre popup
        /// </summary>
        private void ShowPatientQRBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPatientAgg?.Token == null || _qrCodeService == null) return;

            try
            {
                var token = _selectedPatientAgg.Token;
                var qrImage = _qrCodeService.GenerateQRCode(token.TokenId);
                var dialog = new Window
                {
                    Title = $"QR Code — {token.PatientDisplayName}",
                    Width = 400,
                    Height = 450,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Window.GetWindow(this),
                    Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E))
                };

                var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                stack.Children.Add(new TextBlock
                {
                    Text = token.PatientDisplayName,
                    Foreground = Brushes.White,
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 10)
                });
                stack.Children.Add(new System.Windows.Controls.Image
                {
                    Source = qrImage,
                    Width = 300,
                    Height = 300
                });
                stack.Children.Add(new TextBlock
                {
                    Text = token.TokenId,
                    Foreground = Brushes.Gray,
                    FontSize = 12,
                    FontFamily = new FontFamily("Consolas"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 0)
                });

                dialog.Content = stack;
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur: {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Révoquer le token du patient sélectionné
        /// </summary>
        private async void RevokePatientTokenBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPatientAgg?.Token == null || _tokenService == null) return;

            var token = _selectedPatientAgg.Token;
            var result = MessageBox.Show(
                $"Révoquer le token de {token.PatientDisplayName} ?\n\nLe parent ne pourra plus envoyer de messages.",
                "Confirmer la révocation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                var (firebaseOk, firebaseError) = await _tokenService.RevokeTokenAsync(token.TokenId);
                MessageBox.Show(
                    firebaseOk ? "Token révoqué avec succès." : $"Token révoqué localement. Firebase: {firebaseError}",
                    "Révocation",
                    MessageBoxButton.OK,
                    firebaseOk ? MessageBoxImage.Information : MessageBoxImage.Warning);

                OnStatusChanged($"Token {token.PatientDisplayName} révoqué");
                TokenModified?.Invoke(this, EventArgs.Empty);

                // Rafraîchir et re-sélectionner
                var patientId = _selectedPatientAgg.PatientId;
                RefreshPatientsTab();
                var reselect = PatientAggregations.FirstOrDefault(p => p.PatientId == patientId);
                if (reselect != null) PatientsListBox.SelectedItem = reselect;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur: {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Générer un nouveau token après révocation de l'ancien
        /// </summary>
        private async void RegenerateTokenBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedPatientAgg == null || _tokenService == null) return;

            var patientId = _selectedPatientAgg.PatientId;
            var patientName = _selectedPatientAgg.PatientName;
            var oldToken = _selectedPatientAgg.Token;

            try
            {
                RegenerateTokenBtn.IsEnabled = false;
                OnStatusChanged($"Suppression de l'ancien token et génération d'un nouveau pour {patientName}...");

                // Supprimer l'ancien token révoqué pour permettre la création d'un nouveau
                if (oldToken != null)
                {
                    await _tokenService.DeleteTokenAsync(oldToken.TokenId);
                }

                // Générer le nouveau token
                var (token, firebaseOk, firebaseError) = await _tokenService.CreateTokenAsync(patientId, patientName);

                if (!firebaseOk)
                {
                    MessageBox.Show(
                        $"Token créé localement mais Firebase a échoué :\n{firebaseError}\n\nLe token fonctionnera quand Firebase sera accessible.",
                        "Attention",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                else
                {
                    var result = MessageBox.Show(
                        $"Nouveau token généré avec succès !\n\nToken: {token.TokenId}\n\nVoulez-vous l'imprimer maintenant ?",
                        "Token créé",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        PrintToken(token);
                    }
                }

                OnStatusChanged($"Nouveau token généré pour {patientName}");
                TokenModified?.Invoke(this, EventArgs.Empty);
                RefreshPatientsTab();

                // Re-sélectionner le patient après refresh
                var reselect = PatientAggregations.FirstOrDefault(p => p.PatientId == patientId);
                if (reselect != null)
                {
                    PatientsListBox.SelectedItem = reselect;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur: {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                RegenerateTokenBtn.IsEnabled = true;
            }
        }

        #endregion

        #region Utilisateurs Tab

        /// <summary>
        /// Rafraîchit l'onglet Utilisateurs
        /// </summary>
        private async void RefreshTokensTab()
        {
            if (_tokenService == null) return;

            try
            {
                var allTokens = await _tokenService.GetAllTokensAsync();
                Tokens.Clear();

                foreach (var token in allTokens)
                {
                    Tokens.Add(token);
                }

                UpdateTokenStats();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Pilotage] Erreur chargement tokens: {ex.Message}");
            }
        }

        private void UpdateTokenStats()
        {
            var actifs = Tokens.Count(t => t.Active && t.IsActivated);
            var enAttente = Tokens.Count(t => t.Active && !t.IsActivated);
            var revoques = Tokens.Count(t => !t.Active);

            StatActifs.Text = $"{actifs} actif{(actifs > 1 ? "s" : "")}";
            StatEnAttente.Text = $"{enAttente} en attente";
            StatRevoques.Text = $"{revoques} r\u00e9voqu\u00e9{(revoques > 1 ? "s" : "")}";
        }

        private void TokensListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TokensListBox.SelectedItem is PatientToken selected)
            {
                _selectedToken = selected;
                ShowUserDetail(selected);
            }
            else
            {
                _selectedToken = null;
                HideUserDetail();
            }
        }

        private void ShowUserDetail(PatientToken token)
        {
            NoUserSelectedPanel.Visibility = Visibility.Collapsed;
            UserContentPanel.Visibility = Visibility.Visible;

            UserDetailName.Text = token.PatientDisplayName;
            UserDetailPseudo.Text = token.Pseudo ?? "—";
            UserDetailTokenId.Text = token.TokenId;
            UserDetailCreatedAt.Text = token.CreatedAt.ToString("dd/MM/yyyy HH:mm");

            // Statut avec couleur
            UserDetailStatus.Text = token.StatusDisplay;
            UserDetailStatus.Foreground = token.StatusDisplay switch
            {
                "Actif" => new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60)),
                "En attente d'activation" => new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12)),
                "Révoqué" => new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)),
                _ => new SolidColorBrush(Colors.Gray)
            };

            // Stats : nombre de messages pour ce token
            var tokenMessages = _allMessages.Where(m => m.TokenId == token.TokenId).ToList();
            UserStatMessages.Text = tokenMessages.Count.ToString();
            UserStatLastActivity.Text = token.LastActivity?.ToString("dd/MM/yyyy") ?? "—";

            // État des boutons
            RevokeTokenBtn.IsEnabled = token.Active;
            DeleteTokenBtn.IsEnabled = true;
            ShowQRBtn.IsEnabled = token.Active;

            SelectionInfoText.Text = $"Utilisateur: {token.PatientDisplayName}";
        }

        private void HideUserDetail()
        {
            NoUserSelectedPanel.Visibility = Visibility.Visible;
            UserContentPanel.Visibility = Visibility.Collapsed;
        }

        private void NewTokenBtn_Click(object sender, RoutedEventArgs e)
        {
            TokensBtn_Click(sender, e);
        }

        private void ShowQRBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedToken == null || _qrCodeService == null) return;

            try
            {
                var qrImage = _qrCodeService.GenerateQRCode(_selectedToken.TokenId);
                var dialog = new Window
                {
                    Title = $"QR Code — {_selectedToken.PatientDisplayName}",
                    Width = 400,
                    Height = 450,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Owner = Window.GetWindow(this),
                    Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E))
                };

                var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                stack.Children.Add(new TextBlock
                {
                    Text = _selectedToken.PatientDisplayName,
                    Foreground = Brushes.White,
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 10)
                });
                stack.Children.Add(new System.Windows.Controls.Image
                {
                    Source = qrImage,
                    Width = 300,
                    Height = 300
                });
                stack.Children.Add(new TextBlock
                {
                    Text = _selectedToken.TokenId,
                    Foreground = Brushes.Gray,
                    FontSize = 12,
                    FontFamily = new FontFamily("Consolas"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 10, 0, 0)
                });

                dialog.Content = stack;
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur: {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RevokeTokenBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedToken == null || _tokenService == null) return;

            var result = MessageBox.Show(
                $"Révoquer le token de {_selectedToken.PatientDisplayName} ?\n\nLe parent ne pourra plus envoyer de messages.",
                "Confirmer la révocation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                var (firebaseOk, firebaseError) = await _tokenService.RevokeTokenAsync(_selectedToken.TokenId);
                MessageBox.Show(
                    firebaseOk ? "Token révoqué avec succès." : $"Token révoqué localement. Firebase: {firebaseError}",
                    "Révocation",
                    MessageBoxButton.OK,
                    firebaseOk ? MessageBoxImage.Information : MessageBoxImage.Warning);

                RefreshTokensTab();
                TokenModified?.Invoke(this, EventArgs.Empty);
                OnStatusChanged($"Token {_selectedToken.PatientDisplayName} révoqué");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur: {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void DeleteTokenBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedToken == null || _tokenService == null) return;

            var result = MessageBox.Show(
                $"Supprimer définitivement le token de {_selectedToken.PatientDisplayName} ?\n\nCette action est irréversible.",
                "Confirmer la suppression",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                var (firebaseOk, firebaseError) = await _tokenService.DeleteTokenAsync(_selectedToken.TokenId);
                MessageBox.Show(
                    firebaseOk ? "Token supprimé avec succès." : $"Token supprimé localement. Firebase: {firebaseError}",
                    "Suppression",
                    MessageBoxButton.OK,
                    firebaseOk ? MessageBoxImage.Information : MessageBoxImage.Warning);

                _selectedToken = null;
                HideUserDetail();
                RefreshTokensTab();
                TokenModified?.Invoke(this, EventArgs.Empty);
                OnStatusChanged("Token supprimé");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur: {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void NotifyUserBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedToken == null || _firebaseService == null || !_firebaseService.IsConfigured) return;

            var recipientName = _selectedToken.Pseudo ?? _selectedToken.PatientDisplayName;
            var dialog = new Dialogs.QuickReplyDialog(recipientName, isBroadcast: false, openAIService: _openAIService);
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.SelectedTitle))
            {
                try
                {
                    var notification = new PilotageNotification
                    {
                        Type = dialog.SelectedType,
                        Title = dialog.SelectedTitle,
                        Body = dialog.SelectedBody ?? "",
                        TargetParentId = _selectedToken.TokenId,
                        TokenId = _selectedToken.TokenId,
                        SenderName = _settings?.Medecin ?? "Le cabinet"
                    };

                    var (success, error) = await _firebaseService.WriteNotificationAsync(notification);

                    if (success)
                    {
                        MessageBox.Show($"Notification envoyée à {recipientName}!", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                        OnStatusChanged($"Notification envoyée à {recipientName}");
                    }
                    else
                    {
                        MessageBox.Show($"Erreur: {error}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erreur: {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Attachments Management

        private void LoadAttachmentsForPatient(string? patientId)
        {
            Attachments.Clear();

            if (string.IsNullOrEmpty(patientId) || _attachmentService == null)
            {
                NoAttachmentsPanel.Visibility = Visibility.Visible;
                return;
            }

            var attachments = _attachmentService.GetAttachmentsForPatient(patientId);

            foreach (var att in attachments)
            {
                Attachments.Add(att);
            }

            NoAttachmentsPanel.Visibility = Attachments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void ClearAttachments()
        {
            Attachments.Clear();
            NoAttachmentsPanel.Visibility = Visibility.Visible;
        }

        private async void AddAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedMessage == null || string.IsNullOrEmpty(_selectedMessage.PatientId))
            {
                MessageBox.Show("Veuillez d'abord sélectionner un message.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_attachmentService == null)
            {
                MessageBox.Show("Service de pièces jointes non initialisé.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                var attachment = await _attachmentService.AddManualFileAsync(_selectedMessage.PatientId);
                if (attachment != null)
                {
                    Attachments.Add(attachment);
                    NoAttachmentsPanel.Visibility = Visibility.Collapsed;
                    OnStatusChanged($"Document ajouté: {attachment.FileName}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'ajout: {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string filePath && _attachmentService != null)
            {
                _attachmentService.RemoveAttachment(filePath);
                var toRemove = Attachments.FirstOrDefault(a => a.FilePath == filePath);
                if (toRemove != null)
                {
                    Attachments.Remove(toRemove);
                }

                NoAttachmentsPanel.Visibility = Attachments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                OnStatusChanged("Document retiré");
            }
        }

        public async void AddDocumentFromModule(string filePath, string patientId, string documentType)
        {
            if (_attachmentService == null)
            {
                System.Diagnostics.Debug.WriteLine("[Pilotage] AttachmentService non initialisé");
                return;
            }

            try
            {
                var attachment = await _attachmentService.AddAttachmentAsync(filePath, patientId, documentType);
                OnStatusChanged($"📎 Document ajouté au Pilotage: {attachment.FileName}");

                if (_selectedMessage?.PatientId == patientId)
                {
                    LoadAttachmentsForPatient(patientId);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Pilotage] Erreur ajout document: {ex.Message}");
            }
        }

        #endregion

        #region Send Actions

        private void UpdateSendButtonsState()
        {
            bool hasMessage = _selectedMessage != null;
            bool hasEmail = !string.IsNullOrEmpty(_selectedMessage?.ParentEmail);

            SendFirebaseBtn.IsEnabled = hasMessage;
            SendEmailBtn.IsEnabled = hasMessage && hasEmail;
        }

        private async void SendReplyBtn_Click(object sender, RoutedEventArgs e)
        {
            var replyText = ReplyTextBox.Text.Trim();

            if (_selectedMessage == null) return;

            if (string.IsNullOrEmpty(replyText))
            {
                MessageBox.Show("Veuillez saisir une réponse.", "Réponse vide", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_firebaseService == null) return;

            OnStatusChanged("Envoi de la réponse...");
            try
            {
                var (success, error) = await _firebaseService.UpdateMessageReplyAsync(_selectedMessage.Id, replyText);
                if (success)
                {
                    // Mettre à jour le cache
                    _messageCache?.UpdateMessageStatus(_selectedMessage.Id, "replied");

                    MessageBox.Show("Réponse envoyée avec succès via Firebase.", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                    ReplyTextBox.Text = "";
                    RefreshMessages();
                }
                else
                {
                    MessageBox.Show($"Erreur lors de l'envoi : {error}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void SendEmailBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedMessage == null) return;

            var replyText = ReplyTextBox.Text.Trim();
            if (string.IsNullOrEmpty(replyText))
            {
                MessageBox.Show("Veuillez saisir une réponse.", "Réponse vide", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(_selectedMessage.ParentEmail))
            {
                MessageBox.Show("L'email du parent n'est pas renseigné.", "Email manquant", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_emailService == null)
            {
                MessageBox.Show("Service email non initialisé.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Vérifier la configuration SMTP
            if (!_emailService.IsConfigured())
            {
                MessageBox.Show(
                    "La configuration SMTP n'est pas complète.\n\n" +
                    "Veuillez configurer le mot de passe SMTP dans les Paramètres.",
                    "Configuration requise",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var selectedAttachments = Attachments.Where(a => a.IsSelected).ToList();
            var attachmentPaths = selectedAttachments.Select(a => a.FilePath).ToList();

            var attachmentsList = selectedAttachments.Count > 0
                ? string.Join("\n", selectedAttachments.Select(a => $"  - {a.FileName}"))
                : "  (Aucune)";

            // Confirmation avant envoi
            var confirmMessage = $"Envoyer un email à {_selectedMessage.ParentEmail} ?\n\n" +
                                 $"Pièces jointes:\n{attachmentsList}";

            var result = MessageBox.Show(confirmMessage, "Confirmer l'envoi", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;

            // Préparer l'email
            var childNickname = _selectedMessage.ChildNickname ?? _selectedMessage.PatientName ?? "votre enfant";
            var subject = _emailService.GenerateSubject(childNickname);
            var body = _emailService.ComposeEmailBody(replyText, childNickname);

            OnStatusChanged("Envoi de l'email en cours...");
            SendEmailBtn.IsEnabled = false;

            try
            {
                var (success, error) = await _emailService.SendEmailAsync(
                    _selectedMessage.ParentEmail,
                    subject,
                    body,
                    attachmentPaths.Count > 0 ? attachmentPaths : null);

                if (success)
                {
                    // Mettre à jour le statut du message (cache local)
                    _messageCache?.UpdateMessageStatus(_selectedMessage.Id, "replied");

                    // Mettre à jour le statut dans Firebase (sans stocker le contenu email - juste un marqueur)
                    if (_firebaseService != null && _firebaseService.IsConfigured)
                    {
                        await _firebaseService.UpdateMessageReplyAsync(_selectedMessage.Id, "[Réponse envoyée par email]");
                    }

                    // Créer une notification EmailReply pour le parent
                    if (_firebaseService != null && _firebaseService.IsConfigured)
                    {
                        var notification = new PilotageNotification
                        {
                            Type = NotificationType.EmailReply,
                            Title = "Votre médecin vous a répondu",
                            Body = "Vérifiez votre boîte mail pour consulter la réponse.",
                            TargetParentId = _selectedMessage.TokenId ?? "",
                            TokenId = _selectedMessage.TokenId,
                            ReplyToMessageId = _selectedMessage.Id,
                            SenderName = _settings?.Medecin ?? "Le cabinet"
                        };
                        await _firebaseService.WriteNotificationAsync(notification);
                    }

                    // Vider les pièces jointes pour ce patient
                    if (_attachmentService != null && !string.IsNullOrEmpty(_selectedMessage.PatientId))
                    {
                        _attachmentService.ClearPatientAttachments(_selectedMessage.PatientId);
                        LoadAttachmentsForPatient(_selectedMessage.PatientId);
                    }

                    MessageBox.Show(
                        $"Email envoyé avec succès à {_selectedMessage.ParentEmail}",
                        "Succès",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    ReplyTextBox.Text = "";
                    OnStatusChanged("Email envoyé avec succès");
                    RefreshMessages();
                }
                else
                {
                    MessageBox.Show(
                        $"Erreur lors de l'envoi de l'email:\n\n{error}",
                        "Erreur d'envoi",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    OnStatusChanged($"Erreur: {error}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur: {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                OnStatusChanged($"Erreur: {ex.Message}");
            }
            finally
            {
                UpdateSendButtonsState();
            }
        }

        #endregion

        #region Other Event Handlers

        private void TokensBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_tokenService == null || _qrCodeService == null)
            {
                MessageBox.Show("Services non initialisés.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var patientInfos = GetAvailablePatients();
            var dialog = new TokenManagerDialog(_tokenService, _qrCodeService, patientInfos);
            dialog.Owner = Window.GetWindow(this);
            dialog.ShowDialog();

            // Rafraîchir l'onglet utilisateurs si on est dessus
            if (MainTabControl.SelectedIndex == 2)
            {
                RefreshTokensTab();
            }

            // Notifier MainWindow pour rafraîchir le rectangle token
            TokenModified?.Invoke(this, EventArgs.Empty);

            OnStatusChanged("Gestion des tokens");
        }

        private List<TokenManagerDialog.PatientInfo> GetAvailablePatients()
        {
            var result = new List<TokenManagerDialog.PatientInfo>();

            if (_patientIndexService != null)
            {
                try
                {
                    var patients = _patientIndexService.GetAllPatients();
                    result = patients.Select(p => new TokenManagerDialog.PatientInfo
                    {
                        PatientId = p.Id,
                        DisplayName = $"{p.Nom.ToUpperInvariant()} {p.Prenom}"
                    }).OrderBy(p => p.DisplayName).ToList();
                }
                catch (Exception ex)
                {
                    OnStatusChanged($"Erreur chargement patients: {ex.Message}");
                }
            }

            return result;
        }

        private void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            RefreshMessages();
        }

        private void ViewDossierBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedMessage != null && !string.IsNullOrEmpty(_selectedMessage.PatientName))
            {
                NavigateToPatientRequested?.Invoke(this, _selectedMessage.PatientName);
            }
            else
            {
                MessageBox.Show("Le nom du patient n'est pas lié à ce message.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void SuggestionBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedMessage != null)
            {
                if (!string.IsNullOrEmpty(_selectedMessage.SuggestedResponse))
                {
                    ReplyTextBox.Text = _selectedMessage.SuggestedResponse;
                    OnStatusChanged("Suggestion appliquée");
                    UpdateSendButtonsState();
                }
                else
                {
                    MessageBox.Show("L'IA n'a pas encore généré de suggestion pour ce message.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        #endregion

        #region Notification Handlers

        /// <summary>
        /// Envoie une notification broadcast à tous les parents
        /// </summary>
        private async void BroadcastBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_firebaseService == null || !_firebaseService.IsConfigured)
            {
                MessageBox.Show("Firebase n'est pas configuré.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Ouvrir le dialogue en mode broadcast
            var dialog = new Dialogs.QuickReplyDialog("TOUS LES PARENTS", isBroadcast: true, openAIService: _openAIService);
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.SelectedTitle))
            {
                BroadcastBtn.IsEnabled = false;
                OnStatusChanged("Envoi du broadcast en cours...");

                try
                {
                    var senderName = _settings?.Medecin ?? "Le cabinet";
                    var (sent, failed, error) = await _firebaseService.SendBroadcastNotificationAsync(
                        dialog.SelectedTitle,
                        dialog.SelectedBody ?? "",
                        senderName);

                    if (error != null)
                    {
                        MessageBox.Show($"Erreur: {error}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                        OnStatusChanged($"Erreur broadcast: {error}");
                    }
                    else
                    {
                        MessageBox.Show(
                            $"Broadcast envoyé avec succès!\n\n{sent} notification(s) envoyée(s)\n{failed} échec(s)",
                            "Succès",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                        OnStatusChanged($"Broadcast envoyé: {sent} notifications");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erreur: {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    OnStatusChanged($"Erreur: {ex.Message}");
                }
                finally
                {
                    BroadcastBtn.IsEnabled = true;
                }
            }
        }

        /// <summary>
        /// Envoie une notification rapide au parent du message sélectionné
        /// </summary>
        private async void QuickReplyBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedMessage == null) return;

            if (_firebaseService == null || !_firebaseService.IsConfigured)
            {
                MessageBox.Show("Firebase n'est pas configuré.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var recipientName = _selectedMessage.ChildNickname ?? _selectedMessage.PatientName ?? "Parent";
            var dialog = new Dialogs.QuickReplyDialog(recipientName, isBroadcast: false, openAIService: _openAIService);
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.SelectedTitle))
            {
                OnStatusChanged("Envoi de la notification...");

                try
                {
                    var notification = new PilotageNotification
                    {
                        Type = dialog.SelectedType,
                        Title = dialog.SelectedTitle,
                        Body = dialog.SelectedBody ?? "",
                        TargetParentId = _selectedMessage.TokenId ?? "",
                        TokenId = _selectedMessage.TokenId,
                        ReplyToMessageId = _selectedMessage.Id,
                        SenderName = _settings?.Medecin ?? "Le cabinet"
                    };

                    var (success, error) = await _firebaseService.WriteNotificationAsync(notification);

                    if (success)
                    {
                        // Mettre à jour le statut du message dans le cache local
                        _messageCache?.UpdateMessageStatus(_selectedMessage.Id, "replied");

                        // Mettre à jour le statut dans Firebase
                        await _firebaseService.UpdateMessageReplyAsync(_selectedMessage.Id, dialog.SelectedTitle);

                        MessageBox.Show(
                            $"Notification envoyée à {recipientName}!\n\n{dialog.SelectedTitle}",
                            "Succès",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);

                        OnStatusChanged("Notification envoyée");
                        RefreshMessages();
                    }
                    else
                    {
                        MessageBox.Show($"Erreur: {error}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                        OnStatusChanged($"Erreur: {error}");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erreur: {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    OnStatusChanged($"Erreur: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Marquer un message comme traité sans réponse (ex: remerciements, confirmations)
        /// </summary>
        private async void MarkAsReadBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedMessage == null || _firebaseService == null) return;

            try
            {
                OnStatusChanged("Marquage comme traité...");

                var (success, error) = await _firebaseService.UpdateMessageReplyAsync(_selectedMessage.Id, "");
                if (success)
                {
                    _selectedMessage.Status = "replied";
                    _messageCache?.UpdateMessageStatus(_selectedMessage.Id, "replied");

                    OnStatusChanged("✅ Message marqué comme traité");
                    ApplyFilter();
                    HideMessageDetail();
                    ClearAttachments();
                    UpdateCounters();
                    UpdateMessagesBadge();
                }
                else
                {
                    OnStatusChanged($"❌ Erreur: {error}");
                }
            }
            catch (Exception ex)
            {
                OnStatusChanged($"❌ Erreur: {ex.Message}");
            }
        }

        /// <summary>
        /// Supprimer un message de Firebase avec notification au parent
        /// </summary>
        private async void DeleteMessageBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedMessage == null || _firebaseService == null) return;

            // Choix du motif de suppression
            var motifs = new[]
            {
                "Ce message ne relève pas du suivi médical",
                "Contenu hors contexte / hors sujet",
                "Merci d'utiliser Doctolib pour les prises de RDV",
                "Message en doublon",
                "Contenu inapproprié"
            };

            var motifWindow = new Window
            {
                Title = "Supprimer le message",
                Width = 480,
                Height = 380,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
                ResizeMode = ResizeMode.NoResize
            };

            var mainPanel = new StackPanel { Margin = new Thickness(20) };

            mainPanel.Children.Add(new TextBlock
            {
                Text = "Motif de suppression",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 5)
            });

            mainPanel.Children.Add(new TextBlock
            {
                Text = "Le parent recevra une notification avec le motif choisi.",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                Margin = new Thickness(0, 0, 0, 15)
            });

            var listBox = new ListBox
            {
                Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(5),
                Height = 180
            };

            foreach (var motif in motifs)
            {
                listBox.Items.Add(new ListBoxItem
                {
                    Content = motif,
                    Foreground = Brushes.White,
                    FontSize = 13,
                    Padding = new Thickness(10, 8, 10, 8)
                });
            }
            listBox.SelectedIndex = 0;
            mainPanel.Children.Add(listBox);

            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 15, 0, 0)
            };

            var cancelBtn = new Button
            {
                Content = "Annuler",
                Padding = new Thickness(16, 8, 16, 8),
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x3D)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            cancelBtn.Click += (_, __) => { motifWindow.DialogResult = false; motifWindow.Close(); };

            var confirmBtn = new Button
            {
                Content = "Supprimer et notifier",
                Padding = new Thickness(16, 8, 16, 8),
                Background = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                BorderThickness = new Thickness(0)
            };
            confirmBtn.Click += (_, __) => { motifWindow.DialogResult = true; motifWindow.Close(); };

            buttonsPanel.Children.Add(cancelBtn);
            buttonsPanel.Children.Add(confirmBtn);
            mainPanel.Children.Add(buttonsPanel);

            motifWindow.Content = mainPanel;

            if (motifWindow.ShowDialog() != true) return;

            var selectedMotif = (listBox.SelectedItem as ListBoxItem)?.Content?.ToString() ?? motifs[0];
            var messageId = _selectedMessage.Id;
            var tokenId = _selectedMessage.TokenId;
            var patientName = _selectedMessage.PatientName ?? _selectedMessage.ChildNickname ?? "Parent";

            try
            {
                OnStatusChanged("Suppression du message...");

                // 1. Supprimer le message de Firebase
                var (deleteOk, deleteError) = await _firebaseService.DeleteMessageAsync(messageId);

                if (!deleteOk)
                {
                    MessageBox.Show($"Erreur suppression Firebase : {deleteError}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 2. Envoyer une notification au parent avec le motif
                var notification = new PilotageNotification
                {
                    Type = NotificationType.Info,
                    Title = "Message supprimé",
                    Body = selectedMotif,
                    TargetParentId = tokenId ?? "",
                    TokenId = tokenId,
                    ReplyToMessageId = messageId,
                    SenderName = _settings?.Medecin ?? "Le cabinet"
                };

                var (notifOk, notifError) = await _firebaseService.WriteNotificationAsync(notification);

                // 3. Supprimer du cache local
                _messageCache?.RemoveMessage(messageId);

                // 4. Retirer de la liste locale
                var msgToRemove = _allMessages.FirstOrDefault(m => m.Id == messageId);
                if (msgToRemove != null)
                {
                    _allMessages.Remove(msgToRemove);
                }

                var statusMsg = notifOk
                    ? $"Message supprimé et notification envoyée à {patientName}"
                    : $"Message supprimé. Notification échouée : {notifError}";

                MessageBox.Show(statusMsg, "Suppression", MessageBoxButton.OK,
                    notifOk ? MessageBoxImage.Information : MessageBoxImage.Warning);

                OnStatusChanged(statusMsg);

                // 5. Rafraîchir l'affichage
                _selectedMessage = null;
                NoSelectionPanel.Visibility = Visibility.Visible;
                MessageContentPanel.Visibility = Visibility.Collapsed;
                ApplyFilter();
                UpdateCounters();
                UpdateMessagesBadge();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                OnStatusChanged($"Erreur suppression : {ex.Message}");
            }
        }

        #endregion

        #region Helper Methods

        private void OnStatusChanged(string message)
        {
            StatusChanged?.Invoke(this, message);
        }

        public void ShowMessage(string patientName, string pseudo, string content, DateTime receivedAt)
        {
            NoSelectionPanel.Visibility = Visibility.Collapsed;
            MessageContentPanel.Visibility = Visibility.Visible;

            PatientNameText.Text = patientName;
            PatientPseudoText.Text = $" ({pseudo})";
            MessageDateText.Text = $"Reçu le {receivedAt:dd/MM/yyyy} à {receivedAt:HH:mm}";
            MessageContentText.Text = content;

            ReplyTextBox.Text = "";
        }

        #endregion

        // ═══════════════════════════════════════════════════════════════
        // ONGLET VPS
        // ═══════════════════════════════════════════════════════════════

        private VpsMonitoringService? _vpsService;

        private void InitVpsService()
        {
            if (_vpsService != null || _settings == null) return;
            if (!_settings.VpsMonitoringEnabled) return;

            var secureStorage = new SecureStorageService();
            _vpsService = new VpsMonitoringService(_settings, secureStorage);
            _vpsService.MetricsUpdated += (_, m) => Dispatcher.Invoke(() => ApplyMetrics(m));
            _vpsService.ConnectionChanged += (_, ok) => Dispatcher.Invoke(() => SetVpsOnline(ok));
            _vpsService.StartPolling();
        }

        private async void VpsRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (_settings == null) return;

            // Initialisation paresseuse au premier clic
            if (_vpsService == null)
            {
                var secureStorage = new SecureStorageService();
                _vpsService = new VpsMonitoringService(_settings, secureStorage);
                _vpsService.MetricsUpdated += (_, m) => Dispatcher.Invoke(() => ApplyMetrics(m));
                _vpsService.ConnectionChanged += (_, ok) => Dispatcher.Invoke(() => SetVpsOnline(ok));
            }

            VpsRefreshBtn.IsEnabled = false;
            VpsStatusText.Text = "Connexion en cours...";

            var (success, metrics, error) = await _vpsService.FetchNowAsync();

            if (success && metrics != null)
                ApplyMetrics(metrics);
            else
                VpsStatusText.Text = $"Erreur : {error}";

            VpsRefreshBtn.IsEnabled = true;
        }

        private void ApplyMetrics(VpsMetrics m)
        {
            // Barres de progression + couleur adaptative
            UpdateBar(CpuBar, CpuText, m.Cpu);
            UpdateBar(RamBar, RamText, m.Ram);
            UpdateBar(DiskBar, DiskText, m.Disk);

            // Statut connexion
            SetVpsOnline(true);
            VpsLastUpdateText.Text = $"· Mis à jour à {m.FetchedAt:HH:mm:ss}  ·  Uptime {m.Uptime}";

            // Services
            ServicesPanel.Children.Clear();
            foreach (var svc in m.Services)
            {
                var status = VpsMonitoringService.ParseStatus(svc.Value);
                var color = status switch
                {
                    ServiceStatus.Active => "#27AE60",
                    ServiceStatus.Warning => "#F39C12",
                    _ => "#E74C3C"
                };

                var chip = new Border
                {
                    Background = new SolidColorBrush((Color)new ColorConverter().ConvertFrom("#2D2D2D")!),
                    BorderBrush = new SolidColorBrush((Color)new ColorConverter().ConvertFrom(color)!),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(10, 6, 10, 6),
                    Margin = new Thickness(0, 0, 8, 8)
                };
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new Ellipse
                {
                    Width = 8, Height = 8,
                    Fill = new SolidColorBrush((Color)new ColorConverter().ConvertFrom(color)!),
                    Margin = new Thickness(0, 0, 6, 0)
                });
                sp.Children.Add(new TextBlock
                {
                    Text = svc.Key,
                    Foreground = Brushes.White,
                    FontSize = 12
                });
                chip.Child = sp;
                ServicesPanel.Children.Add(chip);
            }
        }

        private void UpdateBar(ProgressBar bar, TextBlock label, double value)
        {
            bar.Value = value;
            label.Text = $"{value:0}%";
            bar.Foreground = value switch
            {
                > 85 => new SolidColorBrush((Color)new ColorConverter().ConvertFrom("#E74C3C")!),
                > 70 => new SolidColorBrush((Color)new ColorConverter().ConvertFrom("#F39C12")!),
                _ => new SolidColorBrush((Color)new ColorConverter().ConvertFrom("#27AE60")!)
            };
        }

        private void SetVpsOnline(bool online)
        {
            var color = online ? "#27AE60" : "#555555";
            var brush = new SolidColorBrush((Color)new ColorConverter().ConvertFrom(color)!);
            VpsOnlineDot.Fill = brush;
            VpsStatusDot.Fill = brush;
            if (!online)
            {
                VpsStatusText.Text = "Non connecté";
                VpsLastUpdateText.Text = "";
            }
            else
            {
                VpsStatusText.Text = "Serveur Parent'aile — en ligne";
            }
        }

        private async void VpsAiAnalysis_Click(object sender, RoutedEventArgs e)
        {
            if (_vpsService?.LastMetrics == null || _openAIService == null)
            {
                MessageBox.Show("Rafraîchissez d'abord les métriques.", "Avis IA", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            VpsAiBtn.IsEnabled = false;
            AiAnalysisPanel.Visibility = Visibility.Visible;
            AiAnalysisText.Text = "Analyse en cours...";

            var m = _vpsService.LastMetrics;
            var services = string.Join(", ", m.Services.Select(s => $"{s.Key}:{s.Value}"));
            var prompt = $@"Tu es un assistant technique. Analyse ces métriques de serveur Linux et donne un résumé clair en 3-4 phrases maximum, en français simple (pas de jargon). Si quelque chose mérite attention, dis-le directement.

Métriques :
- CPU : {m.Cpu}%
- RAM : {m.Ram}%
- Disque : {m.Disk}%
- Uptime : {m.Uptime}
- Services : {services}

Réponds de façon directe et lisible, comme si tu parlais à quelqu'un de non-technique.";

            var (success, result, error) = await _openAIService.GenerateTextAsync(prompt, maxTokens: 300);
            AiAnalysisText.Text = success ? result : $"Erreur : {error}";
            VpsAiBtn.IsEnabled = true;
        }

        private async void VpsLoadLogs_Click(object sender, RoutedEventArgs e)
        {
            if (_vpsService == null || _settings == null)
            {
                var secureStorage = new SecureStorageService();
                _vpsService = new VpsMonitoringService(_settings!, secureStorage);
            }

            var service = (LogServiceCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "nginx";
            LogsText.Text = "Chargement...";

            var (success, logs, error) = await _vpsService.FetchLogsAsync(service);
            LogsText.Text = success ? (string.IsNullOrWhiteSpace(logs) ? "(aucun log)" : logs) : $"Erreur : {error}";
        }

        private async void VpsAnalyzeCleanup_Click(object sender, RoutedEventArgs e)
        {
            if (_settings == null) return;

            if (_vpsService == null)
            {
                var secureStorage = new SecureStorageService();
                _vpsService = new VpsMonitoringService(_settings, secureStorage);
            }

            VpsCleanupAnalyzeBtn.IsEnabled = false;
            CleanupPanel.Visibility = Visibility.Visible;
            CleanupResultBorder.Visibility = Visibility.Collapsed;
            CleanupAiText.Text = "Analyse de l'espace disque en cours...";

            var (success, data, error) = await _vpsService.FetchDiskUsageAsync();

            if (!success || data == null)
            {
                CleanupAiText.Text = $"Erreur : {error}";
                VpsCleanupAnalyzeBtn.IsEnabled = true;
                return;
            }

            // Pré-cocher les cases selon les données
            CleanJournal7d.IsChecked = true;
            CleanDockerLogs.IsChecked = true;
            CleanAptCache.IsChecked = true;
            CleanJournal30d.IsChecked = false;
            CleanTmpFiles.IsChecked = false;

            if (_openAIService != null)
            {
                CleanupAiText.Text = "L'IA analyse les données...";
                var journal = data.GetValueOrDefault("journal", "inconnu");
                var docker = data.GetValueOrDefault("docker", "inconnu");
                var varlog = data.GetValueOrDefault("varlog", "inconnu");
                var apt = data.GetValueOrDefault("apt_cache", "inconnu");
                var freeGb = data.GetValueOrDefault("disk_free_gb", "?");
                var totalGb = data.GetValueOrDefault("disk_total_gb", "?");

                var prompt = $@"Tu es un assistant technique. Analyse ces informations d'espace disque d'un serveur Linux et donne en 4-5 phrases simples :
1. L'état général du disque
2. Ce qui peut être nettoyé sans risque
3. Ce qu'il vaut mieux éviter de supprimer

Données :
- Espace libre : {freeGb} Go / {totalGb} Go
- Journaux système : {journal}
- Docker : {docker}
- /var/log : {varlog}
- Cache apt : {apt}

Réponds en français simple, sans jargon technique.";

                var (ok, result, err) = await _openAIService.GenerateTextAsync(prompt, maxTokens: 350);
                CleanupAiText.Text = ok ? result : $"Analyse indisponible : {err}";
            }
            else
            {
                CleanupAiText.Text = $"Espace libre : {data.GetValueOrDefault("disk_free_gb", "?")} Go\nJournaux : {data.GetValueOrDefault("journal", "?")}\nCache apt : {data.GetValueOrDefault("apt_cache", "?")}";
            }

            VpsCleanupAnalyzeBtn.IsEnabled = true;
        }

        private async void VpsExecuteCleanup_Click(object sender, RoutedEventArgs e)
        {
            var actions = new List<string>();
            if (CleanJournal7d.IsChecked == true) actions.Add("journal_7d");
            if (CleanJournal30d.IsChecked == true) actions.Add("journal_30d");
            if (CleanDockerLogs.IsChecked == true) actions.Add("docker_logs");
            if (CleanAptCache.IsChecked == true) actions.Add("apt_cache");
            if (CleanTmpFiles.IsChecked == true) actions.Add("tmp_files");

            if (actions.Count == 0)
            {
                MessageBox.Show("Sélectionnez au moins une action.", "Nettoyage", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var confirm = MessageBox.Show(
                $"Exécuter {actions.Count} action(s) de nettoyage sur le serveur ?\nCette opération est irréversible.",
                "Confirmer le nettoyage",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            VpsCleanupExecuteBtn.IsEnabled = false;
            CleanupResultBorder.Visibility = Visibility.Visible;
            CleanupResultText.Text = "Nettoyage en cours...";
            CleanupResultText.Foreground = new SolidColorBrush((Color)new ColorConverter().ConvertFrom("#F39C12")!);

            var (success, results, error) = await _vpsService!.ExecuteCleanupAsync(actions);

            if (!success)
            {
                CleanupResultText.Text = $"Erreur : {error}";
                CleanupResultText.Foreground = new SolidColorBrush((Color)new ColorConverter().ConvertFrom("#E74C3C")!);
            }
            else
            {
                var sb = new System.Text.StringBuilder("✅ Nettoyage terminé :\n");
                foreach (var r in results!)
                    sb.AppendLine($"  • {r.Key} : {(r.Value == "ok" ? "✅ succès" : $"⚠️ {r.Value}")}");
                CleanupResultText.Text = sb.ToString();
                CleanupResultText.Foreground = new SolidColorBrush((Color)new ColorConverter().ConvertFrom("#27AE60")!);

                // Rafraîchir les métriques après nettoyage
                await Task.Delay(1000);
                var (s, m, _) = await _vpsService.FetchNowAsync();
                if (s && m != null) ApplyMetrics(m);
            }

            VpsCleanupExecuteBtn.IsEnabled = true;
        }

        private async void VpsAnalyzeLogs_Click(object sender, RoutedEventArgs e)
        {
            var logs = LogsText.Text;
            if (string.IsNullOrWhiteSpace(logs) || logs == "Sélectionnez un service et cliquez sur Charger." || _openAIService == null)
            {
                MessageBox.Show("Chargez d'abord les logs d'un service.", "Analyser logs", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            AiAnalysisPanel.Visibility = Visibility.Visible;
            AiAnalysisText.Text = "Analyse des logs en cours...";

            var service = (LogServiceCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "service";
            var extrait = logs.Length > 3000 ? logs[^3000..] : logs;
            var prompt = $@"Tu es un assistant technique. Analyse ces logs du service ""{service}"" et explique en français simple :
1. Est-ce qu'il y a des erreurs ou problèmes ?
2. Si oui, lesquels et que faire ?
3. Si tout va bien, dis-le brièvement.

Logs :
{extrait}";

            var (success, result, error) = await _openAIService.GenerateTextAsync(prompt, maxTokens: 400);
            AiAnalysisText.Text = success ? result : $"Erreur : {error}";
        }
    }
}
