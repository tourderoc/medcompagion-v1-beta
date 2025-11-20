# 🗺️ Feuille de Route - Migration MVVM MedCompanion

## 📊 Vue d'ensemble du Projet

**Objectif Global** : Migrer progressivement l'application vers l'architecture MVVM  
**Date de début** : 24/10/2025  
**Statut actuel** : Étapes 1, 2, 3 et 5 complétées ✅

---

## ✅ MIGRATION PATHSERVICE [TERMINÉE]

### ⚠️ NOTE IMPORTANTE
La centralisation des chemins via PathService a été complétée avec succès. Toutes les fonctionnalités utilisent maintenant le système PathService pour garantir la cohérence des chemins.

**Ajouté le** : 25/10/2025  
**Complété le** : 26/10/2025  
**Statut** : ✅ Terminé

### Actions Réalisées

1. ✅ **PathService.cs** - Service centralisé de gestion des chemins
   - Méthodes pour tous les types de dossiers (notes, courriers, documents, etc.)
   - Normalisation des noms de patients
   - Gestion de la structure des années
   - Méthodes d'assistance (EnsurePatientStructure, PatientExists, etc.)

2. ✅ **Migration Notes** → PathService
   - NoteViewModel utilise GetNotesDirectory()
   - Fonctionne correctement avec la nouvelle structure

3. ✅ **Migration Documents** → PathService
   - DocumentService utilise GetDocumentsDirectory()
   - Sous-dossiers gérés (bilans, courriers, ordonnances, radiologies, analyses, autres)

4. ✅ **Migration Synthèse** → PathService
   - SynthesisService utilise GetSyntheseDirectory()
   - Dossier synthese à la racine du patient (transversal aux années)

5. ✅ **Migration Formulaires** → PathService
   - FormulaireAssistantService utilise GetFormulairesDirectory()

6. ✅ **Migration Données Patient** → info_patient/
   - Création dossier info_patient/ dédié aux données administratives
   - PatientIndexService migré pour chercher dans info_patient/patient.json
   - Rétrocompatibilité totale avec ancienne structure
   - Script PowerShell de migration fourni (migrate-patient-json.ps1)

### Fichiers Créés/Modifiés
- `MedCompanion/Services/PathService.cs` (nouveau)
- `MedCompanion/ViewModels/NoteViewModel.cs` (modifié)
- `MedCompanion/Services/DocumentService.cs` (modifié)
- `MedCompanion/Services/SynthesisService.cs` (modifié)
- `MedCompanion/Services/FormulaireAssistantService.cs` (modifié)
- `MedCompanion/Services/PatientIndexService.cs` (modifié)
- `MIGRATION_INFO_PATIENT.md` (documentation)
- `migrate-patient-json.ps1` (script migration)

### Documentation Créée
- `PATH_SERVICE_MIGRATION.md`
- `BUGFIX_NOTES_PATHSERVICE.md`
- `BUGFIX_DOCUMENTS_PATHSERVICE.md`
- `BUGFIX_DOCUMENTS_SYNTHESE_PATHSERVICE.md`
- `BUGFIX_FORMULAIRES_PATHSERVICE.md`
- `MIGRATION_INFO_PATIENT.md`

### Commits Git Effectués
1. `feat: Add PathService for centralized path management`
2. `fix: Update NoteViewModel to use PathService`
3. `fix: Update DocumentService to use PathService`
4. `fix: Update SynthesisService to use PathService`
5. `fix: Update FormulaireAssistantService to use PathService`
6. `feat: Migration données patient vers dossier info_patient/`

### Nouvelle Structure des Dossiers Patients
```
patients/DUPONT_Yanis/
  info_patient/          ← NOUVEAU dossier dédié
    patient.json         ← Données administratives
  2025/
    notes/
    chat/
    courriers/
    ordonnances/
    attestations/
    documents/
      bilans/
      courriers/
      ordonnances/
      radiologies/
      analyses/
      autres/
    formulaires/
  synthese/              ← Dossier transversal (racine patient)
```

### Durée Réelle
4 heures (répartie sur 2 jours)

---

## ✅ ÉTAPE 1 : Architecture MVVM de Base [TERMINÉE]

### Actions Réalisées
1. ✅ Création de `ObservableObject.cs` (base INotifyPropertyChanged)
2. ✅ Création de `RelayCommand.cs` (implémentation ICommand)
3. ✅ Création de `ViewModelBase.cs` (classe parente ViewModels)
4. ✅ Création de `PatientSearchViewModel.cs` (200+ lignes)
5. ✅ Intégration dans MainWindow.xaml.cs
6. ✅ Configuration DataContext pour SearchBox
7. ✅ Binding XAML : SearchBox.Text → PatientSearchViewModel.SearchText

### Fichiers Créés
- `MedCompanion/Helpers/ObservableObject.cs`
- `MedCompanion/Commands/RelayCommand.cs`
- `MedCompanion/ViewModels/ViewModelBase.cs`
- `MedCompanion/ViewModels/PatientSearchViewModel.cs`

### Fichiers Modifiés
- `MedCompanion/MainWindow.xaml.cs` (lignes ~40, ~169, ~172)
- `MedCompanion/MainWindow.xaml` (binding SearchBox)

### Commits Git Effectués
1. `Add MVVM base architecture (ObservableObject, RelayCommand, ViewModelBase, PatientSearchViewModel)`
2. `Integrate PatientSearchViewModel in MainWindow code-behind`
3. `Add XAML binding: SearchBox.Text to PatientSearchViewModel.SearchText`
4. `Configure DataContext for SearchBox to bind to PatientSearchViewModel`

### Tests
✅ Compilation : 0 erreur  
✅ Application : Fonctionne normalement  
⚠️ État hybride : Ancien ET nouveau code coexistent

---

## ✅ ÉTAPE 2 : Finaliser PatientSearchViewModel [TERMINÉE]

### Objectif
Remplacer complètement l'ancien code par le nouveau système MVVM pour la recherche patient

### Actions Réalisées
1. ✅ Bindings XAML complets :
   - `SuggestPopup.IsOpen` → `PatientSearchViewModel.IsPopupOpen`
   - `SuggestList.ItemsSource` → `PatientSearchViewModel.Suggestions`
   - `SuggestList.SelectedIndex` → `PatientSearchViewModel.SelectedSuggestionIndex`
   - `ValidateBtn.Command` → `PatientSearchViewModel.ValidateCommand`

2. ✅ Navigation clavier implémentée dans ViewModel
3. ✅ Ancien code supprimé de MainWindow.xaml.cs
4. ✅ Tests complets effectués et validés

### Fichiers Modifiés
- `MedCompanion/MainWindow.xaml` (bindings complets)
- `MedCompanion/MainWindow.xaml.cs` (nettoyage code-behind)

### Commits Git Effectués
1. `Complete PatientSearchViewModel XAML bindings and remove code-behind logic`

---

## ✅ ÉTAPE 3 : NoteViewModel [TERMINÉE]

### Objectif
Extraire la gestion des notes cliniques dans un ViewModel dédié

### Actions Réalisées
1. ✅ Création de `NoteViewModel.cs` (550+ lignes)
2. ✅ Propriétés migrées :
   - `RawNoteText`, `StructuredNoteDocument`
   - `IsStructuredNoteReadOnly`, `IsRawNoteVisible`
   - États des boutons (Modifier, Supprimer, Sauvegarder, Structurer)
   - Collection `Notes` et `SelectedNote`

3. ✅ Commandes implémentées :
   - `StructurerCommand` (structuration note avec IA)
   - `SaveCommand` (sauvegarde note)
   - `EditCommand` (mode édition)
   - `DeleteCommand` (suppression note)

4. ✅ Bindings XAML complets :
   - Tous les TextBox/RichTextBox bindés au ViewModel
   - Boutons bindés aux Commands
   - Visibilité contrôlée par propriétés booléennes

5. ✅ Optimisations UX :
   - Zone "Note brute" masquée en consultation
   - Proportions optimisées (Synthèse 1*, Note structurée 2*)
   - Auto-vidage après sauvegarde

### Difficultés Rencontrées & Solutions

**Problème 1 : Boutons non réactifs**
- **Cause** : CanExecute pas déclenché automatiquement
- **Solution** : Ajout `((RelayCommand)Command).RaiseCanExecuteChanged()` après changement d'état

**Problème 2 : Note structurée non vidée après sauvegarde**
- **Cause** : Événement manquant pour vider RichTextBox
- **Solution** : Ajout événement `NoteClearedAfterSave` + handler dans MainWindow

**Problème 3 : Zone "Note brute" toujours visible**
- **Cause** : Pas de contrôle de visibilité
- **Solution** : Propriété `IsRawNoteVisible` + binding XAML

### Fichiers Créés
- `MedCompanion/ViewModels/NoteViewModel.cs`

### Fichiers Modifiés
- `MedCompanion/MainWindow.xaml` (bindings + proportions)
- `MedCompanion/MainWindow.xaml.cs` (événements ViewModel)

### Commits Git Effectués
1. `Add NoteViewModel with complete MVVM implementation`
2. `Fix note clearing after save and optimize UX layout`

### Durée Réelle
3 heures

---

## 📄 ÉTAPE 4 : LetterViewModel [À VENIR]

### Objectif
Extraire la gestion des courriers dans un ViewModel dédié

### Fonctionnalités à Migrer
- Sélecteur modèle courrier
- Zone édition courrier
- Commandes (Modifier, Sauvegarder, Imprimer, Supprimer)
- Liste des courriers du patient

### Fichiers à Créer
- `MedCompanion/ViewModels/LetterViewModel.cs`

### Durée Estimée
2-3 heures

---

## 💊 ÉTAPE 5 : OrdonnanceViewModel [TERMINÉE]

### Objectif
Extraire la gestion des ordonnances dans un ViewModel dédié

### Actions Réalisées
1. ✅ Création de `OrdonnanceViewModel.cs` (290+ lignes)
2. ✅ Propriétés migrées :
   - Collection `Ordonnances`
   - `SelectedOrdonnance`

3. ✅ Méthodes implémentées :
   - `LoadOrdonnances()` (chargement liste)
   - `Reset()` (réinitialisation)

4. ✅ Bindings XAML complets :
   - `OrdonnancesList.ItemsSource` → `OrdonnanceViewModel.Ordonnances`
   - `SelectedItem` → `OrdonnanceViewModel.SelectedOrdonnance`

5. ✅ Intégration dans MainWindow via DataContext

### Fichiers Créés
- `MedCompanion/ViewModels/OrdonnanceViewModel.cs`
- `ORDONNANCE_XAML_BINDINGS.md` (documentation)

### Fichiers Modifiés
- `MedCompanion/MainWindow.xaml` (bindings)
- `MedCompanion/MainWindow.xaml.cs` (DataContext)

### Commits Git Effectués
1. `Add OrdonnanceViewModel with MVVM implementation`

### Durée Réelle
1 heure

---

## 🎯 ÉTAPES FUTURES

### Priorité Moyenne
- ChatViewModel (gestion chat IA)
- AttestationViewModel (gestion attestations)
- DocumentViewModel (gestion documents)

### Priorité Basse
- FormulairesViewModel
- OrdonnanceViewModel
- SynthesisViewModel

---

## 📊 Indicateurs de Progression

| Étape | Statut | Progression | Durée |
|-------|--------|-------------|-------|
| 0. PathService Migration | ✅ Terminé | 100% | 4h |
| 1. Base MVVM | ✅ Terminé | 100% | 2h |
| 2. Finaliser Recherche | ✅ Terminé | 100% | 1h |
| 3. NoteViewModel | ✅ Terminé | 100% | 3h |
| 4. LetterViewModel | ⏳ À venir | 0% | 2-3h |
| 5. OrdonnanceViewModel | ✅ Terminé | 100% | 1h |
| 6. Autres ViewModels | ⏳ À venir | 0% | 5-8h |
| **TOTAL** | | **~60%** | **11h / ~20h** |

---

## ⚠️ Points d'Attention

### Risques Identifiés
1. **Conflit ancien/nouveau code** : Bien tester après chaque modification
2. **MainWindow trop volumineux** : Fichier de 5700+ lignes difficile à manipuler
3. **Bindings XAML complexes** : Nécessite attention aux détails

### Bonnes Pratiques Adoptées
✅ Commits Git fréquents  
✅ Tests après chaque modification  
✅ Migration progressive (pas de "big bang")  
✅ Conservation de l'ancien code pendant transition

---

## 📝 Notes Techniques

### Architecture Actuelle
- **Pattern** : MVVM (Model-View-ViewModel)
- **Framework** : WPF (.NET 8)
- **Binding** : Two-way avec UpdateSourceTrigger=PropertyChanged
- **Commands** : ICommand via RelayCommand

### Décisions Architecturales
1. Utiliser `ObservableObject` comme classe de base (pas de framework externe)
2. Garder l'ancien code en parallèle pendant migration
3. Un ViewModel par fonctionnalité majeure
4. Event handlers pour communication ViewModel → View

---

## 🔗 Ressources

### Documentation Créée
- Ce fichier (MVVM_MIGRATION_ROADMAP.md)

### Commits Git
- Voir section "Commits Git Effectués" de chaque étape

---

---

## 🎓 Leçons Apprises

### Problèmes Récurrents avec les Boutons
1. **CanExecute non déclenché** → Toujours appeler `RaiseCanExecuteChanged()`
2. **Binding Command ne fonctionne pas** → Vérifier DataContext et propriété Command
3. **Bouton reste désactivé** → Vérifier condition CanExecute ET forcer refresh

### Bonnes Pratiques Identifiées
✅ Toujours créer événements pour communication ViewModel → View  
✅ Utiliser propriétés booléennes pour contrôler visibilité des boutons  
✅ Tester immédiatement après chaque modification  
✅ Documenter les difficultés et solutions

---

---

## ⚠️ DÉCISION STRATÉGIQUE - 27/10/2025 21:45

### 🛑 Migration MVVM ARRÊTÉE à 57%

**Décision** : Ne PAS continuer la migration MVVM complète

**Raisons** :
1. **Fonctionnalités critiques déjà migrées** (57%)
   - ✅ PatientSearchViewModel (~200 lignes)
   - ✅ NoteViewModel (~550 lignes)
   - ✅ OrdonnanceViewModel (~290 lignes)
   - ✅ **AttestationViewModel (~670 lignes)** ← Découverte : déjà fait !
   
2. **Application stable et fonctionnelle**
   - Code hybride = OK
   - Pas de bugs
   - Utilisable quotidiennement

3. **ROI faible pour continuer**
   - Reste à faire : LetterVM, ChatVM, DocumentVM (~10-15h)
   - Bénéfice : Cosmétique (code plus propre)
   - Risque : Bugs potentiels

**Conclusion** : "If it ain't broke, don't fix it" ✅

---

## 🎯 NOUVELLE STRATÉGIE : Partial Classes

### Objectif
Découper MainWindow.xaml.cs (5473 lignes → 9 fichiers)

### Structure Cible
```
MainWindow.xaml.cs           (~600 lignes - Core)
MainWindow.Patient.cs        (~700 lignes) ✅ FAIT
MainWindow.Documents.cs      (~600 lignes) ⏳ PROCHAIN
MainWindow.Notes.cs          (~800 lignes)
MainWindow.Courriers.cs      (~900 lignes)
MainWindow.Chat.cs           (~700 lignes)
MainWindow.Attestations.cs   (~500 lignes)
MainWindow.Ordonnances.cs    (~400 lignes)
MainWindow.Formulaires.cs    (~400 lignes)
```

### Progression Découpage

| Fichier | Statut | Lignes | Priorité |
|---------|--------|--------|----------|
| MainWindow.xaml.cs | Original | ~4700 | - |
| ✅ MainWindow.Patient.cs | **TERMINÉ** | ~700 | Haute |
| ⏳ MainWindow.Documents.cs | À faire | ~600 | **Prochaine** |
| ⏳ MainWindow.Notes.cs | À faire | ~800 | Haute |
| ⏳ MainWindow.Attestations.cs | À faire | ~500 | Haute |
| ⏳ MainWindow.Courriers.cs | À faire | ~900 | Moyenne |
| ⏳ MainWindow.Chat.cs | À faire | ~700 | Moyenne |
| ⏳ MainWindow.Ordonnances.cs | À faire | ~400 | Basse |
| ⏳ MainWindow.Formulaires.cs | À faire | ~400 | Basse |

### Estimation
- **Temps total** : 7-9 heures
- **Par fichier** : 30-45 min
- **Bénéfice** : Code organisé, maintenable

---

## 🤖 NOUVEAU : Système Gestion Prompts IA - 27/10/2025

### Vue d'ensemble
Système professionnel de gestion des prompts avec assistant IA intégré

### Composants Créés

#### 1. PromptReformulationService
**Fichier** : `MedCompanion/Services/PromptReformulationService.cs`

**Fonctionnalités** :
- Reformulation intelligente via OpenAI
- Conservation des placeholders {{Variables}}
- Prompt système spécialisé en prompt engineering médical
- Gestion des erreurs et timeout

**Méthode principale** :
```csharp
public async Task<(bool success, string reformulated, string? error)> 
    ReformulatePromptAsync(string currentPrompt, string userRequest)
```

#### 2. Architecture 3 Niveaux

**Structure hiérarchique** :
```
🏭 ORIGINAL (jamais modifié)
   └─ Version d'usine préservée
   
📄 DEFAULT (peut évoluer)
   └─ Version de référence active
   
✏️ CUSTOM (expérimentations)
   └─ Tests et améliorations
```

**Modèle enrichi** :
```csharp
public class PromptConfig
{
    public string OriginalPrompt { get; set; }  // ← NOUVEAU
    public string DefaultPrompt { get; set; }
    public string? CustomPrompt { get; set; }
    public bool IsCustomActive { get; set; }
}
```

#### 3. Workflow Amélioration Continue

**Cycle complet** :
1. **Reformuler** → Assistant IA part du DEFAULT
2. **Tester** → Sauvegarder comme CUSTOM + Activer
3. **Valider** → Vérifier les résultats
4. **Promouvoir** → CUSTOM devient nouveau DEFAULT (bouton ⬆️)
5. **Sécurité** → Retour ORIGINAL possible (bouton 🏭)

**Commandes ajoutées** :
- `ReformulateCommand` - Reformulation IA
- `PromoteCommand` - Promotion validée
- `RestoreOriginalCommand` - Reset usine

#### 4. Migration Automatique

**Protection rétrocompatibilité** :
```csharp
private bool MigrateConfigIfNeeded(PromptsConfiguration config)
{
    foreach (var prompt in config.Prompts.Values)
    {
        if (string.IsNullOrEmpty(prompt.OriginalPrompt))
        {
            prompt.OriginalPrompt = prompt.DefaultPrompt;
            // Migration auto transparente
        }
    }
}
```

#### 5. Interface Utilisateur

**Nouvel onglet "💡 Assistant IA"** :
- Zone de texte pour demande de modification
- Exemples de demandes
- Bouton "🔄 Reformuler avec IA"
- Indicateur de progression
- Confirmation avant remplacement

**Boutons ajoutés** :
- **💾 Sauvegarder** (vert)
- **✓ Activer** (bleu)
- **⬆️ Promouvoir** (violet) ← NOUVEAU
- **↺ Restaurer défaut** (jaune)
- **🏭 Original** (rouge) ← NOUVEAU

### Fichiers Créés/Modifiés

**Créés** :
- `MedCompanion/Services/PromptReformulationService.cs` (nouveau)

**Modifiés** :
- `MedCompanion/Models/PromptConfig.cs` (ajout OriginalPrompt)
- `MedCompanion/Services/PromptConfigService.cs` (migration auto + méthodes)
- `MedCompanion/ViewModels/PromptsAnalysisViewModel.cs` (nouvelles commandes)
- `MedCompanion/Dialogs/PromptsAnalysisDialog.xaml` (nouvel onglet + boutons)
- `MedCompanion/Dialogs/PromptsAnalysisDialog.xaml.cs` (injection services)

### Résultat

**Système professionnel complet** :
- ✅ Reformulation IA guidée
- ✅ Amélioration continue validée
- ✅ Protection version d'origine
- ✅ Migration automatique
- ✅ UX intuitive

**Phase 2 (Bibliothèque) reportée** : Phase 1 suffisante pour l'instant

### Durée Développement
- **Total** : ~4 heures
- **Service** : 1h
- **Architecture 3 niveaux** : 1h
- **ViewModel** : 1h
- **UI** : 1h

---

**Dernière mise à jour** : 27/10/2025 21:50  
**Maintenu par** : Migration MVVM Progressive
