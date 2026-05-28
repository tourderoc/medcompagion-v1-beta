# PLAN — Mode Urgence Clinique V0 (Risque Suicidaire)

> **Doc associé :** [PLAN_MODE_CONSULTATION_V0A.md](PLAN_MODE_CONSULTATION_V0A.md), [VISION_V2.md](VISION_V2.md)
> **Statut :** Brouillon, pour validation avant implémentation
> **Date :** 2026-05-28

---

## 1. Vision & principes directeurs

### 1.1 Objectif
Ajouter à MedCompanion Mode Consultation un **système transversal d'urgence clinique** qui :
- **détecte** des signaux d'alerte dans le matériel d'une consultation (transcription, extraction IA, notes structurées) ;
- **propose** au médecin d'ouvrir une évaluation structurée, sans jamais l'imposer ;
- **structure** l'évaluation selon une échelle clinique reconnue ;
- **trace** chaque décision pour valeur médico-légale.

### 1.2 Première urgence implémentée : Risque suicidaire
Cas d'usage le plus fréquent en pédopsychiatrie, le plus engageant médico-légalement, et le mieux outillé scientifiquement (C-SSRS).

### 1.3 Principes non-négociables
1. **L'IA détecte, le médecin évalue, le médecin décide.** L'IA ne pose JAMAIS un niveau de risque seule.
2. **Aucune ouverture automatique du mode urgence.** Le déclenchement passe par un **chip de proposition** validé par le médecin (anti-faux-positif + anti-alert-fatigue).
3. **Détection post-sauvegarde** uniquement (pas pendant la dictée live). On évalue à froid, avec tout le contexte de la note structurée.
4. **Chaque consultation est analysée indépendamment.** Une urgence écartée à la consult N n'empêche pas la détection à la consult N+1. Une évaluation effectuée à la consult N reste consultable, mais ne dispense pas le détecteur d'analyser la consult N+1.
5. **Co-localisation avec les notes** : l'évaluation d'urgence est enregistrée dans le **même dossier** que la note qui a déclenché l'alerte (`{patient}/{année}/notes/`), avec un timestamp strictement postérieur → elle apparaît naturellement juste après la note source dans toute liste chronologique.
6. **Pas de notifications externes en V0** (push, email, SMS). Tout reste dans MedCompanion.
7. **Architecture extensible** : module générique `UrgenceClinique` pour ajouter d'autres urgences plus tard sans refacto (maltraitance, décompensation psychotique, anorexie sévère…).
8. **Audit médico-légal** : chaque détection + chaque décision = fichier immuable horodaté dans le dossier patient.

---

## 2. Architecture cible

### 2.1 Vue d'ensemble du flux

```
┌─────────────────────────────────────────────────────────────┐
│  1. Médecin sauvegarde une note (1ère consult OU suivi)     │
│     → événement NoteSavedToPatient                          │
└─────────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────────┐
│  2. UrgenceDispatcher reçoit la note                         │
│     → distribue à tous les UrgenceDetectors enregistrés      │
└─────────────────────────────────────────────────────────────┘
                          │
                          ▼
┌─────────────────────────────────────────────────────────────┐
│  3. SuicideRiskDetector (LLM) analyse la note                │
│     → renvoie UrgenceSignal{ type, confidence, passages }   │
└─────────────────────────────────────────────────────────────┘
                          │
                ┌─────────┴──────────┐
                │ confidence > seuil │
                ▼                    ▼
            ┌──────┐              ┌──────┐
            │ OUI  │              │ NON  │
            └──┬───┘              └──────┘
               │                   (rien)
               ▼
┌─────────────────────────────────────────────────────────────┐
│  4. Chip de proposition s'affiche en haut de la zone Note   │
│     ⚠️ Signal d'alerte détecté — Évaluer le risque ?         │
│     [Ouvrir évaluation] [Voir passages] [Écarter]            │
└─────────────────────────────────────────────────────────────┘
                          │
                ┌─────────┴──────────┐
                │ Médecin clique     │
                ▼                    ▼
        ┌──────────────┐     ┌────────────────┐
        │ Ouvrir éval. │     │ Écarter        │
        └──────┬───────┘     └────────┬───────┘
               │                       │
               ▼                       ▼
   ┌──────────────────────┐    ┌──────────────────────┐
   │ ModeUrgence ouvert   │    │ Log décision écarter │
   │ (panneau latéral)    │    │ (avec motif optionnel)│
   └──────────────────────┘    └──────────────────────┘
```

### 2.2 Composants nouveaux

| Composant | Type | Rôle |
|---|---|---|
| `UrgenceSignal` | Record/Model | Encapsule un signal détecté : type, confidence, passages, timestamp |
| `IUrgenceDetector` | Interface | Contrat : `Task<UrgenceSignal?> DetectAsync(NoteContext)` |
| `SuicideRiskDetector` | Service (impl) | Première implémentation, via LLM avec prompt dédié |
| `UrgenceDispatcher` | Service | Reçoit `NoteSavedToPatient`, distribue à tous les détecteurs, émet les signaux |
| `UrgenceChipViewModel` | ViewModel | État du chip (visible, dismissed, type d'urgence) |
| `UrgenceEvaluationViewModel` | ViewModel | Questionnaire structuré (C-SSRS adapté âge) + score + plan d'action |
| `UrgenceLogService` | Service | Persistance des détections + décisions dans `{patient}/urgences/` |
| `UrgenceChipControl` | UserControl | Le chip orange dans la zone Note |
| `ModeUrgenceControl` | UserControl | Le panneau d'évaluation structurée |

### 2.3 Stockage — co-localisation avec les notes

Les évaluations d'urgence sont enregistrées **au même endroit que les notes de consultation**, dans `{patient}/{année}/notes/`. Convention de nommage :

```
patients/LASTNAME_Firstname/
└── 2026/
    └── notes/
        ├── 2026-05-28_152300_suivi.md                                   ← note de consultation
        ├── 2026-05-28_152334_signal_risque_suicidaire.md                ← signal brut détecté (auto)
        ├── 2026-05-28_152812_evaluation_risque_suicidaire.md            ← évaluation médecin
        └── 2026-06-04_141520_suivi.md                                   ← consult suivante (analysée indépendamment)
```

**Conséquences UX :**
- Dans la frise chronologique des consultations, l'évaluation apparaît visuellement collée à la note qui l'a déclenchée (tri naturel par timestamp).
- Une icône ⚠️ ambre sur la carte de consultation signale qu'une évaluation d'urgence a été produite pour cette consult.
- Un onglet "Historique urgences" filtré accessible depuis le dossier bleu reste utile (montre toutes les évaluations toutes consults confondues, chronologique inversé).

**Format des fichiers :** Markdown + YAML header (encoding UTF-8), comme le reste du dossier patient. **Immuables** : on n'écrase JAMAIS une évaluation précédente, on en crée une nouvelle avec suffixe `_v2.md`, `_v3.md`, etc. qui référence l'ancienne dans le YAML.

**Préfixes pour faciliter le filtrage** :
- `*_signal_*.md` : détections automatiques brutes
- `*_evaluation_*.md` : évaluations validées par le médecin

---

## 3. Le détecteur LLM (SuicideRiskDetector)

### 3.1 Input
La note structurée fraîchement sauvegardée + métadonnées :
- Âge confirmé du patient (V0b — bloc Âge)
- Type de consultation (1ère consult / suivi)
- Motif détecté (si disponible)

### 3.2 Prompt LLM (esquisse)

```
Tu es un assistant clinique spécialisé en pédopsychiatrie. Ta tâche :
analyser cette note de consultation et déterminer s'il y a des signaux
de risque suicidaire qui justifient une évaluation structurée.

ATTENTION : tu NE poses PAS un niveau de risque. Tu identifies SEULEMENT
si un médecin devrait approfondir l'évaluation.

Signaux à rechercher (explicites ET implicites) :
- Idéation suicidaire directe ("envie de mourir", "en finir")
- Idéation indirecte ("tout le monde serait mieux sans moi",
  "j'aimerais juste dormir et plus me réveiller", "ça sert à rien")
- Mention d'un scénario, d'un moyen, d'une date
- Antécédents de TS
- Auto-mutilations
- Désinvestissement massif récent
- Adieux, don d'objets personnels
- Anhédonie sévère + désespoir

Signaux à NE PAS considérer comme alerte seule :
- Tristesse ou pleurs sans contenu suicidaire
- Mention de la mort d'un proche (deuil)
- Métaphores ("je suis mort de fatigue")
- Idées de fuite/fugue sans dimension auto-agressive

Renvoie un JSON :
{
  "alert": true/false,
  "confidence": 0.0 à 1.0,
  "passages": ["citation exacte 1", "citation exacte 2"],
  "motif_pedopsy": "explication brève adaptée à l'âge"
}

Note à analyser :
{NOTE_CONTENT}

Âge du patient : {AGE}
Type de consultation : {TYPE}
```

### 3.3 Seuils & faux positifs

| Confidence | Action |
|---|---|
| `< 0.4` | Aucun signal émis, rien n'apparaît |
| `0.4 – 0.7` | Signal "faible" → chip apparaît, libellé prudent |
| `> 0.7` | Signal "fort" → chip apparaît, légèrement plus saillant (icône clignote 3x à l'apparition) |

**Validation :** avant mise en prod, je proposerai de tester le détecteur sur 10-20 anciennes notes (anonymisées) dont tu connais le verdict réel → mesure faux positifs / faux négatifs, ajustement du seuil.

---

## 4. Le chip de proposition (anti-faux-positif)

### 4.1 Design visuel

```
┌─────────────────────────────────────────────────────────────────┐
│ ⚠️  Signal d'alerte détecté dans cette note —                   │
│     évaluer le risque suicidaire ?                              │
│                                                                 │
│     [📋 Ouvrir évaluation]  [👁 Voir passages]  [✖ Écarter]     │
└─────────────────────────────────────────────────────────────────┘
```

- Couleur fond : `#FFF3CD` (ambre clair) — **pas** rouge (pas une alarme, c'est une proposition)
- Bordure gauche épaisse : `#F39C12` (orange)
- Position : juste au-dessus de la note structurée venant d'être sauvegardée
- Largeur : pleine largeur de la colonne Note
- Hauteur : compact (~70px)
- Auto-dismiss soft après 15 min d'inactivité → le chip se replie en pastille discrète en haut à droite ("⚠ 1 signal en attente"), restant accessible

### 4.2 Trois actions

1. **Ouvrir évaluation** → ouvre `ModeUrgenceControl` en panneau latéral droit (slide-in, n'écrase pas la note)
2. **Voir passages** → popover qui surligne les citations exactes que le LLM a relevées dans la note
3. **Écarter** → demande optionnellement un motif court (faux positif, jugement clinique, autre) → log puis disparaît

Aucune des 3 actions n'est destructive : tout est tracé dans `urgences/`.

---

## 5. Le mode urgence (ModeUrgenceControl)

### 5.1 Layout
Panneau latéral droit, ~480px de largeur, slide-in. **Pas modal** : le médecin garde la vue sur la note.

```
┌────────────────────────────────────────────┐
│ ⚠️ ÉVALUATION RISQUE SUICIDAIRE            │
│ Patient : Noah TROCHU, 7 ans                │
│ Consult : Suivi du 28/05/2026               │
│                                            │
│ ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ │
│                                            │
│ 📋 1. IDÉATION SUICIDAIRE       [▼]        │
│    Présence ? ○ Non  ○ Vague  ● Précise    │
│    Fréquence : [_______________________]   │
│                                            │
│ 📋 2. INTENTIONNALITÉ           [▼]        │
│    ...                                     │
│                                            │
│ 📋 3. SCÉNARIO / PLAN           [▼]        │
│ 📋 4. ACCÈS AUX MOYENS          [▼]        │
│ 📋 5. IMPULSIVITÉ               [▼]        │
│ 📋 6. ANTÉCÉDENTS               [▼]        │
│ 📋 7. FACTEURS PROTECTEURS      [▼]        │
│ 📋 8. DANGEROSITÉ GLOBALE       [▼]        │
│                                            │
│ ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ │
│ ÉVALUATION DU MÉDECIN                      │
│ Niveau de risque :                         │
│ ○ Faible  ○ Modéré  ○ Élevé                │
│                                            │
│ PLAN D'ACTION                              │
│ □ Information parents                      │
│ □ Contrat de sécurité                      │
│ □ Orientation CMP urgence                  │
│ □ SAMU / urgences pédiatriques             │
│ □ Hospitalisation                          │
│ □ Revoyure dans : [____] jours             │
│                                            │
│ Notes libres :                             │
│ ┌────────────────────────────────────────┐ │
│ │                                        │ │
│ └────────────────────────────────────────┘ │
│                                            │
│ [💾 Enregistrer évaluation]                │
└────────────────────────────────────────────┘
```

### 5.2 Adaptation pédiatrique par âge

Le questionnaire **change** selon l'âge confirmé du patient :

- **< 7 ans** : items très simplifiés, questionnement indirect (jeu, dessin), pas de section "scénario" (rarement structuré), accent sur impulsivité + accès moyens (médicaments parents)
- **7-12 ans** : items intermédiaires, questionnement direct possible mais avec vocabulaire adapté, sections "scénario" et "intentionnalité" présentes mais simplifiées
- **13+ ans** : C-SSRS pleine version, tous les items, questionnement direct

Implémentation : 3 jeux de templates de questions dans `Services/Urgence/Templates/`.

### 5.3 Aide LLM optionnelle dans le mode urgence

Pour chaque section, un petit bouton "💡 Suggestion Med" qui demande au LLM, à partir de la note + de ce qui a été coché jusque-là, de **proposer** une formulation pour les notes libres. **Toujours éditable.**

---

## 6. Audit médico-légal

### 6.1 Ce qu'on trace, pour chaque signal détecté

```yaml
---
type: signal
urgence: risque_suicidaire
patient_id: TROCHU_Noah
detection_date: 2026-05-28T15:23:34
note_source: 2026/notes/suivi_2026-05-28_152300.md
detecteur: SuicideRiskDetector_v1
confidence: 0.62
passages:
  - "il dit que ça sert plus à rien"
  - "il aimerait juste dormir et plus se réveiller"
medecin_action: ouvert_evaluation   # ouvert_evaluation | ecarte | timeout
medecin_action_date: 2026-05-28T15:24:11
medecin_action_motif: ""
---
```

### 6.2 Ce qu'on trace, pour chaque évaluation

```yaml
---
type: evaluation
urgence: risque_suicidaire
patient_id: TROCHU_Noah
evaluation_date: 2026-05-28T15:28:12
signal_lie: signal_2026-05-28_152334.md
medecin: Dr ...
niveau_risque: modere
checklist:
  ideation_suicidaire: precise
  intentionnalite: ambivalente
  scenario: absent
  acces_moyens: oui_medicaments_parents
  impulsivite: forte
  antecedents: aucun
  facteurs_protecteurs: bonne_alliance_familiale
  dangerosite_globale: moderee
plan_action:
  - information_parents
  - contrat_securite
  - revoyure_jours: 3
notes_libres: |
  ...
---

# Évaluation complète (texte structuré)
...
```

Ces fichiers sont **immuables**. Si tu modifies une évaluation 1h après, on crée un nouveau fichier `evaluation_..._v2.md` qui référence l'ancien.

---

## 7. Plan de réalisation par étapes

### Étape 1 — Fondations (architecture extensible)
- [ ] Créer `Models/Urgences/UrgenceSignal.cs`
- [ ] Créer `Services/Urgence/IUrgenceDetector.cs`
- [ ] Créer `Services/Urgence/UrgenceDispatcher.cs`
- [ ] Créer `Services/Urgence/UrgenceLogService.cs` (écriture YAML+MD)
- [ ] Câbler `NoteSavedToPatient` → `UrgenceDispatcher`
- [ ] Tests : un détecteur factice qui renvoie toujours `alert=true`, vérifier que le chip apparaît

### Étape 2 — SuicideRiskDetector LLM
- [ ] Créer `Services/Urgence/SuicideRiskDetector.cs`
- [ ] Prompt dédié dans `Services/LLM/Prompts/suicide_risk_detection.txt`
- [ ] Parser JSON robuste (fallback si LLM renvoie format imparfait)
- [ ] Logs détaillés en debug

### Étape 3 — Chip de proposition (UI)
- [ ] Créer `Views/Consultation/Urgence/UrgenceChipControl.xaml(.cs)`
- [ ] `UrgenceChipViewModel` avec état visible/dismissed
- [ ] Intégrer en haut de la zone Note dans `ConsultationModeControl.xaml`
- [ ] Actions : Ouvrir / Voir passages / Écarter
- [ ] Auto-dismiss 15 min + pastille de rappel

### Étape 4 — Mode urgence (questionnaire)
- [ ] Créer `Views/Consultation/Urgence/ModeUrgenceControl.xaml(.cs)`
- [ ] `UrgenceEvaluationViewModel` avec les 8 sections
- [ ] Templates de questions par tranche d'âge (3 fichiers)
- [ ] Slide-in panneau latéral droit, 480px
- [ ] Enregistrement → fichier MD immuable dans `urgences/`

### Étape 5 — Validation clinique
**Corpus de validation disponible** : 10 notes cliniques fictives (7-20 ans) fournies par le médecin, avec verdict clinique de référence — voir `notes_risque_suicidaire_medcompanion.pdf`.

Distribution du corpus :
| Cas | Profil | Verdict clinique attendu | Piège anti-faux-positif |
|---|---|---|---|
| 1 | Garçon 12 ans, "j'aimerais ne plus être là" | Modéré | — |
| 2 | Fille 15 ans, scarifications + recherche moyens | Élevé | — |
| 3 | Garçon 17 ans, TS médicamenteuse récente | Élevé | — |
| 4 | Fille 13 ans, refus scolaire + idées noires | Faible | — |
| 5 | Garçon 9 ans, propos suicidaires en colère | **Pas de risque** | Propos non structurés en contexte affectif |
| 6 | Fille 18 ans, scénario pendaison + matériel | Élevé | — |
| 7 | Garçon 14 ans, repli + post réseaux sociaux | Modéré à élevé | — |
| 8 | Fille 11 ans, propos de mort après deuil grand-père | **Pas de risque structuré** | **Deuil — faux-positif classique à éviter** |
| 9 | Garçon 16 ans, alcool + conduite dangereuse | Vulnérabilité indirecte | — |
| 10 | Fille 20 ans, dépression sévère + accumulation médic. | Très élevé | — |

**Tâches** :
- [ ] Faire passer les 10 notes dans `SuicideRiskDetector` après implémentation étape 2
- [ ] Vérifier que chaque cas est correctement classé (alerte ou pas) et que la confidence est cohérente avec le verdict
- [ ] **Cas critiques anti-faux-positif** : 5 et 8 — le détecteur DOIT comprendre que "propos de mort lors d'un deuil" ou "propos suicidaires en colère sans intentionnalité" ne sont PAS des alertes
- [ ] Ajuster le seuil de confidence si nécessaire
- [ ] Documenter les résultats dans un nouveau fichier `VALIDATION_DETECTEUR_RISQUE_SUICIDAIRE.md`

### Étape 6 — Aide LLM dans l'évaluation (optionnel V0)
- [ ] Bouton "💡 Suggestion Med" par section
- [ ] Affichage en mode édition, jamais imposé

---

## 8. Risques identifiés & mitigations

| Risque | Niveau | Mitigation |
|---|---|---|
| Faux positifs nombreux → alert fatigue → chip ignoré | ÉLEVÉ | Étape 5 de validation obligatoire avant prod. Seuil ajusté. Permettre d'écarter avec motif tracé. |
| Faux négatifs (passage critique manqué par le LLM) | ÉLEVÉ | Le système **ne remplace PAS la vigilance clinique**, c'est un FILET DE SÉCURITÉ supplémentaire. Documenté dans la doc utilisateur. |
| Question médico-légale en cas d'incident | ÉLEVÉ | Audit immuable complet (signal + décision médecin + évaluation). Décision finale = médecin, jamais IA. |
| LLM lent (>10s) bloque l'UI | MOYEN | Détection asynchrone, le chip apparaît quand prêt, n'empêche pas la suite du flow. |
| Stigmatisation du dossier (le mot "suicide" dans tous les patients) | MOYEN | Évaluation n'apparaît PAS dans la synthèse globale du dossier par défaut. Visible uniquement via onglet dédié "Urgences" dans le dossier bleu. |
| Patient mineur — information parents | ÉLEVÉ | La case "Information parents" est dans le plan d'action mais c'est au médecin de décider. Documenté. |

---

## 9. Hors-scope V0 (pour plus tard)

- Autres urgences (maltraitance, anorexie, psychose…) — l'architecture est prête, on les ajoutera comme nouveaux `IUrgenceDetector`
- **Notifications externes** (push téléphone, email, SMS, intégration Parent'aile) — décision explicite : tout reste dans MedCompanion pour V0
- Rappel automatique de revoyure après évaluation "élevée"
- Dashboard "urgences en cours" multi-patients
- Export PDF de l'évaluation pour transmission urgences/CMP
- Lien automatique avec contacts d'urgence du dossier patient

---

## 10. Décisions validées

| Question | Décision |
|---|---|
| Détection consult par consult | ✅ Indépendante — chaque consult est ré-analysée, même si la précédente avait écarté un signal |
| Où ranger les évaluations | ✅ Dans `{patient}/{année}/notes/`, juste après la note source (co-localisation chronologique) |
| Notifications externes | ✅ Hors-scope V0 — pas de système de notification |
| Corpus de validation | ✅ Fourni : 10 notes fictives 7-20 ans avec verdicts cliniques |
