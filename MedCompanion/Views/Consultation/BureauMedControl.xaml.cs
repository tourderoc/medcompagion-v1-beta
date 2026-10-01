using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using MedCompanion.Services;
using MedCompanion.ViewModels;

namespace MedCompanion.Views.Consultation
{
    public partial class BureauMedControl : UserControl
    {
        private BureauMedViewModel? _viewModel;
        private IntPtr _embeddedWindowHandle = IntPtr.Zero;
        private IntPtr _originalParent = IntPtr.Zero;
        private int _originalStyle = 0;
        private DispatcherTimer? _positionGuardTimer;

        // Win32 API
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool MoveWindow(IntPtr hWnd, int X, int Y, int nWidth, int nHeight, bool bRepaint);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr SetFocus(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        [DllImport("user32.dll")]
        private static extern bool IsChild(IntPtr hWndParent, IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const int GWL_STYLE = -16;
        private const int WS_CHILD = 0x40000000;
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_MAXIMIZE = 0x01000000;
        private const int WS_MINIMIZE = 0x20000000;
        private const int WS_TABSTOP = 0x00010000;
        private const int SW_SHOW = 5;
        private const int SW_MINIMIZE = 6;
        private const int SW_RESTORE = 9;
        private const int SW_SHOWNORMAL = 1;
        private const int SW_MAXIMIZE = 3;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint WM_ACTIVATE = 0x0006;
        private const uint WA_CLICKACTIVE = 2;
        private const uint WM_SETFOCUS = 0x0007;
        private const int WM_MOUSEACTIVATE = 0x0021;
        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

        private uint _attachedThreadId = 0;
        private bool _isDispatcherHookRegistered = false;
        private HwndSourceHook? _wndProcHook;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        public BureauMedControl()
        {
            InitializeComponent();
            _viewModel = new BureauMedViewModel();
            DataContext = _viewModel;

            // Resize l'application embarquee quand la zone change de taille
            CentralHostZone.SizeChanged += CentralHostZone_SizeChanged;

            // Transferer le focus clavier vers la fenetre embarquee au clic
            CentralHostZone.PreviewMouseDown += CentralHostZone_PreviewMouseDown;

            AgendaContent.DossierDemande += dossier => DossierDemande?.Invoke(dossier);
            AgendaContent.CreationDossierDemandee += rdv => CreationDossierDemandee?.Invoke(rdv);

            Unloaded += BureauMedControl_Unloaded;
            Loaded += BureauMedControl_Loaded;
        }

        private void BureauMedControl_Loaded(object sender, RoutedEventArgs e)
        {
            // S'abonner aux evenements de la fenetre parente
            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.LocationChanged += Window_PositionChanged;
                window.SizeChanged += Window_PositionChanged;
                window.Activated += Window_Activated;
            }

            var source = PresentationSource.FromVisual(this) as HwndSource;
            if (source != null && _wndProcHook == null)
            {
                _wndProcHook = new HwndSourceHook(WndProc);
                source.AddHook(_wndProcHook);
            }
        }

        private void Window_Activated(object? sender, EventArgs e)
        {
            if (_embeddedWindowHandle != IntPtr.Zero && Visibility == Visibility.Visible)
            {
                FocusEmbeddedWindow();
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_MOUSEACTIVATE && _embeddedWindowHandle != IntPtr.Zero && Visibility == Visibility.Visible)
            {
                // Si l'utilisateur clique sur la fenêtre embarquée ou l'un de ses sous-contrôles
                if (wParam == _embeddedWindowHandle || IsChild(_embeddedWindowHandle, wParam))
                {
                    FocusEmbeddedWindow();
                }
            }
            return IntPtr.Zero;
        }

        private void Window_PositionChanged(object? sender, EventArgs e)
        {
            // Repositionner la fenetre embarquee quand la fenetre principale bouge
            if (_embeddedWindowHandle != IntPtr.Zero)
            {
                ResizeEmbeddedWindow();
            }
        }

        public void Initialize(MedAgentService medAgentService)
        {
            _viewModel?.Initialize(medAgentService);
        }

        /// <summary>Initialise l'Atelier d'écriture avec la factory LLM (Med).</summary>
        public void InitializeAtelier(Services.LLM.LLMServiceFactory llmFactory)
        {
            AtelierContent.Initialize(llmFactory);
        }

        /// <summary>Vrai si l'outil demandé est l'Atelier d'écriture (natif, pas d'embedding Win32).</summary>
        private static bool IsAtelierTool(string toolName)
            => toolName.Contains("Écriture", StringComparison.OrdinalIgnoreCase)
            || toolName.Contains("Ecriture", StringComparison.OrdinalIgnoreCase);

        /// <summary>Vrai si l'outil demandé est la bibliothèque d'infographies (natif).</summary>
        private static bool IsInfographiesTool(string toolName)
            => toolName.Contains("Infographies", StringComparison.OrdinalIgnoreCase);

        /// <summary>Vrai si l'outil demandé est l'agenda (natif).</summary>
        private static bool IsAgendaTool(string toolName)
            => toolName.Contains("Agenda", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Prévient le header de ce qu'il y a dans le cadre : « Analyser » et « Lire l'agenda »
        /// n'ont de sens que sur une application embarquée, pas devant l'agenda de Med.
        /// Paramètres : une application est-elle intégrée, et est-ce Doctolib.
        /// </summary>
        public event Action<bool, bool>? ContenuCadreChange;

        /// <summary>Relais du clic sur un rendez-vous de l'agenda : le dossier à ouvrir.</summary>
        public event Action<string>? DossierDemande;

        /// <summary>Relais de la demande de création d'un dossier depuis l'agenda.</summary>
        public event Action<Models.Agenda.RendezVous>? CreationDossierDemandee;

        private void NotifierContenuCadre()
        {
            bool embarquee = _embeddedWindowHandle != IntPtr.Zero;
            bool doctolib = embarquee
                && (_viewModel?.SelectedTool?.Contains("Doctolib", StringComparison.OrdinalIgnoreCase) ?? false);
            ContenuCadreChange?.Invoke(embarquee, doctolib);
        }

        /// <summary>
        /// Écran d'accueil du Bureau : l'agenda. Appelé à l'entrée dans le mode, et repris
        /// chaque fois qu'un autre outil est quitté — la zone ne revient plus vide.
        /// </summary>
        public void AfficherAccueil() => ShowAgenda();

        private void ShowAgenda()
        {
            if (_embeddedWindowHandle != IntPtr.Zero)
            {
                var oldHandle = _embeddedWindowHandle;
                ReleaseEmbeddedWindow();
                ShowWindow(oldHandle, SW_MINIMIZE);
            }

            PlaceholderText.Visibility = Visibility.Collapsed;
            AgendaContent.Recharger();
            AgendaContent.Visibility = Visibility.Visible;
            NotifierContenuCadre();
        }

        private void HideAgenda()
        {
            if (AgendaContent.Visibility != Visibility.Visible) return;
            AgendaContent.Visibility = Visibility.Collapsed;
        }

        // Methodes publiques appelees depuis le header principal
        public void EmbedTool(string toolName)
        {
            if (_viewModel != null)
            {
                _viewModel.SelectedTool = toolName;
            }

            if (IsAgendaTool(toolName))
            {
                HideAtelier();
                HideInfographies();
                ShowAgenda();
                return;
            }

            if (IsAtelierTool(toolName))
            {
                HideInfographies();
                HideAgenda();
                ShowAtelier();
                return;
            }

            if (IsInfographiesTool(toolName))
            {
                HideAtelier();
                HideAgenda();
                ShowInfographies();
                return;
            }

            HideAtelier();
            HideInfographies();
            HideAgenda();
            EmbedToolInternal(toolName);
        }

        private void ShowInfographies()
        {
            // Même raison que pour l'atelier : une app Win32 embarquée masquerait le WPF
            if (_embeddedWindowHandle != IntPtr.Zero)
            {
                var oldHandle = _embeddedWindowHandle;
                ReleaseEmbeddedWindow();
                ShowWindow(oldHandle, SW_MINIMIZE);
            }

            PlaceholderText.Visibility = Visibility.Collapsed;
            InfographiesContent.Recharger();
            InfographiesContent.Visibility = Visibility.Visible;
            NotifierContenuCadre();
            if (_viewModel != null)
                _viewModel.AnalysisResult = "Bibliothèque d'infographies ouverte.";
        }

        private void HideInfographies()
        {
            if (InfographiesContent.Visibility != Visibility.Visible) return;
            InfographiesContent.Visibility = Visibility.Collapsed;
            PlaceholderText.Visibility = Visibility.Visible;
        }

        private void ShowAtelier()
        {
            // Libérer une éventuelle app embarquée : les fenêtres Win32 embedées
            // dessinent par-dessus le WPF et masqueraient l'atelier.
            if (_embeddedWindowHandle != IntPtr.Zero)
            {
                var oldHandle = _embeddedWindowHandle;
                ReleaseEmbeddedWindow();
                ShowWindow(oldHandle, SW_MINIMIZE);
            }

            PlaceholderText.Visibility = Visibility.Collapsed;
            AtelierContent.Visibility = Visibility.Visible;
            NotifierContenuCadre();
            if (_viewModel != null)
                _viewModel.AnalysisResult = "Atelier d'écriture ouvert.";
        }

        private void HideAtelier()
        {
            if (AtelierContent.Visibility != Visibility.Visible) return;
            AtelierContent.SaveAll();
            AtelierContent.Visibility = Visibility.Collapsed;
            PlaceholderText.Visibility = Visibility.Visible;
        }

        public void ReleaseTool()
        {
            HideAtelier();
            HideInfographies();
            ReleaseEmbeddedWindow();
            ShowAgenda();   // la zone ne revient pas vide : on retombe sur l'agenda
            if (_viewModel != null)
            {
                _viewModel.AnalysisResult = "Application liberee.";
            }
        }

        public void ReleaseToolAndMinimize()
        {
            HideAtelier();
            HideInfographies();
            if (_embeddedWindowHandle != IntPtr.Zero)
            {
                var handleToMinimize = _embeddedWindowHandle;
                ReleaseEmbeddedWindow();
                // Minimiser la fenêtre après l'avoir libérée
                ShowWindow(handleToMinimize, SW_MINIMIZE);
            }
            if (_viewModel != null)
            {
                _viewModel.AnalysisResult = "";
            }
        }

        /// <summary>
        /// Photographie l'agenda Doctolib en plein écran temporaire (pour tout afficher sans défilement
        /// et sans troncature des noms), puis le réintègre immédiatement dans le cadre de Med.
        /// </summary>
        public void CapturerPourAgenda(string vue)
            => _ = CapturerPourAgendaAsync(vue);

        public async System.Threading.Tasks.Task CapturerPourAgendaAsync(string vue)
        {
            if (_embeddedWindowHandle == IntPtr.Zero)
            {
                if (_viewModel != null)
                    _viewModel.AnalysisResult = "Intégrez d'abord Doctolib dans le cadre.";
                return;
            }

            var hwnd = _embeddedWindowHandle;
            var originalStyle = _originalStyle;
            var originalParent = _originalParent;
            var toolName = _viewModel?.SelectedTool ?? "Doctolib";

            try
            {
                if (_viewModel != null)
                    _viewModel.AnalysisResult = $"Passage en plein écran et capture de « {vue} »...";

                // 1. Stopper temporairement le timer de position pour éviter tout conflit
                StopPositionGuard();

                // 2. Détacher la fenêtre du host WPF vers le Desktop
                SetParent(hwnd, IntPtr.Zero);

                // 3. Restaurer le style normal/popup de premier plan
                int normalStyle = (originalStyle | WS_POPUP | WS_CAPTION | WS_THICKFRAME | WS_VISIBLE) & ~WS_CHILD;
                SetWindowLong(hwnd, GWL_STYLE, normalStyle);

                // 4. Maximiser la fenêtre pour afficher la vue complète
                ShowWindow(hwnd, SW_MAXIMIZE);
                SetForegroundWindow(hwnd);

                // 5. Laisser un court délai (250 ms) pour que le moteur Chromium/Electron de Doctolib
                //    recalcule et affiche la page web en pleine résolution (reflow CSS)
                await System.Threading.Tasks.Task.Delay(250);

                // 6. Capturer la zone plein écran du moniteur où se trouve la fenêtre
                byte[] captureBytes;
                IntPtr hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
                if (GetMonitorInfo(hMonitor, ref mi))
                {
                    int x = mi.rcMonitor.Left;
                    int y = mi.rcMonitor.Top;
                    int w = mi.rcMonitor.Right - mi.rcMonitor.Left;
                    int h = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
                    var captureService = new ScreenCaptureService();
                    captureBytes = captureService.CaptureRegion(new Rect(x, y, w, h));
                }
                else if (GetWindowRect(hwnd, out RECT winRect))
                {
                    int x = Math.Max(0, winRect.Left);
                    int y = Math.Max(0, winRect.Top);
                    int w = winRect.Right - x;
                    int h = winRect.Bottom - y;
                    var captureService = new ScreenCaptureService();
                    captureBytes = captureService.CaptureRegion(new Rect(x, y, w, h));
                }
                else
                {
                    captureBytes = CaptureZone();
                }

                // 7. Remettre la fenêtre en mode normal puis la ré-embarquer dans MedCompanion
                ShowWindow(hwnd, SW_SHOWNORMAL);
                CompleteEmbedding(hwnd, toolName);

                // 8. Enregistrer l'image capturée
                var (ok, chemin, err) = _capturesAgenda.Enregistrer(captureBytes, vue);
                if (_viewModel != null)
                {
                    _viewModel.AnalysisResult = ok
                        ? $"Capture plein écran « {vue} » enregistrée à {DateTime.Now:HH'h'mm}. Ouvrez l'agenda et cliquez sur « Mettre à jour »."
                        : err ?? "Capture impossible.";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BureauMedControl] Erreur capture plein écran : {ex.Message}");
                try
                {
                    ShowWindow(hwnd, SW_SHOWNORMAL);
                    CompleteEmbedding(hwnd, toolName);
                }
                catch { }

                if (_viewModel != null)
                    _viewModel.AnalysisResult = $"Erreur capture plein écran : {ex.Message}";
            }
        }

        private readonly Services.Agenda.AgendaCaptureService _capturesAgenda = new();

        public void AnalyzeTool()
        {
            AnalyzeButton_Click(this, new RoutedEventArgs());
        }

        /// <summary>
        /// Capture la zone centrale (fenêtre embarquée) et retourne les bytes PNG.
        /// Utilisé par le dialog d'import Doctolib.
        /// </summary>
        public byte[] CaptureZone()
        {
            var captureService = new ScreenCaptureService();
            var point = CentralHostZone.PointToScreen(new Point(0, 0));
            var rect = new Rect(point.X, point.Y, CentralHostZone.ActualWidth, CentralHostZone.ActualHeight);
            return captureService.CaptureRegion(rect);
        }

        private void EmbedToolInternal(string toolName)
        {
            string searchTitle = toolName;
            if (toolName == "Google Chrome") searchTitle = "Chrome";
            if (toolName == "Microsoft Edge") searchTitle = "Edge";

            if (_viewModel != null)
                _viewModel.AnalysisResult = $"Recherche de {searchTitle}...";

            IntPtr hwnd = FindWindowByTitle(searchTitle);

            if (hwnd == IntPtr.Zero)
            {
                if (_viewModel != null)
                    _viewModel.AnalysisResult = $"{toolName} non trouve. Ouvrez-le d'abord.";
                return;
            }

            // Si c'est déjà la fenêtre actuellement embarquée
            if (_embeddedWindowHandle == hwnd)
            {
                if (IsIconic(hwnd))
                {
                    ShowWindow(hwnd, SW_RESTORE);
                }
                ResizeEmbeddedWindow();
                SetForegroundWindow(hwnd);
                SetFocus(hwnd);
                return;
            }

            // Libérer l'ancienne fenêtre ET la minimiser
            if (_embeddedWindowHandle != IntPtr.Zero)
            {
                var oldHandle = _embeddedWindowHandle;
                ReleaseEmbeddedWindow();
                ShowWindow(oldHandle, SW_MINIMIZE);
            }

            // Si la fenêtre à intégrer est minimisée (dans la barre des tâches) ou maximisée,
            // restaurer son état normal top-level avant de modifier son parent
            if (IsIconic(hwnd))
            {
                ShowWindow(hwnd, SW_RESTORE);
            }
            else if (IsZoomed(hwnd))
            {
                ShowWindow(hwnd, SW_SHOWNORMAL);
            }

            CompleteEmbedding(hwnd, toolName);
        }

        private void CompleteEmbedding(IntPtr hwnd, string toolName)
        {
            _embeddedWindowHandle = hwnd;
            _originalParent = GetParent(hwnd);
            _originalStyle = GetWindowLong(hwnd, GWL_STYLE);

            var hostHandle = GetHostHandle();
            if (hostHandle == IntPtr.Zero)
            {
                if (_viewModel != null)
                    _viewModel.AnalysisResult = "Erreur: impossible d'obtenir le handle host.";
                return;
            }

            // S'assurer que la fenêtre n'est pas sous forme d'icône réduite
            if (IsIconic(hwnd))
            {
                ShowWindow(hwnd, SW_RESTORE);
            }

            // Retirer les styles maximisé/minimisé/popup/caption/thickframe en plus des autres et ajouter WS_TABSTOP
            int newStyle = (_originalStyle & ~WS_POPUP & ~WS_CAPTION & ~WS_THICKFRAME & ~WS_MAXIMIZE & ~WS_MINIMIZE) | WS_CHILD | WS_VISIBLE | WS_TABSTOP;
            SetWindowLong(hwnd, GWL_STYLE, newStyle);
            SetParent(hwnd, hostHandle);

            // Informer le gestionnaire de fenêtres du changement de style
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED | SWP_SHOWWINDOW);

            // Restaurer à nouveau au cas où SetParent a basculé l'état
            ShowWindow(hwnd, SW_RESTORE);

            ResizeEmbeddedWindow();
            StartPositionGuard();

            // Attacher les threads d'entrée et activer le hook de messages clavier
            AttachThreadInputForEmbeddedWindow(hwnd);
            EnsureDispatcherHook(true);

            // Donner le focus clavier actif à la fenêtre embarquée
            FocusEmbeddedWindow();

            PlaceholderText.Visibility = Visibility.Collapsed;
            NotifierContenuCadre();
            if (_viewModel != null)
                _viewModel.AnalysisResult = $"{toolName} intégré avec succès.";
        }

        private void BureauMedControl_Unloaded(object sender, RoutedEventArgs e)
        {
            // Se desabonner des evenements
            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.LocationChanged -= Window_PositionChanged;
                window.SizeChanged -= Window_PositionChanged;
                window.Activated -= Window_Activated;
            }

            var source = PresentationSource.FromVisual(this) as HwndSource;
            if (source != null && _wndProcHook != null)
            {
                source.RemoveHook(_wndProcHook);
                _wndProcHook = null;
            }

            // Liberer la fenetre embarquee quand le controle est decharge
            ReleaseEmbeddedWindow();

            // Sauvegarder le chapitre en cours de l'atelier d'écriture
            AtelierContent.SaveAll();
        }

        private void CentralHostZone_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Redimensionner la fenetre embarquee
            if (_embeddedWindowHandle != IntPtr.Zero)
            {
                ResizeEmbeddedWindow();
            }
        }

        private void CentralHostZone_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // Transferer le focus clavier vers la fenetre embarquee au clic
            if (_embeddedWindowHandle != IntPtr.Zero)
            {
                FocusEmbeddedWindow();
            }
        }

        private void EmbedToolButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;
            // Réutiliser la logique centralisée
            EmbedToolInternal(_viewModel.SelectedTool);
        }

        private void ReleaseToolButton_Click(object sender, RoutedEventArgs e)
        {
            ReleaseEmbeddedWindow();
            if (_viewModel != null)
            {
                _viewModel.AnalysisResult = "Application liberee.";
            }
        }

        private void ReleaseEmbeddedWindow()
        {
            if (_embeddedWindowHandle != IntPtr.Zero)
            {
                // Désactiver le hook de messages clavier et détacher les threads d'entrée
                EnsureDispatcherHook(false);
                DetachThreadInputForEmbeddedWindow();

                // Arreter le timer de garde
                StopPositionGuard();

                var hwnd = _embeddedWindowHandle;

                // Restaurer le style original
                SetWindowLong(hwnd, GWL_STYLE, _originalStyle);

                // Restaurer le parent original (desktop)
                SetParent(hwnd, _originalParent);

                // Informer le gestionnaire de fenêtres du changement de style
                SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED | SWP_SHOWWINDOW);

                // Restaurer la visibilite
                ShowWindow(hwnd, SW_RESTORE);

                _embeddedWindowHandle = IntPtr.Zero;
                _originalParent = IntPtr.Zero;
                _originalStyle = 0;

                PlaceholderText.Visibility = Visibility.Visible;
                NotifierContenuCadre();
            }
        }

        private void AttachThreadInputForEmbeddedWindow(IntPtr hwnd)
        {
            DetachThreadInputForEmbeddedWindow();

            if (hwnd == IntPtr.Zero) return;

            uint childThreadId = GetWindowThreadProcessId(hwnd, out _);
            uint currentThreadId = GetCurrentThreadId();

            if (childThreadId != 0 && childThreadId != currentThreadId)
            {
                if (AttachThreadInput(currentThreadId, childThreadId, true))
                {
                    _attachedThreadId = childThreadId;
                }
            }
        }

        private void DetachThreadInputForEmbeddedWindow()
        {
            if (_attachedThreadId != 0)
            {
                uint currentThreadId = GetCurrentThreadId();
                AttachThreadInput(currentThreadId, _attachedThreadId, false);
                _attachedThreadId = 0;
            }
        }

        public void FocusEmbeddedWindow()
        {
            if (_embeddedWindowHandle == IntPtr.Zero) return;

            // S'assurer que les queues d'entrée clavier sont synchronisées
            if (_attachedThreadId == 0)
            {
                AttachThreadInputForEmbeddedWindow(_embeddedWindowHandle);
            }

            // Activer la fenêtre enfant et lui donner le focus
            SendMessage(_embeddedWindowHandle, WM_ACTIVATE, (IntPtr)WA_CLICKACTIVE, IntPtr.Zero);
            SetFocus(_embeddedWindowHandle);
            SendMessage(_embeddedWindowHandle, WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);

            // Donner le focus au sous-contrôle de rendu Chromium si présent
            IntPtr renderWidget = FindRenderWidgetHostHwnd(_embeddedWindowHandle);
            if (renderWidget != IntPtr.Zero && renderWidget != _embeddedWindowHandle)
            {
                SetFocus(renderWidget);
                SendMessage(renderWidget, WM_SETFOCUS, IntPtr.Zero, IntPtr.Zero);
            }
        }

        private IntPtr FindRenderWidgetHostHwnd(IntPtr parent)
        {
            if (parent == IntPtr.Zero) return IntPtr.Zero;

            IntPtr found = IntPtr.Zero;
            EnumChildWindows(parent, (hWnd, lParam) =>
            {
                var sb = new System.Text.StringBuilder(128);
                GetClassName(hWnd, sb, 128);
                string className = sb.ToString();

                // Sous-contrôle de rendu direct de Chromium / Electron (Doctolib, Chrome, Edge)
                if (className.IndexOf("Chrome_RenderWidgetHostHWND", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    className.IndexOf("Intermediate D3D Window", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    found = hWnd;
                    return false; // Stop enumeration
                }
                return true;
            }, IntPtr.Zero);

            return found != IntPtr.Zero ? found : parent;
        }

        private void ComponentDispatcher_ThreadFilterMessage(ref MSG msg, ref bool handled)
        {
            if (_embeddedWindowHandle == IntPtr.Zero || Visibility != Visibility.Visible) return;

            // Messages clavier : WM_KEYDOWN (0x100) à WM_SYSKEYUP (0x105)
            if (msg.message >= 0x0100 && msg.message <= 0x0108)
            {
                // Si le focus WPF est sur un vrai champ de saisie de MedCompanion, ne pas intercepter
                var focused = Keyboard.FocusedElement;
                if (focused is TextBox || focused is PasswordBox || focused is RichTextBox)
                {
                    return;
                }

                // Si le message est déjà destiné à l'application embarquée ou l'un de ses enfants, ne pas dupliquer
                if (msg.hwnd == _embeddedWindowHandle || IsChild(_embeddedWindowHandle, msg.hwnd))
                {
                    return;
                }

                // Trouver la fenêtre cible qui doit recevoir les touches
                IntPtr targetHwnd = GetFocus();
                if (targetHwnd == IntPtr.Zero || (!IsChild(_embeddedWindowHandle, targetHwnd) && targetHwnd != _embeddedWindowHandle))
                {
                    targetHwnd = FindRenderWidgetHostHwnd(_embeddedWindowHandle);
                }

                // Transmettre le message clavier directement au contrôle actif de Doctolib
                PostMessage(targetHwnd, (uint)msg.message, msg.wParam, msg.lParam);
                handled = true;
            }
        }

        private void EnsureDispatcherHook(bool register)
        {
            if (register && !_isDispatcherHookRegistered)
            {
                ComponentDispatcher.ThreadFilterMessage += ComponentDispatcher_ThreadFilterMessage;
                _isDispatcherHookRegistered = true;
            }
            else if (!register && _isDispatcherHookRegistered)
            {
                ComponentDispatcher.ThreadFilterMessage -= ComponentDispatcher_ThreadFilterMessage;
                _isDispatcherHookRegistered = false;
            }
        }

        private void StartPositionGuard()
        {
            StopPositionGuard();
            _positionGuardTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _positionGuardTimer.Tick += PositionGuardTimer_Tick;
            _positionGuardTimer.Start();
        }

        private void StopPositionGuard()
        {
            if (_positionGuardTimer != null)
            {
                _positionGuardTimer.Stop();
                _positionGuardTimer.Tick -= PositionGuardTimer_Tick;
                _positionGuardTimer = null;
            }
        }

        private void PositionGuardTimer_Tick(object? sender, EventArgs e)
        {
            // Replacer la fenetre si elle a bouge
            if (_embeddedWindowHandle != IntPtr.Zero)
            {
                ResizeEmbeddedWindow();
            }
        }

        private IntPtr GetHostHandle()
        {
            // Obtenir le handle de la zone centrale avec fallback sur la fenêtre principale
            var source = (PresentationSource.FromVisual(CentralHostZone) ?? PresentationSource.FromVisual(this)) as HwndSource;
            if (source != null && source.Handle != IntPtr.Zero) return source.Handle;

            var window = Window.GetWindow(this);
            if (window != null)
            {
                var helper = new WindowInteropHelper(window);
                return helper.Handle;
            }
            return IntPtr.Zero;
        }

        private void ResizeEmbeddedWindow()
        {
            if (_embeddedWindowHandle == IntPtr.Zero) return;

            // Obtenir la fenetre WPF parente
            var window = Window.GetWindow(this);
            if (window == null) return;

            // Si la zone hôte n'est pas encore mesurée/rendue (ex: au moment précis du SwitchMode)
            if (!CentralHostZone.IsLoaded || CentralHostZone.ActualWidth <= 20 || CentralHostZone.ActualHeight <= 20)
            {
                // Différer le redimensionnement dès que le layout WPF est prêt
                Dispatcher.BeginInvoke(new Action(ResizeEmbeddedWindow), DispatcherPriority.Loaded);
                return;
            }

            // Si la fenêtre embarquée est encore sous forme d'icône MDI réduite
            if (IsIconic(_embeddedWindowHandle))
            {
                ShowWindow(_embeddedWindowHandle, SW_RESTORE);
            }

            // Obtenir le facteur DPI
            double dpiX = 1.0, dpiY = 1.0;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                dpiX = source.CompositionTarget.TransformToDevice.M11;
                dpiY = source.CompositionTarget.TransformToDevice.M22;
            }

            // Calculer la position de CentralHostZone relative a la fenetre WPF
            Point relativePoint;
            try
            {
                relativePoint = CentralHostZone.TransformToAncestor(window).Transform(new Point(0, 0));
            }
            catch
            {
                Dispatcher.BeginInvoke(new Action(ResizeEmbeddedWindow), DispatcherPriority.Loaded);
                return;
            }

            // Obtenir les épaisseurs de bordure réelles de CentralHostZone
            double bLeft = CentralHostZone.BorderThickness.Left;
            double bTop = CentralHostZone.BorderThickness.Top;
            double bRight = CentralHostZone.BorderThickness.Right;
            double bBottom = CentralHostZone.BorderThickness.Bottom;

            int offsetX = (int)(bLeft * dpiX);
            int offsetY = (int)(bTop * dpiY);
            int width = (int)((CentralHostZone.ActualWidth - bLeft - bRight) * dpiX);
            int height = (int)((CentralHostZone.ActualHeight - bTop - bBottom) * dpiY);

            // Convertir en pixels physiques
            int x = (int)(relativePoint.X * dpiX) + offsetX;
            int y = (int)(relativePoint.Y * dpiY) + offsetY;

            if (width > 20 && height > 20)
            {
                MoveWindow(_embeddedWindowHandle, x, y, width, height, true);
            }
        }

        private IntPtr FindWindowByTitle(string title)
        {
            IntPtr found = IntPtr.Zero;

            EnumWindows((hWnd, lParam) =>
            {
                if (IsWindowVisible(hWnd))
                {
                    var sb = new System.Text.StringBuilder(256);
                    GetWindowText(hWnd, sb, 256);
                    string windowTitle = sb.ToString();

                    if (!string.IsNullOrEmpty(windowTitle) &&
                        windowTitle.IndexOf(title, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        // Ignorer MedCompanion
                        if (windowTitle.IndexOf("MedCompanion", StringComparison.OrdinalIgnoreCase) < 0)
                        {
                            found = hWnd;
                            return false; // Stop enumeration
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);

            return found;
        }

        private void AnalyzeButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null || _viewModel.IsAnalyzing) return;

            // Calculer les coordonnees ecran de la zone centrale
            var point = CentralHostZone.PointToScreen(new Point(0, 0));
            var rect = new Rect(point.X, point.Y, CentralHostZone.ActualWidth, CentralHostZone.ActualHeight);

            if (_viewModel.AnalyzeCommand.CanExecute(rect))
            {
                _viewModel.AnalyzeCommand.Execute(rect);
            }
        }

        private void ChatInputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(ChatInputBox.Text))
            {
                SendChatMessage();
            }
        }

        private void SendChatButton_Click(object sender, RoutedEventArgs e)
        {
            SendChatMessage();
        }

        private async void SendChatMessage()
        {
            if (_viewModel == null || string.IsNullOrWhiteSpace(ChatInputBox.Text)) return;

            string message = ChatInputBox.Text;
            ChatInputBox.Text = "";

            _viewModel.AnalysisResult = "Med reflechit...";

            try
            {
                // Capturer la zone centrale si une app est embedee
                byte[]? imageBytes = null;
                if (_embeddedWindowHandle != IntPtr.Zero)
                {
                    var captureService = new ScreenCaptureService();
                    var point = CentralHostZone.PointToScreen(new Point(0, 0));
                    var rect = new Rect(point.X, point.Y, CentralHostZone.ActualWidth, CentralHostZone.ActualHeight);
                    imageBytes = captureService.CaptureRegion(rect);
                }

                // Envoyer a Med
                if (_viewModel.MedAgentService != null)
                {
                    string response = await _viewModel.MedAgentService.ProcessVisionRequestAsync(
                        message,
                        imageBytes ?? Array.Empty<byte>());
                    _viewModel.AnalysisResult = response;
                }
                else
                {
                    _viewModel.AnalysisResult = "Service Med non disponible.";
                }
            }
            catch (Exception ex)
            {
                _viewModel.AnalysisResult = $"Erreur: {ex.Message}";
            }
        }
    }
}
