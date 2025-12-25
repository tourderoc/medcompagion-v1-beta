using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using MedCompanion.Models;
using MedCompanion.Services;
using System.Threading;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Dialogue pour régénérer du contenu avec l'IA
    /// Permet de choisir le modèle LLM et de décrire les modifications souhaitées
    /// </summary>
    public partial class RegenerationDialog : Window
    {
        private readonly RegenerationService _regenerationService;
        private readonly string _originalContent;
        private readonly string _contentType;
        private readonly PatientMetadata? _patientMetadata;
        private readonly AppSettings _settings;

        private List<ModelItem> _availableModels = new();

        /// <summary>
        /// Résultat de la régénération (null si annulé)
        /// </summary>
        public string? RegeneratedContent { get; private set; }

        /// <summary>
        /// Indique si la régénération a réussi
        /// </summary>
        public bool IsSuccess { get; private set; }

        public RegenerationDialog(
            RegenerationService regenerationService,
            string originalContent,
            string contentType,
            PatientMetadata? patientMetadata = null)
        {
            InitializeComponent();

            _regenerationService = regenerationService ?? throw new ArgumentNullException(nameof(regenerationService));
            _originalContent = originalContent;
            _contentType = contentType;
            _patientMetadata = patientMetadata;
            _settings = AppSettings.Load();

            // Charger les modèles disponibles au démarrage
            Loaded += async (s, e) => await LoadAvailableModelsAsync();

            // Activer le bouton quand des instructions sont saisies
            InstructionsTextBox.TextChanged += (s, e) =>
            {
                UpdateRegenerateButtonState();
            };
        }

        /// <summary>
        /// Charge la liste des modèles LLM disponibles
        /// </summary>
        private async Task LoadAvailableModelsAsync()
        {
            try
            {
                LoadingModelsText.Text = "Chargement des modèles...";
                ModelComboBox.IsEnabled = false;

                var models = await _regenerationService.GetAvailableModelsAsync();

                _availableModels.Clear();
                foreach (var (provider, model, displayName) in models)
                {
                    _availableModels.Add(new ModelItem
                    {
                        Provider = provider,
                        Model = model,
                        DisplayName = displayName
                    });
                }

                ModelComboBox.ItemsSource = _availableModels;

                if (_availableModels.Count > 0)
                {
                    // Sélectionner le dernier modèle utilisé s'il existe, sinon le premier
                    int selectedIndex = 0;
                    if (!string.IsNullOrEmpty(_settings.LastRegenerationModel))
                    {
                        var lastModel = _availableModels.FindIndex(m => m.Model == _settings.LastRegenerationModel);
                        if (lastModel >= 0)
                        {
                            selectedIndex = lastModel;
                        }
                    }

                    ModelComboBox.SelectedIndex = selectedIndex;
                    ModelComboBox.IsEnabled = true;
                    LoadingModelsText.Text = $"{_availableModels.Count} modèle(s) disponible(s)";
                    LoadingModelsText.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(39, 174, 96)); // Vert
                }
                else
                {
                    LoadingModelsText.Text = "Aucun modèle disponible. Vérifiez Ollama ou la clé OpenAI.";
                    LoadingModelsText.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(231, 76, 60)); // Rouge
                }

                UpdateRegenerateButtonState();
            }
            catch (Exception ex)
            {
                LoadingModelsText.Text = $"Erreur: {ex.Message}";
                LoadingModelsText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(231, 76, 60));
            }
        }

        /// <summary>
        /// Met à jour l'état du bouton Régénérer
        /// </summary>
        private void UpdateRegenerateButtonState()
        {
            RegenerateButton.IsEnabled =
                ModelComboBox.SelectedItem != null &&
                !string.IsNullOrWhiteSpace(InstructionsTextBox.Text);
        }

        /// <summary>
        /// Annule et ferme le dialogue
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            IsSuccess = false;
            RegeneratedContent = null;
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// Lance la régénération
        /// </summary>
        private async void RegenerateButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedModel = ModelComboBox.SelectedItem as ModelItem;
            if (selectedModel == null)
            {
                StatusText.Text = "Veuillez sélectionner un modèle.";
                return;
            }

            string instructions = InstructionsTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(instructions))
            {
                StatusText.Text = "Veuillez décrire les modifications souhaitées.";
                return;
            }

            var busyService = BusyService.Instance;
            var cancellationToken = busyService.Start($"Régénération avec {selectedModel.DisplayName}", canCancel: true);

            try
            {
                // Désactiver l'interface pendant la régénération
                SetUIEnabled(false);
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(52, 152, 219)); // Bleu
                StatusText.Text = $"Régénération en cours avec {selectedModel.DisplayName}...";

                // Étape 1: Préparation
                busyService.UpdateStep("Préparation des données...");
                busyService.UpdateProgress(10);
                await Task.Delay(100); // Laisser l'UI se mettre à jour

                // Étape 2: Anonymisation (si patient présent)
                if (_patientMetadata != null)
                {
                    busyService.UpdateStep("Anonymisation des données patient...");
                    busyService.UpdateProgress(30);
                }

                // Étape 3: Appel au modèle LLM
                busyService.UpdateStep($"Régénération avec {selectedModel.DisplayName}...");
                busyService.UpdateProgress(50);

                // Appeler le service de régénération
                var (success, result, error) = await _regenerationService.RegenerateAsync(
                    _originalContent,
                    instructions,
                    _contentType,
                    selectedModel.Provider,
                    selectedModel.Model,
                    _patientMetadata);

                if (cancellationToken.IsCancellationRequested)
                {
                    StatusText.Text = "⚠️ Régénération annulée";
                    SetUIEnabled(true);
                    return;
                }

                // Étape 4: Finalisation
                busyService.UpdateStep("Finalisation...");
                busyService.UpdateProgress(90);

                if (success)
                {
                    busyService.UpdateProgress(100);

                    // Sauvegarder le modèle sélectionné pour la prochaine fois
                    _settings.LastRegenerationModel = selectedModel.Model;
                    _settings.Save();

                    IsSuccess = true;
                    RegeneratedContent = result;
                    DialogResult = true;
                    Close();
                }
                else
                {
                    StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(231, 76, 60)); // Rouge
                    StatusText.Text = $"Erreur: {error}";
                    SetUIEnabled(true);
                }
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "⚠️ Régénération annulée par l'utilisateur";
                SetUIEnabled(true);
            }
            catch (Exception ex)
            {
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(231, 76, 60));
                StatusText.Text = $"Erreur inattendue: {ex.Message}";
                SetUIEnabled(true);
            }
            finally
            {
                busyService.Stop();
            }
        }

        /// <summary>
        /// Active/désactive l'interface utilisateur
        /// </summary>
        private void SetUIEnabled(bool enabled)
        {
            ModelComboBox.IsEnabled = enabled && _availableModels.Count > 0;
            InstructionsTextBox.IsEnabled = enabled;
            RegenerateButton.IsEnabled = enabled;
            CancelButton.IsEnabled = enabled;

            if (!enabled)
            {
                RegenerateButton.Content = "Régénération...";
            }
            else
            {
                RegenerateButton.Content = "Régénérer";
            }
        }
    }

    /// <summary>
    /// Item pour la ComboBox des modèles
    /// </summary>
    public class ModelItem
    {
        public string Provider { get; set; } = "";
        public string Model { get; set; } = "";
        public string DisplayName { get; set; } = "";
    }
}
