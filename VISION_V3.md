# Vision V3 — Écosystème Parent'aile + MedCompanion

> **Date de création :** 31/01/2026
> **Dernière mise à jour :** 01/02/2026
> **Statut :** Vision validée, V0 en conception

### Évolutions récentes (01/02/2026)
- **Simplification multi-enfants** : 1 compte parent avec email, plusieurs tokens liés à des nicknames d'enfants
- **Réponses par email** : Parent'aile = canal d'entrée uniquement, réponses médicales envoyées par email sécurisé

---

## Philosophie Fondatrice

```
Tout le monde peut être SOUTENU
Certains sont ACCOMPAGNÉS
Quelques-uns sont SOIGNÉS

Chaque niveau a son cadre, ses règles et ses limites.
```

**Principe absolu :** Les deux applications ne doivent JAMAIS être fusionnées.

---

## Vue d'Ensemble

### Les Deux Projets

| Projet | Nature | Public | Plateforme | Stack |
|--------|--------|--------|------------|-------|
| **Parent'aile** | Soutien parental | Tous les parents | Web / PWA | React / TypeScript / Firebase |
| **MedCompanion** | Plateforme de soins | Médecin + patients validés | Desktop WPF | C# / .NET 8 / Local |

### Architecture Globale

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              ÉCOSYSTÈME V3                                   │
├──────────────────────────────────┬──────────────────────────────────────────┤
│          PARENT'AILE             │            MEDCOMPANION                  │
│       (Soutien - Web/PWA)        │       (Plateforme de soins - Desktop)    │
├──────────────────────────────────┼──────────────────────────────────────────┤
│                                  │                                          │
│  SOUTIEN PUBLIC (grisé V0)       │  MODES EXISTANTS                         │
│  • Forum / partage               │  • Accueil                               │
│  • Ateliers                      │  • Bureau (Console V1)                   │
│  • Ressources                    │  • Consultation (en conception)          │
│  • Boutique                      │                                          │
│  • "Faire le point"              │  NOUVEAU MODE                            │
│                                  │  • Pilotage 🎯                           │
│  ESPACE PATIENT (actif V0) 🎯    │    - Gestion tokens                      │
│  • Connexion token               │    - Inbox messages                      │
│  • Pseudo anonyme                │    - Correspondance token↔patient        │
│  • Messages + reformulation IA   │    - Réponses assistées                  │
│  • Historique                    │    - (futur: dashboard Parent'aile)      │
│                                  │                                          │
│  LE SOIN N'EXISTE QUE DANS       │  Accès patient : QR code / token         │
│  MEDCOMPANION                    │  remis en cabinet sur décision médicale  │
│                                  │                                          │
└──────────────────────────────────┴──────────────────────────────────────────┘
```

---

## Séparation des Données — Principe Fondamental

### Architecture de Confidentialité

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         SÉPARATION DES DONNÉES                               │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   PARENT'AILE (Firebase)              MEDCOMPANION (Local)                  │
│   ══════════════════════              ════════════════════                  │
│                                                                             │
│   • Email parent (compte unique)      • Données réelles patient             │
│   • Tokens liés + nickname enfant     • Nom, prénom, dossier                │
│   • Messages envoyés (texte)          • Historique médical                  │
│   • Statut (envoyé/lu/répondu)        • Table de correspondance :           │
│   • Timestamps                          Token → Patient réel                │
│                                       • Email parent (pour réponses)        │
│                                       • Historique réponses envoyées        │
│                                                                             │
│   ══════════════════════              ════════════════════                  │
│   AUCUNE donnée médicale              TOUTES les données médicales          │
│   AUCUN nom réel                      Réponses envoyées par EMAIL           │
│   AUCUNE réponse médicale             (jamais stockées sur Firebase)        │
│   ══════════════════════              ════════════════════                  │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Flux Token — Multi-enfants et Réponses Email

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                    FLUX SIMPLIFIÉ — MULTI-ENFANTS                            │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  1. CABINET (pour chaque enfant)                                            │
│     ────────────────────────────                                            │
│     Médecin génère token dans MedCompanion (Mode Pilotage)                  │
│     Token lié au patient réel (ex: DUPONT Martin)                           │
│     QR code remis au parent                                                 │
│                                                                             │
│  2. PARENT'AILE — Premier enfant                                            │
│     ─────────────────────────────                                           │
│     Parent scanne QR code enfant 1                                          │
│     Inscription : email + mot de passe (compte unique)                      │
│     "Prénom ou surnom de votre enfant ?" → "Théo"                           │
│     Token 1 lié au nickname "Théo"                                          │
│                                                                             │
│  3. PARENT'AILE — Enfants suivants (optionnel)                              │
│     ──────────────────────────────────────────                              │
│     Depuis le dashboard : [+ Ajouter un enfant]                             │
│     Saisie nouveau token (QR code enfant 2)                                 │
│     "Prénom ou surnom ?" → "Emma"                                           │
│     Token 2 lié au nickname "Emma"                                          │
│                                                                             │
│  4. ENVOI MESSAGE                                                           │
│     ─────────────                                                           │
│     Parent choisit l'enfant concerné (Théo ou Emma)                         │
│     Rédige son message (avec aide reformulation IA)                         │
│     Message envoyé avec le token correspondant                              │
│     → Firebase stocke : { token, message, statut }                          │
│                                                                             │
│  5. MEDCOMPANION — Réception                                                │
│     ────────────────────────                                                │
│     Mode Pilotage récupère messages depuis Firebase                         │
│     Correspondance automatique : token → patient réel                       │
│     Médecin voit : "DUPONT Martin" + message                                │
│                                                                             │
│  6. MEDCOMPANION — Réponse par EMAIL                                        │
│     ────────────────────────────────                                        │
│     Médecin rédige sa réponse                                               │
│     Peut joindre documents (attestations, etc.)                             │
│     Clic "Envoyer" → Email envoyé au parent                                 │
│     Firebase mis à jour : statut = "replied"                                │
│                                                                             │
│  7. PARENT reçoit                                                           │
│     ─────────────                                                           │
│     📧 Email avec réponse + pièces jointes éventuelles                      │
│     Sur Parent'aile : notification "Réponse envoyée sur votre email"        │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────────────────┐
│                    PRINCIPE DE SÉCURITÉ — RÉPONSES                           │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   PARENT'AILE = CANAL D'ENTRÉE           EMAIL = CANAL DE SORTIE            │
│   ═══════════════════════════            ═══════════════════════            │
│                                                                             │
│   ✅ Messages des parents                ✅ Réponses du médecin              │
│   ✅ Demandes simples                    ✅ Documents sensibles              │
│   ✅ Aide reformulation IA               ✅ Attestations, courriers          │
│                                          ✅ Traçabilité (preuve d'envoi)     │
│                                                                             │
│   ❌ Jamais de réponse médicale          ❌ Jamais sur plateforme web        │
│   ❌ Jamais de document sensible                                            │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Parent'aile — Plateforme de Soutien Parental

### Mission

Offrir un espace de soutien accessible à tous les parents, sans prétention médicale.

### Stratégie V0 : Grisage Progressif

**Principe :** Ne pas supprimer les pages existantes (travail important), mais les rendre inaccessibles avec message "Bientôt disponible". Ouverture progressive selon les besoins validés.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                      PARENT'AILE — STRATÉGIE GRISAGE                         │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ÉTAT V0                                                                    │
│  ───────                                                                    │
│                                                                             │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐             │
│  │   LANDING       │  │  ESPACE PATIENT │  │   FORUM         │             │
│  │   ✅ Actif      │  │  ✅ Actif       │  │   ░░ Grisé      │             │
│  │   (adapté V0)   │  │  (token requis) │  │   "Bientôt"     │             │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘             │
│                                                                             │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────┐             │
│  │   ATELIERS      │  │   BOUTIQUE      │  │  FAIRE LE POINT │             │
│  │   ░░ Grisé      │  │   ░░ Grisé      │  │   ░░ Grisé      │             │
│  │   "Bientôt"     │  │   "Bientôt"     │  │   "Bientôt"     │             │
│  └─────────────────┘  └─────────────────┘  └─────────────────┘             │
│                                                                             │
│  OUVERTURE PROGRESSIVE                                                      │
│  ─────────────────────                                                      │
│                                                                             │
│  V0 : Espace Patient seulement (messages)                                   │
│  V1 : + Forum (si besoin validé par usage)                                  │
│  V2 : + Ateliers                                                            │
│  V3 : + Boutique                                                            │
│  V4 : + "Faire le point" (renommé, avec garde-fous)                         │
│                                                                             │
│  → Chaque ouverture = décision basée sur usage réel                         │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Modules et Statuts

| Module | Statut Code | Statut V0 | Action |
|--------|-------------|-----------|--------|
| Landing | ✅ Implémenté | ✅ Actif | Adapter message pour V0 |
| **Espace Patient** | ❌ À créer | 🎯 **Priorité** | Nouvelle route `/espace` |
| Forum | ✅ Implémenté | ░ Grisé | Bandeau "Bientôt disponible" |
| Ateliers | ✅ Implémenté | ░ Grisé | Bandeau "Bientôt disponible" |
| Boutique | ✅ Implémenté | ░ Grisé | Bandeau "Bientôt disponible" |
| "Faire le point" | ✅ Implémenté | ░ Grisé | Renommer + bandeau |

### Espace Patient — Spécifications V0

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         ESPACE PATIENT — V0                                  │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ACCÈS — COMPTE UNIQUE PARENT                                               │
│  ────────────────────────────                                               │
│  • Route : /espace                                                          │
│  • Première connexion : QR code + inscription (email + mot de passe)        │
│  • Connexions suivantes : email + mot de passe classique                    │
│  • Un seul compte par parent (même avec plusieurs enfants)                  │
│                                                                             │
│  INSCRIPTION (premier enfant)                                               │
│  ────────────────────────────                                               │
│  1. Scanner QR code ou saisir token                                         │
│  2. Créer compte : email + mot de passe                                     │
│  3. "Prénom ou surnom de votre enfant ?" → ex: "Théo"                       │
│  4. Token lié au nickname dans le compte                                    │
│                                                                             │
│  AJOUT ENFANT (depuis dashboard)                                            │
│  ───────────────────────────────                                            │
│  1. Bouton [+ Ajouter un enfant]                                            │
│  2. Scanner QR code ou saisir token                                         │
│  3. "Prénom ou surnom ?" → ex: "Emma"                                       │
│  4. Nouveau token lié au compte existant                                    │
│                                                                             │
│  FONCTIONNALITÉS V0                                                         │
│  ─────────────────                                                          │
│  • Sélectionner l'enfant concerné (Théo, Emma, etc.)                        │
│  • Écrire un message (texte simple)                                         │
│  • Aide IA reformulation (rendre le message plus clair/pertinent)           │
│  • Voir ses messages envoyés par enfant                                     │
│  • Indicateur statut : "Envoyé" / "Lu" / "Réponse envoyée par email"        │
│                                                                             │
│  RÉPONSES DU MÉDECIN                                                        │
│  ───────────────────                                                        │
│  • Réponses envoyées par EMAIL (jamais affichées sur Parent'aile)           │
│  • Sur Parent'aile : notification "✅ Réponse envoyée sur votre email"      │
│  • Documents joints possibles par email (attestations, etc.)                │
│                                                                             │
│  CE QU'IL NE FAIT PAS (V0)                                                  │
│  ─────────────────────────                                                  │
│  • Pas de pièces jointes côté parent                                        │
│  • Pas de visio                                                             │
│  • Pas d'accès au forum/ateliers/boutique                                   │
│  • Pas de coaching IA conversationnel                                       │
│  • Pas d'affichage des réponses médicales (sécurité)                        │
│                                                                             │
│  IA REFORMULATION                                                           │
│  ────────────────                                                           │
│  L'IA aide le parent à formuler son message de manière :                    │
│  • Plus claire                                                              │
│  • Plus structurée                                                          │
│  • Plus pertinente pour le médecin                                          │
│  → Ce n'est PAS du coaching, c'est de l'aide à la rédaction                 │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Structure Firebase

```javascript
// Collection : accounts (comptes parents)
accounts/
  └── {accountId}/                      // UID Firebase Auth
      ├── email: "parent@email.com"
      ├── createdAt: timestamp
      ├── lastActivity: timestamp
      └── children/                     // sous-collection enfants
          ├── {tokenId1}/
          │   ├── nickname: "Théo"      // prénom/surnom choisi par parent
          │   └── addedAt: timestamp
          └── {tokenId2}/
              ├── nickname: "Emma"
              └── addedAt: timestamp

// Collection : messages
messages/
  └── {messageId}/
      ├── tokenId: "abc123xyz"          // identifie le patient réel
      ├── accountId: "xyz789"           // identifie le compte parent
      ├── content: "Mon fils a du mal à dormir depuis..."
      ├── contentOriginal: "mon fils dort pas bien"  // avant reformulation IA
      ├── sentAt: timestamp
      ├── status: "sent" | "read" | "replied"
      └── repliedAt: timestamp          // date de réponse (si replied)
      // NOTE: Pas de contenu de réponse stocké sur Firebase !
      // La réponse est envoyée par email uniquement
```

**Important :** Les réponses du médecin ne sont JAMAIS stockées sur Firebase.
Seul le statut "replied" et la date sont enregistrés pour informer le parent
qu'une réponse a été envoyée sur son email.

---

## MedCompanion — Plateforme de Soins

### Mission

Centraliser le pilotage des soins : documentation, réflexion, et communication patient.

### Évolution des Modes

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                      MEDCOMPANION — MODES D'INTERFACE                        │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ComboBox Modes (MainWindow)                                                │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                                                                     │   │
│  │   ACCUEIL            ✅ Existant                                    │   │
│  │   Vue d'ensemble, sélection patient                                 │   │
│  │                                                                     │   │
│  │   ─────────────────────────────────────────────────────────────     │   │
│  │                                                                     │   │
│  │   BUREAU             ✅ Existant (Console V1 + Focus Med V2)        │   │
│  │   Documentation post-consultation                                   │   │
│  │   Notes, ordonnances, attestations, courriers                       │   │
│  │   Focus Med : compagnon cognitif                                    │   │
│  │                                                                     │   │
│  │   ─────────────────────────────────────────────────────────────     │   │
│  │                                                                     │   │
│  │   CONSULTATION       🔄 En conception (voir VISION_V2.md)           │   │
│  │   Interface dossier pendant consultation                            │   │
│  │   Layout adaptatif 67/33, Med assistant                             │   │
│  │                                                                     │   │
│  │   ─────────────────────────────────────────────────────────────     │   │
│  │                                                                     │   │
│  │   PILOTAGE           🎯 À créer — PRIORITÉ V0                       │   │
│  │   Gestion tokens patients                                           │   │
│  │   Inbox messages Parent'aile                                        │   │
│  │   Réponses assistées                                                │   │
│  │   (futur: dashboard complet Parent'aile)                            │   │
│  │                                                                     │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Mode Pilotage — Spécifications V0

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         MODE PILOTAGE — V0                                   │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  LAYOUT PROPOSÉ                                                             │
│  ──────────────                                                             │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  PILOTAGE                                              [Tokens] [⚙] │   │
│  ├───────────────────────┬─────────────────────────────────────────────┤   │
│  │                       │                                             │   │
│  │  INBOX (liste)        │  MESSAGE SÉLECTIONNÉ                        │   │
│  │                       │                                             │   │
│  │  ┌─────────────────┐  │  Patient : DUPONT Martin                    │   │
│  │  │ 🔴 DUPONT M.    │  │  Email parent : parent@email.com            │   │
│  │  │    Il y a 2h    │  │  ─────────────────────────────────────      │   │
│  │  ├─────────────────┤  │                                             │   │
│  │  │ 🟡 MARTIN P.    │  │  Message reçu (31/01 14:32) :               │   │
│  │  │    Hier         │  │  "Mon fils a du mal à dormir depuis         │   │
│  │  ├─────────────────┤  │   la rentrée. Il se réveille plusieurs      │   │
│  │  │ 🟢 BERNARD L.   │  │   fois par nuit..."                         │   │
│  │  │    Répondu      │  │                                             │   │
│  │  └─────────────────┘  │  ─────────────────────────────────────      │   │
│  │                       │                                             │   │
│  │  Filtre: [Tous ▼]     │  [Voir dossier patient]                     │   │
│  │                       │                                             │   │
│  │                       │  ─────────────────────────────────────      │   │
│  │                       │                                             │   │
│  │                       │  Réponse par email :                        │   │
│  │                       │  ┌─────────────────────────────────────┐   │   │
│  │                       │  │ Bonjour,                            │   │   │
│  │                       │  │ Suite à votre message...            │   │   │
│  │                       │  └─────────────────────────────────────┘   │   │
│  │                       │                                             │   │
│  │                       │  📎 Pièces jointes : [+ Ajouter]            │   │
│  │                       │     • attestation.pdf                       │   │
│  │                       │                                             │   │
│  │                       │  [💡 Suggestion IA]  [📧 Envoyer par email] │   │
│  │                       │                                             │   │
│  └───────────────────────┴─────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Composants Mode Pilotage

| Composant | Description | Priorité |
|-----------|-------------|----------|
| **Inbox liste** | Liste messages, tri par date/urgence, indicateurs couleur | 🔴 Haute |
| **Vue message** | Contenu, patient réel, email parent, historique | 🔴 Haute |
| **Lien dossier** | Accès rapide au dossier V1 du patient | 🔴 Haute |
| **Zone réponse email** | Écriture + pièces jointes + envoi par EMAIL | 🔴 Haute |
| **Service email** | Configuration SMTP pour envoi des réponses | 🔴 Haute |
| **Suggestion IA** | Brouillon de réponse proposé | 🟡 Moyenne |
| **Gestion tokens** | Créer, voir, révoquer, imprimer QR | 🔴 Haute |
| **Tri IA urgence** | Catégorisation auto des messages | 🟡 Moyenne |

### Gestion Tokens — Interface

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         GESTION TOKENS                                       │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  CRÉER UN TOKEN                                                             │
│  ──────────────                                                             │
│  1. Sélectionner patient dans la liste existante                            │
│  2. Clic "Générer token"                                                    │
│  3. QR code affiché → imprimer ou montrer au parent                         │
│  4. Token enregistré localement avec correspondance patient                 │
│                                                                             │
│  LISTE TOKENS ACTIFS                                                        │
│  ───────────────────                                                        │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ Patient           │ Pseudo        │ Créé le    │ Dernier msg │ Actions│  │
│  ├───────────────────┼───────────────┼────────────┼─────────────┼────────┤  │
│  │ DUPONT Martin     │ MamanDeThéo   │ 15/01/2026 │ Il y a 2h   │ [🗑]   │  │
│  │ MARTIN Paul       │ PapaDeLouis   │ 20/01/2026 │ Hier        │ [🗑]   │  │
│  │ BERNARD Louise    │ (non activé)  │ 28/01/2026 │ -           │ [🗑]   │  │
│  └───────────────────┴───────────────┴────────────┴─────────────┴────────┘  │
│                                                                             │
│  RÉVOQUER UN TOKEN                                                          │
│  ─────────────────                                                          │
│  • Confirmation requise                                                     │
│  • Le parent ne pourra plus envoyer de messages                             │
│  • Historique conservé localement                                           │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Stockage Local MedCompanion

```
Documents/MedCompanion/
├── patients/                          # Existant (dossiers patients)
│   └── DUPONT_Martin/
│       └── ...
│
└── pilotage/                          # NOUVEAU
    ├── tokens.json                    # Table de correspondance
    ├── firebase_config.json           # Config connexion Firebase
    └── messages_cache/                # Cache local des messages (optionnel)
        └── ...
```

**Format tokens.json :**
```json
{
  "tokens": [
    {
      "tokenId": "abc123xyz",
      "patientId": "DUPONT_Martin",
      "patientDisplayName": "DUPONT Martin",
      "pseudo": "MamanDeThéo",
      "createdAt": "2026-01-15T10:30:00Z",
      "active": true,
      "lastActivity": "2026-01-31T14:32:00Z"
    }
  ]
}
```

---

## Pyramide des Niveaux de Service

### Définitions

| Niveau | Définition | Lieu | Cadre |
|--------|------------|------|-------|
| **SOUTENU** | Accès libre aux ressources communautaires | Parent'aile public (grisé V0) | Aucune donnée médicale |
| **ACCOMPAGNÉ** | Relation d'aide structurée | Parent'aile (futur) | Relation d'aide, pas de soin |
| **SOIGNÉ** | Suivi médical à distance validé | MedCompanion Pilotage | Acte médical, secret médical |

### Ticket d'Entrée — Accès aux Soins

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           TICKET D'ENTRÉE                                    │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  PRINCIPE                                                                   │
│  ────────                                                                   │
│  L'accès aux soins à distance est une DÉCISION MÉDICALE.                    │
│  Aucun accès spontané ou auto-initié par les parents.                       │
│                                                                             │
│  MÉCANISME                                                                  │
│  ─────────                                                                  │
│                                                                             │
│  1. Patient vu en cabinet (consultation présentielle obligatoire)           │
│                         │                                                   │
│                         ▼                                                   │
│  2. Décision médicale : soins à distance pertinents ?                       │
│                         │                                                   │
│            ┌────────────┴────────────┐                                      │
│            │                         │                                      │
│           Non                       Oui                                     │
│            │                         │                                      │
│            ▼                         ▼                                      │
│      Suivi classique         3. Génération token (Mode Pilotage)            │
│      (cabinet seul)                  │                                      │
│                                      ▼                                      │
│                              4. QR code remis au parent                     │
│                                      │                                      │
│                                      ▼                                      │
│                              5. Parent active son espace                    │
│                                 (choisit pseudo, écrit messages)            │
│                                      │                                      │
│                                      ▼                                      │
│                              6. Communication via Pilotage                  │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Plan d'Implémentation V0

### Vue d'Ensemble des Briques

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           BRIQUES V0 — VUE GLOBALE                           │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│   PARENT'AILE                          MEDCOMPANION                         │
│                                                                             │
│   ┌─────────────────────┐              ┌─────────────────────┐              │
│   │ B3. Griser          │              │ B1. Mode Pilotage   │              │
│   │     fonctionnalités │              │     (squelette)     │              │
│   │     🔴 Haute        │              │     🔴 Haute        │              │
│   └──────────┬──────────┘              └──────────┬──────────┘              │
│              │                                    │                         │
│              ▼                                    ▼                         │
│   ┌─────────────────────┐              ┌─────────────────────┐              │
│   │ B4. Espace Patient  │              │ B2. Gestion tokens  │              │
│   │     (connexion)     │◄────────────►│                     │              │
│   │     🔴 Haute        │   Token      │     🔴 Haute        │              │
│   └──────────┬──────────┘              └──────────┬──────────┘              │
│              │                                    │                         │
│              ▼                                    │                         │
│   ┌─────────────────────┐                         │                         │
│   │ B5. Espace Patient  │                         │                         │
│   │     (messages)      │                         │                         │
│   │     🔴 Haute        │                         │                         │
│   └──────────┬──────────┘                         │                         │
│              │                                    │                         │
│              │         Firebase                   │                         │
│              └────────────────────────────────────┤                         │
│                                                   ▼                         │
│                                        ┌─────────────────────┐              │
│                                        │ B6. Inbox messages  │              │
│                                        │     🔴 Haute        │              │
│                                        └──────────┬──────────┘              │
│                                                   │                         │
│                                                   ▼                         │
│                                        ┌─────────────────────┐              │
│                                        │ B7. Réponse + push  │              │
│                                        │     🟡 Moyenne      │              │
│                                        └──────────┬──────────┘              │
│                                                   │                         │
│                                                   ▼                         │
│                                        ┌─────────────────────┐              │
│                                        │ B8. IA tri/réponse  │              │
│                                        │     🟡 Moyenne      │              │
│                                        └─────────────────────┘              │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Liste des Briques avec Actions

---

#### Brique 1 : Mode Pilotage — Squelette (MedCompanion)

**Priorité :** 🔴 Haute
**Dépendances :** Aucune
**Projet :** MedCompanion

**Actions à faire :**

| # | Action | Fichier(s) | Détail |
|---|--------|------------|--------|
| 1.1 | Ajouter "Pilotage" dans ComboBox modes | `MainWindow.xaml` | Nouveau item dans la ComboBox existante |
| 1.2 | Créer le UserControl principal | `Views/Pilotage/PilotageControl.xaml` | Layout 2 colonnes (inbox + détail) |
| 1.3 | Créer le ViewModel | `ViewModels/PilotageViewModel.cs` | Propriétés pour tokens, messages, sélection |
| 1.4 | Gérer la visibilité | `MainWindow.xaml.cs` | Afficher PilotageControl quand mode sélectionné |
| 1.5 | Créer le dossier pilotage | `Documents/MedCompanion/pilotage/` | Structure de stockage local |

**Fichiers à créer :**
```
MedCompanion/
├── Views/
│   └── Pilotage/
│       ├── PilotageControl.xaml
│       ├── PilotageControl.xaml.cs
│       ├── InboxListControl.xaml
│       ├── InboxListControl.xaml.cs
│       ├── MessageDetailControl.xaml
│       ├── MessageDetailControl.xaml.cs
│       ├── TokenManagerDialog.xaml
│       └── TokenManagerDialog.xaml.cs
│
├── ViewModels/
│   └── PilotageViewModel.cs
│
└── Services/
    └── PilotageService.cs
```

---

#### Brique 2 : Gestion Tokens (MedCompanion)

**Priorité :** 🔴 Haute
**Dépendances :** Brique 1
**Projet :** MedCompanion

**Actions à faire :**

| # | Action | Fichier(s) | Détail |
|---|--------|------------|--------|
| 2.1 | Créer le service tokens | `Services/TokenService.cs` | CRUD tokens, génération ID unique |
| 2.2 | Créer le modèle Token | `Models/PatientToken.cs` | TokenId, PatientId, Pseudo, Dates, Active |
| 2.3 | Créer la dialog gestion | `Views/Pilotage/TokenManagerDialog.xaml` | Liste tokens, créer, révoquer |
| 2.4 | Générer QR code | `Services/QRCodeService.cs` | Utiliser lib QR (QRCoder ou similaire) |
| 2.5 | Stocker tokens | `pilotage/tokens.json` | Persistence locale |

**Fichiers à créer :**
```
MedCompanion/
├── Models/
│   └── PatientToken.cs
│
└── Services/
    ├── TokenService.cs
    └── QRCodeService.cs
```

---

#### Brique 3 : Griser Fonctionnalités (Parent'aile)

**Priorité :** 🔴 Haute
**Dépendances :** Aucune
**Projet :** Parent'aile

**Actions à faire :**

| # | Action | Fichier(s) | Détail |
|---|--------|------------|--------|
| 3.1 | Créer composant ComingSoon | `components/ui/ComingSoonOverlay.tsx` | Overlay "Bientôt disponible" |
| 3.2 | Protéger route Forum | `App.tsx` + `screens/Forum/` | Rediriger ou afficher overlay |
| 3.3 | Protéger route Ateliers | `App.tsx` + `screens/Workshops/` | Rediriger ou afficher overlay |
| 3.4 | Protéger route Boutique | `App.tsx` + `screens/Shop/` | Rediriger ou afficher overlay |
| 3.5 | Protéger route Teleconsultation | `App.tsx` + `screens/Teleconsultation/` | Renommer en "Faire le point" + overlay |
| 3.6 | Adapter Landing page | `screens/ParentAile/ParentAile.tsx` | Message V0, liens vers Espace Patient |
| 3.7 | Adapter navigation | `components/ui/shortcut-bar.tsx` | Masquer ou griser les liens |

---

#### Brique 4 : Espace Patient — Connexion (Parent'aile)

**Priorité :** 🔴 Haute
**Dépendances :** Briques 2, 3
**Projet :** Parent'aile

**Actions à faire :**

| # | Action | Fichier(s) | Détail |
|---|--------|------------|--------|
| 4.1 | Créer route /espace | `App.tsx` | Nouvelle route protégée |
| 4.2 | Créer page connexion token | `screens/Espace/TokenLogin.tsx` | Saisie manuelle ou scan QR |
| 4.3 | Valider token Firebase | `lib/firebase.ts` | Vérifier existence token dans MedCompanion |
| 4.4 | Créer page inscription | `screens/Espace/Register.tsx` | Email + mot de passe (premier enfant) |
| 4.5 | Créer page nickname enfant | `screens/Espace/ChildNickname.tsx` | "Prénom ou surnom de votre enfant ?" |
| 4.6 | Créer dashboard parent | `screens/Espace/Dashboard.tsx` | Liste enfants + bouton ajouter |
| 4.7 | Créer page ajout enfant | `screens/Espace/AddChild.tsx` | Nouveau token + nickname |
| 4.8 | Créer layout Espace | `screens/Espace/EspaceLayout.tsx` | Header simplifié, navigation minimale |
| 4.9 | Stocker session | `lib/patientSession.ts` | Account + tokens en session |

**Fichiers à créer :**
```
parentaile/src/
├── screens/
│   └── Espace/
│       ├── index.tsx
│       ├── TokenLogin.tsx          # Entrée par token (QR ou saisie)
│       ├── Register.tsx            # Inscription email + mdp
│       ├── Login.tsx               # Connexion email + mdp
│       ├── ChildNickname.tsx       # Choix prénom/surnom enfant
│       ├── Dashboard.tsx           # Liste enfants + actions
│       ├── AddChild.tsx            # Ajouter un token/enfant
│       ├── EspaceLayout.tsx        # Layout commun
│       ├── MessageList.tsx         # Historique messages
│       └── NewMessage.tsx          # Nouveau message
│
└── lib/
    └── patientSession.ts           # Gestion session parent
```

---

#### Brique 5 : Espace Patient — Messages (Parent'aile)

**Priorité :** 🔴 Haute
**Dépendances :** Brique 4
**Projet :** Parent'aile

**Actions à faire :**

| # | Action | Fichier(s) | Détail |
|---|--------|------------|--------|
| 5.1 | Créer page liste messages | `screens/Espace/MessageList.tsx` | Historique par enfant |
| 5.2 | Créer sélecteur enfant | `screens/Espace/MessageList.tsx` | Choisir pour quel enfant voir/écrire |
| 5.3 | Créer page nouveau message | `screens/Espace/NewMessage.tsx` | Formulaire + reformulation IA |
| 5.4 | Adapter IA reformulation | `lib/prompts.ts` | Prompt spécifique reformulation |
| 5.5 | Envoyer message Firebase | `lib/firebase.ts` | Push dans collection messages avec tokenId |
| 5.6 | Afficher statut message | `screens/Espace/MessageList.tsx` | Envoyé / Lu / Réponse envoyée par email |
| 5.7 | Notification réponse | `screens/Espace/MessageList.tsx` | "✅ Réponse envoyée sur votre email" |

**Note importante :** Les réponses du médecin ne sont PAS affichées sur Parent'aile.
Seul un indicateur "Réponse envoyée sur votre email" est affiché pour des raisons de sécurité.

---

#### Brique 6 : Inbox Messages (MedCompanion)

**Priorité :** 🔴 Haute
**Dépendances :** Brique 5
**Projet :** MedCompanion

**Actions à faire :**

| # | Action | Fichier(s) | Détail |
|---|--------|------------|--------|
| 6.1 | Créer service Firebase | `Services/FirebaseService.cs` | Connexion + lecture messages |
| 6.2 | Implémenter InboxListControl | `Views/Pilotage/InboxListControl.xaml` | Liste avec indicateurs |
| 6.3 | Implémenter MessageDetailControl | `Views/Pilotage/MessageDetailControl.xaml` | Affichage détail + contexte patient |
| 6.4 | Faire correspondance token→patient | `Services/TokenService.cs` | Lookup dans tokens.json |
| 6.5 | Afficher nom réel + pseudo | `Views/Pilotage/MessageDetailControl.xaml` | "DUPONT Martin (MamanDeThéo)" |
| 6.6 | Lien vers dossier patient | `Views/Pilotage/MessageDetailControl.xaml` | Bouton "Voir dossier" → ouvre V1 |

**Fichiers à créer :**
```
MedCompanion/
└── Services/
    └── FirebaseService.cs
```

---

#### Brique 7 : Réponse par Email (MedCompanion)

**Priorité :** 🔴 Haute
**Dépendances :** Brique 6
**Projet :** MedCompanion

**Actions à faire :**

| # | Action | Fichier(s) | Détail |
|---|--------|------------|--------|
| 7.1 | Ajouter zone réponse | `Views/Pilotage/MessageDetailControl.xaml` | TextBox + zone pièces jointes |
| 7.2 | Créer service email | `Services/EmailService.cs` | Configuration SMTP, envoi emails |
| 7.3 | Créer template email | `Assets/Templates/EmailTemplate.html` | En-tête cabinet, signature |
| 7.4 | Gérer pièces jointes | `Views/Pilotage/MessageDetailControl.xaml` | Ajout documents (attestations, etc.) |
| 7.5 | Envoyer email | `Services/EmailService.cs` | Envoi réponse + PJ au parent |
| 7.6 | Mettre à jour statut Firebase | `Services/FirebaseService.cs` | status = "replied" + repliedAt |
| 7.7 | Sauvegarder localement | `Services/PilotageService.cs` | Historique réponses dans dossier patient |
| 7.8 | Feedback envoi | `Views/Pilotage/MessageDetailControl.xaml` | Confirmation visuelle |
| 7.9 | Rafraîchir inbox | `ViewModels/PilotageViewModel.cs` | Update liste après envoi |

**Fichiers à créer :**
```
MedCompanion/
├── Services/
│   └── EmailService.cs              # Service envoi emails SMTP
│
└── Assets/
    └── Templates/
        └── EmailTemplate.html       # Template réponse email
```

**Configuration requise :**
```json
// pilotage/email_config.json
{
  "smtp": {
    "host": "smtp.example.com",
    "port": 587,
    "useSsl": true,
    "username": "cabinet@example.com",
    "password": "***"  // ou clé d'app
  },
  "sender": {
    "name": "Cabinet Dr. XXX",
    "email": "cabinet@example.com"
  }
}
```

---

#### Brique 8 : IA Tri et Réponse Assistée (MedCompanion)

**Priorité :** 🟡 Moyenne
**Dépendances :** Brique 7
**Projet :** MedCompanion

**Actions à faire :**

| # | Action | Fichier(s) | Détail |
|---|--------|------------|--------|
| 8.1 | Créer service tri IA | `Services/MessageTriageService.cs` | Catégorisation urgence |
| 8.2 | Définir catégories | `Models/MessageCategory.cs` | Urgent / À voir / Info |
| 8.3 | Afficher indicateurs | `Views/Pilotage/InboxListControl.xaml` | 🔴 🟡 🟢 |
| 8.4 | Créer service suggestion | `Services/ResponseSuggestionService.cs` | Brouillon IA |
| 8.5 | Bouton "Suggestion IA" | `Views/Pilotage/MessageDetailControl.xaml` | Génère brouillon |
| 8.6 | Résumé contexte patient | `Services/PatientContextService.cs` | Infos clés du dossier |

---

## Ordre d'Exécution Recommandé

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         ORDRE D'EXÉCUTION V0                                 │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  PHASE 1 — Fondations (en parallèle)                                        │
│  ───────────────────────────────────                                        │
│                                                                             │
│  MedCompanion                         Parent'aile                           │
│  ┌─────────────────────┐              ┌─────────────────────┐              │
│  │ B1. Mode Pilotage   │              │ B3. Griser          │              │
│  │     (squelette)     │              │     fonctionnalités │              │
│  └──────────┬──────────┘              └──────────┬──────────┘              │
│             │                                    │                         │
│             ▼                                    │                         │
│  ┌─────────────────────┐                         │                         │
│  │ B2. Gestion tokens  │                         │                         │
│  └──────────┬──────────┘                         │                         │
│             │                                    │                         │
│             └──────────────┬─────────────────────┘                         │
│                            │                                               │
│                            ▼                                               │
│  PHASE 2 — Espace Patient                                                  │
│  ────────────────────────                                                  │
│                                                                             │
│              ┌─────────────────────┐                                       │
│              │ B4. Connexion token │                                       │
│              └──────────┬──────────┘                                       │
│                         │                                                  │
│                         ▼                                                  │
│              ┌─────────────────────┐                                       │
│              │ B5. Messages        │                                       │
│              └──────────┬──────────┘                                       │
│                         │                                                  │
│                         ▼                                                  │
│  PHASE 3 — Inbox MedCompanion                                              │
│  ────────────────────────────                                              │
│                                                                             │
│              ┌─────────────────────┐                                       │
│              │ B6. Inbox messages  │                                       │
│              └──────────┬──────────┘                                       │
│                         │                                                  │
│                         ▼                                                  │
│              ┌─────────────────────┐                                       │
│              │ B7. Réponse + push  │                                       │
│              └──────────┬──────────┘                                       │
│                         │                                                  │
│                         ▼                                                  │
│  PHASE 4 — Intelligence (optionnel V0)                                     │
│  ─────────────────────────────────────                                     │
│                                                                             │
│              ┌─────────────────────┐                                       │
│              │ B8. IA tri/réponse  │                                       │
│              └─────────────────────┘                                       │
│                                                                             │
│                         │                                                  │
│                         ▼                                                  │
│                    ✅ V0 COMPLÈTE                                          │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## Contraintes Éthiques

### Principes Non-Négociables

| Principe | Application |
|----------|-------------|
| **Pas de soin sans cabinet** | Tout patient soigné a d'abord été vu en présentiel |
| **Ticket d'entrée médical** | L'accès aux soins à distance est une décision du médecin |
| **Séparation soutien/soin** | Parent'aile ≠ MedCompanion, jamais de confusion |
| **Données réelles = local** | Aucun nom réel sur Firebase, correspondance dans MedCompanion |
| **IA assistante, pas décisionnaire** | L'IA reformule/suggère, l'humain valide toujours |
| **Consentement explicite** | Partage avec intervenants = accord documenté |

### Ce Qui Est Interdit

- Accès spontané aux soins (sans passage cabinet + token)
- Réponse automatique IA sans validation humaine
- Stockage de données médicales sur Firebase
- Nom réel du patient visible sur Parent'aile
- Fusion des bases de données

---

## Critères de Succès V0

### Fonctionnels

- [x] Médecin peut générer un token pour un patient ✅ (Brique 2 complète)
- [x] Médecin peut imprimer QR code avec infos patient ✅ (TokenPdfService)
- [ ] Parent peut se connecter avec token (email + mot de passe)
- [ ] Parent peut donner un nickname à son enfant
- [ ] Parent peut ajouter d'autres enfants (multi-tokens)
- [ ] Parent peut écrire un message pour un enfant spécifique
- [ ] Message bénéficie de l'aide reformulation IA
- [ ] Message arrive dans inbox Pilotage (MedCompanion)
- [ ] Médecin voit nom réel du patient + email parent
- [ ] Médecin peut répondre par EMAIL (pas sur Parent'aile)
- [ ] Médecin peut joindre des documents à l'email
- [ ] Parent reçoit réponse par email
- [ ] Parent voit notification "Réponse envoyée sur votre email" sur Parent'aile
- [ ] Fonctionnalités Parent'aile grisées avec "Bientôt"

### Non-Fonctionnels

- [ ] Aucune donnée médicale sur Firebase
- [ ] Aucune réponse médicale stockée sur Firebase (sécurité)
- [ ] Correspondance token↔patient uniquement locale
- [ ] Email parent stocké pour envoi réponses
- [ ] Historique réponses sauvegardé localement dans dossier patient
- [ ] Réduction perçue de la pression messages/appels

---

## Glossaire

| Terme | Définition |
|-------|------------|
| **Parent'aile** | Plateforme web de soutien parental, ouverte à tous |
| **MedCompanion** | Application desktop professionnelle du médecin |
| **Mode Pilotage** | Nouveau mode MedCompanion pour gérer messages patients |
| **Espace Patient** | Zone Parent'aile accessible uniquement avec token |
| **Token** | Identifiant unique généré par médecin, lié à un patient |
| **Nickname** | Prénom/surnom de l'enfant choisi par le parent pour l'identifier |
| **Compte parent** | Compte unique (email) pouvant gérer plusieurs tokens/enfants |
| **Correspondance** | Lien token↔patient réel, stocké localement dans MedCompanion |
| **Réponse email** | Réponse du médecin envoyée par email (jamais sur Parent'aile) |
| **Grisé** | Fonctionnalité visible mais inaccessible ("Bientôt disponible") |

---

## Documents Liés

- [VISION_V2.md](VISION_V2.md) — Vision MedCompanion V2 (Focus Med, Mémoire, Mode Consultation)
- [CLAUDE.md](CLAUDE.md) — Documentation technique MedCompanion
- Parent'aile repo : https://github.com/tourderoc/parentaile.git

---

**Auteur :** Discussion Claude Code + Utilisateur
**Dernière mise à jour :** 31/01/2026
