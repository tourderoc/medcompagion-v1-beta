# Plan révisé — Cartographie de l'environnement V0 (Étape 5)

> **Document parent** : [PLAN_CARTOGRAPHIE_ENVIRONNEMENT_V0.md](PLAN_CARTOGRAPHIE_ENVIRONNEMENT_V0.md)
> **Statut** : révision verrouillée avec l'utilisateur (2026-06-01)
> **Source clinique** : Dr Lassoued, *Tome 3 — Qui éduque nos enfants ?* (Feuilles dimensionnelles, 3-11 ans)

---

## Pourquoi cette révision

Le plan V0 initial décrit fidèlement le **modèle de données**, le **contenu canonique** des 5 feuilles et le **scoring déterministe**. Cette base est conservée intégralement.

Trois décisions verrouillées lors de la session du 2026-06-01 ne sont pas couvertes par le plan initial et nécessitent l'ajout / la modification documentée ci-dessous :

| # | Décision | Statut plan initial | Action ici |
|---|---|---|---|
| **D3** | Lecture LLM à deux niveaux : par feuille (à la validation de chaque feuille) + branche globale (à la validation de l'étape 5) | « Pas de LLM en V0 » | **Réintroduction V0** : c'est la valeur clinique principale, ne pas reporter. |
| **D4** | Voix Med dans les prompts | Non mentionné | **Déféré V1.0 (Restitution Parents)** : à l'Étape 5 le lecteur est le psy lui-même → ton clinique sobre. La voix du livre n'a de valeur qu'à la restitution aux parents. |
| **D6** | Restitution = feuille dessinée (nervures coloriées + zone de confluence) | « Hors scope V0 » | **Maintenir hors V0 mais réservé** : la feuille dessinée vient avec l'étape Restitution Parents, pas avec la saisie. La saisie reste en tableau. |

Décisions inchangées : **D1** (pas de tige → Projet Thérapeutique), **D2** (saisie 100% manuelle par le psy), **D5** (saisie en tableau dépliable).

> **Note importante sur D4** : à l'Étape 5, le lecteur des sorties LLM = le pédopsychiatre lui-même pendant la consultation. Il a besoin d'une lecture **clinique sobre et directe**, pas de métaphores organiques. La voix du livre (Tome 3) deviendra centrale à l'**Étape Restitution Parents (V1.0)** où le destinataire change — ce sont les parents qui lisent, et la traduction par images organiques du livre prend alors tout son sens. Inutile d'investir dans un style pack maintenant.

---

## Ce qui change vs plan initial

### 1. Lecture LLM par feuille (D3)

À la **fermeture d'une feuille** (clic sur ▴ ou changement de feuille), si la feuille est **complète** (tous items répondus ou explicitement passés), Med produit une **lecture courte** affichée en bas de la feuille dépliée et persistée en YAML.

**Format de la lecture par feuille** (3-5 lignes max) :
1. Une **observation** sur la forme dessinée par les nervures (ce qui tient, ce qui craque)
2. Un **point d'attention clinique** (à creuser, à questionner)
3. **Aucune suggestion d'action** (réservé Projet Thérapeutique — D1 verrouillé)

**Prompt par feuille** (gabarit, ton clinique sobre — destinataire = pédopsychiatre en consultation) :

```
Tu es un assistant clinique destiné à un pédopsychiatre qui lit la cartographie
environnementale d'un enfant qu'il suit.

Feuille : {label_feuille} — {sous_titre}
Patient : {age} ans, motif : {motif_court}

Nervure centrale ({label_centrale}) : {score_centrale}/{max_centrale} → {couleur_centrale}
{items_centrale_avec_oui_non}

Nervures secondaires :
- {label_sec_A} : {score}/{max} → {couleur}  | items : {liste}
- {label_sec_B} : ...
- {label_sec_C} : ...
- {label_sec_D} : ...

Couleur globale de la feuille : {couleur_feuille}

CONSIGNE :
- 3 à 5 lignes maximum, ton clinique direct (pas de métaphores poétiques).
- Décris l'équilibre/déséquilibre entre nervure centrale et secondaires.
- Pointe UN élément clinique à creuser en consultation.
- INTERDIT : suggestions d'action, conseils aux parents, plan thérapeutique
  (réservé Projet Thérapeutique).
- Si la feuille est verte uniforme : nomme la solidité, n'invente pas un problème.
```

**Service** : `Services/Evaluations/FeuilleLectureService.cs` (nouveau, calqué sur `SyntheseSuggesterService`)
- `Task<(bool ok, string? lecture, string? error)> ReadFeuilleAsync(FeuilleEnvironnement, int? age, string motif, CancellationToken)`
- Timeout 60s, maxTokens 800 (réponse courte)
- Pas de JSON, juste du texte (1 seule sortie)

**Stockage** : sous chaque feuille dans le YAML
```yaml
cartographie_environnement:
  feuilles:
    famille:
      centrale: [true, true, true, true, true]
      liens_familiaux: [true, true, true]
      ...
      lecture_med: |
        La nervure centrale tient — le socle est posé.
        Mais la branche du vécu émotionnel craque sur l'item du médiateur :
        l'enfant porte peut-être ce qui ne lui revient pas. À questionner.
      lecture_date: 2026-06-04T15:30
```

### 2. Lecture LLM globale de la branche (D3)

À la **validation de l'étape 5** (toutes les feuilles complètes ou explicitement skippées), Med produit une **lecture globale de la branche environnement** qui croise les 5 feuilles.

**Format de la lecture globale** (8-12 lignes, structurée) :
1. La **forme de la branche** (quelles feuilles sont solides, lesquelles trouées)
2. La **zone de confluence** : où les difficultés convergent (ex : tensions Famille + Écrans = repli)
3. Le **point clinique le plus saillant** à creuser avec la famille
4. **Aucune action / aucune tige** — strictement descriptif.

**Prompt branche globale** (ton clinique sobre) :

```
Tu es un assistant clinique destiné à un pédopsychiatre qui synthétise
la branche environnement complète d'un enfant qu'il suit.

Patient : {age} ans, motif : {motif_court}

Les 5 feuilles de la branche, dans l'ordre :

1. Famille (Le Socle) — couleur : {couleur} — lecture : {lecture_feuille_1}
2. École & Pairs (L'Espace Social) — couleur : {couleur} — lecture : {lecture_feuille_2}
3. Écrans & Médias (L'Influence Numérique) — couleur : {couleur} — lecture : {lecture_feuille_3}
4. Valeurs sociétales (L'Alignement au Monde) — couleur : {couleur} — lecture : {lecture_feuille_4}
5. Cadre éducatif (La Structure Invisible) — couleur : {couleur} — lecture : {lecture_feuille_5}

CONSIGNE :
- 8 à 12 lignes structurées en 3 paragraphes courts :
  a) État global de la branche : feuilles solides vs feuilles fragiles.
  b) Zone de convergence : où plusieurs feuilles fragiles se rejoignent (interaction clinique).
  c) Point central à creuser en consultation suivante.
- Ton clinique direct, pas de métaphores poétiques.
- INTERDIT : suggestions d'action, conseils, plan, prescription.
- Pas de redite littérale des 5 lectures — synthèse, pas concaténation.
```

**Service** : `Services/Evaluations/BrancheEnvironnementLectureService.cs`
- `Task<(bool ok, string? lecture, string? error)> ReadBrancheAsync(CartographieEnvironnement, int? age, string motif, CancellationToken)`
- Timeout 90s (input plus long), maxTokens 1200

**Stockage** : au niveau racine de la cartographie
```yaml
cartographie_environnement:
  ...
  feuilles: { ... }
  lecture_branche_med: |
    La branche tient sur le cadre éducatif et les valeurs, mais elle
    craque côté Famille / Écrans : la zone de confluence est ici, dans
    le repli numérique qui prend le relais d'une parole familiale empêchée.

    [...]
  lecture_branche_date: 2026-06-04T16:10
```

### 3. Voix du livre — déferée V1.0 Restitution Parents (D4)

**Décision** : ne PAS investir dans un style pack « voix Tome 3 » pour l'Étape 5.

**Raison** : à l'Étape 5, le lecteur des sorties LLM est le pédopsychiatre pendant la consultation. Il veut un ton clinique sobre et direct, pas des métaphores organiques. Les images du livre (feuille, nervure, sève, confluence) ne servent leur fonction de traduction qu'au moment où le destinataire change — c'est-à-dire à la **Restitution Parents (V1.0)**, où les parents lisent la sortie.

**Conséquence pratique V0** :
- Pas de fichier `Resources/VoixMed_Tome3.md`
- Pas de style guide, pas de few-shot examples
- Les deux prompts ci-dessus (§1, §2) utilisent un ton clinique simple

**Garde-fou anti-prescription** (conservé) : la consigne `INTERDIT : suggestions d'action, conseils, plan, prescription` est dans chaque prompt. Heuristique post-LLM sur verbes impératifs (`il faut`, `vous devriez`, `proposer de`, `mettre en place`) → bloc « ⚠ Reformulation suggérée » avec bouton relancer. Cette protection reste utile indépendamment du ton.

**À documenter pour V1.0** (Restitution Parents) : c'est à ce moment-là qu'on construira le style pack, à partir d'extraits choisis du Tome 3, et qu'on retravaillera les prompts pour transformer les lectures cliniques en lectures parents-friendly. Hors périmètre actuel.

### 4. Restitution feuille dessinée (D6 — hors V0 mais réservé)

**Périmètre V0 confirmé** : saisie en tableau (plan initial §5), **pas de feuille dessinée**.

**Réservation pour V0.5** (étape Restitution Parents, déjà au backlog) :
- Visualisation graphique de la feuille : nervure centrale verticale + nervures secondaires obliques
- Coloriage des nervures selon score
- Zone de confluence centrale : intersection des couleurs (logique de fusion à définir avec Dr Lassoued — pas dans V0)
- Export PDF infographie A4

**Non bloquant pour V0** : la branche peut être validée et exploitée cliniquement sans la feuille dessinée ; la feuille dessinée est un outil de **restitution aux parents**, pas d'évaluation.

---

## Ce qui reste inchangé vs plan initial

Tous ces points du plan initial sont **conservés tels quels** et ne sont pas redocumentés ici :

| Section plan initial | Statut |
|---|---|
| §2.1 Récupération de l'âge | inchangé |
| §2.2 Enum `EvaluationStep` (déjà avec `CartographieEnvironnement = 5`) | inchangé |
| §2.3 Algorithme de couleur (seuils nervures + règle nervure centrale prioritaire) | inchangé |
| §2.4 Item médiateur reformulé en positif | inchangé |
| §3.1 à §3.4 Modèle de données + service de scoring | inchangé |
| §4 Contenu canonique des 5 feuilles (verbatim Tome 3) | inchangé |
| §5 UI tableau dépliable | inchangé (D5) |
| §6 ViewModel | inchangé, **augmenté** (cf. ci-dessous) |
| §7 Persistance YAML | inchangé, **augmentée** (lectures Med) |
| §8 Intégration au flow | inchangé |
| §9 Cohérence avec Projet Thérapeutique | inchangé (D1) |

---

## Ce qui s'ajoute au ViewModel

`CartographieEnvironnementViewModel` (plan initial §6) reçoit :

```csharp
public class FeuilleViewModel : INotifyPropertyChanged
{
    // ... champs existants ...

    public string? LectureMed { get; set; }
    public DateTime? LectureDate { get; set; }
    public bool IsLectureEnCours { get; set; }
    public ICommand RelancerLectureCommand { get; }     // bouton 🔄
    public ICommand EffacerLectureCommand { get; }      // bouton 🗑

    // Déclenchement automatique : à la fermeture de l'expander OU au
    // clic sur "Voir la lecture de Med", SI la feuille est complète.
}

public class CartographieEnvironnementViewModel : INotifyPropertyChanged
{
    // ... champs existants ...

    public string? LectureBrancheMed { get; set; }
    public DateTime? LectureBrancheDate { get; set; }
    public bool IsLectureBrancheEnCours { get; set; }
    public ICommand RelancerLectureBrancheCommand { get; }

    // Déclenchement : bouton "🟢 Valider l'étape 5" exécute :
    //  1. Si lecture branche absente → la générer (loader)
    //  2. Afficher la lecture pour relecture par le psy
    //  3. Bouton "Valider" final pose ValidationDate
}
```

**Comportement attendu** :
- Lecture par feuille : générée à la fermeture de l'expander (ou clic explicite « 🪶 Lire »), affichée en bloc grisé sous la dernière nervure, éditable manuellement par le psy (TextBox).
- Lecture branche : générée au clic « Valider l'étape 5 », affichée dans un encart distinct en haut/bas du panneau, éditable, puis validée par un second clic « ✓ Confirmer la clôture ».

---

## Ce qui s'ajoute à la persistance

Le bloc YAML défini au §7.1 du plan initial est augmenté :

```yaml
cartographie_environnement:
  age_au_moment_de_la_saisie: 8
  validation_date: 2026-06-04T15:45
  lecture_branche_med: |
    [texte multiligne de la lecture globale]
  lecture_branche_date: 2026-06-04T16:10
  feuilles:
    famille:
      centrale: [true, true, true, true, true]
      liens_familiaux: [true, true, true]
      vecu_emotionnel: [true, true, false]
      messages_educatifs: [true, true, true]
      comportement_enfant: [true, false, true]
      lecture_med: |
        [texte multiligne de la lecture par feuille]
      lecture_date: 2026-06-04T15:30
    ecole_pairs:
      ...
```

Section Markdown miroir (lisible par le psy dans le `.md` patient) :

```markdown
## Étape 5 — Cartographie de l'environnement

**Synthèse branche** : 🟡 Jaune

### Feuille 1 — Famille (Le Socle) — 🟢 Vert
- Fonction parentale effective : 5/5 🟢
- Liens familiaux : 3/3 🟢
- Vécu émotionnel : 2/3 🟡
- Messages éducatifs : 3/3 🟢
- Comportement de l'enfant : 2/3 🟡

**Lecture Med :**
> La nervure centrale tient — le socle est posé. Mais la branche du
> vécu émotionnel craque sur l'item du médiateur : l'enfant porte
> peut-être ce qui ne lui revient pas. À questionner.

### Feuille 2 — École & Pairs (L'Espace Social) — 🔴 Rouge
...

### Lecture globale de la branche

> La branche tient sur le cadre éducatif et les valeurs, mais elle
> craque côté Famille / Écrans : la zone de confluence est ici...
```

---

## Fichiers impactés (récap)

### Nouveaux fichiers
- `Services/Evaluations/FeuilleLectureService.cs` — LLM lecture par feuille (D3)
- `Services/Evaluations/BrancheEnvironnementLectureService.cs` — LLM lecture branche globale (D3)
- Plan initial : tous les fichiers déjà listés (Models, contenu canonique, scoring, VM, vue)

### Fichiers modifiés (vs plan initial)
- `ViewModels/Evaluations/CartographieEnvironnementViewModel.cs` — ajout commandes + état lectures
- `Services/Evaluations/EvaluationPhaseService.cs` — sérialisation lectures Med dans YAML + Markdown
- `ViewModels/ConsultationModeViewModel.cs` — injection des 2 nouveaux services LLM
- `Views/Consultation/ConsultationModeControl.xaml.cs` — signature Initialize étendue
- `Views/Consultation/Evaluation/EvaluationPhaseControl.xaml` — encarts lecture (par feuille + branche)

### Fichiers inchangés
- `Models/Evaluations/CartographieEnvironnement.cs` (couvert plan initial)
- `Models/Evaluations/CartographieEnvironnementContent.cs` (couvert plan initial)
- `Services/Evaluations/EnvironnementScoringService.cs` (couvert plan initial)

---

## Plan d'implémentation phasé (révisé)

| Phase | Contenu | Estimation |
|---|---|---|
| **V0.0** | Modèle + contenu canonique + scoring (plan initial inchangé) | 1 séance |
| **V0.1** | UI tableau dépliable + binding + ViewModel sans LLM | 1-2 séances |
| **V0.2** | Persistance YAML/Markdown basique (sans lectures Med) | 1 séance |
| **V0.3** | Lecture LLM par feuille (D3 part 1) | 1 séance |
| **V0.4** | Lecture LLM branche globale (D3 part 2) | 1 séance |
| **V0.5** | Tests cliniques sur 2-3 patients réels + ajustements ton | 1-2 séances |
| **V1.0** *(post-V0)* | Restitution Parents avec feuille dessinée (D6) | hors V0 |

**Note** : les phases V0.3-V0.4 ajoutent la valeur clinique principale (lectures Med). On peut les coder dans l'ordre, mais V0.3 doit être testée avant V0.4 — la lecture par feuille nourrit la lecture branche.

---

## Décisions verrouillées (récap final)

| # | Décision | Source |
|---|---|---|
| D1 | Pas de tige / pas de suggestion d'action — réservé Projet Thérapeutique | session 2026-06-01 |
| D2 | Saisie 100% manuelle par le psy — pas d'autofill LLM des items | session 2026-06-01 |
| D3 | Lecture LLM 2 niveaux : par feuille + branche globale | session 2026-06-01 |
| D4 | Voix du livre **déférée V1.0 Restitution Parents** — à l'Étape 5 le lecteur est le psy → ton clinique sobre | session 2026-06-01 |
| D5 | Saisie en tableau dépliable | session 2026-06-01 |
| D6 | Restitution feuille dessinée — réservée étape Restitution Parents (post-V0) | session 2026-06-01 |
| D7 | Patient 3-11 ans uniquement (hérité Étape 4) | plan initial §1 |
| D8 | Scoring 3 niveaux (Vert/Jaune/Rouge), nervure centrale prioritaire | plan initial §2.3 |
| D9 | Item médiateur reformulé en positif (Oui = 1 universel) | plan initial §2.4 |
| D10 | Pas de seuil minimal de remplissage avant validation | plan initial §8.3 |

---

## Garde-fous spécifiques aux lectures LLM

1. **Hallucination clinique** : le prompt force le LLM à ne référencer QUE les items cochés/non-cochés et les couleurs calculées. Pas d'inférence sur âge, motif, hors-périmètre.
2. **Voix prescriptive** : heuristique post-LLM sur verbes impératifs courants (`il faut`, `mettre en place`, `proposer`, `vous devriez`). Si détecté → bloc « ⚠ Reformulation suggérée » avec bouton relancer.
3. **Performance** : si le modèle local met >60s par feuille, on permet de **skipper la lecture** et de valider sans (la feuille reste exploitable cliniquement).
4. **Édition humaine** : toutes les lectures Med sont **éditables** par le psy après génération (TextBox). Le `.md` final reflète la version éditée, pas la version LLM brute.
5. **Confidentialité** : les lectures ne sortent jamais du dossier patient. Aucune télémétrie. Cohérent avec le bouton « Décharger Med » déjà implémenté.
