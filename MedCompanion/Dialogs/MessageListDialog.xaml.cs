using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using MedCompanion.Models;
using MedCompanion.Services;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Dialog affichant les messages non traités de tous les patients.
    /// Double-clic sur un message -> charge le patient + active l'onglet Messages.
    /// </summary>
    public partial class MessageListDialog : Window
    {
        private readonly PatientMessageService _messageService;
        private readonly PatientIndexService _patientIndex;
        private List<PatientMessage> _messages = new();

        /// <summary>
        /// Message sélectionné par l'utilisateur (double-clic ou bouton Ouvrir)
        /// </summary>
        public PatientMessage? SelectedMessage { get; private set; }

        /// <summary>
        /// Événement déclenché quand l'utilisateur double-clique sur un message
        /// </summary>
        public event EventHandler<PatientMessage>? MessageSelected;

        public MessageListDialog(PatientMessageService messageService, PatientIndexService patientIndex)
        {
            InitializeComponent();
            _messageService = messageService;
            _patientIndex = patientIndex;

            Loaded += async (s, e) => await LoadMessagesAsync();
            KeyDown += (s, e) => { if (e.Key == Key.Escape) { DialogResult = false; } };
        }

        private async System.Threading.Tasks.Task LoadMessagesAsync()
        {
            try
            {
                LoadingOverlay.Visibility = Visibility.Visible;

                var (success, messages, error) = await _messageService.FetchAndSyncMessagesAsync();

                if (!success)
                {
                    MessageBox.Show($"Erreur: {error}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                _messages = messages;
                MessagesDataGrid.ItemsSource = _messages;

                var count = _messages.Count;
                MessageCountLabel.Text = count == 0 ? "Aucun message en attente" :
                    count == 1 ? "1 message non traité" :
                    $"{count} messages non traités";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur chargement: {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void MessagesDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            SelectAndOpen();
        }

        private void OpenBtn_Click(object sender, RoutedEventArgs e)
        {
            SelectAndOpen();
        }

        private void SelectAndOpen()
        {
            if (MessagesDataGrid.SelectedItem is PatientMessage msg)
            {
                SelectedMessage = msg;
                // L'archivage est déjà fait pendant FetchAndSyncMessagesAsync
                MessageSelected?.Invoke(this, msg);
                DialogResult = true;
            }
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            await LoadMessagesAsync();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
