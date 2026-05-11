# Plan — Blocs Adaptatifs Mode Consultation V0b

*Rédigé le 2026-05-09 — à implémenter après stabilisation V0a*

---

## Contexte

Le Mode Consultation V0a (en production) comporte 8 blocs cliniques fixes :
`identite`, `motif`, `famille`, `fratrie`, `atcds`, `scolarite`, `activites`, `maison`

Ces blocs couvrent 80 % des premières consultations. L'objectif V0b est d'ajouter des
**blocs complémentaires adaptatifs** qui apparaissent selon l'âge du patient et le motif
principal détecté en cours d'interrogatoire.

**Principe fondamental : on n'enlève jamais un bloc une fois affiché.** La structure ne
bouge plus après l'apparition — seuls des blocs peuvent être *ajoutés*.

---

## Blocs complémentaires prévus (11 nouveaux)

| Key | Titre affiché | Déclencheur typique |
|-----|--------------|---------------------|
| `sommeil` | Sommeil & rythmes | Tous âges |
| `comportement` | Comportement & régulation | TDAH, agitation, opposition |
| `vecu_emotionnel` | Vécu émotionnel | Anxiété, dépression, trauma |
| `alimentaire` | Alimentation & corporeité | TCA, somatisation, petite enfance |
| `developpement` | Développement précoce | < 10 ans, TSA, DI suspectée |
| `langage` | Langage & communication | < 8 ans, TSA, bégaiement |
| `motricite` | Motricité & coordination | < 10 ans, TDC, TSA |
| `puberte` | Puberté & corps | 10–17 ans |
| `adolescence` | Vie sociale & identité ado | 12–18 ans |
| `traumatisme` | Événements traumatiques | Tout âge si évoqué |
| `traitement` | Traitements en cours | Toujours pertinent si médicaments |

**Total : 19 blocs** (8 fixes + 11 adaptatifs)

---

## Architecture en 3 couches

```
Couche 1 — Règles d'âge    : BlockSetResolver        (synchrone, zéro LLM)
Couche 2 — Trigger motif   : MotifPrincipalDetector   (event sur CoveredThemes)
Couche 3 — Suggestions LLM : ContextualBlockSuggester (1 appel LLM par consultation)
```

### Couche 1 — BlockSetResolver

Chargé à l'ouverture de la consultation, à partir de l'âge du patient dans `patient.json`.

```
< 6 ans  → développement, langage, motricite, alimentaire (obligatoires)
6–11 ans → développement, langage (optionnels), sommeil, comportement
12–17 ans → adolescence, puberte, vecu_emotionnel
> 17 ans → adulte (blocs futurs V1)
```

Fichier source : `Resources/Consultation/block_library.json`

### Couche 2 — MotifPrincipalDetected

Événement ONE-SHOT déclenché quand `motif_principal` apparaît dans
`CoveredThemes` du bloc `motif`. Une fois déclenché, l'événement ne se redéclenche plus.

Timing réel : ~90 s après le début de l'interrogatoire (fin du 1er batch Whisper).

Cet événement lance la Couche 3.

### Couche 3 — ContextualBlockSuggester

Un seul appel LLM par consultation. Reçoit :
- Contenu textuel du bloc `motif`
- Blocs déjà actifs

Retourne une liste ordonnée de `blockKey` à suggérer (max 4).

Ces blocs apparaissent dans l'UI comme **chips** que le médecin peut accepter (✓) ou ignorer (✕).
Une fois acceptés, ils s'insèrent dans la liste des blocs actifs et le `BlockPrefiller`
les pré-remplit avec du contexte issu des blocs déjà remplis.

---

## Phases d'implémentation

### Phase A — Bibliothèque de blocs JSON (~1h)

Créer `MedCompanion/Resources/Consultation/block_library.json` :

```json
[
  {
    "key": "sommeil",
    "title": "Sommeil & rythmes",
    "expectedThemes": ["endormissement", "réveils", "durée", "cauchemars", "rythmicité"],
    "ageMin": 0,
    "ageMax": 99,
    "triggers": []
  },
  {
    "key": "comportement",
    "title": "Comportement & régulation",
    "expectedThemes": ["agitation", "opposition", "impulsivité", "crises", "règles"],
    "ageMin": 3,
    "ageMax": 18,
    "triggers": ["TDAH", "agitation", "opposition", "comportement"]
  }
]
```

Ajouter tous les 11 blocs complémentaires avec leurs `expectedThemes` et `triggers`.

### Phase B — BlockSetResolver (~1h)

```csharp
// MedCompanion/Services/Consultation/BlockSetResolver.cs
public class BlockSetResolver
{
    public List<BlockDefinition> Resolve(int ageYears)
    {
        // Charge block_library.json
        // Filtre par ageMin <= ageYears <= ageMax
        // Retourne les blocs éligibles (sans les 8 fixes qui sont toujours présents)
    }
}
```

Appelé dans `ConsultationModeViewModel` à l'initialisation, après chargement du patient.

### Phase C — MotifPrincipalDetector (~30 min)

Dans `ConsultationModeViewModel`, surveiller les `CoveredThemes` du bloc `motif` :

```csharp
// Après chaque mise à jour de bloc par l'extraction :
if (!_motifDetected
    && block.Key == "motif"
    && block.CoveredThemes.Contains("motif_principal"))
{
    _motifDetected = true;
    _ = OnMotifPrincipalDetectedAsync(block.FreeText);
}
```

### Phase D — ContextualBlockSuggester (~2h)

```csharp
// MedCompanion/Services/Consultation/ContextualBlockSuggester.cs
public async Task<List<string>> SuggestBlocksAsync(
    ILLMService llm,
    string motifText,
    List<string> activeBlockKeys)
{
    // 1 appel LLM avec prompt dédié
    // Retourne liste de blockKey ordonnée (max 4)
}
```

Prompt : `Resources/Consultation/prompt_block_suggest.txt`
- Reçoit le texte du motif + liste des blocs déjà actifs
- Retourne `["sommeil", "comportement"]` (JSON pur)
- Conservateur : max 4 suggestions, seulement si >80% confiance

### Phase E — BlockPrefiller (~1h)

Une fois un bloc accepté via chip, le pré-remplir avec des infos pertinentes
issues des blocs déjà remplis (ex : si `atcds` mentionne "insomnie", pré-remplir
`sommeil` avec "ATCDs : insomnie mentionnée").

```csharp
// MedCompanion/Services/Consultation/BlockPrefiller.cs
public string Prefill(BlockDefinition newBlock, List<ConsultationBlock> existingBlocks)
{
    // Recherche mentions pertinentes dans les blocs existants
    // Retourne texte de pré-remplissage ou ""
}
```

### Phase F — ViewModel (~2h)

Dans `ConsultationModeViewModel` :

```csharp
// Nouveaux champs
public ObservableCollection<BlockSuggestionViewModel> BlockSuggestions { get; }
public bool HasBlockSuggestions => BlockSuggestions.Count > 0;

// Commandes sur BlockSuggestionViewModel
public ICommand AcceptCommand { get; }   // → ajoute le bloc, préremplit, retire le chip
public ICommand DismissCommand { get; }  // → retire le chip sans ajouter le bloc
```

`BlockSuggestionViewModel` expose : `BlockKey`, `BlockTitle`, `Rationale` (explication LLM).

### Phase G — UI chips (~1h30)

Dans `ConsultationModeControl.xaml`, dans la zone blocs :

```xml
<!-- Bandeau suggestions de blocs (apparaît sous le dernier bloc actif) -->
<ItemsControl ItemsSource="{Binding BlockSuggestions}"
              Visibility="{Binding HasBlockSuggestions, Converter=...}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <Border Background="#1E3A5F" CornerRadius="6" Margin="0,4">
                <StackPanel Orientation="Horizontal">
                    <TextBlock Text="+ " Foreground="#5B9BD5"/>
                    <TextBlock Text="{Binding BlockTitle}" Foreground="White"/>
                    <TextBlock Text="{Binding Rationale}" Foreground="#888" FontSize="11"/>
                    <Button Content="✓" Command="{Binding AcceptCommand}"/>
                    <Button Content="✕" Command="{Binding DismissCommand}"/>
                </StackPanel>
            </Border>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

---

## Estimation totale

| Phase | Description | Durée estimée |
|-------|-------------|---------------|
| A | block_library.json (19 blocs complets) | 1h |
| B | BlockSetResolver | 1h |
| C | MotifPrincipalDetected event | 30 min |
| D | ContextualBlockSuggester + prompt | 2h |
| E | BlockPrefiller | 1h |
| F | ViewModel (chips, commandes) | 2h |
| G | UI chips XAML + styles | 1h30 |
| — | Tests + ajustements | 2h |
| **Total** | | **~11h** |

---

## Whisper — Ajout noms locaux (Le Pradet, Var)

### Villes à ajouter dans `InitialPrompt`

Rayon 20 km autour du Pradet (83220) — noms que Whisper peut mal transcrire :

```
Le Pradet, La Garde, La Valette-du-Var, Le Revest-les-Eaux,
Six-Fours-les-Plages, Sanary-sur-Mer, Ollioules,
Hyères, La Crau, Carqueiranne, Solliès-Pont, Cuers
```

Ajout dans la constante `InitialPrompt` de `WhisperStreamingService.cs` :
```csharp
"Lieux : Le Pradet, La Garde, La Valette-du-Var, Six-Fours-les-Plages, " +
"Sanary-sur-Mer, Ollioules, Hyères, La Crau, Carqueiranne, Solliès-Pont. "
```

### Mon avis sur les noms d'établissements scolaires

**OUI pour les noms atypiques, NON pour les génériques.**

**Pourquoi ça vaut le coup :**
- Les noms d'écoles apparaissent souvent dans l'interrogatoire ("il est au collège X",
  "scolarisé à l'ITEP de Y")
- Whisper transcrit mal les noms propres locaux non-présents dans son corpus
- Un seul ajout dans `InitialPrompt` → bénéfice permanent

**Ce qu'il faut ajouter :**
- Noms inhabituels / provençaux / sans équivalent dictionnaire :
  ex. "Lou Bancau", "Valbertrand", "Val d'Azur", "Terre-Neuve"
- Noms d'ITEP/IME/SESSAD locaux si peu connus

**Ce qu'il ne faut PAS ajouter :**
- "Collège Hugo", "Lycée Bonaparte" → déjà dans le vocabulaire de Whisper
- Noms trop longs qui rognent le budget de prompt (la constante a une limite implicite ~200 tokens)

**Recommandation pratique :**
Ajouter une liste de ~10-15 établissements locaux atypiques, en priorité les ITEP/IME/SESSAD
du Var qui sont les plus susceptibles d'apparaître dans le discours des familles.
Collecter ces noms directement depuis les dossiers existants (établissements déjà mentionnés
par les patients).

---

## Whisper — Vocabulaire personnalisé depuis l'application

**Constat :** les établissements locaux changent, s'ajoutent, et le médecin les connaît mieux que le code.
Passer par un commit à chaque ajout n'est pas viable à long terme.

**Solution : fichier texte éditable depuis l'UI**

- Fichier : `Documents/MedCompanion/whisper_vocab_custom.txt` (un terme par ligne)
- Lu au démarrage de `WhisperStreamingService`, concaténé à la fin de `InitialPrompt`
- UI : petite zone dans les réglages du Mode Consultation — champ "Ajouter un terme" + liste éditable

**Implémentation (~1h) :**

```csharp
// Dans WhisperStreamingService, au démarrage :
var customVocabPath = Path.Combine(PathService.DataRoot, "whisper_vocab_custom.txt");
if (File.Exists(customVocabPath))
{
    var terms = File.ReadAllLines(customVocabPath, Encoding.UTF8)
                    .Where(l => !string.IsNullOrWhiteSpace(l));
    _effectivePrompt = InitialPrompt + " " + string.Join(", ", terms) + ".";
}
else
{
    _effectivePrompt = InitialPrompt;
}
```

L'UI affiche la liste, permet d'ajouter/supprimer sans redémarrer l'app
(le fichier est relu à chaque `StartAsync()`).

---

## Prochaine session : ordre d'exécution recommandé

1. ~~Commit du fix microphone BadDeviceId~~ ✓ fait (939f54e)
2. ~~Ajout noms de villes + établissements Var dans `InitialPrompt`~~ ✓ fait (9f7059c, 205151a)
3. Vocabulaire personnalisé depuis l'UI (~1h) — **priorité avant blocs adaptatifs**
4. Phase A (block_library.json) — socle pour toute la suite
5. Phases B + C ensemble — pas de LLM, logique pure
6. Phase D (ContextualBlockSuggester) — le seul appel LLM supplémentaire
7. Phases E + F + G — UI et intégration
