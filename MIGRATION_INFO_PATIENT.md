# Migration vers dossier info_patient

## Objectif
Réorganiser les fichiers `patient.json` dans un dossier dédié `info_patient/` pour mieux structurer les données administratives des patients.

## Structure avant migration
```
patients/
  DUPONT_Yanis/
    patient.json          ← À la racine (ancien)
    2025/
      notes/
      courriers/
      ...
```

## Nouvelle structure
```
patients/
  DUPONT_Yanis/
    info_patient/         ← NOUVEAU dossier
      patient.json        ← Données administratives
      [futur: adresse.json, contacts.json, etc.]
    2025/
      notes/
      courriers/
      ...
    synthese/
```

## Modifications effectuées

### 1. PathService.cs
✅ **Nouvelles méthodes ajoutées :**
- `GetInfoPatientDirectory(nomComplet)` - Retourne le chemin vers `info_patient/`
- `GetPatientJsonPath(nomComplet)` - Retourne le chemin complet vers `patient.json`

✅ **Méthode obsolète supprimée :**
- ~~`GetPatientMetadataPath()`~~ - Pointait incorrectement vers `2025/patient.json`

✅ **EnsurePatientStructure() mise à jour :**
- Crée automatiquement le dossier `info_patient/` lors de la création d'un patient

### 2. PatientIndexService.cs
✅ **ScanAsync() :**
- Cherche d'abord dans `info_patient/patient.json` (nouveau)
- Fallback sur `patient.json` à la racine (ancien) pour compatibilité

✅ **Upsert() :**
- Crée/écrit `patient.json` dans `info_patient/` pour les nouveaux patients
- Crée le dossier `info_patient/` si nécessaire

✅ **GetMetadata() :**
- Cherche d'abord dans `info_patient/patient.json`
- Fallback sur l'ancienne structure si non trouvé

## Compatibilité ascendante

Le code est **rétrocompatible** :
- ✅ Les patients existants avec `patient.json` à la racine continuent de fonctionner
- ✅ Les nouveaux patients utilisent automatiquement la nouvelle structure
- ✅ Pas de rupture de fonctionnalité

## Migration des données existantes

### Option 1 : Migration automatique progressive
Les patients existants restent dans l'ancienne structure. Au fur et à mesure que vous modifiez leurs informations, ils seront automatiquement migrés vers la nouvelle structure.

### Option 2 : Script de migration manuelle
Pour migrer immédiatement tous les patients existants, exécutez le script PowerShell fourni :

```powershell
.\migrate-patient-json.ps1
```

Ce script :
1. Scanne tous les dossiers patients
2. Trouve les `patient.json` à la racine
3. Crée le dossier `info_patient/`
4. Déplace le fichier vers `info_patient/patient.json`
5. Affiche un résumé des migrations effectuées

## Avantages de la nouvelle structure

1. **Séparation claire** : Infos administratives vs données cliniques
2. **Évolutif** : Facile d'ajouter `adresse.json`, `contacts.json`, `assurances.json`, etc.
3. **Cohérent** : Même logique que les autres dossiers (`notes/`, `courriers/`, etc.)
4. **Organisé** : Plus facile de gérer les données patient

## Étapes futures possibles

Dans `info_patient/`, on pourra ajouter :
- `adresse.json` - Adresse complète du patient
- `contacts.json` - Contacts d'urgence, médecin traitant
- `assurances.json` - Informations mutuelle/assurance
- `antecedents.json` - Antécédents médicaux structurés
- `allergies.json` - Liste des allergies
- `traitements.json` - Traitements au long cours

## État de la migration

✅ **Phase 1 complétée** : Code adapté avec rétrocompatibilité
⏳ **Phase 2** : Migration optionnelle des données existantes (script fourni)
⏳ **Phase 3** : Ajout de nouveaux fichiers d'info administrative

## Compilation

✅ Build réussi sans erreurs
⚠️ 16 warnings existants (non liés à cette migration)
