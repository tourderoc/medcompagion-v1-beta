# Étape 1 - Découpage Attestations

## ✅ Fichier créé
- **MainWindow.Attestations.cs** (606 lignes)

## 🗑️ Méthodes à supprimer de MainWindow.xaml.cs

D'après les erreurs de compilation (lignes approximatives):

1. **Ligne ~3061** : `GenerateCustomAttestationButton_Click` (~130 lignes)
2. **Ligne ~3177** : `AttestationTypeCombo_SelectionChanged` (~30 lignes)  
3. **Ligne ~3208** : `GenererAttestationButton_Click` (~100 lignes)
4. **Ligne ~3308** : `AttestationsList_SelectionChanged` (~50 lignes)
5. **Ligne ~3359** : `AttestationsList_MouseDoubleClick` (~40 lignes)
6. **Ligne ~3399** : `ModifierAttestationButton_Click` (~35 lignes)
7. **Ligne ~3436** : `OuvrirAttestationButton_Click` (~20 lignes)
8. **Ligne ~3460** : `SupprimerAttestationButton_Click` (~40 lignes)
9. **Ligne ~3501** : `ImprimerAttestationButton_Click` (~25 lignes)
10. **Ligne ~3527** : `SauvegarderAttestationModifiee` (~60 lignes)
11. **Ligne ~3588** : `RefreshAttestationsList` (~20 lignes)

**Total à supprimer** : ~550 lignes

## 📊 Résultat attendu
- MainWindow.xaml.cs : 5473 - 550 = ~4920 lignes
- MainWindow.Attestations.cs : 606 lignes
- **Total** : ~5526 lignes (légère augmentation due aux headers/usings)
