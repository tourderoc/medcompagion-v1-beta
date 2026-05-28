# Plan — Amélioration qualité audio MedCompanion

> Objectif : rapprocher la qualité des enregistrements MedCompanion (Whisper) de celle obtenue avec LiveKit/WebRTC dans Parent'aile, en utilisant le même matériel (Amazon USB Streaming Mic).

---

## 1. Diagnostic

### Matériel observé sur le poste

- **Amazon USB Streaming Mic**
- Chipset : C-Media (VID `0D8C` / PID `0220`)
- Driver : USB Audio Class générique Windows
- Format natif : **48 kHz, 24-bit, stéréo**
- **Aucun DSP intégré au micro** (micro budget ~30-40€)
- Pas d'interface logicielle propriétaire

### Pourquoi LiveKit/Parent'aile sonne mieux avec le même micro

LiveKit s'appuie sur **WebRTC APM** (Audio Processing Module) qui applique 3 traitements DSP standards en temps réel :

| Traitement | Effet | Lib WebRTC |
|---|---|---|
| **AEC** (Acoustic Echo Cancellation) | Supprime l'écho des haut-parleurs | WebRTC APM |
| **NS** (Noise Suppression) | Supprime bruit ambiant (clim, ventilo, papier) | RNNoise (ML) ou WebRTC NS |
| **AGC** (Auto Gain Control) | Normalise le volume (voix lointaine remontée) | WebRTC APM |
| **HPF** (High-Pass Filter) | Coupe les basses fréquences parasites | WebRTC APM |
| **VAD** (Voice Activity Detection) | Détecte les zones de parole | WebRTC APM |

### Chaîne audio LiveKit/Parent'aile

```
Mic (raw 48kHz / 24-bit / stéréo)
   → WebRTC capture (48 kHz, format natif préservé)
   → WebRTC APM : AEC + NS + AGC + HPF
   → Encodage Opus
```

### Chaîne audio MedCompanion actuelle

```
Mic (raw 48kHz / 24-bit / stéréo)
   → NAudio capture en 16 kHz / 16-bit / mono   ❌ resampling brutal
   → AUCUN DSP                                    ❌ rien
   → Sauvegarde WAV + envoi Whisper
```

### Problèmes identifiés

1. **Resampling brutal 48 kHz → 16 kHz** sans filtre anti-aliasing → introduit du bruit dans le signal capté
2. **Aucun traitement** sur le signal : bruit ambiant non filtré, niveau variable, pas de high-pass
3. Le résultat est un signal qui ressemble au "raw" du micro = qualité brute d'un mic budget

---

## 2. Plan d'amélioration (incremental)

Plan par étapes croissantes — chaque étape peut être livrée et évaluée indépendamment avant de passer à la suivante.

### Étape 1 — Capture en 48 kHz puis resampling propre

**Effort** : ~15 minutes de dev
**Gain attendu** : modéré mais immédiat (signal plus propre AVANT tout traitement)

**Changements** :

- Modifier `AudioRecorder.cs` pour capturer dans le format natif du micro (48 kHz, mono ou stéréo down-mixé)
- Insérer un `MediaFoundationResampler` NAudio pour downsampler en 16 kHz mono avec filtre anti-aliasing correct
- Garder le format Whisper (16 kHz mono 16-bit) en sortie

**Risque** : faible — c'est du NAudio standard, aucune dépendance externe.

### Étape 2 — Intégration RNNoise (réduction de bruit ML)

**Effort** : ~1 jour de dev
**Gain attendu** : majeur (c'est la lib que LiveKit utilise par défaut pour la suppression de bruit)

**Changements** :

- Ajouter le package NuGet `RNNoise.Net` (ou équivalent maintenu)
- Insérer le filtre RNNoise dans le pipeline : `Capture 48kHz → RNNoise → Resample 16kHz → Whisper`
- RNNoise traite par frames de 480 samples (à 48 kHz) — bufferisation à gérer
- Sauvegarde WAV inchangée mais audio nettoyé

**Risque** : moyen — dépendance native (DLL C), s'assurer du shipping correct.

### Étape 3 — AGC maison (Auto Gain Control)

**Effort** : ~30 minutes de dev (~40 lignes de code)
**Gain attendu** : modéré mais critique pour patients qui parlent bas

**Changements** :

- Mesure du RMS du signal sur fenêtre glissante (ex: 200 ms)
- Ajustement du gain pour normaliser vers une cible (ex: -3 dB peak ou -18 dBFS RMS)
- Limiteur soft pour éviter le clipping en cas de pic
- Insérer dans le pipeline après RNNoise : `Capture → RNNoise → AGC → Resample → Whisper`

**Risque** : faible — code maison NAudio.

### Étape 4 (optionnel) — WebRTC APM complet

**Effort** : ~3-5 jours de dev
**Gain attendu** : marginal vs Étapes 1-3 combinées, sauf si besoin d'AEC

**À envisager seulement si** :

- Étapes 1-3 ne suffisent pas en qualité
- Téléconsultations avec haut-parleurs (besoin d'AEC obligatoire)
- Bruits ambiants extrêmes (cabinet partagé, voirie proche)

**Changements** :

- Compiler `webrtc-audio-processing` (lib C++ séparée du stack WebRTC complet) en DLL
- Wrapper C# via P/Invoke (`WebRtcApm.NET` ou équivalent)
- Remplacer RNNoise + AGC maison par WebRTC APM complet

**Risque** : élevé — compilation native cross-plateforme, dépendance lourde, maintenance.

---

## 3. Comparatif effort / gain

| Étape | Effort | Dépendance externe | Gain qualité |
|---|---|---|---|
| 1 — Capture 48k + resample propre | 15 min | Aucune (NAudio standard) | ⭐⭐ |
| 2 — RNNoise (NS) | 1 jour | DLL native via NuGet | ⭐⭐⭐⭐ |
| 3 — AGC maison | 30 min | Aucune | ⭐⭐⭐ |
| 4 — WebRTC APM complet | 3-5 jours | DLL native custom | ⭐⭐⭐⭐⭐ |

**Combo Étapes 1+2+3** : environ 90% de la qualité LiveKit pour ~1 jour de dev total.

---

## 4. Validation par étape

À chaque étape, sauvegarder un échantillon de référence et comparer :

1. **Test d'écoute subjectif** : enregistrer la même phrase avant/après, écouter à l'aveugle
2. **Test Whisper objectif** : passer le même échantillon dans Whisper, comparer la précision de transcription (mots manqués, erreurs)
3. **Spectrogramme** (optionnel, via Audacity) : visualiser la réduction du bruit de fond

---

## 5. Recommandation immédiate

**Démarrer par l'Étape 1** :

- Aucune dépendance à ajouter
- 15 minutes de dev
- Non-bloquant pour le reste de l'app
- Donne un baseline plus propre avant les étapes ML
- Permet d'évaluer si Whisper s'améliore significativement déjà juste avec un meilleur resampling

Si l'Étape 1 améliore notablement la qualité → enchaîner Étape 2 (RNNoise) qui donnera le plus gros gain.

Si l'Étape 1 ne change rien à la perception → passer direct à l'Étape 2.

---

## 6. Hardware — note complémentaire

L'Amazon USB Streaming Mic reste un micro **basique** sans DSP intégré. Les améliorations software ci-dessus peuvent rattraper LiveKit, mais un micro de meilleure qualité offrirait un signal de départ encore plus propre :

| Micro | Prix | DSP intégré | Pertinence clinique |
|---|---|---|---|
| Amazon USB Streaming Mic (actuel) | ~30-40€ | Non | Acceptable avec software |
| Blue Yeti / Yeti Nano | ~80-100€ | Cardioïde directionnel | Bon |
| Shure MV7 / MV7+ | ~250€ | DSP USB intégré (NS, AGC) | Excellent (recommandation pro) |
| Rode NT-USB+ | ~180€ | DSP léger | Très bon |

**Décision** : commencer par optimiser le software. Si le résultat reste insuffisant après Étapes 1-3, envisager un upgrade hardware vers un Shure MV7 (DSP USB intégré = LiveKit-like out-of-the-box).
