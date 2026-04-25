using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using MedCompanion.Models;
using MedCompanion.Services;

namespace MedCompanion.Views.Messages
{
    /// <summary>
    /// Onglet Messages dans l'Assistant IA - affiche les messages parents du patient courant
    /// Avec filtrage Non traités / Traités-Archivés
    /// </summary>
    public partial class MessagesControl : UserControl
    {
        private PatientMessageService? _messageService;
        private FirebaseService? _firebaseService;
        private PilotageEmailService? _emailService;
        private PatientContextService? _contextService;
        private OpenAIService? _openAIService;
        private TokenService? _tokenService;
        private PilotageAttachmentService? _attachmentService;
        private readonly VpsBridgeService _vpsBridge = new();
        private PatientIndexEntry? _currentPatient;
        private ArchivedMessage? _selectedMessage;

        // Tous les messages chargés (non filtrés)
        private List<ArchivedMessage> _allMessages = new();

        // Pièces jointes du patient courant
        public ObservableCollection<PilotageAttachment> Attachments { get; } = new();

        // Filtre actif
        private MessageFilter _activeFilter = MessageFilter.Unread;
        private bool _isRefreshing = false;

        public event EventHandler<string>? StatusChanged;
        public event EventHandler<int>? UnreadCountChanged;

        public MessagesControl()
        {
            InitializeComponent();
            AttachmentsListBox.ItemsSource = Attachments;
            UpdateFilterButtons();
        }

        /// <summary>
        /// Initialise le contrôle avec les services nécessaires
        /// </summary>
        public void Initialize(
            PatientMessageService messageService,
            FirebaseService firebaseService,
            PilotageEmailService? emailService = null,
            PatientContextService? contextService = null,
            OpenAIService? openAIService = null,
            TokenService? tokenService = null,
            PilotageAttachmentService? attachmentService = null)
        {
            _messageService = messageService;
            _firebaseService = firebaseService;
            _emailService = emailService;
            _contextService = contextService;
            _openAIService = openAIService;
            _tokenService = tokenService;
            _attachmentService = attachmentService;
        }

        /// <summary>
        /// Charge les messages du patient sélectionné
        /// </summary>
        public void SetCurrentPatient(PatientIndexEntry? patient)
        {
            _currentPatient = patient;
            _selectedMessage = null;
            DetailPanel.Visibility = Visibility.Collapsed;
            ReplyTextBox.Text = "";

            if (patient == null)
            {
                _allMessages.Clear();
                MessagesList.ItemsSource = null;
                EmptyLabel.Visibility = Visibility.Visible;
                MessageCountLabel.Text = "0 messages";
                UpdateBadges();
                ClearAttachments();
                return;
            }

            // Par défaut, afficher les non traités
            _activeFilter = MessageFilter.Unread;
            LoadMessages();
            UpdateFilterButtons();
            LoadAttachmentsForPatient(patient.NomComplet);
        }

        private void LoadMessages()
        {
            if (_messageService == null || _currentPatient == null) return;

            var (success, messages, error) = _messageService.LoadMessagesForPatient(_currentPatient.NomComplet);

            if (!success)
            {
                StatusChanged?.Invoke(this, $"Erreur: {error}");
                return;
            }


            _allMessages = messages;
            ApplyFilter();

            // Mettre à jour le badge global dans le header
            var globalUnread = _messageService.GetGlobalUnreadCount();
            UnreadCountChanged?.Invoke(this, globalUnread);
        }

        private async void BtnRefresh_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            await RefreshFromFirebaseAsync();
        }

        /// <summary>
        /// Récupère tous les messages depuis Firebase, archive les nouveaux pour le patient courant, recharge l'affichage.
        /// </summary>
        private async System.Threading.Tasks.Task RefreshFromFirebaseAsync()
        {
            if (_isRefreshing || _firebaseService == null || _currentPatient == null || _tokenService == null || _messageService == null) return;

            _isRefreshing = true;
            BtnRefresh.IsEnabled = false;
            BtnRefresh.ToolTip = "Actualisation en cours...";
            StatusChanged?.Invoke(this, "🔄 Récupération des messages depuis Firebase...");

            try
            {
                var (firebaseMessages, fetchError) = await _firebaseService.FetchMessagesAsync();

                if (fetchError != null)
                {
                    StatusChanged?.Invoke(this, $"Erreur Firebase : {fetchError}");
                    return;
                }

                if (firebaseMessages == null || firebaseMessages.Count == 0)
                {
                    StatusChanged?.Invoke(this, "Aucun message trouvé sur Firebase");
                    LoadMessages();
                    return;
                }

                // Tokens actifs du patient courant
                var allTokens = await _tokenService.GetAllTokensAsync();
                var patientTokenIds = allTokens
                    .Where(t => t.PatientId == _currentPatient.NomComplet && t.Active)
                    .Select(t => t.TokenId)
                    .ToHashSet();

                int newCount = 0;
                foreach (var msg in firebaseMessages)
                {
                    if (!patientTokenIds.Contains(msg.TokenId)) continue;

                    var (ok, _) = _messageService.ArchiveMessage(_currentPatient.NomComplet, msg);
                    if (ok) newCount++;
                }

                LoadMessages();
                StatusChanged?.Invoke(this, newCount > 0
                    ? $"✅ {newCount} message(s) synchronisés"
                    : "✅ Messages à jour");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Erreur: {ex.Message}");
            }
            finally
            {
                _isRefreshing = false;
                BtnRefresh.IsEnabled = true;
                BtnRefresh.ToolTip = "Rafraîchir les messages depuis Firebase";
            }
        }

        /// <summary>
        /// Applique le filtre actif et met à jour l'affichage
        /// </summary>
        private void ApplyFilter()
        {
            List<ArchivedMessage> filtered = _activeFilter switch
            {
                MessageFilter.Unread => _allMessages
                    .Where(m => m.Status != "replied" && m.Status != "archived")
                    .ToList(),
                MessageFilter.Urgent => _allMessages
                    .Where(m => m.Status != "replied" && m.Status != "archived"
                        && (m.Urgency == MessageUrgency.Urgent || m.Urgency == MessageUrgency.Critical))
                    .ToList(),
                MessageFilter.Archived => _allMessages
                    .Where(m => m.Status == "replied" || m.Status == "archived")
                    .ToList(),
                _ => _allMessages.ToList()
            };

            MessagesList.ItemsSource = filtered;
            EmptyLabel.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            EmptyLabel.Text = _activeFilter switch
            {
                MessageFilter.Unread => "Aucun message non traité 👍",
                MessageFilter.Urgent => "Aucun message urgent",
                MessageFilter.Archived => "Aucun message traité",
                _ => "Aucun message"
            };

            MessageCountLabel.Text = filtered.Count == 0 ? "" :
                filtered.Count == 1 ? "1 message" :
                $"{filtered.Count} message(s)";

            UpdateBadges();

            _selectedMessage = null;
            DetailPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Met à jour le badge de compteur sur le bouton Non traités
        /// </summary>
        private void UpdateBadges()
        {
            var unreadCount = _allMessages.Count(m => m.Status != "replied" && m.Status != "archived");
            var urgentCount = _allMessages.Count(m =>
                m.Status != "replied" && m.Status != "archived"
                && (m.Urgency == MessageUrgency.Urgent || m.Urgency == MessageUrgency.Critical));

            UnreadBadge.Visibility = unreadCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            UnreadBadgeText.Text = unreadCount.ToString();

            UrgentBadge.Visibility = urgentCount > 0 ? Visibility.Visible : Visibility.Collapsed;
            UrgentBadgeText.Text = urgentCount.ToString();
        }

        private void UpdateFilterButtons()
        {
            var inactive = new SolidColorBrush(Color.FromRgb(127, 140, 141));

            // Reset all
            FilterUnreadBtn.Background = Brushes.Transparent;
            FilterUnreadBtn.Foreground = inactive;
            FilterUrgentBtn.Background = Brushes.Transparent;
            FilterUrgentBtn.Foreground = inactive;
            FilterArchivedBtn.Background = Brushes.Transparent;
            FilterArchivedBtn.Foreground = inactive;

            // Set active
            switch (_activeFilter)
            {
                case MessageFilter.Unread:
                    FilterUnreadBtn.Background = new SolidColorBrush(Color.FromRgb(52, 152, 219));
                    FilterUnreadBtn.Foreground = Brushes.White;
                    break;
                case MessageFilter.Urgent:
                    FilterUrgentBtn.Background = new SolidColorBrush(Color.FromRgb(192, 57, 43));
                    FilterUrgentBtn.Foreground = Brushes.White;
                    break;
                case MessageFilter.Archived:
                    FilterArchivedBtn.Background = new SolidColorBrush(Color.FromRgb(39, 174, 96));
                    FilterArchivedBtn.Foreground = Brushes.White;
                    break;
            }
        }

        // ===== HANDLERS FILTRE =====

        private void FilterUnreadBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_activeFilter == MessageFilter.Unread) return;
            _activeFilter = MessageFilter.Unread;
            UpdateFilterButtons();
            ApplyFilter();
        }

        private void FilterUrgentBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_activeFilter == MessageFilter.Urgent) return;
            _activeFilter = MessageFilter.Urgent;
            UpdateFilterButtons();
            ApplyFilter();
        }

        private void FilterArchivedBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_activeFilter == MessageFilter.Archived) return;
            _activeFilter = MessageFilter.Archived;
            UpdateFilterButtons();
            ApplyFilter();
        }

        // ===== HANDLERS MESSAGES =====

        private void MessagesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MessagesList.SelectedItem is ArchivedMessage msg)
            {
                _selectedMessage = msg;
                FullMessageText.Text = msg.Content;
                DetailPanel.Visibility = Visibility.Visible;

                // Pré-remplir si déjà répondu
                if (msg.HasReply)
                {
                    ReplyTextBox.Text = msg.ReplyContent;
                    ReplyBtn.Content = "Mettre à jour la réponse";
                }
                else
                {
                    ReplyTextBox.Text = "";
                    ReplyBtn.Content = "Envoyer la réponse";
                }
            }
            else
            {
                _selectedMessage = null;
                DetailPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async void ReplyBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedMessage == null || _currentPatient == null || _messageService == null) return;

            var replyText = ReplyTextBox.Text.Trim();
            if (string.IsNullOrEmpty(replyText))
            {
                MessageBox.Show("Veuillez saisir une réponse.", "Attention", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                ReplyBtn.IsEnabled = false;
                StatusChanged?.Invoke(this, "Envoi de la réponse...");

                // 1. VPS bridge (source de vérité)
                await _vpsBridge.ReplyToMessageAsync(_selectedMessage.FirebaseMessageId, replyText, "Médecin");

                // 2. Firebase (dual-write, sera supprimé au merge)
                if (_firebaseService != null && _firebaseService.IsConfigured)
                {
                    await _firebaseService.UpdateMessageReplyAsync(_selectedMessage.FirebaseMessageId, replyText);
                }

                // 3. Envoyer par email si possible (avec PJ sélectionnées)
                if (_emailService != null && !string.IsNullOrEmpty(_selectedMessage.ParentEmail))
                {
                    var subject = $"Réponse - {_currentPatient.NomComplet}";
                    var selectedPJ = Attachments.Where(a => a.IsSelected).Select(a => a.FilePath).ToList();
                    await _emailService.SendEmailAsync(
                        _selectedMessage.ParentEmail, subject, replyText,
                        selectedPJ.Count > 0 ? selectedPJ : null);
                }

                // 3. Mettre à jour l'archive locale
                var (success, error) = _messageService.MarkAsReplied(_currentPatient.NomComplet, _selectedMessage.FirebaseMessageId, replyText);

                if (success)
                {
                    StatusChanged?.Invoke(this, "✅ Réponse envoyée et archivée");
                    LoadMessages(); // Recharger + réappliquer le filtre
                }
                else
                {
                    StatusChanged?.Invoke(this, $"Réponse envoyée mais erreur archivage: {error}");
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Erreur envoi: {ex.Message}");
                MessageBox.Show($"Erreur lors de l'envoi:\n{ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ReplyBtn.IsEnabled = true;
            }
        }

        private async void MarkReadBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedMessage == null || _currentPatient == null || _messageService == null) return;

            // Marquer comme traité (replied) sans texte de réponse
            var (success, error) = _messageService.MarkAsReplied(
                _currentPatient.NomComplet, _selectedMessage.FirebaseMessageId, "[Traité sans réponse]");

            if (success)
            {
                // VPS bridge
                await _vpsBridge.ReplyToMessageAsync(_selectedMessage.FirebaseMessageId, "[Traité]", "Médecin");

                // Firebase (dual-write)
                if (_firebaseService != null && _firebaseService.IsConfigured)
                {
                    await _firebaseService.UpdateMessageReplyAsync(_selectedMessage.FirebaseMessageId, "[Traité]");
                }

                StatusChanged?.Invoke(this, "✅ Message marqué comme traité");
                LoadMessages();
            }
            else
            {
                StatusChanged?.Invoke(this, $"Erreur: {error}");
            }
        }

        private void ArchiveBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedMessage == null || _currentPatient == null || _messageService == null) return;

            // Marquer comme archivé localement
            var (success, error) = _messageService.MarkAsReplied(_currentPatient.NomComplet, _selectedMessage.FirebaseMessageId, _selectedMessage.ReplyContent ?? "[Archivé sans réponse]");

            if (success)
            {
                StatusChanged?.Invoke(this, "✅ Message archivé");
                LoadMessages(); // Recharger + réappliquer le filtre
            }
            else
            {
                StatusChanged?.Invoke(this, $"Erreur archivage: {error}");
            }
        }

        // ===== NOUVELLES FONCTIONNALITÉS IA & NOTIF =====

        private async void BtnNotifyParent_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPatient == null || _openAIService == null || _contextService == null || _tokenService == null)
            {
                MessageBox.Show("Services non disponibles ou aucun patient sélectionné.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                var dialog = new MedCompanion.Dialogs.ComposeNotificationDialog(_openAIService, _contextService, _currentPatient)
                {
                    Owner = Window.GetWindow(this)
                };

                if (dialog.ShowDialog() == true)
                {
                    var result = dialog.Result;
                    if (result.Success)
                    {
                        StatusChanged?.Invoke(this, "Envoi du message direct...");
                        
                        // 1. Récupérer le token du patient
                        var allTokens = await _tokenService.GetAllTokensAsync();
                        var activeToken = allTokens.FirstOrDefault(t => t.PatientId == _currentPatient.NomComplet && t.Active);
                        
                        // 2. Envoi Notification Push
                        if (result.SendPush)
                        {
                            if (activeToken != null)
                            {
                                var notif = new PilotageNotification
                                {
                                    Id = Guid.NewGuid().ToString(),
                                    Type = NotificationType.Info,
                                    Title = "Nouveau message du Dr.",
                                    Body = result.Message,
                                    TargetParentId = activeToken.TokenId,
                                    TokenId = activeToken.TokenId,
                                    CreatedAt = DateTime.UtcNow,
                                    SenderName = "Médecin"
                                };

                                // VPS bridge (+ FCM push)
                                await _vpsBridge.SendNotificationAsync(notif);

                                // Firebase (dual-write)
                                if (_firebaseService != null && _firebaseService.IsConfigured)
                                    await _firebaseService.WriteNotificationAsync(notif);
                            }
                            else if (activeToken == null)
                            {
                                MessageBox.Show("Impossible d'envoyer la notification Push : aucun token actif trouvé pour ce patient.\n\nLe parent doit être inscrit via l'onglet Pilotage.", "Attention", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        }

                        // 3. Envoi E-mail
                        if (result.SendEmail && _emailService != null)
                        {
                            // On tente de trouver l'email dans les métadonnées ou le dernier message
                            string? targetEmail = null;
                            if (_allMessages.Count > 0)
                            {
                                targetEmail = _allMessages.FirstOrDefault(m => !string.IsNullOrEmpty(m.ParentEmail))?.ParentEmail;
                            }

                            if (!string.IsNullOrEmpty(targetEmail))
                            {
                                await _emailService.SendEmailAsync(targetEmail, $"Message de votre médecin - {_currentPatient.NomComplet}", result.Message);
                            }
                            else
                            {
                                MessageBox.Show("Impossible d'envoyer l'e-mail : aucune adresse e-mail trouvée pour ce patient dans l'historique des messages.", "Attention", MessageBoxButton.OK, MessageBoxImage.Warning);
                                return;
                            }
                        }

                        StatusChanged?.Invoke(this, "✅ Message envoyé avec succès");
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur: {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ===== NOTIFICATION RAPIDE =====

        private async void BtnQuickReply_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedMessage == null || _currentPatient == null) return;

            var recipientName = _selectedMessage.ParentName ?? _currentPatient.NomComplet;
            var dialog = new Dialogs.QuickReplyDialog(recipientName, isBroadcast: false, openAIService: _openAIService);
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true && !string.IsNullOrEmpty(dialog.SelectedTitle))
            {
                StatusChanged?.Invoke(this, "Envoi de la notification...");

                try
                {
                    // Récupérer le token du patient
                    string? tokenId = null;
                    if (_tokenService != null)
                    {
                        var allTokens = await _tokenService.GetAllTokensAsync();
                        var activeToken = allTokens.FirstOrDefault(t => t.PatientId == _currentPatient.NomComplet && t.Active);
                        tokenId = activeToken?.TokenId;
                    }

                    var notification = new PilotageNotification
                    {
                        Type = dialog.SelectedType,
                        Title = dialog.SelectedTitle,
                        Body = dialog.SelectedBody ?? "",
                        TargetParentId = tokenId ?? "",
                        TokenId = tokenId,
                        ReplyToMessageId = _selectedMessage.FirebaseMessageId,
                        SenderName = "Médecin"
                    };

                    // VPS bridge (+ FCM push)
                    var (vpsOk, _) = await _vpsBridge.SendNotificationAsync(notification);

                    // Firebase (dual-write)
                    if (_firebaseService != null && _firebaseService.IsConfigured)
                        await _firebaseService.WriteNotificationAsync(notification);

                    // VPS bridge — marquer le message comme répondu
                    await _vpsBridge.ReplyToMessageAsync(_selectedMessage.FirebaseMessageId, dialog.SelectedTitle, "Médecin");

                    // Firebase — marquer le message comme répondu (dual-write)
                    if (_firebaseService != null && _firebaseService.IsConfigured)
                        await _firebaseService.UpdateMessageReplyAsync(_selectedMessage.FirebaseMessageId, dialog.SelectedTitle);

                    if (vpsOk || (_firebaseService != null && _firebaseService.IsConfigured))
                    {
                        // Mettre à jour l'archive locale
                        _messageService?.MarkAsReplied(_currentPatient.NomComplet, _selectedMessage.FirebaseMessageId, dialog.SelectedTitle);

                        StatusChanged?.Invoke(this, $"✅ Notification envoyée à {recipientName}");
                        LoadMessages();
                    }
                    else
                    {
                        MessageBox.Show("Erreur lors de l'envoi de la notification.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                        StatusChanged?.Invoke(this, "Erreur envoi notification");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erreur: {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusChanged?.Invoke(this, $"Erreur: {ex.Message}");
                }
            }
        }

        // ===== PIECES JOINTES =====

        /// <summary>
        /// Rafraîchit la liste des pièces jointes (appelé après ajout externe depuis Attestations/Courriers)
        /// </summary>
        public void RefreshAttachments()
        {
            if (_currentPatient != null)
                LoadAttachmentsForPatient(_currentPatient.NomComplet);
        }

        private void LoadAttachmentsForPatient(string patientId)
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
            if (_currentPatient == null || _attachmentService == null) return;

            try
            {
                var attachment = await _attachmentService.AddManualFileAsync(_currentPatient.NomComplet);
                if (attachment != null)
                {
                    Attachments.Add(attachment);
                    NoAttachmentsPanel.Visibility = Visibility.Collapsed;
                    StatusChanged?.Invoke(this, $"Document ajouté: {attachment.FileName}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur: {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
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
            }
        }

        private async void BtnSuggestionIA_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedMessage == null || _currentPatient == null || _openAIService == null || _contextService == null) return;

            try
            {
                BtnSuggestionIA.IsEnabled = false;
                StatusChanged?.Invoke(this, "🪄 L'IA analyse le dossier et prépare une réponse...");

                // 1. Récupérer contexte
                var context = _contextService.GetCompleteContext(_currentPatient.NomComplet);
                
                // 2. Prompt
                string prompt = $@"Tu es un assistant médical pour un médecin. Le patient est {_currentPatient.NomComplet}.
Voici le contexte médical du patient (synthèse/notes) :
{context.ClinicalContext}

Le parent a envoyé ce message :
""{_selectedMessage.Content}""

PROPOSE UNE RÉPONSE :
- Ton bienveillant, rassurant et professionnel.
- Basée sur les éléments médicaux fournis si cela peut aider à répondre spécifiquement (prévention, conseils, rendez-vous).
- Concise mais complète.
- Ne mentionne pas que tu es une IA.
- Ne donne QUE le texte de la réponse, sans aucun enrobage.";

                // 3. IA
                var (success, suggestion, error) = await _openAIService.GenerateTextAsync(prompt);
                
                if (success && !string.IsNullOrEmpty(suggestion))
                {
                    ReplyTextBox.Text = suggestion.Trim();
                    StatusChanged?.Invoke(this, "✨ Suggestion IA appliquée");
                }
                else if (!success)
                {
                    StatusChanged?.Invoke(this, $"Erreur IA: {error}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur IA: {ex.Message}");
            }
            finally
            {
                BtnSuggestionIA.IsEnabled = true;
            }
        }
    }

    public enum MessageFilter
    {
        Unread,
        Urgent,
        Archived
    }

    public class StatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value as string) switch
            {
                "received" => new SolidColorBrush(Color.FromRgb(231, 76, 60)),
                "read" => new SolidColorBrush(Color.FromRgb(243, 156, 18)),
                "replied" => new SolidColorBrush(Color.FromRgb(39, 174, 96)),
                "archived" => new SolidColorBrush(Color.FromRgb(149, 165, 166)),
                _ => new SolidColorBrush(Color.FromRgb(149, 165, 166))
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Dot coloré selon le niveau d'urgence du message
    /// </summary>
    public class UrgencyToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value is MessageUrgency urgency ? urgency switch
            {
                MessageUrgency.Critical => new SolidColorBrush(Color.FromRgb(192, 57, 43)),  // Rouge foncé
                MessageUrgency.Urgent => new SolidColorBrush(Color.FromRgb(231, 76, 60)),    // Rouge
                MessageUrgency.Moderate => new SolidColorBrush(Color.FromRgb(243, 156, 18)), // Orange
                MessageUrgency.Low => new SolidColorBrush(Color.FromRgb(39, 174, 96)),       // Vert
                _ => new SolidColorBrush(Color.FromRgb(189, 195, 199))                       // Gris clair
            } : new SolidColorBrush(Color.FromRgb(189, 195, 199));
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Texte de pression temporelle : "Il y a Xh" avec icône selon la durée
    /// </summary>
    public class TimePressureConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not DateTime receivedAt) return "";

            var diff = DateTime.Now - receivedAt;
            if (diff.TotalMinutes < 1) return "À l'instant";
            if (diff.TotalMinutes < 60) return $"Il y a {(int)diff.TotalMinutes}m";
            if (diff.TotalHours < 2) return $"Il y a {(int)diff.TotalHours}h";
            if (diff.TotalHours < 8) return $"⏰ {(int)diff.TotalHours}h en attente";
            if (diff.TotalHours < 24) return $"🔥 {(int)diff.TotalHours}h en attente";
            if (diff.TotalDays < 7) return $"🔥 {(int)diff.TotalDays}j en attente";
            return receivedAt.ToString("dd/MM/yyyy");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    /// <summary>
    /// Couleur du texte de pression temporelle
    /// </summary>
    public class TimePressureColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is not DateTime receivedAt)
                return new SolidColorBrush(Color.FromRgb(149, 165, 166));

            var hours = (DateTime.Now - receivedAt).TotalHours;
            if (hours > 8) return new SolidColorBrush(Color.FromRgb(231, 76, 60));    // Rouge
            if (hours > 2) return new SolidColorBrush(Color.FromRgb(243, 156, 18));   // Orange
            return new SolidColorBrush(Color.FromRgb(149, 165, 166));                  // Gris
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
