# Phase 3 : Bindings XAML pour AttestationViewModel

## 🎯 Objectif
Connecter AttestationViewModel aux contrôles XAML existants dans MainWindow.xaml

---

## 📋 Étape 1 : Ajouter AttestationViewModel dans MainWindow.xaml.cs

### 1.1 Déclarer les services nécessaires

Ajouter après les déclarations de services existantes (vers ligne 150) :

```csharp
private readonly RichTextBoxService _richTextBoxService;
private readonly DialogService _dialogService;
private readonly FileOperationService _fileOperationService;
```

### 1.2 Ajouter la propriété AttestationViewModel

Ajouter après la propriété `NoteViewModel` (vers ligne 180) :

```csharp
public ViewModels.AttestationViewModel AttestationViewModel { get; }
```

### 1.3 Initialiser les services dans le constructeur

Dans le constructeur `MainWindow()`, après l'initialisation de `_pathService` :

```csharp
// Initialiser les services techniques
_richTextBoxService = new Services.RichTextBoxService();
_dialogService = new Services.DialogService();
_fileOperationService = new Services.FileOperationService();
```

### 1.4 Créer AttestationViewModel

Après l'initialisation de `NoteViewModel` :

```csharp
// Créer AttestationViewModel avec tous les services
AttestationViewModel = new ViewModels.AttestationViewModel(
    _attestationService,
    _richTextBoxService,
    _dialogService,
    _fileOperationService,
    _pathService
);
```

---

## 📋 Étape 2 : Bindings XAML dans MainWindow.xaml

### 2.1 ComboBox Types d'Attestations

**Localiser** (vers ligne 2800) :
```xml
<ComboBox x:Name="AttestationTypeCombo"
          Grid.Row="1"
          ...
```

**Remplacer par** :
```xml
<ComboBox x:Name="AttestationTypeCombo"
          Grid.Row="1"
          ItemsSource="{Binding AttestationViewModel.AvailableTemplates}"
          SelectedItem="{Binding AttestationViewModel.SelectedTemplate}"
          DisplayMemberPath="DisplayName"
          ...
```

### 2.2 Liste des Attestations

**Localiser** :
```xml
<ListBox x:Name="AttestationsList"
         BorderThickness="0"
         ...
```

**Remplacer par** :
```xml
<ListBox x:Name="AttestationsList"
         ItemsSource="{Binding AttestationViewModel.Attestations}"
         SelectedItem="{Binding AttestationViewModel.SelectedAttestation}"
         DisplayMemberPath="DisplayText"
         BorderThickness="0"
         ...
```

### 2.3 Boutons - Commandes

**Bouton Générer** :
```xml
<Button x:Name="GenererAttestationButton"
        Content="✨ Générer attestation"
        Command="{Binding AttestationViewModel.GenerateCommand}"
        ...
```

**Bouton Personnalisée (IA)** :
```xml
<Button x:Name="GenerateCustomAttestationButton"
        Content="🤖 Attestation personnalisée (IA)"
        Command="{Binding AttestationViewModel.GenerateCustomCommand}"
        ...
```

**Bouton Modifier** :
```xml
<Button x:Name="ModifierAttestationButton"
        Content="✏️ Modifier"
        Command="{Binding AttestationViewModel.ModifyCommand}"
        ...
```

**Bouton Sauvegarder (modifications)** :
```xml
<Button x:Name="SauvegarderAttestationButton"
        Content="💾 Sauvegarder"
        Command="{Binding AttestationViewModel.SaveModifiedCommand}"
        Visibility="{Binding AttestationViewModel.IsModifying, Converter={StaticResource BoolToVisibilityConverter}}"
        ...
```

**Bouton Annuler (modifications)** :
```xml
<Button x:Name="AnnulerModificationButton"
        Content="❌ Annuler"
        Command="{Binding AttestationViewModel.CancelModifyCommand}"
        Visibility="{Binding AttestationViewModel.IsModifying, Converter={StaticResource BoolToVisibilityConverter}}"
        ...
```

**Bouton Supprimer** :
```xml
<Button x:Name="SupprimerAttestationButton"
        Content="🗑️ Supprimer"
        Command="{Binding AttestationViewModel.DeleteCommand}"
        ...
```

**Bouton Imprimer** :
```xml
<Button x:Name="ImprimerAttestationButton"
        Content="🖨️ Imprimer"
        Command="{Binding AttestationViewModel.PrintCommand}"
        ...
```

**Bouton Ouvrir** :
```xml
<Button x:Name="OuvrirAttestationButton"
        Content="📄 Ouvrir"
        Command="{Binding AttestationViewModel.OpenFileCommand}"
        ...
```

**Bouton Afficher dans explorateur** :
```xml
<Button x:Name="ShowInExplorerButton"
        Content="📁 Explorateur"
        Command="{Binding AttestationViewModel.ShowInExplorerCommand}"
        ...
```

### 2.4 RichTextBox Aperçu

**Note** : Le RichTextBox ne peut pas être bindé directement en XAML (limitation WPF).
On garde les event handlers pour le moment, qui utiliseront `AttestationViewModel.AttestationMarkdown`.

---

## 📋 Étape 3 : Configuration DataContext

### Dans MainWindow.xaml.cs

Ajouter dans le constructeur, après l'initialisation des ViewModels :

```csharp
// Configurer DataContext pour la section Attestations
AttestationTypeCombo.DataContext = this;
AttestationsList.DataContext = this;
GenererAttestationButton.DataContext = this;
// ... (tous les autres boutons)
```

**OU** (plus simple) définir le DataContext au niveau du parent `Grid` :

Dans MainWindow.xaml, trouver le `Grid` parent de la section Attestations et ajouter :

```xml
<Grid DataContext="{Binding RelativeSource={RelativeSource AncestorType=Window}}">
    <!-- Tous les contrôles Attestations ici -->
</Grid>
```

---

## 📋 Étape 4 : Gestion du Patient Courant

### Dans MainWindow.xaml.cs

Ajouter dans la méthode `LoadPatientData()` (ou équivalent) :

```csharp
private void LoadPatientData(PatientMetadata patient)
{
    // ... code existant ...
    
    // Mettre à jour AttestationViewModel avec le patient
    AttestationViewModel.CurrentPatient = patient;
}
```

Et dans la méthode de réinitialisation :

```csharp
private void ResetAll()
{
    // ... code existant ...
    
    AttestationViewModel.Reset();
}
```

---

## 📋 Étape 5 : Nettoyer le Code-Behind

### Dans MainWindow.xaml.cs

**Supprimer** tous les anciens event handlers Attestations :
- `GenererAttestationButton_Click`
- `GenerateCustomAttestationButton_Click`
- `AttestationsList_SelectionChanged`
- `AttestationsList_MouseDoubleClick`
- `ModifierAttestationButton_Click`
- `SupprimerAttestationButton_Click`
- `ImprimerAttestationButton_Click`
- `OuvrirAttestationButton_Click`
- `SauvegarderAttestationModifiee`
- `RefreshAttestationsList`
- Etc.

**Supprimer** dans MainWindow.xaml les attributs d'événements :
- `Click="..."`
- `SelectionChanged="..."`
- `MouseDoubleClick="..."`

---

## ✅ Checklist de Vérification

- [ ] AttestationViewModel déclaré comme propriété dans MainWindow.xaml.cs
- [ ] Services techniques créés et injectés
- [ ] ComboBox Types bindé à AvailableTemplates et SelectedTemplate
- [ ] ListBox Attestations bindé à Attestations et SelectedAttestation
- [ ] Tous les boutons bindés aux Commands
- [ ] DataContext configuré correctement
- [ ] Patient courant passé au ViewModel (CurrentPatient)
- [ ] Ancien code-behind supprimé
- [ ] Compilation réussie (0 erreur)
- [ ] Tests manuels : Sélection patient → Voir templates → Génération

---

## 🎯 Résultat Attendu

Après cette phase :
- ✅ Attestations 100% MVVM
- ✅ Aucun event handler dans code-behind
- ✅ Tout passe par Commands et Bindings
- ✅ Services techniques utilisés
- ✅ Code maintenable et testable

**Réduction de code** :
- MainWindow.xaml.cs : -400 lignes (événements Attestations supprimés)

---

## 📝 Notes Importantes

1. **RichTextBox** : Garde les event handlers pour manipulation FlowDocument (limitation WPF)
2. **Dialogs** : Gérés par DialogService dans le ViewModel
3. **Fichiers** : Gérés par FileOperationService dans le ViewModel
4. **Markdown** : Géré par RichTextBoxService (pas encore utilisé ici, mais disponible)

---

**Durée estimée** : 1-2h
**Difficulté** : Moyenne (beaucoup de bindings à faire, mais répétitif)
