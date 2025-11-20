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
    
    // Services LLM
    private LLMServiceFactory _llmFactory;
    private LLMWarmupService _warmupService;
    private ILLMService? _currentLLMService;
    
    // ViewModels MVVM (propriété publique pour binding XAML)
    public ViewModels.PatientSearchViewModel PatientSearchViewModel { get; }
    public ViewModels.OrdonnanceViewModel OrdonnanceViewModel { get; }
    public ViewModels.NoteViewModel NoteViewModel { get; }
    public ViewModels.AttestationViewModel AttestationViewModel { get; }

    // Services exposés pour UserControls (Avis IA ordonnance, etc.)
    public OpenAIService OpenAIService => _openAIService;
    public ContextLoader ContextLoader => _contextLoader;
    public PatientIndexService PatientIndex => _patientIndex;
    public List<ChatExchange> ChatHistory => _chatHistory;
    public LetterService LetterService => _letterService;
    public StorageService StorageService => _storageService;
    public PathService PathService => _pathService;
    // Note: AssistantTabControl est déjà public via x:Name dans le XAML

    private PatientIndexEntry? _selectedPatient;

    private string? _currentEditingFilePath;

    // Poids de pertinence de la dernière note structurée (pour mise à jour synthèse)
    private double _lastNoteRelevanceWeight = 0.0;
    
    // Instance unique du dialogue Prompts
    private PromptsAnalysisDialog? _promptsDialog;
    
    // Toggle liste patients
    private bool _isPatientsListVisible = false;
    
    // Référence au Grid parent pour gérer les RowDefinitions dynamiquement
    private Grid? _notesGrid;
    
    // Historique de chat temporaire (mémoire RAM - 3 derniers échanges max)
    private List<ChatExchange> _chatHistory = new();
    private List<ChatExchange> _savedChatExchanges = new();
    
    // Templates personnalisés - données temporaires pour l'extraction
    private string? _currentExtractedTemplate;
    private List<string> _currentExtractedVariables = new();
    private MCCModel? _currentAnalyzedMCC; // ✅ CORRECTION : Stocker le MCC complet avec analyse sémantique
    
    // Modèles de courriers types
    private readonly Dictionary<string, string> _letterTemplates = new()
    {
        ["Demande de PAP à l'établissement scolaire"] = @"# Objet : Demande de Plan d'Accompagnement Personnalisé (PAP) pour {{Nom_Prenom}}

À l'attention de : {{Destinataire}}  
École : {{Ecole}}  
Classe : {{Classe}}

Madame, Monsieur,

**Contexte clinique :**

{{Nom_Prenom}}, âgé(e) de {{Age}} ans, est actuellement suivi(e) en pédopsychiatrie pour {{Trouble_Principal}}.  
Ces difficultés se traduisent par {{Description_Symptomes}} et ont un impact sur ses apprentissages scolaires, sa concentration et/ou son comportement en classe.  
La mise en place d'un Plan d'Accompagnement Personnalisé (PAP) permettrait d'adapter l'environnement scolaire afin de soutenir ses capacités et de prévenir la fatigabilité.

**Objectif de la demande :**

Faciliter la réussite scolaire et le bien-être de {{Prenom}} à travers des ajustements pédagogiques cohérents avec ses besoins spécifiques, tout en favorisant son autonomie et sa confiance.

**Aménagements pédagogiques recommandés :**

1. {{Aménagement_1}}  
2. {{Aménagement_2}}  
3. {{Aménagement_3}}  
4. {{Aménagement_4}}  
5. {{Aménagement_5}}

Ces aménagements visent à compenser les difficultés identifiées, à réduire les sources de surcharge cognitive et émotionnelle, et à renforcer la stabilité du cadre scolaire.  
Ils peuvent être ajustés par l'équipe éducative en concertation avec la famille et les professionnels de santé, selon l'évolution de la situation de {{Prenom}}.

**Durée et suivi :**

Une réévaluation pourra être envisagée dans {{Delai_Reevaluation}} ou à la demande de l'équipe pédagogique en cas d'évolution significative.

Je reste à votre disposition pour tout échange complémentaire ou pour participer à une réunion d'équipe éducative si nécessaire.

Veuillez agréer, Madame, Monsieur, l'expression de ma considération distinguée.",

        ["Feuille de route pour les parents"] = @"# Feuille de route pour les parents de {{Prenom}}

**Motif principal :**

Vous consultez aujourd'hui car {{Prenom}} présente {{Motif_Principal}}.  
L'objectif de cette feuille est de vous donner quelques repères simples pour l'aider au quotidien.

**Axes de travail :**

*L'IA analysera le contexte et proposera 2-3 axes pertinents (Sommeil, Écrans, Émotions, Concentration, Opposition, Autonomie, etc.) avec des conseils concrets et cases à cocher.*

**Message du pédopsy :**

L'important n'est pas de tout faire parfaitement, mais d'observer ce qui aide {{Prenom}} à se sentir mieux.  
Ces conseils sont une première base que nous ajusterons ensemble selon votre vécu.

**Suivi :**

Nous referons le point lors de notre prochain rendez-vous le {{Date_Prochain_RDV}}.",

        ["Demande d'évaluation cardio + ECG"] = @"# Objet : Demande d'évaluation cardiovasculaire pré-thérapeutique

Cher confrère,

Je sollicite votre expertise pour {{Nom_Prenom}}, né(e) le {{Date_Naissance}}, suivi en pédopsychiatrie.

**Contexte clinique :**

L'enfant présente {{Diagnostic}} nécessitant une prise en charge médicamenteuse par {{Medicament}}.

**Demande :**

Avant l'instauration de ce traitement, je sollicite :
- Un **examen cardiovasculaire complet**
- Un **électrocardiogramme (ECG)**
- Votre **avis** sur la compatibilité cardiologique du traitement envisagé

**Antécédents :**

{{Antecedents_Cardio}}

Je vous remercie par avance pour votre collaboration et reste à votre disposition pour tout renseignement complémentaire."
    };

    public MainWindow()
    {
        InitializeComponent();
        
        _settings = new AppSettings();
        _pathService = new PathService();

        // NOUVEAU : Initialiser le tracker de poids pour la synthèse
        _synthesisWeightTracker = new SynthesisWeightTracker(_pathService);

        // IMPORTANT: Initialiser le système LLM de manière synchrone d'abord
        _llmFactory = new LLMServiceFactory(_settings);
        _warmupService = new LLMWarmupService(_llmFactory, _settings);
        
        // Initialisation synchrone minimale pour éviter le null
        _llmFactory.InitializeAsync().Wait();
        _currentLLMService = _llmFactory.GetCurrentProvider();
        _openAIService = new OpenAIService(_llmFactory); // Passer la factory pour changement dynamique
        
        // Maintenant on peut initialiser les services qui dépendent de _openAIService
        _storageService = new StorageService(_pathService);
        _contextLoader = new ContextLoader(_storageService);
        _parsingService = new ParsingService();
        _patientIndex = new PatientIndexService(_pathService);
        _promptConfigService = new PromptConfigService(); // Initialiser AVANT les services qui en dépendent
        _letterService = new LetterService(_openAIService, _contextLoader, _storageService);
        _templateExtractor = new TemplateExtractorService(_openAIService);
        _templateManager = new TemplateManagerService();
        _mccLibrary = new MCCLibraryService();
        _promptReformulationService = new PromptReformulationService(_openAIService);
        _attestationService = new AttestationService(_storageService, _pathService, _letterService, _openAIService, _promptConfigService);
        _formulaireService = new FormulaireAssistantService(_openAIService);
        _ordonnanceService = new OrdonnanceService(_letterService, _storageService, _pathService);
        _synthesisService = new SynthesisService(_openAIService, _storageService, _contextLoader, _pathService, _promptConfigService, _synthesisWeightTracker);
        _letterRatingService = new LetterRatingService();
        _documentService = new DocumentService(_openAIService, _pathService);

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
    System.Diagnostics.Debug.WriteLine("[MainWindow] AttestationListRefreshRequested - Rafraîchissement de l'indicateur de poids");
    NotesControlPanel.UpdateWeightIndicator();
};

// MIGRÉ vers AttestationsControl
// AttestationViewModel.AttestationContentLoaded += (s, content) => {
//     AttestationPreviewText.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(content);
// };

// MIGRÉ vers AttestationsControl
// AttestationViewModel.ErrorOccurred += (s, e) => {
//     MessageBox.Show(e.message, e.title, MessageBoxButton.OK, MessageBoxImage.Error);
// };

// MIGRÉ vers AttestationsControl
// AttestationViewModel.InfoMessageRequested += (s, e) => {
//     MessageBox.Show(e.message, e.title, MessageBoxButton.OK, MessageBoxImage.Information);
// };

// MIGRÉ vers AttestationsControl
// AttestationViewModel.FileOpenRequested += (s, path) => {
//     System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo {
//         FileName = path, UseShellExecute = true
//     });
// };

// MIGRÉ vers AttestationsControl
// AttestationViewModel.AttestationInfoDialogRequested += async (s, dialog) => {
//     dialog.Owner = this;
//     var result = dialog.ShowDialog();
//
//     // ✅ Si le dialogue est validé et que le sexe a été collecté, le sauvegarder
//     if (result == true && dialog.CollectedInfo != null && dialog.CollectedInfo.ContainsKey("Sexe"))
//     {
//         var sexe = dialog.CollectedInfo["Sexe"]; // "H" ou "F"
//
//         // Mettre à jour le patient actuel
//         if (AttestationViewModel.CurrentPatient != null)
//         {
//             AttestationViewModel.CurrentPatient.Sexe = sexe;
//
//             // Sauvegarder dans patient.json
//             try
//             {
//                 var patientDir = _selectedPatient?.DirectoryPath;
//                 if (!string.IsNullOrEmpty(patientDir))
//                 {
//                     var patientJsonPath = System.IO.Path.Combine(patientDir, "patient.json");
//                     var json = System.Text.Json.JsonSerializer.Serialize(AttestationViewModel.CurrentPatient,
//                         new System.Text.Json.JsonSerializerOptions
//                         {
//                             WriteIndented = true,
//                             Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
//                         });
//                     await System.IO.File.WriteAllTextAsync(patientJsonPath, json, System.Text.Encoding.UTF8);
//
//                     StatusTextBlock.Text = $"✅ Sexe enregistré : {(sexe == "F" ? "Féminin" : "Masculin")}";
//                     StatusTextBlock.Foreground = new SolidColorBrush(System.Windows.Media.Colors.Green);
//
//                     // Mettre à jour l'affichage de la carte patient
//                     RenderPatientCard(AttestationViewModel.CurrentPatient);
//                 }
//             }
//             catch (Exception ex)
//             {
//                 StatusTextBlock.Text = $"⚠️ Erreur sauvegarde sexe: {ex.Message}";
//                 StatusTextBlock.Foreground = new SolidColorBrush(System.Windows.Media.Colors.Orange);
//             }
//         }
//     }
// };

// MIGRÉ vers AttestationsControl
// AttestationViewModel.CustomAttestationDialogRequested += (s, dialog) => {
//     dialog.Owner = this;
//     dialog.ShowDialog();
// };

        
        // Connecter les événements NoteViewModel
        NoteViewModel.StatusMessageChanged += OnNoteStatusChanged;
        NoteViewModel.NoteContentLoaded += OnNoteContentLoaded;
        NoteViewModel.NoteStructured += OnNoteStructured;
        NoteViewModel.NoteSaveRequested += OnNoteSaveRequested;
        NoteViewModel.NoteDeleteRequested += OnNoteDeleteRequested;
        NoteViewModel.NotesListRefreshRequested += OnNotesListRefreshRequested;
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
        };
        PatientListControlPanel.StatusChanged += (s, msg) => {
            StatusTextBlock.Text = msg;
            StatusTextBlock.Foreground = new SolidColorBrush(
                msg.StartsWith("✅") ? Colors.Green :
                msg.StartsWith("❌") ? Colors.Red : Colors.Gray);
        };

        // Initialiser NotesControl avec SynthesisService et SynthesisWeightTracker
        NotesControlPanel.Initialize(_synthesisService, _synthesisWeightTracker);
        NotesControlPanel.StatusChanged += (s, msg) => {
            StatusTextBlock.Text = msg;
            StatusTextBlock.Foreground = new SolidColorBrush(
                msg.StartsWith("✅") || msg.StartsWith("✓") ? Colors.Green :
                msg.StartsWith("❌") ? Colors.Red :
                msg.StartsWith("⏳") ? Colors.Blue : Colors.Gray);
        };

        // Initialiser FormulairesControl
        FormulairesControlPanel.Initialize(_formulaireService, _letterService, _patientIndex, _documentService, _pathService);
        FormulairesControlPanel.StatusChanged += (s, msg) => {
            StatusTextBlock.Text = msg;
            StatusTextBlock.Foreground = new SolidColorBrush(
                msg.StartsWith("✅") || msg.StartsWith("✓") ? Colors.Green :
                msg.StartsWith("❌") ? Colors.Red :
                msg.StartsWith("⏳") || msg.StartsWith("⏳") ? Colors.Blue :
                msg.StartsWith("⚠️") ? Colors.Orange : Colors.Gray);
        };

        // Initialiser DocumentsControl
        DocumentsControlPanel.Initialize(_documentService, _pathService, _patientIndex, _synthesisWeightTracker);
        DocumentsControlPanel.StatusChanged += (s, msg) => {
            StatusTextBlock.Text = msg;
            StatusTextBlock.Foreground = new SolidColorBrush(
                msg.StartsWith("✅") || msg.StartsWith("✓") ? Colors.Green :
                msg.StartsWith("❌") ? Colors.Red :
                msg.StartsWith("⏳") ? Colors.Blue :
                msg.StartsWith("⚠️") ? Colors.Orange : Colors.Gray);
        };

        PatientSearchViewModel.PatientSelected += (s, patient) => {
            if (patient != null) LoadPatientAsync(patient);
        };
        PatientSearchViewModel.CreatePatientRequested += (s, query) => {
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
        
        // Charger templates personnalisés et intégrer dans le ComboBox
        LoadCustomTemplates();
        
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
        // NOTE: SearchBox events (GotFocus, LostFocus, Paste) sont maintenant gérés 
        // dans PatientSearchControl.xaml.cs
        
        // OBSOLETE: SearchBox.TextChanged - Remplacé par binding MVVM sur SearchText
        // OBSOLETE: SearchBox.KeyDown - Remplacé par InputBindings XAML (↑↓ Entrée Escape)
        // OBSOLETE: SuggestList event handlers - Remplacés par InputBindings XAML
        // OBSOLETE: ValidateBtn.Click - Remplacé par Command binding XAML
        
        AnalysePromptsBtn.Click += AnalysePromptsBtn_Click;
        OpenPatientFolderBtn.Click += OpenPatientFolderBtn_Click;
        
        // OBSOLETE: NotesList.SelectionChanged - Géré par binding SelectedItem sur NoteViewModel.SelectedNote
        // RichTextBox n'a pas besoin de TextChanged pour activer le bouton
        
        LettersList.SelectionChanged += LettersList_SelectionChanged;
        LettersList.MouseDoubleClick += LettersList_MouseDoubleClick;
        
        ChatInput.KeyDown += ChatInput_KeyDown;
        ChatInput.TextChanged += ChatInput_TextChanged;
        ChatSendBtn.Click += ChatSendBtn_Click;
        
        // Échanges sauvegardés
        SavedExchangesList.SelectionChanged += SavedExchangesList_SelectionChanged;
        ViewSavedExchangeBtn.Click += ViewSavedExchangeBtn_Click;
        DeleteSavedExchangeBtn.Click += DeleteSavedExchangeBtn_Click;
        
        LetterEditText.TextChanged += LetterEditText_TextChanged;
        ModifierLetterButton.Click += ModifierLetterButton_Click;
        SupprimerLetterButton.Click += SupprimerLetterButton_Click;
        SauvegarderLetterButton.Click += SauvegarderLetterButton_Click;
        ImprimerLetterButton.Click += ImprimerLetterButton_Click;
        
        TemplateLetterCombo.SelectionChanged += TemplateLetterCombo_SelectionChanged;
        
        // Templates personnalisés
        AnalyzeLetterBtn.Click += AnalyzeLetterBtn_Click;
        SaveTemplateBtn.Click += SaveTemplateBtn_Click;
        // PreviewTemplateBtn, EditTemplateBtn, DeleteTemplateBtn - SUPPRIMÉS (ancien système de templates)

        // Attestations - MIGRÉ vers AttestationsControl
        // AttestationTypeCombo.SelectionChanged += AttestationTypeCombo_SelectionChanged;
        // GenererAttestationButton.Click += GenererAttestationButton_Click;
        // AttestationsList.SelectionChanged += AttestationsList_SelectionChanged;
        // AttestationsList.MouseDoubleClick += AttestationsList_MouseDoubleClick;
        // ModifierAttestationButton.Click += ModifierAttestationButton_Click;
        // SupprimerAttestationButton.Click += SupprimerAttestationButton_Click;
        // ImprimerAttestationButton.Click += ImprimerAttestationButton_Click;
        
        // Formulaires - MIGRÉ vers FormulairesControl (géré en interne par le UserControl)
        // FormulaireTypeCombo.SelectionChanged est maintenant géré dans FormulairesControl.xaml.cs
        // PreremplirFormulaireButton.Click est maintenant géré dans FormulairesControl.xaml.cs

        // Synthèse - MIGRÉ vers NotesControl (géré en interne par le UserControl)
        // NotesControlPanel.GenerateSynthesisBtn.Click est maintenant géré dans NotesControl.xaml.cs
    }
    

    
    private void ResetPatientUI()
    {
        // Réinitialiser le fichier en cours d'édition
        _currentEditingFilePath = null;
        
        // RESET MÉMOIRE CHAT
        _chatHistory.Clear();
        _savedChatExchanges.Clear();
        
        // Reset le ViewModel de Note
        NoteViewModel.Reset();
        
        // Vider les champs de texte
        NotesControlPanel.RawNoteTextBox.Text = string.Empty;
        NotesControlPanel.StructuredNoteTextBox.Document = new FlowDocument();
        ChatInput.Text = string.Empty;
        
        // Vider le chat
        ChatList.Children.Clear();
        
        // Note: NotesList.ItemsSource sera géré automatiquement par le binding sur NoteViewModel.Notes
        
        // NE PAS contrôler manuellement la visibilité - le binding MVVM s'en charge via NoteViewModel.Reset() !
        
        // Remettre zone structurée en readonly
        NotesControlPanel.StructuredNoteTextBox.IsReadOnly = true;
        NotesControlPanel.StructuredNoteTextBox.Background = new SolidColorBrush(Color.FromRgb(250, 250, 250));
        
        // Désélectionner tout élément dans la liste des courriers (IMPORTANT: avant de vider)
        LettersList.SelectedItem = null;
        LettersList.SelectedIndex = -1;
        
        // Vider la liste des courriers
        LettersList.ItemsSource = null;
        
        // IMPORTANT: Réinitialiser complètement la zone courrier ET le ComboBox
        try
        {
            // Créer un tout nouveau FlowDocument vide
            var emptyDoc = new FlowDocument();
            LetterEditText.Document = emptyDoc;
            LetterEditText.IsReadOnly = true;
            LetterEditText.Background = new SolidColorBrush(Color.FromRgb(250, 250, 250));
        }
        catch
        {
            // En cas d'erreur, forcer la réinitialisation
            LetterEditText.Document = null;
            LetterEditText.Document = new FlowDocument();
        }
        
        // Réinitialiser les boutons courrier
        ModifierLetterButton.Visibility = Visibility.Collapsed;
        SupprimerLetterButton.Visibility = Visibility.Collapsed;
        SauvegarderLetterButton.IsEnabled = false;
        ImprimerLetterButton.Visibility = Visibility.Collapsed;
        
        // Réinitialiser le sélecteur de modèles de courrier
        TemplateLetterCombo.SelectedIndex = 0;
        
        // ===== RÉINITIALISER LA SECTION ATTESTATIONS =====
        
        // Attestations - Le reset est maintenant géré par AttestationViewModel
        // Les contrôles ont été migrés vers AttestationsControl
        // AttestationsList.SelectedItem = null;
        // AttestationsList.SelectedIndex = -1;
        
        // Masquer les boutons des échanges sauvegardés
        ViewSavedExchangeBtn.Visibility = Visibility.Collapsed;
        LetterFromChatBtn.Visibility = Visibility.Collapsed;
        DeleteSavedExchangeBtn.Visibility = Visibility.Collapsed;
        
        // Message de bienvenue dans le chat pour le nouveau patient
        AddChatMessage("Système", "💬 Nouvelle conversation démarrée. Posez vos questions sur ce patient.", Colors.Gray);
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

    // OBSOLETE: RefreshNotesList - Le binding sur NoteViewModel.Notes se met à jour automatiquement
    // Cette méthode n'est plus nécessaire avec MVVM
    
    // OBSOLETE: NotesList_SelectionChanged - Géré par binding SelectedItem sur NoteViewModel.SelectedNote
    // Les events OnNoteContentLoaded, OnNoteStatusChanged, etc. gèrent l'affichage
    
    /// <summary>
    /// Nettoie le YAML d'un contenu Markdown (retire le bloc --- ... ---)
    /// </summary>
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
    
   
    
    // ===== COURRIERS =====
    
   
    
   
    
    
    // ===== HANDLERS COURRIERS DÉDIÉS =====
    
   
    
   
    
   


    public void AddChatMessage(string author, string message, Color color, string? exchangeId = null)
    {
        // Vérification de sécurité
        if (ChatList == null || ChatScrollViewer == null)
        {
            System.Diagnostics.Debug.WriteLine($"[WARNING] ChatList or ChatScrollViewer is null. Message: {author}: {message}");
            return;
        }
        
        // Créer un Grid pour contenir le message + bouton sauvegarder
        var messageGrid = new Grid();
        messageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        messageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        
        // DIFFÉRENCIER messages IA (formatage riche) vs messages utilisateur (texte simple)
        if (author == "IA" || author == "📖 IA (archivé)")
        {
            // Messages IA → RichTextBox avec formatage Markdown
            var richTextBox = new RichTextBox
            {
                IsReadOnly = false,
                BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                Padding = new Thickness(8),
                FontFamily = new FontFamily("Segoe UI, Arial"),
                FontSize = 12,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            
            try
            {
                // Convertir Markdown en FlowDocument formaté
                richTextBox.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(message);
            }
            catch
            {
                // En cas d'erreur, afficher en texte brut
                var doc = new FlowDocument();
                doc.Blocks.Add(new Paragraph(new Run(message)));
                richTextBox.Document = doc;
            }
            
            Grid.SetColumn(richTextBox, 0);
            messageGrid.Children.Add(richTextBox);
        }
        else
        {
            // Messages utilisateur/système → TextBox éditable (comportement actuel)
            var messageBox = new TextBox
            {
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(8),
                FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI, Arial"),
                IsReadOnly = false, // ÉDITABLE
                AcceptsReturn = true, // Permet les retours à la ligne
                BorderThickness = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontSize = 12
            };

            // Construire le texte complet avec en-tête et message
            var fullText = $"{author}\n{message}";
            messageBox.Text = fullText;
            
            // Stocker l'auteur et la couleur dans le Tag pour formatage ultérieur si nécessaire
            messageBox.Tag = new { Author = author, Color = color };
            
            Grid.SetColumn(messageBox, 0);
            messageGrid.Children.Add(messageBox);
        }
        
        // Ajouter bouton "💾" seulement pour les messages IA
        if (author == "IA" && _chatHistory.Count > 0)
        {
            var saveButton = new Button
            {
                Content = "💾",
                Width = 30,
                Height = 30,
                Margin = new Thickness(5, 5, 5, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Background = new SolidColorBrush(Color.FromRgb(52, 152, 219)),
                Foreground = new SolidColorBrush(Colors.White),
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                ToolTip = "Sauvegarder cet échange"
            };
            
            // Stocker l'index de l'échange dans le Tag
            var exchangeIndex = _chatHistory.Count - 1;
            saveButton.Tag = exchangeIndex;
            saveButton.Click += SaveExchangeButton_Click;
            
            Grid.SetColumn(saveButton, 1);
            messageGrid.Children.Add(saveButton);
        }

        var border = new Border
        {
            Child = messageGrid,
            Margin = new Thickness(0, 0, 0, 10),
            Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)),
            BorderBrush = new SolidColorBrush(color),
            BorderThickness = new Thickness(2, 0, 0, 0)
        };
        
        // IMPORTANT: Stocker l'ID de l'échange dans le Tag du Border pour le retrouver plus tard
        if (!string.IsNullOrEmpty(exchangeId))
        {
            border.Tag = exchangeId;
        }

        ChatList.Children.Add(border);
        ChatScrollViewer.ScrollToEnd();
    }

    /// <summary>
    /// Parse le Markdown et ajoute les Inlines formatés au TextBlock
    /// </summary>
    private void ParseMarkdownToInlines(string text, TextBlock textBlock, Color defaultColor)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // Ligne vide → Saut de ligne
            if (string.IsNullOrWhiteSpace(line))
            {
                textBlock.Inlines.Add(new LineBreak());
                continue;
            }

            // Titre H1 (# Titre)
            if (line.StartsWith("# "))
            {
                var titleText = line.Substring(2).Trim();
                textBlock.Inlines.Add(new Run(titleText)
                {
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(44, 62, 80))
                });
                textBlock.Inlines.Add(new LineBreak());
                continue;
            }

            // Titre H2 (## Sous-titre)
            if (line.StartsWith("## "))
            {
                var subtitleText = line.Substring(3).Trim();
                textBlock.Inlines.Add(new Run(subtitleText)
                {
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(52, 73, 94))
                });
                textBlock.Inlines.Add(new LineBreak());
                continue;
            }

            // Titre H3 (### Sous-sous-titre)
            if (line.StartsWith("### ") && !line.StartsWith("#### "))
            {
                var h3Text = line.Substring(4).Trim();
                textBlock.Inlines.Add(new Run(h3Text)
                {
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(52, 73, 94))
                });
                textBlock.Inlines.Add(new LineBreak());
                continue;
            }

            // Titre H4 (#### Sous-sous-sous-titre)
            if (line.StartsWith("#### "))
            {
                var h4Text = line.Substring(5).Trim();
                textBlock.Inlines.Add(new Run(h4Text)
                {
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(52, 73, 94))
                });
                textBlock.Inlines.Add(new LineBreak());
                continue;
            }

            // Liste à puces (- Item ou * Item)
            if (line.TrimStart().StartsWith("- ") || line.TrimStart().StartsWith("* "))
            {
                var indent = line.Length - line.TrimStart().Length;
                var bulletText = line.TrimStart().Substring(2);

                // Indentation
                if (indent > 0)
                {
                    textBlock.Inlines.Add(new Run(new string(' ', indent)));
                }

                // Puce
                textBlock.Inlines.Add(new Run("• ")
                {
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(52, 152, 219))
                });

                // Texte de la puce avec styles inline
                ParseInlineStyles(bulletText, textBlock, defaultColor);
                textBlock.Inlines.Add(new LineBreak());
                continue;
            }

            // Ligne de séparation (---)
            if (line.Trim() == "---" || line.Trim().StartsWith("━━━"))
            {
                textBlock.Inlines.Add(new Run(line)
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(189, 195, 199))
                });
                textBlock.Inlines.Add(new LineBreak());
                continue;
            }

            // Paragraphe normal avec styles inline
            ParseInlineStyles(line, textBlock, defaultColor);
            
            // Ajouter un saut de ligne sauf pour la dernière ligne
            if (i < lines.Length - 1)
            {
                textBlock.Inlines.Add(new LineBreak());
            }
        }
    }

    /// <summary>
    /// Parse les styles inline (**gras**, *italique*, `code`) et ajoute les Runs au TextBlock
    /// </summary>
    private void ParseInlineStyles(string text, TextBlock textBlock, Color defaultColor)
    {
        // Pattern pour capturer: **gras**, *italique*, `code`
        var pattern = @"(\*\*[^*]+\*\*)|(\*[^*]+\*)|(`[^`]+`)";
        var regex = new Regex(pattern);

        int lastIndex = 0;

        foreach (Match match in regex.Matches(text))
        {
            // Texte avant le match (normal)
            if (match.Index > lastIndex)
            {
                var normalText = text.Substring(lastIndex, match.Index - lastIndex);
                textBlock.Inlines.Add(new Run(normalText)
                {
                    FontSize = 12,
                    Foreground = new SolidColorBrush(defaultColor)
                });
            }

            // Texte avec style
            var matchedText = match.Value;

            if (matchedText.StartsWith("**") && matchedText.EndsWith("**"))
            {
                // Gras
                var boldText = matchedText.Substring(2, matchedText.Length - 4);
                textBlock.Inlines.Add(new Run(boldText)
                {
                    FontWeight = FontWeights.Bold,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(defaultColor)
                });
            }
            else if (matchedText.StartsWith("*") && matchedText.EndsWith("*"))
            {
                // Italique
                var italicText = matchedText.Substring(1, matchedText.Length - 2);
                textBlock.Inlines.Add(new Run(italicText)
                {
                    FontStyle = FontStyles.Italic,
                    FontSize = 12,
                    Foreground = new SolidColorBrush(defaultColor)
                });
            }
            else if (matchedText.StartsWith("`") && matchedText.EndsWith("`"))
            {
                // Code inline
                var codeText = matchedText.Substring(1, matchedText.Length - 2);
                textBlock.Inlines.Add(new Run(codeText)
                {
                    FontFamily = new FontFamily("Consolas, Courier New"),
                    FontSize = 11,
                    Background = new SolidColorBrush(Color.FromRgb(240, 240, 240)),
                    Foreground = new SolidColorBrush(Color.FromRgb(199, 37, 78))
                });
            }

            lastIndex = match.Index + match.Length;
        }

        // Texte restant après le dernier match (ou tout le texte s'il n'y a pas de match)
        if (lastIndex < text.Length)
        {
            var remainingText = text.Substring(lastIndex);
            textBlock.Inlines.Add(new Run(remainingText)
            {
                FontSize = 12,
                Foreground = new SolidColorBrush(defaultColor)
            });
        }
    }
    
    // ===== TEMPLATES PERSONNALISÉS =====
    
    /// <summary>
    /// Ouvre le dialogue de bibliothèque MCC pour explorer et sélectionner des templates
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
                // Un MCC a été sélectionné depuis la bibliothèque
                var selectedMCC = dialog.SelectedMCC;

                StatusTextBlock.Text = $"⏳ Génération du courrier depuis MCC '{selectedMCC.Name}'...";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);

                // IMPORTANT: Clear any existing letter selection to prevent overwriting
                LettersList.SelectedItem = null;
                _currentEditingFilePath = null;

                // Générer le courrier avec l'IA en utilisant toutes les métadonnées du MCC
                var (success, markdown, error) = await _letterService.GenerateLetterFromMCCAsync(
                    _selectedPatient.NomComplet,
                    selectedMCC
                );

                if (success && !string.IsNullOrEmpty(markdown))
                {
                    // Basculer vers l'onglet Courriers
                    AssistantTabControl.SelectedIndex = 1;

                    // IMPORTANT: Réinitialiser pour forcer création nouveau fichier
                    _currentEditingFilePath = null;
                    
                    // Afficher le brouillon dans la zone courrier
                    LetterEditText.IsReadOnly = false;
                    LetterEditText.Background = new SolidColorBrush(Colors.White);
                    LetterEditText.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(markdown);
                    
                    // Activer ET rendre visible le bouton sauvegarder
                    ModifierLetterButton.Visibility = Visibility.Collapsed;
                    SupprimerLetterButton.Visibility = Visibility.Collapsed;
                    AnnulerLetterButton.Visibility = Visibility.Collapsed;
                    SauvegarderLetterButton.Visibility = Visibility.Visible;
                    SauvegarderLetterButton.IsEnabled = true;
                    SauvegarderLetterButton.Background = new SolidColorBrush(Color.FromRgb(39, 174, 96)); // Vert
                    ImprimerLetterButton.Visibility = Visibility.Collapsed;
                    
                    // Incrémenter le compteur d'utilisation du MCC
                    _mccLibrary.IncrementUsage(selectedMCC.Id);
                    
                    // Détecter les placeholders manquants
                    var patientMetadata = _patientIndex.GetMetadata(_selectedPatient.Id);
                    var (hasMissing, missingFields, availableInfo) = _letterService.DetectMissingInfo(
                        selectedMCC.Name,
                        markdown,
                        patientMetadata,
                        selectedMCC.TemplateMarkdown // Passer le template original pour double détection
                    );
                    
                    // Si des placeholders sont détectés → Ouvrir dialogue (requis OU optionnels)
                    if (hasMissing)
                    {
                        StatusTextBlock.Text = "❓ Informations requises manquantes...";
                        StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
                        
                        var missingDialog = new MissingInfoDialog(missingFields);
                        missingDialog.Owner = this;
                        
                        if (missingDialog.ShowDialog() == true && missingDialog.CollectedInfo != null)
                        {
                            // FUSIONNER infos disponibles + infos collectées
                            var allInfo = new Dictionary<string, string>(availableInfo);
                            foreach (var kvp in missingDialog.CollectedInfo)
                            {
                                allInfo[kvp.Key] = kvp.Value;
                            }
                            
                            // RÉ-ADAPTER LE COURRIER avec l'IA
                            StatusTextBlock.Text = "⏳ Ré-adaptation avec infos complètes...";
                            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);
                            
                            var (success2, updatedMarkdown, error2) =
                                await _letterService.AdaptTemplateWithMissingInfoAsync(
                                    _selectedPatient.NomComplet,
                                    selectedMCC.Name,
                                    markdown,
                                    allInfo
                                );
                            
                            if (success2 && !string.IsNullOrEmpty(updatedMarkdown))
                            {
                                // Mettre à jour l'affichage avec le markdown ré-adapté
                                LetterEditText.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(updatedMarkdown);
                                
                                StatusTextBlock.Text = "✅ Courrier MCC complété - Vous pouvez sauvegarder";
                                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
                            }
                            else
                            {
                                StatusTextBlock.Text = $"⚠️ Erreur ré-adaptation: {error2}";
                                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
                            }
                        }
                        else
                        {
                            StatusTextBlock.Text = "⚠️ Complétez les placeholders manuellement";
                            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
                        }
                    }
                    else
                    {
                        StatusTextBlock.Text = $"✅ Courrier généré depuis MCC '{selectedMCC.Name}' - Type: {selectedMCC.Semantic?.DocType}, Ton: {selectedMCC.Semantic?.Tone}";
                        StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
                    }
                    
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
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors de l'ouverture de la bibliothèque:\n\n{ex.Message}", 
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            
            StatusTextBlock.Text = $"❌ Erreur: {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
        }
    }
    
    
    // ===== ATTESTATIONS =====
    // Les handlers d'attestations sont dans MainWindow.Documents.cs
    
    // ===== DOCUMENTS =====

    private DocumentService _documentService;

#if false // LEGACY CODE - MIGRÉ vers DocumentsControl (Views/Documents/DocumentsControl.xaml.cs)
    private List<PatientDocument> _allDocuments = new();
    private string _currentDocumentFilter = "all";
    private PatientDocument? _currentSynthesizedDocument;
    private string? _currentSynthesisPath;
    
    /// <summary>
    /// Charge tous les documents du patient dans l'onglet Documents intégré
    /// </summary>
    private async void LoadPatientDocuments()
    {
        if (_selectedPatient == null)
        {
            DocumentsDataGrid.ItemsSource = null;
            DocCountLabel.Text = "Aucun document";
            return;
        }
        
        try
        {
            if (_documentService == null)
            {
                _documentService = new DocumentService(_openAIService, _pathService);
            }
            
            _allDocuments = await _documentService.GetAllDocumentsAsync(_selectedPatient.NomComplet);
            ApplyDocumentFilter(_currentDocumentFilter);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors du chargement des documents: {ex.Message}", 
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private void ApplyDocumentFilter(string filter)
    {
        _currentDocumentFilter = filter;
        
        var filtered = filter == "all" 
            ? _allDocuments 
            : _allDocuments.Where(d => d.Category == filter).ToList();
        
        if (DocumentsDataGrid != null)
        {
            DocumentsDataGrid.ItemsSource = filtered;
        }
        
        if (DocCountLabel != null)
        {
            var count = filtered.Count;
            DocCountLabel.Text = count == 0 ? "Aucun document" :
                                 count == 1 ? "1 document" :
                                 $"{count} documents";
        }
    }
    
    private void DocCategoriesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DocCategoriesListBox.SelectedItem is ListBoxItem item && item.Tag is string tag)
        {
            ApplyDocumentFilter(tag);
        }
    }
    
    #region Drag & Drop Documents
    
    private void DocDropZone_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            DocDropZone.Background = new SolidColorBrush(Color.FromRgb(187, 222, 251));
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }
    
    private void DocDropZone_DragLeave(object sender, DragEventArgs e)
    {
        DocDropZone.Background = new SolidColorBrush(Color.FromRgb(227, 242, 253));
    }
    
    private async void DocDropZone_Drop(object sender, DragEventArgs e)
    {
        DocDropZone.Background = new SolidColorBrush(Color.FromRgb(227, 242, 253));
        
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            await ProcessDocumentFilesAsync(files);
        }
    }
    
    #endregion
    
    #region Browse Files Documents
    
    private async void DocBrowseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPatient == null)
        {
            MessageBox.Show("Veuillez d'abord sélectionner un patient.", "Information",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        var openFileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Sélectionner des documents",
            Filter = "Tous les documents|*.pdf;*.docx;*.doc;*.jpg;*.jpeg;*.png;*.txt|" +
                    "PDF|*.pdf|" +
                    "Word|*.docx;*.doc|" +
                    "Images|*.jpg;*.jpeg;*.png|" +
                    "Texte|*.txt",
            Multiselect = true
        };
        
        if (openFileDialog.ShowDialog() == true)
        {
            await ProcessDocumentFilesAsync(openFileDialog.FileNames);
        }
    }
    
    #endregion
    
    #region Process Files Documents
    
    private async Task ProcessDocumentFilesAsync(string[] filePaths)
    {
        if (filePaths == null || filePaths.Length == 0 || _selectedPatient == null)
            return;
        
        var progressWindow = new Window
        {
            Title = "Import en cours...",
            Width = 400,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow
        };
        
        var progressPanel = new StackPanel { Margin = new Thickness(20) };
        var progressText = new TextBlock 
        { 
            Text = "Traitement des documents...",
            FontSize = 14,
            Margin = new Thickness(0, 0, 0, 10)
        };
        var progressBar = new ProgressBar 
        { 
            Height = 25,
            IsIndeterminate = true
        };
        var detailText = new TextBlock 
        { 
            Text = "",
            FontSize = 12,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 10, 0, 0)
        };
        
        progressPanel.Children.Add(progressText);
        progressPanel.Children.Add(progressBar);
        progressPanel.Children.Add(detailText);
        progressWindow.Content = progressPanel;
        
        progressWindow.Show();
        
        int successCount = 0;
        int errorCount = 0;
        var errors = new List<string>();
        
        for (int i = 0; i < filePaths.Length; i++)
        {
            var filePath = filePaths[i];
            var fileName = Path.GetFileName(filePath);
            
            detailText.Text = $"Traitement: {fileName} ({i + 1}/{filePaths.Length})";
            await Task.Delay(100);
            
            try
            {
                var (success, document, message) = await _documentService.ImportDocumentAsync(
                    filePath, _selectedPatient.DirectoryPath);
                
                if (success)
                {
                    successCount++;
                }
                else
                {
                    errorCount++;
                    errors.Add($"{fileName}: {message}");
                }
            }
            catch (Exception ex)
            {
                errorCount++;
                errors.Add($"{fileName}: {ex.Message}");
            }
        }
        
        progressWindow.Close();
        
        // Recharger la liste
        LoadPatientDocuments();
        
        // Afficher le résumé
        var summary = $"✅ {successCount} document(s) importé(s)";
        if (errorCount > 0)
        {
            summary += $"\n❌ {errorCount} erreur(s)";
            if (errors.Any())
            {
                summary += "\n\nDétails:\n" + string.Join("\n", errors.Take(5));
                if (errors.Count > 5)
                    summary += $"\n... et {errors.Count - 5} autre(s)";
            }
        }
        
        MessageBox.Show(summary, "Import terminé", 
            MessageBoxButton.OK, 
            errorCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }
    
    #endregion
    
    #region Document Actions
    
    private void DocumentsDataGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DocumentsDataGrid.SelectedItem is PatientDocument document)
        {
            OpenDocument(document);
        }
    }
    
    private async void DocumentsDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var isDocumentSelected = DocumentsDataGrid.SelectedItem != null;
        
        if (DeleteDocumentButton != null)
        {
            DeleteDocumentButton.IsEnabled = isDocumentSelected;
        }
        
        // Reset de l'aperçu et des boutons lors du changement de sélection
        if (isDocumentSelected && DocumentsDataGrid.SelectedItem is PatientDocument selectedDocument)
        {
            await UpdateDocumentSynthesisState(selectedDocument);
        }
        else
        {
            // Aucun document sélectionné → Reset complet
            ResetSynthesisPreview();
            if (DocSynthesisButton != null)
            {
                DocSynthesisButton.IsEnabled = false;
            }
        }
    }
    
    /// <summary>
    /// Met à jour l'état de la zone de synthèse en fonction du document sélectionné
    /// </summary>
    private async Task UpdateDocumentSynthesisState(PatientDocument document)
    {
        if (_selectedPatient == null || _documentService == null)
            return;
        
        try
        {
            // Vérifier si une synthèse existe déjà
            var (exists, synthesisPath) = _documentService.GetExistingSynthesis(document, _selectedPatient.NomComplet);
            
            if (exists && !string.IsNullOrEmpty(synthesisPath))
            {
                // SYNTHÈSE EXISTE → Charger automatiquement
                var synthesisContent = await _documentService.LoadSynthesisContentAsync(synthesisPath);
                
                // Convertir Markdown en FlowDocument formaté
                try
                {
                    DocSynthesisPreview.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(synthesisContent);
                }
                catch
                {
                    // En cas d'erreur, afficher texte brut
                    var doc = new FlowDocument();
                    doc.Blocks.Add(new Paragraph(new Run(synthesisContent)));
                    DocSynthesisPreview.Document = doc;
                }
                
                // Stocker le document et le chemin pour suppression
                _currentSynthesizedDocument = document;
                _currentSynthesisPath = synthesisPath;
                
                // Bouton Synthèse INACTIF (gris)
                DocSynthesisButton.IsEnabled = false;
                DocSynthesisButton.Background = new SolidColorBrush(Color.FromRgb(189, 195, 199)); // Gris
                
                // Afficher bouton Supprimer, masquer Enregistrer
                DeleteSynthesisBtn.Visibility = Visibility.Visible;
                SaveSynthesisBtn.Visibility = Visibility.Collapsed;
                CloseSynthesisPreviewBtn.Visibility = Visibility.Visible;
                
                StatusTextBlock.Text = $"✓ Synthèse chargée depuis {Path.GetFileName(synthesisPath)}";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);
            }
            else
            {
                // PAS DE SYNTHÈSE → État par défaut
                ResetSynthesisPreview();
                
                // Bouton Synthèse ACTIF (vert)
                DocSynthesisButton.IsEnabled = true;
                DocSynthesisButton.Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Vert
                
                _currentSynthesizedDocument = null;
                _currentSynthesisPath = null;
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"❌ Erreur vérification synthèse: {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
        }
    }
    
    /// <summary>
    /// Réinitialise la zone d'aperçu de synthèse
    /// </summary>
    private void ResetSynthesisPreview()
    {
        // Créer un FlowDocument avec un message par défaut
        var doc = new FlowDocument();
        var paragraph = new Paragraph(new Run("Sélectionnez un document et cliquez sur 'Synthèse' pour voir l'aperçu..."))
        {
            Foreground = new SolidColorBrush(Color.FromRgb(127, 140, 141)), // Gris
            FontSize = 13
        };
        doc.Blocks.Add(paragraph);
        DocSynthesisPreview.Document = doc;
        
        DeleteSynthesisBtn.Visibility = Visibility.Collapsed;
        SaveSynthesisBtn.Visibility = Visibility.Collapsed;
        CloseSynthesisPreviewBtn.Visibility = Visibility.Collapsed;
    }
    
    private void OpenDocument(PatientDocument document)
    {
        try
        {
            if (File.Exists(document.FilePath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = document.FilePath,
                    UseShellExecute = true
                });
            }
            else
            {
                MessageBox.Show("Le fichier n'existe plus à cet emplacement.", 
                    "Fichier introuvable", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Impossible d'ouvrir le document: {ex.Message}", 
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private async void DeleteDocumentButton_Click(object sender, RoutedEventArgs e)
    {
        if (DocumentsDataGrid.SelectedItem is not PatientDocument document)
        {
            MessageBox.Show("Veuillez sélectionner un document à supprimer.", 
                "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        if (_selectedPatient == null || _documentService == null)
        {
            MessageBox.Show("Erreur: Patient ou service non initialisé.", 
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        
        var result = MessageBox.Show(
            $"Êtes-vous sûr de vouloir supprimer ce document ?\n\n" +
            $"Nom: {document.FileName}\n" +
            $"Catégorie: {document.Category}\n" +
            $"Date: {document.DateAddedDisplay}\n\n" +
            $"Cette action est irréversible.",
            "Confirmer la suppression",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );
        
        if (result != MessageBoxResult.Yes)
            return;
        
        try
        {
            StatusTextBlock.Text = "⏳ Suppression en cours...";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
            
            // Utiliser la méthode du service
            var (success, message) = await _documentService.DeleteDocumentAsync(document, _selectedPatient.NomComplet);
            
            if (success)
            {
                // Recharger la liste des documents
                LoadPatientDocuments();
                
                MessageBox.Show("✅ Document supprimé avec succès.", "Succès", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                
                StatusTextBlock.Text = "✅ Document supprimé";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
            }
            else
            {
                MessageBox.Show($"❌ {message}", "Erreur", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
                
                StatusTextBlock.Text = $"❌ {message}";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors de la suppression:\n\n{ex.Message}", 
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            
            StatusTextBlock.Text = $"❌ Erreur: {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
        }
    }
    
    private async void DocSynthesisButton_Click(object sender, RoutedEventArgs e)
    {
        if (DocumentsDataGrid.SelectedItem is not PatientDocument selectedDocument)
        {
            MessageBox.Show("Veuillez sélectionner un document pour générer sa synthèse.", 
                "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        try
        {
            // Afficher message de chargement dans la zone d'aperçu
            var loadingDoc = new FlowDocument();
            var loadingPara = new Paragraph(new Run($"⏳ Génération de la synthèse en cours...\n\nAnalyse du document: {selectedDocument.FileName}"))
            {
                Foreground = new SolidColorBrush(Color.FromRgb(52, 152, 219)), // Bleu
                FontSize = 13
            };
            loadingDoc.Blocks.Add(loadingPara);
            DocSynthesisPreview.Document = loadingDoc;
            
            // Masquer le bouton Enregistrer pendant la génération
            SaveSynthesisBtn.Visibility = Visibility.Collapsed;
            CloseSynthesisPreviewBtn.Visibility = Visibility.Visible;
            
            StatusTextBlock.Text = "⏳ Génération de la synthèse en cours...";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);
            
            // Générer la synthèse avec extraction du poids
            var (synthesis, relevanceWeight) = await _documentService.GenerateSingleDocumentSynthesisAsync(selectedDocument);

            // Stocker le document, la synthèse et le poids
            _currentSynthesizedDocument = selectedDocument;
            _lastDocumentSynthesisWeight = relevanceWeight;
            
            // Afficher la synthèse dans la zone d'aperçu avec formatage
            try
            {
                DocSynthesisPreview.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(synthesis);
            }
            catch
            {
                // En cas d'erreur de conversion, afficher texte brut
                var doc = new FlowDocument();
                doc.Blocks.Add(new Paragraph(new Run(synthesis)));
                DocSynthesisPreview.Document = doc;
            }
            
            // Afficher ET activer le bouton Enregistrer
            SaveSynthesisBtn.Visibility = Visibility.Visible;
            SaveSynthesisBtn.IsEnabled = true;
            
            StatusTextBlock.Text = "✅ Synthèse générée avec succès";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
        }
        catch (Exception ex)
        {
            _currentSynthesizedDocument = null;
            var errorDoc = new FlowDocument();
            var errorPara = new Paragraph(new Run($"❌ Erreur lors de la génération:\n\n{ex.Message}"))
            {
                Foreground = new SolidColorBrush(Colors.Red)
            };
            errorDoc.Blocks.Add(errorPara);
            DocSynthesisPreview.Document = errorDoc;
            
            StatusTextBlock.Text = $"❌ Erreur: {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
        }
    }
    
    private void SaveSynthesisBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSynthesizedDocument == null || _selectedPatient == null)
        {
            MessageBox.Show("Aucune synthèse à sauvegarder.", "Information",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        try
        {
            // CORRECTION: Utiliser PathService pour obtenir le bon chemin (2025/documents/)
            var documentsDir = _pathService.GetDocumentsDirectory(_selectedPatient.NomComplet);
            var syntheseDir = Path.Combine(documentsDir, "syntheses_documents");
            
            if (!Directory.Exists(syntheseDir))
            {
                Directory.CreateDirectory(syntheseDir);
            }
            
            // Générer le nom du fichier
            var originalFileName = Path.GetFileNameWithoutExtension(_currentSynthesizedDocument.FileName);
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var syntheseFileName = $"{originalFileName}_synthese_{timestamp}.md";
            var synthesePath = Path.Combine(syntheseDir, syntheseFileName);
            
            // Extraire le texte Markdown du FlowDocument
            var synthesisMarkdown = MarkdownFlowDocumentConverter.FlowDocumentToMarkdown(DocSynthesisPreview.Document);
            
            // Créer le contenu avec métadonnées
            var syntheseContent = $@"---
document_original: {_currentSynthesizedDocument.FileName}
date_synthese: {DateTime.Now:dd/MM/yyyy HH:mm:ss}
patient: {_selectedPatient.NomComplet}
---

{synthesisMarkdown}
";
            
            // Sauvegarder
            File.WriteAllText(synthesePath, syntheseContent, Encoding.UTF8);
            
            // Message de succès
            MessageBox.Show(
                $"✅ Synthèse sauvegardée avec succès !\n\n" +
                $"Fichier : {syntheseFileName}\n" +
                $"Emplacement : documents/syntheses/",
                "Sauvegarde réussie",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
            
            StatusTextBlock.Text = $"✅ Synthèse sauvegardée : {syntheseFileName}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
            
            // Désactiver le bouton Enregistrer (déjà sauvegardé)
            SaveSynthesisBtn.IsEnabled = false;
            SaveSynthesisBtn.Background = new SolidColorBrush(Color.FromRgb(189, 195, 199)); // Gris
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors de la sauvegarde:\n\n{ex.Message}", 
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            
            StatusTextBlock.Text = $"❌ Erreur sauvegarde: {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
        }
    }
    
    private void CloseSynthesisPreviewBtn_Click(object sender, RoutedEventArgs e)
    {
        // Masquer les boutons et réinitialiser l'aperçu
        ResetSynthesisPreview();
    }
    
    private void DeleteSynthesisBtn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentSynthesisPath) || _currentSynthesizedDocument == null)
        {
            MessageBox.Show("Aucune synthèse à supprimer.", "Information",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        
        var result = MessageBox.Show(
            $"Êtes-vous sûr de vouloir supprimer cette synthèse ?\n\n" +
            $"Document : {_currentSynthesizedDocument.FileName}\n" +
            $"Fichier : {Path.GetFileName(_currentSynthesisPath)}",
            "Confirmer la suppression",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );
        
        if (result != MessageBoxResult.Yes)
            return;
        
        try
        {
            var (success, message) = _documentService.DeleteSynthesis(_currentSynthesisPath);
            
            if (success)
            {
                // Réinitialiser l'aperçu
                ResetSynthesisPreview();
                
                // Réactiver le bouton Synthèse (vert)
                if (DocSynthesisButton != null)
                {
                    DocSynthesisButton.IsEnabled = true;
                    DocSynthesisButton.Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Vert
                }
                
                // Réinitialiser les variables
                _currentSynthesizedDocument = null;
                _currentSynthesisPath = null;
                
                MessageBox.Show("✅ Synthèse supprimée avec succès.", "Succès",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                
                StatusTextBlock.Text = "✅ Synthèse supprimée - Le bouton Synthèse est à nouveau actif";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
            }
            else
            {
                MessageBox.Show($"Erreur lors de la suppression:\n\n{message}", 
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                
                StatusTextBlock.Text = $"❌ Erreur suppression: {message}";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors de la suppression:\n\n{ex.Message}", 
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            
            StatusTextBlock.Text = $"❌ Erreur: {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
        }
    }
    #endregion

#endif // FIN LEGACY CODE - Section Documents migr\u00e9e vers DocumentsControl

    // ===== FORMULAIRES =====
    
    /// <summary>
/// Génère un courrier en mode standard (sans MCC)
/// </summary>
private async Task GenerateStandardLetterAsync(string userRequest)
{
    var patientContext = await GatherPatientContextAsync();
    
    var prompt = $@"Génère un courrier médical selon cette demande : {userRequest}

CONTEXTE PATIENT :
{patientContext}

INSTRUCTIONS :
- Ton professionnel et adapté
- Structure claire avec en-têtes
- Informations médicales pertinentes du patient
- Format Markdown";

    var (success, letter, error) = await _openAIService.GenerateTextAsync(prompt, maxTokens: 2000);

    if (success)
    {
        // Afficher dans l'éditeur
        DisplayLetterInEditor(letter);
        SauvegarderLetterButton.IsEnabled = true;
    }
    else
    {
        MessageBox.Show($"Erreur de génération :\n{error}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

/// <summary>
/// Génère un courrier avec un MCC spécifique
/// </summary>
private async Task GenerateLetterWithMCCAsync(
    MCCModel mcc, 
    string userRequest,
    LetterAnalysisResult analysis)
{
    var patientContext = await GatherPatientContextAsync();
    
    var prompt = $@"{mcc.PromptTemplate}

DEMANDE UTILISATEUR : {userRequest}

CONTEXTE PATIENT :
{patientContext}

MÉTADONNÉES :
- Public : {analysis.Audience}
- Ton : {analysis.Tone}
- Tranche d'âge : {analysis.AgeGroup}

TEMPLATE À SUIVRE :
{mcc.TemplateMarkdown}

Génère le courrier en suivant le template et en l'adaptant au patient.";

    var (success, letter, error) = await _openAIService.GenerateTextAsync(prompt, maxTokens: 2000);

    if (success)
    {
        DisplayLetterInEditor(letter);
        SauvegarderLetterButton.IsEnabled = true;
    }
    else
    {
        MessageBox.Show($"Erreur de génération :\n{error}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

/// <summary>
/// Rassemble le contexte patient pour la génération
/// </summary>
private async Task<string> GatherPatientContextAsync()
{
    var context = new StringBuilder();
    
    if (_selectedPatient == null) return string.Empty;
    
    var metadata = _patientIndex.GetMetadata(_selectedPatient.Id);
    
    context.AppendLine($"NOM : {metadata.Nom} {metadata.Prenom}");
    if (metadata.Age.HasValue)
        context.AppendLine($"ÂGE : {metadata.Age} ans");
    if (!string.IsNullOrEmpty(metadata.Sexe))
        context.AppendLine($"SEXE : {metadata.Sexe}");
    
    // Ajouter notes récentes
    var recentNotes = NoteViewModel.Notes.Take(3);
    if (recentNotes.Any())
    {
        context.AppendLine("\nNOTES RÉCENTES :");
        foreach (var note in recentNotes)
        {
            context.AppendLine($"- {note.DateLabel} : {note.Preview}");
        }
    }
    
    return context.ToString();
}

/// <summary>
/// Affiche le courrier dans l'éditeur de texte
/// </summary>
private void DisplayLetterInEditor(string letterContent)
{
    // IMPORTANT: Clear any existing letter selection to prevent overwriting
    LettersList.SelectedItem = null;
    _currentEditingFilePath = null;

    // Convertir Markdown en FlowDocument
    var flowDoc = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(letterContent);
    LetterEditText.Document = flowDoc;

    // Passer en mode édition
    LetterEditText.IsReadOnly = false;
    LetterEditText.Background = new SolidColorBrush(Colors.White);
    
    // 🐛 FIX: Rendre le bouton Sauvegarder visible ET actif
    ModifierLetterButton.Visibility = Visibility.Collapsed;
    SupprimerLetterButton.Visibility = Visibility.Collapsed;
    AnnulerLetterButton.Visibility = Visibility.Visible;
    SauvegarderLetterButton.Visibility = Visibility.Visible;
    SauvegarderLetterButton.IsEnabled = true;
    SauvegarderLetterButton.Background = new SolidColorBrush(Color.FromRgb(39, 174, 96)); // Vert
    ImprimerLetterButton.Visibility = Visibility.Collapsed;
}

/// <summary>
/// Ouvre le dialogue de création de courrier avec IA intelligente
/// </summary>
private async void CreateLetterWithAIButton_Click(object sender, RoutedEventArgs e)
{
    try
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
            var letterResult = dialog.Result;
            StatusTextBlock.Text = "⏳ Génération du courrier en cours...";
            await Task.Delay(100);

            if (letterResult.UseStandardGeneration)
            {
                // Génération standard → Pas de MCC
                _lastGeneratedLetterMCCId = null;
                _lastGeneratedLetterMCCName = null;
                await GenerateStandardLetterAsync(letterResult.UserRequest);
            }
            else if (letterResult.SelectedMCC != null)
            {
                // 🆕 TRAÇABILITÉ MCC : Stocker les métadonnées pour le matching MCC
                _lastGeneratedLetterMCCId = letterResult.SelectedMCC.Id;
                _lastGeneratedLetterMCCName = letterResult.SelectedMCC.Name;
                System.Diagnostics.Debug.WriteLine($"[MCC Tracking] MCC sélectionné via matching: {letterResult.SelectedMCC.Name} (ID: {letterResult.SelectedMCC.Id})");
                
                await GenerateLetterWithMCCAsync(letterResult.SelectedMCC, 
                    letterResult.UserRequest, letterResult.Analysis);
                _mccLibrary.IncrementUsage(letterResult.SelectedMCC.Id);
            }

            StatusTextBlock.Text = "✅ Courrier généré avec succès";
        }
    }
    catch (Exception ex)
    {
        MessageBox.Show($"Erreur lors de la création du courrier :\n{ex.Message}", 
            "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        StatusTextBlock.Text = "❌ Erreur génération courrier";
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

                // Tronquer intelligemment (max 1000 mots)
                var words = cleanContent.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                var truncated = words.Length > 1000
                    ? string.Join(" ", words.Take(1000)) + "..."
                    : cleanContent;

                context.NotesRecentes.Add($"📋 SYNTHÈSE PATIENT:\n{truncated}");
                allNotesContent.AppendLine(cleanContent); // Pour détection diagnostics

                System.Diagnostics.Debug.WriteLine("[PatientContext] Utilisation de la synthèse patient");
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

        // Extraire diagnostics/troubles mentionnés dans TOUTES les notes (contenu complet)
        // Recherche de mots-clés cliniques courants
        var clinicalKeywords = new[]
        {
            "tdah", "autisme", "tsa", "dys", "trouble", "anxiété",
            "dépression", "toc", "hyperactivité", "attention",
            "opposition", "comportement", "phobie", "anorexie",
            "boulimie", "énurésie", "encoprésie", "tic",
            "dyslexie", "dyspraxie", "dysphasie", "dyscalculie",
            "déficit", "impulsivité", "agitation", "concentration"
        };

        var diagsFound = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allNotesText = allNotesContent.ToString().ToLower();

        foreach (var keyword in clinicalKeywords)
        {
            if (allNotesText.Contains(keyword.ToLower()))
            {
                diagsFound.Add(keyword);
            }
        }

        context.DiagnosticsConnus = diagsFound.ToList();
        
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
    
    
    // ===== ORDONNANCES IDE =====

    /// <summary>
    /// Ouvre le dialogue pour créer une ordonnance IDE
    /// </summary>


    // ===== MÉTHODES PUBLIQUES POUR LES DIALOGUES =====

    /// <summary>
    /// Retourne le service LLM actuellement configuré
    /// </summary>
    public ILLMService? GetCurrentLLMService()
    {
        return _currentLLMService;
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
