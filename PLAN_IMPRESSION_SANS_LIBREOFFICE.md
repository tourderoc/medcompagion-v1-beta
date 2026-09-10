# PLAN — Impression sans LibreOffice (courriers, attestations, ordonnances)

> **Statut :** plan validé dans son principe le 7 septembre 2026, chantier non démarré.
> **Date d'ouverture :** 7 septembre 2026
> **Déclencheur :** LibreOffice ne s'ouvre plus à l'impression (instance `soffice -p` cachée + dialogue de récupération en attente). Voir aussi le correctif `--norestore` déjà appliqué aux trois sites d'impression.
> **Docs liés :** [PLAN_RESTITUTION_PARENTS.md](PLAN_RESTITUTION_PARENTS.md) (le flux HTML → PDF qu'on réutilise), [CLAUDE.md](CLAUDE.md)

---

## 1. Où LibreOffice intervient aujourd'hui

Trois endroits, dont un seul est légitime.

| Usage | Où dans le code | Verdict |
|---|---|---|
| **Impression** : le `.docx` est envoyé à Windows avec le verbe `print`, Windows le confie à LibreOffice en caché | `OrdonnancesControl.xaml.cs` (`ImprimerOrdonnanceButton2_Click`), `AttestationsControl.xaml.cs` (`OnFilePrintRequested`), `CourriersControl.xaml.cs` (`OnFilePrintRequested`), `FileOperationService.PrintFile` | **À supprimer** — c'est la panne du 7 septembre |
| **Conversion docx → PDF** par `soffice --headless` | `DocxToPdfService`, appelé par `OrdonnanceService.SaveOrdonnanceMedicaments` et `OrdonnanceService.ConvertMarkdownToDocxAndPdf` | **À supprimer** — remplacé par le flux HTML |
| **Ouverture du docx** au double-clic (lecture, retouche éventuelle) | `LettersList_MouseDoubleClick`, bouton Voir des attestations, ordonnances | **Conservé** — LibreOffice reste un simple afficheur, jamais indispensable |

Fait structurant qui rend le chantier propre : **le markdown est la source, le docx n'est qu'un dérivé.** Le bouton Modifier édite le markdown dans MedCompanion et régénère le docx à chaque sauvegarde (`CourriersViewModel`, `AttestationViewModel`). Les ordonnances BIO et IDE ont déjà un PDF natif QuestPDF, hors périmètre.

## 2. La décision

> **Le PDF devient le document d'impression. Il est produit par un gabarit HTML A4, converti par Edge en mode caché, et imprimé par le moteur PDFium embarqué avec le dialogue d'impression Windows.**

C'est exactement le flux du dossier de restitution (`EdgeHeadlessPdfService`, gabarits `Resources/Consultation/restitution_*.html`), étendu aux courriers, attestations et ordonnances médicaments.

Contraintes posées par le médecin :

1. **Aucune dépendance à un logiciel tiers, une licence ou un service.** Edge est livré avec Windows 10/11 et déjà utilisé par l'app ; PDFium est déjà embarqué via le paquet `PDFtoImage` (formulaires scannés).
2. **Rendu strictement identique à la génération docx actuelle** : même en-tête, même logo, mêmes polices, mêmes tailles, même signature, même pied. Rien ne doit changer à l'œil.
3. Le double-clic continue d'ouvrir le docx dans LibreOffice ; le bouton Imprimer n'y passe plus jamais.

### Approches écartées

| Approche | Pourquoi non |
|---|---|
| Verbe `print` sur le PDF au lieu du docx | Le lecteur PDF par défaut est Chrome, qui n'expose pas ce verbe. On retomberait sur une dépendance externe. |
| Impression par WebView2 (`CoreWebView2.PrintAsync`) | Faisable, mais on garde une seule voie HTML → PDF, celle de la restitution, pour ne pas maintenir deux moteurs. |
| Syncfusion DocIO (référencé dans le csproj, jamais branché) | Clé de licence à enregistrer, rendu proche mais non garanti. Refusé : dépendance tierce. |
| Re-mise en page QuestPDF depuis le markdown | Un second layout à maintenir, pas identique au pixel au docx. Le HTML est plus proche de la mise en page Word et plus simple à ajuster. |

## 3. Inventaire du rendu à reproduire

### 3.1 Gabarit « courrier » — partagé par courriers et attestations

Source : `LetterService.ExportToDocx` (à partir de la ligne 872) et `ParseMarkdownToWordProfessional`.

- **Page** : A4, marges 1,5 cm sur les quatre côtés.
- **En-tête** : table deux colonnes sans bordure, cellules centrées verticalement.
  - Gauche : logo 4 × 4 cm, premier `.png` trouvé dans `Assets/logo.png/`. Repli : 🦋 en 24 pt.
  - Droite : six lignes Arial 9 pt — médecin (**gras**), spécialité, `RPPS : …`, `FINESS : …`, `Tél : …`, `Courriel : …`. Valeurs lues dans les réglages (`_settings.Medecin`, `Specialite`, `Rpps`, `Finess`, `Telephone`, `Email`).
- **Deux lignes vides** après l'en-tête.
- **Corps** (markdown, front-matter YAML retiré) :
  - `# Titre` : centré, gras, Arial 14 pt.
  - `## Sous-titre` : gras, Arial 12 pt, aligné à gauche.
  - Paragraphe : justifié, Arial 10 pt, interligne simple, 0 pt avant et après. `**gras**` et `*italique*` en ligne.
  - Ligne vide du markdown : paragraphe vide conservé.
  - Tableau markdown : bordure extérieure 1 pt `#2980B9`, bordures intérieures 0,5 pt `#BDC3C7`, en-tête grisé (voir `CreateWordTableFromMarkdown`, ligne ~1630), petit espace après.
- **Signature** : une ligne vide, puis à droite : `Fait au {Ville}, le {date}` 11 pt ; `{Medecin}` **gras** 11 pt ; `Pédopsychiatre` 11 pt.
- **Signature numérique** (si `EnableDigitalSignature`) : ligne vide, image `SignatureImagePath` 3 × 1,5 cm alignée à droite, puis `Signé numériquement le dd/MM/yyyy à HH:mm:ss` Arial 9 pt italique `#666666`.
- **Pied** : trois lignes vides, adresse centrée Arial 9 pt `#666666`. **Le pied est dans le flux du texte, pas ancré en bas de page** : on garde ce comportement.
- **Empreinte** (si signature numérique) : `Empreinte SHA-256: {32 premiers caractères}...` centré, Arial 7 pt `#AAAAAA`, juste après l'adresse.

### 3.2 Gabarit « ordonnance médicaments »

Source : `OrdonnanceDocxService.GenerateOrdonnanceMedicamentsDocx`.

- **Page** : marges haut 0,7 cm, droite 1,27 cm, bas 2,54 cm, gauche 1,27 cm (400 / 720 / 1440 / 720 twips).
- **En-tête** : table pleine largeur, trois colonnes alignées en haut.
  - 50 % : `Dr Nair LASSOUED` **gras** 12 pt ; `PSYCHIATRE DE L'ENFANT ET DE L'ADOLESCENT` ; `390 Avenue de la Première Dfl` ; `83220 Le Pradet` ; `Tel: 07 52 75 87 32`. (Valeurs codées en dur dans le service : à reprendre telles quelles, ou à lire des réglages si on veut en profiter — à décider, sans changer l'affichage.)
  - 25 % : `N° AM :` 8 pt puis image `Assets/barcode_am.png` 150 × 75 ; repli texte `831018791` gras.
  - 25 % : `N° RPPS :` 8 pt puis image `Assets/barcode_rpps.png` 150 × 75 ; repli `_settings.Rpps` gras.
- Ligne vide, **date abrégée à droite** (`FormatDateAbrege(dateCreation)`).
- Ligne vide, **bloc patient** : `M./Mme NOM Prénom, né(e) NOM Prénom` puis `Né(e) le jj/mm/aaaa (N ans[ M mois])`.
- Ligne vide, **médicaments** : pour chacun — dénomination en MAJUSCULES **gras** 11 pt ; posologie ; ligne de soulignés si durée ; `Quantité suffisante pour {durée}[ (N boîtes)]` ; `À renouveler N fois` **gras** vert `#2E7D32` si renouvelable ; ligne vide.
- Deux lignes vides, **signature à droite** : `Dr Lassoued Nair` **gras** 11 pt, date abrégée 10 pt, image de cartouche (`GetSignatureImagePath`).

### 3.3 Deux précisions pour rester « identique »

- **La date.** Le docx affiche la date de génération. Le PDF étant produit au moment d'imprimer, il faut prendre **la date du document** (front-matter ou date du fichier `.md`), jamais la date d'impression, sinon un courrier réimprimé un mois plus tard changerait de date.
- **L'empreinte SHA-256.** Aujourd'hui calculée sur le docx avant insertion de la ligne. Pour le PDF : calculer sur le HTML avant insertion de la ligne, même principe, même affichage.

Le logo pèse 3,9 Mo, ce qui explique les docx de 4 Mo. On le laisse tel quel, ça n'affecte pas le rendu ; le compresser est un sujet à part.

## 4. Le chantier

Ordre conseillé : gabarit courrier d'abord (c'est le cas de la panne), puis attestations (même gabarit), puis ordonnance médicaments.

### Étape 1 — Service markdown → HTML

- Nouveau `Services/Impression/DocumentHtmlService.cs` : lit les mêmes réglages médecin que `LetterService`, applique le gabarit « courrier » ou « ordonnance médicaments », produit un HTML autonome (CSS inline, images en `data:` ou chemin `file:///`).
- Gabarits dans `Resources/Impression/courrier_template.html` et `ordonnance_medicaments_template.html`, avec `@page { size: A4; margin: … }` reprenant les marges ci-dessus.
- Réutiliser la logique de `ParseMarkdownToWordProfessional` (titres, paragraphes, inline, tableaux, YAML) en la portant vers du HTML — ne pas la dupliquer à la main sans relire chaque cas.

### Étape 2 — Conversion HTML → PDF

- `EdgeHeadlessPdfService.ConvertAsync` tel quel. Le PDF est écrit **à côté du `.md`** avec le même nom de base, comme le docx.
- Règle de fraîcheur : régénérer le PDF seulement si le `.md` est plus récent que le `.pdf` (ou si le PDF manque).

### Étape 3 — Service d'impression PDF embarqué

- Nouveau `Services/Impression/PdfPrintService.cs` : rasterise chaque page avec `PDFtoImage.Conversion.ToImage` à 300 dpi, construit un `FixedDocument` WPF une page par image, et l'envoie via `PrintDialog` (choix d'imprimante). Variante silencieuse vers l'imprimante par défaut possible en option.
- Aucun paquet nouveau.

### Étape 4 — Branchement

- `CourriersControl.OnFilePrintRequested`, `AttestationsControl.OnFilePrintRequested`, `OrdonnancesControl.ImprimerOrdonnanceButton2_Click`, `FileOperationService.PrintFile` : le chemin reçu est celui du `.md` (ou du docx dont on déduit le `.md`), on génère ou rafraîchit le PDF, on imprime.
- `OrdonnanceService.SaveOrdonnanceMedicaments` et `ConvertMarkdownToDocxAndPdf` : remplacer `DocxToPdfService` par le flux HTML. `DocxToPdfService` devient inutilisé, à supprimer avec ses références.
- La génération docx (`ExportToDocx`, `GenerateOrdonnanceMedicamentsDocx`) **ne bouge pas**.

### Étape 5 — Garde-fous

- Si le `.docx` est plus récent que le `.md` (retouche manuelle dans LibreOffice), prévenir avant d'imprimer : « le docx a été modifié à la main, le PDF imprimé ne contient pas ces retouches » avec choix imprimer quand même / annuler.
- Si Edge est introuvable (`IsAvailable == false`) : message clair, pas de repli silencieux vers LibreOffice.
- Timeout Edge 30 s déjà géré par le service.

### Étape 6 — Vérification

Comparer côte à côte docx (ouvert dans LibreOffice) et PDF pour :

1. le courrier `2026-09-07_1326_courrier` (GANDOLPHE Emma) — cas de la panne ;
2. une attestation ;
3. une ordonnance médicaments avec renouvellement et un enfant de moins de 3 ans (mois affichés) ;
4. un courrier contenant un tableau markdown et des `**gras**` ;
5. signature numérique activée puis désactivée.

Puis imprimer réellement sur l'EPSON ET-3950 et vérifier que l'impression Icanopee (PDF virtuel) accepte le flux.

## 5. Points ouverts

- **Dialogue d'impression ou envoi direct ?** Recommandation : dialogue (quatre imprimantes physiques plus Icanopee sur le poste). À confirmer.
- **L'aperçu (bouton œil) affiche-t-il le PDF plutôt que le markdown ?** Optionnel. Si oui, ce que le médecin voit dans l'app est exactement ce qui sort.
- **Coordonnées codées en dur dans l'ordonnance médicaments** : les lire des réglages ou les laisser ? Sans impact visuel tant que les réglages contiennent les mêmes valeurs.

## 6. Hors périmètre

- Ordonnances BIO et IDE (QuestPDF, déjà sans LibreOffice).
- Génération docx et ouverture au double-clic.
- Compression du logo.
- Signature numérique elle-même (on la reproduit, on ne la revoit pas).

**Effort estimé :** quelques heures, sans dépendance nouvelle.
