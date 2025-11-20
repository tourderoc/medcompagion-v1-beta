# 🧹 PLAN DE NETTOYAGE MVVM - PatientSearch

## 📊 ANALYSE COMPLÈTE

### Références trouvées (23 occurrences)

#### ✅ Dans PatientSearchViewModel.cs (À GARDER)
- `_selectedSuggestionIndex` : Variable du ViewModel
- `SelectedSuggestionIndex` : Propriété bindée

#### ❌ Dans MainWindow.xaml.cs (À NETTOYER)

**Variables obsolètes (2)** :
- Ligne ~45 : `private List<PatientIndexEntry> _currentSuggestions = new();`
- Ligne ~46 : `private int _selectedSuggestionIndex = -1;`

**Event handlers obsolètes (3)** :
- Ligne ~211 : `SearchBox.TextChanged += SearchBox_TextChanged;`
- Ligne ~212 : `SearchBox.KeyDown += SearchBox_KeyDown;`
- Ligne ~217 : `SuggestList.SelectionChanged += SuggestList_SelectionChanged;`

**Méthodes obsolètes (3)** :
1. **SearchBox_TextChanged** (ligne ~236)
   - Remplacée par `PatientSearchViewModel.OnSearchTextChanged()`
   - Déjà marquée OBSOLETE

2. **SearchBox_KeyDown** (ligne ~287)
   - Navigation ↑↓ avec `_selectedSuggestionIndex`
   - Remplacée par `PatientSearchViewModel.NavigateUp/Down()`
   - ⚠️ **PROBLÈME** : Pas encore connectée au XAML !

3. **SuggestList_SelectionChanged** (ligne ~318)
   - Remplacée par binding `SelectedSuggestionIndex`
   - Déjà marquée OBSOLETE

---

## 🎯 STRATÉGIE DE NETTOYAGE

### Phase 1 : Connecter Navigation Clavier (URGENT)

**Problème** : `SearchBox_KeyDown` utilise `_selectedSuggestionIndex` pour ↑↓, mais le ViewModel a déjà `NavigateUp/Down()` !

**Solution** : Connecter les touches au ViewModel dans XAML

```xml
<!-- Dans MainWindow.xaml, SearchBox -->
<TextBox x:Name="SearchBox" ...>
    <TextBox.InputBindings>
        <KeyBinding Key="Down" Command="{Binding NavigateDownCommand}" />
        <KeyBinding Key="Up" Command="{Binding NavigateUpCommand}" />
        <KeyBinding Key="Enter" Command="{Binding ValidateCommand}" />
        <KeyBinding Key="Escape" Command="{Binding ClosePopupCommand}" />
    </TextBox.InputBindings>
</TextBox>
```

**À ajouter dans PatientSearchViewModel.cs** :
```csharp
public ICommand NavigateDownCommand { get; }
public ICommand NavigateUpCommand { get; }
public ICommand ClosePopupCommand { get; }

// Dans le constructeur :
NavigateDownCommand = new RelayCommand(_ => NavigateDown(), _ => IsPopupOpen && Suggestions.Count > 0);
NavigateUpCommand = new RelayCommand(_ => NavigateUp(), _ => IsPopupOpen && SelectedSuggestionIndex > 0);
ClosePopupCommand = new RelayCommand(_ => ClosePopup());
```

### Phase 2 : Supprimer Event Handlers

Dans `WireSearchEvents()` (ligne ~209), **SUPPRIMER** :
```csharp
❌ SearchBox.TextChanged += SearchBox_TextChanged;
❌ SearchBox.KeyDown += SearchBox_KeyDown;
❌ SuggestList.SelectionChanged += SuggestList_SelectionChanged;
```

### Phase 3 : Supprimer Méthodes Obsolètes

**SUPPRIMER complètement** (lignes ~236-344) :
- `SearchBox_TextChanged()`
- `SearchBox_KeyDown()`
- `SuggestList_SelectionChanged()`

### Phase 4 : Supprimer Variables Obsolètes

**SUPPRIMER** (lignes ~45-46) :
```csharp
❌ private List<PatientIndexEntry> _currentSuggestions = new();
❌ private int _selectedSuggestionIndex = -1;
```

---

## 📝 CHECKLIST D'EXÉCUTION

### Étape 1 : Ajouter Commandes au ViewModel
- [ ] Ajouter `NavigateDownCommand` property
- [ ] Ajouter `NavigateUpCommand` property
- [ ] Ajouter `ClosePopupCommand` property
- [ ] Initialiser dans le constructeur
- [ ] Compiler (`dotnet build`)

### Étape 2 : Connecter XAML
- [ ] Ajouter `<TextBox.InputBindings>` à SearchBox
- [ ] Compiler (`dotnet build`)
- [ ] **TESTER** : ↑↓ doit fonctionner

### Étape 3 : Supprimer Event Handlers
- [ ] Supprimer 3 lignes `+=` dans `WireSearchEvents()`
- [ ] Compiler (`dotnet build`)

### Étape 4 : Supprimer Méthodes
- [ ] Supprimer `SearchBox_TextChanged()`
- [ ] Supprimer `SearchBox_KeyDown()`
- [ ] Supprimer `SuggestList_SelectionChanged()`
- [ ] Compiler (`dotnet build`)

### Étape 5 : Supprimer Variables
- [ ] Supprimer `_currentSuggestions`
- [ ] Supprimer `_selectedSuggestionIndex`
- [ ] Compiler (`dotnet build`)

### Étape 6 : Tests Finaux
- [ ] Recherche patient fonctionne
- [ ] Navigation ↑↓ fonctionne
- [ ] Validation Entrée fonctionne
- [ ] Fermeture Escape fonctionne

### Étape 7 : Commit Git
- [ ] `git add .`
- [ ] `git commit -m "Clean: Remove obsolete patient search code (MVVM complete)"`

---

## ⚠️ RISQUES IDENTIFIÉS

### Risque 1 : InputBindings peut ne pas fonctionner
**Solution de secours** : Garder `SearchBox_KeyDown` mais le simplifier pour appeler le ViewModel

### Risque 2 : CanExecute sur les commandes
**Important** : `NavigateDownCommand` doit vérifier `IsPopupOpen && Suggestions.Count > 0`

### Risque 3 : Focus clavier
**Test** : Vérifier que SearchBox garde le focus pendant la navigation

---

## 🎉 RÉSULTAT ATTENDU

**Avant** : ~150 lignes de code obsolète
**Après** : Code clean, 100% MVVM

**Bénéfices** :
- ✅ Séparation View/ViewModel complète
- ✅ Testabilité (ViewModel indépendant)
- ✅ Maintenabilité (une seule source de vérité)
- ✅ Moins de bugs (pas de duplication)

---

## 📞 POINT BLOQUANT ACTUEL

**AVANT de supprimer quoi que ce soit**, il FAUT :
1. Ajouter les 3 commandes au ViewModel
2. Connecter au XAML avec InputBindings
3. **TESTER** que ça fonctionne

**Sinon** → Navigation clavier cassée ! ⚠️
