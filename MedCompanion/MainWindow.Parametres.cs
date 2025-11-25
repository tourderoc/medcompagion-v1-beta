using System;
using System.Windows;
using MedCompanion.Dialogs;
using MedCompanion.Services;

namespace MedCompanion;

public partial class MainWindow : Window
{
    private SecureStorageService? _secureStorageService;
    private WindowStateService? _windowStateService;

    /// <summary>
    /// Handler pour le bouton Paramètres
    /// </summary>
    private void ParametresBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Créer les services s'ils n'existent pas encore
            _secureStorageService ??= new SecureStorageService();
            _windowStateService ??= new WindowStateService();

            // Ouvrir le dialogue de paramètres (✅ AVEC PromptTracker)
            var dialog = new ParametresDialog(_secureStorageService, _windowStateService, _llmFactory, _promptTracker);
            dialog.Owner = this;
            
            if (dialog.ShowDialog() == true && dialog.SettingsSaved)
            {
                // Les paramètres ont été sauvegardés
                StatusTextBlock.Text = "✅ Paramètres enregistrés";
                StatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);

                // Recharger le service LLM avec les nouvelles clés
                _ = ReloadLLMServiceAsync();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Erreur lors de l'ouverture des paramètres :\n{ex.Message}",
                "Erreur",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    /// <summary>
    /// Recharge le service LLM avec les nouvelles clés API
    /// </summary>
    private async Task ReloadLLMServiceAsync()
    {
        try
        {
            StatusTextBlock.Text = "⏳ Rechargement du service LLM...";
            StatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Blue);

            // Indicateur en orange pendant le rechargement
            LLMStatusIndicator.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 193, 7));
            LLMStatusIndicator.ToolTip = "Rechargement du service LLM...";

            // Réinitialiser le service LLM
            _currentLLMService = await _llmFactory.InitializeAsync();

            // Vérifier la connexion
            var (isConnected, message) = await _currentLLMService.CheckConnectionAsync();

            if (isConnected)
            {
                // Indicateur vert
                LLMStatusIndicator.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(76, 175, 80));
                LLMStatusIndicator.ToolTip = message;
                
                StatusTextBlock.Text = "✅ Service LLM rechargé avec succès";
                StatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
            }
            else
            {
                // Indicateur rouge
                LLMStatusIndicator.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 67, 54));
                LLMStatusIndicator.ToolTip = message;
                
                StatusTextBlock.Text = $"⚠️ {message}";
                StatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Orange);
            }
        }
        catch (Exception ex)
        {
            LLMStatusIndicator.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 67, 54));
            LLMStatusIndicator.ToolTip = $"Erreur: {ex.Message}";
            
            StatusTextBlock.Text = $"❌ Erreur rechargement LLM: {ex.Message}";
            StatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
        }
    }

    /// <summary>
    /// Gère l'événement Window_Loaded pour restaurer l'état de la fenêtre
    /// </summary>
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Créer le service si nécessaire
            _windowStateService ??= new WindowStateService();

            // Restaurer l'état de la fenêtre
            _windowStateService.RestoreWindowState(this);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur lors de la restauration de l'état de la fenêtre: {ex.Message}");
        }
    }

    /// <summary>
    /// Gère l'événement Window_Closing pour sauvegarder l'état de la fenêtre
    /// </summary>
    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        try
        {
            // Créer le service si nécessaire
            _windowStateService ??= new WindowStateService();

            // Sauvegarder l'état de la fenêtre
            _windowStateService.SaveWindowState(this);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Erreur lors de la sauvegarde de l'état de la fenêtre: {ex.Message}");
        }
    }

    /// <summary>
    /// Gère la migration automatique des clés API depuis les variables d'environnement
    /// </summary>
    private void HandleApiKeyMigration(string envKey)
    {
        try
        {
            var result = MessageBox.Show(
                "Une clé API OpenAI a été détectée dans les variables d'environnement.\n\n" +
                "Voulez-vous l'importer dans le stockage sécurisé de l'application ?\n\n" +
                "Cela permettra de gérer vos clés directement depuis l'interface de MedCompanion.",
                "Migration de clé API",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (result == MessageBoxResult.Yes)
            {
                _secureStorageService ??= new SecureStorageService();
                _secureStorageService.SaveApiKey("OpenAI", envKey);

                MessageBox.Show(
                    "✅ Clé API importée avec succès !\n\n" +
                    "Vous pouvez maintenant gérer vos clés depuis le menu Paramètres (⚙️).",
                    "Migration réussie",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                StatusTextBlock.Text = "✅ Clé API OpenAI importée";
                StatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Erreur lors de la migration de la clé API :\n{ex.Message}",
                "Erreur",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }
}
