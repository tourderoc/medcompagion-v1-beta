using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using MedCompanion.Models;

namespace MedCompanion.Dialogs
{
    public partial class QuickReplyDialog : Window
    {
        public string? SelectedTitle { get; private set; }
        public string? SelectedBody { get; private set; }
        public NotificationType SelectedType { get; private set; } = NotificationType.Quick;

        private readonly string _recipientName;
        private readonly bool _isBroadcast;
        private readonly OpenAIService? _openAIService;

        public class TemplateItem
        {
            public string Title { get; set; } = "";
            public string Body { get; set; } = "";
        }

        private ObservableCollection<TemplateItem> _templates = new();

        public QuickReplyDialog(string recipientName, bool isBroadcast = false, OpenAIService? openAIService = null)
        {
            InitializeComponent();
            _recipientName = recipientName;
            _isBroadcast = isBroadcast;
            _openAIService = openAIService;

            RecipientText.Text = isBroadcast
                ? "Destinataire: TOUS LES PARENTS"
                : $"Destinataire: {recipientName}";

            TemplatesListBox.ItemsSource = _templates;

            // Masquer le bouton IA si pas de service LLM
            ReformulateBtn.Visibility = _openAIService != null ? Visibility.Visible : Visibility.Collapsed;

            // Charger les templates RDV par défaut
            LoadTemplates("rdv");

            // Événements pour le mode personnalisé
            CustomTitleBox.TextChanged += (s, e) => UpdatePreview();
            CustomBodyBox.TextChanged += (s, e) => UpdatePreview();
        }

        private void Category_Changed(object sender, RoutedEventArgs e)
        {
            // Ignorer pendant l'initialisation (les contrôles ne sont pas encore prêts)
            if (TemplatesListBox == null || CustomPanel == null || SendButton == null)
                return;

            if (RdvRadio.IsChecked == true)
            {
                LoadTemplates("rdv");
                SelectedType = NotificationType.Quick;
            }
            else if (DoctoLibRadio.IsChecked == true)
            {
                LoadTemplates("doctolib");
                SelectedType = NotificationType.Info;
            }
            else if (InfoRadio.IsChecked == true)
            {
                LoadTemplates("info");
                SelectedType = NotificationType.Info;
            }
            else if (CustomRadio.IsChecked == true)
            {
                ShowCustomPanel();
                SelectedType = NotificationType.Quick;
            }

            // Pour broadcast, forcer le type
            if (_isBroadcast)
            {
                SelectedType = NotificationType.Broadcast;
            }
        }

        private void LoadTemplates(string category)
        {
            _templates.Clear();

            // Vérification de sécurité
            if (CustomPanel == null || TemplatesListBox == null || SendButton == null)
                return;

            CustomPanel.Visibility = Visibility.Collapsed;
            TemplatesListBox.Visibility = Visibility.Visible;

            (string Title, string Body)[] templates = category switch
            {
                "rdv" => QuickReplyTemplates.RdvTemplates,
                "doctolib" => QuickReplyTemplates.DoctoLibTemplates,
                "info" => QuickReplyTemplates.InfoTemplates,
                "broadcast" => QuickReplyTemplates.BroadcastTemplates,
                _ => QuickReplyTemplates.RdvTemplates
            };

            foreach (var (title, body) in templates)
            {
                _templates.Add(new TemplateItem { Title = title, Body = body });
            }

            // Charger les templates broadcast si mode broadcast
            if (_isBroadcast && category != "broadcast")
            {
                // Ajouter aussi les templates broadcast
            }

            SendButton.IsEnabled = false;
            ClearPreview();
        }

        private void ShowCustomPanel()
        {
            if (TemplatesListBox == null || CustomPanel == null || SendButton == null)
                return;

            TemplatesListBox.Visibility = Visibility.Collapsed;
            CustomPanel.Visibility = Visibility.Visible;
            SendButton.IsEnabled = false;
            ClearPreview();
        }

        private void TemplatesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TemplatesListBox.SelectedItem is TemplateItem item)
            {
                SelectedTitle = item.Title;
                SelectedBody = item.Body;
                UpdatePreviewFromSelection(item.Title, item.Body);
                SendButton.IsEnabled = true;
            }
        }

        private void UpdatePreview()
        {
            if (CustomRadio == null || CustomTitleBox == null || CustomBodyBox == null ||
                PreviewTitle == null || PreviewBody == null || SendButton == null)
                return;

            if (CustomRadio.IsChecked == true)
            {
                var title = CustomTitleBox.Text.Trim();
                var body = CustomBodyBox.Text.Trim();

                SelectedTitle = title;
                SelectedBody = body;

                PreviewTitle.Text = string.IsNullOrEmpty(title) ? "Titre..." : title;
                PreviewBody.Text = string.IsNullOrEmpty(body) ? "Corps du message..." : body;

                SendButton.IsEnabled = !string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(body);
            }
        }

        private void UpdatePreviewFromSelection(string title, string body)
        {
            PreviewTitle.Text = title;
            PreviewBody.Text = body;
        }

        private void ClearPreview()
        {
            if (PreviewTitle == null || PreviewBody == null)
                return;

            PreviewTitle.Text = "Titre...";
            PreviewBody.Text = "Corps du message...";
        }

        /// <summary>
        /// Reformule le message avec l'IA : corrige l'orthographe et génère un titre
        /// </summary>
        private async void ReformulateBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_openAIService == null) return;

            var rawText = CustomBodyBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(rawText))
            {
                MessageBox.Show("Saisissez d'abord votre message avant de reformuler.", "Message vide",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // UI loading
            ReformulateBtn.IsEnabled = false;
            ReformulateBtn.Content = "Reformulation...";
            AILoadingPanel.Visibility = Visibility.Visible;
            SendButton.IsEnabled = false;

            try
            {
                var prompt = $@"Tu es un assistant médical. On te donne un message brut écrit par un médecin pour les parents d'un patient.

Tâche :
1. Reformule le message en corrigeant UNIQUEMENT l'orthographe et la grammaire. N'ajoute rien, ne change pas le sens, ne rajoute pas de formules de politesse.
2. Génère un titre court (max 6 mots) qui résume le message.

Message brut :
""{rawText}""

Réponds EXACTEMENT dans ce format (sans rien d'autre) :
TITRE: [le titre]
MESSAGE: [le message reformulé]";

                var (success, result, error) = await _openAIService.GenerateTextAsync(prompt, 500);

                if (success && !string.IsNullOrWhiteSpace(result))
                {
                    // Parser la réponse
                    var (titre, message) = ParseReformulation(result, rawText);

                    // Remplir les champs (modifiables par l'utilisateur)
                    CustomTitleBox.Text = titre;
                    CustomBodyBox.Text = message;

                    // Mettre à jour l'aperçu
                    UpdatePreview();
                }
                else
                {
                    MessageBox.Show($"Erreur IA : {error ?? "Réponse vide"}", "Erreur",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Erreur : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ReformulateBtn.IsEnabled = true;
                ReformulateBtn.Content = "\u2728 Reformuler avec IA";
                AILoadingPanel.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Parse la réponse IA au format "TITRE: ...\nMESSAGE: ..."
        /// </summary>
        private (string titre, string message) ParseReformulation(string response, string fallbackMessage)
        {
            var titre = "";
            var message = fallbackMessage;

            var lines = response.Split('\n', System.StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("TITRE:", System.StringComparison.OrdinalIgnoreCase))
                {
                    titre = trimmed.Substring("TITRE:".Length).Trim();
                }
                else if (trimmed.StartsWith("MESSAGE:", System.StringComparison.OrdinalIgnoreCase))
                {
                    message = trimmed.Substring("MESSAGE:".Length).Trim();
                }
            }

            // Si le message est sur plusieurs lignes après "MESSAGE:"
            var messageIdx = response.IndexOf("MESSAGE:", System.StringComparison.OrdinalIgnoreCase);
            if (messageIdx >= 0)
            {
                message = response.Substring(messageIdx + "MESSAGE:".Length).Trim();
            }

            // Fallback si parsing échoue
            if (string.IsNullOrWhiteSpace(titre))
            {
                titre = "Notification";
            }

            return (titre, message);
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Send_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(SelectedTitle) || string.IsNullOrEmpty(SelectedBody))
            {
                MessageBox.Show("Veuillez sélectionner ou saisir un message.", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }
    }
}
