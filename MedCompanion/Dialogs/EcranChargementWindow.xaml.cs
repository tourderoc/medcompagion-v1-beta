using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Écran de démarrage de Med.
    ///
    /// Il tourne sur SON PROPRE fil, avec son propre Dispatcher. C'est indispensable : la
    /// construction de la fenêtre principale est longue et synchrone, et un écran posé sur le fil
    /// de l'interface resterait figé — exactement le défaut qu'il est censé masquer.
    ///
    /// Il sert à empêcher le médecin de cliquer dans une application encore à moitié prête
    /// (demandé le 26/09/2026), et il disparaît de lui-même quand tout est chargé.
    /// </summary>
    public partial class EcranChargementWindow : Window
    {
        public EcranChargementWindow() => InitializeComponent();

        private void Afficher(string message)
        {
            MessageText.Text = message;
        }

        // ── Pilotage depuis le fil principal ────────────────────────────────

        private static EcranChargementWindow? _fenetre;
        private static Thread? _fil;

        /// <summary>Ouvre l'écran sur un fil dédié et rend la main aussitôt.</summary>
        public static void Ouvrir()
        {
            if (_fil != null) return;

            var prete = new ManualResetEventSlim(false);

            _fil = new Thread(() =>
            {
                _fenetre = new EcranChargementWindow();
                _fenetre.Show();
                prete.Set();
                Dispatcher.Run();
            })
            {
                IsBackground = true,
                Name = "Écran de démarrage"
            };
            _fil.SetApartmentState(ApartmentState.STA);
            _fil.Start();

            // On attend que la fenêtre existe : sinon un Message() immédiat se perdrait.
            prete.Wait(TimeSpan.FromSeconds(5));
        }

        /// <summary>Change la ligne affichée. Sans effet si l'écran est déjà fermé.</summary>
        public static void Message(string texte)
        {
            var f = _fenetre;
            if (f == null) return;
            try { f.Dispatcher.BeginInvoke(new Action(() => f.Afficher(texte))); }
            catch { /* écran déjà fermé : sans conséquence */ }
        }

        /// <summary>Ferme l'écran et arrête son fil. Appelable plusieurs fois.</summary>
        public static void Fermer()
        {
            var f = _fenetre;
            _fenetre = null;
            _fil = null;
            if (f == null) return;

            try
            {
                f.Dispatcher.Invoke(() =>
                {
                    f.Close();
                    f.Dispatcher.InvokeShutdown();
                });
            }
            catch { /* déjà fermé */ }
        }
    }
}
