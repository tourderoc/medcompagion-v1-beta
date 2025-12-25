using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Dialogs;
using MedCompanion.Models;
using MedCompanion.Services;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// ViewModel pour la gestion des courriers médicaux
    /// Approche événementielle similaire à AttestationViewModel
    /// </summary>
    public class CourriersViewModel : ViewModelBase
    {
        #region Services

        private readonly LetterService _letterService;
        private readonly PathService _pathService;
        private readonly PatientIndexService _patientIndex;
        private readonly MCCLibraryService _mccLibrary;
        private readonly LetterRatingService _letterRatingService;
        private readonly LetterReAdaptationService _reAdaptationService;
        private SynthesisWeightTracker? _synthesisWeightTracker;

        #endregion

        #region Événements

        // Événements pour communiquer avec MainWindow/UserControl
        public event EventHandler<string>? StatusMessageChanged;
        public event EventHandler<string>? LetterContentLoaded;
        public event EventHandler<(string title, string message)>? ErrorOccurred;
        public event EventHandler<(string title, string message)>? InfoMessageRequested;
        public event EventHandler<(string title, string message, Action onConfirm)>? ConfirmationRequested;
        public event EventHandler<string>? FilePrintRequested;
        public event EventHandler? CreateLetterWithAIRequested;
        public event EventHandler<(string mccId, string mccName, string letterPath, Action<int> onRatingReceived)>? RatingDialogRequested;
        public event EventHandler<(List<MissingFieldInfo> missingFields, Action<Dictionary<string, string>?> onInfoCollected)>? MissingInfoDialogRequested;
        public event EventHandler<(string filePath, string patientName, Action onContentSaved)>? DetailedViewRequested;

        // NOUVEAU : Événement déclenché après sauvegarde d'un courrier (pour rafraîchir le badge de synthèse)
        public event EventHandler? LetterSaved;

        // NOUVEAU : Événement pour naviguer vers Templates avec le contenu d'un courrier à transformer en MCC
        public event EventHandler<string>? NavigateToTemplatesWithLetter;

        #endregion

        #region Propriétés

        private PatientIndexEntry? _currentPatient;
        public PatientIndexEntry? CurrentPatient
        {
            get => _currentPatient;
            set
            {
                if (SetProperty(ref _currentPatient, value))
                {
                    OnPatientChanged();
                }
            }
        }

        // Collection des courriers du patient
        private ObservableCollection<LetterListItem> _letters;
        public ObservableCollection<LetterListItem> Letters
        {
            get => _letters;
            set => SetProperty(ref _letters, value);
        }

        // Courrier sélectionné dans la liste
        private LetterListItem? _selectedLetter;
        public LetterListItem? SelectedLetter
        {
            get => _selectedLetter;
            set
            {
                if (SetProperty(ref _selectedLetter, value))
                {
                    OnLetterSelected();
                    OnPropertyChanged(nameof(ShowReadOnlyButtons));
                    OnPropertyChanged(nameof(ShowRateButton));
                }
            }
        }

        // Contenu markdown du courrier en cours d'édition
        private string _letterMarkdown;
        public string LetterMarkdown
        {
            get => _letterMarkdown;
            set
            {
                if (SetProperty(ref _letterMarkdown, value))
                {
                    OnPropertyChanged(nameof(CanShowSaveButton));
                }
            }
        }

        /// <summary>
        /// Met à jour le markdown ET déclenche la synchronisation vers le RichTextBox
        /// Utilisé uniquement lors du chargement depuis le ViewModel (pas lors de l'édition manuelle)
        /// </summary>
        public void SetLetterMarkdownWithNotification(string markdown)
        {
            LetterMarkdown = markdown;
            LetterContentLoaded?.Invoke(this, markdown);
        }

        // Templates MCC disponibles
        private ObservableCollection<TemplateItem> _mccTemplates;
        public ObservableCollection<TemplateItem> MCCTemplates
        {
            get => _mccTemplates;
            set => SetProperty(ref _mccTemplates, value);
        }

        // Template sélectionné
        private TemplateItem? _selectedTemplate;
        public TemplateItem? SelectedTemplate
        {
            get => _selectedTemplate;
            set
            {
                if (SetProperty(ref _selectedTemplate, value))
                {
                    OnTemplateSelected();
                }
            }
        }

        // États UI
        private bool _isReadOnly = true;
        public bool IsReadOnly
        {
            get => _isReadOnly;
            set
            {
                if (SetProperty(ref _isReadOnly, value))
                {
                    UpdateCommandStates();
                    OnPropertyChanged(nameof(ShowReadOnlyButtons));
                    OnPropertyChanged(nameof(ShowEditButtons));
                    OnPropertyChanged(nameof(CanShowSaveButton));
                }
            }
        }

        private bool _isGenerating;
        public bool IsGenerating
        {
            get => _isGenerating;
            set
            {
                if (SetProperty(ref _isGenerating, value))
                {
                    UpdateCommandStates();
                }
            }
        }

        private bool _autoAdaptWithAI = true; // Coché par défaut
        public bool AutoAdaptWithAI
        {
            get => _autoAdaptWithAI;
            set => SetProperty(ref _autoAdaptWithAI, value);
        }

        // Chemin du fichier en cours d'édition
        private string? _currentEditingFilePath;
        public string? CurrentEditingFilePath
        {
            get => _currentEditingFilePath;
            set => SetProperty(ref _currentEditingFilePath, value);
        }

        // Métadonnées MCC du courrier généré (pour notation)
        private string? _lastGeneratedLetterMCCId;
        public string? LastGeneratedLetterMCCId
        {
            get => _lastGeneratedLetterMCCId;
            set => SetProperty(ref _lastGeneratedLetterMCCId, value);
        }

        private string? _lastGeneratedLetterMCCName;
        public string? LastGeneratedLetterMCCName
        {
            get => _lastGeneratedLetterMCCName;
            set => SetProperty(ref _lastGeneratedLetterMCCName, value);
        }

        // Message de statut
        private string _statusMessage = "";
        public new string StatusMessage
        {
            get => _statusMessage;
            set
            {
                if (SetProperty(ref _statusMessage, value))
                {
                    StatusMessageChanged?.Invoke(this, value);
                }
            }
        }

        // Progression de la génération (0-100)
        private double _generationProgress;
        public double GenerationProgress
        {
            get => _generationProgress;
            set => SetProperty(ref _generationProgress, value);
        }

        // Texte de statut de la génération (affiché sur le bouton pendant la génération)
        private string _generationStatusText = "💾 Sauvegarder";
        public string GenerationStatusText
        {
            get => _generationStatusText;
            set => SetProperty(ref _generationStatusText, value);
        }

        // ===== Propriétés pour la visibilité des boutons (binding XAML) =====

        /// <summary>
        /// Afficher boutons de lecture (Noter, Modifier, Supprimer, Imprimer, Voir)
        /// </summary>
        public bool ShowReadOnlyButtons => SelectedLetter != null && IsReadOnly && !IsGenerating;

        /// <summary>
        /// Afficher boutons d'édition (Sauvegarder, Annuler)
        /// </summary>
        public bool ShowEditButtons => !IsReadOnly;

        /// <summary>
        /// Activer le bouton Sauvegarder (désactivé pendant la génération)
        /// </summary>
        public bool CanShowSaveButton => !IsReadOnly && !string.IsNullOrWhiteSpace(LetterMarkdown) && !IsGenerating;

        /// <summary>
        /// Afficher bouton Noter (pour tous les courriers sauvegardés)
        /// </summary>
        public bool ShowRateButton => SelectedLetter != null && IsReadOnly;

        /// <summary>
        /// Permet la sélection d'un template (désactivé pendant la génération)
        /// </summary>
        public bool CanSelectTemplate => !IsGenerating;

        #endregion

        #region Commandes

        // Commandes principales
        public ICommand CreateWithAICommand { get; }
        public ICommand SaveLetterCommand { get; }
        public ICommand ModifyLetterCommand { get; }
        public ICommand DeleteLetterCommand { get; }
        public ICommand CancelEditCommand { get; }
        public ICommand PrintLetterCommand { get; }
        public ICommand ViewLetterCommand { get; }
        public ICommand RateLetterCommand { get; }
        public ICommand ShowMCCMetadataCommand { get; }
        public ICommand OpenDetailedViewCommand { get; }

        #endregion

        #region Constructeur

        public CourriersViewModel(
            LetterService letterService,
            PathService pathService,
            PatientIndexService patientIndex,
            MCCLibraryService mccLibrary,
            LetterRatingService letterRatingService,
            LetterReAdaptationService reAdaptationService)
        {
            // Services
            _letterService = letterService ?? throw new ArgumentNullException(nameof(letterService));
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));
            _patientIndex = patientIndex ?? throw new ArgumentNullException(nameof(patientIndex));
            _mccLibrary = mccLibrary ?? throw new ArgumentNullException(nameof(mccLibrary));
            _letterRatingService = letterRatingService ?? throw new ArgumentNullException(nameof(letterRatingService));
            _reAdaptationService = reAdaptationService ?? throw new ArgumentNullException(nameof(reAdaptationService));

            // Collections
            _letters = new ObservableCollection<LetterListItem>();
            _mccTemplates = new ObservableCollection<TemplateItem>();
            _letterMarkdown = string.Empty;
            _statusMessage = "Sélectionnez un patient pour voir ses courriers";

            // Commandes
            CreateWithAICommand = new RelayCommand(
                execute: _ => OnCreateWithAI(),
                canExecute: _ => CanCreateWithAI()
            );

            SaveLetterCommand = new RelayCommand(
                execute: async _ => await SaveLetterAsync(),
                canExecute: _ => CanSaveLetter()
            );

            ModifyLetterCommand = new RelayCommand(
                execute: _ => ModifyLetter(),
                canExecute: _ => CanModifyLetter()
            );

            DeleteLetterCommand = new RelayCommand(
                execute: async _ => await DeleteLetterAsync(),
                canExecute: _ => CanDeleteLetter()
            );

            CancelEditCommand = new RelayCommand(
                execute: _ => CancelEdit(),
                canExecute: _ => CanCancelEdit()
            );

            PrintLetterCommand = new RelayCommand(
                execute: _ => PrintLetter(),
                canExecute: _ => CanPrintLetter()
            );

            ViewLetterCommand = new RelayCommand(
                execute: _ => ViewLetter(),
                canExecute: _ => CanViewLetter()
            );

            RateLetterCommand = new RelayCommand(
                execute: async _ => await RateLetterAsync(),
                canExecute: _ => CanRateLetter()
            );

            ShowMCCMetadataCommand = new RelayCommand(
                execute: _ => ShowMCCMetadata(),
                canExecute: _ => CanShowMCCMetadata()
            );

            OpenDetailedViewCommand = new RelayCommand(
                execute: _ => OpenDetailedView(),
                canExecute: _ => CanOpenDetailedView()
            );

            // S'abonner aux mises à jour de la bibliothèque MCC
            _mccLibrary.LibraryUpdated += (s, e) =>
            {
                // Recharger les templates sur le thread UI
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    LoadMCCTemplates();
                });
            };
        }

        #endregion

        #region Méthodes publiques (API du ViewModel)

        /// <summary>
        /// Charge les templates MCC disponibles
        /// </summary>
        public void LoadMCCTemplates()
        {
            MCCTemplates.Clear();

            // 1. Ajouter l'option par défaut
            MCCTemplates.Add(new TemplateItem
            {
                Name = "-- Sélectionner un modèle --",
                MarkdownContent = "",
                IsMCC = false
            });

            if (_mccLibrary == null)
            {
                StatusMessage = "⚠️ Bibliothèque MCC non disponible";
                return;
            }

            try
            {
                // 2. Charger tous les MCC marqués pour la liste Courriers
                var allMCCs = _mccLibrary.GetAllMCCs();
                var courriersListMCCs = allMCCs.Where(mcc => mcc.IsInCourriersList).ToList();

                foreach (var mcc in courriersListMCCs)
                {
                    var templateItem = new TemplateItem
                    {
                        Name = $"[MCC] {mcc.Name}",
                        MarkdownContent = mcc.TemplateMarkdown,
                        IsMCC = true,
                        MCCId = mcc.Id,
                        MCCName = mcc.Name,
                        DocType = mcc.Semantic?.DocType,
                        Audience = mcc.Semantic?.Audience,
                        AgeGroup = mcc.Semantic?.AgeGroup,
                        Tone = mcc.Semantic?.Tone,
                        Rating = mcc.AverageRating,
                        UsageCount = mcc.UsageCount,
                        IsValidated = mcc.Status == MCCStatus.Validated,
                        Keywords = mcc.Keywords?.ToArray()
                    };

                    MCCTemplates.Add(templateItem);
                }

                StatusMessage = $"{MCCTemplates.Count} template(s) MCC chargé(s)";
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Erreur chargement templates: {ex.Message}";
                ErrorOccurred?.Invoke(this, ("Erreur", $"Impossible de charger les templates: {ex.Message}"));
            }
            finally
            {
                // Sélectionner par défaut "Sélectionner un modèle"
                SelectedTemplate = MCCTemplates.FirstOrDefault();
            }
        }

        /// <summary>
        /// Définit un brouillon de courrier (appelé depuis le Chat)
        /// </summary>
        public void SetDraft(string markdown, string? mccId = null, string? mccName = null)
        {
            // Désélectionner tout courrier existant
            SelectedLetter = null;
            CurrentEditingFilePath = null;

            // Stocker les métadonnées MCC
            LastGeneratedLetterMCCId = mccId;
            LastGeneratedLetterMCCName = mccName;

            // Passer en mode édition
            IsReadOnly = false;
            SetLetterMarkdownWithNotification(markdown);

            StatusMessage = "✅ Brouillon généré - Vous pouvez le modifier puis sauvegarder";
        }

        /// <summary>
        /// Réinitialise complètement le ViewModel
        /// </summary>
        public void Reset()
        {
            CurrentPatient = null;
            Letters.Clear();
            LetterMarkdown = string.Empty;
            SelectedLetter = null;
            SelectedTemplate = MCCTemplates.FirstOrDefault(); // Réinitialiser à "Sélectionner un modèle"
            CurrentEditingFilePath = null;
            LastGeneratedLetterMCCId = null;
            LastGeneratedLetterMCCName = null;
            IsReadOnly = true;
            StatusMessage = "Sélectionnez un patient pour voir ses courriers";
        }

        /// <summary>
        /// Initialise le tracker de poids pour la synthèse
        /// </summary>
        public void InitializeSynthesisWeightTracker(SynthesisWeightTracker tracker)
        {
            _synthesisWeightTracker = tracker;
        }

        /// <summary>
        /// Détermine le poids de pertinence d'un courrier selon son type
        /// </summary>
        private double DetermineLetterWeight()
        {
            // Si MCC utilisé, utiliser les métadonnées MCC
            if (!string.IsNullOrEmpty(LastGeneratedLetterMCCId) && SelectedTemplate != null)
            {
                var metadata = new Dictionary<string, object>
                {
                    ["mccId"] = LastGeneratedLetterMCCId,
                    ["mccName"] = LastGeneratedLetterMCCName ?? "",
                    ["docType"] = SelectedTemplate.DocType ?? ""
                };
                return ContentWeightRules.GetDefaultWeight("courrier_mcc", metadata) ?? 0.5;
            }

            // Sinon, utiliser le type de courrier
            var letterType = GetLetterType();
            return ContentWeightRules.GetDefaultWeight(letterType) ?? 0.5;
        }

        /// <summary>
        /// Détermine le type de courrier pour le système de poids
        /// </summary>
        private string GetLetterType()
        {
            // Si c'est un courrier généré depuis un MCC, utiliser le type MCC
            if (!string.IsNullOrEmpty(LastGeneratedLetterMCCName))
                return "courrier_mcc";

            // Sinon, type générique
            return "courrier_medical";
        }

        #endregion

        #region Méthodes privées (Logique métier)

        /// <summary>
        /// Appelé quand le patient change
        /// </summary>
        private void OnPatientChanged()
        {
            if (CurrentPatient == null)
            {
                Reset();
                return;
            }

            // Réinitialiser l'aperçu du courrier lors du changement de patient
            SelectedLetter = null;
            LetterMarkdown = string.Empty;
            SetLetterMarkdownWithNotification(string.Empty); // Vider l'aperçu dans l'UI
            CurrentEditingFilePath = null;
            LastGeneratedLetterMCCId = null;
            LastGeneratedLetterMCCName = null;
            IsReadOnly = true;

            // Recharger la liste des courriers
            RefreshLettersList();
            StatusMessage = $"Patient: {CurrentPatient.NomComplet}";
        }

        /// <summary>
        /// Recharge la liste des courriers du patient
        /// </summary>
        private void RefreshLettersList()
        {
            Letters.Clear();

            if (CurrentPatient == null || _pathService == null)
            {
                return;
            }

            try
            {
                var lettresDir = _pathService.GetCourriersDirectory(CurrentPatient.NomComplet);

                if (!System.IO.Directory.Exists(lettresDir))
                {
                    StatusMessage = "Aucun dossier de courriers trouvé";
                    return;
                }

                var letterFiles = System.IO.Directory.GetFiles(lettresDir, "*.md")
                    .Select(filePath => CreateLetterListItem(filePath))
                    .OrderByDescending(l => l.Date)
                    .ToList();

                foreach (var letter in letterFiles)
                {
                    Letters.Add(letter);
                }

                StatusMessage = $"{Letters.Count} courrier(s) chargé(s)";
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Erreur chargement courriers: {ex.Message}";
                ErrorOccurred?.Invoke(this, ("Erreur", $"Impossible de charger les courriers: {ex.Message}"));
            }
        }

        /// <summary>
        /// Crée un LetterListItem à partir d'un fichier .md
        /// </summary>
        private LetterListItem CreateLetterListItem(string mdPath)
        {
            var item = new LetterListItem
            {
                Date = System.IO.File.GetLastWriteTime(mdPath),
                Preview = System.IO.Path.GetFileNameWithoutExtension(mdPath),
                MdPath = mdPath,
                DocxPath = System.IO.Path.ChangeExtension(mdPath, ".docx"),
                Type = "Courrier" // Type par défaut
            };

            // Charger les métadonnées MCC si disponibles
            var metaPath = mdPath + ".meta.json";
            if (System.IO.File.Exists(metaPath))
            {
                try
                {
                    var metaJson = System.IO.File.ReadAllText(metaPath);
                    var metadata = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(metaJson);

                    if (metadata.TryGetProperty("mccId", out var mccIdProp))
                        item.MCCId = mccIdProp.GetString();

                    if (metadata.TryGetProperty("mccName", out var mccNameProp))
                    {
                        item.MCCName = mccNameProp.GetString();
                        item.Type = item.MCCName ?? "Courrier"; // Utiliser le nom MCC comme type
                    }

                    if (metadata.TryGetProperty("rating", out var ratingProp) && ratingProp.TryGetInt32(out var rating))
                        item.Rating = rating;
                }
                catch
                {
                    // Ignorer les erreurs de parsing metadata
                }
            }

            return item;
        }

        /// <summary>
        /// Appelé quand un courrier est sélectionné
        /// </summary>
        private void OnLetterSelected()
        {
            if (SelectedLetter == null)
            {
                // Réinitialiser quand déselection
                SetLetterMarkdownWithNotification(string.Empty); // Vider l'aperçu dans l'UI
                CurrentEditingFilePath = null;
                IsReadOnly = true;
                LastGeneratedLetterMCCId = null;
                LastGeneratedLetterMCCName = null;
                StatusMessage = "Sélectionnez un courrier pour voir son aperçu";
                return;
            }

            try
            {
                var filePath = SelectedLetter.MdPath;

                // Charger le contenu markdown
                var markdown = System.IO.File.ReadAllText(filePath);
                LetterMarkdown = markdown;
                CurrentEditingFilePath = filePath;

                // Recharger les métadonnées MCC
                var metaPath = filePath + ".meta.json";
                if (System.IO.File.Exists(metaPath))
                {
                    try
                    {
                        var metaJson = System.IO.File.ReadAllText(metaPath);
                        var metadata = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(metaJson);

                        if (metadata.TryGetProperty("mccId", out var mccIdProp))
                            LastGeneratedLetterMCCId = mccIdProp.GetString();

                        if (metadata.TryGetProperty("mccName", out var mccNameProp))
                            LastGeneratedLetterMCCName = mccNameProp.GetString();
                    }
                    catch
                    {
                        LastGeneratedLetterMCCId = null;
                        LastGeneratedLetterMCCName = null;
                    }
                }
                else
                {
                    LastGeneratedLetterMCCId = null;
                    LastGeneratedLetterMCCName = null;
                }

                // Mode lecture seule
                IsReadOnly = true;

                // Déclencher événement pour affichage dans UserControl
                LetterContentLoaded?.Invoke(this, markdown);

                StatusMessage = "📄 Courrier chargé (lecture seule)";

                UpdateCommandStates();
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Erreur lecture: {ex.Message}";
                ErrorOccurred?.Invoke(this, ("Erreur", $"Impossible de lire le courrier: {ex.Message}"));
            }
        }

        /// <summary>
        /// Gère la sélection d'un template MCC
        /// Charge le markdown et adapte avec IA si toggle activé
        /// </summary>
        private async void OnTemplateSelected()
        {
            if (SelectedTemplate == null)
                return;

            // Ignorer le template par défaut "-- Sélectionner un modèle --"
            if (string.IsNullOrEmpty(SelectedTemplate.MarkdownContent))
            {
                // Ne pas mettre à null, juste retourner pour garder l'affichage
                return;
            }

            try
            {
                // Stocker les métadonnées MCC
                if (SelectedTemplate.IsMCC)
                {
                    LastGeneratedLetterMCCId = SelectedTemplate.MCCId;
                    LastGeneratedLetterMCCName = SelectedTemplate.MCCName;
                }
                else
                {
                    LastGeneratedLetterMCCId = null;
                    LastGeneratedLetterMCCName = null;
                }

                var templateMarkdown = SelectedTemplate.MarkdownContent;

                // Passer en mode édition
                IsReadOnly = false;
                SetLetterMarkdownWithNotification(templateMarkdown);
                CurrentEditingFilePath = null;

                // Si patient sélectionné → Vérifier les infos manquantes et adapter si toggle activé
                if (CurrentPatient != null && _reAdaptationService != null)
                {
                    var busyService = BusyService.Instance;
                    var cancellationToken = busyService.Start("Adaptation du courrier par l'IA...", canCancel: true);

                    IsGenerating = true;
                    GenerationProgress = 0;
                    GenerationStatusText = "⏳ Démarrage...";

                    try
                    {
                        string adaptedMarkdown = templateMarkdown;

                        // ÉTAPE 1 : Adaptation IA (seulement si toggle activé)
                        if (AutoAdaptWithAI && _letterService != null)
                        {
                            GenerationProgress = 10;
                            GenerationStatusText = "⏳ Adaptation IA...";
                            StatusMessage = "⏳ Adaptation IA en cours...";
                            busyService.UpdateStep("Adaptation IA en cours...");
                            busyService.UpdateProgress(20);

                            var (success, result, error) = await _letterService.AdaptTemplateWithAIAsync(
                                CurrentPatient.NomComplet,
                                SelectedTemplate.Name,
                                templateMarkdown
                            );

                            if (success && !string.IsNullOrEmpty(result))
                            {
                                adaptedMarkdown = result;
                                GenerationProgress = 30;
                                busyService.UpdateProgress(40);
                            }
                            else
                            {
                                StatusMessage = $"⚠️ Adaptation IA échouée : {error}";
                                busyService.UpdateStep($"⚠️ Adaptation IA échouée : {error}");
                            }
                        }

                        cancellationToken.ThrowIfCancellationRequested();

                        // ÉTAPE 2 : Réadaptation avec détection des infos manquantes (TOUJOURS, même sans IA)
                        if (!string.IsNullOrEmpty(adaptedMarkdown))
                        {
                            GenerationProgress = 40;
                            GenerationStatusText = "⏳ Vérification...";
                            StatusMessage = "⏳ Vérification des informations...";
                            busyService.UpdateStep("Vérification des informations manquantes...");
                            busyService.UpdateProgress(60);

                            var reAdaptResult = await _reAdaptationService.ReAdaptLetterAsync(
                                adaptedMarkdown,
                                CurrentPatient.NomComplet,
                                SelectedTemplate.Name
                            );

                            if (reAdaptResult.NeedsMissingInfo)
                            {
                                GenerationProgress = 50;
                                GenerationStatusText = "❓ Infos manquantes...";
                                StatusMessage = "❓ Informations manquantes...";
                                
                                // On cache temporairement le BusyService pour laisser place au dialogue
                                busyService.Stop();

                                // Demander les infos manquantes via événement avec TaskCompletionSource
                                var tcs = new TaskCompletionSource<Dictionary<string, string>?>();

                                MissingInfoDialogRequested?.Invoke(this, (
                                    reAdaptResult.MissingFields,
                                    info => tcs.SetResult(info)
                                ));

                                // Attendre vraiment le retour du dialogue (avec timeout de 5 minutes)
                                var collectedInfo = await Task.WhenAny(
                                    tcs.Task,
                                    Task.Delay(TimeSpan.FromMinutes(5))
                                ).ContinueWith(t => tcs.Task.IsCompleted ? tcs.Task.Result : null);

                                if (collectedInfo != null)
                                {
                                    // Relancer le BusyService après le dialogue
                                    busyService.Start("Finalisation de l'adaptation...", canCancel: false);
                                    busyService.UpdateProgress(80);

                                    GenerationProgress = 80;
                                    GenerationStatusText = "⏳ Finalisation...";
                                    StatusMessage = "⏳ Ré-adaptation avec infos complètes...";

                                    var finalResult = await _reAdaptationService.CompleteReAdaptationAsync(
                                        reAdaptResult,
                                        collectedInfo
                                    );

                                    if (finalResult.Success && !string.IsNullOrEmpty(finalResult.ReAdaptedMarkdown))
                                    {
                                        SetLetterMarkdownWithNotification(finalResult.ReAdaptedMarkdown);
                                        GenerationProgress = 90;
                                        StatusMessage = "✅ Courrier complété avec toutes les informations";
                                    }
                                    else
                                    {
                                        SetLetterMarkdownWithNotification(adaptedMarkdown);
                                        StatusMessage = $"⚠️ Erreur ré-adaptation : {finalResult.Error}";
                                    }
                                }
                                else
                                {
                                    // Annulation dialogue -> garder version initiale
                                    SetLetterMarkdownWithNotification(adaptedMarkdown);
                                    StatusMessage = "⚠️ Réadaptation annulée, courrier initial conservé";
                                }
                            }
                            else
                            {
                                // Pas d'infos manquantes → utiliser le résultat
                                SetLetterMarkdownWithNotification(reAdaptResult.ReAdaptedMarkdown ?? adaptedMarkdown);
                                GenerationProgress = 90;

                                if (AutoAdaptWithAI)
                                {
                                    StatusMessage = "✅ Courrier adapté avec succès";
                                }
                                else
                                {
                                    StatusMessage = "✅ Modèle chargé - Vous pouvez le modifier puis sauvegarder";
                                }
                            }
                        }

                        busyService.UpdateProgress(100, "✅ Terminé");
                        await Task.Delay(200);
                    }
                    catch (OperationCanceledException)
                    {
                        StatusMessage = "🚫 Opération annulée";
                        SetLetterMarkdownWithNotification(templateMarkdown); // Retour au template brut
                    }
                    catch (Exception adaptEx)
                    {
                        StatusMessage = $"⚠️ Erreur adaptation IA - Modèle brut affiché : {adaptEx.Message}";
                        System.Diagnostics.Debug.WriteLine($"[CourriersViewModel] Erreur adaptation: {adaptEx}");
                    }
                    finally
                    {
                        GenerationProgress = 100;
                        GenerationStatusText = "💾 Sauvegarder";
                        IsGenerating = false;
                        busyService.Stop();
                    }
                }
                else if (CurrentPatient == null)
                {
                    StatusMessage = "⚠️ Modèle brut affiché - Sélectionnez un patient pour compléter les informations";
                }
                else
                {
                    StatusMessage = $"📝 Modèle '{SelectedTemplate.Name}' chargé - Modifiez-le puis sauvegardez";
                }

                // Réinitialiser la sélection du ComboBox à "Sélectionner un modèle"
                SelectedTemplate = MCCTemplates.FirstOrDefault();
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Erreur chargement modèle: {ex.Message}";
                ErrorOccurred?.Invoke(this, ("Erreur", $"Impossible de charger le modèle: {ex.Message}"));

                // Réinitialiser aussi en cas d'erreur
                SelectedTemplate = MCCTemplates.FirstOrDefault();
            }
        }

        #endregion

        #region Commandes - Implémentations

        private bool CanCreateWithAI()
        {
            return CurrentPatient != null && !IsGenerating;
        }

        private void OnCreateWithAI()
        {
            CreateLetterWithAIRequested?.Invoke(this, EventArgs.Empty);
        }

        private bool CanSaveLetter()
        {
            return !IsReadOnly && !string.IsNullOrWhiteSpace(LetterMarkdown) && CurrentPatient != null;
        }

        private async Task SaveLetterAsync()
        {
            if (CurrentPatient == null || _letterService == null || string.IsNullOrWhiteSpace(LetterMarkdown))
            {
                StatusMessage = "⚠️ Rien à sauvegarder";
                return;
            }

            var busyService = BusyService.Instance;
            busyService.Start("Sauvegarde du courrier...", canCancel: false);

            try
            {
                bool success;
                string message;
                // Capturer l'état "nouveau courrier" au début
                bool wasNewLetter = CurrentEditingFilePath == null;
                string? mdFilePath = CurrentEditingFilePath;

                IsGenerating = true;
                GenerationProgress = 10;
                busyService.UpdateProgress(20, "Préparation des fichiers...");

                // Cas 1: Mise à jour d'un courrier existant
                if (CurrentEditingFilePath != null)
                {
                    System.IO.File.WriteAllText(CurrentEditingFilePath, LetterMarkdown);
                    success = true;
                    message = "Courrier mis à jour";
                }
                // Cas 2: Nouveau courrier
                else
                {
                    GenerationProgress = 30;
                    busyService.UpdateProgress(40, "Enregistrement du brouillon...");
                    (success, message, mdFilePath) = _letterService.SaveDraft(
                        CurrentPatient.NomComplet,
                        LetterMarkdown
                    );

                    if (success && mdFilePath != null)
                    {
                        CurrentEditingFilePath = mdFilePath;
                    }
                }

                // Export vers .docx
                if (success && mdFilePath != null)
                {
                    GenerationProgress = 60;
                    busyService.UpdateProgress(70, "Exportation vers Word (.docx)...");
                    var (exportSuccess, exportMessage, docxPath) = _letterService.ExportToDocx(
                        CurrentPatient.NomComplet,
                        LetterMarkdown,
                        mdFilePath
                    );

                    if (exportSuccess)
                    {
                        message = "✅ Courrier sauvegardé et exporté (.docx)";
                    }
                    else
                    {
                        message = $"⚠️ Sauvegardé mais erreur export: {exportMessage}";
                    }

                    // NOUVEAU : Enregistrer le poids de pertinence (NOUVEAU COURRIER UNIQUEMENT)
                    if (_synthesisWeightTracker != null && CurrentPatient != null && wasNewLetter)
                    {
                        var weight = DetermineLetterWeight();
                        var letterType = GetLetterType();
                        _synthesisWeightTracker.RecordContentWeight(
                            CurrentPatient.NomComplet,
                            letterType,
                            mdFilePath,
                            weight,
                            $"Courrier {LastGeneratedLetterMCCName ?? letterType} (poids: {weight:F1})"
                        );

                        System.Diagnostics.Debug.WriteLine(
                            $"[CourriersViewModel] Poids enregistré: {weight:F1} pour {letterType}");
                    }

                    // Persister les métadonnées MCC
                    if (!string.IsNullOrEmpty(LastGeneratedLetterMCCId))
                    {
                        try
                        {
                            var metaPath = mdFilePath + ".meta.json";
                            var metadata = new
                            {
                                mccId = LastGeneratedLetterMCCId,
                                mccName = LastGeneratedLetterMCCName,
                                generatedDate = DateTime.Now
                            };
                            var metaJson = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                            System.IO.File.WriteAllText(metaPath, metaJson);
                        }
                        catch
                        {
                            // Ignorer les erreurs de sauvegarde metadata
                        }
                    }
                }

                StatusMessage = message;
                busyService.UpdateProgress(100, "Terminé !");
                await Task.Delay(200);

                // Réinitialiser l'interface
                IsReadOnly = true;
                LetterMarkdown = string.Empty;
                CurrentEditingFilePath = null;

                // Recharger la liste
                RefreshLettersList();

                // Désélectionner
                SelectedLetter = null;

                // NOUVEAU : Déclencher l'événement LetterSaved pour rafraîchir le badge de synthèse
                LetterSaved?.Invoke(this, EventArgs.Empty);

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ {ex.Message}";
                ErrorOccurred?.Invoke(this, ("Erreur", $"Impossible de sauvegarder le courrier: {ex.Message}"));
            }
            finally
            {
                IsGenerating = false;
                GenerationProgress = 0;
                GenerationStatusText = "💾 Sauvegarder";
                busyService.Stop();
            }
        }

        private bool CanModifyLetter()
        {
            return SelectedLetter != null && IsReadOnly && !IsGenerating;
        }

        private void ModifyLetter()
        {
            if (SelectedLetter == null)
                return;

            try
            {
                // Recharger le contenu du fichier pour s'assurer qu'on édite bien le courrier sauvegardé
                var filePath = SelectedLetter.MdPath;

                if (!System.IO.File.Exists(filePath))
                {
                    StatusMessage = "⚠️ Fichier introuvable";
                    ErrorOccurred?.Invoke(this, ("Erreur", "Le fichier du courrier n'existe plus."));
                    return;
                }

                var markdown = System.IO.File.ReadAllText(filePath);
                CurrentEditingFilePath = filePath;

                // Passer en mode édition
                IsReadOnly = false;

                // Charger le contenu dans l'éditeur
                SetLetterMarkdownWithNotification(markdown);

                StatusMessage = "✏️ Mode édition activé";
                UpdateCommandStates();
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Erreur: {ex.Message}";
                ErrorOccurred?.Invoke(this, ("Erreur", $"Impossible de charger le courrier: {ex.Message}"));
            }
        }

        private bool CanDeleteLetter()
        {
            return SelectedLetter != null && !IsGenerating;
        }

        private async Task DeleteLetterAsync()
        {
            if (SelectedLetter == null)
            {
                StatusMessage = "⚠️ Aucun courrier sélectionné";
                return;
            }

            var filePath = SelectedLetter.MdPath;

            // Demander confirmation via événement
            bool confirmed = false;
            ConfirmationRequested?.Invoke(this, (
                "Confirmer la suppression",
                $"Êtes-vous sûr de vouloir supprimer ce courrier ?\n\n{System.IO.Path.GetFileName(filePath)}",
                () => confirmed = true
            ));

            // Attendre la réponse (l'événement sera traité synchroniquement dans le UserControl)
            await Task.Delay(100); // Petit délai pour laisser le temps au dialog

            if (!confirmed)
            {
                StatusMessage = "Suppression annulée";
                return;
            }

            try
            {
                // Supprimer le fichier .md
                System.IO.File.Delete(filePath);

                // Supprimer le fichier .docx si existant
                var docxPath = System.IO.Path.ChangeExtension(filePath, ".docx");
                if (System.IO.File.Exists(docxPath))
                {
                    System.IO.File.Delete(docxPath);
                }

                // Supprimer les métadonnées si existantes
                var metaPath = filePath + ".meta.json";
                if (System.IO.File.Exists(metaPath))
                {
                    System.IO.File.Delete(metaPath);
                }

                StatusMessage = "✅ Courrier supprimé";

                // Réinitialiser l'interface
                LetterMarkdown = string.Empty;
                CurrentEditingFilePath = null;
                IsReadOnly = true;

                // Recharger la liste
                RefreshLettersList();

                // Désélectionner
                SelectedLetter = null;
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Erreur suppression: {ex.Message}";
                ErrorOccurred?.Invoke(this, ("Erreur", $"Impossible de supprimer le courrier: {ex.Message}"));
            }
        }

        private bool CanCancelEdit()
        {
            return !IsReadOnly;
        }

        private void CancelEdit()
        {
            // Réinitialiser l'interface
            IsReadOnly = true;
            IsGenerating = false; // ✅ CORRECTION : Réactiver les boutons si annulation pendant génération
            SelectedLetter = null;
            SetLetterMarkdownWithNotification(string.Empty); // ✅ CORRECTION : Vider l'aperçu dans l'UI
            CurrentEditingFilePath = null;
            LastGeneratedLetterMCCId = null;
            LastGeneratedLetterMCCName = null;

            StatusMessage = "❌ Modifications annulées";
            UpdateCommandStates();
        }

        private bool CanPrintLetter()
        {
            return SelectedLetter != null && !IsGenerating;
        }

        private void PrintLetter()
        {
            if (SelectedLetter == null)
            {
                StatusMessage = "⚠️ Aucun courrier sélectionné";
                return;
            }

            var docxPath = SelectedLetter.DocxPath;

            if (!System.IO.File.Exists(docxPath))
            {
                StatusMessage = "⚠️ Fichier .docx introuvable. Sauvegardez d'abord le courrier.";
                return;
            }

            // Déclencher événement pour impression (géré par UserControl)
            FilePrintRequested?.Invoke(this, docxPath);
            StatusMessage = "🖨️ Document envoyé à l'imprimante";
        }

        private bool CanViewLetter()
        {
            return SelectedLetter != null && !IsGenerating;
        }

        private void ViewLetter()
        {
            if (SelectedLetter == null || CurrentPatient == null)
            {
                StatusMessage = "⚠️ Aucun courrier sélectionné";
                return;
            }

            var markdownPath = SelectedLetter.FilePath;

            if (!System.IO.File.Exists(markdownPath))
            {
                ErrorOccurred?.Invoke(this, ("Erreur", "Le fichier du courrier n'existe plus."));
                StatusMessage = "⚠️ Fichier introuvable";
                return;
            }

            // Déclencher événement pour ouvrir le dialogue de vue détaillée
            DetailedViewRequested?.Invoke(this, (markdownPath, CurrentPatient.NomComplet, () =>
            {
                // Callback après sauvegarde dans le dialogue
                RefreshLettersList();
                StatusMessage = "✅ Courrier sauvegardé depuis la vue détaillée";
            }));
            StatusMessage = "👁️ Ouverture en vue détaillée...";
        }

        private bool CanRateLetter()
        {
            return SelectedLetter != null && !IsGenerating;
        }

        private async Task RateLetterAsync()
        {
            if (SelectedLetter == null)
            {
                StatusMessage = "⚠️ Aucun courrier sélectionné";
                return;
            }

            var letterPath = SelectedLetter.MdPath;
            int receivedRating = 0;
            bool isFromMCC = !string.IsNullOrEmpty(LastGeneratedLetterMCCId);

            // Déclencher événement pour ouvrir dialogue de notation (géré par UserControl)
            RatingDialogRequested?.Invoke(this, (
                LastGeneratedLetterMCCId ?? "",  // Peut être vide pour courriers sans MCC
                LastGeneratedLetterMCCName ?? "Courrier",
                letterPath,
                rating => receivedRating = rating
            ));

            // Attendre le retour du dialogue
            await Task.Delay(100);

            if (receivedRating == 0)
            {
                StatusMessage = "Notation annulée";
                return;
            }

            try
            {
                // Créer l'objet LetterRating
                var letterRating = new LetterRating
                {
                    LetterPath = letterPath,
                    Rating = receivedRating,
                    RatingDate = DateTime.Now,
                    MCCId = LastGeneratedLetterMCCId,
                    MCCName = LastGeneratedLetterMCCName,
                    PatientName = CurrentPatient?.NomComplet ?? "",
                    Comment = "" // Peut être étendu plus tard
                };

                // Sauvegarder via LetterRatingService
                var (success, error) = _letterRatingService.AddOrUpdateRating(letterRating);

                if (!success)
                {
                    ErrorOccurred?.Invoke(this, ("Erreur", $"Impossible de sauvegarder la notation: {error}"));
                    StatusMessage = $"❌ Erreur sauvegarde: {error}";
                    return;
                }

                // Mettre à jour le fichier .meta.json avec la notation
                var metaPath = letterPath + ".meta.json";
                try
                {
                    var metadata = new
                    {
                        mccId = LastGeneratedLetterMCCId,
                        mccName = LastGeneratedLetterMCCName,
                        generatedDate = DateTime.Now,
                        rating = receivedRating,
                        ratingDate = DateTime.Now
                    };
                    var metaJson = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    System.IO.File.WriteAllText(metaPath, metaJson);

                    // Mettre à jour le LetterListItem dans la collection
                    if (SelectedLetter != null)
                    {
                        SelectedLetter.Rating = receivedRating;
                    }
                }
                catch (Exception metaEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Erreur sauvegarde metadata: {metaEx.Message}");
                }

                // Afficher message contextuel selon la note ET le type de courrier
                if (receivedRating == 5 && isFromMCC)
                {
                    // Courrier MCC avec 5 étoiles
                    InfoMessageRequested?.Invoke(this, ("Excellent !", $"⭐⭐⭐⭐⭐ Le MCC '{LastGeneratedLetterMCCName}' a obtenu la note maximale !"));
                    StatusMessage = "⭐⭐⭐⭐⭐ Excellent MCC noté 5/5 !";
                }
                else if (receivedRating == 5 && !isFromMCC)
                {
                    // Courrier NON-MCC avec 5 étoiles → Proposer d'ajouter à la bibliothèque MCC
                    bool userWantsToConvert = false;
                    ConfirmationRequested?.Invoke(this, (
                        "Transformer en MCC ?",
                        $"🌟 Excellent courrier noté 5★ !\n\n" +
                        $"Voulez-vous transformer ce courrier en modèle MCC ?\n" +
                        $"Vous serez automatiquement redirigé vers l'onglet Templates.",
                        () => userWantsToConvert = true
                    ));

                    // Petit délai pour laisser le dialogue se fermer
                    await Task.Delay(100);

                    if (userWantsToConvert)
                    {
                        // Déclencher événement pour naviguer vers Templates avec le contenu du courrier
                        NavigateToTemplatesWithLetter?.Invoke(this, letterPath);
                        StatusMessage = "🔄 Redirection vers Templates...";
                    }
                    else
                    {
                        StatusMessage = "⭐⭐⭐⭐⭐ Excellent courrier noté 5/5 !";
                    }
                }
                else if (receivedRating <= 3 && isFromMCC)
                {
                    InfoMessageRequested?.Invoke(this, ("MCC à revoir", $"⚠️ Le MCC '{LastGeneratedLetterMCCName}' a obtenu {receivedRating}/5.\n\nCe modèle pourrait nécessiter une amélioration."));
                    StatusMessage = $"⚠️ MCC noté {receivedRating}/5 - À améliorer";
                }
                else
                {
                    StatusMessage = $"✅ Courrier noté {receivedRating}/5";
                }

                // Recharger la liste pour afficher la note
                RefreshLettersList();

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                StatusMessage = $"❌ Erreur notation: {ex.Message}";
                ErrorOccurred?.Invoke(this, ("Erreur", $"Impossible de noter le courrier: {ex.Message}"));
            }
        }

        private bool CanShowMCCMetadata()
        {
            return SelectedLetter != null && !string.IsNullOrEmpty(LastGeneratedLetterMCCId);
        }

        private void ShowMCCMetadata()
        {
            if (SelectedLetter == null || string.IsNullOrEmpty(LastGeneratedLetterMCCId))
            {
                StatusMessage = "⚠️ Aucun MCC associé à ce courrier";
                return;
            }

            // Récupérer les détails du MCC depuis la bibliothèque
            var mcc = _mccLibrary?.GetAllMCCs().FirstOrDefault(m => m.Id == LastGeneratedLetterMCCId);

            if (mcc == null)
            {
                InfoMessageRequested?.Invoke(this, (
                    "MCC Introuvable",
                    $"Le MCC '{LastGeneratedLetterMCCName}' (ID: {LastGeneratedLetterMCCId}) n'est plus dans la bibliothèque."
                ));
                return;
            }

            // Construire le message d'information
            var info = $"📋 **{mcc.Name}**\n\n";
            info += $"🆔 ID: {mcc.Id}\n";
            info += $"⭐ Note moyenne: {mcc.AverageRating:F1}/5\n";
            info += $"📊 Utilisations: {mcc.UsageCount}\n";
            info += $"✅ Validé: {(mcc.Status == MCCStatus.Validated ? "Oui" : "Non")}\n\n";

            if (mcc.Semantic != null)
            {
                info += $"**Métadonnées sémantiques:**\n";
                if (!string.IsNullOrEmpty(mcc.Semantic.DocType))
                    info += $"• Type: {mcc.Semantic.DocType}\n";
                if (!string.IsNullOrEmpty(mcc.Semantic.Audience))
                    info += $"• Audience: {mcc.Semantic.Audience}\n";
                if (!string.IsNullOrEmpty(mcc.Semantic.AgeGroup))
                    info += $"• Tranche d'âge: {mcc.Semantic.AgeGroup}\n";
                if (!string.IsNullOrEmpty(mcc.Semantic.Tone))
                    info += $"• Ton: {mcc.Semantic.Tone}\n";
            }

            if (mcc.Keywords != null && mcc.Keywords.Count > 0)
            {
                info += $"\n🔑 Mots-clés: {string.Join(", ", mcc.Keywords)}";
            }

            InfoMessageRequested?.Invoke(this, ("Détails MCC", info));
            StatusMessage = $"ℹ️ Détails MCC: {mcc.Name}";
        }

        private bool CanOpenDetailedView()
        {
            return SelectedLetter != null && !IsGenerating;
        }

        /// <summary>
        /// Ouvre la vue détaillée plein écran pour le courrier sélectionné
        /// </summary>
        private void OpenDetailedView()
        {
            if (SelectedLetter == null || CurrentPatient == null)
            {
                StatusMessage = "⚠️ Aucun courrier sélectionné";
                return;
            }

            var filePath = SelectedLetter.MdPath;

            if (!System.IO.File.Exists(filePath))
            {
                ErrorOccurred?.Invoke(this, ("Erreur", "Le fichier du courrier n'existe plus."));
                StatusMessage = "⚠️ Fichier introuvable";
                return;
            }

            // Déclencher événement pour ouvrir la vue détaillée (géré par UserControl)
            DetailedViewRequested?.Invoke(this, (
                filePath,
                CurrentPatient.NomComplet,
                () => OnDetailedViewContentSaved(filePath)
            ));

            StatusMessage = $"📄 Vue détaillée: {System.IO.Path.GetFileName(filePath)}";
        }

        /// <summary>
        /// Callback appelé quand le contenu est sauvegardé dans la vue détaillée
        /// </summary>
        private void OnDetailedViewContentSaved(string filePath)
        {
            // Recharger le courrier si c'est celui actuellement édité
            if (CurrentEditingFilePath == filePath && System.IO.File.Exists(filePath))
            {
                try
                {
                    var markdown = System.IO.File.ReadAllText(filePath);
                    LetterMarkdown = markdown;
                    LetterContentLoaded?.Invoke(this, markdown);
                    StatusMessage = "✅ Courrier mis à jour depuis vue détaillée";
                }
                catch (Exception ex)
                {
                    StatusMessage = $"⚠️ Erreur rechargement: {ex.Message}";
                }
            }

            // Rafraîchir la liste
            RefreshLettersList();
        }

        /// <summary>
        /// Met à jour l'état des commandes
        /// </summary>
        private void UpdateCommandStates()
        {
            (CreateWithAICommand as RelayCommand)?.RaiseCanExecuteChanged();
            (SaveLetterCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ModifyLetterCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (DeleteLetterCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (CancelEditCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (PrintLetterCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ViewLetterCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (RateLetterCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (ShowMCCMetadataCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (OpenDetailedViewCommand as RelayCommand)?.RaiseCanExecuteChanged();

            // Notifier les changements de visibilité des boutons
            UpdateButtonsVisibility();
        }

        /// <summary>
        /// Notifie les changements de visibilité des boutons (propriétés calculées)
        /// </summary>
        private void UpdateButtonsVisibility()
        {
            OnPropertyChanged(nameof(ShowReadOnlyButtons));
            OnPropertyChanged(nameof(ShowEditButtons));
            OnPropertyChanged(nameof(CanShowSaveButton));
            OnPropertyChanged(nameof(ShowRateButton));
            OnPropertyChanged(nameof(CanSelectTemplate));
        }

        #endregion
    }
}
