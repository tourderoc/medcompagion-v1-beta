# PLAN — Phase d'Évaluation Ciblée V0

> **Docs associés :** [PLAN_MODE_CONSULTATION_V0A.md](PLAN_MODE_CONSULTATION_V0A.md), [PLAN_MODE_URGENCE_V0.md](PLAN_MODE_URGENCE_V0.md), [VISION_V2.md](VISION_V2.md)
> **Statut :** Brouillon validé pour démarrage — implémentation prévue sur plusieurs séances
> **Date :** 2026-05-28

---

## 1. Vision & principes directeurs

### 1.1 Le constat clinique qui a déclenché ce plan

Le parcours clinique en pédopsychiatrie ne se fait PAS en N consultations fixes :
- certaines évaluations sont bouclées en 1 séance
- d'autres prennent 3-5 séances étalées sur des semaines
- les hypothèses évoluent dans le temps
- l'ordre d'exploration dépend de ce que l'enfant amène

Architecture précédente envisagée : **parcours rigide** → rejetée car non fidèle à la pratique réelle.

### 1.2 Modèle retenu : "chapitre linéaire avec marque-page"

L'évaluation se pense comme **rédiger un chapitre**, pas comme remplir un formulaire :
- on commence où on en est
- on s'arrête quand le temps de la séance est écoulé
- on reprend exactement à la même position la fois d'après
- on finit quand on a terminé — pas avant, pas après

L'évaluation comporte **3 étapes linéaires** :
1. **Préparation clinique** (durée typique : 5-30 min)
2. **Évaluation ciblée** (durée variable : 1 à plusieurs séances)
3. **Cartographie clinique globale** (durée typique : 15-30 min)

**Le marque-page est au niveau de l'étape** (pas plus fin). Si on s'arrête au milieu de l'Étape 1, on retrouve l'Étape 1 à la reprise, avec tout ce qui a été saisi.

### 1.3 Principes non-négociables

1. **Linéarité des étapes** : on ne saute pas. Étape 1 → Étape 2 → Étape 3 dans cet ordre.
2. **Persistance multi-séances** : une évaluation peut s'étaler sur N consultations. L'état est sauvegardé entre les séances.
3. **Validation médecin partout** : tout ce que Med produit est PROPOSITION. Le médecin valide, modifie, rejette. Rien n'est automatique.
4. **Une seule évaluation active à la fois par patient.** Pas de phases parallèles.
5. **La note de consultation de chaque séance reste l'objet médico-légal primaire.** L'évaluation est un travail clinique, la note est le document officiel.
6. **Anti-checklist DSM** : l'output d'une exploration d'axe est une observation narrative, pas une case cochée.

---

## 2. UI/UX cible

### 2.1 Porte d'entrée : le combo "+"

Le combo "+" (déjà existant pour ajouter une consultation) reçoit une 3e option :

```
┌─ Combo "+" ──────────────────────┐
│  • 1ère consultation             │
│  • Suivi                         │
│  • Phase d'évaluation  ← NOUVEAU │
└──────────────────────────────────┘
```

Le libellé du combo est **stable** : il dit toujours "Phase d'évaluation", quel que soit l'état. C'est dans la zone Actions que l'état apparaît.

### 2.2 Zone Actions : 3 états visibles

```
┌── Cas 1 : aucune évaluation jamais commencée ────┐
│ Aucune évaluation en cours pour ce patient.      │
│ [ ▶ Commencer ]                                  │
└──────────────────────────────────────────────────┘

┌── Cas 2 : évaluation en cours, séance active ────┐
│ Étape 1 — Préparation clinique                   │
│ [contenu UI de l'étape]                          │
│ [✓ Valider l'étape]  [⏸ Terminer la séance]      │
└──────────────────────────────────────────────────┘

┌── Cas 3 : évaluation suspendue (entre séances) ──┐
│ 📖 Évaluation en cours                            │
│ Marque-page : Étape 1 — Préparation               │
│ Dernière session : il y a 12 jours                │
│ [ ▶ Poursuivre ]                                  │
└──────────────────────────────────────────────────┘
```

### 2.3 Progressive disclosure des étapes

Au sein d'une séance active (Cas 2), une fois Étape 1 validée, Étape 2 **s'affiche en dessous** (ou remplace selon design choisi). Idem Étape 2 → Étape 3.

L'utilisateur ne voit jamais Étape 2 tant que Étape 1 n'est pas validée. Anti-overload.

### 2.4 Indicateur discret côté patient (optionnel V0, recommandé V0.1)

Quand une évaluation est en cours, un mini-badge `📖 Évaluation en cours` apparaît dans le header du patient, sous "Né(e) le ...". Permet de voir l'état sans cliquer sur le combo.

### 2.5 Bouton "⏸ Terminer la séance"

- Sauvegarde tout ce qui a été saisi
- Persiste le marque-page (étape courante)
- Ferme la zone Actions
- **N'a pas de différence sémantique entre "pause" et "fin de séance"** — c'est juste "j'arrête ici"
- Au retour, le bouton "▶ Poursuivre" reprend exactement à l'état sauvegardé

---

## 3. Modèle de données

### 3.1 Stockage

Une évaluation = un fichier Markdown + YAML header **persistant et éditable**, dans le dossier patient :

```
patients/LASTNAME_Firstname/
└── evaluations/
    └── 2026-05-28_evaluation_001.md   ← évaluation en cours / finie
```

Contrairement aux notes (immuables), **le fichier d'évaluation est éditable** au cours de l'évaluation : on revient dessus, on complète, on corrige. Une fois clôturée, il devient immuable.

### 3.2 Format YAML

```yaml
---
type: evaluation
patient: "TROCHU Noah"
date_debut: 2026-05-28T14:00
date_derniere_modif: 2026-06-04T15:30
date_cloture: null              # rempli quand finie

etape_courante: 2               # 1 / 2 / 3
etape_1_validee: true
etape_2_validee: false
etape_3_validee: false

# Données structurées par étape (rempli au fur et à mesure)
preparation:
  hypotheses_principales: [...]
  differentiels: [...]
  a_eliminer: [...]
  points_vigilance: [...]
  questions_cliniques: [...]
  validation_medecin_date: 2026-05-28T14:25

evaluation_ciblee:
  axes_explores: [...]
  observations: [...]
  ...

cartographie:
  ...
---

# Corps Markdown : narration libre, observations, etc.
```

### 3.3 Une seule évaluation active par patient

Si une évaluation existe avec `date_cloture: null`, c'est elle qui est active. Sinon, "Commencer" en crée une nouvelle.

Historique des évaluations passées accessible via le dossier `evaluations/` (et plus tard via un onglet "Historique évaluations" du dossier bleu).

---

## 4. Étape 1 — Préparation clinique (V0)

### 4.1 Objectif clinique

5-30 min, avant de commencer l'évaluation ciblée. Organiser la pensée :
- Quelles sont mes hypothèses principales ?
- Quels différentiels dois-je écarter ?
- Que dois-je éliminer prudemment ?
- Quels points méritent vigilance ?
- Quelles questions cliniques dois-je résoudre ?

### 4.2 UI

```
ÉTAPE 1 — PRÉPARATION CLINIQUE

┌─ Contexte (lecture seule, généré auto) ──────────┐
│ Patient : 7 ans, motif : "agitation scolaire"    │
│ Synthèse récente : ...                            │
│ Observations consult précédente : ...             │
└──────────────────────────────────────────────────┘

[ ✨ Suggérer (IA) ]

┌─ Hypothèses principales (éditable) ──────────────┐
│ • TDAH                                            │
│ • [+ ajouter]                                     │
└──────────────────────────────────────────────────┘

┌─ Diagnostics différentiels ──────────────────────┐
│ • Anxiété                                         │
│ • Trouble apprentissages                          │
│ • [+ ajouter]                                     │
└──────────────────────────────────────────────────┘

┌─ Diagnostics à éliminer ─────────────────────────┐
│ • Dépression                                      │
│ • TSA                                             │
│ • [+ ajouter]                                     │
└──────────────────────────────────────────────────┘

┌─ Points de vigilance ────────────────────────────┐
│ • Sommeil — réveils nocturnes mentionnés          │
│ • [+ ajouter]                                     │
└──────────────────────────────────────────────────┘

┌─ Questions cliniques à résoudre ─────────────────┐
│ • Vrai trouble attentionnel ?                     │
│ • Rôle du sommeil ?                               │
│ • Impact émotionnel ?                             │
│ • Retentissement scolaire ?                       │
│ • [+ ajouter]                                     │
└──────────────────────────────────────────────────┘

[ ⏸ Terminer la séance ]   [ ✓ Valider l'étape 1 ]
```

### 4.3 Génération LLM

Le bouton **`✨ Suggérer (IA)`** envoie au LLM :
- Synthèse globale du patient
- Observations cliniques précédentes (dernière consult au moins)
- Motif de consultation
- Âge confirmé
- Données développementales si disponibles

Prompt : "Tu es pédopsychiatre. Sur la base de ces éléments, propose au médecin des hypothèses principales (max 3), diagnostics différentiels (max 4), diagnostics à éliminer prudemment (max 3), points de vigilance (max 4), et questions cliniques à résoudre (max 5)."

Réponse JSON parsée → remplit les 5 zones. **Tout est éditable** : ajout, modification, suppression libre.

### 4.4 Discipline du prompt LLM (anti-dérives)

- **Cap dur sur le nombre** (3-5 selon catégorie)
- **Justification courte obligatoire** pour chaque item ("pourquoi ce différentiel pour cet enfant ?")
- **Pas d'hypothèses 'par sécurité'** sans justification clinique précise
- **Pas d'introduction d'axes que les données ne supportent pas**
- **Reconnaître la non-pertinence explicite** : le LLM peut dire "rien à signaler côté X" plutôt que gratter

### 4.5 Validation médecin

Le bouton **`✓ Valider l'étape 1`** :
- Persiste les 5 catégories validées
- Marque `etape_1_validee: true` + `validation_medecin_date`
- Fait apparaître Étape 2 (ou la rend disponible si workflow "remplace")

---

## 5. Étape 2 — Évaluation ciblée (V0.1 — séance ultérieure)

### 5.1 Objectif clinique

Explorer cliniquement les axes pertinents en fonction des hypothèses validées. **Sans script rigide.** Le médecin explore ce que l'enfant amène, dans l'ordre qui fait sens à chaque consult.

### 5.2 Génération des axes

Le LLM, à partir des hypothèses validées en Étape 1 + de l'âge, propose des **axes d'attention clinique** organisés en 3 catégories :

- **Axes principaux** (de l'hypothèse principale)
- **Différentiels** (à creuser pour écarter ou confirmer)
- **Facteurs systémiques** (familial, scolaire, écrans, etc.)

Exemple pour suspicion TDAH :

```
AXES PRINCIPAUX (TDAH)
  • Attention
  • Impulsivité
  • Hyperactivité
  • Fonctions exécutives
  • Retentissement scolaire

DIFFÉRENTIELS
  • Anxiété
  • Sommeil
  • Apprentissages

FACTEURS SYSTÉMIQUES
  • Écrans
  • Fonctionnement familial
  • Estime de soi
```

### 5.3 Exploration libre par axe

Chaque axe a 4 états visuels :
- ⚪ Non abordé
- ◐ Brièvement abordé
- ◉ Exploré
- ✓ Consolidé

L'output d'une exploration est une **observation narrative libre** (texte), pas une case cochée. Le LLM peut proposer 2-3 questions ouvertes par axe (intégrables dans le dialogue clinique), mais elles ne sont pas un script obligatoire.

### 5.4 Re-anchorage des hypothèses

Possibilité explicite de **modifier les hypothèses** mid-évaluation. Si en séance 2 on bascule sur "anxiété" plutôt que "TDAH", on retourne en Étape 1 pour ré-ancrer, et les axes sont régénérés. Ce qui a été observé n'est PAS perdu — il est conservé dans le corpus de l'évaluation.

### 5.5 Mécanisme de clôture

Med doit aider à clôturer (sans imposer) :
- Indicateur "maturité de l'étape" basé sur les états d'axes
- Quand la plupart des axes principaux sont au moins "explorés", proposition discrète : "Vous pouvez passer à l'Étape 3 si vous estimez avoir les éléments."
- Jamais bloquant. Toujours validation médecin.

---

## 6. Étape 3 — Cartographie clinique globale (V0.2 — séance ultérieure)

### 6.1 Objectif clinique

Sortir du tout-diagnostic. À ce stade, on intègre :
- Comment l'enfant fonctionne (cognition, affect, relations)
- Comment la famille fonctionne (dynamiques, ressources, contraintes)
- Comment l'environnement fonctionne (école, pairs, contexte social)
- Ce qui entretient la souffrance
- Ce qui protège
- Ce qui pourrait aider

### 6.2 Format

Pas une checklist. Un **document narratif structuré** produit par le LLM à partir de toute l'évaluation (étape 1 + 2), puis retravaillé par le médecin.

Sections proposées (modulaires) :
- Fonctionnement individuel
- Dynamique familiale
- Environnement scolaire / social
- Diagnostic intégré (si retenu)
- Facteurs entretenant la souffrance
- Facteurs protecteurs
- Pistes thérapeutiques

### 6.3 Output

Un document Markdown propre, éditable, qui devient la **synthèse de l'évaluation**. Source pour :
- La restitution aux parents (V0.3+)
- L'orientation thérapeutique
- L'historique du dossier

### 6.4 Clôture définitive

À la validation finale de l'Étape 3, l'évaluation reçoit `date_cloture` + devient immuable. Plus possible d'éditer (sauf via nouvelle évaluation, ou correction d'erreur tracée).

---

## 7. Les 6 garde-fous architecturaux

Issus de l'analyse critique. À implémenter sans exception :

1. **Mécanisme de clôture de phase** — indicateurs de maturité par étape, proposition non-bloquante
2. **Fil rouge clinique narratif** — au début de chaque séance reprenant une évaluation, Med affiche un mini-résumé (5-10 lignes) régénéré
3. **Re-anchorage explicite des hypothèses** — possibilité de retourner Étape 1 sans perdre Étape 2
4. **Discipline anti-checklist** — observations narratives libres, pas de cases cochées
5. **Note de consultation standalone par séance** — la consultation produit toujours sa note (médico-légal), distincte de l'évaluation persistante
6. **Cap sur le nombre d'éléments générés par LLM** — 3-5 max par catégorie + justification clinique courte obligatoire

---

## 8. Plan de réalisation par étapes

### V0 — Fondations + Étape 1 Préparation (cette séance + 1 ou 2 suivantes)
- [ ] Modèle `EvaluationPhase` (POCO + sérialisation YAML+MD)
- [ ] `EvaluationPhaseService` (CRUD : create, load active, save, close)
- [ ] Ajout option "Phase d'évaluation" dans le combo "+"
- [ ] Zone Actions avec 3 états (jamais commencé / en cours / suspendu)
- [ ] UI Étape 1 — Préparation clinique (5 catégories éditables)
- [ ] Service LLM `PreparationSuggesterService` + prompt dédié
- [ ] Bouton "Valider l'étape 1" + persistance
- [ ] Bouton "Terminer la séance" + persistance marque-page
- [ ] Bouton "Poursuivre" + reprise à l'état exact
- [ ] Tests : créer, suspendre, reprendre, finir Étape 1

### V0.1 — Étape 2 Évaluation ciblée
- [ ] UI Étape 2 — axes par catégorie avec états visuels
- [ ] Génération LLM des axes à partir des hypothèses validées
- [ ] Exploration libre par axe (texte narratif)
- [ ] Cap d'axes + justification courte
- [ ] Re-anchorage des hypothèses
- [ ] Indicateur de maturité de l'étape

### V0.2 — Étape 3 Cartographie globale
- [ ] UI Étape 3 — document narratif éditable
- [ ] Génération LLM cartographique à partir de toute l'évaluation
- [ ] Bouton "Clôturer l'évaluation" + immuabilisation

### V0.3 — Polish et intégration
- [ ] Badge "📖 Évaluation en cours" dans le header patient
- [ ] Fil rouge clinique au début de chaque séance qui reprend une éval
- [ ] Onglet "Historique évaluations" dans le dossier bleu
- [ ] Note de consultation auto-générée résumant ce qui a été fait dans la séance

### V1 — Restitution évaluation aux parents (optionnel)
- [ ] Template PDF restitution évaluation (style cohérent avec restitution 1ère consult)
- [ ] Génération hybride LLM + templates fixes

---

## 9. Risques identifiés & mitigations

| Risque | Niveau | Mitigation |
|---|---|---|
| Évaluation interminable, jamais clôturée | ÉLEVÉ | Indicateur maturité par étape + proposition de clôture non-bloquante |
| Fragmentation cognitive multi-séances | ÉLEVÉ | Fil rouge clinique en début de chaque séance |
| Glissement vers checklist DSM | ÉLEVÉ | Outputs narratifs, pas de cases cochées, axes = "attentions" pas "critères" |
| LLM trop large dans ses suggestions | MOYEN | Cap dur 3-5 par catégorie + justification clinique courte obligatoire |
| Enfermement dans la première hypothèse | MOYEN | Re-anchorage explicite Étape 1 ↔ Étape 2 |
| Perte de continuité si autre clinicien voit l'enfant | MOYEN | Note de consultation standalone par séance (objet médico-légal) |
| Sur-objectivation = appauvrissement clinique | MOYEN | Zone "observations libres" non-structurée toujours présente |
| Latence LLM dégrade l'UX | FAIBLE | Génération asynchrone + indicateurs visuels d'attente |

---

## 10. Hors-scope V0 (pour plus tard)

- Restitution évaluation aux parents (V1)
- Comparaison entre évaluations successives du même patient
- Évolution temporelle des hypothèses sur plusieurs évaluations
- Suggestions inter-patients (anonymisées) — "des patients similaires ont aussi exploré X"
- Export PDF complet de l'évaluation pour transmission

---

## 11. Décisions validées

| Question | Décision |
|---|---|
| Modèle de structure | ✅ Chapitre linéaire avec marque-page (pas axes parallèles) |
| Ordre des étapes | ✅ Linéaire : 1 → 2 → 3, pas de saut |
| Persistance | ✅ Multi-séances avec reprise exacte de la position |
| Une seule évaluation active par patient | ✅ Confirmé |
| UI porte d'entrée | ✅ Item "Phase d'évaluation" dans le combo "+" |
| UI état | ✅ Dans la zone Actions (3 états : commencer / en cours / poursuivre) |
| Progressive disclosure | ✅ Étape 2 visible après validation Étape 1, etc. |
| Bouton Terminer = Pause | ✅ Pas de différence sémantique, juste "j'arrête ici" |
| Output des axes | ✅ Narratif libre, pas de cases cochées |
| Note de consult / Évaluation | ✅ Deux objets distincts. Note = médico-légal. Évaluation = travail clinique persistant. |
| Roadmap | ✅ V0 = Étape 1 seulement. Étapes 2 et 3 dans V0.1 / V0.2 |

---

## 12. Questions ouvertes (à arbitrer au moment d'implémenter)

1. **Progressive disclosure Étape 2** : Étape 2 s'affiche EN DESSOUS d'Étape 1 (long scroll), ou REMPLACE Étape 1 (avec navigation tabs) ?
2. **Format du marque-page** : juste "Étape 1" ou plus fin (ex: "Étape 1, dernière catégorie modifiée : Différentiels") ?
3. **Indicateur de maturité** : binaire (mature / pas mature) ou progressif (% axes explorés) ?
4. **Notification de revoyure** : si une évaluation est suspendue depuis > X jours, faut-il un rappel discret ?
