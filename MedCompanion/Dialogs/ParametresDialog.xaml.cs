using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
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

            SettingsSaved = false;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
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
    }
}
