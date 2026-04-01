# Plan d'Amélioration du Matching MCC

> **Date** : Janvier 2026
> **Objectif** : Améliorer la pertinence et la flexibilité du système de matching MCC
> **Fichiers impactés** : `MCCMatchingService.cs`, `MCCLibraryService.cs`, `MCCScoringService.cs` (nouveau)

---

## ✅ STATUT : TOUTES LES ÉTAPES IMPLÉMENTÉES (9 janvier 2026)

| # | Étape | Statut | Fichiers modifiés |
|---|-------|--------|-------------------|
| 1 | Simplifier filtre DocType | ✅ Terminé | MCCLibraryService.cs |
| 2 | Enrichir alias Audience | ✅ Terminé | MCCMatchingService.cs, MCCLibraryService.cs |
| 3 | Matching partiel (patterns) | ✅ Terminé | MCCLibraryService.cs |
| 4 | Consolider code scoring | ✅ Terminé | MCCScoringService.cs (nouveau) |
| 5 | Améliorer matching mots-clés | ✅ Terminé | MCCMatchingService.cs, MCCLibraryService.cs |
| 6 | Pondérer rating (Bayesian) | ✅ Terminé | MCCMatchingService.cs, MCCLibraryService.cs |
| 7 | Revoir score popularité | ✅ Terminé | MCCMatchingService.cs, MCCLibraryService.cs |

### Améliorations apportées :
- **Filtre DocType** : Valeur par défaut "courrier" - plus besoin de le mentionner
- **Alias Audience** : 50+ termes ajoutés (équipe éducative, pédopsychiatre, MDPH, etc.)
- **Patterns partiels** : "éducatif" → school, "psychiatre" → doctor
- **Rating Bayesian** : MCCs peu notés ne dominent plus
- **Popularité Sqrt** : Courbe plus progressive
- **Mots-clés** : Évite faux positifs (attention ≠ inattention)
- **Service centralisé** : MCCScoringService.cs créé

---

## 1. État Actuel du Système

### 1.1 Flux de matching

```
Requête utilisateur
        ↓
PromptReformulationService.AnalyzeLetterRequestAsync()
        ↓
Extraction: DocType, Audience, Tone, AgeGroup, Keywords
        ↓
┌─────────────────────────────────────────┐
│  FILTRE 1 : Type de Document (DocType)  │  ← Problématique
│  FILTRE 2 : Statut (Active/Validated)   │  ← OK
│  FILTRE 3 : Audience Compatible         │  ← Trop restrictif
└─────────────────────────────────────────┘
        ↓
Candidats filtrés → Scoring (210 pts max)
        ↓
Seuil minimum : 105 pts (50%)
```

### 1.2 Scoring actuel (210 pts)

| Critère | Points | Problème identifié |
|---------|--------|-------------------|
| Type de document | 50 | Toujours 50 pts car déjà filtré = inutile |
| Mots-clés | 40 | Matching substring trop permissif |
| Audience | 30 | Alias incomplets |
| Tranche d'âge | 20 | OK |
| Ton | 15 | Alias incomplets |
| Qualité (notes) | 30 | Non pondéré par volume de votes |
| Popularité (usage) | 15 | Sature trop vite (log) |
| Statut validé | 10 | OK |

### 1.3 Problèmes identifiés

1. **Filtre DocType inutile** - Usage 100% courriers, friction UX
2. **Alias Audience incomplets** - "équipe éducative" ≠ "école"
3. **Duplication du code scoring** - 2 implémentations différentes
4. **Matching mots-clés trop large** - Faux positifs avec substring
5. **Rating non pondéré** - 1 vote 5★ = 100 votes 4.8★

---

## 2. Plan d'Amélioration par Étapes

### Étape 1 : Simplifier le filtre DocType

**Objectif** : Supprimer la friction "courrier" obligatoire

**Modification** :
- Fichier : `MCCLibraryService.cs` (ligne 351)
- Changement : Valeur par défaut "courrier" si DocType vide ou non reconnu

**Code avant** :
```csharp
.Where(m => m.Semantic?.DocType == docType && ...)
```

**Code après** :
```csharp
// Si docType est vide ou "courrier", ne pas filtrer par type
var filterByDocType = !string.IsNullOrEmpty(docType) && docType != "courrier";
.Where(m => (!filterByDocType || m.Semantic?.DocType == docType) && ...)
```

**Risque de régression** : 🟢 **FAIBLE**
- Impact : Élargit les candidats, ne casse rien
- Test : Vérifier que les courriers sont toujours trouvés
- Rollback : Simple (1 ligne)

---

### Étape 2 : Enrichir les alias Audience

**Objectif** : Reconnaître "équipe éducative", "établissement scolaire", etc.

**Modification** :
- Fichiers : `MCCMatchingService.cs` (ligne 38-47), `MCCLibraryService.cs` (ligne 419-427)
- Changement : Étendre les listes d'alias + ajouter matching partiel

**Nouveaux alias proposés** :

```csharp
private static readonly Dictionary<string, List<string>> AUDIENCE_ALIASES = new()
{
    ["school"] = new() {
        // Existants
        "school", "ecole", "scolaire", "enseignant", "professeur",
        // Nouveaux
        "equipe educative", "etablissement scolaire", "etablissement",
        "directeur", "directrice", "instituteur", "institutrice",
        "maitre", "maitresse", "college", "lycee", "maternelle", "primaire",
        "avs", "aesh", "cpe", "psychologue scolaire", "medecin scolaire",
        "rased", "coordonnateur", "referent"
    },
    ["parents"] = new() {
        "parents", "famille", "parent", "family",
        // Nouveaux
        "mere", "pere", "tuteur", "representant legal"
    },
    ["doctor"] = new() {
        "doctor", "medecin", "confrere", "specialiste", "physician",
        // Nouveaux
        "psychiatre", "psychologue", "orthophoniste", "ergotherapeute",
        "psychomotricien", "neurologue", "pediatre", "generaliste",
        "praticien", "therapeute", "neuropsychologue", "orthoptiste"
    },
    ["institution"] = new() {
        "institution", "administratif", "administration", "mdph", "cpam",
        // Nouveaux
        "caf", "tribunal", "prefecture", "mairie", "securite sociale",
        "assurance maladie", "mutuelle", "organisme", "service"
    },
    ["judge"] = new() {
        "judge", "juge", "tribunal", "justice", "legal",
        // Nouveaux
        "avocat", "magistrat", "procureur", "greffier", "expert judiciaire"
    }
};
```

**Risque de régression** : 🟢 **FAIBLE**
- Impact : Élargit les matchs, ne casse rien
- Test : Vérifier que les anciens alias fonctionnent toujours
- Rollback : Simple (remettre anciennes listes)

---

### Étape 3 : Ajouter le matching partiel (patterns)

**Objectif** : Matcher "educatif" → "school" même si pas dans la liste exacte

**Modification** :
- Fichier : `MCCLibraryService.cs`
- Changement : Nouvelle fonction de matching par patterns

**Nouveau code** :

```csharp
private static readonly Dictionary<string, List<string>> AUDIENCE_PATTERNS = new()
{
    ["school"] = new() { "educa", "scola", "enseign", "ecole", "college", "lycee", "professeur" },
    ["doctor"] = new() { "medecin", "docteur", "psychiatr", "psycholog", "orthophon", "neurolog" },
    ["institution"] = new() { "mdph", "cpam", "caf", "admin", "tribunal", "prefecture" },
    ["parents"] = new() { "parent", "famille", "tuteur", "mere", "pere" },
    ["judge"] = new() { "juge", "justice", "avocat", "tribunal", "legal" }
};

private bool AudienceMatchesPattern(string targetAudience, string mccAudience)
{
    var target = targetAudience.ToLower().Normalize();

    // Trouver la catégorie du MCC
    string? mccCategory = null;
    foreach (var (category, aliases) in AUDIENCE_ALIASES)
    {
        if (aliases.Any(a => a == mccAudience.ToLower()))
        {
            mccCategory = category;
            break;
        }
    }

    if (mccCategory == null) return false;

    // Vérifier si le target contient un pattern de cette catégorie
    if (AUDIENCE_PATTERNS.TryGetValue(mccCategory, out var patterns))
    {
        return patterns.Any(p => target.Contains(p));
    }

    return false;
}
```

**Risque de régression** : 🟡 **MOYEN**
- Impact : Peut créer des faux positifs si patterns trop courts
- Test : Tester avec "education nationale" (devrait → school), "médecine du travail" (devrait → doctor)
- Rollback : Désactiver la fonction pattern, revenir aux alias seuls

---

### Étape 4 : Consolider le code de scoring

**Objectif** : Une seule source de vérité pour le calcul de score

**Modification** :
- Supprimer : `MCCLibraryService.CalculateMatchScoreWithKeywords()` (lignes 469-540)
- Garder : `MCCMatchingService.CalculateDetailedScore()`
- Créer : Méthode partagée ou service dédié `MCCScoringService`

**Architecture proposée** :

```
MCCScoringService (nouveau)
├── CalculateScore(MCCModel, LetterAnalysisResult) → ScoreResult
├── AUDIENCE_ALIASES (centralisé)
├── TONE_ALIASES (centralisé)
└── AUDIENCE_PATTERNS (centralisé)

MCCMatchingService
└── Utilise MCCScoringService

MCCLibraryService
└── Utilise MCCScoringService
```

**Risque de régression** : 🟡 **MOYEN**
- Impact : Refactoring structurel, peut introduire des bugs
- Test : Comparer scores avant/après sur 10 requêtes types
- Rollback : Plus complexe, nécessite backup des fichiers

---

### Étape 5 : Améliorer le matching de mots-clés

**Objectif** : Éviter les faux positifs (attention ≠ inattention)

**Modification** :
- Fichier : `MCCMatchingService.cs` (lignes 408-438)
- Changement : Matching par mots entiers + synonymes médicaux

**Code avant** :
```csharp
mccKeywords.Any(mk => mk.Contains(k) || k.Contains(mk))
```

**Code après** :
```csharp
private bool KeywordMatches(string userKeyword, string mccKeyword)
{
    var user = userKeyword.ToLower().Trim();
    var mcc = mccKeyword.ToLower().Trim();

    // 1. Match exact
    if (user == mcc) return true;

    // 2. Match mot entier (pas substring)
    var userWords = user.Split(' ', '-', '_');
    var mccWords = mcc.Split(' ', '-', '_');
    if (userWords.Any(uw => mccWords.Any(mw => uw == mw))) return true;

    // 3. Synonymes médicaux
    if (AreMedicalSynonyms(user, mcc)) return true;

    return false;
}
```

**Risque de régression** : 🟡 **MOYEN**
- Impact : Peut réduire certains matchs légitimes
- Test : Vérifier que "TDAH" matche toujours "trouble attention"
- Rollback : Revenir à l'ancien code

---

### Étape 6 : Pondérer le rating par le volume

**Objectif** : Éviter qu'un MCC avec 1 seul vote 5★ soit favorisé

**Modification** :
- Fichier : `MCCMatchingService.cs` (ligne ~485)
- Changement : Utiliser moyenne bayésienne

**Code avant** :
```csharp
double qualityScore = (mcc.AverageRating / 5.0) * 30;
```

**Code après** :
```csharp
// Bayesian average : plus de votes = plus de confiance
const double PRIOR_RATING = 3.5;  // Note "neutre" a priori
const int PRIOR_WEIGHT = 5;       // Équivalent à 5 votes

double bayesianRating = (mcc.AverageRating * mcc.TotalRatings + PRIOR_RATING * PRIOR_WEIGHT)
                        / (mcc.TotalRatings + PRIOR_WEIGHT);
double qualityScore = (bayesianRating / 5.0) * 30;
```

**Exemple** :
| MCC | Votes | Moyenne | Score avant | Score après |
|-----|-------|---------|-------------|-------------|
| A | 1 | 5.0 | 30 pts | 22.5 pts |
| B | 50 | 4.5 | 27 pts | 26.7 pts |
| C | 100 | 4.2 | 25.2 pts | 25.0 pts |

**Risque de régression** : 🟢 **FAIBLE**
- Impact : Change le classement, mais améliore la qualité
- Test : Vérifier que les MCCs populaires bien notés restent en tête
- Rollback : Simple (1 formule)

---

### Étape 7 : Revoir le score de popularité

**Objectif** : Mieux différencier les MCCs très utilisés

**Modification** :
- Fichier : `MCCMatchingService.cs` (ligne ~495)
- Changement : Courbe moins agressive

**Code avant** :
```csharp
double usageScore = Math.Min(Math.Log(mcc.UsageCount + 1) * 5, 15);
// 20 utilisations → 15 pts (max atteint)
```

**Code après** :
```csharp
// Racine carrée : progression plus douce
double usageScore = Math.Min(Math.Sqrt(mcc.UsageCount) * 1.5, 15);
// 20 utilisations → 6.7 pts
// 100 utilisations → 15 pts (max atteint)
```

**Risque de régression** : 🟢 **FAIBLE**
- Impact : MCCs peu utilisés seront moins pénalisés
- Test : Comparer classements avant/après
- Rollback : Simple (1 formule)

---

## 3. Ordre d'Implémentation Recommandé

| Ordre | Étape | Risque | Temps estimé | Dépendances |
|-------|-------|--------|--------------|-------------|
| 1 | Étape 1 : Simplifier filtre DocType | 🟢 Faible | 15 min | Aucune |
| 2 | Étape 2 : Enrichir alias Audience | 🟢 Faible | 20 min | Aucune |
| 3 | Étape 6 : Pondérer rating | 🟢 Faible | 10 min | Aucune |
| 4 | Étape 7 : Revoir score popularité | 🟢 Faible | 10 min | Aucune |
| 5 | Étape 3 : Matching partiel | 🟡 Moyen | 30 min | Après étape 2 |
| 6 | Étape 5 : Améliorer matching mots-clés | 🟡 Moyen | 30 min | Aucune |
| 7 | Étape 4 : Consolider scoring | 🟡 Moyen | 1h | Après toutes les autres |

---

## 4. Tableau Récapitulatif des Risques

| Étape | Fichiers modifiés | Risque | Impact si bug | Rollback |
|-------|-------------------|--------|---------------|----------|
| 1 | MCCLibraryService.cs | 🟢 | Aucun MCC trouvé | 1 ligne |
| 2 | MCCMatchingService.cs, MCCLibraryService.cs | 🟢 | Matchs incorrects | Listes |
| 3 | MCCLibraryService.cs | 🟡 | Faux positifs | Fonction |
| 4 | 3 fichiers + nouveau service | 🟡 | Scores incorrects | Backup |
| 5 | MCCMatchingService.cs | 🟡 | Moins de matchs | Fonction |
| 6 | MCCMatchingService.cs | 🟢 | Classement différent | 1 formule |
| 7 | MCCMatchingService.cs | 🟢 | Classement différent | 1 formule |

---

## 5. Tests de Non-Régression

### 5.1 Cas de test à valider après chaque étape

| # | Requête | Résultat attendu |
|---|---------|------------------|
| 1 | "courrier pour l'école" | Match MCC audience=school |
| 2 | "lettre pour équipe éducative" | Match MCC audience=school |
| 3 | "courrier pour le psychiatre" | Match MCC audience=doctor |
| 4 | "attestation MDPH" | Match MCC audience=institution |
| 5 | "courrier parents TDAH" | Match MCC keywords contient TDAH |
| 6 | "aménagements scolaires TSA" | Match MCC keywords contient TSA + audience=school |

### 5.2 Validation du scoring

Avant chaque étape, sauvegarder le résultat de :
```csharp
var result = await _matchingService.AnalyzeAndMatchAsync("courrier école TDAH", patient);
// Sauvegarder : TopMatches, Scores, ScoreBreakdown
```

Après l'étape, comparer et s'assurer que :
- Les 3 premiers MCCs sont toujours pertinents
- Aucun MCC manifestement incorrect n'apparaît

---

## 6. Checklist par Étape

### Avant de commencer une étape
- [ ] Lire le code actuel
- [ ] Comprendre l'impact
- [ ] Préparer le rollback

### Après chaque étape
- [ ] Build sans erreur
- [ ] Tests manuels (6 cas ci-dessus)
- [ ] Comparer scores avant/après
- [ ] Commit avec message descriptif

---

## 7. Prochaines Étapes (V2 - Futur)

Ces améliorations sont plus complexes et peuvent être envisagées plus tard :

1. **Matching sémantique avec embeddings** - Utiliser OpenAI embeddings pour comparer le sens
2. **Indexation de la bibliothèque** - Pré-indexer par catégorie pour performances
3. **Seuil adaptatif** - Ajuster le seuil 105 pts selon la confiance LLM
4. **Apprentissage des préférences** - Apprendre des choix utilisateur pour améliorer le matching

---

*Document créé le 9 janvier 2026*
