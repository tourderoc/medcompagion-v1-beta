using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MedCompanion.Models.Evaluations
{
    /// <summary>
    /// Données de l'Étape 4 — Cartographie de l'enfant.
    /// 7 segments : 6 questionnaires (Attachement, Psychomotricité, Langage, Émotions, Imaginaire, Pensée)
    /// + 1 profil descriptif (Tempérament, sans score).
    /// Outil 3-11 ans. Hors fourchette → étape sautée par le ViewModel.
    /// </summary>
    public class CartographieEnfant : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        /// <summary>
        /// Âge du patient au moment de la saisie. Conservé pour la traçabilité (immutable
        /// une fois l'étape validée — sinon le scoring deviendrait incohérent avec l'âge actuel).
        /// </summary>
        public int? AgeAuMomentDeLaSaisie { get; set; }

        public ChenilleSegment Attachement            { get; }
        public PsychomotriciteProfile Psychomotricite { get; } = new();
        public TemperamentProfile Temperament         { get; } = new();
        public AttentionProfile Attention             { get; } = new();
        public ChenilleSegment Langage         { get; }
        public ChenilleSegment Emotions        { get; }
        public ChenilleSegment Imaginaire      { get; }
        public ChenilleSegment Pensee          { get; }

        private DateTime? _validationDate;
        public DateTime? ValidationDate
        {
            get => _validationDate;
            set
            {
                if (_validationDate != value)
                {
                    _validationDate = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsValidated));
                }
            }
        }
        public bool IsValidated => ValidationDate.HasValue;

        public CartographieEnfant()
        {
            // Les affirmations et phrases-boussoles canoniques viennent de CartographieContent
            // (créé à la sous-étape 3). Pour l'instant, segments instanciés avec contenu placeholder
            // — sera remplacé par les valeurs canoniques quand CartographieContent existera.
            Attachement = CartographieContent.NewAttachement();
            Langage     = CartographieContent.NewLangage();
            Emotions        = CartographieContent.NewEmotions();
            Imaginaire      = CartographieContent.NewImaginaire();
            Pensee          = CartographieContent.NewPensee();
        }
    }
}
