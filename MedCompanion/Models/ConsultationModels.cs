using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using MedCompanion.Commands;

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

    /// <summary>Option cochable pour une carte d'observation clinique</summary>
    public class ObservationOptionViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string Label { get; init; } = "";

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public ICommand ToggleCommand => _cmd ??= new RelayCommand(_ => IsSelected = !IsSelected);
        private ICommand? _cmd;
    }

    /// <summary>
    /// Carte d'observation clinique avec cases à cocher + notes optionnelles.
    /// Les options peuvent être génériques (fallback) ou générées par le LLM.
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

        // Options sous forme d'items cochables (remplace List<string> Options)
        private ObservableCollection<ObservationOptionViewModel> _optionItems = new();
        public ObservableCollection<ObservationOptionViewModel> OptionItems
        {
            get => _optionItems;
            private set
            {
                _optionItems = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(OptionItems)));
                NotifySelectionChanged();
            }
        }

        /// <summary>Remplace les options et subscribe aux changements de chaque item.</summary>
        public void SetOptions(IEnumerable<string> labels, IEnumerable<string>? preselected = null)
        {
            var preSet = preselected?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var items = new ObservableCollection<ObservationOptionViewModel>();
            foreach (var l in labels)
            {
                var opt = new ObservationOptionViewModel { Label = l };
                if (preSet.Contains(l)) opt.IsSelected = true;
                opt.PropertyChanged += OnOptionChanged;
                items.Add(opt);
            }
            OptionItems = items;
        }

        private void OnOptionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ObservationOptionViewModel.IsSelected))
                NotifySelectionChanged();
        }

        private void NotifySelectionChanged()
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasAnySelection)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedOptionsText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedOption)));
        }

        public bool HasAnySelection => OptionItems.Any(o => o.IsSelected);
        public string SelectedOptionsText => string.Join(", ", OptionItems.Where(o => o.IsSelected).Select(o => o.Label));
        public string? SelectedOption => OptionItems.FirstOrDefault(o => o.IsSelected)?.Label; // compat save/load

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

        public ICommand ToggleExpandCommand => _toggleExpandCommand ??= new RelayCommand(_ => IsExpanded = !IsExpanded);
        private ICommand? _toggleExpandCommand;
    }

    /// <summary>
    /// Session d'observations cliniques (phase 2 de la 1ère consultation)
    /// </summary>
    public class ClinicalObservationsSession : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private ObservableCollection<ClinicalObservationCard> _cards = new();
        public ObservableCollection<ClinicalObservationCard> Cards
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

    // ─── Restitution aux Parents V0e ────────────────────────────────────────

    public enum TypeAccompagnant
    {
        LesDeuParents,
        MereSeule,
        PereSeul,
        GrandParents,
        Educateur,
        Autre
    }

    public class RestitutionAuxParents : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Notify(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));

        private TypeAccompagnant _typeAccompagnant = TypeAccompagnant.LesDeuParents;
        public TypeAccompagnant TypeAccompagnant
        {
            get => _typeAccompagnant;
            set { _typeAccompagnant = value; Notify(nameof(TypeAccompagnant)); Notify(nameof(MentionLegale)); Notify(nameof(HasMentionLegale)); }
        }

        private string _nomAccompagnant = "";
        public string NomAccompagnant
        {
            get => _nomAccompagnant;
            set { _nomAccompagnant = value; Notify(nameof(NomAccompagnant)); Notify(nameof(MentionLegale)); }
        }

        private int _nombreSeances = 2;
        public int NombreSeances
        {
            get => _nombreSeances;
            set { _nombreSeances = value; Notify(nameof(NombreSeances)); }
        }

        private string _notesLibres = "";
        public string NotesLibres
        {
            get => _notesLibres;
            set { _notesLibres = value; Notify(nameof(NotesLibres)); }
        }

        public string MentionLegale
        {
            get
            {
                var accomp = string.IsNullOrWhiteSpace(NomAccompagnant) ? "l'accompagnant" : NomAccompagnant;
                return TypeAccompagnant switch
                {
                    TypeAccompagnant.LesDeuParents => "",
                    TypeAccompagnant.MereSeule =>
                        $"Ce document a été remis à la mère. Conformément à l'exercice conjoint de l'autorité parentale, il doit être communiqué au père.",
                    TypeAccompagnant.PereSeul =>
                        $"Ce document a été remis au père. Conformément à l'exercice conjoint de l'autorité parentale, il doit être communiqué à la mère.",
                    TypeAccompagnant.GrandParents =>
                        $"Ce document a été remis aux grands-parents ({accomp}). Les représentants légaux de l'enfant doivent en être informés.",
                    TypeAccompagnant.Educateur =>
                        $"Ce document a été remis à l'éducateur/référent ({accomp}). Les représentants légaux de l'enfant doivent en être informés.",
                    _ =>
                        $"Ce document a été remis à {accomp}. Les représentants légaux de l'enfant doivent en être informés."
                };
            }
        }

        public bool HasMentionLegale => TypeAccompagnant != TypeAccompagnant.LesDeuParents;

        public DateTime DateRestitution { get; set; } = DateTime.Now;
        public string? GeneratedHtmlPath { get; set; }
        public string? GeneratedPdfPath { get; set; }
    }

    // ─── Synthèse Initiale V0d ──────────────────────────────────────────────

    /// <summary>
    /// Poids proposés pour chaque composant de la synthèse initiale
    /// Basés sur l'analyse IA + validation Med
    /// </summary>
    public class InitialSynthesisWeights : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private double _interrogatoireWeight = 0.5;
        /// <summary>Poids de l'interrogatoire (fiabilité données parental)</summary>
        public double InterrogatoireWeight
        {
            get => _interrogatoireWeight;
            set { _interrogatoireWeight = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InterrogatoireWeight))); }
        }

        private double _observationsWeight = 0.8;
        /// <summary>Poids des observations cliniques directes</summary>
        public double ObservationsWeight
        {
            get => _observationsWeight;
            set { _observationsWeight = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ObservationsWeight))); }
        }

        private Dictionary<string, double> _documentWeights = new();
        /// <summary>Poids individuels des documents/bilans importés</summary>
        public Dictionary<string, double> DocumentWeights
        {
            get => _documentWeights;
            set { _documentWeights = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DocumentWeights))); }
        }

        private string? _llmJustification;
        /// <summary>Justification courte de l'évaluation des poids par l'IA</summary>
        public string? LLMJustification
        {
            get => _llmJustification;
            set { _llmJustification = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LLMJustification))); }
        }

        /// <summary>Poids moyen global (pour tracking accumulation)</summary>
        public double AverageWeight
        {
            get
            {
                var allWeights = new List<double> { _interrogatoireWeight, _observationsWeight };
                allWeights.AddRange(_documentWeights.Values);
                return allWeights.Count > 0 ? allWeights.Average() : 0.5;
            }
        }
    }

    // ─── V0d : Document Importé en Consultation ─────────────────────────

    /// <summary>
    /// Document importé/scanné pendant la consultation
    /// Peut être un bilan, rapport, etc. avec synthèse générée
    /// </summary>
    public class ImportedConsultationDocument : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string DocumentId { get; set; } = Guid.NewGuid().ToString();
        public DateTime DateAdded { get; set; } = DateTime.Now;

        private string _fileName = "";
        public string FileName
        {
            get => _fileName;
            set { _fileName = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FileName))); }
        }

        private string _filePath = "";
        public string FilePath
        {
            get => _filePath;
            set { _filePath = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FilePath))); }
        }

        private string _documentSynthesis = "";
        /// <summary>Synthèse générée du document par LLM</summary>
        public string DocumentSynthesis
        {
            get => _documentSynthesis;
            set { _documentSynthesis = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DocumentSynthesis))); }
        }

        private double _weight = 0.5;
        /// <summary>Poids de fiabilité du document (0.1-1.0)</summary>
        public double Weight
        {
            get => _weight;
            set { _weight = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Weight))); }
        }

        private bool _isSynthesizing = false;
        public bool IsSynthesizing
        {
            get => _isSynthesizing;
            set { _isSynthesizing = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSynthesizing))); }
        }

        private string _category = "Documents";
        /// <summary>Catégorie: Documents, Bilans, etc.</summary>
        public string Category
        {
            get => _category;
            set { _category = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Category))); }
        }
    }

    // ─── Consultation de Suivi V0 ───────────────────────────────────────────

    /// <summary>
    /// État d'une consultation de suivi : cases rapides + transcription + extraction IA.
    /// "RAS / Va bien" est exclusif : si coché, les autres cases et la transcription sont ignorées.
    /// </summary>
    public class ConsultationSuivi : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void Set<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string name = "")
        {
            if (!Equals(field, value)) { field = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)); }
        }

        private bool _ras;                 public bool RAS                 { get => _ras; set => Set(ref _ras, value); }
        private bool _renouvellement;      public bool Renouvellement      { get => _renouvellement; set => Set(ref _renouvellement, value); }
        private bool _pasEffetsSecondaires;public bool PasEffetsSecondaires{ get => _pasEffetsSecondaires; set => Set(ref _pasEffetsSecondaires, value); }
        private bool _adhesionOk;          public bool AdhesionOk          { get => _adhesionOk; set => Set(ref _adhesionOk, value); }
        private bool _evolutionScolaire;   public bool EvolutionScolaire   { get => _evolutionScolaire; set => Set(ref _evolutionScolaire, value); }
        private bool _sommeilCorrect;      public bool SommeilCorrect      { get => _sommeilCorrect; set => Set(ref _sommeilCorrect, value); }
        private bool _aRevoir;             public bool ARevoir             { get => _aRevoir; set => Set(ref _aRevoir, value); }

        private string _transcription = "";
        public string Transcription { get => _transcription; set => Set(ref _transcription, value); }

        private string _aiExtraction = "";
        /// <summary>Extraction IA en puces compactes (généré depuis la transcription).</summary>
        public string AIExtraction { get => _aiExtraction; set => Set(ref _aiExtraction, value); }

        public void Reset()
        {
            RAS = false; Renouvellement = false; PasEffetsSecondaires = false;
            AdhesionOk = false; EvolutionScolaire = false; SommeilCorrect = false; ARevoir = false;
            Transcription = ""; AIExtraction = "";
        }
    }
}
