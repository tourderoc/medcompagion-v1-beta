# Implémentation UI de Recherche Patient

## État actuel

✅ **Backend créé:**
- `Models/PatientMetadata.cs` - Modèles de données
- `Services/PatientIndexService.cs` - Service d'indexation et recherche
- `Dialogs/CreatePatientDialog.xaml(.cs)` - Dialog de création patient

## Ce qui reste à faire

### 1. MainWindow.xaml - Refonte complète

**Nouvelles sections à ajouter:**

```xml
<!-- HEADER avec barre de recherche -->
<Grid Grid.Column="2">
    <TextBox x:Name="SearchBox" TextChanged="SearchBox_TextChanged" KeyDown="SearchBox_KeyDown"/>
    <Popup x:Name="SuggestionsPopup" PlacementTarget="{Binding ElementName=SearchBox}">
        <ListBox x:Name="SuggestionsList" SelectionChanged="SuggestionsList_SelectionChanged"/>
    </Popup>
    <Button x:Name="ValiderPatientButton" Click="ValiderPatientButton_Click"/>
</Grid>

<!-- CARTE PATIENT (visible si patient sélectionné) -->
<Border x:Name="PatientCard" Visibility="Collapsed">
    <TextBlock x:Name="PatientNameLabel"/>
    <TextBlock x:Name="PatientAgeLabel"/>
    <TextBlock x:Name="PatientDobLabel"/>
    <TextBlock x:Name="PatientSexeLabel"/>
    <Button Click="OuvrirDossierPatientButton_Click"/>
</Border>

<!-- NOUVEAU: Liste notes patient (colonne gauche) -->
<ListBox x:Name="PatientNotesList" SelectionChanged="PatientNotesList_SelectionChanged">
    <ListBox.ItemTemplate>
        <DataTemplate>
            <StackPanel>
                <TextBlock Text="{Binding DateLabel}"/>
                <TextBlock Text="{Binding Preview}"/>
            </StackPanel>
        </DataTemplate>
    </ListBox.ItemTemplate>
</ListBox>
```

### 2. MainWindow.xaml.cs - Ajouts nécessaires

```csharp
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.Dialogs;

public partial class MainWindow : Window
{
    private readonly PatientIndexService _patientIndexService;
    private PatientIndexEntry? _selectedPatient;
    private List<PatientIndexEntry> _currentSuggestions = new();
    private int _selectedSuggestionIndex = -1;
    
    public MainWindow()
    {
        InitializeComponent();
        _patientIndexService = new PatientIndexService();
        
        // Initialiser l'index au démarrage
        InitializePatientIndex();
    }
    
    private async void InitializePatientIndex()
    {
        StatusTextBlock.Text = "⏳ Chargement de l'index patients...";
        await _patientIndexService.ScanAsync();
        _patientIndexService.StartWatching();
        StatusTextBlock.Text = "✓ Prêt";
    }
    
    // === RECHERCHE ===
    
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text;
        
        // Gérer placeholder
        if (string.IsNullOrEmpty(query))
        {
            SearchPlaceholder.Visibility = Visibility.Visible;
            SuggestionsPopup.IsOpen = false;
            return;
        }
        
        SearchPlaceholder.Visibility = Visibility.Collapsed;
        
        // Rechercher seulement après 3 caractères
        if (query.Length < 3)
        {
            SuggestionsPopup.IsOpen = false;
            return;
        }
        
        // Rechercher les patients
        _currentSuggestions = _patientIndexService.Search(query, 10);
        
        // Préparer les items pour la liste
        var items = new List<object>();
        
        if (_currentSuggestions.Count == 0)
        {
            // Item "Créer"
            items.Add(new { 
                IsCreateItem = true, 
                DisplayText = $"➕ Créer \"{query}\"",
                Query = query 
            });
        }
        else
        {
            foreach (var patient in _currentSuggestions)
            {
                items.Add(new { 
                    IsCreateItem = false, 
                    DisplayText = patient.DisplayLabel,
                    Patient = patient 
                });
            }
        }
        
        SuggestionsList.ItemsSource = items;
        SuggestionsList.DisplayMemberPath = "DisplayText";
        SuggestionsPopup.IsOpen = true;
        _selectedSuggestionIndex = -1;
    }
    
    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (!SuggestionsPopup.IsOpen)
            return;
            
        switch (e.Key)
        {
            case Key.Down:
                if (_selectedSuggestionIndex < SuggestionsList.Items.Count - 1)
                {
                    _selectedSuggestionIndex++;
                    SuggestionsList.SelectedIndex = _selectedSuggestionIndex;
                }
                e.Handled = true;
                break;
                
            case Key.Up:
                if (_selectedSuggestionIndex > 0)
                {
                    _selectedSuggestionIndex--;
                    SuggestionsList.SelectedIndex = _selectedSuggestionIndex;
                }
                e.Handled = true;
                break;
                
            case Key.Enter:
                if (_selectedSuggestionIndex >= 0)
                {
                    var item = SuggestionsList.SelectedItem;
                    HandleSuggestionSelection(item);
                }
                e.Handled = true;
                break;
                
            case Key.Escape:
                SuggestionsPopup.IsOpen = false;
                e.Handled = true;
                break;
        }
    }
    
    private void SuggestionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SuggestionsList.SelectedItem != null)
        {
            ValiderPatientButton.IsEnabled = true;
        }
    }
    
    private void SuggestionsList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Gérer le clic sur une suggestion
        var listBox = sender as ListBox;
        var item = listBox?.SelectedItem;
        
        if (item != null)
        {
            HandleSuggestionSelection(item);
            e.Handled = true;
        }
    }
    
    private void HandleSuggestionSelection(object item)
    {
        var itemType = item.GetType();
        var isCreateProp = itemType.GetProperty("IsCreateItem");
        var isCreate = (bool)isCreateProp.GetValue(item);
        
        if (isCreate)
        {
            // Ouvrir dialog de création
            var queryProp = itemType.GetProperty("Query");
            var query = (string)queryProp.GetValue(item);
            
            // Essayer de parser avec Doctolib
            var parseResult = _parsingService.ParseDoctolibBlock(query);
            
            CreatePatientDialog dialog;
            if (parseResult.Success)
            {
                dialog = new CreatePatientDialog(
                    parseResult.Prenom, 
                    parseResult.Nom, 
                    parseResult.Dob, 
                    parseResult.Sex
                );
            }
            else
            {
                // Parser simple "Prénom Nom"
                var (prenom, nom) = _parsingService.ParseSimpleFormat(query);
                dialog = new CreatePatientDialog();
                if (prenom != null) dialog.PrenomTextBox.Text = prenom;
                if (nom != null) dialog.NomTextBox.Text = nom;
            }
            
            if (dialog.ShowDialog() == true && dialog.Result != null)
            {
                var (success, message, id, path) = _patientIndexService.Upsert(dialog.Result);
                
                if (success && id != null)
                {
                    // Recharger l'index et sélectionner le patient
                    _patientIndexService.ScanAsync().Wait();
                    var patient = _patientIndexService.Search(id, 1).FirstOrDefault();
                    if (patient != null)
                    {
                        SelectPatient(patient);
                    }
                }
            }
        }
        else
        {
            // Patient existant sélectionné
            var patientProp = itemType.GetProperty("Patient");
            var patient = (PatientIndexEntry)patientProp.GetValue(item);
            _selectedPatient = patient;
            ValiderPatientButton.IsEnabled = true;
        }
        
        SuggestionsPopup.IsOpen = false;
    }
    
    private void ValiderPatientButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPatient != null)
        {
            SelectPatient(_selectedPatient);
        }
    }
    
    // === SÉLECTION PATIENT ===
    
    private void SelectPatient(PatientIndexEntry patient)
    {
        _selectedPatient = patient;
        
        // Afficher la carte patient
        PatientCard.Visibility = Visibility.Visible;
        PatientNameLabel.Text = $"{patient.Nom} {patient.Prenom}";
        
        if (patient.Age.HasValue)
            PatientAgeLabel.Text = $"{patient.Age} ans";
        else
            PatientAgeLabel.Text = "";
            
        if (!string.IsNullOrEmpty(patient.DobFormatted))
            PatientDobLabel.Text = $"Né(e) le {patient.DobFormatted}";
        else
            PatientDobLabel.Text = "";
            
        if (!string.IsNullOrEmpty(patient.Sexe))
            PatientSexeLabel.Text = patient.Sexe == "H" ? "Homme" : "Femme";
        else
            PatientSexeLabel.Text = "";
        
        // Charger les notes
        LoadPatientNotes(patient.Id);
        
        // Clear search box
        SearchBox.Text = "";
        SearchPlaceholder.Visibility = Visibility.Visible;
        ValiderPatientButton.IsEnabled = false;
        
        StatusTextBlock.Text = $"✓ Dossier chargé: {patient.NomComplet}";
        StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
    }
    
    private void LoadPatientNotes(string patientId)
    {
        var notes = _patientIndexService.GetPatientNotes(patientId);
        
        var noteItems = notes.Select(n => new {
            DateLabel = n.date.ToString("dd/MM/yyyy HH:mm"),
            Preview = n.preview,
            FilePath = n.filePath
        }).ToList();
        
        PatientNotesList.ItemsSource = noteItems;
    }
    
    private void PatientNotesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PatientNotesList.SelectedItem != null)
        {
            var item = PatientNotesList.SelectedItem;
            var filePathProp = item.GetType().GetProperty("FilePath");
            var filePath = (string)filePathProp.GetValue(item);
            
            // Charger et afficher la note
            var content = File.ReadAllText(filePath);
            NoteStructureeTextBox.Text = content;
        }
    }
    
    private void OuvrirDossierPatientButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPatient != null)
        {
            Process.Start("explorer.exe", _selectedPatient.DirectoryPath);
        }
    }
    
    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        SearchPlaceholder.Visibility = Visibility.Collapsed;
    }
    
    private void SearchBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(SearchBox.Text))
        {
            SearchPlaceholder.Visibility = Visibility.Visible;
        }
    }
}
```

### 3. Modifications dans les méthodes existantes

```csharp
// Modifier StructurerButton_Click pour utiliser le patient sélectionné
private async void StructurerButton_Click(object sender, RoutedEventArgs e)
{
    if (_selectedPatient == null)
    {
        StatusTextBlock.Text = "⚠️ Veuillez d'abord sélectionner un patient.";
        StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
        return;
    }
    
    // ... reste du code existant mais utiliser _selectedPatient.NomComplet
}

// Modifier ValiderSauvegarderButton_Click
private void ValiderSauvegarderButton_Click(object sender, RoutedEventArgs e)
{
    if (_selectedPatient == null)
    {
        StatusTextBlock.Text = "⚠️ Patient requis pour sauvegarder.";
        return;
    }
    
    // Sauvegarder avec StorageService existant
    var (success, message, filePath) = _storageService.SaveStructuredNote(
        _selectedPatient.NomComplet,
        NoteStructureeTextBox.Text
    );
    
    if (success)
    {
        // Recharger la liste des notes
        LoadPatientNotes(_selectedPatient.Id);
        StatusTextBlock.Text = message;
        StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
    }
}
```

## Étapes d'intégration

1. **Sauvegarder l'ancien MainWindow.xaml** (si besoin de revenir en arrière)
2. **Remplacer MainWindow.xaml** par le nouveau design
3. **Mettre à jour MainWindow.xaml.cs** avec toutes les méthodes ci-dessus
4. **Tester le workflow complet:**
   - Recherche patient (>= 3 lettres)
   - Navigation clavier (↑/↓, Enter)
   - Sélection patient → carte visible + notes chargées
   - Créer nouveau patient si introuvable
   - Parser Doctolib lors de la création
   - Sauvegarder note → liste notes mise à jour

## Points d'attention

- **Suppression NomCompletTextBox**: Le champ patient actuel n'est plus nécessaire
- **Migration données**: Si des patients existent sans `patient.json`, l'index les inférera depuis le nom du dossier
- **FileSystemWatcher**: Maintient l'index à jour automatiquement
- **Performance**: L'index est en mémoire, le scan initial peut prendre quelques secondes si beaucoup de patients

## Tests suggérés

1. Lancer l'app → index se charge
2. Taper "dup" → suggestions apparaissent
3. ↓ pour naviguer, Enter pour sélectionner
4. Carte patient s'affiche + notes listées
5. Cliquer sur une note → s'affiche dans note structurée
6. Créer note brute → Structurer → Sauvegarder
7. Liste notes se met à jour automatiquement
8. Rechercher nom inexistant → "Créer ..." proposé
9. Coller bloc Doctolib → dialog pré-rempli
