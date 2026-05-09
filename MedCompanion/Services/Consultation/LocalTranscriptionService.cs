using System;
using System.Threading;
using System.Threading.Tasks;
using MedCompanion.Services.Voice;

namespace MedCompanion.Services.Consultation
{
    /// <summary>
    /// Enregistrement Handy par chunks automatiques.
    /// Chaque ChunkSeconds, Handy s'arrête (~4s de coupure pour transcrire),
    /// puis reprend. Un indicateur visuel signale la coupure.
    /// </summary>
    public class HandyChunkedRecordingService : IDisposable
    {
        public const int DefaultChunkSeconds = 180; // 3 minutes

        private readonly HandyVoiceInputService _handy;
        private Timer?   _cycleTimer;
        private bool     _isActive;
        private int      _chunkIndex;

        public bool IsActive => _isActive;

        /// <summary>Coupure commencée : Handy s'arrête, transcrit, va retaper dans le TextBox.</summary>
        public event Action? CutoverStarted;

        /// <summary>Coupure terminée : Handy a repris l'enregistrement.</summary>
        public event Action? CutoverEnded;

        /// <summary>Le focus doit être remis sur le TextBox de transcription avant que Handy tape.</summary>
        public event Func<Task>? FocusRequired;

        /// <summary>Messages courts pour l'UI (status bar).</summary>
        public event Action<string>? StatusChanged;

        public HandyChunkedRecordingService(string hotkey = "Ctrl+Space")
        {
            _handy = new HandyVoiceInputService { Hotkey = hotkey };
        }

        // ── Démarrage ────────────────────────────────────────────────────────

        public async Task StartAsync(int chunkSeconds = DefaultChunkSeconds)
        {
            if (_isActive) return;

            _isActive    = true;
            _chunkIndex  = 0;

            await _handy.StartRecordingAsync();

            _cycleTimer = new Timer(
                _ => _ = CycleAsync(),
                null,
                TimeSpan.FromSeconds(chunkSeconds),
                TimeSpan.FromSeconds(chunkSeconds));

            StatusChanged?.Invoke("● Enregistrement...");
        }

        // ── Arrêt manuel ─────────────────────────────────────────────────────

        public async Task StopAsync()
        {
            if (!_isActive) return;
            _isActive = false;

            _cycleTimer?.Dispose();
            _cycleTimer = null;

            if (_handy.IsRecording)
            {
                // Focus AVANT stop : Handy tape le texte dans notre TextBox
                if (FocusRequired != null) await FocusRequired.Invoke();
                await Task.Delay(150);
                await _handy.StopRecordingAsync();
                // Laisser Handy finir de taper
                await Task.Delay(3000);
            }

            StatusChanged?.Invoke("Enregistrement arrêté.");
        }

        // ── Cycle automatique ─────────────────────────────────────────────────

        private async Task CycleAsync()
        {
            if (!_isActive) return;

            _chunkIndex++;
            CutoverStarted?.Invoke();
            StatusChanged?.Invoke($"⚠ Coupure {_chunkIndex} — pause ~5s");

            // Focus AVANT stop : Handy tape le texte dans notre TextBox
            if (FocusRequired != null) await FocusRequired.Invoke();
            await Task.Delay(150);

            // Arrêter Handy → Handy transcrit et tape dans le TextBox focusé
            await _handy.StopRecordingAsync();

            // Laisser Handy finir de taper
            await Task.Delay(5000);

            if (!_isActive) return;

            // Focus avant restart
            if (FocusRequired != null) await FocusRequired.Invoke();
            await Task.Delay(150);

            // Reprendre l'enregistrement
            await _handy.StartRecordingAsync();

            CutoverEnded?.Invoke();
            StatusChanged?.Invoke("● Enregistrement...");
        }

        public void Dispose()
        {
            _cycleTimer?.Dispose();
        }
    }
}
