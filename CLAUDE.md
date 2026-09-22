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

- [REPRISE_19_09.md](REPRISE_19_09.md) — Les mesures moteur à ne pas refaire. Son §1.1 (infographies) est remplacé par le plan ci-dessous, son §2 est périmé : le travail du 18/09 est committé (`b535f1b`).
- [PLAN_BIBLIOTHEQUE_INFOGRAPHIES.md](PLAN_BIBLIOTHEQUE_INFOGRAPHIES.md) — **Bibliothèque d'infographies.** Étape 1 codée le 22/09/2026 (Bureau → Infographies pour importer/classer/valider ; carte « Infographies » en consultation pour imprimer, remise notée dans le dossier patient), à essayer. Étape 2 (création par Qwen-Image 2.1 via ComfyUI) à cadrer après essai et lecture de la licence.
- **Depuis le 21/09/2026, le projet vit sur le NVMe : `N:\PosteTravail\Desktop\MedCompagion V1 béta`** (Corsair MP510 sur adaptateur PCIe x1). `Desktop` et `Documents` ont été déplacés de `D:\PosteTravail\` ; les originaux restent sur D: jusqu'à validation par le médecin. Prochaine étape : alléger le SSD SATA C:, qui sature.

- [VISION_V3.md](VISION_V3.md) — Vision écosystème Parent'aile + MedCompanion (V0 en cours)
- [VISION_V2.md](VISION_V2.md) — Vision MedCompanion V2 (Focus Med, Mémoire, Mode Consultation)
- [PLAN_MODE_CONSULTATION_V0A.md](PLAN_MODE_CONSULTATION_V0A.md) — Plan détaillé Mode Consultation V0a
- [PLAN_RESTITUTION_V2_RESTE_A_FAIRE.md](PLAN_RESTITUTION_V2_RESTE_A_FAIRE.md) — **Dossier de Restitution V2** : refonte blocs 1→32 terminée le 11/09/2026, principes acquis à ne pas défaire. Annexe contacts (bloc 33) le 16/09. **Service qualité ouvert le 17/09 (§6) : phase 1 mise en page + repagination dans le navigateur, 0 débordement sur 175 pages ; phases 2 (contradictions) et 3 (linguistique) à faire.** Restent 3 chantiers de fond, patch v2 de la Synthèse Globale en tête.
- [PLAN_CARTOGRAPHIE_ENFANT_V2.md](PLAN_CARTOGRAPHIE_ENFANT_V2.md) — **Reconstruction Cartographie de l'enfant** (grille unique + items par tranche d'âge, feuille parents en salle d'attente). Construit à côté du bloc Évaluation, sans y toucher.
- [PLAN_IMPRESSION_SANS_LIBREOFFICE.md](PLAN_IMPRESSION_SANS_LIBREOFFICE.md) — **Impression sans LibreOffice** : gabarit HTML A4 → Edge caché → PDF → PDFium. Rendu identique au docx. Plan validé 07/09/2026, chantier non démarré.
- [PLAN_MOTEUR_LLM_LOCAL.md](PLAN_MOTEUR_LLM_LOCAL.md) — **Moteur LLM local** : Med 100 % llama.cpp (Ollama à retirer), 3050 = Whisper / 5060 Ti = LLM, Qwen + Gemma QAT MTP, modèles sur partition SSD dédiée, voyant fidèle sans warm-up, switch optimisé. Plan en 8 étapes ouvert le 14/09/2026 ; étapes 1 à 4 faites (modèles sur `M:\llm` et `M:\whisper`, réglages `LlamaCppModelsDir` / `WhisperModelsDir`, sans repli ; plus aucun déchargement croisé LLM/Whisper, carte Whisper par `WhisperGpuUuid`) et 6 (warm-up supprimé, état publié par `LlamaCppServerManager.EtatChange`, voyant fidèle, veille 2 h). Étape 7 (switch) validée le 15/09 : bascules 4,6-5,5 s ; restent l'étape 5 (mesure RAM, garde-fou de la pré-lecture) et 8 (retrait d'Ollama).
- [PLAN_MED_100_LOCAL.md](PLAN_MED_100_LOCAL.md) — **Med 100 % local** : retrait d'Ollama, d'OpenAI et de l'anonymisation, en 8 étapes (0 à 7). Ouvert le 15/09/2026 ; étape 0 faite (structuration des notes sur Gemma 4 QAT via llama.cpp). Inventaire des appels directs à Ollama, ordre imposé (cloud avant anonymisation), piège du réglage `OllamaModel`.
- [SETUP_WHISPER_GPU.md](SETUP_WHISPER_GPU.md) — **Guide installation Whisper GPU + 7 pièges résolus** (à lire avant install nouveau poste)
