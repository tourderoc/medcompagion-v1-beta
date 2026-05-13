# Plan Implémentation — Mode Consultation UI V0c

*Date : 2026-05-13 — Optimisation UI pour blocs adaptatifs V0b*

---

## Contexte

Flash Gemini (2026-05-12) a implémenté l'architecture V0b :
- ✓ BlockSetResolver (ajout blocs contextuels par âge)
- ✓ MotifPrincipalDetector (détection motif principal)
- ✓ ContextualBlockSuggester (chips LLM)
- ✓ BlockPrefiller (pré-remplissage)
- ✓ WhisperVocabService (vocabulaire custom)
- ✓ Structure gelée après ~90s (IsStructureFrozen flag)

**Ce qui manque :** optimisation UI pour réduire clutter + meilleure visibilité des blocs complétés.

---

## Problème Actuel

1. **Zone transcription trop grande** — prend 50% de l'espace, sert juste de feedback que ça marche
2. **Blocs complétés se mélangent avec les en cours** — pas de distinction visuelle
3. **Pas de "résumé temps réel"** — difficile de voir rapidement ce qui est fait vs. ce qui reste
4. **Bloc à 100% reste dans le flux actif** — charge cognitive inutile

---

## Solution Proposée

### Nouvelle Layout

```
┌─ Mode Consultation (Saisie/Extraction) ────────────────────────┐
│                                                                   │
│ ┌─ Transcription compactée (25%) ─┐  ┌─ Blocs complétés (25%) ─┐ │
│ │                                 │  │                          │ │
│ │ Dernières 5 lignes STT          │  │ ✓ Identité — Chems...   │ │
│ │ pour vérifier que ça marche     │  │ ✓ Famille — Parents...  │ │
│ │                                 │  │                          │ │
│ │ ...                             │  │ (cliquables pour éditer) │ │
│ └─────────────────────────────────┘  └──────────────────────────┘ │
│                                                                   │
│ ┌─ Blocs actifs (50%, scrollable) ───────────────────────────────┐ │
│ │  [Âge : en attente de confirmation] [violet]                  │ │
│ │  [Motif : en cours 50%]             [violet]                  │ │
│ │  [Identité : 100%]  ← sera masqué dès qu'on scroll?          │ │
│ │  [Motif : 60%]                                                │ │
│ │  [Fratrie : 30%]                                              │ │
│ └───────────────────────────────────────────────────────────────┘ │
└───────────────────────────────────────────────────────────────────┘
```

### Comportement

1. **Bloc passe à 100%** → apparaît en zone "Blocs complétés" (avec ✓ vert)
2. **Zone Blocs complétés affiche** : Titre + 1-2 lignes d'aperçu + ✓
3. **Bloc reste cliquable** dans la zone complétés pour édition
4. **Bloc RESTE dans Blocs actifs** MAIS peut être masqué (eye-off icon)
5. **En fin de consultation** → vue d'ensemble finale avant sauvegarde

---

## Implémentation (Phases)

### Phase 1 — ViewModel : Computed Properties (~30 min)

Dans `ConsultationBlockViewModel` :

```csharp
public bool IsCompleted => ProgressPct >= 100;

public string CompactPreview
{
    get
    {
        if (string.IsNullOrWhiteSpace(FreeText)) return "";
        var lines = FreeText.Split('\n');
        var preview = lines[0].Length > 50
            ? lines[0].Substring(0, 47) + "..."
            : lines[0];
        return preview;
    }
}

private bool _isHidden = false;
public bool IsHidden
{
    get => _isHidden;
    set => SetProp(ref _isHidden, value);
}

public void ToggleHidden() => IsHidden = !IsHidden;
```

Ajouter une commande dans ViewModel :
```csharp
public ICommand ToggleBlockVisibilityCommand =>
    _toggleBlockVisibility ??= new RelayCommand(b =>
    {
        if (b is ConsultationBlockViewModel vm) vm.ToggleHidden();
    });
```

### Phase 2 — ViewModel Consultat ion : Collections séparées (~45 min)

Dans `ConsultationModeViewModel` :

```csharp
private ObservableCollection<ConsultationBlockViewModel> _activeBlocks = new();
private ObservableCollection<ConsultationBlockViewModel> _completedBlocks = new();

public IReadOnlyList<ConsultationBlockViewModel> ActiveBlocks => _activeBlocks;
public IReadOnlyList<ConsultationBlockViewModel> CompletedBlocks => _completedBlocks;

// Mettre à jour lors de chaque changement
private void UpdateBlockCollections()
{
    _activeBlocks.Clear();
    _completedBlocks.Clear();

    foreach (var block in InterrogatoireBlocks.Where(b => !b.IsHidden))
    {
        if (block.IsCompleted)
            _completedBlocks.Add(block);
        else
            _activeBlocks.Add(block);
    }
}

// Appeler depuis OnExtractedSegmentAsync après chaque mise à jour
// et depuis ToggleBlockVisibilityCommand
```

### Phase 3 — XAML : Nouvelle Layout (~1h)

Restructurer le Grid qui contient :
- Row 0 : Header (Âge, Motif)
- Row 1 : (NEW) Grid 2 colonnes 50/50
  - Col 0 : Transcription compactée (25%)
  - Col 1 : Blocs complétés (25%)
- Row 2 : Blocs actifs (50%)

**Transcription compactée :**
```xaml
<Border Background="#FAFAFA" BorderThickness="1" BorderBrush="#E0E0E0" CornerRadius="6" Padding="10">
    <ScrollViewer VerticalScrollBarVisibility="Auto" MaxHeight="150">
        <TextBlock Text="{Binding TranscriptionInput}"
                   TextWrapping="Wrap"
                   FontSize="11"
                   Foreground="#5D6D7E"
                   TextTrimming="CharacterEllipsis"
                   MaxLines="5"/>
    </ScrollViewer>
</Border>
```

**Blocs complétés :**
```xaml
<ItemsControl ItemsSource="{Binding CompletedBlocks}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <Border Margin="0,0,0,6" CornerRadius="6"
                    Background="#F0FAF5" BorderBrush="#27AE60" BorderThickness="1" Padding="8">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>
                    
                    <!-- Titre + préview -->
                    <StackPanel Grid.Column="0">
                        <TextBlock Text="{Binding Title}" FontSize="11" FontWeight="SemiBold"
                                   Foreground="#27AE60"/>
                        <TextBlock Text="{Binding CompactPreview}" FontSize="9"
                                   Foreground="#7F8C8D" Margin="0,2,0,0"/>
                    </StackPanel>
                    
                    <!-- Checkmark vert -->
                    <TextBlock Grid.Column="1" Text="✓" FontSize="16"
                               Foreground="#27AE60" FontWeight="Bold"/>
                </Grid>
                <!-- IsMouseOver → rendre cliquable pour éditer -->
                <Border.InputBindings>
                    <MouseBinding Gesture="LeftClick"
                                  Command="{Binding Path=DataContext.EditBlockCommand,
                                            RelativeSource={RelativeSource AncestorType=UserControl}}"
                                  CommandParameter="{Binding}"/>
                </Border.InputBindings>
            </Border>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

**Blocs actifs (avec eye-off icon) :**
```xaml
<!-- Ajouter bouton masquage dans la section titre du bloc -->
<Button Content="👁" Click="..." ToolTip="Masquer ce bloc"
        Width="24" Height="24" Padding="0" Margin="4,0,0,0"/>
```

### Phase 4 — Code-behind : Interactions (~30 min)

`ConsultationModeControl.xaml.cs` :

```csharp
private void HideBlockButton_Click(object sender, RoutedEventArgs e)
{
    if (sender is Button btn && btn.DataContext is ConsultationBlockViewModel vm)
    {
        vm.IsHidden = true;
        (DataContext as ConsultationModeViewModel)?.UpdateBlockCollections();
    }
}

private void CompletedBlockClicked(object sender, MouseButtonEventArgs e)
{
    if (sender is Border border && border.DataContext is ConsultationBlockViewModel vm)
    {
        // Afficher un popup/dialog pour éditer
        // Ou appeler vm.Edit() si existe
    }
}
```

### Phase 5 — Aperçu Final (~30 min)

À la fin de la consultation (FinalNote state), afficher une vue synthétique :

```xaml
<ItemsControl ItemsSource="{Binding InterrogatoireBlocks}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <Grid Margin="0,0,0,8">
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="120"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
                
                <TextBlock Grid.Column="0" Text="{Binding Title}" FontWeight="Bold" Foreground="#2C3E50"/>
                
                <TextBlock Grid.Column="1" Text="{Binding FreeText, StringFormat='Longueur : {0} car.'}"
                           FontSize="10" Foreground="#7F8C8D"/>
                
                <!-- Badge si 100% ou si vide -->
                <TextBlock Grid.Column="1" HorizontalAlignment="Right"
                           Text="{Binding ProgressPct, StringFormat='{}✓ {0}%'}"
                           Foreground="{Binding ProgressBarColor}"
                           Visibility="{Binding IsCompleted, Converter=...}"/>
            </Grid>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

---

## Estimation Totale

| Phase | Description | Durée |
|-------|-------------|-------|
| 1 | ViewModel : Computed + IsHidden | 30 min |
| 2 | Collections séparées (Active/Completed) | 45 min |
| 3 | XAML refactoring (layout 3x3 grid) | 1h |
| 4 | Code-behind interactions | 30 min |
| 5 | Aperçu final + polish | 30 min |
| — | Tests + ajustements | 30 min |
| **Total** | | **~3h30** |

---

## Vérifications Flash — Ce qui marche bien

✓ Architecture V0b (BlockSetResolver, détecteurs, chips)
✓ WhisperVocabService intégré
✓ Blocs contextuels par âge
✓ Freezing structure après ~90s
✓ Chips pour suggestions motif

---

## Petits Ajustements à Flash (Mini-fixes)

1. **IsStructureFrozen** — OK mais vérifier que les chips apparaissent seulement après motif détecté
2. **BlockPrefiller** — Vérifier qu'il n'injecte du texte que si bloc vide
3. **Transcription** — Réduire la hauteur (actuellement trop grande)
4. **Responsive** — Sur petits écrans, les 50/50% pourraient devenir 100% stackés

---

## Prochaine Session

1. Phase 1 + 2 ensemble (ViewModel) — 1h15
2. Phase 3 + 4 (XAML + interactions) — 1h30
3. Phase 5 + tests — 1h
4. **Total : ~3h30 pour la UI complète**

Ensuite on peut passer aux blocs eux-mêmes (développement complet block_library.json, LLM suggestions fine-tuned, etc.).
