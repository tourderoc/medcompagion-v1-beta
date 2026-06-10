using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MedCompanion.Models.Evaluations
{
    /// <summary>
    /// Profil Attention &amp; Fonctions Exécutives — sphère 8 de la Cartographie de l'enfant.
    /// Portrait descriptif sur 6 axes (1-5) : Attention soutenue, Attention sélective,
    /// Attention divisée, Inhibition, Planification, Flexibilité attentionnelle.
    /// Pas de score global, pas de couleur — interprétation clinique par axe.
    /// </summary>
    public class AttentionProfile : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Set(ref int field, int value, [CallerMemberName] string? n = null)
        {
            var clamped = value < 0 ? 0 : (value > 5 ? 5 : value);
            if (field != clamped) { field = clamped; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n)); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsRenseigne))); }
        }

        private int _attentionSoutenue;
        public int AttentionSoutenue { get => _attentionSoutenue; set => Set(ref _attentionSoutenue, value); }

        private int _attentionSelective;
        public int AttentionSelective { get => _attentionSelective; set => Set(ref _attentionSelective, value); }

        private int _attentionDivisee;
        public int AttentionDivisee { get => _attentionDivisee; set => Set(ref _attentionDivisee, value); }

        private int _inhibition;
        public int Inhibition { get => _inhibition; set => Set(ref _inhibition, value); }

        private int _planification;
        public int Planification { get => _planification; set => Set(ref _planification, value); }

        private int _flexibiliteAttentionnelle;
        public int FlexibiliteAttentionnelle { get => _flexibiliteAttentionnelle; set => Set(ref _flexibiliteAttentionnelle, value); }

        /// <summary>True si au moins un axe est noté (≥ 1).</summary>
        public bool IsRenseigne
            => AttentionSoutenue > 0 || AttentionSelective > 0 || AttentionDivisee > 0
            || Inhibition > 0 || Planification > 0 || FlexibiliteAttentionnelle > 0;
    }
}
