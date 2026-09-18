# Reprise — ce qui attend au prochain chat

Écrit le 18/09/2026 en fin de séance, pour repartir sans avoir à reconstituer le contexte.

---

## 1. Deux sujets ouverts, explicitement mis de côté

### 1.1 La bibliothèque d'infographies

**Jamais commencé — même pas discuté sur le fond.** Le sujet a été posé le 16/09 puis laissé de côté :

> « je pense à faire une bibliothèque d'infographies dans Med qu'on pourrait ajouter dans les annexes pour mieux comprendre la maladie s'il y a un diagnostic de trouble spécifique, et pour les médicaments aussi s'il y a une indication »

**Ce qu'on sait déjà, et qui cadre le sujet :**
- Les annexes du Dossier de Restitution existent déjà : annexe méthodologique (générique) et annexe contacts (bloc 33, faite le 16/09).
- Le document est rendu en **HTML unique**, servant à la fois l'aperçu WebView2 et le PDF (Edge headless). Une image devra donc être embarquée en `data:` URI ou servie depuis un chemin stable — pas de dépendance réseau.
- La repagination tourne dans le navigateur et **déplace des cartes entières**. Une infographie sera une carte comme une autre : si elle ne tient pas sur une page, elle bascule entière. Une image plus haute qu'une A4 utile sera signalée par la phase 1 comme non corrigeable.

**Questions à trancher avant d'écrire une ligne :** d'où viennent les visuels (créés, achetés, libres de droits), qui décide qu'un diagnostic déclenche telle fiche, est-ce automatique ou proposé, et où ces fichiers vivent (le SSD est presque plein — voir §1.2).

### 1.2 Le SSD / l'adaptateur NVMe

**En attente du montage matériel.** Décidé le 17/09 :

- Adaptateur **GLOTRENDS PA09-X1** (~11,59 €), pour un port PCIe x1 libre et accessible.
- Y monter le **NVMe de 500 Go** (garder celui de 1 To pour la future machine AM5).
- Objectif : y déplacer `Documents` (5,3 Go) puis `Desktop` (34,4 Go) — 40 Go sur les 197 Go qui vivent aujourd'hui sur le **disque mécanique D:** (1,8 To, 1,1 M de fichiers).
- **Les modèles LLM restent sur le SSD SATA (`M:`)**, qui leur est dédié.
- PCIe 2.0 x1 ≈ 450–500 Mo/s : sans importance pour les petits fichiers (on est limité par la latence et les IOPS), décisif pour la lecture séquentielle d'un modèle.
- **Déconseillé : une carte graphique sur ce port x1.** Et « un modèle par carte » ne demanderait que deux cartes, or la carte mère n'a que deux ports x16.

> **⚠️ À FAIRE DIRE AU PROCHAIN CHAT : le nouveau chemin du projet** une fois `Desktop` déplacé. Aujourd'hui `D:\PosteTravail\Desktop\MedCompagion V1 béta`. Sans ça, il cherchera dans un dossier qui n'existe plus.

---

## 2. État du travail au 18/09 — non committé

**605 vérifications au TestRunner, 0 échec.** Tout compile. Rien n'est committé : le dernier commit est `b85e211` (17/09).

Fichiers modifiés ou créés aujourd'hui :

| Fichier | Quoi |
|---|---|
| `Services/LLM/LlamaCppModelProfile.cs` | `ReasoningBudget` (nouveau réglage), Gemma 131072 → 32768, migration des réglages persistés |
| `Services/LLM/LlamaCppServerManager.cs` | `--reasoning-budget` + message de fin ; note sur le `--reasoning-effort medium` trompeur |
| `Services/Restitutions/RestitutionSuggesterService.cs` | Consigne cartographie, `TermesInterditsPour()`, `RelireLangueAsync()`, feuille de route générée d'emblée |
| `ViewModels/.../RestitutionEditorViewModel.Qualite.cs` | Le constat peut porter une proposition de réécriture |
| `ViewModels/.../RestitutionEditorViewModel.Qualite.Phase2.cs` | **nouveau** — détection des citations prématurées |
| `ViewModels/.../RestitutionEditorViewModel.Qualite.Phase3.cs` | **nouveau** — couche linguistique |
| `ViewModels/.../RestitutionEditorViewModel.Phase3Texte.cs` | **nouveau** — extraction du texte libre (la garde de sécurité) |
| `Views/Restitutions/RestitutionEditorView.xaml` | Panneaux des phases 2 et 3, affichage accepter/refuser |
| `TestRunner/Program.cs` | Sections 38, 38b, 39, 40 ; correction d'un test rouge préexistant |

**Med était ouvert en fin de séance** : le binaire de `bin\` n'a pas pu être remplacé. Fermer Med et relancer une compilation pour voir les nouveaux panneaux.

---

## 3. Ce qui a coûté cher à établir — à ne pas refaire

### 3.1 Les moteurs, mesurés sur la machine

Relevés dans `%APPDATA%\MedCompanion\logs` (90 appels Qwen, 201 Gemma) :

| | Qwen3.8-27B IQ4_XS | Gemma 4 12B QAT |
|---|---|---|
| Génération | **42–54 t/s** | 90–107 t/s |
| Chargement à froid | **~31 s** (13,5 Go depuis `M:`) | 5,4 s |
| Contexte médian / max utilisé | 6 681 / **22 622** | 5 533 / 17 739 |
| Cache de prompt réutilisé | **88 à 96 %** | idem |

**Conclusions à ne pas rejouer :**
- **Qwen ne déborde pas** de la VRAM. Le `failed to fit params` du journal dit seulement que l'auto-ajusteur a renoncé parce qu'on impose `-ngl 99` — le débit prouve que tout tourne sur la carte. Qwen est 2× plus lent que Gemma parce qu'il fait 2× le travail : c'est le prix normal d'un 27B.
- **Le contexte de Qwen (32768) est bien dimensionné.** Le descendre à 8k tronquerait **47 % des appels**, en silence — il n'y a aucune gestion de dépassement dans le code, llama.cpp rogne le prompt sans rien dire.
- **`-fa on`, `-ngl 99`, cache KV q8_0, MTP, cache de prompt : tout est déjà optimal.** Rien à gratter.
- **`DraftTokens` reste à 3.** Mesuré : 3 → acceptation 0,84 / 48 t/s ; 5 → 0,37 / 34 t/s.
- **`reasoning_effort` est déjà sur « faible »** et part dans chaque requête — le `--reasoning-effort medium` de la ligne de commande est un défaut de repli qui ne s'applique jamais. **Ne pas s'y fier pour diagnostiquer.**
- Le seul levier restant était le **plafond de réflexion**, posé aujourd'hui à 800 tokens : Qwen délibérait ~2 000 tokens pour ~640 de réponse, effort « faible » compris.

**À vérifier au prochain démarrage**, en tête de journal : `--reasoning-budget 800` présent, et `n_ctx_slot = 32768` pour Gemma (il affichait 131072). Puis juger le rendu : si les blocs paraissent moins argumentés, remonter à 1200 ou repasser à `-1`.

**Écarté :** le modèle `Bonsai-2-27B-Ternary-CRACK-GGUF` vu sur Hugging Face. Ce n'est pas un 7B mais un **27B à 2,13 bits** (7,2 Go = la taille du fichier), abliteré, format `PQ2_0` non standard, dépôt sans téléchargements. Aucun intérêt ici.

### 3.2 Les principes du service qualité

Trois règles nées d'erreurs réelles, valables pour tout ce qu'on ajoutera :

1. **Ne rapporter que l'actionnable, et dire son périmètre.** Un rapport qui énumère l'inactionnable apprend à être ignoré ; un contrôle muet est indiscernable d'un contrôle qui n'a pas tourné.
2. **Détecteur et correcteur partagent leur source.** `ToleranceArrondiPx` pour la phase 1, `TermesInterditsPour()` pour la phase 2. Ils ont divergé une fois et une page à +1 mm était signalée sans pouvoir être réparée.
3. **Mesurer dans le moteur qui rend.** L'estimation en C# s'est trompée deux fois, dans les deux sens.

Et une règle de méthode : **quand un tic revient, chercher d'abord d'où il vient.** Trois tics du modèle ont été corrigés dans le prompt, à quatre lignes chacun, plutôt qu'en aval.

---

## 4. Le reste du backlog

- **Comparaison Qwen / Gemma bloc par bloc** — en cours côté médecin, aucune conclusion à tirer d'ici là.
- **Phase 2, couche modèle** : confrontations ciblées de deux sections (Synthèse ↔ Projet, 7.1→7.5 entre elles, Projet ↔ Feuille de route, Synthèse ↔ Conclusion).
- Chantiers de fond du dossier de restitution : patch v2 de la Synthèse Globale (en tête), inversion de la source de vérité du projet, 8 sphères contre 5 axes — voir `PLAN_RESTITUTION_V2_RESTE_A_FAIRE.md`.
- `PLAN_MED_100_LOCAL.md` étapes 1 à 6 ; `PLAN_MOTEUR_LLM_LOCAL.md` étapes 5 et 8 (retrait d'Ollama).
- `ff.pdf` à la racine : non suivi par git, **volontairement laissé hors de tous les commits**, préexistant.
