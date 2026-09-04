# PLAN — Cartographie de l'enfant V2 (reconstruction)

> **Statut :** bloc fonctionnel, éprouvé de bout en bout sur patient réel (1er septembre 2026). Construit à côté du bloc Évaluation, sans y toucher — le branchement des deux reste à décider.
> **Date d'ouverture :** 1er septembre 2026
> **Remplace à terme :** [GRILLES_CARTOGRAPHIE_CANONIQUES.md](GRILLES_CARTOGRAPHIE_CANONIQUES.md) (laissé intact — témoin de ce qui tourne aujourd'hui)
> **Docs liés :** [PLAN_PHASE_EVALUATION_V0.md](PLAN_PHASE_EVALUATION_V0.md), [PLAN_RESTITUTION_PARENTS.md](PLAN_RESTITUTION_PARENTS.md)

---

## 1. Pourquoi on reconstruit

La V1 met **toute** l'adaptation à l'âge dans la grille de conversion score → couleur, et **zéro** dans les items. Les mêmes 6 affirmations servent à 3 ans et à 11 ans. Ça produit un artefact aux deux bouts :

- **Plafond (10-11 ans).** Les items sont si faciles à cet âge que tout sature à 6/6. Pour récupérer du pouvoir discriminant, la grille a été durcie : 5/6 = jaune foncé, 4/6 = rouge foncé. L'instrument n'a plus que 3 niveaux utilisables sur 6, et un seul « non » — même sur l'item le moins pertinent — déclenche une alerte.
- **Plancher (3-4 ans).** Deux items de l'Attachement portaient sur les copains d'école. Un enfant de 3 ans sécure et pas encore scolarisé perd 2 points structurellement : son plafond réel est 4/6, il ne peut pas atteindre le vert foncé.

Comme l'outil sert à **orienter** (« cet axe mérite-t-il un bilan standardisé ? »), un artefact de seuil n'est pas cosmétique : c'est une demande de bilan en trop ou en moins.

## 2. La décision structurante

> **La grille de couleur devient unique. Ce sont les questions qui changent selon l'âge.**

Aujourd'hui les items sont fixes et la grille bouge. C'est l'inverse. La difficulté redevient une propriété des questions, là où elle doit être.

### 2.1 La grille unique

C'est celle des 5-6 ans de la V1 — la seule des quatre qui soit bien formée : strictement monotone, un niveau par score, les 6 niveaux utilisés. Les trois autres en sont des déformations.

| Score | Niveau |
|---|---|
| 6 | Vert foncé |
| 5 | Vert clair |
| 4 | Jaune clair |
| 3 | Jaune foncé |
| 2 | Rouge clair |
| 1 | Rouge foncé |
| 0 | Rouge foncé |

### 2.2 Les 4 tranches d'âge

**3-4 / 5-6 / 7-9 / 10-11** — inchangées par rapport à la V1. Elles suivent les vraies ruptures développementales (avant école / GS-CP / élémentaire / préado).

### 2.3 Le cahier des charges d'écriture

Dans chaque tranche, les 6 items doivent être calibrés pour qu'un **enfant qui va bien à cet âge-là en coche 5 ou 6**. Des items qui donnent 6/6 à tout le monde sont trop faciles : c'est un défaut d'écriture, et non plus quelque chose qu'on rattrape en durcissant la conversion.

### 2.4 Rôle du LLM

Med **n'écrit pas les items** — le seuil décide d'une orientation, il doit être stable. Med sert à :
- choisir la tranche et pré-sélectionner les sphères pertinentes selon le 1er entretien ;
- **contextualiser l'illustration, pas l'énoncé** — l'affirmation cotée reste canonique, Med ajoute entre parenthèses un exemple tiré du dossier (le prénom de la fratrie, « à la garderie » vs « au collège ») ;
- opérer le passage **axe → hypothèse → orientation** après la séance.

---

## 3. Le déroulé réel de la séance

C'est lui qui dicte la forme du bloc. La moitié « parents » ne coûte pas une minute de consultation — c'est ce qui fait tenir la cartographie dans une séance normale.

| Moment | Qui | Quoi |
|---|---|---|
| **Avant le RDV** | médecin | Med génère la feuille (tranche d'âge + illustrations du 1er entretien), impression |
| **Accueil** | médecin + parents | remise de la feuille |
| **Séance** | enfant en cabinet / parents en salle d'attente | les 3 profils sont remplis par observation — les 5 questionnaires par le parent |
| **–10 min** | tout le monde | récupération de la feuille, scan (~2 min), puis résumé oral du comportement de l'enfant, écoute des parents, « on est encore en phase d'évaluation » |
| **Après** | médecin | lecture croisée, hypothèse, orientation |

### 3.1 Le partage parent / médecin

Partage **par accès à l'information**, et c'est ce qui le rend solide :

- **5 questionnaires cotés — remplis par le parent** : Attachement, Langage, Émotions, Imaginaire, Pensée. Ils portent sur ce que seul le parent voit : la durée, le quotidien, le lien.
- **3 profils descriptifs (6 axes 1-5, sans score ni couleur) — remplis par le médecin en observant** : Tempérament, Psychomotricité, Attention. Ils portent sur ce que seul le clinicien voit, dans la pièce, en 30 minutes.

Aucun des deux ne peut coter la moitié de l'autre.

### 3.2 Règles du bloc

1. **Le scan pendant la séance n'affiche aucun résultat.** Ni couleur, ni score, ni roue. C'est un **accusé de lecture** : « feuille reconnue, 5 questionnaires, 30 cases lues ». L'écran de résultat appartient à l'après, quand la famille est partie.
2. **Le scan est non bloquant.** Il tourne en fond pendant que le médecin enchaîne sur son résumé oral. Pas de barre de progression devant la famille.
3. **L'image scannée est archivée**, pas seulement les cases extraites. Corollaire du point 1 : si la lecture rate, ça se découvre le soir, famille partie. Avec l'image, on relit et on corrige à la main. Et c'est de toute façon un document rempli par le parent, il a sa place au dossier.
4. **Lecture par cases cochées, pas par OCR de texte.** Position fixe, lecture sans ambiguïté et sans modèle. Le texte des items peut être contextualisé, la géométrie des cases ne bouge jamais.
5. **L'informateur est nommé** sur la feuille et stocké (mère / père / autre). Un 4/6 rempli par le parent qui vit avec l'enfant ne se lit pas comme un 4/6 rempli par celui qui le voit deux week-ends par mois. Une seule feuille, un seul informateur.
6. **Trois états d'attente** de la feuille :
   - remise → remplissage en salle d'attente, retour le jour même ;
   - confiée → transmise à l'autre parent, retour à la séance suivante ;
   - non revenue → **partiel, défaut de retour d'évaluation**. État enregistré, pas note libre : le dossier doit le dire et Med doit le savoir pour ne pas lire 3 profils comme une cartographie complète.
7. **Le bloc doit pouvoir se fermer avec les profils seuls.** Ils tiennent cliniquement tout seuls.
8. **Deux clôtures distinctes.** *Fin de séance* = profils remplis + feuille scannée, la donnée est là. *Fin du bloc* = hypothèse et orientation posées, plus tard, au bureau, éventuellement plusieurs jours après. Le bloc ne se referme pas quand la porte se ferme.
9. **Le résumé oral du comportement reste au médecin.** Med ne le reformule pas et ne l'assiste pas — décision explicite.

### 3.3 Manque identifié

Les 3 profils n'ont ni score, ni couleur, ni seuil — or l'exemple canonique de la démarche (« profil attention chargé → je demande un bilan attentionnel standardisé ») part d'un profil. Pas besoin d'un score, mais il manque, **par sphère**, un champ explicite : **hypothèse retenue** + **orientation proposée**. C'est ce qui matérialise la philosophie dans les données.

---

## 4. UI cible

- Une carte apparaît **automatiquement dans la frise, après la 1ère consultation clôturée**.
- Nom retenu provisoirement : **« Cartographie de l'enfant »** (plutôt que « 2ème séance » — si la feuille repart chez l'autre parent, le bloc déborde sur la séance suivante).
- Clic → zone de travail : bouton **« Imprimer questionnaire parents »**, puis les **3 profils** à remplir.
### 4.1 Ce qui est construit ✅

| Élément | Fichier |
|---|---|
| Les 120 items en code, par axe et par bande | [CartographieItemsV2.cs](MedCompanion/Models/Evaluations/CartographieItemsV2.cs) |
| Le gabarit A4 de la feuille parent | [questionnaire_cartographie.html](MedCompanion/Resources/Formulaires/questionnaire_cartographie.html) |
| Le générateur PDF (Edge headless) | [QuestionnaireCartographieService.cs](MedCompanion/Services/Evaluations/QuestionnaireCartographieService.cs) |
| Jalon « Cartographie de l'enfant » dans la frise, après la 1ère consultation | `RefreshFriseStages()` dans [ConsultationModeViewModel.cs](MedCompanion/ViewModels/ConsultationModeViewModel.cs) |
| Zone de travail : bouton d'impression + accès aux profils | `IsCartographieMode` dans [ConsultationModeControl.xaml](MedCompanion/Views/Consultation/ConsultationModeControl.xaml) |
| Les 18 axes observés en code, avec pôles, nature et couleurs | [ProfilsObservesV2.cs](MedCompanion/Models/Evaluations/ProfilsObservesV2.cs) |
| Écran des 3 profils : 18 lignes, 5 pastilles, couleur immédiate | [ProfilsObservesViewModel.cs](MedCompanion/ViewModels/ProfilsObservesViewModel.cs) + `ShowProfilsObserves` |
| Persistance de la fiche de séance (YAML + MD) | [CartographieV2Service.cs](MedCompanion/Services/Evaluations/CartographieV2Service.cs) |
| Carte dans l'onglet BILANS du dossier bleu | [CartographieV2CardViewModel.cs](MedCompanion/ViewModels/CartographieV2CardViewModel.cs) |

### 4.1.1 Sauvegarder = verser

**💾 Sauvegarder** écrit la fiche du jour **et** la fait apparaître dans l'onglet BILANS. Il n'y a pas de second geste de publication.

C'était l'inverse au départ : un bouton « Terminer la séance » gardait le dossier vierge tant que la séance n'était pas close, pour qu'il ne reçoive que du terminé. **Renversé à l'usage** — attendre la fin de la séance pour voir sa cartographie au dossier n'a pas de sens quand elle est déjà utile en cours de route.

Ce que la publication différée protégeait est en réalité porté par la carte elle-même : **son état de complétude dit ce qui est recueilli et ce qui manque.** Une cartographie en cours a sa place au dossier dès lors qu'elle dit qu'elle est en cours — et c'est plus sûr qu'un moment de publication qu'il faut penser à déclencher.

La sauvegarde reste **explicite** : pas d'enregistrement automatique, qui écrirait des profils à moitié remplis sans rien demander.

**Une fiche par séance :** `{patient}/{année}/cartographies/{yyyy-MM-dd}_cartographie.md`. Sauvegarder deux fois le même jour met à jour la même fiche — sans quoi l'onglet BILANS se remplirait de doublons au fil des clics. Rouvrir le bloc le même jour reprend les cotations déjà posées.

**Le titre de la carte ne porte pas « V2 »** : « Cartographie de l'enfant — 01/09/2026 (10 ans) ». Dans le dossier d'un enfant, à côté de vrais bilans, un numéro de version ne veut rien dire pour qui le lit — et ce dossier peut être lu par quelqu'un d'autre que son auteur. Le `version: 2` vit dans le fichier.

**Ce qui manque est écrit sur la carte** : « 18 axes observés · questionnaire parent non recueilli ». Sans cette ligne, dix-huit axes et aucun questionnaire se liraient comme une cartographie complète. C'est la règle du §3.2 point 6, appliquée dès maintenant.

**Suppression** : un bouton 🗑 sur la carte, avec confirmation. Si la fiche supprimée est celle de la séance en cours, l'écran des profils est remis à zéro — laisser des cotations affichées dont le fichier vient d'être supprimé donnerait à croire qu'elles sont encore enregistrées.

### 4.1.2 Agrandir la zone de travail

Le mécanisme **existait déjà** : `Focus Travail` (**F1**) masque le dossier bleu et donne toute la largeur à la zone de travail ; **F2** revient. Un bouton « ⛶ Agrandir » a simplement été ajouté dans le bandeau des profils pour le rendre découvrable là où il sert.

Mais la contrainte réelle des dix-huit lignes est la **hauteur**, pas la largeur. C'est pourquoi **la frise se replie pendant la saisie des profils** — c'est elle qui mangeait le haut de l'écran. Pas de fenêtre séparée : sur un écran unique, une seconde fenêtre impose un alt-tab en pleine consultation et peut se perdre derrière la principale.

Et surtout, **les trois profils sont disposés en trois colonnes** : six lignes de haut au lieu de dix-huit, plus de défilement du tout. Chaque axe tient sur deux lignes — libellé et pastilles, puis les deux pôles en petit dessous. Faire défiler avec un enfant en face coûte plus cher qu'un écran un peu dense.

### 4.1.3 Une séance appartient à un enfant

Règle inscrite dans le setter de `CurrentPatient` : **tout changement de patient purge la séance de cartographie en cours** — cotations, fiche courante, message d'état — referme le bloc et recharge les cartes du nouveau dossier.

Sans elle, deux défauts observés en test réel :
- les cotations d'un enfant restaient affichées sous le suivant ;
- une fiche créée sous un patient puis enregistrée sous un autre était **écrite dans le bon dossier avec le mauvais nom**.

En plus de la purge, le nom du patient est **réécrit à chaque enregistrement** plutôt que figé à la création de la fiche. Deux gardes valent mieux qu'une quand l'erreur consiste à mettre l'observation d'un enfant dans le dossier d'un autre.

**Aucun fichier existant du bloc Évaluation n'est modifié.** `CartographieEnfant`, `CartographieContent`, `CartographieScoringService` et `EvaluationPhaseService` sont intacts — c'est ce qui permet de construire sans toucher à la dette du §11.

### 4.2 La feuille imprimée

- **Un seul recto**, vérifié au rendu réel sur la tranche 10-11 ans (énoncés les plus longs). Pas de verso, donc pas de second calage au scan. Marge disponible pour les illustrations contextualisées à venir.
- **Un seul gabarit pour les 4 tranches.** La géométrie est identique : seul le texte des 30 énoncés change. D'où un seul jeton, **`MEDCOMP-FORM-CARTO-V1`**, qui désigne la *mise en page*. Le préfixe `MEDCOMP-FORM-` est celui du formulaire de complétion, volontairement : la feuille hérite ainsi de la **reconnaissance tolérante à l'OCR** déjà éprouvée (distance d'édition, décodage de version), mesurée sur exemplaire réel où un jeton était ressorti « MEDCOMP-FORN-COMPLETION-VS ». La tranche imprimée dans le bandeau sert à autre chose : dire à quels énoncés les cases lues correspondent. Elle doit être conservée avec les réponses, **jamais recalculée depuis l'âge courant de l'enfant**.
- **Deux cases Oui / Non**, à abscisse constante sur toute la feuille. Toutes les cases OUI partagent un x, toutes les NON un autre : la détection de marque n'a qu'un couple de colonnes à connaître. Deux cases plutôt qu'une : une case unique confondrait « non » et « pas répondu », et une ligne sautée par un parent produirait un point perdu, donc une couleur plus sombre, donc potentiellement une orientation.
- **Blocs franchement délimités** pour la lecture séquentielle : cadre plein, en-tête numéroté, `data-bloc` et `data-index` sur chacun, six lignes par bloc, ordre 1→5 figé.
- **Repères de calage et jeton** repris à l'identique du formulaire de complétion, repère bas-droit plus petit pour détecter un scan à 180°.
- **Pas de rappel du 1er entretien** — les parents en ont déjà reçu la restitution.
- **Pas de phrases-boussoles sur la feuille** : elles annoncent au parent ce qu'on mesure et orientent la réponse. Conservées pour l'écran et la restitution.
- **Champ informateur** en en-tête : Mère / Père / Autre + prénom et lien.
- Feuille vierge générée en temporaire, pas versée au dossier : c'est le **scan de la feuille remplie** qui sera archivé.

### 4.1.4 La carte 3 — récupérer la feuille remplie

Le bloc a maintenant **trois cartes dans l'ordre du temps** : imprimer (accueil) → observer (séance) → scanner (–10 min). La zone de travail n'est plus un menu, c'est le déroulé de la séance.

L'acquisition **reprend exactement le procédé du formulaire de complétion** : `ScanDocumentDialog`, qui offre le scanner, l'import d'un fichier et la photo. Rien de nouveau à maintenir.

Ce que fait la carte 3, et rien d'autre :
- copie l'image dans `{patient}/{année}/cartographies/{date}_questionnaire_scan.*`
- passe l'état à `scanne`, l'enregistre
- affiche **un accusé de réception**, pas un résultat : « Feuille reçue et archivée. La lecture des réponses se fera après la séance. »

Aucune analyse n'est lancée sur le moment. La famille est dans la pièce.

**L'état de la feuille est affiché sur la carte** et suit le vrai parcours : *non encore remise → remise, en attente de retour → scannée, lecture à faire → scannée et lue*. **Imprimer pose automatiquement « remise »** — dans ce flux, imprimer c'est pour remettre, et ça évite un clic dans un moment qui n'en a pas.

**« Scannée » ne veut pas dire « complète ».** Une feuille archivée mais non dépouillée n'apporte aucun score : `EstComplete` exige que les réponses soient **lues**, pas seulement acquises. La carte du dossier ne passera au vert qu'à ce moment-là.

### 4.1.5 Le dépouillement

Après le scan, une fenêtre s'ouvre — même enchaînement que le formulaire de complétion : **l'image de la feuille à gauche, les 30 réponses OUI / NON à droite**, et les 5 scores calculés en direct par la grille unique, avec leur couleur.

- **Trois états par item**, pas deux : OUI, NON, et vide. C'est la raison d'être des deux cases sur la feuille — une ligne sautée par le parent ne doit pas devenir un « non ».
- **Un axe partiellement rempli est signalé avant l'enregistrement** : son score sous-estime mécaniquement l'enfant, puisqu'il compte les « oui » sur six items dont certains n'ont pas de réponse.
- **La tranche vient de la fiche**, pas de l'âge courant : un enfant qui passe de 9 à 10 ans entre l'impression et le dépouillement ne doit pas changer de questionnaire en cours de route.
- Un bouton **« Reprendre le dépouillement »** rouvre la fenêtre sans rescanner.
- C'est **l'enregistrement des scores**, et non le scan, qui rend la cartographie complète.

**La feuille scannée est versée aux Documents du dossier bleu**, catégorie « Formulaires », exactement comme le formulaire de complétion : elle y est consultable, et **son crayon rouvre le dépouillement** au lieu de la saisie champ par champ, qui n'aurait rien à y lire.

**Le formulaire est déclaré, pas reconnu.** La reconnaissance par le contenu suppose une couche texte ; une feuille imprimée, remplie à la main puis scannée n'en a pas — le jeton comme le titre ne sont plus que des pixels. Le document repartait alors dans la catégorisation LLM, qui le classait « bilans » : au mauvais endroit, sans son crayon, et pondéré comme un élément clinique. Or l'utilisateur vient de cliquer « Scanner la feuille remplie » sur la carte Cartographie : **quand le geste dit déjà quel formulaire arrive, le faire deviner est une occasion de se tromper, pas une sécurité.** D'où `ImportFormulaireConnuAsync`, qui court-circuite la reconnaissance.

### 4.1.6 La lecture automatique

Bouton **⚡ Lecture automatique** dans la fenêtre de dépouillement. **Même méthode que le formulaire de complétion**, reprise pièce par pièce :

1. **La géométrie est lue sur le gabarit**, jamais codée en dur : le template porte sa propre carte de coordonnées (`<div id="coordmap">` + script), qu'Edge extrait au moment de la lecture. Un énoncé qui passerait sur deux lignes décalerait tout ce qui suit — mesurer évite d'avoir à l'anticiper.
2. La page scannée est **découpée bloc par bloc** (5 axes), avec 1 mm de marge.
3. Chaque bloc est **lu par le modèle vision** sous schéma JSON contraint — six lignes, deux colonnes.

Un bloc à la fois plutôt que la page entière : sur le formulaire, la lecture pleine page confondait des champs voisins. Ici une erreur reste contenue à un axe.

**Carte de coordonnées vérifiée sur le gabarit réel** : 5 blocs, 30 lignes, et **toutes les cases OUI à x = 176,48 mm, toutes les NON à x = 188,48 mm** — la promesse de conception (une seule paire de colonnes à connaître) tient, mesurée et non supposée. Les rects des 30 lignes et de leurs deux cases sont également émis : de quoi passer à une lecture pixel par pixel si le modèle vision se montre trop hésitant.

**Dans le doute, le lecteur répond « vide ».** Une case laissée vide se corrige à la main ; une case devinée passe inaperçue. Et rien n'est enregistré : le résultat **pré-remplit** la saisie, que le médecin vérifie sur l'image.

✅ **Éprouvée sur une vraie feuille manuscrite** (1er septembre 2026) : **29 réponses lues sur 30**, la trentième laissée vide par le modèle — exactement le comportement demandé par la consigne « dans le doute, réponds null ». Le médecin la complète à la main.

La fenêtre offre aussi, comme celle du formulaire, **le visualiseur PDF d'Edge** (zoom, défilement — c'est de l'écriture manuscrite qu'on relit) et **le sélecteur de modèle de lecture** (`LlamaCppProfiles.VisionCapable`), pour comparer les modèles sur une même feuille. Lire des croix dans de petites cases n'est pas lire des lettres capitales : rien ne dit que le meilleur modèle soit le même que pour le formulaire.

### 4.2.1 Le détail des réponses, pas seulement le score

**Les 30 réponses sont persistées**, six par axe (`oui` / `non` / `vide`), à côté des cinq scores.

Pourquoi : **un 4/6 ne dit pas la même chose selon QUELS items ont échoué.** En Attachement, manquer la séparation et la prudence avec l'inconnu, ou manquer le recours et la consolabilité, ce sont deux enfants différents — même score, même couleur. Les six dimensions de chaque axe sont stables précisément pour qu'on sache *ce qui* accroche ; ne garder que la somme jetterait l'information que cette structure existe pour produire.

- **Sur la carte du dossier**, chaque axe du questionnaire est **cliquable** : il déplie les six réponses du parent, ✓ vert / ✗ rouge / — gris. Replié par défaut — la carte porte déjà 18 axes de profil et 5 scores.
- **Dans le corps Markdown** de la fiche, chaque axe liste ses six énoncés avec leur réponse : la fiche se lit sans l'application.
- **La reprise d'un dépouillement retrouve l'exact contenu coché.** Auparavant, seul le score étant stocké, elle reconstituait « les N premiers items en oui » — faux dès que les oui n'étaient pas les premiers.

⚠️ Les fiches dépouillées avant ce changement n'ont que leurs scores. Le scan étant archivé, il suffit de rouvrir le crayon et de relancer la lecture pour reconstituer le détail.

### 4.2.2 Qui a rempli la feuille

La règle était posée dès le §3.2 : *un 5/6 rempli par le parent qui vit avec l'enfant ne se lit pas comme un 5/6 rempli par celui qui le voit deux week-ends par mois.* La feuille posait la question, **rien ne recueillait la réponse** — l'information était collectée puis jetée, ce qui est le pire des deux.

Elle est maintenant capturée à trois niveaux :
- **Lue automatiquement** : le bandeau « Qui remplit ce questionnaire ? » est une **zone** de la carte de coordonnées (`data-zone="informateur"`, mesurée à 29,5 → 38,0 mm), découpée et lue comme les blocs d'axes. Case cochée + prénom manuscrit.
- **Corrigeable** en tête du dépouillement : Mère / Père / Autre + prénom et lien.
- **Stockée** dans la fiche (`informateur`, `informateur_nom`) et affichée sous le titre du questionnaire, dans la carte comme dans le corps Markdown.

La lecture de l'informateur est **indépendante de celle des axes** : elle est reprise même si les blocs échouent, et son échec ne fait pas tomber les trente réponses.

### 4.3 Le bloc est fonctionnel ✅

Le parcours complet tourne, éprouvé de bout en bout sur un patient réel le 1er septembre 2026 : impression → observation → scan → lecture automatique → dépouillement vérifié → carte au dossier avec les deux moitiés.

**Une synthèse qui présente, et ne conclut pas — carte 4.** Elle a d'abord été écartée (« l'interprétation appartient aux étapes suivantes »), puis reprise sous une forme différente : ce n'est pas une seconde interprétation, c'est **le pont** qui manquait vers l'étape qui raisonne.

La ligne est fine et posée explicitement : cette synthèse dit *« voici les deux moitiés, voilà ce qu'elles valent, prêtes à être croisées »* — jamais *« cet enfant présente un trouble de X »*. Le jour où elle conclurait, il y aurait deux endroits où s'écrit le même diagnostic, et ils divergeraient. Le prompt l'interdit nommément : aucun diagnostic même sous forme d'hypothèse, aucune orientation, aucun recalcul de score.

### 4.3.1 Les deux curseurs de fiabilité

Le médecin qualifie **les deux moitiés**, pas seulement le questionnaire parent. Pondérer la seule feuille reviendrait à traiter implicitement les dix-huit axes observés comme certains — or un enfant vu vingt minutes, malade ou figé lors d'une première rencontre, ça se pondère aussi. La dissymétrie aurait été un jugement caché.

Ce que ça apporte : **rendre auditable ce qui reste sinon dans la tête du médecin.** Il sait, en récupérant la feuille, si elle a été remplie posément ou cochée en quatre-vingt-dix secondes dans le couloir. Cette information oriente déjà sa lecture ; sans elle, dans six mois, personne ne saura pourquoi tel 5/6 a pesé peu.

- **Quatre niveaux nommés** — Fiable (1,00) · Moyennement fiable (0,65) · Peu fiable (0,30) · Non exploitable. Le médecin choisit un **mot**, le système tient le **nombre** : on ne distingue pas 0,6 de 0,7 de façon reproductible, mais « fiable » de « peu fiable », oui.
- **Échelle 0-1**, la même que `SynthesisWeightTracker` et que le poids des documents importés — le modèle ne voit qu'une seule notion de poids.
- **Zéro n'est pas un poids, c'est un état.** « Non exploitable » porte un poids `null` : la source est **écartée et dite comme telle**, jamais pesée à zéro dans un calcul.
- **La fiabilité ne modifie jamais une valeur.** Un 4/6 reste un 4/6, sa couleur ne bouge pas. Elle qualifie la source, pas la mesure — sinon on obtiendrait des scores « ajustés » que plus personne ne pourrait retracer.
- **Les deux fiabilités sont exigées avant de rédiger** : une synthèse non qualifiée est précisément celle qu'on voulait éviter.

### 4.3.2 Un signal objectif en plus du jugement

Les **axes incomplets** sont affichés à côté des curseurs : un axe avec deux items sans réponse est mécaniquement plus faible, indépendamment de tout jugement. C'est une fiabilité **par axe**, que le curseur global ne peut pas produire — les deux se complètent au lieu de se répéter.

### 4.3.3 Le croisement appartient à la Synthèse Globale

**Décision : le croisement de cette synthèse avec l'interrogatoire et les bilans ne se fait PAS ici**, mais dans la phase **Synthèse Globale**, où il est plus pertinent — c'est là que toutes les sources sont réunies.

La carte 4 s'arrête donc à sa mission : présenter et qualifier les deux moitiés de la cartographie. Elle produit un matériau pondéré, prêt à être croisé ; le croisement est un autre geste, à un autre moment du parcours.

Ce qui reste à faire en aval : **la Synthèse Globale devra lire cette synthèse et ses deux poids.** Sans ça, la cartographie restera visible au dossier sans jamais atteindre le raisonnement.

### 4.3.35 Le modèle qui rédige la synthèse

L'étape **`cartographie_synthese`** est entrée au catalogue `EtapesConsultation`, dans une phase propre — **Cartographie de l'enfant** — placée entre le 1er entretien et le suivi, comme dans le parcours réel. Elle apparaît donc dans « Affectation par étape » du moteur local, avec son sélecteur de modèle, et la génération bascule dessus via `PreparerModeleAsync` comme les autres étapes de raisonnement.

Non affectée, elle hérite simplement du modèle courant — aucune bascule, aucun redémarrage de serveur.

**Pas d'entrée pour la lecture des cases de la feuille**, volontairement : c'est une tâche de **vision**, servie par `LlamaCppProfiles.VisionCapable` et choisie dans la fenêtre de dépouillement elle-même. La mêler aux modèles de texte laisserait croire qu'un modèle sans projecteur peut lire une image.

### 4.3.4 Où vit la synthèse dans le dossier

Elle apparaît dans l'onglet **SYNTHESE**, **juste après la Synthèse Initiale** — à sa place chronologique : la cartographie suit la 1ère consultation et précède le bilan final de l'évaluation.

Le bloc porte **le texte et ce qui le qualifie** — l'informateur et les deux fiabilités — mais **pas les scores ni les axes** : ils vivent dans BILANS, et les répéter ferait deux endroits à tenir à jour. Une synthèse lue sans savoir ce qu'elle vaut est une synthèse mal lue ; une synthèse doublée de données déjà affichées ailleurs est une source de divergence.

Seules les cartographies **qui ont un texte** y figurent : une cartographie sans synthèse n'a rien à dire à cet endroit.

### 4.3.5 Clôture de la séance

Un bouton **🔒 Clôturer la séance**, sous les quatre cartes, fige la cartographie en **lecture seule**. Irréversible, avec confirmation.

Ce n'est pas un geste de publication — la carte est au dossier dès la première sauvegarde. C'est un geste de **fermeture** : ce qui a été observé ce jour-là cesse de bouger. Une cartographie indéfiniment modifiable ne serait plus le témoin d'une séance.

La garde est posée **dans l'écriture** (`EcrireCartoV2` refuse toute écriture sur une fiche close), et non sur chaque bouton : un chemin oublié — le crayon du dossier bleu, par exemple — passerait sinon à travers. Les pastilles des profils cessent aussi de répondre, par leur commande et non par leur apparence : un axe grisé mais cliquable laisserait croire à une saisie enregistrée.

Un bandeau `🔒 Séance clôturée — lecture seule` s'affiche en tête du bloc.

### 4.4 Reste à faire, par ordre d'utilité

1. **Brancher la cartographie sur l'étape d'interprétation** (voir ci-dessus).
2. **L'état « confiée à l'autre parent »** — prévu au §3.2, pas encore posable depuis la carte 3.
3. Les **illustrations contextualisées** par Med dans les énoncés de la feuille (§2.4).
4. La **lecture pixel par pixel** en repli, si le modèle vision se montre hésitant sur d'autres feuilles. La carte de coordonnées émet déjà la position exacte des 60 cases : tout est en place.

Le bouton d'impression reste accessible tant que l'âge est dans 3-11 ; hors fourchette il est désactivé, avec le message qui dit l'âge trouvé.

---

## 5. Axe 1 — ATTACHEMENT ✅ validé

### 5.1 Ce qui est retiré de la V1

Les items 5 et 6 de la V1 (« il a au moins un copain à l'école », « il demande à inviter ses copains ») mesurent la **sociabilité entre pairs**, pas l'attachement. Deux construits additionnés dans un même score, et cause du plancher à 3 ans. Retirés — les pairs trouveront leur place ailleurs.

### 5.2 Les 6 dimensions stables

L'item n°*i* mesure la **même dimension** dans les 4 tranches. Ça discipline l'écriture, garde une géométrie constante sur la feuille, et dit qualitativement *ce qui* accroche quand un enfant perd un point.

| # | Dimension | Ce qu'elle capte |
|---|---|---|
| 1 | **Séparation** | supporte d'être sans le parent |
| 2 | **Recours** | vient vers l'adulte quand ça va mal (havre de sécurité) |
| 3 | **Consolabilité** | se laisse apaiser |
| 4 | **Reprise du lien** | renoue après l'absence ou la tension |
| 5 | **Confiance en la disponibilité** | une parole suffit à le rassurer |
| 6 | **Prudence avec l'inconnu** | garde la bonne distance |

### 5.3 Les 4 versions

Cotation binaire, « oui » = 1 point. Formulation parent, toutes positives (oui = favorable).

#### 3-4 ans
1. Il accepte de rester sans moi dans un lieu qu'il connaît (crèche, école, chez ses grands-parents).
2. Quand il a mal ou qu'il a peur, il vient me chercher.
3. Quand il est bouleversé, il se calme dans mes bras ou avec ma voix.
4. Quand je reviens le chercher, il vient vers moi, content.
5. Il s'éloigne pour jouer et revient vers moi de temps en temps.
6. Il garde une réserve avec les adultes qu'il ne connaît pas.

#### 5-6 ans
1. Il passe la journée à l'école sans que la séparation soit un problème.
2. Quand quelque chose l'a blessé ou inquiété dans sa journée, il finit par m'en parler.
3. Quand il est en colère ou triste, il accepte que je l'aide à se calmer.
4. Quand on se retrouve le soir, il vient vers moi et me raconte.
5. Il se rassure quand je lui dis à l'avance ce qui va se passer (qui vient le chercher, à quelle heure).
6. Il ne part pas facilement avec un adulte qu'il ne connaît pas.

#### 7-9 ans
1. Il peut passer une journée entière chez quelqu'un d'autre sans avoir besoin de m'appeler.
2. Quand il a un problème trop gros pour lui, il vient me le dire plutôt que de le garder.
3. Quand il est débordé, il accepte encore d'être réconforté par un adulte.
4. Après une absence ou une journée difficile, il revient vers moi de lui-même.
5. Une parole de ma part suffit à le rassurer quand il s'inquiète.
6. Il garde la bonne distance avec les adultes qu'il connaît peu.

#### 10-11 ans
1. Il peut partir plusieurs jours (colonie, classe verte, chez un ami) sans que ça tourne mal.
2. Quand quelque chose de grave ou d'embarrassant lui arrive, il finit par m'en parler, ou à un adulte de confiance.
3. Quand il va mal, il accepte encore un geste ou une parole de réconfort, même s'il fait le grand.
4. Après une dispute entre nous, c'est réparable — on se reparle.
5. Il me fait confiance quand je lui dis que je serai là.
6. Il garde une réserve appropriée avec les adultes qu'il connaît peu, y compris en ligne.

### 5.4 Ce que la structure démontre

Dimension 4 : à 3 ans, « il vient vers moi content quand je reviens » ; à 11 ans, « après une dispute, on se reparle ». Même dimension, expression développementale différente. C'est ce qu'un item unique ne pouvait pas faire — et c'est pour ça que la grille devait se tordre. Elle ne se tord plus.

---

## 6. Axe 2 — LANGAGE & COMMUNICATION ✅ validé

### 6.1 Ce qui est retiré de la V1

Deux items sur six ne mesuraient pas le langage :

- « Il exprime ses besoins sans forcément crier ou se fâcher » → régulation émotionnelle.
- « Il utilise des mots pour décrire ses émotions ou ses pensées » → **doublon exact avec Émotions n°2** (« Il peut mettre des mots sur ce qu'il ressent »). Un enfant qui ne verbalise pas ses affects perdait un point **deux fois, dans deux sphères**. Signal doublé artificiellement.

→ La verbalisation des émotions est rendue **exclusivement** à la sphère Émotions.

Manquait par ailleurs la question la plus utile à 3-4 ans : **est-ce que les gens qui ne le connaissent pas le comprennent ?** — premier signal d'un trouble d'articulation. Ajoutée en dimension 2.

### 6.2 Les 6 dimensions stables

| # | Dimension | Ce qu'elle capte |
|---|---|---|
| 1 | **Compréhension** | ce qui rentre |
| 2 | **Se faire comprendre** | ce qui sort, et sa clarté pour autrui |
| 3 | **Récit** | organiser un propos dans la durée |
| 4 | **Conversation** | tenir l'échange, s'ajuster à l'autre |
| 5 | **Réparation** | signaler qu'on n'a pas compris |
| 6 | **Le langage comme outil** | obtenir, expliquer, négocier par les mots |

La dimension 6 est bornée volontairement au **langage qui remplace l'acte** (demander plutôt que prendre, argumenter plutôt que subir), et non au langage qui nomme l'affect — qui appartient aux Émotions.

### 6.3 Les 4 versions

#### 3-4 ans
1. Il comprend une consigne simple sans qu'on ait besoin de lui montrer.
2. Les gens qui ne le connaissent pas comprennent ce qu'il dit.
3. Il raconte un petit bout de ce qu'il a fait (un moment, un événement).
4. Il répond quand on lui parle et reste un moment dans l'échange.
5. Quand il n'a pas compris, ça se voit — il redemande ou il montre.
6. Il demande avec des mots ce qu'il veut, plutôt que de le prendre ou de pleurer.

#### 5-6 ans
1. Il comprend une consigne en deux temps (« range tes chaussures et va te laver les mains »).
2. Il parle en phrases complètes, sans que j'aie besoin de traduire pour les autres.
3. Il raconte sa journée dans l'ordre, et on comprend.
4. Il attend son tour pour parler, sans couper tout le temps.
5. Il dit quand il n'a pas compris, plutôt que de faire semblant.
6. Il explique ce qu'il veut, ou pourquoi il n'est pas d'accord.

#### 7-9 ans
1. Il comprend une explication un peu longue sans qu'on ait à la redécouper.
2. Il trouve ses mots sans tourner longtemps autour.
3. Il raconte une histoire ou un film avec assez de détails pour qu'on suive.
4. Il tient une conversation : il relance, il pose des questions à l'autre.
5. Il pose une question précise sur ce qu'il n'a pas compris.
6. Il défend son point de vue avec des arguments.

#### 10-11 ans
1. Il comprend ce qui n'est pas dit directement — l'allusion, l'ironie, le second degré.
2. Il arrive à expliquer clairement quelque chose de compliqué.
3. Il raconte un événement en tenant compte de ce que je sais déjà et de ce que j'ignore.
4. Il adapte sa façon de parler selon la personne (un copain, un adulte, un professeur).
5. Il reformule ou fait préciser, plutôt que de partir sur un malentendu.
6. Il négocie, il argumente, et il peut changer d'avis en discutant.

### 6.4 Ce que la structure démontre

Dimension 1 : de « comprend une consigne simple » à « comprend le second degré ». À 11 ans, les difficultés pragmatiques se voient là, pas dans la compréhension littérale que tout le monde réussit. Dimension 3 : finit sur la prise en compte de ce que sait l'interlocuteur — marqueur réel à cet âge, invisible avec un item unique.

---

## 7. Axe 3 — ÉMOTIONS ✅ validé

### 7.1 Ce qui est retiré de la V1

- **« Quand il est débordé, il accepte d'être réconforté ou calmé »** → c'est la **consolabilité**, qui appartient à l'Attachement (dimension 3). Deuxième doublon inter-sphères. Retiré d'ici.
- **« Il retrouve son calme en quelques minutes »** + **« Il vit ses émotions intensément mais revient à l'équilibre sans blocage »** → deux formulations de la **même** dimension. Le retour au calme comptait double dans un score sur 6. Fusionnés.
- Manquait la dimension la plus discriminante en clinique : **la proportion**. Ce qui signe une dysrégulation n'est pas de ressentir fort, mais que la réaction soit sans commune mesure avec l'événement. Ajoutée en dimension 3.
- Récupère en échange la **verbalisation des affects**, rendue par le Langage.

**Ligne de partage avec l'Attachement :** accepter le réconfort de l'autre = Attachement ; avoir ses propres moyens de s'apaiser = Émotions. Un enfant peut avoir l'un sans l'autre.

### 7.2 Les 6 dimensions stables

| # | Dimension | Ce qu'elle capte |
|---|---|---|
| 1 | **Expressivité** | ce qu'il ressent est lisible |
| 2 | **Nommer** | mettre des mots dessus |
| 3 | **Proportion** | la réaction est à la mesure de l'événement |
| 4 | **Retour au calme** | ça redescend, et en combien de temps |
| 5 | **Moyens propres** | il a ses façons à lui de s'apaiser |
| 6 | **Émotions d'autrui** | il les perçoit et en tient compte |

### 7.3 Les 4 versions

#### 3-4 ans
1. On voit tout de suite quand il est content, triste ou fâché.
2. Il dit des mots simples pour ce qu'il ressent (« content », « peur », « pas content »).
3. Ses colères et ses chagrins restent à la mesure de ce qui vient de se passer.
4. Après une grosse colère ou un gros chagrin, il redescend en quelques minutes.
5. Il a des moyens à lui pour se rassurer (un doudou, un coin, un geste).
6. Il remarque quand quelqu'un pleure ou est triste.

#### 5-6 ans
1. Je sais lire sur son visage ce qu'il ressent, même s'il ne le dit pas.
2. Il peut dire ce qu'il ressent avec ses mots (triste, en colère, jaloux).
3. Il ne s'effondre pas pour une contrariété ordinaire.
4. Quand c'est passé, c'est passé — il repart sur autre chose.
5. Quand ça monte, il sait s'isoler ou faire quelque chose qui le calme.
6. Il reconnaît quand un autre enfant est triste ou en colère.

#### 7-9 ans
1. Quand quelque chose l'a touché, ça finit par se voir — il ne masque pas tout.
2. Il distingue des émotions proches : déçu, vexé, en colère, ce n'est pas pareil pour lui.
3. Sa réaction est proportionnée — il ne part pas très haut pour peu de chose.
4. Il retrouve son calme sans que la journée entière en soit gâchée.
5. Il a des façons à lui de se calmer, sans qu'un adulte ait à intervenir.
6. Il tient compte de ce que ressent l'autre : il s'arrête, il console, il s'excuse.

#### 10-11 ans
1. Même quand il cache, je finis par savoir que ça ne va pas — il ne reste pas hermétique.
2. Il peut expliquer *pourquoi* il se sent comme ça, pas seulement ce qu'il ressent.
3. Il encaisse une déception ou une injustice sans que ça prenne des proportions.
4. Après une contrariété, il revient à lui-même dans la journée — il ne rumine pas des jours.
5. Il sait ce qui lui fait du bien quand ça ne va pas, et il y a recours.
6. Il perçoit quand quelqu'un va mal, même sans que ce soit dit.

### 7.4 Ce que la structure démontre

Dimension 1 à 10-11 ans : **cacher ses émotions à ses parents est normal à cet âge.** L'item ne peut donc pas être « il montre ce qu'il ressent » — un préado sain répondrait non. Il porte sur le fait qu'un canal reste ouvert malgré la pudeur. Une formulation unique pour 3-11 ans ne peut pas produire ça.

Dimension 4 : de « quelques minutes » à « il ne rumine pas des jours ». La fenêtre temporelle de la récupération émotionnelle change d'échelle entre 3 et 11 ans.

---

## 8. Axe 4 — IMAGINAIRE & MONDE INTÉRIEUR ✅ validé

### 8.1 Ce qui est retiré de la V1

- **Doublon interne** : « Il transforme les objets ou les lieux pour jouer *comme si* » et « Il a des personnages imaginaires ou fait *comme si* dans ses jeux » = deux fois le jeu symbolique. Fusionnés.
- **Doublon avec Pensée** : « questions existentielles (vie, mort, pourquoi…) » ici et « questions sur le pourquoi des choses » là-bas. **Arbitrage : Pensée garde la curiosité causale** (comment le monde fonctionne), **Imaginaire garde le questionnement existentiel** (pourquoi on existe, pourquoi on meurt).

### 8.2 Le problème propre à cet axe : un item dont la valence s'inverse

Le jeu de faire-semblant culmine vers 4-6 ans et **décline normalement** ensuite. « Il a des personnages imaginaires » coché OUI chez un enfant de 11 ans n'est pas une bonne nouvelle — c'est possiblement un signal. L'item V1 ne perd pas seulement du pouvoir discriminant avec l'âge : **sa valeur clinique s'inverse.** C'est le seul axe où l'item unique 3-11 ans ne se contente pas d'être insensible, il ment.

Les dimensions sont donc définies au niveau qui survit au déclin du faire-semblant : ce qui persiste est la **symbolisation**, qui change de véhicule (jeu → récit → fiction et création).

### 8.3 Les 6 dimensions stables

| # | Dimension | Ce qu'elle capte |
|---|---|---|
| 1 | **Vie intérieure** | il a un dedans à lui |
| 2 | **Symboliser & créer** | transformer, inventer — le véhicule change avec l'âge |
| 3 | **Accès** | il en donne un peu à voir |
| 4 | **Élaboration** | il y digère ce qu'il vit |
| 5 | **Frontière** | l'imaginaire reste à sa place |
| 6 | **Questionnement existentiel** | il se pose les grandes questions |

La dimension 5 est **nouvelle** : c'est elle qui distingue l'imaginaire *ressource* de l'imaginaire qui *envahit*, et elle est intrinsèquement liée à l'âge — une frontière poreuse est normale à 3 ans, pas à 11.

La dimension 4 est bornée pour ne pas empiéter sur Émotions n°5 : là-bas **s'apaiser**, ici **élaborer** (rejouer, mettre en récit, digérer).

### 8.4 Les 4 versions

#### 3-4 ans
1. Il joue seul en se racontant des choses à voix haute.
2. Il transforme les objets pour jouer : un bâton devient une épée, un carton une maison.
3. Il me montre ou me raconte un bout de ce qu'il est en train de jouer.
4. Il rejoue dans ses jeux des choses qu'il a vécues (le docteur, l'école, une dispute).
5. Quand un jeu ou une histoire lui fait peur, il se rassure si on lui dit que ce n'est pas pour de vrai.
6. Il pose des questions sur les grandes choses (d'où viennent les bébés, où sont les gens qui ne sont plus là).

#### 5-6 ans
1. Il s'invente des histoires dans sa tête, il rêvasse.
2. Il invente des scénarios de jeu élaborés, avec des rôles et des règles.
3. Il me raconte ce qu'il imagine, ou ce dont il a rêvé.
4. Quand quelque chose l'a marqué, ça se retrouve dans ses jeux ou ses dessins.
5. Il sait faire la différence entre ce qu'il invente et ce qui est arrivé pour de vrai.
6. Il pose des questions sur la mort, la naissance, le temps.

#### 7-9 ans
1. Il a un monde à lui — des histoires, des univers, des choses qu'il se raconte.
2. Il invente des histoires, des mondes ou des personnages, ou il les dessine.
3. Il me raconte ses idées, ses histoires, ce qu'il invente.
4. Son imaginaire lui fait du bien : il s'y ressource sans s'y perdre.
5. Il fait clairement la part entre l'imaginaire et le réel.
6. Il s'interroge sur des choses qui le dépassent — la mort, l'infini, l'injustice.

#### 10-11 ans
1. Il a une vie intérieure à lui, où il se retire parfois (pensées, projets, rêveries).
2. Il crée ou s'investit dans des univers de fiction : écrire, dessiner, jouer, construire, lire.
3. Il me laisse entrer un peu dans ce qui l'occupe ou ce qu'il pense.
4. Ses histoires, ses lectures ou ses jeux l'aident à digérer ce qu'il traverse.
5. Son imaginaire reste à sa place — il ne déborde pas sur sa vie de tous les jours.
6. Il se pose des questions sur le sens, sur lui-même, sur ce qu'il deviendra.

### 8.5 Arbitrages validés

- **Dimension 3 à 10-11 ans : « il me laisse entrer *un peu* ».** Le « un peu » est volontaire et conservé — un préado qui donne un accès complet à son monde intérieur n'est pas plus sain qu'un autre.
- **Dimension 2 à 10-11 ans : les jeux vidéo comptent** comme univers de fiction investi, malgré leur statut de facteur systémique à surveiller par ailleurs.

---

## 9. Axe 5 — PENSÉE & ORGANISATION COGNITIVE ✅ validé

### 9.1 Le problème de fond : 4 items sur 6 appartenaient ailleurs

| Item V1 | Où il appartient réellement |
|---|---|
| « Il comprend les consignes sans qu'il faille toujours les répéter » | **Langage** D1 — mot pour mot le même item |
| « Il arrive à expliquer ce qu'il pense, même simplement » | **Langage** D2/D6 |
| « Il peut se concentrer quelques minutes sans se disperser » | **Profil Attention** — attention soutenue |
| « Il s'adapte quand on change d'avis ou d'activité » | **Profil Attention** (flexibilité) *et* **Tempérament** (adaptabilité) |

Seuls deux items étaient propres. **Cette sphère ne mesurait pas la pensée : elle mesurait du langage, de l'attention et du tempérament.** Un enfant TDAH y perdait des points déjà comptés dans le profil Attention ; un enfant dysphasique des points déjà comptés dans le Langage. C'est la démonstration la plus nette du défaut de frontières de la V1. Sphère reconstruite intégralement.

### 9.2 Décision : l'attention sort du questionnaire parents

L'attention est **la sphère du médecin**, remplie par son observation, et c'est celle qui débouche sur une demande de bilan attentionnel standardisé. Si le questionnaire parents la cote aussi, l'avis des parents pré-empte l'observation clinique sur la seule sphère où le médecin voulait garder la main. **Aucun item d'attention, de concentration, d'inhibition ou de flexibilité dans les 5 questionnaires parents.**

### 9.3 Les 6 dimensions stables

| # | Dimension | Ce qu'elle capte |
|---|---|---|
| 1 | **Curiosité causale** | il veut comprendre comment ça marche |
| 2 | **Apprentissage** | ce qu'il apprend tient |
| 3 | **Mémoire du vécu** | il se souvient de ce qu'il a vécu |
| 4 | **Raisonnement** | il fait des liens, il anticipe |
| 5 | **Résolution de problème** | il trouve une solution |
| 6 | **Repérage dans le temps** | hier, demain, la semaine, l'avenir |

La dimension 2 est celle qui manquait le plus : **est-ce que ce qu'on lui apprend reste acquis ?** Marqueur parental par excellence d'un trouble des apprentissages ou d'une déficience — la V1 ne l'interrogeait nulle part.

La dimension 4 est formulée en « faire des liens et prévoir », **jamais** en « réfléchir avant d'agir » — sinon on remesure l'inhibition, donc l'Attention.

### 9.4 Les 4 versions

#### 3-4 ans
1. Il demande comment ça marche, pourquoi ça fait ça.
2. Ce qu'on lui montre, il l'attrape et il arrive à le refaire.
3. Quand on lui reparle de quelque chose qu'il a vécu, il s'en souvient.
4. Il comprend que si on fait ceci, il arrive cela (si je lâche, ça tombe).
5. Devant un petit obstacle, il essaie quelque chose de lui-même.
6. Il comprend « après », « tout à l'heure », « demain ».

#### 5-6 ans
1. Il veut savoir comment les choses fonctionnent, et la réponse l'intéresse vraiment.
2. Ce qu'il apprend à l'école ou à la maison finit par tenir.
3. Il se souvient d'événements d'il y a plusieurs semaines.
4. Il comprend les conséquences simples de ce qu'il fait.
5. Il trouve des solutions à de petits problèmes du quotidien.
6. Il se repère dans la journée et dans la semaine (l'école, le week-end, les jours).

#### 7-9 ans
1. Il creuse ce qui l'intéresse : il pose des questions, il cherche à savoir.
2. Une notion apprise reste acquise — on ne repart pas de zéro à chaque fois.
3. Il se souvient de choses qui se sont passées il y a des mois, avec des détails justes.
4. Il fait des liens : il voit ce qui va arriver s'il continue comme ça.
5. Devant un problème concret, il trouve comment s'en sortir.
6. Il se repère dans le mois et l'année, il situe les événements dans le temps.

#### 10-11 ans
1. Quand quelque chose l'intrigue, il va chercher la réponse lui-même.
2. Il apprend de nouvelles choses sans que ça lui demande un effort disproportionné.
3. Il a des souvenirs construits de son enfance, il peut y revenir.
4. Il raisonne et il pèse — il voit les conséquences un peu à distance.
5. Face à une situation nouvelle, il trouve une solution qui tient la route.
6. Il se projette : la semaine prochaine, les vacances, l'année d'après.

---

## 10. Les 3 profils observés — règles communes

Remplis par le **médecin en observant l'enfant pendant la séance**, pendant que le parent remplit sa feuille en salle d'attente. Six axes cotés 1-5 par profil, `0 = non renseigné` (état de départ : on ne note que ce qu'on a vu).

### 10.1 Un seul écran, trois blocs

Les 5 questionnaires parents sont trois actes séparés ; **les 3 profils sont un seul acte d'observation**, tiré des mêmes trente minutes. Ils s'affichent donc ensemble, sur un écran unique. Les séparer obligerait à naviguer d'avant en arrière au fil de ce qu'on remarque.

### 10.2 Deux natures assumées, rendues visibles par la couleur

| Profil | Nature | Couleur |
|---|---|---|
| **Tempérament** | *portrait* — axes bipolaires, aucun pôle n'est mauvais (« il n'est pas trop ou pas assez, il est lui ») | **aucune** |
| **Psychomotricité** | *compétence* — 5 toujours favorable | rouge / vert |
| **Attention** | *compétence* — 5 toujours favorable | rouge / vert |

Règle : **rouge = défavorable, vert = favorable, pas de couleur = neutre.** Seuils : 1-2 rouge, 3 sans couleur, 4-5 vert.

La conséquence heureuse : le Tempérament est *par nature* le bloc sans couleur. **La distinction portrait / compétence devient visible sans un mot d'explication.**

### 10.3 Corollaire obligatoire : 5 est toujours le bon côté dans les blocs colorés

La V1 mélangeait les polarités *à l'intérieur* d'un même profil — « Motricité fine » à 5 est bon, « Impulsivité motrice » à 5 est mauvais. Avec la règle de couleur, on verrait du vert à 5 sur une ligne et du rouge à 5 sur la suivante, au moment précis où le médecin a trois secondes pour regarder, l'enfant en face.

Donc **tout axe inversé est reformulé** : « Impulsivité motrice » → « Contrôle moteur ». La règle devient universelle dans ces deux blocs (plus c'est haut, mieux c'est) et la silhouette du radar redevient lisible : plus grande = mieux.

### 10.4 Saisie pendant la séance

Le médecin remplit **pendant** la consultation. L'interface n'a donc pas le droit d'être autre chose que : un écran, aucun défilement, aucune boîte de dialogue, **un clic par axe**. Cinq pastilles cliquables par ligne, couleur immédiate, rien de coché par défaut.

### 10.5 Les recoupements de la V1 entre profils

Cinq axes sur dix-huit faisaient double emploi. Aucun score n'était gonflé — il n'y en a pas — mais **une seule observation se retrouvait notée trois ou quatre fois sous des noms différents, et se relisait ensuite comme une convergence.** Un enfant agité produisait un signal sur quatre axes répartis dans trois profils : ça ressemble à une corroboration, c'est une seule chose vue une fois. C'est précisément ce qui alimente le passage axe → hypothèse → orientation.

| Doublon | Profils concernés | Arbitrage |
|---|---|---|
| Adaptabilité ≡ Flexibilité attentionnelle | Tempérament / Attention | → Tempérament |
| Niveau d'activité vs Impulsivité motrice vs Inhibition | les trois | → Tempérament garde le niveau d'activité (neutre) ; le contrôle revient une seule fois entre Psychomotricité et Attention |
| Temps de réaction | Tempérament | Supprimé — retouchait l'impulsivité une 4ᵉ fois |
| Motricité fine ≡ Dextérité | doublon **interne** à la Psychomotricité | Fusionnés |
| Motricité globale vs Coordination | Psychomotricité | Fusionnés |

---

## 11. Profil A — TEMPÉRAMENT ✅ validé

*Portrait. Axes bipolaires, sans couleur : aucun pôle n'est meilleur que l'autre.*

### 11.1 Deux axes retirés

- **« Temps de réaction »** — retouchait le niveau d'activité et l'impulsivité, qui appartiennent aux deux autres blocs.
- **« Rythme / Régularité »** — coupe la plus décisive : le rythme (sommeil, appétit, régularité) **ne s'observe pas dans la pièce en trente minutes**. C'est un savoir de parent, pas une observation clinique, et c'est la définition même de ces profils. Il relève de l'anamnèse.

Par ailleurs, « Rythme / Régularité » (1 = très irrégulier → 5 = très stable) et « Adaptabilité » (1 = change difficilement → 5 = s'adapte facilement) étaient formulés **avec un bon côté**, ce qui contredit la nature de portrait. Adaptabilité est reformulée en deux façons d'être : un enfant qui s'ajuste instantanément à tout n'est pas forcément celui qui va le mieux — il ne signale rien.

### 11.2 Deux axes ajoutés

- **« Approche / retrait »** — la chose la plus observable des trois premières minutes d'une consultation avec un enfant, et elle manquait.
- **« Humeur de fond »**.

### 11.3 Les 6 axes

| # | Axe | 1 | 5 |
|---|---|---|---|
| 1 | **Niveau d'activité** | Posé, économe de ses mouvements | En mouvement permanent |
| 2 | **Approche / retrait** | Observe longtemps avant d'entrer | Va vers la nouveauté d'emblée |
| 3 | **Réactivité sensorielle** | Peu réactif aux bruits, textures, lumières | Très sensible aux stimulations |
| 4 | **Intensité émotionnelle** | Émotions discrètes, peu visibles | Émotions fortes et démonstratives |
| 5 | **Adaptabilité** | A besoin de temps pour accepter le changement | S'ajuste immédiatement |
| 6 | **Humeur de fond** | Sérieux, grave, peu souriant | Enjoué, souriant d'emblée |

### 11.4 Pourquoi 2 et 5 sont deux axes distincts

L'approche est la **première** réaction ; l'adaptabilité est l'ajustement **dans la durée**. Un enfant qui se retire d'abord puis s'adapte très bien est un profil fréquent et parlant — un axe unique l'écrase.

---

## 12. Profil B — PSYCHOMOTRICITÉ ✅ validé

*Compétence. Axes unipolaires, 5 toujours favorable, colorés.*

### 12.1 Ce qui est corrigé

- **« Motricité fine » ≡ « Dextérité »** — la dextérité *est* la motricité fine. Doublon **interne** au profil. Fusionnés.
- **« Motricité globale » vs « Coordination »** — largement superposés chez un enfant observé trente minutes dans un bureau. Fusionnés.
- **« Impulsivité motrice » était inversé** (5 = pire, alors que ses cinq voisins ont 5 = mieux). C'est l'axe qui a motivé la règle de couleur. Devient **« Contrôle moteur »**.
- **« Tonus » était un axe bipolaire déguisé** : hypotonie et hypertonie sont toutes deux anormales, le milieu est le bon. Coté 1-5 avec 5 = vert, un enfant raide comme un piquet serait ressorti en vert. Devient **« Régulation du tonus »** (5 = tonus ajusté). Même piège que « Régularité » côté tempérament : un axe qui se croit descriptif alors qu'il a un optimum.

### 12.2 Les 6 axes

| # | Axe | 1 | 5 |
|---|---|---|---|
| 1 | **Aisance motrice globale** | Malhabile, se cogne, trébuche | Se déplace et se pose avec aisance |
| 2 | **Précision des gestes fins** | Gestes imprécis, tenue du crayon difficile | Gestes précis et ajustés |
| 3 | **Régulation du tonus** | Tonus mal ajusté : avachi ou raide | Tonus ajusté à la situation |
| 4 | **Contrôle moteur** | Ne tient pas en place, touche tout, ne freine pas | Peut rester posé, peut retenir un geste |
| 5 | **Repérage corporel et spatial** | Se repère mal dans l'espace et par rapport à son corps | Se repère bien |
| 6 | **Praxies / imitation gestuelle** | Ne reproduit pas un geste, perd la séquence | Reproduit un geste et enchaîne une séquence |

### 12.3 Arbitrage validé sur l'axe 6

Le choix était entre **« Investissement du corps »** (le corps habité avec plaisir) et **« Praxies / imitation gestuelle »**. Praxies retenu : plus étroit, **aucun recouvrement** avec Tempérament n°1 « Niveau d'activité », et pointe directement vers une demande de bilan psychomoteur. L'investissement du corps frôlait le tempérament — exactement le genre de frontière molle qui a produit les doublons de la V1.

### 12.4 Frontière validée avec l'Attention

**Psychomotricité = contrôle du corps** (tenir en place, freiner un geste).
**Attention = inhibition cognitive** (attendre son tour de parole, ne pas répondre avant la fin, résister à ce qui distrait).

Ce sont deux observations réellement différentes dans une pièce — confirmé par l'auteur. L'axe 4 de chaque profil ne mesure donc pas la même chose, et aucun des deux ne doit contenir de contenu de l'autre.

---

## 13. Profil C — ATTENTION & FONCTIONS EXÉCUTIVES ✅ validé

*Compétence. Axes unipolaires, 5 toujours favorable, colorés.*

C'est **le seul profil auquel une décision est attachée** : il déclenche la demande de bilan attentionnel standardisé. Ses six axes sont donc les six choses qui, vues dans une pièce, font penser « il faut un bilan ».

### 13.1 Deux axes retirés

- **« Flexibilité attentionnelle »** → revient au Tempérament (adaptabilité). La raison dépasse le partage de territoire : dans un bureau, **l'observable est le même événement** — on change d'activité, on regarde si l'enfant suit. Pas de tri de cartes, pas de changement de règle testé. Une seule observation, notée une seule fois.
- **« Attention divisée »** → construit de laboratoire, jamais testé en consultation. Un axe qu'on ne remplit pas honnêtement est pire qu'un axe absent : il finit rempli au jugé.

### 13.2 Deux axes ajoutés

- **Mémoire de travail** — tenir la consigne pendant qu'on l'exécute. Central en fonctions exécutives, immédiatement visible dans la pièce, absent de la V1. À ne pas confondre avec la « Mémoire du vécu » de la sphère Pensée, qui est **épisodique et remplie par le parent** — celle-ci avait été formulée exprès pour laisser la place libre ici.
- **Maintien de l'effort** — persévérer quand ça résiste. Le classique « attention span / persistence », réservé pour ce profil au moment d'écrire le tempérament.

### 13.3 Les 6 axes

| # | Axe | 1 | 5 |
|---|---|---|---|
| 1 | **Attention soutenue** | Décroche au bout de quelques secondes | Reste plusieurs minutes sur une activité |
| 2 | **Résistance à la distraction** | Le moindre bruit ou objet le détourne | Reste sur ce qu'il fait malgré ce qui se passe autour |
| 3 | **Mémoire de travail** | Perd la consigne en cours de route | Garde la consigne en tête pendant qu'il fait |
| 4 | **Inhibition** | Répond avant la fin, coupe, ne peut pas attendre | Attend son tour, laisse finir la question |
| 5 | **Planification** | Se lance sans savoir par où | S'y prend dans un ordre, sait par où commencer |
| 6 | **Maintien de l'effort** | Abandonne dès que ça résiste | Persévère quand c'est difficile |

L'axe 4 est **purement verbal et cognitif** — aucun contenu moteur, qui appartient à l'axe 4 de la Psychomotricité. La frontière validée en §12.4 est appliquée dans la formulation elle-même.

### 13.4 Réserve signalée

L'axe 6 touche autant à la motivation qu'à l'attention : un enfant qui abandonne peut être déficitaire, découragé ou déprimé. Il reste à sa place — c'est le classique de la clinique attentionnelle — mais c'est le seul des six qui ne se lit pas tout seul.

---

## 14. État des axes et des profils

| Axe | Statut |
|---|---|
| Attachement | ✅ validé (24 items) |
| Langage & communication | ✅ validé (24 items) |
| Émotions | ✅ validé (24 items) |
| Imaginaire & monde intérieur | ✅ validé (24 items) |
| Pensée & organisation cognitive | ✅ validé (24 items) |

4 tranches × 5 axes × 6 items = **120 items**, écrits et validés un axe à la fois.

| Profil observé (médecin) | Nature | Statut |
|---|---|---|
| Tempérament | portrait, sans couleur | ✅ validé (6 axes) |
| Psychomotricité | compétence, colorée | ✅ validé (6 axes) |
| Attention & fonctions exécutives | compétence, colorée | ✅ validé (6 axes) |

**Le contenu clinique de la Cartographie V2 est complet : 120 items parents + 18 axes observés.**

### 10.1 Audit des doublons de la V1 — récapitulatif

Le travail axe par axe a mis au jour que **les sphères de la V1 n'avaient pas de frontières**. Chaque doublon faisait perdre deux points pour une seule difficulté, gonflant artificiellement le signal :

| Doublon | Sphères concernées | Arbitrage |
|---|---|---|
| Sociabilité entre pairs | dans Attachement, sans y appartenir | Retiré de l'Attachement |
| Verbalisation des affects | Langage **et** Émotions | → Émotions seul |
| Acceptation du réconfort | Attachement **et** Émotions | → Attachement seul (consolabilité) ; Émotions garde les moyens propres |
| Retour au calme | deux fois **dans** Émotions | Fusionné |
| Jeu symbolique | deux fois **dans** Imaginaire | Fusionné |
| Questions « pourquoi » | Imaginaire **et** Pensée | Existentiel → Imaginaire ; causal → Pensée |
| Compréhension des consignes | Langage **et** Pensée | → Langage seul |
| Expliquer sa pensée | Langage **et** Pensée | → Langage seul |
| Concentration | Pensée **et** profil Attention | → profil Attention seul (médecin) |
| Adaptation au changement | Pensée, profil Attention **et** Tempérament | → profils seuls (médecin) |

**Règle héritée de cet audit :** aucune dimension ne peut appartenir à deux sphères. Toute évolution future des items doit repasser par ce contrôle.

---

## 15. Points techniques à régler avant d'implémenter

À faire **avant** d'écrire les items dans le code, sinon on casse les évaluations existantes.

1. **Persistance positionnelle sans version.** [EvaluationPhaseService.cs:500](MedCompanion/Services/Evaluations/EvaluationPhaseService.cs#L500) écrit `items: [true, false, …]` et [la relecture](MedCompanion/Services/Evaluations/EvaluationPhaseService.cs#L1012) réapplique les booléens par index sur des items reconstruits. Aucune trace du texte ni de la version. Défaut déjà latent aujourd'hui ; bloquant avec des items par tranche. → **estampiller la tranche dans le YAML du segment** (`bande: 3-4`) et relire depuis l'estampille, jamais depuis l'âge courant.
2. **Ordre de construction.** [CartographieEnfant.cs:55](MedCompanion/Models/Evaluations/CartographieEnfant.cs#L55) construit ses segments dans le constructeur, avant que `AgeAuMomentDeLaSaisie` soit renseigné. → différer la construction des items, ou pouvoir les reconstruire une fois la tranche connue.
3. **Simplification du scoring.** `CartographieScoringService.Calculer(score)` au lieu de `Calculer(score, age)` ; l'âge ne sert plus qu'à `IsApplicable` (3-11). Nettoie une quinzaine d'appels dans `RestitutionSuggesterService`.
4. **Affichage du score dans la restitution.** [RestitutionHtmlPreviewService.cs:3610](MedCompanion/Services/Restitutions/RestitutionHtmlPreviewService.cs#L3610) rend aux parents une roue colorée avec « 4/6 » au centre et une légende « Très fragilisé / Fragilisé / … ». À revoir : la couleur parle bien, le chiffre et le mot de gravité transforment une hypothèse clinique en verdict chiffré.

---

## 16. Décisions actées

| Question | Décision |
|---|---|
| Grille de couleur | ✅ Unique, indépendante de l'âge |
| Adaptation à l'âge | ✅ Portée par les items, 4 tranches |
| Génération des items par le LLM | ❌ Refusé — le seuil décide d'une orientation |
| Contextualisation par le LLM | ✅ Sur l'illustration, jamais sur l'énoncé coté |
| Feuille remplie à la maison | ❌ Refusé — salle d'attente uniquement |
| Affichage d'un résultat pendant la séance | ❌ Aucun — accusé de lecture seulement |
| Résumé oral du comportement | ✅ Reste au médecin, Med n'y touche pas |
| Attention dans le questionnaire parents | ❌ Sortie — sphère du médecin, elle seule déclenche le bilan attentionnel |
| Frontières entre sphères | ✅ Aucune dimension ne peut appartenir à deux sphères |
| Les 120 items | ✅ Écrits et validés axe par axe (§5 à §9) |
| Les 3 profils observés | ✅ 18 axes validés profil par profil (§11 à §13) |
| Nature des profils | ✅ Deux natures assumées : Tempérament = portrait sans couleur, Psychomotricité et Attention = compétences colorées |
| Polarité dans les blocs colorés | ✅ 5 est toujours le bon côté — tout axe inversé est reformulé |
| Code couleur | ✅ Rouge 1-2, neutre 3, vert 4-5 ; portrait jamais coloré ; non renseigné sans couleur |
| Saisie des profils | ✅ Pendant la séance — un écran, un clic par axe, rien de coté au départ |
| Rythme / régularité dans les profils | ❌ Retiré — ne s'observe pas dans la pièce, relève de l'anamnèse |
| Sauvegarde automatique des profils | ❌ Refusée — un bouton explicite, la fiche existe quand le médecin le décide |
| Emplacement des cartographies V2 | ✅ Onglet BILANS du dossier bleu, qui héberge déjà des cartographies générées |
| Moment du versement au dossier | ✅ **Dès la sauvegarde** — renversé à l'usage : c'est l'état de complétude porté par la carte, et non un moment de publication, qui dit où en est la cartographie |
| Lecture automatique des cases | ✅ Éprouvée sur feuille manuscrite réelle : **29 réponses sur 30**, la trentième laissée vide par prudence |
| Étape de synthèse dans le bloc | ✅ **Retenue** (après avoir été écartée) — mais elle PRÉSENTE et QUALIFIE sans jamais conclure : c'est le pont vers l'étape qui raisonne, pas une seconde interprétation |
| Fiabilité des sources | ✅ **Deux curseurs**, un par moitié — pondérer la seule feuille parent traiterait implicitement l'observation du médecin comme certaine |
| Échelle de fiabilité | ✅ Quatre niveaux nommés → poids 0-1, la même échelle que les documents importés. « Non exploitable » = poids null, source écartée, jamais pesée à zéro |
| Effet de la fiabilité sur les scores | ❌ Aucun — elle qualifie la source, jamais la valeur. Un 4/6 reste un 4/6 |
| Croisement avec interrogatoire et bilans | ⏭ **Reporté à la phase Synthèse Globale**, où toutes les sources sont réunies — la carte 4 s'arrête à produire un matériau pondéré |
| Clôture de la séance | ✅ Bouton dédié, irréversible, lecture seule — garde posée dans l'écriture et non sur les boutons |
| « V2 » dans le titre de la carte | ❌ Refusé — la version vit dans le fichier, pas dans le libellé lu par un tiers |
| Fenêtre séparée pour agrandir | ❌ Refusée — `Focus Travail` (F1) existait déjà ; la contrainte réelle est la hauteur, d'où le repli de la frise |
| Cohabitation avec le bloc Évaluation | ⏸ Aucune pour l'instant — on construit à côté, on branchera une fois testé |

## Séance 3 — carte 3 : Évaluation ciblée

Med dérive de l'orientation validée **au plus 5 axes** d'observation. Chaque axe porte son
intitulé, **ce qu'il vient trancher** (repris mot pour mot de l'orientation), **4 à 6 constats
cochables OUI / NON**, et sa propre zone de remarques.

Deux temps de génération, pour la même raison que l'orientation : un appel pose les axes, puis un
appel par axe produit ses constats — chacun sachant ce que son axe sert, et voyant les constats
déjà posés pour ne pas les redire sous un autre nom.

| Décision | État |
|---|---|
| Origine des axes | ✅ L'orientation **telle qu'à l'écran**, pas celle enregistrée — le médecin vient souvent de l'affiner sans cliquer Enregistrer |
| Axe sans rattachement | ❌ Refusé dans le prompt — un axe qui ne se rattache à rien est un inventaire qui revient par la fenêtre |
| Nombre d'axes | ✅ 5 maximum — un axe, c'est du temps d'observation dans une séance qui en a peu |
| Formulation des constats | ✅ **Constat, jamais inférence** — « se retourne quand on entre » se coche ; « trouble de l'attention » se conclut |
| Portée des constats | ✅ Observable dans le cabinet, pendant cette séance — rien qui suppose l'école ou le récit d'un tiers |
| Troisième état | ✅ **Case vide = non observé**, jamais « non » — la fiche l'écrit `- [ ]` et le dit en toutes lettres |
| Se dédire | ✅ Recliquer une case la décoche — sinon une erreur de clic en séance devient une donnée qu'on ne peut plus qu'inverser en mentant |
| Remarques | ✅ **Une zone par axe** — une phrase orpheline ne dirait plus, à la synthèse, sur quoi elle portait |
| Chiffre affiché | ✅ Constats **renseignés** sur constats proposés — jamais un score : compter les oui donnerait un chiffre qui ressemble à une gravité |
| Orientation vide | ✅ Refus explicite, **sans dépenser d'appel** — plutôt que produire des axes génériques |
| Axe dont les constats échouent | ✅ Il reste, avec sa charpente ; l'échec est nommé |
| Réécriture des axes | ❌ Bloquée si des axes existent — les remplacer laisserait les coches sans leurs constats |
| Écriture de la fiche | ✅ Les **deux** rubriques écrites quel que soit le bouton — sinon enregistrer l'orientation effacerait les coches sans rien dire |
| Modèle | ✅ Étape `evaluation_ciblee` au catalogue, phase Environnement & évaluation ciblée |

## Séance 3 — carte 4 : Cartographie de l'environnement, versant médecin

Les **14 items** qui mettent en cause l'adulte qui remplit ne peuvent pas lui être posés : le
médecin les cote depuis l'entretien, dans les 4 feuilles et leurs 11 nervures. Les 22 autres
partent en salle d'attente sur la feuille parents.

| Décision | État |
|---|---|
| Échelle | ✅ **OUI / NON**, la même que la feuille parents — les deux moitiés doivent se lire ensemble |
| Troisième état | ✅ **Case vide = non renseigné**, jamais « non ». Ces items sont des affirmations favorables : un « non » y est un signal, en faire le défaut peindrait en rouge tout ce qui n'a pas été abordé |
| Items du parent à l'écran | ✅ **Affichés en grisé**, non cliquables, étiquetés « feuille parents » — sinon le médecin coterait 3 lignes en croyant qu'elles font toute la nervure |
| Couleur des nervures | ⏭ **Attend les deux moitiés** — colorer sur les seuls items du médecin donnerait une teinte qui a l'air d'un résultat. La nervure annonce ce qui lui manque (`0/2 cotés · 2 de la feuille parent`) |
| Texte des items | ❌ Non éditable — questions fixes, communes à tous les patients : c'est ce qui rend une feuille comparable d'un dossier à l'autre |
| Se dédire | ✅ Recliquer une case la décoche |
| Écriture dans la fiche | ✅ Seuls les items **répondus** — écrire les vides remplirait la fiche de lignes qui ressembleraient à un travail fait |
| Clé de relecture | ✅ **Le texte de l'item**. Un item reformulé perd son ancienne réponse : la faire glisser attribuerait au médecin une réponse à une question qu'on ne lui a pas posée |

## Séance 3 — carte 5 : feuille parents, scan et dépouillement

Deux gestes séparés : **scanner** en fin de séance (deux minutes, la famille est encore là),
**dépouiller** après (sans elle). Le scan est archivé à côté de la fiche ET versé aux Documents du
dossier bleu, où son crayon rouvre le dépouillement.

| Décision | État |
|---|---|
| Nombre de lignes par bloc | ✅ **Lu dans la carte de coordonnées** (`nb`), jamais supposé — les blocs font 5/6/9/2 items, coder « six » ferait inventer des réponses dans *Cadre & repères* |
| Désaccord gabarit / catalogue | ✅ Le bloc est **refusé**, pas lu — un décalage d'une ligne fausserait toute une feuille en silence |
| Case douteuse | ✅ Le modèle répond `null` — une case vide se corrige à la main, une case devinée passe inaperçue |
| Pré-remplissage | ✅ Ne touche que les lignes **encore vides** : une correction manuelle n'est pas reprise par une seconde lecture |
| Informateur | ✅ Lu indépendamment, conservé même si les blocs échouent, et **nommé dans la fiche** |
| Score / couleur au dépouillement | ❌ **Aucun**, contrairement à la feuille de l'enfant : une feuille d'environnement se lit sur ses deux moitiés, un chiffre sur la seule part du parent aurait l'air d'un résultat. Chaque bloc annonce à la place `+ 5 cotés par vous` |
| Avertissement « bloc incomplet » | ❌ Retiré : là-bas il protégeait un SCORE creux ; ici rien n'est scoré, et un retour partiel est un fait clinique — c'est même le cas prévu quand la feuille ne revient pas complète |
| Chemin du scan | ✅ Retenu dans la fiche — sans lui, « reprendre le dépouillement » n'a aucune image à montrer |
| Versement aux Documents | ✅ Formulaire **déclaré** (`CARTOENV`), pas reconnu : une feuille manuscrite scannée n'a pas de couche texte et finirait classée « bilans » |
| Fenêtre de dépouillement | ✅ Jumelle de celle de l'enfant, mais **séparée** — fondre les deux aurait obligé à masquer ce qui n'a pas de sens dans l'une ou l'autre |

## Séance 3 — carte 6 : Synthèse de la séance

Trois temps, dans cet ordre : les réponses **réunies et montrées**, les **deux fiabilités**, puis
le **texte** rédigé en conséquence.

| Décision | État |
|---|---|
| Réunion des deux moitiés | ✅ C'est ici que la feuille parents et vos 14 items se rejoignent — nulle part avant |
| Couleur d'une nervure | ✅ **Seulement si elle est complète.** 2 à 4 items : un seul manquant déplace la teinte d'un tiers. Le gris n'est pas un défaut d'affichage, il dit qu'on ne sait pas encore |
| Couleur d'une feuille | ✅ Seulement si TOUTES ses nervures sont lisibles — une feuille dont une tige manque n'est pas « un peu moins sûre » |
| Deux blocs | ✅ Environnement et évaluation ciblée restent **séparés jusqu'au bout** |
| Deux fiabilités | ✅ Un curseur chacun. L'environnement repose pour moitié sur une feuille de salle d'attente, l'évaluation ciblée sur ce que le médecin a vu : un seul poids traiterait l'un comme l'autre |
| Effet du poids | ✅ Il module la **prudence des formulations**, jamais un chiffre — aucun compte n'est corrigé |
| Source « non exploitable » | ✅ **Écartée** du texte et son absence DITE, jamais pesée à zéro |
| Les deux écartées | ✅ Refus explicite, **sans dépenser d'appel** |
| Rédaction avant fiabilités | ❌ Bloquée — sans poids, la synthèse affirmerait du même ton une feuille remplie avec soin et une feuille griffonnée dans le couloir |
| Structure de l'appel | ✅ Trois appels courts : environnement, évaluation ciblée, puis mise en regard. La mise en regard est un plus — si elle échoue, les deux présentations sont gardées |
| Nervures non lisibles dans le prompt | ✅ **Dites**, jamais omises — les taire laisserait croire que la feuille a été lue en entier |
| Conclusion | ❌ Aucune. La synthèse présente et qualifie ; le croisement avec l'interrogatoire et les bilans reste à la **Synthèse Globale** |
| Fiabilités dans le texte | ✅ Écrites **en tête**, pas en note de bas de page : elles conditionnent la lecture de ce qui suit |
| Modèle | ✅ Étape `seance3_synthese` au catalogue |

## Séance 3 — versement au dossier bleu

Trois entrées, et non une : deux cartes dans **BILANS**, un bloc dans **SYNTHÈSE**.

| Décision | État |
|---|---|
| Cartographie et évaluation ciblée | ✅ **Deux cartes distinctes** — les fondre laisserait croire qu'un même regard les a produites, alors que l'une repose pour moitié sur une feuille de salle d'attente |
| Moment du versement | ✅ **Dès l'enregistrement**, sans attendre la fin de la séance — c'est l'état porté par la carte qui dit où en est le travail, pas sa présence |
| Cartographie incomplète | ✅ Versée telle quelle, avec son compte de nervures lisibles — la présenter comme une cartographie complète ferait fonder plus tard un raisonnement sur du gris |
| Nervure dépliable | ✅ Le clic ouvre ses réponses, avec leur **source** (feuille parents / entretien) — un état seul ne dit pas ce qui accroche |
| Couleur d'un axe ciblé | ❌ Aucune — trois « oui » sur quatre ne valent pas un score, ils disent trois faits |
| Remarques du médecin | ✅ Affichées sous les constats de l'axe : c'est ce qu'il a vu, ça pèse plus qu'une case |
| Synthèse au dossier | ✅ Onglet SYNTHÈSE, après celle de la cartographie de l'enfant — le dossier suit l'ordre des séances |
| Fiabilités | ✅ Elles **voyagent avec le texte**, en encart — sans elles, la synthèse se relirait dans six mois avec l'assurance d'une source jugée douteuse |

## Séance 3 — carte 7 : Terminer la séance

| Décision | État |
|---|---|
| Ce que fait le bouton | ✅ Enregistre **tout** (les six cartes), puis fige la fiche en lecture seule |
| Ordre | ✅ Enregistrer **puis** clôturer — la garde d'écriture refuse une fiche déjà close, poser la date d'abord empêcherait d'enregistrer le travail qu'on est en train de clore |
| Garde-fou | ✅ Il **nomme** ce qui manque, partie par partie, avec le compte exact (« 9 de vos items non cotés », « 20 réponses manquantes sur 22 ») |
| Blocage | ❌ Aucun — une feuille qui ne revient pas de la salle d'attente est un fait clinique, pas une négligence. Bloquer obligerait à inventer des réponses pour pouvoir fermer |
| Où vit le garde-fou | ✅ Sur la **fiche**, pas sur les écrans : on enregistre d'abord, et c'est de ce qui a été écrit qu'on doit répondre |
| « Non revenue » vs « non dépouillée » | ✅ Distingués — ce ne sont pas les mêmes suites à donner |
| Irréversibilité | ✅ Assumée, comme à la séance 2 : une séance indéfiniment modifiable ne serait plus le témoin d'un jour donné |
| Synthèse reprise à l'enregistrement | ✅ Seulement si l'écran en porte une — sinon enregistrer depuis une autre carte effacerait une synthèse déjà rédigée |

## Retrait du bloc Évaluation V1 — étape 1 : couper l'entrée

Les deux nouveaux blocs couvrent 4 des 5 étapes de la V1. La 5ᵉ — le **Bilan Final** — n'a pas
de remplaçant, et c'est la seule sortie que l'aval consomme (Synthèse Globale, Projet
thérapeutique, Restitution). Le retrait se fait donc en trois temps, dont voici le premier.

### Ce que disent les dossiers (relevé du 02/09/2026)

37 fiches d'évaluation V1, sur 35 patients, **toutes clôturées**. 16 patients portent un
diagnostic retenu. Leur Synthèse Globale :

| Situation | Nombre |
|---|---|
| Synthèse **validée et remplie** — la conclusion y est déjà | 12 |
| Synthèse brouillon, vide | 1 (ANGELETTI) |
| Aucune synthèse | 3 (JUANICO, MAKOUAR, SAINT-LUC) |

**La migration en masse a donc été écartée.** 15 des 17 synthèses sont validées, et chez MANCINI
la synthèse dit *trouble de l'adaptation avec anxiété* là où la V1 disait *trouble anxieux
généralisé* : réinjecter le V1 y remettrait une conclusion que le médecin avait remplacée.
Restent **4 dossiers** à traiter à la main, avec un brouillon proposé par Med.

### Étape 1 — faite

| Décision | État |
|---|---|
| Jalon « Évaluation » dans la frise | ✅ Devient **« Évaluation (archive) »**, affiché **uniquement** pour les dossiers qui en portent une, et sans chemin de création |
| Bouton « Commencer » | ✅ Retiré — l'écran dit à la place où le travail se fait désormais |
| Entrée du menu « + » | ✅ Offerte aux seuls dossiers portant une V1, libellée « archive » |
| `StartCommand` | ✅ `CanExecute` forcé à `false` — garde de dernier recours si un binding réapparaît |
| `CanStart` | ✅ Laissé intact : il pilote aussi l'affichage du panneau archive |
| Verrou de la Synthèse Globale | ✅ **`evalCompleted || SeanceEnvAchevee`** — sans ce relais, tout patient évalué par les séances 2 et 3 resterait devant un jalon verrouillé par une étape qui n'existe plus pour lui. Strictement plus permissif : aucun dossier ne perd l'accès |
| Code de lecture V1 | ✅ **Intact** — les 37 fiches restent lisibles et l'aval continue de les lire |

### Étape 2 — les dossiers dont la conclusion n'est pas encore reprise

**Aucun moteur de migration n'a été écrit, et c'est le résultat de la vérification :**
`SyntheseGlobaleSuggesterService` filtre les évaluations sur `!IsActive` seulement — pas sur
`IsValidated`. Les 37 fiches étant toutes clôturées, le flux existant lit déjà leur Bilan Final.
Générer une Synthèse Globale sur ces dossiers reprend donc leur conclusion sans code nouveau.

Ce qui manquait n'était pas un moteur, mais **de savoir lesquels restent à faire**.

| Décision | État |
|---|---|
| Marqueur | ✅ Sur le jalon archive : `⚠ conclusion à reprendre en Synthèse` |
| Critère de reprise | ✅ Une synthèse **validée**. Un brouillon ne compte pas — le seul brouillon relevé était vide, le compter aurait effacé le marqueur d'un dossier où rien n'avait été repris |
| Effacement | ✅ **Automatique** — le marqueur disparaît dès la validation. Pas de liste à tenir ni à penser à effacer |
| Vérification | ✅ Règle passée sur les 35 dossiers réels : **4 marqués** (ANGELETTI, JUANICO, MAKOUAR, SAINT-LUC), **12 déjà repris** |
| Nouveau champ | ✅ `FriseStageViewModel.Note` — remplace la ligne d'état quand il y a plus important à dire que « Clôturée » |

### Étape 3a — la Synthèse Globale voit enfin les deux séances

**Un trou, pas une dette.** `PatientContextService.ClinicalContext` = synthèse existante OU notes ;
les évaluations lues étaient V1 uniquement. Un enfant évalué par les séances 2 et 3 obtenait donc
une Synthèse Globale **qui ignorait ses deux cartographies** — le travail était au dossier, mais
invisible de la chaîne qui en dépend.

`EvaluationV2ContextService` restitue les deux séances sous une forme lisible par un modèle, et
sera réutilisé tel quel pour repointer les trois autres consommateurs.

| Décision | État |
|---|---|
| Ce qui est transmis | ✅ Les **synthèses** d'abord, avec leurs fiabilités, puis un résumé par axe et par nervure |
| Ce qui ne l'est pas | ❌ Les 156 items un par un — le prompt porte déjà tout le dossier, et noyer une conclusion sous ses justificatifs la rend moins lisible. Mesuré : **1 152 caractères** pour une séance complète |
| Feuille non lisible | ✅ **Dite**, jamais omise, avec la consigne « ne pas l'interpréter » |
| Case non cochée | ✅ **Absente** du contexte — jamais transmise comme un « non » |
| Orientation diagnostique | ✅ Étiquetée « mise au point de l'attention, PAS un diagnostic » — sans quoi un modèle la lit comme une conclusion |
| Fiabilités | ✅ Transmises, avec la consigne qu'elles qualifient la source et jamais la valeur |
| Garde « dossier vide » | ✅ Les séances V2 comptent comme matière : un dossier sans V1 mais avec deux cartographies n'est plus refusé |
| Chemins câblés | ✅ Création (`GenerateInitialAsync`) **et** relecture incrémentale (`SuggestPatchAsync`) |
| Bloc V1 dans le prompt | ✅ Conservé, renommé « ancien parcours » — les dossiers anciens continuent d'être lus |

**Restent à repointer :** `ProjetTherapeutiqueSuggesterService`, `SyntheseGlobaleRelectureService`,
`DossierReaderService` + `RestitutionHtmlPreviewService`.

### Étape 3b — les trois autres consommateurs

`EvaluationV2ContextService` a été écrit une fois ; le brancher a coûté quelques lignes par moteur.

| Consommateur | État | Note |
|---|---|---|
| `SyntheseGlobaleSuggesterService` — création | ✅ Bloc `[C]` | + garde « dossier vide » corrigée |
| `SyntheseGlobaleSuggesterService` — patch | ✅ Bloc `[D]` | |
| `SyntheseGlobaleRelectureService` | ✅ Bloc `[D]` | **Le plus important** : la relecture vérifie que chaque affirmation est SOURCÉE. Sans les cartographies, elle aurait signalé « non sourcé » tout ce que la synthèse en tire à juste titre. La consigne précise qu'une feuille NON LISIBLE ou une source « non exploitable » reste, elle, une source invalide |
| `ProjetTherapeutiqueSuggesterService` — création | ✅ Bloc `[D]` | Consigne ajoutée : les cartographies disent aussi **ce qui tient**, ce sur quoi un projet s'appuie |
| `ProjetTherapeutiqueSuggesterService` — patch | ✅ Bloc `[C bis]` | + garde « dossier vide » corrigée |
| `DossierReaderService` + `RestitutionHtmlPreviewService` | ⏭ **Non fait — et c'est délibéré** | |

**Pourquoi la Restitution n'a pas été branchée.** Ce n'est pas un câblage : ses pages sont des
maquettes construites autour des FORMES V1 — `BuildCartoEnfantPageA/B/C` dessine la chenille à 6
segments, `BuildEnvEduPage1/2/3` les 5 feuilles, sur ~250 lignes de HTML mis en page. La V2 n'a ni
la même structure (5 axes + 18 profils observés) ni le même découpage (4 feuilles, 36 items).

Les brancher mécaniquement produirait un document faux pour les parents. C'est une **refonte
graphique du document de restitution**, pas un repointage — et le ton destiné aux parents relève
d'une décision clinique, pas technique. À traiter comme un chantier à part.

### Où en est le retrait du bloc V1

| | |
|---|---|
| Entrée coupée | ✅ |
| 4 dossiers à reprendre en Synthèse | ⏳ marqués dans la frise, à faire par le médecin |
| Synthèse Globale (création, patch, relecture) | ✅ repointée |
| Projet thérapeutique (création, patch) | ✅ repointé |
| Restitution | ⏭ refonte graphique à part |
| Suppression des ~7 300 lignes | ⏸ **bloquée par la Restitution**, qui reste le seul consommateur des formes V1 |

## Dossier de restitution — audit page par page

Le dossier de restitution (32 blocs, DossierRestitutionInitial) est le document remis à la
famille — c'est elle qui décide de sa circulation. Audit mené page par page en situation réelle,
pas bloc par bloc : plusieurs blocs (`carto_s2..s8`, `env_edu_f2..f5`) ne produisent AUCUNE page,
absorbés dans le premier bloc de leur groupe.

### Page 1 — Identité & couverture

| Décision | État |
|---|---|
| Dates d'évaluation | ✅ Construites depuis les séances RÉELLES (1er entretien + cartographie + environnement), plus les fiches V1 en repli. Avant : vide pour tout patient évalué par les séances 2/3 |
| Année scolaire | ✅ **Calculée** depuis la date, plus de `"2025-2026"` codé en dur |
| Bascule de rentrée | ✅ `ScolariteRentreeService` — boîte à 3 réponses (Mettre à jour / Rien n'a changé / Plus tard), déclenchée aux séances qui supposent un parent en face |

### Page 2 — Restitution 1-page parents

| Décision | État |
|---|---|
| Feuille de route | ✅ **Différée** — ne se rédige plus depuis le dossier bleu (elle inventait un parcours avant que le projet existe) mais depuis les blocs `pt_s1..s5`, une fois remplis. En attente sinon, sans appel au modèle |
| Qui porte l'action | ✅ Consigne posée dans la feuille de route : « vous prendrez rendez-vous… », « je revois… », neutre si le projet ne précise pas — en attendant un vrai champ porteur sur `ProjetAction` (à faire aux pages du projet) |
| « Ce qui peut aider » | ✅ Recentrée sur les gestes du quotidien à la maison — interdiction explicite d'y écrire une orientation, un rendez-vous, un suivi |

### Page 3 — Identification, Motif, Contexte familial

| Décision | État |
|---|---|
| `patient_identification` | ✅ Rendu **déterministe** (identité, dates, évaluateur, lieu) — seule la phrase de présentation reste rédigée par le modèle |
| Identité des parents | ✅ Extraite d'ADMIN et transmise **en clair, en tête du prompt** (`ExtraireIdentiteParentsAdmin`) — plus noyée dans le JSON brut de `patient.json`, où un modèle modeste (Gemma) la reconstituait depuis les notes au lieu de la lire |
| Accompagnant du 1er entretien | ✅ Distingué de l'accompagnant ADMIN — les notes du 1er entretien font foi sur qui était présent CE jour-là |
| Père / Mère | ✅ Prénom et nom viennent EXCLUSIVEMENT du bloc ADMIN, jamais reconstitués des notes |
| « Autres figures » | ✅ **Bug corrigé** — Gemma classait les professionnels ayant fait un bilan parmi les figures d'attachement. Règle posée : lien affectif durable ET vécu partagé, jamais un professionnel ; doute → exclusion |

### Page 4 (Antécédents) — bug identifié en testant, corrigé au passage

| Décision | État |
|---|---|
| « Bilans résumé » / « Suivi résumé » | ✅ **Bug corrigé** — l'évaluation en cours (cartographie) et le propre suivi du médecin se retrouvaient listés comme un bilan antérieur. La règle existait déjà dans « Parcours — détail » mais pas dans les deux résumés compacts, générés AVANT lui dans la séquence |

### Trou transversal — corrigé

`RenderForLlm()` ne transmettait **aucune** cartographie au modèle — ni V1 (chargée mais jamais
mise en texte, seulement utilisée pour les dessins des pages 8-21), ni V2
(`EvaluationV2ContextService`, déjà écrit et branché ailleurs, mais pas ici). Le bloc
`patient_situation_actuelle` cite pourtant les cartographies comme sa source principale : il les
décrivait sans jamais les voir.

| Décision | État |
|---|---|
| Cartographie V1 (chenille + 3 profils) | ✅ Textifiée dans `RenderForLlm()`, même format que celui déjà éprouvé dans la Synthèse Globale |
| Cartographie de l'environnement V1 (5 feuilles) | ✅ Textifiée — couleur calculée par `EnvironnementScoringService` |
| Séances V2 (cartographie enfant + environnement) | ✅ `EvaluationV2ContextService.PourPrompt()`, même lecteur que la Synthèse Globale et le Projet thérapeutique |
| Segment ou profil non renseigné | ✅ Transmis à 0, jamais omis — sauf Tempérament et Attention qui disparaissent si `IsRenseigne` est faux |
| Cartographie entièrement vierge | ✅ Aucune section générée — pas de zéros qui laisseraient croire à une évaluation faite |
| Seuil de rendu | ✅ Score non nul sur au moins un segment, pas `IsValidated` seul — une cartographie en cours vaut mieux qu'une absence pour « Situation actuelle » |

### Page 4 (Antécédents) — deux bugs, une seule cause

Signalé par l'utilisateur : le détail des bilans (QI, fragilités, hypothèses, valeurs biologiques,
examen cardiologique) s'affichait au milieu du dossier, dans « Antécédents », au lieu d'une
annexe. En creusant, la cause réelle était double et plus profonde qu'un problème de position.

**Bug 1 — mauvais endroit.** La page « Parcours — détail » (déjà appelée « annexe » dans son
propre en-tête HTML) était rendue entre les Antécédents résumés (page B) et la Situation actuelle
(page C) — au milieu du parcours narratif, pas à la fin.

**Bug 2 — mauvaise attribution (la vraie cause du symptôme visible).** `ParseAntecedents`
découpait le markdown sur TOUT titre en gras rencontré. Or « Parcours — détail » contient
lui-même deux titres imbriqués (« Suivi antérieur », « Bilans réalisés »), écrits par
`RunProgressiveSubsectionsAsync` immédiatement après le titre « Parcours — détail » lui-même. Le
découpage à plat les traitait comme de NOUVELLES sous-sections de premier niveau : « Suivi
antérieur » (contient « suivi ») écrasait silencieusement le résumé compact « Suivi résumé »,
« Bilans réalisés » (contient « bilan ») écrasait « Bilans résumé » — et « Parcours — détail »
se retrouvait vide. Le détail n'était donc pas seulement mal placé : il remplaçait le résumé
compact directement sur la page qui devait rester courte.

| Décision | État |
|---|---|
| Position de l'annexe | ✅ Déplacée à la **toute fin** du document, après « Conclusion et perspectives » — rendue une seule fois, hors de la boucle principale |
| Numéro de renvoi (page B → annexe) | ✅ Calculé comme `totalPages` (la vraie dernière page), plus une estimation à `pageNumber + 2` |
| Libellé du renvoi | ✅ Explicite : « Détail des suivis et bilans → Annexe, p.N » |
| `ParseAntecedents` | ✅ N'ancre plus que sur les **six titres canoniques** ; tout titre en gras qui n'en est pas un (les titres imbriqués de Parcours — détail) reste le contenu de l'ancre précédente |
| Cartographie vierge / sans détail | ✅ Aucune annexe générée — pas de page finale vide |
| Vérification | ✅ Test bout en bout sur `BuildPreviewHtml()` : ordre réel des pages, numéro de renvoi, absence d'annexe quand il n'y a rien à y mettre |

**Bug 3 — le vrai problème persistait quand même (retest utilisateur, même jour).** Après les
correctifs 1 et 2, le symptôme est resté identique EN PLUS de faire disparaître l'annexe. Cause :
Gemma (modèle local) n'écrit pas toujours une section « Parcours — détail » séparée — il écrit le
détail complet (titre du bilan + sous-puces QI/fragilités/hypothèses/recommandations) directement
sous « Bilans résumé ». La détection de « contenu détaillé » reposait sur la longueur de LIGNE
(> 80 caractères) : comme le modèle avait découpé le détail en plusieurs puces courtes plutôt
qu'une seule ligne longue, aucune ligne ne dépassait le seuil — la détection ne se déclenchait
jamais, et le résumé compact affichait le détail complet tel quel, sans annexe du tout.

Correctif : `SplitResumeItems()` (nouvelle méthode dans `RestitutionHtmlPreviewService`) remplace
la détection par longueur de ligne par une lecture structurelle, bloc par bloc (séparés par ligne
vide) : un bloc réduit à une seule puce courte reste tel quel dans le résumé ; un bloc composé d'un
titre suivi de sous-puces (peu importe leur longueur individuelle) est reconnu comme un « item
détaillé » — seul son titre reste dans le résumé compact, le bloc entier part vers l'annexe (soit
sous la section « Parcours — détail » si le modèle l'a produite, soit en fallback direct sinon).
Filet de sécurité indépendant du modèle utilisé : fonctionne que le LLM respecte ou non la consigne
de concision, et quelle que soit la longueur des puces individuelles.

| Décision | État |
|---|---|
| Détection de détail | ✅ Structurelle (titre + sous-puces), plus par longueur de ligne |
| Résumé compact (page B) | ✅ N'affiche plus que le titre de chaque item, même sans section « Parcours — détail » explicite |
| Annexe sans section dédiée du modèle | ✅ Toujours générée en fallback à partir du détail extrait du résumé |
| Vérification | ✅ Test reproduisant exactement la structure réelle observée (titre + puces courtes sous « Bilans résumé », sans « Parcours — détail ») : le titre reste dans le résumé, le QI n'apparaît qu'en annexe |
