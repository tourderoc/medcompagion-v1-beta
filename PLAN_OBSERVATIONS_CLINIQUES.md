# Plan Implémentation — Observations Cliniques ("Clinique / Enfant")

*Date : 2026-05-13 — Deuxième partie de la 1ère Consultation*

---

## Contexte

Le Mode Consultation V0c inclut actuellement l'**Interrogatoire** (Anamnèse/Parents) avec transcription Whisper et extraction LLM.

Nous ajoutons maintenant la **deuxième phase** : **Observations Générales** (Clinique/Enfant).
- Temps : ~10 minutes
- Médecin seul avec l'enfant
- Interface ultra-rapide : quick-checks (boutons radio) + texte libre optionnel
- Saisie par **clic/souris** (pas clavier)

---

## Objectif UX/UI

### Navigation
Sous le titre "1ère Consultation", créer **deux tabs/boutons distincts** :
- **Tab 1** : "Anamnèse / Parents" (interface existante de transcription)
- **Tab 2** : "Clinique / Enfant" (nouvelle interface à créer)

Clic sur Tab 2 masque la transcription et affiche les **10 Cartes d'Observation**.

### Structure
**Grille 2 colonnes** de cartes (5 cartes par colonne).

Chaque **Carte d'Observation** :
- Titre (branche clinique)
- Série de boutons radio (1 choix à la fois) — chips colorées, click-friendly
- Bouton "+" pour révéler zone texte libre (optionnelle)
- Zone texte s'agrandit quand on clique

---

## Les 10 Briques d'Observation

### Ordre optimal (progression clinique)

| # | Branche | Options | Notes |
|---|---------|---------|-------|
| 1 | **Contact/Rapport** | Bon, Distant, Fuyant, Adhésif, Instable | Établir lien PREMIER |
| 2 | **Langage** | Adapté, Riche, Pauvre/Immaturité, Inexistant | Fondamental, observable |
| 3 | **Compréhension** | Adaptée, Limitée, Consignes simples uniquement | Complément du langage |
| 4 | **Psychomotricité** | Harmonieuse, Instabilité motrice, Inhibition, Maladresse | Observable facilement |
| 5 | **Mimique & Regard** | Expressive, Faciès figé, Regard fuyant, Pauvreté des mimiques | Observation fine |
| 6 | **Profil Cognitif estimé** | Harmonieux, Dysharmonieux, Supérieur, Retard suspecté | Synthèse globale |
| 7 | **Humeur / Anxiété** | Stable, Triste, Irritable, Angoissé | Psychoaffectif |
| 8 | **Imaginaire / Jeu** | Riche, Pauvre, Bizarre, Stéréotypé | Créativité, TSA markers |
| 9 | **Rapport au cadre** | Respecté, Opposition, Désinhibé, Passif | Compliance, comportement |
| 10 | **Vigilance** | R.A.S, Signes de négligence, Signes de maltraitance | **SÉCURITÉ** — fin, sensible |

---

## Architecture Technique

### Models (ConsultationModels.cs)

```csharp
public enum ClinicalObservationBranch
{
    Contact,
    Langage,
    Comprehension,
    Psychomotricite,
    MimiquRegard,
    ProfilCognitif,
    HumeurAnxiete,
    ImaginaireJeu,
    RapportCadre,
    Vigilance
}

public class ClinicalObservationCard
{
    public ClinicalObservationBranch Branch { get; set; }
    public string Title { get; set; }
    public List<string> Options { get; set; } = new();
    public string? SelectedOption { get; set; }  // null si aucun choix
    public string FreeText { get; set; } = "";   // optionnel
    public bool IsExpanded { get; set; } = false; // texte libre visible?
}

public class ClinicalObservationsSession
{
    public List<ClinicalObservationCard> Cards { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public string? GeneratedClinicalNarrative { get; set; } // fusion IA
}
```

### ViewModel (ConsultationModeViewModel.cs)

Ajouter :
```csharp
// État observations cliniques
private ClinicalObservationsSession _clinicalObservations = new();
public ClinicalObservationsSession ClinicalObservations 
{
    get => _clinicalObservations;
    set => SetProperty(ref _clinicalObservations, value);
}

private bool _isInClinicalMode = false;
public bool IsInClinicalMode
{
    get => _isInClinicalMode;
    set => SetProperty(ref _isInClinicalMode, value);
}

// Commandes
public ICommand SwitchToInterrogatoireCommand { get; }
public ICommand SwitchToClinicalCommand { get; }
public ICommand SelectObservationCommand { get; }
public ICommand ToggleCardExpandCommand { get; }
public ICommand TerminateObservationsCommand { get; }
```

Méthodes :
```csharp
private void InitializeClinicalObservations()
{
    _clinicalObservations.Cards.Clear();
    // Créer 10 cartes avec options
    AddCard(ClinicalObservationBranch.Contact, "Contact/Rapport",
        new[] { "Bon", "Distant", "Fuyant", "Adhésif", "Instable" });
    // ... etc pour les 10 briques
}

public void SelectObservationOption(ClinicalObservationCard card, string option)
{
    card.SelectedOption = option;
    // Auto-save local
}

public void ToggleCardExpand(ClinicalObservationCard card)
{
    card.IsExpanded = !card.IsExpanded;
}

public async Task TerminateClinicalObservationsAsync()
{
    // Générer le paragraphe clinique via LLM
    var narrative = await GenerateClinicalNarrativeAsync(_clinicalObservations);
    _clinicalObservations.GeneratedClinicalNarrative = narrative;
    
    // Basculer à la vue "Note finale"
    // ...
}

private async Task<string> GenerateClinicalNarrativeAsync(ClinicalObservationsSession obs)
{
    // Construire prompt LLM
    var prompt = "Génère un paragraphe clinique cohérent à partir de ces observations:\n";
    foreach (var card in obs.Cards)
    {
        if (card.SelectedOption != null)
            prompt += $"- {card.Title}: {card.SelectedOption}";
        if (!string.IsNullOrWhiteSpace(card.FreeText))
            prompt += $" ({card.FreeText})";
        prompt += "\n";
    }
    
    var (ok, result, _) = await _llmService.ChatAsync(prompt, new(), maxTokens: 500);
    return ok ? result : "";
}
```

### XAML (ConsultationModeControl.xaml)

#### Tabs Navigation
```xaml
<StackPanel Orientation="Horizontal" Margin="0,0,0,12">
    <Button Content="📋 Anamnèse / Parents"
            Command="{Binding SwitchToInterrogatoireCommand}"
            Style="{DynamicResource TabActiveStyle}"/>
    <Button Content="👁 Clinique / Enfant"
            Command="{Binding SwitchToClinicalCommand}"
            Style="{DynamicResource TabInactiveStyle}"/>
</StackPanel>
```

#### Clinical Cards Grid
```xaml
<!-- Grille 2 colonnes -->
<Grid Visibility="{Binding IsInClinicalMode, Converter={StaticResource BoolToVis}}">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition Width="*"/>
    </Grid.ColumnDefinitions>
    
    <ItemsControl ItemsSource="{Binding ClinicalObservations.Cards}">
        <!-- Template pour chaque carte -->
        <ItemsControl.ItemTemplate>
            <DataTemplate>
                <Border Background="#FAFAFA" BorderThickness="1" BorderBrush="#E0E0E0" 
                        CornerRadius="6" Padding="12" Margin="6">
                    
                    <!-- Titre + expand btn -->
                    <Grid>
                        <Grid.RowDefinitions>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="Auto"/>
                            <RowDefinition Height="Auto"/>
                        </Grid.RowDefinitions>
                        
                        <!-- Titre -->
                        <TextBlock Grid.Row="0" Text="{Binding Title}" 
                                   FontSize="12" FontWeight="SemiBold" 
                                   Foreground="#2C3E50" Margin="0,0,0,8"/>
                        
                        <!-- Chips / Radio buttons -->
                        <WrapPanel Grid.Row="1" Margin="0,0,0,8">
                            <ItemsControl ItemsSource="{Binding Options}">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <Button Content="{Binding}"
                                                Command="{Binding Path=DataContext.SelectObservationCommand, 
                                                          RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                                CommandParameter="{Binding}"
                                                Style="{StaticResource ChipButtonStyle}"
                                                Margin="0,0,4,4"/>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                        </WrapPanel>
                        
                        <!-- Bouton expand texte + Zone texte -->
                        <StackPanel Grid.Row="2">
                            <Button Content="+ Notes" 
                                    Command="{Binding Path=DataContext.ToggleCardExpandCommand,
                                              RelativeSource={RelativeSource AncestorType=Border}}"
                                    CommandParameter="{Binding}"
                                    FontSize="10" Background="Transparent" 
                                    BorderThickness="0" Foreground="#3498DB" Cursor="Hand"/>
                            
                            <TextBox Text="{Binding FreeText, UpdateSourceTrigger=PropertyChanged}"
                                     Visibility="{Binding IsExpanded, Converter={StaticResource BoolToVis}}"
                                     AcceptsReturn="True" TextWrapping="Wrap"
                                     Height="60" Margin="0,6,0,0" Padding="6"/>
                        </StackPanel>
                    </Grid>
                </Border>
            </DataTemplate>
        </ItemsControl.ItemTemplate>
    </ItemsControl>
</Grid>
```

---

## Phases Implémentation

### Phase A — Modèles & Initialization (~1h)

1. Ajouter enums + classes ConsultationModels.cs
2. Initialiser les 10 cartes avec options
3. Parser depuis JSON ou hardcoder?

### Phase B — ViewModel (~1.5h)

1. Ajouter properties + commands
2. Implémenter switch entre Interrogatoire ↔ Clinique
3. SelectObservationOption, ToggleCardExpand
4. State management (sauvegarder sélections)

### Phase C — XAML UI (~1.5h)

1. Tabs navigation
2. Grid 2 colonnes avec cartes
3. Chips styling (couleurs, feedback visuel)
4. TextBox déroulable

### Phase D — LLM Integration (~1h)

1. GenerateClinicalNarrativeAsync
2. Prompt engineering (observations → paragraphe cohérent)
3. Fusionner dans le rapport final

### Phase E — Tests & Polish (~30 min)

1. Clic-flow fluide
2. Auto-save observations (localStorage)
3. Validation avant terminer? (soft)

---

## Estimation Totale

| Phase | Durée |
|-------|-------|
| A | 1h |
| B | 1.5h |
| C | 1.5h |
| D | 1h |
| E | 0.5h |
| **Total** | **~5.5h** |

---

## Intégration avec Consultation Existante

### Workflow complet :

```
1. Consultation initialise en Mode Interrogatoire
   ├─ Médecin avec parents (10-15 min)
   ├─ Whisper transcription + extraction LLM
   └─ Bloc age + motif → 100% → Structure gelée

2. Médecin clique "Terminer Anamnèse"
   └─ Basculer Tab → Clinique/Enfant

3. Mode Clinique actif
   ├─ Médecin seul avec enfant (10 min)
   ├─ Clique sur options → sélection visuelle (chip colorée)
   ├─ Clique "+" pour notes libres optionnelles
   └─ Clic "Terminer Observations"

4. LLM génère paragraphe clinique
   └─ Fusionné dans rapport final = Note Anamnèse + Note Clinique

5. "Note Finale" affiche :
   - Résumé blocs interrogatoire (aperçu V0c)
   - Qualité check signalements
   - **Paragraphe clinique généré**
   - TextBox pour éditions manuelles
   - Bouton "Sauvegarder dossier"
```

---

## Risques & Mitigations

| Risque | Impact | Mitigation |
|--------|--------|-----------|
| LLM génère du charabia | Rupture clinique | Valider prompt, ajouter examples, fallback texte brut |
| 10 min pas assez | Médecin stressé | Timer visible (optionnel), pas bloquant |
| Oubli d'une branche | Data incomplete | Soft validation: "Vous avez pas coché Contact — continuer?" |
| Performance grid 10 cartes | Slow rendering | Virtualize si besoin, lazy-load TextBox |

---

## Données Stockage

### Par consultation :
```json
{
  "interrogatoire": { ... },
  "clinicalObservations": {
    "cards": [
      {
        "branch": "Contact",
        "selectedOption": "Bon",
        "freeText": "Enfant souriant, regard direct"
      },
      ...
    ],
    "generatedClinicalNarrative": "Enfant présenté en consultation... [paragraphe fusionné]"
  }
}
```

---

## Prochaines Étapes

1. ✓ Approbation plan
2. Démarrer Phase A (modèles)
3. Tester UI flow
4. LLM integration
5. Merge avec système existant

---

## Notes

- **Design sobre** : garder cohérence avec interface V0c existante
- **Click-friendly** : pas clavier, accélérer workflow médecin
- **Auto-save** : chaque sélection = sauvegarde locale
- **Flexible** : texte libre partout, données structurées = optionnel
