# Plan de Refactoring MainWindow.xaml.cs

## Objectif
Découper le fichier MainWindow.xaml.cs (2000+ lignes) en 3 fichiers partiels (partial class) pour améliorer la maintenabilité.

## Structure proposée

### Fichier 1: `MainWindow.xaml.cs` (PRINCIPAL - Core & Init)
**Contenu à GARDER:**
- Tous les champs privés (services, ViewModels, variables d'état)
- Constructeur
- InitializeComponent()
- InitializePatientIndex()
- WireSearchEvents()
- ResetPatientUI()
- RenderPatientCard()
- Utilitaires UI (ParseMarkdownToInlines, ParseInlineStyles, AddChatMessage, etc.)
- Classe PatientDisplayInfo (en bas du fichier)

**Lignes estimées: ~500**

### Fichier 2: `MainWindow.Patient.cs` (Patient & Clinical)
**Méthodes à DÉPLACER:**
- OnSearchBoxPaste
- SearchBox_GotFocus / LostFocus
- CreatePatientBorder_Click
- LoadPatientAsync
- PatientHasStructuredNotes
- LoadPatientsInPanel
- TogglePatientsBtn_Click
- ApplyPatientSorting
- UpdatePatientCount
- PatientsDataGrid_SelectionChanged / MouseDoubleClick
- DeletePatientButton_Click
- CountPatientContent
- AnalysePromptsBtn_Click
- OpenPatientFolderBtn_Click
- StructurerButton_Click
- OnNoteStatusChanged
- OnNoteContentLoaded
- OnNoteStructured
- OnNoteSaveRequested
- OnNoteDeleteRequested
- OnNotesListRefreshRequested
- OnNoteClearedAfterSave
- NoteViewModel_PropertyChanged
- StructuredNoteText_TextChanged
- EnterConsultationMode / ExitConsultationMode
- FermerConsultationButton_Click
- FindParentGrid
- ChatInput_KeyDown / TextChanged
- ChatSendBtn_Click
- LoadSavedExchanges
- ViewSavedExchangeBtn_Click / DeleteSavedExchangeBtn_Click
- SaveExchangeButton_Click
- RefreshSavedExchangesList
- UpdateMemoryIndicator
- LoadPatientSynthesis
- GenerateSynthesisButton_Click

**Lignes estimées: ~800**

### Fichier 3: `MainWindow.Documents.cs` (Documents médicaux)
**Méthodes à DÉPLACER:**
- TemplateLetterCombo_SelectionChanged
- RefreshLettersList
- LettersList_SelectionChanged / MouseDoubleClick
- LetterEditText_TextChanged
- ModifierLetterButton_Click / SupprimerLetterButton_Click / SauvegarderLetterButton_Click
- ImprimerLetterButton_Click
- ChatInput_TextChanged (détection d'intent)
- ShowSuggestionBanner / HideSuggestionBanner
- CloseSuggestionBtn_Click / IgnoreSuggestionBtn_Click
- OpenTemplateSelector
- GenerateLetterFromTemplate
- ChooseTemplateBtn_Click
- TemplateMenuItem_Click
- LoadCustomTemplates
- AnalyzeLetterBtn_Click / SaveTemplateBtn_Click
- RefreshCustomTemplatesList
- PreviewTemplateBtn_Click / EditTemplateBtn_Click / DeleteTemplateBtn_Click
- AttestationTypeCombo_SelectionChanged
- GenererAttestationButton_Click / GenerateCustomAttestationButton_Click
- AttestationsList_SelectionChanged / MouseDoubleClick
- ModifierAttestationButton_Click / SupprimerAttestationButton_Click / ImprimerAttestationButton_Click
- OuvrirAttestationButton_Click
- SauvegarderAttestationModifiee
- RefreshAttestationsList
- LoadPatientDocuments
- ApplyDocumentFilter
- DocCategoriesListBox_SelectionChanged
- DocDropZone_DragOver / DragLeave / Drop
- DocBrowseButton_Click
- ProcessDocumentFilesAsync
- DocumentsDataGrid_MouseDoubleClick / SelectionChanged
- DeleteDocumentButton_Click
- DocSynthesisButton_Click / SaveSynthesisBtn_Click / CloseSynthesisPreviewBtn_Click / DeleteSynthesisBtn_Click
- UpdateDocumentSynthesisState
- ResetSynthesisPreview
- OpenDocument
- FormulaireTypeCombo_SelectionChanged
- PreremplirFormulaireButton_Click
- LoadPatientFormulaires
- FormulairesList_MouseDoubleClick / SelectionChanged
- SupprimerFormulaireButton_Click
- OuvrirModelePAIButton_Click
- OpenDropWindowButton_Click
- IDEOrdonnanceButton_Click
- OrdonnancesList_SelectionChanged / MouseDoubleClick
- SupprimerOrdonnanceButton_Click / ImprimerOrdonnanceButton_Click

**Lignes estimées: ~900**

## Étapes d'implémentation

1. ✅ Supprimer le fichier MainWindow.Patient.cs incorrect
2. ⏳ Créer MainWindow.Patient.cs avec les bonnes méthodes
3. ⏳ Créer MainWindow.Documents.cs avec les bonnes méthodes
4. ⏳ Modifier MainWindow.xaml.cs pour supprimer les méthodes déplacées
5. ⏳ Compiler et vérifier qu'il n'y a pas d'erreurs
6. ⏳ Tester l'application

## Notes importantes
- Les champs privés restent dans MainWindow.xaml.cs et sont accessibles depuis tous les fichiers partiels
- Tous les fichiers doivent avoir: `public partial class MainWindow : Window`
- Les using statements doivent être cohérents dans tous les fichiers
