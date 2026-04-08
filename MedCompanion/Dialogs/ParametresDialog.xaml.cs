using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.Services.LLM;
using MedCompanion.Services.Voice;
using MedCompanion.Services.Web;
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
        private readonly AgentConfigService _agentConfigService;
        private readonly SecureStorageService _secureStorage;
        private const string OLLAMA_WEB_API_KEY = "ollama_web_api_key";
        private const string SMTP_PASSWORD_KEY = "pilotage_smtp_password";
        private const string VPS_TOKEN_KEY = "vps_monitoring_token";
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
            _agentConfigService = new AgentConfigService();
            _secureStorage = secureStorage;
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

            // Charger les paramètres des agents
            LoadAgentSettings();
            LoadWebAgentSettings();
            LoadPilotageAgentSettings();

            // Charger la clé API Ollama Web
            LoadOllamaWebApiKey();

            // Charger le mot de passe SMTP Pilotage
            LoadSmtpPassword();

            // Charger les paramètres VPS Monitoring
            LoadVpsSettings();

            // Charger les paramètres Handy (dictée vocale)
            LoadHandySettings();

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
                SaveAgentSettings();
                SaveWebAgentSettings();
                SavePilotageAgentSettings();
                SaveOllamaWebApiKey();
                SaveSmtpPassword();
                SaveVpsSettings();
                SaveHandySettings();

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

        private Task RefreshLocalModelsAsync()
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
            return Task.CompletedTask;
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

        // ===== PARAMÈTRES AGENTS =====

        private async void LoadAgentSettings()
        {
            try
            {
                // Vérifier que les contrôles existent
                if (MedEnabledCheckBox == null || MedTemperatureSlider == null ||
                    MedPostureTextBox == null || MedLLMProviderCombo == null || MedLLMModelCombo == null)
                {
                    System.Diagnostics.Debug.WriteLine("[ParametresDialog] Contrôles Agents non initialisés");
                    return;
                }

                var medConfig = _agentConfigService.GetMedConfig();
                if (medConfig != null)
                {
                    MedEnabledCheckBox.IsChecked = medConfig.IsEnabled;
                    MedTemperatureSlider.Value = medConfig.Temperature;
                    MedPostureTextBox.Text = medConfig.Posture;

                    // Provider LLM - sélectionner sans déclencher l'événement de changement
                    foreach (ComboBoxItem item in MedLLMProviderCombo.Items)
                    {
                        if (item.Content?.ToString() == medConfig.LLMProvider)
                        {
                            MedLLMProviderCombo.SelectedItem = item;
                            break;
                        }
                    }

                    // Modèle LLM - charger la liste puis sélectionner le modèle sauvegardé
                    await UpdateMedModelsListAsync(medConfig.LLMProvider);

                    // Restaurer le modèle sauvegardé s'il existe dans la liste
                    MedLLMModelCombo.Text = medConfig.LLMModel;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ParametresDialog] Erreur chargement paramètres Agents : {ex.Message}");
            }
        }

        private void SaveAgentSettings()
        {
            try
            {
                // Vérifier que les contrôles existent
                if (MedEnabledCheckBox == null || MedTemperatureSlider == null ||
                    MedPostureTextBox == null || MedLLMProviderCombo == null || MedLLMModelCombo == null)
                {
                    System.Diagnostics.Debug.WriteLine("[ParametresDialog] Contrôles Agents non initialisés - sauvegarde ignorée");
                    return;
                }

                var medConfig = _agentConfigService.GetMedConfig() ?? AgentConfig.CreateDefaultMed();

                medConfig.IsEnabled = MedEnabledCheckBox.IsChecked ?? true;
                medConfig.Temperature = MedTemperatureSlider.Value;
                medConfig.Posture = MedPostureTextBox.Text ?? string.Empty;

                // Provider LLM
                if (MedLLMProviderCombo.SelectedItem is ComboBoxItem providerItem)
                {
                    medConfig.LLMProvider = providerItem.Content?.ToString() ?? "OpenAI";
                }

                // Modèle LLM
                medConfig.LLMModel = MedLLMModelCombo.Text ?? "gpt-4o-mini";

                _agentConfigService.UpdateAgentConfig(medConfig);
                _agentConfigService.Save();

                System.Diagnostics.Debug.WriteLine($"[ParametresDialog] Config Med sauvegardée : Provider={medConfig.LLMProvider}, Model={medConfig.LLMModel}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ParametresDialog] Erreur sauvegarde paramètres Agents : {ex.Message}");
                // Ne pas propager l'exception pour ne pas bloquer les autres sauvegardes
            }
        }

        private async void MedLLMProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MedLLMProviderCombo.SelectedItem is ComboBoxItem item)
            {
                var provider = item.Content?.ToString() ?? "OpenAI";
                await UpdateMedModelsListAsync(provider);
            }
        }

        private async Task UpdateMedModelsListAsync(string provider)
        {
            if (MedLLMModelCombo == null) return;

            MedLLMModelCombo.Items.Clear();

            if (provider == "Ollama" && _llmFactory != null)
            {
                // Pour Ollama, charger automatiquement les modèles disponibles
                try
                {
                    RefreshMedModelsBtn.IsEnabled = false;
                    var models = await _llmFactory.GetAvailableOllamaModelsAsync();

                    if (models.Count > 0)
                    {
                        foreach (var model in models)
                        {
                            MedLLMModelCombo.Items.Add(new ComboBoxItem { Content = model });
                        }
                    }
                    else
                    {
                        // Fallback sur les modèles par défaut si Ollama ne répond pas
                        var defaultModels = AgentConfigService.GetDefaultModelsForProvider(provider);
                        foreach (var model in defaultModels)
                        {
                            MedLLMModelCombo.Items.Add(new ComboBoxItem { Content = model });
                        }
                    }
                }
                catch
                {
                    // En cas d'erreur, utiliser les modèles par défaut
                    var defaultModels = AgentConfigService.GetDefaultModelsForProvider(provider);
                    foreach (var model in defaultModels)
                    {
                        MedLLMModelCombo.Items.Add(new ComboBoxItem { Content = model });
                    }
                }
                finally
                {
                    RefreshMedModelsBtn.IsEnabled = true;
                }
            }
            else
            {
                // Pour OpenAI, utiliser la liste par défaut
                var models = AgentConfigService.GetDefaultModelsForProvider(provider);
                foreach (var model in models)
                {
                    MedLLMModelCombo.Items.Add(new ComboBoxItem { Content = model });
                }
            }

            if (MedLLMModelCombo.Items.Count > 0)
            {
                MedLLMModelCombo.SelectedIndex = 0;
            }
        }

        private async void RefreshMedModels_Click(object sender, RoutedEventArgs e)
        {
            if (_llmFactory == null) return;

            RefreshMedModelsBtn.IsEnabled = false;
            try
            {
                var provider = (MedLLMProviderCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "OpenAI";

                if (provider == "Ollama")
                {
                    var models = await _llmFactory.GetAvailableOllamaModelsAsync();

                    MedLLMModelCombo.Items.Clear();
                    foreach (var model in models)
                    {
                        MedLLMModelCombo.Items.Add(new ComboBoxItem { Content = model });
                    }

                    if (MedLLMModelCombo.Items.Count > 0)
                    {
                        MedLLMModelCombo.SelectedIndex = 0;
                    }
                }
                else
                {
                    // Pour OpenAI, utiliser la liste par défaut
                    await UpdateMedModelsListAsync(provider);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du rafraîchissement des modèles :\n{ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                RefreshMedModelsBtn.IsEnabled = true;
            }
        }

        private void ResetMedPosture_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Voulez-vous réinitialiser la posture de Med à sa valeur par défaut ?",
                "Réinitialiser la posture",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var defaultMed = AgentConfig.CreateDefaultMed();
                MedPostureTextBox.Text = defaultMed.Posture;
            }
        }

        // ===== OLLAMA WEB API KEY =====

        private void LoadOllamaWebApiKey()
        {
            try
            {
                var apiKey = _secureStorage.GetApiKey(OLLAMA_WEB_API_KEY);
                if (!string.IsNullOrEmpty(apiKey))
                {
                    OllamaWebPasswordBox.Password = apiKey;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ParametresDialog] Erreur chargement clé Ollama Web : {ex.Message}");
            }
        }

        private void SaveOllamaWebApiKey()
        {
            try
            {
                var apiKey = OllamaWebPasswordBox.Password;
                if (!string.IsNullOrEmpty(apiKey))
                {
                    _secureStorage.SaveApiKey(OLLAMA_WEB_API_KEY, apiKey);
                    System.Diagnostics.Debug.WriteLine("[ParametresDialog] Clé API Ollama Web sauvegardée");
                }
                else
                {
                    // Supprimer la clé si vide
                    _secureStorage.DeleteApiKey(OLLAMA_WEB_API_KEY);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ParametresDialog] Erreur sauvegarde clé Ollama Web : {ex.Message}");
            }
        }

        private async void TestOllamaWebConnection_Click(object sender, RoutedEventArgs e)
        {
            TestOllamaWebBtn.IsEnabled = false;
            try
            {
                var apiKey = OllamaWebPasswordBox.Password;
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    MessageBox.Show("Veuillez entrer une clé API Ollama.", "Test connexion", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Sauvegarder temporairement pour le test
                _secureStorage.SaveApiKey(OLLAMA_WEB_API_KEY, apiKey);

                var webSearchService = new OllamaWebSearchService(_secureStorage);
                var (success, message) = await webSearchService.TestConnectionAsync();

                if (success)
                {
                    MessageBox.Show("Connexion réussie à l'API Ollama Web Search.", "Test réussi", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Échec de la connexion :\n{message}", "Test échoué", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du test :\n{ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                TestOllamaWebBtn.IsEnabled = true;
            }
        }

        // ===== MOT DE PASSE SMTP PILOTAGE =====

        private void LoadSmtpPassword()
        {
            try
            {
                var password = _secureStorage.GetApiKey(SMTP_PASSWORD_KEY);
                if (!string.IsNullOrEmpty(password))
                {
                    SmtpPasswordBox.Password = password;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ParametresDialog] Erreur chargement mot de passe SMTP : {ex.Message}");
            }
        }

        private void SaveSmtpPassword()
        {
            try
            {
                var password = SmtpPasswordBox.Password;
                if (!string.IsNullOrEmpty(password))
                {
                    _secureStorage.SaveApiKey(SMTP_PASSWORD_KEY, password);
                    // Mettre à jour aussi dans AppSettings pour que PilotageEmailService puisse l'utiliser
                    _viewModel.Settings.SmtpPassword = password;
                    System.Diagnostics.Debug.WriteLine("[ParametresDialog] Mot de passe SMTP sauvegardé");
                }
                else
                {
                    // Supprimer le mot de passe si vide
                    _secureStorage.DeleteApiKey(SMTP_PASSWORD_KEY);
                    _viewModel.Settings.SmtpPassword = "";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ParametresDialog] Erreur sauvegarde mot de passe SMTP : {ex.Message}");
            }
        }

        private void TestSmtpConnection_Click(object sender, RoutedEventArgs e)
        {
            TestSmtpBtn.IsEnabled = false;
            try
            {
                var password = SmtpPasswordBox.Password;
                if (string.IsNullOrWhiteSpace(password))
                {
                    MessageBox.Show("Veuillez entrer le mot de passe SMTP.", "Test connexion", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Mettre à jour temporairement le mot de passe dans les settings pour le test
                var tempSettings = AppSettings.Load();
                tempSettings.SmtpPassword = password;

                var emailService = new PilotageEmailService(tempSettings);
                var (success, error) = emailService.TestConnection();

                if (success)
                {
                    MessageBox.Show(
                        "Configuration SMTP valide.\n\n" +
                        $"Serveur: {tempSettings.SmtpHost}:{tempSettings.SmtpPort}\n" +
                        $"Expéditeur: {tempSettings.SmtpFromEmail}\n\n" +
                        "Note: Le test réel d'envoi se fera lors du premier email.",
                        "Configuration valide",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Configuration invalide :\n{error}", "Erreur configuration", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du test :\n{ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                TestSmtpBtn.IsEnabled = true;
            }
        }

        // ===== VPS MONITORING =====

        private void LoadVpsSettings()
        {
            try
            {
                VpsUrlTextBox.Text = _viewModel.Settings.VpsMonitoringUrl;
                VpsEnabledCheckBox.IsChecked = _viewModel.Settings.VpsMonitoringEnabled;

                var token = _secureStorage.GetApiKey(VPS_TOKEN_KEY);
                if (!string.IsNullOrEmpty(token))
                    VpsTokenBox.Password = token;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ParametresDialog] Erreur chargement VPS : {ex.Message}");
            }
        }

        private void SaveVpsSettings()
        {
            try
            {
                _viewModel.Settings.VpsMonitoringUrl = VpsUrlTextBox.Text.Trim();
                _viewModel.Settings.VpsMonitoringEnabled = VpsEnabledCheckBox.IsChecked == true;

                var token = VpsTokenBox.Password;
                if (!string.IsNullOrEmpty(token))
                    _secureStorage.SaveApiKey(VPS_TOKEN_KEY, token);
                else
                    _secureStorage.DeleteApiKey(VPS_TOKEN_KEY);

                System.Diagnostics.Debug.WriteLine("[ParametresDialog] Paramètres VPS sauvegardés");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ParametresDialog] Erreur sauvegarde VPS : {ex.Message}");
            }
        }

        private async void TestVpsConnection_Click(object sender, RoutedEventArgs e)
        {
            TestVpsBtn.IsEnabled = false;
            try
            {
                var url = VpsUrlTextBox.Text.Trim();
                var token = VpsTokenBox.Password;

                if (string.IsNullOrWhiteSpace(url))
                {
                    MessageBox.Show("Veuillez entrer l'URL du VPS.", "Test connexion", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                using var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                if (!string.IsNullOrWhiteSpace(token))
                    http.DefaultRequestHeaders.Add("X-Token", token);

                var response = await http.GetAsync($"{url.TrimEnd('/')}/metrics");

                if (response.IsSuccessStatusCode)
                    MessageBox.Show("✅ Connexion réussie ! Le serveur VPS répond correctement.", "Test VPS", MessageBoxButton.OK, MessageBoxImage.Information);
                else
                    MessageBox.Show($"⚠️ Le serveur a répondu avec le code {(int)response.StatusCode}.\nVérifiez le token ou l'URL.", "Test VPS", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"❌ Impossible de joindre le VPS :\n{ex.Message}", "Test VPS", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                TestVpsBtn.IsEnabled = true;
            }
        }

        // ===== SUB-AGENT WEB =====

        private async void LoadWebAgentSettings()
        {
            try
            {
                if (WebEnabledCheckBox == null || WebTemperatureSlider == null ||
                    WebPostureTextBox == null || WebLLMProviderCombo == null || WebLLMModelCombo == null)
                {
                    return;
                }

                var webConfig = _agentConfigService.GetWebConfig();
                if (webConfig != null)
                {
                    WebEnabledCheckBox.IsChecked = webConfig.IsEnabled;
                    WebTemperatureSlider.Value = webConfig.Temperature;
                    WebPostureTextBox.Text = webConfig.Posture;

                    // Provider LLM
                    foreach (ComboBoxItem item in WebLLMProviderCombo.Items)
                    {
                        if (item.Content?.ToString() == webConfig.LLMProvider)
                        {
                            WebLLMProviderCombo.SelectedItem = item;
                            break;
                        }
                    }

                    // Modèle LLM
                    await UpdateWebModelsListAsync(webConfig.LLMProvider);
                    WebLLMModelCombo.Text = webConfig.LLMModel;

                    // Max résultats
                    foreach (ComboBoxItem item in WebMaxResultsCombo.Items)
                    {
                        if (item.Content?.ToString() == webConfig.MaxSearchResults.ToString())
                        {
                            WebMaxResultsCombo.SelectedItem = item;
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ParametresDialog] Erreur chargement paramètres Web Agent : {ex.Message}");
            }
        }

        private void SaveWebAgentSettings()
        {
            try
            {
                if (WebEnabledCheckBox == null || WebTemperatureSlider == null ||
                    WebPostureTextBox == null || WebLLMProviderCombo == null || WebLLMModelCombo == null)
                {
                    return;
                }

                var webConfig = _agentConfigService.GetWebConfig() ?? AgentConfig.CreateDefaultWeb();

                webConfig.IsEnabled = WebEnabledCheckBox.IsChecked ?? false;
                webConfig.Temperature = WebTemperatureSlider.Value;
                webConfig.Posture = WebPostureTextBox.Text ?? string.Empty;

                // Provider LLM
                if (WebLLMProviderCombo.SelectedItem is ComboBoxItem providerItem)
                {
                    webConfig.LLMProvider = providerItem.Content?.ToString() ?? "Ollama";
                }

                // Modèle LLM
                webConfig.LLMModel = WebLLMModelCombo.Text ?? "llama3";

                // Max résultats
                if (WebMaxResultsCombo.SelectedItem is ComboBoxItem maxItem)
                {
                    if (int.TryParse(maxItem.Content?.ToString(), out int maxResults))
                    {
                        webConfig.MaxSearchResults = maxResults;
                    }
                }

                _agentConfigService.UpdateAgentConfig(webConfig);
                _agentConfigService.Save();  // Persister dans le fichier JSON

                System.Diagnostics.Debug.WriteLine($"[ParametresDialog] Config Web Agent sauvegardée : Provider={webConfig.LLMProvider}, Model={webConfig.LLMModel}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ParametresDialog] Erreur sauvegarde paramètres Web Agent : {ex.Message}");
            }
        }

        private async void WebLLMProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (WebLLMProviderCombo.SelectedItem is ComboBoxItem item)
            {
                var provider = item.Content?.ToString() ?? "Ollama";
                await UpdateWebModelsListAsync(provider);
            }
        }

        private async Task UpdateWebModelsListAsync(string provider)
        {
            if (WebLLMModelCombo == null) return;

            WebLLMModelCombo.Items.Clear();

            if (provider == "Ollama" && _llmFactory != null)
            {
                try
                {
                    RefreshWebModelsBtn.IsEnabled = false;
                    var models = await _llmFactory.GetAvailableOllamaModelsAsync();

                    if (models.Count > 0)
                    {
                        foreach (var model in models)
                        {
                            WebLLMModelCombo.Items.Add(new ComboBoxItem { Content = model });
                        }
                    }
                    else
                    {
                        var defaultModels = AgentConfigService.GetDefaultModelsForProvider(provider);
                        foreach (var model in defaultModels)
                        {
                            WebLLMModelCombo.Items.Add(new ComboBoxItem { Content = model });
                        }
                    }
                }
                catch
                {
                    var defaultModels = AgentConfigService.GetDefaultModelsForProvider(provider);
                    foreach (var model in defaultModels)
                    {
                        WebLLMModelCombo.Items.Add(new ComboBoxItem { Content = model });
                    }
                }
                finally
                {
                    RefreshWebModelsBtn.IsEnabled = true;
                }
            }
            else
            {
                var models = AgentConfigService.GetDefaultModelsForProvider(provider);
                foreach (var model in models)
                {
                    WebLLMModelCombo.Items.Add(new ComboBoxItem { Content = model });
                }
            }

            if (WebLLMModelCombo.Items.Count > 0)
            {
                WebLLMModelCombo.SelectedIndex = 0;
            }
        }

        private async void RefreshWebModels_Click(object sender, RoutedEventArgs e)
        {
            if (_llmFactory == null) return;

            RefreshWebModelsBtn.IsEnabled = false;
            try
            {
                var provider = (WebLLMProviderCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Ollama";

                if (provider == "Ollama")
                {
                    var models = await _llmFactory.GetAvailableOllamaModelsAsync();

                    WebLLMModelCombo.Items.Clear();
                    foreach (var model in models)
                    {
                        WebLLMModelCombo.Items.Add(new ComboBoxItem { Content = model });
                    }

                    if (WebLLMModelCombo.Items.Count > 0)
                    {
                        WebLLMModelCombo.SelectedIndex = 0;
                    }
                }
                else
                {
                    await UpdateWebModelsListAsync(provider);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du rafraîchissement des modèles :\n{ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                RefreshWebModelsBtn.IsEnabled = true;
            }
        }

        private void ResetWebPosture_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Voulez-vous réinitialiser la posture du Sub-Agent Web à sa valeur par défaut ?",
                "Réinitialiser la posture",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var defaultWeb = AgentConfig.CreateDefaultWeb();
                WebPostureTextBox.Text = defaultWeb.Posture;
            }
        }

        // ===== AGENT DE PILOTAGE =====

        private async void LoadPilotageAgentSettings()
        {
            try
            {
                if (PilotageEnabledCheckBox == null || PilotageTemperatureSlider == null ||
                    PilotagePostureTextBox == null || PilotageLLMProviderCombo == null || PilotageLLMModelCombo == null)
                {
                    return;
                }

                var pilotageConfig = _agentConfigService.GetPilotageConfig();
                if (pilotageConfig != null)
                {
                    PilotageEnabledCheckBox.IsChecked = pilotageConfig.IsEnabled;
                    PilotageTemperatureSlider.Value = pilotageConfig.Temperature;
                    PilotagePostureTextBox.Text = pilotageConfig.Posture;

                    // Provider LLM
                    foreach (ComboBoxItem item in PilotageLLMProviderCombo.Items)
                    {
                        if (item.Content?.ToString() == pilotageConfig.LLMProvider)
                        {
                            PilotageLLMProviderCombo.SelectedItem = item;
                            break;
                        }
                    }

                    // Modèle LLM
                    await UpdatePilotageModelsListAsync(pilotageConfig.LLMProvider);
                    PilotageLLMModelCombo.Text = pilotageConfig.LLMModel;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ParametresDialog] Erreur chargement paramètres Pilotage Agent : {ex.Message}");
            }
        }

        private void SavePilotageAgentSettings()
        {
            try
            {
                if (PilotageEnabledCheckBox == null || PilotageTemperatureSlider == null ||
                    PilotagePostureTextBox == null || PilotageLLMProviderCombo == null || PilotageLLMModelCombo == null)
                {
                    return;
                }

                var pilotageConfig = _agentConfigService.GetPilotageConfig() ?? AgentConfig.CreateDefaultPilotage();

                pilotageConfig.IsEnabled = PilotageEnabledCheckBox.IsChecked ?? true;
                pilotageConfig.Temperature = PilotageTemperatureSlider.Value;
                pilotageConfig.Posture = PilotagePostureTextBox.Text ?? string.Empty;

                // Provider LLM
                if (PilotageLLMProviderCombo.SelectedItem is ComboBoxItem providerItem)
                {
                    pilotageConfig.LLMProvider = providerItem.Content?.ToString() ?? "Ollama";
                }

                // Modèle LLM
                pilotageConfig.LLMModel = PilotageLLMModelCombo.Text ?? "gpt-os:20b";

                _agentConfigService.UpdateAgentConfig(pilotageConfig);
                _agentConfigService.Save();

                System.Diagnostics.Debug.WriteLine($"[ParametresDialog] Config Pilotage Agent sauvegardée : Provider={pilotageConfig.LLMProvider}, Model={pilotageConfig.LLMModel}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ParametresDialog] Erreur sauvegarde paramètres Pilotage Agent : {ex.Message}");
            }
        }

        private async void PilotageLLMProvider_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PilotageLLMProviderCombo.SelectedItem is ComboBoxItem item)
            {
                var provider = item.Content?.ToString() ?? "Ollama";
                await UpdatePilotageModelsListAsync(provider);
            }
        }

        private async Task UpdatePilotageModelsListAsync(string provider)
        {
            if (PilotageLLMModelCombo == null) return;

            PilotageLLMModelCombo.Items.Clear();

            if (provider == "Ollama" && _llmFactory != null)
            {
                try
                {
                    RefreshPilotageModelsBtn.IsEnabled = false;
                    var models = await _llmFactory.GetAvailableOllamaModelsAsync();

                    if (models.Count > 0)
                    {
                        foreach (var model in models)
                        {
                            PilotageLLMModelCombo.Items.Add(new ComboBoxItem { Content = model });
                        }
                    }
                    else
                    {
                        var defaultModels = AgentConfigService.GetDefaultModelsForProvider(provider);
                        foreach (var model in defaultModels)
                        {
                            PilotageLLMModelCombo.Items.Add(new ComboBoxItem { Content = model });
                        }
                    }
                }
                catch
                {
                    var defaultModels = AgentConfigService.GetDefaultModelsForProvider(provider);
                    foreach (var model in defaultModels)
                    {
                        PilotageLLMModelCombo.Items.Add(new ComboBoxItem { Content = model });
                    }
                }
                finally
                {
                    RefreshPilotageModelsBtn.IsEnabled = true;
                }
            }
            else
            {
                var models = AgentConfigService.GetDefaultModelsForProvider(provider);
                foreach (var model in models)
                {
                    PilotageLLMModelCombo.Items.Add(new ComboBoxItem { Content = model });
                }
            }

            if (PilotageLLMModelCombo.Items.Count > 0)
            {
                PilotageLLMModelCombo.SelectedIndex = 0;
            }
        }

        private async void RefreshPilotageModels_Click(object sender, RoutedEventArgs e)
        {
            if (_llmFactory == null) return;

            RefreshPilotageModelsBtn.IsEnabled = false;
            try
            {
                var provider = (PilotageLLMProviderCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Ollama";

                if (provider == "Ollama")
                {
                    var models = await _llmFactory.GetAvailableOllamaModelsAsync();

                    PilotageLLMModelCombo.Items.Clear();
                    foreach (var model in models)
                    {
                        PilotageLLMModelCombo.Items.Add(new ComboBoxItem { Content = model });
                    }

                    if (PilotageLLMModelCombo.Items.Count > 0)
                    {
                        PilotageLLMModelCombo.SelectedIndex = 0;
                    }
                }
                else
                {
                    await UpdatePilotageModelsListAsync(provider);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du rafraîchissement des modèles :\n{ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                RefreshPilotageModelsBtn.IsEnabled = true;
            }
        }

        private void ResetPilotagePosture_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Voulez-vous réinitialiser la posture de l'Agent de Pilotage à sa valeur par défaut ?",
                "Réinitialiser la posture",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var defaultPilotage = AgentConfig.CreateDefaultPilotage();
                PilotagePostureTextBox.Text = defaultPilotage.Posture;
            }
        }

        // ===== PARAMÈTRES HANDY (DICTÉE VOCALE) =====

        private void LoadHandySettings()
        {
            try
            {
                var settings = AppSettings.Load();

                // Charger l'état activé/désactivé
                HandyEnabledCheckBox.IsChecked = settings.HandyEnabled;

                // Charger le hotkey
                var hotkey = settings.HandyHotkey ?? "Ctrl+Space";

                // Chercher dans les items existants ou définir le texte
                bool found = false;
                foreach (ComboBoxItem item in HandyHotkeyCombo.Items)
                {
                    if (item.Content?.ToString() == hotkey)
                    {
                        HandyHotkeyCombo.SelectedItem = item;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    // Si le hotkey n'est pas dans la liste, l'ajouter comme texte (ComboBox éditable)
                    HandyHotkeyCombo.Text = hotkey;
                }

                System.Diagnostics.Debug.WriteLine($"[ParametresDialog] Paramètres Handy chargés : Enabled={settings.HandyEnabled}, Hotkey={hotkey}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ParametresDialog] Erreur chargement paramètres Handy : {ex.Message}");
                // Valeurs par défaut
                HandyEnabledCheckBox.IsChecked = true;
                HandyHotkeyCombo.SelectedIndex = 0; // Ctrl+Space
            }
        }

        private void SaveHandySettings()
        {
            try
            {
                // Récupérer le hotkey (soit sélectionné, soit tapé)
                string hotkey;
                if (HandyHotkeyCombo.SelectedItem is ComboBoxItem item)
                {
                    hotkey = item.Content?.ToString() ?? "Ctrl+Space";
                }
                else
                {
                    hotkey = HandyHotkeyCombo.Text ?? "Ctrl+Space";
                }

                // Sauvegarder dans AppSettings via _viewModel.Settings
                _viewModel.Settings.HandyEnabled = HandyEnabledCheckBox.IsChecked ?? true;
                _viewModel.Settings.HandyHotkey = hotkey;

                System.Diagnostics.Debug.WriteLine($"[ParametresDialog] Paramètres Handy sauvegardés : Enabled={_viewModel.Settings.HandyEnabled}, Hotkey={hotkey}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ParametresDialog] Erreur sauvegarde paramètres Handy : {ex.Message}");
            }
        }

        private async void TestHandy_Click(object sender, RoutedEventArgs e)
        {
            TestHandyBtn.IsEnabled = false;
            try
            {
                // Récupérer le hotkey configuré
                string hotkey;
                if (HandyHotkeyCombo.SelectedItem is ComboBoxItem item)
                {
                    hotkey = item.Content?.ToString() ?? "Ctrl+Space";
                }
                else
                {
                    hotkey = HandyHotkeyCombo.Text ?? "Ctrl+Space";
                }

                // Créer une instance du service pour tester
                var voiceService = new HandyVoiceInputService { Hotkey = hotkey };

                MessageBox.Show(
                    $"Test du raccourci : {hotkey}\n\n" +
                    "Assurez-vous que Handy est en cours d'exécution.\n" +
                    "Le raccourci va être simulé dans 2 secondes.\n\n" +
                    "Si Handy réagit, la configuration est correcte.",
                    "Test Handy",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                await Task.Delay(2000);

                // Simuler le raccourci
                await voiceService.ToggleRecordingAsync();

                // Attendre un peu puis arrêter
                await Task.Delay(3000);
                if (voiceService.IsRecording)
                {
                    await voiceService.StopRecordingAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Erreur lors du test :\n{ex.Message}",
                    "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                TestHandyBtn.IsEnabled = true;
            }
        }
    }
}
