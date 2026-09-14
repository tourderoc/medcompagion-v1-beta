# PLAN — Moteur LLM local : modèles sur SSD, deux cartes, voyant fidèle, switch optimisé

> **Statut :** plan validé dans son principe le 14 septembre 2026. Aucune étape démarrée.
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

*Code + mesures.* — **Statut : à faire**

1. **Pré-lecture du modèle inactif** dans le cache Windows : lecture séquentielle, basse priorité, en arrière-plan. **Seulement si la RAM utilisée est sous 12 Go.** Jamais de second serveur.
2. **Anticipation par le schéma** : à l'ouverture d'une phase, pré-lire le modèle de la prochaine étape qui en change. Exemple : pendant l'extraction Gemma du 1er entretien, pré-lire Qwen.
3. **Étendre le schéma aux tâches hors consultation** — proposition à valider :
   - dossier de restitution et synthèse patient → Qwen ;
   - courriers, attestations, chat → Gemma.
4. **Gemma à 32 768 de contexte** au lieu de 131 072 : 1,1 Go de VRAM rendu, même temps de chargement.
5. Corriger l'estimation `SecondesEstimees` : ses 8 s par bascule ne sont vraies que si le fichier est en cache.

**Validation :** un 1er entretien complet chronométré ; switch en ~5 s quand la RAM utilisée est sous 12 Go ; la pré-lecture n'augmente pas la RAM *utilisée* (seulement le cache).

### Étape 8 — Retrait d'Ollama

*Plus tard, une fois les étapes 1 à 7 stables.* — **Statut : à faire**

1. **Dépendance à traiter d'abord :** l'OCR (`GlmOcrService`, réglage `OcrModel = glm-ocr:latest`) est servi par Ollama. Il lui faut une alternative, par exemple la vision de Gemma QAT sur llama.cpp.
2. Retirer Ollama du sélecteur et de la fabrique, ainsi que la bascule automatique vers OpenAI prévue quand Ollama est absent.
3. Retirer le lancement d'Ollama au démarrage de Windows (`demarrer-ollama-5060ti.vbs`, clé Run).
4. Retirer le crochet `LibererVramConcurrente`, devenu sans objet.
5. Libérer `D:\PosteTravail\.ollama\models`.

---

## 6. Décisions en attente

| Question | Proposition | Statut |
|---|---|---|
| Taille de la partition M: | 48 Go | à confirmer |
| Liste des suppressions de l'étape 1 | celle de l'étape 1 | à confirmer |
| Filet de déchargement au repos | 2 h, réglable | à confirmer (alternative : jamais tant que Med est ouvert) |
| Affectation des tâches hors consultation | restitution + synthèse → Qwen ; courriers, attestations, chat → Gemma | à valider à l'étape 7 |

---

## 7. Option matérielle — seulement si le résultat reste insuffisant

**Décision du 14/09/2026 :** le plan est mené jusqu'au bout **avec le matériel actuel**. Un changement de carte mère n'est envisagé qu'après l'étape 7, sur mesures.

### Pourquoi pas un NVMe sur la carte actuelle

Sur l'**ASRock AB350 Pro4**, l'emplacement NVMe (M2_1, Ultra M.2) et le slot **PCIE4** se partagent les 4 mêmes lignes du processeur : **occuper M2_1 désactive PCIE4**. Or la 3050 (Whisper) est dans PCIE4 — `nvidia-smi` la voit en Gen3 x4, et le BIOS nomme l'emplacement `PCIE4_M2_1`. Le second M.2 (M2_2) n'accepte que du SATA : aucun gain. Avec deux cartes graphiques, **NVMe et 3050 sont incompatibles sur cette carte mère.**

### Critères de déclenchement de l'achat

Mesurés après l'étape 7, en usage réel :

- un switch Qwen ↔ Gemma dépasse régulièrement ~10 s alors que la RAM utilisée est sous 12 Go ;
- ou le premier chargement après le démarrage du PC (~70 s depuis le SSD SATA) reste gênant malgré la pré-lecture ;
- ou la saturation mémoire persiste et l'étape 5 montre un besoin réel, pas un défaut corrigeable.

### Cible : une B550 + un NVMe Gen4

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
