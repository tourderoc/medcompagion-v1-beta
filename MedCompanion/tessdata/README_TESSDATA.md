# Installation des données Tesseract pour OCR Doctolib

## Fichier requis : `fra.traineddata`

Pour que l'import Doctolib fonctionne, vous devez télécharger le fichier de données linguistiques françaises de Tesseract.

### Étapes d'installation

1. **Télécharger fra.traineddata**
   - URL : https://github.com/tesseract-ocr/tessdata/raw/main/fra.traineddata
   - Taille : ~15 MB

2. **Placer le fichier dans AppData (RECOMMANDÉ)**

   **Chemin Windows :**
   ```
   C:\Users\[VotreNom]\AppData\Roaming\MedCompanion\tessdata\fra.traineddata
   ```

   **Accès rapide :**
   - Appuyez sur `Windows + R`
   - Tapez : `%APPDATA%\MedCompanion\tessdata`
   - Copiez `fra.traineddata` dans ce dossier

   Le dossier `tessdata` sera créé automatiquement au premier lancement de MedCompanion.

3. **Vérifier l'installation**
   - Dans MedCompanion, ouvrez "Infos patient"
   - Cliquez sur "Importer depuis 2 captures d'écran"
   - Si le fichier est bien installé, vous pourrez sélectionner des images
   - Si le fichier est manquant, un message d'erreur explicite s'affichera avec le chemin exact

### Structure finale attendue

```
C:\Users\[VotreNom]\AppData\Roaming\
└── MedCompanion\
    └── tessdata\
        └── fra.traineddata    ← FICHIER À TÉLÉCHARGER
```

### Confidentialité et sécurité

✅ **OCR 100% local** - Aucune donnée médicale n'est envoyée vers le cloud
✅ **Conformité RGPD** - Toutes les données restent sur votre machine
✅ **Tesseract open-source** - Logiciel libre maintenu par Google

### Fonctionnalités de l'import Doctolib

Une fois `fra.traineddata` installé, vous pourrez :

- Importer 1 ou 2 captures d'écran Doctolib (PNG/JPG)
- Extraction automatique des données via OCR :
  - Numéro de sécurité sociale (NIR)
  - Adresse complète (rue, code postal, ville)
  - Lieu de naissance
  - Téléphone et email (si présents)
- Code couleur de confiance :
  - 🟩 **Vert** : Confiance élevée (>80%)
  - 🟧 **Orange** : À vérifier (50-80%)
  - 🟥 **Rouge** : Incertain (<50%)
- Validation manuelle obligatoire avant sauvegarde

### Dépannage

**Erreur : "Fichier de données Tesseract manquant"**
→ Vérifiez que `fra.traineddata` est bien dans le dossier `tessdata/`

**Erreur : "Unable to load library 'leptonica-1.82.0'"**
→ Redémarrez Visual Studio et l'application

**Mauvaise qualité OCR**
→ Assurez-vous que les captures d'écran sont nettes et à résolution suffisante (800×600 minimum)

### Alternatives (non recommandées pour données médicales)

D'autres modèles Tesseract existent mais ne sont PAS recommandés pour des raisons de confidentialité :
- ❌ API cloud (Google Vision, Azure OCR, etc.) - **INTERDIT pour données médicales**
- ❌ GPT-4 Vision - Nécessite consentement explicite utilisateur

**Toujours privilégier l'OCR local avec Tesseract pour les données de santé.**
