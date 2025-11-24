using System;
using System.Windows;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service pour gérer la sauvegarde et restauration de l'état de la fenêtre principale
    /// </summary>
    public class WindowStateService
    {
        /// <summary>
        /// Sauvegarde l'état actuel de la fenêtre
        /// </summary>
        public void SaveWindowState(Window window)
        {
            if (window == null)
                throw new ArgumentNullException(nameof(window));

            var settings = Properties.Settings.Default;

            // Sauvegarder l'état de la fenêtre
            settings.WindowLastState = window.WindowState.ToString();

            // Si la fenêtre est en mode Normal, sauvegarder position et taille
            if (window.WindowState == WindowState.Normal)
            {
                settings.WindowLastLeft = window.Left;
                settings.WindowLastTop = window.Top;
                settings.WindowLastWidth = window.Width;
                settings.WindowLastHeight = window.Height;
            }
            else
            {
                // Si maximisé ou minimisé, sauvegarder RestoreBounds
                settings.WindowLastLeft = window.RestoreBounds.Left;
                settings.WindowLastTop = window.RestoreBounds.Top;
                settings.WindowLastWidth = window.RestoreBounds.Width;
                settings.WindowLastHeight = window.RestoreBounds.Height;
            }

            settings.Save();
        }

        /// <summary>
        /// Restaure l'état de la fenêtre au démarrage
        /// </summary>
        public void RestoreWindowState(Window window)
        {
            if (window == null)
                throw new ArgumentNullException(nameof(window));

            var settings = Properties.Settings.Default;
            var startupPreference = settings.WindowStartupPreference;

            // Déterminer l'état de démarrage
            WindowState targetState = WindowState.Normal;

            if (startupPreference == "Remember")
            {
                // Restaurer le dernier état
                if (Enum.TryParse<WindowState>(settings.WindowLastState, out var lastState))
                {
                    targetState = lastState;
                }
            }
            else if (startupPreference == "Maximized")
            {
                targetState = WindowState.Maximized;
            }
            // "Windowed" reste en Normal

            // Restaurer position et taille (seulement si valides)
            if (settings.WindowLastWidth > 0 && settings.WindowLastHeight > 0)
            {
                window.Width = settings.WindowLastWidth;
                window.Height = settings.WindowLastHeight;

                // Vérifier que la position est visible sur un écran
                if (IsPositionValid(settings.WindowLastLeft, settings.WindowLastTop, 
                    settings.WindowLastWidth, settings.WindowLastHeight))
                {
                    window.Left = settings.WindowLastLeft;
                    window.Top = settings.WindowLastTop;
                }
                else
                {
                    // Centrer la fenêtre si la position sauvegardée n'est plus valide
                    window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                }
            }

            // Appliquer l'état de la fenêtre
            window.WindowState = targetState;
        }

        /// <summary>
        /// Retourne la préférence de démarrage actuelle
        /// </summary>
        public string GetStartupPreference()
        {
            return Properties.Settings.Default.WindowStartupPreference;
        }

        /// <summary>
        /// Définit la préférence de démarrage
        /// </summary>
        public void SetStartupPreference(string preference)
        {
            if (preference != "Remember" && preference != "Windowed" && preference != "Maximized")
                throw new ArgumentException("Préférence invalide. Valeurs acceptées: Remember, Windowed, Maximized", nameof(preference));

            Properties.Settings.Default.WindowStartupPreference = preference;
            Properties.Settings.Default.Save();
        }

        /// <summary>
        /// Vérifie si une position de fenêtre est valide (visible sur au moins un écran)
        /// </summary>
        private bool IsPositionValid(double left, double top, double width, double height)
        {
            var rect = new Rect(left, top, width, height);

            // Utiliser WPF SystemParameters pour vérifier la visibilité
            var workArea = SystemParameters.WorkArea;
            var screenRect = new Rect(workArea.Left, workArea.Top, workArea.Width, workArea.Height);

            // Vérifier si au moins une partie de la fenêtre est visible
            return rect.IntersectsWith(screenRect);
        }
    }
}
