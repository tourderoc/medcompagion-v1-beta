# CLAUDE.md

## Project Overview

**MedCompanion** - WPF desktop app for psychiatrists (patient records, notes, prescriptions, certificates, AI-generated documents).

**Tech Stack:** .NET 8.0 WPF, C#, OpenAI/Ollama, QuestPDF/PDFsharp/PdfPig, DocumentFormat.OpenXml

## Build Commands

```bash
dotnet build medcompagnio2.sln                    # Build solution
dotnet run --project MedCompanion/MedCompanion.csproj  # Run app
```

## Architecture

```
MedCompanion/
├── Commands/       # RelayCommand
├── Dialogs/        # Modal windows
├── Models/         # Data models
├── Services/       # Business logic + Services/LLM/
├── ViewModels/     # MVVM ViewModels
├── Views/          # UserControls (7 sections refactored)
└── MainWindow*.cs  # Partial classes
```

### MainWindow Partial Classes
- `MainWindow.xaml.cs`: Core init, services (~1500 lines)
- `MainWindow.Documents.cs`: MCC/Templates analysis (~240 lines)
- `MainWindow.Patient.cs`: Patient loading, chat (~1090 lines)
- `MainWindow.LLM.cs`: LLM switching

### Key Services
- **LLM**: `ILLMService`, `LLMServiceFactory`, `OpenAILLMProvider`, `OllamaLLMProvider`
- **Data**: `PathService`, `PatientIndexService`, `StorageService`
- **MCC**: `MCCMatchingService`, `MCCLibraryService`, `PromptReformulationService`

### Patient Data Structure
```
Documents/MedCompanion/patients/LASTNAME_Firstname/
├── info_patient/patient.json
└── 2025/  (notes/, chat/, ordonnances/, attestations/, courriers/, documents/)
```

## Refactored UserControls (Nov 2025)

| Control | Location | Features |
|---------|----------|----------|
| PatientListControl | Views/Patients/ | List, search, delete |
| NotesControl | Views/Notes/ | Notes + Synthèse |
| OrdonnancesControl | Views/Ordonnances/ | Prescriptions + Renewal |
| AttestationsControl | Views/Attestations/ | Certificates |
| FormulairesControl | Views/Formulaires/ | MDPH/PAI forms |
| DocumentsControl | Views/Documents/ | Import, synthesis |
| CourriersControl | Views/Courriers/ | Letters, MCC matching |

**Pattern for all UserControls:**
```csharp
// Initialize
ControlPanel.Initialize(service1, service2, ...);
ControlPanel.StatusChanged += (s, msg) => StatusTextBlock.Text = msg;

// Load patient
ControlPanel.SetCurrentPatient(_selectedPatient);
```

## Key Patterns

### Tuple Return Pattern
```csharp
var (success, result, error) = await service.MethodAsync();
if (!success) { MessageBox.Show(error); return; }
```

### MCC Scoring (max 210 pts)
- DocType: 50pts | Keywords: 40pts | Audience: 30pts | Age: 20pts
- Tone: 15pts | Ratings: 30pts | Usage: 15pts | Validated: 10pts
- Threshold: 70 pts minimum

## Code Health: 9/10

- ✅ 0 errors, ~230 warnings (nullable)
- ✅ 7 UserControls refactored
- ✅ All legacy `#if false` blocks deleted (~1400 lines)
- ✅ Clean architecture with event-driven communication

## Recent Commits (Nov 2025)

- `7a6650d` - Supprimer blocs #if false legacy (~1400 lignes)
- `bb3ca08` - Intégrer CourriersControl dans MainWindow
- `cd6697b` - DocumentsControl refactoring

## Notes for AI Assistants

1. **Partial classes**: Patient→MainWindow.Patient.cs, LLM→MainWindow.LLM.cs
2. **Encoding**: Always UTF-8 for French characters
3. **Paths**: Use `PathService`, never hardcode
4. **Services**: Tuple return pattern `(bool, T, string?)`
5. **MVVM**: ViewModels with INotifyPropertyChanged
6. **Events**: UserControls use `StatusChanged` event
7. **Docs**: Check INTEGRATION_*.md, BUGFIX_*.md for details

## Documents Liés

- [VISION_V3.md](VISION_V3.md) — Vision écosystème Parent'aile + MedCompanion (V0 en cours)
- [VISION_V2.md](VISION_V2.md) — Vision MedCompanion V2 (Focus Med, Mémoire, Mode Consultation)
