# PLAN — Dossier de Restitution V2 : ce qui reste à faire

> **Statut :** refonte page par page terminée le 11 septembre 2026 (blocs 1 → 32), testée en réel. Le chantier 3.1 (annexe contacts) est fait le 16 septembre 2026 ; **trois chantiers restent ouverts.**
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
