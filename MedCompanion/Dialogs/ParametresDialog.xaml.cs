using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MedCompanion.Services;
using MedCompanion.Services.LLM;
using MedCompanion.ViewModels;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Logique d'interaction pour ParametresDialog.xaml
    /// </summary>
    public partial class ParametresDialog : Window
    {
        private readonly ParametresViewModel _viewModel;
        private readonly PromptTrackerService? _promptTracker;
        private readonly LLMServiceFactory? _llmFactory;
        private readonly AuthenticationService _authService;
        public bool SettingsSaved { get; private set; }

        public ParametresDialog(
            SecureStorageService secureStorage,
            WindowStateService windowStateService,
            LLMServiceFactory? llmFactory = null,
            PromptTrackerService? promptTracker = null)
        {
            InitializeComponent();

            _viewModel = new ParametresViewModel(secureStorage, windowStateService, llmFactory);
            _promptTracker = promptTracker;
            _llmFactory = llmFactory;
            _authService = new AuthenticationService();
            DataContext = _viewModel;

            // Synchroniser PasswordBox avec ViewModel (binding ne fonctionne pas directement)
            OpenAIPasswordBox.PasswordChanged += (s, e) =>
            {
                _viewModel.OpenAIApiKey = OpenAIPasswordBox.Password;
            };

            // Initialiser le PasswordBox avec la valeur existante
            if (!string.IsNullOrEmpty(_viewModel.OpenAIApiKey))
            {
                OpenAIPasswordBox.Password = _viewModel.OpenAIApiKey;
            }

            // S'abonner aux événements du tracker si disponible
            if (_promptTracker != null)
            {
                _promptTracker.PromptLogged += OnPromptLogged;
                LoadPromptHistory();
            }

            // Charger les paramètres du Chat
            LoadChatSettings();

            // Charger le modèle d'anonymisation
            LoadAnonymizationModel();

            // Charger les paramètres de sécurité
            LoadSecuritySettings();

            SettingsSaved = false;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // ✅ IMPORTANT: Sauvegarder d'abord les modifications dans _viewModel.Settings
                // avant d'appeler _viewModel.SaveSettings() qui écrit le JSON
                SaveChatSettings();
                SaveAnonymizationModel();

                // Ensuite sauvegarder dans le fichier JSON
                _viewModel.SaveSettings();

                SettingsSaved = true;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Erreur lors de la sauvegarde des paramètres :\n{ex.Message}",
                    "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            SettingsSaved = false;
            DialogResult = false;
            Close();
        }

        // ===== PROMPT TRACKER =====

        private void OnPromptLogged(object? sender, EventArgs e)
        {
            // Rafraîchir la liste sur le thread UI
            Dispatcher.Invoke(() => LoadPromptHistory());
        }

        private void LoadPromptHistory(string? filter = null)
        {
            if (_promptTracker == null)
            {
                // DEBUG: Afficher si le tracker est null
                TotalPromptsText.Text = "⚠️ Tracker non initialisé";
                return;
            }

            var history = _promptTracker.GetHistory(filter);
            PromptHistoryList.ItemsSource = history;

            // Mettre à jour les statistiques
            var (total, tokens, rate) = _promptTracker.GetStatistics();
            TotalPromptsText.Text = $"Total prompts : {total}";
            TotalTokensText.Text = $"Total tokens : {tokens:N0}";
            SuccessRateText.Text = $"Taux de succès : {rate:P1}";
        }

        private void ModuleFilter_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (_promptTracker == null) return;

            var selected = (ModuleFilterCombo.SelectedItem as ComboBoxItem)?.Content.ToString();
            var filter = selected == "Tous" ? null : selected;
            LoadPromptHistory(filter);
        }

        private void PromptHistory_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PromptHistoryList.SelectedItem is Models.PromptLogEntry entry)
            {
                SystemPromptText.Text = entry.SystemPrompt;
                UserPromptText.Text = entry.UserPrompt;
                AIResponseText.Text = entry.AIResponse;
            }
        }

        private void RefreshPrompts_Click(object sender, RoutedEventArgs e)
        {
            LoadPromptHistory();
        }

        private void ExportPrompts_Click(object sender, RoutedEventArgs e)
        {
            if (_promptTracker == null) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Fichier texte (*.txt)|*.txt|Tous les fichiers (*.*)|*.*",
                FileName = $"prompts_export_{DateTime.Now:yyyyMMdd_HHmmss}.txt"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var history = _promptTracker.GetHistory();
                    var export = new StringBuilder();

                    foreach (var entry in history)
                    {
                        export.AppendLine($"=== {entry.Timestamp:yyyy-MM-dd HH:mm:ss} - {entry.Module} ===");
                        export.AppendLine($"Provider: {entry.LLMProvider} ({entry.ModelName})");
                        export.AppendLine($"Tokens: {entry.TokensUsed}");
                        export.AppendLine($"Success: {entry.Success}");
                        if (!string.IsNullOrEmpty(entry.Error))
                        {
                            export.AppendLine($"Error: {entry.Error}");
                        }
                        export.AppendLine();
                        export.AppendLine("SYSTEM PROMPT:");
                        export.AppendLine(entry.SystemPrompt);
                        export.AppendLine();
                        export.AppendLine("USER PROMPT:");
                        export.AppendLine(entry.UserPrompt);
                        export.AppendLine();
                        export.AppendLine("AI RESPONSE:");
                        export.AppendLine(entry.AIResponse);
                        export.AppendLine();
                        export.AppendLine(new string('=', 80));
                        export.AppendLine();
                    }

                    File.WriteAllText(dialog.FileName, export.ToString());
                    MessageBox.Show(
                        $"Historique exporté vers :\n{dialog.FileName}",
                        "Export réussi",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Erreur lors de l'export :\n{ex.Message}",
                        "Erreur",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        // ===== PARAMÈTRES CHAT =====

        private void LoadChatSettings()
        {
            try
            {
                var settings = Models.ChatSettings.Load();
                settings.Validate();

                EnableCompactionCheckBox.IsChecked = settings.EnableCompaction;
                CompactionThresholdSlider.Value = settings.CompactionThreshold;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ParametresDialog] Erreur chargement paramètres Chat : {ex.Message}");
                // Utiliser les valeurs par défaut si erreur
                EnableCompactionCheckBox.IsChecked = true;
                CompactionThresholdSlider.Value = 20000;
            }
        }

        private void SaveChatSettings()
        {
            try
            {
                var settings = new Models.ChatSettings
                {
                    EnableCompaction = EnableCompactionCheckBox.IsChecked ?? true,
                    CompactionThreshold = (int)CompactionThresholdSlider.Value
                };

                settings.Validate();
                settings.Save();

                System.Diagnostics.Debug.WriteLine($"[ParametresDialog] Paramètres Chat sauvegardés : Threshold={settings.CompactionThreshold}, Enabled={settings.EnableCompaction}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ParametresDialog] Erreur sauvegarde paramètres Chat : {ex.Message}");
                throw; // Propager l'exception pour afficher le message d'erreur
            }
        }
        // ===== MODÈLE D'ANONYMISATION =====

        private void LoadAnonymizationModel()
        {
            // Initialiser la ComboBox avec le modèle actuel
            var currentModel = _viewModel.Settings.AnonymizationModel;
            AnonymizationModelCombo.Items.Clear();
            
            if (!string.IsNullOrEmpty(currentModel))
            {
                AnonymizationModelCombo.Items.Add(currentModel);
                AnonymizationModelCombo.SelectedItem = currentModel;
            }

            // Charger les modèles disponibles si possible sans bloquer
            Dispatcher.InvokeAsync(async () =>
            {
                await RefreshLocalModelsAsync();
            });
        }

        private async Task RefreshLocalModelsAsync()
        {
            try
            {
                RefreshLocalModelsBtn.IsEnabled = false;
                
                // Récupérer les modèles via la factory
                // On crée une instance temporaire si nécessaire car la factory injectée peut être null dans le constructeur (à vérifier)
                // Mais ici on a _viewModel.LLMFactory accessible via _viewModel
                
                // Note: _viewModel n'expose pas LLMFactory publiquement par défaut, on va devoir ruser ou l'ajouter
                // Pour l'instant, on suppose qu'on peut utiliser une méthode statique ou un service dédié
                // Solution propre : passer par LLMServiceFactory injecté dans le constructeur
                
                // On va utiliser une méthode helper simple ici pour éviter de modifier trop de code
                // Idéalement, _viewModel devrait exposer une commande, mais on est en code-behind pour le prototype
                
                var models = new System.Collections.Generic.List<string>();
                
                // Utiliser le factory injecté s'il est disponible, sinon tenter de créer un provider temporaire
                // Comme on n'a pas accès direct au factory ici (champ privé dans MainWindow ou App), 
                // on va modifier le constructeur pour le stocker ou l'utiliser.
                // Ah, LLMServiceFactory est passé au constructeur ! On va le stocker.
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur refresh models: {ex.Message}");
            }
            finally
            {
                RefreshLocalModelsBtn.IsEnabled = true;
            }
        }
        
        private async void RefreshLocalModelsBtn_Click(object sender, RoutedEventArgs e)
        {
            RefreshLocalModelsBtn.IsEnabled = false;
            try 
            {
                // Utiliser le Factory stocké pour lister les modèles Ollama
                if (_llmFactory != null)
                {
                    var models = await _llmFactory.GetAvailableOllamaModelsAsync();
                    
                    AnonymizationModelCombo.Items.Clear();
                    foreach (var model in models)
                    {
                        AnonymizationModelCombo.Items.Add(model);
                    }
                    
                    // Restaurer la sélection précédente
                    var current = _viewModel.Settings.AnonymizationModel;
                    if (models.Contains(current))
                    {
                        AnonymizationModelCombo.SelectedItem = current;
                    }
                    else if (models.Any())
                    {
                        AnonymizationModelCombo.SelectedIndex = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la récupération des modèles locaux :\n{ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                RefreshLocalModelsBtn.IsEnabled = true;
            }
        }

        private void SaveAnonymizationModel()
        {
            if (AnonymizationModelCombo.SelectedItem != null)
            {
                // La ComboBox peut contenir soit des strings directs, soit des ComboBoxItem
                string? selectedModel = null;

                if (AnonymizationModelCombo.SelectedItem is string modelString)
                {
                    selectedModel = modelString;
                }
                else if (AnonymizationModelCombo.SelectedItem is ComboBoxItem item && item.Tag != null)
                {
                    selectedModel = item.Tag.ToString();
                }

                if (!string.IsNullOrWhiteSpace(selectedModel))
                {
                    _viewModel.Settings.AnonymizationModel = selectedModel;
                    System.Diagnostics.Debug.WriteLine($"[ParametresDialog] Modèle d'anonymisation sauvegardé : {selectedModel}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[ParametresDialog] ERREUR: Modèle d'anonymisation invalide");
                }
            }
        }

        // ===== PARAMÈTRES SÉCURITÉ =====

        private void LoadSecuritySettings()
        {
            // Charger l'état actuel de l'authentification
            SecurityEnabledCheckBox.IsChecked = _authService.IsAuthenticationEnabled;

            // Si première utilisation, afficher un message
            if (_authService.IsFirstLaunch)
            {
                SecurityStatusText.Text = "Aucun mot de passe configuré. Utilisez l'assistant au démarrage.";
                SecurityStatusText.Foreground = new SolidColorBrush(Colors.Orange);
                PasswordSection.IsEnabled = false;
                PinSection.IsEnabled = false;
            }
        }

        private void SecurityEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_authService.IsFirstLaunch)
            {
                // Ne pas permettre d'activer sans avoir configuré les credentials
                SecurityEnabledCheckBox.IsChecked = false;
                SecurityStatusText.Text = "Configurez d'abord un mot de passe au démarrage de l'application.";
                SecurityStatusText.Foreground = new SolidColorBrush(Colors.Orange);
                return;
            }

            if (SecurityEnabledCheckBox.IsChecked == true)
            {
                _authService.EnableAuthentication();
                SecurityStatusText.Text = "Authentification activée.";
                SecurityStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E7D32"));
            }
            else
            {
                _authService.DisableAuthentication();
                SecurityStatusText.Text = "Authentification désactivée. L'application s'ouvrira directement.";
                SecurityStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E65100"));
            }
        }

        private void ChangePassword_Click(object sender, RoutedEventArgs e)
        {
            var currentPassword = CurrentPasswordBox.Password;
            var newPassword = NewPasswordBox.Password;
            var confirmPassword = ConfirmNewPasswordBox.Password;

            // Validation
            if (string.IsNullOrWhiteSpace(currentPassword))
            {
                ShowSecurityError("Veuillez entrer votre mot de passe actuel.");
                return;
            }

            if (newPassword != confirmPassword)
            {
                ShowSecurityError("Les nouveaux mots de passe ne correspondent pas.");
                return;
            }

            if (newPassword.Length < 6)
            {
                ShowSecurityError("Le nouveau mot de passe doit contenir au moins 6 caractères.");
                return;
            }

            // Changer le mot de passe
            var (success, error) = _authService.ChangePassword(currentPassword, newPassword);

            if (success)
            {
                ShowSecuritySuccess("Mot de passe modifié avec succès.");
                CurrentPasswordBox.Password = "";
                NewPasswordBox.Password = "";
                ConfirmNewPasswordBox.Password = "";
            }
            else
            {
                ShowSecurityError(error ?? "Erreur lors du changement de mot de passe.");
            }
        }

        private void ChangePin_Click(object sender, RoutedEventArgs e)
        {
            var password = PasswordForPinBox.Password;
            var newPin = NewPinBox.Text;

            // Validation
            if (string.IsNullOrWhiteSpace(password))
            {
                ShowSecurityError("Veuillez entrer votre mot de passe pour confirmer.");
                return;
            }

            if (newPin.Length != 4 || !int.TryParse(newPin, out _))
            {
                ShowSecurityError("Le code PIN doit contenir exactement 4 chiffres.");
                return;
            }

            // Changer le PIN
            var (success, error) = _authService.ChangePin(password, newPin);

            if (success)
            {
                ShowSecuritySuccess("Code PIN modifié avec succès.");
                PasswordForPinBox.Password = "";
                NewPinBox.Text = "";
            }
            else
            {
                ShowSecurityError(error ?? "Erreur lors du changement de code PIN.");
            }
        }

        private void PinBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // N'accepter que les chiffres
            e.Handled = !Regex.IsMatch(e.Text, @"^[0-9]+$");
        }

        private void ShowSecurityError(string message)
        {
            SecurityStatusText.Text = message;
            SecurityStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D32F2F"));
        }

        private void ShowSecuritySuccess(string message)
        {
            SecurityStatusText.Text = message;
            SecurityStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E7D32"));
        }

        private void ResetSecurity_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Voulez-vous vraiment réinitialiser tous les paramètres de sécurité ?\n\n" +
                "Cette action supprimera votre mot de passe et code PIN.\n" +
                "Au prochain démarrage, l'assistant de configuration s'affichera.",
                "Réinitialiser la sécurité",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    _authService.ResetAllSettings();
                    ShowSecuritySuccess("Sécurité réinitialisée. Redémarrez l'application pour voir l'assistant.");

                    // Mettre à jour l'interface
                    SecurityEnabledCheckBox.IsChecked = false;
                    PasswordSection.IsEnabled = false;
                    PinSection.IsEnabled = false;
                }
                catch (Exception ex)
                {
                    ShowSecurityError($"Erreur lors de la réinitialisation : {ex.Message}");
                }
            }
        }
    }
}
