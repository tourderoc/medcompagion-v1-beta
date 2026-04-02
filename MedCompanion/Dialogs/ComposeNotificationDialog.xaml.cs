using System;
using System.Threading.Tasks;
using System.Windows;
using MedCompanion.Models;
using MedCompanion.Services;

namespace MedCompanion.Dialogs
{
    public class NotificationResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public bool SendPush { get; set; }
        public bool SendEmail { get; set; }
    }

    public partial class ComposeNotificationDialog : Window
    {
        private readonly OpenAIService _openAIService;
        private readonly PatientContextService _contextService;
        private readonly PatientIndexEntry _patient;
        
        public NotificationResult Result { get; private set; }

        public ComposeNotificationDialog(
            OpenAIService openAIService,
            PatientContextService contextService,
            PatientIndexEntry patient)
        {
            InitializeComponent();
            _openAIService = openAIService;
            _contextService = contextService;
            _patient = patient;

            TitleText.Text = $"🚀 Notifier le parent de {patient.NomComplet}";
            Result = new NotificationResult { Success = false };
        }

        private void DraftTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ReformulateBtn.IsEnabled = !string.IsNullOrWhiteSpace(DraftTextBox.Text);
            UpdateButtonStates();
        }

        private void UpdateButtonStates()
        {
            SendBtn.IsEnabled = !string.IsNullOrWhiteSpace(ResultTextBox.Text) && 
                               (ChannelPushCheck.IsChecked == true || ChannelEmailCheck.IsChecked == true);
        }

        private async void ReformulateBtn_Click(object sender, RoutedEventArgs e)
        {
            var draft = DraftTextBox.Text.Trim();
            if (string.IsNullOrEmpty(draft)) return;

            SetBusy(true);
            try
            {
                // 1. Récupérer le contexte patient
                var context = _contextService.GetCompleteContext(_patient.NomComplet);
                
                // 2. Préparer le prompt selon le canal dominant
                bool isEmail = ChannelEmailCheck.IsChecked == true;
                string formatInstruction = isEmail 
                    ? "C'est un E-MAIL : structure le message avec des salutations, des paragraphes clairs et une conclusion professionnelle."
                    : "C'est une NOTIFICATION mobile : sois très concis, direct et bienveillant (maximum 2-3 phrases).";

                string prompt = $@"Tu es un assistant médical pour un médecin. 
Le patient est {_patient.NomComplet}.
Contexte médical du patient :
{context.ClinicalContext}

L'utilisateur (le médecin) a écrit ce brouillon pour le parent :
""{draft}""

REFORMULATION :
- Garde un ton bienveillant, rassurant et professionnel.
- {formatInstruction}
- Ne mentionne pas que tu es une IA.
- Ne fournis QUE le texte reformulé final, sans aucune introduction ni commentaire.";

                // 3. Appeler l'IA
                var (success, reformulated, error) = await _openAIService.GenerateTextAsync(prompt);
                
                if (success && !string.IsNullOrEmpty(reformulated))
                {
                    ResultTextBox.Text = reformulated.Trim();
                }
                else if (!success)
                {
                    MessageBox.Show($"Erreur IA : {error}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la reformulation : {ex.Message}", "Erreur IA", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
                UpdateButtonStates();
            }
        }

        private void SetBusy(bool busy)
        {
            AILoadingBar.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            ReformulateBtn.IsEnabled = !busy;
            SendBtn.IsEnabled = !busy;
            DraftTextBox.IsEnabled = !busy;
            ResultTextBox.IsReadOnly = busy;
        }

        private void SendBtn_Click(object sender, RoutedEventArgs e)
        {
            Result = new NotificationResult
            {
                Success = true,
                Message = ResultTextBox.Text,
                SendPush = ChannelPushCheck.IsChecked == true,
                SendEmail = ChannelEmailCheck.IsChecked == true
            };
            DialogResult = true;
            Close();
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
