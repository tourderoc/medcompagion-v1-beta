using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Models;
using MedCompanion.Services;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// ViewModel pour la section "Apprendre nouveau modèle" (Templates)
    /// Gère l'analyse IA de courriers pour créer des MCC
    /// </summary>
    public class TemplatesViewModel : INotifyPropertyChanged
    {
        #region Services

        private readonly TemplateExtractorService _templateExtractor;
        private readonly MCCLibraryService _mccLibrary;

        #endregion

        #region Propriétés

        // Contenu de l'exemple de courrier
        private string _exampleLetterText = string.Empty;
        public string ExampleLetterText
        {
            get => _exampleLetterText;
            set
            {
                if (SetProperty(ref _exampleLetterText, value))
                {
                    UpdateCommandStates();
                }
            }
        }

        // MCC actuellement analysé
        private MCCModel? _currentAnalyzedMCC;
        public MCCModel? CurrentAnalyzedMCC
        {
            get => _currentAnalyzedMCC;
            set
            {
                if (SetProperty(ref _currentAnalyzedMCC, value))
                {
                    OnPropertyChanged(nameof(HasAnalyzedMCC));
                    UpdateCommandStates();
                }
            }
        }

        // Nom du template (éditable)
        private string _templateName = string.Empty;
        public string TemplateName
        {
            get => _templateName;
            set
            {
                if (SetProperty(ref _templateName, value))
                {
                    UpdateCommandStates();
                }
            }
        }

        // Variables détectées (affichage)
        private string _detectedVariables = string.Empty;
        public string DetectedVariables
        {
            get => _detectedVariables;
            set => SetProperty(ref _detectedVariables, value);
        }

        // Template extrait (affichage)
        private string _extractedTemplate = string.Empty;
        public string ExtractedTemplate
        {
            get => _extractedTemplate;
            set => SetProperty(ref _extractedTemplate, value);
        }

        // Métadonnées sémantiques
        private string _semanticType = "-";
        public string SemanticType
        {
            get => _semanticType;
            set => SetProperty(ref _semanticType, value);
        }

        private string _semanticAudience = "-";
        public string SemanticAudience
        {
            get => _semanticAudience;
            set => SetProperty(ref _semanticAudience, value);
        }

        private string _semanticAgeGroup = "-";
        public string SemanticAgeGroup
        {
            get => _semanticAgeGroup;
            set => SetProperty(ref _semanticAgeGroup, value);
        }

        private string _semanticTone = "-";
        public string SemanticTone
        {
            get => _semanticTone;
            set => SetProperty(ref _semanticTone, value);
        }

        // Collections pour ItemsControl
        private ObservableCollection<string> _clinicalKeywords = new();
        public ObservableCollection<string> ClinicalKeywords
        {
            get => _clinicalKeywords;
            set => SetProperty(ref _clinicalKeywords, value);
        }

        private ObservableCollection<KeyValuePair<string, string>> _structureSections = new();
        public ObservableCollection<KeyValuePair<string, string>> StructureSections
        {
            get => _structureSections;
            set => SetProperty(ref _structureSections, value);
        }

        // États UI
        private bool _isAnalyzing;
        public bool IsAnalyzing
        {
            get => _isAnalyzing;
            set
            {
                if (SetProperty(ref _isAnalyzing, value))
                {
                    UpdateCommandStates();
                }
            }
        }

        private bool _showResultPanel;
        public bool ShowResultPanel
        {
            get => _showResultPanel;
            set => SetProperty(ref _showResultPanel, value);
        }

        private string _statusMessage = string.Empty;
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        // Propriétés calculées
        public bool HasAnalyzedMCC => CurrentAnalyzedMCC != null;
        public bool CanAnalyze => !IsAnalyzing && !string.IsNullOrWhiteSpace(ExampleLetterText);
        public bool CanSave => HasAnalyzedMCC && !IsAnalyzing && !string.IsNullOrWhiteSpace(TemplateName);

        #endregion

        #region Commandes

        public ICommand AnalyzeLetterCommand { get; }
        public ICommand SaveTemplateCommand { get; }
        public ICommand OpenMCCLibraryCommand { get; }

        #endregion

        #region Events

        public event EventHandler<string>? StatusMessageChanged;
        public event EventHandler? MCCLibraryRequested;
        public event EventHandler? TemplateSaved;
        public event EventHandler<(string title, string message)>? ErrorOccurred;

        #endregion

        #region Constructeur

        public TemplatesViewModel(
            TemplateExtractorService templateExtractor,
            MCCLibraryService mccLibrary)
        {
            _templateExtractor = templateExtractor ?? throw new ArgumentNullException(nameof(templateExtractor));
            _mccLibrary = mccLibrary ?? throw new ArgumentNullException(nameof(mccLibrary));

            // Initialiser les commandes
            AnalyzeLetterCommand = new RelayCommand(
                execute: async _ => await AnalyzeLetterAsync(),
                canExecute: _ => CanAnalyze
            );

            SaveTemplateCommand = new RelayCommand(
                execute: _ => SaveTemplate(),
                canExecute: _ => CanSave
            );

            OpenMCCLibraryCommand = new RelayCommand(
                execute: _ => OpenMCCLibrary(),
                canExecute: _ => !IsAnalyzing
            );
        }

        #endregion

        #region Méthodes

        /// <summary>
        /// Analyse un exemple de courrier avec l'IA (3 phases)
        /// </summary>
        private async Task AnalyzeLetterAsync()
        {
            if (string.IsNullOrWhiteSpace(ExampleLetterText))
                return;

            IsAnalyzing = true;
            ShowResultPanel = false;

            try
            {
                // ✅ PHASE 1 : Analyse sémantique
                StatusMessage = "⏳ Phase 1/3 : Analyse sémantique...";
                StatusMessageChanged?.Invoke(this, StatusMessage);

                var (semanticSuccess, semantic, semanticError) =
                    await _templateExtractor.AnalyzeDocumentSemantic(ExampleLetterText);

                if (!semanticSuccess)
                {
                    ErrorOccurred?.Invoke(this, ("Erreur Phase 1", semanticError));
                    StatusMessage = $"❌ Erreur sémantique: {semanticError}";
                    StatusMessageChanged?.Invoke(this, StatusMessage);
                    return;
                }

                // ✅ PHASE 2 : Extraction template
                StatusMessage = "⏳ Phase 2/3 : Extraction template...";
                StatusMessageChanged?.Invoke(this, StatusMessage);

                var (templateSuccess, template, name, variables, templateError) =
                    await _templateExtractor.ExtractTemplateFromExample(ExampleLetterText);

                if (!templateSuccess)
                {
                    ErrorOccurred?.Invoke(this, ("Erreur Phase 2", templateError));
                    StatusMessage = $"❌ Erreur template: {templateError}";
                    StatusMessageChanged?.Invoke(this, StatusMessage);
                    return;
                }

                // ✅ PHASE 3 : Analyse structurelle
                StatusMessage = "⏳ Phase 3/3 : Analyse structurelle...";
                StatusMessageChanged?.Invoke(this, StatusMessage);

                var (structureSuccess, sections, structureError) =
                    await _templateExtractor.AnalyzeTemplateStructure(template);

                if (!structureSuccess)
                {
                    ErrorOccurred?.Invoke(this, ("Erreur Phase 3", structureError));
                    StatusMessage = $"❌ Erreur structure: {structureError}";
                    StatusMessageChanged?.Invoke(this, StatusMessage);
                    return;
                }

                // ✅ FUSION : Combiner les résultats
                semantic.Sections = sections;

                // Créer le MCC complet
                var mcc = new MCCModel
                {
                    Id = Guid.NewGuid().ToString("N").Substring(0, 16),
                    Name = name,
                    Version = 1,
                    Created = DateTime.Now,
                    LastModified = DateTime.Now,
                    Semantic = semantic,
                    TemplateMarkdown = template,
                    PromptTemplate = "",
                    Keywords = semantic.Keywords?.AUtiliser ?? new List<string>(),
                    Status = MCCStatus.Active,
                    UsageCount = 0,
                    AverageRating = 0.0,
                    TotalRatings = 0
                };

                // Stocker le MCC et mettre à jour l'UI
                CurrentAnalyzedMCC = mcc;
                UpdateUIFromMCC(mcc);

                ShowResultPanel = true;
                StatusMessage = "✅ MCC extrait avec analyse sémantique complète";
                StatusMessageChanged?.Invoke(this, StatusMessage);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ("Erreur", ex.Message));
                StatusMessage = $"❌ Erreur: {ex.Message}";
                StatusMessageChanged?.Invoke(this, StatusMessage);
            }
            finally
            {
                IsAnalyzing = false;
            }
        }

        /// <summary>
        /// Met à jour l'interface avec les données du MCC analysé
        /// </summary>
        private void UpdateUIFromMCC(MCCModel mcc)
        {
            // Mettre à jour les propriétés
            TemplateName = mcc.Name;
            DetectedVariables = string.Join(", ", mcc.Keywords.Select(v => $"{{{{{v}}}}}"));
            ExtractedTemplate = mcc.TemplateMarkdown;

            // Métadonnées sémantiques
            if (mcc.Semantic != null)
            {
                SemanticType = mcc.Semantic.DocType ?? "-";
                SemanticAudience = mcc.Semantic.Audience ?? "-";
                SemanticAgeGroup = mcc.Semantic.AgeGroup ?? "-";
                SemanticTone = mcc.Semantic.Tone ?? "-";

                // Mots-clés cliniques
                ClinicalKeywords.Clear();
                if (mcc.Semantic.ClinicalKeywords != null && mcc.Semantic.ClinicalKeywords.Count > 0)
                {
                    foreach (var keyword in mcc.Semantic.ClinicalKeywords)
                        ClinicalKeywords.Add(keyword);
                }
                else
                {
                    ClinicalKeywords.Add("Aucun mot-clé détecté");
                }

                // Structure détectée
                StructureSections.Clear();
                if (mcc.Semantic.Sections != null && mcc.Semantic.Sections.Count > 0)
                {
                    foreach (var section in mcc.Semantic.Sections)
                        StructureSections.Add(section);
                }
                else
                {
                    StructureSections.Add(new KeyValuePair<string, string>("Structure", "Non analysée"));
                }
            }
        }

        /// <summary>
        /// Sauvegarde le template dans la bibliothèque MCC
        /// </summary>
        private void SaveTemplate()
        {
            if (CurrentAnalyzedMCC == null || string.IsNullOrWhiteSpace(TemplateName))
                return;

            try
            {
                // Mettre à jour le nom du MCC
                CurrentAnalyzedMCC.Name = TemplateName;

                // Sauvegarder avec MCCLibrary
                var (success, message) = _mccLibrary.AddMCC(CurrentAnalyzedMCC);

                if (success)
                {
                    // Réinitialiser l'interface
                    ExampleLetterText = string.Empty;
                    ShowResultPanel = false;
                    CurrentAnalyzedMCC = null;
                    TemplateName = string.Empty;
                    DetectedVariables = string.Empty;
                    ExtractedTemplate = string.Empty;

                    // Notifier
                    TemplateSaved?.Invoke(this, EventArgs.Empty);
                    StatusMessage = "✅ MCC sauvegardé avec analyse sémantique complète";
                    StatusMessageChanged?.Invoke(this, StatusMessage);
                }
                else
                {
                    ErrorOccurred?.Invoke(this, ("Erreur", message));
                    StatusMessage = $"❌ {message}";
                    StatusMessageChanged?.Invoke(this, StatusMessage);
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ("Erreur", ex.Message));
                StatusMessage = $"❌ Erreur: {ex.Message}";
                StatusMessageChanged?.Invoke(this, StatusMessage);
            }
        }

        /// <summary>
        /// Ouvre la bibliothèque MCC
        /// </summary>
        private void OpenMCCLibrary()
        {
            MCCLibraryRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Met à jour l'état des commandes
        /// </summary>
        private void UpdateCommandStates()
        {
            OnPropertyChanged(nameof(CanAnalyze));
            OnPropertyChanged(nameof(CanSave));
        }

        #endregion

        #region INotifyPropertyChanged

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return false;

            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion
    }
}
