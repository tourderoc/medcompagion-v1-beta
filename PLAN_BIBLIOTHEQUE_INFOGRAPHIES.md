# Bibliothèque d'infographies

Ouvert le 22/09/2026. Remplace le §1.1 de `REPRISE_19_09.md`.

## Principe

Les infographies sont des **ressources rangées dans Med**, plus des fichiers éparpillés sur le poste. Deux usages, deux écrans :

- **Au Bureau (hors consultation)** : on prépare. Import, classement, relecture, validation.
- **En consultation** : on choisit et on imprime. Seules les fiches validées y apparaissent.

## Étape 1 — faite le 22/09/2026 (à essayer)

**Stockage** : `Documents\MedCompanion\bibliotheque\infographies\<id>\` avec `fiche.json` et `image.png`. Ce dossier est couvert par la sauvegarde vers E: (tout `Documents\MedCompanion` hors patients). Une fiche supprimée part dans `_corbeille\`, rien n'est effacé.

**Classement** : trois critères plutôt qu'une arborescence.
- **Type** : Trouble · Traitement · Conseils pratiques · Démarches · Hygiène de vie
- **Sujets** (plusieurs par fiche) : TDAH, Méthylphénidate…
- **Public** : Parents · Enfant · Adolescent · École

Les listes sont modifiables : une nouvelle valeur saisie sur une fiche devient un choix proposé. On ne touche pas au code pour ajouter une catégorie.

**Validation** : une fiche importée est « à relire ». Le médecin la valide depuis le Bureau. **Remplacer l'image la repasse « à relire »**, car c'est l'ancienne image qui avait été validée.

**Bureau → 🖼 Infographies** (`Views/Infographies/InfographiesAtelierControl`) : liste avec filtres et recherche, aperçu, fiche (titre, type, sujets, public, notes). Import par bouton ou par glisser-déposer, plusieurs fichiers à la fois, en PDF, PNG ou JPG. **Un PDF est converti en PNG** (première page, 300 ppp, via `PDFtoImage`), et le PDF d'origine est gardé sous `original.pdf`. Toute la bibliothèque travaille ainsi sur des images : miniatures, aperçu, impression et futur HTML de la Restitution. Un PDF de plusieurs pages est signalé dans les notes de la fiche.

**Pré-remplissage par Med** (`InfographieAnalyseService`) : à chaque import, et sur le bouton « ✨ Pré-remplir avec Med », le modèle vision local (Gemma 4 via llama.cpp, comme la lecture de la Cartographie) lit l'image et propose titre, type, sujets et public, en reprenant l'orthographe des catégories existantes. Il recopie aussi dans les notes, sans les juger, les affirmations chiffrées ou réglementaires à vérifier. Un OCR Tesseract a été écarté : les polices stylisées et le texte éparpillé des infographies le mettent en échec. La fiche reste « à relire ». Le 22/09, les 5 premières fiches ont été importées et validées.

**Consultation → carte « 🖼 Infographies » → 📚 Bibliothèque** (`InfographiesBibliothequeWindow`), à la place de la carte fictive « Points à évoquer » :
- miniatures, filtres, recherche sans accents (« ecole » trouve « École ») ;
- **Imprimer** : la page est orientée selon la forme de l'image (paysage pour NotebookLM), marge de 8 mm. « Microsoft Print to PDF » donne un PDF ;
- chaque impression est **notée dans le dossier du patient** (`info_patient/infographies_remises.json`), et la miniature affiche « Déjà remise le… ».

## Étape 2 — création par Med (à cadrer)

La chaîne visée, reprise du travail sur NotebookLM : recherche des sources → tri par le médecin → rédaction par le LLM local → mise en page.

**Avant d'écrire du code :**
1. **Essai de Qwen-Image 2.1** (sorti le 20/09/2026, 7B, ComfyUI) sur la 5060 Ti : INT8 ~7,3 Go + encodeur Qwen3-VL 8B compressé ~5 Go. Juger **les accents français**, la justesse du texte et le temps de génération.
2. **Lire la licence** : « Qwen Research License », qui pourrait interdire l'usage commercial.
3. Selon l'essai, choisir entre deux voies : **Qwen dessine toute l'infographie**, ou **Qwen ne fait que les illustrations** et Med pose le texte relu dans un gabarit HTML (zéro faute, style commun, traduction facile).

**Contraintes connues** : llama.cpp ne fait pas d'images, il faut ComfyUI (Python) ou stable-diffusion.cpp à côté. La 5060 Ti porte le LLM, donc il faudra décharger l'un pour charger l'autre. Aucune donnée patient n'est en jeu : la recherche peut passer par internet.

## Plus tard

- Joindre des fiches en **annexe du dossier de Restitution**, proposées selon le diagnostic ou le traitement retenu, jamais ajoutées sans le choix du médecin.
- Signaler les fiches validées depuis plus de deux ans (les recommandations évoluent).
- Critère « Langue » si des versions traduites arrivent.
