# MedCompanion

Application WPF .NET 8 pour structurer des notes cliniques en pédopsychiatrie à l'aide de l'IA OpenAI.

## Configuration

### Prérequis
- .NET 8 SDK
- Une clé API OpenAI

### Configuration de la clé API

Définissez la variable d'environnement `OPENAI_API_KEY` avec votre clé API :

**Windows (CMD):**
```cmd
setx OPENAI_API_KEY "sk-votre-cle-ici"
```

**Après avoir défini la clé, redémarrez votre terminal/éditeur pour que la variable soit prise en compte.**

## Utilisation

### Lancer l'application

Depuis le répertoire `MedCompanion` :

```cmd
dotnet run
```

### Fonctionnalités

1. **Nom complet** : Saisissez le prénom et nom du patient (ex: Yanis Dupont)
2. **Note brute** : Entrez vos notes cliniques brutes (multi-ligne, texte libre)
3. **Bouton Structurer** : Lance la structuration de la note via l'API OpenAI
4. **Note structurée** : Affiche le compte-rendu clinique structuré et formaté
5. **Valider & Sauvegarder** : Enregistre la note structurée en Markdown avec métadonnées YAML
6. **Ouvrir le dossier** : Ouvre le dossier du patient dans l'Explorateur Windows
7. **Charger 3 dernières notes** : Affiche les 3 dernières notes du patient pour contexte
8. **💬 Barre IA (chat avec contexte)** : Posez des questions sur le patient avec contexte intelligent
   - Contexte automatique : NOTE FONDATRICE (≤ 500 mots) + 2 DERNIÈRES NOTES (≤ 220 mots chacune)
   - Déduplication automatique des notes
   - Indication du contexte utilisé en pied de réponse
   - Raccourci : Ctrl+Enter pour envoyer
9. **Barre de statut** : Messages d'état (clé manquante, traitement en cours, erreurs, etc.)

### Caractéristiques

**Structuration de notes :**
- Modèle utilisé : `gpt-4o-mini` (configurable dans `OpenAIService.cs`)
- Temperature : 0.2 (pour des résultats cohérents)
- Max tokens : 1200
- System prompt : "Tu es pédopsychiatre."

**Chat avec contexte intelligent :**
- Temperature : 0.3 (légèrement plus créatif pour les réponses)
- Max tokens : 1500
- System prompt : Instructions détaillées pour réponses cliniques structurées
- Contexte automatique basé sur :
  - **NOTE FONDATRICE** : La première note du patient (plus ancienne), tronquée à 500 mots
  - **DERNIÈRES NOTES** : Les 2 notes les plus récentes, tronquées à 220 mots chacune
  - Déduplication automatique (évite les doublons entre fondatrice et dernières)
  
**Gestion des erreurs :** 401, 429, 500 avec messages explicites

### Stockage des notes

- **Emplacement** : `%USERPROFILE%\Documents\MedCompanion\patients\`
- **Arborescence** : `patients\{Nom_Prenom}\{YYYY}\{YYYY-MM-DD_HHmm}_{Nom_Prenom}.md`
- **Format** : Markdown avec en-tête YAML contenant les métadonnées
- **Versioning** : Si un fichier existe déjà, suffixes automatiques (-v2, -v3, etc.)
- **Exemple d'en-tête YAML** :
  ```yaml
  ---
  patient: "Yanis Dupont"
  date: "2025-01-12T14:30"
  source: "MedCompanion"
  type: "note-structuree"
  version: "1"
  ---
  ```

## Structure du projet

```
MedCompanion/
├── MainWindow.xaml          # Interface utilisateur
├── MainWindow.xaml.cs       # Logique de l'UI et gestion des événements
├── OpenAIService.cs         # Service d'appel à l'API OpenAI
├── StorageService.cs        # Service de sauvegarde des notes
├── ContextLoader.cs         # Service de chargement des notes précédentes
├── App.xaml                 # Configuration de l'application
├── MedCompanion.csproj      # Configuration du projet
└── README.md                # Documentation
```

## Sécurité et confidentialité

- La clé API est lue depuis les variables d'environnement (jamais en dur dans le code)
- Les notes sont stockées localement sur votre machine uniquement
- Aucune transmission des données sauf vers l'API OpenAI pour la structuration
- Organisation par patient et par année pour faciliter la gestion
- Les dossiers sont créés automatiquement selon les besoins

## Workflow typique

### Workflow principal : Création de note
1. Saisir le nom complet du patient (ex: "Yanis Dupont")
2. Saisir la note brute (observations cliniques)
3. Cliquer sur **Structurer** → la note est structurée par l'IA
4. Cliquer sur **Valider & Sauvegarder** → la note est enregistrée en Markdown
5. Utiliser **Ouvrir le dossier** pour accéder aux notes sauvegardées

### Workflow consultation : Chat IA avec contexte
1. Saisir le nom complet du patient
2. (Optionnel) Cliquer sur **Charger 3 dernières notes** pour voir l'historique en un coup d'œil
3. Utiliser la **💬 Barre IA** pour poser des questions :
   - "Fais une analyse du cas"
   - "Quels sont les points de vigilance ?"
   - "Proposition de feuille de route thérapeutique"
   - etc.
4. L'IA répond en s'appuyant automatiquement sur :
   - La note fondatrice (première note du patient)
   - Les 2 dernières notes
   - Le contexte affiché en pied de réponse

### Contexte intelligent
- **Aucune note** : L'IA le signale et propose de créer une première note
- **1 note** : Sert de note fondatrice uniquement
- **2 notes** : Note fondatrice + 1 dernière note
- **3+ notes** : Note fondatrice + 2 dernières notes (sans doublon)
- **Changement de patient** : Le contexte s'actualise automatiquement
