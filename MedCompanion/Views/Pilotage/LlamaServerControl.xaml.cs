using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MedCompanion.Models;
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

        // ═══ Affectation d'un modèle par étape ═══════════════════════════════

        private EtapeModeleService? _etapes;
        private LLMServiceFactory? _factory;

        /// <summary>Modèles proposés au choix : profils llama.cpp, plus les modèles Ollama trouvés.</summary>
        private readonly System.Collections.Generic.List<(string provider, string model, string libelle)> _modelesDispo = new();

        /// <summary>
        /// Branche l'affectation par étape. Appelé depuis MainWindow via PilotageControl : ce
        /// contrôle est déclaré en XAML et n'a donc pas de constructeur paramétrable.
        /// </summary>
        public async void InitEtapes(EtapeModeleService etapes, LLMServiceFactory factory)
        {
            _etapes  = etapes;
            _factory = factory;

            _modelesDispo.Clear();
            foreach (var p in LlamaCppProfiles.All.Where(p => p.IsReady))
                _modelesDispo.Add(("LlamaCpp", p.Id, p.ShortName + "  ·  cpp"));

            BuildProfileCards();

            // Les modèles Ollama demandent un appel réseau : le panneau s'affiche d'abord avec les
            // profils locaux, la liste s'enrichit ensuite. Bloquer l'affichage pour ça donnerait un
            // onglet figé quand Ollama n'est pas lancé.
            try
            {
                var ollama = await factory.GetAvailableOllamaModelsAsync();
                foreach (var m in ollama.Where(m => LlamaCppProfiles.Resolve(m) == null))
                    _modelesDispo.Add(("Ollama", m, m + "  ·  ollama"));

                if (ollama.Count > 0) BuildProfileCards();
            }
            catch { /* Ollama absent : on reste sur les profils locaux */ }
        }

        private void BuildProfileCards()
        {
            ProfilesPanel.Children.Clear();

            if (_etapes != null)
                ProfilesPanel.Children.Add(BuildSchemaEtapes());

            foreach (var profile in LlamaCppProfiles.All)
                ProfilesPanel.Children.Add(BuildCard(profile));
        }

        /// <summary>
        /// Le schéma du parcours, une colonne par étape.
        ///
        /// Sa raison d'être n'est pas seulement de configurer : c'est de RENDRE VISIBLE LE COÛT.
        /// Chaque changement de modèle arrête et relance llama-server (6 à 10 s), parce que Qwen et
        /// Gemma ne tiennent pas ensemble en VRAM. Un réglage étape par étape sans vue d'ensemble
        /// conduit à alterner sans s'en rendre compte et à payer l'attente en pleine consultation.
        /// D'où la pastille de couleur par modèle et le compteur de bascules en tête de phase.
        /// </summary>
        private Border BuildSchemaEtapes()
        {
            var stack = new StackPanel();

            stack.Children.Add(new TextBlock
            {
                Text = "Affectation par étape",
                Foreground = Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold
            });
            stack.Children.Add(new TextBlock
            {
                Text = "Le modèle bascule tout seul avant chaque étape. Chaque changement relance le serveur (~8 s) : "
                     + "regrouper les étapes sur un même modèle évite l'attente.",
                Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 700,
                Margin = new Thickness(0, 2, 0, 12)
            });

            var actif = new CheckBox
            {
                Content = "Bascule automatique",
                IsChecked = _etapes!.Actif,
                Foreground = Brushes.White,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 14),
                ToolTip = "Décoché : tout se fait sur le modèle choisi dans l'en-tête, aucune bascule."
            };
            actif.Checked   += (_, _) => { _etapes.DefinirActif(true);  BuildProfileCards(); };
            actif.Unchecked += (_, _) => { _etapes.DefinirActif(false); BuildProfileCards(); };
            stack.Children.Add(actif);

            // Ordre du parcours réel : 1er entretien → cartographie (2e séance) →
            // environnement & évaluation ciblée (3e séance) → suivi.
            foreach (var phase in new[]
                     {
                         EtapesConsultation.PhasePremiere,
                         EtapesConsultation.PhaseCartographie,
                         EtapesConsultation.PhaseEnvironnement,
                         EtapesConsultation.PhaseSuivi
                     })
                stack.Children.Add(BuildPhase(phase));

            return new Border
            {
                Background   = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x2D)),
                BorderBrush  = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x3D)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding      = new Thickness(18, 16, 18, 16),
                Margin       = new Thickness(0, 0, 0, 16),
                Child        = stack
            };
        }

        private StackPanel BuildPhase(string phase)
        {
            var bloc = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };

            var bascules = _etapes!.CompterBascules(phase);
            var enTete = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            enTete.Children.Add(new TextBlock
            {
                Text = phase.ToUpperInvariant(),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            enTete.Children.Add(new TextBlock
            {
                Text = bascules == 0
                    ? "   ·   aucune bascule"
                    : $"   ·   {bascules} bascule(s) · ~{_etapes.SecondesEstimees(phase)} s d'attente",
                Foreground = bascules > 1
                    ? new SolidColorBrush(Color.FromRgb(0xF5, 0xB0, 0x41))   // au-delà d'une, ça se voit
                    : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center
            });
            bloc.Children.Add(enTete);

            var ligne = new WrapPanel();
            foreach (var etape in EtapesConsultation.Toutes.Where(e => e.Phase == phase))
                ligne.Children.Add(BuildCarteEtape(etape));
            bloc.Children.Add(ligne);

            return bloc;
        }

        /// <summary>Palette stable par modèle : la même couleur doit désigner le même modèle partout.</summary>
        private static Brush CouleurModele(string? model) => model switch
        {
            null      => new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55)),
            "Qwen3.8-27B" => new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22)),
            _ when model.StartsWith("gemma", StringComparison.OrdinalIgnoreCase)
                      => new SolidColorBrush(Color.FromRgb(0x35, 0x98, 0xDB)),
            _         => new SolidColorBrush(Color.FromRgb(0x8E, 0x44, 0xAD))
        };

        private Border BuildCarteEtape(EtapeConsultation etape)
        {
            var affectation = _etapes!.Affectation(etape.Id);
            var stack = new StackPanel { Width = 210 };

            var titre = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            titre.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 8, Height = 8,
                Fill = CouleurModele(etape.EnArrierePlan ? null : affectation?.Model),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            titre.Children.Add(new TextBlock
            {
                Text = etape.Libelle,
                Foreground = Brushes.White,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 190,
                VerticalAlignment = VerticalAlignment.Center
            });
            stack.Children.Add(titre);

            if (etape.EnArrierePlan)
            {
                // Pas de sélecteur : une étape lancée sans être attendue ne doit jamais décider
                // seule d'un redémarrage du serveur pendant que le médecin travaille ailleurs.
                stack.Children.Add(new TextBlock
                {
                    Text = "hérite du modèle courant",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x77, 0x77, 0x77)),
                    FontSize = 10,
                    FontStyle = FontStyles.Italic
                });
            }
            else
            {
                // Style sombre complet (voir DarkCombo dans le XAML) : sans lui, le gabarit natif
                // affiche la liste sur fond clair, donc du texte blanc illisible.
                var combo = new ComboBox
                {
                    Style = (Style)FindResource("DarkCombo"),
                    Height = 26
                };

                // Style posé sur chaque item : ItemContainerStyle ne s'applique pas de façon fiable
                // à des ComboBoxItem construits à la main — ils sont déjà leur propre conteneur.
                var styleItem = (Style)FindResource("DarkComboItem");

                combo.Items.Add(new ComboBoxItem { Content = "— modèle courant", Tag = null, Style = styleItem });
                foreach (var (provider, model, libelle) in _modelesDispo)
                    combo.Items.Add(new ComboBoxItem { Content = libelle, Tag = (provider, model), Style = styleItem });

                combo.SelectedIndex = 0;
                if (affectation != null)
                {
                    for (int i = 1; i < combo.Items.Count; i++)
                        if (combo.Items[i] is ComboBoxItem ci && ci.Tag is ValueTuple<string, string> t
                            && t.Item1 == affectation.Provider && t.Item2 == affectation.Model)
                        { combo.SelectedIndex = i; break; }
                }

                combo.SelectionChanged += (_, _) =>
                {
                    if (combo.SelectedItem is not ComboBoxItem ci) return;
                    if (ci.Tag is ValueTuple<string, string> t) _etapes.Definir(etape.Id, t.Item1, t.Item2);
                    else                                        _etapes.Effacer(etape.Id);

                    // Reconstruire : les pastilles et le compteur de bascules changent avec ce choix.
                    Dispatcher.BeginInvoke(new Action(BuildProfileCards));
                };

                stack.Children.Add(combo);
            }

            stack.Children.Add(new TextBlock
            {
                Text = etape.Description,
                Foreground = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                FontSize = 10,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0)
            });

            return new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x25)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x3D, 0x3D, 0x3D)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 10, 10),
                Child = stack
            };
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
