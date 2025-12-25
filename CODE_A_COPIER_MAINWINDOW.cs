// ═══════════════════════════════════════════════════════════════════
// CODE À AJOUTER DANS MainWindow.xaml.cs
// ═══════════════════════════════════════════════════════════════════
//
// Instructions :
// 1. Ouvrir : MedCompanion/MainWindow.xaml.cs
// 2. Ajouter le code ci-dessous dans le constructeur et après le constructeur
//
// ═══════════════════════════════════════════════════════════════════

// ┌─────────────────────────────────────────────────────────────────┐
// │ PARTIE 1 : Dans le constructeur MainWindow()                    │
// │ À ajouter APRÈS InitializeComponent();                          │
// └─────────────────────────────────────────────────────────────────┘

public MainWindow()
{
    InitializeComponent();

    _settings = new AppSettings();
    _pathService = new PathService();
    // ... code existant ...

    // ┌─────────────────────────────────────────────────────────┐
    // │ ✅ AJOUTER ICI : Raccourci F12 pour test anonymisation │
    // └─────────────────────────────────────────────────────────┘

    // Raccourci F12 pour ouvrir le test d'anonymisation (DEV ONLY)
    this.KeyDown += (s, e) =>
    {
        if (e.Key == Key.F12)
        {
            OpenAnonymizationTestDialog();
            e.Handled = true;
        }
    };

    // ... reste du code existant ...
}


// ┌─────────────────────────────────────────────────────────────────┐
// │ PARTIE 2 : Nouvelle méthode                                     │
// │ À ajouter APRÈS le constructeur (vers ligne ~200-300)           │
// └─────────────────────────────────────────────────────────────────┘

/// <summary>
/// Ouvre le dialogue de test de l'anonymisation Phase 3 (DEV ONLY)
/// Permet de tester le déroulement complet des 3 phases avec logs détaillés
/// </summary>
private void OpenAnonymizationTestDialog()
{
    try
    {
        var testDialog = new Dialogs.AnonymizationTestDialog(
            _anonymizationService,
            _settings
        );
        testDialog.Owner = this;
        testDialog.ShowDialog();
    }
    catch (Exception ex)
    {
        MessageBox.Show(
            $"Erreur lors de l'ouverture du test d'anonymisation :\n\n{ex.Message}",
            "Test Anonymisation",
            MessageBoxButton.OK,
            MessageBoxImage.Error
        );
    }
}


// ═══════════════════════════════════════════════════════════════════
// FIN DU CODE À AJOUTER
// ═══════════════════════════════════════════════════════════════════
//
// Après avoir ajouté ce code :
//
// 1. Recompiler : dotnet build medcompagnio2.sln
// 2. Lancer l'app : dotnet run --project MedCompanion/MedCompanion.csproj
// 3. Appuyer sur F12 pour ouvrir le dialogue de test
//
// ═══════════════════════════════════════════════════════════════════
