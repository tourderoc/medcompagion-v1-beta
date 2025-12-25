using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service d'intégration avec CZUR Lens pour scanner des documents
    /// </summary>
    public class ScannerService
    {
        private readonly PathService _pathService;
        private const string CzurLensPath = @"C:\Program Files (x86)\CZUR Lens\CZUR Lens.exe";
        private const string DefaultCzurOutputFolder = @"C:\Users\Public\Documents\CZUR\CZUR Lens";
        private string? _tempScanFolder;
        private FileSystemWatcher? _fileWatcher;

        public event EventHandler<string>? NewDocumentDetected;

        public ScannerService(PathService pathService)
        {
            _pathService = pathService;
        }

        /// <summary>
        /// Vérifie si CZUR Lens est installé
        /// </summary>
        public bool IsCzurLensInstalled()
        {
            return File.Exists(CzurLensPath);
        }

        /// <summary>
        /// Crée un dossier temporaire pour les scans
        /// </summary>
        public string CreateTempScanFolder()
        {
            var tempPath = Path.Combine(Path.GetTempPath(), "MedCompanion_Scans", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            if (!Directory.Exists(tempPath))
            {
                Directory.CreateDirectory(tempPath);
            }
            _tempScanFolder = tempPath;
            return tempPath;
        }

        /// <summary>
        /// Lance CZUR Lens
        /// </summary>
        public (bool success, string message) LaunchCzurLens()
        {
            try
            {
                if (!IsCzurLensInstalled())
                {
                    return (false, "CZUR Lens n'est pas installé.\nChemin attendu: " + CzurLensPath);
                }

                var processInfo = new ProcessStartInfo
                {
                    FileName = CzurLensPath,
                    UseShellExecute = true
                };

                Process.Start(processInfo);
                return (true, "CZUR Lens lancé avec succès");
            }
            catch (Exception ex)
            {
                return (false, $"Erreur lors du lancement de CZUR Lens: {ex.Message}");
            }
        }

        /// <summary>
        /// Surveille un dossier pour détecter les nouveaux fichiers PDF
        /// </summary>
        public async Task<string?> WaitForScannedDocumentAsync(string folderPath, CancellationToken cancellationToken, int timeoutSeconds = 300)
        {
            try
            {
                // Obtenir la liste des fichiers existants avant le scan
                var existingFiles = Directory.Exists(folderPath)
                    ? Directory.GetFiles(folderPath, "*.pdf").ToHashSet()
                    : new HashSet<string>();

                var endTime = DateTime.Now.AddSeconds(timeoutSeconds);

                while (DateTime.Now < endTime && !cancellationToken.IsCancellationRequested)
                {
                    if (Directory.Exists(folderPath))
                    {
                        var currentFiles = Directory.GetFiles(folderPath, "*.pdf");

                        // Chercher un nouveau fichier
                        foreach (var file in currentFiles)
                        {
                            if (!existingFiles.Contains(file))
                            {
                                // Attendre que le fichier soit complètement écrit
                                await Task.Delay(1000, cancellationToken);

                                // Vérifier que le fichier n'est pas verrouillé
                                if (IsFileReady(file))
                                {
                                    return file;
                                }
                            }
                        }
                    }

                    await Task.Delay(500, cancellationToken);
                }

                return null;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ScannerService] Erreur surveillance: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Vérifie si un fichier est prêt (pas verrouillé)
        /// </summary>
        private bool IsFileReady(string filePath)
        {
            try
            {
                using (var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    return stream.Length > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Nettoie le dossier temporaire
        /// </summary>
        public void CleanupTempFolder()
        {
            try
            {
                if (!string.IsNullOrEmpty(_tempScanFolder) && Directory.Exists(_tempScanFolder))
                {
                    Directory.Delete(_tempScanFolder, true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ScannerService] Erreur nettoyage: {ex.Message}");
            }
        }

        /// <summary>
        /// Obtient tous les fichiers PDF dans un dossier (pour sélection manuelle)
        /// </summary>
        public string[] GetScannedDocuments(string folderPath)
        {
            try
            {
                if (!Directory.Exists(folderPath))
                    return Array.Empty<string>();

                return Directory.GetFiles(folderPath, "*.pdf")
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Démarre la surveillance automatique d'un dossier pour détecter les nouveaux PDF
        /// </summary>
        public (bool success, string message) StartAutoMonitoring(string folderPath)
        {
            try
            {
                // Arrêter la surveillance précédente si elle existe
                StopAutoMonitoring();

                // Créer le dossier s'il n'existe pas
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                // Créer le FileSystemWatcher
                _fileWatcher = new FileSystemWatcher(folderPath)
                {
                    Filter = "*.pdf",
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                    EnableRaisingEvents = true
                };

                // S'abonner aux événements
                _fileWatcher.Created += OnFileCreated;
                _fileWatcher.Changed += OnFileChanged;

                return (true, $"Surveillance démarrée sur: {folderPath}");
            }
            catch (Exception ex)
            {
                return (false, $"Erreur démarrage surveillance: {ex.Message}");
            }
        }

        /// <summary>
        /// Arrête la surveillance automatique
        /// </summary>
        public void StopAutoMonitoring()
        {
            if (_fileWatcher != null)
            {
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.Created -= OnFileCreated;
                _fileWatcher.Changed -= OnFileChanged;
                _fileWatcher.Dispose();
                _fileWatcher = null;
            }
        }

        /// <summary>
        /// Gestionnaire d'événement pour nouveau fichier créé
        /// </summary>
        private async void OnFileCreated(object sender, FileSystemEventArgs e)
        {
            await HandleNewFile(e.FullPath);
        }

        /// <summary>
        /// Gestionnaire d'événement pour fichier modifié
        /// </summary>
        private async void OnFileChanged(object sender, FileSystemEventArgs e)
        {
            await HandleNewFile(e.FullPath);
        }

        /// <summary>
        /// Traite un nouveau fichier détecté
        /// </summary>
        private async Task HandleNewFile(string filePath)
        {
            try
            {
                // Attendre que le fichier soit complètement écrit
                await Task.Delay(1000);

                // Vérifier que le fichier est prêt
                if (IsFileReady(filePath) && File.Exists(filePath))
                {
                    // Notifier qu'un nouveau document a été détecté
                    NewDocumentDetected?.Invoke(this, filePath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ScannerService] Erreur traitement fichier: {ex.Message}");
            }
        }

        /// <summary>
        /// Obtient le dossier de sortie par défaut de CZUR Lens
        /// </summary>
        public string GetDefaultCzurOutputFolder()
        {
            // Essayer Bureau/lens en premier (usage courant)
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var lensPath = Path.Combine(desktopPath, "lens");

            if (Directory.Exists(lensPath))
            {
                return lensPath;
            }

            // Essayer le dossier par défaut CZUR
            if (Directory.Exists(DefaultCzurOutputFolder))
            {
                return DefaultCzurOutputFolder;
            }

            // Sinon, créer Bureau/lens
            if (!Directory.Exists(lensPath))
            {
                Directory.CreateDirectory(lensPath);
            }

            return lensPath;
        }
    }
}
