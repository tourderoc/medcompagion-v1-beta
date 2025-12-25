using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using MedCompanion.Services;

namespace MedCompanion.Dialogs
{
    public partial class ScanDocumentDialog : Window
    {
        // Windows API pour minimiser une fenêtre
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

        private const int SW_MINIMIZE = 6;

        private readonly ScannerService _scannerService;
        private string? _selectedFilePath;
        private string _monitoringFolder;

        public string? ScannedFilePath => _selectedFilePath;

        public ScanDocumentDialog(ScannerService scannerService)
        {
            InitializeComponent();
            _scannerService = scannerService;

            // Obtenir le dossier de surveillance par défaut
            _monitoringFolder = _scannerService.GetDefaultCzurOutputFolder();
            MonitoringFolderTextBox.Text = _monitoringFolder;

            // S'abonner à l'événement de nouveau document détecté
            _scannerService.NewDocumentDetected += OnNewDocumentDetected;

            // Lancer CZUR Lens et démarrer la surveillance
            Loaded += OnDialogLoaded;
        }

        private void OnDialogLoaded(object sender, RoutedEventArgs e)
        {
            // Lancer CZUR Lens
            var (success, message) = _scannerService.LaunchCzurLens();

            if (!success)
            {
                MessageBox.Show($"Impossible de lancer CZUR Lens:\n\n{message}",
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                DialogResult = false;
                Close();
                return;
            }

            // Démarrer la surveillance automatique
            var (monitorSuccess, monitorMessage) = _scannerService.StartAutoMonitoring(_monitoringFolder);

            if (!monitorSuccess)
            {
                MessageBox.Show($"Impossible de démarrer la surveillance:\n\n{monitorMessage}",
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                DialogResult = false;
                Close();
                return;
            }

            StatusText.Text = "✅ CZUR Lens lancé - Scannez votre document !";
        }

        private void OnNewDocumentDetected(object? sender, string filePath)
        {
            // S'assurer qu'on est sur le thread UI
            Dispatcher.Invoke(() =>
            {
                _selectedFilePath = filePath;

                StatusText.Text = $"✅ Document détecté: {Path.GetFileName(filePath)}";
                StatusText.Foreground = System.Windows.Media.Brushes.Green;

                // Minimiser CZUR Lens automatiquement
                MinimizeCzurLens();

                // Fermer le dialogue après un court délai
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };

                timer.Tick += (s, e) =>
                {
                    timer.Stop();

                    // Vérifier que la fenêtre est bien affichée et chargée
                    if (IsLoaded && IsVisible)
                    {
                        try
                        {
                            DialogResult = true;
                            Close();
                        }
                        catch (InvalidOperationException)
                        {
                            // Si DialogResult ne peut pas être défini, fermer simplement
                            Close();
                        }
                    }
                };

                timer.Start();
            });
        }

        /// <summary>
        /// Minimise la fenêtre CZUR Lens
        /// </summary>
        private void MinimizeCzurLens()
        {
            try
            {
                // Chercher le processus CZUR Lens
                var czurProcess = Process.GetProcesses()
                    .FirstOrDefault(p => p.ProcessName.Contains("CZUR", StringComparison.OrdinalIgnoreCase) ||
                                        p.MainWindowTitle.Contains("CZUR", StringComparison.OrdinalIgnoreCase));

                if (czurProcess != null && czurProcess.MainWindowHandle != IntPtr.Zero)
                {
                    // Minimiser la fenêtre
                    ShowWindow(czurProcess.MainWindowHandle, SW_MINIMIZE);
                }
            }
            catch
            {
                // Ignorer les erreurs de minimisation
            }
        }

        private void UpdateFolderButton_Click(object sender, RoutedEventArgs e)
        {
            var newFolder = MonitoringFolderTextBox.Text.Trim();

            if (string.IsNullOrEmpty(newFolder))
            {
                MessageBox.Show("Veuillez entrer un chemin de dossier valide.",
                    "Dossier invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!Directory.Exists(newFolder))
            {
                var result = MessageBox.Show($"Le dossier n'existe pas:\n{newFolder}\n\nVoulez-vous le créer ?",
                    "Créer le dossier ?", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        Directory.CreateDirectory(newFolder);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Impossible de créer le dossier:\n\n{ex.Message}",
                            "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                }
                else
                {
                    return;
                }
            }

            // Arrêter l'ancienne surveillance
            _scannerService.StopAutoMonitoring();

            // Mettre à jour le dossier
            _monitoringFolder = newFolder;

            // Redémarrer la surveillance sur le nouveau dossier
            var (success, message) = _scannerService.StartAutoMonitoring(_monitoringFolder);

            if (!success)
            {
                MessageBox.Show($"Impossible de surveiller ce dossier:\n\n{message}",
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            else
            {
                StatusText.Text = $"✅ Surveillance mise à jour - Scannez votre document !";
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            _selectedFilePath = null;
            DialogResult = false;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // Arrêter la surveillance
            _scannerService.StopAutoMonitoring();

            // Se désabonner de l'événement
            _scannerService.NewDocumentDetected -= OnNewDocumentDetected;
        }
    }
}
