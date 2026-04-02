using System;
using System.Collections.Generic;
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
        private PatientIndexEntry? _currentPatient;
        private ArchivedMessage? _selectedMessage;

        // Tous les messages chargés (non filtrés)
        private List<ArchivedMessage> _allMessages = new();

        // Filtre actif : true = non traités, false = traités/archivés
        private bool _showUnread = true;

        public event EventHandler<string>? StatusChanged;

        public MessagesControl()
        {
            InitializeComponent();
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
            TokenService? tokenService = null)
        {
            _messageService = messageService;
            _firebaseService = firebaseService;
            _emailService = emailService;
            _contextService = contextService;
            _openAIService = openAIService;
            _tokenService = tokenService;
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
                UpdateBadge();
                return;
            }

            // Par défaut, afficher les non traités
            _showUnread = true;
            LoadMessages();
            UpdateFilterButtons();
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
        }

        /// <summary>
        /// Applique le filtre actif et met à jour l'affichage
        /// </summary>
        private void ApplyFilter()
        {
            List<ArchivedMessage> filtered;

            if (_showUnread)
            {
                // Non traités = received, read (tout sauf replied et archived)
                filtered = _allMessages
                    .Where(m => m.Status != "replied" && m.Status != "archived")
                    .ToList();
            }
            else
            {
                // Traités / Archivés = replied, archived
                filtered = _allMessages
                    .Where(m => m.Status == "replied" || m.Status == "archived")
                    .ToList();
            }

            MessagesList.ItemsSource = filtered;
            EmptyLabel.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            // Adapter le texte vide selon le filtre
            EmptyLabel.Text = _showUnread
                ? "Aucun message non traité 👍"
                : "Aucun message traité";

            // Compteur contextuel
            MessageCountLabel.Text = filtered.Count == 0 ? "" :
                filtered.Count == 1 ? "1 message" :
                $"{filtered.Count} message(s)";

            // Mettre à jour le badge
            UpdateBadge();

            // Réinitialiser le détail
            _selectedMessage = null;
            DetailPanel.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Met à jour le badge de compteur sur le bouton Non traités
        /// </summary>
        private void UpdateBadge()
        {
            var unreadCount = _allMessages.Count(m => m.Status != "replied" && m.Status != "archived");
            if (unreadCount > 0)
            {
                UnreadBadge.Visibility = Visibility.Visible;
                UnreadBadgeText.Text = unreadCount.ToString();
            }
            else
            {
                UnreadBadge.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Met à jour l'apparence visuelle des boutons filtre (actif/inactif)
        /// </summary>
        private void UpdateFilterButtons()
        {
            if (_showUnread)
            {
                FilterUnreadBtn.Background = new SolidColorBrush(Color.FromRgb(52, 152, 219));   // Bleu actif
                FilterUnreadBtn.Foreground = Brushes.White;
                FilterArchivedBtn.Background = Brushes.Transparent;
                FilterArchivedBtn.Foreground = new SolidColorBrush(Color.FromRgb(127, 140, 141)); // Gris inactif
            }
            else
            {
                FilterUnreadBtn.Background = Brushes.Transparent;
                FilterUnreadBtn.Foreground = new SolidColorBrush(Color.FromRgb(127, 140, 141));
                FilterArchivedBtn.Background = new SolidColorBrush(Color.FromRgb(39, 174, 96));    // Vert actif
                FilterArchivedBtn.Foreground = Brushes.White;
            }
        }

        // ===== HANDLERS FILTRE =====

        private void FilterUnreadBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_showUnread) return; // Déjà actif
            _showUnread = true;
            UpdateFilterButtons();
            ApplyFilter();
        }

        private void FilterArchivedBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_showUnread) return; // Déjà actif
            _showUnread = false;
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

                // 1. Mettre à jour Firebase
                if (_firebaseService != null && _firebaseService.IsConfigured)
                {
                    await _firebaseService.UpdateMessageReplyAsync(_selectedMessage.FirebaseMessageId, replyText);
                }

                // 2. Envoyer par email si possible
                if (_emailService != null && !string.IsNullOrEmpty(_selectedMessage.ParentEmail))
                {
                    var subject = $"Réponse - {_currentPatient.NomComplet}";
                    await _emailService.SendEmailAsync(_selectedMessage.ParentEmail, subject, replyText);
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
                            if (activeToken != null && _firebaseService != null)
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

    /// <summary>
    /// Convertisseur de statut en couleur pour les badges
    /// </summary>
    public class StatusToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (value as string) switch
            {
                "received" => new SolidColorBrush(Color.FromRgb(231, 76, 60)),   // Rouge
                "read" => new SolidColorBrush(Color.FromRgb(243, 156, 18)),      // Orange
                "replied" => new SolidColorBrush(Color.FromRgb(39, 174, 96)),    // Vert
                "archived" => new SolidColorBrush(Color.FromRgb(149, 165, 166)), // Gris
                _ => new SolidColorBrush(Color.FromRgb(149, 165, 166))
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
