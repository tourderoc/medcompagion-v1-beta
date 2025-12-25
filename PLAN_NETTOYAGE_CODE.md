# Plan de Nettoyage MedCompanion V1 Beta

> **Date de création:** 25/12/2024
> **État initial:** 296 warnings, 0 erreurs
> **Objectif:** Code propre, maintenable, sans warnings

---

## Résumé des Warnings Actuels

| Code | Nombre | Description | Priorité |
|------|--------|-------------|----------|
| CS8618 | 176 | Propriété non-nullable non initialisée | Phase 3 |
| CS8619 | 96 | Nullabilité tuple incompatible | Phase 3 |
| CS8625 | 48 | Conversion null → non-nullable | Phase 3 |
| CS8601 | 48 | Assignation null possible | Phase 3 |
| CS8622 | 36 | Nullabilité paramètre delegate | Phase 3 |
| CS8604 | 36 | Argument null possible | Phase 3 |
| CS1998 | 36 | Méthode async sans await | Phase 2 |
| CS8603 | 28 | Retour null possible | Phase 3 |
| CS8602 | 28 | Déréférencement null possible | Phase 3 |
| CS0618 | 16 | API obsolète (PDFsharp) | Phase 2 |
| CS0067 | 12 | Événements jamais utilisés | Phase 1 |
| CS0219 | 4 | Variables locales inutilisées | Phase 1 |
| CS4014 | 4 | Appel async non attendu | Phase 2 |
| CS0108 | 4 | Membre masque membre hérité | Phase 2 |

---

## Phase 1 : Nettoyage Fichiers (Risque: Aucun)

### 1.1 Supprimer fichiers backup
- [ ] `MedCompanion/Dialogs/MCCLibraryDialog.xaml.bak`
- [ ] `MedCompanion/Dialogs/MCCLibraryDialog.xaml.cs.bak`
- [ ] `MedCompanion/Dialogs/MCCLibraryDialog.xaml.cs.bak2`
- [ ] `MedCompanion/Dialogs/MCCLibraryDialog.xaml.cs.original`
- [ ] `MedCompanion/Dialogs/ScannedFormEditorDialog.xaml.backup`
- [ ] `MedCompanion/MainWindow.xaml.backup_chat`
- [ ] `MedCompanion/MainWindow.xaml.bak`

### 1.2 Supprimer fichiers temporaires build
- [ ] `MedCompanion/build_errors_debug.txt`
- [ ] `MedCompanion/build_errors_debug_2.txt`
- [ ] `MedCompanion/build_full_output.txt`
- [ ] `MedCompanion/build_last_error_v4.txt`
- [ ] `MedCompanion/build_last_errors.txt`
- [ ] `MedCompanion/build_last_errors_v2.txt`
- [ ] `MedCompanion/build_last_errors_v3.txt`
- [ ] `MedCompanion/build_log.txt`
- [ ] `MedCompanion/build_output.txt`
- [ ] `build_analysis.txt` (racine)
- [ ] `build_errors.txt` (racine)
- [ ] `build_log.txt` (racine)

### 1.3 Supprimer fichiers obsolètes racine
- [ ] `CODE_A_COPIER_MAINWINDOW.cs`
- [ ] `COMMIT_MESSAGE_CLEANUP.txt`
- [ ] `SYNTHESE_COMPLETE.md`

### 1.4 Validation Phase 1
- [ ] `dotnet build` - Doit compiler sans erreur
- [ ] Commit: `cleanup: suppression fichiers temporaires et backups`

---

## Phase 2 : Code Mort Détecté par Compilateur (Risque: Faible)

### 2.1 Événements jamais utilisés (CS0067)
**Fichier:** `MedCompanion/ViewModels/CourriersViewModel.cs`
- [ ] Supprimer `ShowInExplorerRequested` (ligne ~44)
- [ ] Supprimer `FileOpenRequested` (ligne ~42)
- [ ] Supprimer `LetterListRefreshRequested` (ligne ~38)

### 2.2 Variables locales inutilisées (CS0219)
- [ ] Identifier et supprimer les 4 variables

### 2.3 Méthodes async sans await (CS1998)
**Action:** Retirer `async` ou ajouter `await Task.CompletedTask`
- [ ] `PatientListDialog.xaml.cs` ligne 165
- [ ] `MarkdownToPdfService.cs` ligne 26
- [ ] `LLMServiceFactory.cs` ligne 29
- [ ] Autres (36 au total) - lister après analyse

### 2.4 APIs obsolètes PDFsharp (CS0618)
- [ ] `PDFFormFillerService.cs:593` - Remplacer `ReadOnly` par `Import`
- [ ] `ScannedFormEditorDialog.xaml.cs:1524-1525` - Utiliser `XUnit.FromPoint()`
- [ ] `MarkdownToPdfService.cs:116` - Mettre à jour méthode Image

### 2.5 Validation Phase 2
- [ ] `dotnet build` - Vérifier réduction warnings
- [ ] Test manuel de l'application
- [ ] Commit: `cleanup: suppression code mort et APIs obsolètes`

---

## Phase 3 : Warnings Nullabilité (Risque: Moyen)

### 3.1 Stratégie Choisie
> **À décider:**
> - [ ] Option A: Désactiver nullable globalement, réactiver progressivement
> - [ ] Option B: Corriger fichier par fichier
> - [ ] Option C: Ajouter `= null!` ou `= default!` aux propriétés

### 3.2 Fichiers Prioritaires (plus de warnings)
1. [ ] `Models/MCCModel.cs`
2. [ ] `Models/MCCMatchResult.cs`
3. [ ] `Models/GenerationFeedback.cs`
4. [ ] `Services/MCCMatchingService.cs`
5. [ ] `Services/PromptReformulationService.cs`
6. [ ] `Services/MCCLibraryService.cs`
7. [ ] `Services/LLMGatewayService.cs`
8. [ ] `Services/PDFFormFillerService.cs`

### 3.3 Pattern de Correction
```csharp
// Avant (CS8618)
public string Name { get; set; }

// Après - Option 1: Valeur par défaut
public string Name { get; set; } = string.Empty;

// Après - Option 2: Required (C# 11)
public required string Name { get; set; }

// Après - Option 3: Nullable explicite
public string? Name { get; set; }
```

### 3.4 Validation Phase 3
- [ ] `dotnet build` - Objectif: < 50 warnings
- [ ] Test manuel complet
- [ ] Commit par groupe de fichiers

---

## Phase 4 : Optimisations Optionnelles

### 4.1 Mise à jour .gitignore
- [ ] Ajouter `*.bak`, `*.backup`, `*.original`
- [ ] Ajouter `build_*.txt`

### 4.2 Uniformisation du code
- [ ] Vérifier encodage UTF-8 partout
- [ ] Normaliser les fins de ligne (CRLF Windows)

### 4.3 Documentation
- [ ] Mettre à jour CLAUDE.md avec nouvel état
- [ ] Supprimer ce fichier PLAN_NETTOYAGE_CODE.md une fois terminé

---

## Suivi d'Avancement

| Phase | Statut | Date | Commit |
|-------|--------|------|--------|
| 1.1 Fichiers backup | ⏳ En attente | - | - |
| 1.2 Fichiers build | ⏳ En attente | - | - |
| 1.3 Fichiers racine | ⏳ En attente | - | - |
| 2.1 CS0067 | ⏳ En attente | - | - |
| 2.2 CS0219 | ⏳ En attente | - | - |
| 2.3 CS1998 | ⏳ En attente | - | - |
| 2.4 CS0618 | ⏳ En attente | - | - |
| 3.x Nullabilité | ⏳ En attente | - | - |

---

## Commandes Utiles

```bash
# Voir tous les warnings
dotnet build 2>&1 | grep "warning CS"

# Compter les warnings par type
dotnet build 2>&1 | grep "warning CS" | sed -E 's/.*warning (CS[0-9]+).*/\1/' | sort | uniq -c | sort -rn

# Build propre
dotnet clean && dotnet build
```

---

**Auteur:** Claude Code
**Dernière mise à jour:** 25/12/2024
