using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MedCompanion.Services.LLM;

namespace MedCompanion.Views.Pilotage
{
    /// <summary>
    /// Pilotage du moteur local llama.cpp : état du serveur, mesures en direct, et réglages par
    /// profil de modèle.
    ///
    /// Principe de conception : aucun réglage n'est présenté sans la mesure correspondante. Un
    /// paramètre modifié à l'aveugle a déjà coûté 30 % de débit sans que rien ne le signale — le
    /// bandeau supérieur (VRAM, débit) est donc indissociable des cartes de réglage.
    /// </summary>
    public partial class LlamaServerControl : UserControl
    {
        private readonly DispatcherTimer _refreshTimer;
        private bool _suspendEvents;

        /// <summary>
        /// Le bandeau sert à deux choses : l'alerte de débordement (recalculée à chaque
        /// rafraîchissement) et les messages ponctuels suite à une action. Sans cette échéance, le
        /// rafraîchissement périodique effacerait un message avant que l'utilisateur l'ait lu.
        /// </summary>
        private DateTime _noticeUntil = DateTime.MinValue;

        private void ShowNotice(string message, int seconds = 12)
        {
            OverflowWarningText.Text   = message;
            OverflowWarning.Visibility = Visibility.Visible;
            _noticeUntil = DateTime.Now.AddSeconds(seconds);
        }

        public LlamaServerControl()
        {
            InitializeComponent();

            // Refléter l'état persisté sans déclencher le gestionnaire (qui arrêterait le serveur).
            _suspendEvents = true;
            EngineEnabledCheck.IsChecked = LlamaCppProfiles.Enabled;
            _suspendEvents = false;

            BuildProfileCards();

            // Rafraîchissement seulement quand l'onglet est visible : lire les compteurs GPU et
            // interroger nvidia-smi toutes les 3 s en permanence serait du gaspillage.
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
            _refreshTimer.Tick += (_, _) => RefreshState();

            IsVisibleChanged += (_, e) =>
            {
                if ((bool)e.NewValue) { RefreshState(); _refreshTimer.Start(); }
                else                   _refreshTimer.Stop();
            };

            LlmThroughputMonitor.Measured += OnThroughputMeasured;
            Unloaded += (_, _) =>
            {
                _refreshTimer.Stop();
                LlmThroughputMonitor.Measured -= OnThroughputMeasured;
            };
        }

        // ── État et mesures ───────────────────────────────────────────────────

        private void RefreshState()
        {
            // Un message ponctuel encore affiché a priorité sur l'alerte de débordement.
            bool noticeActive = DateTime.Now < _noticeUntil;

            var running = LlamaCppServerManager.IsRunning;
            var loaded  = LlamaCppServerManager.RunningProfile;

            StateDot.Fill  = new SolidColorBrush(running
                ? Color.FromRgb(0x2E, 0xCC, 0x71)
                : Color.FromRgb(0x88, 0x88, 0x88));
            StateText.Text = running ? "Serveur actif" : "Serveur arrêté";

            // Distinguer le modèle CHARGÉ du modèle SÉLECTIONNÉ : entre les deux, il y a un
            // redémarrage qui n'a pas encore eu lieu (il se produit au prochain appel).
            var selected = LlamaCppServerManager.CurrentProfile;
            LoadedModelText.Text = running
                ? (loaded != null && loaded != selected
                    ? $"{loaded.ShortName} chargé — {selected.ShortName} prendra effet au prochain appel"
                    : $"{loaded?.ShortName ?? selected.ShortName} · contexte {FormatContext(loaded?.ContextSize ?? selected.ContextSize)}")
                : $"{selected.ShortName} sera chargé au prochain appel";

            StopBtn.IsEnabled    = running;
            RestartBtn.IsEnabled = running;

            bool hasAdapter = GpuMemoryProbe.TryReadAdapterMemory(out _, out var freeBytes);
            VramFreeText.Text = hasAdapter ? $"{freeBytes / 1024 / 1024:N0} Mo" : "—";

            var pid = LlamaCppServerManager.RunningProcessId;
            if (pid is null)
            {
                VramDedicatedText.Text = "—";
                VramSharedText.Text    = "—";
                if (!noticeActive) OverflowWarning.Visibility = Visibility.Collapsed;
                return;
            }

            var reading = GpuMemoryProbe.Read(pid.Value);
            if (!reading.Available)
            {
                VramDedicatedText.Text = "indisponible";
                VramSharedText.Text    = "indisponible";
                if (!noticeActive) OverflowWarning.Visibility = Visibility.Collapsed;
                return;
            }

            VramDedicatedText.Text = $"{reading.DedicatedMb:N0} Mo";
            VramSharedText.Text    = $"{reading.SharedMb:N0} Mo";

            // Débordement = beaucoup de partagé ET plus de VRAM libre. Le partagé seul ne prouve
            // rien : mesuré 998 Mo sur Gemma avec 6,4 Go encore libres, sans aucune pression.
            bool overflow = hasAdapter && GpuMemoryProbe.LooksLikeOverflow(reading, freeBytes);
            if (!overflow)
            {
                if (!noticeActive) OverflowWarning.Visibility = Visibility.Collapsed;
            }
            else
            {
                OverflowWarning.Visibility = Visibility.Visible;
                OverflowWarningText.Text =
                    $"Débordement probable : {reading.SharedMb:N0} Mo en mémoire système alors qu'il ne reste " +
                    $"que {freeBytes / 1024 / 1024:N0} Mo de VRAM libre. La génération va ralentir. " +
                    $"Réduisez le contexte de {LlamaCppServerManager.CurrentProfile.ShortName} ou fermez " +
                    $"les applications qui utilisent la carte.";
            }
        }

        private void OnThroughputMeasured(LlmThroughputMonitor.Sample sample)
        {
            Dispatcher.BeginInvoke(new Action(() =>
                ThroughputText.Text = $"{sample.ShortLabel}  ({sample.Tokens} tokens)"));
        }

        private static string FormatContext(int tokens) =>
            tokens >= 1024 ? $"{tokens / 1024}k" : tokens.ToString();

        // ── Cartes de réglage par profil ──────────────────────────────────────

        private void BuildProfileCards()
        {
            ProfilesPanel.Children.Clear();
            foreach (var profile in LlamaCppProfiles.All)
                ProfilesPanel.Children.Add(BuildCard(profile));
        }

        private Border BuildCard(LlamaCppModelProfile profile)
        {
            var stack = new StackPanel();

            // En-tête : nom + rôle
            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            header.Children.Add(new TextBlock
            {
                Text = profile.ShortName,
                Foreground = Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            header.Children.Add(new TextBlock
            {
                Text = profile.SupportsReasoning ? "  ·  raisonnement" : "  ·  volume et long contexte",
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });
            stack.Children.Add(header);

            stack.Children.Add(new TextBlock
            {
                Text = profile.ModelPath,
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                FontSize = 10,
                Margin = new Thickness(0, 0, 0, 14)
            });

            // Contexte
            var ctxRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
            ctxRow.Children.Add(Label("Contexte", 110));
            var ctxBox = new TextBox
            {
                Text = profile.ContextSize.ToString(),
                Width = 90,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x3D)),
                Padding = new Thickness(6, 4, 6, 4),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            ctxRow.Children.Add(ctxBox);
            ctxRow.Children.Add(new TextBlock
            {
                Text = $"  tokens (max {profile.MaxContextSize})",
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });
            stack.Children.Add(ctxRow);

            // Cache KV quantifié
            var kvCheck = Check("Cache KV compressé (q8_0)", profile.KvQuantized,
                "Divise par deux l'empreinte du cache. C'est ce réglage, absent d'Ollama, qui rend les contextes longs possibles.");
            stack.Children.Add(kvCheck);

            // MTP — grisé quand ni le modèle ni un brouillon séparé ne le portent
            var mtpCheck = Check("Prédiction multi-tokens (MTP)", profile.MtpEnabled,
                profile.MtpAvailable
                    ? (profile.HasMtpTensors
                        ? "Accélère la génération en proposant plusieurs tokens par passe de vérification. Tenseurs intégrés au modèle."
                        : "Accélère la génération. Brouillon fourni dans un fichier séparé : " + profile.DraftModelPath)
                    : "Indisponible : ni tenseurs MTP dans le GGUF, ni brouillon séparé (vérifié par inspection du fichier).");
            mtpCheck.IsEnabled = profile.MtpAvailable;
            stack.Children.Add(mtpCheck);

            // Tokens proposés par passe — le réglage le plus sensible du lot, d'où la mesure en
            // infobulle plutôt qu'un simple champ nu.
            var draftRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            draftRow.Children.Add(Label("Tokens brouillon", 110));
            var draftBox = new TextBox
            {
                Text = profile.DraftTokens.ToString(),
                Width = 60,
                IsEnabled = profile.MtpAvailable,
                Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E)),
                Foreground = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x3D)),
                Padding = new Thickness(6, 4, 6, 4),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "Nombre de tokens proposés par passe de vérification. Mesuré sur Qwen : "
                        + "3 → acceptation 0,84 et 48 t/s ; 5 → acceptation 0,37 et 34 t/s. "
                        + "Trop haut, les tokens refusés coûtent plus qu'ils ne rapportent. À changer en mesurant."
            };
            draftRow.Children.Add(draftBox);
            stack.Children.Add(draftRow);

            // Actions
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            var applyBtn = new Button { Content = "Appliquer", Style = (Style)FindResource("BtnAccent") };
            var resetBtn = new Button { Content = "Valeurs par défaut", Style = (Style)FindResource("Btn") };
            var status   = new TextBlock
            {
                Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(6, 0, 0, 0)
            };

            applyBtn.Click += (_, _) =>
            {
                if (_suspendEvents) return;
                if (!int.TryParse(ctxBox.Text.Trim(), out var ctx))
                {
                    status.Foreground = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
                    status.Text = "Contexte invalide.";
                    return;
                }

                profile.ContextSize = ctx;                       // borné par le profil
                profile.KvQuantized = kvCheck.IsChecked == true;
                profile.MtpEnabled  = mtpCheck.IsChecked == true;
                if (int.TryParse(draftBox.Text.Trim(), out var draft)) profile.DraftTokens = draft;
                LlamaCppProfiles.SaveSettings();

                ctxBox.Text   = profile.ContextSize.ToString();   // refléter le bornage éventuel
                draftBox.Text = profile.DraftTokens.ToString();
                status.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));

                // Les arguments sont fixés au démarrage : un réglage ne prend effet qu'au
                // rechargement. Le dire explicitement évite de croire à un réglage sans effet.
                status.Text = LlamaCppServerManager.IsRunning
                    ? "Enregistré — actif après redémarrage du serveur."
                    : "Enregistré.";
            };

            resetBtn.Click += (_, _) =>
            {
                profile.ResetToDefaults();
                LlamaCppProfiles.SaveSettings();
                _suspendEvents = true;
                ctxBox.Text        = profile.ContextSize.ToString();
                draftBox.Text      = profile.DraftTokens.ToString();
                kvCheck.IsChecked  = profile.KvQuantized;
                mtpCheck.IsChecked = profile.MtpEnabled;
                _suspendEvents = false;
                status.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0xCC, 0x71));
                status.Text = "Réglages par défaut restaurés.";
            };

            actions.Children.Add(applyBtn);
            actions.Children.Add(resetBtn);
            actions.Children.Add(status);
            stack.Children.Add(actions);

            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x3D)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(18, 16, 18, 16),
                Margin = new Thickness(0, 0, 0, 14),
                Child = stack
            };
        }

        private static TextBlock Label(string text, double width) => new()
        {
            Text = text,
            Width = width,
            Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };

        private static CheckBox Check(string text, bool isChecked, string tooltip) => new()
        {
            Content = text,
            IsChecked = isChecked,
            ToolTip = tooltip,
            Foreground = Brushes.White,
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 8)
        };

        // ── Boutons du bandeau ────────────────────────────────────────────────

        private void RefreshBtn_Click(object sender, RoutedEventArgs e) => RefreshState();

        private void EngineEnabledCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (_suspendEvents) return;

            bool enabled = EngineEnabledCheck.IsChecked == true;
            LlamaCppProfiles.SetEnabled(enabled);   // arrête le serveur si on désactive

            // Le routage a déjà été calculé pour le modèle actif : il faut le re-sélectionner pour
            // que la bascule prenne effet. Le dire, sinon l'utilisateur croit le réglage sans effet.
            ShowNotice(enabled
                ? "Moteur local activé. Re-sélectionnez le modèle dans l'en-tête pour qu'il repasse par llama.cpp."
                : "Moteur local désactivé — tout repasse par Ollama. Re-sélectionnez le modèle dans l'en-tête pour appliquer.");

            RefreshState();
        }

        private void StopBtn_Click(object sender, RoutedEventArgs e)
        {
            LlamaCppServerManager.Stop();
            RefreshState();
        }

        private async void RestartBtn_Click(object sender, RoutedEventArgs e)
        {
            RestartBtn.IsEnabled = false;
            try
            {
                LlamaCppServerManager.Stop();
                var (ok, msg) = await LlamaCppServerManager.EnsureRunningAsync();
                if (!ok)
                    ShowNotice(msg);
            }
            finally
            {
                RestartBtn.IsEnabled = true;
                RefreshState();
            }
        }
    }
}
