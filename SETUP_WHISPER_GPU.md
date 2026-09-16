# Setup Whisper GPU — Guide d'installation

> **Objectif** : permettre à Whisper de tourner sur GPU NVIDIA pour la dictée temps réel
> dans le mode Consultation. Ce guide compile tous les pièges rencontrés le **2026-05-10**
> sur le PC maison (RTX 3050 6GB) et leurs solutions, pour ne pas perdre de temps lors
> de l'installation sur d'autres postes.

---

## Prérequis machine

| Élément | Minimum | Recommandé |
|---------|---------|------------|
| OS | Windows 11 x64 | — |
| GPU | NVIDIA 4GB VRAM | RTX 16GB (large-v3) |
| Driver NVIDIA | 591.x | dernier stable |
| CUDA Toolkit | **13.x** (pas 12 !) | 13.1+ |
| Espace disque | 2 GB (medium) | 4 GB (large-v3) |

### Vérification rapide

```bash
nvcc --version   # doit afficher "release 13.x"
nvidia-smi       # vérifier GPU + driver
```

DLL critique attendue :
```
C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.X\bin\x64\cudart64_13.dll
```

---

## NuGet packages (déjà dans `.csproj`)

Aucune action manuelle, juste un `dotnet restore` au premier build :

```xml
<PackageReference Include="Whisper.net" Version="1.9.0" />
<PackageReference Include="Whisper.net.Runtime" Version="1.9.0" />
<PackageReference Include="Whisper.net.Runtime.Cuda" Version="1.9.0" />
<PackageReference Include="NAudio" Version="2.2.1" />
```

⚠️ **Ne pas utiliser** `Whisper.net.Runtime.Cuda.Windows` (le sous-package), c'est `Whisper.net.Runtime.Cuda` qui inclut les bons binaires CUDA 13.

---

## Pré-charger le modèle (gain de temps important)

Le modèle Whisper est **téléchargé au 1er clic "Dicter"** depuis Hugging Face :
- `medium` : **1.5 GB** (~5 min de téléchargement)
- `large-v3` : **3 GB** (~10 min)

### Astuce pour les nouveaux postes

Plutôt que de re-télécharger, copier depuis un poste déjà configuré :

**Source** (PC déjà configuré) :
```
%APPDATA%\MedCompanion\models\ggml-medium.bin
%APPDATA%\MedCompanion\models\ggml-large-v3.bin
```

**Destination** (nouveau PC), même chemin :
```
%APPDATA%\MedCompanion\models\
```

Une fois copié, l'app détecte le fichier au démarrage et **skip le téléchargement**.

---

## Configuration recommandée par poste

| Poste | GPU | VRAM | Modèle conseillé | Latence transcription |
|-------|-----|------|------------------|------------------------|
| **Bureau cabinet** | RTX 5060 Ti | 16 GB | `large-v3` | < 1s |
| **Maison** | RTX 3050 | 6 GB | `medium` | 1-2s |

Pour changer le modèle par défaut, modifier `WhisperModelManager.cs` ligne 24 :

```csharp
public WhisperModelSize ModelSize { get; set; } = WhisperModelSize.Medium;
// ou WhisperModelSize.LargeV3
```

---

## Pièges connus (TOUS résolus en code)

> Aucune action manuelle pour les corriger — le code applique les fixes automatiquement.
> Cette section sert uniquement à comprendre si tu rencontres l'erreur sur un poste exotique.

### 🐛 Piège 1 : `Failed to load native whisper library. Le module spécifié est introuvable`

**Cause** : Les DLLs CUDA sont sur disque mais pas dans le `PATH` au moment où Whisper.net charge la librairie native.

**Fix code** : `WhisperStreamingService.EnsureCudaInPath()` ajoute automatiquement au démarrage :
```
C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.1\bin\x64
C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.0\bin\x64
C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.0\bin
```

**Si ton poste a CUDA dans un chemin custom** : ajouter le chemin dans la liste `cudaCandidates` du fichier.

---

### 🐛 Piège 2 : Whisper utilise CPU au lieu de GPU (transcription très lente, CPU 95%)

**Cause** : Whisper.net 1.7.0 (octobre 2024) compilé contre **CUDA 12**. Si le poste a CUDA 13 uniquement (cas typique en 2026), il manque `cudart64_12.dll` et Whisper retombe en CPU sans erreur.

**Fix code** : Mise à jour vers **Whisper.net 1.9.0** qui inclut le runtime CUDA 13.

**Vérification** : Task Manager → Performance → GPU Compute doit monter pendant la transcription. La mémoire GPU dédiée doit grimper de ~1.5 GB (medium) ou ~3 GB (large-v3).

---

### 🐛 Piège 3 : `modelStream.Length` lève `NotSupportedException` au téléchargement

**Cause** : Le stream HTTP retourné par `WhisperGgmlDownloader.GetGgmlModelAsync` ne supporte pas `.Length` (pas de seek).

**Fix code** : `WhisperModelManager` utilise des tailles estimées en dur (`ModelEstimatedBytes`) pour la barre de progression au lieu de lire `.Length`.

---

### 🐛 Piège 4 : Mémoire GPU sature après plusieurs Start/Stop (multiples instances Whisper)

**Cause** : Chaque clic "Dicter" créait un **nouveau** `WhisperFactory` + `WhisperProcessor` sans disposer les anciens. Au bout de 3-4 cycles, la VRAM est pleine.

**Fix code** : Pattern **singleton** dans `WhisperStreamingService` — la factory et le processor sont créés **une seule fois** dans `EnsureWhisperInitializedAsync()` et conservés jusqu'à `Dispose()` du service.

---

### 🐛 Piège 5 : Hallucinations Whisper (`Sous-titres par Lepenois-Malinois`, `Bienvenue dans le monde de la pédophilie`, etc.)

**Causes multiples** :
1. Whisper a été entraîné sur des sous-titres YouTube → réflexes de fin de vidéo
2. Mode "context" activé par défaut → hallucination du chunk N propage au chunk N+1
3. Le mot "pédopsychiatrie" dans le prompt initial déclenche des associations problématiques
4. Chunks audio trop courts ou trop silencieux → le modèle invente

**Fix code** (4 garde-fous) :
1. **`WithNoContext()`** sur le builder Whisper → chaque chunk traité indépendamment
2. **Prompt neutre** : `"Conversation médicale entre un médecin et une famille en français."` (pas le mot "pédopsychiatrie")
3. **Liste noire de patterns** dans `KnownHallucinations[]` → filtré en post-traitement
4. **Seuils audio stricts** : min 1.2s d'audio, RMS > 0.020 → ignore les chunks borderline

---

### 🐛 Piège 6 : Pertes audio pendant que Whisper transcrit (impression de "coupures")

**Cause** : L'ancien code utilisait `WaitAsync(0)` — si une transcription était en cours quand un nouveau flush arrivait, le buffer audio était **vidé ET jeté**.

**Fix code** : Utilisation de `Channel<float[]>` (queue zéro-perte). L'audio s'accumule, est traité dans l'ordre, jamais perdu.

---

### 🐛 Piège 7 : `WhisperGgmlDownloader.GetGgmlModelAsync` ne compile plus

**Cause** : En Whisper.net 1.9.0, la méthode est devenue **non-statique** (était statique en 1.7.0).

**Fix code** : `WhisperGgmlDownloader.Default.GetGgmlModelAsync(...)` (instance singleton).

### 🐛 Piège 8 : boucles d'hallucination — le modèle recrache le vocabulaire, ou répète une phrase

**Symptôme (16/09/2026).** Un tronçon entier transcrit ainsi : *« Test de bilan : WISC-V, WPPSI-IV, ADOS-2, Termes cliniques : … »* répété quinze fois ; le tronçon suivant redevient normal. Ou bien une phrase plausible mais fausse répétée en boucle : *« Les études de la médecine ont été réalisées en 2018. »*

**Cause.** Le premier cas est le **prompt lui-même** : le vocabulaire personnalisé injecté par `WhisperVocabService` conditionne le décodeur ; quand un tronçon ne contient pas de parole, le modèle n'a rien à transcrire et **continue le texte qu'on lui a donné**. Le second est une boucle de répétition classique de Whisper, indépendante du prompt. Le filtre `KnownHallucinations` n'attrape ni l'un ni l'autre : il ne connaît que des phrases types (« Sous-titres réalisés par… »).

**Ce que la mesure a montré** — banc d'essai `WhisperBench`, trois séances réelles rejouées, 16/09/2026 :

| Configuration | Phrase la plus répétée (16 min de consultation) | Mots produits |
|---|---|---|
| large-v3 + vocabulaire complet (**ancienne config**) | **28 répétitions** | 2 062 |
| large-v3 sans prompt | 17 | 1 174 |
| **large-v3-french sans prompt** | **2** (« Oui. ») | 1 049 |

Sur les mêmes tronçons, le modèle français transcrit la parole réelle là où le large-v3 générique boucle. Il corrige aussi la contamination du prompt sur du texte normal (« SVT » au lieu de « Test de B.S.V.T. »).

Le prompt, lui, **double le temps de transcription** (80 s → 144 s pour 16 min d'audio) : il est retraité à chaque tronçon. Et il n'a amélioré aucun terme que le modèle français ne trouvait déjà (« Concerta » correct dans les quatre configurations testées).

**Correctif appliqué le 16/09/2026** — trois réglages :

1. `WhisperModelPath = M:\whisper\ggml-large-v3-french.bin` — modèle [bofenghuang/whisper-large-v3-french](https://huggingface.co/bofenghuang/whisper-large-v3-french), fichier `ggml-model.bin` (3,1 Go), téléchargé à la main. Renseigné, ce réglage **prime sur le catalogue des quatre tailles et rien n'est téléchargé** ; le fichier manquant lève une erreur explicite plutôt que de rapatrier un large-v3 générique sous ce nom.
2. `WhisperVocabPromptActif = false` — plus aucun prompt. Le fichier de vocabulaire reste en place, réactivable.
3. Mode Batch **30 s coupé sur une pause** (au lieu de 90 s sèches) : texte identique, temps GPU inchangé, attente ramenée de ~100 s à ~35 s.

**Réserve connue** : aucun enregistrement de test ne contenait Médikinet, Vyvanse ou Quasym. Si un nom rare est mal transcrit, remettre un prompt **court** — les noms propres seuls, sans les intitulés de sections, qui sont ce qui bouclait.

**Rejouer la mesure** après tout changement (modèle, prompt, découpage) :

```
WhisperBench --session %APPDATA%\MedCompanion\recordings\session_AAAAMMJJ_HHMMSS
             [--modele large-v3|francais|tous] [--prompt complet|court|aucun|tous]
             [--seg 90|30vad|tous] [--ref transcription_corrigee.txt]
```

Il rejoue l'audio déjà enregistré hors de Med, compte les échos du prompt et les répétitions, et écrit un `rapport.md`. Avec `--ref`, il donne un taux d'erreur mot.

---

## Vérification post-installation (checklist)

1. ☐ `nvcc --version` retourne CUDA 13.x
2. ☐ `nvidia-smi` montre le GPU + driver récent
3. ☐ `dotnet build MedCompanion/MedCompanion.csproj` passe sans erreur
4. ☐ Lancer l'app, sélectionner un patient
5. ☐ Type consultation = `1ère consultation — Interrogatoire`
6. ☐ Cliquer **🎙 Dicter**
   - 1er clic : téléchargement du modèle (suivre la progression dans la barre de status)
   - Clics suivants : démarrage immédiat
7. ☐ Status passe à `● Enregistrement 00:02 • 0 segments`
8. ☐ Task Manager : **GPU Compute monte** pendant qu'on parle
9. ☐ Mémoire GPU dédiée : ~1.5 GB pour medium, ~3 GB pour large-v3
10. ☐ Le texte apparaît avec ~1-2s de latence après pause
11. ☐ Cliquer **⏹ Arrêter** puis **Extraire avec IA** → les 8 blocs se remplissent

---

## Architecture du service (vue rapide)

```
Micro NAudio (16kHz mono)
    ↓
WhisperStreamingService.OnAudioData
    ↓ accumule dans buffer + détecte silence (VAD)
    ↓ flush si silence >1s OU buffer >10s
Channel<float[]> (queue zéro-perte)
    ↓
TranscriptionLoopAsync (tâche en arrière-plan)
    ↓ Whisper GPU (factory singleton)
    ↓ FilterHallucinations (post-traitement)
    ↓ TextAppended event
ConsultationModeViewModel.TranscriptionInput
    ↓ tous les ~50 mots
SegmentReady event → IncrementalExtractorService → LLM → 8 blocs
```

**Fichiers clés** :
- [WhisperStreamingService.cs](MedCompanion/Services/Consultation/WhisperStreamingService.cs)
- [WhisperModelManager.cs](MedCompanion/Services/Consultation/WhisperModelManager.cs)
- [IncrementalExtractorService.cs](MedCompanion/Services/Consultation/IncrementalExtractorService.cs)
- [ConsultationModeViewModel.cs](MedCompanion/ViewModels/ConsultationModeViewModel.cs)

---

## Si quelque chose ne marche toujours pas

Activer les logs Visual Studio (Output window) pendant l'exécution. Le service expose tous les événements via `StatusChanged` qui s'affiche dans la barre de status de l'UI :

- `Téléchargement Whisper : XX%`
- `Chargement modèle GPU...`
- `● Enregistrement 02:30 • 5 segments`
- `⚠ Capture audio interrompue (raison) — redémarrage...`
- `✗ Erreur Whisper : <message>`

Si un message d'erreur apparaît, il indique la couche défaillante (téléchargement, chargement GPU, capture audio, ou transcription).
