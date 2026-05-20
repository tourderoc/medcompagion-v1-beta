# Plan — Hub de consultations (frise chronologique)

> Refonte de la zone centrale du Mode Consultation : remplacer le ComboBox de
> type par une **frise chronologique horizontale** de consultations + un bouton
> **"+"** pour en créer de nouvelles.
>
> Statut : **plan validé, implémentation à démarrer**. Daté du 2026-05-20.

---

## 1. Vision

La zone centrale devient un **hub de consultations** qui reflète le parcours
clinique du patient dans le temps.

```
┌──────────────┐ ┌──────────────┐ ┌──────────────┐  ┌───┐
│ 🩺 1ère       │ │ 🔄 Suivi      │ │ 🔄 Suivi      │  │ + │
│ 02/12/2025   │ │ 15/01/2026   │ │ 20/05/2026   │  └───┘
└──────────────┘ └──────────────┘ └──────────────┘
```

- Chaque consultation = une **carte** juxtaposée horizontalement (chronologique)
- Toujours visible à l'ouverture du dossier patient
- Bouton **"+"** en fin de frise pour créer une nouvelle consultation
- Séparation des rôles : **centre = vue rapide/visuelle**, **dossier droite = détail textuel complet**

---

## 2. Décisions actées

| # | Décision | Note |
|---|----------|------|
| 1 | Remplacer le ComboBox par un bouton "+" | Exprime "créer un acte", pas "changer un réglage" |
| 2 | Contenu carte = **date + type uniquement** | Simple pour commencer |
| 3 | 2 types au menu : 1ère consultation / Suivi | 1ère = flux existant, Suivi = placeholder |
| 4 | Mode édition → frise réduite en **bandeau fin** | Libère la place pour la dictée + blocs |
| 5 | **Carte mentale = plus tard** | Niveau 1 (tuiles) puis niveau 2 (radiale) ultérieurement |
| 6 | **Flux Suivi = plus tard** | La carte se crée, message "à venir" pour l'instant |
| 7 | Clic sur carte passée → **ouvre la note en lecture** | La carte mentale viendra plus tard |
| 8 | **Confirmation** si consultation non sauvegardée en cours | "Abandonner la consultation en cours ?" |

---

## 3. États de la zone centrale

### État 1 — Dossier ouvert, rien en cours
```
Frise horizontale des consultations passées + bouton [+]
(si aucune consultation : juste le bouton [+] mis en avant)
```

### État 2 — Nouvelle consultation en édition
```
Frise réduite en bandeau fin de pastilles cliquables (en haut)
Zone d'édition (dictée + blocs + extraction) prend la place principale
```

### État 3 — Clic sur une consultation passée
```
Ouvre la note en lecture (pour l'instant)
[plus tard] → carte mentale de visualisation rapide
```

---

## 4. Architecture technique

### 4.1 Nouveau modèle — `ConsultationCardViewModel`

```csharp
public class ConsultationCardViewModel
{
    public DateTime Date      { get; set; }
    public string   Type      { get; set; }   // "1ère consultation" | "Suivi"
    public string   Icon      { get; set; }   // "🩺" | "🔄"
    public string   Title     { get; set; }   // titre de la note
    public string   FilePath  { get; set; }   // chemin du .md
    public bool     IsActive  { get; set; }   // consultation en cours d'édition
}
```

### 4.2 Chargement des consultations passées

Réutiliser `RefreshConsultationNotesAsync()` (déjà existant) qui lit les notes
du dossier patient. Alimente une nouvelle collection :

```csharp
public ObservableCollection<ConsultationCardViewModel> ConsultationCards { get; }
```

Déduction du type depuis le titre/nom de fichier :
- titre contient "Interrogatoire" ou "1ère" → **1ère consultation** (🩺)
- titre contient "Suivi" → **Suivi** (🔄)
- sinon → générique

### 4.3 ViewModel — ajouts

```csharp
public ObservableCollection<ConsultationCardViewModel> ConsultationCards { get; }
public bool IsEditingConsultation { get; set; }   // pilote frise pleine vs bandeau
public ICommand NewConsultationCommand { get; }   // param : "premiere" | "suivi"
public ICommand OpenCardCommand { get; }          // ouvre une carte passée

private void LoadConsultationCards();              // remplit la frise
private bool ConfirmAbandonIfEditing();            // confirmation si non sauvegardé
```

### 4.4 Logique `NewConsultationCommand`

```
Si IsEditingConsultation && note non sauvegardée :
    → MessageBox "Abandonner la consultation en cours ?"
    → si Non : annuler

Selon le type choisi :
  "premiere" :
    → InitInterrogatoireBlocks()  (flux existant)
    → IsEditingConsultation = true (frise → bandeau)
  "suivi" :
    → message "Flux consultation de suivi à venir" (placeholder)
```

---

## 5. UI (XAML)

### 5.1 Remplacer le ComboBox

Zone actuelle (`Type: [ComboBox]`) → remplacée par :
- un `ScrollViewer` horizontal contenant un `ItemsControl` (les cartes)
- un bouton "+" en fin

### 5.2 Carte (DataTemplate)
```
┌──────────────┐
│ 🩺            │
│ 1ère consult │
│ 02/12/2025   │
└──────────────┘
```
- Largeur ~130px, hauteur ~70px
- Bordure arrondie, fond clair
- Surbrillance si `IsActive`

### 5.3 Bouton "+" → menu
`Popup` ou `ContextMenu` avec 2 entrées :
- 🩺 1ère consultation
- 🔄 Consultation de suivi

### 5.4 Bandeau réduit (mode édition)
Quand `IsEditingConsultation == true` : la frise passe en hauteur réduite
(~30px), pastilles cliquables compactes.

---

## 6. Fichiers touchés

| Fichier | Action |
|---------|--------|
| `ViewModels/ConsultationCardViewModel.cs` | 🆕 nouveau |
| `ViewModels/ConsultationModeViewModel.cs` | + collection cartes, commandes, états |
| `Views/Consultation/ConsultationModeControl.xaml` | remplacer ComboBox par frise + "+" |
| `Views/Consultation/ConsultationModeControl.xaml.cs` | menu "+", handlers |

---

## 7. Étapes d'implémentation

1. Créer `ConsultationCardViewModel`
2. Ajouter `ConsultationCards` + `LoadConsultationCards()` dans le ViewModel
3. UI : frise horizontale (ItemsControl + ScrollViewer) à la place du ComboBox
4. Bouton "+" + menu (2 types)
5. `NewConsultationCommand` avec confirmation si édition en cours
6. États visuels : frise pleine ↔ bandeau (`IsEditingConsultation`)
7. Rebrancher le flux 1ère consultation sur le menu "+"
8. Placeholder "Suivi à venir"
9. Clic carte passée → ouverture note en lecture

---

## 8. Hors périmètre (étapes futures)

- **Carte mentale** (niveau 1 tuiles, puis niveau 2 radiale)
- **Flux complet Consultation de suivi** (blocs spécifiques + prompts + pré-chargement du résumé de la dernière consult)
- Mode "libre" (note manuelle simple) : à décider si on le garde comme 3ème option du "+"
