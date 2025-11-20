# Système d'évaluation des courriers - Implémentation

## 📁 Fichiers créés

✅ **Modèle** : `MedCompanion/Models/LetterRating.cs`
✅ **Service** : `MedCompanion/Services/LetterRatingService.cs`
✅ **Dialogue** : `MedCompanion/Dialogs/RateLetterDialog.xaml` + `.xaml.cs`

## 🎯 Fonctionnalités

### 1. Modèle LetterRating
```csharp
public class LetterRating
{
    public string Id { get; set; }
    public string LetterPath { get; set; }         // Chemin du .docx
    public int Rating { get; set; }                // 1-5 étoiles
    public string? Comment { get; set; }           // Commentaire optionnel
    public DateTime RatingDate { get; set; }
    public string? MCCId { get; set; }             // MCC utilisé (ou null)
    public string? MCCName { get; set; }
    public string? UserRequest { get; set; }       // Demande originale
    public string? PatientContext { get; set; }
    public string? PatientName { get; set; }
    
    // Propriétés calculées
    public bool IsMCCCandidate => Rating == 5 && string.IsNullOrEmpty(MCCId);
    public bool NeedsMCCReview => Rating <= 3 && !string.IsNullOrEmpty(MCCId);
}
```

### 2. Service LetterRatingService

**Méthodes principales** :
- `AddOrUpdateRating(LetterRating rating)` - Sauvegarder/mettre à jour une évaluation
- `GetRatingForLetter(string letterPath)` - Récupérer l'évaluation d'un courrier
- `GetAllRatings()` - Toutes les évaluations
- `GetRatingsForMCC(string mccId)` - Évaluations d'un MCC spécifique
- `GetMCCAverageRating(string mccId)` - Note moyenne d'un MCC
- `GetMCCCandidates()` - Courriers 5★ sans MCC (candidats pour créer un MCC)
- `GetMCCsNeedingReview()` - Liste des MCC avec notes ≤3★
- `GetMCCStatistics(string mccId)` - Stats complètes d'un MCC

**Stockage** : `%AppData%\MedCompanion\letter-ratings.json`

### 3. Dialogue RateLetterDialog

Interface graphique avec :
- 5 boutons étoiles cliquables
- Zone de commentaire optionnelle
- Indication visuelle de la qualité (couleur + texte)
- Boutons Annuler/Valider

## 🔧 Intégration dans MainWindow

### Étape 1 : Ajouter le service dans MainWindow

```csharp
// Dans MainWindow.xaml.cs
private LetterRatingService _letterRatingService;

// Dans le constructeur
public MainWindow()
{
    InitializeComponent();
    // ... autres initialisations ...
    
    _letterRatingService = new LetterRatingService();
}
```

### Étape 2 : Ajouter un bouton "Noter" dans la liste des courriers

**Option A : Dans MainWindow.Documents.cs (section Courriers)**

Trouver où s'affiche la liste des courriers sauvegardés et ajouter :

```csharp
private void RateLetterButton_Click(object sender, RoutedEventArgs e)
{
    // Récupérer le chemin du courrier depuis le DataContext du bouton
    if (sender is Button button && button.Tag is string letterPath)
    {
        ShowRateLetterDialog(letterPath, null, null);
    }
}

private void ShowRateLetterDialog(string letterPath, string? mccId, string? mccName)
{
    // Vérifier si une évaluation existe déjà
    var existingRating = _letterRatingService.GetRatingForLetter(letterPath);
    
    var dialog = new RateLetterDialog(letterPath, mccId, mccName)
    {
        Owner = this
    };
    
    // Pré-remplir si évaluation existante
    if (existingRating != null)
    {
        dialog.LoadExistingRating(existingRating);
    }
    
    var result = dialog.ShowDialog();
    
    if (result == true && dialog.Rating != null)
    {
        // Compléter les infos si nécessaire
        if (_selectedPatient != null)
        {
            dialog.Rating.PatientName = _selectedPatient.NomComplet;
        }
        
        // Sauvegarder
        var (success, error) = _letterRatingService.AddOrUpdateRating(dialog.Rating);
        
        if (success)
        {
            // Rafraîchir l'affichage de la liste
            RefreshLettersList();
            
            // Gérer les actions selon la note
            HandleRatingActions(dialog.Rating);
        }
        else
        {
            MessageBox.Show($"Erreur de sauvegarde : {error}", "Erreur", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}

private void HandleRatingActions(LetterRating rating)
{
    // MCC à revoir (≤3 étoiles avec MCC)
    if (rating.NeedsMCCReview)
    {
        System.Diagnostics.Debug.WriteLine(
            $"⚠️ MCC à revoir : {rating.MCCName} (note: {rating.Rating}★)"
        );
        
        // TODO: Marquer le MCC pour révision dans MCCLibraryService
    }
    
    // Candidat MCC (5 étoiles sans MCC)
    if (rating.IsMCCCandidate)
    {
        System.Diagnostics.Debug.WriteLine(
            $"⭐ Candidat MCC détecté : {rating.LetterPath}"
        );
        
        // Optionnel : proposer immédiatement de créer un MCC
        var response = MessageBox.Show(
            "Ce courrier a obtenu 5 étoiles !\n\n" +
            "Voulez-vous le transformer en modèle MCC pour réutilisation future ?",
            "Créer un nouveau MCC",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question
        );
        
        if (response == MessageBoxResult.Yes)
        {
            // TODO: Ouvrir le dialogue de création MCC avec ce courrier
            // CreateMCCFromLetter(rating);
        }
    }
}
```

### Étape 3 : Afficher la note dans la liste des courriers

**Dans le XAML de la liste des courriers**, ajouter une colonne :

```xml
<!-- Exemple de colonne pour afficher les étoiles -->
<DataGridTemplateColumn Header="Note" Width="100">
    <DataGridTemplateColumn.CellTemplate>
        <DataTemplate>
            <StackPanel Orientation="Horizontal">
                <!-- Étoiles ou bouton "Noter" -->
                <TextBlock x:Name="RatingDisplay" 
                          FontSize="14"
                          VerticalAlignment="Center"/>
                <Button Content="⭐ Noter"
                        Click="RateLetterButton_Click"
                        Tag="{Binding Path}"
                        Margin="5,0,0,0"
                        Visibility="{Binding HasRating, 
                            Converter={StaticResource BoolToVisibilityConverter}}"/>
            </StackPanel>
        </DataTemplate>
    </DataGridTemplateColumn.CellTemplate>
</DataGridTemplateColumn>
```

### Étape 4 : Intégrer dans le flux de génération de courrier

**Après la sauvegarde d'un courrier généré**, stocker les métadonnées :

```csharp
// Dans MainWindow.xaml.cs - après sauvegarde d'un courrier
private string? _lastGeneratedLetterPath = null;
private string? _lastUsedMCCId = null;
private string? _lastUsedMCCName = null;
private string? _lastUserRequest = null;

// Après avoir généré et sauvegardé un courrier
private void AfterLetterSaved(string letterPath, string? mccId, string? mccName, string? userRequest)
{
    _lastGeneratedLetterPath = letterPath;
    _lastUsedMCCId = mccId;
    _lastUsedMCCName = mccName;
    _lastUserRequest = userRequest;
    
    // L'utilisateur peut maintenant aller dans "Courriers sauvegardés" et noter
}

// Ou proposer immédiatement après sauvegarde (optionnel)
private void ProposeRatingAfterSave()
{
    if (string.IsNullOrEmpty(_lastGeneratedLetterPath))
        return;
        
    var response = MessageBox.Show(
        "Courrier sauvegardé !\n\nSouhaitez-vous l'évaluer maintenant ?",
        "Évaluation",
        MessageBoxButton.YesNo,
        MessageBoxImage.Question
    );
    
    if (response == MessageBoxResult.Yes)
    {
        ShowRateLetterDialog(_lastGeneratedLetterPath, _lastUsedMCCId, _lastUsedMCCName);
    }
}
```

## 📊 Intégration dans la bibliothèque MCC

### Afficher les statistiques dans MCCLibraryDialog

```csharp
// Dans MCCLibraryDialog.xaml.cs
private void DisplayMCCWithStats(MCCModel mcc)
{
    var stats = _letterRatingService.GetMCCStatistics(mcc.Id);
    
    if (stats.TotalRatings > 0)
    {
        // Afficher : ⭐ 4.2/5 (12 avis) - 83% satisfaction
        MCCStatsText.Text = $"⭐ {stats.AverageRating:F1}/5 ({stats.TotalRatings} avis) - " +
                           $"{stats.SatisfactionRate:F0}% satisfaction";
    }
    
    // Flag "À revoir" si note moyenne ≤3
    if (stats.AverageRating <= 3 && stats.TotalRatings >= 2)
    {
        MCCWarningBadge.Visibility = Visibility.Visible;
        MCCWarningBadge.Text = "⚠️ À revoir";
    }
}
```

## 🔄 Workflows

### Workflow 1 : Évaluation courrier avec MCC
```
1. Génération courrier avec MCC → Sauvegarde
2. Utilisateur ouvre "Courriers sauvegardés"
3. Clic sur bouton "⭐ Noter" à côté du courrier
4. Sélection 1-5 étoiles + commentaire optionnel
5. Validation
6. Si ≤3★ → MCC marqué "⚠️ À revoir" dans bibliothèque
```

### Workflow 2 : Courrier excellent sans MCC
```
1. Génération courrier SANS MCC trouvé → Sauvegarde
2. Utilisateur note 5★
3. Système détecte : IsMCCCandidate = true
4. Proposition : "Créer un MCC avec ce courrier ?"
5. Si oui → Ouverture dialogue création MCC pré-rempli
```

### Workflow 3 : Consultation stats MCC
```
1. Ouverture bibliothèque MCC
2. Pour chaque MCC : affichage note moyenne + nb avis
3. Badge "⚠️ À revoir" sur MCC avec mauvaises notes
4. Clic sur MCC → Détails avec distribution notes
```

## 📝 TODO : Prochaines étapes

1. ✅ Créer les fichiers de base (modèle, service, dialogue)
2. ⏳ **Intégrer dans MainWindow.Documents.cs** :
   - Ajouter `_letterRatingService` 
   - Créer méthode `ShowRateLetterDialog()`
   - Ajouter bouton "Noter" dans liste courriers
   - Afficher les étoiles à côté des courriers notés

3. ⏳ **Intégrer dans MCCLibraryDialog** :
   - Afficher stats (note moyenne, nb avis) pour chaque MCC
   - Badge "⚠️ À revoir" sur MCC mal notés
   - Détails des évaluations au clic

4. ⏳ **Créer page "Candidats MCC"** :
   - Liste des courriers 5★ sans MCC
   - Bouton "Créer MCC" pour chaque candidat
   - Extraction automatique sémantique + mots-clés

## 🎨 UI/UX

### Affichage dans liste courriers
```
📄 2025-11-04_courrier_ecole.docx     [★★★★★] 5/5
📄 2025-11-03_courrier_CPAM.docx      [★★★☆☆] 3/5
📄 2025-11-02_courrier.docx           [⭐ Noter]
```

### Affichage dans bibliothèque MCC
```
📋 Courrier PAI - École
   ⭐ 4.2/5 (12 avis) • 83% satisfaction
   Utilisé 23 fois

📋 Courrier certificat médical
   ⚠️ À revoir • ⭐ 2.8/5 (5 avis) • 40% satisfaction
   Utilisé 8 fois
```

## 🧪 Tests suggérés

1. **Test évaluation basique**
   - Générer un courrier
   - Le sauvegarder
   - L'évaluer avec 4★
   - Vérifier sauvegarde dans `letter-ratings.json`

2. **Test modification évaluation**
   - Noter un courrier à 3★
   - Rouvrir → modifier à 5★
   - Vérifier mise à jour

3. **Test candidat MCC**
   - Générer courrier SANS MCC
   - Noter 5★
   - Vérifier que `IsMCCCandidate = true`

4. **Test MCC à revoir**
   - Générer courrier AVEC MCC
   - Noter ≤3★
   - Vérifier badge dans bibliothèque

## 📦 Fichier de données

**Emplacement** : `%AppData%\MedCompanion\letter-ratings.json`

**Structure** :
```json
{
  "ratings": [
    {
      "id": "abc-123",
      "letter_path": "C:\\...\\courrier.docx",
      "rating": 5,
      "comment": "Parfait pour l'école",
      "rating_date": "2025-11-04T20:15:00",
      "mcc_id": null,
      "mcc_name": null,
      "user_request": "courrier PAI école",
      "patient_name": "Jean Dupont",
      "is_mcc_candidate": true,
      "needs_mcc_review": false
    }
  ],
  "version": "1.0",
  "last_updated": "2025-11-04T20:15:00"
}
