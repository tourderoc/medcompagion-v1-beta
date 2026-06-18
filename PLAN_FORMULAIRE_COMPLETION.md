# PLAN — Formulaire de Complétion (1ère consultation)

> **But** : pendant que les parents patientent (médecin seul avec l'enfant), leur remettre un
> formulaire papier **pré-rempli** qu'ils n'ont qu'à **vérifier / corriger**. Après l'entretien,
> le médecin **scanne** le formulaire et **Med extrait** les données (vision locale) pour alimenter
> le dossier — toujours sous **validation humaine**.
>
> **Place dans le parcours** : nouvelle étape de la 1ère consultation, **après l'Interrogatoire**
> (nouveau bouton « Formulaire parents »), en miroir de la collecte du dossier de restitution.

---

## 1. Principes directeurs (non négociables)

1. **Med propose → le médecin valide.** Aucune donnée OCR/vision n'est écrite dans le dossier sans
   relecture (réutilise l'étape de cartouches + le système de confiance couleur existant).
2. **100 % local.** Génération (PdfSharp) et extraction (vision **Ollama** — `gemma3:12b` cabinet,
   `gemma3:4b` repli) ne sortent jamais vers le cloud. Cohérent avec l'esprit du projet.
3. **Ne traiter que les deltas.** Les champs déjà connus sont pré-remplis ; l'extraction se concentre
   sur ce qui a été *corrigé* ou *ajouté* (cases à cocher + champs manuscrits).
4. **Correspondance stricte.** Le formulaire est le **miroir papier** de la collecte du dossier :
   un champ AcroForm ⇄ une donnée du modèle (PatientMetadata / bloc d'interrogatoire).
5. **Le temps avec l'enfant reste sanctuarisé.** La relecture du formulaire se fait **après**
   l'entretien, jamais en parallèle.
6. **Tout est facultatif et sans friction** (photo, autorisations). Repli manuel si le formulaire
   revient vide/incomplet (allophones, faible littératie).

---

## 2. Décisions techniques actées

| Sujet | Décision | Raison |
|---|---|---|
| Génération PDF | **HTML/CSS → PDF via Edge headless** (`EdgeHeadlessPdfService`) | Moteur **Chromium** = rendu CSS fidèle au pixel ; **Edge préinstallé** Win10/11 → aucune dépendance ; itération rapide (le médecin a un bon retour sur HTML/CSS). Déjà utilisé par la restitution. |
| Génération PDF — repli | Template AcroForm + PdfSharp (`PDFFormFillerService`) | **Dernier recours** uniquement. Très pénible à créer pour un formulaire « tout en cases » (cases à lettres). Pertinent surtout si on voulait des champs interactifs. |
| ~~LibreOffice~~ | **Écarté** | `HtmlToPdfService` (LibreOffice) donne un rendu faible sur layout précis → **ne pas l'utiliser** ici. |
| Extraction | **Modèle de vision local** via Ollama (`AnalyzeImageAsync`) | Lit cases à cocher + manuscrit + mise en page → JSON structuré. Tesseract seul insuffisant (aveugle aux cases, faible sur le manuscrit). |
| Validation | Réutilise **cartouches éditables + niveaux de confiance** | Cohérent avec l'étape de relecture existante. |
| Conservation | Le **scan original** est archivé dans le dossier | Preuve de consentement (signatures, photo, autorisations). |

---

## 3. Modèle de données & correspondance des champs

> Convention de nommage AcroForm : `section_champ` en minuscules, sans accents
> (ex. `enfant_nom`, `pere_prenom`, `atcd_tdah`, `fratrie_2_prenom`).

### 3.1 Mapping formulaire → modèle

| Section formulaire | Champ(s) | Pré-rempli ? | Destination (modèle) |
|---|---|---|---|
| 1. Enfant | nom, prénom, DDN | ✅ | `PatientMetadata.Nom/Prenom/Dob` |
| 2. Père | prénom (pré), nom (même/diff), tél port., tél fixe, email, profession | partiel | **à créer** : contacts parent (voir §3.2) + bloc `famille` |
| 3. Mère | idem père | partiel | idem |
| 4. Adresse foyer | adresse (pré), correcte / à modifier | ✅ | `PatientMetadata.Adresse*` |
| 5. Situation familiale | parents ensemble/séparés/divorcés/garde alternée/recomposée/autre ; garde principale | ❌ | bloc `famille` (statut_parental) + **à créer** `SituationFamiliale` |
| 6. Fratrie | ordre, prénom (pré), DDN, correct / à corriger ; autre enfant | partiel | bloc `fratrie` |
| 7. Antécédents familiaux | TDAH, dyslexie, TSA, anxieux, dépression, bipolarité, addictions, T. suicide → oui/non/ne sait pas | ❌ | bloc `atcds` (atcds_familiaux) + **à créer** structure ATCD |
| 8. Photo | autorisation oui/non | ❌ | `PatientMetadata.ConsentementPhoto` + `PhotoFileName` |
| 9. Autorisations | usage infos, SMS, emails (oui/non) + signatures/dates | ❌ | **à créer** : `ConsentUsageInfos`, `ConsentSMS`, `ConsentEmail` |

### 3.2 Extensions de `PatientMetadata` à prévoir (Phase 0)

- **Contacts parents** (le modèle n'a aujourd'hui qu'un seul « Accompagnant ») :
  `PereNom/PrenomPere` n'existent pas en tant que contacts → ajouter
  `PereTelephone/PereEmail/PereProfession`, `MereTelephone/MereEmail/MereProfession`
  (les prénoms/âges/jobs cliniques restent dans le bloc `famille`).
- **Situation familiale** : `SituationFamiliale` (ensemble/séparés/divorcés/recomposée/autre),
  `ModeGardePrincipal`.
- **Consentements** : `ConsentUsageInfos`, `ConsentSMS`, `ConsentEmail` (bool?), + dates/signatures.
- **Antécédents familiaux structurés** : soit un sous-objet `AntecedentsFamiliaux`
  (dict clé→{oui/non/nesaitpas}), soit injection dans le bloc `atcds`. **À trancher** (cf. §7).

> ⚠️ Décision à valider : antécédents familiaux **structurés dans patient.json** (réutilisables,
> filtrables) **ou** simplement fusionnés en texte dans le bloc `atcds` (plus simple, cohérent
> avec la relecture par cartouches). Recommandation : **structuré** (cases à cocher = données nettes).

---

## 4. Feuille de route par phases

### Phase 0 — Modèle de données & correspondance ⏱️ socle
- [ ] Figer la liste définitive des champs (tableau §3.1) et la **convention de nommage**.
- [ ] Étendre `PatientMetadata` (§3.2) + sérialisation patient.json.
- [ ] Trancher la représentation des **antécédents familiaux** (structuré vs texte).
- **Livrable** : `PatientMetadata` à jour + tableau de mapping validé.
- **Critère d'acceptation** : un patient existant se charge sans casser patient.json.

### Phase 1 — Template HTML/CSS (design, one-shot)
- [ ] Reproduire la maquette en **HTML/CSS A4** (`@page { size: A4 }`), fidèle au visuel.
      Cases à lettres = rangée de `<span>` bordés (ou table 1 ligne) ; cases à cocher = caractères/CSS.
- [ ] Insérer des **placeholders** pour les valeurs pré-remplissables (`{{enfant_nom}}`, etc.),
      nommés selon la convention §3.
- [ ] Déposer le template dans `Resources/Formulaires/formulaire_completion.html`.
- [ ] Vérifier le rendu via `EdgeHeadlessPdfService.ConvertAsync()`.
- **Livrable** : template HTML + PDF de contrôle fidèle.
- **Critère** : le PDF Edge headless reproduit la maquette (cases/grilles correctes) sur A4.
- *(Prototype possible hors app — ex. Antigravity — puis intégration ; conserver le même moteur Edge.)*

### Phase 2 — Génération du formulaire pré-rempli
- [ ] `FormulaireCompletionService` : charge le template HTML → **remplace les placeholders** par les
      valeurs connues (`PatientMetadata` + blocs : enfant, parents, adresse, fratrie) → écrit un HTML
      temporaire → `EdgeHeadlessPdfService.ConvertAsync` → PDF.
- [ ] Bouton **« 📋 Formulaire parents »** dans le mode Consultation, **après l'Interrogatoire**.
- [ ] Aperçu + export PDF dans le dossier patient (`{annee}/documents/`).
- **Livrable** : PDF pré-rempli généré en 1 clic.
- **Critère** : Léo / Thomas / Sophie / adresse / fratrie déjà connus apparaissent pré-remplis.

### Phase 3 — Impression / remise
- [ ] Impression directe ou ouverture du PDF pour impression.
- **Critère** : formulaire imprimable A4 lisible, cases à remplir vides correctes.

### Phase 4 — Scan + extraction vision (local)
- [ ] Réutiliser `ScannerService` / import d'image existant.
- [ ] `FormulaireExtractionService` : envoie l'image du scan à **Ollama vision** (`gemma3:12b`)
      via `AnalyzeImageAsync` avec un **prompt → JSON** strict (schéma = champs §3.1).
- [ ] Parsing JSON tolérant (réutiliser `StripMarkdownFences`) + **niveau de confiance par champ**
      (case cochée = haute ; manuscrit = à vérifier).
- **Livrable** : JSON structuré depuis un scan.
- **Critère** : sur un formulaire test rempli main, antécédents (cases) corrects ; champs manuscrits
  marqués « à vérifier ».

### Phase 5 — Relecture / validation → dossier
- [ ] Écran de relecture (réutilise cartouches + code couleur confiance) : le médecin voit
      pré-rempli vs extrait, corrige, **valide**.
- [ ] À la validation : écrire dans `PatientMetadata` (contacts, consentements, situation) et
      **fusionner** les compléments cliniques dans les blocs (`famille`, `fratrie`, `atcds`,
      `developpement`) — réutiliser `MergeVerifiedFactsIntoBlockAsync`.
- **Critère** : aucune écriture sans clic de validation ; données visibles dans le dossier + panneau Admin.

### Phase 6 — Conservation & photo
- [ ] Archiver le **scan original** (preuve de consentement) dans `{annee}/documents/`.
- [ ] Photo enfant : prise au cabinet, stockée **localement** (`PhotoFileName`), affichée dans la fiche.
- **Critère** : scan retrouvable ; photo affichée ; suppression possible sur demande.

---

## 5. Composants concernés (réutilisation maximale)

| Existant | Rôle dans ce plan |
|---|---|
| `EdgeHeadlessPdfService` (HTML → Edge headless / Chromium) | **Génération (Phase 2)** — moteur PDF principal |
| `PDFFormFillerService` (PdfSharp AcroForm) | Repli uniquement (dernier recours) |
| `ScannerService` / import image | Scan (Phase 4) |
| `ILLMService.AnalyzeImageAsync` (Ollama vision) | Extraction (Phase 4) |
| Cartouches éditables + `MergeVerifiedFactsIntoBlockAsync` + confiance couleur | Relecture/validation (Phase 5) |
| `PatientIndexService` / patient.json | Persistance (Phases 0, 5) |
| Page Administratif du dossier | Affichage des coordonnées (déjà fait) |

À créer : `FormulaireCompletionService` (génération), `FormulaireExtractionService` (vision→JSON),
écran de relecture dédié (ou réutilisation de l'écran cartouches).

---

## 6. Confidentialité, sécurité, éthique

- Génération et extraction **strictement locales** (PdfSharp + Ollama). Jamais OpenAI cloud sur la
  photo/le scan d'un mineur.
- Antécédents familiaux = données de santé sur des **tiers** → rester proportionné (oui/non/ne sait pas,
  pas de détails nominatifs imposés).
- Consentements (photo, SMS, email, usage) **explicites et révocables** ; refus sans friction.
- Conserver le scan signé comme **preuve juridique** de consentement.

---

## 7. Décisions ouvertes (à trancher avant Phase 1)

1. **Antécédents familiaux** : structurés dans patient.json **ou** texte fusionné dans `atcds` ?
   (reco : structuré).
2. **Rédaction du template HTML/CSS** : prototypage externe (Antigravity/Flash) puis intégration,
   ou directement dans le repo ? (Dans tous les cas, **rendu final via `EdgeHeadlessPdfService`**.)
3. **Contacts parents** : nouveaux champs `PatientMetadata` (reco) vs sous-objet dédié ?
4. **Écran de relecture** : réutiliser tel quel l'écran cartouches ou un écran spécifique formulaire ?
5. **Tablette** plus tard ? (hors périmètre V1 — papier d'abord, plus inclusif en salle d'attente.)

---

## 8. Ordre de réalisation conseillé

`Phase 0 (modèle)` → `Phase 1 (template)` → `Phase 2 (génération pré-remplie)` → **jalon démo : imprimer un formulaire pré-rempli** → `Phase 4 (extraction vision)` → `Phase 5 (relecture/validation)` → `Phase 6 (conservation/photo)`.

> Jalon de valeur le plus rapide : **Phases 0→2** (formulaire pré-rempli imprimable). L'extraction
> (4-5) vient ensuite, une fois la chaîne papier validée en consultation réelle.
