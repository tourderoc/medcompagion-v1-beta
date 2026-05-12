# Plan — Blocs Adaptatifs Mode Consultation V0b (Architecture Simplifiée)

*Rédigé le 2026-05-12 — Refonte clinique & architecturale*

---

## Contexte & Insight Clinique

Le Mode Consultation V0a (en production) comporte 8 blocs fixes. L'objectif V0b est d'**adapter la structure sans la rendre instable** en s'appuyant sur deux déclencheurs réels :

1. **Âge du patient** (confirmé dès le 1er batch Whisper, ~90s)
2. **Motif principal** (détecté après le 1er batch, ~90s)

**Insight clinique clé :** les dossiers patients contiennent souvent des âges erronés (erreurs d'enregistrement, fusions de dossiers, dates de naissance incorrectes). L'âge doit être **confirmé en première question**, ce qui coïncide avec l'arrivée du 1er batch Whisper.

**Principe architectural :** structure stable après ~90s. Jamais de suppression de bloc. Chips de suggestions motif disponibles en zone dédiée.

---

## Nouvelle Hiérarchie

### 1. Méta-blocs (Déclencheurs, pas de contenu clinique)

| Bloc | Rôle |
|------|------|
| **Âge** | Détermine le profil consultation (< 3 ans vs ≥ 3 ans) |
| **Motif principal** | Déclenche les blocs supplémentaires (~90s) |

Ces deux blocs ne sont pas des champs de saisie classiques. Ce sont des **signaux d'activation** qui orientent le reste de la structure.

### 2. Noyau fixe (8 blocs, toujours présents)

| Bloc | Contenu |
|------|---------|
| `identite` | Identité (séparé de l'Âge) |
| `histoire_maladie` | Historique & plaintes (séparé du Motif) |
| `famille` | Famille & contexte social |
| `fratrie` | Fratrie & fratries |
| `atcds` | ATCDs médicaux & psychiatriques |
| `sommeil_ecrans` | Sommeil, rythmes, exposition écrans |
| + **macro-bloc contextuel par âge** (voir section 3) |

### 3. Macro-blocs Contextuels (Automatiques selon l'âge)

#### Si enfant < 3 ans

Ajout automatique, silencieux du macro-bloc :

**`petite_enfance`** — Périnatalité & développement précoce
- Grossesse & accouchement
- Périnatalité & néonatalité
- Développement psychomoteur (marche, langage, acquisitions)
- Interactions sociales précoces & attachement
- Mode de garde
- Alimentation & diversification

**Justification :** avant 3 ans, la consultation est une narration développementale continue, pas des domaines séparés.

---

#### Si enfant ≥ 3 ans

Ajout automatique, silencieux du macro-bloc :

**`scolarite_activites`** — Scolarité & vie sociale
- Environnement scolaire & adaptations
- Difficultés scolaires & comportement en classe
- Relations sociales & amitiés
- Activités extrascolaires & loisirs
- Autonomie scolaire

---

### 4. Blocs Supplémentaires (Chips, déclenchés par motif)

Après le 1er batch (~90s), si le motif détecté suggère une pertinence clinique :

| Motif Détecté | Bloc Suggéré | Contenu |
|---------------|-------------|---------|
| TDAH, agitation, opposition | `comportement` | Régulation, impulsivité, frustration |
| Anxiété, phobie, stress | `vecu_emotionnel` | Anxiété, peurs, ruminations, coping |
| Suspect. TSA, DI, retard dév. (enfant > 3 ans) | `developpement` | Périnatalité, acquisitions, interactions (rétroactif) |
| Retard de langage, bégaiement | `langage` | Compréhension, expression, articulation |
| Enfant < 10 ans | `motricite` | Coordination, équilibre, maladresse |
| Ado 10–17 ans | `puberte` | Changements physiques, image de soi |
| Ado 12–18 ans | `adolescence` | Identité, pair, autonomisation, comportements à risque |
| Trauma, abus, événements majeurs | `traumatisme` | Circonstances, symptômes, impact |
| Médicaments en cours | `traitement` | Traitements actuels & historique |

**Règle :** chip = suggestion, pas automatique. Le médecin accepte (✓) ou ignore (✕).

---

## Structure Finale Type

### Consultation < 3 ans avec motif "retard de langage"

```
1. Âge                         [2 ans] → déclenche "Petite enfance"
2. Motif principal             [retard langage]
3. Identité
4. Histoire de la maladie
5. Famille
6. Fratrie
7. ATCDs
8. Sommeil & écrans
9. ▶ Petite enfance            [automatique, > 3 ans N/A]
   
Chips disponibles (~90s) :
  + Langage (motif "retard langage")
  + Développement (âge < 3 ans + anamnèse)
```

### Consultation 8 ans avec motif "suspect. TSA"

```
1. Âge                         [8 ans] → déclenche "Scolarité"
2. Motif principal             [TSA suspecté]
3. Identité
4. Histoire de la maladie
5. Famille
6. Fratrie
7. ATCDs
8. Sommeil & écrans
9. ▶ Scolarité & activités     [automatique, > 3 ans]

Chips disponibles (~90s) :
  + Développement (motif TSA)
  + Motricité (âge < 10 ans + TSA)
  + Comportement (potentiel comorbide TDAH)
```

---

## Implémentation (~6–7h au lieu de 11h)

### Phase A — block_library.json (~45 min)

Struktur simplifié :

```json
[
  {
    "key": "petite_enfance",
    "title": "Petite enfance",
    "expectedThemes": ["grossesse", "accouchement", "marche", "langage", ...],
    "ageMin": 0,
    "ageMax": 3,
    "triggerType": "age_automatic"
  },
  {
    "key": "scolarite_activites",
    "title": "Scolarité & activités",
    "expectedThemes": ["école", "comportement classe", "amis", ...],
    "ageMin": 3,
    "ageMax": 99,
    "triggerType": "age_automatic"
  },
  {
    "key": "comportement",
    "title": "Comportement & régulation",
    "expectedThemes": ["agitation", "impulsivité", "frustration", ...],
    "triggerType": "motif_chip",
    "motifKeywords": ["TDAH", "agitation", "opposition"]
  },
  ...
]
```

**Total : 6 fixes + 2 macro + ~8 chips = 16 blocs max**

### Phase B — BlockSetResolver (~30 min)

```csharp
// Charger à l'init, basé sur patient.Age (hypothèse)
public List<string> Resolve(int ageYears)
{
    var fixed_core = new[] { "identite", "histoire_maladie", "famille", ... };
    var contextual = ageYears < 3 ? "petite_enfance" : "scolarite_activites";
    return fixed_core.Append(contextual).ToList();
}
```

### Phase C — MotifPrincipalDetector (~20 min)

Event ONE-SHOT quand motif_principal apparaît dans CoveredThemes après 1er batch.

### Phase D — ContextualBlockSuggester (~1h30)

Un seul appel LLM. Reçoit motif + blocs actifs, retourne max 4 blockKey à suggérer en chips.

### Phase E — BlockPrefiller (~45 min)

Quand un chip est accepté (✓), pré-remplir avec contexte des blocs existants.

### Phase F — ViewModel (~1h)

- `ObservableCollection<BlockSuggestionViewModel> BlockSuggestions`
- AcceptCommand / DismissCommand par chip

### Phase G — UI Chips (~45 min)

Zone dédiée, séparée de la liste des blocs actifs.

---

## Whisper — Vocabulaire Personnalisé

### Fichier `whisper_vocab_custom.txt`

Éditable depuis l'UI de consultation. Le médecin ajoute lui-même les établissements au fil des consultations.

Implémentation : ~1h
- Fichier texte dans `Documents/MedCompanion/`
- Rechargé à chaque `StartAsync()`
- Concaténé à la fin de `InitialPrompt`
- UI : petite zone "Ajouter un établissement" dans les réglages Consultation

---

## Prochaine session : Ordre d'exécution

1. Phase A (block_library.json simplifié) — 45 min
2. Phase B (BlockSetResolver) — 30 min
3. Phases C + D ensemble (détection motif + LLM) — 2h
4. Phase E (BlockPrefiller) — 45 min
5. Phase F (ViewModel) — 1h
6. Phase G (UI) — 45 min
7. Vocabulaire personnalisé Whisper — 1h

**Total estimé : ~7h**

---

## Avantages de cette architecture

| Aspect | V0b Ancien | V0b Nouveau |
|--------|-----------|-----------|
| Nb blocs max | 19 | 16 |
| Complexité BlockSetResolver | Regles complexes | Juste age < 3 vs >= 3 |
| Stabilité UI | Blocs s'ajoutent en continu | Blocs gelés après ~90s |
| Cognitive load (médecin) | Grille éclatée | Narrative cohérente |
| Implémentation | 11h | 7h |

---

## Questions validées

✓ Sommeil & écrans dans noyau fixe — universel, critique < 3 ans
✓ Macro-bloc Petite enfance — meilleur que petits blocs séparés
✓ Périnatalité pour enfant > 3 ans + suspicion NDD — chip motif
✓ Automatique pour âge, chip pour motif — règle d'or
✓ Stabilité UI — gel après ~90s, chips en zone dédiée
