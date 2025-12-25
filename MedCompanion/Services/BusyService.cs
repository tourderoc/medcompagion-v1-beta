using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service centralisé pour gérer l'état "occupé" de l'application.
    /// Affiche un overlay modal avec progression et possibilité d'annulation.
    /// Pattern Singleton pour accès global.
    /// </summary>
    public class BusyService : INotifyPropertyChanged
    {
        // Singleton
        private static BusyService? _instance;
        private static readonly object _lock = new();

        public static BusyService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new BusyService();
                    }
                }
                return _instance;
            }
        }

        private BusyService() { }

        // ===== PROPRIÉTÉS BINDABLES =====

        private bool _isBusy;
        /// <summary>
        /// Indique si une opération est en cours
        /// </summary>
        public bool IsBusy
        {
            get => _isBusy;
            private set => SetProperty(ref _isBusy, value);
        }

        private string _message = string.Empty;
        /// <summary>
        /// Message principal affiché (ex: "Structuration de la note...")
        /// </summary>
        public string Message
        {
            get => _message;
            private set => SetProperty(ref _message, value);
        }

        private string _step = string.Empty;
        /// <summary>
        /// Étape détaillée (ex: "Anonymisation des données...")
        /// </summary>
        public string Step
        {
            get => _step;
            private set => SetProperty(ref _step, value);
        }

        private double _progress = -1;
        /// <summary>
        /// Progression (0-100). -1 = indéterminé (barre qui défile)
        /// </summary>
        public double Progress
        {
            get => _progress;
            private set => SetProperty(ref _progress, value);
        }

        private bool _canCancel = true;
        /// <summary>
        /// Indique si l'opération peut être annulée
        /// </summary>
        public bool CanCancel
        {
            get => _canCancel;
            private set => SetProperty(ref _canCancel, value);
        }

        private bool _isCancellationRequested;
        /// <summary>
        /// Indique si l'annulation a été demandée
        /// </summary>
        public bool IsCancellationRequested
        {
            get => _isCancellationRequested;
            private set => SetProperty(ref _isCancellationRequested, value);
        }

        /// <summary>
        /// Source de token d'annulation pour l'opération en cours
        /// </summary>
        public CancellationTokenSource? CancellationSource { get; private set; }

        /// <summary>
        /// Fenêtre TopMost pour afficher l'overlay au-dessus de tout
        /// </summary>
        private Window? _overlayWindow;

        // ===== MÉTHODES PUBLIQUES =====

        /// <summary>
        /// Démarre une opération avec overlay modal
        /// </summary>
        /// <param name="message">Message principal</param>
        /// <param name="canCancel">Permet l'annulation</param>
        /// <returns>CancellationToken à passer aux opérations async</returns>
        public CancellationToken Start(string message, bool canCancel = true)
        {
            // Annuler toute opération précédente
            CancellationSource?.Cancel();
            CancellationSource?.Dispose();

            CancellationSource = new CancellationTokenSource();
            IsCancellationRequested = false;
            Message = message;
            Step = string.Empty;
            Progress = -1; // Indéterminé par défaut
            CanCancel = canCancel;
            IsBusy = true;

            // Créer et afficher la fenêtre overlay TopMost
            ShowOverlayWindow();

            System.Diagnostics.Debug.WriteLine($"[BusyService] Start: {message}");

            return CancellationSource.Token;
        }

        /// <summary>
        /// Met à jour l'étape en cours
        /// </summary>
        public void UpdateStep(string step)
        {
            Step = step;
            System.Diagnostics.Debug.WriteLine($"[BusyService] Step: {step}");
        }

        /// <summary>
        /// Met à jour la progression (0-100)
        /// </summary>
        public void UpdateProgress(double progress, string? step = null)
        {
            Progress = Math.Clamp(progress, 0, 100);
            if (step != null)
            {
                Step = step;
            }
            System.Diagnostics.Debug.WriteLine($"[BusyService] Progress: {progress:F0}% - {step ?? Step}");
        }

        /// <summary>
        /// Termine l'opération (succès ou erreur)
        /// </summary>
        public void Stop()
        {
            IsBusy = false;
            Message = string.Empty;
            Step = string.Empty;
            Progress = -1;
            IsCancellationRequested = false;

            // Fermer la fenêtre overlay
            HideOverlayWindow();

            // Ne pas disposer immédiatement le CancellationSource
            // car des opérations peuvent encore vérifier le token
            var oldSource = CancellationSource;
            CancellationSource = null;

            // Disposer après un délai
            System.Threading.Tasks.Task.Delay(100).ContinueWith(_ => oldSource?.Dispose());

            System.Diagnostics.Debug.WriteLine($"[BusyService] Stop");
        }

        /// <summary>
        /// Demande l'annulation de l'opération en cours
        /// </summary>
        public void Cancel()
        {
            if (CanCancel && CancellationSource != null && !CancellationSource.IsCancellationRequested)
            {
                IsCancellationRequested = true;
                Step = "Annulation en cours...";
                CancellationSource.Cancel();
                System.Diagnostics.Debug.WriteLine($"[BusyService] Cancel requested");
            }
        }

        /// <summary>
        /// Vérifie si l'annulation a été demandée et lève une exception si oui
        /// </summary>
        public void ThrowIfCancellationRequested()
        {
            CancellationSource?.Token.ThrowIfCancellationRequested();
        }

        // ===== MÉTHODES PRIVÉES =====

        /// <summary>
        /// Affiche une fenêtre overlay TopMost au-dessus de toutes les fenêtres
        /// </summary>
        private void ShowOverlayWindow()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                // Fermer toute fenêtre précédente
                if (_overlayWindow != null)
                {
                    _overlayWindow.Close();
                    _overlayWindow = null;
                }

                // Créer une nouvelle fenêtre overlay
                var overlayContent = new Views.BusyOverlay();

                _overlayWindow = new Window
                {
                    Content = overlayContent,
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = System.Windows.Media.Brushes.Transparent,
                    ShowInTaskbar = false,
                    Topmost = true, // IMPORTANT: Toujours au premier plan
                    WindowState = WindowState.Maximized,
                    ResizeMode = ResizeMode.NoResize,
                    Owner = Application.Current.MainWindow // Lié à la fenêtre principale
                };

                // Empêcher la fermeture de la fenêtre
                _overlayWindow.Closing += (s, e) =>
                {
                    if (IsBusy)
                    {
                        e.Cancel = true;
                    }
                };

                _overlayWindow.Show();
                System.Diagnostics.Debug.WriteLine("[BusyService] Overlay window shown");
            });
        }

        /// <summary>
        /// Masque et ferme la fenêtre overlay
        /// </summary>
        private void HideOverlayWindow()
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (_overlayWindow != null)
                {
                    _overlayWindow.Close();
                    _overlayWindow = null;
                    System.Diagnostics.Debug.WriteLine("[BusyService] Overlay window closed");
                }
            });
        }

        // ===== INotifyPropertyChanged =====

        public event PropertyChangedEventHandler? PropertyChanged;

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
