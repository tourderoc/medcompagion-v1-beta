# Plan d'implémentation — Étape 4 : Cartographie de l'enfant (V0)

**Source clinique** : Dr Lassoued, *Connaître son enfant — Tome 2 : La chenille universelle* (3-11 ans).
**Statut** : V0, mode médecin uniquement.
**Position dans le flow** : après étape 3 (Synthèse diagnostique), avant clôture. Étape 5 (Cartographie de l'environnement) stubbée pour plus tard.

---

## 1. Vue d'ensemble

La cartographie de l'enfant est un **outil de repérage structuré** (pas un diagnostic) qui produit un portrait développemental sur **7 segments** :

1. Attachement & sécurité intérieure
2. Psychomotricité & exploration
3. Tempérament
4. Langage & communication
5. Émotions
6. Imaginaire & monde intérieur
7. Pensée & organisation cognitive

**Inputs** (saisis par le médecin pendant la consultation) :
- 6 segments avec questionnaire 6 items binaires (Oui/Non) — segments 1, 2, 4, 5, 6, 7
- 1 segment "Tempérament" avec profil sur 6 axes (échelle 1-5)
- Total : 36 cases à cocher + 6 sliders

**Outputs** (calculés automatiquement) :
- Pour chaque segment de questionnaire : score brut 0-6 → **niveau couleur** via grille conditionnée par âge
- Pour le tempérament : profil sur 6 axes (pas de couleur, juste les valeurs)
- Lecture émotionnelle textuelle correspondant au niveau

**Restrictions** :
- Patient 3-11 ans uniquement. Hors fourchette → étape sautée + message clair, validation étape 3 mène directement à la clôture.
- Aucune dépendance LLM (scoring 100 % déterministe).

**Hors scope V0** :
- Rendu visuel "chenille colorée" (réservé à la future étape de restitution aux parents).
- Mode "questionnaire parent" exportable PDF.
- Tranches d'âge hors 3-11.

---

## 2. Architecture

### 2.1 Récupération de l'âge

`PatientIndexEntry.Age` (propriété calculée depuis `Dob`) est déjà accessible via `_patient?.Age` dans `EvaluationPhaseViewModel`, déjà utilisée par les étapes 2 et 3. Aucun nouveau service.

### 2.2 Évolution de l'enum

```csharp
public enum EvaluationStep
{
    Preparation = 1,
    EvaluationCiblee = 2,
    Synthese = 3,
    CartographieEnfant = 4,       // renommé depuis "Cartographie"
    CartographieEnvironnement = 5 // nouveau, stubbé
}
```

### 2.3 Grilles de scoring (UNE par âge, valable pour TOUS les segments de questionnaire)

Source canonique : [GRILLES_CARTOGRAPHIE_CANONIQUES.md](GRILLES_CARTOGRAPHIE_CANONIQUES.md) — verrouillé par l'auteur.

**6 niveaux de couleur** (du meilleur au plus préoccupant) :
`VertFonce > VertClair > JauneClair > JauneFonce > RougeClair > RougeFonce`

**Une seule grille par tranche d'âge**, applicable identiquement aux 6 segments de questionnaire (1, 2, 4, 5, 6, 7) :

| Âge | 6 | 5 | 4 | 3 | 2 | 1 | 0 |
|-----|---|---|---|---|---|---|---|
| 3-4 | VF | VC | VC | JC | JF | RC | RF |
| 5-6 | VF | VC | JC | JF | RC | RF | RF |
| 7-9 | VF | VC | JC | RF | RF | RF | RF |
| 10-11 | VF | JF | RF | RF | RF | RF | RF |

> Note clinique : la grille évolue avec l'âge — un score 4 chez un 3-4 ans = vert clair (bien), le même score 4 chez un 10-11 ans = rouge foncé (alerte). Ce qui est normatif à 3 ans devient préoccupant à 11 ans.

**Segment 3 — Tempérament** : pas de score, pas de couleur. **Profil descriptif** sur 6 axes (1 à 5), affiché tel quel (radar chart ou liste).

### 2.4 Lexique d'interprétation des couleurs

**Un seul lexique** (pas un par segment). Le clinicien adapte mentalement au domaine évalué.

| Couleur | Synthèse clinique |
|---|---|
| Vert foncé | Compétence intégrée — sécurité solide, lien stable, langage fluide |
| Vert clair | Base sécurisante présente — organisation cognitive fonctionnelle, corps actif |
| Jaune clair | Ambivalence — équilibre fragile, vérification du lien, jeu symbolique irrégulier |
| Jaune foncé | Fragilité marquée — instabilité, difficulté à symboliser, émotions difficilement canalisées |
| Rouge clair | Anxiété et tension — communication détournée, blocages, appel au lien |
| Rouge foncé | Difficulté majeure — insécurité affective, isolement verbal, désorganisation cognitive |

Stocké en constantes statiques (`CartographieContent.cs`), pas en LLM.

---

## 3. Modèles de données

Fichiers à créer dans `MedCompanion/Models/Evaluations/` :

### `CartographieEnfant.cs`
```csharp
public class CartographieEnfant : INotifyPropertyChanged
{
    public int? AgeAuMomentDeLaSaisie { get; set; }  // snapshot, pour traçabilité

    public ChenilleSegment Attachement       { get; }
    public ChenilleSegment Psychomotricite   { get; }
    public TemperamentProfile Temperament    { get; }
    public ChenilleSegment Langage           { get; }
    public ChenilleSegment Emotions          { get; }
    public ChenilleSegment Imaginaire        { get; }
    public ChenilleSegment Pensee            { get; }

    public DateTime? ValidationDate { get; set; }
    public bool IsValidated => ValidationDate.HasValue;
}
```

### `ChenilleSegment.cs`
```csharp
public class ChenilleSegment : INotifyPropertyChanged
{
    public string Key { get; }                      // "attachement", "langage", ...
    public string Label { get; }                    // "Attachement & sécurité intérieure"
    public string PhraseBoussole { get; }           // "Il a besoin de sentir qu'il peut s'éloigner..."

    public ObservableCollection<ChenilleItem> Items { get; }  // 6 items binaires

    // Recalculés automatiquement quand Items change
    public int Score { get; }                       // 0-6, somme des Oui
    public NiveauSegment? Niveau { get; }           // calculé via grille par âge
    public string LectureEmotionnelle { get; }      // texte du livre
}

public class ChenilleItem : INotifyPropertyChanged
{
    public string Affirmation { get; }
    public bool IsChecked { get; set; }
}
```

### `TemperamentProfile.cs`
```csharp
public class TemperamentProfile : INotifyPropertyChanged
{
    public int NiveauActivite      { get; set; }   // 1-5
    public int Regularite          { get; set; }
    public int ReactiviteSensorielle { get; set; }
    public int IntensiteEmotionnelle { get; set; }
    public int Adaptabilite        { get; set; }
    public int TempsDeReaction     { get; set; }
}
```

### `NiveauSegment.cs`
```csharp
public enum NiveauSegment
{
    VertFonce,
    VertClair,
    JauneClair,
    JauneFonce,
    RougeClair,
    RougeFonce
}
```

### Modif `EvaluationPhase.cs`
- Ajouter `public CartographieEnfant CartographieEnfant { get; set; } = new();`
- Ajouter `public bool IsCartographieEnfantValidated => CartographieEnfant.IsValidated;`
- Renommer enum `Cartographie` → `CartographieEnfant`, ajouter `CartographieEnvironnement = 5`

---

## 4. Service de scoring

Fichier : `MedCompanion/Services/Evaluations/CartographieScoringService.cs`

```csharp
public static class CartographieScoringService
{
    // Une seule grille par tranche d'âge, identique pour tous les segments
    public static NiveauSegment? Calculer(int score, int? age)
    {
        if (age == null || age < 3 || age > 11) return null;
        if (score < 0 || score > 6) return null;
        return Grille[ChoisirBande(age.Value)][score];
    }

    private static BandeAge ChoisirBande(int age) => age switch
    {
        <= 4 => BandeAge.ThreeFour,
        <= 6 => BandeAge.FiveSix,
        <= 9 => BandeAge.SevenNine,
        _    => BandeAge.TenEleven
    };

    private enum BandeAge { ThreeFour, FiveSix, SevenNine, TenEleven }

    private static readonly Dictionary<BandeAge, NiveauSegment[]> Grille = new()
    {
        // index = score (0 à 6)
        [BandeAge.ThreeFour]  = { RougeFonce, RougeClair, JauneFonce, JauneClair, VertClair, VertClair, VertFonce },
        [BandeAge.FiveSix]    = { RougeFonce, RougeFonce, RougeClair, JauneFonce, JauneClair, VertClair, VertFonce },
        [BandeAge.SevenNine]  = { RougeFonce, RougeFonce, RougeFonce, RougeFonce, JauneClair, VertClair, VertFonce },
        [BandeAge.TenEleven]  = { RougeFonce, RougeFonce, RougeFonce, RougeFonce, RougeFonce, JauneFonce, VertFonce },
    };
}
```

100 % déterministe, testable unitairement, **largement plus simple** que ce qui était prévu initialement (une seule grille au lieu de trois).

---

## 5. Persistance

Étendre `EvaluationPhaseService` :

### Sérialisation YAML (ajouter sous-bloc)
```yaml
cartographie_enfant:
  age_au_moment: 7
  validee: true
  validation_date: 2026-06-15T10:30:00
  attachement:
    items: [true, true, false, true, true, false]
    score: 4
    niveau: jaune_clair
  psychomotricite:
    items: [true, false, true, false, true, true]
    score: 4
    niveau: vert_clair
  temperament:
    activite: 5
    regularite: 2
    reactivite_sensorielle: 4
    intensite_emotionnelle: 5
    adaptabilite: 2
    temps_reaction: 4
  langage: ...
  emotions: ...
  imaginaire: ...
  pensee: ...
```

### Rendu Markdown lisible
Ajouter une section `## Étape 4 — Cartographie de l'enfant` au document, avec pour chaque segment :
- Titre + phrase-boussole
- Score + niveau (en couleur via emoji 🟢🟡🔴)
- Lecture émotionnelle

---

## 6. ViewModel

Étendre `EvaluationPhaseViewModel` :

### Nouveaux flags d'état
```csharp
public bool IsWorkingCartographieEnfant { get; set; }
public bool IsCartographieDisponible => _patient?.Age is >= 3 and <= 11;
```

### Nouveaux exposés
```csharp
public CartographieEnfant? Cartographie => Phase?.CartographieEnfant;
```

### Nouvelles commandes
```csharp
public ICommand ValidateCartographieEnfantCommand { get; }
public ICommand BackToSyntheseCommand { get; }
```

### Modif routing
- `ValidateSynthese()` :
  - Si `IsCartographieDisponible` → ne pas clôturer, passer à `IsWorkingCartographieEnfant = true`, mettre `Phase.EtapeCourante = CartographieEnfant`
  - Sinon → clôture comme aujourd'hui (avec message "Cartographie non applicable pour cet âge — clôture directe")
- `ValidateCartographieEnfant()` :
  - V0 : clôture directe (étape 5 stubbée). Plus tard : passer à `CartographieEnvironnement`.
- `ShowPhase()` (mode lecture seule) : router vers `IsWorkingCartographieEnfant` si `Phase.EtapeCourante == CartographieEnfant`.

---

## 7. XAML

### `EvaluationPhaseControl.xaml`

Ajouter un **5e cas** (CAS 2quater) après l'étape 3 :

```xml
<Border Visibility="{Binding IsWorkingCartographieEnfant, Converter={StaticResource BoolToVis}}">
  <StackPanel>
    <TextBlock Text="🐛 ÉTAPE 4 — CARTOGRAPHIE DE L'ENFANT"/>

    <!-- 6 cards expansibles par segment de questionnaire -->
    <Expander Header="🧷 Attachement & sécurité intérieure">
      <StackPanel>
        <TextBlock Text="{Binding PhraseBoussole}"/>
        <ItemsControl ItemsSource="{Binding Items}">
          <ItemsControl.ItemTemplate>
            <DataTemplate>
              <CheckBox Content="{Binding Affirmation}" IsChecked="{Binding IsChecked}"/>
            </DataTemplate>
          </ItemsControl.ItemTemplate>
        </ItemsControl>
        <Border Background="{Binding NiveauColor}" Padding="6">
          <TextBlock Text="{Binding NiveauLabel}"/>
        </Border>
        <TextBlock Text="{Binding LectureEmotionnelle}" FontStyle="Italic"/>
      </StackPanel>
    </Expander>

    <!-- Card tempérament (6 sliders) -->
    <Expander Header="🔥 Tempérament">
      <Grid>
        <Slider Minimum="1" Maximum="5" Value="{Binding NiveauActivite}"/>
        <!-- etc. -->
      </Grid>
    </Expander>

    <!-- Actions -->
    <StackPanel Orientation="Horizontal">
      <Button Content="↩ Retour Étape 3" Command="{Binding BackToSyntheseCommand}"/>
      <Button Content="✓ Valider et clôturer" Command="{Binding ValidateCartographieEnfantCommand}"/>
    </StackPanel>
  </StackPanel>
</Border>
```

### Mode lecture seule
Ajouter dans la bannière "📖 Évaluation clôturée" un 4e bouton "Étape 4 — Cartographie" qui appelle `ViewStepCommand` avec param `"4"`. Étendre `ViewStep` dans le VM pour gérer le param "4".

---

## 8. Affichage dans le dossier bleu

Le bloc `DiagnosticSyntheseCardViewModel` (déjà rendu dans l'onglet SYNTHESE) est complété par un sous-bloc "🐛 Cartographie de l'enfant" :
- Pour chaque segment : nom + niveau (badge coloré) + score brut
- Profil tempérament : 6 lignes "Activité : 5/5", etc.

À implémenter en étendant `DiagnosticSyntheseCardViewModel` et son rendu XAML.

---

## 9. Ordre d'implémentation (sous-étapes)

| # | Sous-étape | Fichiers principaux | Effort |
|---|---|---|---|
| 1 | Enum + modèles de données | `EvaluationPhase.cs`, nouveaux fichiers `Models/Evaluations/` | ~30 min |
| 2 | Service de scoring (1 seule grille par âge) | `CartographieScoringService.cs` | ~20 min |
| 3 | Constantes : items + phrases-boussoles + lexique unique des couleurs | `CartographieContent.cs` (statique) | ~25 min |
| 4 | Persistance YAML + Markdown | `EvaluationPhaseService.cs` (extension) | ~45 min |
| 5 | ViewModel : flags, expositions, commandes, routing | `EvaluationPhaseViewModel.cs` | ~45 min |
| 6 | XAML étape 4 (CAS 2quater) | `EvaluationPhaseControl.xaml` | ~60 min |
| 7 | Mode lecture seule : nav étape 4 dans bannière | XAML + VM `ViewStep` | ~15 min |
| 8 | Skip si âge hors fourchette + messages UI | `ValidateSynthese()` + dialog | ~20 min |
| 9 | Affichage dans dossier bleu (extension du bloc synthèse) | `DiagnosticSyntheseCardViewModel`, `ConsultationModeControl.xaml` | ~45 min |
| 10 | Tests bout en bout + corrections | tous fichiers | ~60 min |

**Total estimé : ~5-7 heures** sur 1-2 sessions (revu à la baisse grâce à la simplification "1 seule grille par âge").

---

## 10. Risques et edge cases

- **Date de naissance manquante** (`Dob` vide → `Age` = null) : étape sautée comme hors fourchette + message demander de compléter la fiche patient.
- **Patient devient hors fourchette pendant une évaluation longue** : on conserve l'âge au moment de la saisie dans `AgeAuMomentDeLaSaisie` pour traçabilité (immutable une fois validé).
- **Migration des données existantes** : aucune évaluation n'a aujourd'hui de `cartographie_enfant:` dans son YAML — le parser doit tolérer son absence (`null` → nouvelle cartographie vide).
- **Renommage enum `Cartographie` → `CartographieEnfant`** : un fichier existant pourrait avoir `etape_courante: 4` qui pointait sur l'ancienne valeur. Le mapping reste cohérent (valeur 4 = nouveau `CartographieEnfant`), donc pas de migration nécessaire.
- **Tempérament sans validation requise** : pas de score, donc pas de notion "complet/incomplet". Considéré rempli si au moins un axe est ≠ 0.

---

## 11. Hors scope — à traiter dans des incréments futurs

- **V0.1** : extension au-delà de 11 ans (tomes 0-3 / 12-15 / 16+ du Dr Lassoued)
- **V0.2** : rendu visuel "chenille colorée" + étape de restitution aux parents (PDF imprimable)
- **V0.3** : étape 5 Cartographie de l'environnement (matériel source à fournir)
- **V0.4** : intégration LLM optionnelle pour générer une **synthèse narrative croisant cartographie + synthèse diagnostique** (texte cohérent prêt pour le dossier)
