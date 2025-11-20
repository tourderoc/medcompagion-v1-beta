# 🧩 Feuille de route - Système de Prompts Intelligent (MCC)

**Date de création :** 31/10/2025  
**Version :** 1.0  
**Durée estimée :** 6-8 semaines

---

## 📊 Vue d'ensemble du projet

### 🎯 Objectif global

Mettre en place un **système de prompts intelligent** dans MedCompanion qui :
- Apprend automatiquement à partir des documents (courriers, attestations, notes)
- Extrait la structure ET la sémantique des documents
- Améliore continuellement la qualité des textes générés par l'IA
- Crée une bibliothèque de **Modèles de Communication Clinique (MCC)** réutilisables

### 🎁 Bénéfices attendus

| Métrique | Amélioration attendue |
|----------|----------------------|
| **Qualité des textes générés** | +30-40% |
| **Temps de réécriture manuelle** | -50% |
| **Pertinence contextuelle** | +60% |
| **Réutilisation de bonnes pratiques** | Automatique |

### 🏗️ Architecture globale

```
┌─────────────────────────────────────────────────────────────┐
│                    UTILISATEUR (Médecin)                     │
└─────────────────────┬───────────────────────────────────────┘
                      │
                      ▼
         ┌────────────────────────────┐
         │  IntelligentPromptService  │ ◄─── Point d'entrée unique
         └────────┬──────────┬────────┘
                  │          │
        ┌─────────▼──────┐   │
        │ MCCLibraryService│◄─┘
        │  (Bibliothèque)  │
        └─────────┬────────┘
                  │
        ┌─────────▼──────────┐
        │  SemanticAnalysis  │ ◄─── Analyse IA
        │    + Extraction    │
        └─────────┬──────────┘
                  │
        ┌─────────▼──────────┐
        │   MCCLearning      │ ◄─── Notation & Promotion
        │     Service        │
        └────────────────────┘
```

### 📦 Composants existants réutilisables

✅ **TemplateExtractorService** : Extraction de templates  
✅ **PromptConfigService** : Gestion de prompts avec versionnage  
✅ **OpenAIService** : Interface IA  
✅ **StorageService** : Persistance JSON  

---

## 🏗️ Décision architecturale : MVVM ou pas ?

### 🎯 Approche retenue : ARCHITECTURE HYBRIDE

Après analyse, nous adoptons une **approche hybride** qui équilibre pragmatisme et qualité :

#### ❌ Services (Backend) → PAS de MVVM

**Composants concernés :**
- MCCLibraryService
- IntelligentPromptService
- MCCLearningService
- TemplateExtractorService (enrichi)
- Modèles de données (MCCModel, SemanticAnalysis, GenerationFeedback)

**Justification :**
- Ces services sont de la **logique métier pure** sans interaction UI
- Aucun binding, aucun PropertyChanged nécessaire
- Classes simples avec méthodes async = Développement plus rapide
- **Gain estimé : ~1 semaine** sur les phases 1-2

#### ✅ Interfaces utilisateur → AVEC MVVM

**Composants concernés :**
- MCCDashboardDialog (Dashboard statistiques)
- MCCLibraryDialog (Gestion bibliothèque)
- RatingControl (Contrôle de notation - optionnel)
- Dialogs Import/Export

**Justification :**
- Cohérence avec la migration MVVM en cours du projet
- Binding de données propre (ObservableCollection, INotifyPropertyChanged)
- Testabilité accrue (unit tests des ViewModels)
- Séparation claire présentation/logique
- Meilleure maintenabilité des UIs complexes

### 📊 Tableau de décision par composant

| Composant | Implémentation | Justification |
|-----------|---------------|---------------|
| **MCCLibraryService** | Classe simple | Service pur, aucune UI |
| **IntelligentPromptService** | Classe simple | Orchestration backend |
| **MCCLearningService** | Classe simple | Algorithmes, pas d'UI |
| **TemplateExtractorService** | Classe simple | Service existant étendu |
| **MCCModel, SemanticAnalysis** | POCO | Modèles de données purs |
| **MCCDashboardDialog** | MVVM | UI complexe avec stats dynamiques |
| **MCCLibraryDialog** | MVVM | Gestion liste, filtres, édition |
| **RatingControl** | Simple / MVVM | UserControl (MVVM si réutilisé) |
| **Import/Export Dialogs** | MVVM | UI avec validation |

### 🎁 Avantages de cette approche

✅ **Pragmatisme** : MVVM uniquement là où c'est utile  
✅ **Cohérence** : Aligné avec la migration MVVM du projet  
✅ **Performance** : Services légers sans overhead MVVM  
✅ **Maintenabilité** : UIs structurées avec ViewModels  
✅ **Gain de temps** : ~1 semaine économisée (5-7 semaines vs 6-8)  

### 📁 Structure des fichiers résultante

```
MedCompanion/
│
├── Services/                           # Pas de MVVM
│   ├── MCCLibraryService.cs           # Classe simple
│   ├── IntelligentPromptService.cs    # Classe simple
│   ├── MCCLearningService.cs          # Classe simple
│   └── TemplateExtractorService.cs    # Extension existant
│
├── Models/                             # POCOs purs
│   ├── MCCModel.cs                    # Pas de PropertyChanged
│   ├── SemanticAnalysis.cs            # Pas de PropertyChanged
│   └── GenerationFeedback.cs          # Pas de PropertyChanged
│
├── ViewModels/                         # MVVM pour UIs
│   ├── MCCDashboardViewModel.cs       # ObservableCollection + ICommand
│   └── MCCLibraryViewModel.cs         # ObservableCollection + ICommand
│
├── Dialogs/                            # XAML avec binding
│   ├── MCCDashboardDialog.xaml        # DataContext = ViewModel
│   └── MCCLibraryDialog.xaml          # DataContext = ViewModel
│
└── Controls/
    └── RatingControl.xaml             # UserControl simple
```

### 🔄 Interaction Services ↔ ViewModels

```csharp
// ViewModel utilise les services (injection de dépendances)
public class MCCDashboardViewModel : ViewModelBase
{
    private readonly MCCLibraryService _library;      // Service simple
    private readonly MCCLearningService _learning;    // Service simple
    
    public ObservableCollection<MCCStatItem> Stats { get; }  // Pour UI
    
    public MCCDashboardViewModel(
        MCCLibraryService library,
        MCCLearningService learning
    )
    {
        _library = library;
        _learning = learning;
        
        // Charger stats depuis services
        LoadStatistics();
    }
    
    private void LoadStatistics()
    {
        var stats = _library.GetStatistics();  // Service simple
        // Transformer en ObservableCollection pour binding UI
        Stats = new ObservableCollection<MCCStatItem>(...);
    }
}
```

### ⚠️ Ce qu'il faut ÉVITER

❌ **MVVM pour services backend** : Over-engineering inutile  
❌ **PropertyChanged dans services** : Aucun bénéfice  
❌ **ViewModels sans UI associée** : Complexité inutile  
❌ **Modèles avec logique métier** : Garder POCOs purs  

### ✅ Résumé de la décision

> **"MVVM là où ça apporte de la valeur (UIs complexes), pas là où c'est inutile (services backend)"**

Cette approche hybride offre le meilleur compromis entre qualité du code, maintenabilité et rapidité de développement.


## 🚀 Phase 1 : Fondations (2-3 semaines)

### 🎯 Objectifs
- Créer les modèles de données pour MCC
- Enrichir l'extraction de templates avec analyse sémantique
- Mettre en place le stockage de bibliothèque MCC

### 📋 Tâches détaillées

#### 1.1 Créer les modèles de données (3 jours)

**Fichier :** `MedCompanion/Models/MCCModel.cs`

```csharp
public class MCCModel
{
    public string Id { get; set; }
    public string Name { get; set; }
    public int Version { get; set; }
    public DateTime Created { get; set; }
    public DateTime LastModified { get; set; }
    
    // Statistiques d'utilisation
    public int UsageCount { get; set; }
    public double AverageRating { get; set; }
    public int TotalRatings { get; set; }
    
    // Analyse sémantique
    public SemanticAnalysis Semantic { get; set; }
    
    // Template et prompt
    public string TemplateMarkdown { get; set; }
    public string PromptTemplate { get; set; }
    public List<string> Keywords { get; set; }
    
    // État
    public MCCStatus Status { get; set; } // Draft, Active, Validated, Deprecated
}

public class SemanticAnalysis
{
    public string Tone { get; set; }        // formel, bienveillant, technique
    public string Audience { get; set; }    // école, parents, médecin, institution
    public string AgeGroup { get; set; }    // 0-3, 3-6, 6-12, 12-18 ans
    public string DocType { get; set; }     // courrier, attestation, note, compte-rendu
    public List<string> ClinicalKeywords { get; set; }
    public Dictionary<string, string> Sections { get; set; }
}

public enum MCCStatus
{
    Draft,      // En cours de création
    Active,     // Utilisable
    Validated,  // Promu après bonnes notes
    Deprecated  // Obsolète
}
```

**Fichier :** `MedCompanion/Models/GenerationFeedback.cs`

```csharp
public class GenerationFeedback
{
    public string Id { get; set; }
    public string GenerationId { get; set; }
    public string MCCUsed { get; set; }
    public int Rating { get; set; }         // 1-5 étoiles
    public string Comment { get; set; }
    public DateTime Timestamp { get; set; }
    public string PatientContext { get; set; } // Hash anonymisé
}
```

#### 1.2 Enrichir TemplateExtractorService (5 jours)

**Fichier :** `MedCompanion/Services/TemplateExtractorService.cs`

Ajouter la méthode :

```csharp
public async Task<(bool success, SemanticAnalysis analysis, string error)> 
    AnalyzeDocumentSemantic(string documentText)
{
    var systemPrompt = @"Tu es un expert en analyse de documents médicaux.
Analyse ce document et identifie :

1. **TON** : formel / bienveillant / technique / institutionnel
2. **PUBLIC** : école / parents / médecin / MDPH / autre institution
3. **TRANCHE D'ÂGE** : 0-3 ans / 3-6 ans / 6-12 ans / 12-18 ans / adulte
4. **TYPE** : courrier / attestation / compte-rendu / note / certificat
5. **MOTS-CLÉS CLINIQUES** : Liste des termes médicaux importants
6. **SECTIONS** : Structure du document (en-tête, contexte, recommandations, etc.)

FORMAT DE RÉPONSE :
```json
{
  ""tone"": ""formel_bienveillant"",
  ""audience"": ""ecole"",
  ""age_group"": ""6-12"",
  ""doc_type"": ""courrier"",
  ""clinical_keywords"": [""TDAH"", ""aménagements"", ""PAP""],
  ""sections"": {
    ""intro"": ""présentation du contexte"",
    ""diagnostic"": ""éléments cliniques"",
    ""recommandations"": ""préconisations pratiques""
  }
}
```";

    var userPrompt = $"DOCUMENT À ANALYSER :\n\n{documentText}";
    
    var (success, result) = await _openAIService.ChatAvecContexteAsync(
        string.Empty, userPrompt, null, systemPrompt
    );
    
    if (!success) return (false, null, result);
    
    // Parser le JSON retourné
    var analysis = JsonSerializer.Deserialize<SemanticAnalysis>(result);
    return (true, analysis, string.Empty);
}

public async Task<(bool success, MCCModel mcc, string error)>
    GenerateMCCFromExample(string exampleDocument)
{
    // 1. Extraire template (méthode existante)
    var (extractSuccess, template, name, variables, extractError) = 
        await ExtractTemplateFromExample(exampleDocument);
    
    if (!extractSuccess) return (false, null, extractError);
    
    // 2. Analyser sémantique (NOUVEAU)
    var (analyzeSuccess, semantic, analyzeError) = 
        await AnalyzeDocumentSemantic(exampleDocument);
    
    if (!analyzeSuccess) return (false, null, analyzeError);
    
    // 3. Créer le MCC
    var mcc = new MCCModel
    {
        Id = GenerateMCCId(name, semantic),
        Name = name,
        Version = 1,
        Created = DateTime.Now,
        LastModified = DateTime.Now,
        Semantic = semantic,
        TemplateMarkdown = template,
        PromptTemplate = GeneratePromptFromTemplate(template, semantic),
        Keywords = semantic.ClinicalKeywords,
        Status = MCCStatus.Active
    };
    
    return (true, mcc, string.Empty);
}
```

#### 1.3 Créer MCCLibraryService (4 jours)

**Fichier :** `MedCompanion/Services/MCCLibraryService.cs`

```csharp
public class MCCLibraryService
{
    private readonly string _libraryPath;
    private Dictionary<string, MCCModel> _library;
    
    public MCCLibraryService()
    {
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MedCompanion"
        );
        _libraryPath = Path.Combine(appData, "mcc-library.json");
        _library = LoadLibrary();
    }
    
    // Rechercher le meilleur MCC selon critères
    public MCCModel FindBestMCC(
        string docType,
        Dictionary<string, string> metadata
    )
    {
        // Filtrer par type de document
        var candidates = _library.Values
            .Where(m => m.Semantic.DocType == docType && m.Status == MCCStatus.Active)
            .ToList();
        
        if (!candidates.Any()) return null;
        
        // Scorer chaque candidat
        var scored = candidates.Select(mcc => new
        {
            MCC = mcc,
            Score = CalculateMatchScore(mcc, metadata)
        })
        .OrderByDescending(x => x.Score)
        .ToList();
        
        return scored.FirstOrDefault()?.MCC;
    }
    
    private double CalculateMatchScore(MCCModel mcc, Dictionary<string, string> metadata)
    {
        double score = 0;
        
        // Correspondance audience (+30 points)
        if (metadata.TryGetValue("audience", out var audience) && 
            mcc.Semantic.Audience == audience)
            score += 30;
        
        // Correspondance tranche d'âge (+20 points)
        if (metadata.TryGetValue("age_group", out var ageGroup) && 
            mcc.Semantic.AgeGroup == ageGroup)
            score += 20;
        
        // Qualité (rating moyen * 10)
        score += mcc.AverageRating * 10;
        
        // Usage (log pour éviter biais des très utilisés)
        score += Math.Log(mcc.UsageCount + 1) * 5;
        
        return score;
    }
    
    // Ajouter un nouveau MCC
    public (bool success, string message) AddMCC(MCCModel mcc)
    {
        if (_library.ContainsKey(mcc.Id))
            return (false, "MCC existe déjà");
        
        _library[mcc.Id] = mcc;
        return SaveLibrary();
    }
    
    // Incrémenter usage
    public void IncrementUsage(string mccId)
    {
        if (_library.TryGetValue(mccId, out var mcc))
        {
            mcc.UsageCount++;
            mcc.LastModified = DateTime.Now;
            SaveLibrary();
        }
    }
    
    // Obtenir statistiques
    public Dictionary<string, object> GetStatistics()
    {
        return new Dictionary<string, object>
        {
            ["total_mccs"] = _library.Count,
            ["active_mccs"] = _library.Values.Count(m => m.Status == MCCStatus.Active),
            ["validated_mccs"] = _library.Values.Count(m => m.Status == MCCStatus.Validated),
            ["total_usage"] = _library.Values.Sum(m => m.UsageCount),
            ["average_rating"] = _library.Values
                .Where(m => m.TotalRatings > 0)
                .Average(m => m.AverageRating)
        };
    }
}
```

#### 1.4 Tests et validation (3 jours)

**Tests unitaires à créer :**
- Test d'extraction de template avec analyse sémantique
- Test de recherche de MCC (différents critères)
- Test de scoring des MCC
- Test de persistance bibliothèque

**Validation :**
- Tester avec 10 exemples de documents variés
- Vérifier la qualité de l'analyse sémantique
- Valider le système de scoring

### ✅ Livrables Phase 1
- ✅ Modèles MCCModel, SemanticAnalysis, GenerationFeedback
- ✅ TemplateExtractorService enrichi
- ✅ MCCLibraryService fonctionnel
- ✅ Tests passants
- ✅ Documentation technique

---

## 🎯 Phase 2 : Interception intelligente (1-2 semaines)

### 🎯 Objectifs
- Créer le point d'entrée unique pour toutes les générations IA
- Intégrer la sélection automatique de MCC
- Refactorer les appels IA existants

### 📋 Tâches détaillées

#### 2.1 Créer IntelligentPromptService (5 jours)

**Fichier :** `MedCompanion/Services/IntelligentPromptService.cs`

```csharp
public class IntelligentPromptService
{
    private readonly OpenAIService _openAI;
    private readonly MCCLibraryService _mccLibrary;
    private readonly PromptConfigService _promptConfig;
    
    public IntelligentPromptService(
        OpenAIService openAI,
        MCCLibraryService mccLibrary,
        PromptConfigService promptConfig
    )
    {
        _openAI = openAI;
        _mccLibrary = mccLibrary;
        _promptConfig = promptConfig;
    }
    
    /// <summary>
    /// Point d'entrée intelligent pour génération IA
    /// </summary>
    public async Task<(bool success, string result, string mccUsed)> GenerateWithIntelligence(
        string taskType,              // "note", "courrier", "attestation"
        string userRequest,
        string patientContext,
        Dictionary<string, string> metadata
    )
    {
        // 1. Déterminer le type de document
        var docType = DetermineDocType(taskType, userRequest);
        
        // 2. Rechercher le meilleur MCC
        var mcc = _mccLibrary.FindBestMCC(docType, metadata);
        
        // 3. Construire le prompt enrichi
        string systemPrompt;
        string enhancedUserPrompt;
        
        if (mcc != null)
        {
            // Utiliser le MCC trouvé
            systemPrompt = BuildSystemPromptWithMCC(mcc);
            enhancedUserPrompt = BuildUserPromptWithMCC(
                userRequest, patientContext, mcc
            );
            
            // Incrémenter usage
            _mccLibrary.IncrementUsage(mcc.Id);
        }
        else
        {
            // Fallback sur prompts de base
            systemPrompt = _promptConfig.GetActivePrompt("system_global");
            enhancedUserPrompt = BuildStandardUserPrompt(
                userRequest, patientContext
            );
        }
        
        // 4. Appeler l'IA
        var (success, result) = await _openAI.ChatAvecContexteAsync(
            patientContext,
            enhancedUserPrompt,
            null,
            systemPrompt
        );
        
        return (success, result, mcc?.Id ?? "default");
    }
    
    private string BuildSystemPromptWithMCC(MCCModel mcc)
    {
        var basePrompt = _promptConfig.GetActivePrompt("system_global");
        
        return $@"{basePrompt}

🎯 MODÈLE DE COMMUNICATION CLINIQUE (MCC) ACTIF
----
Nom : {mcc.Name}
Public : {mcc.Semantic.Audience}
Ton : {mcc.Semantic.Tone}
Mots-clés : {string.Join(", ", mcc.Keywords)}

Template à suivre :
{mcc.PromptTemplate}";
    }
}
```

#### 2.2 Refactorer les appels IA (3 jours)

**Fichiers à modifier :**
- `LetterService.cs` → utiliser IntelligentPromptService
- `AttestationService.cs` → utiliser IntelligentPromptService  
- `MainWindow.xaml.cs` (chat) → utiliser IntelligentPromptService

**Exemple de refactoring :**

```csharp
// AVANT
var (success, result) = await _openAIService.ChatAvecContexteAsync(
    patientContext, userRequest, null, systemPrompt
);

// APRÈS
var (success, result, mccUsed) = await _intelligentPromptService.GenerateWithIntelligence(
    "courrier", userRequest, patientContext, metadata
);
```

#### 2.3 Tests d'intégration (2 jours)

- Tester génération de courriers avec MCC
- Tester fallback si aucun MCC trouvé
- Vérifier que les statistiques sont bien mises à jour

### ✅ Livrables Phase 2
- ✅ IntelligentPromptService opérationnel
- ✅ Refactoring des services existants
- ✅ Tests d'intégration passants
- ✅ Sélection automatique de MCC fonctionnelle

---

## ⭐ Phase 3 : Apprentissage (2 semaines)

### 🎯 Objectifs
- Implémenter le système de notation
- Créer l'algorithme de promotion automatique
- Dashboard de statistiques

### 📋 Tâches détaillées

#### 3.1 UI de notation (3 jours)

**Fichier :** `MedCompanion/Controls/RatingControl.xaml`

```xml
<UserControl x:Class="MedCompanion.Controls.RatingControl">
    <StackPanel Orientation="Horizontal" HorizontalAlignment="Center">
        <TextBlock Text="Qualité de ce document :" 
                   Margin="0,0,10,0" 
                   VerticalAlignment="Center"/>
        
        <!-- 5 étoiles cliquables -->
        <Button x:Name="Star1" Content="⭐" Click="Star_Click" Tag="1"/>
        <Button x:Name="Star2" Content="⭐" Click="Star_Click" Tag="2"/>
        <Button x:Name="Star3" Content="⭐" Click="Star_Click" Tag="3"/>
        <Button x:Name="Star4" Content="⭐" Click="Star_Click" Tag="4"/>
        <Button x:Name="Star5" Content="⭐" Click="Star_Click" Tag="5"/>
    </StackPanel>
</UserControl>
```

**Intégration dans :**
- Onglet Courriers (après génération)
- Onglet Attestations (après génération)
- Onglet Notes (après structuration)

#### 3.2 MCCLearningService (5 jours)

**Fichier :** `MedCompanion/Services/MCCLearningService.cs`

```csharp
public class MCCLearningService
{
    private readonly MCCLibraryService _library;
    private readonly string _feedbackPath;
    private List<GenerationFeedback> _feedbacks;
    
    // Seuils de décision
    private const int MIN_RATINGS_FOR_PROMOTION = 10;
    private const double PROMOTION_THRESHOLD = 4.0;
    private const int MIN_RATINGS_FOR_DEPRECATION = 5;
    private const double DEPRECATION_THRESHOLD = 2.5;
    
    public void AddFeedback(string generationId, string mccId, int rating, string comment = "")
    {
        var feedback = new GenerationFeedback
        {
            Id = Guid.NewGuid().ToString(),
            GenerationId = generationId,
            MCCUsed = mccId,
            Rating = rating,
            Comment = comment,
            Timestamp = DateTime.Now
        };
        
        _feedbacks.Add(feedback);
        SaveFeedbacks();
        
        // Mettre à jour les stats du MCC
        UpdateMCCRating(mccId, rating);
        
        // Vérifier si promotion/dégradation nécessaire
        CheckForStatusChange(mccId);
    }
    
    private void UpdateMCCRating(string mccId, int newRating)
    {
        var mcc = _library.GetMCC(mccId);
        if (mcc == null) return;
        
        // Calcul moyenne mobile
        var totalRatings = mcc.TotalRatings + 1;
        var newAverage = ((mcc.AverageRating * mcc.TotalRatings) + newRating) / totalRatings;
        
        mcc.TotalRatings = totalRatings;
        mcc.AverageRating = newAverage;
        mcc.LastModified = DateTime.Now;
        
        _library.UpdateMCC(mcc);
    }
    
    private void CheckForStatusChange(string mccId)
    {
        var mcc = _library.GetMCC(mccId);
        if (mcc == null) return;
        
        // PROMOTION : Active → Validated
        if (mcc.Status == MCCStatus.Active && 
            mcc.TotalRatings >= MIN_RATINGS_FOR_PROMOTION &&
            mcc.AverageRating >= PROMOTION_THRESHOLD)
        {
            mcc.Status = MCCStatus.Validated;
            mcc.LastModified = DateTime.Now;
            _library.UpdateMCC(mcc);
            
            System.Diagnostics.Debug.WriteLine(
                $"[MCCLearning] MCC promu : {mcc.Name} (rating: {mcc.AverageRating:F2})"
            );
        }
        
        // DÉGRADATION : Active → Deprecated
        else if (mcc.Status == MCCStatus.Active &&
                 mcc.TotalRatings >= MIN_RATINGS_FOR_DEPRECATION &&
                 mcc.AverageRating < DEPRECATION_THRESHOLD)
        {
            mcc.Status = MCCStatus.Deprecated;
            mcc.LastModified = DateTime.Now;
            _library.UpdateMCC(mcc);
            
            System.Diagnostics.Debug.WriteLine(
                $"[MCCLearning] MCC déprécié : {mcc.Name} (rating: {mcc.AverageRating:F2})"
            );
        }
    }
    
    public Dictionary<string, object> GetLearningStatistics()
    {
        return new Dictionary<string, object>
        {
            ["total_feedbacks"] = _feedbacks.Count,
            ["average_rating_all"] = _feedbacks.Average(f => f.Rating),
            ["promoted_mccs"] = _library.GetMCCsByStatus(MCCStatus.Validated).Count,
            ["deprecated_mccs"] = _library.GetMCCsByStatus(MCCStatus.Deprecated).Count,
            ["recent_promotions"] = GetRecentPromotions(30) // 30 derniers jours
        };
    }
}
```

#### 3.3 Dashboard statistiques (4 jours)

**Nouvelle fenêtre :** `MedCompanion/Dialogs/MCCDashboardDialog.xaml`

Afficher :
- Nombre total de MCC (par statut)
- MCC les plus utilisés (top 10)
- MCC les mieux notés (top 10)
- Graphique d'évolution des notes
- Historique des promotions

### ✅ Livrables Phase 3
- ✅ Système de notation opérationnel
- ✅ MCCLearningService avec algorithme de promotion
- ✅ Dashboard de statistiques
- ✅ Boucle d'amélioration continue active

---

## 🎨 Phase 4 : Polissage (1 semaine)

### 🎯 Objectifs
- Interface de gestion de bibliothèque MCC
- Import/Export de MCC
- Documentation utilisateur

### 📋 Tâches détaillées

#### 4.1 UI de gestion bibliothèque (3 jours)

**Nouvel onglet dans Templates :** "Bibliothèque MCC"

Fonctionnalités :
- Liste de tous les MCC (filtrables par statut)
- Prévisualisation d'un MCC (métadonnées + template)
- Édition des métadonnées
- Export/Import JSON
- Suppression de MCC

#### 4.2 Import/Export (2 jours)

```csharp
public class MCCImportExportService
{
    public (bool success, string message) ExportMCC(string mccId, string filePath)
    {
        var mcc = _library.GetMCC(mccId);
        if (mcc == null) return (false, "MCC introuvable");
        
        var json = JsonSerializer.Serialize(mcc, _jsonOptions);
        File.WriteAllText(filePath, json);
        
        return (true, "MCC exporté");
    }
    
    public (bool success, string message) ImportMCC(string filePath)
    {
        var json = File.ReadAllText(filePath);
        var mcc = JsonSerializer.Deserialize<MCCModel>(json);
        
        // Régénérer ID pour éviter conflits
        mcc.Id = Guid.NewGuid().ToString();
        mcc.Version = 1;
        mcc.Created = DateTime.Now;
        
        return _library.AddMCC(mcc);
    }
    
    public (bool success, string message) ExportLibrary(string directoryPath)
    {
        // Exporter toute la bibliothèque
        foreach (var mcc in _library.GetAllMCCs())
        {
            var fileName = $"{mcc.Id}.json";
            ExportMCC(mcc.Id, Path.Combine(directoryPath, fileName));
        }
        
        return (true, $"{_library.GetAllMCCs().Count} MCC exportés");
    }
}
```

#### 4.3 Documentation (2 jours)

**Documents à créer :**
- Guide utilisateur : "Comment fonctionne le système MCC ?"
- Guide admin : "Gérer la bibliothèque MCC"
- FAQ : Questions fréquentes

### ✅ Livrables Phase 4
- ✅ Interface de gestion complète
- ✅ Import/Export fonctionnel
- ✅ Documentation utilisateur/admin

---

## ⚠️ Risques et mitigations

### Risques techniques

| Risque | Impact | Probabilité | Mitigation |
|--------|--------|-------------|------------|
| **Performance** (bibliothèque > 1000 MCC) | Moyen | Moyenne | Cache en mémoire + indexation |
| **Qualité analyse IA** | Élevé | Moyenne | Validation manuelle + seuil de confiance |
| **Stockage** (croissance fichiers JSON) | Faible | Élevée | Rotation + archivage ancien nes versions |
| **Conflits de MCC** (plusieurs candidats équivalents) | Moyen | Moyenne | Système de scoring robuste |

### Risques UX

| Risque | Impact | Probabilité | Mitigation |
|--------|--------|-------------|------------|
| **Manque de transparence** | Élevé | Moyenne | Indiquer quel MCC est utilisé |
| **Sur-automatisation** | Moyen | Faible | Toggle pour désactiver le système |
| **Feedback loop faible** | Élevé | Élevée | Inciter notation (non intrusif) |

---

## 📊 Critères de succès

### Métriques quantitatives

| KPI | Objectif Phase 1 | Objectif Phase 3 | Objectif Phase 4 |
|-----|------------------|------------------|------------------|
| **MCC créés** | 5-10 | 20-30 | 50+ |
| **Taux d'utilisation MCC** | - | 60% | 80% |
| **Rating moyen** | - | 3.5/5 | 4.0/5 |
| **Taux de promotion** | - | 20% | 30% |
| **Réduction temps réécriture** | - | -30% | -50% |

### Métriques qualitatives

✅ **Satisfaction utilisateur** : Retours positifs sur la pertinence des textes  
✅ **Transparence** : L'utilisateur comprend le système  
✅ **Fiabilité** : Pas de génération aberrante  
✅ **Évolutivité** : Bibliothèque s'enrichit naturellement  

---

## 🎯 Prochaines étapes après Phase 4

### Évolutions futures possibles

1. **Multi-LLM** : Support de plusieurs modèles IA (GPT-4, Claude, Mistral)
2. **Personnalisation médecin** : MCC spécifiques par praticien
3. **Analyse de sentiment** : Détecter le ton émotionnel des documents
4. **Suggestions proactives** : "Ce MCC pourrait correspondre à votre situation"
5. **Partage communautaire** : Bibliothèque partagée entre praticiens (anonymisée)

---

## 📅 Calendrier récapitulatif

```
Semaine 1-2    : Phase 1.1-1.2 (Modèles + Analyse sémantique)
Semaine 3      : Phase 1.3-1.4 (Bibliothèque + Tests)
Semaine 4-5    : Phase 2 (Interception intelligente)
Semaine 6-7    : Phase 3 (Apprentissage + Notation)
Semaine 8      : Phase 4 (Polissage + Documentation)
```

**Date de fin estimée :** Mi-décembre 2025

---

## ✅ Validation et go/no-go

### Conditions pour démarrer
- ✅ Architecture actuelle compatible
- ✅ OpenAIService fonctionnel
- ✅ Temps disponible (6-8 semaines)
- ✅ Prototype validé sur Étape 1

### Décision finale
**GO** pour commencer par un **proof-of-concept Phase 1** (2 semaines).

Si succès → Continuer phases suivantes  
Si échec → Revoir l'approche

---

*Document créé le 31/10/2025 - Version 1.0*
