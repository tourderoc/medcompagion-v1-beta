# PLAN — Med 100 % local : retrait d'Ollama, du cloud et de l'anonymisation

> **Statut :** plan rédigé le 15 septembre 2026, à attaquer étape par étape à partir du 16/09. Étape 0 faite.
> **Date d'ouverture :** 15 septembre 2026
> **Méthode :** une étape par séance, testée dans Med en réel avant la suivante. Med reste utilisable après chaque étape.
> **Déclencheur :** le moteur llama.cpp est stable (bascules de 4,6 à 5,5 s, [PLAN_MOTEUR_LLM_LOCAL.md](PLAN_MOTEUR_LLM_LOCAL.md) étape 7). Med tourne toute la journée avec Ollama fermé ; ce qui en dépend encore casse.
> **Remplace :** l'étape 8 du plan moteur, qui ne citait que l'OCR — l'inventaire réel est bien plus large.
> **Docs liés :** [PLAN_MOTEUR_LLM_LOCAL.md](PLAN_MOTEUR_LLM_LOCAL.md), [CLAUDE.md](CLAUDE.md)

---

## 1. La cible

Décision du médecin, 15 septembre 2026 : **Med tourne en local, tout le temps.**

1. **Deux modèles, un seul moteur :** Qwen3.8-27B et Gemma 4 12B QAT + MTP, sur llama.cpp. Plus aucun appel à Ollama. Ollama **reste installé** sur le poste mais endormi (étape 7) : l'application fait comme s'il n'existait plus.
2. **Plus d'accès cloud pour les données patient :** ni OpenAI, ni modèles « -cloud » d'Ollama.
3. **Plus d'anonymisation.** Elle n'existait que pour protéger les envois vers le cloud ; en local, rien ne quitte la machine. La passerelle LLM la saute déjà quand le modèle est local.
4. **GLM-OCR n'est plus utilisé** (confirmé par le médecin) : il sort sans remplaçant.

**Règle d'ordre :** on retire le cloud **avant** l'anonymisation. Retirer l'anonymisation en laissant OpenAI dans le sélecteur ouvrirait une porte vers le cloud sans aucune protection.

---

## 2. Inventaire (relevé le 15/09/2026)

### Ce qui appelle encore Ollama directement, hors sélecteur

| Fonction | Où | Modèle aujourd'hui | Utilisée ? |
|---|---|---|---|
| ~~Structuration des notes~~ | `OpenAIService.StructurerNoteAsync` | ~~`gemma-4-abliterated:e4b`~~ | ✅ **rebranchée le 15/09** (étape 0) |
| Agent de Pilotage — tri des messages des parents | `PilotageAgentService` | `gpt-oss:20b` (Ollama local) | oui |
| Agent Med — compagnon entre les consultations | `MedAgentService` + `agents_config.json` | `gemma-4-abliterated:e4b` (Ollama local) | oui |
| Agent Web — recherche internet | `WebAgentService` + `agents_config.json` | **`gpt-oss:120b-cloud`** — raisonnement **sur les serveurs d'Ollama** | à confirmer |
| Recherche web elle-même | `OllamaWebSearchService` | API en ligne `https://ollama.com/api` (clé), **indépendante de l'Ollama installé** | à confirmer |
| Assistant formulaires — réécriture des remarques | `FormulaireAssistantService.RewriteRemarquesWithLocalLLMAsync` | `AnonymizationModel` | oui (MDPH) |
| Régénération de documents — liste et choix des modèles | `RegenerationService.CreateProvider`, `GetAvailableModelsAsync` | Ollama ou OpenAI | oui |
| Nettoyage du texte OCR des documents importés | `DocumentService` | seulement si le fournisseur courant est Ollama — **déjà inactif** sous llama.cpp | non |
| GLM-OCR | `GlmOcrService`, réglage `OcrModel` | `glm-ocr` | **non** (abandonné) |
| Libération de la VRAM d'Ollama avant chaque démarrage de llama.cpp | `LlamaCppServerManager.LibererVramConcurrente`, `OllamaLLMProvider.DechargerModelesResidentsAsync` | — | sans objet une fois Ollama retiré |

**Déjà sur llama.cpp, rien à faire :** tout le mode consultation (les étapes de `etapes-modeles.json`), le dossier de restitution, l'import Doctolib (il utilise le modèle courant malgré son nom), la lecture vision des formulaires.

### L'accès cloud

- **OpenAI** : dans le sélecteur, la fabrique (`LLMServiceFactory`, bascule automatique vers OpenAI quand Ollama est absent), `OpenAILLMProvider`, les agents (fournisseur par défaut du code : OpenAI `gpt-4o-mini`), la régénération, la clé dans le stockage sécurisé.
- **Modèles « -cloud » d'Ollama** : listés dans le sélecteur quand Ollama tourne. ⚠️ `LLMGatewayService.IsLocalProvider()` les classe comme **locaux** (le fournisseur est Ollama) : ils partent sur les serveurs d'Ollama **sans anonymisation**. Disparaît avec Ollama.

### L'anonymisation

Tissée dans une dizaine de fichiers : `AnonymizationService`, `LLMGatewayService` (phases 1 à 3), `PromptConfigService.GetAnonymizedPromptAsync`, `OpenAIService.ExtractPIIAsync`, `LetterService`, `LetterReAdaptationService`, `FormulaireAssistantService`, `ScannedFormImportDialog`, `ChatViewModel`, fenêtres de test `SimplePhase3TestDialog` et `AnonymizationPhase3Dialog`, réglage `AnonymizationModel` dans `ParametresDialog`.

### Réglages et système

- `AppSettings` : `OllamaBaseUrl`, `AnonymizationModel`, `OcrModel`, `PilotageAgentModel`, `OllamaReasoningEffort`.
- ⚠️ **Piège :** `OllamaModel` sert **aussi** à mémoriser le profil llama.cpp choisi (`gemma4-qat-mtp`). Le renommer impose une reprise de l'ancienne valeur, sinon Med oublie le modèle d'ouverture.
- Système : `demarrer-ollama-5060ti.vbs` et sa clé Run au démarrage de Windows ; modèles dans `D:\PosteTravail\.ollama\models`.

---

## 3. Les étapes

### Étape 0 — Structuration des notes sur Gemma

*Code.* — **Statut : ✅ fait le 15/09/2026, à valider sur une vraie note**

- `StructurerNoteAsync` bascule le moteur sur **Gemma 4 12B QAT + MTP** (comme une étape de consultation : le sélecteur suit) au lieu d'appeler Ollama.
- Anonymisation retirée de ce chemin (pseudonyme puis désanonymisation autour d'un modèle local).
- Le **sexe du patient** est désormais précisé au modèle : le pseudonyme portait l'accord, un prénom épicène (Camille) ne le dit pas.
- La vérification « fournisseur courant configuré » est retirée : elle aurait bloqué la structuration sur un OpenAI sans clé.

**Validation :** structurer une note avec Ollama fermé ; relire les accords sur un patient au prénom épicène ; vérifier le poids de synthèse.

### Étape 1 — Agents Med et Pilotage sur llama.cpp

*Code.*

1. Ajouter une branche `LlamaCpp` à la création du fournisseur des agents (`MedAgentService`, `PilotageAgentService`), en passant par la fabrique comme l'étape 0 — jamais un second serveur.
2. Pilotage (tri des messages des parents) → **Gemma QAT** ; Med → **Gemma QAT**.
3. Mettre `agents_config.json` à jour, et le fournisseur par défaut du code (aujourd'hui OpenAI `gpt-4o-mini`).

**Validation :** trier trois vrais messages de parents et comparer avec le tri de `gpt-oss:20b` ; une conversation avec Med.

### Étape 2 — Agent Web

*Décision d'abord, code ensuite.*

- **Décision :** garder la recherche internet ? Si oui, les *questions* partent vers ollama.com (acceptable tant qu'elles ne contiennent pas de données patient) et le **raisonnement** repasse en local sur Gemma. Si non, l'agent Web sort.
- Aujourd'hui son raisonnement tourne sur **`gpt-oss:120b-cloud`** : à corriger dans tous les cas.

**Validation :** une recherche web aboutit, raisonnement local (vérifiable dans les journaux llama.cpp).

### Étape 3 — Assistant formulaires et régénération sur llama.cpp

*Code.*

1. `RewriteRemarquesWithLocalLLMAsync` → Gemma QAT via la fabrique.
2. `RegenerationService` : ne proposer que Qwen et Gemma (profils llama.cpp) dans la liste des modèles.

**Validation :** un formulaire MDPH avec remarques ; une régénération de document avec chacun des deux modèles.

### Étape 4 — Retirer le cloud

*Code.* À faire une fois les étapes 1 à 3 validées : plus rien d'utile ne passe par OpenAI.

1. Retirer OpenAI et les modèles « -cloud » du sélecteur et de la fabrique, dont la bascule automatique vers OpenAI.
2. Retirer `OpenAILLMProvider`, les branches OpenAI des agents et de la régénération, la saisie de la clé dans les paramètres.
3. Supprimer la clé OpenAI du stockage sécurisé.

**Validation :** une journée normale ; le sélecteur ne montre plus que Qwen et Gemma.

### Étape 5 — Retirer l'anonymisation

*Code.* Devenue du code mort à l'étape 4.

1. Retirer les phases 1 à 3 de `LLMGatewayService` et `PromptConfigService` (les appels deviennent directs).
2. Retirer `AnonymizationService`, `ExtractPIIAsync`, les deux fenêtres de test, le réglage `AnonymizationModel`.
3. Nettoyer les services qui l'injectent (courriers, ré-adaptation, formulaires, import de formulaires scannés, chat).

**Validation :** un courrier, un formulaire, une conversation, une synthèse — le vrai nom apparaît bien là où il doit.

### Étape 6 — Retirer Ollama du code

*Code.*

1. Retirer `OllamaLLMProvider`, `OllamaModelInfo`, `IsOllamaAvailableAsync`, la section Ollama du sélecteur.
2. Retirer `GlmOcrService` et le réglage `OcrModel` ; le nettoyage OCR d'Ollama dans `DocumentService`.
3. Retirer le crochet `LibererVramConcurrente` et son test de process Ollama.
4. Réglages : retirer `OllamaBaseUrl`, `PilotageAgentModel`, `OllamaReasoningEffort` ; **renommer `OllamaModel` avec reprise de l'ancienne valeur**.
5. Les libellés et commentaires qui citent Ollama (Pilotage, consultation, synthèse).

**Validation :** compilation sans aucune référence à Ollama ; ouverture de Med sur Gemma ; parcours complet d'une consultation.

### Étape 7 — Endormir Ollama sur le poste (sans le désinstaller)

*Manuel.* — **Statut : ✅ fait le 16/09/2026**

> **Décision du médecin, 16/09/2026 : on garde Ollama installé**, au cas où on en aurait besoin ; l'application, elle, fait comme s'il n'existait plus. Le coût est nul : 87 Go de modèles sur D: (disque mécanique, 1,6 To libre), et rien en mémoire tant qu'il ne tourne pas.
>
> **La nuance qui compte : installation et code sont deux choses séparées.** Aucune branche Ollama « au cas où » n'est gardée dans l'application (étapes 1 à 6) — ce serait du code mort que personne ne teste, et la porte des modèles « -cloud » resterait ouverte. Si le besoin revient, le code est dans l'historique git et se rebranche en une heure.

1. ✅ **Démarrage automatique coupé** le 16/09/2026 : clé `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `Ollama` supprimée. Elle valait `wscript.exe "C:\Users\nair\AppData\Local\Programs\Ollama\demarrer-ollama-5060ti.vbs"` — à recréer à l'identique pour revenir en arrière. Le script `.vbs` est conservé.
   *Pourquoi :* un Ollama qui tourne prend de la RAM, parfois de la VRAM, et concurrence le cache Windows dont dépendent les bascules à 4-5 s.
2. Ollama **reste installé**, ainsi que ses modèles dans `D:\PosteTravail\.ollama\models`. Il se relance à la main en cas de besoin.
3. **À revoir dans quelques mois** : si rien ne l'a rappelé, désinstaller et libérer les 87 Go.

---

## 4. Décisions en attente

| Question | Proposition | Statut |
|---|---|---|
| Recherche internet | garder, raisonnement local sur Gemma | à décider (étape 2) |
| Modèle de l'agent de Pilotage | Gemma QAT (tri court, fréquent) | à valider sur vrais messages |
| Retrait complet d'OpenAI | oui — Med 100 % local | à confirmer par le médecin |
| Désinstaller Ollama du poste | non : gardé installé et endormi, l'application fait comme s'il n'existait plus | ✅ décidé le 16/09 — à revoir dans quelques mois |

---

## 5. Journal

| Date | Étape | Fait |
|---|---|---|
| 15/09/2026 | — | Inventaire et plan rédigés. Constat : l'agent Web raisonne sur un modèle cloud d'Ollama ; les modèles « -cloud » sont classés locaux par la passerelle, donc jamais anonymisés. |
| 15/09/2026 | 0 | Structuration des notes sur Gemma 4 QAT + MTP (llama.cpp), sans anonymisation, sexe précisé au modèle. Compilé, à valider sur une vraie note. |
| 16/09/2026 | 7 | ✅ Ollama gardé installé mais endormi : clé de démarrage automatique supprimée (valeur notée à l'étape 7 pour revenir en arrière). Le code, lui, le retire complètement — pas de branche « au cas où ». |
