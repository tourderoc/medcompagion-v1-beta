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
            // Le voyant suit l'état publié par le moteur lui-même (voir AfficherEtatMoteur).
            LlamaCppServerManager.EtatChange += OnEtatMoteurChange;
            AfficherEtatMoteur();

            // Garde l'autre modèle prêt dans le cache Windows (étape 7 du plan moteur).
            LlamaCppPrelecture.Demarrer();

            // Badge de débit : alimenté par les providers locaux après chaque génération.
            Services.LLM.LlmThroughputMonitor.Measured += OnThroughputMeasured;

            // Lancé avant de remplir le sélecteur, qui interroge Ollama et peut prendre quelques secondes :
            // le modèle commence à charger sans l'attendre.
            _ = ChargerModeleOuvertureAsync();

            await PopulateLLMComboBoxAsync();
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

    /// <summary>
    /// Charge le modèle d'ouverture — UN seul chemin de démarrage. Il y en avait deux qui couraient en
    /// parallèle : le service de warm-up, et la sélection initiale du sélecteur qui déclenchait une
    /// bascule complète comme un clic. Le warm-up n'apportait rien sur llama.cpp (il rappelait la
    /// fonction de démarrage déjà exécutée), et les deux écrivaient la couleur du voyant : le dernier
    /// qui finissait gagnait. Supprimé le 14/09/2026 (PLAN_MOTEUR_LLM_LOCAL.md, étape 6).
    /// </summary>
    private async Task ChargerModeleOuvertureAsync()
    {
        var fournisseur = _settings.LLMProvider;
        var modele      = fournisseur == "OpenAI" ? null : _settings.OllamaModel;

        var (ok, message) = await _llmFactory.SwitchProviderAsync(fournisseur, modele);

        await Dispatcher.InvokeAsync(() =>
        {
            _currentLLMService = _llmFactory.GetCurrentProvider();
            if (fournisseur != "LlamaCpp")
                _etatFournisseurExterne = (ok, message);

            var nom = fournisseur == "LlamaCpp"
                ? LlamaCppServerManager.RunningProfile?.ShortName ?? modele
                : _llmFactory.GetActiveModelName();

            StatusTextBlock.Text       = ok ? $"✅ {nom} prêt" : $"❌ {message}";
            StatusTextBlock.Foreground = new SolidColorBrush(ok ? Colors.Green : Colors.Red);
            AfficherEtatMoteur();
        });
    }

    // ── Voyant ───────────────────────────────────────────────────────────────

    private static readonly Color CouleurArrete     = Color.FromRgb(149, 165, 166);
    private static readonly Color CouleurChargement = Color.FromRgb(255, 193, 7);
    private static readonly Color CouleurPret       = Color.FromRgb(76, 175, 80);
    private static readonly Color CouleurErreur     = Color.FromRgb(244, 67, 54);

    /// <summary>
    /// Résultat de la dernière bascule pour un fournisseur dont l'état n'est pas suivi en direct
    /// (Ollama, OpenAI). llama.cpp, lui, publie son état : ce champ ne le concerne pas.
    /// </summary>
    private (bool ok, string message)? _etatFournisseurExterne;

    private bool _voyantPulse;

    private void OnEtatMoteurChange() => Dispatcher.BeginInvoke(new Action(AfficherEtatMoteur));

    /// <summary>
    /// Seul endroit qui décide de l'aspect du voyant. Pour llama.cpp il traduit l'état publié par
    /// <see cref="LlamaCppServerManager"/> — veille, bascule vision, arrêt depuis Pilotage, serveur mort
    /// compris — et nomme le modèle RÉELLEMENT chargé, pas celui du sélecteur.
    /// </summary>
    private void AfficherEtatMoteur()
    {
        Color  couleur;
        string infobulle;
        var    pulse  = false;
        var    vision = false;

        if (_llmFactory.GetActiveProviderName() is not ("LlamaCpp" or "Aucun"))
        {
            var etat = _etatFournisseurExterne;
            couleur   = etat is null ? CouleurChargement : etat.Value.ok ? CouleurPret : CouleurErreur;
            infobulle = etat?.message ?? "Initialisation du modèle…";
        }
        else
        {
            var profil = LlamaCppServerManager.RunningProfile;
            vision     = LlamaCppServerManager.ModeVisionEnCours;
            var nom    = profil == null ? "" : profil.ShortName + (vision ? " — mode vision (lecture d'image)" : "");

            // Le sélecteur ne bouge PAS pendant une lecture d'image : il dit quel modèle sert le texte, et
            // ce choix n'a pas changé. C'est le voyant qui montre la substitution, et le retour prévu.
            var retourTexte = vision
                ? $"\n{LlamaCppServerManager.CurrentProfile.ShortName} reprendra au prochain appel texte."
                : "";

            switch (LlamaCppServerManager.Etat)
            {
                case EtatMoteurLlm.Chargement:
                    couleur   = CouleurChargement;
                    infobulle = LlamaCppServerManager.EtatMessage + retourTexte;
                    break;

                case EtatMoteurLlm.Pret:
                    couleur   = CouleurPret;
                    infobulle = $"{nom}\nChargé et prêt · contexte {profil?.ContextSize:N0} tokens{retourTexte}";
                    break;

                case EtatMoteurLlm.Genere:
                    couleur   = CouleurPret;
                    pulse     = true;
                    infobulle = $"{nom}\nGénération en cours{retourTexte}";
                    break;

                case EtatMoteurLlm.Erreur:
                    couleur   = CouleurErreur;
                    infobulle = LlamaCppServerManager.EtatMessage;
                    break;

                default:
                    couleur   = CouleurArrete;
                    infobulle = $"{LlamaCppServerManager.EtatMessage}\n" +
                                $"Le prochain appel chargera {LlamaCppServerManager.CurrentProfile.ShortName} " +
                                "(quelques secondes si le fichier est en cache, jusqu'à ~70 s sinon).";
                    break;
            }
        }

        LLMStatusIndicator.Background = new SolidColorBrush(couleur);
        LLMStatusIndicator.ToolTip    = infobulle;
        LLMStatusIcon.Text            = vision ? "👁" : "🤖";

        if (pulse != _voyantPulse)
        {
            _voyantPulse = pulse;
            if (pulse)
            {
                LLMStatusIndicator.BeginAnimation(UIElement.OpacityProperty,
                    new System.Windows.Media.Animation.DoubleAnimation(1.0, 0.45, TimeSpan.FromMilliseconds(650))
                    {
                        AutoReverse    = true,
                        RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever
                    });
            }
            else
            {
                LLMStatusIndicator.BeginAnimation(UIElement.OpacityProperty, null);
                LLMStatusIndicator.Opacity = 1.0;
            }
        }
    }
    
    private async Task PopulateLLMComboBoxAsync()
    {
        try
        {
            LLMModelCombo.Items.Clear();

            // Le moteur local passe EN TÊTE et ne dépend plus d'Ollama. Il était construit à
            // l'intérieur du bloc « Ollama répond » : Ollama fermé, le sélecteur n'offrait plus
            // qu'OpenAI, et restait vide puisque le modèle actif — llama.cpp — n'y figurait pas.
            // Or llama.cpp tourne sans Ollama, et c'est lui qui doit rester quand Ollama sera retiré.
            bool entreesAuDessus = false;
            if (LlamaCppProfiles.Enabled)
            {
                LLMModelCombo.Items.Add(new ComboBoxItem
                {
                    Content = "⚙️ MOTEUR LOCAL (llama.cpp)",
                    IsEnabled = false,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(230, 126, 34))
                });

                // Un profil = une entrée, avec ses propres réglages (contexte, MTP, cache KV)
                // pilotables dans Pilotage → Moteur local.
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
                entreesAuDessus = true;

                // Sélection immédiate : interroger un Ollama fermé prend quelques secondes, et le
                // sélecteur resterait vide pendant ce temps alors que le modèle actif est déjà listé.
                SelectCurrentModel();
            }

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

                        // Les modèles Ollama restent servis par Ollama, listés à part des profils
                        // llama.cpp : un même modèle peut ainsi être essayé via l'un ou l'autre
                        // moteur et comparé.
                        foreach (var model in localModels)
                        {
                            var item = new ComboBoxItem
                            {
                                Content = $"  {model}",
                                Tag = new { Provider = "Ollama", Model = model }
                            };
                            LLMModelCombo.Items.Add(item);
                        }
                        entreesAuDessus = true;
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
                        entreesAuDessus = true;
                    }
                }
            }

            // Séparateur avant OpenAI — seulement s'il y a quelque chose au-dessus.
            if (entreesAuDessus)
            {
                LLMModelCombo.Items.Add(new ComboBoxItem
                {
                    Content = "─────────────",
                    IsEnabled = false
                });
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

    /// <summary>
    /// Vrai pendant qu'on aligne le sélecteur sur une bascule déjà faite. Sans ce garde, poser
    /// SelectedItem redéclencherait LLMModelCombo_SelectionChanged, qui relancerait une bascule vers
    /// le modèle déjà chargé — soit un redémarrage inutile du serveur de 6 à 10 secondes, en boucle.
    /// </summary>
    private bool _syncSelecteurModele;

    /// <summary>
    /// Aligne l'en-tête sur le modèle réellement actif. Branché sur
    /// <see cref="Services.LLM.LLMServiceFactory.ActiveModelChanged"/> : couvre donc aussi bien les
    /// bascules automatiques des étapes de consultation que n'importe quel autre changement fait
    /// par le code.
    /// </summary>
    private void OnActiveModelChanged(object? sender, (string provider, string model) info)
    {
        // L'événement peut venir d'un thread de travail (bascule d'étape lancée en tâche de fond).
        Dispatcher.InvokeAsync(() =>
        {
            try
            {
                _syncSelecteurModele = true;

                foreach (var item in LLMModelCombo.Items)
                {
                    if (item is not ComboBoxItem ci || ci.Tag == null) continue;

                    var tag = ci.Tag as dynamic;
                    var provider = tag.Provider as string;
                    var model    = tag.Model as string;

                    var correspond = provider == info.provider &&
                                     (provider == "OpenAI" || model == info.model);
                    if (!correspond) continue;

                    if (!ReferenceEquals(LLMModelCombo.SelectedItem, ci))
                        LLMModelCombo.SelectedItem = ci;
                    break;
                }

                // Le niveau de réflexion n'a de sens que sur les modèles dont le template l'accepte :
                // le laisser visible après une bascule vers Gemma proposerait un réglage sans effet.
                if (!_llmFactory.CurrentModelSupportsReasoningEffort())
                {
                    ReasoningEffortCombo.Visibility = Visibility.Collapsed;
                }
                else
                {
                    // Le rendre visible NE SUFFIT PAS : sans sélection il s'affiche comme une case
                    // blanche vide à côté du sélecteur de modèle. On réapplique le niveau persisté,
                    // avec repli si ce cran n'existe plus dans la liste.
                    var niveau = _settings.OllamaReasoningEffort;
                    var connu = ReasoningEffortCombo.Items.OfType<ComboBoxItem>()
                                                          .Any(ci => (ci.Tag as string) == niveau);
                    if (!connu) niveau = Services.LLM.ReasoningLevels.Medium;

                    _llmFactory.SetReasoningEffort(niveau);

                    foreach (var it in ReasoningEffortCombo.Items)
                        if (it is ComboBoxItem ci && (ci.Tag as string) == niveau)
                        { ReasoningEffortCombo.SelectedItem = ci; break; }

                    ReasoningEffortCombo.Visibility = Visibility.Visible;
                }
            }
            catch { /* l'affichage ne doit jamais faire échouer une bascule */ }
            finally { _syncSelecteurModele = false; }
        });
    }

    /// <summary>
    /// Aligne le sélecteur sur le modèle mémorisé, SANS déclencher de bascule. Sans le garde, poser
    /// SelectedItem relançait LLMModelCombo_SelectionChanged comme un clic : au démarrage c'était un
    /// second chargement du modèle en course avec le premier, et après un échec de bascule une
    /// nouvelle tentative au lieu d'un simple retour à l'affichage précédent.
    /// </summary>
    private void SelectCurrentModel()
    {
        _syncSelecteurModele = true;
        try
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
        finally { _syncSelecteurModele = false; }
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

            // 2. KV cache vidé → rechargement immédiat pour que Med reste disponible.
            //    Le voyant suit tout seul (état publié par le moteur) ; seul le texte de statut est posé ici.
            StatusTextBlock.Text          = "💤 Cache effacé. Rechargement de Med...";
            StatusTextBlock.Foreground    = new SolidColorBrush(Colors.DarkOrange);

            var (reloadOk, _) = await provider.WarmupAsync();
            if (reloadOk)
            {
                UnloadModelBtn.BorderBrush    = new SolidColorBrush(Color.FromRgb(76, 175, 80));
                StatusTextBlock.Text          = $"✅ KV cache vidé et Med rechargé.";
                StatusTextBlock.Foreground    = new SolidColorBrush(Colors.Green);
            }
            else
            {
                // Rechargement échoué : Med est déchargé mais accessible au prochain appel.
                StatusTextBlock.Text          = "💤 KV cache vidé. Rechargement au prochain appel.";
                StatusTextBlock.Foreground    = new SolidColorBrush(Colors.Gray);
            }
            AfficherEtatMoteur();
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

        // Sélection posée par le code pour refléter une bascule DÉJÀ faite : il n'y a rien à
        // basculer, et le faire relancerait le serveur pour le modèle qui vient d'être chargé.
        if (_syncSelecteurModele)
            return;

        try
        {
            var tag = selectedItem.Tag as dynamic;
            var provider = tag.Provider as string;
            var model = tag.Model as string;
            
            // Le voyant n'est plus posé ici : llama.cpp publie lui-même « chargement » puis « prêt ».
            StatusTextBlock.Text = $"⏳ Changement vers {provider} ({model})...";
            StatusTextBlock.Foreground = new SolidColorBrush(Colors.Blue);

            var (success, message) = await _llmFactory.SwitchProviderAsync(provider!, model);

            if (provider != "LlamaCpp")
                _etatFournisseurExterne = (success, message);
            AfficherEtatMoteur();

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

                StatusTextBlock.Text = message;
                StatusTextBlock.Foreground = new SolidColorBrush(Colors.Green);
            }
            else
            {
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

            if (_llmFactory.GetActiveProviderName() != "LlamaCpp")
                _etatFournisseurExterne = (false, $"Erreur : {ex.Message}");
            AfficherEtatMoteur();
        }
    }
}
