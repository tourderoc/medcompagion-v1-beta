using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using MedCompanion.Models;
using Microsoft.Win32;

namespace MedCompanion.Services.Backup
{
    /// <summary>
    /// Déclenche les sauvegardes automatiques.
    ///
    /// Trois déclencheurs, qui couvrent ensemble la façon dont le poste est réellement utilisé :
    ///
    /// • <b>peu après le démarrage</b> de MedCompanion, avec un délai pour ne pas ralentir l'ouverture ;
    /// • <b>à intervalle régulier</b> — une sauvegarde ne coûte qu'une poignée de secondes, donc la
    ///   faire toutes les heures ne coûte rien et change la perte maximale d'une journée à une heure ;
    /// • <b>au réveil de la machine</b>. C'est le point important sur ce poste, laissé en veille :
    ///   pendant la veille le processus est gelé, aucun minuteur ne se déclenche. Sans cet
    ///   abonnement, la première sauvegarde du matin n'aurait lieu qu'une heure après l'arrivée.
    /// </summary>
    public sealed class BackupScheduler : IDisposable
    {
        private static readonly TimeSpan Battement = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan DelaiDemarrage = TimeSpan.FromSeconds(45);
        private static readonly TimeSpan DelaiReveil = TimeSpan.FromSeconds(30);

        private readonly BackupService _service = new();
        private readonly DispatcherTimer _battement;
        private DispatcherTimer? _differe;
        private bool _demarre;
        private bool _libere;

        /// <summary>Message court destiné à la barre d'état. Jamais une boîte de dialogue.</summary>
        public event EventHandler<string>? StatusChanged;

        public event EventHandler<BackupResult>? Completed;

        public BackupScheduler()
        {
            _battement = new DispatcherTimer { Interval = Battement };
            _battement.Tick += (_, _) => _ = VerifierEtLancerAsync();
        }

        public void Start()
        {
            if (_demarre) return;
            _demarre = true;

            _battement.Start();
            SystemEvents.PowerModeChanged += OnPowerModeChanged;

            var reglages = BackupSettings.Load();
            if (reglages.Enabled && reglages.RunAtStartup)
                Differer(DelaiDemarrage);
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Resume)
                Differer(DelaiReveil);
        }

        /// <summary>Programme une vérification unique après un délai, en remplaçant celle en attente.</summary>
        private void Differer(TimeSpan delai)
        {
            _differe?.Stop();
            _differe = new DispatcherTimer { Interval = delai };
            _differe.Tick += (emetteur, args) =>
            {
                ((DispatcherTimer)emetteur!).Stop();
                _differe = null;
                _ = VerifierEtLancerAsync();
            };
            _differe.Start();
        }

        /// <summary>
        /// Lance une sauvegarde si elle est due. Silencieuse par nature : elle ne doit jamais
        /// interrompre une consultation par une fenêtre qui s'ouvre.
        /// </summary>
        public async Task VerifierEtLancerAsync(bool forcer = false)
        {
            if (_libere) return;

            var reglages = BackupSettings.Load();
            if (!reglages.Enabled) return;
            if (!forcer && !reglages.IsDue(DateTime.UtcNow)) return;
            if (BackupService.EnCours) return;

            var (ok, erreur) = _service.VerifierDestination(reglages);
            if (!ok)
            {
                StatusChanged?.Invoke(this, $"Sauvegarde impossible : {erreur}");
                return;
            }

            StatusChanged?.Invoke(this, "Sauvegarde en cours…");

            var dernierMessage = DateTime.MinValue;
            var avancement = new Progress<BackupProgress>(p =>
            {
                // La barre d'état n'a pas besoin d'être rafraîchie plus d'une fois par seconde.
                if ((DateTime.Now - dernierMessage).TotalSeconds < 1) return;
                dernierMessage = DateTime.Now;

                if (p.Phase == BackupPhase.Copie && p.FilesTotal > 0)
                    StatusChanged?.Invoke(this, $"Sauvegarde… {p.FilesDone}/{p.FilesTotal}");
            });

            BackupResult resultat;
            try
            {
                resultat = await _service.RunAsync(reglages, avancement, CancellationToken.None)
                                        .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"Sauvegarde en erreur : {ex.Message}");
                return;
            }

            StatusChanged?.Invoke(this,
                resultat.Success ? $"Sauvegarde terminée — {resultat.Resume()}"
                                 : $"Sauvegarde échouée — {resultat.Error}");

            Completed?.Invoke(this, resultat);
        }

        public void Dispose()
        {
            if (_libere) return;
            _libere = true;

            _battement.Stop();
            _differe?.Stop();
            _differe = null;

            if (_demarre)
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
    }
}
