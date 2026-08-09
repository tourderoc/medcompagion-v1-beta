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
| Extraction | **Hybride géométrie + vision** (voir §4 Phase 4) | Le formulaire étant **généré par nous**, sa géométrie est connue au pixel près : inutile de la faire redécouvrir par un modèle. Cases à cocher = mesure de densité d'encre à coordonnées connues (déterministe). Manuscrit = **GLM-OCR** sur bandes découpées. |
| ~~Extraction VLM page entière~~ | **Écarté** | Le tableau antécédents (8 lignes × 3 colonnes = 24 cases identiques) est le pire cas connu des VLM : décalage de ligne, confusion de colonnes, cochages hallucinés. Inacceptable sur des données de santé de tiers. |
| Modèle manuscrit | **`glm-ocr:latest`** via Ollama (1.1B, F16, 2,07 Go, MIT) | #1 OmniDocBench V1.5 (94.62). Sur des **bandes découpées** (une lettre par case), la tâche est triviale. Précision mesurée sur français accentué : parfaite. |
| Appel du modèle | **`GlmOcrService`** en streaming avec coupure au vol | ⚠️ **Défaut d'intégration mesuré** : le build Ollama de GLM-OCR n'émet jamais sa fin de séquence et réémet la transcription en boucle (jusqu'à 19 fois) jusqu'à épuiser `num_predict`. Constaté sur `/api/generate` **et** `/api/chat`, à température 0, avec toute consigne testée. Le service coupe donc le flux dès la première répétition : **31,5 s → 2 s** sur le même document. |
| Contexte | `num_ctx = 8192` (au lieu de 32768 par défaut) | Le défaut sature la VRAM d'une carte 6 Go en cache KV sans bénéfice. Mesuré : ~100 tok/s sur RTX 3050. |
| Forme des consignes | **Une seule ligne** | GLM-OCR est un modèle OCR, pas un modèle d'instruction : une consigne multi-lignes est recopiée telle quelle au milieu de la transcription. |
| ~~`gemma3:12b`~~ | **Écarté pour l'extraction** | Modèle généraliste, plus faible qu'un OCR spécialisé sur les grilles de cases et le manuscrit contraint. |
| Validation | Réutilise **cartouches éditables + niveaux de confiance** | Cohérent avec l'étape de relecture existante. |
| Conservation | Le **scan original** est archivé dans le dossier | Preuve de consentement (signatures, photo, autorisations). |

---

## 3. Modèle de données & correspondance des champs

> ⚠️ **Périmètre calé sur le template HTML réel** (`formulaire_completion.html`).
> Fratrie, profession et téléphone fixe ont été retirés du formulaire papier — ils ne font plus
> partie du périmètre V1.

> Convention de nommage : placeholders `{{snake_case}}` dans le HTML, champs JSON en camelCase.

### 3.1 Mapping formulaire → modèle (ce qui existe dans le template)

| Zone template | Placeholder(s) HTML | Pré-rempli ? | Destination (modèle) |
|---|---|---|---|
| **Bandeau enfant** (haut de page) | `{{enfant_prenom}}` `{{enfant_nom}}` `{{enfant_dob}}` `{{ecole}}` `{{classe}}` | ✅ | `PatientMetadata.Prenom/Nom/Dob` + bloc `scolarite` |
| **Date du RDV** | `{{date_rdv}}` | ✅ | date de génération |
| **1. Coordonnées père** | `{{pere_prenom}}` + cases nom vides + cases tél portable vides + cases email vides | partiel (prénom seul) | **à créer** : `PerePrenomContact`, `PereNomContact`, `PereTelephone`, `PereEmail` |
| **2. Coordonnées mère** | `{{mere_prenom}}` + idem | partiel (prénom seul) | **à créer** : `MerePrenomContact`, `MereNomContact`, `MereTelephone`, `MereEmail` |
| **3. Adresse** | `{{adresse_rue}}` `{{adresse_cp}}` `{{adresse_ville}}` | ✅ | `PatientMetadata.Adresse*` |
| **4. Situation familiale** | cases à cocher : ensemble/séparés/divorcés/garde alternée/recomposée/autre + mode de garde principal | ❌ | **à créer** : `SituationFamiliale`, `ModeGardePrincipal` |
| **5. Antécédents familiaux** | cases oui/non/ne sait pas × 8 items (TDAH, dyslexie, TSA, anxieux, dépression, bipolarité, addictions, T. suicide) | ❌ | **à créer** : `AntecedentsFamiliaux` (dict clé → oui/non/ne sait pas) |
| **6. Photo** | case autorisation oui/non | ❌ | **à créer** : `ConsentementPhoto` (bool?) |
| **7. Autorisations** | usage infos oui/non, SMS oui/non, emails oui/non | ❌ | **à créer** : `ConsentUsageInfos`, `ConsentSMS`, `ConsentEmail` (bool?) |

### 3.2 Extensions de `PatientMetadata` à prévoir (Phase 0)

- **Contacts parents** : ajouter `PerePrenomContact`, `PereNomContact`, `PereTelephone`, `PereEmail`
  et les équivalents `Mere*`. Les données cliniques (âge, métier) restent dans le bloc `famille`.
- **Situation familiale** : `SituationFamiliale` (ensemble/séparés/divorcés/recomposée/autre),
  `ModeGardePrincipal`.
- **Consentements** : `ConsentementPhoto`, `ConsentUsageInfos`, `ConsentSMS`, `ConsentEmail` (bool?).
- **Antécédents familiaux** : sous-objet `AntecedentsFamiliaux`
  (dict clé → `"oui"/"non"/"ne sait pas"`) — cas à cocher = données nettes, structuré dans patient.json.

> Fratrie, profession et téléphone fixe : **hors périmètre V1** (absents du template papier).

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

### Phase 4 — Scan + extraction **hybride** (local)

> **Principe** : deux besoins de natures différentes → deux techniques différentes.
> Les cases à cocher ne sont **pas** un problème d'OCR mais de géométrie ; seul le manuscrit
> justifie un modèle.

#### 4a — Instrumentation du template ✅ **FAIT**
- [x] **Nommage** dans `formulaire_completion.html` : `data-field` + `data-charset` sur chaque
      champ. La clé `data-field` est aussi la clé du JSON de sortie → une seule source de vérité.
- [x] Table des antécédents : `data-atcd` nomme la **ligne**, le script dérive les 3 colonnes
      (`atcd_<ligne>_oui|non|nsp`). Ajouter une ligne = un seul attribut, pas trois.
- [x] **4 repères de calage** à 3 mm des bords : 4 mm en haut-gauche/haut-droit/bas-gauche,
      **2,5 mm en bas-droite** — l'asymétrie détecte une numérisation à 180°. Placés dans la marge
      (contenu à 10 mm) : aucun recouvrement.
- **Mesuré** : 73 champs nommés — 55 cases à cocher (dont 27 antécédents), 10 rangées de lettres,
  8 lignes libres. Aucun champ ne déborde de l'A4.

#### 4b — Carte de coordonnées auto-générée ✅ **FAIT**
- [x] `<script>` du template : après rendu des cases de lettres, parcourt tous les `[data-field]`,
      convertit `getBoundingClientRect()` en **mm page** et écrit le JSON dans `<div id="coordmap">`.
      Émet aussi **une entrée par cellule** des rangées de lettres (lecture caractère par caractère).
- [x] `EdgeHeadlessPdfService.ExtractCoordMapAsync()` : invocation Edge `--dump-dom`
      + `--virtual-time-budget=5000`. **Mesuré : 1,7 s.**
- [ ] Écrire la carte en sidecar à côté du PDF, **versionnée avec le template** (Phase 2).
- **Piège rencontré** : `msedge.exe` est une application GUI — sa sortie standard n'est pas
  capturable par une redirection de shell (`>` donne un fichier vide). Il faut un tube explicite
  (`RedirectStandardOutput`), ce que fait le service.
- **Second piège** : sans `--user-data-dir` dédié, Edge headless partage le profil par défaut et la
  génération peut échouer **sans message** quand le navigateur du médecin est ouvert. Corrigé sur
  `ConvertAsync` **et** `ExtractCoordMapAsync`.
- **Reste** : mode debug superposant les rects sur le PDF (confort de vérification, non bloquant).

#### 4c — Redressement du scan
- [ ] Détecter les 4 repères → **homographie** → image canonique 210×297 mm @ 300 dpi
      (2480×3508 px). Corrige inclinaison **et** perspective (photo smartphone acceptable).
- **Critère** : un scan penché de 5° et une photo en biais donnent la même image canonique.

#### 4d — Cases à cocher : densité d'encre (déterministe, sans IA)
- [ ] Pour chaque case : cropper le rect **avec un inset de ~15 %** (exclure la bordure imprimée),
      mesurer le taux de pixels sombres.
- [ ] **Double seuil** → trois états : `cochée` / `vide` / **`incertaine`**. La bande grise
      intermédiaire force la relecture au lieu de deviner.
- [ ] Calibrer les seuils empiriquement sur des formulaires réels (une case vide n'est pas à 0 %).
- **Critère** : sur 24 cases d'antécédents remplies main, **zéro faux positif/négatif silencieux** ;
  tout doute remonte en `incertaine`.

#### 4e — Cases à lettres : GLM-OCR sur bandes découpées
- [ ] `ollama pull glm-ocr:latest`.
- [ ] Ajouter `OcrModel` à `AppSettings` (défaut `glm-ocr:latest`) et instancier un provider dédié :
      `new OllamaLLMProvider(_settings.OllamaBaseUrl, _settings.OcrModel)` — **même motif que
      l'anonymisation** (`OpenAIService.cs:192`), pour ne pas perturber le modèle de conversation.
- [ ] Cropper chaque `.letter-boxes-row` (le `data-len` donne le nombre de cellules) et envoyer la
      bande à `AnalyzeImageAsync` avec **jeu de caractères contraint par champ** :
      `A-Z` (noms), `0-9` (téléphone), `alphanum + @ . - _` (email).
- [ ] Parsing tolérant (`StripMarkdownFences`) + longueur attendue = `data-len` (contrôle de cohérence).
- **Point dur assumé** : l'**email** (chaîne arbitraire, casse significative, aucun dictionnaire de
  rattrapage) → **toujours** marqué à vérifier, quel que soit le score.
- **Critère** : noms et téléphones lus correctement en capitales d'imprimerie ; emails proposés
  mais systématiquement en relecture.

#### 4f — Fusion
- [ ] `FormulaireExtractionService` assemble géométrie + OCR → JSON unique (schéma §3.1) avec
      **niveau de confiance par champ** (case nette = haute ; case incertaine = basse ;
      manuscrit = à vérifier ; email = toujours à vérifier).
- **Livrable** : JSON structuré depuis un scan, prêt pour l'écran de relecture (Phase 5).

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
| `EdgeHeadlessPdfService` (HTML → Edge headless / Chromium) | **Génération (Phase 2)** + **carte de coordonnées (Phase 4b)** via `--dump-dom` |
| `PDFFormFillerService` (PdfSharp AcroForm) | Repli uniquement (dernier recours) |
| `ScannerService` / import image | Scan (Phase 4c) |
| `ILLMService.AnalyzeImageAsync` (Ollama `/api/generate` + `images[]`) | **Manuscrit uniquement (Phase 4e)** — déjà au bon format pour GLM-OCR, aucune plomberie à changer |
| Motif provider dédié (`OpenAIService.cs:192`) | Instancier `glm-ocr` sans toucher au modèle de conversation (Phase 4e) |
| Cartouches éditables + `MergeVerifiedFactsIntoBlockAsync` + confiance couleur | Relecture/validation (Phase 5) |
| `PatientIndexService` / patient.json | Persistance (Phases 0, 5) |
| Page Administratif du dossier | Affichage des coordonnées (déjà fait) |

À créer : `FormulaireCompletionService` (génération), `FormulaireGeometryService` (calage +
densité d'encre, Phases 4c-4d), `FormulaireExtractionService` (orchestration géométrie + OCR → JSON,
Phase 4f), écran de relecture dédié (ou réutilisation de l'écran cartouches).

---

## 6. Confidentialité, sécurité, éthique

- Génération et extraction **strictement locales** (PdfSharp + Ollama). Jamais OpenAI cloud sur la
  photo/le scan d'un mineur.
- Antécédents familiaux = données de santé sur des **tiers** → rester proportionné (oui/non/ne sait pas,
  pas de détails nominatifs imposés).
- Consentements (photo, SMS, email, usage) **explicites et révocables** ; refus sans friction.
- Conserver le scan signé comme **preuve juridique** de consentement.

---

## 7. Décisions ouvertes (à trancher avant Phase 4)

1. **Antécédents familiaux** : ✅ **Tranché** — structurés dans patient.json
   (dict clé → oui/non/ne sait pas). Cases à cocher = données nettes.
2. **Template HTML/CSS** : ✅ **Fait** — `Resources/Formulaires/formulaire_completion.html`.
3. **Contacts parents** : ✅ **Tranché** — nouveaux champs plats dans `PatientMetadata`
   (`PerePrenomContact`, `PereNomContact`, `PereTelephone`, `PereEmail` + équivalents Mère).
4. **Fratrie / profession / tél fixe** : ✅ **Hors périmètre V1** — absents du template papier.
5. **Écran de relecture** : réutiliser l'écran cartouches existant ou écran dédié formulaire ?
6. **Tablette** : hors périmètre V1 — papier d'abord, plus inclusif en salle d'attente.
7. **Méthode d'extraction** : ✅ **Tranché** — hybride. Géométrie (densité d'encre à coordonnées
   connues) pour les cases à cocher, **GLM-OCR** sur bandes découpées pour le manuscrit. Voir §4 Phase 4.
8. **Bibliothèque de traitement d'image** : ✅ **Tranché — aucune.** On ne redresse jamais l'image :
   on calcule la transformation depuis les 4 repères, on projette les coins de chaque champ dans le
   scan d'origine et on y échantillonne les pixels. Restent à écrire : détection des repères
   (seuillage dans les zones d'angle, dont on connaît la position) et une homographie 4 points
   (système 8×8). Les pixels bruts viennent de WPF (`CopyPixels`). **Zéro dépendance nouvelle**,
   cohérent avec le choix d'Edge préinstallé.
9. **QR code sur le formulaire** : ⏸️ **Reporté** — à revoir plus tard, hors périmètre immédiat.

---

## 8. Ordre de réalisation conseillé

`Phase 0 (modèle)` → `Phase 1 (template)` → `Phase 2 (génération pré-remplie)` → **jalon démo : imprimer un formulaire pré-rempli** → `Phase 4a→4f (extraction hybride)` → `Phase 5 (relecture/validation)` → `Phase 6 (conservation/photo)`.

> Jalon de valeur le plus rapide : **Phases 0→2** (formulaire pré-rempli imprimable). L'extraction
> (4-5) vient ensuite, une fois la chaîne papier validée en consultation réelle.

> **Ordre imposé dans la Phase 4** : `4a` (nommage + repères) bloque tout le reste — sans
> `data-field` ni repères, ni la carte de coordonnées ni le calage ne sont possibles.
> `4d` (cases) est **indépendant de tout modèle** et couvre la majorité du formulaire : il peut être
> livré et validé en consultation réelle **avant** que GLM-OCR soit installé.
