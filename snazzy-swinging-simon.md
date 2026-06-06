# PLAN — Hub Restitutions & Dossier de Restitution Initial

> **Date :** 2026-06-06
> **Source clinique :** PDF exemple fourni par utilisateur (Dossier Evan Liseron, 16 pages)
> **Documents liés :** [PLAN_RESTITUTION_PARENTS.md](c:\Users\nair\Desktop\MedCompagion V1 béta\PLAN_RESTITUTION_PARENTS.md) (ancien plan de la Restitution 1er entretien V0e, déjà implémenté en partie)

---

## Contexte

Le mode consultation V2 a livré : Phase d'évaluation 3 étapes (Préparation + Évaluation Ciblée + Cartographie Enfant + Cartographie Environnement + Bilan Final), Synthèse Globale V0.5, Projet Thérapeutique V1.4. Il manque le **livrable final** du flux : le document que le pédopsy remet aux parents (et autres intervenants).

**Constat sur l'existant** :
- Une **Restitution 1er entretien** (V0e) existe déjà comme Phase 4 du Mode Consultation. Génère un HTML 1-page + PDF via `HtmlToPdfService`, template `restitution_template.html`.
- ⚠️ Elle est **stockée uniquement dans `%TEMP%/MedCompanion_Restitution/`** — perdue à la prochaine ouverture du patient.
- Aucun endroit ne regroupe les restitutions faites pour un patient.

**Vision validée avec l'utilisateur (PDF exemple analysé)** :
- Un nouveau **Dossier de restitution initial** = livrable 16 pages = Bilan + Projet thérapeutique remis aux parents + intervenants (école, orthophoniste, pédiatre).
- Voix **mixte par section** : pages parents en voix livre Tome 2/3, pages cliniques en ton clinique sobre avec DSM nommé (TDAH, TOP, TAG retenus/écartés).
- Toutes les restitutions du patient regroupées via un **Hub Restitutions** unique.

**Objectif** : permettre au psy de générer, éditer, valider et exporter en PDF des dossiers de restitution, en réutilisant les sources de vérité (Synthèse Globale validée + Projet Thérapeutique validé + Cartographies + Bilan Final).

---

## Décisions verrouillées (Q1→Q6 validés)

| # | Décision | Source |
|---|---|---|
| **Q1** | Préremplissage auto de chaque bloc depuis les sources cliniques validées (Synthèse, Projet, Cartographies, Bilan Final, patient.json) | User OK |
| **Q2** | Reformulation par bloc = **chips rapides** (Plus court / Plus simple / Voix livre / Plus clinique) **+ zone consigne libre** | User OK |
| **Q3** | **Voix mixte par section** dans le Dossier de restitution initial (voir [[feedback-med-voix-destinataire]]) | User refinement |
| **Q4** | Modularité par **toggle "Inclure dans le PDF" par bloc**. Pas de profil pré-défini Surveillance/Standard/Pathologie pour l'instant | User OK |
| **Q5** | Versions immuables. v1 validée fige le contenu. Modification → v2 brouillon | User OK |
| **Q6** | Restitution validée apparaît dans zone "Documents du patient" comme **bloc unique** (date + icône PDF) — pas bloc par bloc | User OK |
| **D7** | Bouton "Restitutions" placé dans la **zone Med centrale**, après "Documents du patient" | User décision |
| **D8** | Au clic → choix entre types de restitution dans l'**espace de travail gauche** | User décision |
| **D9** | Pendant édition (blocs non validés), le brouillon vit dans l'espace de travail gauche — **rien n'apparaît dans Documents tant que non validé** | User décision |

---

## Architecture UX

### Zone Med (panneau central)

Nouveau bloc après "Documents du patient" :
```
┌─ Med ──────────────────────────┐
│ [🚫][💬][📋]                    │
│                                │
│ 📄 Documents du patient        │
│   [Importer] [Scanner]         │
│                                │
│ 📋 Restitutions [+ Nouvelle]   │ ← NOUVEAU
│                                │
│ 🗒 Points à évoquer            │
└────────────────────────────────┘
```

### Espace de travail gauche (au clic "+ Nouvelle")

```
┌─ Quel type de restitution ? ──────────────────────┐
│  [ 📄 Restitution 1er entretien            ]      │
│  [ 📚 Dossier de restitution initial       ]      │
│      (Bilan + Projet thérapeutique)               │
│  (futur : Réévaluation 3 mois…)                   │
└───────────────────────────────────────────────────┘
```

### Espace de travail gauche (édition live par blocs)

```
┌─ 📚 Dossier de restitution initial — v1 brouillon ┐
│ Patient : Evan LISERON   Créé le 06/06/2026       │
│                                                   │
│ ┌─ Bloc 1 — Identité & couverture ─ ☑ Inclure ─┐ │
│ │ Préremplit auto depuis patient.json           │ │
│ │ [contenu éditable]                            │ │
│ │                                               │ │
│ │ [✏️ + court][🎈 + simple][📖 voix livre]      │ │
│ │ [🩺 + clinique]   Consigne: [_____________]   │ │
│ │ [✨ Reformuler]              [✅ Valider]     │ │
│ └───────────────────────────────────────────────┘ │
│                                                   │
│ ┌─ Bloc 2 — Restitution 1-page parents ──────── │ │
│ │ Sources : Synthèse Globale + Bilan Final      │ │
│ │ Voix livre par défaut                         │ │
│ │ ... (idem pattern)                            │ │
│ └─────────────────────────────────────────────── │ │
│                                                   │
│ ... (blocs 3 à 8)                                 │
│                                                   │
│ [⏸ Reprendre plus tard]  [✓ Valider le dossier]  │
└───────────────────────────────────────────────────┘
```

### Zone Documents (après validation)

```
┌─ Documents du patient ─────────────────────┐
│ [Importer] [Scanner]                       │
│                                            │
│ 📋 Restitutions générées                   │ ← sous-section
│ ┌────────────────────────────────────────┐ │
│ │ 📄 Restitution 1er entretien           │ │
│ │    Faite le 13/05/2026          [📄]   │ │
│ ├────────────────────────────────────────┤ │
│ │ 📚 Dossier de restitution initial v1   │ │
│ │    Fait le 06/06/2026           [📄]   │ │
│ └────────────────────────────────────────┘ │
│                                            │
│ 📎 Documents importés                      │ ← sous-section
│ ┌────────────────────────────────────────┐ │
│ │ ... PDFs externes                      │ │
│ └────────────────────────────────────────┘ │
└────────────────────────────────────────────┘
```

---

## Architecture technique

### Réutilisation

| Asset existant | Réutilisation |
|---|---|
| `Services/HtmlToPdfService.cs` | Génération PDF depuis HTML (LibreOffice headless) |
| `Resources/Consultation/restitution_template.html` | Template Restitution 1er entretien (déjà OK) |
| Pattern Synthèse Globale (bloc + validation + diff) | Inspiration pour pattern bloc-par-bloc |
| Pattern Projet Thérapeutique (versions immuables) | Inspiration versionning v(N+1) |
| `SyntheseGlobaleService` / `ProjetTherapeutiqueService` | Lecture des dernières versions validées (sources Q1) |
| `CartographieEnfantService` / `BrancheEnvironnementLectureService` | Sources §4 et §5 |
| `EvaluationPhaseService` | Source §3 et données Bilan Final |

### Nouveau

#### Modèles — `Models/Restitutions/`

**`RestitutionType.cs`**
```csharp
public enum RestitutionType
{
    PremierEntretien,        // existante, à migrer
    DossierInitial,          // NOUVEAU — Bilan + Projet, 16 pages
    // ReevaluationTrimestrielle, ReevaluationSemestrielle (futur)
}

public enum RestitutionStatut { Brouillon, Validee }
```

**`RestitutionBase.cs`** — abstract
```csharp
public abstract class RestitutionBase : INotifyPropertyChanged
{
    public string Id { get; set; }              // GUID stable
    public RestitutionType Type { get; }
    public int Version { get; set; }            // 1, 2, …
    public string? VersionPrecedenteFichier { get; set; }
    public RestitutionStatut Statut { get; set; }
    public DateTime DateCreation { get; set; }
    public DateTime? DateValidation { get; set; }
    public string PatientNomComplet { get; set; }
    public string? GeneratedPdfPath { get; set; }
    public ObservableCollection<RestitutionBloc> Blocs { get; }
}
```

**`RestitutionBloc.cs`**
```csharp
public class RestitutionBloc : INotifyPropertyChanged
{
    public string Key { get; }                  // "couverture", "synthese_diag", "projet_7_1", …
    public string Titre { get; }
    public int Ordre { get; }
    public string ContenuPreremplit { get; set; }  // texte initial Med
    public string ContenuValide { get; set; }      // texte validé par psy (peut diverger)
    public bool IsValidated { get; set; }
    public bool IsIncludedInPdf { get; set; } = true;  // Q4 toggle
    public string VoixCible { get; }            // "livre" | "clinique" | "mixte"
    public ObservableCollection<ReformulationEntry> Historique { get; }  // trace optionnelle
    public string? SourceCliniqueFichier { get; set; }  // d'où vient le préremplit
}

public class ReformulationEntry
{
    public DateTime Date { get; set; }
    public string Consigne { get; set; }        // "Plus court" ou texte libre
    public string ResultatBrut { get; set; }
}
```

**`DossierRestitutionInitial.cs`** — concrete
```csharp
public class DossierRestitutionInitial : RestitutionBase
{
    // 8 sections — initialisées dans le constructeur avec
    // Key, Titre, Ordre, VoixCible
    // Section 7 (Projet) = 5 sous-blocs 7.1→7.5 générés dynamiquement
    // selon les axes actifs du Projet Thérapeutique validé
}
```

**`RestitutionPremierEntretien.cs`** — adaptateur de l'existant `RestitutionAuxParents` (Models/ConsultationModels.cs), pour rentrer dans la nouvelle structure unifiée.

#### Services — `Services/Restitutions/`

**`RestitutionService.cs`** — load/save/list/validate
- `SaveBrouillon(RestitutionBase r)` → `patients/X_Y/restitutions/YYYY/restitution_{type}_v{N}_brouillon.md`
- `Validate(RestitutionBase r)` → renomme en `_v{N}_{YYYYMMDD}.md`, met `Statut = Validee`, fige
- `ListByPatient(string patientNomComplet)` → toutes les restitutions du patient, triées DESC par date
- `CreateNewVersion(RestitutionBase precedente)` → clone v(N+1) brouillon, contenu = ContenuValide du précédent

**`RestitutionSuggesterService.cs`** — préremplit chaque bloc
- `PrefillBlocAsync(RestitutionBloc bloc, Patient patient, ContexteSourcesCliniques sources)` → appel LLM avec voix cible + source
- `ContexteSourcesCliniques` agrège : dernière Synthèse Globale validée, dernier Projet Thérapeutique validé, Cartographies validées, Bilan Final, patient.json
- Préremplissage **séquentiel** au démarrage (un appel LLM par bloc) avec spinner

**`RestitutionReformulationService.cs`**
- `ReformulerAsync(RestitutionBloc bloc, string consigne)` → appel LLM ciblé
- Chips → consignes prédéfinies :
  - "Plus court" → "Réduis de moitié sans perdre l'essentiel"
  - "Plus simple" → "Reformule pour un parent non médecin"
  - "Voix livre" → "Reformule en métaphores organiques (chenille, feuille, sève)"
  - "Plus clinique" → "Reformule avec terminologie pédopsychiatrique précise"

**`DossierRestitutionPdfService.cs`** — composition du HTML 16 pages
- Charge `dossier_restitution_initial_template.html` (multi-pages avec `page-break-after: always`)
- Substitue les `{{BLOC_KEY}}` par ContenuValide des blocs avec `IsIncludedInPdf == true`
- Appelle `HtmlToPdfService.ConvertHtmlToPdf` (existant)
- Sauvegarde le PDF dans `patients/X_Y/restitutions/YYYY/restitution_DossierInitial_v{N}_{date}.pdf`

#### Templates HTML — `Resources/Consultation/`

**`restitution_template.html`** — existant, **migrer mais inchangé**

**`dossier_restitution_initial_template.html`** — NOUVEAU
- 16 pages A4 portrait (`@page` size A4)
- CSS `page-break-after: always` entre sections
- Placeholders `{{BLOC_COUVERTURE}}`, `{{BLOC_RESTITUTION_1PAGE}}`, `{{BLOC_PATIENT_CONTEXTE_1}}`, …, `{{BLOC_CONCLUSION}}`
- Reproduit fidèlement les styles du PDF exemple (couleurs MedCompanion, échelle 5 niveaux, etc.)
- Si bloc `IsIncludedInPdf == false` → page sautée

#### ViewModels — `ViewModels/Restitutions/`

| ViewModel | Rôle |
|---|---|
| `RestitutionsHubViewModel` | Liste des types disponibles + bouton retour vers liste |
| `RestitutionEditorViewModel` | Édition live d'une restitution (orchestrateur des blocs) |
| `RestitutionBlocViewModel` | Wrapper d'un bloc : commandes `ReformulerCommand` / `ValiderBlocCommand` / `ToggleInclureCommand` |
| `RestitutionDocumentCardViewModel` | Carte affichée dans Documents (date + icône PDF + clic) |

#### Modifications de fichiers existants

| Fichier | Modification |
|---|---|
| `Views/Med/MedPanelControl.xaml` (ou équivalent) | Ajouter le bloc "📋 Restitutions" entre Documents et Points à évoquer |
| `Views/Documents/DocumentsControl.xaml` | Ajouter sous-section "Restitutions générées" en haut, lister via `RestitutionDocumentCardViewModel` |
| `ViewModels/ConsultationModeViewModel.cs` | Migrer `RestitutionAuxParents` vers le nouveau `RestitutionService` (persistance) — préserver les commandes existantes |
| `MainWindow.xaml.cs` | Instancier les nouveaux services et les injecter |

---

## Storage

### Arborescence

```
patients/LASTNAME_Firstname/
├── 2026/
│   ├── restitutions/
│   │   ├── restitution_PremierEntretien_v1_20260513.md     ← migré depuis temp
│   │   ├── restitution_PremierEntretien_v1_20260513.pdf
│   │   ├── restitution_DossierInitial_v1_brouillon.md      ← en cours
│   │   ├── restitution_DossierInitial_v1_20260606.md       ← validé immuable
│   │   └── restitution_DossierInitial_v1_20260606.pdf
```

### Format YAML+Markdown (exemple Dossier Initial)

```yaml
---
type: DossierInitial
version: 1
statut: Validee
patient: LISERON Evan
date_creation: 2026-06-05T14:30:00
date_validation: 2026-06-06T09:15:00
version_precedente_fichier: null
sources:
  synthese_globale_fichier: synthese_globale/synthese_v3_20260520.md
  projet_therapeutique_fichier: projet_therapeutique/projet_v2_20260525.md
  bilan_final_fichier: evaluations/eval_phase_20260517.md
blocs:
  - key: couverture
    titre: "Identité & couverture"
    voix_cible: livre
    is_included_in_pdf: true
    is_validated: true
    source_clinique: patient.json
  - key: restitution_1page
    titre: "Restitution 1-page parents"
    voix_cible: livre
    ...
---

## Bloc 1 — Identité & couverture

[contenu validé du bloc]

## Bloc 2 — Restitution 1-page parents

[contenu validé du bloc]

...

## Bloc 8 — Conclusion et perspectives

[contenu validé du bloc]
```

---

## Plan d'implémentation par phases

### Phase P1 — Persistance de l'existante Restitution 1er entretien (~2h)

1. Créer `RestitutionService` minimal (save/load/list par patient)
2. Modifier `ConfirmRestitutionAsync` dans `ConsultationModeViewModel` : sauvegarder dans `patients/X_Y/restitutions/YYYY/` au lieu de `%TEMP%`
3. Tester : générer une Restitution 1er entretien → vérifier qu'elle persiste au redémarrage

### Phase P2 — Hub UI dans zone Med + carte Documents (~3h)

1. Créer `RestitutionsHubViewModel` + UI dans le panneau Med
2. Bouton "+ Nouvelle" → affiche choix dans espace de travail gauche
3. Créer `RestitutionDocumentCardViewModel` + intégration dans `DocumentsControl` (sous-section "Restitutions générées")
4. Clic icône PDF → ouverture via `Process.Start`
5. Tester : Restitution 1er entretien existante apparaît bien dans la liste + clic ouvre PDF

### Phase P3 — Modèle Dossier de Restitution Initial + Sources (~4h)

1. Créer `RestitutionType`, `RestitutionBase`, `RestitutionBloc`, `DossierRestitutionInitial` (Models/Restitutions/)
2. Définir les 8 sections + sous-sections 7.1→7.5 avec `VoixCible`
3. Créer `ContexteSourcesCliniques` qui agrège Synthèse / Projet / Cartographies / Bilan Final / patient.json
4. Étendre `RestitutionService` pour gérer le nouveau type
5. Tests unitaires : instancier un dossier vide, sauvegarder, recharger, vérifier intégrité

### Phase P4 — Préremplissage Med par bloc (~4h)

1. Créer `RestitutionSuggesterService` avec `PrefillBlocAsync`
2. Prompts par bloc avec voix cible + source clinique injectée
3. Workflow : à l'ouverture d'un nouveau dossier → préremplissage séquentiel des 8 blocs (spinner)
4. Tester : ouvrir un dossier sur un patient ayant Synthèse + Projet validés → vérifier que les 8 blocs sont préremplis cohérents

### Phase P5 — UI éditeur live (~5h)

1. Créer `RestitutionEditorViewModel` + `RestitutionBlocViewModel`
2. UI : ScrollViewer avec ItemsControl sur les blocs
3. DataTemplate bloc : titre, toggle "Inclure", textarea contenu, chips reformulation, zone consigne, boutons Reformuler/Valider
4. Auto-save brouillon (debounce 500ms)
5. Bouton "Valider le dossier" : actif quand tous les blocs `IsValidated` (ou explicitement marqués `IsIncludedInPdf == false`)
6. Tester : édition, reformulation par chip, reformulation libre, validation bloc par bloc

### Phase P6 — Template HTML 16 pages + export PDF (~4h)

1. Créer `dossier_restitution_initial_template.html` (reproduire fidèlement le PDF exemple)
2. Créer `DossierRestitutionPdfService.GeneratePdfAsync` : substitution placeholders + appel `HtmlToPdfService`
3. Bouton "Valider le dossier" → génère le PDF, le sauvegarde, fige la v1
4. La carte apparaît dans Documents
5. Tester : valider un dossier → PDF généré → ouvrir et vérifier mise en page conforme

### Phase P7 — Versionning v(N+1) (~2h)

1. Sur un dossier validé, bouton "Modifier" → crée v(N+1) brouillon (clone ContenuValide → ContenuValide nouveau)
2. v(N) reste lisible et immuable
3. Documents liste les deux versions
4. Tester : valider v1, créer v2 brouillon, modifier un bloc, valider, vérifier que v1 est intacte et v2 distincte

### Phase P8 — Polish & tests end-to-end (~2h)

1. Vérifier flow complet patient fictif (équivalent Evan Liseron)
2. Tester avec/sans Synthèse validée (cas où le préremplit ne peut pas s'appuyer dessus)
3. Tester toggle "Inclure" → PDF saute bien les sections exclues
4. Vérifier comportement si Projet Thérapeutique a moins de 5 axes (section 7 s'adapte)

**Estimation totale : ~26h**

---

## Fichiers impactés

### Nouveaux fichiers
- `Models/Restitutions/RestitutionType.cs`
- `Models/Restitutions/RestitutionBase.cs`
- `Models/Restitutions/RestitutionBloc.cs`
- `Models/Restitutions/DossierRestitutionInitial.cs`
- `Services/Restitutions/RestitutionService.cs`
- `Services/Restitutions/RestitutionSuggesterService.cs`
- `Services/Restitutions/RestitutionReformulationService.cs`
- `Services/Restitutions/DossierRestitutionPdfService.cs`
- `Services/Restitutions/ContexteSourcesCliniques.cs`
- `ViewModels/Restitutions/RestitutionsHubViewModel.cs`
- `ViewModels/Restitutions/RestitutionEditorViewModel.cs`
- `ViewModels/Restitutions/RestitutionBlocViewModel.cs`
- `ViewModels/Restitutions/RestitutionDocumentCardViewModel.cs`
- `Views/Restitutions/RestitutionsHubControl.xaml`
- `Views/Restitutions/RestitutionEditorControl.xaml`
- `Resources/Consultation/dossier_restitution_initial_template.html`

### Fichiers modifiés
- `ViewModels/ConsultationModeViewModel.cs` — `ConfirmRestitutionAsync` : passer du temp à `RestitutionService`
- `Views/Documents/DocumentsControl.xaml` + `.cs` — sous-section Restitutions générées
- Panneau Med (à identifier — `Views/Consultation/ConsultationModeControl.xaml` ou un sous-control) — ajouter bloc "📋 Restitutions"
- `MainWindow.xaml.cs` — instancier `RestitutionService` + `RestitutionSuggesterService` + `RestitutionReformulationService` + `DossierRestitutionPdfService` et les injecter

### Fichiers consultés mais non modifiés
- `Resources/Consultation/restitution_template.html` (déjà OK)
- `Services/HtmlToPdfService.cs` (réutilisé tel quel)
- `Services/Synthesis/SyntheseGlobaleService.cs` (lecture dernière version validée)
- `Services/Therapeutique/ProjetTherapeutiqueService.cs` (idem)
- `Services/Evaluations/EvaluationPhaseService.cs` (sources §3, §4, §5, Bilan Final)

---

## Vérification end-to-end

**Build** : `dotnet build medcompagnio2.sln` → 0 erreur, ~230 warnings max.

**Scénario manuel de validation** (utilisateur, sur un patient fictif type Evan) :
1. Patient créé avec données complètes
2. Évaluation menée (3 étapes validées + Bilan Final)
3. Synthèse Globale v1 validée
4. Projet Thérapeutique v1 validé
5. Clic sur "📋 Restitutions" → "+ Nouvelle" → choisir "Dossier de restitution initial"
6. Vérifier que les 8 blocs se préremplissent automatiquement avec contenu cohérent issu des sources
7. Tester un chip "Plus simple" sur un bloc → vérifier reformulation
8. Tester une consigne libre "Raccourcir et insister sur les forces"
9. Valider tous les blocs → bouton "Valider le dossier" devient actif
10. Cliquer "Valider" → PDF 16 pages généré
11. Vérifier que la carte apparaît dans Documents avec date + icône PDF
12. Clic sur l'icône → PDF s'ouvre, mise en page conforme à l'exemple fourni
13. Cliquer "Modifier" → v2 brouillon créée, v1 reste lisible
14. Sur un patient sans Projet validé → vérifier comportement (warning + permettre saisie manuelle)

---

## Risques & garde-fous

| Risque | Mitigation |
|---|---|
| **Préremplissage LLM lent** (8 blocs séquentiels = 8 appels LLM) | Spinner clair + possibilité d'annuler en cours + cacher en cas d'échec d'un bloc |
| **Échec de la conversion HTML→PDF** (LibreOffice absent ou crash) | Sauvegarder le HTML quand même + bouton "Régénérer PDF" + message d'erreur explicite |
| **Voix livre mal appliquée par le LLM** (résultat trop clinique sur blocs parents) | Few-shot dans le prompt + chip "Voix livre" permet correction rapide |
| **Sources cliniques manquantes** (pas de Synthèse validée, Projet incomplet) | Préremplit best-effort + warning visible sur le bloc concerné |
| **Modèle saturé sur les longs prompts** (16 pages = beaucoup de tokens) | Préremplir bloc par bloc avec contexte minimal pertinent (pas tout le dossier à chaque appel) |
| **Versionning : confusion v1/v2** | Statut affiché clairement + DateValidation visible + nom de fichier explicite |
| **Régression Restitution 1er entretien** (déjà en prod) | Préserver toute la logique existante dans `ConsultationModeViewModel`, juste rediriger le stockage |

---

## Hors périmètre

- Restitution Réévaluation 3 mois / 6 mois / annuelle (V2)
- Profils pré-définis Surveillance / Standard / Pathologie (V2 si besoin émerge)
- Génération auto de visuels (camemberts, radars, feuilles colorées) — pour V1 on utilise des SVG statiques + valeurs paramétrées en CSS
- Édition WYSIWYG inline du HTML (édition reste textarea, prévisualisation = PDF final)
- Onglet RESTITUTION dans le dossier bleu droit — l'affichage dans Documents suffit pour V1
