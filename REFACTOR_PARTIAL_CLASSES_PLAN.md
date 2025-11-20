# Plan de Découpage MainWindow.xaml.cs en Classes Partielles

## 📊 Analyse du fichier actuel

- **Taille totale** : 5473 lignes
- **Complexité** : Très élevée
- **Problème** : Tout dans un seul fichier

---

## 🎯 Stratégie de découpage en 9 fichiers

### 1️⃣ **MainWindow.xaml.cs** (Core - ~600 lignes)
**Lignes** : 1-300 environ + variables globales

**Contenu** :
- Using statements
- Déclaration de la classe `MainWindow`
- Toutes les variables privates (_services, _selectedPatient, etc.)
- Constructeur complet
- InitializePatientIndex()
- WireSearchEvents()
- LoadPatientsInPanel()
- Méthodes utilitaires (FindParentGrid, CleanYamlFromMarkdown, etc.)

---

### 2️⃣ **MainWindow.Patient.cs** (~700 lignes)
**Lignes** : ~400-1100

**Contenu** :
- LoadPatientAsync()
- RenderPatientCard()
- ResetPatientUI()
- SearchBox handlers (GotFocus, LostFocus, OnSearchBoxPaste)
- CreatePatientBorder_Click()
- PatientsDataGrid handlers (SelectionChanged, MouseDoubleClick)
- DeletePatientButton_Click()
- CountPatientContent()
- TogglePatientsBtn_Click()
- ApplyPatientSorting()
- UpdatePatientCount()
- AnalysePromptsBtn_Click()
- OpenPatientFolderBtn_Click()

---

### 3️⃣ **MainWindow.Notes.cs** (~800 lignes)
**Lignes** : ~300-1100

**Contenu** :
- Event handlers NoteViewModel :
  - OnNoteStatusChanged()
  - OnNoteContentLoaded()
  - OnNoteStructured()
  - OnNoteSaveRequested()
  - OnNoteDeleteRequested()
  - OnNotesListRefreshRequested()
  - OnNoteClearedAfterSave()
- NoteViewModel_PropertyChanged()
- StructuredNoteText_TextChanged()
- StructurerButton_Click()
- Mode consultation :
  - EnterConsultationMode()
  - ExitConsultationMode()
  - FermerConsultationButton_Click()

---

### 4️⃣ **MainWindow.Courriers.cs** (~900 lignes)
**Lignes** : ~1100-2000

**Contenu** :
- TemplateLetterCombo_SelectionChanged() (grosse méthode ~200 lignes)
- RefreshLettersList()
- LettersList handlers (SelectionChanged, MouseDoubleClick)
- Boutons courriers :
  - LetterEditText_TextChanged()
  - ModifierLetterButton_Click()
  - SupprimerLetterButton_Click()
  - SauvegarderLetterButton_Click()
  - ImprimerLetterButton_Click()
- Templates personnalisés :
  - LoadCustomTemplates()
  - AnalyzeLetterBtn_Click()
  - SaveTemplateBtn_Click()
  - RefreshCustomTemplatesList()
  - PreviewTemplateBtn_Click()
  - EditTemplateBtn_Click()
  - DeleteTemplateBtn_Click()
- Génération courriers :
  - OpenTemplateSelector()
  - GenerateLetterFromTemplate()
  - TemplateMenuItem_Click()

---

### 5️⃣ **MainWindow.Chat.cs** (~700 lignes)
**Lignes** : ~1500-2200

**Contenu** :
- Détection d'intents :
  - ChatInput_TextChanged()
  - ShowSuggestionBanner()
  - HideSuggestionBanner()
  - CloseSuggestionBtn_Click()
  - IgnoreSuggestionBtn_Click()
  - ChooseTemplateBtn_Click()
- Chat :
  - ChatInput_KeyDown()
  - ChatSendBtn_Click()
  - AddChatMessage()
  - ParseMarkdownToInlines()
  - ParseInlineStyles()
- Mém
