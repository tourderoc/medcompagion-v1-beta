using System.Windows;
using MedCompanion.Models;

namespace MedCompanion.Dialogs
{
    public partial class LetterFromChatDialog : Window
    {
        public string? UserRequest { get; private set; }
        public ChatExchange Exchange { get; private set; }
        
        public LetterFromChatDialog(ChatExchange exchange)
        {
            InitializeComponent();
            Exchange = exchange;
            
            // Afficher info conversation
            ConversationInfoTextBlock.Text = $"📅 {exchange.Timestamp:dd/MM/yyyy HH:mm} - {exchange.Etiquette ?? "Sans étiquette"}\n" +
                                            $"❓ Question: {TruncateText(exchange.Question, 100)}";
        }
        
        private string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            
            if (text.Length <= maxLength)
                return text;
            
            return text.Substring(0, maxLength) + "...";
        }
        
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
        
        private void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            var request = RequestTextBox.Text?.Trim();
            
            if (string.IsNullOrWhiteSpace(request))
            {
                MessageBox.Show(
                    "Veuillez saisir une demande pour le courrier.",
                    "Champ requis",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                RequestTextBox.Focus();
                return;
            }
            
            UserRequest = request;
            DialogResult = true;
            Close();
        }
    }
}
