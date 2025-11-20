# Plan de Migration Attestations avec Services Techniques

## 🎯 Objectif
Créer **AttestationViewModel** avec une approche **Services + MVVM hybride**, puis améliorer les ViewModels existants.

---

## 📋 Phase 1 : Créer les Services Techniques

### 1. RichTextBoxService
**Fichier** : `MedCompanion/Services/RichTextBoxService.cs`

**Responsabilités** :
- Conversion Markdown → FlowDocument
- Conversion FlowDocument → Markdown  
- Manipulation RichTextBox (SetContent, GetContent)
- Gestion des styles et formatage

**Méthodes** :
```csharp
FlowDocument ConvertMarkdownToFlowDocument(string markdown)
string ConvertFlowDocumentToMarkdown(FlowDocument document)
void SetRichTextBoxContent(RichTextBox rtb, string markdown)
string GetRichTextBoxContent(RichTextBox rtb)
void ClearRichTextBox(RichTextBox rtb)
```

### 2. DialogService
**Fichier** : `MedCompanion/Services/DialogService.cs`

**Responsabilités** :
- Affichage dialogs standards (Confirmation, Error, Info)
- Affichage dialogs personnalisés
- Gestion des résultats

**Méthodes** :
```csharp
bool? ShowConfirmation(string title, string message)
void ShowError(string title, string message)
void ShowInfo(string title, string message)
T? ShowCustomDialog<T>(Window dialog) where T : class
```

### 3. FileOperationService
**Fichier** : `MedCompanion/Services/FileOperationService.cs`

**Responsabilités** :
- Ouverture de fichiers
- Impression de documents
- Affichage dans l'explorateur

**Méthodes** :
```csharp
void OpenFile(string filePath)
void PrintFile(string filePath)
void ShowInExplorer(string filePath)
bool FileExists(string filePath)
```

---

## 📋 Phase 2 : Créer AttestationViewModel

### AttestationViewModel
**Fichier** : `MedCompanion/ViewModels/AttestationViewModel.cs`

**Services injectés** :
```csharp
private readonly AttestationService _attestationService;
private readonly RichTextBoxService _richTextService;
private readonly DialogService _dialogService;
private readonly FileOperationService _fileService;
private readonly PathService _pathService;
```

**Propriétés** :
- `AvailableTemplates` (ObservableCollection<AttestationTemplate>)
- `SelectedTemplateType` (string)
- `Attestations` (ObservableCollection - liste patient)
- `SelectedAttestation` (sélection)
- `AttestationMarkdown` (string - contenu)
- `CurrentPatient` (PatientMetadata)
- `IsGenerating`, `CanModify`, `CanDelete`, etc. (bool)

**Commandes** :
- `GenerateCommand` - Génération standard
- `GenerateCustomCommand` - Génération avec IA
- `ModifyCommand` - Éditer attestation
- `SaveModifiedCommand` - Sauvegarder modifications
- `DeleteCommand` - Supprimer attestation
- `PrintCommand` - Imprimer
- `OpenFileCommand` - Ouvrir DOCX
- `RefreshListCommand` - Rafraîchir liste

---

## 📋 Phase 3 : Bindings XAML

### MainWindow.xaml - Section Attestations

**ComboBox Types** :
```xml
<ComboBox ItemsSource="{Binding AttestationViewModel.AvailableTemplates}"
          SelectedItem="{Binding AttestationViewModel.SelectedTemplateType}" />
```

**Liste Attestations** :
```xml
<ListBox ItemsSource="{Binding AttestationViewModel.Attestations}"
         SelectedItem="{Binding AttestationViewModel.SelectedAttestation}" />
```

**Boutons** :
```xml
<Button Content="Générer" Command="{Binding AttestationViewModel.GenerateCommand}" />
<Button Content="Personnalisée (IA)" Command="{Binding AttestationViewModel.GenerateCustomCommand}" />
<Button Content="Modifier" Command="{Binding AttestationViewModel.ModifyCommand}" />
<Button Content="Supprimer" Command="{Binding AttestationViewModel.DeleteCommand}" />
<Button Content="Imprimer" Command="{Binding AttestationViewModel.PrintCommand}" />
```

**RichTextBox** (géré par service) :
- Pas de binding direct (limitation WPF)
- Manipulation via RichTextBoxService dans les event handlers

---

## 📋 Phase 4 : Améliorer ViewModels Existants

### 4.1 Améliorer NoteViewModel

**Utiliser RichTextBoxService** :
```csharp
// Avant (dans NoteViewModel)
private void ConvertMarkdown()
{
    // 50 lignes de code de conversion...
}

// Après (simplifié)
private void ConvertMarkdown()
{
    var flowDoc = _richTextService.ConvertMarkdownToFlowDocument(markdown);
    RaiseEvent...
}
```

**Utiliser DialogService** :
```csharp
// Avant
MessageBox.Show(...)

// Après
_dialogService.ShowConfirmation(...)
```

### 4.2 Améliorer les autres ViewModels

- OrdonnanceViewModel → Utiliser DialogService
- PatientSearchViewModel → Utiliser DialogService si nécessaire
- Futurs ViewModels → Utiliser les 3 services dès le départ

---

## 📊 Avantages de cette Approche

✅ **Réduction MainWindow.xaml.cs** : De 5700 lignes → ~500 lignes (-90%)
✅ **Réutilisabilité** : Services utilisables partout
✅ **Testabilité** : Services testables indépendamment
✅ **Maintenabilité** : Logique centralisée
✅ **Cohérence** : Même approche partout
✅ **Performance** : Pas d'impact négatif
✅ **Évolutivité** : Facile d'ajouter des fonctionnalités

---

## 📈 Ordre d'Implémentation

1. ✅ **RichTextBoxService** (priorité 1 - utilisé partout)
2. ✅ **DialogService** (priorité 2 - simplifie beaucoup)
3. ✅ **FileOperationService** (priorité 3 - utilitaire)
4. ✅ **AttestationViewModel** (première migration complète avec services)
5. ✅ **Bindings XAML** (connecter ViewModel à View)
6. ⏳ **Améliorer NoteViewModel** (optionnel, quand temps disponible)
7. ⏳ **Améliorer autres ViewModels** (progressivement)

---

## 🎯 Résultat Final Attendu

### Structure du Code
```
Services/
├─ PathService.cs ✅ (déjà créé)
├─ RichTextBoxService.cs ✅ (nouveau)
├─ DialogService.cs ✅ (nouveau)
├─ FileOperationService.cs ✅ (nouveau)
├─ AttestationService.cs ✅ (existe)
├─ DocumentService.cs ✅ (existe)
└─ ... autres services métier

ViewModels/
├─ ViewModelBase.cs ✅
├─ PatientSearchViewModel.cs ✅
├─ NoteViewModel.cs ✅ (à améliorer avec services)
├─ OrdonnanceViewModel.cs ✅
├─ AttestationViewModel.cs ✅ (nouveau avec services)
└─ ... futurs ViewModels

MainWindow.xaml.cs
└─ ~500 lignes ✅ (réduit de 90%)
```

### Qualité du Code
- ✅ MVVM pur avec services techniques
- ✅ Séparation claire des responsabilités
- ✅ Code maintenable et évolutif
- ✅ Tests faciles à écrire
- ✅ Pas de duplication

---

## ⏱️ Estimation Temps

| Phase | Durée estimée |
|-------|---------------|
| 1. Services techniques | 2-3h |
| 2. AttestationViewModel | 2-3h |
| 3. Bindings XAML | 1h |
| 4. Tests et debug | 1-2h |
| **Total Phase 1-3** | **6-9h** |
| 5. Améliorer ViewModels existants | 2-4h (optionnel) |
| **Total complet** | **8-13h** |

---

**Prêt à commencer ?** 🚀
