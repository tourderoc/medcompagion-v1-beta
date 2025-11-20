# Bug Fix : Notes ne s'affichent pas après création

## 🐛 Problème Identifié

**Symptôme** : Lorsqu'un nouveau patient est créé et qu'une note est sauvegardée, elle n'apparaît pas dans la liste des notes de l'interface.

**Cause racine** : `PatientIndexService.GetPatientNotes()` n'utilisait pas le nouveau `PathService` et cherchait les notes au mauvais endroit.

### Détails techniques

**Incohérence de structure de dossiers :**

- **Ancienne structure** (ce que PatientIndexService attendait) :
  ```
  patients/FROMENTIN_David/2025/*.md
  ```

- **Nouvelle structure** (ce que PathService crée) :
  ```
  patients/FROMENTIN_David/notes/*.md
  ```

## ✅ Solution Implémentée

### 1. Modification de `PatientIndexService.cs`

**Changement du constructeur :**
```csharp
// AVANT
public PatientIndexService()

// APRÈS
public PatientIndexService(PathService? pathService = null)
{
    _pathService = pathService;
    // ...
}
```

**Refactorisation de `GetPatientNotes()` :**
```csharp
// Si PathService est disponible, utiliser la nouvelle structure /notes/
if (_pathService != null)
{
    var patientName = entry.NomComplet;
    var notesFolder = _pathService.GetNotesDirectory(patientName);
    if (Directory.Exists(notesFolder))
    {
        foreach (var mdFile in Directory.GetFiles(notesFolder, "*.md"))
        {
            AddNoteToList(mdFile, notes);
        }
    }
}
else
{
    // Fallback : ancienne structure /2025/*.md pour compatibilité
    // ...
}
```

**Extraction de la logique dans `AddNoteToList()` :**
- Méthode helper pour extraire les informations d'une note
- Parsing de la date depuis le nom du fichier (format: `YYYY-MM-DD_HHmm`)
- Extraction d'un aperçu de la note
- Support des deux formats (nouveau et ancien)

### 2. Modification de `MainWindow.xaml.cs`

**Injection de PathService dans PatientIndexService :**
```csharp
// AVANT
_patientIndex = new PatientIndexService();

// APRÈS
_patientIndex = new PatientIndexService(_pathService);
```

## 🔍 Tests de Validation

- ✅ Compilation réussie (15 avertissements mineurs sur les nullables, pas d'erreurs)
- ✅ Compatibilité ascendante maintenue (fallback vers ancienne structure)
- ✅ Architecture cohérente entre PathService, StorageService et PatientIndexService

## 📁 Fichiers Modifiés

1. `MedCompanion/Services/PatientIndexService.cs`
   - Ajout du paramètre `PathService?` dans le constructeur
   - Refactorisation de `GetPatientNotes()`
   - Création de la méthode helper `AddNoteToList()`

2. `MedCompanion/MainWindow.xaml.cs`
   - Injection de `_pathService` dans `PatientIndexService`

3. `BUGFIX_NOTES_PATHSERVICE.md` (ce fichier)

## 🔄 Compatibilité

Le fix maintient la **compatibilité ascendante** grâce au fallback :
- Si PathService est disponible → Cherche dans `/notes/`
- Si PathService n'est pas disponible → Cherche dans `/2025/` (ancien format)

Cela permet une migration progressive sans casser les dossiers patients existants.

## 📅 Date du Fix

26 octobre 2025, 06:56 UTC+1
