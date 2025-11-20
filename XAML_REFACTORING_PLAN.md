# 📐 Plan de Refactorisation XAML - MainWindow

## 📊 État des Lieux

### Problème Actuel
- **Fichier** : `MedCompanion/MainWindow.xaml`
- **Taille actuelle** : **3112 lignes** 🔴
- **Statut** : Fichier monolithique difficile à maintenir

### Problèmes Identifiés
1. ❌ **Maintenance difficile** : Trouver un élément spécifique prend du temps
2. ❌ **Lisibilité réduite** : Structure difficile à comprendre
3. ❌ **Pas de réutilisation** : Code dupliqué entre sections
4. ❌ **Conflits Git** : Risque élevé de conflits sur modifications simultanées
5. ❌ **Performance IDE** : Visual Studio peut ralentir sur fichiers volumineux

---

## 🎯 Objectif

**Réduire MainWindow.xaml de 3112 → ~600 lignes (-80%)** en extrayant des UserControls réutilisables.

---

## 🔧 Solution 1 : Extraction UserControls (PRINCIPALE)

### Architecture Proposée

```
MedCompanion/
├── MainWindow.xaml (600 lignes) ← Structure principale
├── Views/
│   ├── Patient/
│   │   ├── PatientSearchControl.xaml (200-300 lignes)
│   │   ├── PatientCardControl.xaml (100-150 lignes)
│   │   └── PatientListControl.xaml (150-200 lignes)
│   ├── Notes/
│   │   └── NotesControl.xaml (400-500 lignes)
│   ├── Letters/
│   │   └── LettersControl.xaml (300-400 lignes)
│   ├── Attestations/
│   │   └── AttestationsControl.xaml (300-400 lignes)
│   ├── Documents/
│   │   └── DocumentsControl.xaml (400-500 lignes)
│   ├── Formulaires/
│   │   └── FormulairesControl.xaml (200-300 lignes)
│   ├── Ordonnances/
│   │   └── OrdonnancesControl.xaml (200-300 lignes)
│   ├── Synthesis/
│   │   └── SynthesisControl.xaml (200-300 lignes)
│   └── Chat/
│       └── ChatControl.xaml (300-400 lignes)
```

---

## 📋 Détail des UserControls à Créer

### 1️⃣ PatientSearchControl.xaml (Priorité: ✅ HAUTE)

**Taille estimée** : 200-300 lignes

**Contenu** :
- SearchBox avec placeholder
- Popup suggestions
- Bouton "Valider"
- Gestion navigation clavier (↑↓ Entrée Escape)

**ViewModel** : ✅ `PatientSearchViewModel` (déjà créé)

**Propriétés exposées** :
```xml
<UserControl DataContext="{Binding PatientSearchViewModel}">
```

**Événements à gérer** :
- `PatientSelected` → Chargement patient dans MainWindow
- `CreatePatientRequested` → Ouverture dialogue création

---

### 2️⃣ PatientCardControl.xaml (Priorité: 🟡 MOYENNE)

**Taille estimée** : 100-150 lignes

**Contenu** :
- Nom/Prénom patient
- Âge
- Date de naissance
- Sexe
- Bouton "Ouvrir dossier"

**ViewModel** : ⏳ À créer `PatientCardViewModel`

**Propriétés exposées** :
```csharp
public PatientMetadata CurrentPatient { get; set; }
```

---

### 3️⃣ NotesControl.xaml (Priorité: ✅ HAUTE)

**Taille estimée** : 400-500 lignes

**Contenu** :
- Zone "Note brute" (TextBox)
- Zone "Note structurée" (RichTextBox)
- Boutons : Structurer, Sauvegarder, Modifier, Supprimer, Annuler
- Liste des notes (DataGrid)
- Synthèse patient

**ViewModel** : ✅ `NoteViewModel` (déjà créé)

**Propriétés exposées** :
```xml
<UserControl DataContext="{Binding NoteViewModel}">
```

**Bindings** :
- `RawNoteText` ↔ Note brute
- `StructuredNoteDocument` ↔ Note structurée
- `Notes` → Liste notes
- `SelectedNote` ↔ Sélection
- Commandes : `StructurerCommand`, `SaveCommand`, `EditCommand`, `DeleteCommand`, `CancelCommand`

---

### 4️⃣ LettersControl.xaml (Priorité: 🟡 MOYENNE)

**Taille estimée** : 300-400 lignes

**Contenu** :
- ComboBox sélection modèle courrier
- Toggle "Adaptation IA"
- Zone édition courrier (RichTextBox)
- Liste courriers (ListBox)
- Boutons : Modifier, Sauvegarder, Supprimer, Imprimer

**ViewModel** : ⏳ À créer `LetterViewModel`

**Propriétés exposées** :
```csharp
public ObservableCollection<LetterItem> Letters { get; set; }
public LetterItem SelectedLetter { get; set; }
public FlowDocument LetterDocument { get; set; }
public bool IsAutoAdaptEnabled { get; set; }
```

---

### 5️⃣ AttestationsControl.xaml (Priorité: 🔵 BASSE)

**Taille estimée** : 300-400 lignes

**Contenu** :
- ComboBox type attestation
- Bouton "Générer attestation"
- Bouton "Générer attestation personnalisée"
- Preview attestation (RichTextBox)
- Liste attestations (ListBox)
- Boutons : Modifier, Supprimer, Imprimer

**ViewModel** : ⏳ À créer `AttestationViewModel`

---

### 6️⃣ DocumentsControl.xaml (Priorité: 🟡 MOYENNE)

**Taille estimée** : 400-500 lignes

**Contenu** :
- Zone drag & drop
- Bouton "Parcourir fichiers"
- Bouton "Ouvrir fenêtre drag & drop"
- DataGrid documents
- Liste catégories (ListBox)
- Compteur documents
- Zone synthèse document
- Boutons : Synthèse, Enregistrer synthèse, Supprimer synthèse

**ViewModel** : ⏳ À créer `DocumentViewModel`

---

### 7️⃣ FormulairesControl.xaml (Priorité: 🔵 BASSE)

**Taille estimée** : 200-300 lignes

**Contenu** :
- ComboBox type formulaire (PAI, MDPH)
- Bouton "Ouvrir modèle PAI"
- Bouton "Pré-remplir avec l'IA" (MDPH)
- Liste formulaires (DataGrid)
- Zone synthèse formulaire
- Bouton "Supprimer"

**ViewModel** : ⏳ À créer `FormulaireViewModel`

---

### 8️⃣ OrdonnancesControl.xaml (Priorité: ✅ HAUTE)

**Taille estimée** : 200-300 lignes

**Contenu** :
- Bouton "Nouvelle ordonnance IDE"
- Liste ordonnances (ListBox)
- Preview ordonnance (RichTextBox)
- Boutons : Supprimer, Ouvrir

**ViewModel** : ✅ `OrdonnanceViewModel` (déjà créé)

**Propriétés exposées** :
```xml
<UserControl DataContext="{Binding OrdonnanceViewModel}">
```

---

### 9️⃣ SynthesisControl.xaml (Priorité: 🔵 BASSE)

**Taille estimée** : 200-300 lignes

**Contenu** :
- Bouton "Générer/Actualiser Synthèse"
- Label "Dernière mise à jour"
- Zone preview synthèse (RichTextBox)

**ViewModel** : ⏳ À créer `SynthesisViewModel`

---

### 🔟 ChatControl.xaml (Priorité: 🟡 MOYENNE)

**Taille estimée** : 300-400 lignes

**Contenu** :
- Zone messages chat (StackPanel)
- TextBox saisie message
- Bouton "Envoyer"
- Liste échanges sauvegardés
- Boutons : Voir, Supprimer

**ViewModel** : ⏳ À créer `ChatViewModel`

---

## 🗺️ Roadmap d'Implémentation

### Phase 1 : UserControls avec ViewModel existant (2-3h)
**Objectif** : Extraire les contrôles dont le ViewModel existe déjà

1. ✅ **NotesControl.xaml**
   - ViewModel : ✅ `NoteViewModel`
   - Complexité : 🟡 Moyenne
   - Gain : ~500 lignes

2. ✅ **PatientSearchControl.xaml**
   - ViewModel : ✅ `PatientSearchViewModel`
   - Complexité : 🟢 Facile
   - Gain : ~300 lignes

3. ✅ **OrdonnancesControl.xaml**
   - ViewModel : ✅ `OrdonnanceViewModel`
   - Complexité : 🟢 Facile
   - Gain : ~300 lignes

**Gain Phase 1** : ~1100 lignes (3112 → 2012)

---

### Phase 2 : UserControls sans ViewModel (4-5h)
**Objectif** : Créer ViewModels puis extraire UserControls

4. ⏳ **LettersControl.xaml**
   - ViewModel : ⏳ Créer `LetterViewModel`
   - Complexité : 🟡 Moyenne
   - Gain : ~400 lignes

5. ⏳ **ChatControl.xaml**
   - ViewModel : ⏳ Créer `ChatViewModel`
   - Complexité : 🟡 Moyenne
   - Gain : ~400 lignes

6. ⏳ **DocumentsControl.xaml**
   - ViewModel : ⏳ Créer `DocumentViewModel`
   - Complexité : 🔴 Difficile (drag & drop)
   - Gain : ~500 lignes

**Gain Phase 2** : ~1300 lignes (2012 → 712)

---

### Phase 3 : UserControls simples (2h)

7. ⏳ **PatientCardControl.xaml**
   - ViewModel : ⏳ Créer `PatientCardViewModel`
   - Complexité : 🟢 Facile
   - Gain : ~150 lignes

8. ⏳ **AttestationsControl.xaml**
   - ViewModel : ⏳ Créer `AttestationViewModel`
   - Complexité : 🟡 Moyenne
   - Gain : ~400 lignes

9. ⏳ **FormulairesControl.xaml**
   - ViewModel : ⏳ Créer `FormulaireViewModel`
   - Complexité : 🟢 Facile
   - Gain : ~300 lignes

10. ⏳ **SynthesisControl.xaml**
    - ViewModel : ⏳ Créer `SynthesisViewModel`
    - Complexité : 🟢 Facile
    - Gain : ~300 lignes

**Gain Phase 3** : ~1150 lignes (712 → ~550-600)

---

## 📊 Résultat Final Estimé

| Avant | Après | Gain |
|-------|-------|------|
| **3112 lignes** | **~600 lignes** | **-2512 lignes (-80%)** |

---

## 🔧 Solution 2 : ResourceDictionary Styles

### Objectif
Centraliser les styles réutilisables pour éviter la duplication

### Structure Proposée

```
MedCompanion/Styles/
├── ButtonStyles.xaml       (Styles boutons)
├── TextBoxStyles.xaml      (Styles TextBox/RichTextBox)
├── DataGridStyles.xaml     (Styles DataGrid)
├── ListBoxStyles.xaml      (Styles ListBox)
├── ComboBoxStyles.xaml     (Styles ComboBox)
└── Colors.xaml             (Palette couleurs)
```

### Exemple : ButtonStyles.xaml

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    
    <!-- Bouton principal (bleu) -->
    <Style x:Key="PrimaryButton" TargetType="Button">
        <Setter Property="Background" Value="#2196F3"/>
        <Setter Property="Foreground" Value="White"/>
        <Setter Property="FontSize" Value="13"/>
        <Setter Property="Padding" Value="15,8"/>
        <Setter Property="BorderThickness" Value="0"/>
        <Setter Property="Cursor" Value="Hand"/>
    </Style>
    
    <!-- Bouton succès (vert) -->
    <Style x:Key="SuccessButton" TargetType="Button">
        <Setter Property="Background" Value="#27AE60"/>
        <Setter Property="Foreground" Value="White"/>
        <Setter Property="FontSize" Value="13"/>
        <Setter Property="Padding" Value="15,8"/>
        <Setter Property="BorderThickness" Value="0"/>
        <Setter Property="Cursor" Value="Hand"/>
    </Style>
    
    <!-- Bouton danger (rouge) -->
    <Style x:Key="DangerButton" TargetType="Button">
        <Setter Property="Background" Value="#E74C3C"/>
        <Setter Property="Foreground" Value="White"/>
        <Setter Property="FontSize" Value="13"/>
        <Setter Property="Padding" Value="15,8"/>
        <Setter Property="BorderThickness" Value="0"/>
        <Setter Property="Cursor" Value="Hand"/>
    </Style>
    
</ResourceDictionary>
```

### Usage dans App.xaml

```xml
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceDictionary Source="Styles/ButtonStyles.xaml"/>
            <ResourceDictionary Source="Styles/TextBoxStyles.xaml"/>
            <ResourceDictionary Source="Styles/DataGridStyles.xaml"/>
            <ResourceDictionary Source="Styles/Colors.xaml"/>
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Application.Resources>
```

### Avantages
✅ Styles réutilisables partout  
✅ Cohérence visuelle garantie  
✅ Maintenance centralisée  
✅ Moins de duplication code

---

## 🏗️ Solution 3 : Navigation par Régions (Avancée)

### Principe
Utiliser `ContentControl` pour afficher dynamiquement les différentes vues

### Implémentation

```xml
<Window>
    <Grid>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="200"/> <!-- Menu -->
            <ColumnDefinition Width="*"/>    <!-- Contenu -->
        </Grid.ColumnDefinitions>
        
        <!-- Menu Navigation -->
        <StackPanel Grid.Column="0">
            <Button Content="Notes" Command="{Binding NavigateToNotesCommand}"/>
            <Button Content="Courriers" Command="{Binding NavigateToLettersCommand}"/>
            <Button Content="Documents" Command="{Binding NavigateToDocumentsCommand}"/>
            <!-- ... -->
        </StackPanel>
        
        <!-- Zone de contenu dynamique -->
        <ContentControl Grid.Column="1" 
                        Content="{Binding CurrentView}"/>
    </Grid>
</Window>
```

### ViewModel

```csharp
public class MainViewModel : ViewModelBase
{
    private object _currentView;
    public object CurrentView
    {
        get => _currentView;
        set => SetProperty(ref _currentView, value);
    }
    
    public ICommand NavigateToNotesCommand { get; }
    public ICommand NavigateToLettersCommand { get; }
    
    public MainViewModel()
    {
        NavigateToNotesCommand = new RelayCommand(_ => 
            CurrentView = new NotesControl());
            
        NavigateToLettersCommand = new RelayCommand(_ => 
            CurrentView = new LettersControl());
    }
}
```

### Avantages
✅ Navigation fluide entre sections  
✅ Charge uniquement la vue active (performance)  
✅ Séparation claire des responsabilités

### Inconvénients
⚠️ Plus complexe à implémenter  
⚠️ Nécessite repenser l'architecture complète

---

## 📝 Guide d'Implémentation UserControl

### Étape 1 : Créer le UserControl

**Fichier** : `MedCompanion/Views/Notes/NotesControl.xaml`

```xml
<UserControl x:Class="MedCompanion.Views.Notes.NotesControl"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    
    <!-- Contenu extrait de MainWindow.xaml -->
    <Grid>
        <!-- ... -->
    </Grid>
    
</UserControl>
```

### Étape 2 : Code-behind

**Fichier** : `MedCompanion/Views/Notes/NotesControl.xaml.cs`

```csharp
namespace MedCompanion.Views.Notes
{
    public partial class NotesControl : UserControl
    {
        public NotesControl()
        {
            InitializeComponent();
        }
    }
}
```

### Étape 3 : Intégrer dans MainWindow

```xml
<Window xmlns:notes="clr-namespace:MedCompanion.Views.Notes">
    
    <Grid>
        <!-- Utiliser le UserControl -->
        <notes:NotesControl DataContext="{Binding NoteViewModel}"/>
    </Grid>
    
</Window>
```

### Étape 4 : Gérer les Événements

**Si le UserControl doit communiquer avec MainWindow** :

```csharp
// Dans NotesControl.xaml.cs
public event EventHandler<string> StatusChanged;

private void OnStatusChanged(string message)
{
    StatusChanged?.Invoke(this, message);
}

// Dans MainWindow.xaml.cs
notesControl.StatusChanged += (s, msg) => {
    StatusTextBlock.Text = msg;
};
```

---

## ⚠️ Points d'Attention

### 1. DataContext
- Toujours passer le bon ViewModel au UserControl
- Utiliser `{Binding PropertyName}` dans le UserControl

### 2. Événements
- Les événements entre UserControl et parent doivent être explicites
- Préférer les Commands quand possible

### 3. Tests
- Tester chaque UserControl après extraction
- Vérifier que tous les bindings fonctionnent

### 4. Git
- Faire un commit après chaque UserControl extrait
- Message clair : "Extract NotesControl UserControl"

---

## 📅 Calendrier Recommandé

| Phase | Durée | Contenu |
|-------|-------|---------|
| **Phase 1** | 2-3h | UserControls avec ViewModel existant |
| **Phase 2** | 4-5h | Créer ViewModels + UserControls |
| **Phase 3** | 2h | UserControls simples |
| **Polish** | 2h | ResourceDictionary + nettoyage |
| **TOTAL** | **10-12h** | Refactorisation complète |

---

## 🎯 Bénéfices Attendus

### Maintenabilité
✅ Code organisé par fonctionnalité  
✅ Fichiers de taille raisonnable (<500 lignes)  
✅ Facilité de navigation dans le code

### Réutilisabilité
✅ UserControls réutilisables dans d'autres projets  
✅ Styles centralisés et cohérents

### Performance
✅ Visual Studio plus réactif  
✅ Compilation plus rapide  
✅ Moins de risques de conflits Git

### Collaboration
✅ Plusieurs développeurs peuvent travailler simultanément  
✅ Moins de conflits de merge

---

## 📚 Ressources

### Documentation Microsoft
- [UserControl (WPF)](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/usercontrol)
- [ResourceDictionary](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/systems/xaml-resources-define)
- [Styles and Templates](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/controls/styles-templates-overview)

### Exemples de Projets
- [WPF MVVM Sample](https://github.com/microsoft/WPF-Samples)

---

## ⏰ Quand Faire Cette Refactorisation ?

### ❌ PAS MAINTENANT
Cette refactorisation ne doit **PAS** être faite en parallèle de la migration MVVM pour éviter les conflits.

### ✅ PLUS TARD
**Ordre recommandé des tâches** :

1. 🚨 **Priorité 1** : Fonctionnalité PATH
2. 🔄 **Priorité 2** : Finir migration MVVM (LetterViewModel, ChatViewModel, etc.)
3. 📐 **Priorité 3** : Refactorisation XAML (ce document)

---

**Dernière mise à jour** : 25/10/2025 20:53  
**Statut** : 📋 Document de référence pour refactorisation future  
**Maintenu par** : Équipe de développement MedCompanion
