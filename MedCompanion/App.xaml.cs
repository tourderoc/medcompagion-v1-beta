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
    /// <summary>
    /// Journal des erreurs non rattrapées : %APPDATA%\MedCompanion\logs\crash.log
    /// </summary>
    private static readonly string CrashLogPath = System.IO.Path.Combine(
        System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
        "MedCompanion", "logs", "crash.log");

    /// <summary>
    /// Filet de sécurité global. Sans lui, une exception survenue hors du thread UI (tâche de fond,
    /// continuation asynchrone) termine le processus SANS rien écrire nulle part : ni journal
    /// Windows, ni message. Constaté sur la fermeture silencieuse du formulaire au clic OCR, où
    /// l'absence complète de trace rendait le diagnostic impossible.
    ///
    /// Les erreurs du thread UI sont marquées traitées : dans un usage clinique, perdre une saisie
    /// en cours coûte plus cher que de continuer dans un état dégradé. Elles restent journalisées
    /// et signalées — jamais avalées en silence.
    /// </summary>
    private void InstallGlobalErrorHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash("UI", args.Exception);
            MessageBox.Show(
                $"Une erreur inattendue est survenue :\n\n{args.Exception.Message}\n\n" +
                $"Le détail a été enregistré dans :\n{CrashLogPath}\n\n" +
                "L'application reste ouverte — enregistrez votre travail en cours.",
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        // Exception sur un thread de fond : le processus va mourir, on ne peut que tracer.
        System.AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogCrash("Fond", args.ExceptionObject as System.Exception);

        // Tâche dont l'exception n'a jamais été observée (async void, fire-and-forget).
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            LogCrash("Tâche", args.Exception);
            args.SetObserved();
        };
    }

    private static void LogCrash(string origin, System.Exception? ex)
    {
        try
        {
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(CrashLogPath)!);
            System.IO.File.AppendAllText(CrashLogPath,
                $"===== {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} [{origin}]====={System.Environment.NewLine}" +
                $"{ex}{System.Environment.NewLine}{System.Environment.NewLine}");
        }
        catch { /* journaliser ne doit jamais aggraver la situation */ }
    }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        InstallGlobalErrorHandlers();

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
