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

    public class BlockDefinition
    {
        public string Key { get; set; } = "";
        public string Title { get; set; } = "";
        public List<string> ExpectedThemes { get; set; } = new();
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
}
