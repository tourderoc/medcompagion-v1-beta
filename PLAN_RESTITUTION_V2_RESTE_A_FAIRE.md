# PLAN — Dossier de Restitution V2 : ce qui reste à faire

> **Statut :** refonte page par page terminée le 11 septembre 2026 (blocs 1 → 32), testée en réel. Annexe contacts faite le 16/09. **Service qualité ouvert le 17/09 : phase 1 (mise en page) et repagination automatique faites — 0 débordement sur 175 pages mesurées** ; voir §6. Trois chantiers de fond restent ouverts.
> **Date d'ouverture :** 11 septembre 2026
> **Docs liés :** [PLAN_RESTITUTION_PARENTS.md](PLAN_RESTITUTION_PARENTS.md), [PLAN_CARTOGRAPHIE_ENFANT_V2.md](PLAN_CARTOGRAPHIE_ENFANT_V2.md), [CLAUDE.md](CLAUDE.md)

---

## 1. Ce qui est fait

L'audit page par page du **Dossier de Restitution Clinique** (32 blocs) est terminé. Le document a été relu et reconstruit dans l'ordre où les parents le lisent, de la couverture à la conclusion :

| Bloc | État |
|---|---|
| 1-7 — Couverture, restitution 1-page, patient & contexte | Parcours de soins réécrit (parsing ligne à ligne), annexe détaillée en page 6, mots-clés de conclusion sur chaque bilan |
| 8-15 — Cartographie de l'enfant (8 sphères) | Recâblée sur les données V2, style graphique V1 conservé (secteurs colorés) |
| 16-21 — Cartographie de l'environnement | Recâblée feuille par feuille sur la V2, passage 5 → 4 cartes, échelle de couleur par dimension, lecture globale de la branche |
| 22-26 — Synthèse diagnostique | Med relit le dossier bleu comme un clinicien : consultations + leurs synthèses, bilans externes limités à leurs synthèses, séances lues intégralement |
| 27-31 — Projet thérapeutique (7.1 → 7.5) | Structuré en actions (`quoi / porteur / échéance / degré`), éditeur miroir du modèle, cycle générer → modifier → régénérer sans effacement |
| 2 — Feuille de route | Alimentée par les actions structurées, triées par priorité puis par échéance |
| 32 — Conclusion | Quatre blocs : l'enfant rendu entier, ses forces ancrées, ce qui reste ouvert, ce qu'est ce document |
| 33 — Annexe contacts | Qui est qui autour de l'enfant, lu du dossier bleu : parents, école, médecin traitant, intervenants des bilans, et les professionnels qui restent à trouver |

**Conséquence hors périmètre :** la Restitution était le dernier consommateur des formes de l'Évaluation V1. Sa suppression (~7 300 lignes) n'est plus bloquée — voir la note de suivi correspondante.

---

## 2. Principes acquis — à ne pas défaire

Ces règles ont été posées une par une pendant la refonte, souvent après un contre-exemple observé en vrai. Les rouvrir sans raison, c'est refaire les mêmes pannes.

**Le chaînage.** Chaque niveau attend et lit le niveau du dessous : lectures → synthèse → projet → feuille de route → conclusion. Chaque niveau porte son verrou et son message d'attente ; aucun ne peut partir avant son amont.

**Pas de circularité.** Aucun niveau ne relit sa propre version antérieure. `DossierReading` a trois rendus pour ça : `RenderForLlm()`, `RenderForSynthese()` (séances intégrales, Synthèse Globale et Projet retirés), `RenderForProjet()` (projet antérieur retiré).

**Rien deux fois.** Une même proposition ne figure jamais à deux endroits du dossier. C'est une règle écrite dans les prompts inter-sections du projet, et c'est la raison pour laquelle la feuille de route et les rendez-vous ont quitté la conclusion.

**La couleur vient de la donnée.** Jamais d'un mot-clé produit par le modèle. Un vocabulaire riche du modèle ne doit pas faire retomber une pastille en gris. Et le gris veut dire « on ne sait pas encore » — un seul sens, un seul gris.

**Une consigne de prompt ne suffit pas.** Sur un modèle local modeste (Gemma), le garde-fou est dans le code : parsing structurel plutôt qu'heuristique de longueur, listes de valeurs fermées exposées au prompt *et* à l'éditeur, validation de la forme JSON avant d'accepter une régénération.

**Le texte du médecin fait foi.** On propage, on ne régénère pas : `PropagerBlocStructureAsync` conserve mot pour mot ce qui est saisi et ne met à jour que ce que la saisie rend incohérent. Une réponse hors format ne remplace jamais la saisie. Seule exception assumée : le champ `pourTrancher` d'un bilan, qui est une formulation clinique et non une décision.

**Déclenchements manuels.** Le médecin clique, rien ne part tout seul. « Plus simple c'est mieux. »

**Écarter une section est une décision, pas un trou.** *(posé le 16/09/2026)* Une section du projet peut n'avoir aucune indication — un enfant sans besoin développemental particulier, une famille qui n'a besoin d'aucun accompagnement. Le dire, avec son motif et ce qui ferait reconsidérer, est une information clinique ; laisser la page vide se lit comme un oubli.

La mécanique est celle de la 7.2, généralisée aux **7.3 et 7.4** : un champ `indication` en tête de section (`degre` / `porteur` / `motif` / `critereReevaluation`), avec un degré de plus que les actions — **« non indiqué à ce stade »**, qui ne vaut que pour une section entière et jamais pour une action isolée.

- **Éditeur** : choisir « non indiqué à ce stade » ou « à réévaluer plus tard » replie les sous-sections et affiche un bandeau. Rien n'est effacé — un lien « afficher quand même » les rouvre, et changer de degré réarme le repli.
- **Document** : la page porte l'indication, le motif et « À reconsidérer si… », puis s'arrête. Aucune carte n'est alignée sous une indication qu'on vient d'écarter.
- **7.5 École garde sa logique propre** : le cadre scolaire est une décision administrative, pas une indication clinique. **7.1 Médical** aussi : ses actions portent déjà leur degré, une indication globale par-dessus ferait doublon.

**L'ordre d'édition n'est pas l'ordre de lecture.** *(posé le 16/09/2026)* Le médecin rédige dans l'ordre où l'information devient disponible ; les parents lisent dans l'ordre du document. Les deux listes sont désormais distinctes : `_dossier.Blocs` est l'ordre du document — c'est elle que l'aperçu et le PDF parcourent — et `RestitutionEditorViewModel.OrdreEdition` donne l'ordre de rédaction.

Un seul écart aujourd'hui : la **« Restitution 1-page parents » descend juste après le projet thérapeutique** dans l'éditeur, et reste en page 2 dans le document. Sa section « Notre feuille de route » ne se rédige pas depuis le dossier mais depuis le projet que le médecin vient de décider (`RedigerFeuilleDeRouteAsync`). La laisser en deuxième position coûtait deux fois : « Générer tout » atteignait cette page avant le projet — la feuille de route retombait immanquablement sur son message d'attente — et il fallait remonter trente blocs après avoir fini le projet, ce qui s'oubliait. Les cinq autres sections se rédigent depuis les notes du patient, lues dès l'ouverture : les descendre ne leur enlève rien.

Si un bloc du projet manque (dossier d'un ancien parcours), rien n'est déplacé — on ne devine pas une position.

**Voix selon le destinataire.** Clinique sobre pour le médecin, voix du livre pour les parents — et sur la dernière page, un ton mixte sans condescendance.

---

## 3. Les chantiers

### 3.1 Annexe contacts — ✅ fait le 16 septembre 2026

Une page entre la conclusion et l'annexe méthodologique, qui rassemble **qui est qui** autour de l'enfant. Code : `RestitutionHtmlPreviewService.Contacts.cs`. Tests : section 33 du TestRunner (18 vérifications).

**Rien n'est généré et rien n'est à saisir** — tout existe déjà dans le dossier bleu :

| Carte | Source |
|---|---|
| Mère, Père | `patient.json` — prénom, nom, téléphone, email |
| Accompagnant | `patient.json`, **seulement si ce n'est ni la mère ni le père** — sinon on ferait croire à un tiers |
| École | `patient.json` — nom, classe, adresse assemblée, téléphone, email (coordonnées de l'annuaire Éducation Nationale) |
| Médecin traitant, médecin référent | `patient.json` — « Dr » ajouté seulement si le nom saisi ne le porte pas déjà |
| Intervenants | `info_patient/intervenants.json` — extraits automatiquement à l'import de chaque bilan, avec le document d'origine rendu lisible (`2025-03-12_bilan_orthophonique.pdf` → « bilan orthophonique ») |

**Réponses aux trois questions qui étaient ouvertes :**
- *Le praticien de chaque bilan* → aucun champ à créer : `IntervenantService` le capte déjà à l'import, et `SourceDocument` dit de quel bilan il vient.
- *Position* → **avant** l'annexe méthodologique. Les contacts sont la page utile aux parents, ils la chercheront juste après la conclusion ; la méthodologique est générique et destinée aux professionnels, elle ferme le dossier.
- *Les professionnels « à trouver »* → une section **« Reste à identifier »** en bas de page, avec une ligne pointillée à remplir à la main. Elle recopie mot pour mot les actions du projet dont le porteur est `professionnel à trouver` — jamais reformulées, sinon la page contacts et la section 7 diraient deux choses différentes.

**Deux règles tenues, les mêmes que sur la conclusion :**
- Un champ vide ne s'écrit pas, une carte vide ne se dessine pas, et un dossier sans aucun contact n'ouvre pas de page blanche. Les parents n'ont pas à lire les trous du dossier administratif.
- Rien n'est inventé : la page n'affiche que ce qui est saisi quelque part.

**Un piège traité :** `.page` est en `overflow: hidden`. Un dossier très suivi aurait vu ses derniers intervenants **disparaître du PDF sans le moindre signe**. La page se répartit donc sur plusieurs A4 selon la hauteur estimée des cartes, avec « (suite) » et une numérotation ; « Reste à identifier » n'est jamais coupé en deux.

**Effet de bord utile :** `PathService` accepte désormais une racine patients optionnelle (`new PathService(dossierTemporaire)`), uniquement pour que les tests écrivent un patient fictif sans jamais toucher aux vrais dossiers du cabinet.

### 3.2 Synthèse Globale déjà validée + nouveau dossier de Restitution

Aujourd'hui, un dossier de Restitution produit sur un patient qui a déjà une Synthèse Globale validée traite la situation comme une **création v1** — donc un écrasement. Le comportement attendu est un **patch v2** : la synthèse existante est la base, le nouveau dossier l'amende.

Le risque est réel et documenté : 15 des 17 synthèses globales sont validées, et au moins un patient a une synthèse révisée à la main qui contredit la conclusion V1. Écraser ferait régresser des documents signés.

À reprendre du côté `SyntheseGlobaleService` et des points de création / patch / relecture.

### 3.3 Inverser le sens de la source de vérité du Projet Thérapeutique

Le Projet Thérapeutique est encore alimenté depuis la Synthèse Globale : `ProjetTherapeutique.SyntheseGlobaleSourceFichier` pointe le fichier source, relu par `ProjetTherapeutiqueRelectureService`.

Depuis la refonte, le projet du dossier de Restitution (7.1 → 7.5) est la version structurée et vivante — celle qui porte les actions avec leur porteur, leur échéance et leur degré. C'est elle qui devrait faire foi.

Chantier à cadrer : qui devient la source, ce qui se propage dans quel sens, et ce qu'on fait des projets antérieurs qui existent déjà sous l'ancienne forme.

### 3.4 Réconcilier les 8 sphères avec les 5 axes + 3 profils

Le Dossier de Restitution garde une structure en **8 sphères**, héritée de la V1. La Cartographie de l'enfant V2 est bâtie sur **5 axes cotés + 3 profils observés**. Les blocs `carto_s1` → `carto_s8` sont aujourd'hui alimentés par `GetSegmentDataV2` — ça fonctionne, mais la correspondance n'est pas franche.

Un point est déjà réglé : le **Tempérament** ne doit pas devenir un score coloré. C'est un portrait, sans note et sans couleur — le décider autrement reviendrait à noter un enfant sur son caractère.

Reste à trancher si le dossier s'aligne sur les 5 axes de la V2, ou si les 8 sphères restent la grille de lecture destinée aux parents avec un mapping explicite et documenté.

---

## 4. Ordre suggéré

1. ~~**3.1 Annexe contacts**~~ — fait le 16/09/2026.
2. **3.2 Patch v2 de la Synthèse Globale** — c'est celui qui protège des documents déjà signés. **Prochain.**
3. **3.3 Sens de la source de vérité du Projet** — dépend de 3.2, qui fixe le statut de la Synthèse Globale.
4. **3.4 Sphères vs axes** — refonte de présentation, à faire quand le reste est stable.

---

## 5. À surveiller sur l'annexe contacts

- **Les intervenants dépendent de l'extraction à l'import.** Un bilan importé avant que `IntervenantService` n'existe, ou dont l'en-tête n'a pas été lu, ne produit pas de carte. Rien ne sera faux — il manquera simplement quelqu'un. À regarder sur les premiers dossiers réels.
- **Le champ médecin traitant est libre.** « Dr » est ajouté seulement s'il est absent, mais un nom saisi bizarrement s'affichera tel quel.

---

## 6. Service qualité du dossier — ouvert le 17 septembre 2026

Un bouton **🔍 Qualité** dans la barre du bas, **avant** l'export PDF : les phases 2 et 3 modifient les blocs, donc le PDF ; contrôler après obligerait à réexporter. Le panneau prend la place de l'éditeur à gauche, l'aperçu reste à droite — on regarde la page dont on parle.

Trois phases, dans cet ordre. **Rien ne se corrige tout seul.**

### 6.1 Phase 1 — Mise en page ✅ faite

**Le défaut qu'elle révèle.** À l'écran, `.page` est en `min-height: 297mm` : une page trop longue s'allonge et tout reste lisible. En impression, elle passe en `height: 297mm; overflow: hidden` — Edge imprime 297 mm et **coupe le reste, sans trait ni avertissement**. L'aperçu montrait donc du contenu que le PDF supprimait, et rien ne prévenait le médecin.

**La mesure, pas l'estimation.** La phase exécute un script dans le WebView2 de l'aperçu — le même moteur que l'export. Il neutralise le plancher `min-height` le temps de la mesure, sinon toute page non débordante mesurerait exactement 297 mm et la marge restante serait invisible.

**La correction est automatique.** La repagination (§6.2) a déjà réparti les blocs au moment où la mesure a lieu : ce que la phase signale est donc ce qu'elle n'a **pas** pu corriger — un bloc unique trop haut pour tenir sur une page, où qu'on le mette. La seule issue est alors de raccourcir son texte, et le message le dit.

Deux niveaux de constat : **■ bloquant** (dépasse encore après repagination, en mm) et **● conforme**.

Les pages denses (au-dessus de 92 % de remplissage) ne font **pas** un constat chacune. Avant la repagination, « remplie à 99 % » annonçait une coupure ; maintenant une page qui bascule est découpée automatiquement, donc les énumérer n'appelle aucune action — et un rapport qui énumère l'inactionnable apprend à être ignoré. Une ligne récapitulative suffit : *« 3 pages sont remplies à plus de 92 % : si leur texte s'allonge, des pages seront ajoutées automatiquement. »* *(Leçon du 17/09, après un rapport qui affichait quatre cartes sur lesquelles le médecin ne pouvait rien.)*

**Un seul seuil pour les deux.** `RestitutionHtmlPreviewService.ToleranceArrondiPx` (4 px ≈ 1 mm, soit le quart d'une ligne) est partagé par le script du navigateur et le contrôle qualité. Ils ont divergé une fois — 4 px dans le script, 2 px dans le contrôle — et une page à +1 mm était signalée au médecin alors que la repagination refusait de la corriger. **Un test vérifie que les deux valeurs restent égales** (section 37 du TestRunner, qui lit le `var GARDE` du script).

**Deux règles tenues :**
- *Un contrôle muet n'existe pas.* Quand tout va bien, le rapport dit combien de pages il a vérifiées, et combien il n'a pas surveillées. Sinon « rien à signaler » est indiscernable de « le contrôle n'a pas tourné ».
- *On ne surveille la marge que là où le contenu bouge.* La couverture est un gabarit à champs, l'annexe méthodologique une ressource figée : les signaler « à 95 % » sur chaque dossier apprend à ignorer le rapport. Un **débordement** y reste signalé — ce serait un défaut du gabarit, à corriger une fois pour tous. *(Remonté par le médecin après le premier essai réel.)*

### 6.2 Repagination automatique ✅ faite

`RestitutionHtmlPreviewService.Repagination.cs` — un script injecté dans le HTML, qui tourne dans l'aperçu **et** dans l'export.

Pour chaque page bâtie sur des cartes : il les retire, les remet une par une, et **mesure après chaque ajout**. Dès qu'une carte ferait dépasser l'A4, elle ouvre une page suivante — clone de l'ossature, « (suite) » au sous-titre. **Une carte n'est jamais coupée en deux.** Les numéros de page sont réécrits à la fin.

**Deux niveaux**, parce qu'un ne suffisait pas : une page qui n'a qu'**une seule carte** débordant à elle seule (`Situation quotidienne et ressources`, +56 mm) se découpe **à l'intérieur** de la carte, sur ses blocs internes, en reproduisant son en-tête.

`--virtual-time-budget=10000` a été ajouté à l'export PDF : sans lui, Edge pouvait imprimer avant que le script ait fini.

**Résultat mesuré : 0 débordement sur 175 pages**, 7 dossiers dont un rempli en réel.

### 6.3 Pourquoi l'estimation en C# a été abandonnée

Deux estimateurs de hauteur ont été calibrés sur des mesures réelles. **Les deux se sont trompés** :

| Estimateur | Erreur | Conséquence |
|---|---|---|
| Cartographie | surestime de 6 % | découpages inutiles |
| Synthèse 5.2 | sous-estime de 21 % | page coupée quand même |

Et la conclusion « le Projet thérapeutique a de la marge », tirée de 7 dossiers du poste, **s'est effondrée au premier dossier réellement rempli** : trois de ses pages dépassaient, jusqu'à **+76 mm** — une quinzaine de lignes perdues. L'échantillon n'était pas représentatif.

**La leçon : une hauteur de texte ne se calcule pas hors du moteur qui la rend.**

Les paginations C# (cartographie, synthèse 5.2, annexe contacts) sont **conservées** : elles donnent un meilleur point de départ, et restent le seul filet si le script ne s'exécute pas.

**Piège à connaître :** le script vit dans une chaîne C#. Une erreur de syntaxe JavaScript **ne fait pas échouer la compilation** et désactive silencieusement toute la repagination. D'où l'attribut `data-repagine` posé sur `<html>` quand le script a tourné — que le banc vérifie. Trois bugs silencieux ont été trouvés comme ça le premier jour (clone inséré dans la page, clone pris sur la page courante, `return` prématuré sur les pages à une seule carte).

### 6.4 Le banc de mesure — `RestitutionBench/`

Un projet console qui charge les dossiers du disque, rend leur HTML, le mesure dans Edge headless et rapporte les hauteurs. **Il ne sort que des chiffres : aucun contenu de dossier n'est affiché.**

C'est lui qui a corrigé chacune de mes erreurs de la journée. Il expose aussi `EstimationsSynthese52` (via `InternalsVisibleTo`) pour confronter estimation et mesure côte à côte.

```
dotnet run --project RestitutionBench            # les 7 plus gros dossiers
dotnet run --project RestitutionBench -- 20      # les 20 plus gros
```

### 6.5 Phases 2 et 3 — premières couches faites le 18/09/2026

**Phase 2 — couche déterministe ✅.** Bouton « Relire le document ». Relit le **texte produit**, jamais le dossier patient : la vérification des faits reste au médecin. Premier contrôle livré : **un bloc ne cite pas la cartographie avant qu'elle soit présentée au lecteur**.

> Observé sur dossier réel : « Contexte familial » (rang 5) écrivait « la constance des scores parentaux entre les deux passations de cartographie », alors que la cartographie n'est présentée qu'au **rang 8**.

Deux régimes, dérivés du **rang dans la liste canonique**, jamais d'une liste de clés écrite à la main — déplacer un bloc déplace la consigne avec lui :
- **avant le rang 8** : ni « cartographie », ni « sphère », ni « score », ni « passation », ni « grille ». La consigne demande de **reformuler en observation**, pas de retirer l'information ;
- **1-page parents** : autre régime, car elle se lit seule, hors séquence. La démarche peut être évoquée en mots simples ; les scores, numéros de sphère et « passation » non — ils ne disent rien à un parent.

**Prévention dans le prompt + détection en phase 2**, et **une seule source pour les deux** : `RestitutionSuggesterService.TermesInterditsPour()`. Un test vérifie que chaque terme surveillé est nommé dans la consigne — c'est la leçon des deux seuils de la phase 1 qui avaient divergé.

**Phase 3 — Langue ✅.** Bouton « Relire la langue », deux couches :
- **déterministe** : neuf tournures nommées (« est en couple avec », « il est à noter que », « au niveau de »…) + les phrases recopiées d'un bloc à l'autre. Elle **conseille sans proposer de réécriture** : refaire une phrase demande de la comprendre ;
- **modèle** : réécriture proposée, affichée avec le texte d'origine barré, acceptée d'un clic. Estampillée du moteur.

**LA GARDE DE SÉCURITÉ — à ne jamais défaire.** `TexteLibreDuDossier` écarte `porteur`, `echeance`, `degre` et `statut` **avant même de les lire** : ce sont des vocabulaires fermés. « les parents » reformulé en « la famille » casserait les pastilles de couleur, l'annexe contacts et le tri de la feuille de route, **sans que rien ne le signale**. Double verrou (par la clé, puis par la valeur), et la liste des valeurs est **dérivée des vocabulaires eux-mêmes** pour qu'une nouvelle liste fermée soit protégée sans qu'on y pense.

Autres garanties, toutes testées : rien n'est appliqué sans clic ; une retouche du médecin n'est jamais écrasée (le remplacement est ancré sur le texte d'origine) ; si le moteur échoue, la couche déterministe tient et le résumé le dit.

**Reste à faire en phase 2 : la confrontation de deux sections par le modèle.** Deux moteurs pour deux choses différentes :
- **Déterministe (code)** pour les faits : une classe en page 1 et une autre en 7.5, une date de bilan qui diffère entre le parcours et le projet, un « professionnel en place » sans intervenant au dossier, un diagnostic cité dans la conclusion et absent de la synthèse.
- **LLM** pour le sens : « l'enfant est bien entouré » face à « épuisement parental marqué ». Aucun code ne verra jamais ça.

**Pas une passe sur les 25 pages d'un coup** — des **confrontations ciblées**, deux sections à la fois (Environnement ↔ 7.4, Synthèse ↔ Conclusion, Cartographie ↔ Synthèse, Projet ↔ Feuille de route). C'est là qu'un modèle modeste est fiable, et la trouvaille arrive déjà localisée.

**Pourquoi la phase 3 a quand même été construite après avoir été mise « en réserve ».** Le tic « la mère est en couple avec le père » venait du **prompt**, corrigé à la source le 17/09 — et cette correction reste la bonne, elle ne coûte rien à l'exécution. La couche 3 ne la remplace pas : elle rattrape les **dossiers rédigés avant** la correction, et les fois où le modèle n'obéit pas. Sa couche déterministe ne consulte aucun modèle ; seule la couche de réécriture en appelle un, à la demande.

**Méthode à retenir : quand un tic revient, chercher d'abord d'où il vient.** Le plus souvent, ce n'est pas le modèle qui dérape, c'est la consigne qui le lui demande.

### 6.6 Le sélecteur de moteur

Qwen ou Gemma, retenu dans les réglages (`QualiteMoteur`). Les deux sont en essai. Deux conditions pour que la comparaison vaille quelque chose :

- **Chaque constat porte le nom du moteur qui l'a produit** — sinon on a deux listes sans savoir laquelle vient d'où.
- **Ne rien accepter avant d'avoir lancé les deux** : une correction déjà appliquée change le texte que le second moteur lira, et on le jugerait sur un autre dossier que le premier.

C'est la règle « propose, n'applique jamais » qui rend la comparaison possible.

### 6.7 Ce qui reste ouvert

- **35 pages sur 175 entre 90 et 100 %** de remplissage. Elles tiennent, la repagination les rattrapera si elles basculent, et la phase 1 les signale.
- Les tests de la phase 1 utilisent des mesures simulées (section 36 du TestRunner). Le trajet réel WebView2 ↔ ViewModel a été validé à la main le 17/09.
