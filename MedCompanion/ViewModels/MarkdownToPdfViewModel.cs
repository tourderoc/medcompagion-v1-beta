using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using MedCompanion.Services;
using MedCompanion.Models;
using MedCompanion.Commands;
using MedCompanion.Helpers;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// ViewModel pour la conversion Markdown vers PDF
    /// </summary>
    public class MarkdownToPdfViewModel : ObservableObject
    {
        private readonly MarkdownToPdfService _markdownToPdfService;
        private readonly StorageService _storageService;
        private readonly PathService _pathService;

        private string _markdownContent = string.Empty;
        private string _outputPath = string.Empty;
        private OrdonnanceType _ordonnanceType = OrdonnanceType.Medicaments;
        private bool _isConverting = false;

        public event EventHandler<string>? StatusChanged;

        // Propriétés pour l'interface
        public string MarkdownContent
        {
            get => _markdownContent;
            set
            {
                _markdownContent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanConvert));
            }
        }

        public string OutputPath
        {
            get => _outputPath;
            set
            {
                _outputPath = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanConvert));
            }
        }

        public OrdonnanceType OrdonnanceType
        {
            get => _ordonnanceType;
            set
            {
                _ordonnanceType = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanConvert));
            }
        }

        public bool CanConvert => !string.IsNullOrWhiteSpace(_markdownContent) && !string.IsNullOrWhiteSpace(_outputPath);

        public bool IsConverting
        {
            get => _isConverting;
            private set
            {
                _isConverting = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanConvert));
            }
        }

        // Commandes
        public ICommand ConvertToPdfCommand { get; }

        public ICommand BrowseOutputPathCommand { get; }

        public MarkdownToPdfViewModel(
            MarkdownToPdfService markdownToPdfService,
            StorageService storageService,
            PathService pathService)
        {
            _markdownToPdfService = markdownToPdfService;
            _storageService = storageService;
            _pathService = pathService;

            ConvertToPdfCommand = new RelayCommand(async _ => await ConvertToPdfAsync(), _ => CanConvert);
            BrowseOutputPathCommand = new RelayCommand(BrowseOutputPath);
        }

        /// <summary>
        /// Convertit le Markdown en PDF de manière asynchrone
        /// </summary>
        private async Task ConvertToPdfAsync()
        {
            if (string.IsNullOrWhiteSpace(_markdownContent) || string.IsNullOrWhiteSpace(_outputPath))
                return;

            try
            {
                IsConverting = true;
                StatusChanged?.Invoke(this, "🔄 Conversion Markdown vers PDF en cours...");

                // Obtenir les métadonnées patient si possible
                PatientMetadata? patientMetadata = null;
                var fileName = Path.GetFileNameWithoutExtension(_outputPath);
                
                // Extraire le nom du patient depuis le chemin de sortie
                var patientName = ExtractPatientNameFromPath(_outputPath);
                if (!string.IsNullOrEmpty(patientName))
                {
                    var patientDir = _pathService.GetPatientRootDirectory(patientName);
                    var patientJsonPath = _pathService.GetPatientJsonPath(patientName);
                    
                    if (File.Exists(patientJsonPath))
                    {
                        try
                        {
                            var json = File.ReadAllText(patientJsonPath);
                            patientMetadata = System.Text.Json.JsonSerializer.Deserialize<PatientMetadata>(json);
                        }
                        catch { }
                    }
                }

                // Convertir en PDF
                var (success, pdfPath, error) = await _markdownToPdfService.ConvertMarkdownToPdfAsync(
                    _markdownContent,
                    _outputPath,
                    patientMetadata,
                    _ordonnanceType
                );

                if (success && !string.IsNullOrEmpty(pdfPath))
                {
                    StatusChanged?.Invoke(this, $"✅ PDF généré avec succès : {Path.GetFileName(pdfPath)}");
                    
                    // Ouvrir le dossier contenant le PDF
                    var folder = Path.GetDirectoryName(pdfPath);
                    if (!string.IsNullOrEmpty(folder))
                    {
                        System.Diagnostics.Process.Start("explorer.exe", folder);
                    }
                }
                else
                {
                    StatusChanged?.Invoke(this, $"❌ Erreur lors de la conversion : {error}");
                }
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"❌ Erreur inattendue : {ex.Message}");
            }
            finally
            {
                IsConverting = false;
            }
        }

        /// <summary>
        /// Ouvre une boîte de dialogue pour sélectionner le chemin de sortie
        /// </summary>
        private void BrowseOutputPath()
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Sauvegarder le PDF",
                Filter = "Fichiers PDF|*.pdf",
                DefaultExt = "pdf",
                FileName = Path.GetFileName(_outputPath)
            };

            if (dialog.ShowDialog() == true)
            {
                OutputPath = dialog.FileName;
            }
        }

        /// <summary>
        /// Extrait le nom du patient depuis un chemin de fichier
        /// </summary>
        private string ExtractPatientNameFromPath(string path)
        {
            // Chercher un pattern comme "ordonnances/NOM_PATIENT_"
            var fileName = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(fileName))
                return string.Empty;

            // Pattern pour extraire le nom du patient
            var patterns = new[]
            {
                @"ORDONNANCE\\(.+?)_Ordonnance_",
                @"ordonnances\\(.+?)_IDE_Ordonnance_",
                @"ordonnances\\(.+?)_BIO_Ordonnance_",
                @"ordonnances\\(.+?)_Med_Ordonnance_"
            };

            foreach (var pattern in patterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(fileName, pattern);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }

            return string.Empty;
        }
    }
}
