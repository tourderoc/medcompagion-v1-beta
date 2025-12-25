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
    /// ViewModel pour le dialogue de bibliothèque MCC
    /// Gère l'affichage, la recherche, et les actions CRUD sur les MCC
    /// </summary>
    public class MCCLibraryViewModel : INotifyPropertyChanged
    {
        #region Services

        private readonly MCCLibraryService _mccLibrary;
        private readonly LetterRatingService _ratingService;

        #endregion

        #region Collections

        private ObservableCollection<MCCDisplayItem> _allMCCs = new();
        public ObservableCollection<MCCDisplayItem> AllMCCs
        {
            get => _allMCCs;
            set => SetProperty(ref _allMCCs, value);
        }

        private ObservableCollection<MCCDisplayItem> _filteredMCCs = new();
        public ObservableCollection<MCCDisplayItem> FilteredMCCs
        {
            get => _filteredMCCs;
            set => SetProperty(ref _filteredMCCs, value);
        }

        #endregion

        #region Sélection & Recherche

        private MCCDisplayItem? _selectedMCCItem;
        public MCCDisplayItem? SelectedMCCItem
        {
            get => _selectedMCCItem;
            set
            {
                if (SetProperty(ref _selectedMCCItem, value))
                {
                    UpdateSelection();
                }
            }
        }

        public MCCModel? SelectedMCC => _selectedMCCItem?.MCC;

        private string _searchQuery = string.Empty;
        public string SearchQuery
        {
            get => _searchQuery;
            set
            {
                if (SetProperty(ref _searchQuery, value))
                {
                    ApplySearch();
                }
            }
        }

        #endregion

        #region UI State

        private bool _isLoading;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        private string _counterText = "0 MCC disponibles";
        public string CounterText
        {
            get => _counterText;
            set => SetProperty(ref _counterText, value);
        }

        public bool ShouldGenerate { get; private set; }

        #endregion

        #region Aperçu (Colonne 2)

        private string _previewTitle = "Sélectionnez un MCC";
        public string PreviewTitle
        {
            get => _previewTitle;
            set => SetProperty(ref _previewTitle, value);
        }

        private string _previewSubtitle = string.Empty;
        public string PreviewSubtitle
        {
            get => _previewSubtitle;
            set => SetProperty(ref _previewSubtitle, value);
        }

        private string _usageStats = "📊 0 utilisations";
        public string UsageStats
        {
            get => _usageStats;
            set => SetProperty(ref _usageStats, value);
        }

        private string _dateStats = string.Empty;
        public string DateStats
        {
            get => _dateStats;
            set => SetProperty(ref _dateStats, value);
        }

        private string _templatePreview = "Sélectionnez un MCC dans la liste pour voir son aperçu...";
        public string TemplatePreview
        {
            get => _templatePreview;
            set => SetProperty(ref _templatePreview, value);
        }

        private string _variablesText = "Aucune";
        public string VariablesText
        {
            get => _variablesText;
            set => SetProperty(ref _variablesText, value);
        }

        #endregion

        #region Sémantique (Colonne 3)

        private bool _showRatingSection;
        public bool ShowRatingSection
        {
            get => _showRatingSection;
            set => SetProperty(ref _showRatingSection, value);
        }

        private string _ratingStarsText = "★★★★★";
        public string RatingStarsText
        {
            get => _ratingStarsText;
            set => SetProperty(ref _ratingStarsText, value);
        }

        private string _ratingAverageText = "0/5";
        public string RatingAverageText
        {
            get => _ratingAverageText;
            set => SetProperty(ref _ratingAverageText, value);
        }

        private string _ratingCountText = "(0 évaluations)";
        public string RatingCountText
        {
            get => _ratingCountText;
            set => SetProperty(ref _ratingCountText, value);
        }

        private string _ratingSatisfactionText = "Satisfaction : 0%";
        public string RatingSatisfactionText
        {
            get => _ratingSatisfactionText;
            set => SetProperty(ref _ratingSatisfactionText, value);
        }

        private string _ratingDistributionText = string.Empty;
        public string RatingDistributionText
        {
            get => _ratingDistributionText;
            set => SetProperty(ref _ratingDistributionText, value);
        }

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

        private string _semanticAge = "-";
        public string SemanticAge
        {
            get => _semanticAge;
            set => SetProperty(ref _semanticAge, value);
        }

        private string _semanticTone = "-";
        public string SemanticTone
        {
            get => _semanticTone;
            set => SetProperty(ref _semanticTone, value);
        }

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

        #endregion

        #region Boutons Enable/Visibility

        public bool CanEdit => SelectedMCC != null && !IsLoading;
        public bool CanOptimize => SelectedMCC != null && !IsLoading;
        public bool CanAddToCourriers => SelectedMCC != null && !SelectedMCC.IsInCourriersList && !IsLoading;
        public bool CanRemoveFromCourriers => SelectedMCC != null && SelectedMCC.IsInCourriersList && !IsLoading;
        public bool ShowRemoveFromCourriers => SelectedMCC != null && SelectedMCC.IsInCourriersList;
        public bool CanDelete => SelectedMCC != null && !IsLoading;
        public bool CanGenerate => SelectedMCC != null && !IsLoading;

        #endregion

        #region Commandes

        public ICommand RefreshCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand OptimizeCommand { get; }
        public ICommand AddToCourriersCommand { get; }
        public ICommand RemoveFromCourriersCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand GenerateCommand { get; }

        #endregion

        #region Events

        public event EventHandler<MCCModel>? EditRequested;
        public event EventHandler<MCCModel>? OptimizeRequested;
        public event EventHandler<MCCModel>? DeleteConfirmationRequested;
        public event EventHandler<bool>? CloseDialogRequested;
        public event EventHandler<(string title, string message)>? ErrorOccurred;

        #endregion

        #region Constructeur

        public MCCLibraryViewModel(
            MCCLibraryService mccLibrary,
            LetterRatingService? ratingService = null)
        {
            _mccLibrary = mccLibrary ?? throw new ArgumentNullException(nameof(mccLibrary));
            _ratingService = ratingService ?? new LetterRatingService();

            // Initialiser les commandes
            RefreshCommand = new RelayCommand(
                execute: async _ => await LoadMCCsAsync(),
                canExecute: _ => !IsLoading
            );

            EditCommand = new RelayCommand(
                execute: _ => EditMCC(),
                canExecute: _ => CanEdit
            );

            OptimizeCommand = new RelayCommand(
                execute: async _ => await OptimizeMCCAsync(),
                canExecute: _ => CanOptimize
            );

            AddToCourriersCommand = new RelayCommand(
                execute: _ => AddToCourriers(),
                canExecute: _ => CanAddToCourriers
            );

            RemoveFromCourriersCommand = new RelayCommand(
                execute: _ => RemoveFromCourriers(),
                canExecute: _ => CanRemoveFromCourriers
            );

            DeleteCommand = new RelayCommand(
                execute: _ => RequestDelete(),
                canExecute: _ => CanDelete
            );

            GenerateCommand = new RelayCommand(
                execute: _ => GenerateWithMCC(),
                canExecute: _ => CanGenerate
            );
        }

        #endregion

        #region Méthodes

        /// <summary>
        /// Charge tous les MCC avec leurs statistiques
        /// </summary>
        public async Task LoadMCCsAsync()
        {
            if (IsLoading) return;

            IsLoading = true;

            try
            {
                await Task.Run(() =>
                {
                    var mccs = _mccLibrary.GetAllMCCs();

                    var displayItems = mccs.Select(mcc =>
                    {
                        var stats = _ratingService.GetMCCStatistics(mcc.Id);

                        return new MCCDisplayItem
                        {
                            MCC = mcc,
                            Name = mcc.Name,
                            CreatedDisplay = mcc.Created.ToString("dd/MM/yyyy"),
                            UsageCount = mcc.UsageCount,
                            KeywordsPreview = mcc.Keywords != null && mcc.Keywords.Count > 0
                                ? string.Join(", ", mcc.Keywords.Take(3)) + (mcc.Keywords.Count > 3 ? "..." : "")
                                : "Aucun",
                            Semantic = mcc.Semantic ?? new SemanticAnalysis(),
                            AverageRating = stats.AverageRating,
                            RatingCount = stats.TotalRatings,
                            RatingDisplay = FormatRatingDisplay(stats),
                            RatingColor = GetRatingColor(stats.AverageRating, stats.TotalRatings)
                        };
                    }).ToList();

                    AllMCCs = new ObservableCollection<MCCDisplayItem>(displayItems);
                    FilteredMCCs = new ObservableCollection<MCCDisplayItem>(displayItems);
                });

                UpdateCounter();
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ("Erreur", $"Erreur chargement MCC :\n\n{ex.Message}"));
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// Applique le filtre de recherche
        /// </summary>
        private void ApplySearch()
        {
            var query = SearchQuery.Trim().ToLower();

            if (string.IsNullOrEmpty(query))
            {
                FilteredMCCs = new ObservableCollection<MCCDisplayItem>(AllMCCs);
            }
            else
            {
                var filtered = AllMCCs.Where(mcc =>
                    mcc.Name.ToLower().Contains(query) ||
                    (mcc.MCC.Keywords != null && mcc.MCC.Keywords.Any(k => k.ToLower().Contains(query))) ||
                    (mcc.MCC.Semantic?.DocType?.ToLower().Contains(query) ?? false) ||
                    (mcc.MCC.Semantic?.Audience?.ToLower().Contains(query) ?? false)
                ).ToList();

                FilteredMCCs = new ObservableCollection<MCCDisplayItem>(filtered);
            }

            UpdateCounter();
        }

        /// <summary>
        /// Met à jour le compteur
        /// </summary>
        private void UpdateCounter()
        {
            var count = FilteredMCCs.Count;
            CounterText = count == 0 ? "Aucun MCC" :
                         count == 1 ? "1 MCC" :
                         $"{count} MCCs";
        }

        /// <summary>
        /// Met à jour l'affichage lors d'une sélection
        /// </summary>
        private void UpdateSelection()
        {
            if (SelectedMCC == null)
            {
                // Reset affichage
                PreviewTitle = "Sélectionnez un MCC";
                PreviewSubtitle = string.Empty;
                UsageStats = "📊 0 utilisations";
                DateStats = string.Empty;
                TemplatePreview = "Sélectionnez un MCC dans la liste pour voir son aperçu...";
                VariablesText = "Aucune";
                ShowRatingSection = false;
                SemanticType = "-";
                SemanticAudience = "-";
                SemanticAge = "-";
                SemanticTone = "-";
                ClinicalKeywords.Clear();
                StructureSections.Clear();
            }
            else
            {
                // Colonne 2 : Aperçu
                PreviewTitle = SelectedMCC.Name;
                PreviewSubtitle = $"Version {SelectedMCC.Version} • {SelectedMCC.Created:dd/MM/yyyy}";
                UsageStats = $"📊 {SelectedMCC.UsageCount} utilisation{(SelectedMCC.UsageCount > 1 ? "s" : "")}";
                DateStats = $"Créé le {SelectedMCC.Created:dd/MM/yyyy} • Modifié le {SelectedMCC.LastModified:dd/MM/yyyy}";
                TemplatePreview = SelectedMCC.TemplateMarkdown ?? "Aucun aperçu disponible";

                // Variables
                if (SelectedMCC.Keywords != null && SelectedMCC.Keywords.Count > 0)
                {
                    VariablesText = string.Join(", ", SelectedMCC.Keywords.Select(k => $"{{{{{k}}}}}"));
                }
                else
                {
                    VariablesText = "Aucune variable détectée";
                }

                // Colonne 3 : Sémantique
                UpdateSemanticPanel();
                UpdateRatingSection();
            }

            // Notifier changements boutons
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(CanOptimize));
            OnPropertyChanged(nameof(CanAddToCourriers));
            OnPropertyChanged(nameof(CanRemoveFromCourriers));
            OnPropertyChanged(nameof(ShowRemoveFromCourriers));
            OnPropertyChanged(nameof(CanDelete));
            OnPropertyChanged(nameof(CanGenerate));
        }

        /// <summary>
        /// Met à jour le panneau sémantique
        /// </summary>
        private void UpdateSemanticPanel()
        {
            if (SelectedMCC?.Semantic == null)
            {
                SemanticType = "-";
                SemanticAudience = "-";
                SemanticAge = "-";
                SemanticTone = "-";
                ClinicalKeywords.Clear();
                StructureSections.Clear();
                return;
            }

            var semantic = SelectedMCC.Semantic;

            SemanticType = semantic.DocType ?? "-";
            SemanticAudience = semantic.Audience ?? "-";
            SemanticAge = semantic.AgeGroup ?? "-";
            SemanticTone = semantic.Tone ?? "-";

            // Mots-clés cliniques
            ClinicalKeywords.Clear();
            if (semantic.ClinicalKeywords != null && semantic.ClinicalKeywords.Count > 0)
            {
                foreach (var keyword in semantic.ClinicalKeywords)
                    ClinicalKeywords.Add(keyword);
            }

            // Structure
            StructureSections.Clear();
            if (semantic.Sections != null && semantic.Sections.Count > 0)
            {
                foreach (var section in semantic.Sections)
                    StructureSections.Add(section);
            }
        }

        /// <summary>
        /// Met à jour la section d'évaluations
        /// </summary>
        private void UpdateRatingSection()
        {
            if (SelectedMCC == null)
            {
                ShowRatingSection = false;
                return;
            }

            var stats = _ratingService.GetMCCStatistics(SelectedMCC.Id);

            if (stats.TotalRatings == 0)
            {
                ShowRatingSection = false;
                return;
            }

            ShowRatingSection = true;

            RatingStarsText = GetStarsDisplay(stats.AverageRating);
            RatingAverageText = $"{stats.AverageRating:F1}/5";
            RatingCountText = $"({stats.TotalRatings} évaluation{(stats.TotalRatings > 1 ? "s" : "")})";
            RatingSatisfactionText = $"Satisfaction : {FormatSatisfactionRate(stats)}";
            RatingDistributionText = FormatRatingDistribution(stats);
        }

        /// <summary>
        /// Éditer un MCC
        /// </summary>
        private void EditMCC()
        {
            if (SelectedMCC != null)
            {
                EditRequested?.Invoke(this, SelectedMCC);
            }
        }

        /// <summary>
        /// Optimiser un MCC avec l'IA
        /// </summary>
        private async Task OptimizeMCCAsync()
        {
            if (SelectedMCC == null) return;

            // Vérifier si le MCC a un template valide
            if (string.IsNullOrWhiteSpace(SelectedMCC.TemplateMarkdown))
            {
                ErrorOccurred?.Invoke(this, ("Information", "Ce MCC n'a pas de template valide à optimiser."));
                return;
            }

            // Déclencher l'événement pour que le code-behind gère l'optimisation
            OptimizeRequested?.Invoke(this, SelectedMCC);
            await Task.CompletedTask; // Maintenir la signature async pour ICommand
        }

        /// <summary>
        /// Ajouter à la liste Courriers
        /// </summary>
        private void AddToCourriers()
        {
            if (SelectedMCC == null) return;

            SelectedMCC.IsInCourriersList = true;
            var (success, message) = _mccLibrary.UpdateMCC(SelectedMCC);

            if (success)
            {
                // Recharger pour rafraîchir l'affichage
                var task = LoadMCCsAsync();
                UpdateSelection();
            }
            else
            {
                ErrorOccurred?.Invoke(this, ("Erreur", message));
            }
        }

        /// <summary>
        /// Retirer de la liste Courriers
        /// </summary>
        private void RemoveFromCourriers()
        {
            if (SelectedMCC == null) return;

            SelectedMCC.IsInCourriersList = false;
            var (success, message) = _mccLibrary.UpdateMCC(SelectedMCC);

            if (success)
            {
                var task = LoadMCCsAsync();
                UpdateSelection();
            }
            else
            {
                ErrorOccurred?.Invoke(this, ("Erreur", message));
            }
        }

        /// <summary>
        /// Demande confirmation de suppression
        /// </summary>
        private void RequestDelete()
        {
            if (SelectedMCC != null)
            {
                DeleteConfirmationRequested?.Invoke(this, SelectedMCC);
            }
        }

        /// <summary>
        /// Confirme et effectue la suppression (appelé après confirmation utilisateur)
        /// </summary>
        public void ConfirmDelete()
        {
            if (SelectedMCC == null) return;

            var (success, message) = _mccLibrary.DeleteMCC(SelectedMCC.Id);

            if (success)
            {
                var task = LoadMCCsAsync();
                SelectedMCCItem = null; // Reset sélection
            }
            else
            {
                ErrorOccurred?.Invoke(this, ("Erreur", message));
            }
        }

        /// <summary>
        /// Générer avec le MCC sélectionné
        /// </summary>
        private void GenerateWithMCC()
        {
            if (SelectedMCC != null)
            {
                ShouldGenerate = true;
                CloseDialogRequested?.Invoke(this, true);
            }
        }

        #endregion

        #region Helpers Formatage

        private string FormatRatingDisplay(MCCStatistics stats)
        {
            if (stats.TotalRatings == 0)
                return "Aucune évaluation";

            return $"⭐ {stats.AverageRating:F1}/5 ({stats.TotalRatings})";
        }

        private string GetRatingColor(double avgRating, int totalRatings)
        {
            if (totalRatings == 0) return "Gray";
            if (avgRating >= 4.5) return "#27AE60"; // Vert
            if (avgRating >= 3.5) return "#F39C12"; // Orange
            return "#E74C3C"; // Rouge
        }

        private string GetStarsDisplay(double rating)
        {
            int fullStars = (int)Math.Floor(rating);
            bool hasHalfStar = (rating - fullStars) >= 0.5;
            int emptyStars = 5 - fullStars - (hasHalfStar ? 1 : 0);

            return new string('★', fullStars) +
                   (hasHalfStar ? "⯨" : "") +
                   new string('☆', emptyStars);
        }

        private string FormatSatisfactionRate(MCCStatistics stats)
        {
            if (stats.TotalRatings == 0) return "0%";

            int satisfied = stats.FiveStars + stats.FourStars;
            double rate = (double)satisfied / stats.TotalRatings * 100;
            return $"{rate:F0}%";
        }

        private string FormatRatingDistribution(MCCStatistics stats)
        {
            var parts = new List<string>();

            if (stats.FiveStars > 0) parts.Add($"5★ ({stats.FiveStars})");
            if (stats.FourStars > 0) parts.Add($"4★ ({stats.FourStars})");
            if (stats.ThreeStars > 0) parts.Add($"3★ ({stats.ThreeStars})");
            if (stats.TwoStars > 0) parts.Add($"2★ ({stats.TwoStars})");
            if (stats.OneStar > 0) parts.Add($"1★ ({stats.OneStar})");

            return string.Join(" • ", parts);
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
