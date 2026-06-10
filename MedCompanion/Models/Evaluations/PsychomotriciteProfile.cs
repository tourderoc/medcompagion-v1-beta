using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MedCompanion.Models.Evaluations
{
    /// <summary>
    /// Profil psychomoteur — sphère 5 de la Cartographie de l'enfant.
    /// Portrait descriptif sur 6 axes (1-5) : Motricité globale, Motricité fine,
    /// Tonus, Dextérité, Coordination, Impulsivité motrice.
    /// Pas de score global, pas de couleur — interprétation clinique par axe.
    /// </summary>
    public class PsychomotriciteProfile : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Set(ref int field, int value, [CallerMemberName] string? n = null)
        {
            var clamped = value < 0 ? 0 : (value > 5 ? 5 : value);
            if (field != clamped) { field = clamped; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n)); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRenseigne))); }
        }

        private int _motriciteGlobale;
        public int MotriciteGlobale { get => _motriciteGlobale; set => Set(ref _motriciteGlobale, value); }

        private int _motriciteFinee;
        public int MotriciteFine { get => _motriciteFinee; set => Set(ref _motriciteFinee, value); }

        private int _tonus;
        public int Tonus { get => _tonus; set => Set(ref _tonus, value); }

        private int _dexterite;
        public int Dexterite { get => _dexterite; set => Set(ref _dexterite, value); }

        private int _coordination;
        public int Coordination { get => _coordination; set => Set(ref _coordination, value); }

        private int _impulsiviteMotrice;
        public int ImpulsiviteMotrice { get => _impulsiviteMotrice; set => Set(ref _impulsiviteMotrice, value); }

        /// <summary>True si au moins un axe est noté (≥ 1).</summary>
        public bool IsRenseigne
            => MotriciteGlobale > 0 || MotriciteFine > 0 || Tonus > 0
            || Dexterite > 0 || Coordination > 0 || ImpulsiviteMotrice > 0;
    }
}
