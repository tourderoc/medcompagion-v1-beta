using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks;
using MedCompanion.Commands;
using MedCompanion.Services;

namespace MedCompanion.ViewModels
{
    public class BureauMedViewModel : INotifyPropertyChanged
    {
        private MedAgentService? _medAgentService;
        private ScreenCaptureService _captureService;
        private string _selectedTool = "Firefox";
        private bool _isAnalyzing;
        private bool _isMovingWindow;
        private string _analysisResult = "Pret.";
        private string _chatInput = "";

        public MedAgentService? MedAgentService => _medAgentService;

        public BureauMedViewModel()
        {
            _captureService = new ScreenCaptureService();
            Tools = new ObservableCollection<string> 
            { 
                "Firefox", 
                "Google Chrome",
                "Microsoft Edge",
                "LibreOffice", 
                "Doctolib", 
                "MedCap", 
                "Archives",
                "Calculatrice",
                "Notes"
            };
            AnalyzeCommand = new RelayCommand(async (param) => await AnalyzeCentralZoneAsync(param), _ => !IsAnalyzing);
            MoveToolCommand = new RelayCommand(async (param) => await MoveToolAsync(param), _ => !IsMovingWindow);
        }

        public ObservableCollection<string> Tools { get; }

        public string SelectedTool
        {
            get => _selectedTool;
            set
            {
                if (_selectedTool != value)
                {
                    _selectedTool = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsAnalyzing
        {
            get => _isAnalyzing;
            set
            {
                if (_isAnalyzing != value)
                {
                    _isAnalyzing = value;
                    OnPropertyChanged();
                    if (AnalyzeCommand is RelayCommand relay) relay.RaiseCanExecuteChanged();
                }
            }
        }

        public bool IsMovingWindow
        {
            get => _isMovingWindow;
            set
            {
                if (_isMovingWindow != value)
                {
                    _isMovingWindow = value;
                    OnPropertyChanged();
                    if (MoveToolCommand is RelayCommand relay) relay.RaiseCanExecuteChanged();
                }
            }
        }

        public string AnalysisResult
        {
            get => _analysisResult;
            set
            {
                if (_analysisResult != value)
                {
                    _analysisResult = value;
                    OnPropertyChanged();
                }
            }
        }

        public string ChatInput
        {
            get => _chatInput;
            set
            {
                if (_chatInput != value)
                {
                    _chatInput = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand AnalyzeCommand { get; }
        public ICommand MoveToolCommand { get; }

        public void Initialize(MedAgentService medAgentService)
        {
            _medAgentService = medAgentService;
        }

        private async System.Threading.Tasks.Task AnalyzeCentralZoneAsync(object? parameter)
        {
            if (IsAnalyzing || _medAgentService == null) return;
            
            if (!(parameter is System.Windows.Rect rect))
            {
                AnalysisResult = "Erreur interne : coordonnées manquantes.";
                return;
            }

            IsAnalyzing = true;
            AnalysisResult = "Capture de la zone centrale...";

            try
            {
                byte[] imageBytes = _captureService.CaptureRegion(rect);
                
                if (imageBytes == null || imageBytes.Length == 0)
                {
                    AnalysisResult = "Échec de la capture d'écran.";
                    return;
                }

                AnalysisResult = "Analyse visuelle par Med...";
                var prompt = $"Analysez cette capture d'écran de l'outil {SelectedTool}. Que pouvez-vous me dire sur le contenu visible ?";
                
                var result = await _medAgentService.ProcessVisionRequestAsync(prompt, imageBytes);
                AnalysisResult = result;
            }
            catch (Exception ex)
            {
                AnalysisResult = $"Erreur d'analyse : {ex.Message}";
            }
            finally
            {
                IsAnalyzing = false;
            }
        }

        private async System.Threading.Tasks.Task MoveToolAsync(object? parameter)
        {
            if (!(parameter is System.Windows.Rect rect) || IsMovingWindow) 
            {
                AnalysisResult = "Action impossible.";
                return;
            }

            IsMovingWindow = true;
            
            // On simplifie le nom pour la recherche (ex: "Google Chrome" -> "Chrome")
            string searchTitle = SelectedTool;
            if (SelectedTool == "Google Chrome") searchTitle = "Chrome";
            if (SelectedTool == "Microsoft Edge") searchTitle = "Edge";

            AnalysisResult = $"Recherche de {searchTitle}...";

            try
            {
                string scriptPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Scripts", "AlignWindow.ps1");
                
                if (!System.IO.File.Exists(scriptPath))
                {
                    scriptPath = System.IO.Path.Combine(Environment.CurrentDirectory, "Assets", "Scripts", "AlignWindow.ps1");
                    if (!System.IO.File.Exists(scriptPath))
                    {
                         // Tentative ultime si lancé depuis le projet parent
                         scriptPath = System.IO.Path.Combine(Environment.CurrentDirectory, "MedCompanion", "Assets", "Scripts", "AlignWindow.ps1");
                    }
                }

                if (!System.IO.File.Exists(scriptPath)) 
                {
                    AnalysisResult = "Erreur : Script d'alignement introuvable.";
                    return;
                }

                string x = ((int)rect.Left).ToString(CultureInfo.InvariantCulture);
                string y = ((int)rect.Top).ToString(CultureInfo.InvariantCulture);
                string w = ((int)rect.Width).ToString(CultureInfo.InvariantCulture);
                string h = ((int)rect.Height).ToString(CultureInfo.InvariantCulture);

                string args = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" -WindowTitle \"{searchTitle}\" -X {x} -Y {y} -Width {w} -Height {h}";
                
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                using (var process = Process.Start(psi))
                {
                    if (process != null)
                    {
                        var outputTask = process.StandardOutput.ReadToEndAsync();
                        var errorTask = process.StandardError.ReadToEndAsync();
                        
                        await process.WaitForExitAsync();
                        
                        string output = await outputTask;
                        string error = await errorTask;

                        if (process.ExitCode != 0)
                        {
                            // On affiche l'erreur détaillée du script s'il y en a une
                            AnalysisResult = !string.IsNullOrEmpty(error) ? $"Détail : {error.Split('\n')[0]}" : $"{SelectedTool} non trouvé.";
                        }
                        else
                        {
                            AnalysisResult = $"{SelectedTool} prêt dans la zone.";
                        }
                    }
                    else
                    {
                        AnalysisResult = "Impossible d'initier PowerShell.";
                    }
                }
            }
            catch (Exception ex)
            {
                AnalysisResult = $"Erreur technique : {ex.Message}";
            }
            finally
            {
                IsMovingWindow = false;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
