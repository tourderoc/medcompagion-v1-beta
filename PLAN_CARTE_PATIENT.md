# Carte du patient

Ouvert le 14/10/2026. **Maquette validée le même jour**, rien de codé. La maquette, faite sur le dossier de Liam CHEDRU, est un fichier local non publié, puisqu'il contient des données de patient.

## Le besoin

Voir un patient **d'un coup d'œil**, sans relire trois notes : qui il est, sa famille, pourquoi il vient, ce qui a été fait, où il en est. C'est un soulagement au quotidien. C'est aussi la mémoire de travail du futur rôle d'**interne** (PLAN_JOURNEE_COMPAGNON.md, étapes 4 et 6).

## Décisions du 14/10/2026

**1. L'affichage :** un **bouton dans le bandeau du patient**, à gauche de « Doctolib ». Il affiche la carte **à la place du dossier bleu**, et un second clic revient au dossier.

**2. La forme :** une page au format du dossier bleu. L'enfant est **au centre** ; les blocs sont autour, reliés à lui par des traits, avec une icône et 2 à 5 lignes chacun. Le **parcours** occupe le bas de la page. Pas de toile d'araignée : la place est comptée.

**3. Les blocs, à place fixe :**

| Bloc | Contenu |
|---|---|
| Centre | Prénom NOM, âge, date de naissance, classe, école, commune |
| 👪 Famille | Parents ensemble / séparés, mode de garde, parents et leur âge, fratrie (nombre, âges), antécédents familiaux |
| 🎯 Motif | Le motif en une ligne, qui a orienté, 1 ou 2 signes marquants |
| 📅 Suivi | Nombre de consultations et de séances, première et dernière date, prochain RDV |
| 🧪 Bilans | Bilans faits (type, date, auteur) ; bilans demandés et pas encore reçus |
| ✉️ Courriers | Courriers faits : destinataire, objet, date |
| 💊 Traitement | Médicaments de la dernière ordonnance Med, posologie, date |
| 🤝 Autour de l'enfant | Intervenants (orthophoniste, psychomotricienne…), médecin traitant |
| 🧭 Parcours 3-11 ans | 1ère consultation → Cartographie → Environnement → Restitution → Bilan semestriel, avec les dates et la prochaine étape |

**4. Ce qui manque s'écrit en gris** (« non renseigné »). C'est ce qu'il reste à demander, et ce sera la matière de l'interne.

**5. Chaque bloc dit d'où il tient ses informations.** Un clic sur un élément ouvre le document source.

**6. L'ordre de recherche, fixé par le médecin : l'admin du dossier bleu d'abord, puis la 1ère consultation, puis les notes.**

## Les sources

| Information | Source | IA ? |
|---|---|---|
| Âge, école, classe, commune | `info_patient/patient.json` (Ecole, Classe, EcoleCommune, DateConfirmationScolarite) | non |
| Parents ensemble / séparés, garde, antécédents familiaux | `info_patient/formulaire_data.json` (SituationFamiliale, GardePrincipale, Tdah, Dyslexie…) | non |
| Intervenants | `info_patient/intervenants.json` | non |
| Bilans faits | `documents/bilans/` (date et titre dans le nom du fichier) | non |
| Bilans demandés | courriers (« Objet : … » contenant bilan, examen, ECG…) et ordonnances `BIO_*` (biologie) | non |
| Courriers | `courriers/*.md` : en-tête YAML, « À l'attention de », « # Objet » | non |
| Médicaments | `ordonnances/MED_*.md` : nom en gras, posologie, date. 348 ordonnances MED dans les dossiers au 14/10 | non |
| Suivi, parcours | notes, séances, frise (`ConsultationModeViewModel.RefreshFriseStages`), registre des passages | non |
| Fratrie, âge des parents, motif, qui a orienté | 1ère consultation (blocs `famille`, `fratrie`, `motif`, `identite`), puis les notes | **Gemma** : pré-remplit, le médecin valide une fois, c'est ensuite figé |

**Ce que Med ne verra pas :** les ordonnances sécurisées, qui ne se font que dans Doctolib, sauf si une note les mentionne. La carte le dit.

**Ce que Med ne gère pas encore :** les courriers ne se font que dans le mode Console, pas encore en mode Consultation. La carte lit les fichiers quel que soit le mode qui les a écrits.

## Les étapes

1. **La carte sans IA.** Le bouton du bandeau, l'affichage à la place du dossier bleu, tous les blocs qui se lisent dans les fichiers. Les blocs qui dépendent de la 1ère consultation affichent « à extraire ».
   > **Codée le 14/10/2026, à valider à l'usage.**
   >
   > **Les fichiers :** `Services/Carte/CartePatientService.cs`, `Models/Carte/CartePatient.cs` et `Views/Consultation/CartePatientControl.xaml`. Le bouton « 🧭 Carte » est dans `MainWindow.xaml`, visible en mode Consultation seulement.
   >
   > **Au-delà du plan :**
   > - La fratrie, le motif et les parents sont repris **tels quels** des blocs structurés de la 1ère consultation (`interrogatoire_blocks_json` dans le YAML de la note), sans IA. Gemma (étape 2) ne servira que pour les dossiers sans 1ère consultation structurée, c'est-à-dire les plus anciens.
   > - Les lignes qui ont un document source l'ouvrent déjà au clic.
   >
   > **Un bilan demandé est reconnu comme reçu** si un document du dossier, postérieur à la demande, porte le mot qui désigne l'examen (cardi, ortho, EEG…). Le mot générique « bilan » ne suffit pas : il faisait passer n'importe quel bilan pour la réponse.
   >
   > **Vérifié sur 4 dossiers :**
   > - Liam : complet.
   > - Nohlan : Risperdal, et l'examen cardiaque reconnu reçu grâce au compte rendu du cardiologue.
   > - Yousra : bilan cardiologique du 18/08 en attente.
   > - Marie Astrid : ancien dossier, presque tout en gris, et c'est juste.
2. **L'extraction par Gemma, avec validation.**
   > **Codée le 14/10/2026, essayée et validée par le médecin le même jour** : les informations proposées correspondent aux notes. Les fichiers sont `Services/Carte/CarteExtractionService.cs` et `Dialogs/CarteCompletionDialog.xaml`. Le bouton « ✨ Compléter avec Med » apparaît sur la carte quand des champs manquent. **Mesuré le 14/10 : 487 dossiers sur 695 n'ont pas de 1ère consultation structurée.**
   >
   > **Ce que reçoit le modèle :** les 3 notes les plus anciennes et les 2 plus récentes, sans leur en-tête YAML, 3 500 caractères au plus par note. Il les lit avec le modèle actif de Med.
   >
   > **Ce qu'il doit rendre :** un JSON où chaque champ porte la date de sa note. La consigne exige de ne rien déduire et de laisser vide ce qui n'est pas écrit.
   >
   > **La validation :** la fenêtre ne montre que les champs absents de la carte. Ce que le médecin valide va dans `info_patient/carte.json`, sans jamais toucher à l'admin. Un champ validé vide n'est plus reproposé.
   >
   > **L'affichage :** les valeurs issues des notes s'affichent « (note du jj/mm/aaaa) », pour qu'une classe de l'an dernier se reconnaisse.
   >
   > Le plan initial prévoyait Gemma aussi pour les dossiers à 1ère consultation structurée. Ce n'est pas nécessaire : l'étape 1 lit déjà leurs blocs. Fratrie, motif, âge des parents : Med pré-remplit depuis la 1ère consultation puis les notes, le médecin valide ou corrige, et c'est enregistré dans le dossier patient. Med ne retouche plus ce qui a été validé.
3. **Les liens vers les sources** (clic : ouvrir la note, le bilan, le courrier, l'ordonnance) et l'agrandissement plein écran (F3).
4. **Plus tard, pour l'interne :** les points ouverts (à revoir, bilans attendus non reçus, ordonnance à renouveler), à partir des mêmes données.
