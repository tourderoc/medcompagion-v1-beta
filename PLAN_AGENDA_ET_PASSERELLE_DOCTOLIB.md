# Agenda dans Med et passerelle Doctolib

Ouvert le 24/09/2026. **Doctolib reste la source de vérité et le logiciel de prise de rendez-vous. Med ne modifie jamais rien chez lui.**

Deux chantiers indépendants, utiles séparément :

- **A — L'agenda de secours** : Med sait qui vient, même quand Doctolib est inaccessible (rare, mais ça arrive).
- **B — La passerelle GDT** : un clic dans Doctolib ouvre le bon dossier dans Med, et les documents de Med remontent dans Doctolib.

Puis, une fois A en place : **C — la journée assistée**.

## Ce qui a été vérifié le 24/09/2026

**Les voies écartées.** L'API Doctolib est réservée aux éditeurs sous contrat. Les services de synchronisation du marché (Docal, Syncali, Meditrust) font transiter les noms des patients par un tiers, souvent vers Google Agenda, qui n'est pas hébergeur de données de santé — contraire à la ligne « tout en local ». La lecture de l'agenda à l'écran par le modèle vision reste un dernier recours : un horaire mal lu est un rendez-vous manqué.

**L'export CSV existe** (Paramètres → Données → Exports), en CSV ou XLSX, avec un choix « Type de données » et une période. Un export « Agenda » a déjà été produit par le passé (`export_rdv_2025-11-01-2025-11-30.csv`). C'est un fichier figé : pas de flux, donc pas de mise à jour automatique.

**L'export patients** (6 481 lignes le 24/09) donne : `id`, `import_identifier`, `gender`, `last_name`, `maiden_name`, `first_name`, `birthdate`, `email`, `phone_number`, `secondary_phone_number`, `address`, `zipcode`, `city`, `insurance_type`, `crucial_info`, `referrer`, `occupation`, `regular_doctor_name`, `regular_doctor_city`, `notes`, `no_notifications_for_doctor_appointment`. **L'`id` Doctolib est la clé stable** pour relier rendez-vous, patient Doctolib et dossier MedCompanion sans risque d'homonyme.

**L'export Agenda** (`export_rdv_2026-08-01-2026-08-31.csv`, 229 rendez-vous, séparateur `;`) donne 48 colonnes, dont tout ce qu'il faut :

- **identité** : `Doctolib Patient ID`, `Nom du patient`, `Prénom du patient`, `Nom de naissance`, `Date de naissance`, `Téléphone portable`, `Email du patient`, adresse ;
- **créneau** : `Date de début` (`03/08/2026`), `Début` (`09h00`), `Durée du RDV` (minutes), `Agenda`, `Location` ;
- **nature** : `Motif du RDV` (« Enfant - Première consultation de psychiatrie »), `Notes`, `Nouveau patient`, `RDV Internet` ;
- **suivi** : `Statut` — sur août : **Vu** (180), **À venir** (36), **Déplacé** (13) — et `Heure d'arrivée`, `Heure de prise en charge`, `Heure de départ`, horodatées ISO à la seconde. `Heure de départ` est renseignée, `Heure d'arrivée` non (la salle d'attente Doctolib n'est pas utilisée).

**Conséquence pour le chantier C** : « Vu » + `Heure de départ` disent qu'une consultation a bien eu lieu et quand elle s'est terminée. Med peut donc comparer avec ses propres notes sans rien deviner.

**Reste à vérifier** : aucun statut « Annulé » ni « Absent » sur ce mois — soit il n'y en a pas eu, soit ces rendez-vous sont absents de l'export. À confirmer sur un mois qui en contient avant de coder la détection des rendez-vous non honorés.

**Le retour des documents passe par « Dossiers synchronisés »** de l'application de bureau Doctolib (et non par « Rapports d'examen PDF », qui renvoie à cet écran) : Doctolib surveille un dossier local et intègre automatiquement les **PDF et images** qu'on y dépose. **Les sous-dossiers sont ignorés** — Med devra écrire à la racine. Le dossier configuré est `Desktop\Document Doctolib` ; il pointait encore sur `C:\Users\nair\Desktop\` après le déménagement du 21/09 et était donc **introuvable**, chemin à corriger vers `N:\PosteTravail\Desktop\Document Doctolib`. Reste à vérifier si Doctolib rattache seul le document au bon patient ou s'il faut l'affecter à la main (auquel cas on cherchera une convention de nom de fichier).

**L'interface GDT** (Paramètres avancés → Intégration et connexion aux dispositifs → Interface GDT) se configure **par ordinateur** et demande : un nom de fichier, un dossier où Doctolib écrit le fichier GDT-IN, un jeu de caractères (**ISO-8859-1 / CP1252**, pas UTF-8), la **version GDT 02.10**, des codes d'examen optionnels, et une **ligne de commande** facultative (`C:\dossier\app.exe|argument1|argument2`) lancée au moment de la demande. Le retour des documents passe probablement par l'autre type de connexion, « Rapports d'examen PDF » — à vérifier.

## Chantier A — L'agenda de secours

### Concept arrêté avec le médecin le 25/09/2026

**L'agenda de Med ne doit rien coûter.** S'il faut un export pour le tenir à jour, il a échoué. Ce n'est pas un second Doctolib : c'est un filet de secours, et le socle de la gestion de la journée. **Partage des rôles** : Doctolib garde l'agenda de référence, la facturation et les ordonnances sécurisées ; Med prend la main pendant la consultation.

**Le besoin prioritaire, et le seul pour l'instant : remplir l'agenda depuis l'écran.**

- **Vue Semaine** : une capture, Med remplit ou met à jour les six jours. Lecture **colonne par colonne** (une image étroite par jour) — en pleine largeur, les noms sont tronqués (« GIUDICELLI OTTONELLO Ethann », « BARTHELE@ ») et un modèle de vision s'y trompe.
- **Vue Liste du jour** : la lecture précise, dans la disposition trouvée par le médecin (barre latérale repliée, tout tient en une prise, texte grand). Colonnes utiles : horaire, patient, motif, **date de naissance** (la clé pour retrouver le dossier sans homonyme), téléphone, notes. Adresse, code postal et ville ne servent à rien.
- **Ce que Med écrit** : il ajoute et met à jour **sans demander**, mais dans sa copie à lui, jamais dans Doctolib.
- **Ce qu'il ne fait pas** : **supprimer**. Un rendez-vous connu et absent de la capture peut être annulé… ou hors cadre, masqué, mal lu. Il est signalé, pas effacé. Une **ligne barrée**, elle, est un signal net : annulé.
- **Trace** : un court journal en haut de l'agenda (ajouté / modifié / annulé ce matin), pour vérifier d'un coup d'œil.

**Écartés, ne pas reproposer** : le PDF imprimé (Microsoft Print to PDF ne produit aucun texte extractable — 0 lettre lue par PdfPig — et changer de destination d'impression gêne les ordonnances) ; les plages d'absence ; l'arobase et le portrait rouge ; les lignes grisées et le triangle de la vue Liste.

**Mis de côté pour plus tard** : une **carte mentale par patient**, qui remplacerait le coup d'œil d'avant-consultation.

**A0 — La lecture du matin (essai avant tout code).** Idée du médecin le 24/09 : chaque matin, Med lit l'écran Doctolib avec Gemma vision — le mécanisme existe déjà (Doctolib embarqué dans le Bureau, `BureauMedControl.CaptureZone`, `ProcessVisionRequestAsync`) — pour rattraper ce que l'export figé ignore : annulations, ajouts, déplacements du jour.

- **Deuxième lecture, jamais la source.** Le CSV reste la référence. Med **compare et n'affiche que les écarts** ; il ne réécrit pas l'agenda et n'écrit jamais dans un dossier patient à partir d'une image.
- **Vue « Liste » du jour, pas la grille Semaine** : lignes régulières, heure et nom en clair. La grille tronque les noms — le pire cas pour un modèle de vision.
- Un rendez-vous présent dans les deux sources est sûr ; vu seulement à l'écran, il est marqué « à confirmer ». Si l'image est illisible, le modèle doit le dire au lieu de deviner.
- **L'essai décide** : capturer la vue Liste d'une journée, la faire lire, comparer aux lignes du CSV du jour. Si les horaires et les noms ne ressortent pas justes sur une vingtaine de lignes, la voie est abandonnée et rien n'aura été construit dessus.

1. **Stockage local** dans `Documents\MedCompanion\agenda\` (donc sauvegardé sur E:) : un rendez-vous = date, heure, durée, motif, statut, identifiant Doctolib du patient, nom, téléphone.
2. **Import du CSV** par un bouton, avec fusion sur l'identifiant Doctolib : réimporter la même période ne crée pas de doublons et met à jour les statuts.
3. **Vue Jour et Semaine** dans Med, en lecture seule, avec recherche.
4. **L'agenda est l'écran d'accueil du Bureau** (décidé le 24/09). C'est un outil natif, comme l'Atelier d'écriture et les Infographies, mais il s'affiche **par défaut à l'ouverture du mode Bureau**, à la place du message « Sélectionnez un outil », et le Bureau y **retombe quand on quitte un autre outil** ou qu'on libère une application intégrée. Une application embarquée (Firefox, Doctolib) le recouvre tant qu'elle est intégrée : les fenêtres Win32 dessinent par-dessus le WPF. Ouverture sur **la journée**, la semaine à un clic.
5. **Fraîcheur affichée** : « agenda à jour au 24/09 à 12 h », et une alerte au-delà de deux jours. Sans flux, la mise à jour reste un geste manuel — autant le rendre visible plutôt que de faire croire à un agenda vivant.

**Le piège à éviter** : croire Med à jour un jour où Doctolib est tombé. D'où la date de fraîcheur partout, en évidence.

**Fait le 24/09/2026 (points 1 à 4)** : `Models/Agenda/RendezVous.cs`, `Services/Agenda/AgendaService.cs` (lecteur CSV maison : `;`, guillemets doublés, retours à la ligne dans les notes, UTF-8 sans BOM), `ViewModels/Agenda/AgendaViewModel.cs`, `Views/Agenda/AgendaControl.xaml`. Un fichier JSON par mois, fusion sur l'`Id` du rendez-vous. Essai sur l'export d'août : **229 rendez-vous importés, réimport = 0 doublon**.

**Deux enseignements de cet essai, pour la suite :**

- **Les rendez-vous non honorés se repèrent sans statut dédié.** Sur le 03/08, trois rendez-vous passés sont restés « À venir » au lieu de « Vu ». Doctolib n'a pas de statut « Absent » : **un rendez-vous passé encore « À venir » est le signal** à exploiter au chantier C, à confirmer avec le médecin.
**Trois enseignements de la lecture d'écran, mesurés le 26/09/2026 (modèle Qwen3-VL-8B) :**

- **Un modèle de vision ne voit pas un trait de rature.** Sur la vue Liste du 26/09, il lit les 19 lignes sans une faute — noms, horaires, dates de naissance — mais rend `barre: false` pour les deux rendez-vous annulés, même avec une consigne insistante : un trait d'un pixel disparaît quand l'encodeur réduit l'image. **L'analyse des pixels, elle, les trouve tous les deux sans aucun faux positif.** D'où la règle retenue : Med **repère le trait par l'image** (fin, avec des morceaux de lettres juste au-dessus et au-dessous, ce qui le distingue d'une bordure de champ), **découpe la ligne, l'agrandit trois fois et ne fait relire que celle-là** — 1 seconde par ligne. Sur la grille Semaine, la même détection ne trouve que 8 ratures sur 12 (le trait passe au-dessus du seuil sur les blocs colorés) : **les annulations viennent de la vue Liste, pas de la semaine.**
- **Le gris ne veut pas dire annulé.** Dans la vue Liste, les premières lignes de la journée sont pâlies parce qu'elles sont **passées**. Confondre les deux remplirait l'agenda de fausses annulations — la consigne le dit explicitement au modèle.
- **Deux lectures de la même ligne peuvent différer d'une lettre** (`HAMMOUCHI` / `HAMMOUCHE`), et créaient deux rendez-vous au même créneau. Le rapprochement tolère désormais une distance d'édition **au même créneau seulement** ; ailleurs elle confondrait deux patients voisins. La vue Liste fait autorité sur l'orthographe, la grille de la semaine ne peut que compléter — elle tronque.

**Le rapprochement se fait sur la base de Med, décidé le 26/09/2026.** Pas sur l'export Doctolib de 6 481 patients : un rapprochement ne sert que s'il y a un dossier à ouvrir, et les patients dormants n'ajouteraient que des occasions de se tromper. Base réelle : **718 dossiers, 666 datés, aucun homonyme** (même nom et prénom, naissances différentes). Le nom identifie donc déjà à lui seul — **la date de naissance n'est pas la clé, c'est le garde-fou** : elle refuse un rapprochement douteux et départage une troncature.

- **Quatre verdicts**, jamais un seul : *sûr* (nom + naissance) · *à confirmer* (nom seul) · *ambigu* (plusieurs dossiers, Med s'abstient) · *aucun*. Un lien sûr est mémorisé dans `agenda/liens.json` : les lectures suivantes sont immédiates et insensibles aux variantes d'orthographe. Ce fichier remplace l'idée de mémoriser le `Doctolib Patient ID` — la lecture d'écran ne le donne pas.
- **Le repêchage par la date est ce qui fait le travail.** Mesuré sur le samedi 26/09 : 18 des 19 rendez-vous rapprochés avec certitude, dont cinq que le nom seul ne retrouvait pas — `OSENAT MAUREL Logan` → `MAUREL_Logan_OSENAT` (dossier créé dans l'autre ordre), `LAMIM SCHOUAMACHER` → `LAMIM-SCHOUMACHER` (faute de lecture), `KASMI Mohamed` → `KASMI_Mohammed`, `BALDÉ Ismael` → `BALDE_Ismaël`, `CORRETTE Zoe` → `CORRETTE_Zoé`. Sur tout septembre : 243 rendez-vous → 150 sûrs, 40 à confirmer, 52 sans dossier, 1 ambigu.
- **Les jumeaux sont le cas dangereux** : `FRANKE_Chloe` et `FRANKE_Maxime` sont nés le 11/05/2017. Nom et date concordent pour les deux — seul le **prénom exact** tranche, et sans lui Med s'abstient.
- **Ce qui reste sans dossier n'est pas une erreur** : consultations parents (BECCO Virginie, THONNIER Fabienne…), patients sans dossier Med. Med l'affiche (liseré orange, info-bulle) au lieu de forcer un lien. Cas à part : `DI MARTINO Matthis` → le dossier `MATHIS_Di_Martino` existe mais sans date de naissance et avec le nom inversé ; c'est le dossier qui est à corriger, pas l'algorithme.

- **Le rapprochement par le nom ne suffira pas.** Doctolib rend les noms en majuscules sans accents, et les dossiers MedCompanion n'ont pas tous la même convention (`WARCKOL_Héloïse`, `DE-SOUSA-MENDES_Louise`, `BEN HAMADI_Lilia`, `CHAPELLE_Charlyne_CHARLYNE`). Sur 208 patients d'un mois : 139 dossiers retrouvés en comparant tel quel, **169 en comparant sans accents ni séparateurs**. Les 39 restants sont des patients sans dossier Med, ou des variantes d'écriture. **D'où la règle du chantier 2 : rapprocher une fois, à la main, puis mémoriser le `Doctolib Patient ID` dans le dossier patient.** Ensuite le lien est sûr et définitif.

## Chantier B — La passerelle GDT

1. **Med surveille le dossier GDT-IN** (`FileSystemWatcher`), lit le fichier en CP1252, en extrait l'identité du patient (nom, prénom, date de naissance, sexe) et ouvre le dossier correspondant. S'il n'existe pas, Med propose de le créer, prérempli.
2. **Rapprochement** : sur la date de naissance plus le nom, confirmé à la main la première fois, puis mémorisé avec l'identifiant Doctolib.
3. **Med lancé s'il est fermé**, par la ligne de commande GDT ; s'il tourne déjà, c'est la surveillance du dossier qui fait le travail (instance unique).
4. **Retour des documents** : les PDF produits par Med (note, dossier de Restitution, ordonnance) déposés dans le dossier de retour pour être rattachés au patient dans Doctolib. À cadrer après vérification de « Rapports d'examen PDF ».

## Chantier C — La journée assistée (plus tard)

Med suit l'état de chaque rendez-vous (à venir, en cours, terminé, non honoré) et, en fin de journée, rappelle ce qui n'a pas été noté : « la consultation de 14 h n'a pas de note, comment s'est-elle passée ? ». Puis la préparation avant chaque consultation : dernière note, points restés ouverts, bilans attendus, ordonnance à renouveler.

**Deux règles posées dès maintenant :** rien ne s'écrit sans le médecin, et **Med se tait pendant la consultation**, comme le mode silencieux actuel.

## Ordre proposé

A1 à A4 d'abord (utile tout de suite), puis B, puis C. B peut passer avant A si l'export Agenda déçoit.
