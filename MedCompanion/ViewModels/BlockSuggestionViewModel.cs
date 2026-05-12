using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Models;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// V0b — ViewModel pour une suggestion de bloc supplémentaire (chip UI).
    /// Gère les commandes Accept/Dismiss pour chaque chip.
    /// </summary>
    public class BlockSuggestionViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private readonly ConsultationModeViewModel _parent;

        public BlockSuggestion Suggestion { get; }

        public string BlockKey => Suggestion.BlockKey;
        public string Title => Suggestion.Title;
        public string Reason => Suggestion.Reason;

        public bool IsAccepted => Suggestion.IsAccepted;
        public bool IsDismissed => Suggestion.IsDismissed;

        /// <summary>
        /// Icône du chip selon le type de bloc
        /// </summary>
        public string Icon => BlockKey switch
        {
            "comportement"    => "🧠",
            "vecu_emotionnel" => "💭",
            "developpement"   => "👶",
            "langage"         => "🗣️",
            "motricite"       => "🏃",
            "puberte"         => "🌱",
            "adolescence"     => "🎭",
            "traumatisme"     => "⚡",
            "traitement"      => "💊",
            _                 => "📋"
        };

        public ICommand AcceptCommand { get; }
        public ICommand DismissCommand { get; }

        public BlockSuggestionViewModel(BlockSuggestion suggestion, ConsultationModeViewModel parent)
        {
            Suggestion = suggestion;
            _parent = parent;

            AcceptCommand = new RelayCommand(
                async _ => await _parent.AcceptBlockSuggestionAsync(Suggestion),
                _ => !IsAccepted && !IsDismissed);

            DismissCommand = new RelayCommand(
                _ => _parent.DismissBlockSuggestion(Suggestion),
                _ => !IsAccepted && !IsDismissed);
        }
    }
}
