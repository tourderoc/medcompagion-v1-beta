using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NAudio.Wave;
using Whisper.net;
using Whisper.net.Ggml;

namespace MedCompanion.Services.Consultation
{
    /// <summary>
    /// Capture micro continue → transcription Whisper GPU → texte temps réel.
    ///
    /// Architecture :
    ///  - WhisperFactory/Processor créés UNE SEULE FOIS (au 1er Start), gardés jusqu'à Dispose.
    ///  - Chaque Start réinitialise l'état de session (buffers, accumulateurs).
    ///  - Audio passe par une Channel : aucune perte si la transcription est en cours.
    ///  - WaveIn disposé proprement à chaque Stop.
    /// </summary>
    public class WhisperStreamingService : IDisposable
    {
        // ── Config ────────────────────────────────────────────────────────────
        private const int   SampleRate            = 16000;
        private const int   Channels              = 1;
        private const float SilenceRmsThreshold   = 0.015f;  // VAD : seuil détection silence
        private const int   SilenceDurationMs     = 1000;    // silence → flush (équilibre réactivité/qualité)
        private const int   MaxBufferDurationMs   = 10000;   // flush forcé après 10s
        private const int   MinWordsToTriggerLlm  = 50;
        private const int   MinAudioDurationMs    = 1200;    // anti-hallucination : chunks min 1.2s
        private const float SegmentRmsThreshold   = 0.020f;  // anti-hallucination : RMS segment plus strict

        // Prompt neutre : évite les mots-clés qui déclenchent des associations
        // problématiques dans le modèle (ex: "pédopsychiatrie" → "pédophilie")
        private const string InitialPrompt =
            "Conversation médicale entre un médecin et une famille en français.";

        private static readonly string[] KnownHallucinations =
        {
            // Sous-titres YouTube (propagation depuis dataset d'entraînement)
            "sous-titres réalisés par la communauté d'amara.org",
            "sous-titrage par lepenois-malinois",
            "sous-titrage par lepinois-malinois",
            "sous-titres par la communauté d'amara",
            "sous-titres réalisés par",
            "sous-titrage",
            "sous-titres",
            // Outros vidéo
            "merci d'avoir regardé cette vidéo",
            "merci d'avoir regardé",
            "n'oubliez pas de vous abonner",
            "abonnez-vous",
            "à la prochaine",
            "à très bientôt",
            // Marqueurs audio
            "♪",
            "[musique]",
            "voix off",
            "[applaudissements]",
            "[rires]",
            // Patterns dangereux à filtrer absolument (associations malsaines)
            "bienvenue dans le monde de la pédophilie",
            "monde de la pédophilie",
            "pédophilie",
            "pornographie",
        };

        // ── Whisper (créés une seule fois) ────────────────────────────────────
        private WhisperFactory?   _factory;
        private WhisperProcessor? _processor;
        private bool _whisperInitialized;
        private readonly SemaphoreSlim _initLock = new(1, 1);

        // ── Capture audio (recréés à chaque Start/Stop) ───────────────────────
        private WaveInEvent? _waveIn;
        private CancellationTokenSource? _cts;
        private Channel<float[]>? _audioQueue;
        private Task? _transcriptionTask;
        private Task? _heartbeatTask;
        private DateTime _startedAt;
        private int      _totalSamplesProcessed;
        private int      _totalChunksTranscribed;

        // ── Session state (reset à chaque Start) ──────────────────────────────
        private readonly List<float> _audioBuffer  = new();
        private readonly object      _bufferLock   = new();
        private int    _silentMs            = 0;
        private int    _bufferMs            = 0;
        private int    _newWordCount        = 0;
        private string _segmentAccumulator  = "";

        private bool _isDisposed;

        public bool IsActive { get; private set; }

        // ── Events ────────────────────────────────────────────────────────────

        /// <summary>Texte transcrit à appender dans le TextBox.</summary>
        public event Action<string>? TextAppended;

        /// <summary>Segment prêt pour extraction LLM (≥ MinWordsToTriggerLlm mots nouveaux).</summary>
        public event Action<string>? SegmentReady;

        /// <summary>Messages status pour l'UI.</summary>
        public event Action<string>? StatusChanged;

        /// <summary>Demande au ViewModel de réinitialiser la session (transcription, blocs).</summary>
        public event Action? SessionResetRequested;

        // ── Initialisation Whisper (lazy, une seule fois) ─────────────────────

        private async Task EnsureWhisperInitializedAsync(WhisperModelManager modelManager)
        {
            if (_whisperInitialized) return;

            await _initLock.WaitAsync();
            try
            {
                if (_whisperInitialized) return;

                EnsureCudaInPath();

                StatusChanged?.Invoke("Téléchargement modèle Whisper...");
                var progress = new Progress<int>(pct =>
                    StatusChanged?.Invoke($"Téléchargement Whisper : {pct}%"));
                await modelManager.EnsureModelAsync(progress);

                StatusChanged?.Invoke("Chargement modèle GPU...");
                _factory   = WhisperFactory.FromPath(modelManager.ModelPath);
                _processor = _factory.CreateBuilder()
                                     .WithLanguage("fr")
                                     .WithSingleSegment()
                                     .WithNoContext()           // ← clé : pas de propagation entre chunks
                                     .WithPrompt(InitialPrompt)
                                     .WithTemperature(0f)
                                     .Build();

                _whisperInitialized = true;
            }
            finally
            {
                _initLock.Release();
            }
        }

        // ── Démarrage ─────────────────────────────────────────────────────────

        public async Task StartAsync(WhisperModelManager modelManager)
        {
            if (IsActive) return;

            try
            {
                await EnsureWhisperInitializedAsync(modelManager);

                // Reset complet de la session
                ResetSessionState();
                SessionResetRequested?.Invoke();

                // Queue + tâche de transcription en arrière-plan
                _cts            = new CancellationTokenSource();
                _audioQueue     = Channel.CreateUnbounded<float[]>(new UnboundedChannelOptions
                {
                    SingleReader = true,
                    SingleWriter = false
                });
                _transcriptionTask = Task.Run(() =>
                    TranscriptionLoopAsync(_audioQueue.Reader, _cts.Token));

                // Capture micro
                _waveIn = new WaveInEvent
                {
                    WaveFormat         = new WaveFormat(SampleRate, 16, Channels),
                    BufferMilliseconds = 250,
                    NumberOfBuffers    = 4
                };
                _waveIn.DataAvailable    += OnAudioData;
                _waveIn.RecordingStopped += OnRecordingStoppedUnexpectedly;
                _waveIn.StartRecording();

                _startedAt              = DateTime.Now;
                _totalSamplesProcessed  = 0;
                _totalChunksTranscribed = 0;
                _heartbeatTask          = Task.Run(() => HeartbeatLoopAsync(_cts.Token));

                IsActive = true;
                StatusChanged?.Invoke("● Enregistrement...");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"✗ Erreur Whisper : {ex.Message}");
                IsActive = false;
                await CleanupCaptureAsync();
            }
        }

        // ── Arrêt ─────────────────────────────────────────────────────────────

        public async Task StopAsync()
        {
            if (!IsActive) return;
            IsActive = false;

            StatusChanged?.Invoke("Arrêt en cours...");

            // 1) Arrêter la capture audio
            if (_waveIn != null)
            {
                _waveIn.DataAvailable    -= OnAudioData;
                _waveIn.RecordingStopped -= OnRecordingStoppedUnexpectedly;
                try { _waveIn.StopRecording(); } catch { }
                _waveIn.Dispose();
                _waveIn = null;
            }

            // 2) Annuler le heartbeat
            _cts?.Cancel();

            // 3) Vider le buffer restant dans la queue
            FlushBufferToQueue(force: true);

            // 4) Fermer la queue → la tâche de transcription va se terminer après avoir tout traité
            _audioQueue?.Writer.Complete();

            if (_transcriptionTask != null)
            {
                try { await _transcriptionTask; }
                catch { /* ignoré */ }
                _transcriptionTask = null;
            }

            if (_heartbeatTask != null)
            {
                try { await _heartbeatTask; }
                catch { /* ignoré */ }
                _heartbeatTask = null;
            }

            _cts?.Dispose();
            _cts = null;
            _audioQueue = null;

            StatusChanged?.Invoke("Enregistrement arrêté.");
        }

        /// <summary>
        /// Appelé si NAudio s'arrête de manière inattendue (erreur driver, périphérique débranché…).
        /// On essaie de redémarrer automatiquement.
        /// </summary>
        private async void OnRecordingStoppedUnexpectedly(object? sender, StoppedEventArgs e)
        {
            if (!IsActive) return; // Stop volontaire, pas une erreur

            var msg = e.Exception?.Message ?? "raison inconnue";
            StatusChanged?.Invoke($"⚠ Capture audio interrompue ({msg}) — redémarrage...");

            try
            {
                await Task.Delay(500);
                if (!IsActive) return;

                // Recréer le WaveInEvent
                if (_waveIn != null)
                {
                    _waveIn.DataAvailable    -= OnAudioData;
                    _waveIn.RecordingStopped -= OnRecordingStoppedUnexpectedly;
                    _waveIn.Dispose();
                }

                _waveIn = new WaveInEvent
                {
                    WaveFormat         = new WaveFormat(SampleRate, 16, Channels),
                    BufferMilliseconds = 250,
                    NumberOfBuffers    = 4
                };
                _waveIn.DataAvailable    += OnAudioData;
                _waveIn.RecordingStopped += OnRecordingStoppedUnexpectedly;
                _waveIn.StartRecording();

                StatusChanged?.Invoke("● Enregistrement... (capture relancée)");
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke($"✗ Échec relance audio : {ex.Message}");
                IsActive = false;
            }
        }

        /// <summary>
        /// Heartbeat toutes les 2s : status stable, indicateur transcription en cours.
        /// Le statut reste TOUJOURS "● Enregistrement..." pour éviter le clignotement
        /// qui faisait croire à un arrêt.
        /// </summary>
        private async Task HeartbeatLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(2_000, ct);
                    if (!IsActive) return;

                    var elapsed     = DateTime.Now - _startedAt;
                    var transcMark  = Volatile.Read(ref _isTranscribing) == 1 ? " ✎" : "";
                    var queueDepth  = _audioQueue?.Reader.Count ?? 0;
                    var queueMark   = queueDepth > 0 ? $" ({queueDepth} en file)" : "";

                    StatusChanged?.Invoke(
                        $"● Enregistrement {elapsed:mm\\:ss} • " +
                        $"{_totalChunksTranscribed} segments{transcMark}{queueMark}");
                }
            }
            catch (OperationCanceledException) { /* normal */ }
        }

        private async Task CleanupCaptureAsync()
        {
            if (_waveIn != null)
            {
                try { _waveIn.StopRecording(); } catch { }
                _waveIn.Dispose();
                _waveIn = null;
            }
            _audioQueue?.Writer.TryComplete();
            if (_transcriptionTask != null)
            {
                try { await _transcriptionTask; } catch { }
                _transcriptionTask = null;
            }
            _cts?.Dispose();
            _cts = null;
            _audioQueue = null;
        }

        private void ResetSessionState()
        {
            lock (_bufferLock)
            {
                _audioBuffer.Clear();
                _silentMs = 0;
                _bufferMs = 0;
            }
            _segmentAccumulator = "";
            _newWordCount       = 0;
        }

        // ── Capture audio ─────────────────────────────────────────────────────

        private void OnAudioData(object? sender, WaveInEventArgs e)
        {
            if (!IsActive) return;

            var samples = ConvertToFloat(e.Buffer, e.BytesRecorded);
            var rms     = ComputeRms(samples);
            var chunkMs = (int)(samples.Length * 1000.0 / SampleRate);

            Interlocked.Add(ref _totalSamplesProcessed, samples.Length);

            bool shouldFlush;
            lock (_bufferLock)
            {
                _audioBuffer.AddRange(samples);
                _bufferMs += chunkMs;

                if (rms < SilenceRmsThreshold) _silentMs += chunkMs;
                else                            _silentMs = 0;

                shouldFlush = _silentMs >= SilenceDurationMs || _bufferMs >= MaxBufferDurationMs;
            }

            if (shouldFlush) FlushBufferToQueue(force: false);
        }

        /// <summary>
        /// Extrait l'audio du buffer et l'envoie dans la queue (jamais perdu).
        /// </summary>
        private void FlushBufferToQueue(bool force)
        {
            float[] samples;
            lock (_bufferLock)
            {
                if (_audioBuffer.Count == 0) return;
                if (!force && _silentMs < SilenceDurationMs && _bufferMs < MaxBufferDurationMs)
                    return;

                samples = _audioBuffer.ToArray();
                _audioBuffer.Clear();
                _silentMs = 0;
                _bufferMs = 0;
            }

            // Anti-hallucination : ignorer chunks trop courts ou silencieux
            var durationMs = (int)(samples.Length * 1000.0 / SampleRate);
            if (durationMs < MinAudioDurationMs) return;
            if (ComputeRms(samples) < SegmentRmsThreshold) return;

            _audioQueue?.Writer.TryWrite(samples);
        }

        // ── Boucle de transcription (consommateur de la queue) ────────────────

        private int _isTranscribing = 0; // 0 = idle, 1 = en cours

        private async Task TranscriptionLoopAsync(ChannelReader<float[]> reader, CancellationToken ct)
        {
            try
            {
                await foreach (var samples in reader.ReadAllAsync(ct))
                {
                    if (_processor == null) continue;

                    try
                    {
                        Interlocked.Exchange(ref _isTranscribing, 1);

                        var text = await TranscribeAsync(samples);
                        text = FilterHallucinations(text);

                        Interlocked.Increment(ref _totalChunksTranscribed);

                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            TextAppended?.Invoke(text + " ");

                            _segmentAccumulator += " " + text;
                            _newWordCount       += CountWords(text);

                            if (_newWordCount >= MinWordsToTriggerLlm)
                            {
                                var segment = _segmentAccumulator.Trim();
                                _segmentAccumulator = "";
                                _newWordCount       = 0;
                                SegmentReady?.Invoke(segment);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        StatusChanged?.Invoke($"✗ Erreur transcription : {ex.Message}");
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _isTranscribing, 0);
                    }
                }
            }
            catch (OperationCanceledException) { /* normal */ }
        }

        private async Task<string> TranscribeAsync(float[] samples)
        {
            if (_processor == null) return "";

            var results = new List<string>();
            await foreach (var segment in _processor.ProcessAsync(samples))
            {
                if (!string.IsNullOrWhiteSpace(segment.Text))
                    results.Add(segment.Text.Trim());
            }
            return string.Join(" ", results);
        }

        // ── CUDA PATH ─────────────────────────────────────────────────────────

        private static void EnsureCudaInPath()
        {
            var cudaCandidates = new[]
            {
                @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.1\bin\x64",
                @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.1\bin",
                @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.0\bin\x64",
                @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v13.0\bin",
                @"C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA\v12.0\bin",
            };

            var currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var candidate in cudaCandidates)
            {
                if (Directory.Exists(candidate) && !currentPath.Contains(candidate))
                {
                    Environment.SetEnvironmentVariable("PATH", candidate + ";" + currentPath);
                    currentPath = candidate + ";" + currentPath;
                }
            }
        }

        // ── Filtrage hallucinations ───────────────────────────────────────────

        private static string FilterHallucinations(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            var lines = text.Split('\n');
            var keep  = new List<string>();

            foreach (var line in lines)
            {
                var trimmed   = line.Trim();
                var lowerLine = trimmed.ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;

                bool isHallucination = false;
                foreach (var pattern in KnownHallucinations)
                {
                    if (lowerLine.Contains(pattern)) { isHallucination = true; break; }
                }
                if (isHallucination) continue;

                if (IsRepeatedShortPhrase(trimmed)) continue;

                keep.Add(trimmed);
            }

            return string.Join(" ", keep).Trim();
        }

        private static bool IsRepeatedShortPhrase(string text)
        {
            var parts = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(p => p.Trim().ToLowerInvariant())
                            .Where(p => p.Length > 0)
                            .ToList();
            if (parts.Count < 3) return false;
            var first = parts[0];
            if (first.Length > 15) return false;
            return parts.Count(p => p == first) >= 3;
        }

        // ── Utilitaires ───────────────────────────────────────────────────────

        private static float[] ConvertToFloat(byte[] buffer, int bytesRecorded)
        {
            var samples = new float[bytesRecorded / 2];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = BitConverter.ToInt16(buffer, i * 2) / 32768f;
            return samples;
        }

        private static float ComputeRms(float[] samples)
        {
            if (samples.Length == 0) return 0f;
            double sum = samples.Sum(s => (double)s * s);
            return (float)Math.Sqrt(sum / samples.Length);
        }

        private static int CountWords(string text) =>
            text.Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;

        // ── Dispose ───────────────────────────────────────────────────────────

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            try { StopAsync().GetAwaiter().GetResult(); } catch { }

            _processor?.Dispose();
            _factory?.Dispose();
            _initLock.Dispose();
        }
    }
}
