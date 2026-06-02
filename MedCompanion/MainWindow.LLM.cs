using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MedCompanion.Services.LLM;

namespace MedCompanion;

public partial class MainWindow : Window
{
    // ===== SYSTÈME LLM =====
    
    // TODO: Copier ICI les méthodes LLM depuis MainWindow.xaml.cs
    
    private async void InitializeLLMSystem()
    {
        try
        {
            // La factory est déjà initialisée de manière synchrone dans le constructeur
            // On lance juste le warm-up en arrière-plan
            
            // S'abonner aux événements de warm-up
            _warmupService.StatusChanged += OnLLMWarmupStatusChanged;
            
            // Charger les modèles Ollama disponibles et peupler le ComboBox
            await PopulateLLMComboBoxAsync();
            
            // Lancer le warm-up automatique en arrière-plan
            _ = Task.Run(async () =>
            {
                await _warmupService.WarmupAsync();
            });
        }
        catch (Exception ex)
        {
            Dispatcher.Invoke(() =>
            {
                StatusTextBlock.Text = $"❌ Erreur initialisation LLM: {ex.Message}";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            });
        }
    }
    
    private void OnLLMWarmupStatusChanged(object? sender, WarmupStatusEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            // Mettre à jour l'indicateur selon le statut
            switch (e.Status)
            {
                case "initializing":
                case "checking":
                case "warming":
                    LLMStatusIndicator.Background = new SolidColorBrush(Color.FromRgb(255, 193, 7)); // Orange
                    LLMStatusIndicator.ToolTip = e.Message;
                    break;
                
                case "ready":
                    LLMStatusIndicator.Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Vert
                    LLMStatusIndicator.ToolTip = e.Message;
                    _currentLLMService = _llmFactory.GetCurrentProvider();
                    break;
                
                case "error":
                    LLMStatusIndicator.Background = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Rouge
                    LLMStatusIndicator.ToolTip = e.Message;
                    break;
                
                case "fallback":
                case "warning":
                    LLMStatusIndicator.Background = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Orange foncé
                    LLMStatusIndicator.ToolTip = e.Message;
                    break;
            }
            
            // Mettre à jour le texte de statut
            StatusTextBlock.Text = e.Message;
            StatusTextBlock.Foreground = new SolidColorBrush(
                e.Status == "ready" ? Colors.Green :
                e.Status == "error" ? Colors.Red :
                Colors.Blue
            );
        });
    }
    
    private async Task PopulateLLMComboBoxAsync()
    {
        try
        {
            LLMModelCombo.Items.Clear();
            
            // Vérifier si Ollama est disponible
            var ollamaAvailable = await _llmFactory.IsOllamaAvailableAsync();
            
            if (ollamaAvailable)
            {
                // Récupérer les modèles Ollama
                var ollamaModels = await _llmFactory.GetAvailableOllamaModelsAsync();
                
                if (ollamaModels.Any())
                {
                    // Ajouter header LOCAL
                    var localHeader = new ComboBoxItem
                    {
                        Content = "🖥️ LOCAL (Ollama)",
                        IsEnabled = false,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(76, 175, 80))
                    };
                    LLMModelCombo.Items.Add(localHeader);
                    
                    // Ajouter chaque modèle Ollama
                    foreach (var model in ollamaModels)
                    {
                        var item = new ComboBoxItem
                        {
                            Content = $"  {model}",
                            Tag = new { Provider = "Ollama", Model = model }
                        };
                        LLMModelCombo.Items.Add(item);
                    }
                    
                    // Séparateur
                    var separator = new ComboBoxItem
                    {
                        Content = "─────────────",
                        IsEnabled = false
                    };
                    LLMModelCombo.Items.Add(separator);
                }
            }
            
            // Ajouter header CLOUD
            var cloudHeader = new ComboBoxItem
            {
                Content = "☁️ CLOUD (OpenAI)",
                IsEnabled = false,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(52, 152, 219))
            };
            LLMModelCombo.Items.Add(cloudHeader);
            
            // Ajouter OpenAI
            var openAIItem = new ComboBoxItem
            {
                Content = $"  {_settings.OpenAIModel}",
                Tag = new { Provider = "OpenAI", Model = _settings.OpenAIModel }
            };
            LLMModelCombo.Items.Add(openAIItem);
            
            // Sélectionner le modèle actuel selon la config
            SelectCurrentModel();
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"⚠️ Erreur chargement modèles: {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Orange);
        }
    }
    
    private void SelectCurrentModel()
    {
        foreach (var item in LLMModelCombo.Items)
        {
            if (item is ComboBoxItem comboItem && comboItem.Tag != null)
            {
                var tag = comboItem.Tag as dynamic;
                if (tag.Provider == _settings.LLMProvider && 
                    (_settings.LLMProvider == "OpenAI" || tag.Model == _settings.OllamaModel))
                {
                    LLMModelCombo.SelectedItem = comboItem;
                    return;
                }
            }
        }
    }
    
    /// <summary>
    /// Décharge le modèle LLM courant de la VRAM. Wipe le KV cache et libère la mémoire GPU.
    /// Utile quand Med "dérive" après usage prolongé sur plusieurs patients.
    /// Pour OpenAI : no-op (service distant sans état local).
    /// </summary>
    private async void UnloadModelBtn_Click(object sender, RoutedEventArgs e)
    {
        var provider = _currentLLMService;
        if (provider == null) return;

        UnloadModelBtn.IsEnabled = false;
        var originalBrush       = UnloadModelBtn.BorderBrush;
        UnloadModelBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 193, 7));   // orange : opération en cours

        try
        {
            var (success, message) = await provider.UnloadAsync();
            if (success)
            {
                UnloadModelBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(76, 175, 80));   // vert : OK
                StatusTextBlock.Text       = $"💤 {message}";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
                // Couleur de l'indicateur principal : "froid" jusqu'au prochain appel
                LLMStatusIndicator.Background = new SolidColorBrush(Color.FromRgb(149, 165, 166));   // gris
                LLMStatusIndicator.ToolTip    = "Med déchargé. Premier appel : ~5-10s de réveil.";
            }
            else
            {
                UnloadModelBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(244, 67, 54));    // rouge : échec
                StatusTextBlock.Text       = $"❌ {message}";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text       = $"❌ Erreur déchargement : {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            UnloadModelBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(244, 67, 54));
        }
        finally
        {
            UnloadModelBtn.IsEnabled = true;
            // Revenir au cadre neutre après 2 secondes
            _ = Task.Delay(2000).ContinueWith(_ =>
                Dispatcher.Invoke(() => UnloadModelBtn.BorderBrush = originalBrush));
        }
    }

    /// <summary>
    /// Bouton manuel "🎙 Reset Whisper" — appelé quand l'utilisateur sent que la qualité
    /// de transcription se dégrade sur une longue session de consultations.
    /// </summary>
    private async void WhisperResetButton_Click(object sender, RoutedEventArgs e)
    {
        var whisper = _whisperStreamingService;
        if (whisper == null) return;

        WhisperResetButton.IsEnabled = false;
        var originalBrush = WhisperResetButton.BorderBrush;
        WhisperResetButton.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 193, 7));   // orange : en cours

        try
        {
            var (success, message) = await whisper.ResetEngineAsync();
            if (success)
            {
                WhisperResetButton.BorderBrush = new SolidColorBrush(Color.FromRgb(76, 175, 80));   // vert
                StatusTextBlock.Text       = $"🎙 {message}";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
            }
            else
            {
                WhisperResetButton.BorderBrush = new SolidColorBrush(Color.FromRgb(244, 67, 54));   // rouge
                StatusTextBlock.Text       = $"❌ {message}";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text       = $"❌ Erreur reset Whisper : {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            WhisperResetButton.BorderBrush = new SolidColorBrush(Color.FromRgb(244, 67, 54));
        }
        finally
        {
            WhisperResetButton.IsEnabled = true;
            _ = Task.Delay(2000).ContinueWith(_ =>
                Dispatcher.Invoke(() => WhisperResetButton.BorderBrush = originalBrush));
        }
    }

    /// <summary>
    /// Déchargement silencieux du modèle (fire-and-forget) — appelé automatiquement
    /// lors du changement de patient pour éviter la contamination de contexte entre dossiers.
    /// </summary>
    private void UnloadModelSilently()
    {
        var provider = _currentLLMService;
        if (provider == null) return;
        // Fire-and-forget : on ne bloque pas le changement de patient sur l'unload
        _ = Task.Run(async () =>
        {
            try
            {
                await provider.UnloadAsync();
                Dispatcher.InvokeAsync(() =>
                {
                    LLMStatusIndicator.Background = new SolidColorBrush(Color.FromRgb(149, 165, 166));   // gris
                    LLMStatusIndicator.ToolTip    = "Med déchargé (changement patient).";
                });
            }
            catch { /* silencieux : meilleur effort */ }
        });
    }

    /// <summary>
    /// Réinitialise le processor Whisper en arrière-plan, sans bloquer l'UI.
    /// Appelé au changement de patient pour éviter la dégradation progressive de la
    /// qualité de transcription (KV cache décodeur, fragmentation VRAM, résidus de contexte).
    /// </summary>
    private void ResetWhisperEngineSilently()
    {
        var whisper = _whisperStreamingService;
        if (whisper == null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var (ok, _) = await whisper.ResetEngineAsync();
                if (ok)
                {
                    Dispatcher.InvokeAsync(() =>
                    {
                        if (WhisperResetButton != null)
                            WhisperResetButton.ToolTip = "Whisper réinitialisé (changement patient).";
                    });
                }
            }
            catch { /* silencieux : meilleur effort */ }
        });
    }

    private async void LLMModelCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LLMModelCombo.SelectedItem is not ComboBoxItem selectedItem || selectedItem.Tag == null)
            return;
        
        try
        {
            var tag = selectedItem.Tag as dynamic;
            var provider = tag.Provider as string;
            var model = tag.Model as string;
            
            // Indicateur en orange pendant le changement
            LLMStatusIndicator.Background = new SolidColorBrush(Color.FromRgb(255, 193, 7));
            LLMStatusIndicator.ToolTip = $"Changement vers {provider} ({model})...";
            
            StatusTextBlock.Text = $"⏳ Changement vers {provider} ({model})...";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);
            
            // Effectuer le changement avec warm-up
            var (success, message) = await _llmFactory.SwitchProviderAsync(provider!, model);
            
            if (success)
            {
                _currentLLMService = _llmFactory.GetCurrentProvider();
                
                // Sauvegarder le choix dans les paramètres
                _settings.Save();
                
                LLMStatusIndicator.Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Vert
                LLMStatusIndicator.ToolTip = message;
                
                StatusTextBlock.Text = message;
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
            }
            else
            {
                LLMStatusIndicator.Background = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Rouge
                LLMStatusIndicator.ToolTip = message;
                
                StatusTextBlock.Text = $"❌ {message}";
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
                
                // Revenir à la sélection précédente
                SelectCurrentModel();
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"❌ Erreur: {ex.Message}";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Red);
            
            LLMStatusIndicator.Background = new SolidColorBrush(Color.FromRgb(244, 67, 54));
            LLMStatusIndicator.ToolTip = $"Erreur: {ex.Message}";
        }
    }
}
