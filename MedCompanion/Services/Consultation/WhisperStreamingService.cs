using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using NAudio.MediaFoundation;
using NAudio.Wave;
using Whisper.net;
using Whisper.net.Ggml;

namespace MedCompanion.Services.Consultation
{
    public enum RecordingMode
    {
        /// <summary>Flush sur silence (réactif mais peut fragmenter sur débit lent).</summary>
        Streaming,
        /// <summary>Flush toutes les N secondes (qualité maximale, latence prévisible).</summary>
        Batch
    }

    /// <summary>
    /// Capture micro continue → transcription Whisper GPU → texte.
    ///
    /// Deux modes :
    ///  - Streaming : flush sur silence VAD (réactif)
    ///  - Batch : flush sur timer fixe (qualité max, recommandé pour consultations)
    ///
    /// Architecture :
    ///  - WhisperFactory/Processor créés UNE SEULE FOIS, gardés jusqu'à Dispose
    ///  - Audio passe par une Channel zéro-perte
    ///  - WaveIn disposé proprement à chaque Stop
    ///  - Audio + transcription sauvegardés (debug) — désactivable via SaveAudioEnabled
    /// </summary>
    public class WhisperStreamingService : IDisposable
    {
        // ── À METTRE À FALSE QUAND TOUT EST STABLE ─────────────────────────────
        public const bool SaveAudioEnabled = true;
        // ── Config ────────────────────────────────────────────────────────────
        // Pour une consultation réelle (pauses de réflexion, débit lent) :
        // on tolère 2.5s de silence avant de flusher → Whisper reçoit des chunks
        // longs avec contexte = meilleure qualité de transcription.
        private const int   SampleRate            = 16000;   // cible Whisper
        private const int   CaptureSampleRate     = 48000;   // format natif des micros USB → resample propre vers SampleRate
        private const int   Channels              = 1;
        private const float SilenceRmsThreshold   = 0.015f;  // VAD : seuil détection silence
        private const int   SilenceDurationMs     = 2500;    // tolère pauses naturelles de réflexion
        private const int   MaxBufferDurationMs   = 15000;   // flush forcé après 15s (chunks plus longs = meilleur contexte)
        private const int   MinWordsToTriggerLlm  = 50;
        private const int   MinAudioDurationMs    = 500;     // capturer aussi les courtes réponses ("oui", "non")
        private const float SegmentRmsThreshold   = 0.020f;  // anti-hallucination : RMS segment

        // Prompt neutre de base + vocabulaire custom injecté dynamiquement
        private const string BasePrompt =
            "Conversation médicale entre un médecin et une famille en français. ";

        // Vocabulaire custom (chargé depuis whisper_vocab_custom.txt)
        private readonly WhisperVocabService _vocabService = new();
        private string _dynamicPrompt = BasePrompt;

        /// <summary>Service de vocabulaire personnalisé (accès UI/Settings)</summary>
        public WhisperVocabService VocabService => _vocabService;

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
        private string?           _modelPath;   // chemin du modèle, pour recréer le factory lors d'un reset complet
        private bool _whisperInitialized;
        private readonly SemaphoreSlim _initLock = new(1, 1);

        // ── Mode (configurable avant Start) ───────────────────────────────────
        public RecordingMode Mode                 { get; set; } = RecordingMode.Batch;
        public int           BatchDurationSeconds { get; set; } = 90;

        // ── Capture audio (recréés à chaque Start/Stop) ───────────────────────
        private WaveInEvent? _waveIn;
        // Resampling propre 48 kHz → 16 kHz (filtre anti-aliasing via MediaFoundationResampler)
#pragma warning disable CS0414
        private BufferedWaveProvider? _captureBuffer;
#pragma warning restore CS0414
        private MediaFoundationResampler? _resampler;
        private byte[] _resampleScratch = new byte[64 * 1024];   // buffer de lecture pour MFR
        private CancellationTokenSource? _cts;
        private Channel<float[]>? _audioQueue;
        private Task? _transcriptionTask;
        private Task? _heartbeatTask;
        private Task? _batchTimerTask;
        private AudioRecorder? _audioRecorder;
        private DateTime _startedAt;
        private DateTime _batchCycleStartedAt;   // début du chunk batch courant
        private int      _totalSamplesProcessed;
        private int      _totalChunksTranscribed;
        private float    _currentAudioRms;       // niveau audio instantané
        private DateTime _lastTranscriptionAt;
        private DateTime _lastAudioAt;
        private string   _logPath = "";

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

        /// <summary>Niveau micro instantané (0.0 = silence, 1.0 = max). Fréquence : 1s.</summary>
        public event Action<float>? AudioLevelChanged;

        /// <summary>Progression du chunk batch courant (0-100 %). Fréquence : 1s.</summary>
        public event Action<int>? BatchProgressChanged;

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

                // Charger le vocabulaire personnalisé
                _vocabService.Load();
                var vocabFragment = _vocabService.BuildPromptFragment();
                _dynamicPrompt = string.IsNullOrWhiteSpace(vocabFragment)
                    ? BasePrompt
                    : BasePrompt + vocabFragment;
                System.Diagnostics.Debug.WriteLine(
                    $"[Whisper] Prompt dynamique : {_vocabService.Count} termes custom chargés.");

                _modelPath = modelManager.ModelPath;
                _factory   = WhisperFactory.FromPath(modelManager.ModelPath);
                _processor = _factory.CreateBuilder()
                                     .WithLanguage("fr")
                                     // PAS de .WithSingleSegment() : avec des chunks Batch de 90s,
                                     // Whisper doit pouvoir émettre plusieurs segments (un par fenêtre native de 30s).
                                     // Sinon seul le dernier segment est conservé → on perd le début du chunk.
                                     .WithNoContext()           // ← clé : pas de propagation entre chunks
                                     .WithPrompt(_dynamicPrompt)
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
                if (WaveInEvent.DeviceCount == 0)
                {
                    IsActive = false;
                    StatusChanged?.Invoke("✗ Aucun microphone détecté. Branchez un micro et réessayez.");
                    return;
                }

                _waveIn = new WaveInEvent
                {
                    WaveFormat         = new WaveFormat(SampleRate, 16, Channels),
                    BufferMilliseconds = 250,
                    NumberOfBuffers    = 4
                };
                _waveIn.DataAvailable    += OnAudioData;
                _waveIn.RecordingStopped += OnRecordingStoppedUnexpectedly;
                _captureBuffer = null;
                _resampler = null;

                try
                {
                    _waveIn.StartRecording();
                }
                catch (Exception ex)
                {
                    IsActive = false;
                    StatusChanged?.Invoke($"✗ Erreur microphone : {ex.Message}. Vérifiez les autorisations dans Paramètres → Confidentialité → Microphone.");
                    _waveIn.Dispose();
                    _waveIn = null;
                    return;
                }

                _startedAt              = DateTime.Now;
                _batchCycleStartedAt    = DateTime.Now;
                _totalSamplesProcessed  = 0;
                _totalChunksTranscribed = 0;
                _currentAudioRms        = 0f;
                _lastTranscriptionAt    = DateTime.Now;
                _lastAudioAt            = DateTime.Now;
                _heartbeatTask          = Task.Run(() => HeartbeatLoopAsync(_cts.Token));

                if (SaveAudioEnabled)
                    _audioRecorder = new AudioRecorder(SampleRate, Channels);

                // En mode Batch : timer qui flush toutes les N secondes
                if (Mode == RecordingMode.Batch)
                    _batchTimerTask = Task.Run(() => BatchTimerLoopAsync(_cts.Token));

                InitLogFile();
                Log($"Session démarrée — mode={Mode}, batch={BatchDurationSeconds}s, save={SaveAudioEnabled}");
                if (_audioRecorder != null) Log($"Audio sauvegardé dans : {_audioRecorder.SessionFolder}");

                IsActive = true;
                var modeMsg = Mode == RecordingMode.Batch
                    ? $"● Capture (batch {BatchDurationSeconds}s)..."
                    : "● Enregistrement (streaming)...";
                StatusChanged?.Invoke(modeMsg);
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
            Log("StopAsync appelé");
            IsActive = false;

            StatusChanged?.Invoke("Arrêt en cours...");

            // 1) Arrêter la capture audio (plus aucun OnAudioData après ça)
            if (_waveIn != null)
            {
                _waveIn.DataAvailable    -= OnAudioData;
                _waveIn.RecordingStopped -= OnRecordingStoppedUnexpectedly;
                try { _waveIn.StopRecording(); } catch { }
                _waveIn.Dispose();
                _waveIn = null;
            }

            // 1bis) Disposer du resampler et du buffer de capture
            try { _resampler?.Dispose(); } catch { }
            _resampler = null;
            _captureBuffer = null;

            // 2) Flush le buffer restant dans la queue AVANT d'annuler le token
            //    Important: le token n'est PAS encore annulé ici, donc TranscriptionLoop
            //    peut encore lire et traiter ce dernier chunk.
            FlushBufferToQueue(force: true);

            // 3) Fermer la queue — TranscriptionLoop se termine proprement
            //    après avoir traité tous les éléments restants.
            _audioQueue?.Writer.Complete();

            // 4) Attendre que la transcription du dernier segment soit terminée
            //    AVANT d'annuler le token (sinon ReadAllAsync(ct) peut s'arrêter prématurément)
            if (_transcriptionTask != null)
            {
                StatusChanged?.Invoke("⏳ Transcription du dernier segment...");
                try { await _transcriptionTask; }
                catch { /* ignoré */ }
                _transcriptionTask = null;
            }

            // 5) Maintenant annuler le token → arrête heartbeat + batch timer
            _cts?.Cancel();

            if (_heartbeatTask != null)
            {
                try { await _heartbeatTask; }
                catch { /* ignoré */ }
                _heartbeatTask = null;
            }

            if (_batchTimerTask != null)
            {
                try { await _batchTimerTask; }
                catch { /* ignoré */ }
                _batchTimerTask = null;
            }

            _audioRecorder?.Dispose();
            _audioRecorder = null;

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
            var exMsg = e.Exception?.Message ?? "(pas d'exception)";
            Log($"NAudio RecordingStopped : exception={exMsg}, IsActive={IsActive}");

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
        /// Heartbeat toutes les 2s : indicateurs détaillés pour diagnostic temps réel.
        ///   • timer session
        ///   • compteur segments
        ///   • niveau audio instantané (barre 0-8 blocs)
        ///   • temps depuis dernière transcription
        ///   • profondeur de la queue
        /// </summary>
        private async Task HeartbeatLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(1_000, ct);
                    if (!IsActive) return;

                    var elapsed     = DateTime.Now - _startedAt;
                    var sinceTransc = (DateTime.Now - _lastTranscriptionAt).TotalSeconds;
                    var sinceAudio  = (DateTime.Now - _lastAudioAt).TotalSeconds;
                    var transcMark  = Volatile.Read(ref _isTranscribing) == 1 ? " ✎" : "";

                    // Émettre niveau micro (0-1) et progression batch (0-100%)
                    AudioLevelChanged?.Invoke(_currentAudioRms);

                    int batchPct = 0;
                    if (Mode == RecordingMode.Batch && BatchDurationSeconds > 0)
                    {
                        var cycleElapsed = (DateTime.Now - _batchCycleStartedAt).TotalSeconds;
                        batchPct = Math.Min(100, (int)(cycleElapsed / BatchDurationSeconds * 100));
                    }
                    BatchProgressChanged?.Invoke(batchPct);

                    // Alerte si plus d'audio depuis 3s OU plus de transcription depuis 30s
                    string alert = "";
                    if (sinceAudio > 3.0)        alert = "  ⚠ MICRO MUET";
                    else if (sinceTransc > 30.0) alert = $"  ⚠ pas de transcription depuis {sinceTransc:F0}s";

                    StatusChanged?.Invoke(
                        $"● {elapsed:mm\\:ss} • {_totalChunksTranscribed} seg{transcMark}{alert}");
                }
            }
            catch (OperationCanceledException) { /* normal */ }
            catch (Exception ex)
            {
                Log($"Heartbeat CRASH : {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static string BuildAudioBar(float rms)
        {
            // RMS typiquement 0 à 0.1, normalise sur 8 blocs
            int level = Math.Min(8, (int)(rms * 80));
            return new string('▮', level) + new string('░', 8 - level);
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
            _currentAudioRms = rms;
            _lastAudioAt     = DateTime.Now;

            // En mode Batch : on accumule SANS flush automatique, le timer s'en occupe
            if (Mode == RecordingMode.Batch)
            {
                lock (_bufferLock)
                {
                    _audioBuffer.AddRange(samples);
                    _bufferMs += chunkMs;
                }
                return;
            }

            // Mode Streaming : flush sur silence VAD ou max buffer
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
        /// Mode Batch : flush du buffer toutes les BatchDurationSeconds.
        /// </summary>
        private async Task BatchTimerLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    await Task.Delay(BatchDurationSeconds * 1000, ct);
                    if (!IsActive) return;

                    Log($"Batch timer : flush {BatchDurationSeconds}s");
                    FlushBufferToQueue(force: true);
                }
            }
            catch (OperationCanceledException) { /* normal */ }
            catch (Exception ex)
            {
                Log($"BatchTimer CRASH : {ex.GetType().Name}: {ex.Message}");
            }
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

            var durationMs = (int)(samples.Length * 1000.0 / SampleRate);
            var rms        = ComputeRms(samples);

            // En mode Streaming : filtrer les chunks trop courts/silencieux (anti-hallucination)
            // En mode Batch : on envoie toujours, le contexte de 90s permet à Whisper de bien gérer
            if (Mode == RecordingMode.Streaming)
            {
                if (durationMs < MinAudioDurationMs)
                {
                    Log($"Chunk REJETÉ (trop court : {durationMs}ms < {MinAudioDurationMs}ms)");
                    return;
                }
                if (rms < SegmentRmsThreshold)
                {
                    Log($"Chunk REJETÉ (trop silencieux : RMS={rms:F4} < {SegmentRmsThreshold})");
                    return;
                }
            }
            else
            {
                Log($"Chunk batch envoyé : {durationMs / 1000.0:F1}s, RMS={rms:F4}");
                _batchCycleStartedAt = DateTime.Now; // repart à zéro pour le prochain chunk
            }

            _audioQueue?.Writer.TryWrite(samples);
        }

        // ── Boucle de transcription (consommateur de la queue) ────────────────

        private int _isTranscribing = 0; // 0 = idle, 1 = en cours

        private async Task TranscriptionLoopAsync(ChannelReader<float[]> reader, CancellationToken ct)
        {
            try
            {
                await foreach (var rawSamples in reader.ReadAllAsync(ct))
                {
                    if (_processor == null) { Log("Processor null, skip"); continue; }

                    // Audio brut directement vers Whisper — le pipeline NS/AGC dégradait la qualité.
                    var samples = rawSamples;

                    var durationS = samples.Length / (double)SampleRate;
                    Log($"Transcription chunk {durationS:F1}s (raw)...");
                    var sw = System.Diagnostics.Stopwatch.StartNew();

                    try
                    {
                        Interlocked.Exchange(ref _isTranscribing, 1);

                        // Sauvegarde audio brut tel que reçu du micro
                        _audioRecorder?.SaveChunk(samples);

                        var text = await TranscribeAsync(samples);
                        var rawText = text;
                        text = FilterHallucinations(text);

                        Interlocked.Increment(ref _totalChunksTranscribed);
                        _lastTranscriptionAt = DateTime.Now;

                        // Sauvegarde la transcription juste après le .wav du même chunk
                        _audioRecorder?.SaveTranscription(text);

                        Log($"  → {sw.ElapsedMilliseconds}ms, brut: \"{Truncate(rawText, 60)}\", filtré: \"{Truncate(text, 60)}\"");

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
                                Log($"  → Segment LLM envoyé ({CountWords(segment)} mots)");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"  ✗ EXCEPTION transcription : {ex.GetType().Name}: {ex.Message}");
                        StatusChanged?.Invoke($"✗ Erreur transcription : {ex.Message}");
                    }
                    finally
                    {
                        Interlocked.Exchange(ref _isTranscribing, 0);
                    }
                }
                Log("TranscriptionLoop terminée normalement");
            }
            catch (OperationCanceledException)
            {
                Log("TranscriptionLoop annulée");
            }
            catch (Exception ex)
            {
                Log($"TranscriptionLoop CRASH : {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
                StatusChanged?.Invoke($"✗ Boucle transcription crashée : {ex.Message}");
            }
        }

        private static string Truncate(string s, int len) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= len ? s : s.Substring(0, len) + "…");

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

        // ── Logging fichier (post-mortem) ─────────────────────────────────────

        private void InitLogFile()
        {
            try
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MedCompanion", "logs");
                Directory.CreateDirectory(dir);
                _logPath = Path.Combine(dir, $"whisper_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            }
            catch { _logPath = ""; }
        }

        private void Log(string message)
        {
            if (string.IsNullOrEmpty(_logPath)) return;
            try
            {
                File.AppendAllText(_logPath,
                    $"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
            }
            catch { /* ignoré */ }
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

        // ── Reset engine (anti-saturation Whisper) ────────────────────────────

        /// <summary>
        /// Réinitialise le moteur Whisper. Deux niveaux :
        ///
        ///  • <paramref name="full"/> = false (défaut) : recrée uniquement le <c>_processor</c>
        ///    (KV cache décodeur, résidus de contexte). Le factory/modèle reste en VRAM.
        ///    Coût ≈ 200-500 ms. Suffisant contre la contamination de contexte entre chunks.
        ///
        ///  • <paramref name="full"/> = true : dispose AUSSI le <c>_factory</c> et recharge le
        ///    modèle depuis le disque. Seul moyen de réellement défragmenter la VRAM et de
        ///    réinitialiser le contexte CUDA natif — corrige la dégradation progressive de
        ///    qualité observée après plusieurs sessions (typiquement à partir de la 4ᵉ).
        ///    Coût ≈ 1-2 s (rechargement modèle). À appeler au changement de patient.
        ///
        /// No-op si Whisper n'a jamais été initialisé OU si une session est active.
        /// </summary>
        /// <summary>
        /// Décharge complètement le modèle chargé (factory + processor) et remet le flag
        /// d'initialisation à false, sans recharger. Utilisé pour changer de taille de modèle :
        /// le prochain StartAsync chargera le nouveau modèle depuis zéro.
        /// </summary>
        public async Task UnloadModelAsync()
        {
            if (!_whisperInitialized) return;
            if (IsActive) await StopAsync();
            await _initLock.WaitAsync();
            try
            {
                try { _processor?.Dispose(); } catch { }
                _processor = null;
                try { _factory?.Dispose(); } catch { }
                _factory = null;
                _modelPath = null;
                _whisperInitialized = false;
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
            finally { _initLock.Release(); }
        }

        public async Task<(bool ok, string message)> ResetEngineAsync(bool full = false)
        {
            if (!_whisperInitialized) return (true, "Whisper non initialisé — rien à réinitialiser.");
            if (IsActive) return (false, "Enregistrement en cours — arrêtez d'abord la transcription.");

            await _initLock.WaitAsync();
            try
            {
                Log($"ResetEngineAsync (full={full}) : disposal {(full ? "factory + processor" : "processor")} + recréation");
                StatusChanged?.Invoke(full ? "🔄 Réinitialisation complète Whisper..." : "🔄 Réinitialisation Whisper...");

                try { _processor?.Dispose(); } catch { /* ignoré */ }
                _processor = null;

                if (full)
                {
                    // Détruire le factory libère le modèle ggml + le contexte CUDA natif → défragmente la VRAM.
                    try { _factory?.Dispose(); } catch { /* ignoré */ }
                    _factory = null;
                }

                // Force la libération de la mémoire native (KV cache décodeur, VRAM, etc.)
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                if (full)
                {
                    // Recharger le modèle depuis le disque (le factory venait d'être détruit).
                    if (string.IsNullOrEmpty(_modelPath) || !File.Exists(_modelPath))
                    {
                        _whisperInitialized = false;
                        return (false, "Chemin du modèle introuvable — réinitialisation impossible (relancez l'app).");
                    }
                    _factory = WhisperFactory.FromPath(_modelPath);
                }

                if (_factory == null)
                {
                    _whisperInitialized = false;
                    return (false, "Factory Whisper nulle — réinitialisation impossible (relancez l'app).");
                }

                _processor = _factory.CreateBuilder()
                                     .WithLanguage("fr")
                                     .WithNoContext()
                                     .WithPrompt(_dynamicPrompt)
                                     .WithTemperature(0f)
                                     .Build();
                StatusChanged?.Invoke(full ? "✓ Whisper réinitialisé (complet)." : "✓ Whisper réinitialisé.");
                return (true, full ? "Whisper réinitialisé (rechargement complet)." : "Whisper réinitialisé.");
            }
            catch (Exception ex)
            {
                Log($"ResetEngineAsync échec : {ex.Message}");
                return (false, $"Réinitialisation échouée : {ex.Message}");
            }
            finally
            {
                _initLock.Release();
            }
        }

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
