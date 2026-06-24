using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace MedCompanion.Models.Evaluations
{
    /// <summary>
    /// Un segment de la Chenille Universelle avec questionnaire 6 items binaires.
    /// Le Score (0-6) est recalculé automatiquement quand un item change.
    /// Le Niveau couleur est attribué par CartographieScoringService selon l'âge — pas
    /// stocké directement ici, calculé à la demande par le ViewModel.
    /// </summary>
    public class ChenilleSegment : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public string Key            { get; }
        public string Label          { get; }
        public string PhraseBoussole { get; }

        public ObservableCollection<ChenilleItem> Items { get; } = new();

        public int Score => Items.Count(i => i.IsChecked);

        /// <summary>
        /// True si le médecin a explicitement évalué ce segment (même à 0/6).
        /// Distingue "non touché" (false) de "évalué à zéro" (true + Score=0).
        /// </summary>
        public bool IsEvaluated { get; set; }

        public ChenilleSegment(string key, string label, string phraseBoussole, params string[] affirmations)
        {
            Key            = key;
            Label          = label;
            PhraseBoussole = phraseBoussole;
            foreach (var a in affirmations)
            {
                var item = new ChenilleItem(a);
                item.PropertyChanged += OnItemChanged;
                Items.Add(item);
            }
        }

        private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChenilleItem.IsChecked))
                OnPropertyChanged(nameof(Score));
        }
    }

    /// <summary>
    /// Un item du questionnaire d'un segment : affirmation fixe + case binaire.
    /// </summary>
    public class ChenilleItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public string Affirmation { get; }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked != value) { _isChecked = value; OnPropertyChanged(); } }
        }

        public ChenilleItem(string affirmation) { Affirmation = affirmation; }
    }
}
