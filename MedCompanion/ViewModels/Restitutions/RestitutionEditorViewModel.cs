using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Models.Restitutions;
using MedCompanion.Services.Restitutions;

namespace MedCompanion.ViewModels.Restitutions
{
    public class RpSectionViewModel : INotifyPropertyChanged
    {
        public string Title { get; }

        private string _content = "";
        public string Content
        {
            get => _content;
            set
            {
                if (_content == value) return;
                _content = value ?? "";
                OnPropertyChanged();
                ContentChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        private bool _isInstructionPanelVisible;
        public bool IsInstructionPanelVisible
        {
            get => _isInstructionPanelVisible;
            set { if (_isInstructionPanelVisible == value) return; _isInstructionPanelVisible = value; OnPropertyChanged(); }
        }

        private string _userInstruction = "";
        public string UserInstruction
        {
            get => _userInstruction;
            set { if (_userInstruction == value) return; _userInstruction = value ?? ""; OnPropertyChanged(); }
        }

        private bool _isGenerating;
        public bool IsGenerating
        {
            get => _isGenerating;
            set
            {
                if (_isGenerating == value) return;
                _isGenerating = value;
                OnPropertyChanged();
                (ToggleInstructionCommand as RelayCommand)?.RaiseCanExecuteChanged();
                (RegenerateCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public ICommand ToggleInstructionCommand { get; private set; } = null!;
        public ICommand RegenerateCommand        { get; private set; } = null!;
        public ICommand CancelInstructionCommand { get; private set; } = null!;

        public event EventHandler? ContentChanged;

        public RpSectionViewModel(string title) { Title = title; }

        public void InitActions(Func<Task> regenerateAction)
        {
            ToggleInstructionCommand = new RelayCommand(
                _ => IsInstructionPanelVisible = !IsInstructionPanelVisible,
                _ => !IsGenerating);
            RegenerateCommand = new RelayCommand(
                async _ => await regenerateAction(),
                _ => !IsGenerating);
            CancelInstructionCommand = new RelayCommand(_ =>
            {
                IsInstructionPanelVisible = false;
                UserInstruction = "";
            });
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public class RestitutionBlocViewModel : INotifyPropertyChanged
    {
        public RestitutionBloc Model { get; }

        public string Title => Model.Titre;
        public string SectionType => Model.Key;
        public bool IsCouverture => Model.Key == "couverture";
        public bool IsRestitutionParents => Model.Key == "restitution_1page";
        public bool IsIdentification   => Model.Key == "patient_identification";
        public bool IsContexteFamilial => Model.Key == "patient_contexte_familial";
        public bool HasReformuleButton => !IsCouverture && !IsIdentification && !IsRestitutionParents && !IsContexteFamilial;

        // ── Champs structurés du bloc restitution parents ─────────────────────

        public ObservableCollection<RpSectionViewModel> RpSections { get; } = new();
        private bool _syncingParents;

        private static readonly string[] RpMarkdownTitles =
        {
            "**Ce que nous avons compris**",
            "**Ses forces et ses réussites**",
            "**Les difficultés actuellement observées**",
            "**Ce qui peut aider**",
            "**Notre feuille de route**",
            "**Son environnement : points clés**"
        };

        public static readonly string[] RpDisplayTitles =
        {
            "Ce que nous avons compris",
            "Ses forces et ses réussites",
            "Les difficultés actuellement observées",
            "Ce qui peut aider",
            "Notre feuille de route",
            "Son environnement : points clés"
        };

        // ── Champs structurés du bloc couverture ──────────────────────────────
        // Synchronisés avec Contenu (parse ↔ sérialise le markdown **Label** : valeur).

        private bool _syncingCouverture; // anti-boucle parse ↔ serialize

        private string _cvNomPrenom    = "";
        private string _cvDateNaissance= "";
        private string _cvAge          = "";
        private string _cvEcole        = "";
        private string _cvClasse       = "";
        private string _cvAnneeScolaire= "";
        private string _cvMotif        = "";
        private string _cvDatesEval    = "";

        public string CvNomPrenom     { get => _cvNomPrenom;     set { if (SetCv(ref _cvNomPrenom,     value)) FlushCouvertureToContenu(); } }
        public string CvDateNaissance { get => _cvDateNaissance; set { if (SetCv(ref _cvDateNaissance, value)) FlushCouvertureToContenu(); } }
        public string CvAge           { get => _cvAge;           set { if (SetCv(ref _cvAge,           value)) FlushCouvertureToContenu(); } }
        public string CvEcole         { get => _cvEcole;         set { if (SetCv(ref _cvEcole,         value)) FlushCouvertureToContenu(); } }
        public string CvClasse        { get => _cvClasse;        set { if (SetCv(ref _cvClasse,        value)) FlushCouvertureToContenu(); } }
        public string CvAnneeScolaire { get => _cvAnneeScolaire; set { if (SetCv(ref _cvAnneeScolaire, value)) FlushCouvertureToContenu(); } }
        public string CvMotif         { get => _cvMotif;         set { if (SetCv(ref _cvMotif,         value)) FlushCouvertureToContenu(); } }
        public string CvDatesEval     { get => _cvDatesEval;     set { if (SetCv(ref _cvDatesEval,     value)) FlushCouvertureToContenu(); } }

        private bool SetCv(ref string field, string value)
        {
            if (field == value) return false;
            field = value ?? "";
            return true;
        }

        private void ParseContenuToCouvertureFields()
        {
            if (_syncingCouverture) return;
            _syncingCouverture = true;
            try
            {
                string Pick(params string[] labels)
                {
                    foreach (var lbl in labels)
                    {
                        var m = Regex.Match(Model.ContenuValide ?? "",
                            $@"\*\*\s*{Regex.Escape(lbl)}\s*\*\*\s*:\s*(.+?)(?:\r?\n|$)",
                            RegexOptions.IgnoreCase);
                        if (m.Success)
                        {
                            var v = m.Groups[1].Value.Trim();
                            if (!string.IsNullOrWhiteSpace(v)) return v;
                        }
                    }
                    return "";
                }

                _cvNomPrenom     = Pick("Nom et prénom", "Nom", "Prénom et nom");
                _cvDateNaissance = Pick("Date de naissance", "Naissance");
                _cvAge           = Pick("Âge", "Age");
                _cvEcole         = Pick("Établissement", "Etablissement", "École", "Ecole");
                _cvClasse        = Pick("Classe", "Niveau scolaire");
                _cvAnneeScolaire = Pick("Année scolaire", "Annee scolaire");
                _cvMotif         = Pick("Motif de consultation", "Motif");
                _cvDatesEval     = Pick("Dates d'évaluation", "Date d'évaluation", "Dates d'evaluation", "Évaluations");

                OnPropertyChanged(nameof(CvNomPrenom));
                OnPropertyChanged(nameof(CvDateNaissance));
                OnPropertyChanged(nameof(CvAge));
                OnPropertyChanged(nameof(CvEcole));
                OnPropertyChanged(nameof(CvClasse));
                OnPropertyChanged(nameof(CvAnneeScolaire));
                OnPropertyChanged(nameof(CvMotif));
                OnPropertyChanged(nameof(CvDatesEval));
            }
            finally { _syncingCouverture = false; }
        }

        private void FlushCouvertureToContenu()
        {
            if (_syncingCouverture) return;
            _syncingCouverture = true;
            try
            {
                var sb = new StringBuilder();
                void Line(string label, string val)
                    => sb.AppendLine($"**{label}** : {(string.IsNullOrWhiteSpace(val) ? "Non renseigné" : val.Trim())}");

                Line("Nom et prénom",        _cvNomPrenom);
                Line("Date de naissance",    _cvDateNaissance);
                Line("Âge",                  _cvAge);
                Line("Établissement",        _cvEcole);
                Line("Classe",               _cvClasse);
                Line("Année scolaire",       _cvAnneeScolaire);
                Line("Motif de consultation",_cvMotif);
                Line("Dates d'évaluation",   _cvDatesEval);

                Model.ContenuValide = sb.ToString().TrimEnd();
                OnPropertyChanged(nameof(Contenu));
            }
            finally { _syncingCouverture = false; }
        }

        // ── Init / Parse / Flush du bloc restitution parents ─────────────────

        private void InitRpSections(Func<RestitutionBlocViewModel, int, RpSectionViewModel, Task>? sectionAction)
        {
            for (int i = 0; i < RpDisplayTitles.Length; i++)
            {
                var idx     = i;
                var section = new RpSectionViewModel(RpDisplayTitles[i]);
                var secRef  = section;
                Func<Task> regenerate = sectionAction != null
                    ? () => sectionAction(this, idx, secRef)
                    : () => Task.CompletedTask;
                section.InitActions(regenerate);
                section.ContentChanged += (_, __) => FlushParentsToContenu();
                RpSections.Add(section);
            }
            ParseContenuToParentsFields();
        }

        private void ParseContenuToParentsFields()
        {
            if (_syncingParents || RpSections.Count == 0) return;
            _syncingParents = true;
            try
            {
                var contenu = Model.ContenuValide ?? "";
                for (int i = 0; i < RpMarkdownTitles.Length && i < RpSections.Count; i++)
                {
                    var startTitle = RpMarkdownTitles[i];
                    var startIdx = contenu.IndexOf(startTitle, StringComparison.OrdinalIgnoreCase);
                    if (startIdx < 0) { RpSections[i].Content = ""; continue; }

                    var afterTitle = startIdx + startTitle.Length;
                    while (afterTitle < contenu.Length && (contenu[afterTitle] == '\r' || contenu[afterTitle] == '\n'))
                        afterTitle++;

                    var endIdx = contenu.Length;
                    for (int j = i + 1; j < RpMarkdownTitles.Length; j++)
                    {
                        var nextIdx = contenu.IndexOf(RpMarkdownTitles[j], afterTitle, StringComparison.OrdinalIgnoreCase);
                        if (nextIdx >= 0 && nextIdx < endIdx) { endIdx = nextIdx; break; }
                    }

                    RpSections[i].Content = contenu.Substring(afterTitle, endIdx - afterTitle).TrimEnd();
                }
            }
            finally { _syncingParents = false; }
        }

        private void FlushParentsToContenu()
        {
            if (_syncingParents) return;
            _syncingParents = true;
            try
            {
                var sb = new StringBuilder();
                for (int i = 0; i < RpSections.Count; i++)
                {
                    if (i > 0) sb.AppendLine();
                    sb.AppendLine(RpMarkdownTitles[i]);
                    sb.AppendLine();
                    sb.AppendLine(RpSections[i].Content);
                }
                Model.ContenuValide = sb.ToString().TrimEnd();
                OnPropertyChanged(nameof(Contenu));
            }
            finally { _syncingParents = false; }
        }

        // ── Champs structurés du bloc identification ──────────────────────────

        private bool _syncingIdentification;

        private string _idPresentation    = "";
        private string _idPeriodeEval     = "";
        private string _idDateRestitution = "";
        private string _idEvaluateur      = "";
        private string _idLieu            = "";

        public string IdPresentation    { get => _idPresentation;    set { if (SetId(ref _idPresentation,    value)) FlushIdentificationToContenu(); } }
        public string IdPeriodeEval     { get => _idPeriodeEval;     set { if (SetId(ref _idPeriodeEval,     value)) FlushIdentificationToContenu(); } }
        public string IdDateRestitution { get => _idDateRestitution; set { if (SetId(ref _idDateRestitution, value)) FlushIdentificationToContenu(); } }
        public string IdEvaluateur      { get => _idEvaluateur;      set { if (SetId(ref _idEvaluateur,      value)) FlushIdentificationToContenu(); } }
        public string IdLieu            { get => _idLieu;            set { if (SetId(ref _idLieu,            value)) FlushIdentificationToContenu(); } }

        private bool SetId(ref string field, string value)
        {
            if (field == value) return false;
            field = value ?? "";
            return true;
        }

        private void ParseContenuToIdentificationFields()
        {
            if (_syncingIdentification) return;
            _syncingIdentification = true;
            try
            {
                var contenu = Model.ContenuValide ?? "";

                // Extrait la valeur (monoligne ou multiligne) entre **Label** : et le prochain **Label** ou fin
                string PickBlock(string label, params string[] nextLabels)
                {
                    var marker   = $"**{label}**";
                    var startIdx = contenu.IndexOf(marker, StringComparison.Ordinal);
                    if (startIdx < 0) return "";
                    var colonIdx = contenu.IndexOf(':', startIdx + marker.Length);
                    if (colonIdx < 0) return "";
                    var after = colonIdx + 1;
                    while (after < contenu.Length && contenu[after] == ' ') after++;
                    var end = contenu.Length;
                    foreach (var nl in nextLabels)
                    {
                        var ni = contenu.IndexOf($"**{nl}**", after, StringComparison.Ordinal);
                        if (ni >= 0 && ni < end) end = ni;
                    }
                    return contenu.Substring(after, end - after).TrimEnd();
                }

                _idPresentation    = PickBlock("Présentation", "Période d'évaluation", "Evaluateur", "Lieu");
                _idPeriodeEval     = PickBlock("Période d'évaluation", "Date de restitution", "Évaluateur", "Evaluateur", "Lieu");
                _idDateRestitution = PickBlock("Date de restitution", "Évaluateur", "Evaluateur", "Lieu");
                _idEvaluateur      = PickBlock("Évaluateur", "Evaluateur", "Lieu");
                if (string.IsNullOrEmpty(_idEvaluateur)) _idEvaluateur = PickBlock("Evaluateur", "Lieu");
                _idLieu            = PickBlock("Lieu");

                OnPropertyChanged(nameof(IdPresentation));
                OnPropertyChanged(nameof(IdPeriodeEval));
                OnPropertyChanged(nameof(IdDateRestitution));
                OnPropertyChanged(nameof(IdEvaluateur));
                OnPropertyChanged(nameof(IdLieu));
            }
            finally { _syncingIdentification = false; }
        }

        private void FlushIdentificationToContenu()
        {
            if (_syncingIdentification) return;
            _syncingIdentification = true;
            try
            {
                var sb = new StringBuilder();
                void Line(string label, string val)
                    => sb.AppendLine($"**{label}** : {(string.IsNullOrWhiteSpace(val) ? "Non renseigné(e)" : val.Trim())}");

                sb.AppendLine($"**Présentation** : {(string.IsNullOrWhiteSpace(_idPresentation) ? "Non renseigné(e)" : _idPresentation.Trim())}");
                sb.AppendLine();
                Line("Période d'évaluation", _idPeriodeEval);
                Line("Date de restitution",  _idDateRestitution);
                Line("Évaluateur",           _idEvaluateur);
                Line("Lieu",                 _idLieu);

                Model.ContenuValide = sb.ToString().TrimEnd();
                OnPropertyChanged(nameof(Contenu));
            }
            finally { _syncingIdentification = false; }
        }

        // ── Champs structurés du bloc contexte familial ──────────────────────

        private bool _syncingContexteFamilial;

        private string _cfRecit         = "";
        private string _cfPere          = "";
        private string _cfMere          = "";
        private string _cfFratrie       = "";
        private string _cfAutresFigures = "";
        private string _cfPointsRetenir = "";

        public string CfRecit         { get => _cfRecit;         set { if (SetCf(ref _cfRecit,         value)) FlushContexteFamilialToContenu(); } }
        public string CfPere          { get => _cfPere;          set { if (SetCf(ref _cfPere,          value)) FlushContexteFamilialToContenu(); } }
        public string CfMere          { get => _cfMere;          set { if (SetCf(ref _cfMere,          value)) FlushContexteFamilialToContenu(); } }
        public string CfFratrie       { get => _cfFratrie;       set { if (SetCf(ref _cfFratrie,       value)) FlushContexteFamilialToContenu(); } }
        public string CfAutresFigures { get => _cfAutresFigures; set { if (SetCf(ref _cfAutresFigures, value)) FlushContexteFamilialToContenu(); } }
        public string CfPointsRetenir { get => _cfPointsRetenir; set { if (SetCf(ref _cfPointsRetenir, value)) FlushContexteFamilialToContenu(); } }

        private bool SetCf(ref string field, string value)
        {
            if (field == value) return false;
            field = value ?? "";
            return true;
        }

        private void ParseContenuToContexteFamilialFields()
        {
            if (_syncingContexteFamilial) return;
            _syncingContexteFamilial = true;
            try
            {
                var contenu = Model.ContenuValide ?? "";

                string PickCf(string label, params string[] nextLabels)
                {
                    var marker   = $"**{label}**";
                    var startIdx = contenu.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                    if (startIdx < 0) return "";
                    var after = startIdx + marker.Length;
                    // skip optional " :" suffix
                    if (after < contenu.Length && contenu[after] == ' ') after++;
                    if (after < contenu.Length && contenu[after] == ':') after++;
                    while (after < contenu.Length && (contenu[after] == ' ' || contenu[after] == '\r' || contenu[after] == '\n')) after++;
                    var end = contenu.Length;
                    foreach (var nl in nextLabels)
                    {
                        var ni = contenu.IndexOf($"**{nl}**", after, StringComparison.OrdinalIgnoreCase);
                        if (ni >= 0 && ni < end) end = ni;
                    }
                    return contenu.Substring(after, end - after).TrimEnd();
                }

                _cfRecit         = PickCf("Récit familial",  "Père", "Mère", "Fratrie", "Autres figures", "Points à retenir");
                _cfPere          = PickCf("Père",             "Mère", "Fratrie", "Autres figures", "Points à retenir");
                _cfMere          = PickCf("Mère",             "Fratrie", "Autres figures", "Points à retenir");
                _cfFratrie       = PickCf("Fratrie",          "Autres figures", "Points à retenir");
                _cfAutresFigures = PickCf("Autres figures",   "Points à retenir");
                _cfPointsRetenir = PickCf("Points à retenir");

                OnPropertyChanged(nameof(CfRecit));
                OnPropertyChanged(nameof(CfPere));
                OnPropertyChanged(nameof(CfMere));
                OnPropertyChanged(nameof(CfFratrie));
                OnPropertyChanged(nameof(CfAutresFigures));
                OnPropertyChanged(nameof(CfPointsRetenir));
            }
            finally { _syncingContexteFamilial = false; }
        }

        private void FlushContexteFamilialToContenu()
        {
            if (_syncingContexteFamilial) return;
            _syncingContexteFamilial = true;
            try
            {
                var sb = new StringBuilder();
                void Block(string label, string val)
                {
                    sb.AppendLine($"**{label}**");
                    sb.AppendLine();
                    if (!string.IsNullOrWhiteSpace(val)) sb.AppendLine(val.Trim());
                    sb.AppendLine();
                }

                Block("Récit familial",  _cfRecit);
                Block("Père",            _cfPere);
                Block("Mère",            _cfMere);
                Block("Fratrie",         _cfFratrie);
                Block("Autres figures",  _cfAutresFigures);
                sb.AppendLine("**Points à retenir**");
                sb.AppendLine();
                if (!string.IsNullOrWhiteSpace(_cfPointsRetenir)) sb.Append(_cfPointsRetenir.Trim());

                Model.ContenuValide = sb.ToString().TrimEnd();
                OnPropertyChanged(nameof(Contenu));
            }
            finally { _syncingContexteFamilial = false; }
        }

        // ── Contenu brut (TextBox pour les autres blocs) ──────────────────────

        public string Contenu
        {
            get => Model.ContenuValide;
            set
            {
                if (Model.ContenuValide != value)
                {
                    Model.ContenuValide = value;
                    OnPropertyChanged();
                    if (IsCouverture) ParseContenuToCouvertureFields();
                    if (IsRestitutionParents) ParseContenuToParentsFields();
                    if (IsIdentification) ParseContenuToIdentificationFields();
                    if (IsContexteFamilial) ParseContenuToContexteFamilialFields();
                }
            }
        }

        // ── Reformulation avec instruction (blocs texte libre) ───────────────

        private bool _isReformulePanelVisible;
        public bool IsReformulePanelVisible
        {
            get => _isReformulePanelVisible;
            set { if (_isReformulePanelVisible == value) return; _isReformulePanelVisible = value; OnPropertyChanged(); }
        }

        private string _reformuleInstruction = "";
        public string ReformuleInstruction
        {
            get => _reformuleInstruction;
            set { if (_reformuleInstruction == value) return; _reformuleInstruction = value ?? ""; OnPropertyChanged(); }
        }

        public ICommand ToggleReformulePanelCommand { get; private set; } = null!;
        public ICommand RegenerateCommand           { get; private set; } = null!;
        public ICommand CancelReformulePanelCommand { get; private set; } = null!;

        private bool _isGenerating;
        public bool IsGenerating
        {
            get => _isGenerating;
            set
            {
                if (_isGenerating != value)
                {
                    _isGenerating = value;
                    OnPropertyChanged();
                    (GenerateCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (ToggleReformulePanelCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (RegenerateCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        public ICommand GenerateCommand { get; }

        public RestitutionBlocViewModel(RestitutionBloc model,
            Func<RestitutionBlocViewModel, Task> generateAction,
            Func<RestitutionBlocViewModel, int, RpSectionViewModel, Task>? generateSectionAction = null,
            Func<RestitutionBlocViewModel, Task>? reformulateAction = null)
        {
            Model = model;
            GenerateCommand = new RelayCommand(async _ => await generateAction(this), _ => !IsGenerating);

            ToggleReformulePanelCommand = new RelayCommand(
                _ => IsReformulePanelVisible = !IsReformulePanelVisible,
                _ => !IsGenerating);
            RegenerateCommand = reformulateAction != null
                ? new RelayCommand(async _ => await reformulateAction(this), _ => !IsGenerating)
                : new RelayCommand(_ => { }, _ => false);
            CancelReformulePanelCommand = new RelayCommand(_ =>
            {
                IsReformulePanelVisible = false;
                ReformuleInstruction    = "";
            });

            if (IsCouverture) ParseContenuToCouvertureFields();
            if (IsIdentification) ParseContenuToIdentificationFields();
            if (IsContexteFamilial) ParseContenuToContexteFamilialFields();
            if (IsRestitutionParents) InitRpSections(generateSectionAction);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RestitutionEditorViewModel : INotifyPropertyChanged
    {
        private readonly RestitutionService _restitutionService;
        private readonly RestitutionSuggesterService _suggesterService;
        private readonly DossierReaderService _dossierReader;
        private readonly RestitutionHtmlPreviewService? _previewService;
        private readonly string _patientName;
        private DossierRestitutionInitial _dossier;

        // Debounce des refreshs de la preview HTML quand le médecin tape.
        private System.Windows.Threading.DispatcherTimer? _previewDebounceTimer;

        // CTS pour annulation : créé à chaque lancement de GenerateAll, annulé via StopCommand.
        private CancellationTokenSource? _generationCts;

        // DossierReading lu en début de GenerateAll, partagé entre les 8 blocs pour cohérence.
        private DossierReading? _currentReading;

        public ObservableCollection<RestitutionBlocViewModel> Blocs { get; } = new();

        public string PatientName => _patientName;

        private string _statusMessage = "";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        private bool _isGeneratingAll;
        /// <summary>True pendant le préremplissage séquentiel des 8 blocs. Pilote l'affichage du bouton Stop.</summary>
        public bool IsGeneratingAll
        {
            get => _isGeneratingAll;
            set
            {
                if (_isGeneratingAll != value)
                {
                    _isGeneratingAll = value;
                    OnPropertyChanged();
                    (GenerateAllCommand as RelayCommand)?.RaiseCanExecuteChanged();
                    (StopGenerationCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private int _progressIndex;
        /// <summary>Numéro du bloc en cours (1-based) pendant le préremplissage. 0 quand inactif.</summary>
        public int ProgressIndex
        {
            get => _progressIndex;
            private set { if (_progressIndex != value) { _progressIndex = value; OnPropertyChanged(); OnPropertyChanged(nameof(ProgressLabel)); } }
        }

        public int ProgressTotal => Blocs.Count;

        public string ProgressLabel
            => IsGeneratingAll && ProgressIndex > 0
                ? $"Bloc {ProgressIndex}/{ProgressTotal}"
                : "";

        public ICommand SaveCommand            { get; }
        public ICommand GenerateAllCommand     { get; }
        public ICommand StopGenerationCommand  { get; }
        public ICommand CloseCommand           { get; }

        public event Action? RequestClose;

        /// <summary>
        /// Notifié quand l'aperçu HTML doit être rafraîchi (à la fin du debounce 500 ms après
        /// la dernière édition). Le code-behind du View s'y abonne pour rafraîchir le WebView2.
        /// </summary>
        public event Action? PreviewRefreshRequested;

        public RestitutionEditorViewModel(
            DossierRestitutionInitial dossier,
            string patientName,
            RestitutionService restitutionService,
            RestitutionSuggesterService suggesterService,
            DossierReaderService dossierReader,
            RestitutionHtmlPreviewService? previewService = null)
        {
            _dossier            = dossier;
            _patientName        = patientName;
            _restitutionService = restitutionService;
            _suggesterService   = suggesterService;
            _dossierReader      = dossierReader;
            _previewService     = previewService;

            foreach (var bloc in _dossier.Blocs)
            {
                var vm = new RestitutionBlocViewModel(bloc, GenerateBlocAsync, GenerateSectionBlocAsync, ReformulateBlocAsync);
                // Quand le médecin tape dans un bloc, on déclenche un refresh de l'aperçu (debounce).
                vm.PropertyChanged += OnBlocPropertyChanged;
                Blocs.Add(vm);
            }

            SaveCommand           = new RelayCommand(async _ => await SaveAsync());
            GenerateAllCommand    = new RelayCommand(async _ => await GenerateAllAsync(), _ => !IsGeneratingAll);
            StopGenerationCommand = new RelayCommand(_ => StopGeneration(),               _ =>  IsGeneratingAll);
            CloseCommand          = new RelayCommand(_ => RequestClose?.Invoke());
        }

        /// <summary>
        /// Construit le HTML d'aperçu complet du dossier en l'état actuel. Appelée par le
        /// code-behind du View pour alimenter le WebView2.
        /// </summary>
        public string BuildPreviewHtml()
            => _previewService?.BuildPreviewHtml(_dossier, _patientName) ?? "<html><body><p>Aperçu indisponible.</p></body></html>";

        private void OnBlocPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(RestitutionBlocViewModel.Contenu)) return;

            // Debounce 500ms : on évite de re-rendre à chaque touche tapée par le médecin.
            if (_previewDebounceTimer == null)
            {
                _previewDebounceTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(500)
                };
                _previewDebounceTimer.Tick += (s, _) =>
                {
                    _previewDebounceTimer.Stop();
                    PreviewRefreshRequested?.Invoke();
                };
            }
            _previewDebounceTimer.Stop();
            _previewDebounceTimer.Start();
        }

        // ── Reformulation d'un bloc texte libre avec instruction ─────────────────

        private async Task ReformulateBlocAsync(RestitutionBlocViewModel blocVm)
        {
            blocVm.IsGenerating = true;
            try
            {
                _currentReading ??= await _dossierReader.ReadAsync(_patientName);
                var ct          = _generationCts?.Token ?? CancellationToken.None;
                var instruction = blocVm.ReformuleInstruction.Trim();
                StatusMessage   = $"✍ Reformulation — {blocVm.Title}...";

                var newContent = await _suggesterService.ReformuleBlocWithInstructionAsync(
                    blocVm.Model, blocVm.Contenu, instruction, _currentReading!, ct);

                if (!string.IsNullOrWhiteSpace(newContent) && !ct.IsCancellationRequested)
                {
                    blocVm.Contenu                  = newContent;
                    blocVm.IsReformulePanelVisible  = false;
                    blocVm.ReformuleInstruction     = "";
                    await SaveAsync();
                    StatusMessage = "✓ Bloc reformulé.";
                }
            }
            catch (OperationCanceledException) { StatusMessage = "⏸ Reformulation annulée."; }
            catch (Exception ex)               { StatusMessage = $"❌ Erreur reformulation : {ex.Message}"; }
            finally { blocVm.IsGenerating = false; }
        }

        // ── Génération d'une seule section du bloc restitution parents ──────────

        private async Task GenerateSectionBlocAsync(RestitutionBlocViewModel blocVm, int sectionIndex, RpSectionViewModel section)
        {
            section.IsGenerating = true;
            blocVm.IsGenerating  = true;
            try
            {
                _currentReading ??= await _dossierReader.ReadAsync(_patientName);
                var ct          = _generationCts?.Token ?? CancellationToken.None;
                var instruction = section.UserInstruction.Trim();
                StatusMessage   = $"✍ Reformulation — {RestitutionBlocViewModel.RpDisplayTitles[sectionIndex]}...";

                string newContent;
                if (string.IsNullOrWhiteSpace(instruction))
                {
                    newContent = await _suggesterService.SuggestRestitution1PageSectionAsync(sectionIndex, _currentReading!, ct);
                }
                else
                {
                    newContent = await _suggesterService.SuggestRestitution1PageSectionWithInstructionAsync(
                        sectionIndex, section.Content, instruction, _currentReading!, ct);
                }

                if (!string.IsNullOrWhiteSpace(newContent) && !ct.IsCancellationRequested)
                {
                    section.Content = newContent;
                    section.IsInstructionPanelVisible = false;
                    section.UserInstruction = "";
                    await SaveAsync();
                    StatusMessage = "✓ Section reformulée.";
                }
            }
            catch (OperationCanceledException) { StatusMessage = "⏸ Reformulation annulée."; }
            catch (Exception ex)               { StatusMessage = $"❌ Erreur reformulation : {ex.Message}"; }
            finally
            {
                section.IsGenerating = false;
                blocVm.IsGenerating  = false;
            }
        }

        // ── Génération d'un seul bloc (déclenchée par le bouton Suggérer du bloc) ──

        private async Task GenerateBlocAsync(RestitutionBlocViewModel blocVm)
        {
            blocVm.IsGenerating = true;
            try
            {
                _currentReading ??= await _dossierReader.ReadAsync(_patientName);
                var ct = _generationCts?.Token ?? CancellationToken.None;

                // Certains blocs sont composites : ils sont produits en plusieurs appels LLM
                // séquentiels (un par sous-section) pour fiabiliser le rendu et respecter les
                // limites de tokens. On les achemine vers la méthode progressive idoine.
                switch (blocVm.Model.Key)
                {
                    case "couverture":
                    {
                        // Couverture déterministe (identité, scolarité, dates) + petit appel LLM
                        // en fallback uniquement pour le champ « Motif de consultation » quand
                        // les notes ne sont pas structurées par sections labellisées.
                        StatusMessage = "✍ Couverture — extraction des informations...";
                        var content = await _suggesterService.BuildCouvertureFromDataAsync(_currentReading!, ct);
                        if (!string.IsNullOrWhiteSpace(content))
                        {
                            blocVm.Contenu = content;
                            await SaveAsync();
                            StatusMessage = "✓ Couverture renseignée depuis le dossier.";
                        }
                        else
                            StatusMessage = "⚠ Couverture : données insuffisantes — remplissez manuellement.";
                        break;
                    }

                    case "restitution_1page":
                        await RunProgressiveAsync(blocVm, "Restitution parents", 6,
                            (cb, c) => _suggesterService.SuggestRestitution1PageProgressiveAsync(_currentReading!, cb, c), ct);
                        break;

                    case "patient_contexte_familial":
                        await RunProgressiveAsync(blocVm, "Contexte familial", 6,
                            (cb, c) => _suggesterService.SuggestContexteFamilialProgressiveAsync(_currentReading!, cb, c), ct);
                        break;

                    case "patient_antecedents":
                        await RunProgressiveAsync(blocVm, "Antécédents", 6,
                            (cb, c) => _suggesterService.SuggestAntecedentsProgressiveAsync(_currentReading!, cb, c), ct);
                        break;

                    case "patient_situation_actuelle":
                        await RunProgressiveAsync(blocVm, "Situation actuelle", 5,
                            (cb, c) => _suggesterService.SuggestSituationActuelleProgressiveAsync(_currentReading!, cb, c), ct);
                        break;

                    case "carto_s1": case "carto_s2": case "carto_s3": case "carto_s4":
                    case "carto_s5": case "carto_s6": case "carto_s7": case "carto_s8":
                    {
                        // V0.9 : 1 bloc = 1 sphère, appel LLM indépendant.
                        var sphereNum = int.Parse(blocVm.Model.Key.Substring("carto_s".Length));
                        await RunProgressiveAsync(blocVm, blocVm.Title, 1,
                            (cb, c) => _suggesterService.SuggestCartoSphereAsync(sphereNum, _currentReading!, cb, c), ct);
                        break;
                    }

                    case "env_edu_f1": case "env_edu_f2": case "env_edu_f3":
                    case "env_edu_f4": case "env_edu_f5":
                    {
                        // V0.10 : 1 bloc = 1 feuille environnement, appel LLM indépendant.
                        var feuilleIdx = int.Parse(blocVm.Model.Key.Substring("env_edu_f".Length));
                        await RunProgressiveAsync(blocVm, blocVm.Title, 1,
                            (cb, c) => _suggesterService.SuggestEnvEduFeuilleAsync(feuilleIdx, _currentReading!, cb, c), ct);
                        break;
                    }

                    case "env_edu_global":
                        await RunProgressiveAsync(blocVm, blocVm.Title, 1,
                            (cb, c) => _suggesterService.SuggestEnvEduGlobalAsync(_currentReading!, cb, c), ct);
                        break;

                    case "synthese_diag_s1":
                        await RunProgressiveAsync(blocVm, blocVm.Title, 1,
                            (cb, c) => _suggesterService.SuggestSyntheseDiagS1Async(_currentReading!, cb, c), ct);
                        break;

                    case "synthese_diag_s2":
                        await RunProgressiveAsync(blocVm, blocVm.Title, 1,
                            (cb, c) => _suggesterService.SuggestSyntheseDiagS2Async(_currentReading!, cb, c), ct);
                        break;

                    case "synthese_diag_s3":
                        await RunProgressiveAsync(blocVm, blocVm.Title, 1,
                            (cb, c) => _suggesterService.SuggestSyntheseDiagS3Async(_currentReading!, cb, c), ct);
                        break;

                    case "synthese_diag_s4":
                        await RunProgressiveAsync(blocVm, blocVm.Title, 1,
                            (cb, c) => _suggesterService.SuggestSyntheseDiagS4Async(_currentReading!, cb, c), ct);
                        break;

                    case "synthese_diag_s5":
                        await RunProgressiveAsync(blocVm, blocVm.Title, 1,
                            (cb, c) => _suggesterService.SuggestSyntheseDiagS5Async(_currentReading!, cb, c), ct);
                        break;

                    case "pt_s1":
                        await RunProgressiveAsync(blocVm, blocVm.Title, 1,
                            (cb, c) => _suggesterService.SuggestPtS1Async(_currentReading!, cb, c), ct);
                        break;

                    case "pt_s2":
                        await RunProgressiveAsync(blocVm, blocVm.Title, 1,
                            (cb, c) => _suggesterService.SuggestPtS2Async(_currentReading!, cb, c), ct);
                        break;

                    case "pt_s3":
                        await RunProgressiveAsync(blocVm, blocVm.Title, 1,
                            (cb, c) => _suggesterService.SuggestPtS3Async(_currentReading!, cb, c), ct);
                        break;

                    case "pt_s4":
                        await RunProgressiveAsync(blocVm, blocVm.Title, 1,
                            (cb, c) => _suggesterService.SuggestPtS4Async(_currentReading!, cb, c), ct);
                        break;

                    case "pt_s5":
                        await RunProgressiveAsync(blocVm, blocVm.Title, 1,
                            (cb, c) => _suggesterService.SuggestPtS5Async(_currentReading!, cb, c), ct);
                        break;

                    case "conclusion":
                        await RunProgressiveAsync(blocVm, blocVm.Title, 1,
                            (cb, c) => _suggesterService.SuggestConclusionAsync(_currentReading!, cb, c), ct);
                        break;

                    default:
                    {
                        var result = await _suggesterService.PrefillBlocAsync(blocVm.Model, _currentReading, ct);
                        if (!result.Suggestion.StartsWith("(Erreur"))
                        {
                            blocVm.Contenu = result.Suggestion;
                            await SaveAsync();
                        }
                        else
                        {
                            StatusMessage = $"Erreur gén. {blocVm.Title} : {result.Suggestion}";
                        }
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessage = $"⏸ Génération du bloc « {blocVm.Title} » annulée.";
            }
            finally
            {
                blocVm.IsGenerating = false;
            }
        }

        /// <summary>
        /// Exécute une génération progressive (multi-LLM séquentielles) sur un bloc,
        /// en remettant à zéro le contenu puis en rafraîchissant l'UI à chaque section
        /// produite. Met aussi à jour le statut "section N/total".
        /// </summary>
        private async Task RunProgressiveAsync(
            RestitutionBlocViewModel blocVm,
            string label,
            int totalSections,
            Func<Action<string>, CancellationToken, Task> runner,
            CancellationToken ct)
        {
            blocVm.Contenu = "";
            int done = 0;
            StatusMessage = $"✍ {label} — section 1/{totalSections}...";

            await runner(accumulated =>
            {
                done++;
                blocVm.Contenu = accumulated;
                StatusMessage = done < totalSections
                    ? $"✍ {label} — section {done + 1}/{totalSections}..."
                    : $"✓ {label} complète.";
            }, ct);

            if (!string.IsNullOrWhiteSpace(blocVm.Contenu))
                await SaveAsync();
        }

        // ── Génération séquentielle de tous les blocs ───────────────────────

        private async Task GenerateAllAsync()
        {
            if (IsGeneratingAll) return;

            _generationCts?.Dispose();
            _generationCts = new CancellationTokenSource();
            var ct = _generationCts.Token;

            IsGeneratingAll = true;
            ProgressIndex   = 0;

            try
            {
                StatusMessage = "📖 Lecture du dossier patient...";
                _currentReading = await _dossierReader.ReadAsync(_patientName);

                int i = 0;
                foreach (var blocVm in Blocs)
                {
                    i++;
                    if (ct.IsCancellationRequested) break;

                    // Ne pas écraser un bloc déjà rempli (le médecin l'a peut-être édité à la main).
                    if (!string.IsNullOrWhiteSpace(blocVm.Contenu)) continue;

                    ProgressIndex = i;
                    StatusMessage = $"✍ Bloc {i}/{Blocs.Count} — {blocVm.Title}...";
                    blocVm.IsGenerating = true;

                    try
                    {
                        // Blocs composites : on délègue à la méthode progressive qui en interne
                        // fait N appels LLM séquentiels. Garantit que les sous-sections sont
                        // toutes présentes et fiables (chacune dans sa propre fenêtre de tokens).
                        switch (blocVm.Model.Key)
                        {
                            case "couverture":
                            {
                                // Voir GenerateBlocAsync : déterministe + LLM-fallback motif uniquement.
                                var content = await _suggesterService.BuildCouvertureFromDataAsync(_currentReading!, ct);
                                if (!string.IsNullOrWhiteSpace(content))
                                {
                                    blocVm.Contenu = content;
                                    await SaveAsync();
                                }
                                else
                                    StatusMessage = "⚠ Couverture : données insuffisantes — remplissez manuellement.";
                                break;
                            }

                            case "restitution_1page":
                                await RunProgressiveAsync(blocVm, "Restitution parents", 6,
                                    (cb, c) => _suggesterService.SuggestRestitution1PageProgressiveAsync(_currentReading, cb, c), ct);
                                break;

                            case "patient_contexte_familial":
                                await RunProgressiveAsync(blocVm, "Contexte familial", 6,
                                    (cb, c) => _suggesterService.SuggestContexteFamilialProgressiveAsync(_currentReading, cb, c), ct);
                                break;

                            case "patient_antecedents":
                                await RunProgressiveAsync(blocVm, "Antécédents", 6,
                                    (cb, c) => _suggesterService.SuggestAntecedentsProgressiveAsync(_currentReading, cb, c), ct);
                                break;

                            case "patient_situation_actuelle":
                                await RunProgressiveAsync(blocVm, "Situation actuelle", 5,
                                    (cb, c) => _suggesterService.SuggestSituationActuelleProgressiveAsync(_currentReading, cb, c), ct);
                                break;

                            case "carto_s1": case "carto_s2": case "carto_s3": case "carto_s4":
                            case "carto_s5": case "carto_s6": case "carto_s7": case "carto_s8":
                            {
                                var sphereNum = int.Parse(blocVm.Model.Key.Substring("carto_s".Length));
                                await RunProgressiveAsync(blocVm, blocVm.Title, 1,
                                    (cb, c) => _suggesterService.SuggestCartoSphereAsync(sphereNum, _currentReading!, cb, c), ct);
                                break;
                            }

                            case "env_edu_f1": case "env_edu_f2": case "env_edu_f3":
                            case "env_edu_f4": case "env_edu_f5":
                            {
                                var feuilleIdx = int.Parse(blocVm.Model.Key.Substring("env_edu_f".Length));
                                await RunProgressiveAsync(blocVm, blocVm.Title, 1,
                                    (cb, c) => _suggesterService.SuggestEnvEduFeuilleAsync(feuilleIdx, _currentReading!, cb, c), ct);
                                break;
                            }

                            case "env_edu_global":
                                await RunProgressiveAsync(blocVm, blocVm.Title, 1,
                                    (cb, c) => _suggesterService.SuggestEnvEduGlobalAsync(_currentReading!, cb, c), ct);
                                break;

                            case "synthese_diag_s1":
                                await RunProgressiveAsync(blocVm, blocVm.Title, 1,
                                    (cb, c) => _suggesterService.SuggestSyntheseDiagS1Async(_currentReading!, cb, c), ct);
                                break;

                            case "synthese_diag_s2":
                                await RunProgressiveAsync(blocVm, blocVm.Title, 1,
                                    (cb, c) => _suggesterService.SuggestSyntheseDiagS2Async(_currentReading!, cb, c), ct);
                                break;

                            case "synthese_diag_s3":
                                await RunProgressiveAsync(blocVm, blocVm.Title, 1,
                                    (cb, c) => _suggesterService.SuggestSyntheseDiagS3Async(_currentReading!, cb, c), ct);
                                break;

                            case "synthese_diag_s4":
                                await RunProgressiveAsync(blocVm, blocVm.Title, 1,
                                    (cb, c) => _suggesterService.SuggestSyntheseDiagS4Async(_currentReading!, cb, c), ct);
                                break;

                            case "synthese_diag_s5":
                                await RunProgressiveAsync(blocVm, blocVm.Title, 1,
                                    (cb, c) => _suggesterService.SuggestSyntheseDiagS5Async(_currentReading!, cb, c), ct);
                                break;

                            case "pt_s1":
                                await RunProgressiveAsync(blocVm, blocVm.Title, 1,
                                    (cb, c) => _suggesterService.SuggestPtS1Async(_currentReading!, cb, c), ct);
                                break;

                            case "pt_s2":
                                await RunProgressiveAsync(blocVm, blocVm.Title, 1,
                                    (cb, c) => _suggesterService.SuggestPtS2Async(_currentReading!, cb, c), ct);
                                break;

                            case "pt_s3":
                                await RunProgressiveAsync(blocVm, blocVm.Title, 1,
                                    (cb, c) => _suggesterService.SuggestPtS3Async(_currentReading!, cb, c), ct);
                                break;

                            case "pt_s4":
                                await RunProgressiveAsync(blocVm, blocVm.Title, 1,
                                    (cb, c) => _suggesterService.SuggestPtS4Async(_currentReading!, cb, c), ct);
                                break;

                            case "pt_s5":
                                await RunProgressiveAsync(blocVm, blocVm.Title, 1,
                                    (cb, c) => _suggesterService.SuggestPtS5Async(_currentReading!, cb, c), ct);
                                break;

                            case "conclusion":
                                await RunProgressiveAsync(blocVm, blocVm.Title, 1,
                                    (cb, c) => _suggesterService.SuggestConclusionAsync(_currentReading!, cb, c), ct);
                                break;

                            default:
                            {
                                var result = await _suggesterService.PrefillBlocAsync(blocVm.Model, _currentReading, ct);
                                if (ct.IsCancellationRequested) break;

                                if (!result.Suggestion.StartsWith("(Erreur"))
                                {
                                    blocVm.Contenu = result.Suggestion;
                                    await SaveAsync();  // auto-save après chaque bloc → reprise possible
                                }
                                else
                                {
                                    StatusMessage = $"⚠ Bloc {i} échoué : {result.Suggestion}. Passage au suivant.";
                                }
                                break;
                            }
                        }
                    }
                    finally
                    {
                        blocVm.IsGenerating = false;
                    }
                }

                StatusMessage = ct.IsCancellationRequested
                    ? $"⏸ Arrêté au bloc {ProgressIndex}/{Blocs.Count}. Les blocs déjà rédigés sont sauvegardés."
                    : "✓ Préremplissage terminé. À vous de relire et ajuster.";
            }
            catch (OperationCanceledException)
            {
                StatusMessage = $"⏸ Génération annulée au bloc {ProgressIndex}/{Blocs.Count}.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Erreur : {ex.Message}";
            }
            finally
            {
                IsGeneratingAll = false;
                ProgressIndex   = 0;
            }
        }

        private void StopGeneration()
        {
            try { _generationCts?.Cancel(); }
            catch { /* meilleur effort */ }
            StatusMessage = "⏸ Arrêt demandé...";
        }

        // ── Sauvegarde brouillon ────────────────────────────────────────────

        private async Task SaveAsync()
        {
            try
            {
                await Task.Run(() => _restitutionService.SaveBrouillon(_dossier));
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur sauvegarde : {ex.Message}";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
