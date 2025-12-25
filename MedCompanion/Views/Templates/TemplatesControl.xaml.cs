using System;
using System.Windows;
using System.Windows.Controls;
using MedCompanion.Services;
using MedCompanion.ViewModels;

namespace MedCompanion.Views.Templates
{
    /// <summary>
    /// UserControl pour la section "Apprendre nouveau modèle" (Templates)
    /// Architecture MVVM : ViewModel = TemplatesViewModel
    /// </summary>
    public partial class TemplatesControl : UserControl
    {
        private TemplatesViewModel? _viewModel;

        public TemplatesControl()
        {
            InitializeComponent();
        }

        #region Events for MainWindow

        /// <summary>
        /// Événement pour les changements de statut (affichage barre de statut globale)
        /// </summary>
        public event EventHandler<string>? StatusChanged;

        /// <summary>
        /// Événement pour les erreurs (MessageBox)
        /// </summary>
        public event EventHandler<(string title, string message)>? ErrorOccurred;

        /// <summary>
        /// Événement pour demander l'ouverture de la bibliothèque MCC
        /// </summary>
        public event EventHandler? MCCLibraryRequested;

        /// <summary>
        /// Événement déclenché après la sauvegarde d'un template
        /// </summary>
        public event EventHandler? TemplateSaved;

        #endregion

        #region Initialization

        /// <summary>
        /// Initialise le contrôle avec les services nécessaires
        /// </summary>
        public void Initialize(
            TemplateExtractorService templateExtractor,
            MCCLibraryService mccLibrary)
        {
            if (templateExtractor == null)
                throw new ArgumentNullException(nameof(templateExtractor));
            if (mccLibrary == null)
                throw new ArgumentNullException(nameof(mccLibrary));

            // Créer le ViewModel
            _viewModel = new TemplatesViewModel(templateExtractor, mccLibrary);

            // S'abonner aux événements du ViewModel
            _viewModel.StatusMessageChanged += (s, msg) => StatusChanged?.Invoke(this, msg);
            _viewModel.ErrorOccurred += (s, e) => ErrorOccurred?.Invoke(this, e);
            _viewModel.MCCLibraryRequested += (s, e) => MCCLibraryRequested?.Invoke(this, e);
            _viewModel.TemplateSaved += (s, e) => TemplateSaved?.Invoke(this, e);

            // Assigner le DataContext
            DataContext = _viewModel;
        }

        #endregion

        #region Public Methods (for MainWindow integration if needed)

        /// <summary>
        /// Réinitialise l'interface (appelé si nécessaire depuis MainWindow)
        /// </summary>
        public void Reset()
        {
            if (_viewModel != null)
            {
                _viewModel.ExampleLetterText = string.Empty;
                _viewModel.ShowResultPanel = false;
                _viewModel.CurrentAnalyzedMCC = null;
                _viewModel.TemplateName = string.Empty;
                _viewModel.DetectedVariables = string.Empty;
                _viewModel.ExtractedTemplate = string.Empty;
                _viewModel.StatusMessage = string.Empty;
            }
        }

        #endregion
    }
}
