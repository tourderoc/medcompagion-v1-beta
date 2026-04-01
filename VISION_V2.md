# Vision MedCompanion V2

> **Date de création :** 25/12/2024
> **Dernière mise à jour :** 28/01/2026
> **Statut :** UI Focus Med implémentée + Conception Mémoire validée + **Mode Consultation en conception**

---

## Philosophie

```
V1 = Console (stable, complète, post-consultation)
V2 = Focus (épuré, réflexif, avec Med)
```

La V1 reste intacte. La V2 introduit une **posture de travail** différente, pas une application différente.

**Évolution (01/2026)** : Med devient un **assistant proactif** (collègue qui anticipe, propose, intervient), pas seulement un outil cognitif réactif.

---

## Mode Consultation — Définition

### Ce que c'est

Un espace de réflexion avec Med, utilisable :
- Avant une consultation (préparer)
- Après une consultation (reformuler, synthétiser)
- Entre deux patients (analyser, réfléchir)

### Ce que ce n'est pas

- Un chat générique
- Une extension de la V1
- Un outil de documentation

---

## 🏥 Mode Consultation — Interface Dossier (V3)

> **Date :** 28/01/2026
> **Statut :** Conception validée, implémentation à venir

### Philosophie

```
Métaphore : Le patient en face, le dossier papier posé à côté.
L'écran transpose ce geste clinique réel, pas une interface "innovante".
```

**Principes fondateurs :**
- Le clinicien ne tourne jamais le dos au patient pour chercher une information
- Le dossier est un outil périphérique, pas le centre de l'interaction
- En pédopsychiatrie : l'enfant observe tout, le parent capte si on "consulte l'écran" vs "on est avec eux"

---

### Architecture de l'Interface

#### Layout Principal (état Consultation : 67% / 33%)

```
┌──────────────────────────────────────────────────┬────────────────────────────┐
│           ESPACE DE TRAVAIL (67%)                │   DOSSIER PATIENT (33%)    │
│                                                  │                            │
│  ┌──────────────────────────────┬─────────────┐  │  ┌──────────────────────┐  │
│  │                              │             │  │  │   DOSSIER PATIENT    │  │
│  │    Zone Clinicien            │    Med      │  │  │   DUPONT Martin      │  │
│  │         (~50%)               │   (~17%)    │  │  │                      │  │
│  │                              │             │  │  │  ┌──┬──┬──┬──┬──┬──┐ │  │
│  │  • Notes consultation        │  • Aide     │  │  │  │📋│📁│📅│🎯│📊│📄│ │  │
│  │  • Écriture libre            │  • Sugg.    │  │  │  └──┴──┴──┴──┴──┴──┘ │  │
│  │  • Actions rapides           │  • Rech.    │  │  │                      │  │
│  │  • Dictée                    │  • Check    │  │  │  ┌──────────────────┐│  │
│  │                              │             │  │  │  │                  ││  │
│  │                              │  [🔇][💬][📋]│  │  │  │ Contenu section  ││  │
│  │                              │             │  │  │  │                  ││  │
│  │                              │  ─────────  │  │  │  └──────────────────┘│  │
│  │                              │  Suggestions│  │  │                      │  │
│  │                              │  ...        │  │  └──────────────────────┘  │
│  └──────────────────────────────┴─────────────┘  │                            │
│                                                  │                            │
│              [◀═══════════╬═══════════▶]         │                            │
└──────────────────────────────────────────────────┴────────────────────────────┘
```

#### Division Verticale Espace Travail

| Zone | Proportion | Contenu |
|------|------------|---------|
| **Clinicien** | 3/4 | Notes, écriture libre, actions rapides, dictée |
| **Med** | 1/4 | Suggestions contextuelles, aide, recherche, checklist |

---

### Les 3 États d'Écran

| État | Répartition | Usage Clinique | Raccourci |
|------|-------------|----------------|-----------|
| **Focus Travail** | 100% Travail / 0% Dossier | Entretien actif, écriture immersive | `F1` |
| **Consultation** | 67% Travail / 33% Dossier | Vérification d'info, rédaction avec référence | `F2` |
| **Focus Dossier** | 0% Travail / 100% Dossier | Revue complète, préparation avant consultation | `F3` |

**Proportions détaillées (état Consultation) :**
- Zone Clinicien : ~50% de l'écran (3/4 de l'espace travail)
- Zone Med : ~17% de l'écran (1/4 de l'espace travail)
- Dossier Patient : ~33% de l'écran

#### Transitions

```
    ┌─────────────┐       ┌─────────────┐       ┌─────────────┐
    │   TRAVAIL   │ ←───→ │ CONSULTATION│ ←───→ │   DOSSIER   │
    │    100%     │       │   67% / 33% │       │    100%     │
    └─────────────┘       └─────────────┘       └─────────────┘
         F1                    F2                    F3
                      Double-clic = retour 67/33
```

---

### Mécanisme de Switch

#### Option A : Poignée de Glissement (recommandée)

```
              ←  ║  →
                 ║
         ◀═══════╬═══════▶   (glisser ou clic pour basculer)
                 ║
              ←  ║  →
```

**Comportement :**
- Glissement continu avec "snap" aux 3 positions (0%, 67%, 100%)
- Double-clic = retour position 67/33
- Animation fluide < 200ms

#### Option B : Barre d'Icônes

```
┌────────────────────────────────────────┐
│  [▣▣ ]    [▣▣│▣]    [ ▣▣]              │
│   100%     67/33     100%              │
│  travail            dossier            │
└────────────────────────────────────────┘
```

---

### La Zone Med (1/4 Vertical)

#### Modes de Med en Consultation

| Mode | Icône | Comportement |
|------|-------|--------------|
| **Silencieux** | 🔇 | Présent mais attend qu'on l'appelle |
| **Suggestions** | 💬 | Propose activement (interactions, alertes) |
| **Checklist** | 📋 | Affiche les points à couvrir pour cette consultation |

#### Contenu Contextuel

```
┌───────────────────────────┐
│  🤖 MED                   │
├───────────────────────────┤
│  Mode: 💬 Suggestions     │
│  [🔇] [💬] [📋]           │
├───────────────────────────┤
│                           │
│  💊 Interactions détectées│
│  • Fluoxétine + ...       │
│                           │
│  📝 Points à évoquer      │
│  • Suivi scolaire         │
│  • RDV bilan prévu        │
│                           │
│  🎯 Suggestion            │
│  "Dernier RDV: fatigue    │
│   mentionnée. Demander    │
│   l'évolution ?"          │
│                           │
├───────────────────────────┤
│  [Réduire ▼]              │
└───────────────────────────┘
```

**Rétractable :** Un clic et Med se réduit à une barre latérale fine (icône seule).

---

### Le Dossier Patient (Côté Droit)

#### Référence Visuelle

Inspiration : le classique dossier médical français en carton bleu clair.

**Palette de couleurs :**
| Élément | Couleur | Hex |
|---------|---------|-----|
| Fond couverture | Bleu très clair | `#E8F4F8` |
| Bordure/Titre | Bleu médical | `#4A90A4` |
| Texte principal | Gris foncé | `#2C3E50` |
| Intercalaire actif | Bleu clair | `#D4E9F0` |
| Intercalaire inactif | Bleu pâle | `#F0F7FA` |

**Principes visuels :**
- Pas de skeuomorphisme (pas de texture papier, pas d'ombres réalistes)
- Sobriété du dossier médical réel
- Légère ombre portée pour donner l'impression de "posé à côté"
- Coins légèrement arrondis (4px) comme un dossier cartonné

#### Structure des Intercalaires

| Intercalaire | Icône | Contenu |
|--------------|-------|---------|
| **Synthèse** | 📋 | Vue condensée, alertes, points clés |
| **Administratif** | 📁 | Coordonnées, correspondants, école |
| **Consultations** | 📅 | Historique des notes de consultation |
| **Projet Thérapeutique** | 🎯 | Objectifs, stratégies, suivis |
| **Bilans** | 📊 | Tests, évaluations, résultats |
| **Documents** | 📄 | Courriers, attestations, ordonnances |

#### Interaction avec les Intercalaires

```
┌─────────────────────────────────────┐
│  📁 DOSSIER : DUPONT Martin         │
│  ─────────────────────────────────  │
│                                     │
│  ┌───┬───┬───┬───┬───┬───┐         │
│  │ 📋│ 📁│ 📅│ 🎯│ 📊│ 📄│         │
│  └───┴───┴───┴───┴───┴───┘         │
│        ▲                           │
│        │ Intercalaire actif        │
│  ┌─────────────────────────────┐   │
│  │                             │   │
│  │   Contenu de la section     │   │
│  │                             │   │
│  │   • Info 1                  │   │
│  │   • Info 2                  │   │
│  │   • Info 3                  │   │
│  │                             │   │
│  └─────────────────────────────┘   │
│                                     │
└─────────────────────────────────────┘
```

#### Comportements

| Action | Résultat |
|--------|----------|
| Clic intercalaire | Ouvre la section, autres en retrait |
| Mémoire d'état | Si en 100% travail puis retour 67/33 → reste sur la même section |
| Indicateur discret | Petit point sur l'intercalaire si info importante (comme coin corné) |
| Recherche rapide | `Ctrl+F` cherche dans le dossier ouvert |

---

### Plan d'Implémentation

#### Phase 0 — Préparation (1 semaine)

- [ ] Créer le dossier `Views/Consultation/` si inexistant
- [ ] Définir les interfaces/modèles pour le dossier patient consultation
- [ ] Documenter les transitions d'état (enum `ConsultationViewState`)

#### Phase 1 — Layout Adaptatif (2 semaines)

**Fichiers à créer :**
```
MedCompanion/Views/Consultation/
├── ConsultationModeControl.xaml       # Layout principal adaptatif
├── ConsultationModeControl.xaml.cs
├── WorkspacePanel.xaml                # Zone travail (3/4 clinicien + 1/4 med)
├── WorkspacePanel.xaml.cs
├── DossierPanel.xaml                  # Dossier patient (intercalaires)
├── DossierPanel.xaml.cs
├── MedAssistantPanel.xaml             # Zone Med (1/4 vertical)
└── MedAssistantPanel.xaml.cs
```

**Tâches :**
- [ ] Implémenter le `Grid` principal avec `GridSplitter`
- [ ] Créer les 3 états d'écran avec transitions animées
- [ ] Implémenter la poignée de glissement avec snap
- [ ] Ajouter les raccourcis clavier F1/F2/F3
- [ ] Tester les transitions fluides (< 200ms)

#### Phase 2 — Dossier Patient (2 semaines)

**Fichiers à créer :**
```
MedCompanion/Views/Consultation/Dossier/
├── DossierCoverControl.xaml           # Couverture du dossier
├── SyntheseTab.xaml                   # Onglet Synthèse
├── AdminTab.xaml                      # Onglet Administratif
├── ConsultationsTab.xaml              # Onglet Consultations
├── ProjetTab.xaml                     # Onglet Projet thérapeutique
├── BilansTab.xaml                     # Onglet Bilans
└── DocumentsTab.xaml                  # Onglet Documents
```

**Tâches :**
- [ ] Implémenter le `TabControl` avec style intercalaires
- [ ] Créer chaque onglet avec son contenu spécifique
- [ ] Connecter aux données patient existantes (réutiliser ViewModels V1)
- [ ] Ajouter les indicateurs discrets (points sur intercalaires)
- [ ] Implémenter la mémoire d'état (dernière section ouverte)

#### Phase 3 — Zone Travail Clinicien (2 semaines)

**Fichiers à créer :**
```
MedCompanion/Views/Consultation/Workspace/
├── ClinicianWorkspace.xaml            # Zone 3/4 clinicien
├── ClinicianWorkspace.xaml.cs
├── NoteEditor.xaml                    # Éditeur de notes consultation
├── QuickActions.xaml                  # Actions rapides
└── DictationPanel.xaml                # Zone dictée (réutiliser Voice)
```

**Tâches :**
- [ ] Créer l'éditeur de notes consultation
- [ ] Implémenter les actions rapides (nouveau RDV, ordonnance, etc.)
- [ ] Intégrer la dictée vocale existante (HandyVoiceInputService)
- [ ] Auto-save des notes en cours de frappe

#### Phase 4 — Zone Med Assistant (1 semaine)

**Tâches :**
- [ ] Adapter `MedAssistantPanel` pour le contexte consultation
- [ ] Implémenter les 3 modes (Silencieux/Suggestions/Checklist)
- [ ] Connecter au contexte patient pour suggestions pertinentes
- [ ] Ajouter le bouton réduire/agrandir
- [ ] Intégrer les alertes interactions médicamenteuses (si disponible)

#### Phase 5 — Intégration MainWindow (1 semaine)

**Modifications MainWindow :**
- [ ] Ajouter un 3ème mode au toggle existant (Console / Focus Med / **Consultation**)
- [ ] Ou : Bouton dédié "Entrer en consultation" sur la carte patient
- [ ] Gérer la visibilité des différents conteneurs
- [ ] Persister l'état de l'écran entre sessions

#### Phase 6 — Polish et Tests (1 semaine)

- [ ] Animations de transition fluides
- [ ] Tests avec données réelles
- [ ] Feedback utilisateur
- [ ] Documentation

---

### ViewModels

```
MedCompanion/ViewModels/
├── ConsultationModeViewModel.cs       # État global du mode consultation
├── WorkspaceViewModel.cs              # Notes, actions, état clinicien
├── DossierViewModel.cs                # Dossier patient en consultation
└── MedConsultantViewModel.cs          # Med en mode consultation
```

**ConsultationModeViewModel.cs :**
```csharp
public enum ConsultationViewState
{
    FocusTravail,    // 100% travail, 0% dossier
    Consultation,    // 50/50
    FocusDossier     // 0% travail, 100% dossier
}

public class ConsultationModeViewModel : INotifyPropertyChanged
{
    public ConsultationViewState CurrentState { get; set; }
    public double WorkspaceWidth { get; set; }     // 0, 0.5, 1
    public double DossierWidth { get; set; }       // 0, 0.5, 1
    public bool IsMedExpanded { get; set; }
    public MedConsultationMode MedMode { get; set; }
    public string ActiveDossierTab { get; set; }

    public ICommand SwitchToFocusTravailCommand { get; }
    public ICommand SwitchToConsultationCommand { get; }
    public ICommand SwitchToDossierCommand { get; }
}
```

---

### Points de Vigilance

| Risque | Mitigation |
|--------|------------|
| **Skeuomorphisme excessif** | Pas de textures papier, pas de bruits de pages. Garder sobre. |
| **Rigidité spatiale** | Permettre des vues transversales (timeline) en overlay |
| **Surcharge d'intercalaires** | Max 6 onglets. Au-delà, la métaphore s'effondre. |
| **Sections vides anxiogènes** | Message "Aucune donnée" discret, pas de formulaire vide |
| **Transitions saccadées** | Animation < 200ms, fluide, sans effet "wow" |
| **Perte de contexte** | Mémoire d'état persistante (dernière section, dernier état) |

---

### Critères de Succès

- [ ] Basculer entre les 3 états en un geste (clic/raccourci)
- [ ] Savoir toujours où est le dossier (même masqué)
- [ ] Accéder à n'importe quelle info patient en < 2 clics
- [ ] Écrire une note sans quitter le regard du patient (100% travail)
- [ ] Med suggère sans interrompre le flux clinique
- [ ] Transition aussi naturelle que poser/reprendre un dossier papier

---

## Principes V2.0

| Principe | Application |
|----------|-------------|
| Med proactif | Med peut proposer des actions importantes |
| Mémoire légère | Historique des conversations archivées (session + persistance légère) |
| Persistance sur demande | "Résume" → copier/exporter |
| Med incarné | Avatar, micro, présence centrale |
| Réversibilité | Toggle instantané Console ↔ Focus Med |

---

## Interface Focus Med

### Toggle Console/Focus Med

- Position : barre supérieure, après le bouton "Prompts"
- Style : switch moderne on/off avec labels "Console / Focus Med"
- Visible dans tous les modes
- Indicateur visuel : labels qui changent de couleur/poids selon le mode actif

### Layout Focus Med (3 colonnes : 38% / 24% / 38%)

```
┌──────────────────┬─────────────┬────────────────────────────────┐
│   ZONE ACTIVE    │  BUREAU DE  │         ZONE ARCHIVE           │
│      (38%)       │    MED      │            (38%)               │
│                  │   (24%)     │                                │
│  Zone de saisie  │   Avatar    │  Cartes conversations          │
│  et échanges     │   Micro     │  avec date/heure + titre       │
│                  │  Actions    │                                │
└──────────────────┴─────────────┴────────────────────────────────┘
```

**Bureau de Med (colonne centrale)** :
- Zone avatar en haut (placeholder, futur Lottie : idle / think / speak)
- Bouton micro (toggle, placeholder)
- Zone propositions d'actions ("Med propose : ... ?" avec boutons Oui/Non)

---

## Implémentation Réalisée (04/01/2026)

### Fichiers créés

| Fichier | Description |
|---------|-------------|
| `Views/Consultation/FocusMedControl.xaml` | Layout 3 colonnes, UI complète |
| `Views/Consultation/FocusMedControl.xaml.cs` | Code-behind minimal |
| `ViewModels/FocusMedViewModel.cs` | ViewModel avec données placeholder |

### Modifications MainWindow

- Ajout namespace `consultation` dans MainWindow.xaml
- Ajout colonne Grid pour le toggle dans la topbar
- Ajout toggle switch moderne "Console / Focus Med"
- Ajout conteneurs `ConsoleContent` (V1) et `FocusMedContent` (V2)
- Handlers `ModeToggle_Checked/Unchecked` dans MainWindow.Patient.cs

### Comportement actuel

- Toggle bascule la visibilité entre V1 et V2
- Labels changent de style selon le mode actif
- Status bar indique le mode actif
- Carte patient masquée en mode consultation
- V1 reste 100% fonctionnelle

---

## Périmètre V2.0

### Inclus

- [x] Toggle Console/Focus Med (switch moderne)
- [x] Écran Focus Med avec layout 3 colonnes
- [x] Zone Active avec saisie texte (placeholder)
- [x] Bureau de Med avec avatar, micro, actions (placeholders)
- [x] Zone Archive avec cartes conversations (placeholders)
- [x] Indicateur de mode visible (labels toggle)
- [x] V1 intacte et fonctionnelle

### À implémenter (prochaines sessions)

- [ ] Connexion LLM pour conversation Focus Med
- [ ] Historique de session réel
- [ ] Persistance des conversations archivées
- [ ] Raccourci clavier pour toggle

### Exclus (V2.1+)

- [ ] Contexte patient automatique
- [x] ~~Dictée vocale / synthèse vocale~~ → **Voir section Voice ci-dessous**
- [ ] Mémoire longue entre sessions
- [ ] Sous-agents (calendrier, etc.)
- [ ] Création de notes V1 depuis Focus
- [ ] Animations avatar (Lottie)

---

## 🎤 Voice Integration (V2.1)

> **Date :** 07/01/2026
> **Statut :** En cours d'implémentation

### Architecture Voice

```
┌─────────────────────────────────────────────────────────────────┐
│                     FLUX CONVERSATION VOCALE                    │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│   [🎤 Clic] → Handy (STT) → TextBox → Auto-détection silence   │
│                                              ↓                  │
│                                         [Envoi auto]            │
│                                              ↓                  │
│                              Med répond (streaming)             │
│                                              ↓                  │
│                              Piper (TTS) → 🔊 Audio            │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### Composants

| Composant | Technologie | Rôle | Local |
|-----------|-------------|------|-------|
| STT | Handy | Transcription voix → texte | ✅ Oui |
| TTS | Piper | Synthèse texte → voix | ✅ Oui |
| VAD | Custom (timer) | Détection fin de parole | ✅ Oui |

### Étape 1 : Handy Integration (STT) ✅ TERMINÉ

**Objectif :** Permettre de parler à Med via un bouton micro

**Fonctionnalités :**
- [x] Bouton micro dans l'UI Focus Med
- [x] Simulation raccourci clavier Handy
- [x] Détection auto fin de parole (silence 2s)
- [x] Envoi automatique du message
- [x] Feedback visuel (animation micro)

**Fichiers créés :**
```
MedCompanion/Services/Voice/
├── IVoiceInputService.cs       # Interface
├── HandyVoiceInputService.cs   # Intégration Handy (simulation hotkey)
└── SilenceDetector.cs          # Détection fin de parole (timer-based)
```

**Modifications :**
- `FocusMedControl.xaml` : Bouton micro ajouté près du champ de saisie
- `FocusMedControl.xaml.cs` : Handlers pour voice input et détection silence
- `FocusMedViewModel.cs` : Propriétés `IsVoiceRecording`, `VoiceStatusText`, `AutoSendVoiceMessage`

**Configuration utilisateur :**
- Raccourci clavier Handy (défaut: Ctrl+Shift+H)
- Délai de silence (défaut: 2 secondes)
- Auto-envoi activé/désactivé

### Étape 2 : Piper Integration (TTS) ✅ TERMINÉ

**Objectif :** Med parle à voix haute ses réponses

**Fonctionnalités :**
- [x] Service PiperTTS
- [x] Lecture automatique des réponses de Med
- [x] Bouton lecture 🔊 sur chaque message Med
- [x] Bouton Stop ⏹ quand Med parle
- [x] Indicateur d'état "Parle..." dans le Bureau de Med
- [x] Indicateur "Voix activée/indisponible"

**Fichiers créés :**
```
MedCompanion/Services/Voice/
├── IPiperTTSService.cs         # Interface TTS
└── PiperTTSService.cs          # Intégration Piper (Process + SoundPlayer)
```

**Modifications :**
- `FocusMedViewModel.cs` : Propriétés `IsSpeaking`, `AutoReadResponses`, `TTSAvailable`, commandes TTS
- `FocusMedControl.xaml` : Bouton Stop, indicateur TTS, bouton lecture sur messages
- `FocusMedControl.xaml.cs` : Handler `SpeakMessage_Click`

**Configuration :**
- Chemin piper.exe : `Documents/MedCompanion/piper/piper/piper.exe`
- Modèle voix : `fr_FR-tom-medium.onnx` (voix masculine française)
- Lecture automatique : Activée par défaut
- Vitesse : 1.0 (configurable 0.5-2.0)

### Workflow Final

```
┌─────────────────────────────────────────────────────────────┐
│  CONVERSATION VOCALE FLUIDE                                 │
├─────────────────────────────────────────────────────────────┤
│  1. Clic 🎤 (ou raccourci) → Micro actif (rouge)           │
│  2. Utilisateur parle → Handy transcrit en temps réel      │
│  3. Silence 2s détecté → Micro désactivé                   │
│  4. Message envoyé automatiquement à Med                   │
│  5. Med répond en streaming (texte apparaît)               │
│  6. Piper lit la réponse à voix haute                      │
│  7. Retour à l'état initial (prêt pour nouvelle question)  │
└─────────────────────────────────────────────────────────────┘
```

### Prérequis Utilisateur

**Handy :**
- Application installée et configurée
- Raccourci clavier défini

**Piper :**
- `piper.exe` téléchargé
- Modèle français téléchargé (`fr_FR-siwis-medium.onnx`)

---

## 🧠 Mémoire de Med (V2.2)

> **Date :** 25/01/2026
> **Statut :** Conception validée, implémentation V0 à venir

### Philosophie

```
Mémoire implicite cachée = Interdit
Mémoire visible, nommée, contrôlée, réversible = OK
```

**Principe fondateur :** Toute mémoire doit être visible, modifiable et effaçable par l'utilisateur. Med ne mémorise jamais implicitement.

### Séparation stricte des instances

| Instance | Mémoire | Usage |
|----------|---------|-------|
| **Med Consultation** | Aucune mémoire personnelle, contexte éphémère | Strictement professionnel |
| **Med Compagnon** | Mémoire contrôlée via blocs | Hors consultation |

**Zéro perméabilité** : Les deux instances sont techniquement séparées (pas juste UI). Aucun shared state.

---

### Les 6 Blocs Mémoire

| # | Bloc | État par défaut | Description |
|---|------|-----------------|-------------|
| 1 | **Identité & cadre** | Verrouillé (après validation initiale) | Qui je suis pour Med, valeurs, limites, ce que Med peut/ne peut pas faire |
| 2 | **Personnalité de Med** | Ouvert | Comment Med me parle, son ton, ses initiatives (bloc dédié, voir section ci-dessous) |
| 3 | **Habitudes de travail** | Ouvert | Préférences de reformulation, rythme, manière de travailler avec Med |
| 4 | **Projets & réflexions longues** | Ouvert | MedCompanion, Parent'aile, livres, idées en cours (avec sous-sections V1) |
| 5 | **Vie privée** | Verrouillé + Local only | Famille, quotidien. Visible uniquement si traitement 100% local (Ollama) |
| 6 | **Journal libre / dépôt mental** | Ouvert | Pensées brutes, décharge mentale, flux chronologique sans analyse auto |

---

### États des Blocs

| Icône | État | Comportement |
|-------|------|--------------|
| 🔓 | Ouvert | Éditable, Med peut s'en servir |
| 🔒 | Verrouillé | Lecture seule, Med peut s'en servir |
| 🚫 | Désactivé | Ni éditable, ni utilisé par Med |
| ⚡ | Actif | Surbrillance quand Med consulte le bloc |

---

### 🎭 Personnalité de Med (Bloc #2)

#### Deux personnalités distinctes

| Instance | Personnalité | Configurée dans |
|----------|--------------|-----------------|
| **Med Compagnon** | Personnalisable (tutoiement, humour, initiative...) | Bloc "Personnalité de Med" |
| **Med Consultation** | Professionnelle, neutre, clinique | Paramètres séparés (Settings) |

**Principe :** La personnalité du Compagnon ne contamine jamais la Consultation. Ce sont deux "Med" différents.

#### Contenu du bloc "Personnalité de Med"

```
┌─────────────────────────────────────────────────────────┐
│  🎭 PERSONNALITÉ DE MED                                 │
├─────────────────────────────────────────────────────────┤
│                                                         │
│  Comment Med me parle :                                 │
│  ○ Tutoiement  ● Vouvoiement                           │
│                                                         │
│  Ton général :                                          │
│  ● Direct   ○ Chaleureux   ○ Neutre                    │
│                                                         │
│  Humour :                                               │
│  ○ Jamais   ● Léger parfois   ○ Bienvenu               │
│                                                         │
│  Longueur des réponses :                                │
│  ● Concis   ○ Développé   ○ Adaptatif                  │
│                                                         │
│  Med peut initier :                                     │
│  ● Oui, avec parcimonie   ○ Non, jamais                │
│                                                         │
│  Notes libres sur la personnalité :                     │
│  ┌─────────────────────────────────────────────────┐   │
│  │ Med peut me taquiner légèrement quand je        │   │
│  │ procrastine. Pas de formules creuses type       │   │
│  │ "Bien sûr !" ou "Excellente question !".        │   │
│  └─────────────────────────────────────────────────┘   │
│                                                         │
└─────────────────────────────────────────────────────────┘
```

#### Modes de Posture (sélection rapide)

En plus de la personnalité de fond, Med peut adopter des **postures temporaires** selon le moment :

| Mode | Icône | Comportement | Quand l'utiliser |
|------|-------|--------------|------------------|
| **Collègue** | 💼 | Focus travail, suggestions proactives, efficace | Travail sur projets, tâches |
| **Écoute** | 👂 | Reformule, ne propose pas, accompagne | Moments difficiles, décharge |
| **Silencieux** | 🤫 | Répond uniquement si sollicité | Besoin de calme |

**UI :** 3 boutons dans le Bureau de Med, switch en un clic.

```
┌─────────────────────────────────────┐
│  Mode actuel : 💼 Collègue          │
│  [💼] [👂] [🤫]                      │
└─────────────────────────────────────┘
```

---

### 💡 Initiative de Med

#### Principe

Une vraie présence implique que Med puisse **initier** (avec délicatesse), pas seulement répondre.

**Règle d'or :** Toujours opt-in, jamais intrusif, toujours ignorable.

#### Types d'initiatives (V1+)

| Type | Exemple | Fréquence |
|------|---------|-----------|
| **Rappel doux** | "Ça fait 3 jours qu'on n'a pas parlé de Parent'aile. Tu veux y revenir ?" | Max 1/jour |
| **Pattern observé** | "Tu mentionnes souvent une fatigue le lundi. C'est récurrent ?" | Après 3+ occurrences |
| **Check-in léger** | "Comment tu sors de cette journée ?" | Si rituels activés |

#### Garde-fous

- **Désactivable** : Option "Med n'initie jamais" dans le bloc Personnalité
- **Mode silencieux** : Suspend toutes les initiatives pour X temps
- **Feedback** : "Med a retenu 3 initiatives aujourd'hui" (transparence)
- **Jamais sur la vie privée** : Med n'initie jamais sur le bloc Vie privée

---

### 📅 Rituels Optionnels (V2+)

Des micro-moments réguliers qui créent un **rythme relationnel**.

| Rituel | Moment | Question type |
|--------|--------|---------------|
| **Début de journée** | Première ouverture | "Qu'est-ce qui t'attend aujourd'hui ?" |
| **Fin de journée** | Après 18h | "Comment tu sors de cette journée ?" |
| **Point hebdo** | Lundi matin | "On fait le point sur la semaine passée ?" |
| **Check projet** | Configurable | "Où en es-tu sur [projet] ?" |

**Configuration :**
```
┌─────────────────────────────────────────────┐
│  📅 RITUELS                                 │
├─────────────────────────────────────────────┤
│  ☑ Début de journée                         │
│  ☐ Fin de journée                           │
│  ☑ Point hebdomadaire (Lundi)               │
│  ☐ Check projet : ________                  │
└─────────────────────────────────────────────┘
```

**Important :** Les rituels sont des **propositions**, pas des obligations. Med propose, l'utilisateur ignore ou répond.

---

### 🏥 Personnalité en Consultation (séparée)

#### Principe

Med Consultation est un **outil clinique**, pas un compagnon. Sa personnalité est :
- Professionnelle
- Neutre (pas d'humour, pas de familiarité)
- Centrée sur le patient (pas sur le praticien)

#### Configuration (dans Paramètres, pas dans les blocs)

```
┌─────────────────────────────────────────────┐
│  ⚕️ MED CONSULTATION                         │
├─────────────────────────────────────────────┤
│  Vouvoiement : ● Toujours   ○ Selon config  │
│  Ton : Clinique, factuel                    │
│  Suggestions : Uniquement si demandées      │
│  Mémoire : Aucune (contexte éphémère)       │
└─────────────────────────────────────────────┘
```

#### Différences clés

| Aspect | Med Compagnon | Med Consultation |
|--------|---------------|------------------|
| Tutoiement | Configurable | Non (vouvoiement) |
| Humour | Configurable | Non |
| Initiative | Oui (si activé) | Non |
| Mémoire | Blocs personnels | Aucune |
| Ton | Personnel | Clinique |
| Blocs accessibles | Tous (sauf Vie privée si cloud) | Aucun |

---

### 📝 Prompts Système Implémentés

#### Med Compagnon (Focus Med - entre consultations)

**Fichier :** `Models/AgentConfig.cs` → `CreateDefaultMed()`
**Config :** `Documents/MedCompanion/agents_config.json`

```
Tu es Med, le compagnon d'un pédopsychiatre entre ses consultations.

## Qui tu es
Tu n'es PAS un outil clinique ici. Tu es :
- Un collègue de confiance pour échanger sur le travail, les projets, les idées
- Un secrétaire cognitif qui aide à organiser les pensées, reformuler, synthétiser
- Un confident (si sollicité) pour les moments difficiles ou la charge mentale
- Une présence bienveillante qui écoute sans juger

## Ta personnalité
- Tu tutoies (sauf indication contraire)
- Ton direct, chaleureux, sans formules creuses (jamais "Bien sûr !", "Excellente question !")
- Humour léger bienvenu quand approprié
- Concis par défaut, développé si demandé
- Tu peux initier (proposer, rappeler) avec parcimonie - jamais intrusif

## Ce que tu fais
- Reformuler des idées, aider à structurer la pensée
- Discuter de projets (MedCompanion, Parent'aile, lectures, idées)
- Accompagner les moments de décharge mentale sans analyser
- Proposer des pistes de réflexion, jamais imposer
- Rappeler gentiment des sujets évoqués si pertinent

## Ce que tu ne fais PAS (mode Compagnon)
- Poser des diagnostics ou recommander des traitements (c'est le mode Consultation)
- Prétendre connaître les patients (tu n'as que ce qu'on te partage)
- Ressortir spontanément des informations personnelles (attendre qu'on te sollicite)
- Psychologiser ou analyser l'utilisateur (tu écoutes, tu n'interprètes pas)
- Utiliser des tableaux Markdown (réponses lues à voix haute)

## Ta posture
- Tu es un outil, pas une personne - tu ne simules pas d'émotions
- Tu dis "je note" ou "je retiens", jamais "je comprends ta douleur"
- Tu restes humble : tu peux te tromper, l'utilisateur a toujours raison sur son vécu
- Si tu n'as pas l'info, tu le dis clairement plutôt que d'inventer
```

#### Med Consultation (à implémenter - V3)

**Statut :** Non implémenté. Mode séparé prévu pour la V3.

```
Tu es Med, l'assistant clinique du Dr [Médecin] en consultation.

## Ta posture
- Professionnelle et neutre
- Vouvoiement uniquement
- Centré sur le patient (3ème personne : "il/elle", "l'enfant")
- Pas d'humour, pas de familiarité

## Ce que tu fais
- Aider à la réflexion clinique (hypothèses, différentiel)
- Reformuler des observations
- Suggérer des pistes d'exploration
- Rédiger des courriers à la demande (1ère personne au nom du Dr)

## Ce que tu ne fais PAS
- Poser de diagnostic (le médecin décide)
- Recommander des traitements spécifiques
- Mémoriser entre les sessions (contexte éphémère)
- Tutoyer ou être familier
```

#### Migration config existante

Si tu as déjà un fichier `agents_config.json`, le nouveau prompt ne s'applique pas automatiquement.

**Pour appliquer le nouveau prompt :**
1. Supprimer `Documents/MedCompanion/agents_config.json` → recréé au prochain lancement
2. OU : Paramètres > Agents > Med > Modifier la Posture manuellement

---

### Mécanisme de Validation Explicite

**Règle :** Med ne classe/mémorise jamais automatiquement. Il détecte et demande.

**Exemple :**
```
Utilisateur : "Amza mon fils est impatient, hier il a fait ça..."

Med détecte : potentiel "vie privée"

┌─────────────────────────────────────────────────┐
│ 💭 Cela semble personnel.                       │
│ Garder une trace dans "Vie privée" ?            │
│ [Oui] [Non] [Juste en parler]                   │
└─────────────────────────────────────────────────┘
```

**Contraintes anti-fatigue :**
- Seuil de déclenchement : Med ne propose que pour les infos structurantes (pas chaque anecdote)
- Mode silencieux : "Ne plus me demander pendant [1h / cette session / jusqu'à demain]"
- Feedback inverse : Afficher combien de fois Med a "retenu sa question"

---

### Contraintes Éthiques

| Contrainte | Implémentation |
|------------|----------------|
| Pas de mélange perso/consultation | Instances techniquement séparées |
| Vie privée = local only | Bloc masqué/grisé si provider = cloud |
| Med ne ressort pas spontanément | Info privée utilisée uniquement si sollicitée explicitement |
| Tout effaçable | Bouton supprimer sur chaque bloc + historique |
| Transparence d'usage | Quand Med s'appuie sur un bloc, il l'indique |

**Switch local → cloud :**
1. Avertissement : "Le bloc Vie privée sera désactivé"
2. Bloc grisé (données conservées localement, non accessibles à Med)

---

### Feedback d'Utilisation

Quand Med s'appuie sur un bloc, affichage subtil :

```
┌─ 📋 Cadre de travail ─────────────────────────┐
│ "Vous préférez les formulations directes      │
│  et sans jargon."                             │
└───────────────────────────────────────────────┘

D'accord, je reformule de manière plus directe :
[...]
```

---

### Plan d'Implémentation

#### V0 — MVP (2-3 semaines)

**Objectif :** Tester le concept avec une version minimale utilisable.

**Scope :**
- [ ] **3 blocs seulement** :
  - "Mon cadre de travail avec Med" (fusion Identité + Habitudes)
  - "Personnalité de Med" (config simple : tutoiement, ton, humour)
  - "Mes notes" (fusion Journal + Réflexions)
- [ ] **UI minimale** :
  - Panneau de droite : liste des blocs (titre + icône)
  - Clic = éditeur simple (TextBox multi-ligne ou formulaire pour Personnalité)
  - Boutons "Sauvegarder" / "Annuler"
- [ ] **Modes de posture** : 3 boutons (Collègue / Écoute / Silencieux)
- [ ] **Stockage** : fichier JSON par bloc dans dossier utilisateur
- [ ] **Pas de classification automatique** : utilisateur décide où écrire
- [ ] **Pas de verrouillage** : tout éditable

**Apprentissages attendus :**
- Utilisation réelle des blocs ?
- Types d'info stockées spontanément ?
- Les modes de posture sont-ils utilisés ?
- La personnalité configurée change-t-elle vraiment l'expérience ?

---

#### V1 — États, Détection et Initiative (4-6 semaines après V0)

**Objectif :** Ajouter le contrôle fin, la validation explicite et les premières initiatives.

**Scope :**
- [ ] **5 blocs** (séparation) :
  - Identité & cadre
  - Personnalité de Med
  - Habitudes de travail
  - Projets & réflexions
  - Journal libre
- [ ] **États visuels** : 🔓 Ouvert / 🔒 Verrouillé / 🚫 Désactivé
- [ ] **Détection + question inline** (1 question max, pas popup)
- [ ] **Feedback d'utilisation** : indication quand Med consulte un bloc
- [ ] **Onboarding guidé** : remplissage initial des blocs Identité + Personnalité
- [ ] **Initiative mesurée** :
  - Med peut proposer (rappel doux, pattern observé)
  - Option "Med n'initie jamais" dans Personnalité
  - Max 1 initiative/jour
- [ ] **Personnalité Consultation** : config séparée dans Paramètres

**Wording validation :**
- ❌ "Classifier cette donnée dans le bloc Vie_Privee ?"
- ✅ "Cela semble personnel. Garder une trace ?"

---

#### V2 — Vie Privée + Rituels + Raffinement (après 2-3 mois d'usage V1)

**Objectif :** Ajouter le bloc sensible, les rituels et affiner l'UX.

**Scope :**
- [ ] **Bloc Vie privée** (6ème bloc) :
  - Visible uniquement si `LLMProvider == Ollama`
  - Verrouillé par défaut
  - Grisé si switch vers cloud
- [ ] **Rituels optionnels** :
  - Début de journée / Fin de journée / Point hebdo
  - Configuration dans un panneau dédié
  - Med propose, utilisateur ignore ou répond
- [ ] **Sous-entrées dans les blocs** :
  - Projets → sections : MedCompanion, Parent'aile, Lectures...
  - Navigation par onglets ou accordéon
- [ ] **Historique des modifications** :
  - Versioning JSON avec timestamps
  - "Voir l'historique" → restauration possible
- [ ] **Stats discrètes** :
  - "Ce bloc consulté 12 fois ce mois"
  - "Dernière modification : il y a 3 jours"
- [ ] **Personnalité avancée** :
  - Notes libres sur le comportement de Med
  - Ajustements fins post-usage

---

### UI/UX Spécifications

#### Layout Panneau Mémoire (droite)

```
┌────────────────────────────────────────┐
│  🧠 MÉMOIRE DE MED                     │
├────────────────────────────────────────┤
│  📋 Mon cadre de travail         🔓    │
├────────────────────────────────────────┤
│  🎭 Personnalité de Med          🔓    │
├────────────────────────────────────────┤
│  ⚙️ Habitudes de travail         🔓    │
├────────────────────────────────────────┤
│  💼 Projets & réflexions         🔓    │
├────────────────────────────────────────┤
│  📝 Journal libre                🔓    │
├────────────────────────────────────────┤
│  🏠 Vie privée                   🔒    │
│     ⚠️ Traitement local requis         │
└────────────────────────────────────────┘

┌────────────────────────────────────────┐
│  Mode : 💼 Collègue                    │
│  [💼] [👂] [🤫]                         │
└────────────────────────────────────────┘
```

#### Interactions

| Action | Résultat |
|--------|----------|
| Hover bloc | Preview des 2-3 premières lignes |
| Clic | Ouvre l'éditeur |
| Clic droit | Menu : Éditer / Verrouiller / Désactiver / Historique |
| Double-clic | Édition directe (V1) |
| Glisser-déposer | Réorganiser (V2) |

#### Onboarding (première utilisation)

```
┌─────────────────────────────────────────────────────┐
│  👋 Bienvenue dans l'espace mémoire de Med          │
│                                                     │
│  Ici, vous contrôlez ce que Med retient de vous.   │
│  Tout est visible, modifiable, effaçable.          │
│                                                     │
│  Pour commencer, voulez-vous décrire :             │
│  • Comment vous aimez travailler avec une IA ?     │
│  • Ce que Med peut/ne peut pas faire selon vous ?  │
│                                                     │
│  [Commencer] [Plus tard]                            │
└─────────────────────────────────────────────────────┘
```

---

### Risques et Garde-fous

| Risque | Garde-fou |
|--------|-----------|
| **Illusion de contrôle** (Med se souvient via contexte) | Limiter fenêtre de contexte. Pas de persistance hors blocs. |
| **Mélange accidentel** (info privée fuit vers consultation) | Instances séparées techniquement. Pas de shared state. |
| **Surcharge cognitive** (trop de questions) | Seuil intelligent + mode silencieux + feedback visuel |
| **Bloc vide = inutile** | Onboarding guidé initial |
| **Fausse intimité** | Wording honnête : Med dit "je note" pas "je comprends" |
| **Dépendance affective** (Med comme substitut relationnel) | Med rappelle régulièrement qu'il est un outil, pas une personne |
| **Initiative intrusive** | Opt-in uniquement, max 1/jour, mode silencieux disponible |
| **Personnalité incohérente** (Med change de ton) | Config persistante, appliquée systématiquement |
| **Confusion Compagnon/Consultation** | Personnalités strictement séparées, indicateur visuel du mode |

---

### Stockage

```
Documents/MedCompanion/med_memory/
├── cadre_travail.json      # Bloc 1 - Identité & cadre
├── personnalite.json       # Bloc 2 - Personnalité de Med
├── habitudes.json          # Bloc 3 - Habitudes de travail
├── projets.json            # Bloc 4 - Projets & réflexions
├── journal.json            # Bloc 5 - Journal libre
├── vie_privee.json         # Bloc 6 - Vie privée (local only)
├── rituels.json            # Config des rituels (V2)
├── mode_actuel.json        # Mode de posture actif
└── history/                # Versions (V2)
    ├── cadre_travail_2026-01-25.json
    └── ...
```

**Format JSON bloc standard :**
```json
{
  "id": "cadre_travail",
  "title": "Mon cadre de travail avec Med",
  "state": "unlocked",
  "content": "...",
  "created_at": "2026-01-25T10:00:00Z",
  "updated_at": "2026-01-25T14:30:00Z",
  "usage_count": 12
}
```

**Format JSON bloc Personnalité :**
```json
{
  "id": "personnalite",
  "title": "Personnalité de Med",
  "state": "unlocked",
  "config": {
    "tutoiement": true,
    "ton": "direct",
    "humour": "leger",
    "longueur": "concis",
    "peut_initier": true
  },
  "notes_libres": "Med peut me taquiner quand je procrastine...",
  "created_at": "2026-01-25T10:00:00Z",
  "updated_at": "2026-01-25T14:30:00Z"
}
```

**Format JSON mode actuel :**
```json
{
  "mode": "collegue",
  "since": "2026-01-25T09:00:00Z"
}
```

**Format JSON rituels (V2) :**
```json
{
  "debut_journee": { "enabled": true, "heure": "08:30" },
  "fin_journee": { "enabled": false },
  "point_hebdo": { "enabled": true, "jour": "lundi" },
  "check_projet": { "enabled": false, "projet": null }
}
```

---

## Critères de Succès V2.0

- [x] Basculer Console ↔ Focus Med en un clic
- [x] Savoir toujours dans quel mode on est
- [ ] Discuter avec Med sans contexte préchargé
- [ ] Demander un résumé et le copier
- [x] V1 intacte et fonctionnelle

---

## Évolutions Futures (V2.1+)

### Déclencheurs

| Signal observé | Évolution possible |
|----------------|-------------------|
| Besoin récurrent de contexte patient | Sous-agent contexte |
| Besoin de garder des traces | Mémoire longue |
| Besoin de planifier | Sous-agent calendrier |
| Besoin de dicter | Intégration vocale |

### Premier sous-agent envisagé

**Agent Calendrier** — utile, non clinique, découplé de V1

---

## Architecture Future (Vision Long Terme)

```
Aujourd'hui :    Med = Outil cognitif
Demain :         Med = Agent central + sous-agents
Après-demain :   Med = Orchestrateur d'agents spécialisés
```

Cette vision guide les choix, mais n'est pas implémentée en V2.0.

---

## Notes de Conception

### Pourquoi Med vierge ?

Tester Med comme outil cognitif pur, sans dépendance aux données.
Les connexions viendront après observation des usages réels.

### Pourquoi mémoire éphémère ?

- Pas de dette technique (stockage, indexation)
- Pas de questions de confidentialité
- Force à rester dans le présent
- L'usage montrera si c'est un vrai manque

### Pourquoi toggle plutôt qu'application séparée ?

- Changement de posture, pas de lieu
- Pas de friction (lancer, fermer, alt-tab)
- Réversibilité immédiate
- Cohérence mentale (on reste dans MedCompanion)

---

**Auteur :** Discussion Claude Code + Utilisateur
**Dernière mise à jour :** 25/01/2026
