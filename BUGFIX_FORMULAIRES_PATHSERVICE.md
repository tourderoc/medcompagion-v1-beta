# 🐛 BUGFIX : Correction chemin formulaires

**Date :** 26/10/2025  
**Problème :** Formulaires utilisant l'ancien code (DirectoryPath direct)  
**Statut :** ✅ CORRIGÉ

---

## 📋 Problème identifié

### Structure incohérente détectée

Comme pour les notes et documents, les formulaires créaient **deux emplacements possibles** :

```
patients/TEST_Test/
├── formulaires/             ← ANCIEN CODE (racine patient) ❌
│   └── MDPH_20251026_*.md
└── 2025/
    ├── notes/
    ├── courriers/
    └── formulaires/         ← Devrait être ici ! ✅
        └── MDPH_20251026_*.md
```

### Cause du bug

**Dans `MainWindow.xaml.cs` - 3 occurrences trouvées :**

1. **`PreremplirFormulaireButton_Click`** (ligne ~4116) - Génération formulaire MDPH
2. **`LoadPatientFormulaires`** (ligne ~4279) - Chargement liste formulaires  
3. **`OuvrirModelePAIButton_Click`** (ligne ~4457) - Copie du modèle PAI

```csharp
// ❌ ANCIEN CODE - Utilisait DirectoryPath direct
var formulairesDir = Path.Combine(_selectedPatient.DirectoryPath, "formulaires");
```

Ce code **ne passait pas par PathService**, créant une structure parallèle à la racine du patient au lieu d'utiliser la structure standardisée dans `2025/formulaires/`.

---

## ✅ Solution appliquée

### Code corrigé (3 occurrences)

```csharp
// ✅ NOUVEAU CODE - Utilise PathService
var formulairesDir = _pathService.GetFormulairesDirectory(_selectedPatient.NomComplet);
```

### Structure après correction

```
patients/TEST_Test/
└── 2025/
    ├── notes/
    ├── courriers/
    ├── documents/
    └── formulaires/         ← TOUT AU MÊME ENDROIT ✅
        ├── MDPH_*.md
        ├── MDPH_*.docx
        ├── PAI_*.pdf
        └── PAI_*.json
```

---

## 🔍 Détails techniques

### PathService.GetFormulairesDirectory()

Cette méthode retourne **automatiquement** :
- `Documents/MedCompanion/patients/DUPONT_Yanis/2025/formulaires/`

Elle garantit :
1. ✅ Structure cohérente (année/formulaires/)
2. ✅ Création automatique des dossiers si nécessaire
3. ✅ Centralisation de la logique de chemins

### Fichiers modifiés

**MedCompanion/MainWindow.xaml.cs** - 3 méthodes corrigées :
1. `PreremplirFormulaireButton_Click` - Ligne ~4116
2. `LoadPatientFormulaires` - Ligne ~4279
3. `OuvrirModelePAIButton_Click` - Ligne ~4457

### Tests de compilation

```bash
✅ Compilation réussie avec 16 avertissements (non critiques)
```

---

## 📊 Impact

### Avant
- Formulaire MDPH généré → `formulaires/` ❌ (racine patient)
- Modèle PAI copié → `formulaires/` ❌ (racine patient)
- Liste formulaires chargée → `formulaires/` ❌ (racine patient)

### Après
- Formulaire MDPH généré → `2025/formulaires/` ✅
- Modèle PAI copié → `2025/formulaires/` ✅
- Liste formulaires chargée → `2025/formulaires/` ✅

---

## 🔄 Migration des données existantes

**Optionnel** : Les anciens formulaires dans `formulaires/` (racine) peuvent rester en place ou être déplacés manuellement.

Les **nouveaux formulaires** seront créés au bon endroit : `2025/formulaires/`

---

## ✅ Validation

- [x] Code corrigé pour utiliser PathService (3 occurrences)
- [x] Compilation réussie
- [x] Structure cohérente garantie
- [x] Documentation créée

---

## 📝 Notes

Ce bug faisait partie d'une **migration plus large vers PathService** pour centraliser toute la gestion des chemins de fichiers patients.

**Bugs similaires corrigés :**
- ✅ Notes : `BUGFIX_NOTES_PATHSERVICE.md`
- ✅ Documents (import) : `BUGFIX_DOCUMENTS_PATHSERVICE.md`
- ✅ Documents (synthèse) : `BUGFIX_DOCUMENTS_SYNTHESE_PATHSERVICE.md`
- ✅ Formulaires : Ce fichier

**Migration PathService maintenant complète pour :**
- Notes ✅
- Courriers ✅
- Documents ✅
- Attestations ✅
- Ordonnances ✅
- Formulaires ✅
- Chat ✅
- Synthèses ✅

---

## 🎯 Résultat final

Toutes les fonctionnalités utilisent maintenant **PathService** de manière cohérente, garantissant une structure de dossiers unifiée et maintenable.
