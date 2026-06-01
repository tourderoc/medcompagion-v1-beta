# Plan d'implémentation — Étape 5 : Cartographie de l'environnement (V0)

**Source clinique** : Dr Lassoued, *Grilles de Cotation Canoniques — Les Feuilles Dimensionnelles (Tome 3)* (3-11 ans).
**Statut** : V0, mode médecin uniquement.
**Position dans le flow** : après étape 4 (Cartographie de l'enfant), dernière étape avant clôture de la Phase d'Évaluation.

---

## 1. Vue d'ensemble

La cartographie de l'environnement est un **outil de repérage systémique** (pas un diagnostic) qui produit un portrait de l'écosystème de l'enfant sur **5 feuilles** :

1. **Famille** (Le Socle)
2. **École & Pairs** (L'Espace Social)
3. **Écrans & Médias** (L'Influence Numérique)
4. **Valeurs sociétales** (L'Alignement au Monde)
5. **Cadre éducatif** (La Structure Invisible)

**Inputs** (saisis par le médecin pendant la consultation) :
- Chaque feuille = 1 **nervure centrale** (5 items binaires Oui/Non) + 2 à 4 **nervures secondaires** (3 ou 4 items binaires)
- Total : **73 cases à cocher** réparties sur les 5 feuilles

**Outputs** (calculés automatiquement) :
- Score brut par nervure (centrale et secondaires)
- **Couleur par feuille** : Vert / Jaune / Rouge (3 niveaux, fidèle au Tome 3)
- **Couleur globale Étape 5** : la pire couleur parmi les 5 feuilles (logique « feuille trouée » du Tome 3)

**Restrictions** :
- Patient 3-11 ans uniquement (cohérent avec Étape 4). Hors fourchette → étape sautée + message clair, validation étape 4 mène directement à la clôture.
- Aucune dépendance LLM dans cette V0 (scoring 100 % déterministe). Autofill LLM possible en V0.1 (Phase 2).

**Hors scope V0** :
- **La Tige** (zone d'ajustement / « petit pas possible ») → migrera vers le futur module **Projet Thérapeutique**, qui agrégera les tiges des deux cartographies (Enfant + Environnement). Aucun champ Tige dans le modèle Étape 5.
- Rendu visuel « feuille colorée » avec dessin organique (réservé à une future étape de restitution aux parents).
- Mode « questionnaire parent » exportable PDF.

---

## 2. Architecture

### 2.1 Récupération de l'âge

`_patient?.Age` déjà accessible dans `EvaluationPhaseViewModel` (utilisé par étapes 2, 3, 4). Aucun nouveau service.

### 2.2 Évolution de l'enum

L'enum existe déjà :

```csharp
public enum EvaluationStep
{
    Preparation = 1,
    EvaluationCiblee = 2,
    Synthese = 3,
    CartographieEnfant = 4,
    CartographieEnvironnement = 5  // déjà présent, à dé-stubber
}
```

### 2.3 Algorithme de couleur (transcription canonique Tome 3)

**Seuils par nervure** :

| Nervure | Vert | Jaune | Rouge |
|---|---|---|---|
| Centrale (5 items) | 5/5 | 3-4/5 | < 3/5 |
| Secondaire (3 items) | 3/3 | 2/3 | < 2/3 |
| Secondaire (4 items) | 4/4 | 3/4 | < 3/4 |

**Couleur par feuille** (règle du Tome 3 : « la nervure centrale a la priorité ») :

```
Si centrale = Rouge       → feuille = Rouge
Sinon si centrale = Vert ET toutes secondaires ≥ Jaune → feuille = Vert
Sinon                     → feuille = Jaune
```

**Couleur globale Étape 5** : pire couleur parmi les 5 feuilles.

> Note clinique : contrairement à l'Étape 4 (chenille), pas de conditionnement par tranche d'âge. Les items sont génériques (« gain progressif en autonomie adapté à son âge »), et c'est le clinicien qui apprécie selon le développement.

### 2.4 Item à scoring positif uniforme

**Décision** : tous les items utilisent la règle universelle « Oui = 1 point ». L'item « médiateur » du Tome 3 (Famille / Vécu émotionnel, scoring inversé à l'origine) est **reformulé en positif** :

> ~~« L'enfant porte-t-il des tensions qui ne sont pas les siennes ? » (0 si oui)~~
> → **« L'enfant n'est pas pris comme médiateur des tensions adultes ? »** (1 si oui)

Cohérence ergonomique : case cochée = positif, partout.

---

## 3. Modèle de données

### 3.1 Classes (parallèles à `ChenilleSegment` / `CartographieEnfant`)

```csharp
namespace MedCompanion.Models.Evaluations;

// Un item binaire (Oui/Non) d'une nervure
public class FeuilleItem : INotifyPropertyChanged
{
    public string Affirmation { get; }
    public bool IsChecked { get; set; }   // notifie
}

// Une nervure (centrale ou secondaire) avec ses items
public class Nervure : INotifyPropertyChanged
{
    public string Key { get; }            // "centrale", "liens_familiaux", etc.
    public string Label { get; }          // "Fonction parentale effective"
    public bool IsCentrale { get; }
    public ObservableCollection<FeuilleItem> Items { get; }
    public int Score => Items.Count(i => i.IsChecked);
    public int MaxScore => Items.Count;   // 3, 4 ou 5
}

// Une feuille = nervure centrale + secondaires
public class FeuilleEnvironnement : INotifyPropertyChanged
{
    public string Key { get; }            // "famille", "ecole_pairs", etc.
    public string Label { get; }          // "Famille"
    public string SousTitre { get; }      // "Le Socle"
    public Nervure NervureCentrale { get; }
    public ObservableCollection<Nervure> NervuresSecondaires { get; }
    // Couleur calculée par EnvironnementScoringService (pas stockée)
}

// Conteneur de l'étape 5
public class CartographieEnvironnement : INotifyPropertyChanged
{
    public int? AgeAuMomentDeLaSaisie { get; set; }
    public FeuilleEnvironnement Famille          { get; }
    public FeuilleEnvironnement EcolePairs       { get; }
    public FeuilleEnvironnement EcransMedias     { get; }
    public FeuilleEnvironnement ValeursSocietales{ get; }
    public FeuilleEnvironnement CadreEducatif    { get; }
    public DateTime? ValidationDate { get; set; }
    public bool IsValidated => ValidationDate.HasValue;
}
```

### 3.2 Contenu canonique

Fichier `Models/Evaluations/CartographieEnvironnementContent.cs` (parallèle à `CartographieContent.cs`) :
- Méthodes statiques `NewFamille()`, `NewEcolePairs()`, etc. qui instancient les feuilles avec leurs nervures et leurs items canoniques (transcription verbatim du Tome 3, sauf item médiateur reformulé).
- Méthodes `NiveauLabel(NiveauFeuille)`, `NiveauColor(NiveauFeuille)` pour binding XAML.

### 3.3 Enum couleur

```csharp
public enum NiveauFeuille { Vert, Jaune, Rouge }
```

> Pas de réutilisation de `NiveauSegment` (chenille à 6 niveaux), car l'environnement utilise 3 niveaux. Deux échelles différentes, deux types — explicite.

### 3.4 Service de scoring

`Services/Evaluations/EnvironnementScoringService.cs` :

```csharp
public static class EnvironnementScoringService
{
    public const int AgeMin = 3;
    public const int AgeMax = 11;
    public static bool IsApplicable(int? age) => age >= 3 && age <= 11;

    public static NiveauFeuille CalculerNervure(int score, int maxScore) { ... }
    public static NiveauFeuille CalculerFeuille(FeuilleEnvironnement feuille) { ... }
    public static NiveauFeuille CalculerGlobal(CartographieEnvironnement carto) { ... }
}
```

---

## 4. Contenu canonique des 5 feuilles

Source : transcription verbatim du Tome 3 (sauf item médiateur reformulé, §2.4).

### 4.1 Feuille **Famille** — « Le Socle »

**Nervure centrale — Fonction parentale effective** (5 items)
1. Au moins un adulte tient un cadre stable et rassurant ?
2. Ce cadre est posé sans violence ni passages brutaux d'un extrême à l'autre ?
3. L'enfant est protégé des conflits entre adultes ?
4. L'enfant comprend clairement ce qu'on attend de lui (attentes expliquées) ?
5. Les adultes parviennent à garder une certaine cohérence entre eux ?

**Secondaire A — Liens familiaux** (3 items)
1. Présence d'au moins une personne de confiance (écouté/soutenu) ?
2. Relations globales apaisées et respectueuses (sans tensions lourdes) ?
3. L'enfant circule librement sans être pris au milieu de conflits ?

**Secondaire B — Vécu émotionnel** (3 items)
1. L'enfant semble détendu et en confiance dans l'espace familial ?
2. Peut-il exprimer ses émotions sans crainte de rejet ou de punition ?
3. *L'enfant n'est pas pris comme médiateur des tensions adultes ?* **(reformulé)**

**Secondaire C — Messages éducatifs** (3 items)
1. Règles expliquées avec des mots adaptés ?
2. Cohérence globale entre les différents éducateurs ?
3. Attentes réalistes (sans menace, chantage ou humiliation) ?

**Secondaire D — Comportement de l'enfant** (3 items)
1. Expression des besoins sans crises ou retrait systématiques ?
2. Place ajustée dans la famille (ni trop effacé, ni parentifié) ?
3. Réaction ajustée aux tensions (pose des limites, cherche du soutien) ?

### 4.2 Feuille **École & Pairs** — « L'Espace Social »

**Nervure centrale — Position d'élève** (5 items)
1. Compréhension des règles, consignes et exercices ?
2. Capacité d'attention adaptée à l'âge sans se perdre ?
3. Gestion de la frustration face à la difficulté sans s'effondrer ?
4. Capacité à solliciter de l'aide en cas de blocage ?
5. *Gain progressif en autonomie adapté à son âge ?* **(libellé générique)**

**Secondaire A — Attitude parentale** (4 items)
1. Écoute régulière du vécu scolaire sans jugement ?
2. Compréhension des attentes de l'institution (même si désaccord) ?
3. Valorisation des efforts plutôt que des seuls résultats ?
4. Capacité à parler des tensions scolaires sans reproches ?

**Secondaire B — Vécu émotionnel** (3 items)
1. Fréquentation de l'école sans peur excessive ni somatisation ?
2. Possibilité de nommer ses émotions liées à l'école ?
3. Maintien d'une confiance en soi minimale dans le cadre scolaire ?

**Secondaire C — Difficultés scolaires** (3 items)
1. Compréhension réelle du travail demandé ?
2. Organisation du travail (savoir par où commencer) ?
3. Persévérance face à l'échec initial ?

**Secondaire D — Comportement** (3 items)
1. Place adaptée dans la classe (ni invisible, ni envahissant) ?
2. Expression des désaccords sans comportement extrême ?
3. Comportement perçu comme un message et non comme l'identité ?

### 4.3 Feuille **Écrans & Médias** — « L'Influence Numérique »

**Nervure centrale — Vigilance parentale** (5 items)
1. Connaissance des contenus consultés par l'enfant ?
2. Existence de règles claires à la maison ?
3. Dialogue ouvert sur le vécu numérique ?
4. Capacité à poser des limites sans conflit systématique ?
5. Offre d'espaces réguliers « sans écran » ?

**Secondaire A — Vécu émotionnel** (3 items)
1. Enfant apaisé (non agité/triste) après l'usage ?
2. Capacité à nommer ses ressentis (plaisir, frustration) ?
3. L'usage n'est pas une fuite face au stress/solitude ?

**Secondaire B — Qualité des contenus** (3 items)
1. Accès à des contenus variés, créatifs et adaptés ?
2. Usage actif (apprendre, créer) et non seulement passif ?
3. Supervision globale de l'autonomie numérique ?

**Secondaire C — Gestion du temps** (3 items)
1. Préservation des temps de jeu libre et de discussion ?
2. Moments « débranchés » organisés dans la semaine ?
3. Régulation raisonnable du temps global ?

**Secondaire D — Impact social** (3 items)
1. Usage servant de lien social (projets, discussions) ?
2. Absence d'isolement vis-à-vis de la vie familiale ?
3. Maintien des capacités d'interaction réelle ?

### 4.4 Feuille **Valeurs sociétales** — « L'Alignement au Monde »

**Nervure centrale — Harmonie valeurs familiales / sociétales** (5 items)
1. Accord global entre les valeurs transmises et l'environnement ?
2. Aisance éducative malgré les pressions extérieures ?
3. Absence de conflit intérieur majeur entre convictions et société ?
4. Pas de besoin de « traduction » permanente des messages du monde ?
5. Sentiment de cohérence globale pour l'enfant ?

**Secondaire A — Adaptation de l'enfant** (3 items)
1. Compréhension des attentes du monde extérieur ?
2. Capacité à s'adapter sans se renier ?
3. Maintien d'une liberté intérieure malgré la conformité ?

**Secondaire B — Message culturel du milieu** (3 items)
1. Repères éducatifs du quartier/milieu clairs et cohérents ?
2. L'entourage soutient indirectement les valeurs familiales ?
3. Absence de contradiction permanente avec le milieu de vie ?

### 4.5 Feuille **Cadre éducatif** — « La Structure Invisible »

**Nervure centrale — Positionnement parental face aux règles** (5 items)
1. Connaissance et acceptation des lois actuelles ?
2. Capacité à expliquer une règle gênante sans agressivité ?
3. Capacité à dire « non » malgré l'inconfort ?
4. Refus de laisser l'enfant décider pour éviter les crises ?
5. Capacité à réviser une règle devenue inadaptée ?

**Secondaire A — Cadre à la maison** (3 items)
1. Règles connues, claires et régulièrement rappelées ?
2. Réaction ferme mais sans violence en cas de transgression ?
3. L'enfant comprend le « pourquoi » de l'interdit ?

**Secondaire B — Rapport à l'autorité extérieure** (3 items)
1. Cohérence des propos tenus sur les institutions ?
2. Pas de décrédibilisation des figures d'autorité devant l'enfant ?
3. Aide à l'insertion sociale sans renier ses propres opinions ?

---

## 5. UI — Tableau dépliable par feuille

### 5.1 Vue d'ensemble (collapsed par défaut)

```
ÉTAPE 5 — CARTOGRAPHIE DE L'ENVIRONNEMENT          🟡 Synthèse : Fragile

┌─ Famille — Le Socle ─────────────────── 🟢 Vert ─ ▾ ─┐
│  Fonction parentale effective              5/5  🟢   │
│  Liens familiaux                           3/3  🟢   │
│  Vécu émotionnel                           2/3  🟡   │
│  Messages éducatifs                        3/3  🟢   │
│  Comportement de l'enfant                  2/3  🟡   │
└──────────────────────────────────────────────────────┘

┌─ École & Pairs — L'Espace Social ───── 🔴 Rouge ─ ▾ ─┐
│  Position d'élève                          2/5  🔴   │
│  Attitude parentale                        3/4  🟡   │
│  Vécu émotionnel                           1/3  🔴   │
│  Difficultés scolaires                     2/3  🟡   │
│  Comportement                              3/3  🟢   │
└──────────────────────────────────────────────────────┘

┌─ Écrans & Médias — L'Influence Num. ── 🟡 Jaune ─ ▸ ─┐  (replié)
┌─ Valeurs sociétales — L'Alignement ─── 🟢 Vert ──ㅤ▸ ─┐  (replié)
┌─ Cadre éducatif — La Structure ─────── 🟡 Jaune ─ ▸ ─┐  (replié)

[ ⏸ Terminer la séance ]    [ ✓ Valider l'étape 5 ]
```

### 5.2 Feuille dépliée (clic sur ▾)

Au clic, la feuille déplie ses nervures avec les cases à cocher :

```
┌─ Famille — Le Socle ─────────────────── 🟢 Vert ─ ▴ ─┐
│                                                       │
│  ▌ Fonction parentale effective (centrale)   5/5  🟢  │
│    [✓] Au moins un adulte tient un cadre stable…     │
│    [✓] Ce cadre est posé sans violence…              │
│    [✓] L'enfant est protégé des conflits…            │
│    [✓] L'enfant comprend ce qu'on attend de lui…     │
│    [✓] Les adultes gardent une cohérence…            │
│                                                       │
│  ▌ Liens familiaux                            3/3  🟢 │
│    [✓] Présence d'au moins une personne…             │
│    [✓] Relations globales apaisées…                  │
│    [✓] L'enfant circule librement…                   │
│                                                       │
│  ▌ Vécu émotionnel                            2/3  🟡 │
│    [✓] L'enfant semble détendu et en confiance…      │
│    [✓] Peut-il exprimer ses émotions…                │
│    [ ] L'enfant n'est pas pris comme médiateur…      │
│                                                       │
│  ▌ Messages éducatifs                         3/3  🟢 │
│    [✓] …                                              │
│                                                       │
│  ▌ Comportement de l'enfant                   2/3  🟡 │
│    [✓] …                                              │
└──────────────────────────────────────────────────────┘
```

### 5.3 Composants WPF à créer

| Composant | Rôle |
|---|---|
| `EnvironnementStepControl.xaml` | UserControl racine de l'étape, contient les 5 feuilles |
| `FeuilleExpanderControl.xaml` | Expander WPF par feuille : header (label + score + badge couleur) + content (les nervures) |
| `NervureControl.xaml` | Bloc nervure : label + items checkbox + score + badge couleur |
| `FeuilleBadgeConverter` | Bool/int → couleur hex (binding badge) |

### 5.4 Header badge couleur synthèse

`Synthèse : Vert / Jaune / Rouge` calculé via `EnvironnementScoringService.CalculerGlobal()`, affiché en haut à droite de l'étape, mis à jour à chaque coche.

---

## 6. ViewModel

`ViewModels/Evaluations/CartographieEnvironnementViewModel.cs` (parallèle au VM existant de la chenille) :

```csharp
public class CartographieEnvironnementViewModel : INotifyPropertyChanged
{
    public CartographieEnvironnement Modele { get; }
    public ObservableCollection<FeuilleViewModel> Feuilles { get; }
    public NiveauFeuille Synthese { get; }              // recalculé
    public ICommand ValiderEtapeCommand { get; }
    public bool IsApplicable { get; }                   // âge 3-11
}

public class FeuilleViewModel : INotifyPropertyChanged
{
    public FeuilleEnvironnement Modele { get; }
    public NervureViewModel Centrale { get; }
    public ObservableCollection<NervureViewModel> Secondaires { get; }
    public NiveauFeuille Couleur { get; }               // recalculé
    public bool IsExpanded { get; set; }                // état UI
}
```

Pattern de notification : chaque `FeuilleItem.IsChecked` → bubble vers `Nervure.Score` → bubble vers `Feuille.Couleur` → bubble vers `Synthese`. Implémentable simplement via `PropertyChanged` cascadé (déjà fait dans la chenille).

---

## 7. Persistance

### 7.1 Format

Ajout dans le YAML d'évaluation (cf. `PLAN_PHASE_EVALUATION_V0.md` §3.2) :

```yaml
cartographie_environnement:
  age_au_moment_de_la_saisie: 8
  validation_date: 2026-06-04T15:45
  feuilles:
    famille:
      centrale: [true, true, true, true, true]
      liens_familiaux: [true, true, true]
      vecu_emotionnel: [true, true, false]
      messages_educatifs: [true, true, true]
      comportement_enfant: [true, false, true]
    ecole_pairs:
      centrale: [true, false, true, false, false]
      attitude_parentale: [true, true, true, false]
      vecu_emotionnel: [false, true, false]
      difficultes_scolaires: [true, true, false]
      comportement: [true, true, true]
    ecrans_medias: { ... }
    valeurs_societales: { ... }
    cadre_educatif: { ... }
```

### 7.2 Service de sérialisation

Extension du service d'évaluation existant. Pas de nouveau service dédié.

---

## 8. Intégration au flow Phase d'Évaluation

### 8.1 Ordre des étapes

```
Étape 1 — Préparation clinique         ✅ implémenté
Étape 2 — Évaluation ciblée            ✅ implémenté
Étape 3 — Synthèse diagnostique        ✅ implémenté
Étape 4 — Cartographie de l'enfant     ✅ implémenté
Étape 5 — Cartographie de l'environnement  ← cette implémentation
└─ Clôture de l'évaluation
```

### 8.2 Saut si hors fourchette

Si `_patient.Age < 3 || _patient.Age > 11` à la validation de l'étape 4, **l'étape 5 est sautée** avec message clair :

> *« Cartographie de l'environnement disponible uniquement pour les patients 3-11 ans (V0). Le patient a X ans — étape sautée. Vous pouvez clôturer l'évaluation. »*

Et le bouton Clôturer apparaît directement.

### 8.3 Validation

Pas de seuil minimal de remplissage (cohérent avec l'étape 4). Le clinicien peut valider même avec des items non cochés — l'absence de coche n'est PAS un score 0 fiable, c'est un Oui-Non binaire que le médecin endosse.

---

## 9. Cohérence avec le futur module Projet Thérapeutique

Le module Projet Thérapeutique (non encore implémenté) lira **les deux cartographies** :
- **Cartographie de l'enfant** (Étape 4) → segments rouges/jaunes
- **Cartographie de l'environnement** (Étape 5) → feuilles rouges/jaunes

Et proposera des **tiges** (« petits pas possibles ») par segment/feuille en difficulté, à co-construire avec la famille.

→ C'est pour cette raison que la Tige n'est PAS dans le modèle de l'Étape 5. Elle vit dans un objet `ProjetTherapeutique` qui référence les cartographies, pas dans les cartographies elles-mêmes. Cette séparation garde l'évaluation descriptive et le projet thérapeutique prescriptif — distinction clinique non négociable.

---

## 10. Phases d'implémentation

| Phase | Contenu | Estimation |
|---|---|---|
| **V0.0** | Modèle de données + contenu canonique + scoring service | 1 séance |
| **V0.1** | UI tableau dépliable + binding + ViewModel | 1-2 séances |
| **V0.2** | Persistance YAML + intégration au flow d'évaluation | 1 séance |
| **V0.3** | Tests manuels + ajustements ergonomiques | 1 séance |
| **V1** *(plus tard)* | Autofill LLM depuis Étape 2 (entretien) | hors V0 |

---

## 11. Décisions verrouillées (résumé)

| Décision | Choix |
|---|---|
| Format UI | Tableau dépliable par feuille (5 expanders) |
| Niveaux de couleur | 3 niveaux (Vert / Jaune / Rouge), fidèle Tome 3 |
| Tranches d'âge | Aucune — libellés génériques uniques |
| Conditionnement âge des libellés | Aucun (« adapté à son âge » dans les items concernés) |
| Item médiateur (Famille / Vécu émotionnel) | Reformulé en positif, règle universelle Oui = 1 |
| Tige | **Hors évaluation** → futur Projet Thérapeutique |
| Couleur globale étape | Pire couleur parmi les 5 feuilles |
| LLM | Pas en V0. Autofill envisagé en V1. |
| Fourchette d'âge | 3-11 ans (cohérent Étape 4) |
