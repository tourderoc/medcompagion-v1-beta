# 🎯 Bindings XAML pour OrdonnanceViewModel

## 📍 Localisation dans MainWindow.xaml

Cherchez l'onglet "💊 Ordonnances" (ligne ~4500 approximativement)

```xml
<TabItem Header="💊 Ordonnances">
```

---

## ✏️ Modifications à Faire

### 1. Ajouter le DataContext au Grid principal

**AVANT** (ligne qui contient `<TabItem Header="💊 Ordonnances">`):
```xml
<TabItem Header="💊 Ordonnances">
    <Grid Margin="0,10,0,0">
```

**APRÈS**:
```xml
<TabItem Header="💊 Ordonnances">
    <Grid Margin="0,10,0,0" DataContext="{Binding OrdonnanceViewModel}">
```

---

### 2. Binding de la Liste des Ordonnances

Cherchez `<ListBox x:Name="OrdonnancesList"` dans l'onglet Ordonnances.

**AJOUTER** l'attribut `ItemsSource`:
```xml
<ListBox x:Name="OrdonnancesList"
         ItemsSource="{Binding Ordonnances}"
         SelectedItem="{Binding SelectedOrdonnance}"
         BorderThickness="0"
         ...
```

---

### 3. Binding du Compteur

Cherchez `<TextBlock x:Name="OrdonnancesCountLabel"`

**AJOUTER**:
```xml
<TextBlock x:Name="OrdonnancesCountLabel" 
           Text="{Binding OrdonnancesCount}"
           FontSize="12" 
           ...
```

---

### 4. Binding de la Preview

Cherchez `<RichTextBox x:Name="OrdonnancePreviewText"`

**REMPLACER PAR** un Binding Markdown (si vous utilisez le convertisseur):
```xml
<RichTextBox x:Name="OrdonnancePreviewText"
             Document="{Binding PreviewMarkdown, Converter={StaticResource MarkdownToFlowDocumentConverter}}"
             IsReadOnly="True"
             ...
```

OU si vous préférez garder le binding simple sans convertisseur, gardez tel quel et gérez dans le code-behind.

---

### 5. Binding du Bouton Générer IDE

Cherchez `<Button x:Name="IDEOrdonnanceButton"`

**AJOUTER**:
```xml
<Button x:Name="IDEOrdonnanceButton"
        Command="{Binding GenerateIDECommand}"
        Content="🏥 IDE"
        ...
```

---

### 6. Binding du Bouton Supprimer

Cherchez `<Button x:Name="SupprimerOrdonnanceButton"`

**AJOUTER**:
```xml
<Button x:Name="SupprimerOrdonnanceButton"
        Command="{Binding DeleteCommand}"
        Content="🗑️ Supprimer"
        ...
```

---

### 7. Binding du Bouton Ouvrir DOCX

Cherchez `<Button x:Name="ImprimerOrdonnanceButton"`

**AJOUTER**:
```xml
<Button x:Name="ImprimerOrdonnanceButton"
        Command="{Binding OpenDocxCommand}"
        Content="🖨️ Ouvrir DOCX"
        ...
```

---

## ⚠️ NOTE IMPORTANTE

Les bindings ci-dessus sont **OPTIONNELS**. L'application fonctionne déjà avec le ViewModel initialisé dans MainWindow.xaml.cs !

Les Event Handlers dans le code-behind peuvent appeler les méthodes du ViewModel :
- `IDEOrdonnanceButton_Click` → Déclenche `GenerateIDERequested` (déjà connecté)
- `SupprimerOrdonnanceButton_Click` → Peut appeler `OrdonnanceViewModel.DeleteSelectedOrdonnance()`
- etc.

---

## 🚀 Test Rapide

1. Sauvegardez ces modifications
2. Compilez : `dotnet build`
3. Lancez l'application
4. Sélectionnez un patient
5. Allez dans l'onglet Ordonnances
6. Cliquez sur "🏥 IDE" → Le dialogue devrait s'ouvrir

---

## 📝 Résumé

- ✅ ViewModel créé et compilé
- ✅ Intégré dans MainWindow.xaml.cs (constructeur)
- ⏳ Bindings XAML (optionnel, peut être fait progressivement)
- ⏳ Tests dans l'application

**L'application fonctionne déjà même sans les bindings XAML !**
Les bindings XAML sont juste une amélioration pour suivre le pattern MVVM pur.
