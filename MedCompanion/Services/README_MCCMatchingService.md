# 📚 MCCMatchingService - Guide d'utilisation

## 🎯 Objectif

Le **MCCMatchingService** est un service centralisé qui orchestre tout le processus de matching MCC (Modèle de Communication Clinique). Il offre une visibilité complète sur chaque étape du processus avec des logs détaillés.

## 🏗️ Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    MCCMatchingService                        │
│                                                               │
│  📝 Demande utilisateur + Contexte patient                   │
│           ↓                                                   │
│  🧠 Analyse IA (PromptReformulationService)                  │
│           ↓                                                   │
│  🔍 Recherche MCC (MCCLibraryService)                        │
│           ↓                                                   │
│  🎯 Calcul du score détaillé                                 │
│           ↓                                                   │
│  ✅ MCCMatchResult (avec logs complets)                      │
└─────────────────────────────────────────────────────────────┘
```

## 📦 Modèles créés

### MCCMatchResult.cs

Résultat structuré contenant :
- **HasMatch** : Match trouvé ou non
- **SelectedMCC** : Le template MCC sélectionné
- **RawScore** : Score brut (0-210 points)
- **NormalizedScore** : Score normalisé (0-100%)
- **Analysis** : Métadonnées de l'analyse IA
- **MatchingLogs** : Logs détaillés étape par étape
- **ScoreBreakdown** : Détail du scoring par critère
- **TotalMCCsChecked** : Nombre de MCC consultés
- **FailureReason** : Raison de l'échec si pas de match

## 🚀 Utilisation

### Exemple simple

```csharp
// Instancier le service
var matchingService = new MCCMatchingService(
    _reformulationService,
    _libraryService
);

// Analyser et matcher
var (success, result, error) = await matchingService.AnalyzeAndMatchAsync(
    "Je voudrais un courrier pour l'école concernant les difficultés d'attention de mon patient",
    patientContext
);

if (success && result.HasMatch)
{
    // Match trouvé !
    Console.WriteLine($"✅ MCC trouvé : {result.SelectedMCC.Name}");
    Console.WriteLine($"📊 Score : {result.NormalizedScore:F1}%");
    
    // Afficher les logs
    matchingService.PrintMatchingLogs(result);
}
else if (success && !result.HasMatch)
{
    // Pas de match, mais pas d'erreur
    Console.WriteLine($"⚠️ Pas de match : {result.FailureReason}");
    Console.WriteLine($"💡 Meilleur score : {result.NormalizedScore:F1}%");
}
else
{
    // Erreur
    Console.WriteLine($"❌ Erreur : {error}");
}
```

### Exemple avec détail du scoring

```csharp
var (success, result, error) = await matchingService.AnalyzeAndMatchAsync(
    userRequest,
    patientContext
);

if (success && result.HasMatch)
{
    // Afficher le détail du scoring
    Console.WriteLine("📊 Détail du score :");
    foreach (var (criterion, points) in result.ScoreBreakdown.OrderByDescending(x => x.Value))
    {
        Console.WriteLine($"  • {criterion}: {points:F1} pts");
    }
    
    Console.WriteLine($"\n🎯 Total : {result.RawScore:F1} / 210 pts ({result.NormalizedScore:F1}%)");
}
```

## 📋 Logs générés

Le service génère des logs détaillés à chaque étape :

```
[10:08:45] 🚀 DÉBUT DU MATCHING MCC
[10:08:45] 📝 Demande utilisateur : courrier pour l'école...
[10:08:45] 👤 Contexte patient disponible : Martin Lucas, 8 ans
[10:08:45] 🧠 Analyse sémantique en cours...
[10:08:46] ✅ Analyse réussie :
    • Type de document : school_letter
    • Audience : school
    • Ton : formal
    • Tranche d'âge : child
    • Mots-clés : attention, concentration, école
    • Confiance IA : 85%
[10:08:46] 🔍 Recherche dans la bibliothèque MCC...
[10:08:46] 📚 Nombre total de MCC : 1
[10:08:46] 📊 Candidats trouvés : 1
[10:08:46] 🎯 Analyse des scores :
    • "Accompagnement psychologique d'un élève" : 150.0 pts (71.4%)
      Détail du scoring :
        - Type de document: 50.0 pts
        - Audience: 30.0 pts
        - Mots-clés: 26.7 pts
        - Tranche d'âge: 20.0 pts
        - Ton: 15.0 pts
        - Qualité (notes): 0.0 pts
        - Popularité (usage): 6.9 pts
        - Statut validé: 0.0 pts
[10:08:46] 🎲 Vérification du seuil :
    • Score obtenu : 150.0 pts (71.4%)
    • Seuil minimum : 70.0 pts (33.3%)
[10:08:46] ✅ MATCH RÉUSSI avec '"Accompagnement psychologique d'un élève"'
```

## 🎯 Système de scoring (210 points max)

| Critère | Points max | Description |
|---------|------------|-------------|
| **Type de document** | 50 pts | Toujours attribué (filtrage obligatoire) |
| **Mots-clés** | 40 pts | Correspondance avec les mots-clés extraits |
| **Audience** | 30 pts | Correspondance de l'audience cible |
| **Tranche d'âge** | 20 pts | Correspondance de la tranche d'âge |
| **Ton** | 15 pts | Correspondance du ton du document |
| **Qualité (notes)** | 30 pts | Note moyenne des utilisateurs |
| **Popularité (usage)** | 15 pts | Nombre d'utilisations (logarithmique) |
| **Statut validé** | 10 pts | Bonus si le MCC est validé |

**Seuil minimum** : 70 points (33.3%) pour un match réussi

## ⚙️ Configuration

### Modifier le seuil minimum

```csharp
// Dans MCCMatchingService.cs
private const double MIN_CONFIDENCE_SCORE = 70.0;  // Par défaut
```

Augmentez pour être plus strict, diminuez pour être plus tolérant.

### Modifier le nombre de candidats analysés

```csharp
// Dans AnalyzeAndMatchAsync()
var matchingMCCs = _libraryService.FindBestMatchingMCCs(
    analysisResult.DocType,
    metadata,
    analysisResult.Keywords,
    maxResults: 3  // Top 3 pour debug
);
```

## 🐛 Debug et troubleshooting

### Afficher tous les logs dans la console

```csharp
matchingService.PrintMatchingLogs(result);
```

### Vérifier pourquoi un MCC n'a pas matché

```csharp
if (!result.HasMatch)
{
    Console.WriteLine($"Raison : {result.FailureReason}");
    Console.WriteLine($"Meilleur score : {result.RawScore:F1} / 210 pts");
    Console.WriteLine($"Nombre de MCC consultés : {result.TotalMCCsChecked}");
    
    // Afficher tous les logs pour voir chaque étape
    foreach (var log in result.MatchingLogs)
    {
        Console.WriteLine(log);
    }
}
```

### Analyser le scoring d'un MCC

```csharp
if (result.HasMatch)
{
    var breakdown = result.ScoreBreakdown;
    
    // Identifier les points faibles
    var weakPoints = breakdown.Where(x => x.Value == 0).ToList();
    if (weakPoints.Any())
    {
        Console.WriteLine("⚠️ Critères non satisfaits :");
        foreach (var (criterion, _) in weakPoints)
        {
            Console.WriteLine($"  • {criterion}");
        }
    }
}
```

## 📝 Notes techniques

1. **Thread-safe** : Le service peut être utilisé de manière concurrente
2. **Async/Await** : Toutes les méthodes sont asynchrones
3. **Logs horodatés** : Chaque log contient l'heure précise
4. **Normalisation automatique** : Les scores sont automatiquement convertis en pourcentage
5. **Gestion d'erreurs** : Try/catch global avec logs d'erreur détaillés

## 🔄 Migration depuis l'ancienne méthode

### Avant (dans CreateLetterWithAIDialog)

```csharp
// Analyse
var (analysisSuccess, analysisResult, analysisError) = 
    await _reformulationService.AnalyzeLetterRequestAsync(userRequest, patientContext);

// Matching
var matchingMCCs = _mccLibraryService.FindBestMatchingMCCs(
    analysisResult.DocType,
    metadata,
    analysisResult.Keywords,
    maxResults: 1
);

// Vérification manuelle du score
if (matchingMCCs.Any() && matchingMCCs[0].score >= 70.0)
{
    // ...
}
```

### Après (avec MCCMatchingService)

```csharp
// Tout en une seule méthode avec logs détaillés
var (success, result, error) = await _matchingService.AnalyzeAndMatchAsync(
    userRequest,
    patientContext
);

if (success && result.HasMatch)
{
    // Utiliser result.SelectedMCC et result.NormalizedScore
}
```

## 🎓 Avantages

✅ **Clarté** : Tout le flux est visible dans les logs  
✅ **Maintenabilité** : Un seul point d'entrée pour le matching  
✅ **Debug** : Logs détaillés à chaque étape  
✅ **Testabilité** : Service isolé et facilement testable  
✅ **Réutilisabilité** : Utilisable partout dans l'application  
✅ **Normalisation** : Score toujours en pourcentage  
✅ **Traçabilité** : Historique complet du processus  

---

**Créé le** : 2025-11-02  
**Version** : 1.0  
**Auteur** : Système MedCompanion
