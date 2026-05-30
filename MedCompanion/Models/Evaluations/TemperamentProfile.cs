using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MedCompanion.Models.Evaluations
{
    /// <summary>
    /// Profil de tempérament — segment 3 de la Chenille Universelle.
    /// Pas de score, pas de couleur : c'est un portrait descriptif sur 6 axes (1-5),
    /// destiné à visualiser la "forme intérieure" de l'enfant pour ajuster l'environnement.
    /// </summary>
    public class TemperamentProfile : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Set(ref int field, int value, [CallerMemberName] string? n = null)
        {
            // Clamp 1-5 (0 = non renseigné toléré pour l'état initial)
            var clamped = value < 0 ? 0 : (value > 5 ? 5 : value);
            if (field != clamped) { field = clamped; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n)); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRenseigne))); }
        }

        private int _niveauActivite;
        public int NiveauActivite { get => _niveauActivite; set => Set(ref _niveauActivite, value); }

        private int _regularite;
        public int Regularite { get => _regularite; set => Set(ref _regularite, value); }

        private int _reactiviteSensorielle;
        public int ReactiviteSensorielle { get => _reactiviteSensorielle; set => Set(ref _reactiviteSensorielle, value); }

        private int _intensiteEmotionnelle;
        public int IntensiteEmotionnelle { get => _intensiteEmotionnelle; set => Set(ref _intensiteEmotionnelle, value); }

        private int _adaptabilite;
        public int Adaptabilite { get => _adaptabilite; set => Set(ref _adaptabilite, value); }

        private int _tempsDeReaction;
        public int TempsDeReaction { get => _tempsDeReaction; set => Set(ref _tempsDeReaction, value); }

        /// <summary>True si au moins un axe est noté (≥ 1).</summary>
        public bool IsRenseigne
            => NiveauActivite > 0 || Regularite > 0 || ReactiviteSensorielle > 0
            || IntensiteEmotionnelle > 0 || Adaptabilite > 0 || TempsDeReaction > 0;
    }
}
