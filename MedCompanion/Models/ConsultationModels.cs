using System.ComponentModel;

namespace MedCompanion.Models
{
    /// <summary>
    /// États d'affichage du Mode Consultation
    /// Contrôle la répartition entre espace de travail et dossier patient
    /// </summary>
    public enum ConsultationViewState
    {
        /// <summary>
        /// 100% Travail / 0% Dossier - Entretien actif, écriture immersive
        /// </summary>
        FocusTravail,

        /// <summary>
        /// 67% Travail / 33% Dossier - Vérification d'info, rédaction avec référence
        /// </summary>
        Consultation,

        /// <summary>
        /// 0% Travail / 100% Dossier - Revue complète, préparation avant consultation
        /// </summary>
        FocusDossier
    }

    /// <summary>
    /// Modes de comportement de Med en consultation
    /// </summary>
    public enum MedConsultationMode
    {
        /// <summary>
        /// Présent mais attend qu'on l'appelle
        /// </summary>
        Silencieux,

        /// <summary>
        /// Propose activement (interactions, alertes)
        /// </summary>
        Suggestions,

        /// <summary>
        /// Affiche les points à couvrir pour cette consultation
        /// </summary>
        Checklist
    }

    /// <summary>
    /// Intercalaires du dossier patient
    /// </summary>
    public enum DossierTab
    {
        /// <summary>
        /// Page de garde / Couverture du dossier papier
        /// </summary>
        Couverture,

        /// <summary>
        /// Vue condensée, alertes, points clés
        /// </summary>
        Synthese,

        /// <summary>
        /// Coordonnées, correspondants, école
        /// </summary>
        Administratif,

        /// <summary>
        /// Historique des notes de consultation
        /// </summary>
        Consultations,

        /// <summary>
        /// Objectifs, stratégies, suivis
        /// </summary>
        ProjetTherapeutique,

        /// <summary>
        /// Tests, évaluations, résultats
        /// </summary>
        Bilans,

        /// <summary>
        /// Courriers, attestations, ordonnances
        /// </summary>
        Documents
    }

    /// <summary>
    /// Suggestion contextuelle de Med en consultation
    /// </summary>
    public class MedSuggestion : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private string _icon = "";
        public string Icon
        {
            get => _icon;
            set { _icon = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon))); }
        }

        private string _title = "";
        public string Title
        {
            get => _title;
            set { _title = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title))); }
        }

        private string _content = "";
        public string Content
        {
            get => _content;
            set { _content = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Content))); }
        }

        private string _category = "";
        /// <summary>
        /// Catégorie : Interaction, PointAEvoquer, Rappel, Alerte
        /// </summary>
        public string Category
        {
            get => _category;
            set { _category = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Category))); }
        }
    }

    // ─── Interrogatoire V0a ──────────────────────────────────────────────────

    public enum ConsultationType
    {
        Normal,
        PremiereConsultation,
        Suivi,
        BilanInitial,
        ProjetTherapeutique
    }

    public enum InterrogatoireState
    {
        Saisie,
        Extraction,
        FinalNote
    }

    // ─── Trigger type for V0b adaptive blocks ─────────────────────────────

    public enum BlockTriggerType
    {
        /// <summary>Noyau fixe, toujours présent</summary>
        CoreFixed,
        /// <summary>Automatique selon l'âge (petite_enfance vs scolarite)</summary>
        AgeAutomatic,
        /// <summary>Suggestion chip déclenchée par motif</summary>
        MotifChip
    }

    public class BlockDefinition
    {
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public List<string> ExpectedThemes { get; set; } = new();

        // ── V0b fields ──────────────────────────────────────────────────────

        /// <summary>
        /// Type de déclencheur : core_fixed, age_automatic, motif_chip
        /// </summary>
        public string TriggerType { get; set; } = "core_fixed";

        /// <summary>Âge minimum pour ce bloc (0 si pas de contrainte)</summary>
        public int? AgeMin { get; set; }

        /// <summary>Âge maximum pour ce bloc (99 si pas de contrainte)</summary>
        public int? AgeMax { get; set; }

        /// <summary>Mots-clés motif qui déclenchent la suggestion chip</summary>
        public List<string> MotifKeywords { get; set; } = new();

        /// <summary>Ordre d'affichage dans la liste</summary>
        public int Order { get; set; } = 99;

        /// <summary>Parse TriggerType string to enum</summary>
        public BlockTriggerType TriggerTypeEnum => TriggerType?.ToLowerInvariant() switch
        {
            "core_fixed"     => BlockTriggerType.CoreFixed,
            "age_automatic"  => BlockTriggerType.AgeAutomatic,
            "motif_chip"     => BlockTriggerType.MotifChip,
            _                => BlockTriggerType.CoreFixed
        };
    }

    // ─── V0b : Suggestion chip pour bloc supplémentaire ────────────────────

    /// <summary>
    /// Suggestion de bloc supplémentaire, affichée comme chip dans l'UI.
    /// Le médecin accepte (✓) ou ignore (✕).
    /// </summary>
    public class BlockSuggestion : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string BlockKey { get; set; } = "";
        public string Title { get; set; } = "";
        public string Reason { get; set; } = "";

        private bool _isAccepted;
        public bool IsAccepted
        {
            get => _isAccepted;
            set { _isAccepted = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAccepted))); }
        }

        private bool _isDismissed;
        public bool IsDismissed
        {
            get => _isDismissed;
            set { _isDismissed = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDismissed))); }
        }
    }

    public class ConsultationBlock
    {
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public string FreeText { get; set; } = "";
        public List<string> ExpectedThemes { get; set; } = new();
        public List<string> CoveredThemes { get; set; } = new();
        public int ProgressPct => ExpectedThemes.Count == 0
            ? 0
            : (int)(100.0 * CoveredThemes.Count / ExpectedThemes.Count);
    }

    public class BlockUpdate
    {
        public string BlockKey { get; set; } = "";
        public string AppendText { get; set; } = "";
        public List<string> NewThemes { get; set; } = new();
    }

    public class ExtractionResult
    {
        public List<BlockUpdate> Updates { get; set; } = new();
    }

    // ─── Item de checklist ───────────────────────────────────────────────────

    /// <summary>
    /// Item de checklist pour une consultation
    /// </summary>
    public class ChecklistItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private string _text = "";
        public string Text
        {
            get => _text;
            set { _text = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text))); }
        }

        private bool _isChecked = false;
        public bool IsChecked
        {
            get => _isChecked;
            set { _isChecked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); }
        }

        private string _source = "";
        /// <summary>
        /// Source de l'item : "auto" (généré), "user" (ajouté manuellement)
        /// </summary>
        public string Source
        {
            get => _source;
            set { _source = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Source))); }
        }
    }

    public enum QualityIssueType
    {
        Medication,   // nom de médicament phonétique
        Coherence,    // incohérence logique (âge, date...)
        Unclear       // terme ambigu, précision demandée
    }

    public class QualityIssue
    {
        public string           BlockKey    { get; set; } = "";
        public string           BlockTitle  { get; set; } = "";
        public string           Original    { get; set; } = "";
        public string           Suggestion  { get; set; } = "";
        public string           Reason      { get; set; } = "";
        public QualityIssueType Type        { get; set; }
    }

    // ─── Observations Cliniques V0c ────────────────────────────────────────────

    /// <summary>
    /// Les 10 branches d'observation clinique (Clinique/Enfant)
    /// </summary>
    public enum ClinicalObservationBranch
    {
        Contact,
        Langage,
        Comprehension,
        Psychomotricite,
        MimiquRegard,
        ProfilCognitif,
        HumeurAnxiete,
        ImaginaireJeu,
        RapportCadre,
        Vigilance
    }

    /// <summary>
    /// Carte d'observation clinique avec choix radio + notes optionnelles
    /// </summary>
    public class ClinicalObservationCard : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public ClinicalObservationBranch Branch { get; set; }

        private string _title = "";
        public string Title
        {
            get => _title;
            set { _title = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Title))); }
        }

        private List<string> _options = new();
        public List<string> Options
        {
            get => _options;
            set { _options = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Options))); }
        }

        private string? _selectedOption;
        public string? SelectedOption
        {
            get => _selectedOption;
            set { _selectedOption = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedOption))); }
        }

        private string _freeText = "";
        public string FreeText
        {
            get => _freeText;
            set { _freeText = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FreeText))); }
        }

        private bool _isExpanded = false;
        public bool IsExpanded
        {
            get => _isExpanded;
            set { _isExpanded = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded))); }
        }
    }

    /// <summary>
    /// Session d'observations cliniques (phase 2 de la 1ère consultation)
    /// </summary>
    public class ClinicalObservationsSession : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private List<ClinicalObservationCard> _cards = new();
        public List<ClinicalObservationCard> Cards
        {
            get => _cards;
            set { _cards = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Cards))); }
        }

        public DateTime CreatedAt { get; set; }

        private string? _generatedClinicalNarrative;
        public string? GeneratedClinicalNarrative
        {
            get => _generatedClinicalNarrative;
            set { _generatedClinicalNarrative = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GeneratedClinicalNarrative))); }
        }
    }
}
