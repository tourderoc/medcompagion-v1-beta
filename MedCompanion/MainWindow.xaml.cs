using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MedCompanion.Commands;
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.Services.LLM;
using MedCompanion.Dialogs;

namespace MedCompanion;

public partial class MainWindow : Window
{
    private readonly PathService _pathService;
    private readonly OpenAIService _openAIService;
    private readonly StorageService _storageService;
    private readonly ContextLoader _contextLoader;
    private readonly ParsingService _parsingService;
    private readonly PatientIndexService _patientIndex;
    private readonly PatientContextService _patientContextService; // ✅ NOUVEAU
    private readonly LetterReAdaptationService _reAdaptationService; // ✅ NOUVEAU
    private readonly AnonymizationService _anonymizationService; // ✅ NOUVEAU
    private readonly ChatMemoryService _chatMemoryService; // ✅ NOUVEAU
    private readonly LetterService _letterService;
    private readonly TemplateExtractorService _templateExtractor;
    private readonly TemplateManagerService _templateManager;
    private readonly MCCLibraryService _mccLibrary;
    private readonly PromptReformulationService _promptReformulationService;
    private readonly AttestationService _attestationService;
    private readonly FormulaireAssistantService _formulaireService;
    private readonly OrdonnanceService _ordonnanceService;
    private readonly SynthesisService _synthesisService;
    private readonly SynthesisWeightTracker _synthesisWeightTracker;
    private readonly AppSettings _settings;
    private readonly LetterRatingService _letterRatingService;
    private readonly PromptConfigService _promptConfigService;
    private readonly PromptTrackerService _promptTracker;
    private readonly RegenerationService _regenerationService;
    
    // Services LLM
    private LLMServiceFactory _llmFactory;
    private LLMWarmupService _warmupService;
    private ILLMService? _currentLLMService;
    private readonly LLMGatewayService _llmGatewayService; // ✅ NOUVEAU - Gateway centralisé
    
    // ViewModels MVVM (propriété publique pour binding XAML)
    public ViewModels.PatientSearchViewModel PatientSearchViewModel { get; }
    public ViewModels.OrdonnanceViewModel OrdonnanceViewModel { get; }
    public ViewModels.NoteViewModel NoteViewModel { get; }
    public ViewModels.AttestationViewModel AttestationViewModel { get; }

    // Services exposés pour UserControls (Avis IA ordonnance, etc.)
    public OpenAIService OpenAIService => _openAIService;
    public ContextLoader ContextLoader => _contextLoader;
    public PatientIndexService PatientIndex => _patientIndex;
    // MIGRÉ vers ChatViewModel - Historique temporaire géré par ChatControl
    // public List<ChatExchange> ChatHistory => _chatHistory;
    public LetterService LetterService => _letterService;
    public StorageService StorageService => _storageService;
    public PathService PathService => _pathService;
    public AnonymizationService AnonymizationService => _anonymizationService;
    public LLMGatewayService LLMGatewayService => _llmGatewayService;
    // Note: AssistantTabControl est déjà public via x:Name dans le XAML

    private PatientIndexEntry? _selectedPatient;
    private PatientIndexEntry? _currentPatientDuplicate;
    private int _currentPatientDuplicateScore;

    // Poids de pertinence de la dernière note structurée (pour mise à jour synthèse)
    private double _lastNoteRelevanceWeight = 0.0;
    
    // Instance unique du dialogue Prompts
    private PromptsAnalysisDialog? _promptsDialog;
    
    // Toggle liste patients
    private bool _isPatientsListVisible = false;
    
    // Référence au Grid parent pour gérer les RowDefinitions dynamiquement
    private Grid? _notesGrid;
    
    // Historique de chat temporaire (mémoire RAM - 3 derniers échanges max) - MIGRÉ vers ChatViewModel
    // private List<ChatExchange> _chatHistory = new();
    // private List<ChatExchange> _savedChatExchanges = new();

    // Templates personnalisés - SUPPRIMÉ : Variables migrées vers TemplatesViewModel (05/12/2025)



    public MainWindow()
    {
        InitializeComponent();
        // Test Phase 3 (F12)
this.KeyDown += (s, e) =>
{
    if (e.Key == Key.F12)
    {
        new Dialogs.SimplePhase3TestDialog(_anonymizationService).ShowDialog();
        e.Handled = true;
    }
};
    
        _settings = AppSettings.Load();
        _pathService = new PathService();

        // NOUVEAU : Initialiser le tracker de poids pour la synthèse
        _synthesisWeightTracker = new SynthesisWeightTracker(_pathService);

        // Initialiser les services de paramètres
        _secureStorageService = new SecureStorageService();
        _windowStateService = new WindowStateService();

        // IMPORTANT: Initialiser le système LLM de manière synchrone d'abord
        _llmFactory = new LLMServiceFactory(_settings, _secureStorageService);
        _llmFactory.ApiKeyMigrationDetected += (s, envKey) => {
            // Proposer la migration au prochain cycle UI
            Dispatcher.InvokeAsync(() => HandleApiKeyMigration(envKey));
        };
        
        _warmupService = new LLMWarmupService(_llmFactory, _settings);
        
        // Initialisation asynchrone sécurisée
        _llmFactory.InitializeAsync();
        _currentLLMService = _llmFactory.GetCurrentProvider();

        // ✅ ORDRE CRITIQUE : Initialiser AnonymizationService AVANT PromptConfigService
        // ✅ MODIFIÉ : Passer AppSettings pour permettre la détection du provider LLM
        _anonymizationService = new AnonymizationService(_settings);

        // ✅ Initialiser PromptConfigService AVEC AnonymizationService pour anonymisation automatique
        _promptConfigService = new PromptConfigService(_anonymizationService);
        _promptTracker = new PromptTrackerService(); // Service de tracking des prompts

        // ✅ MODIFIÉ : Passer AnonymizationService ET PromptTrackerService au constructeur
        _openAIService = new OpenAIService(_llmFactory, _promptConfigService, _anonymizationService, _promptTracker);

        // ✅ NOUVEAU : Initialiser ChatMemoryService (pour mémoire intelligente du Chat)
        _chatMemoryService = new ChatMemoryService(_openAIService);

        // Maintenant on peut initialiser les services qui dépendent de _openAIService
        _storageService = new StorageService(_pathService);
        _contextLoader = new ContextLoader(_storageService);
        _parsingService = new ParsingService();
        _patientIndex = new PatientIndexService(_pathService);

        // ✅ NOUVEAU : Initialiser PatientContextService
        _patientContextService = new PatientContextService(_storageService, _patientIndex);

        // ✅ NOUVEAU : Initialiser LLMGatewayService AVANT les services qui l'utilisent
        _llmGatewayService = new LLMGatewayService(_llmFactory, _anonymizationService, _openAIService, _pathService);

        // ✅ NOUVEAU : Initialiser LetterReAdaptationService
        _reAdaptationService = new LetterReAdaptationService(_patientContextService, _openAIService, _anonymizationService);

        _letterService = new LetterService(_openAIService, _contextLoader, _storageService, _patientContextService, _anonymizationService, _promptConfigService, _llmGatewayService); // ✅ Ajout LLMGatewayService
        _templateExtractor = new TemplateExtractorService(_openAIService);
        _templateExtractor.SetLLMGatewayService(_llmGatewayService); // ✅ Connexion au service d'anonymisation
        _templateManager = new TemplateManagerService();
        _mccLibrary = new MCCLibraryService();
        _promptReformulationService = new PromptReformulationService(_openAIService);
        _attestationService = new AttestationService(_storageService, _pathService, _letterService, _llmGatewayService, _promptConfigService, _patientContextService, _promptTracker); // ✅ MODIFIÉ : Utilise LLMGatewayService
        // ✅ Initialiser FormulaireAssistantService AVEC tous les services nécessaires
        _formulaireService = new FormulaireAssistantService(
            _llmGatewayService,
            _promptConfigService,
            _patientContextService,
            _anonymizationService,
            _llmFactory,
            _settings
        );
        _ordonnanceService = new OrdonnanceService(_letterService, _storageService, _pathService);
        _synthesisService = new SynthesisService(_openAIService, _storageService, _contextLoader, _pathService, _promptConfigService, _synthesisWeightTracker, _anonymizationService, _promptTracker);  // ✅ MODIFIÉ : Ajout AnonymizationService + PromptTracker
        _letterRatingService = new LetterRatingService();
        _documentService = new DocumentService(_llmGatewayService, _pathService, _llmFactory, _settings); // ✅ MODIFIÉ : Utilise LLMGatewayService + LLMFactory
        _scannerService = new ScannerService(_pathService);
        _regenerationService = new RegenerationService(_settings, _anonymizationService, _promptConfigService, _openAIService);  // ✅ MODIFIÉ : Ajout OpenAIService pour Phase 3

        // Initialiser OrdonnanceViewModel
        // NOTE: La logique des ordonnances a été migrée vers OrdonnancesControl
        OrdonnanceViewModel = new ViewModels.OrdonnanceViewModel(_ordonnanceService);

        // Initialiser NoteViewModel
        NoteViewModel = new ViewModels.NoteViewModel(_storageService, _openAIService);
        NoteViewModel.InitializeSynthesisWeightTracker(_synthesisWeightTracker);
        
        // Initialiser AttestationViewModel
        AttestationViewModel = new ViewModels.AttestationViewModel(_attestationService, _pathService);
        AttestationViewModel.InitializeSynthesisWeightTracker(_synthesisWeightTracker);

        // Connecter les événements
AttestationViewModel.StatusMessageChanged += (s, msg) => {
    StatusTextBlock.Text = msg;
    StatusTextBlock.Foreground = new SolidColorBrush(
        msg.StartsWith("✅") ? Colors.Green :
        msg.StartsWith("❌") ? Colors.Red :
        msg.StartsWith("⏳") ? Colors.Blue : Colors.Gray);
};

// NOUVEAU : Rafraîchir l'indicateur de poids après création/modification d'attestation
AttestationViewModel.AttestationListRefreshRequested += (s, e) => {
    System.Diagnostics.Debug.WriteLine("[MainWindow] AttestationListRefreshRequested - Rafraîchissement via ViewModel");
    NotesControlPanel.SynthesisViewModel?.UpdateNotificationBadge();
};



        
        // Connecter les événements NoteViewModel
        NoteViewModel.StatusMessageChanged += OnNoteStatusChanged;
        NoteViewModel.NoteContentLoaded += OnNoteContentLoaded;
        NoteViewModel.NoteStructured += OnNoteStructured;
        NoteViewModel.NoteSaveRequested += OnNoteSaveRequested;
        NoteViewModel.NoteDeleteRequested += OnNoteDeleteRequested;
        NoteViewModel.NoteClearedAfterSave += OnNoteClearedAfterSave;
        NoteViewModel.PatientListRefreshRequested += OnPatientListRefreshRequested;
        NoteViewModel.NoteSaved += OnNoteSaved;
        
        // IMPORTANT: Abonnement PropertyChanged pour forcer la mise à jour du RichTextBox
        // (le binding WPF standard ne fonctionne pas bien avec RichTextBox.IsReadOnly)
        NoteViewModel.PropertyChanged += NoteViewModel_PropertyChanged;
        
        // IMPORTANT: Handler TextChanged pour activer le bouton Sauvegarder lors des modifications
        NotesControlPanel.StructuredNoteTextBox.TextChanged += StructuredNoteText_TextChanged;
        
        // Initialiser le ViewModel de recherche patient
        PatientSearchViewModel = new ViewModels.PatientSearchViewModel(_patientIndex);
        
        // IMPORTANT : Définir le DataContext APRÈS avoir créé tous les ViewModels
        this.DataContext = this;

        // Initialiser PatientListControl
        PatientListControlPanel.Initialize(_patientIndex, _pathService);
        PatientListControlPanel.PatientSelected += (s, patient) => {
            if (patient != null) LoadPatientAsync(patient);
        };
        PatientListControlPanel.PatientDeleted += (s, e) => {
            // Réinitialiser l'interface après suppression
            ResetPatientUI();

            // ✅ NOUVEAU : Recharger la liste des patients après suppression
            PatientListControlPanel.LoadPatients();
        };
        PatientListControlPanel.StatusChanged += (s, msg) => {
            StatusTextBlock.Text = msg;
            StatusTextBlock.Foreground = new SolidColorBrush(
                msg.StartsWith("✅") ? Colors.Green :
                msg.StartsWith("❌") ? Colors.Red : Colors.Gray);
        };

        // Initialiser NotesControl avec SynthesisService, SynthesisWeightTracker et NoteViewModel
        NotesControlPanel.Initialize(_synthesisService, _synthesisWeightTracker, NoteViewModel, _regenerationService);
        NotesControlPanel.StatusChanged += (s, msg) => {
            StatusTextBlock.Text = msg;
            StatusTextBlock.Foreground = new SolidColorBrush(
                msg.StartsWith("✅") || msg.StartsWith("✓") ? Colors.Green :
                msg.StartsWith("❌") ? Colors.Red :
                msg.StartsWith("⏳") ? Colors.Blue : Colors.Gray);
        };

        // Initialiser OcrService
        _ocrService = new OcrService(Path.Combine(_pathService.GetAppDataPath(), "tessdata"));

        // Initialiser FormulairesControl
        FormulairesControlPanel.Initialize(_formulaireService, _letterService, _patientIndex, _documentService, _pathService, _synthesisWeightTracker, _ocrService);
        FormulairesControlPanel.StatusChanged += (s, msg) => {
            StatusTextBlock.Text = msg;
            StatusTextBlock.Foreground = new SolidColorBrush(
                msg.StartsWith("✅") || msg.StartsWith("✓") ? Colors.Green :
                msg.StartsWith("❌") ? Colors.Red :
                msg.StartsWith("⏳") || msg.StartsWith("⏳") ? Colors.Blue :
                msg.StartsWith("⚠️") ? Colors.Orange : Colors.Gray);
        };

        // Initialiser DocumentsControl
        DocumentsControlPanel.Initialize(_documentService, _pathService, _patientIndex, _synthesisWeightTracker, _scannerService, _regenerationService);
        DocumentsControlPanel.StatusChanged += (s, msg) => {
            StatusTextBlock.Text = msg;
            StatusTextBlock.Foreground = new SolidColorBrush(
                msg.StartsWith("✅") || msg.StartsWith("✓") ? Colors.Green :
                msg.StartsWith("❌") ? Colors.Red :
                msg.StartsWith("⏳") ? Colors.Blue :
                msg.StartsWith("⚠️") ? Colors.Orange : Colors.Gray);
        };
        // NOUVEAU : Rafraîchir le badge de synthèse après sauvegarde d'une synthèse de document
        DocumentsControlPanel.DocumentSynthesisSaved += (s, e) => {
            NotesControlPanel.SynthesisViewModel?.UpdateNotificationBadge();
            System.Diagnostics.Debug.WriteLine("[MainWindow] Badge synthèse mis à jour après sauvegarde synthèse document");
        };

        // Initialiser CourriersControl
        CourriersControlPanel.Initialize(_letterService, _pathService, _patientIndex, _mccLibrary, _letterRatingService, _reAdaptationService, _synthesisWeightTracker, _regenerationService); // ✅ Ajout RegenerationService pour régénération IA
        CourriersControlPanel.StatusChanged += (s, msg) => {
            StatusTextBlock.Text = msg;
            StatusTextBlock.Foreground = new SolidColorBrush(
                msg.StartsWith("✅") || msg.StartsWith("✓") ? Colors.Green :
                msg.StartsWith("❌") ? Colors.Red :
                msg.StartsWith("⏳") ? Colors.Blue :
                msg.StartsWith("⚠️") ? Colors.Orange : Colors.Gray);
        };
        CourriersControlPanel.CreateLetterWithAIRequested += async (s, e) => {
            await HandleCreateLetterWithAIAsync();
        };
        // NOUVEAU : Rafraîchir le badge de synthèse après sauvegarde d'un courrier
        CourriersControlPanel.LetterSaved += (s, e) => {
            NotesControlPanel.SynthesisViewModel?.UpdateNotificationBadge();
            System.Diagnostics.Debug.WriteLine("[MainWindow] Badge synthèse mis à jour après sauvegarde courrier");
        };
        // NOUVEAU : Naviguer vers Templates avec courrier à transformer en MCC
        CourriersControlPanel.NavigateToTemplatesWithLetter += OnNavigateToTemplatesWithLetter;

        // Initialiser ChatControl (avec LLMGatewayService pour anonymisation centralisée)
        ChatControlPanel.Initialize(_openAIService, _storageService, _patientContextService, _anonymizationService, _promptConfigService, _llmGatewayService, _promptTracker, _chatMemoryService);
        ChatControlPanel.StatusChanged += (s, msg) => {
            StatusTextBlock.Text = msg;
            StatusTextBlock.Foreground = new SolidColorBrush(
                msg.StartsWith("✅") || msg.StartsWith("✓") ? Colors.Green :
                msg.StartsWith("❌") ? Colors.Red :
                msg.StartsWith("⏳") ? Colors.Blue :
                msg.StartsWith("⚠️") ? Colors.Orange : Colors.Gray);
        };
        ChatControlPanel.SaveExchangeRequested += (s, exchange) => {
            // Ouvrir le dialogue pour saisir l'étiquette
            var dialog = new Dialogs.SaveChatDialog();
            dialog.Owner = this;
            
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Etiquette))
            {
                ChatControlPanel.CompleteSaveExchange(exchange, dialog.Etiquette);
            }
        };

        // Initialiser TemplatesControl
        TemplatesPanel.Initialize(_templateExtractor, _mccLibrary);
        TemplatesPanel.StatusChanged += (s, msg) => {
            StatusTextBlock.Text = msg;
            StatusTextBlock.Foreground = new SolidColorBrush(
                msg.StartsWith("✅") || msg.StartsWith("✓") ? Colors.Green :
                msg.StartsWith("❌") ? Colors.Red :
                msg.StartsWith("⏳") ? Colors.Blue :
                msg.StartsWith("⚠️") ? Colors.Orange : Colors.Gray);
        };
        TemplatesPanel.ErrorOccurred += (s, e) => {
            MessageBox.Show(e.message, e.title, MessageBoxButton.OK, MessageBoxImage.Error);
        };
        TemplatesPanel.MCCLibraryRequested += (s, e) => {
            OpenMCCLibraryDialog();
        };
        TemplatesPanel.TemplateSaved += (s, e) => {
            MessageBox.Show("✅ MCC ajouté avec succès", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
        };

        PatientSearchViewModel.PatientSelected += (s, patient) => {
            if (patient != null) LoadPatientAsync(patient);
        };
        
        PatientSearchViewModel.OpenPatientListRequested += (s, e) => {
            var dialog = new Dialogs.PatientListDialog(_patientIndex);
            dialog.Owner = this;

            // Écouter le double-clic pour charger le patient sans fermer la dialog
            dialog.PatientDoubleClicked += (sender, patient) => {
                LoadPatientAsync(patient);
            };

            if (dialog.ShowDialog() == true && dialog.SelectedPatient != null)
            {
                LoadPatientAsync(dialog.SelectedPatient);
            }
        };
        PatientSearchViewModel.CreatePatientRequested += (s, query) => {
            // 🐛 DEBUG: Logger l'appel
            System.Diagnostics.Debug.WriteLine($"[CreatePatientRequested] Query='{query}'");

            // Parser le texte avec Doctolib
            var parseResult = _parsingService.ParseDoctolibBlock(query);
            
            CreatePatientDialog dialog;
            if (parseResult.Success && !string.IsNullOrEmpty(parseResult.Prenom) && !string.IsNullOrEmpty(parseResult.Nom))
            {
                dialog = new CreatePatientDialog(
                    parseResult.Prenom, 
                    parseResult.Nom, 
                    parseResult.Dob, 
                    parseResult.Sex
                );
                
                // Si texte restant, le mettre dans note brute
                if (!string.IsNullOrEmpty(parseResult.RemainingText))
                {
                    NotesControlPanel.RawNoteTextBox.Text = parseResult.RemainingText;
                }
            }
            else
            {
                // Parser simple "Prénom Nom"
                var (prenom, nom) = _parsingService.ParseSimpleFormat(query);
                dialog = new CreatePatientDialog();
                if (prenom != null) dialog.PrenomTextBox.Text = prenom;
                if (nom != null) dialog.NomTextBox.Text = nom;
            }
            
            dialog.Owner = this;
            if (dialog.ShowDialog() == true && dialog.Result != null)
            {
                var (success, message, id, path) = _patientIndex.Upsert(dialog.Result);

                // ✅ NOUVEAU: Gérer les doublons détectés
                if (!success && message.StartsWith("DUPLICATE_DETECTED"))
                {
                    // Parser le message: DUPLICATE_DETECTED|ID|NomComplet|Date
                    var parts = message.Split('|');
                    var existingId = parts.Length > 1 ? parts[1] : "";
                    var existingName = parts.Length > 2 ? parts[2] : "";
                    var existingDob = parts.Length > 3 ? parts[3] : "";

                    // Créer l'ID du nouveau patient pour comparaison
                    var newId = $"{dialog.Result.Nom}_{dialog.Result.Prenom.Replace(" ", "_")}";
                    var newName = $"{dialog.Result.Prenom} {dialog.Result.Nom}";
                    var newDob = "";
                    if (!string.IsNullOrEmpty(dialog.Result.Dob) && DateTime.TryParse(dialog.Result.Dob, out var dob))
                    {
                        newDob = dob.ToString("dd/MM/yyyy");
                    }

                    // Afficher le dialogue de confirmation
                    var duplicateDialog = new Dialogs.DuplicatePatientDialog(
                        existingId,
                        existingName,
                        existingDob,
                        newName,
                        newDob,
                        newId
                    );
                    duplicateDialog.Owner = this;

                    if (duplicateDialog.ShowDialog() == true)
                    {
                        if (duplicateDialog.Result == Dialogs.DuplicateDialogResult.UseExisting)
                        {
                            // Utiliser le patient existant → Charger le patient
                            var existingPatient = _patientIndex.GetAllPatients()
                                .FirstOrDefault(p => p.Id == existingId);

                            if (existingPatient != null)
                            {
                                LoadPatientAsync(existingPatient);
                                StatusTextBlock.Text = $"✓ Patient existant chargé: {existingName}";
                                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
                            }
                        }
                        else if (duplicateDialog.Result == Dialogs.DuplicateDialogResult.CreateAnyway)
                        {
                            // Créer quand même → Message à l'utilisateur
                            StatusTextBlock.Text = "⚠️ Création annulée - Doublon détecté. Modifiez le nom pour créer un nouveau dossier.";
                            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
                        }
                        // Sinon Cancel → Ne rien faire
                    }

                    return;
                }

                if (success && id != null && path != null)
                {
                    // Créer PatientIndexEntry et charger immédiatement
                    var newPatient = new PatientIndexEntry
                    {
                        Id = id,
                        Prenom = dialog.Result.Prenom,
                        Nom = dialog.Result.Nom,
                        Dob = dialog.Result.Dob,
                        Sexe = dialog.Result.Sexe,
                        DirectoryPath = path
                    };

                    LoadPatientAsync(newPatient);

                    // Rafraîchir la liste des patients pour afficher le nouveau patient
                    LoadPatientsInPanel();
                }
                else
                {
                    StatusTextBlock.Text = $"❌ {message}";
                    StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                }
            }
        };
        
        // Vérifier la clé API
        if (!_openAIService.IsApiKeyConfigured())
        {
            StatusTextBlock.Text = "⚠️ Clé API OpenAI non configurée. Définissez OPENAI_API_KEY.";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
        }
        
        // Initialiser l'index patient
        InitializePatientIndex();
        
        // Charger templates personnalisés (géré par CourriersControl)
        CourriersControlPanel.ReloadTemplates();
        
        // Wire events
        WireSearchEvents();
        
        // Lancer le warm-up automatique
        InitializeLLMSystem();
    }
    
    // ===== SYSTÈME LLM =====
    

    // ===== INITIALISATION =====
    
    private async void InitializePatientIndex()
    {
        StatusTextBlock.Text = "⏳ Chargement de l'index patients...";
        StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);
        
        await _patientIndex.ScanAsync();
        _patientIndex.StartWatching();
        
        // Charger automatiquement tous les patients dans le panneau fixe
        LoadPatientsInPanel();
        
        StatusTextBlock.Text = "✓ Prêt";
        StatusTextBlock.Foreground = new SolidColorBrush(Colors.Gray);
    }

    private void WireSearchEvents()
    {
        
        
        AnalysePromptsBtn.Click += AnalysePromptsBtn_Click;
        OpenPatientFolderBtn.Click += OpenPatientFolderBtn_Click;
        


        // Templates personnalisés - MIGRÉ vers TemplatesControl (05/12/2025)
        // Legacy event handlers removed: AnalyzeLetterBtn_Click, SaveTemplateBtn_Click

    }
    

    
    private void ResetPatientUI()
    {
        // RESET MÉMOIRE CHAT - MIGRÉ vers ChatControl.Reset()
        // _chatHistory.Clear();
        // _savedChatExchanges.Clear();
        
        // Reset le ViewModel de Note
        NoteViewModel.Reset();
        
        // Vider les champs de texte
        NotesControlPanel.RawNoteTextBox.Text = string.Empty;
        NotesControlPanel.StructuredNoteTextBox.Document = new FlowDocument();
       
        
        // Remettre zone structurée en readonly
        NotesControlPanel.StructuredNoteTextBox.IsReadOnly = true;
        NotesControlPanel.StructuredNoteTextBox.Background = new SolidColorBrush(Color.FromRgb(250, 250, 250));

        // Réinitialiser la section Courriers (migré vers CourriersControl)
        CourriersControlPanel.Reset();

        // Réinitialiser ChatControl
        ChatControlPanel.Reset();

        
    }

    private void RenderPatientCard(PatientMetadata metadata)
    {
        PatientNameLabel.Text = $"{metadata.Nom} {metadata.Prenom}";
        
        if (metadata.Age.HasValue)
            PatientAgeLabel.Text = $"{metadata.Age} ans";
        else
            PatientAgeLabel.Text = "";
            
        if (!string.IsNullOrEmpty(metadata.DobFormatted))
            PatientDobLabel.Text = $"Né(e) le {metadata.DobFormatted}";
        else
            PatientDobLabel.Text = "";
            
        if (!string.IsNullOrEmpty(metadata.Sexe))
            PatientSexLabel.Text = metadata.Sexe == "H" ? "Homme" : "Femme";
        else
            PatientSexLabel.Text = "";
    }

    
    private string CleanYamlFromMarkdown(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return content;
        
        // Vérifier si le contenu commence par ---
        if (!content.TrimStart().StartsWith("---"))
            return content;
        
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        bool inYaml = false;
        int yamlEndIndex = 0;
        
        for (int i = 0; i < lines.Length; i++)
        {
            if (i == 0 && lines[i].Trim() == "---")
            {
                inYaml = true;
                continue;
            }
            if (inYaml && lines[i].Trim() == "---")
            {
                yamlEndIndex = i + 1;
                break;
            }
        }
        
        if (yamlEndIndex > 0 && yamlEndIndex < lines.Length)
        {
            // Retourner tout après le second ---
            return string.Join("\n", lines.Skip(yamlEndIndex)).TrimStart();
        }
        
        return content;
    }
    
    /// <summary>
    /// Estime le nombre de tokens dans un texte (approximation : 1 token ≈ 4 caractères)
    /// </summary>
    private int EstimateTokens(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        return text.Length / 4;
    }
    
    
    // ===== TEMPLATES PERSONNALISÉS =====
    
    /// <summary>
    /// Ouvre le dialogue de bibliothèque MCC
    /// Appelé depuis TemplatesControl
    /// </summary>
    private async void OpenMCCLibraryDialog()
    {
        try
        {
            var dialog = new MCCLibraryDialog(_mccLibrary, _letterRatingService);
            dialog.Owner = this;
            
            // Gérer le résultat du dialogue (génération demandée)
            if (dialog.ShowDialog() == true && dialog.ShouldGenerate && dialog.SelectedMCC != null)
            {
                if (_selectedPatient == null)
                {
                    MessageBox.Show("Pour générer un courrier, veuillez d'abord sélectionner un patient.", 
                        "Patient requis", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                await GenerateLetterFromMCCAsync(dialog.SelectedMCC);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors de l'ouverture de la bibliothèque MCC : {ex.Message}",
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Ouvre le dialogue de bibliothèque MCC pour explorer et sélectionner des templates
    /// Génère ensuite un courrier si un MCC est sélectionné
    /// </summary>
    private async void OpenMCCLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPatient == null)
        {
            MessageBox.Show("Veuillez d'abord sélectionner un patient.", "Information",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        try
        {
            var dialog = new MCCLibraryDialog(_mccLibrary, _letterRatingService);
            dialog.Owner = this;
            
            if (dialog.ShowDialog() == true && dialog.SelectedMCC != null)
            {
                // Utiliser la logique partagée
                await GenerateLetterFromMCCAsync(dialog.SelectedMCC);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors de l'ouverture de la bibliothèque:\n\n{ex.Message}", 
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            
            StatusTextBlock.Text = $"❌ Erreur: {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
        }
    }
    
    
    
    /// <summary>
    /// Génère un courrier à partir d'un MCC sélectionné
    /// Logique partagée utilisée par OpenMCCLibraryButton_Click et OpenMCCLibraryDialog
    /// </summary>
    private async Task GenerateLetterFromMCCAsync(MCCModel selectedMCC)
    {
        if (_selectedPatient == null) return;

        var busyService = BusyService.Instance;
        var cancellationToken = busyService.Start($"Génération du courrier depuis MCC '{selectedMCC.Name}'...", canCancel: true);

        try
        {
            StatusTextBlock.Text = $"⏳ Génération du courrier depuis MCC '{selectedMCC.Name}'...";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);
            busyService.UpdateProgress(10, "Génération IA en cours...");

            // Générer le courrier avec l'IA en utilisant toutes les métadonnées du MCC
            var (success, markdown, error) = await _letterService.GenerateLetterFromMCCAsync(
                _selectedPatient.NomComplet,
                selectedMCC
            );

            if (success && !string.IsNullOrEmpty(markdown))
            {
                cancellationToken.ThrowIfCancellationRequested();
                busyService.UpdateProgress(40, "Analyse et ré-adaptation...");

                // Basculer vers l'onglet Courriers
                AssistantTabControl.SelectedIndex = 1;

                // Incrémenter le compteur d'utilisation du MCC
                _mccLibrary.IncrementUsage(selectedMCC.Id);

                // Réadaptation avec le service universel
                string finalMarkdown = markdown;
                
                if (_reAdaptationService != null)
                {
                    busyService.UpdateStep("Vérification des informations manquantes...");
                    var reAdaptResult = await _reAdaptationService.ReAdaptLetterAsync(
                        markdown,
                        _selectedPatient.NomComplet,
                        selectedMCC.Name
                    );

                    if (reAdaptResult.NeedsMissingInfo)
                    {
                        StatusTextBlock.Text = "❓ Informations requises manquantes...";
                        StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
                        
                        // Cacher le busy service pour le dialogue
                        busyService.Stop();

                        var missingDialog = new MissingInfoDialog(reAdaptResult.MissingFields);
                        missingDialog.Owner = this;

                        if (missingDialog.ShowDialog() == true && missingDialog.CollectedInfo != null)
                        {
                            // Redémarrer le busy service
                            busyService.Start("Finalisation de l'adaptation...", canCancel: false);
                            busyService.UpdateProgress(80);

                            StatusTextBlock.Text = "⏳ Ré-adaptation avec infos complètes...";
                            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);

                            var finalResult = await _reAdaptationService.CompleteReAdaptationAsync(
                                reAdaptResult,
                                missingDialog.CollectedInfo
                            );

                            if (finalResult.Success)
                            {
                                finalMarkdown = finalResult.ReAdaptedMarkdown ?? markdown;
                                StatusTextBlock.Text = "✅ Courrier MCC complété - Vous pouvez sauvegarder";
                                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
                            }
                            else
                            {
                                StatusTextBlock.Text = $"⚠️ Erreur ré-adaptation : {finalResult.Error}";
                                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
                            }
                        }
                        else
                        {
                            StatusTextBlock.Text = "⚠️ Réadaptation annulée";
                            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
                        }
                    }
                    else
                    {
                        finalMarkdown = reAdaptResult.ReAdaptedMarkdown ?? markdown;
                        StatusTextBlock.Text = $"✅ Courrier généré depuis MCC '{selectedMCC.Name}'";
                        StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
                    }
                }

                // Afficher dans CourriersControl
                CourriersControlPanel.DisplayGeneratedLetter(finalMarkdown, selectedMCC.Id, selectedMCC.Name);
                
                busyService.UpdateProgress(100, "Terminé");
                await Task.Delay(200);

                MessageBox.Show(
                    $"✅ Courrier généré avec succès depuis le MCC !\n\n" +
                    $"Template : {selectedMCC.Name}\n" +
                    $"Type : {selectedMCC.Semantic?.DocType ?? "Non spécifié"}\n" +
                    $"Audience : {selectedMCC.Semantic?.Audience ?? "Non spécifiée"}\n" +
                    $"Ton : {selectedMCC.Semantic?.Tone ?? "Non spécifié"}\n\n" +
                    $"Le brouillon est affiché dans l'onglet Courriers.\n" +
                    $"Vous pouvez le modifier puis le sauvegarder.",
                    "Succès",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            else
            {
                MessageBox.Show(
                    $"❌ Erreur lors de la génération:\n\n{error}",
                    "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );

                StatusTextBlock.Text = $"❌ Erreur: {error}";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            }
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "🚫 Génération annulée";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur imprévue lors de la génération:\n\n{ex.Message}", 
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            
            StatusTextBlock.Text = $"❌ Erreur: {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
        }
        finally
        {
            busyService.Stop();
        }
    }
    
    // ===== DOCUMENTS =====
    
    // Services OCR
    private OcrService _ocrService;

    private DocumentService _documentService;
    private ScannerService _scannerService;


    
private async Task HandleCreateLetterWithAIAsync()
{
    if (_selectedPatient == null)
    {
        MessageBox.Show("Veuillez d'abord sélectionner un patient.", "Patient requis",
            MessageBoxButton.OK, MessageBoxImage.Warning);
        return;
    }

    // Construire le contexte patient enrichi
    var patientContext = BuildPatientContext(_selectedPatient);

    var dialog = new CreateLetterWithAIDialog(_promptReformulationService, _mccLibrary, patientContext)
    {
        Owner = this
    };

    var result = dialog.ShowDialog();

    if (result == true && dialog.Result.Success)
    {
        var busyService = BusyService.Instance;
        var cancellationToken = busyService.Start("Génération du courrier en cours...", canCancel: true);

        try
        {
            var letterResult = dialog.Result;
            StatusTextBlock.Text = "⏳ Génération du courrier en cours...";
            busyService.UpdateProgress(10, "Initialisation de la génération...");
            await Task.Delay(100);

            string? mccId = null;
            string? mccName = null;
            string? generatedLetter = null;

            if (letterResult.UseStandardGeneration)
            {
                // Génération standard → Pas de MCC
                busyService.UpdateStep("Génération standard par l'IA...");
                generatedLetter = await GenerateLetterContentAsync(letterResult.UserRequest, null, null);
            }
            else if (letterResult.SelectedMCC != null)
            {
                mccId = letterResult.SelectedMCC.Id;
                mccName = letterResult.SelectedMCC.Name;
                System.Diagnostics.Debug.WriteLine($"[MCC Tracking] MCC sélectionné via matching: {mccName} (ID: {mccId})");

                busyService.UpdateStep($"Génération via MCC '{mccName}'...");
                generatedLetter = await GenerateLetterContentAsync(
                    letterResult.UserRequest,
                    letterResult.SelectedMCC,
                    letterResult.Analysis);
                _mccLibrary.IncrementUsage(letterResult.SelectedMCC.Id);
            }

            cancellationToken.ThrowIfCancellationRequested();

            // ✅ NOUVEAU : Réadaptation avec le service universel
            if (!string.IsNullOrEmpty(generatedLetter) && _reAdaptationService != null)
            {
                busyService.UpdateProgress(50, "Vérification des informations manquantes...");
                StatusTextBlock.Text = "⏳ Vérification des informations manquantes...";
                await Task.Delay(100);

                var reAdaptResult = await _reAdaptationService.ReAdaptLetterAsync(
                    generatedLetter,
                    _selectedPatient.NomComplet,
                    mccName ?? "Courrier généré par IA",
                    letterResult.UserRequest
                );

                if (reAdaptResult.NeedsMissingInfo)
                {
                    // Cacher le busy service pour le dialogue
                    busyService.Stop();

                    var missingDialog = new MissingInfoDialog(reAdaptResult.MissingFields);
                    missingDialog.Owner = this;

                    if (missingDialog.ShowDialog() == true && missingDialog.CollectedInfo != null)
                    {
                        // Redémarrer le busy service
                        busyService.Start("Finalisation de l'adaptation...", canCancel: false);
                        busyService.UpdateProgress(80);

                        StatusTextBlock.Text = "⏳ Réadaptation avec les nouvelles informations...";
                        await Task.Delay(100);

                        var finalResult = await _reAdaptationService.CompleteReAdaptationAsync(
                            reAdaptResult,
                            missingDialog.CollectedInfo
                        );
                        
                        if (finalResult.Success)
                        {
                            generatedLetter = finalResult.ReAdaptedMarkdown;
                        }
                    }
                    // Si annulé, on garde generatedLetter tel quel
                }
                else
                {
                    generatedLetter = reAdaptResult.ReAdaptedMarkdown ?? generatedLetter;
                }
            }

            if (!string.IsNullOrEmpty(generatedLetter))
            {
                // Afficher dans CourriersControl
                CourriersControlPanel.DisplayGeneratedLetter(generatedLetter, mccId, mccName);
                StatusTextBlock.Text = "✅ Courrier généré avec succès";
                busyService.UpdateProgress(100, "Terminé");
                await Task.Delay(200);
            }
        }
        catch (OperationCanceledException)
        {
            StatusTextBlock.Text = "🚫 Génération annulée";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors de la création du courrier :\n{ex.Message}",
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusTextBlock.Text = "❌ Erreur génération courrier";
        }
        finally
        {
            busyService.Stop();
        }
    }
}

/// <summary>
/// Gère la navigation vers l'onglet Templates avec le contenu d'un courrier à transformer en MCC
/// </summary>
private void OnNavigateToTemplatesWithLetter(object? sender, string letterPath)
{
    try
    {
        // 1. Vérifier que le fichier existe
        if (!File.Exists(letterPath))
        {
            MessageBox.Show("Le fichier du courrier n'existe plus.", "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Error);
            StatusTextBlock.Text = "❌ Fichier introuvable";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            return;
        }

        // 2. Lire le contenu du courrier markdown
        string letterMarkdown = File.ReadAllText(letterPath);

        // 3. Naviguer vers l'onglet Templates (index 6)
        AssistantTabControl.SelectedIndex = 6;

        // 4. Accéder au ViewModel du TemplatesControl et copier le contenu
        if (TemplatesPanel.DataContext is ViewModels.TemplatesViewModel viewModel)
        {
            viewModel.ExampleLetterText = letterMarkdown;
        }

        // 5. Afficher message de succès
        StatusTextBlock.Text = "✅ Courrier copié dans Templates. Cliquez sur 'Analyser avec l'IA' pour le transformer en MCC.";
        StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);

        System.Diagnostics.Debug.WriteLine($"[MainWindow] Courrier {Path.GetFileName(letterPath)} copié vers Templates");
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Erreur lors du chargement du courrier:\n{ex.Message}",
            "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        StatusTextBlock.Text = "❌ Erreur chargement courrier";
        StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
    }
}

/// <summary>
/// Génère le contenu du courrier et le retourne (sans afficher)
/// </summary>
private async Task<string?> GenerateLetterContentAsync(string userRequest, MCCModel? mcc, LetterAnalysisResult? analysis)
{
    // ✅ ANONYMISATION : Générer le pseudonyme
    var sexe = _selectedPatient.Sexe ?? "M";
    var (nomAnonymise, anonContext) = _anonymizationService.Anonymize("", _selectedPatient.NomComplet, sexe);

    // ✅ NOUVEAU : Utiliser PatientContextService pour le contexte complet
    var contextBundle = _patientContextService.GetCompleteContext(_selectedPatient.NomComplet, userRequest);
    var patientContext = contextBundle.ToPromptText(nomAnonymise, anonContext);  // ✅ Passer le contexte d'anonymisation

    System.Diagnostics.Debug.WriteLine($"[GenerateLetterContentAsync] {contextBundle.ToDebugText()}");

    // ✅ Utiliser le système de prompts configurables
    var systemPrompt = _promptConfigService.GetActivePrompt("system_global")
        .Replace("{{Medecin}}", _settings.Medecin);

    string userPrompt;
    if (mcc != null && analysis != null)
    {
        // ✅ Exploiter TOUTES les métadonnées du MCC et de l'analyse
        // Si le MCC a un PromptTemplate personnalisé, l'utiliser en priorité
        var basePrompt = !string.IsNullOrWhiteSpace(mcc.PromptTemplate)
            ? mcc.PromptTemplate
            : _promptConfigService.GetActivePrompt("template_adaptation");

        // Construire les informations sémantiques du MCC
        var mccSemanticInfo = "";
        if (mcc.Semantic != null)
        {
            mccSemanticInfo = $@"
📋 CARACTÉRISTIQUES DU MODÈLE MCC ""{mcc.Name}"":
- Type de document: {mcc.Semantic.DocType ?? "non spécifié"}
- Public cible: {mcc.Semantic.Audience ?? "non spécifié"}
- Ton recommandé: {mcc.Semantic.Tone ?? "professionnel"}
- Tranche d'âge: {mcc.Semantic.AgeGroup ?? "tout âge"}
- Niveau de détail: {mcc.Semantic.DetailLevel ?? "standard"}";

            // Ajouter les thèmes cliniques si disponibles
            if (mcc.Semantic.Themes != null && mcc.Semantic.Themes.Any())
            {
                mccSemanticInfo += $"\n- Thèmes cliniques: {string.Join(", ", mcc.Semantic.Themes)}";
            }

            // Ajouter les mots-clés à utiliser/éviter
            if (mcc.Semantic.Keywords != null)
            {
                if (mcc.Semantic.Keywords.AUtiliser != null && mcc.Semantic.Keywords.AUtiliser.Any())
                {
                    mccSemanticInfo += $"\n- ✅ Mots-clés à utiliser: {string.Join(", ", mcc.Semantic.Keywords.AUtiliser)}";
                }
                if (mcc.Semantic.Keywords.AEviter != null && mcc.Semantic.Keywords.AEviter.Any())
                {
                    mccSemanticInfo += $"\n- ❌ Mots-clés à éviter: {string.Join(", ", mcc.Semantic.Keywords.AEviter)}";
                }
            }
        }

        // Ajouter les keywords du MCC si disponibles
        var mccKeywordsInfo = "";
        if (mcc.Keywords != null && mcc.Keywords.Any())
        {
            mccKeywordsInfo = $"\n- Mots-clés contextuels: {string.Join(", ", mcc.Keywords)}";
        }

        // Construire les informations de l'analyse de la demande utilisateur
        var requestAnalysisInfo = $@"

🎯 ANALYSE DE LA DEMANDE UTILISATEUR:
- Mots-clés identifiés: {string.Join(", ", analysis.Keywords ?? new List<string>())}
- Type de document demandé: {analysis.DocType ?? "non spécifié"}
- Public cible: {analysis.Audience ?? "non spécifié"}
- Ton souhaité: {analysis.Tone ?? "professionnel"}
- Tranche d'âge: {analysis.AgeGroup ?? "non spécifié"}
- Confiance de l'analyse: {analysis.ConfidenceScore:P0}";

        // Construire le prompt enrichi
        userPrompt = basePrompt
            .Replace("{{Contexte}}", patientContext)
            .Replace("{{Template_Name}}", mcc.Name)
            .Replace("{{Template_Markdown}}", mcc.TemplateMarkdown);

        // Ajouter les métadonnées enrichies
        userPrompt += mccSemanticInfo;
        userPrompt += mccKeywordsInfo;
        userPrompt += requestAnalysisInfo;

        // Ajouter la demande utilisateur originale
        userPrompt += $"\n\n📝 DEMANDE UTILISATEUR ORIGINALE:\n{userRequest}";

        // Instructions finales pour l'IA
        var tone = mcc.Semantic?.Tone ?? "professionnel";
        var audience = mcc.Semantic?.Audience ?? analysis.Audience;
        var ageGroup = mcc.Semantic?.AgeGroup ?? analysis.AgeGroup;

        userPrompt += $"\n\n⚠️ INSTRUCTIONS IMPORTANTES:\n";
        userPrompt += $"1. Respecte le ton \"{tone}\" et adapte-le au public \"{audience}\"\n";
        userPrompt += $"2. Utilise le template comme structure de base mais adapte-le à la demande utilisateur\n";
        userPrompt += $"3. Intègre les mots-clés pertinents naturellement dans le texte\n";
        userPrompt += $"4. Assure-toi que le document est approprié pour la tranche d'âge {ageGroup}\n";
        userPrompt += $"5. Conserve le format Markdown du template";
    }
    else
    {
        // Utiliser le prompt de génération avec contexte
        userPrompt = _promptConfigService.GetActivePrompt("letter_generation_with_context")
            .Replace("{{Contexte}}", patientContext)
            .Replace("{{User_Request}}", userRequest);
    }

    // ✅ Utiliser ChatAsync avec les prompts configurables
    var messages = new List<(string role, string content)>
    {
        ("user", userPrompt)
    };

    var (success, letter, error) = await _currentLLMService.ChatAsync(systemPrompt, messages, maxTokens: 2000);

    // ✅ Logger le prompt dans le tracker
    _promptTracker.LogPrompt(new Models.PromptLogEntry
    {
        Timestamp = DateTime.Now,
        Module = "Courrier",
        SystemPrompt = systemPrompt,
        UserPrompt = userPrompt,
        AIResponse = letter ?? error ?? "",
        TokensUsed = EstimateTokens(systemPrompt + userPrompt + (letter ?? "")),
        LLMProvider = _currentLLMService?.GetType().Name ?? "Unknown",
        ModelName = "gpt-4o-mini", // TODO: récupérer dynamiquement
        Success = success,
        Error = error
    });

    if (success)
    {
        // ✅ Désanonymiser : remplacer le pseudonyme par le vrai nom
        var deanonymizedLetter = _anonymizationService.Deanonymize(letter, anonContext);
        return deanonymizedLetter;
    }
    else
    {
        MessageBox.Show($"Erreur de génération :\n{error}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        return null;
    }
}

/// <summary>
/// Construit le contexte patient pour l'analyse IA des demandes de courriers
/// </summary>
private PatientContext BuildPatientContext(PatientIndexEntry patient)
{
    var context = new PatientContext();
    
    try
    {
        // Récupérer les métadonnées du patient
        var metadata = _patientIndex.GetMetadata(patient.Id);
        
        if (metadata != null)
        {
            context.NomComplet = $"{metadata.Prenom} {metadata.Nom}";
            context.Age = metadata.Age ?? patient.Age; // ✅ Fallback sur patient.Age si metadata.Age est null
            context.Sexe = metadata.Sexe == "H" ? "Homme" : metadata.Sexe == "F" ? "Femme" : metadata.Sexe;
            context.DateNaissance = metadata.DobFormatted ?? patient.DobFormatted;
        }
        else
        {
            // Fallback complet depuis PatientIndexEntry
            context.NomComplet = patient.NomComplet;
            context.Age = patient.Age; // ✅ Copier l'âge aussi
            context.Sexe = patient.Sexe == "H" ? "Homme" : patient.Sexe == "F" ? "Femme" : patient.Sexe;
            context.DateNaissance = patient.DobFormatted;
        }
        
        // NOUVEAU : Priorité à la synthèse patient si disponible
        var synthesisPath = System.IO.Path.Combine(patient.DirectoryPath, "synthese", "synthese.md");
        var allNotesContent = new System.Text.StringBuilder();

        if (System.IO.File.Exists(synthesisPath))
        {
            // ✅ SYNTHÈSE DISPONIBLE → Utiliser comme contexte prioritaire
            try
            {
                var synthesisContent = System.IO.File.ReadAllText(synthesisPath, System.Text.Encoding.UTF8);

                // Retirer YAML frontmatter si présent
                var cleanContent = synthesisContent;
                if (synthesisContent.TrimStart().StartsWith("---"))
                {
                    var lines = synthesisContent.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    int yamlEndIndex = 0;
                    bool inYaml = false;

                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (i == 0 && lines[i].Trim() == "---")
                        {
                            inYaml = true;
                            continue;
                        }
                        if (inYaml && lines[i].Trim() == "---")
                        {
                            yamlEndIndex = i + 1;
                            break;
                        }
                    }

                    if (yamlEndIndex > 0 && yamlEndIndex < lines.Length)
                    {
                        cleanContent = string.Join("\n", lines.Skip(yamlEndIndex)).TrimStart();
                    }
                }

                // ✅ CORRECTION : Injecter TOUTE la synthèse sans limitation
                // Pas de troncature - utiliser le contenu complet pour un contexte maximal
                context.NotesRecentes.Add($"📋 SYNTHÈSE PATIENT COMPLÈTE :\n{cleanContent}");
                allNotesContent.AppendLine(cleanContent); // Pour détection diagnostics

                System.Diagnostics.Debug.WriteLine($"[PatientContext] Utilisation de la synthèse patient complète ({cleanContent.Length} caractères)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PatientContext] Erreur lecture synthèse: {ex.Message}, fallback notes");
                // En cas d'erreur, continuer vers fallback
            }
        }

        // ⚠️ FALLBACK : Si pas de synthèse ou erreur, utiliser notes récentes
        if (context.NotesRecentes.Count == 0)
        {
            var recentNotes = NoteViewModel.Notes.Take(3).ToList();

            if (recentNotes.Any())
            {
                foreach (var note in recentNotes)
                {
                    // Pour le contexte IA : utiliser preview limité à 300 caractères (au lieu de 150)
                    var preview = !string.IsNullOrEmpty(note.Preview)
                        ? (note.Preview.Length > 300 ? note.Preview.Substring(0, 300) + "..." : note.Preview)
                        : note.DateLabel;

                    context.NotesRecentes.Add(preview);

                    // Pour la détection de diagnostics : utiliser le contenu COMPLET de la note
                    if (!string.IsNullOrEmpty(note.Preview))
                    {
                        allNotesContent.AppendLine(note.Preview);
                    }
                }
            }

            System.Diagnostics.Debug.WriteLine("[PatientContext] Fallback: 3 dernières notes");
        }

        
        
        // ✅ Laisser la liste vide - l'IA utilisera la synthèse patient
        context.DiagnosticsConnus = new List<string>();
        
        
        System.Diagnostics.Debug.WriteLine($"[PatientContext] Construit pour {context.NomComplet}");
        System.Diagnostics.Debug.WriteLine($"[PatientContext] - Notes: {context.NotesRecentes.Count}");
        System.Diagnostics.Debug.WriteLine($"[PatientContext] - Diagnostics détectés: {string.Join(", ", context.DiagnosticsConnus)}");
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[PatientContext] Erreur construction: {ex.Message}");
    }
    
    return context;
}
    
    
   
    /// Retourne le service LLM actuellement configuré
    /// </summary>
    public ILLMService? GetCurrentLLMService()
    {
        return _currentLLMService;
    }

    /// <summary>
    /// Rafraîchit la liste des templates MCC dans la combobox Courriers
    /// Appelé depuis MCCLibraryDialog après ajout/retrait d'un MCC
    /// </summary>
    public void RefreshCourriersTemplates()
    {
        try
        {
            if (CourriersControlPanel != null)
            {
                CourriersControlPanel.ReloadTemplates();
                System.Diagnostics.Debug.WriteLine("[MainWindow] Templates Courriers rafraîchis");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[MainWindow] CourriersControlPanel n'est pas initialisé");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] Erreur rafraîchissement templates: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[MainWindow] Stack trace: {ex.StackTrace}");
        }
    }
}

/// <summary>
/// Classe pour l'affichage des patients dans le panneau/DataGrid
/// </summary>
public class PatientDisplayInfo
{
    public PatientIndexEntry Patient { get; set; } = null!;
    public string NomComplet => Patient.NomComplet;
    public string AgeDisplay { get; set; } = string.Empty;
    public string LastConsultDisplay { get; set; } = string.Empty;
    public string CreationDisplay { get; set; } = string.Empty;
    public DateTime? LastConsultDate { get; set; }
    public DateTime CreationDate { get; set; }
}
