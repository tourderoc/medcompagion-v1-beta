# 🐛 BUGFIX : Correction chemin synthèses de documents

**Date :** 26/10/2025  
**Problème :** Double structure de dossiers documents (ancien + nouveau)  
**Statut :** ✅ CORRIGÉ

---

## 📋 Problème identifié

### Structure incohérente détectée

L'utilisateur a constaté **deux dossiers `documents/` différents** :

```
patients/TEST_Test/
├── documents/               ← ANCIEN CODE (racine patient)
│   └── syntheses/          ← Synthèses stockées ici ❌
│       └── 2025-10-20_bilan-orthophonique-Dupon...md
└── 2025/
    ├── notes/
    ├── courriers/
    └── documents/          ← NOUVEAU CODE (dans année)
        ├── bilans/         ← PDFs importés ici ✅
        └── syntheses/      ← Devrait être ici !
```

### Cause du bug

**Dans `MainWindow.xaml.cs` ligne ~4168 (`SaveSynthesisBtn_Click`) :**

```csharp
// ❌ ANCIEN CODE - Utilisait DirectoryPath direct
var documentsDir = Path.Combine(_selectedPatient.DirectoryPath, "documents");
var syntheseDir = Path.Combine(documentsDir, "syntheses");
```

Ce code **ne passait pas par PathService**, créant une structure parallèle à la racine du patient au lieu d'utiliser la structure standardisée dans `2025/documents/`.

---

## ✅ Solution appliquée

### Code corrigé

```csharp
// ✅ NOUVEAU CODE - Utilise PathService
var documentsDir = _pathService.GetDocumentsDirectory(_selectedPatient.NomComplet);
var syntheseDir = Path.Combine(documentsDir, "syntheses");
```

### Structure après correction

```
patients/TEST_Test/
└── 2025/
    ├── notes/
    ├── courriers/
    └── documents/          ← TOUT AU MÊME ENDROIT ✅
        ├── bilans/         ← PDFs importés
        ├── syntheses/      ← Synthèses de documents ✅
        ├── courriers/
        └── autres/
```

---

## 🔍 Détails techniques

### PathService.GetDocumentsDirectory()

Cette méthode retourne **automatiquement** :
- `Documents/MedCompanion/patients/DUPONT_Yanis/2025/documents/`

Elle garantit :
1. ✅ Structure cohérente (année/documents/)
2. ✅ Création automatique des dossiers si nécessaire
3. ✅ Centralisation de la logique de chemins

### Fichiers modifiés

- **MedCompanion/MainWindow.xaml.cs** : Ligne ~4168, méthode `SaveSynthesisBtn_Click`

### Tests de compilation

```bash
✅ Compilation réussie avec 16 avertissements (non critiques)
```

---

## 📊 Impact

### Avant
- Import document → `2025/documents/bilans/` ✅
- Synthèse document → `documents/syntheses/` ❌ (racine patient)

### Après
- Import document → `2025/documents/bilans/` ✅
- Synthèse document → `2025/documents/syntheses/` ✅

---

## 🔄 Migration des données existantes

**Optionnel** : Les anciennes synthèses dans `documents/syntheses/` (racine) peuvent rester en place ou être déplacées manuellement.

Les **nouvelles synthèses** seront créées au bon endroit : `2025/documents/syntheses/`

---

## ✅ Validation

- [x] Code corrigé pour utiliser PathService
- [x] Compilation réussie
- [x] Structure cohérente garantie
- [x] Documentation créée

---

## 📝 Notes

Ce bug faisait partie d'une **migration plus large vers PathService** pour centraliser toute la gestion des chemins de fichiers patients.

**Bugs similaires corrigés :**
- ✅ Notes : `BUGFIX_NOTES_PATHSERVICE.md`
- ✅ Documents (import) : `BUGFIX_DOCUMENTS_PATHSERVICE.md`
- ✅ Documents (synthèse) : Ce fichier
- ✅ Documents (suppression cascade synthèse) : Ajouté 29/10/2025

**Prochaines étapes :**
- Continuer la migration MVVM (ViewModels pour autres fonctionnalités)
- Vérifier tous les autres usages de `_selectedPatient.DirectoryPath`

---

## 🔄 Mise à jour 29/10/2025

### Amélioration : Suppression en cascade

**Problème :** Lors de la suppression d'un document, sa synthèse associée restait orpheline dans `syntheses_documents/`.

**Solution :** Modification de `DocumentService.DeleteDocumentAsync()` pour supprimer automatiquement la synthèse associée au document supprimé (sans toucher aux autres synthèses).

**Code ajouté :**
```csharp
// 1. Vérifier et supprimer la synthèse associée si elle existe
var (synthesisExists, synthesisPath) = GetExistingSynthesis(document, nomComplet);
if (synthesisExists && !string.IsNullOrEmpty(synthesisPath) && File.Exists(synthesisPath))
{
    File.Delete(synthesisPath);
}
```

### Renommage du dossier

Le dossier `syntheses/` a été renommé en `syntheses_documents/` pour éviter toute confusion avec le dossier `synthese/` (synthèse patient à la racine).

### Architecture MainWindow

**MainWindow.xaml.cs** est maintenant réparti en **3 fichiers partiels** pour améliorer la maintenabilité :
- `MainWindow.xaml.cs` : Code principal, initialisation, handlers généraux
- `MainWindow.Patient.cs` : Gestion des patients (chargement, recherche, création)
- `MainWindow.Documents.cs` : Gestion des documents (import, synthèse, suppression)

Cette séparation facilite la navigation et la maintenance du code.
