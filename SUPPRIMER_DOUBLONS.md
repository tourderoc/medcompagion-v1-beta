# ⚠️ DOUBLONS À SUPPRIMER DE MainWindow.xaml.cs

## Instructions

Ouvrez `MedCompanion/MainWindow.xaml.cs` et **SUPPRIMEZ complètement** ces 8 méthodes (elles sont déjà dans `MainWindow.Patient.cs`) :

### 1. StructuredNoteText_TextChanged
Ligne ~431 - Cherchez :
```csharp
private void StructuredNoteText_TextChanged(object sender, TextChangedEventArgs e)
{
    // Si on est en mode édition...
}
```

### 2. UpdateMemoryIndicator
Ligne ~1021 - Cherchez :
```csharp
private void UpdateMemoryIndicator()
{
    // Pour l'instant, cette méthode...
}
```

### 3. SaveExchangeButton_Click
Ligne ~1031 - Cherchez :
```csharp
private void SaveExchangeButton_Click(object sender, RoutedEventArgs e)
{
    if (_selectedPatient == null...
}
```

### 4. ViewSavedExchangeBtn_Click
Ligne ~1079 - Cherchez :
```csharp
private void ViewSavedExchangeBtn_Click(object sender, RoutedEventArgs e)
{
    if (SavedExchangesList.SelectedItem...
}
```

### 5. DeleteSavedExchangeBtn_Click  
Ligne ~1098 - Cherchez :
```csharp
private void DeleteSavedExchangeBtn_Click(object sender, RoutedEventArgs e)
{
    if (SavedExchangesList.SelectedItem...
}
```

### 6. RefreshSavedExchangesList
Ligne ~1135 - Cherchez :
```csharp
private void RefreshSavedExchangesList()
{
    SavedExchangesList.ItemsSource = null;
}
```

### 7. LoadSavedExchanges
Ligne ~1144 - Cherchez :
```csharp
private void LoadSavedExchanges()
{
    if (_selectedPatient == null)
}
```

### 8. LoadPatientSynthesis
Ligne ~4399 - Cherchez :
```csharp
private void LoadPatientSynthesis()
{
    if (_selectedPatient == null)
}
```

## ✅ Après suppression

Compilez avec :
```bash
cd MedCompanion
dotnet build
```

Vous ne devriez plus avoir d'erreurs de doublons !
