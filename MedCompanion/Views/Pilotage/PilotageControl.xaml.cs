using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
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

        // Architecture polling : 3 tentatives × 30 min, puis arrêt
        private int _retryCount = 0;
        private const int MAX_RETRIES = 3;
        private bool _pollingExhausted = false;
        private DateTime? _lastFirebaseSyncTimestamp = null;

        // Listeners Firestore temps réel (SDK)
        private Google.Cloud.Firestore.FirestoreChangeListener? _messagesListener;
        private Google.Cloud.Firestore.FirestoreChangeListener? _tokensListener;

        // Onglet Utilisateurs : tokens
        // (les collections Messages/Patients ont été migrées vers ConsoleMessages)

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
                    case 0: RefreshTokensTab(); break; // Utilisateurs (anciennement index 2)
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

            // Listener temps réel si SDK disponible, sinon polling limité
            if (_firebaseService != null && _firebaseService.IsListenerConfigured)
                StartListeners();
            else
                StartInitialSyncWithRetry();

            OnStatusChanged("Mode Pilotage prêt");
            RefreshTokensTab();
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
            RefreshTokensTab();
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
        /// Lance la synchronisation initiale puis programme jusqu'à MAX_RETRIES tentatives de 30 min.
        /// Remplace l'ancien DispatcherTimer infini.
        /// </summary>
        private async void StartInitialSyncWithRetry()
        {
            _retryCount = 0;
            _pollingExhausted = false;
            await RunSyncAttemptAsync();
        }

        /// <summary>
        /// Tente une sync incrémentale Firebase. En l'absence de nouveaux messages, programme un retry.
        /// </summary>
        private async System.Threading.Tasks.Task RunSyncAttemptAsync()
        {
            if (_pollingExhausted || _firebaseService == null || _messageCache == null) return;

            System.Diagnostics.Debug.WriteLine($"[Pilotage] 🔍 Tentative sync #{_retryCount + 1}/{MAX_RETRIES}");

            try
            {
                var (firebaseMessages, error) = await _firebaseService.FetchMessagesAsync(since: _lastFirebaseSyncTimestamp);

                if (!string.IsNullOrEmpty(error))
                {
                    System.Diagnostics.Debug.WriteLine($"[Pilotage] ⚠️ Erreur sync: {error}");
                    await ScheduleRetryOrExhaustAsync();
                    return;
                }

                var cachedIds = _messageCache.GetCachedMessageIds();
                var newMessages = firebaseMessages.Where(m => !cachedIds.Contains(m.Id)).ToList();

                // Mettre à jour le timestamp pour la prochaine sync incrémentale
                _lastFirebaseSyncTimestamp = DateTime.UtcNow;

                if (newMessages.Count > 0)
                {
                    int count = newMessages.Count;
                    NewMessagesDetected?.Invoke(this, count);

                    await Dispatcher.InvokeAsync(() =>
                    {
                        OnStatusChanged($"📬 {count} nouveau(x) message(s) !");
                        LastSyncText.Text = $"Dernière synchro: {DateTime.Now:HH:mm}";
                    });

                    // Nouveau message = succès, reset du compteur de retry
                    _retryCount = 0;
                    System.Diagnostics.Debug.WriteLine($"[Pilotage] 📬 {count} nouveaux messages détectés");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[Pilotage] ✓ Aucun nouveau message");
                    await Dispatcher.InvokeAsync(() =>
                        LastSyncText.Text = $"Dernière synchro: {DateTime.Now:HH:mm}");
                    await ScheduleRetryOrExhaustAsync();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Pilotage] ❌ Erreur sync: {ex.Message}");
                await ScheduleRetryOrExhaustAsync();
            }
        }

        /// <summary>
        /// Incrémente le compteur de retry. Si MAX_RETRIES atteint, arrête. Sinon, programme la prochaine tentative à +30 min.
        /// </summary>
        private async System.Threading.Tasks.Task ScheduleRetryOrExhaustAsync()
        {
            _retryCount++;
            if (_retryCount >= MAX_RETRIES)
            {
                _pollingExhausted = true;
                await Dispatcher.InvokeAsync(() =>
                    LastSyncText.Text = "Sync arrêtée — utilisez Rafraîchir");
                System.Diagnostics.Debug.WriteLine("[Pilotage] ⏹️ Polling épuisé après MAX_RETRIES");
                return;
            }

            System.Diagnostics.Debug.WriteLine($"[Pilotage] ⏳ Prochain retry dans 30 min (tentative {_retryCount + 1}/{MAX_RETRIES})");
            _ = System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(TimeSpan.FromMinutes(30));
                await Dispatcher.InvokeAsync(async () => await RunSyncAttemptAsync());
            });
        }

        /// <summary>
        /// Arrête le polling
        /// </summary>
        public void StopPolling()
        {
            _pollingExhausted = true;
            _messagesListener?.StopAsync();
            _messagesListener = null;
            _tokensListener?.StopAsync();
            _tokensListener = null;
            System.Diagnostics.Debug.WriteLine("[Pilotage] ⏹️ Polling + listeners arrêtés");
        }

        /// <summary>
        /// Démarre les listeners Firestore temps réel (SDK).
        /// Sur erreur, bascule automatiquement vers le polling limité.
        /// </summary>
        private void StartListeners()
        {
            if (_firebaseService == null || _messageCache == null) return;

            System.Diagnostics.Debug.WriteLine("[Pilotage] Démarrage des listeners Firestore...");

            _messagesListener = _firebaseService.ListenToMessages(
                onNewMessages: messages => Dispatcher.InvokeAsync(() => HandleIncomingMessages(messages)),
                onError: ex => Dispatcher.InvokeAsync(() =>
                {
                    System.Diagnostics.Debug.WriteLine($"[Pilotage] Listener erreur, bascule polling: {ex.Message}");
                    OnStatusChanged("Listener déconnecté — bascule vers polling");
                    _messagesListener?.StopAsync();
                    _messagesListener = null;
                    StartInitialSyncWithRetry();
                }));

            _tokensListener = _firebaseService.ListenToTokens(
                onStatusChange: statuses => Dispatcher.InvokeAsync(() =>
                {
                    System.Diagnostics.Debug.WriteLine($"[Pilotage] Tokens mis à jour: {statuses.Count}");
                    RefreshTokensTab();
                }),
                onError: ex =>
                    System.Diagnostics.Debug.WriteLine($"[Pilotage] Listener tokens erreur: {ex.Message}"));

            OnStatusChanged("Listeners Firestore actifs ✅");
        }

        /// <summary>
        /// Traite les messages entrants depuis le listener.
        /// Déduplique via le cache, met à jour le badge et le statut.
        /// </summary>
        private void HandleIncomingMessages(List<Models.PatientMessage> messages)
        {
            if (_messageCache == null) return;

            var cachedIds = _messageCache.GetCachedMessageIds();
            var newMessages = messages.Where(m => !cachedIds.Contains(m.Id)).ToList();
            if (newMessages.Count == 0) return;

            foreach (var msg in newMessages)
                _messageCache.CacheMessage(msg);

            NewMessagesDetected?.Invoke(this, newMessages.Count);
            OnStatusChanged($"📬 {newMessages.Count} nouveau(x) message(s) !");
            LastSyncText.Text = $"Dernière synchro: {DateTime.Now:HH:mm}";
        }

        #endregion

        #region Tab Control

        private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.Source != MainTabControl) return;

            switch (MainTabControl.SelectedIndex)
            {
                case 0: // Utilisateurs
                    RefreshTokensTab();
                    SelectionInfoText.Text = "Gestion des utilisateurs Parent'aile";
                    break;
                // case 1: Serveur VPS — pas d'action nécessaire
            }
        }

        #endregion


        #region Utilisateurs Tab

        /// <summary>
        /// Rafraîchit l'onglet Utilisateurs
        /// </summary>
        public async void RefreshTokensTab()
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

            // Stats : activité du token (les messages sont maintenant dans Console)
            UserStatMessages.Text = "—";
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
            // Refresh manuel : reset de l'état polling, relance synchro
            _pollingExhausted = false;
            _retryCount = 0;
            _lastFirebaseSyncTimestamp = null;
            StartInitialSyncWithRetry();
            RefreshTokensTab();
        }

        #endregion


        #region Helper Methods

        private void OnStatusChanged(string message)
        {
            StatusChanged?.Invoke(this, message);
        }

        /// <summary>
        /// Envoie une notification broadcast à tous les parents (bouton dans Utilisateurs)
        /// </summary>
        private async void BroadcastBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_firebaseService == null || !_firebaseService.IsConfigured)
            {
                MessageBox.Show("Firebase n'est pas configuré.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

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
                            "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
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
