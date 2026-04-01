using System;
using System.Windows.Threading;

namespace MedCompanion.Services.Voice
{
    /// <summary>
    /// Détecteur de fin de parole basé sur l'inactivité du texte
    /// Déclenche un événement quand le texte n'a pas changé pendant un certain temps
    /// </summary>
    public class SilenceDetector
    {
        private readonly DispatcherTimer _timer;
        private string _lastText = string.Empty;
        private DateTime _lastChangeTime;
        private bool _isActive;

        /// <summary>
        /// Délai de silence en millisecondes avant de déclencher l'événement (défaut: 2000ms)
        /// </summary>
        public int SilenceDelayMs { get; set; } = 2000;

        /// <summary>
        /// Indique si le détecteur est actif
        /// </summary>
        public bool IsActive => _isActive;

        /// <summary>
        /// Événement déclenché quand un silence est détecté (fin de parole)
        /// </summary>
        public event EventHandler<string>? SilenceDetected;

        /// <summary>
        /// Événement déclenché quand du texte est en cours de saisie
        /// </summary>
        public event EventHandler? TextChanging;

        public SilenceDetector()
        {
            _timer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(100) // Vérification toutes les 100ms
            };
            _timer.Tick += Timer_Tick;
        }

        /// <summary>
        /// Démarre la détection de silence
        /// </summary>
        /// <param name="initialText">Texte initial dans le champ de saisie</param>
        public void Start(string initialText = "")
        {
            _lastText = initialText;
            _lastChangeTime = DateTime.Now;
            _isActive = true;
            _timer.Start();
        }

        /// <summary>
        /// Arrête la détection de silence
        /// </summary>
        public void Stop()
        {
            _timer.Stop();
            _isActive = false;
            _lastText = string.Empty;
        }

        /// <summary>
        /// Met à jour le texte surveillé (appeler à chaque changement du TextBox)
        /// </summary>
        public void UpdateText(string currentText)
        {
            if (!_isActive) return;

            if (currentText != _lastText)
            {
                _lastText = currentText;
                _lastChangeTime = DateTime.Now;
                TextChanging?.Invoke(this, EventArgs.Empty);
            }
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (!_isActive) return;

            // Vérifier si le texte n'a pas changé depuis le délai configuré
            var elapsed = (DateTime.Now - _lastChangeTime).TotalMilliseconds;

            if (elapsed >= SilenceDelayMs && !string.IsNullOrWhiteSpace(_lastText))
            {
                // Silence détecté avec du texte
                Stop();
                SilenceDetected?.Invoke(this, _lastText);
            }
        }

        /// <summary>
        /// Réinitialise le timer de détection
        /// </summary>
        public void Reset()
        {
            _lastChangeTime = DateTime.Now;
        }
    }
}
