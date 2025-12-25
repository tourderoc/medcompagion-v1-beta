using System.Configuration;
using System.Data;
using System.Windows;
using MedCompanion.Dialogs;
using MedCompanion.Services;

namespace MedCompanion;

/// <summary>
/// Interaction logic for App.xaml
/// Gère le flux d'authentification au démarrage
/// </summary>
public partial class App : Application
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        var authService = new AuthenticationService();

        // Cas 1 : Première utilisation - Afficher l'assistant de configuration
        if (authService.IsFirstLaunch)
        {
            System.Diagnostics.Debug.WriteLine("[App] Première utilisation - Affichage SetupWizard");

            var setupWizard = new SetupWizardWindow(authService);
            var result = setupWizard.ShowDialog();

            if (result != true || !setupWizard.IsSetupComplete)
            {
                // L'utilisateur a fermé sans configurer
                Shutdown();
                return;
            }

            // Recharger l'état après configuration (l'utilisateur peut avoir désactivé l'auth)
            authService = new AuthenticationService();
        }

        // Cas 2 : Authentification désactivée - Ouvrir directement MainWindow
        if (!authService.IsAuthenticationEnabled)
        {
            System.Diagnostics.Debug.WriteLine("[App] Auth désactivée - Ouverture MainWindow directe");
            ShowMainWindow();
            return;
        }

        // Cas 3 : Authentification requise - Afficher LoginWindow
        System.Diagnostics.Debug.WriteLine($"[App] Auth requise - Password: {authService.IsPasswordRequired}");

        var loginWindow = new LoginWindow(authService);
        var loginResult = loginWindow.ShowDialog();

        if (loginResult == true && loginWindow.IsAuthenticated)
        {
            System.Diagnostics.Debug.WriteLine("[App] Authentification réussie");
            ShowMainWindow();
        }
        else
        {
            // L'utilisateur a fermé sans s'authentifier
            System.Diagnostics.Debug.WriteLine("[App] Authentification annulée");
            Shutdown();
        }
    }

    private void ShowMainWindow()
    {
        var mainWindow = new MainWindow();
        // Définir MainWindow comme fenêtre principale de l'application
        MainWindow = mainWindow;

        // Fermer l'application quand MainWindow est fermée
        mainWindow.Closed += (s, e) => Shutdown();

        mainWindow.Show();
    }
}
