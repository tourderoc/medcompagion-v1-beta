using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace MedCompanion.Models.Evaluations
{
    /// <summary>
    /// Un item binaire (Oui/Non) d'une nervure d'une feuille de l'environnement.
    /// Règle universelle : case cochée = Oui = 1 point.
    /// </summary>
    public class FeuilleItem : INotifyPropertyChanged
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

        public FeuilleItem(string affirmation) { Affirmation = affirmation; }
    }

    /// <summary>
    /// Une nervure (centrale ou secondaire) d'une feuille. Contient ses items binaires
    /// et calcule son score brut (0..N) à la demande.
    /// </summary>
    public class Nervure : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public string Key        { get; }
        public string Label      { get; }
        public bool   IsCentrale { get; }

        public ObservableCollection<FeuilleItem> Items { get; } = new();

        public int Score    => Items.Count(i => i.IsChecked);
        public int MaxScore => Items.Count;

        public Nervure(string key, string label, bool isCentrale, params string[] affirmations)
        {
            Key        = key;
            Label      = label;
            IsCentrale = isCentrale;
            foreach (var a in affirmations)
            {
                var item = new FeuilleItem(a);
                item.PropertyChanged += OnItemChanged;
                Items.Add(item);
            }
        }

        private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FeuilleItem.IsChecked))
                OnPropertyChanged(nameof(Score));
        }
    }

    /// <summary>
    /// Une feuille dimensionnelle = 1 nervure centrale + 2 à 4 nervures secondaires.
    /// La couleur n'est pas stockée ici, elle est calculée à la demande par
    /// EnvironnementScoringService selon les seuils canoniques du Tome 3.
    /// </summary>
    public class FeuilleEnvironnement : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public string Key       { get; }
        public string Label     { get; }
        public string SousTitre { get; }

        public Nervure NervureCentrale { get; }
        public ObservableCollection<Nervure> NervuresSecondaires { get; } = new();

        private string? _lectureMed;
        /// <summary>
        /// Lecture clinique courte produite par Med pour cette feuille (D3 V0.3).
        /// Éditable manuellement par le psy après génération.
        /// </summary>
        public string? LectureMed
        {
            get => _lectureMed;
            set { if (_lectureMed != value) { _lectureMed = value; OnPropertyChanged(); } }
        }

        private DateTime? _lectureDate;
        public DateTime? LectureDate
        {
            get => _lectureDate;
            set { if (_lectureDate != value) { _lectureDate = value; OnPropertyChanged(); } }
        }

        public FeuilleEnvironnement(string key, string label, string sousTitre,
                                    Nervure centrale, params Nervure[] secondaires)
        {
            Key             = key;
            Label           = label;
            SousTitre       = sousTitre;
            NervureCentrale = centrale;
            foreach (var n in secondaires) NervuresSecondaires.Add(n);
        }
    }

    /// <summary>
    /// Données de l'Étape 5 — Cartographie de l'environnement.
    /// 5 feuilles dimensionnelles. Outil 3-11 ans (cohérent Étape 4).
    /// Source canonique : Tome 3, Grilles de Cotation Canoniques.
    /// </summary>
    public class CartographieEnvironnement : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        /// <summary>
        /// Âge du patient au moment de la saisie. Conservé pour la traçabilité (immutable
        /// une fois l'étape validée).
        /// </summary>
        public int? AgeAuMomentDeLaSaisie { get; set; }

        public FeuilleEnvironnement Famille           { get; }
        public FeuilleEnvironnement EcolePairs        { get; }
        public FeuilleEnvironnement EcransMedias      { get; }
        public FeuilleEnvironnement ValeursSocietales { get; }
        public FeuilleEnvironnement CadreEducatif     { get; }

        // ── Lecture LLM globale de la branche (V0.4) ────────────────────────

        private string? _lectureBrancheMed;
        /// <summary>
        /// Lecture clinique globale (V0.4) qui croise les 5 feuilles. Générée à la demande
        /// par BrancheEnvironnementLectureService. Éditable manuellement par le psy.
        /// </summary>
        public string? LectureBrancheMed
        {
            get => _lectureBrancheMed;
            set { if (_lectureBrancheMed != value) { _lectureBrancheMed = value; OnPropertyChanged(); } }
        }

        private DateTime? _lectureBrancheDate;
        public DateTime? LectureBrancheDate
        {
            get => _lectureBrancheDate;
            set { if (_lectureBrancheDate != value) { _lectureBrancheDate = value; OnPropertyChanged(); } }
        }

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

        public CartographieEnvironnement()
        {
            Famille           = CartographieEnvironnementContent.NewFamille();
            EcolePairs        = CartographieEnvironnementContent.NewEcolePairs();
            EcransMedias      = CartographieEnvironnementContent.NewEcransMedias();
            ValeursSocietales = CartographieEnvironnementContent.NewValeursSocietales();
            CadreEducatif     = CartographieEnvironnementContent.NewCadreEducatif();
        }
    }
}
