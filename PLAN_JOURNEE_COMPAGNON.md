# Med compagnon de la journée de travail

Ouvert le 12/10/2026, **rien de codé**. Prolonge le « Chantier C — La journée assistée » de [PLAN_AGENDA_ET_PASSERELLE_DOCTOLIB.md](PLAN_AGENDA_ET_PASSERELLE_DOCTOLIB.md), dont la vision du 26/09 (trois rôles, droit d'interrompre selon le moment) reste valable et n'est pas recopiée ici.

## Le besoin, tel que le médecin l'a posé

Med doit suivre la journée de travail. Aujourd'hui, une consultation peut commencer ou non, se terminer sans aucune note, et personne ne le remarque. Med tient trois rôles :

- **le collègue**, à l'arrivée ;
- **la secrétaire**, pendant les pauses et entre les consultations : « ce patient est-il là ? », « tu n'as rien écrit pour 14 h » ;
- **l'interne**, pendant la consultation : discret, quelques messages ou questions.

Il faut aussi rattraper l'information qui tombe hors de la dictée. C'est souvent la phrase sur le pas de la porte : « de toute façon il est adopté ».

## Décisions arrêtées le 12/10/2026

**1. Deux mémoires séparées, jamais mélangées.**

| | Mémoire de Med — « Registre des passages » | Dossier patient |
|---|---|---|
| Contenu | Qui est venu, quand, état du rendez-vous (vu / absent / excusé / déplacé / sans note) | Tout le clinique |
| Rôle | Savoir **de qui** on parle | Savoir **quoi** |
| Durée | S'efface seule | Permanent |

Quand on lui parle d'un patient, Med le reconnaît grâce au registre, puis va lire le dossier. **Aucun contenu clinique n'entre dans le registre.** Le registre reste une donnée de santé : il est couvert par la sauvegarde des données patients, comme les dossiers.

**2. Oubli automatique : 6 mois sans venue ET sans rendez-vous à venir.** La seule durée ne suffit pas : un patient revu tous les 6 mois disparaîtrait juste avant de revenir.

**3. L'écoute est bornée par la consultation, pas par l'horloge.**
- La dictée et l'extraction gardent leur chemin habituel, rien ne change.
- **Après l'extraction, le micro reste ouvert** jusqu'à la fermeture du dossier ou l'ouverture du patient suivant.
- À la fermeture, Med ne retient que les **faits nouveaux, absents de la note**. Il propose chaque fait avec la phrase entendue entre guillemets, et le médecin répond oui ou non. La citation sert à juger les erreurs de Whisper et les erreurs d'attribution (le parent parle-t-il de l'enfant ou de la sœur ?).
- **Aucun audio n'est conservé.** La transcription est effacée une fois les propositions traitées.
- Hors consultation, le micro ne s'ouvre que sur demande. L'écoute permanente toute la journée reste écartée tant que ce déclenchement suffit (règle du 26/09).

**4. Le découpage se fait par consultation, pas par tranches fixes de 30 min.** Le cache KV n'est pas un problème : Whisper transcrit au fil de l'eau en texte, et le LLM ne lit ce texte qu'une seule fois, à la fermeture. Trente minutes de parole font environ 8 000 tokens, ce qui est confortable pour Qwen comme pour Gemma. Une tranche fixe couperait une consultation en deux et perdrait le contexte. Les bornes viennent de l'agenda et de l'ouverture ou la fermeture du dossier.

**5. Information des familles : une pancarte en salle d'attente** indique que la consultation est susceptible d'être enregistrée. Elle est en place selon le médecin.

**6. Règles reprises du 26/09 :**
- le moment décide si Med a le droit de parler, jamais l'importance de ce qu'il a trouvé ;
- rien ne s'écrit sans le médecin ;
- seulement ce sur quoi le médecin peut agir ;
- les règles décident, le LLM formule.

## Ce qui existe déjà

- **Agenda** : `Models/Agenda/RendezVous.cs`, stocké dans `agenda/<aaaa-mm>.json`. Le fichier porte déjà `Statut` (tel que Doctolib l'écrit), `HeureDepart` (fin réelle relevée par Doctolib, donc la preuve qu'une consultation a eu lieu), ainsi que `Dossier` et `Certitude` pour le rapprochement.
- **Rapprochement agenda → dossier** : `Services/Agenda/AgendaRapprochementService.cs`, avec trois verdicts. Med ne devine pas.
- **Événements de dossier** : `CreerEvenementDossierAsync` dans `ConsultationModeViewModel` (absent, excusé, RDV déplacé, note de liaison).
- **Mémoire de Med** : `Models/MedMemoryBlock.cs` et `Services/MedMemoryService.cs`. Il y a trois blocs de **texte libre** injectés dans le prompt. Le registre ne sera pas un quatrième bloc de texte : ce sont des **données structurées**, seulement affichées sous forme de bloc.
- **Dictée** : `Services/Consultation/WhisperStreamingService.cs`, avec capture continue, `StartAsync` / `StopAsync` et l'événement `SegmentReady`. Whisper reste chargé en permanence depuis le 26/09.

## Les étapes

### Étape 1 — Le fil de la journée et le registre des passages
C'est la base de tout le reste.

> **Codée le 12/10/2026, à valider à l'usage.**
>
> - `Services/Journee/JourneeService.cs` et `Models/Journee/FilDuJour.cs`.
> - Les états sont **recalculés depuis le disque** à chaque lecture. Une note écrite le soir corrige d'elle-même la consultation de 14 h.
>
> **Les preuves, par ordre de priorité :**
> 1. événement absent, excusé ou déplacé ;
> 2. note ou séance datée du jour (la date vient du nom du fichier) ;
> 3. fichier écrit dans le dossier pendant la fenêtre, ou dictée signalée (`activites_<date>.json`), ou Doctolib « Vu » / heure de départ ;
> 4. le statut Doctolib.
>
> **Ce qui ne compte pas comme preuve :**
> - les écritures dans `chat/`, `info_patient/`, `synthese/` et `messages/` ;
> - les fichiers `.json`, `.bak` et `.tmp`.
>
> **Le rattachement au dossier :** les rendez-vous importés par CSV n'étaient jamais rapprochés de leur dossier. Le fil les rapproche **en mémoire**, sans réécrire l'agenda, et seulement si le lien est sûr. Pour aujourd'hui, le rapprochement est enregistré dans l'agenda, toutes les 2 min au plus.
>
> **Le registre :** `Documents/MedCompanion/journee/registre_passages.json`, couvert par la sauvegarde. Il ne garde que des faits (venu, absent, excusé, déplacé), jamais « aucune trace ». À la première ouverture, il reprend tout l'agenda depuis août : 329 patients en 1 s.
>
> **L'affichage :**
> - une pastille par rendez-vous dans l'agenda, avec les preuves dans l'info-bulle ;
> - une ligne « Fil du jour » sous la fraîcheur, actualisée toutes les 3 min ;
> - le bloc « Registre des passages » dans la Mémoire de Med (Focus Med).
>
> **Mesuré sur la journée du 12/10 :** 26 rendez-vous ; 15 terminés avec note ; 4 sans aucune trace, vérifiés (dossiers réellement vides ce jour-là) ; 2 sans dossier sûr (Mina Cherif, et les parents Ben Hamouda) ; 5 à venir. Sur le 09/10, les 4 « venu, pas de note » sont justes : courrier, ordonnance ou import faits, mais aucune note.
- Chaque rendez-vous du jour reçoit un état Med : à venir → arrivé → en cours → terminé (avec note / **sans note**), ou absent / excusé / déplacé.
- L'état se déduit des gestes déjà faits : ouverture du dossier, « Débuter », enregistrement de la note, boutons d'événement, `HeureDepart` Doctolib.
- Le registre est un fichier JSON dans le dossier MedCompanion, avec une ligne par patient : dernière venue, prochain rendez-vous, dernier état. Il est purgé selon la règle des 6 mois. C'est le 4e bloc « Registre des passages » de l'écran d'accueil, en lecture seule.
- **Fini quand :** en fin de journée, chaque rendez-vous a un état juste, sans que le médecin ait rien saisi de plus qu'aujourd'hui.

### Étape 2 — La secrétaire
- Un bandeau entre les consultations : « 14 h — Théo : pas de note », « 15 h — pas ouvert : absent ? ». Chaque ligne a ses boutons : écrire la note / absent / excusé / déplacé.
- Le soir, un récapitulatif de ce qui reste ouvert.
- Le bandeau est muet s'il n'y a rien. **Med ne dit pas « pas de note »** quand le lien entre le rendez-vous et le dossier n'est pas sûr (`Certitude` ≠ « sur ») : il dit « dossier à confirmer ».

### Étape 3 — L'écoute de fin de consultation
- Le micro reste ouvert après l'extraction. À la fermeture, Med propose les faits nouveaux avec leur citation, le médecin valide, puis la transcription est effacée.
- Un **interrupteur « pas d'écoute » par patient** est prévu pour une famille qui refuse.
- Le même principe pourra servir plus tard pour les **appels de parents**, versés en note de liaison.

### Étape 4 — L'interne : préparation et fin de consultation
- **À l'ouverture du dossier** : dernière note, points restés ouverts, bilans attendus, ordonnance qui arrive à échéance.
- **À la fermeture** : une checklist courte (attestation ? ordonnance ? prochain rendez-vous ?).

### Étape 5 — Le collègue du matin
- Il raconte **la journée du médecin** à partir du fil et du registre : nombre de rendez-vous, nouveaux patients, annulations depuis la veille, notes manquantes d'hier.
- Les faits viennent du code, le ton vient du LLM. C'est dans ces conversations que se rempliront « Mon cadre de travail » et « Personnalité de Med ».

### Étape 6 — L'interne pendant la consultation
- De rares messages, préparés à l'avance, limités à 2 ou 3, affichés dans la zone Med, sans son ni fenêtre qui s'ouvre.
- Cette étape vient en dernier parce qu'elle a besoin des étapes 1, 3 et 4, et parce que c'est la plus risquée pour la concentration.

## Présence du patient — décidé le 12/10/2026

**Une dictée ou une note écrite dans le dossier d'un patient attendu, autour de l'heure de son rendez-vous, signifie qu'il est là.** La simple ouverture du dossier ne compte pas, car on ouvre souvent un dossier pour préparer ou relire. Le médecin n'a aucun geste à ajouter.

| Situation | Conclusion de Med |
|---|---|
| Dictée ou note dans le dossier, de 15 min avant à 45 min après le début du RDV | Présent, consultation **en cours** (la fenêtre tolère le retard) |
| Note écrite après la fenêtre (le soir) | Note rattrapée : **terminée avec note**, pas « en cours » |
| Activité dans le dossier d'un patient sans RDV ce jour | **Hors agenda** (appel, urgence, venue imprévue) : la secrétaire demande ce que c'était |
| Bouton « Patient absent / excusé / déplacé » | Le clic l'emporte sur tout |
| Fin de fenêtre sans aucune activité | La secrétaire demande : « absent ? » |
| `HeureDepart` relevée par Doctolib | Confirmation après coup |

Les bornes (−15 / +45 min) sont à ajuster à l'usage. Ce sont des règles, pas un modèle.

## Questions ouvertes

1. ~~Comment Med sait-il que le patient est arrivé ?~~ Tranché, voir ci-dessus.
2. **L'interne pendant la consultation :** rappels de dossier seulement (bilan, ordonnance, point resté ouvert), ou aussi des questions cliniques à poser ? Le niveau de risque n'est pas le même. À trancher avant l'étape 6.
3. **La fraîcheur de l'agenda :** tout dépend d'un agenda à jour chaque matin. Il faut vérifier à l'usage que la lecture d'écran Doctolib suffit, ou bien passer par la passerelle GDT (Chantier B).
4. **Les consultations de parents seuls et les nouveaux patients sans dossier :** leur place dans le fil et dans le registre reste à définir.

## Au passage

L'écran d'accueil affiche encore « Ollama – huihui_ai/gemma-4-abliterated:e4b » dans le bloc Assistant IA, alors que Med tourne sur llama.cpp. C'est à nettoyer avec le retrait d'Ollama (PLAN_MED_100_LOCAL.md).
