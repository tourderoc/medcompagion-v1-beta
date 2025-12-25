using System.Windows.Documents;
using System.Windows.Media;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// ViewModel pour un message de chat individuel
    /// </summary>
    public class ChatMessageViewModel : ViewModelBase
    {
        private string _author = "";
        public string Author
        {
            get => _author;
            set => SetProperty(ref _author, value);
        }

        private string _content = "";
        public string Content
        {
            get => _content;
            set => SetProperty(ref _content, value);
        }

        private Color _borderColor = Colors.Gray;
        public Color BorderColor
        {
            get => _borderColor;
            set => SetProperty(ref _borderColor, value);
        }

        private bool _isFromAI = false;
        public bool IsFromAI
        {
            get => _isFromAI;
            set
            {
                if (SetProperty(ref _isFromAI, value))
                {
                    OnPropertyChanged(nameof(ShowSaveButton));
                }
            }
        }

        private bool _isArchived = false;
        public bool IsArchived
        {
            get => _isArchived;
            set
            {
                if (SetProperty(ref _isArchived, value))
                {
                    OnPropertyChanged(nameof(ShowSaveButton));
                }
            }
        }

        /// <summary>
        /// Afficher le bouton 💾 seulement pour les messages IA non archivés
        /// </summary>
        public bool ShowSaveButton => IsFromAI && !IsArchived;

        private int? _exchangeIndex;
        public int? ExchangeIndex
        {
            get => _exchangeIndex;
            set => SetProperty(ref _exchangeIndex, value);
        }

        private string? _exchangeId;
        public string? ExchangeId
        {
            get => _exchangeId;
            set => SetProperty(ref _exchangeId, value);
        }

        // Pour l'affichage UI
        private FlowDocument? _richContent;
        public FlowDocument? RichContent
        {
            get => _richContent;
            set => SetProperty(ref _richContent, value);
        }

        private string? _plainText;
        public string? PlainText
        {
            get => _plainText;
            set => SetProperty(ref _plainText, value);
        }
    }
}
