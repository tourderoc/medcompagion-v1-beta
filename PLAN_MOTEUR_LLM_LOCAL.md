# PLAN — Moteur LLM local : modèles sur SSD, deux cartes, voyant fidèle, switch optimisé

> **Statut :** plan validé dans son principe le 14 septembre 2026. Étapes 1 à 4, 6 et 7 faites et validées (7 le 15/09 : bascules de 4,6 à 5,5 s) ; restent l'étape 5 (mesure RAM) et l'étape 8 (retrait d'Ollama). Point ouvert : le garde-fou de la pré-lecture écarte Qwen en pleine journée.
> **Date d'ouverture :** 14 septembre 2026
> **Méthode :** une étape à la fois, validée en réel avant de passer à la suivante.
> **Déclencheurs :** RAM saturée à 100 % pendant une génération (12/09), voyant vert qui ne correspond pas à l'état du modèle, switch Qwen ↔ Gemma lent, modèles présents en trois copies.
> **Docs liés :** [SETUP_WHISPER_GPU.md](SETUP_WHISPER_GPU.md), [PLAN_RESTITUTION_V2_RESTE_A_FAIRE.md](PLAN_RESTITUTION_V2_RESTE_A_FAIRE.md), [CLAUDE.md](CLAUDE.md)

---

## 1. La cible

Décisions du médecin, 14 septembre 2026 :

1. **Med tourne uniquement sur llama.cpp.** Ollama sera retiré à moyen terme.
2. **Deux cartes, deux rôles, aucune bascule entre elles :**
   - RTX 3050 (6 Go) → affichage + Whisper large-v3 (dictée) ;
   - RTX 5060 Ti (16 Go) → les LLM de Med, exclusivement.
3. **Deux modèles, pas plus :**
   - **Qwen3.8-27B** → synthèses et tâches qui demandent du raisonnement ;
   - **Gemma 4 12B QAT + MTP** → tout le reste, et la vision.
4. **Med s'ouvre toujours sur Gemma 4 12B QAT + MTP.**
5. **Les trois modèles (Qwen, Gemma, Whisper large) sont sur le SSD**, dans une partition qui leur est réservée. Med ne va jamais les chercher ailleurs.
6. **Le voyant traduit fidèlement l'état réel du modèle.** Le service de warm-up est retiré.
7. **Garde-fou mémoire :** rien n'est anticipé en mémoire si la RAM *utilisée* dépasse 12 Go.
8. **Le modèle reste chargé bien plus de 5 minutes au repos.**

---

## 2. État des lieux (relevé le 14/09/2026)

### Où sont les fichiers

| Élément | Emplacement lu par Med | Disque |
|---|---|---|
| Qwen, Gemma QAT + MTP | `C:\llamacpp-models` (réglage `LlamaCppModelsDir`) | SSD ✅ |
| Whisper large-v3 (2,9 Go) | `C:\Users\nair\AppData\Roaming\MedCompanion\models` — **codé en dur** dans `WhisperModelManager` | SSD ✅ |
| `llama-server.exe` | `C:\Users\nair\llama.cpp\build\bin\Release` | SSD ✅ |
| Fichier d'échange | `D:\pagefile.sys`, 29,7 Go | **HDD** ⚠️ |
| Modèles Ollama | `D:\PosteTravail\.ollama\models` | HDD |

**Rien n'est chargé depuis un disque mécanique** : confirmé par les lignes de commande relevées pendant les traces.

Mais les GGUF existent en **trois copies de tailles identiques** :

| Copie | Taille | Rôle |
|---|---|---|
| `C:\llamacpp-models` | 27,2 Go | utilisée par Med |
| `C:\Users\nair\llamacpp-models` | 27,6 Go | **inutilisée, sur le SSD** — et c'est le dossier de repli codé en dur (`ModelsDirParDefaut`) si le réglage se vide |
| `D:\PosteTravail\llamacpp-models` | 27 Go | secours froid, à conserver |

### Le SSD

WD Green 240 Go, SATA, sans DRAM. Une seule partition utile : C: (223 Go, **71 Go libres**), plus F: (579 Mo, système). **Aucun espace non alloué.**

### Les cartes

- `LlamaCppGpuUuid` → 5060 Ti ✅
- `WhisperGpuDevice = 1` → 3050 ✅, vérifié empiriquement le 11/09/2026. **Piège :** l'ordre CUDA (1 = 3050) est l'inverse de celui de `nvidia-smi` (1 = 5060 Ti). Un numéro est fragile.

### Le voyant et le warm-up

Au démarrage, **deux chemins chargent le modèle en parallèle** :

- **le sélecteur** : `SelectCurrentModel()` pose la sélection **sans le garde `_syncSelecteurModele`**, ce qui déclenche une vraie bascule, comme un clic ;
- **`LLMWarmupService`**, lancé juste après en tâche de fond.

Les deux écrivent la couleur du voyant : le dernier qui finit gagne.

Ce que fait réellement le warm-up :

- **llama.cpp** : rien. `CheckConnectionAsync` démarre déjà le serveur, puis `WarmupAsync` rappelle la même fonction, qui rend la main aussitôt.
- **Ollama** : il génère une réponse complète à « Bonjour », **sans limite de tokens** et **sans le contexte des vrais appels** (65 536). Ollama doit donc recharger le modèle au premier vrai appel : le préchauffage est perdu.

Une fois vert, **le voyant ne bouge plus**. Ces événements changent l'état réel sans le mettre à jour : veille de 5 min, déchargement avant dictée, bascule vers le modèle vision, arrêt depuis Pilotage, rechargement silencieux au premier appel, déchargement par Ollama lui-même.

Autre fragilité : le profil llama.cpp par défaut dans le code est **Qwen**, et l'initialisation ne pose pas le profil enregistré. Seule la bascule du sélecteur le fait.

### Le schéma de switch existant

`EtapeModeleService` + `%APPDATA%\MedCompanion\etapes-modeles.json` affectent déjà un modèle à chaque étape de consultation :

| Phase | Étapes, dans l'ordre | Bascules |
|---|---|---|
| 1er entretien | Extraction **Gemma** → passe qualité (hérite) → suggestions **Qwen** → synthèse **Qwen** → restitution **Qwen** | 1 |
| Cartographie | Synthèse **Gemma** | 0 |
| Environnement | Orientation **Qwen** → évaluation ciblée **Qwen** → synthèse séance **Qwen** | 0 |
| Suivi | Extraction **Gemma** | 0 |

**Trou :** le schéma ne couvre que le mode consultation. Le dossier de restitution, la synthèse patient, les courriers et le chat utilisent le modèle sélectionné à la main.

---

## 3. Mesures de référence (12/09/2026)

Base factuelle du plan. À refaire après chaque étape qui touche au moteur.

| Mesure | Résultat |
|---|---|
| Chargement Qwen, fichier lu **depuis le disque** | **70 s** (13,5 Go à ~190 Mo/s) |
| Chargement Qwen, fichier **en cache Windows** | **4,6 s** |
| Chargement Gemma 12B | 3,5 s |
| Retour sur Qwen juste après Gemma | 4,6 s — les deux tiennent en cache |
| VRAM Qwen, contexte 32 768 | 14 611 Mo |
| VRAM Gemma, contexte 131 072 / 32 768 | 8 955 Mo / 7 861 Mo |
| Libération de la VRAM après arrêt du serveur | 239 ms |
| RAM réellement utilisée par llama-server (`--no-mmap`) | ~1,3 Go — contre 12,9 Go sans ce drapeau |
| Mémoire « validée » par llama-server avec Qwen | ~17 Go (adossement de la VRAM, pas de la RAM occupée) |
| Qwen, bloc « 7.1 Prise en charge médicale », avec réflexion | 52 s, 2 642 tokens pour un plafond à 2 900 |
| Même bloc sans réflexion | 14,5 s, mais 2 règles de la consigne enfreintes |

---

## 4. Principes acquis — à ne pas défaire

- **`--no-mmap` reste.** Il divise par dix la RAM réellement occupée par le serveur.
- **Jamais deux llama-server en même temps**, jamais un modèle réparti sur deux cartes.
- **Jamais de second modèle chargé « en RAM »** pour accélérer le switch. llama.cpp ne sait pas faire glisser un modèle de la VRAM vers la RAM : ce serait un second serveur sur le processeur, ~13,5 Go réellement occupés. Le bon mécanisme est le cache fichier de Windows, rendu instantanément sous pression mémoire.
- **Le seuil des 12 Go porte sur la RAM « utilisée »**, jamais sur la mémoire « validée », qui est gonflée par la VRAM.
- **La limite de mémoire validée ne doit pas baisser** (aujourd'hui 62 Go = RAM + fichier d'échange). L'épisode du 12/09 est monté à 66 Go validés.
- **Mesurer avant d'optimiser.**

### Déjà fait le 12/09/2026 (dans l'arbre de travail, non commité)

- Réflexion coupée quand aucun niveau n'est demandé ; prose coupée au plafond rendue au lieu d'une erreur ; JSON tronqué traité en erreur, avec une reprise sans réflexion.
- Réserve de réflexion portée de 2 000 à 4 000 tokens.
- Déchargement des modèles Ollama avant tout démarrage de llama-server, avec attente de libération effective.
- Verrou sur le port 8899 avant démarrage.
- `ForceKill` attend la mort du serveur, et non la fin de `taskkill`.
- Un timeout de chargement tue le serveur au lieu de l'abandonner vivant.

---

## 5. Les étapes

### Étape 1 — Ranger le SSD

*Sans code, sans risque.* — **Statut : ✅ fait et validé dans Med le 14/09/2026** (ouverture sur Gemma QAT, bascule Qwen, lecture vision d'un formulaire, dictée large-v3)

> **Réalisé :** empreintes SHA-256 de la copie `D:` identiques pour les 7 GGUF ; réglage `OllamaModel` passé de `gemma4:12b` (Gemma *standard*, dont le fichier allait disparaître) à `gemma4-qat-mtp`, ancienne version gardée dans `appsettings.avant-etape1-20260914.json` ; suppressions effectuées. Restent dans `C:\llamacpp-models` : Qwen, Gemma QAT, MTP, projecteur vision QAT ; côté Whisper : `ggml-large-v3.bin`.
>
> **Écart constaté :** seulement **+9,4 Go** libérés (C: 71,2 → 80,6 Go libres) au lieu des ~37 Go attendus. Explication la plus probable : les deux dossiers `C:\llamacpp-models` et `C:\Users\nair\llamacpp-models` étaient des **liens physiques vers les mêmes données** — la « copie » n'occupait donc pas d'espace propre. Les 9,4 Go correspondent exactement aux fichiers qui n'existaient qu'une fois (Gemma standard, projecteurs vision, MTP q8, Whisper medium). Non vérifiable a posteriori : les liens sont supprimés.

1. Avant toute suppression, vérifier l'empreinte SHA-256 de la copie `D:` sur Qwen et Gemma QAT (les tailles sont déjà identiques).
2. Supprimer la copie inutilisée `C:\Users\nair\llamacpp-models` (27,6 Go, dont la variante MTP q8).
3. Dans `C:\llamacpp-models`, retirer ce qui ne sert plus :
   - `gemma4-12b.gguf` — Gemma standard, 7,4 Go ;
   - `gemma4-12b-mmproj.gguf` — vision du Gemma standard ;
   - `mmproj-F16.gguf` — vision de Qwen, jamais utilisée, 0,9 Go.
4. Retirer `ggml-medium.bin` (Whisper medium, 1,4 Go).

**On garde :** `Qwen3.8-27B-IQ4_XS.gguf`, `gemma4-12b-qat.gguf`, `gemma4-12b-qat-mtp.gguf`, `gemma4-12b-qat-mmproj.gguf` (vision Gemma), `ggml-large-v3.bin`.

**Transitoire accepté :** le Gemma standard apparaîtra « fichier absent » dans le sélecteur jusqu'à l'étape 3.

**Validation :** Med démarre ; Gemma QAT et Qwen se chargent ; la lecture vision d'un formulaire fonctionne ; C: a ~100 Go libres.

### Étape 2 — Partition dédiée aux modèles

*Opération manuelle, en administrateur.* — **Statut : ✅ fait le 14/09/2026**

> **Réalisé :** C: réduit de 48 Go (174,9 Go, 25,1 Go libres) ; M: « MODELES_IA » créé sur le SSD (48,1 Go, NTFS) ; fichier d'échange C: 8–16 Go + D: géré par le système ; limite de mémoire validée **68,5 Go** (62,4 avant) ; TRIM actif. Modèles copiés vers `M:\llm` (Qwen, Gemma QAT, MTP, projecteur vision QAT) et `M:\whisper` (large-v3) : **5 empreintes SHA-256 identiques** aux originaux. M: : 25,9 Go libres. Les originaux restent sur C: jusqu'à la validation de l'étape 3.

1. Prérequis : étape 1 terminée, point de restauration Windows, données patients sauvegardées sur E:.
2. Gestion des disques → C: → **Réduire le volume de 48 Go** (49 152 Mo).
3. Nouveau volume simple, NTFS, lettre **M:**, nom **MODELES_IA**.
4. Créer `M:\llm\` et `M:\whisper\`, puis **copier** (sans déplacer) les modèles gardés. Les anciens emplacements restent en place jusqu'à la validation de l'étape 3.
5. Fichier d'échange : **taille fixée sur C: (SSD), 8 à 16 Go**, en **conservant celui de D:** géré par le système, en secours. Objectif : servir les pics courants depuis le SSD sans réduire la limite de mémoire validée. Taille bornée plutôt que « gérée par le système » sur C: : après la réduction, C: n'a plus qu'une trentaine de Go libres, qu'un fichier d'échange automatique pourrait dévorer.
6. Vérifier que TRIM est actif : `fsutil behavior query DisableDeleteNotify` doit renvoyer 0.

**À savoir :** aucun gain de vitesse attendu. C'est le même SSD SATA, le goulot de ~190 Mo/s reste. Le bénéfice : un emplacement unique et isolé, que ni Windows ni les modèles ne peuvent déborder.

**Validation :** M: fait 48 Go ; les fichiers copiés ont les mêmes tailles et empreintes ; la limite de mémoire validée n'a pas baissé.

### Étape 3 — Med ne lit que M:

*Code.* — **Statut : ✅ fait et validé dans Med le 14/09/2026**

> **Réalisé :** réglages `LlamaCppModelsDir = M:\llm` et nouveau `WhisperModelsDir = M:\whisper` (ancienne version : `appsettings.avant-etape3-20260914.json`) ; repli codé en dur supprimé (`ModelsDirParDefaut`) ; dossier non renseigné → profils non chargeables et message explicite dans `EnsureRunningAsync` ; profil Gemma standard et projecteur vision de Qwen retirés du code. Contre-épreuve : anciens dossiers de C: renommés, les 4 tests passent (Gemma QAT, bascule Qwen, vision, dictée), journal serveur sur `M:\llm\gemma4-12b-qat.gguf` + `M:\llm\gemma4-12b-qat-mmproj.gguf`. Whisper large-v3 sauvegardé sur `D:\PosteTravail\llamacpp-models\whisper` (empreinte identique), puis anciens dossiers supprimés : **+22,1 Go sur C: (47,5 Go libres)**.
>
> **Pris en avance sur l'étape 6 (point 3) :** Med s'ouvre toujours sur Gemma QAT. Au premier test, il rouvrait sur Qwen : dernier modèle utilisé mémorisé, **et** profil par défaut du code à Qwen (`LlamaCppServerManager.CurrentProfile`) que le warm-up démarre sans lire le réglage. Les deux sont forcés sur Gemma QAT dans le constructeur de `MainWindow`, et le défaut du code est passé à Gemma QAT. **✅ Validé le 14/09/2026 : Med s'ouvre sur Gemma 4 12B QAT + MTP.**
>
> **À mesurer :** le médecin constate des chargements nettement plus rapides. Prudence avant d'en créditer la partition — c'est le même SSD, et les fichiers venaient d'être lus deux fois (copie + empreintes), donc ils étaient dans le cache Windows. Refaire la mesure après un redémarrage du PC (cache vide) pour trancher.

1. `LlamaCppModelsDir` → `M:\llm` (le réglage existe).
2. Rendre le dossier des modèles Whisper réglable, pointé sur `M:\whisper`.
3. **Supprimer le repli silencieux** `ModelsDirParDefaut`. Un modèle introuvable produit un message explicite, jamais une recherche ailleurs.
4. Retirer du code le profil Gemma standard et le projecteur vision de Qwen.

**Validation :**
- les lignes de commande de llama-server pointent sur `M:\llm` ;
- le journal Whisper pointe sur `M:\whisper` ;
- l'ancien dossier `C:\llamacpp-models`, **renommé temporairement**, ne manque à rien.

Puis suppression des anciens emplacements sur C:.

### Étape 4 — Deux cartes, zéro bascule

*Code + vérification.* — **Statut : ✅ fait et validé le 14/09/2026**

> **Constat avant modification (dictée mesurée) :** Whisper bien sur la 3050 (~3,6 Go, 5060 Ti intacte), mais **deux déchargements croisés** hérités de l'époque d'une carte unique : le LLM tué au démarrage de chaque dictée (`ConsultationModeViewModel`, `StartRecordingCommand`), et Whisper déchargé immédiatement à chaque arrêt (`WhisperStreamingService.StopAsync`). Conséquence : l'extraction qui suit une dictée payait un rechargement complet du LLM, et chaque reprise de dictée relisait Whisper.
>
> **Réalisé :** les deux déchargements supprimés ; veille de Whisper portée de 5 min à **2 h** (`IdleUnloadTimeout`), seul déchargement restant ; nouveau réglage `WhisperGpuUuid` (3050) qui prime sur `WhisperGpuDevice` : Med pose `CUDA_DEVICE_ORDER=PCI_BUS_ID` pour son propre processus (constructeur statique de `WhisperStreamingService`) et retrouve le numéro via `nvidia-smi`. Ancienne version des réglages : `appsettings.avant-etape4-20260914.json`.
>
> **Validation (trace des deux cartes, 13:39–13:43) :** llama-server **même processus du début à la fin** (dictée, arrêt, extraction, 2e dictée), 5060 Ti stable ~9,1 Go ; 3050 stable ~3,97 Go entre les dictées ; seule la 3050 travaille pendant la transcription ; extraction à 91 % sans rechargement ; les deux cartes libérées à la fermeture de Med.

1. Désigner la carte de Whisper de façon stable (à partir de l'UUID de la 3050, `GPU-8e95679b-…`) plutôt que par un numéro dont l'ordre s'inverse selon l'outil.
2. Supprimer le déchargement du LLM avant la dictée (`ConsultationModeViewModel`, autour de la ligne 7978) : il n'a plus de raison d'être.
3. Évaluer le maintien de Whisper large-v3 chargé tant que Med est ouvert, plutôt que relu à chaque dictée. La décision dépend de sa part dans la RAM de l'application : voir étape 5.

**Validation :** dictée pendant qu'un LLM est chargé. La 3050 monte d'environ 3 Go, la 5060 Ti ne bouge pas, et aucun rechargement du LLM après la dictée.

### Étape 5 — Mesurer la RAM en conditions réelles

*Mesure, pas de modification de Med.* — **Statut : à faire**

1. Enregistreur en tâche de fond, toutes les 2 s :
   - RAM utilisée, validée, en cache ;
   - par process : MedCompanion, llama-server, msedgewebview2, Doctolib, navigateurs ;
   - classement complet des consommateurs dès que la RAM disponible passe sous 4 Go.
2. **Une journée de consultations réelle**, en notant l'heure des étapes marquantes (dictée, synthèse, restitution, lecture vision).
3. Suspects à départager :
   - Whisper large-v3 dans la mémoire de l'application ;
   - MedCompanion au fil d'une longue consultation ;
   - le rendu des PDF en images pour la vision ;
   - les aperçus WebView2 du dossier de restitution.

**Déjà écartés :** llama-server (~1 Go utilisé), la génération de synthèse patient (≤ 0,24 Mo de texte par dossier), MedCompanion au repos (~200 Mo).

**Non expliqué :** l'épisode du 12/09 — 30,4 Go utilisés, 66,3 Go validés, 73 Mo disponibles.

**Livrable :** le ou les coupables chiffrés, et un correctif ciblé — étape à part si nécessaire.

### Étape 6 — Voyant fidèle, sans warm-up, ouverture sur Gemma

*Code.* — **Statut : ✅ fait et validé dans Med le 14/09/2026.** Reste mineur : exposer `LlamaCppIdleUnloadMinutes` dans Pilotage → Moteur local (aujourd'hui dans appsettings.json), et tester le déchargement au repos avec un délai court.

> **Réalisé :**
> - `LLMWarmupService` supprimé (fichier, champ, réglages `EnableAutoWarmup` / `WarmupTimeoutSeconds`).
> - Un seul chemin de démarrage : `ChargerModeleOuvertureAsync` (une bascule explicite vers le modèle mémorisé), lancé avant le remplissage du sélecteur ; `SelectCurrentModel` pose la sélection sous le garde `_syncSelecteurModele` et ne déclenche plus de bascule (ni au démarrage, ni en retour après échec).
> - `LlamaCppServerManager` publie l'état (`EtatMoteurLlm` : Arrêté, Chargement, Prêt, Génère, Erreur + `EtatMessage`, événement `EtatChange`) : chargement publié avant les préparatifs, prêt/erreur à l'issue du démarrage, arrêté dans `StopInternal`, génère via `SuivreRequete()` autour des trois envois du provider (chat, flux, image), erreur si le process meurt hors arrêt demandé (`Process.Exited`).
> - Voyant : un seul endroit, `AfficherEtatMoteur` (MainWindow.LLM.cs) — gris / orange / vert / vert pulsé / rouge, infobulle sur le modèle **réellement chargé** (`RunningProfile`, mention « mode vision », contexte). Les quatre écritures manuelles (warm-up, bascule, bouton décharger, paramètres) retirées. Ollama/OpenAI : résultat de la dernière bascule.
> - Déchargement au repos : 5 min → **2 h**, réglage `LlamaCppIdleUnloadMinutes` (0 = jamais), mesuré depuis la fin de la dernière requête et jamais pendant une génération. Réglage **non encore exposé dans Pilotage** (appsettings.json seulement).
> - Surveillance minute par minute au repos : deux `/health` consécutifs sans réponse (cas du réveil de mise en veille) → serveur arrêté et voyant rouge avec explication ; redémarrage au prochain appel.

1. **Retirer `LLMWarmupService`.**
2. **Un seul chemin de démarrage** : poser la sélection initiale sous le garde `_syncSelecteurModele`.
3. **Ouverture toujours sur Gemma 4 12B QAT + MTP**, quel que soit le dernier modèle utilisé. Aujourd'hui : dernier modèle utilisé (réglage actuel `gemma4:12b`, soit le Gemma *standard*), et Qwen par défaut dans le code.
4. **États publiés par le moteur lui-même**, que le voyant se contente d'afficher :

   | État | Voyant |
   |---|---|
   | Arrêté / déchargé (se rechargera au prochain appel) | gris |
   | Chargement | orange |
   | Prêt, en VRAM | vert |
   | En train de générer | pulsation |
   | Erreur, serveur mort | rouge |

   L'infobulle affiche le modèle **réellement chargé** (`RunningProfile`), son contexte et la carte. Plus aucun des quatre endroits qui écrivent la couleur à la main aujourd'hui (warm-up, bascule, bouton décharger, paramètres).
5. **Déchargement au repos :** la veille de 5 min est remplacée par **aucun déchargement tant que Med est ouvert**, avec un **filet de sécurité à 2 h** d'inactivité, réglable dans Pilotage → Moteur local. Déchargement aussi à la fermeture de Med et par le bouton.
6. Détecter la perte du serveur au réveil d'une mise en veille du PC.

**Validation, scénario par scénario :**
- ouverture à froid : vert seulement quand le serveur répond ;
- switch Qwen ↔ Gemma ;
- lecture vision d'un formulaire ;
- arrêt depuis Pilotage ;
- filet de sécurité, testé avec un délai court ;
- llama-server tué à la main : le voyant le montre.

### Étape 7 — Optimiser le switch Qwen ↔ Gemma

*Code + mesures.* — **Statut : ✅ fait et validé le 15/09/2026** — bascules de **4,6 à 5,5 s** dans les deux sens, fichiers en cache. Reste ouvert : le garde-fou mémoire, qui écarte Qwen en pleine journée (voir « Réalisé le 15/09 »).

> **Simplifiée le 14/09/2026 sur proposition du médecin.** Deux modèles seulement, et ce sera le cas « pour l'instant » : « l'autre » modèle est toujours connu. Règle unique — **quand l'un est chargé sur la carte, l'autre est gardé prêt dans le cache Windows.** L'anticipation par le schéma de consultation est abandonnée : elle n'apporte plus rien.
>
> **Attendu réaliste :** switch de ~70 s (fichier froid) à ~4-5 s (fichier en cache, mesuré le 12/09). Pas zéro : Qwen (14,6 Go) et Gemma (9 Go) ne tiennent pas ensemble dans les 16 Go de la 5060 Ti, il faut toujours décharger l'un pour charger l'autre.
>
> **Mesure de la RAM (étape 5) décalée après cette étape**, pour mesurer la configuration finale, pré-lecture comprise.

1. **Pré-lecture de l'autre modèle** dès que le moteur est prêt, puis rafraîchie toutes les 30 min : fichier GGUF + compagnons (brouillon MTP, projecteur vision). Lecture séquentielle, fil de basse priorité, E/S en priorité basse ; **interrompue dès qu'un chargement commence**, pour laisser le disque au serveur. Jamais de second serveur.
2. **Garde-fou mémoire revu :** la règle « RAM utilisée < 12 Go » bloquerait presque toujours (11 Go utilisés Med fermé, relevé du 14/09). Le cache étant repris instantanément par Windows, il ne peut pas faire planter la machine ; la règle devient **RAM disponible ≥ taille du fichier + 4 Go**, vérifiée avant chaque fichier et pendant la lecture.
3. **Journal** `%APPDATA%\MedCompanion\prelecture-modeles.log` : chaque pré-lecture (fichier, durée, débit — un débit très élevé signe un fichier déjà en cache) et chaque chargement de modèle (durée réelle). Sert à valider l'étape et à recouper la journée de mesure.
4. Réglage `LlamaCppPrelectureActive` (activée par défaut) pour couper la pré-lecture sans recompiler.

**Hors switch, à décider à part :**
- **Gemma à 32 768 de contexte** au lieu de 131 072 : 1,1 Go de VRAM rendu, même temps de chargement.
- Corriger l'estimation `SecondesEstimees` affichée dans Pilotage (8 s par bascule, vrai seulement si le fichier est en cache).
- Étendre le schéma aux tâches hors consultation (dossier de restitution, synthèse patient → Qwen ; courriers, attestations, chat → Gemma).

**Validation :** au démarrage, le journal montre la pré-lecture de Qwen ; switch Gemma → Qwen puis Qwen → Gemma chronométrés dans le journal (~4-5 s) ; la pré-lecture n'augmente pas la RAM *utilisée* (seulement le cache) ; après un redémarrage du PC, le premier switch vers Qwen reste court une fois la pré-lecture terminée.

> **Réalisé le 15/09/2026 — ce qui manquait pour tenir les 4-5 s.**
>
> **1. Un journal par chargement.** `llama-server.log` était écrasé à chaque démarrage et jamais refermé : au lancement suivant, l'ouverture échouait sans rien dire, et le nouveau chargement n'avait aucun journal. Désormais `logs\llama-server_AAAAMMJJ_HHMMSS_<modèle>.log` (40 conservés), avec en tête les arguments exacts et le temps écoulé avant le lancement (`port · Ollama`), en pied la durée « serveur prêt ». C'est ce qui a permis les mesures ci-dessous.
>
> **2. Où partent les secondes** — le « contexte quantifié » était soupçonné ; les journaux l'innocentent. La fin du chargement (contexte MTP, cache KV q8_0) prend **0,3 à 0,4 s** chez Qwen. Tout le reste est la **lecture des poids**, qui dépend du cache Windows :
>
> | Chargement | Avant lancement | Lecture des poids | Fin | Total |
> |---|---|---|---|---|
> | Qwen, fichier froid | 4,1 s | 29,5 s | 0,4 s | 35,6 s |
> | Qwen, en partie en cache | 4,1 s | 9,2 s | 0,3 s | 15,6 s |
> | Gemma, en cache | 4,1 s | 1,9 s | 0,7 s | 8,6 s |
> | **Qwen, en cache, après correctif** | **0,0 s** | 4,0 s | 0,3 s | **4,6 s** |
> | **Gemma, en cache, après correctif** | **0,0 s** | 3,2 s | 0,8 s | **4,6 s** |
>
> **3. Les 4,1 s « avant lancement » : Ollama.** Chaque démarrage appelait `localhost:11434/api/ps` pour libérer la VRAM d'Ollama. Ollama fermé, la requête échouait en **4,23 s** (mesuré : Windows essaie ::1 puis 127.0.0.1, ~2 s par tentative refusée). `DechargerModelesResidentsAsync` vérifie maintenant qu'un process `ollama` tourne avant d'appeler : 0,0 s.
>
> **4. Pistes écartées.** *mmap* au lieu de `--no-mmap` : réglage `LlamaCppLoadMode` ajouté pour comparer, **non poursuivi** — le gain possible (1-2 s) ne justifie pas de revenir sur le principe acquis de la section 4 (`--no-mmap` divise par dix la RAM occupée par le serveur) ; reste sur `no-mmap`. *Direct I/O* (`--load-mode dio`) : non intégré sous Windows (PR llama.cpp #26014 ouverte), contourne le cache — donc plus lent dans notre cas favorable — et plafonné par le SATA. *Quantification plus petite de Qwen* : perte de qualité, non.
>
> **Point ouvert — le garde-fou mémoire en pleine journée.** Le 15/09, de 13 h à 16 h, la pré-lecture a écarté Qwen à chaque tentative (10,7 à 14,8 Go disponibles pour 17,0 requis). Dans ces conditions la bascule vers Qwen retombe à ~30 s. À trancher avec la mesure de RAM de l'étape 5 : assouplir la marge, ou constater que 32 Go ne suffisent pas à garder les deux modèles en cache (critère matériel de la section 7).
>
> **Avertissement vu au lancement de Qwen, sans conséquence constatée :** llama.cpp signale qu'il ne peut pas « faire tenir les réglages dans la mémoire libre » (couches imposées par `-ngl 99`) ; la mémoire partagée du GPU reste plate et la génération normale. À surveiller si le débit de Qwen baisse.

### Étape 8 — Retrait d'Ollama

*Plus tard, une fois les étapes 1 à 7 stables.* — **Statut : déplacée le 15/09/2026 dans [PLAN_MED_100_LOCAL.md](PLAN_MED_100_LOCAL.md)**, avec l'inventaire complet : l'OCR n'était pas la seule dépendance (agents, formulaires, régénération, structuration des notes), et le retrait d'Ollama y est lié à celui du cloud et de l'anonymisation. Texte d'origine conservé ci-dessous.

1. **Dépendance à traiter d'abord :** l'OCR (`GlmOcrService`, réglage `OcrModel = glm-ocr:latest`) est servi par Ollama. Il lui faut une alternative, par exemple la vision de Gemma QAT sur llama.cpp.
2. Retirer Ollama du sélecteur et de la fabrique, ainsi que la bascule automatique vers OpenAI prévue quand Ollama est absent.
3. Retirer le lancement d'Ollama au démarrage de Windows (`demarrer-ollama-5060ti.vbs`, clé Run).
4. Retirer le crochet `LibererVramConcurrente`, devenu sans objet.
5. Libérer `D:\PosteTravail\.ollama\models`.

---

## 6. Décisions en attente

| Question | Proposition | Statut |
|---|---|---|
| Taille de la partition M: | 48 Go | ✅ décidé et fait le 14/09 |
| Liste des suppressions de l'étape 1 | celle de l'étape 1 | ✅ décidé et fait le 14/09 |
| Filet de déchargement au repos | 2 h, réglable | ✅ adopté le 14/09 (LLM et Whisper) |
| Stratégie de switch | l'autre modèle gardé en cache, un seul à la fois | ✅ décidé le 14/09 (proposition du médecin) |
| Matériel | aucun achat sur AM4 ; machine AM5 étudiée d'ici ~6 mois sur mesures | ✅ décidé le 14/09 |
| Affectation des tâches hors consultation | restitution + synthèse → Qwen ; courriers, attestations, chat → Gemma | ouvert |
| Gemma à 32 768 de contexte | 1,1 Go de VRAM rendu | ouvert |
| Garde-fou de la pré-lecture en journée | marge « taille + 4 Go » à revoir : Qwen écarté de 13 h à 16 h le 15/09 | ouvert — avec la mesure de l'étape 5 |
| Mode de chargement mmap | réglage `LlamaCppLoadMode` disponible | non poursuivi le 15/09 (reste `no-mmap`) |

---

## 7. Matériel — aucun achat sur AM4, machine AM5 à étudier d'ici ~6 mois

### Décision du 14/09/2026 (fin de journée) — elle remplace l'option B550 ci-dessous

**Plus aucune dépense sur ce poste.** La plateforme AM4 est en fin de vie : le Ryzen 7 5700X est déjà de la dernière génération qu'elle accepte, et une carte mère B550 ou de la RAM DDR4 ne se réutiliseraient pas sur AM5 (DDR5). Le seul achat qui se serait reporté — un NVMe — est inutilisable aujourd'hui, puisqu'il désactiverait le slot de la 3050. **Les deux cartes graphiques (5060 Ti, 3050), elles, passeront dans la future machine.**

**Une machine AM5 sera étudiée dans environ six mois**, quand les besoins de Med seront fixés : l'application évolue encore et tout n'y est pas intégré. D'ici là, on accumule des **mesures** plutôt que des impressions, pour dimensionner sur des chiffres.

Ce qui justifie d'attendre : le logiciel a déjà apporté l'essentiel du gain — switch d'une minute à ~5 s, travail décrit comme « fluide » par le médecin (14/09), sans rien acheter. Ce qui reste lié au matériel (premier chargement à froid, RAM tendue quand le poste est chargé) est gênant, pas bloquant.

### Cahier des besoins à renseigner d'ici là

| Question encore ouverte | Ce qu'elle dimensionne | Où trouver la réponse |
|---|---|---|
| Reste-t-on sur Qwen 27B + Gemma 12B, ou un modèle plus gros deviendra-t-il utile ? | La **VRAM** — c'est la carte graphique, pas la plateforme, qui borne le choix des modèles | Usage de Med, qualité des sorties |
| RAM réellement utilisée par Med, Whisper, Doctolib, navigateurs sur une journée | **64 Go ou plus** de DDR5 | Enregistreur RAM (étape 5), `mesures\ram-*.txt` |
| Fréquence des switchs Qwen ↔ Gemma, durée des chargements, efficacité du cache | Nécessité d'un **NVMe dédié aux modèles** | `prelecture-modeles.log` (étape 7) |
| Deux cartes sur la durée ? | Carte mère capable de donner **8 lignes PCIe à chacune** (toutes ne le font pas) | Usage Whisper / LLM |
| Ollama retiré, OCR passé sur la vision de Gemma ? | Charge quotidienne de la machine | Étape 8 |

Processeur : l'inférence se fait entièrement sur la carte graphique, un milieu de gamme AM5 suffira probablement. Durée de vie de la plateforme : AMD a annoncé un support AM5 sur plusieurs générations (jusqu'en 2027 au moins selon les annonces connues) — **à revérifier au moment d'acheter**.

---

> *Historique, conservé pour mémoire : l'analyse ci-dessous a précédé la décision de ne plus investir sur AM4.*

### Pourquoi pas un NVMe sur la carte actuelle

Sur l'**ASRock AB350 Pro4**, l'emplacement NVMe (M2_1, Ultra M.2) et le slot **PCIE4** se partagent les 4 mêmes lignes du processeur : **occuper M2_1 désactive PCIE4**. Or la 3050 (Whisper) est dans PCIE4 — `nvidia-smi` la voit en Gen3 x4, et le BIOS nomme l'emplacement `PCIE4_M2_1`. Le second M.2 (M2_2) n'accepte que du SATA : aucun gain. Avec deux cartes graphiques, **NVMe et 3050 sont incompatibles sur cette carte mère.**

### Critères de déclenchement de l'achat *(abandonnés le 14/09/2026)*

Mesurés après l'étape 7, en usage réel :

- un switch Qwen ↔ Gemma dépasse régulièrement ~10 s alors que la RAM utilisée est sous 12 Go ;
- ou le premier chargement après le démarrage du PC (~70 s depuis le SSD SATA) reste gênant malgré la pré-lecture ;
- ou la saturation mémoire persiste et l'étape 5 montre un besoin réel, pas un défaut corrigeable.

### Cible envisagée : une B550 + un NVMe Gen4 *(abandonnée le 14/09/2026 — plateforme AM4 sans avenir)*

Sur B550, M2_1 et le 1er slot x16 sont reliés au processeur, en Gen4 ; le 2e slot x16 dépend du chipset. NVMe et 3050 cohabitent donc. Le processeur (Ryzen 7 5700X) et la RAM DDR4 sont conservés.

Liste de vérification avant achat :

1. Le **2e slot x16 est câblé en x4** — pas x1 ni x2.
2. **M2_1 est relié au processeur.**
3. **Notes de bas de page** : ce que désactive chaque M.2. **M2_2 restera vide** — sur plusieurs B550 (MSI B550-A PRO, Gigabyte B550 Aorus Pro), c'est lui qui coupe le 2e slot graphique. Sur l'ASRock B550 Pro4, PCIE3 est en Gen3 x4 et le M.2 secondaire partage avec des ports SATA.
4. **Espacement** : la 5060 Ti (2 à 2,5 slots d'épaisseur) ne recouvre pas le 2e slot.
5. **3 ports SATA** restent actifs pour les 3 disques.
6. **BIOS** compatible Ryzen 5000 à la livraison, ou mise à jour possible sans processeur.
7. **Licence Windows** liée au compte Microsoft avant l'opération : l'activation peut sauter au changement de carte mère.

**Effet sur le plan :** l'étape 2 (partition) disparaît, les modèles vont sur le NVMe ; l'étape 3 pointe vers le NVMe au lieu de M:.

Gain attendu, pour garder les pieds sur terre : lecture de Qwen à froid de ~70 s à ~2 s, envoi vers la carte de ~2 s à ~1 s. **Aucun gain sur la vitesse de génération**, qui se fait entièrement dans la VRAM.

---

## 8. Journal

| Date | Étape | Fait |
|---|---|---|
| 14/09/2026 | — | Plan rédigé après état des lieux et discussion |
| 14/09/2026 | — | Matériel : on garde l'AB350 Pro4 ; B550 + NVMe seulement si les critères de la section 7 sont atteints après l'étape 7 |
| 14/09/2026 | 1 | Copie D: vérifiée (SHA-256), réglage `OllamaModel` → `gemma4-qat-mtp`, modèles inutilisés supprimés. C: : 80,6 Go libres (+9,4 Go, pas +37 : dossiers en liens physiques). À valider dans Med. |
| 14/09/2026 | 1 | ✅ Validé par le médecin : ouverture Gemma QAT, bascule Qwen, vision formulaire, dictée. |
| 14/09/2026 | 2 | ✅ Partition M: 48 Go créée par le médecin, fichier d'échange C: 8–16 Go + D:, limite validée 68,5 Go. Modèles copiés sur M:, empreintes identiques. |
| 14/09/2026 | 3 | ✅ Med lit uniquement M: (LLM + Whisper), sans repli ; validé avec anciens dossiers renommés. Anciennes copies C: supprimées (+22,1 Go). Whisper large-v3 sauvegardé sur D:. |
| 14/09/2026 | 6 (pt 3) | ✅ Ouverture forcée sur Gemma QAT (réglage mémorisé + profil par défaut du code). Validé par le médecin. |
| 14/09/2026 | 4 | ✅ Plus de bascule entre cartes : LLM non déchargé avant dictée, Whisper non déchargé après, veille Whisper 2 h, carte Whisper par UUID. Validé par trace (même processus llama-server sur tout le parcours). |
| 14/09/2026 | 5 | Enregistreur RAM installé (démarrage de session, `AppData\Roaming\MedCompanion\mesures\ram-AAAA-MM-JJ.txt`). Journée de mesure prévue le 15/09, 9 h–18 h. Premier relevé : 11 Go de RAM utilisée **Med fermé** (Doctolib ~2 Go, navigateurs ~2,4 Go) — le seuil de 12 Go de l'étape 7 sera probablement à revoir. |
| 14/09/2026 | 6 | Warm-up supprimé, démarrage unique, état publié par le moteur, voyant piloté par l'état, veille LLM 2 h, surveillance de santé. Compilé, à valider en réel. |
| 14/09/2026 | 6 | ✅ Scénarios 1 à 6 validés par le médecin (ouverture, pulsation en génération, bascule Qwen, lecture vision, arrêt Pilotage → gris, serveur tué → rouge). Deux défauts relevés et corrigés : bouton Redémarrer de Pilotage inactif une fois le serveur arrêté (défaut antérieur — devient « ▶ Démarrer ») ; lecture d'image invisible dans l'en-tête (voyant 👁 + « X reprendra au prochain appel texte », le sélecteur restant sur le modèle texte). Correctifs compilés, à valider. |
| 14/09/2026 | 6 | ✅ Correctifs validés par le médecin (bouton Démarrer, voyant 👁 en lecture d'image). Étape 6 close. |
| 14/09/2026 | 7 | Étape simplifiée (l'autre modèle gardé en cache) et codée : `LlamaCppPrelecture`, journal `prelecture-modeles.log`, durée de chaque chargement. Premier usage réel : switchs **4,6 à 6,5 s** ; rafraîchissement utile (Qwen partiellement évincé en 20 min, relu) ; **anomalie : 1er switch vers Qwen en 31,5 s** malgré la pré-lecture terminée, suivants à 4,6 s — cause inconnue, à observer après redémarrage ; rafraîchissement de 16:41 ignoré (16,6 Go disponibles pour 17,0 requis) : ~16 Go utilisés par les programmes + 12,9 Go de Qwen + 4 Go de marge dépassent les 32 Go — seul l'autre modèle est pré-lu, le cache laissé par Windows sur le modèle chargé compte comme disponible et ne bloque rien ; marge peut-être un peu stricte, à trancher avec la mesure du 15/09. Impression du médecin : « fluide, je travaille avec aisance, mieux que l'ancien setup ». Journée complète de mesure le 15/09. |
| 14/09/2026 | — | **Matériel : aucun achat sur AM4** (plateforme en fin de vie, B550/DDR4 non réutilisables). Les cartes graphiques suivront. **Machine AM5 à étudier dans ~6 mois**, sur un cahier des besoins chiffré (section 7), quand les besoins de Med seront fixés. L'option B550 + NVMe est abandonnée. |
| 15/09/2026 | 7 | Journaux par chargement (l'ancien fichier unique, jamais refermé, empêchait de journaliser après une bascule). Mesures : le contexte quantifié prend 0,3-0,4 s ; le temps part dans la lecture des poids (Qwen 29,5 s à froid) et dans **4,1 s d'attente d'Ollama fermé** avant chaque lancement. Correctif Ollama. |
| 15/09/2026 | 7 | ✅ Validé par le médecin : bascules **4,6 à 5,5 s** (Qwen et Gemma, fichiers en cache), « c'est excellent ». mmap et Direct I/O écartés. Point ouvert : garde-fou de la pré-lecture qui écarte Qwen en journée. |
| 15/09/2026 | — | Sélecteur de modèles : la section llama.cpp s'affiche aussi quand Ollama est fermé (elle était imbriquée dans le test « Ollama répond »). |
