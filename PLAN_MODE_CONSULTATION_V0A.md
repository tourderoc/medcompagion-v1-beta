# Plan technique — Mode Consultation Interrogatoire (V0a)

> Plan de référence pour l'implémentation du Mode Consultation dans MedCompanion V2.
> Périmètre : V0a uniquement (extraction depuis transcription texte, sans micro ni live).
> Date : 2026-05-07
> Statut : design validé, prêt pour implémentation

---

## 1. Contexte & objectifs

### 1.1 Vision

Le Mode Consultation transforme MedCompanion en **assistant de prise de notes pendant la consultation**.
L'IA remplace la frappe du médecin pendant la partie interrogatoire d'une 1ère consultation pédopsychiatrique : elle écoute (V0c+), comprend, et écrit la note dans le **style télégraphique du Dr Lassoued**, organisée en 8 blocs cliniques.

### 1.2 Philosophie

> "L'outil remplace simplement ce que je tape, tout reste en local."

- Aucune nouvelle collecte de données vs pratique actuelle
- Audio jamais conservé (V0c+, en mémoire seulement le temps de la transcription)
- Transcription brute = artefact transitoire, supprimé à la validation finale
- Seule la note finale validée est persistée (comme aujourd'hui dans le dossier patient)
- Pas de modal consentement parental requis (cohérent avec la philosophie)

### 1.3 Périmètre V0a (cette étape)

**Inclus :**
- Sélecteur type de consultation (V0 = "1ère consultation" seule active)
- Saisie/import d'une transcription textuelle
- Extraction LLM en un seul cycle sur le texte complet
- 8 blocs cliniques affichés dans le panneau Med avec barres de progression
- Note finale éditable
- Sauvegarde dans le dossier patient

**Exclu (V0b+) :**
- Capture micro
- Whisper (transcription locale)
- VU-mètre, sélection device
- Boutons Commencer/Pause/Terminer
- Auto-save temp 60s
- Cycle d'extraction incrémental
- Diarisation (WhisperX)
- Bloc Observation
- Modules complémentaires contextuels

### 1.4 Roadmap globale

| Étape | Contenu | Effort estimé |
|---|---|---|
| **V0a** | Extraction depuis texte | ~6.5j (cette étape) |
| V0b | Whisper offline sur fichier audio | ~5j |
| V0c | Live (capture micro + extraction incrémentale) | ~10j |
| V0d | Diarisation WhisperX + polish | ~10j |
| V1 | Bloc Observation, modules contextuels | TBD |

---

## 2. Décisions actées

| # | Décision | Justification |
|---|---|---|
| 1 | 8 blocs Interrogatoire (pas 9) | Couvre les 3 notes réelles fournies, fusion Écrans+Sommeil dans "Maison" |
| 2 | 1 barre de progression par bloc | Choix user assumé malgré biais de conformation |
| 3 | Texte libre par bloc (pas d'items structurés) | Reproduit le style télégraphique réel du user |
| 4 | Convention "dit que" = fait/ressenti | Pas attribution locuteur ; faits = sans marqueur, ressentis = "dit que" |
| 5 | LLM = sélecteur existant (choix libre, local recommandé) | User unique, peut décider local vs cloud |
| 6 | Audio jamais stocké | Philosophie "remplace ma frappe" |
| 7 | Pas de modal consentement parental | Aucune nouvelle collecte vs pratique actuelle |
| 8 | Pas de bouton Réextraire en V0 | Édition manuelle de la note finale suffit |
| 9 | Pas d'onglets Interrogatoire/Observation en V0 | Observation = V1, UI directe en V0 |
| 10 | Style note imité par few-shot (3 exemples du user) | Imitation > description abstraite |
| 11 | Auto-save temp toutes les 60s (V0b+) | Pas en V0a (mode batch) |
| 12 | 3 boutons Commencer/Pause/Terminer (V0b+) | Pas en V0a |

---

## 3. Architecture C#

### 3.1 Arborescence

```
MedCompanion/
├── Models/Consultation/
│   ├── ConsultationType.cs              (enum)
│   ├── ConsultationSession.cs           (root state)
│   ├── ConsultationBlock.cs             (1 bloc)
│   ├── BlockDefinition.cs               (def issue du JSON)
│   └── ExtractionResult.cs              (sortie LLM parsée)
│
├── Services/Consultation/
│   ├── IConsultationSessionService.cs
│   ├── ConsultationSessionService.cs    (cycle de vie session)
│   ├── IInterrogatoireExtractorService.cs
│   ├── InterrogatoireExtractorService.cs (extraction LLM)
│   ├── BlockProgressCalculator.cs       (calcul %)
│   └── BlockDefinitionLoader.cs         (charge JSON config)
│
├── ViewModels/Consultation/
│   ├── ConsultationModeViewModel.cs     (état UI global)
│   └── ConsultationBlockViewModel.cs    (1 bloc + INotifyPropertyChanged)
│
├── Views/Consultation/
│   ├── ConsultationModeControl.xaml(.cs)      (bloc gauche)
│   ├── ConsultationBlockListControl.xaml(.cs) (panneau Med)
│   └── BlockCardControl.xaml(.cs)             (1 bloc affichable)
│
└── Resources/Consultation/
    ├── interrogatoire_blocks.json       (def des 8 blocs)
    └── prompt_system.txt                (prompt LLM avec few-shot)
```

### 3.2 Services existants réutilisés

- `ILLMService` — pour appeler le LLM sélectionné
- `LLMServiceFactory` — instanciation provider local/cloud
- `PathService` — chemins dossier patient
- `PatientIndexService` — patient courant

### 3.3 Convention de code (alignée sur l'existant)

- Tuple return : `(bool success, T result, string? error)`
- INotifyPropertyChanged dans ViewModels
- UserControls avec event `StatusChanged`
- UTF-8 partout (français)
- Patterns existants : voir `DocumentsControl`, `CourriersControl` comme modèles

---

## 4. Modèles de données

### 4.1 `ConsultationType`

```csharp
public enum ConsultationType
{
    PremiereConsultation,
    Suivi,           // futur
    BilanInitial,    // futur
    ProjetTherapeutique // futur
}
```

### 4.2 `ConsultationSession`

```csharp
public class ConsultationSession
{
    public Guid SessionId { get; set; } = Guid.NewGuid();
    public string PatientFolderName { get; set; }    // ex: ZIADI_Elissa
    public ConsultationType Type { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime? EndedAt { get; set; }
    public string TranscriptionRaw { get; set; }     // input texte
    public List<ConsultationBlock> Blocks { get; set; } = new();
    public string? FinalNote { get; set; }           // note validée
    public string LLMModelUsed { get; set; }
}
```

### 4.3 `ConsultationBlock`

```csharp
public class ConsultationBlock
{
    public string Key { get; set; }                  // "identite", "motif", ...
    public string Title { get; set; }                // "Identité"
    public string FreeText { get; set; } = "";       // texte libre style user
    public List<string> ExpectedThemes { get; set; } = new();
    public List<string> CoveredThemes { get; set; } = new();
    public int ProgressPct => ExpectedThemes.Count == 0
        ? 0
        : (int)(100.0 * CoveredThemes.Count / ExpectedThemes.Count);
    public DateTime LastUpdated { get; set; }
}
```

### 4.4 `BlockDefinition` (chargé depuis JSON)

```csharp
public class BlockDefinition
{
    public string Key { get; set; }
    public string Title { get; set; }
    public List<string> ExpectedThemes { get; set; }
}
```

### 4.5 `ExtractionResult` & `BlockUpdate`

```csharp
public class ExtractionResult
{
    public List<BlockUpdate> Updates { get; set; } = new();
}

public class BlockUpdate
{
    public string BlockKey { get; set; }
    public string AppendText { get; set; }       // texte à AJOUTER au bloc
    public List<string> NewThemes { get; set; }  // thèmes désormais couverts
}
```

> En V0a, l'extraction étant un seul appel sur tout le texte, `appendText` remplace en pratique le contenu complet du bloc. En V0c (live), il s'agit d'ajouts incrémentaux.

---

## 5. Configuration des 8 blocs

### 5.1 Fichier `Resources/Consultation/interrogatoire_blocks.json`

```json
[
  {
    "key": "identite",
    "title": "Identité",
    "expectedThemes": ["age", "accompagnant", "classe", "ecole"]
  },
  {
    "key": "motif",
    "title": "Motif & histoire",
    "expectedThemes": ["motif_principal", "anciennete", "retentissement"]
  },
  {
    "key": "famille",
    "title": "Famille",
    "expectedThemes": ["statut_parental", "pere", "mere"]
  },
  {
    "key": "fratrie",
    "title": "Fratrie",
    "expectedThemes": ["presence", "noms_ages"]
  },
  {
    "key": "atcds",
    "title": "ATCDs médicaux",
    "expectedThemes": ["medical", "allergies"]
  },
  {
    "key": "scolarite",
    "title": "Scolarité",
    "expectedThemes": ["rapport_ecole", "niveau_ou_copains"]
  },
  {
    "key": "activites",
    "title": "Activités",
    "expectedThemes": ["pratique_ou_absence"]
  },
  {
    "key": "maison",
    "title": "Maison (écrans/sommeil)",
    "expectedThemes": ["ecrans", "sommeil"]
  }
]
```

### 5.2 Calcul de progression

Pour chaque bloc :
```
progressPct = (CoveredThemes.Count / ExpectedThemes.Count) × 100
```

Couleur barre :
- 0% → gris
- 1-50% → orange
- 51-99% → jaune
- 100% → vert

---

## 6. Prompt système final

### 6.1 Fichier `Resources/Consultation/prompt_system.txt`

```
Tu es un assistant clinique spécialisé en pédopsychiatrie.
Ton rôle : extraire les informations d'une transcription de consultation
et produire une note d'interrogatoire dans le style du Dr Lassoued.

# Règles strictes

1. N'INVENTE JAMAIS d'information non explicitement présente dans la transcription.
2. Si une info manque pour un bloc, n'écris RIEN dans ce bloc (pas de "non précisé", pas de "?").
3. Style télégraphique, fragments courts, format clé:valeur quand pertinent.
4. Pas de phrases complètes. Abréviations OK (atcds, ras, +, ++, +++).
5. Convention "dit que" / "elle dit que" :
   - UNIQUEMENT pour ressentis subjectifs ou jugements (ex: "dit qu'il s'angoisse vite")
   - Faits objectifs : SANS marqueur (ex: "age: 9 ans", "Medikinet 40mg/j depuis mars")
   - Diagnostic posé par autre praticien : SANS marqueur ("suivi par psychologue Mme X")
6. Reformulation du médecin : ne JAMAIS la reprendre comme déclaration primaire.
7. Tu corriges les fautes de frappe et orthographe (le médecin tape vite, toi non).
8. Pour les traitements :
   - Liés au motif (psy, neuro) → bloc Motif
   - Somatiques sans lien (lunettes, asthme) → bloc ATCDs

# Structure attendue (8 blocs)

[Identité]            age, accompagnant, classe, école/collège (nom + ville)
[Motif & histoire]    motif principal, ancienneté, retentissement, suivis en cours,
                      traitements liés au motif, événements de vie marquants
[Famille]             statut parental, modalités de garde, père, mère,
                      conjoints recomposés (prénom/âge/profession)
[Fratrie]             frères/sœurs, prénoms, âges (rattachement parent si recomposé)
[ATCDs médicaux]      médical + allergies
[Scolarité]           rapport à l'école, niveau (moyenne), copains/copines,
                      harcèlement, relation avec les profs
[Activités]           extra-scolaires : sport, loisirs, ou "ne fait pas"
[Maison]              écrans + sommeil + comportement domestique

# Exemples de notes du Dr Lassoued (à imiter pour le style)

## Exemple 1 — adolescente, scarifications

[Identité]
age: 14 ans accompagnée par sa mère
classe: 4ème, collège A.

[Motif & histoire]
depuis qq mois scarifications
suivi psychologique en cours
rejet dans le collège et moqueries de la part des camarades
baisse des résultats scolaires
difficulté relationnelle avec le père
troubles du comportement alimentaire

[Famille]
parents séparés, garde partagée
père: M., 42 ans, chauffeur poids lourd, belle-mère: A., pas d'autres enfants
mère: C., 38 ans, infirmière, beau-père: T.

[Fratrie]
côté mère: 2 sœurs de 8 ans, L. et E.

[ATCDs médicaux]
ras, pas d'allergie

[Scolarité]
ça passe bien avec les profs
a des copines au collège

[Activités]
ne fait plus

[Maison]
téléphone, regarde des vidéos, joue aux jeux
sommeil: ça va

## Exemple 2 — adolescent, sous Medikinet

[Identité]
age: 14 ans accompagné par sa mère
classe: 4ème, collège (à Draguignan, exclu du précédent)

[Motif & histoire]
difficultés en 5ème, exclusion du collège
elle dit que son enfant est étiqueté
sous traitement: Medikinet 40mg/j depuis mars (avait palpitations, perte d'appétit)
à arrêté

[Famille]
parents ensemble
père: K., 49 ans, malade (glaucome des yeux)
mère: F., 43 ans, agent d'entretien

[Fratrie]
1 sœur: S., 9 ans
A., 3 ans

[ATCDs médicaux]
ras
allergie: pollen et poussière

[Scolarité]
7 de moyenne

[Activités]
foot, mécanique

[Maison]
écran +++
sommeil: ras

## Exemple 3 — enfant, crises d'angoisse

[Identité]
age: 6 ans accompagné par sa mère
classe: CP, école Saint-J. (Brignoles)

[Motif & histoire]
dit qu'il a fait une crise d'angoisse en prématuré avec AVC cérébral
difficulté en motricité
école privée
crises violentes d'angoisse depuis la rentrée de septembre
a frappé les maîtresses
en cas de crise il a sauté le portail de l'école
suivi par psychologue, peu fréquent
dit qu'il s'angoisse très vite

[Famille]
parents ensemble
père: T., 40 ans, réparateur
mère: L., 39 ans, coiffeuse

[Fratrie]
N., 11 ans

[ATCDs médicaux]
lunettes de vue
pas d'allergie

[Scolarité]
aime aller à l'école
a des copains

[Activités]
ne fait pas (séparation difficile)

[Maison]
écran: contrôlé par les parents
sommeil: dort dans sa chambre, ok

# Format de sortie

Tu produis UNIQUEMENT un objet JSON strict (pas de prose, pas de markdown).

```json
{
  "updates": [
    {
      "blockKey": "identite",
      "appendText": "age: 9 ans accompagné par sa mère\nclasse: CM1, école Jean-Moulin",
      "newThemes": ["age", "accompagnant", "classe", "ecole"]
    },
    {
      "blockKey": "motif",
      "appendText": "...",
      "newThemes": ["motif_principal"]
    }
  ]
}
```

Les `blockKey` valides sont :
identite, motif, famille, fratrie, atcds, scolarite, activites, maison

Les `newThemes` doivent correspondre aux thèmes définis pour le bloc (voir structure).

# Transcription à analyser

{TRANSCRIPTION}
```

### 6.2 Note sur les exemples few-shot

Les 3 exemples sont les vraies notes du Dr Lassoued, **anonymisées par initiales** pour les prénoms et lieux. Les fautes de frappe ont été corrigées (l'IA doit produire propre). La structure et le style télégraphique sont préservés.

---

## 7. UI V0a

### 7.1 Bloc gauche — Mode saisie (avant extraction)

```
┌─ [▾ 1ère consultation]                    ← Sélecteur type
│
│  Note de consultation
│  Thursday 7 May 2026
│
│  ── Transcription source ─────────────────
│  ┌──────────────────────────────────────┐
│  │ (textarea : colle ou tape la         │
│  │  transcription complète ici)         │
│  │                                      │
│  │                                      │
│  │                                      │
│  └──────────────────────────────────────┘
│
│  [📂 Importer .txt]   [⚙ Extraire avec IA]
│
└─
```

### 7.2 Panneau Med (centre) — pendant et après extraction

```
┌─ Med ─────────────────────────────────────┐
│                                           │
│ ▸ 1. Identité           ████████░░  100%  │
│ ▸ 2. Motif & histoire   ██████████  100%  │
│ ▾ 3. Famille            ██████░░░░   66%  │
│   parents séparés                         │
│   père: hervé, 57 ans, signalisation      │
│   mère: delphine, 43 ans, ménage          │
│ ▸ 4. Fratrie            ██████████  100%  │
│ ▸ 5. ATCDs médicaux     ██████████  100%  │
│ ▸ 6. Scolarité          ████████░░   50%  │
│ ▸ 7. Activités          ██████████  100%  │
│ ▸ 8. Maison             ██████████  100%  │
│                                           │
│ [✓ Voir note finale]                      │
└───────────────────────────────────────────┘
```

Comportement :
- Replié par défaut, click sur ▸ pour déplier
- Couleur barre selon %
- Tout cliquable pour édition inline du texte du bloc

### 7.3 Bloc gauche — Mode note finale (après extraction)

```
┌─ Note finale — 1ère consult — 7 May 2026
│
│  ┌──────────────────────────────────────┐
│  │ [Identité]                           │
│  │ age: 14 ans accompagnée par sa mère  │
│  │ classe: 4ème, collège A.             │
│  │                                      │
│  │ [Motif & histoire]                   │
│  │ depuis qq mois scarifications        │
│  │ suivi psychologique en cours         │
│  │ ...                                  │
│  │                                      │
│  │ [Famille]                            │
│  │ parents séparés, garde partagée      │
│  │ ...                                  │
│  │                                      │
│  │ (8 blocs concaténés, éditable libre) │
│  └──────────────────────────────────────┘
│
│  [↩ Retour]    [💾 Sauvegarder dossier]
│
└─
```

Comportement :
- Textarea unique modifiable
- Pré-rempli par concaténation des 8 blocs
- Bouton "Retour" → revient au mode saisie sans sauvegarder
- Bouton "Sauvegarder" → écrit dans dossier patient et clôt la session

### 7.4 Bloc droit — Dossier patient

Inchangé. Reste en place, fournit le contexte patient.

---

## 8. Pipeline de fonctionnement V0a

```
[1] User clique "1ère consultation" dans le sélecteur
        ↓
[2] ConsultationModeViewModel initialise une session vide
        ↓
[3] User colle/importe la transcription dans la textarea
        ↓
[4] User clique "Extraire avec IA"
        ↓
[5] ConsultationModeViewModel appelle InterrogatoireExtractorService.ExtractAsync(transcription)
        ↓
[6] InterrogatoireExtractorService construit le prompt :
    - système (template + 8 blocs + 3 examples)
    - user (transcription)
        ↓
[7] InterrogatoireExtractorService appelle ILLMService.GenerateAsync(prompt)
        ↓
[8] Parse JSON output → ExtractionResult
        ↓
[9] Pour chaque BlockUpdate : merge dans ConsultationBlock correspondant
        ↓
[10] BlockProgressCalculator recalcule progressPct
        ↓
[11] PropertyChanged → UI panneau Med se remplit
        ↓
[12] User clique "Voir note finale"
        ↓
[13] ConsultationModeViewModel concatène les blocs → string note
        ↓
[14] Vue bascule en mode note finale (textarea editable)
        ↓
[15] User édite si besoin
        ↓
[16] User clique "Sauvegarder dossier"
        ↓
[17] ConsultationSessionService écrit le .md dans le dossier patient
        ↓
[18] Session clôturée, transcription brute supprimée de la mémoire
```

---

## 9. Sauvegarde

### 9.1 Note finale

**Chemin :**
```
{PathService.PatientsRoot}/{PatientFolderName}/{annee}/notes/{YYYY-MM-DD}_1ere_consult_interrogatoire.md
```

Exemple :
```
Documents/MedCompanion/patients/ZIADI_Elissa/2026/notes/2026-05-07_1ere_consult_interrogatoire.md
```

**Format Markdown :**
```markdown
---
type: 1ere_consultation
sous_type: interrogatoire
date: 2026-05-07
duree_min: 23
llm_used: gpt-oss:120b-cloud
session_id: a1b2c3d4-...
---

# Interrogatoire — 1ère consultation
## 7 mai 2026

[Identité]
...

[Motif & histoire]
...

[Famille]
...

(reste de la note)
```

### 9.2 Pas de stockage de transcription brute en V0a

À la sauvegarde de la note finale, la `TranscriptionRaw` est effacée de la session en mémoire. Seul le `.md` final reste.

---

## 10. Plan de tests V0a

### 10.1 Critère de succès

Sur 3 transcriptions textuelles réelles (à fournir par le user, anonymisées si besoin) :
- **Tous les blocs touchés dans la transcription sont correctement alimentés**
- **Aucune hallucination** (info dans le bloc qui n'est pas dans la transcription)
- **Style respecté** : télégraphique, "dit que" pour ressentis, factuel sinon
- **Moins de 20% de corrections manuelles** nécessaires sur la note finale

### 10.2 Cas de test

| Test | Source | Critère |
|---|---|---|
| T1 | Transcription consult 15-30 min, motif simple | Tous blocs alimentés, 0 hallucination |
| T2 | Transcription consult 45 min, motif complexe (ex: TSA suspecté) | Idem + cohérence sur durée |
| T3 | Transcription avec contradiction (parent vs enfant sur 1 fait) | Contradiction marquée inline correctement |

### 10.3 Critère d'échec / fallback

Si T1-T3 échouent :
- Itération sur le prompt (ajout d'exemples few-shot, durcissement règles)
- Test d'un autre modèle LLM
- Si aucune solution → revoir l'approche avant V0b

### 10.4 Critère de validation pour passer en V0b

- 3/3 tests passent avec corrections manuelles minimales
- User confirme que la note IA est cliniquement utilisable
- Décision explicite "Go V0b"

---

## 11. Estimation effort V0a

| Tâche | Estimation |
|---|---|
| Modèles + JSON config blocs | 0.5j |
| `InterrogatoireExtractorService` (build prompt, appel LLM, parse JSON) | 1j |
| `ConsultationSessionService` (cycle vie, save final dans dossier patient) | 0.5j |
| `BlockProgressCalculator` + `BlockDefinitionLoader` | 0.5j |
| ViewModels + INotifyPropertyChanged | 0.5j |
| `ConsultationModeControl` (UI gauche, mode saisie + note finale) | 1j |
| `ConsultationBlockListControl` + `BlockCardControl` (panneau Med) | 1j |
| Intégration MainWindow (sélecteur type, refonte panneau Med) | 0.5j |
| Tests sur 3 transcriptions + tuning prompt | 1j |
| **Total V0a** | **~6.5 jours dev** |

Plan réaliste sur 1.5–2 semaines en mode itératif (commits fréquents, validation à chaque étape).

---

## 12. Risques techniques identifiés

| Risque | Mitigation |
|---|---|
| LLM hallucine des infos | Prompt strict + tests sur 3 transcriptions |
| LLM mal classe (Medikinet en ATCDs au lieu de Motif) | Règle explicite dans prompt + few-shot |
| Output JSON malformé | Parsing robuste avec retry (1 tentative de re-prompt si parse fail) |
| Latence LLM (gpt-oss:120b cloud peut prendre 30s+ sur transcription longue) | Loader UI clair, timeout 90s |
| User édite la note finale et perd le travail (clic "Retour") | Confirmation avant retour si modifications |
| Transcription mal formattée (caractères spéciaux, encodage) | Normalisation UTF-8 systématique |
| Patient non sélectionné au moment de "Extraire" | Bouton désactivé tant que pas de patient |

---

## 13. Dépendances

### 13.1 Bibliothèques

Aucune nouvelle dépendance NuGet pour V0a (réutilise `ILLMService` existant).

### 13.2 Fichiers à modifier

- `MainWindow.xaml` : ajouter `ConsultationModeControl` dans bloc gauche (en remplacement ou complément de la zone "Note de consultation" actuelle, à voir)
- `MainWindow.xaml.cs` : initialiser le contrôle, brancher events
- Possiblement créer `MainWindow.Consultation.cs` (partial class) pour isoler la logique mode consult

### 13.3 Fichiers à créer

Voir arborescence section 3.1.

---

## 14. Étapes d'implémentation (ordre suggéré)

1. **Modèles + JSON config** : poser les classes et le JSON, vérifier chargement
2. **`InterrogatoireExtractorService`** : extraire avec un prompt simplifié, valider sur 1 transcription
3. **Tests prompt** : itérer sur le prompt et les few-shot examples jusqu'à qualité satisfaisante
4. **ViewModels** : INotifyPropertyChanged + binding
5. **UI panneau Med** : `ConsultationBlockListControl` + `BlockCardControl`
6. **UI bloc gauche** : `ConsultationModeControl` mode saisie
7. **Bascule note finale** : mode édition + sauvegarde
8. **Intégration MainWindow** : sélecteur type + refonte
9. **Tests T1/T2/T3** : validation finale

À chaque étape : commit, vérification visuelle, validation manuelle.

---

## 15. Décisions actées (récapitulatif)

| # | Décision | Statut |
|---|---|---|
| 1 | Localisation arborescence proposée | ✅ |
| 2 | JSON externalisé pour blocs | ✅ |
| 3 | Note finale .md dans `{patient}/{annee}/notes/` | ✅ |
| 4 | Pas d'auto-save temp en V0a (extraction batch) | ✅ |
| 5 | Cycle d'extraction en un seul appel LLM | ✅ |
| 6 | 3 exemples few-shot anonymisés par initiales | ✅ |
| 7 | LLM = sélecteur existant, choix libre user | ✅ |
| 8 | Pas de bouton Réextraire en V0 | ✅ |
| 9 | Pas de modal consentement parental | ✅ |
| 10 | Audio jamais conservé (V0c+ in-memory only) | ✅ |

---

## 16. Prochaines étapes

1. **Validation finale du plan** par le user
2. **Démarrage implémentation** étape par étape (section 14)
3. **Commits fréquents** avec messages clairs
4. **Tests T1/T2/T3** à fournir au moment de la phase de tuning prompt
5. **Décision Go/No-Go V0b** après validation V0a

---

## Annexes

### Annexe A — Notes de référence du Dr Lassoued (sources des few-shot)

> Notes anonymisées utilisées comme few-shot examples dans le prompt.
> Voir section 6.1 pour la version intégrée au prompt système.

### Annexe B — Liens vers documentation existante

- [CLAUDE.md](CLAUDE.md) — Vue projet
- [VISION_V2.md](VISION_V2.md) — Vision MedCompanion V2
- [VISION_V3.md](VISION_V3.md) — Vision écosystème

---

*Document de plan technique. À mettre à jour à chaque itération significative.*
