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

            // Badge de débit : alimenté par les providers locaux après chaque génération.
            Services.LLM.LlmThroughputMonitor.Measured += OnThroughputMeasured;

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
                    // Les modèles Ollama suffixés "-cloud" ne sont PAS locaux : aucun poids sur le
                    // disque, l'inférence part sur les serveurs Ollama. Les afficher sous l'en-tête
                    // "LOCAL" ferait croire à tort qu'ils respectent le secret médical.
                    var localModels = ollamaModels.Where(m => !IsOllamaCloudModel(m)).ToList();
                    var cloudModels = ollamaModels.Where(IsOllamaCloudModel).ToList();

                    if (localModels.Any())
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

                        // Les modèles Ollama restent servis par Ollama. Les variantes llama.cpp sont
                        // listées séparément ci-dessous plutôt que de détourner ces entrées : un même
                        // modèle peut ainsi être essayé via l'un ou l'autre moteur et comparé.
                        foreach (var model in localModels)
                        {
                            var item = new ComboBoxItem
                            {
                                Content = $"  {model}",
                                Tag = new { Provider = "Ollama", Model = model }
                            };
                            LLMModelCombo.Items.Add(item);
                        }
                    }

                    // Section dédiée au moteur local : un profil = une entrée, avec ses propres
                    // réglages (contexte, MTP, cache KV) pilotables dans Pilotage → Moteur local.
                    if (LlamaCppProfiles.Enabled)
                    {
                        LLMModelCombo.Items.Add(new ComboBoxItem
                        {
                            Content = "⚙️ MOTEUR LOCAL (llama.cpp)",
                            IsEnabled = false,
                            FontWeight = FontWeights.Bold,
                            Foreground = new SolidColorBrush(Color.FromRgb(230, 126, 34))
                        });

                        foreach (var profile in LlamaCppProfiles.All)
                        {
                            // Suffixe « cpp » : le nom du modèle seul ne suffit pas à distinguer la
                            // version servie par Ollama de celle servie ici, et une fois la liste
                            // refermée seul l'item sélectionné reste visible (pas son en-tête).
                            var label = $"  {profile.ShortName} · cpp";

                            // Un téléchargement en cours laisse un fichier présent mais tronqué :
                            // le proposer ferait échouer le chargement. On affiche la progression.
                            var progress = profile.DownloadProgress;
                            if (progress is double pct)
                                label = $"  {profile.ShortName} · cpp (téléchargement {pct * 100:0}%)";
                            else if (!profile.IsReady)
                                label = $"  {profile.ShortName} · cpp (fichier absent)";

                            LLMModelCombo.Items.Add(new ComboBoxItem
                            {
                                Content   = label,
                                IsEnabled = profile.IsReady,
                                Tag       = new { Provider = "LlamaCpp", Model = profile.Id }
                            });
                        }
                    }

                    if (cloudModels.Any())
                    {
                        var ollamaCloudHeader = new ComboBoxItem
                        {
                            Content = "☁️ OLLAMA CLOUD — jamais de patient réel",
                            IsEnabled = false,
                            FontWeight = FontWeights.Bold,
                            Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0))
                        };
                        LLMModelCombo.Items.Add(ollamaCloudHeader);

                        foreach (var model in cloudModels)
                        {
                            // Le préfixe ☁️ est porté par l'item lui-même : une fois le ComboBox
                            // refermé, seul l'item sélectionné reste visible (pas son en-tête).
                            var item = new ComboBoxItem
                            {
                                Content = $"  ☁️ {model}",
                                Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0)),
                                Tag = new { Provider = "Ollama", Model = model }
                            };
                            LLMModelCombo.Items.Add(item);
                        }
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
            
            // Ajouter OpenAI (même logique : le marqueur ☁️ doit rester visible ComboBox fermé)
            var openAIItem = new ComboBoxItem
            {
                Content = $"  ☁️ {_settings.OpenAIModel}",
                Foreground = new SolidColorBrush(Color.FromRgb(52, 152, 219)),
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
    
    /// <summary>
    /// Ouvre le banc d'essai OCR (GLM-OCR local) : charger une image, comparer les trois modes
    /// d'extraction et lire la sortie brute du modèle.
    /// </summary>
    private void OcrTestButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Dialogs.OcrTestDialog(_settings) { Owner = this };
        dialog.ShowDialog();
    }

    /// <summary>
    /// Vrai si le modèle Ollama est un modèle "cloud" (suffixe -cloud) : aucun poids local,
    /// l'inférence est exécutée sur les serveurs Ollama. À ne jamais utiliser sur données patient.
    /// Règle portée par <see cref="Services.LLM.OllamaModelInfo"/> (source unique, hors UI).
    /// </summary>
    internal static bool IsOllamaCloudModel(string modelName) =>
        Services.LLM.OllamaModelInfo.IsCloudModel(modelName);

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
    /// <summary>
    /// Affiche le débit de la dernière génération dans l'en-tête. Appelé depuis le thread qui a
    /// exécuté la requête LLM (jamais l'UI) : d'où le passage par le Dispatcher.
    /// </summary>
    private void OnThroughputMeasured(Services.LLM.LlmThroughputMonitor.Sample sample)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            ThroughputText.Text     = sample.ShortLabel;
            ThroughputBadge.ToolTip = sample.Tooltip;
            ThroughputBadge.Visibility = Visibility.Visible;
        }));
    }

    private async void UnloadModelBtn_Click(object sender, RoutedEventArgs e)
    {
        var provider = _currentLLMService;
        if (provider == null) return;

        UnloadModelBtn.IsEnabled = false;
        var originalBrush       = UnloadModelBtn.BorderBrush;
        UnloadModelBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 193, 7));   // orange : opération en cours

        try
        {
            // 1. Vider le KV cache GPU
            var (unloadOk, unloadMsg) = await provider.UnloadAsync();
            if (!unloadOk)
            {
                UnloadModelBtn.BorderBrush    = new SolidColorBrush(Color.FromRgb(244, 67, 54));
                StatusTextBlock.Text          = $"❌ {unloadMsg}";
                StatusTextBlock.Foreground    = new SolidColorBrush(Colors.Red);
                return;
            }

            // 2. KV cache vidé → rechargement immédiat pour que Med reste disponible
            LLMStatusIndicator.Background = new SolidColorBrush(Color.FromRgb(255, 193, 7));   // orange : rechargement
            LLMStatusIndicator.ToolTip    = "KV cache effacé — rechargement de Med...";
            StatusTextBlock.Text          = "💤 Cache effacé. Rechargement de Med...";
            StatusTextBlock.Foreground    = new SolidColorBrush(Colors.DarkOrange);

            var (warmupOk, warmupMsg) = await provider.WarmupAsync();
            if (warmupOk)
            {
                UnloadModelBtn.BorderBrush    = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                LLMStatusIndicator.Background = new SolidColorBrush(Color.FromRgb(76, 175, 80));   // vert
                LLMStatusIndicator.ToolTip    = $"Med prêt ({warmupMsg})";
                StatusTextBlock.Text          = $"✅ KV cache vidé et Med rechargé.";
                StatusTextBlock.Foreground    = new SolidColorBrush(Colors.Green);
            }
            else
            {
                // Warmup échoué : Med est déchargé mais accessible au prochain appel (Ollama auto-reload)
                LLMStatusIndicator.Background = new SolidColorBrush(Color.FromRgb(149, 165, 166));   // gris
                LLMStatusIndicator.ToolTip    = "Med déchargé. Premier appel : ~5-10s de réveil.";
                StatusTextBlock.Text          = "💤 KV cache vidé. Premier appel : ~5-10s.";
                StatusTextBlock.Foreground    = new SolidColorBrush(Colors.Gray);
            }
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text          = $"❌ Erreur : {ex.Message}";
            StatusTextBlock.Foreground    = new SolidColorBrush(Colors.Red);
            UnloadModelBtn.BorderBrush    = new SolidColorBrush(Color.FromRgb(244, 67, 54));
        }
        finally
        {
            UnloadModelBtn.IsEnabled = true;
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
            var (success, message) = await whisper.ResetEngineAsync(full: true);
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
    /// Réinitialise COMPLÈTEMENT le moteur Whisper en arrière-plan (teardown factory + processor,
    /// rechargement du modèle depuis le disque), sans bloquer l'UI.
    /// Appelé au changement de patient pour éviter la dégradation progressive de la qualité de
    /// transcription : seul le reset complet défragmente la VRAM et réinitialise le contexte CUDA
    /// natif (le reset partiel ne vidait que le KV cache décodeur, d'où la dégradation à partir
    /// de ~4 sessions). Coût ~1-2 s, invisible car on n'enregistre pas pendant le chargement patient.
    /// </summary>
    private void ResetWhisperEngineSilently()
    {
        var whisper = _whisperStreamingService;
        if (whisper == null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var (ok, _) = await whisper.ResetEngineAsync(full: true);
                if (ok)
                {
                    _ = Dispatcher.InvokeAsync(() =>
                    {
                        if (WhisperResetButton != null)
                            WhisperResetButton.ToolTip = "Whisper réinitialisé (changement patient).";
                    });
                }
            }
            catch { /* silencieux : meilleur effort */ }
        });
    }

    /// <summary>
    /// Change le niveau de réflexion ("low" | "medium" | "high") du modèle Ollama actif.
    /// Visible uniquement pour les modèles qui exposent ce réglage (voir CurrentModelSupportsReasoningEffort).
    /// </summary>
    private void ReasoningEffortCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReasoningEffortCombo.SelectedItem is not ComboBoxItem selected || selected.Tag is not string level)
            return;

        _llmFactory.SetReasoningEffort(level);
        _settings.OllamaReasoningEffort = level;
        _settings.Save();

        // "off" n'est pas un niveau mais un mode du serveur : le prochain appel provoquera un
        // redémarrage de llama-server (~6-7 s mesurés). L'annoncer évite de croire à un blocage.
        StatusTextBlock.Text = level == Services.LLM.ReasoningLevels.Off
            ? "🚫 Réflexion désactivée — le modèle redémarrera au prochain appel (~6 s)."
            : $"🧠 Niveau de réflexion : {selected.Content}";
        StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);
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

                // Note : ConsultationModeControl et les suggesters (Projet Thérapeutique, Synthèse
                // Globale...) ont été injectés avec LiveLlmServiceProxy (voir MainWindow.xaml.cs) —
                // ils suivent donc automatiquement ce changement, aucune mise à jour à faire ici.

                // Niveau de réflexion : visible seulement pour les modèles qui le supportent
                // (gpt-oss, Qwen3.8-27B...). On réapplique le dernier niveau choisi (ou "high" par
                // défaut) pour que le réglage soit immédiatement actif sur le nouveau modèle.
                if (_llmFactory.CurrentModelSupportsReasoningEffort())
                {
                    // Le réglage persisté peut désigner un cran qui n'existe plus (ex. "minimal",
                    // retiré car rejeté par le template Qwen). On retombe alors sur un niveau valide
                    // plutôt que de laisser le sélecteur sans sélection visible.
                    var savedLevel = _settings.OllamaReasoningEffort;
                    bool known = ReasoningEffortCombo.Items.OfType<ComboBoxItem>()
                                                           .Any(ci => (ci.Tag as string) == savedLevel);
                    if (!known)
                        savedLevel = Services.LLM.ReasoningLevels.Medium;

                    _llmFactory.SetReasoningEffort(savedLevel);
                    ReasoningEffortCombo.Visibility = Visibility.Visible;
                    foreach (var item in ReasoningEffortCombo.Items)
                    {
                        if (item is ComboBoxItem ci && (ci.Tag as string) == savedLevel)
                        {
                            ReasoningEffortCombo.SelectedItem = ci;
                            break;
                        }
                    }
                }
                else
                {
                    ReasoningEffortCombo.Visibility = Visibility.Collapsed;
                }

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
