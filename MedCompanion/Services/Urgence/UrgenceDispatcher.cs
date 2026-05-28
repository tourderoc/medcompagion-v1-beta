using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MedCompanion.Models.Urgences;

namespace MedCompanion.Services.Urgence
{
    /// <summary>
    /// Orchestre l'analyse d'urgence après chaque sauvegarde de note.
    /// Lance tous les détecteurs enregistrés en parallèle, persiste les signaux émis,
    /// et notifie l'UI via SignalDetected.
    /// </summary>
    public class UrgenceDispatcher
    {
        private readonly List<IUrgenceDetector> _detectors = new();
        private readonly UrgenceLogService _logService;

        public UrgenceDispatcher(UrgenceLogService logService)
        {
            _logService = logService;
        }

        /// <summary>Enregistre un détecteur. À appeler une fois au démarrage de l'app.</summary>
        public void Register(IUrgenceDetector detector)
        {
            if (detector == null) return;
            _detectors.Add(detector);
        }

        /// <summary>
        /// Émis pour chaque signal détecté ET persisté. L'UI s'abonne pour afficher le chip.
        /// </summary>
        public event Action<UrgenceSignal>? SignalDetected;

        /// <summary>
        /// Lance tous les détecteurs sur la note. Aucun throw remonté à l'appelant —
        /// un détecteur en échec ne doit jamais bloquer la sauvegarde de la note.
        /// </summary>
        public async Task AnalyzeAsync(UrgenceNoteContext context, CancellationToken ct = default)
        {
            if (_detectors.Count == 0) return;
            if (string.IsNullOrWhiteSpace(context.NoteContent)) return;

            var tasks = _detectors.Select(d => SafeDetectAsync(d, context, ct));
            var results = await Task.WhenAll(tasks);

            foreach (var signal in results.Where(s => s != null))
            {
                try
                {
                    _logService.WriteSignal(signal!);
                    SignalDetected?.Invoke(signal!);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[UrgenceDispatcher] Erreur persistance signal : {ex.Message}");
                }
            }
        }

        private static async Task<UrgenceSignal?> SafeDetectAsync(IUrgenceDetector detector, UrgenceNoteContext ctx, CancellationToken ct)
        {
            try
            {
                return await detector.DetectAsync(ctx, ct);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UrgenceDispatcher] Détecteur '{detector.Name}' a échoué : {ex.Message}");
                return null;
            }
        }
    }
}
