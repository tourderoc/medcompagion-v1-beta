# Signature Numérique pour les Courriers

## 📋 Vue d'ensemble

Le système de signature numérique ajoute automatiquement :
1. ✍️ **Image de votre signature manuscrite scannée**
2. ⏰ **Horodatage précis** (date + heure d'émission)
3. 🔐 **Empreinte SHA-256** (garantit l'intégrité du document)

## 🎨 Configuration

### Étape 1 : Préparer votre signature

1. **Scannez votre signature manuscrite**
   - Utilisez un scanner ou une application mobile (CamScanner, Adobe Scan, etc.)
   - Format recommandé : **PNG avec fond transparent**
   - Dimensions recommandées : **400x150 pixels**

2. **Nommez le fichier** : `signature.png`

3. **Placez-le dans** : `MedCompanion/Assets/signature.png`

### Étape 2 : Activer/Désactiver la signature

Dans `AppSettings.cs` :

```csharp
// Signature numérique
public bool EnableDigitalSignature { get; set; } = true;  // true = activé, false = désactivé
public string SignatureImagePath { get; set; } = "Assets/signature.png";
```

## 📄 Rendu dans le Document .docx

Après la signature textuelle habituelle, le système ajoute automatiquement :

```
Fait au Le Pradet, le 18/10/2025

Dr Lassoued Nair
Pédopsychiatre

[IMAGE: Votre signature manuscrite]
Signé numériquement le 18/10/2025 à 20:36:27

────────────────────────────────────────
390 1er DFL Le Pradet 83220
Empreinte SHA-256: a3f2e8b9d1c4f7a2...
```

## 🔐 Sécurité et Traçabilité

### Horodatage
- Format : `JJ/MM/AAAA à HH:MM:SS`
- Généré au moment de l'export .docx
- Non modifiable après génération

### Empreinte SHA-256
- **Hash cryptographique** du contenu complet du .docx
- Permet de **vérifier l'intégrité** du document
- Si le document est modifié, le hash ne correspondra plus

### Vérification d'intégrité

Pour vérifier qu'un document n'a pas été altéré :

1. Calculer le SHA-256 actuel du fichier
2. Comparer avec l'empreinte affichée en pied de page
3. S'ils correspondent → Document intact ✅
4. S'ils diffèrent → Document modifié ⚠️

**Outil de vérification (Windows PowerShell)** :
```powershell
Get-FileHash -Path "chemin\vers\courrier.docx" -Algorithm SHA256
```

## 🎯 Valeur Juridique

### Signature Simple (Configuration actuelle)
- ✅ Valable pour courriers administratifs
- ✅ Courriers scolaires (PAP, aménagements, etc.)
- ✅ Comptes-rendus aux parents
- ✅ Courriers médicaux non-prescriptifs

### Signature Avancée (Non implémentée)
Pour des documents à valeur juridique renforcée (prescriptions médicales, certificats officiels), vous devrez utiliser un certificat numérique (ex: CPS - Carte de Professionnel de Santé).

## ⚙️ Dépannage

### ❌ Signature non affichée

**Vérifier** :
1. Le fichier `signature.png` existe dans `MedCompanion/Assets/`
2. Le paramètre `EnableDigitalSignature = true` dans `AppSettings.cs`
3. Recompiler l'application avec `dotnet build`

### ❌ Empreinte SHA-256 manquante

L'empreinte est ajoutée **après** la création initiale du .docx. Si elle manque :
- Vérifier que `EnableDigitalSignature = true`
- Consulter les logs de débogage pour erreurs éventuelles

### 🔧 Modifier l'emplacement de la signature

Dans `AppSettings.cs`, modifier :
```csharp
public string SignatureImagePath { get; set; } = "Assets/ma_signature.png";
```

## 📝 Notes Techniques

### Taille de l'image signature
- Affichée dans le .docx : **3cm × 1.5cm**
- Alignement : **Droite**
- Position : Après la signature textuelle

### Style de l'horodatage
- Police : **Arial**
- Taille : **9pt**
- Style : **Italique**
- Couleur : **Gris (#666666)**
- Alignement : **Droite**

### Empreinte SHA-256
- Police : **Arial**
- Taille : **7pt**
- Couleur : **Gris clair (#AAAAAA)**
- Alignement : **Centré**
- Position : Pied de page

## ✅ Checklist Finale

Avant d'utiliser la signature numérique :

- [ ] ✅ Fichier `signature.png` créé et placé dans `Assets/`
- [ ] ✅ Paramètre `EnableDigitalSignature = true`
- [ ] ✅ Application recompilée avec `dotnet build`
- [ ] ✅ Test d'export d'un courrier .docx réussi
- [ ] ✅ Vérification visuelle : signature, horodatage et hash présents

## 🚀 Utilisation

Une fois configuré, **rien à faire** ! Le système ajoute automatiquement la signature numérique à chaque export .docx si `EnableDigitalSignature = true`.

Pour désactiver temporairement :
```csharp
public bool EnableDigitalSignature { get; set; } = false;
