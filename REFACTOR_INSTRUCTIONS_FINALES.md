# 🎯 Instructions de Refactoring - MainWindow.xaml.cs

## 📌 APPROCHE RECOMMANDÉE

Vu la taille du fichier (2100+ lignes), je vous recommande **l'approche manuelle guidée** qui est plus sûre.

## 🔧 OPTION 1 : Découpage Manuel (RECOMMANDÉ)

### Étape 1 : Créer les fichiers vides

Créez 2 nouveaux fichiers dans le dossier `MedCompanion/` :
- `MainWindow.Patient.cs`
- `MainWindow.Documents.cs`

### Étape 2 : En-tête de chaque fichier

**Dans `MainWindow.Patient.cs` :**
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MedCompanion.Commands;
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.Dialogs;

namespace MedCompanion;

public partial class MainWindow : Window
{
    // COPIER ICI LES MÉTHODES DE LA SECTION PATIENT
}
```

**Dans `MainWindow.Documents.cs` :**
```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MedCompanion.Commands;
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.Dialogs;

namespace MedCompanion;

public partial class MainWindow : Window
{
    // COPIER ICI LES MÉTHODES DE LA SECTION DOCUMENTS
}
```

### Étape 3 : Copier les méthodes (Utilisez Ctrl+F pour les trouver rapidement)

## 📋 SECTION PATIENT (→ MainWindow.Patient.cs)

**Rechercher et copier ces méthodes** dans `MainWindow.xaml.cs` vers `MainWindow.Patient.cs` :

#### Recherche Patient
- `OnSearchBoxPaste`
- `SearchBox_GotFocus`
- `SearchBox_LostFocus`
- `CreatePatientBorder_Click`

#### Chargement Patient
- `PatientHasStructuredNotes`
- `LoadPatientAsync`

#### Liste Patients
- `LoadPatientsInPanel`
- `TogglePatientsBtn_Click`
- `ApplyPatientSorting`
- `UpdatePatientCount`
- `PatientsDataGrid_SelectionChanged`
- `PatientsDataGrid_MouseDoubleClick`
- `DeletePatientButton_Click`
- `CountPatientContent`

#### Boutons Patient
- `AnalysePromptsBtn_Click`
- `OpenPatientFolderBtn_Click`

#### Notes Cliniques
- `StructurerButton_Click`
- `OnNoteStatusChanged`
- `OnNoteContentLoaded`
- `OnNoteStructured`
- `OnNoteSaveRequested`
- `OnNoteDeleteRequested`
- `OnNotesListRefreshRequested`
- `OnNoteClearedAfterSave`
- `NoteViewModel_PropertyChanged`
- `StructuredNoteText_TextChanged`
- `EnterConsultationMode`
- `ExitConsultationMode`
- `FermerConsultationButton_Click`
- `FindParentGrid`

#### Chat IA
- `ChatInput_KeyDown`
- `ChatSendBtn_Click`
- `SaveExchangeButton_Click`
- `LoadSavedExchanges`
- `ViewSavedExchangeBtn_Click`
- `DeleteSavedExchangeBtn_Click`
- `RefreshSavedExchangesList`
- `UpdateMemoryIndicator`

#### Synthèse Patient
- `LoadPatientSynthesis`
- `GenerateSynthesisButton_Click`

---

## 📄 SECTION DOCUMENTS (→ MainWindow.Documents.cs)

**Rechercher et copier ces méthodes** dans `MainWindow.xaml.cs` vers `MainWindow.Documents.cs` :

#### Courriers
- `TemplateLetterCombo_SelectionChanged`
- `RefreshLettersList`
- `LettersList_SelectionChanged`
- `LettersList_MouseDoubleClick`
- `LetterEditText_TextChanged`
- `ModifierLetterButton_Click`
- `SupprimerLetterButton_Click`
- `SauvegarderLetterButton_Click`
- `ImprimerLetterButton_Click`

#### Détection Intent
- `ChatInput_TextChanged` (la deuxième, celle avec détection d'intent)
- `ShowSuggestionBanner`
- `HideSuggestionBanner`
- `CloseSuggestionBtn_Click`
- `IgnoreSuggestionBtn_Click`
- `OpenTemplateSelector`
- `GenerateLetterFromTemplate`
- `ChooseTemplateBtn_Click`
- `TemplateMenuItem_Click`

#### Templates Personnalisés
- `LoadCustomTemplates`
- `AnalyzeLetterBtn_Click`
- `SaveTemplateBtn_Click`
- `RefreshCustomTemplatesList`
- `PreviewTemplateBtn_Click`
- `EditTemplateBtn_Click`
- `DeleteTemplateBtn_Click`

#### Attestations
- `AttestationTypeCombo_SelectionChanged`
- `GenererAttestationButton_Click`
- `GenerateCustomAttestationButton_Click`
- `AttestationsList_SelectionChanged`
- `AttestationsList_MouseDoubleClick`
- `ModifierAttestationButton_Click`
- `OuvrirAttestationButton_Click`
- `SupprimerAttestationButton_Click`
- `ImprimerAttestationButton_Click`
- `SauvegarderAttestationModifiee`
- `RefreshAttestationsList`

#### Documents
- `LoadPatientDocuments`
- `ApplyDocumentFilter`
- `DocCategoriesListBox_SelectionChanged`
- `DocDropZone_DragOver`
- `DocDropZone_DragLeave`
- `DocDropZone_Drop`
- `DocBrowseButton_Click`
- `ProcessDocumentFilesAsync`
- `DocumentsDataGrid_MouseDoubleClick`
- `DocumentsDataGrid_SelectionChanged`
- `OpenDocument`
- `DeleteDocumentButton_Click`
- `DocSynthesisButton_Click`
- `UpdateDocumentSynthesisState`
- `ResetSynthesisPreview`
- `SaveSynthesisBtn_Click`
- `CloseSynthesisPreviewBtn_Click`
- `DeleteSynthesisBtn_Click`
- `OpenDropWindowButton_Click`

#### Formulaires
- `FormulaireTypeCombo_SelectionChanged`
- `PreremplirFormulaireButton_Click`
- `LoadPatientFormulaires`
- `FormulairesList_MouseDoubleClick`
- `FormulairesList_SelectionChanged`
- `SupprimerFormulaireButton_Click`
- `OuvrirModelePAIButton_Click`

#### Ordonnances IDE
- `IDEOrdonnanceButton_Click`
- `OrdonnancesList_SelectionChanged`
- `OrdonnancesList_MouseDoubleClick`
- `SupprimerOrdonnanceButton_Click`
- `ImprimerOrdonnanceButton_Click`

---

### Étape 4 : Modifier MainWindow.xaml.cs

**Ajouter `partial` à la déclaration de classe** (ligne ~18) :
```csharp
public partial class MainWindow : Window  // Ajouter "partial"
```

### Étape 5 : Supprimer les doublons

**Supprimez les méthodes copiées** de `MainWindow.xaml.cs` (gardez seulement dans les fichiers partiels)

### Étape 6 : Compiler et tester

```bash
dotnet build
```

Si erreurs, vérifiez que :
- ✅ Les 3 fichiers ont `public partial class MainWindow : Window`
- ✅ Aucune méthode n'est dupliquée
- ✅ Tous les using statements sont présents

---

## 🚀 OPTION 2 : Approche Automatique (RISQUÉE)

Si vous préférez, je peux créer les fichiers directement via code, mais il y aura probablement des erreurs de compilation à corriger manuellement.

**Voulez-vous :**
1. ✅ Suivre le guide manuel ci-dessus (30 min, plus sûr)
2. ❌ Que je génère automatiquement les fichiers (5 min, risque d'erreurs)

---

## 💡 Astuce

Utilisez **Ctrl+F** dans Visual Studio pour chercher rapidement chaque méthode et la copier-coller dans le bon fichier.

Les champs privés (`_pathService`, `_selectedPatient`, etc.) restent dans `MainWindow.xaml.cs` - ils sont automatiquement accessibles depuis tous les fichiers partiels !
