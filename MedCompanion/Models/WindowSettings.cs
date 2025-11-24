using System.Windows;

namespace MedCompanion.Models
{
    /// <summary>
    /// Modèle pour les paramètres d'affichage de la fenêtre
    /// </summary>
    public class WindowSettings
    {
        public string StartupPreference { get; set; } = "Remember";
        public WindowState LastWindowState { get; set; } = WindowState.Normal;
        public double LastLeft { get; set; }
        public double LastTop { get; set; }
        public double LastWidth { get; set; } = 1200;
        public double LastHeight { get; set; } = 800;
    }
}
