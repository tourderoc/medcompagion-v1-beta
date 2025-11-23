# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

**MedCompanion** is a WPF desktop application for psychiatrists to manage patient records, clinical notes, prescriptions, medical certificates, and generate documents using AI. The application uses a hybrid MVVM architecture and is currently in transition from a monolithic structure to a more maintainable, modular design.

**Technology Stack:**
- .NET 8.0 Windows (WPF)
- C# with nullable reference types enabled
- LLM Integration: OpenAI API and Ollama (local)
- PDF Generation: QuestPDF, PDFsharp, PdfPig
- Document Processing: DocumentFormat.OpenXml

## Build and Run Commands

### Building the Project
```bash
# Build the solution
dotnet build medcompagnio2.sln

# Build specific project
dotnet build MedCompanion/MedCompanion.csproj

# Build for Release
dotnet build MedCompanion/MedCompanion.csproj -c Release
```

### Running the Application
```bash
# Run from project directory
dotnet run --project MedCompanion/MedCompanion.csproj

# Run Debug build
dotnet run --project MedCompanion/MedCompanion.csproj -c Debug
```

### Viewing Build Output
Build warnings and errors are often saved to `build_output.txt` in the project root.

## Architecture Overview

### Project Structure

The application follows a hybrid architecture combining MVVM patterns with service-oriented design:

```
MedCompanion/
├── Commands/              # ICommand implementations (RelayCommand)
├── Dialogs/              # Modal windows for specific workflows
├── Helpers/              # Base classes (ObservableObject)
├── Models/               # Data models and domain entities
├── Services/             # Business logic and external integrations
│   └── LLM/             # LLM provider abstraction layer
├── ViewModels/          # MVVM ViewModels with data binding
├── Views/               # UserControls (in progress - refactoring)
└── MainWindow*.cs       # Partial classes for main window logic
```

### MainWindow Partial Classes

The MainWindow has been split into partial classes to manage complexity:

- **MainWindow.xaml.cs** (~86KB): Core initialization, service setup, and general UI logic
- **MainWindow.Documents.cs** (~96KB): Letter templates, MCC matching, document generation
- **MainWindow.Patient.cs** (~71KB): Patient loading, context management, notes display
- **MainWindow.Ordonnances.cs** (~300 bytes): Prescription-related logic (lightweight)
- **MainWindow.Formulaires.cs** (~31KB): Medical forms and attestations
- **MainWindow.LLM.cs** (~9KB): LLM provider switching and warmup

### Key Services Architecture

#### LLM Service Layer
The application uses a **factory pattern** for LLM providers:

- `ILLMService`: Common interface for all LLM providers
- `LLMServiceFactory`: Creates and manages provider instances
- `OpenAILLMProvider`: OpenAI API implementation
- `OllamaLLMProvider`: Local Ollama server implementation
- `LLMWarmupService`: Pre-warms models for faster response

**Usage Pattern:**
```csharp
// Get current LLM provider (configurable at runtime)
var llm = _llmFactory.GetCurrentProvider();
var (success, result, error) = await llm.GenerateTextAsync(prompt);
```

#### Patient Data Management

Patient data follows a **file-based storage** pattern organized by year:

```
Documents/MedCompanion/patients/
└── LASTNAME_Firstname/
    ├── info_patient/
    │   └── patient.json          # Metadata (name, DOB, sex, school)
    └── 2025/                      # Year-based folders
        ├── notes/                 # Clinical notes (.md with YAML frontmatter)
        ├── chat/                  # Chat interactions with AI
        ├── ordonnances/           # Prescriptions (PDF)
        ├── attestations/          # Medical certificates (PDF)
        ├── courriers/             # Letters (PDF)
        └── documents/             # Imported patient documents
```

**Key Services:**
- `PathService`: Centralized path management (avoids hardcoded paths)
- `PatientIndexService`: Maintains searchable index of all patients
- `StorageService`: Handles file I/O with proper encoding (UTF-8)

#### MCC (Modèle de Communication Clinique) System

The **MCC Library** is an intelligent letter template matching system:

1. **User Input** → `PromptReformulationService` analyzes intent using AI
2. **Semantic Analysis** → Extracts document type, audience, keywords, tone
3. **Matching** → `MCCLibraryService.FindBestMatchingMCCs()` scores templates
4. **Scoring System** (max 210 points):
   - Document type match: 50 pts (mandatory filter)
   - Keywords relevance: 40 pts
   - Audience match: 30 pts
   - Age range match: 20 pts
   - Tone match: 15 pts
   - User ratings: 30 pts
   - Usage popularity: 15 pts
   - Validated status: 10 pts
5. **Threshold**: 70 points minimum (33.3%) for acceptance

**Centralized Service:**
```csharp
var matchingService = new MCCMatchingService(_reformulationService, _mccLibraryService);
var (success, result, error) = await matchingService.AnalyzeAndMatchAsync(userRequest, patientContext);

if (success && result.HasMatch) {
    // result.SelectedMCC contains the best matching template
    // result.NormalizedScore shows confidence (0-100%)
    // result.ScoreBreakdown explains the scoring
}
```

See `MedCompanion/Services/README_MCCMatchingService.md` for detailed documentation.

#### Document Generation

Documents are generated using a **template-based pipeline**:

1. **Template Selection**: Manual or AI-assisted (MCC matching)
2. **Variable Extraction**: Parse `{{Variable_Name}}` placeholders
3. **AI Enhancement**: Use OpenAI/Ollama to generate contextual content
4. **PDF Generation**: QuestPDF for professional formatting
5. **Metadata Tracking**: Store generation parameters for rating feedback

**Rating System:**
The `LetterRatingService` allows users to rate AI-generated letters (1-5 stars) to improve future matching and template quality.

### MVVM ViewModels

The application uses ViewModels for data-driven UI components:

- `PatientSearchViewModel`: Patient search autocomplete, creation
- `NoteViewModel`: Clinical notes listing, structuring, synthesis
- `OrdonnanceViewModel`: Prescription management
- `AttestationViewModel`: Medical certificate generation
- `PromptsAnalysisViewModel`: Prompt configuration and testing

**Data Binding Pattern:**
```csharp
// In MainWindow constructor
PatientSearchViewModel = new PatientSearchViewModel(_patientIndex, ...);

// In XAML
<UserControl DataContext="{Binding PatientSearchViewModel}"/>
```

## Important Development Patterns

### Patient Context Loading

When a patient is selected, the application loads rich context for AI:

1. **Metadata**: Name, age, sex, school from `patient.json`
2. **Clinical History**: All structured notes from `notes/` folder
3. **Recent Context**: Last 3 notes or chat exchanges for continuity

This context is passed to `PromptReformulationService` and `OpenAIService` for personalized, contextual document generation.

### Error Handling in Services

Services follow a **tuple return pattern** for clear error handling:

```csharp
public async Task<(bool success, string result, string? error)> MethodAsync()
{
    try {
        // ... logic
        return (true, result, null);
    }
    catch (Exception ex) {
        return (false, "", ex.Message);
    }
}

// Usage
var (success, data, error) = await service.MethodAsync();
if (!success) {
    MessageBox.Show(error, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
    return;
}
```

### Prompt Configuration System

Prompts are **externalized and hot-reloadable**:

- `PromptConfigService` loads prompts from JSON configuration
- Supports versioning and A/B testing of prompts
- Changes are detected via `FileSystemWatcher`
- OpenAIService auto-reloads when prompts change

```csharp
// Prompts are cached but auto-refresh on file changes
_promptConfig.PromptsReloaded += OnPromptsReloaded;
```

## File Encoding

**Critical**: All patient files use **UTF-8 encoding** to handle French characters (accents, special characters). Several PowerShell scripts exist for encoding fixes:
- `fix-encoding.ps1`
- `fix-encoding-simple.ps1`
- `fix-encoding.py`

Always use `Encoding.UTF8` when reading/writing files.

## Refactoring History (November 2025)

### ✅ Refactoring Completed - Phase 1 (Simple Sections)

**Decision Made:** Follow **Option B** from `REFACTORING_ROADMAP_OPTION_B.md` - Refactor simple and medium complexity sections first, leave most complex section (Courriers/MCC) in legacy code intentionally.

**Sections Refactored into UserControls:**

1. **PatientListControl** (`Views/Patients/PatientListControl`)
   - 185 lines XAML extracted from MainWindow
   - 220 lines code-behind with autonomous patient list management
   - Events: `PatientSelected`, `PatientDeleted`, `StatusChanged`
   - Methods: `Initialize()`, `LoadPatients()`, deletion with confirmation
   - Commit: `c4c4d90` - "feat: Refactorer la liste Patients en PatientListControl"

2. **NotesControl** (`Views/Notes/NotesControl`)
   - Already existed for notes brute/structurée
   - **ADDED:** Synthèse Patient functionality migrated from MainWindow.Patient.cs
   - Methods: `LoadPatientSynthesis()` (67 lines), `GenerateSynthesisButton_Click()` (169 lines)
   - Initialize with `SynthesisService`, auto-load on `SetCurrentPatient()`
   - Event: `StatusChanged` for communication with MainWindow
   - Legacy code disabled in MainWindow.Patient.cs with `#if false`

3. **OrdonnancesControl** (`Views/Ordonnances/OrdonnancesControl`)
   - Uses `OrdonnanceViewModel` (already existed)
   - Clean MVVM pattern with data binding
   - No code migration needed (ViewModel handles everything)
   - **NEW (Nov 2025): Prescription Renewal Feature**
     - Button "🔄 Renouveler la dernière ordonnance" in MedicamentsControl
     - Automatically enabled/disabled based on prescription availability
     - Dialog (`RenewOrdonnanceDialog`) with medication selection (checkboxes)
     - Pre-fills prescription form with selected medications
     - Integrated with synthesis weight system (weight: 0.2 for renewals)
     - Implementation files:
       - `MedCompanion/Dialogs/RenewOrdonnanceDialog.xaml[.cs]`
       - `MedCompanion/Services/OrdonnanceService.cs` (ParseMedicamentsFromMarkdown, GetLastOrdonnanceMedicaments)
       - `MedCompanion/Views/Ordonnances/MedicamentsControl.xaml[.cs]` (LoadMedicamentsFromPreviousOrdonnance, RenewOrdonnanceButton_Click)

4. **AttestationsControl** (`Views/Attestations/AttestationsControl`)
   - Uses `AttestationViewModel` (already existed)
   - Clean MVVM pattern with data binding
   - Multiple bug fixes during integration:
     - Placeholder replacement issues
     - Gender selection (M/F/NB) with dialog
     - Deletion not refreshing UI
     - PDF generation date format

5. **FormulairesControl** (`Views/Formulaires/FormulairesControl`)
   - 293 lines XAML extracted from MainWindow
   - 740 lines code-behind migrated from MainWindow.Formulaires.cs
   - Handles MDPH (AI generation with 10 sections) and PAI (model PDF copy)
   - Methods: `PreremplirFormulaireButton_Click()`, `OuvrirModelePAIButton_Click()`
   - Methods: `LoadPatientFormulaires()`, deletion, preview PAI synthesis
   - Initialize with 4 services: `FormulaireService`, `LetterService`, `PatientIndex`, `DocumentService`
   - Event: `StatusChanged` for communication
   - Legacy code disabled in MainWindow.Formulaires.cs with `#if false`
   - **Note:** `OpenDropWindowButton_Click()` moved outside `#if false` (used by Documents section)
   - **Bug fixed:** NullReferenceException during XAML loading (added null checks)
   - Commit: `966e688` - "feat: Refactorer Synthèse et Formulaires en UserControls"

6. **DocumentsControl** (`Views/Documents/DocumentsControl`)
   - 343 lines XAML extracted from MainWindow (351 lines removed)
   - 723 lines code-behind migrated from MainWindow.xaml.cs (909 lines)
   - Handles patient documents import, viewing, deletion, AI synthesis
   - Features:
     - Browse button for file selection (multi-file dialog)
     - Document list with category filters (bilans, courriers, ordonnances, etc.)
     - Double-click to open documents with system default app
     - Delete with confirmation dialog
     - AI synthesis generation for selected document
     - Synthesis save/delete/preview with Markdown formatting
     - DocumentDropWindow integration (floating window with auto minimize/restore)
   - Initialize with 3 services: `DocumentService`, `PathService`, `PatientIndexService`
   - Event: `StatusChanged` for communication
   - Legacy code disabled in MainWindow.xaml.cs with `#if false` (lines 1173-1831)
   - **UX improvements:**
     - Removed duplicate drag & drop zone (kept only floating window)
     - Auto-minimize main window when drag & drop window opens
     - Auto-restore main window when drag & drop window closes
     - Green "Enregistrer" button after synthesis generation
   - Commit: `cd6697b` - "feat: Refactorer section Documents en DocumentsControl"

### Architecture Pattern Used for All UserControls

**Initialization Pattern:**
```csharp
// In MainWindow constructor
FormulairesControlPanel.Initialize(service1, service2, ...);
FormulairesControlPanel.StatusChanged += (s, msg) => {
    StatusTextBlock.Text = msg;
    StatusTextBlock.Foreground = GetColorFromMessage(msg);
};
```

**Patient Loading Pattern:**
```csharp
// In LoadPatientAsync()
FormulairesControlPanel.SetCurrentPatient(_selectedPatient);
```

**Event Communication:**
```csharp
// In UserControl
public event EventHandler<string>? StatusChanged;

// Raise event
StatusChanged?.Invoke(this, "✅ Operation completed");
```

### Current State After Refactoring & Cleanup (23/11/2025)

**MainWindow.xaml:**
- Original: ~3112 lines
- After refactoring: Much lighter (6 major sections extracted)
- Each section replaced with: `<namespace:ControlName x:Name="..."/>`

**Code Organization (after cleanup):**
- MainWindow.xaml.cs: Initialization, service setup, coordination (~1570 lines)
- MainWindow.Patient.cs: Patient loading, context (~1379 lines)
- MainWindow.Documents.cs: **Courriers/MCC matching** (~1235 lines)
- MainWindow.Formulaires.cs: Minimal stub (~12 lines)
- MainWindow.LLM.cs: LLM provider switching

**Legacy Code Cleanup COMPLETED:**
- ✅ **All `#if false` blocks deleted** after successful validation
- ✅ **~2490 lines of legacy code removed** across 4 files:
  - MainWindow.Formulaires.cs: ~733 lines removed
  - MainWindow.Patient.cs (PatientListControl): ~173 lines removed
  - MainWindow.Patient.cs (NotesControl/Synthèse): ~252 lines removed
  - MainWindow.xaml.cs (Documents): ~661 lines removed
  - MainWindow.Documents.cs (Attestations): ~671 lines removed
- ✅ Application tested and validated with **no regressions**

**Compilation Status:**
- ✅ **0 errors** after all refactoring and cleanup
- ~231 warnings (pre-existing, mostly nullable references)

### What Remains INTENTIONALLY in Legacy (Strategic Decision)

**Courriers/MCC Section (NOT refactored - DECISION: Keep as-is):**
- Location: `MainWindow.Documents.cs` (~1235 lines after cleanup)
- Functionality: Letter generation with AI, MCC template matching, semantic analysis
- Complexity: Very high (OpenAI integration, scoring algorithms, template management)
- **Decision rationale (November 2025):**
  - ✅ **Risk too high** vs benefit for this critical feature
  - ✅ **Already 6 sections refactored successfully** - diminishing returns
  - ✅ **Code works well** - no pressing need to refactor
  - ✅ **Can be done later** if truly needed with more preparation

**Future Refactoring (Long-term: Only if necessary):**
- Courriers/MCC section could be refactored in 6+ months if needed
- Would require careful planning with step-by-step approach
- Not a priority given current architecture stability

### Testing Validated (23/11/2025)

**All sections tested after legacy code cleanup:**
1. ✅ Patient list: search, select, delete
2. ✅ Notes: structure, save, edit, delete
3. ✅ Synthèse: generate complete, incremental update
4. ✅ Ordonnances: create, modify, delete, print, renew from previous prescription
5. ✅ Attestations: create with placeholders, gender selection, delete
6. ✅ Formulaires: MDPH AI generation, PAI model opening, delete
7. ✅ Documents: import (browse), view, delete, synthesis generation/save
8. ✅ All cross-section workflows validated

### Key Learnings & Decisions

1. **Pragmatic Approach:** Refactor simple sections first, leave complex for later
2. **Event-Driven:** UserControls communicate via events, not direct coupling
3. **Service Injection:** `Initialize()` method pattern for dependency injection
4. **Legacy Safety:** Keep old code disabled but available (`#if false`)
5. **Incremental Testing:** Test after each section refactored
6. **Bug Fixing:** Fix bugs discovered during refactoring immediately

### Git Commits Created

**Phase 1 - Refactoring into UserControls:**
- `c4c4d90` - PatientListControl refactoring
- `966e688` - Synthèse and FormulairesControl refactoring
- `cd6697b` - DocumentsControl refactoring

**Phase 2 - Legacy Code Cleanup (23/11/2025):**
- `ad72d94` - Supprimer bloc legacy MainWindow.Formulaires.cs (~733 lines)
- `dd959d9` - Supprimer bloc legacy PatientListControl (~173 lines)
- `e8c72d8` - Supprimer bloc legacy NotesControl/Synthèse (~252 lines)
- `a624899` - Supprimer bloc legacy Documents (~661 lines)
- `f45a870` - Supprimer bloc legacy Attestations (~671 lines)

**Phase 3 - CourriersControl Refactoring (EN COURS):**
- `861f5d1` - wip: Créer CourriersControl (non intégré) - voir section TODO ci-dessous

When working on UI code, prefer creating new UserControls in `Views/` folder rather than expanding MainWindow.

## Code Health & Technical Debt (November 2025)

### Current State: 9/10 (upgraded from 8/10 after legacy cleanup 23/11/2025)

**Compilation Status:**
- ✅ **0 errors** - Application compiles successfully
- ⚠️ **~231 warnings** - Mostly nullable reference types (CS8618), inoffensive
- ✅ **Functionality** - All features working in production

**Architecture:**
- ✅ Services well structured (Factory pattern, tuple returns, PathService)
- ✅ **6 major sections refactored** into clean UserControls
- ✅ Consistent pattern across all UserControls (Initialize, SetCurrentPatient, StatusChanged)
- ✅ **All legacy `#if false` blocks removed** (~2490 lines cleaned up)
- ⚠️ 1 complex section intentionally kept as-is (Courriers/MCC)
- ⚠️ Hybrid MVVM + code-behind (partial classes remain)

### Known Technical Debt

**High Priority:**
1. **MainWindow.Documents.cs** - ~1235 lines of complex logic (letter generation, MCC matching, templates)
   - Risk: Hard to maintain, difficult to test, regression-prone
   - **Decision (November 2025): INTENTIONALLY kept as-is** - strategic choice after 6 successful refactorings
   - Rationale: Critical feature, high risk, low immediate benefit, can be done later if needed

2. **Zero automated tests** - No unit tests, integration tests, or UI tests
   - Risk: Regressions not caught early, manual testing only
   - Impact: Slows down future refactoring

3. **No centralized logging** - Errors shown via MessageBox only
   - Risk: Hard to diagnose production issues
   - Impact: No telemetry for user problems

**Medium Priority:**
4. **~231 nullable warnings** - Non-nullable properties without initialization
   - Impact: Noise in build logs, masks real issues
   - Fix: Progressive cleanup (variables unused first, then nullable)

5. **State management** - `_selectedPatient` shared across MainWindow
   - Risk: Order-dependent initialization, potential bugs
   - Future: Consider centralized state management

### Priorities Roadmap

**Completed (23/11/2025):**
- ✅ Test 6 refactored sections intensively in production
- ✅ Monitor for bugs related to refactoring
- ✅ **Delete all `#if false` blocks** - ~2490 lines removed
- ✅ No regressions detected

**Medium Term (1-2 months):**
- Fix easy warnings (unused variables, async without await)
- Add structured logging (Serilog/NLog)
- Unit tests for critical services (MCC matching, document service)
- Improve error handling and telemetry

**Long Term (6+ months) - Only if needed:**
- OPTIONAL: Refactor Courriers/MCC section (intentionally deferred)
- Centralized state management
- Production telemetry dashboard
- Full MVVM migration (eliminate partial classes)

### Why Not Refactor Courriers/MCC Now?

**Decision:** Pragmatic approach - successfully refactored 6 sections, intentionally defer most complex one

**Reasoning:**
- ✅ Already refactored 6 major sections successfully
- ✅ ~3000+ lines of code migrated to UserControls
- ✅ Application architecture significantly improved
- ⚠️ Courriers/MCC has very high regression risk
- ⚠️ Diminishing returns - already achieved 80% of benefit
- ✅ Better to consolidate gains than risk breaking critical feature
- ✅ Can be done later (6+ months) if truly needed

**Result:**
- Phase 1 successful (6 sections refactored, 0 errors)
- Courriers/MCC intentionally deferred
- Score improved from 7/10 to 8/10

## Recent Features

### Prescription Renewal (November 2025)
**Feature:** One-click prescription renewal from previous prescriptions

**Location:** `Views/Ordonnances/MedicamentsControl`

**Functionality:**
- Button "🔄 Renouveler la dernière ordonnance" automatically enabled when previous prescriptions exist
- Opens dialog (`RenewOrdonnanceDialog`) showing all medications from last prescription
- User can select which medications to renew via checkboxes (all checked by default)
- "Tout cocher" / "Tout décocher" buttons for bulk selection
- Selected medications pre-fill the prescription form
- User can modify dosage, duration, quantity before generating new prescription
- Integrated with synthesis weight tracking system (weight: 0.2 for renewal)

**Implementation:**
- **Dialog:** `MedCompanion/Dialogs/RenewOrdonnanceDialog.xaml[.cs]`
  - `SelectableMedicament` wrapper class with `INotifyPropertyChanged` for checkbox binding
  - Displays medication name, presentation, dosage, duration, quantity, renewability
- **Service:** `MedCompanion/Services/OrdonnanceService.cs`
  - `ParseMedicamentsFromMarkdown()` - Parses markdown prescription files
  - `GetLastOrdonnanceMedicaments()` - Retrieves medications from most recent prescription
  - `SaveOrdonnanceMedicaments()` - Updated to accept metadata for weight tracking
- **Control:** `MedCompanion/Views/Ordonnances/MedicamentsControl.xaml[.cs]`
  - `LoadMedicamentsFromPreviousOrdonnance()` - Pre-fills medication list
  - `RenewOrdonnanceButton_Click()` - Handler for renewal button
  - `UpdateRenewButtonState()` - Enables/disables button based on prescription availability

**Weight System Integration:**
- Renewal metadata `{ "is_renewal": true }` passed to `SaveOrdonnanceMedicaments()`
- `ContentWeightRules.GetDefaultWeight("ordonnance", metadata)` returns 0.2 for renewals
- Weight recorded via `SynthesisWeightTracker.RecordContentWeight()` for synthesis updates

### AI-Powered Letter Creation
- Dialog: `CreateLetterWithAIDialog` - Natural language letter requests
- Dialog: `MCCMatchResultDialog` - Preview matched templates
- See `INTEGRATION_COURRIER_IA.md` for integration details

### Letter Rating System
- Users can rate generated letters (1-5 stars)
- Ratings stored per MCC template in JSON
- Used to improve future matching scores
- See `LETTER_RATING_SYSTEM_IMPLEMENTATION.md`

### Multilingual MCC Matching
- Semantic analysis supports French medical terminology
- Keyword matching uses TF-IDF scoring
- See `BUGFIX_MCC_MATCHING_MULTILINGUE.md`

## Configuration

Application settings are in `AppSettings.cs`:

```csharp
public class AppSettings {
    // Doctor information
    public string Medecin { get; set; }
    public string Rpps { get; set; }

    // LLM configuration
    public string LLMProvider { get; set; } = "OpenAI"; // or "Ollama"
    public string OpenAIModel { get; set; } = "gpt-4o-mini";
    public string OllamaModel { get; set; } = "llama3.2:latest";
    public bool EnableAutoWarmup { get; set; } = true;
}
```

**Note**: OpenAI API key is stored in environment variable `OPENAI_API_KEY` (not in code/config).

## Common Workflows

### Adding a New Service

1. Create interface in `Services/` (if needed for abstraction)
2. Implement service class following tuple return pattern
3. Register in MainWindow constructor
4. Inject into ViewModels or use directly in partial classes

### Adding a New Dialog

1. Create XAML + code-behind in `Dialogs/`
2. Use `Owner = this` when showing to maintain parent window context
3. Return results via `DialogResult` and custom properties
4. Example pattern: See `MCCMatchResultDialog.xaml.cs`

### Adding a New MCC Template

1. Use `MCCLibraryDialog` UI (accessible from MainWindow)
2. Or manually call `MCCLibraryService.AddMCC()`
3. Provide semantic metadata: DocType, Audience, Keywords, AgeRange, Tone
4. Templates stored in JSON with automatic versioning

### Testing LLM Integration

1. Use "Prompts Analysis" dialog to test prompts in isolation
2. Check LLM provider status in top status bar
3. Use "Warmup" feature to test connectivity
4. Monitor Debug output for LLM service logs

## Git Workflow

The project uses descriptive commit messages:

```bash
git commit -m "feat: Add MCC Library System and refactor MainWindow into partial classes"
git commit -m "fix(MCC): Improve semantic keyword matching with TF-IDF"
git commit -m "refactor: Extract NotesControl UserControl"
```

## Notes for AI Assistants

1. **Maintain Partial Class Structure**: When modifying MainWindow logic, use the appropriate partial class file:
   - Patient operations → `MainWindow.Patient.cs`
   - Document generation → `MainWindow.Documents.cs`
   - LLM switching → `MainWindow.LLM.cs`

2. **Preserve Encoding**: Always use `Encoding.UTF8` for file I/O to handle French characters

3. **Use PathService**: Never hardcode file paths - use `PathService` for all patient file operations

4. **Follow Tuple Return Pattern**: New service methods should return `(bool success, T result, string? error)`

5. **MCC System is Critical**: The MCC matching system is a core feature - understand the scoring before modifying

6. **Respect MVVM**: New UI features should use ViewModels with INotifyPropertyChanged, not code-behind logic

7. **Test with French Content**: The application is designed for French medical practice - test with accented characters

8. **Check Recent Docs**: Implementation details in markdown files (INTEGRATION_*, BUGFIX_*, etc.) are authoritative

9. **Backward Compatibility**: Support both old patient folder structure (flat) and new (year-based) via PathService

### CourriersControl Refactoring - TODO (23/11/2025)

**État actuel :**
- ✅ `Views/Courriers/CourriersControl.xaml` créé (~380 lignes)
- ✅ `Views/Courriers/CourriersControl.xaml.cs` créé (~785 lignes)  
- ✅ Compile avec 0 erreurs
- ⏳ **NON ENCORE INTÉGRÉ** dans MainWindow.xaml
- Commit: `861f5d1`

**Méthodes migrées :**
- Liste: `RefreshLettersList`, `LettersList_SelectionChanged`, `LettersList_MouseDoubleClick`
- CRUD: `SauvegarderLetterButton_Click`, `SupprimerLetterButton_Click`, `ModifierLetterButton_Click`, `AnnulerLetterButton_Click`
- Templates: `TemplateLetterCombo_SelectionChanged`, `LoadCustomTemplates`
- Rating: `RateLetterButton_Click`, `ShowRateLetterDialog`, `HandleRatingActions`
- IA: `CreateLetterWithAIButton_Click` (délègue via event)

**Events du contrôle :**
- `StatusChanged` - Communication status vers MainWindow
- `CreateLetterWithAIRequested` - Déléguer création IA au MainWindow
- `DisplayGeneratedLetter()` - Méthode publique pour afficher un courrier généré

**À faire pour terminer l'intégration :**

1. **MainWindow.xaml** :
   - Ajouter: `xmlns:courriers="clr-namespace:MedCompanion.Views.Courriers"`
   - Remplacer onglet Courriers (lignes 703-1118) par: `<courriers:CourriersControl x:Name="CourriersControlPanel"/>`

2. **MainWindow.xaml.cs** :
   - Initialiser: `CourriersControlPanel.Initialize(_letterService, _pathService, _patientIndex, _mccLibrary, _letterRatingService, _letterTemplates);`
   - Connecter events StatusChanged et CreateLetterWithAIRequested
   - Dans LoadPatientAsync(): `CourriersControlPanel.SetCurrentPatient(_selectedPatient);`

3. **MainWindow.Documents.cs** - Supprimer méthodes dupliquées, GARDER:
   - `AnalyzeLetterBtn_Click`, `SaveTemplateBtn_Click` (onglet Templates)
   - `GenerateLetterFromTemplate`, `OpenTemplateSelector` (utilisés par chat)
   - Code `CreateLetterWithAIDialog`
