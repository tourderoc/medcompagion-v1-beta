# 🗺️ FEUILLE DE ROUTE REFACTORING - Option B (Minimal)

**Date de création** : 08/11/2025  
**Durée estimée** : 5-7 jours  
**Objectif** : Rendre le code maintenable sans risque majeur

---

## 📊 VUE D'ENSEMBLE

### Avant Refactoring
```
❌ MainWindow.xaml         3112 lignes (ingérable)
❌ MainWindow.xaml.cs      ~5000 lignes (ingérable)
⚠️ Code hybride            Ancien + Nouveau système
⚠️ Avertissements          107 warnings
```

### Après Refactoring (Option B)
```
✅ MainWindow.xaml         ~600 lignes (-80%)
✅ MainWindow.xaml.cs      ~600 lignes (-88%)
✅ 7 UserControls          ~300-500 lignes chacun
✅ 6 Partial Classes       ~400-700 lignes chacune
✅ Code propre             MVVM cohérent
✅ Maintenable             Fichiers gérables
```

---

## 🎯 PHILOSOPHIE

**"Quick Wins avec Impact Maximum"**

✅ Focus sur lisibilité et maintenabilité  
✅ Pas de réécriture massive (trop risqué)  
✅ Refactoring progressif et testé  
✅ Commits Git fréquents (sécurité)

---

## 📋 PHASE 1 : DÉCOUPAGE XAML (3-4 JOURS)

### 🎯 Objectif
MainWindow.xaml : **3112 lignes → 600 lignes (-80%)**

### 📅 JOUR 1 : UserControls avec ViewModel existant

#### Matin : NotesControl.xaml (~3-4h)

**Actions** :
1. Créer dossier `MedCompanion/Views/Notes/`
2. Créer `NotesControl.xaml` + `NotesControl.xaml.cs`
3. Extraire section Notes de MainWindow.xaml (lignes ~XXX-XXX)
4. Configurer DataContext : `{Binding NoteViewModel}`

**Contenu à extraire** :
- Zone "Note brute" (TextBox)
- Zone "Note structurée" (RichTextBox)
- Boutons : Structurer, Modifier, Sauvegarder, Supprimer, Annuler
- Liste des notes (ListBox)
- Synthèse patient (Border)

**Code NotesControl.xaml.cs** :
```csharp
namespace MedCompanion.Views.Notes
{
    public partial class NotesControl : UserControl
    {
        public NotesControl()
        {
            InitializeComponent();
        }
    }
}
```

**Intégration dans MainWindow.xaml** :
```xml
<Window xmlns:notes="clr-namespace:MedCompanion.Views.Notes">
    <!-- ... -->
    <notes:NotesControl DataContext="{Binding NoteViewModel}"/>
</Window>
```

**Checklist** :
- [ ] Créer fichiers NotesControl.xaml + .cs
- [ ] Copier XAML (section Notes complète)
- [ ] Ajouter namespace dans MainWindow.xaml
- [ ] Remplacer section par `<notes:NotesControl/>`
- [ ] Compiler : `dotnet build MedCompanion/MedCompanion.csproj`
- [ ] Tester : Ouvrir patient, créer note, structurer, sauvegarder
- [ ] Commit : `git commit -m "refactor: Extract NotesControl UserControl"`

---

#### Après-midi : PatientSearchControl.xaml (~2-3h)

**Actions** :
1. Créer dossier `MedCompanion/Views/Patient/`
2. Créer `PatientSearchControl.xaml` + `PatientSearchControl.xaml.cs`
3. Extraire barre recherche de MainWindow.xaml

**Contenu à extraire** :
- SearchBox avec placeholder
- Popup suggestions
- Bouton "Valider"
- Bouton "Créer patient"

**Intégration dans MainWindow.xaml** :
```xml
<Window xmlns:patient="clr-namespace:MedCompanion.Views.Patient">
    <!-- ... -->
    <patient:PatientSearchControl DataContext="{Binding PatientSearchViewModel}"/>
</Window>
```

**Checklist** :
- [ ] Créer fichiers PatientSearchControl.xaml + .cs
- [ ] Copier section recherche patient
- [ ] Intégrer dans MainWindow.xaml
- [ ] Compiler et tester recherche
- [ ] Commit : `git commit -m "refactor: Extract PatientSearchControl UserControl"`

---

### 📅 JOUR 2 : UserControls avec ViewModel existant (suite)

#### Matin : OrdonnancesControl.xaml (~2-3h)

**Actions** :
1. Créer dossier `MedCompanion/Views/Ordonnances/`
2. Créer `OrdonnancesControl.xaml` + `OrdonnancesControl.xaml.cs`

**Contenu à extraire** :
- Boutons "Médicaments" / "IDE"
- Liste ordonnances (ListBox)
- Zone prévisualisation (RichTextBox)
- Boutons actions (Supprimer, Ouvrir)

**Checklist** :
- [ ] Créer fichiers OrdonnancesControl
- [ ] Extraire section Ordonnances
- [ ] DataContext : `{Binding OrdonnanceViewModel}`
- [ ] Compiler et tester
- [ ] Commit : `git commit -m "refactor: Extract OrdonnancesControl UserControl"`

---

#### Après-midi : AttestationsControl.xaml (~3h)

**Actions** :
1. Créer dossier `MedCompanion/Views/Attestations/`
2. Créer `AttestationsControl.xaml` + `AttestationsControl.xaml.cs`

**Contenu à extraire** :
- ComboBox type attestation
- Boutons génération (normal + IA)
- Liste attestations
- Zone prévisualisation
- Boutons actions

**Checklist** :
- [ ] Créer fichiers AttestationsControl
- [ ] Extraire section Attestations
- [ ] DataContext : `{Binding AttestationViewModel}`
- [ ] Compiler et tester
- [ ] Commit : `git commit -m "refactor: Extract AttestationsControl UserControl"`

---

### 📅 JOUR 3 : UserControls sans ViewModel

#### Matin : DocumentsControl.xaml (~3-4h)

**Actions** :
1. Créer dossier `MedCompanion/Views/Documents/`
2. Créer `DocumentsControl.xaml` + `DocumentsControl.xaml.cs`

**Contenu à extraire** :
- Zone drag & drop
- DataGrid documents
- Liste catégories
- Zone synthèse document
- Boutons actions

**Note** : Pas de ViewModel, garder code-behind pour l'instant

**Checklist** :
- [ ] Créer fichiers DocumentsControl
- [ ] Extraire section Documents
- [ ] Copier event handlers dans .xaml.cs
- [ ] Compiler et tester drag & drop
- [ ] Commit : `git commit -m "refactor: Extract DocumentsControl UserControl"`

---

#### Après-midi : LettersControl.xaml (~2-3h)

**Actions** :
1. Créer dossier `MedCompanion/Views/Letters/`
2. Créer `LettersControl.xaml` + `LettersControl.xaml.cs`

**Contenu à extraire** :
- ComboBox modèle courrier
- Toggle "Adaptation IA"
- Zone édition (RichTextBox)
- Liste courriers
- Boutons actions

**Checklist** :
- [ ] Créer fichiers LettersControl
- [ ] Extraire section Courriers
- [ ] Copier event handlers
- [ ] Compiler et tester
- [ ] Commit : `git commit -m "refactor: Extract LettersControl UserControl"`

---

### 📅 JOUR 4 : UserControls finaux + Tests

#### Matin : ChatControl.xaml (~2-3h)

**Actions** :
1. Créer dossier `MedCompanion/Views/Chat/`
2. Créer `ChatControl.xaml` + `ChatControl.xaml.cs`

**Contenu à extraire** :
- Zone messages chat (StackPanel)
- TextBox saisie
- Bouton "Envoyer"
- Liste échanges sauvegardés
- Bannière suggestions

**Checklist** :
- [ ] Créer fichiers ChatControl
- [ ] Extraire section Discussion
- [ ] Copier event handlers
- [ ] Compiler et tester chat
- [ ] Commit : `git commit -m "refactor: Extract ChatControl UserControl"`

---

#### Après-midi : Tests complets + Nettoyage (~3h)

**Actions** :
1. Tester TOUTES les fonctionnalités
2. Vérifier MainWindow.xaml (~600 lignes maintenant)
3. Nettoyer code commenté
4. Organiser namespaces

**Checklist complète** :
- [ ] Recherche patient fonctionne
- [ ] Création patient fonctionne
- [ ] Notes : création, structuration, sauvegarde
- [ ] Courriers : création, modification, impression
- [ ] Attestations : génération, modification
- [ ] Documents : drag & drop, synthèse
- [ ] Chat : messages, sauvegarde échanges
- [ ] Ordonnances : création, visualisation
- [ ] Performance : Pas de ralentissement
- [ ] Designer Visual Studio : Réactif

**Commit final Phase 1** :
```bash
git add .
git commit -m "refactor: Complete XAML UserControls extraction - MainWindow.xaml 3112→600 lines"
```

---

## 📋 PHASE 2 : DÉCOUPAGE CODE-BEHIND (2-3 JOURS)

### 🎯 Objectif
MainWindow.xaml.cs : **~5000 lignes → 600 lignes (-88%)**

### 📅 JOUR 5 : Partial Classes (Formulaires + Ordonnances)

#### Matin : MainWindow.Formulaires.cs (~2h)

**Actions** :
1. Créer fichier `MedCompanion/MainWindow.Formulaires.cs`

**Méthodes à déplacer** :
- FormulaireTypeCombo_SelectionChanged
- PreremplirFormulaireButton_Click
- LoadPatientFormulaires
- FormulairesList_MouseDoubleClick
- FormulairesList_SelectionChanged
- SupprimerFormulaireButton_Click
- OuvrirModelePAIButton_Click

**Structure** :
```csharp
namespace MedCompanion
{
    public partial class MainWindow : Window
    {
        // Méthodes Formulaires ici
    }
}
```

**Checklist** :
- [ ] Créer MainWindow.Formulaires.cs
- [ ] Déplacer méthodes formulaires
- [ ] Supprimer de MainWindow.xaml.cs
- [ ] Compiler : `dotnet build`
- [ ] Tester fonctionnalité formulaires
- [ ] Commit : `git commit -m "refactor: Extract formulaires methods to partial class"`

---

#### Après-midi : MainWindow.Ordonnances.cs (~2h)

**Actions** :
1. Créer fichier `MedCompanion/MainWindow.Ordonnances.cs`

**Méthodes à déplacer** :
- IDEOrdonnanceButton_Click
- OrdonnancesList_SelectionChanged
- OrdonnancesList_MouseDoubleClick
- SupprimerOrdonnanceButton_Click
- ImprimerOrdonnanceButton_Click
- LoadPatientOrdonnances (si existe)

**Checklist** :
- [ ] Créer MainWindow.Ordonnances.cs
- [ ] Déplacer méthodes ordonnances
- [ ] Compiler et tester
- [ ] Commit : `git commit -m "refactor: Extract ordonnances methods to partial class"`

---

### 📅 JOUR 6 : Partial Classes (LLM + Attestations)

#### Matin : MainWindow.LLM.cs (~3h)

**Actions** :
1. Créer fichier `MedCompanion/MainWindow.LLM.cs`

**Méthodes à déplacer** :
- ChatInput_KeyDown
- ChatInput_TextChanged
- ChatSendBtn_Click
- LoadSavedExchanges
- ViewSavedExchangeBtn_Click
- DeleteSavedExchangeBtn_Click
- SaveExchangeButton_Click
- RefreshSavedExchangesList
- UpdateMemoryIndicator
- ShowSuggestionBanner
- HideSuggestionBanner
- CloseSuggestionBtn_Click
- IgnoreSuggestionBtn_Click
- ChooseTemplateBtn_Click
- LetterFromChatBtn_Click
- LLMModelCombo_SelectionChanged

**Checklist** :
- [ ] Créer MainWindow.LLM.cs
- [ ] Déplacer méthodes chat/IA
- [ ] Compiler et tester chat complet
- [ ] Commit : `git commit -m "refactor: Extract LLM/chat methods to partial class"`

---

#### Après-midi : MainWindow.Attestations.cs (~2h)

**Actions** :
1. Créer fichier `MedCompanion/MainWindow.Attestations.cs`

**Méthodes à déplacer** :
- AttestationTypeCombo_SelectionChanged
- GenererAttestationButton_Click
- GenerateCustomAttestationButton_Click
- AttestationsList_SelectionChanged
- AttestationsList_MouseDoubleClick
- ModifierAttestationButton_Click
- SupprimerAttestationButton_Click
- SauvegarderAttestationButton_Click
- AnnulerAttestationButton_Click
- ImprimerAttestationButton_Click
- RefreshAttestationsList

**Checklist** :
- [ ] Créer MainWindow.Attestations.cs
- [ ] Déplacer méthodes attestations
- [ ] Compiler et tester
- [ ] Commit : `git commit -m "refactor: Extract attestations methods to partial class"`

---

### 📅 JOUR 7 : Nettoyage Final + Documentation

#### Matin : Vérification MainWindow.xaml.cs (~2h)

**Actions** :
1. Vérifier contenu restant dans MainWindow.xaml.cs
2. S'assurer qu'il reste uniquement :
   - Champs privés (services, ViewModels, variables)
   - Constructeur
   - InitializeComponent()
   - WireSearchEvents()
   - Méthodes utilitaires UI (ParseMarkdown, etc.)
   - Classe PatientDisplayInfo

**Checklist** :
- [ ] MainWindow.xaml.cs ~600 lignes
- [ ] Tous les champs privés présents
- [ ] Constructeur propre
- [ ] Pas de méthodes métier (tout dans partial classes)

---

#### Après-midi : Tests finaux + Documentation (~3h)

**Tests complets** :
- [ ] Recherche patient
- [ ] Création patient
- [ ] Notes complètes
- [ ] Courriers complets
- [ ] Attestations complètes
- [ ] Documents drag & drop + synthèse
- [ ] Formulaires PAI/MDPH
- [ ] Ordonnances IDE
- [ ] Chat IA
- [ ] Synthèse patient
- [ ] Pas de régression fonctionnelle

**Documentation** :
- [ ] Mettre à jour MVVM_MIGRATION_ROADMAP.md
- [ ] Noter difficultés rencontrées
- [ ] Documenter architecture finale

**Commit final Phase 2** :
```bash
git add .
git commit -m "refactor: Complete code-behind partial classes - MainWindow.xaml.cs 5000→600 lines"
```

---

## 📊 RÉSULTAT FINAL

### Structure Finale du Projet

```
MedCompanion/
├── MainWindow.xaml (600 lignes ✅)
├── MainWindow.xaml.cs (600 lignes ✅)
├── MainWindow.Patient.cs (700 lignes) ✅ Déjà fait
├── MainWindow.Documents.cs (600 lignes) ✅ Déjà fait
├── MainWindow.Formulaires.cs (400 lignes) ⏳
├── MainWindow.Ordonnances.cs (400 lignes) ⏳
├── MainWindow.LLM.cs (500 lignes) ⏳
├── MainWindow.Attestations.cs (500 lignes) ⏳
│
├── Views/
│   ├── Notes/
│   │   └── NotesControl.xaml + .cs (500 lignes) ⏳
│   ├── Patient/
│   │   └── PatientSearchControl.xaml + .cs (300 lignes) ⏳
│   ├── Ordonnances/
│   │   └── OrdonnancesControl.xaml + .cs (300 lignes) ⏳
│   ├── Attestations/
│   │   └── AttestationsControl.xaml + .cs (400 lignes) ⏳
│   ├── Documents/
│   │   └── DocumentsControl.xaml + .cs (500 lignes) ⏳
│   ├── Letters/
│   │   └── LettersControl.xaml + .cs (400 lignes) ⏳
│   └── Chat/
│       └── ChatControl.xaml + .cs (400 lignes) ⏳
```

---

## ✅ CHECKLIST GLOBALE

### Phase 1 : Découpage XAML (3-4 jours)
- [ ] NotesControl.xaml
- [ ] PatientSearchControl.xaml
- [ ] OrdonnancesControl.xaml
- [ ] AttestationsControl.xaml
- [ ] DocumentsControl.xaml
- [ ] LettersControl.xaml
- [ ] ChatControl.xaml
- [ ] Tests complets
- [ ] MainWindow.xaml < 700 lignes

### Phase 2 : Découpage Code-Behind (2-3 jours)
- [ ] MainWindow.Formulaires.cs
- [ ] MainWindow.Ordonnances.cs
- [ ] MainWindow.LLM.cs
- [ ] MainWindow.Attestations.cs
- [ ] Tests complets
- [ ] MainWindow.xaml.cs < 700 lignes

### Validation Finale
- [ ] Compilation sans erreur
- [ ] Toutes fonctionnalités testées
- [ ] Pas de régression
- [ ] Designer Visual Studio réactif
- [ ] Code maintenable (fichiers < 700 lignes)
- [ ] Documentation à jour

---

## 🎯 BÉNÉFICES OBTENUS

### Maintenabilité
✅ Fichiers de taille raisonnable (< 700 lignes)  
✅ Organisation logique par fonctionnalité  
✅ Navigation facile dans le code

### Performance
✅ Designer Visual Studio réactif  
✅ Compilation plus rapide  
✅ Recherche dans fichiers plus efficace

### Collaboration
✅ Moins de conflits Git  
✅ Plusieurs développeurs peuvent travailler simultanément  
✅ Code review plus facile

### Évolutivité
✅ Ajout de fonctionnalités plus simple  
✅ UserControls réutilisables  
✅ Tests unitaires possibles (sur UserControls)

---

## ⚠️ POINTS D'ATTENTION

### Pendant le Refactoring
1. **Toujours compiler** après chaque modification
2. **Tester immédiatement** la fonctionnalité modifiée
3. **Commit fréquents** (sécurité)
4. **Ne pas mélanger** refactoring et nouvelles fonctionnalités

### Risques Identifiés
1. **DataContext perdu** → Vérifier bindings dans UserControls
2. **Event handlers cassés** → Tester toutes les actions utilisateur
3. **Références circulaires** → Éviter dépendances entre UserControls
4. **Performance** → Vérifier pas de ralentissement après extraction

---

## 🚀 APRÈS LE REFACTORING

### Option A : Continuer amélioration (Si temps disponible)
- Centraliser styles (Phase 4)
- Corriger avertissements (Phase 5)
- Créer ViewModels manquants

### Option B : Nouvelles fonctionnalités (Recommandé)
- Code maintenant maintenable
- Fichiers de taille acceptable
- Prêt pour évoluer

---

## 📝 COMMANDES GIT UTILES

```bash
# Avant de commencer
git status
git branch refactoring-option-b
git checkout refactoring-option-b

# Après chaque UserControl
git add .
git commit -m "refactor: Extract XXXControl UserControl"

# Après chaque Partial Class
git add .
git commit -m "refactor: Extract XXX methods to partial class"

# Fin de journée
git push origin refactoring-option-b

# À la fin (merge dans main)
git checkout main
git merge refactoring-option-b
git push origin main
```

---

## 🎉 CONCLUSION

**Durée totale** : 5-7 jours  
**Impact** : Code maintenable et professionnel  
**Risque** : Faible (refactoring progressif)  

**Résultat** :
- MainWindow.xaml : 3112 → 600 lignes (-80%)
- MainWindow.xaml.cs : 5000 → 600 lignes (-88%)
- Architecture propre et maintenable
- Prêt pour évolution future

**Prochaine étape** : Commencer Phase 1, Jour 1 - NotesControl.xaml

---

**Date de création** : 08/11/2025  
**Dernière mise à jour** : 08/11/2025  
**Maintenu par** : Équipe MedCompanion
