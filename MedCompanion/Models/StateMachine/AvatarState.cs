using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MedCompanion.Models.StateMachine
{
    public class AvatarState : INotifyPropertyChanged
    {
        private string _name = "New State";
        private string _description = "";
        private bool _isLooping = true;
        private double _graphX;
        private double _graphY;

        public Guid Id { get; set; } = Guid.NewGuid();

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string Description
        {
            get => _description;
            set { _description = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// The sequence of media to play in this state.
        /// </summary>
        public ObservableCollection<MediaItem> MediaSequence { get; set; } = new();

        /// <summary>
        /// If true, the sequence loops indefinitely. 
        /// If false, it plays once and then stops or triggers an "OnMediaEnd" event.
        /// </summary>
        public bool IsLooping
        {
            get => _isLooping;
            set { _isLooping = value; OnPropertyChanged(); }
        }

        // UI Position for the Graph Editor
        public double GraphX
        {
            get => _graphX;
            set { _graphX = value; OnPropertyChanged(); }
        }

        public double GraphY
        {
            get => _graphY;
            set { _graphY = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
