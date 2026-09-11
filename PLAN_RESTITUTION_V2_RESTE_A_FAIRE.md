# PLAN — Dossier de Restitution V2 : ce qui reste à faire

> **Statut :** refonte page par page terminée le 11 septembre 2026 (blocs 1 → 32), testée en réel. Quatre chantiers restent ouverts, aucun n'est démarré.
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

**Voix selon le destinataire.** Clinique sobre pour le médecin, voix du livre pour les parents — et sur la dernière page, un ton mixte sans condescendance.

---

## 3. Les quatre chantiers ouverts

### 3.1 Annexe contacts, en toute fin de document

**Demandé il y a longtemps, jamais construit.** C'est le chantier le plus mûr et le moins risqué des quatre.

Une page en fin de dossier, après la conclusion, qui rassemble **qui est qui** : le médecin traitant, et pour chaque bilan cité dans le parcours de soins, le professionnel qui l'a réalisé.

Ce qui existe déjà :
- `PatientMetadata` porte `MedecinTraitantNom`, `Prenom`, `Adresse`, `CodePostal`, `Ville`, `Telephone` — la donnée est là, structurée, il n'y a rien à générer.
- Le parcours de soins et son annexe détaillée (page 6) citent les bilans. Reste à décider si le praticien y est déjà saisissable ou s'il faut un champ.

Questions ouvertes avant de coder :
- Le praticien de chaque bilan : champ structuré dans le bloc antécédents, ou extraction depuis le texte existant ?
- Page numérotée dans le dossier, ou annexe hors numérotation comme la page 6 ?
- Les coordonnées des professionnels à trouver (« professionnel à trouver » dans le projet) : on les laisse vides, ou la page dit explicitement qu'ils restent à identifier ?

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

1. **3.1 Annexe contacts** — autonome, la donnée existe, aucun risque de régression sur l'existant.
2. **3.2 Patch v2 de la Synthèse Globale** — c'est celui qui protège des documents déjà signés.
3. **3.3 Sens de la source de vérité du Projet** — dépend de 3.2, qui fixe le statut de la Synthèse Globale.
4. **3.4 Sphères vs axes** — refonte de présentation, à faire quand le reste est stable.
