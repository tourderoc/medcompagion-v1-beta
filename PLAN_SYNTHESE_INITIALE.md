# Plan Implémentation — Synthèse Initiale (3e phase 1ère Consultation)

*Date : 2026-05-13 — Troisième partie de la 1ère Consultation*

---

## Contexte

Le Mode Consultation V0c inclut maintenant:
1. **Phase 1** : Interrogatoire (Anamnèse/Parents) ✅
2. **Phase 2** : Observations Cliniques (Clinique/Enfant) ✅
3. **Phase 3** : Synthèse Initiale (NOUVEAU) — Fusion intelligente et pondérée des données

La synthèse initiale doit:
- ✅ Être générée avec des **poids de fiabilité** pour chaque composant
- ✅ Être **validée/ajustée par Med** avant génération
- ✅ Contenir **données uniquement** (pas d'axes, pas de recommandations)
- ✅ Être sauvegardée et visible dans **Mode Console** aussi
- ✅ Enregistrée dans le **tracker d'accumulation** global

---

## Objectif UX/UI

### Navigation
Après terminer les observations cliniques, afficher:
- **Tab 3** : "📋 Synthèse Initiale"
- Contient deux sections: Poids + Synthèse générée

### Workflow Med
```
1. Afficher proposition des poids
   ├─ LLM analyse données → propose poids (0.1-1.0)
   ├─ Med ajuste via slider
   └─ Bouton "Valider les poids"

2. Générer synthèse avec poids finalisés
   ├─ Appel IA avec JSON structuré (données + poids)
   ├─ IA retourne synthèse fluide
   └─ Affichage live

3. Sauvegarder + tracker
   ├─ Markdown + YAML métadonnées
   ├─ 2025/synthese/synthese_initiale_YYYYMMDD_HHMMSS.md
   └─ Enregistrement dans SynthesisWeightTracker
```

---

## Modèles de Données

### ConsultationModels.cs — AJOUTER

```csharp
/// <summary>
/// Poids proposés pour chaque composant de la synthèse initiale
/// </summary>
public class InitialSynthesisWeights : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private double _interrogatoireWeight = 0.5;
    public double InterrogatoireWeight
    {
        get => _interrogatoireWeight;
        set { _interrogatoireWeight = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InterrogatoireWeight))); }
    }

    private double _observationsWeight = 0.8;
    public double ObservationsWeight
    {
        get => _observationsWeight;
        set { _observationsWeight = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ObservationsWeight))); }
    }

    private Dictionary<string, double> _documentWeights = new();
    public Dictionary<string, double> DocumentWeights
    {
        get => _documentWeights;
        set { _documentWeights = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DocumentWeights))); }
    }

    private string? _llmJustification;
    public string? LLMJustification
    {
        get => _llmJustification;
        set { _llmJustification = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LLMJustification))); }
    }

    /// <summary>Poids moyen global (pour tracking)</summary>
    public double AverageWeight => CalculateAverageWeight();

    private double CalculateAverageWeight()
    {
        var allWeights = new List<double> { _interrogatoireWeight, _observationsWeight };
        allWeights.AddRange(_documentWeights.Values);
        return allWeights.Count > 0 ? allWeights.Average() : 0.5;
    }
}
```

---

## ViewModel

### ConsultationModeViewModel.cs — AJOUTER

```csharp
// État synthèse initiale
private InitialSynthesisWeights _synthesisWeights = new();
public InitialSynthesisWeights SynthesisWeights
{
    get => _synthesisWeights;
    set => SetProperty(ref _synthesisWeights, value);
}

private bool _isSynthesisMode = false;
public bool IsSynthesisMode
{
    get => _isSynthesisMode;
    set => SetProperty(ref _isSynthesisMode, value);
}

private string _synthesisContent = "";
public string SynthesisContent
{
    get => _synthesisContent;
    set => SetProperty(ref _synthesisContent, value);
}

private bool _areWeightsLoading = false;
public bool AreWeightsLoading
{
    get => _areWeightsLoading;
    set => SetProperty(ref _areWeightsLoading, value);
}

private bool _isGeneratingSynthesis = false;
public bool IsGeneratingSynthesis
{
    get => _isGeneratingSynthesis;
    set => SetProperty(ref _isGeneratingSynthesis, value);
}

private string _synthesisStatusMessage = "";
public string SynthesisStatusMessage
{
    get => _synthesisStatusMessage;
    set => SetProperty(ref _synthesisStatusMessage, value);
}

// Commandes
public ICommand SwitchToSynthesisCommand { get; }
public ICommand ProposeWeightsCommand { get; }
public ICommand GenerateSynthesisCommand { get; }
public ICommand SaveSynthesisCommand { get; }

// Initialisation dans constructor
SwitchToSynthesisCommand = new RelayCommand(SwitchToSynthesis);
ProposeWeightsCommand = new RelayCommand(async () => await ProposeWeightsAsync());
GenerateSynthesisCommand = new RelayCommand(async () => await GenerateSynthesisAsync());
SaveSynthesisCommand = new RelayCommand(async () => await SaveSynthesisAsync());

// Méthodes
private void SwitchToSynthesis()
{
    IsInterrogatoireMode = false;
    IsInClinicalMode = false;
    IsSynthesisMode = true;
}

private async Task ProposeWeightsAsync()
{
    AreWeightsLoading = true;
    SynthesisStatusMessage = "⏳ Analyse des données...";

    try
    {
        // Collecter les données
        var interrogatoireData = BuildInterrogatoireJSON();
        var observationsData = BuildObservationsJSON();
        var documentsData = BuildDocumentsJSON();

        // Appel IA pour proposer les poids
        var prompt = BuildWeightProposalPrompt(interrogatoireData, observationsData, documentsData);
        
        var (success, response, _) = await _llmService.ChatAsync(prompt, new(), maxTokens: 500);
        
        if (success)
        {
            // Parser la réponse pour extraire les poids proposés
            var weights = ExtractWeightsFromResponse(response);
            
            SynthesisWeights.InterrogatoireWeight = weights.ContainsKey("interrogatoire") 
                ? weights["interrogatoire"] 
                : 0.5;
            
            SynthesisWeights.ObservationsWeight = weights.ContainsKey("observations")
                ? weights["observations"]
                : 0.8;
            
            SynthesisWeights.DocumentWeights = weights
                .Where(kv => !kv.Key.StartsWith("interrogatoire") && !kv.Key.StartsWith("observations"))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            
            SynthesisWeights.LLMJustification = ExtractJustificationFromResponse(response);
            
            SynthesisStatusMessage = "✅ Poids proposés (ajustable via sliders)";
        }
        else
        {
            SynthesisStatusMessage = "❌ Erreur: " + response;
        }
    }
    catch (Exception ex)
    {
        SynthesisStatusMessage = $"❌ Erreur: {ex.Message}";
    }
    finally
    {
        AreWeightsLoading = false;
    }
}

private async Task GenerateSynthesisAsync()
{
    IsGeneratingSynthesis = true;
    SynthesisStatusMessage = "⏳ Génération synthèse pondérée...";

    try
    {
        // Construire JSON structuré avec données + poids validés
        var synthesisJSON = BuildInitialSynthesisJSON(SynthesisWeights);
        
        var prompt = BuildSynthesisGenerationPrompt(synthesisJSON);
        
        var (success, synthesis, _) = await _llmService.ChatAsync(prompt, new(), maxTokens: 2000);
        
        if (success)
        {
            SynthesisContent = synthesis;
            SynthesisStatusMessage = "✅ Synthèse générée avec succès";
        }
        else
        {
            SynthesisStatusMessage = "❌ Erreur génération: " + synthesis;
        }
    }
    catch (Exception ex)
    {
        SynthesisStatusMessage = $"❌ Erreur: {ex.Message}";
    }
    finally
    {
        IsGeneratingSynthesis = false;
    }
}

private async Task SaveSynthesisAsync()
{
    if (string.IsNullOrEmpty(SynthesisContent))
    {
        SynthesisStatusMessage = "❌ Aucune synthèse à sauvegarder";
        return;
    }

    try
    {
        // Construire le Markdown avec métadonnées YAML
        var yaml = $@"---
date_synthese: {DateTime.Now:yyyy-MM-ddTHH:mm:ss}
type: initial_consultation
weights:
  interrogatoire: {SynthesisWeights.InterrogatoireWeight:F1}
  observations: {SynthesisWeights.ObservationsWeight:F1}
  moyenne: {SynthesisWeights.AverageWeight:F1}
---

{SynthesisContent}";

        // Sauvegarder le fichier
        var syntheseDir = _pathService.GetSyntheseDirectory(_currentPatient.NomComplet);
        Directory.CreateDirectory(syntheseDir);
        
        var fileName = $"synthese_initiale_{DateTime.Now:yyyyMMdd_HHmmss}.md";
        var filePath = Path.Combine(syntheseDir, fileName);
        
        File.WriteAllText(filePath, yaml, Encoding.UTF8);

        // Enregistrer dans le tracker
        _synthesisWeightTracker.RecordContentWeight(
            _currentPatient.NomComplet,
            "synthese_initiale",
            filePath,
            SynthesisWeights.AverageWeight,
            $"Synthèse initiale 1ère consultation (Int:{SynthesisWeights.InterrogatoireWeight:F1}, Obs:{SynthesisWeights.ObservationsWeight:F1})"
        );

        SynthesisStatusMessage = $"✅ Synthèse sauvegardée: {fileName}";
        
        // Optionnel: rafraîchir le Mode Console
        // OnSynthesisSaved?.Invoke(this, EventArgs.Empty);
    }
    catch (Exception ex)
    {
        SynthesisStatusMessage = $"❌ Erreur sauvegarde: {ex.Message}";
    }
}

// Helpers
private string BuildInterrogatoireJSON()
{
    var obj = new
    {
        age = _currentConsultation?.Age ?? 0,
        motif = _currentConsultation?.Motif ?? "",
        blocks = _consultationBlocks.Select(b => new
        {
            key = b.Key,
            title = b.Title,
            covered_themes = b.CoveredThemes,
            progress = b.ProgressPct
        })
    };
    return System.Text.Json.JsonSerializer.Serialize(obj, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
}

private string BuildObservationsJSON()
{
    var obj = new
    {
        total_cards = _clinicalObservations.Cards.Count,
        filled_cards = _clinicalObservations.Cards.Count(c => c.SelectedOption != null),
        observations = _clinicalObservations.Cards.Select(c => new
        {
            branch = c.Branch.ToString(),
            title = c.Title,
            selected = c.SelectedOption,
            notes = c.FreeText
        })
    };
    return System.Text.Json.JsonSerializer.Serialize(obj, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
}

private string BuildDocumentsJSON()
{
    // À adapter: récupérer bilans/docs importés pendant la consultation
    return "{}";
}

private string BuildWeightProposalPrompt(string interrogatoire, string observations, string documents)
{
    return $@"Tu analyses une 1ère consultation pédopsychiatrique. Voici les données:

INTERROGATOIRE (données parental):
{interrogatoire}

OBSERVATIONS CLINIQUES (directes):
{observations}

DOCUMENTS:
{documents}

Évalue la FIABILITÉ/PERTINENCE de chaque composant sur 0.1-1.0:
- 0.9-1.0: Très complet, très cohérent
- 0.7-0.8: Bon, fiable
- 0.5-0.6: Moyen
- 0.3-0.4: Limité
- 0.1-0.2: Très partiel

Réponds en JSON:
{{
  ""weights"": {{
    ""interrogatoire"": 0.X,
    ""observations"": 0.X
  }},
  ""justification"": ""Courte explication de l'évaluation""
}}";
}

private Dictionary<string, double> ExtractWeightsFromResponse(string response)
{
    // Parser JSON de la réponse
    try
    {
        var json = System.Text.Json.JsonDocument.Parse(response);
        var weights = new Dictionary<string, double>();
        
        if (json.RootElement.TryGetProperty("weights", out var weightsObj))
        {
            foreach (var prop in weightsObj.EnumerateObject())
            {
                if (double.TryParse(prop.Value.ToString(), out var weight))
                {
                    weights[prop.Name] = Math.Clamp(weight, 0.1, 1.0);
                }
            }
        }
        
        return weights;
    }
    catch
    {
        return new Dictionary<string, double>();
    }
}

private string ExtractJustificationFromResponse(string response)
{
    try
    {
        var json = System.Text.Json.JsonDocument.Parse(response);
        if (json.RootElement.TryGetProperty("justification", out var justif))
        {
            return justif.GetString() ?? "";
        }
    }
    catch { }
    
    return "";
}

private string BuildInitialSynthesisJSON(InitialSynthesisWeights weights)
{
    var obj = new
    {
        consultation_type = "premiere_consultation",
        weights = new
        {
            interrogatoire = weights.InterrogatoireWeight,
            observations = weights.ObservationsWeight,
            moyenne = weights.AverageWeight
        },
        data = new
        {
            interrogatoire = BuildInterrogatoireJSON(),
            observations = BuildObservationsJSON()
        }
    };
    return System.Text.Json.JsonSerializer.Serialize(obj, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
}

private string BuildSynthesisGenerationPrompt(string synthesisJSON)
{
    return $@"Tu es un clinicien pédopsychiatre. Voici les données d'une 1ère consultation avec leurs poids de fiabilité:

{synthesisJSON}

Les poids indiquent:
- Poids 0.8+: donner plus d'importance, détailler davantage
- Poids 0.5-0.7: équilibré
- Poids <0.5: noter sans surinterprétation

Génère une SYNTHÈSE CLINIQUE INITIALE (données uniquement):
❌ PAS d'axes cliniques proposés
❌ PAS de recommandations thérapeutiques
❌ PAS de projet de soin
✅ JUSTE: fusion fluide, intelligente et pondérée des données

Format Markdown:
# Synthèse Initiale - Première Consultation

## Contexte
[Âge, motif principal, date]

## Anamnèse (poids {weights.InterrogatoireWeight:F1})
[Ce que les parents rapportent - sans surinterprétation si poids bas]

## Observations Cliniques Directes (poids {weights.ObservationsWeight:F1})
[Observations précises de l'enfant - valoriser si poids élevé]

## Tableau Clinique Synthétisé
[Fusion cohérente: ce qu'on sait de cet enfant, à partir des données plus fiables]";
}
```

---

## XAML

### ConsultationModeControl.xaml — AJOUTER

```xaml
<!-- Tab navigation (ajouter 3e tab) -->
<StackPanel Orientation="Horizontal" Margin="0,0,0,12" Height="40">
    <Button Content="📋 Interrogatoire"
            Command="{Binding SwitchToInterrogatoireCommand}"
            Height="40" VerticalAlignment="Top"
            Background="#3498DB" Foreground="White" Padding="12,0"/>
    <Button Content="👁 Observations"
            Command="{Binding SwitchToClinicalCommand}"
            Height="40" VerticalAlignment="Top"
            Background="#27AE60" Foreground="White" Padding="12,0" Margin="4,0,0,0"/>
    <Button Content="📄 Synthèse Initiale"
            Command="{Binding SwitchToSynthesisCommand}"
            Height="40" VerticalAlignment="Top"
            Background="#9B59B6" Foreground="White" Padding="12,0" Margin="4,0,0,0"/>
</StackPanel>

<!-- Synthèse Initiale Panel -->
<Grid Visibility="{Binding IsSynthesisMode, Converter={StaticResource BoolToVis}}">
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto"/>     <!-- Poids -->
        <RowDefinition Height="*"/>        <!-- Synthèse -->
        <RowDefinition Height="Auto"/>     <!-- Boutons -->
    </Grid.RowDefinitions>

    <!-- SECTION 1: Poids -->
    <Border Grid.Row="0" Background="#F8F9FA" Padding="12" Margin="0,0,0,12" BorderThickness="0,0,0,1" BorderBrush="#E0E0E0">
        <StackPanel>
            <TextBlock Text="📊 Poids de Fiabilité des Composants" FontWeight="Bold" FontSize="14" Margin="0,0,0,12"/>
            
            <!-- Interrogatoire -->
            <Grid Margin="0,0,0,12">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="120"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="60"/>
                </Grid.ColumnDefinitions>
                <TextBlock Grid.Column="0" Text="Interrogatoire:" VerticalAlignment="Center"/>
                <Slider Grid.Column="1" Minimum="0.1" Maximum="1.0" SmallChange="0.1" LargeChange="0.2"
                        Value="{Binding SynthesisWeights.InterrogatoireWeight, UpdateSourceTrigger=PropertyChanged}"
                        Margin="12,0"/>
                <TextBlock Grid.Column="2" Text="{Binding SynthesisWeights.InterrogatoireWeight, StringFormat='{0:F1}'}" 
                          HorizontalAlignment="Right" VerticalAlignment="Center"/>
            </Grid>

            <!-- Observations -->
            <Grid Margin="0,0,0,12">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="120"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="60"/>
                </Grid.ColumnDefinitions>
                <TextBlock Grid.Column="0" Text="Observations:" VerticalAlignment="Center"/>
                <Slider Grid.Column="1" Minimum="0.1" Maximum="1.0" SmallChange="0.1" LargeChange="0.2"
                        Value="{Binding SynthesisWeights.ObservationsWeight, UpdateSourceTrigger=PropertyChanged}"
                        Margin="12,0"/>
                <TextBlock Grid.Column="2" Text="{Binding SynthesisWeights.ObservationsWeight, StringFormat='{0:F1}'}" 
                          HorizontalAlignment="Right" VerticalAlignment="Center"/>
            </Grid>

            <!-- Justification LLM -->
            <TextBlock Text="{Binding SynthesisWeights.LLMJustification}" 
                      Foreground="#7F8C8D" FontSize="11" FontStyle="Italic" TextWrapping="Wrap"/>

            <!-- Boutons Poids -->
            <StackPanel Orientation="Horizontal" Margin="0,12,0,0">
                <Button Content="🔄 Proposer à nouveau" 
                        Command="{Binding ProposeWeightsCommand}"
                        IsEnabled="{Binding AreWeightsLoading, Converter={StaticResource InvertBool}}"
                        Padding="12,6" Background="#34495E" Foreground="White" Cursor="Hand"/>
            </StackPanel>

            <!-- Message d'état -->
            <TextBlock Text="{Binding SynthesisStatusMessage}" Foreground="#27AE60" Margin="0,8,0,0" FontSize="11"/>
        </StackPanel>
    </Border>

    <!-- SECTION 2: Synthèse générée -->
    <Border Grid.Row="1" Background="White" Padding="12" BorderThickness="1" BorderBrush="#DDD">
        <StackPanel>
            <TextBlock Text="✅ Synthèse Initiale" FontWeight="Bold" FontSize="13" Margin="0,0,0,8"/>
            
            <RichTextBox Name="SynthesisPreview" IsReadOnly="True" Height="300" 
                        Padding="12" BorderThickness="1" BorderBrush="#E0E0E0"
                        Foreground="#2C3E50" FontFamily="Segoe UI" FontSize="11">
                <FlowDocument/>
            </RichTextBox>

            <TextBlock Text="{Binding SynthesisStatusMessage}" Foreground="#3498DB" Margin="0,8,0,0" FontSize="11"/>
        </StackPanel>
    </Border>

    <!-- SECTION 3: Boutons action -->
    <StackPanel Grid.Row="2" Orientation="Horizontal" Margin="0,12,0,0">
        <Button Content="✨ Générer Synthèse" 
                Command="{Binding GenerateSynthesisCommand}"
                IsEnabled="{Binding IsGeneratingSynthesis, Converter={StaticResource InvertBool}}"
                Padding="12,8" Background="#27AE60" Foreground="White" Cursor="Hand" Margin="0,0,8,0"/>
        
        <Button Content="💾 Sauvegarder" 
                Command="{Binding SaveSynthesisCommand}"
                IsEnabled="{Binding SynthesisContent, Converter={StaticResource StringNotEmptyConverter}}"
                Padding="12,8" Background="#3498DB" Foreground="White" Cursor="Hand"/>

        <Button Content="← Retour Observations" 
                Command="{Binding SwitchToClinicalCommand}"
                Padding="12,8" Background="#95A5A6" Foreground="White" Cursor="Hand" Margin="8,0,0,0"/>
    </StackPanel>
</Grid>
```

---

## Phases Implémentation

### Phase A — Modèles & Poids (~1h)

1. Ajouter `InitialSynthesisWeights` dans ConsultationModels.cs
2. Ajouter propriétés ViewModel (SynthesisWeights, IsSynthesisMode, etc)
3. Implémenter `ProposeWeightsAsync()` et helpers de parsing JSON

### Phase B — ViewModel & Logic (~1.5h)

1. Implémenter `GenerateSynthesisAsync()` avec prompts pondérés
2. Implémenter `SaveSynthesisAsync()` avec YAML + tracker
3. Implémenter commands de navigation

### Phase C — XAML UI (~1h)

1. Ajouter 3e tab navigation
2. Créer section poids (sliders)
3. Créer section synthèse (RichTextBox)
4. Boutons action

### Phase D — Intégration Console (~1h)

1. Synthèse initiale visible dans "Synthèse Patient" (Mode Console)
2. Format unifié avec synthèse globale
3. Tester accumulateur tracker

### Phase E — Tests & Polish (~30 min)

1. Flow complet: Interrogatoire → Observations → Synthèse
2. Vérifier poids sauvegardés dans tracker
3. Vérifier visibilité Mode Console

---

## Estimation Totale

| Phase | Durée |
|-------|-------|
| A | 1h |
| B | 1.5h |
| C | 1h |
| D | 1h |
| E | 0.5h |
| **Total** | **~5h** |

---

## Données Stockage

### Par patient: `2025/synthese/synthese_initiale_YYYYMMDD_HHMMSS.md`

```markdown
---
date_synthese: 2025-05-13T14:30:00
type: initial_consultation
weights:
  interrogatoire: 0.5
  observations: 0.8
  moyenne: 0.65
---

# Synthèse Initiale - Première Consultation

## Contexte
Enfant de 7 ans, motif principal: difficulté scolaire

## Anamnèse (poids 0.5)
Parents rapportent début progressif des difficultés depuis la moyenne section...

## Observations Cliniques Directes (poids 0.8)
Contact bon, langage adapté, observations cliniques en concordance...

## Tableau Clinique Synthétisé
Synthèse fluide et pondérée...
```

### Tracker: `2025/synthese/update_tracker.json`

```json
{
  "AccumulatedWeight": 0.65,
  "LastSynthesisUpdate": null,
  "PendingItems": [
    {
      "ItemId": "...",
      "ItemType": "synthese_initiale",
      "FilePath": "2025/synthese/synthese_initiale_20250513_143000.md",
      "RelevanceWeight": 0.65,
      "Justification": "Synthèse initiale (Int:0.5, Obs:0.8)",
      "IncludedInSynthesis": false
    }
  ],
  "TotalItemsSinceLastUpdate": 1
}
```

---

## Prochaines Étapes

1. ✓ Approbation plan
2. Démarrer Phase A (modèles)
3. Tester intégration JSON IA
4. Valider format synthèse
5. Merge avec système existant

---

## Notes

- **Synthèse = données seulement**: Pas d'interprétation clinique, juste fusion
- **Poids flexibles**: Med peut toujours ajuster avant génération
- **Transparence**: Poids sauvegardés, traçabilité complète
- **Accumulation**: Compte pour trigger "actualiser synthèse globale"
- **Console**: Visible comme "Synthèse Initiale" distincte de "Synthèse Globale"
