# Parsing Doctolib - Documentation

## Vue d'ensemble

Le `ParsingService` permet de détecter automatiquement et extraire les informations patient depuis un bloc de texte copié-collé depuis Doctolib.

## Format attendu

```
Prénom
né(e) NOM
H/F/M, DD/MM/YYYY (âge optionnel)
[texte restant optionnel pour la note brute]
```

## Fonctionnalités

### 1. Détection automatique

Quand vous collez un bloc Doctolib dans le champ "Patient" :

```
David
né(e) FROMENTIN
H, 01/04/2021 (4 ans 6 mois)
```

L'application :
- ✅ Détecte automatiquement le format Doctolib
- ✅ Extrait : Prénom = David, Nom = FROMENTIN, Sexe = H, DOB = 01/04/2021
- ✅ Remplace le contenu du champ par "David FROMENTIN"
- ✅ Ouvre automatiquement le dossier patient
- ✅ Affiche un message de confirmation avec les informations détectées

### 2. Variantes supportées

#### Accents et casse
- `né(e)`, `ne(e)`, `née`, `nee` → tous acceptés
- Insensible à la casse

#### Séparateurs de date
- `01/04/2021` → slash
- `01-04-2021` → tiret
- Les deux formats sont normalisés vers `DD/MM/YYYY`

#### Sexe
- `H` → Homme
- `F` → Femme  
- `M` → Mappé automatiquement vers `H`

#### Espaces
- Tolère les espaces multiples
- Nettoie automatiquement les lignes vides en début/fin

### 3. Texte restant

Si le bloc contient plus de 3 lignes, le texte restant est automatiquement placé dans le champ "Note brute" :

```
David
né(e) FROMENTIN
H, 01/04/2021 (4 ans 6 mois)

Motif de consultation: troubles du sommeil
Observation: difficultés d'endormissement
```

→ La note brute contiendra :
```
Motif de consultation: troubles du sommeil
Observation: difficultés d'endormissement
```

### 4. Cas particuliers

#### Seulement 2 lignes (prénom + nom)
```
Sophie
née BERNARD
```
→ Reconnu : Sophie BERNARD (sans sexe ni date de naissance)

#### Format simple (fallback)
Si le format Doctolib n'est pas détecté, le système utilise le parsing simple :
```
Jean Dupont
```
→ Prénom = Jean, Nom = DUPONT

## Architecture technique

### Classe `ParsingService`

#### Méthode `ParseDoctolibBlock(string input)`

**Étapes :**
1. Nettoie l'entrée (`\r\n` → `\n`, trim, supprime lignes vides)
2. Extrait les 3 premières lignes
3. Applique les regex :
   - L1 : Prénom (capitalisation)
   - L2 : `^\s*n[eé]\(e\)\s+(.+?)\s*$` → Nom (uppercase)
   - L3 : `^\s*([HFM])\s*,\s*(\d{2}[/-]\d{2}[/-]\d{4})(?:\s*\(([^)]+)\))?` → Sexe, DOB, Âge
4. Normalise les données (M→H, -→/)
5. Collecte le texte restant pour la note brute

**Retour :** `DoctolibParseResult`
```csharp
public class DoctolibParseResult
{
    public bool Success { get; set; }
    public string? Prenom { get; set; }
    public string? Nom { get; set; }
    public string? Sex { get; set; }
    public string? Dob { get; set; }
    public string? AgeText { get; set; }
    public string? RemainingText { get; set; }
}
```

### Intégration dans `MainWindow`

Le hook `NomCompletTextBox_TextChanged` :
1. Écoute les changements dans le champ Patient
2. Tente le parsing Doctolib
3. Si succès :
   - Stocke les données patient (`_patientPrenom`, `_patientNom`, `_patientSex`, `_patientDob`)
   - Remplace le contenu par "Prénom Nom"
   - Transfère le texte restant vers "Note brute"
   - Affiche un message de confirmation
   - Ouvre le dossier patient automatiquement

## Tests d'acceptation

### ✅ Test 1 : Bloc exact
**Entrée :**
```
David
né(e) FROMENTIN
H, 01/04/2021 (4 ans 6 mois)
```
**Résultat :** David FROMENTIN, DOB = 01/04/2021, Sexe = H

### ✅ Test 2 : Variante avec tirets
**Entrée :**
```
Jade
nee MARTIN
F, 11-02-2015 (9 ans)
```
**Résultat :** Jade MARTIN, DOB = 11/02/2015, Sexe = F

### ✅ Test 3 : M → H
**Entrée :**
```
Marc
né(e) DUPONT
M, 15/03/2018
```
**Résultat :** Sexe = H (mappé)

### ✅ Test 4 : 2 lignes seulement
**Entrée :**
```
Sophie
née BERNARD
```
**Résultat :** Sophie BERNARD (sans sexe/DOB)

### ✅ Test 5 : Avec note brute
**Entrée :**
```
David
né(e) FROMENTIN
H, 01/04/2021 (4 ans 6 mois)

Motif de consultation: troubles du sommeil
```
**Résultat :** Note brute remplie automatiquement

### ✅ Test 6 : Espaces multiples
**Entrée :**
```
  Marie-Claire  
  née    LAURENT  
  F  ,  23/08/2019  ( 5 ans )
```
**Résultat :** Marie-Claire LAURENT, nettoyé correctement

## Utilisation

1. Copiez le bloc patient depuis Doctolib
2. Collez-le dans le champ "Patient" de MedCompanion
3. L'application détecte automatiquement le format et :
   - Extrait les informations
   - Ouvre le dossier patient
   - Remplit la note brute si présente
   - Affiche un message de confirmation

C'est aussi simple que ça ! 🎉
