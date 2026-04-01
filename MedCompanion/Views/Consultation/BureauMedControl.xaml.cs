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

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const int GWL_STYLE = -16;
        private const int WS_CHILD = 0x40000000;
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WS_CAPTION = 0x00C00000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_MAXIMIZE = 0x01000000;
        private const int WS_MINIMIZE = 0x20000000;
        private const int SW_SHOW = 5;
        private const int SW_MINIMIZE = 6;
        private const int SW_RESTORE = 9;
        private const int SW_SHOWNORMAL = 1;

        public BureauMedControl()
        {
            InitializeComponent();
            _viewModel = new BureauMedViewModel();
            DataContext = _viewModel;

            // Resize l'application embarquee quand la zone change de taille
            CentralHostZone.SizeChanged += CentralHostZone_SizeChanged;

            // Transferer le focus clavier vers la fenetre embarquee au clic
            CentralHostZone.PreviewMouseDown += CentralHostZone_PreviewMouseDown;

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
            }
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

        // Methodes publiques appelees depuis le header principal
        public void EmbedTool(string toolName)
        {
            if (_viewModel != null)
            {
                _viewModel.SelectedTool = toolName;
            }
            EmbedToolInternal(toolName);
        }

        public void ReleaseTool()
        {
            ReleaseEmbeddedWindow();
            if (_viewModel != null)
            {
                _viewModel.AnalysisResult = "Application liberee.";
            }
        }

        public void ReleaseToolAndMinimize()
        {
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

        public void AnalyzeTool()
        {
            AnalyzeButton_Click(this, new RoutedEventArgs());
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

            // Libérer l'ancienne fenêtre ET la minimiser
            if (_embeddedWindowHandle != IntPtr.Zero)
            {
                var oldHandle = _embeddedWindowHandle;
                ReleaseEmbeddedWindow();
                ShowWindow(oldHandle, SW_MINIMIZE);
            }

            // Vérifier si la fenêtre est minimisée ou maximisée
            bool needsRestore = IsIconic(hwnd) || IsZoomed(hwnd);

            if (needsRestore)
            {
                // Restaurer la fenêtre en mode normal
                ShowWindow(hwnd, SW_SHOWNORMAL);

                // Attendre que Windows traite la restauration avant de continuer
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    CompleteEmbedding(hwnd, toolName);
                }), DispatcherPriority.Background);
            }
            else
            {
                CompleteEmbedding(hwnd, toolName);
            }
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

            // Retirer les styles maximisé/minimisé en plus des autres
            int newStyle = (_originalStyle & ~WS_POPUP & ~WS_CAPTION & ~WS_THICKFRAME & ~WS_MAXIMIZE & ~WS_MINIMIZE) | WS_CHILD | WS_VISIBLE;
            SetWindowLong(hwnd, GWL_STYLE, newStyle);
            SetParent(hwnd, hostHandle);
            ShowWindow(hwnd, SW_SHOW);
            ResizeEmbeddedWindow();
            StartPositionGuard();

            // Donner le focus clavier a la fenetre embarquee
            SetForegroundWindow(hwnd);
            SetFocus(hwnd);

            PlaceholderText.Visibility = Visibility.Collapsed;
            if (_viewModel != null)
                _viewModel.AnalysisResult = $"{toolName} integre avec succes.";
        }

        private void BureauMedControl_Unloaded(object sender, RoutedEventArgs e)
        {
            // Se desabonner des evenements
            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.LocationChanged -= Window_PositionChanged;
                window.SizeChanged -= Window_PositionChanged;
            }

            // Liberer la fenetre embarquee quand le controle est decharge
            ReleaseEmbeddedWindow();
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
            // Transferer le focus clavier vers la fenetre embarquee
            if (_embeddedWindowHandle != IntPtr.Zero)
            {
                SetForegroundWindow(_embeddedWindowHandle);
                SetFocus(_embeddedWindowHandle);
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
                // Arreter le timer de garde
                StopPositionGuard();

                // Restaurer le style original
                SetWindowLong(_embeddedWindowHandle, GWL_STYLE, _originalStyle);

                // Restaurer le parent original (desktop)
                SetParent(_embeddedWindowHandle, _originalParent);

                // Restaurer la visibilite
                ShowWindow(_embeddedWindowHandle, SW_SHOW);

                _embeddedWindowHandle = IntPtr.Zero;
                _originalParent = IntPtr.Zero;
                _originalStyle = 0;

                PlaceholderText.Visibility = Visibility.Visible;
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
            // Obtenir le handle de la zone centrale
            var source = PresentationSource.FromVisual(CentralHostZone) as HwndSource;
            return source?.Handle ?? IntPtr.Zero;
        }

        private void ResizeEmbeddedWindow()
        {
            if (_embeddedWindowHandle == IntPtr.Zero) return;

            // Obtenir la fenetre WPF parente
            var window = Window.GetWindow(this);
            if (window == null) return;

            // Obtenir le facteur DPI
            double dpiX = 1.0, dpiY = 1.0;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                dpiX = source.CompositionTarget.TransformToDevice.M11;
                dpiY = source.CompositionTarget.TransformToDevice.M22;
            }

            // Calculer la position de CentralHostZone relative a la fenetre WPF
            var relativePoint = CentralHostZone.TransformToAncestor(window).Transform(new Point(0, 0));

            // Ajouter la bordure (2px WPF = variable pixels selon DPI)
            int borderOffset = (int)(2 * dpiX);

            // Convertir en pixels physiques
            int x = (int)(relativePoint.X * dpiX) + borderOffset;
            int y = (int)(relativePoint.Y * dpiY) + borderOffset;
            int width = (int)((CentralHostZone.ActualWidth - 4) * dpiX);
            int height = (int)((CentralHostZone.ActualHeight - 4) * dpiY);

            MoveWindow(_embeddedWindowHandle, x, y, width, height, true);
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
