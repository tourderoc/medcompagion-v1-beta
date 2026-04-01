using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Media;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MedCompanion.Services.Voice
{
    /// <summary>
    /// Service de synthèse vocale utilisant Piper TTS
    /// Permet à Med de parler ses réponses à voix haute
    /// Refactorisé pour le streaming phrase par phrase (amélioration latence)
    /// </summary>
    public class PiperTTSService : IPiperTTSService, IDisposable
    {
        private bool _isSpeaking;
        private CancellationTokenSource? _cancellationTokenSource;

        // Flag pour demander l'arrêt immédiat
        private volatile bool _stopRequested;

        // Streaming queues
        private BlockingCollection<string>? _phraseQueue;
        private BlockingCollection<string>? _audioSegmentQueue;
        private Task? _producerTask;
        private Task? _consumerTask;
        private CancellationTokenSource? _streamCts;

        private Process? _piperProcess;

        // Chemins par défaut
        private string _piperExePath = string.Empty;
        private string _modelPath = string.Empty;
        private float _speed = 1.0f;

        // Dossier temporaire pour les fichiers audio
        private readonly string _tempFolder;

        public bool IsSpeaking => _isSpeaking;

        public bool IsAvailable
        {
            get
            {
                var (success, _) = CheckConfiguration();
                return success;
            }
        }

        public string PiperExePath
        {
            get => _piperExePath;
            set => _piperExePath = value ?? string.Empty;
        }

        public string ModelPath
        {
            get => _modelPath;
            set => _modelPath = value ?? string.Empty;
        }

        public float Speed
        {
            get => _speed;
            set => _speed = Math.Clamp(value, 0.5f, 2.0f);
        }

        public event EventHandler? SpeechStarted;
        public event EventHandler? SpeechCompleted;
        public event EventHandler<string>? SpeechError;

        public PiperTTSService()
        {
            // Créer un dossier temporaire pour les fichiers audio
            _tempFolder = Path.Combine(Path.GetTempPath(), "MedCompanion_TTS");
            if (!Directory.Exists(_tempFolder))
            {
                Directory.CreateDirectory(_tempFolder);
            }

            // Chemins par défaut (Documents/MedCompanion/piper/piper/)
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var defaultPiperFolder = Path.Combine(documentsPath, "MedCompanion", "piper", "piper");

            _piperExePath = Path.Combine(defaultPiperFolder, "piper.exe");
            _modelPath = Path.Combine(defaultPiperFolder, "models", "fr_FR-tom-medium.onnx");
        }

        /// <summary>
        /// Vérifie que Piper est correctement configuré
        /// </summary>
        public (bool success, string? error) CheckConfiguration()
        {
            if (string.IsNullOrEmpty(_piperExePath) || !File.Exists(_piperExePath))
            {
                return (false, $"piper.exe non trouvé: {_piperExePath}");
            }

            if (string.IsNullOrEmpty(_modelPath) || !File.Exists(_modelPath))
            {
                return (false, $"Modèle non trouvé: {_modelPath}");
            }

            return (true, null);
        }

        /// <summary>
        /// Synthétise et lit le texte à voix haute (Mode Streaming)
        /// </summary>
        public async Task SpeakAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var (configOk, configError) = CheckConfiguration();
            if (!configOk)
            {
                SpeechError?.Invoke(this, configError ?? "Configuration invalide");
                return;
            }

            // Arrêter toute lecture en cours
            await StopAsync();

            _stopRequested = false;
            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            try
            {
                _isSpeaking = true;
                SpeechStarted?.Invoke(this, EventArgs.Empty);

                // Nettoyer le texte du Markdown (astérisques, etc.)
                var cleanedText = CleanMarkDown(text);

                // Découper le texte en phrases
                var sentences = SplitIntoSentences(cleanedText);
                
                if (!sentences.Any()) return;

                // File d'attente thread-safe pour les fichiers audio
                var audioQueue = new BlockingCollection<string>();

                // Tâche Producteur : Génère l'audio pour chaque phrase
                var producerTask = Task.Run(async () =>
                {
                    try
                    {
                        foreach (var sentence in sentences)
                        {
                            if (token.IsCancellationRequested) break;
                            if (string.IsNullOrWhiteSpace(sentence)) continue;

                            var outputFile = Path.Combine(_tempFolder, $"med_speech_part_{Guid.NewGuid():N}.wav");
                            
                            var success = await GenerateAudioAsync(sentence, outputFile, token);

                            if (success && File.Exists(outputFile))
                            {
                                audioQueue.Add(outputFile, token);
                            }
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[PiperTTS] Erreur producteur: {ex.Message}");
                    }
                    finally
                    {
                        audioQueue.CompleteAdding();
                    }
                }, token);

                // Boucle Consommateur
                foreach (var audioFile in audioQueue.GetConsumingEnumerable(token))
                {
                    if (token.IsCancellationRequested || _stopRequested)
                    {
                        TryDeleteFile(audioFile);
                        continue;
                    }

                    // Jouer le segment
                    await PlayAudioAsync(audioFile, token);

                    // Nettoyer après lecture
                    TryDeleteFile(audioFile);

                    // Vérifier si arrêt demandé après chaque phrase
                    if (_stopRequested) break;
                }

                await producerTask;
            }
            catch (OperationCanceledException)
            {
                // Lecture interrompue
            }
            catch (Exception ex)
            {
                SpeechError?.Invoke(this, $"Erreur TTS: {ex.Message}");
            }
            finally
            {
                _isSpeaking = false;
                SpeechCompleted?.Invoke(this, EventArgs.Empty);
            }
        }

        private List<string> SplitIntoSentences(string text)
        {
            var sentences = new List<string>();
            text = text.Replace("\r\n", " ").Replace("\n", " ").Trim();
            
            // Regex qui split après [.!?] suivis d'un espace.
            var parts = Regex.Split(text, @"(?<=[.!?])\s+");
            
            foreach (var part in parts)
            {
                if (!string.IsNullOrWhiteSpace(part))
                {
                    sentences.Add(part.Trim());
                }
            }
            
            return sentences;
        }

        /// <summary>
        /// Nettoie les caractères Markdown qui gêne la lecture (ex: **)
        /// </summary>
        private string CleanMarkDown(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            // Enlever les astérisques (gras, italique) qui sont lus "astérisque"
            text = text.Replace("*", "");

            // Enlever les dièses de titres au début des lignes
            text = Regex.Replace(text, @"^#+\s+", "", RegexOptions.Multiline);

            // Enlever les backticks de code
            text = text.Replace("`", "");

            // Supprimer les lignes de séparation de tableaux Markdown (ex: |---|---|)
            text = Regex.Replace(text, @"^[\s|:-]+$", "", RegexOptions.Multiline);

            // Remplacer les pipes (|) par des espaces pour une lecture fluide
            text = text.Replace("|", " ");

            // Enlever les tirets de liste s'ils sont suivis d'espace (optionnel, mais améliore parfois le débit)
            // On garde la ponctuation normale

            return text;
        }

        private void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        private async Task<bool> GenerateAudioAsync(string text, string outputFile, CancellationToken token)
        {
            try
            {
                var cleanText = text
                    .Replace("\"", "'")
                    .Replace("\r", " ")
                    .Replace("\n", " ")
                    .Trim();

                var arguments = $"--model \"{_modelPath}\" --output_file \"{outputFile}\"";

                if (Math.Abs(_speed - 1.0f) > 0.01f)
                {
                    arguments += $" --length_scale {(1.0f / _speed).ToString(System.Globalization.CultureInfo.InvariantCulture)}";
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = _piperExePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(_piperExePath) ?? ""
                };

                _piperProcess = new Process { StartInfo = startInfo };
                _piperProcess.Start();

                await _piperProcess.StandardInput.WriteLineAsync(cleanText);
                _piperProcess.StandardInput.Close();

                var completed = await Task.Run(() => _piperProcess.WaitForExit(10000), token);

                if (!completed)
                {
                    _piperProcess.Kill();
                    Debug.WriteLine("Timeout génération pipeline");
                    return false;
                }

                if (_piperProcess.ExitCode != 0)
                {
                    var error = await _piperProcess.StandardError.ReadToEndAsync();
                    Debug.WriteLine($"Erreur Piper process: {error}");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Erreur génération audio (phrase): {ex.Message}");
                return false;
            }
            finally
            {
                _piperProcess?.Dispose();
                _piperProcess = null;
            }
        }

        private async Task PlayAudioAsync(string audioFile, CancellationToken token)
        {
            try
            {
                // Lire la durée du fichier WAV
                var duration = GetWavDuration(audioFile);
                if (duration <= TimeSpan.Zero)
                {
                    Debug.WriteLine($"[PiperTTS] Durée invalide pour {audioFile}");
                    return;
                }

                using var player = new SoundPlayer(audioFile);
                player.Load();

                if (token.IsCancellationRequested || _stopRequested)
                    return;

                // Lancer la lecture (non-bloquant)
                player.Play();

                // Attendre la durée du fichier, en vérifiant régulièrement si arrêt demandé
                var elapsed = TimeSpan.Zero;
                var checkInterval = TimeSpan.FromMilliseconds(50);

                while (elapsed < duration)
                {
                    if (token.IsCancellationRequested || _stopRequested)
                    {
                        player.Stop();
                        return;
                    }

                    await Task.Delay(checkInterval, CancellationToken.None);
                    elapsed += checkInterval;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PiperTTS] Erreur lecture: {ex.Message}");
            }
        }

        /// <summary>
        /// Lit la durée d'un fichier WAV depuis son header
        /// </summary>
        private TimeSpan GetWavDuration(string filePath)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
                using var reader = new BinaryReader(fs);

                // WAV header: RIFF (4) + size (4) + WAVE (4) + fmt (4) + chunk size (4)
                // + audio format (2) + channels (2) + sample rate (4) + byte rate (4) + block align (2) + bits per sample (2)
                // + data (4) + data size (4)

                // Skip to byte rate (offset 28)
                fs.Seek(28, SeekOrigin.Begin);
                var byteRate = reader.ReadInt32();

                // Find data chunk
                fs.Seek(12, SeekOrigin.Begin); // After RIFF header
                while (fs.Position < fs.Length - 8)
                {
                    var chunkId = new string(reader.ReadChars(4));
                    var chunkSize = reader.ReadInt32();

                    if (chunkId == "data")
                    {
                        if (byteRate > 0)
                        {
                            var seconds = (double)chunkSize / byteRate;
                            return TimeSpan.FromSeconds(seconds);
                        }
                        break;
                    }

                    // Skip this chunk
                    fs.Seek(chunkSize, SeekOrigin.Current);
                }

                // Fallback: estimate from file size (assume 22050 Hz, 16-bit, mono = 44100 bytes/sec)
                var fileSize = new FileInfo(filePath).Length;
                return TimeSpan.FromSeconds(fileSize / 44100.0);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PiperTTS] Erreur lecture durée WAV: {ex.Message}");
                // Fallback
                var fileSize = new FileInfo(filePath).Length;
                return TimeSpan.FromSeconds(fileSize / 44100.0);
            }
        }

        /// <summary>
        /// Initialise une session de lecture en streaming
        /// </summary>
        public Task InitializeStreamAsync()
        {
            StopAsync().Wait();

            _stopRequested = false;
            _streamCts = new CancellationTokenSource();
            _phraseQueue = new BlockingCollection<string>();
            _audioSegmentQueue = new BlockingCollection<string>();
            _isSpeaking = true;

            SpeechStarted?.Invoke(this, EventArgs.Empty);

            var token = _streamCts.Token;

            // Tâche Producteur : Synthétise les phrases en WAV
            _producerTask = Task.Run(async () =>
            {
                try
                {
                    foreach (var phrase in _phraseQueue.GetConsumingEnumerable(token))
                    {
                        if (token.IsCancellationRequested) break;
                        if (string.IsNullOrWhiteSpace(phrase)) continue;

                        var cleanedPhrase = CleanMarkDown(phrase);
                        var outputFile = Path.Combine(_tempFolder, $"med_stream_part_{Guid.NewGuid():N}.wav");

                        var success = await GenerateAudioAsync(cleanedPhrase, outputFile, token).ConfigureAwait(false);
                        if (success && File.Exists(outputFile))
                        {
                            _audioSegmentQueue.Add(outputFile, token);
                        }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PiperTTS] Erreur producteur stream: {ex.Message}");
                }
                finally
                {
                    _audioSegmentQueue.CompleteAdding();
                }
            }, token);

            // Tâche Consommateur : Lit les WAV séquentiellement
            _consumerTask = Task.Run(async () =>
            {
                try
                {
                    foreach (var audioFile in _audioSegmentQueue.GetConsumingEnumerable(token))
                    {
                        if (token.IsCancellationRequested || _stopRequested)
                        {
                            TryDeleteFile(audioFile);
                            continue;
                        }

                        await PlayAudioAsync(audioFile, token).ConfigureAwait(false);
                        TryDeleteFile(audioFile);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PiperTTS] Erreur consommateur stream: {ex.Message}");
                }
                finally
                {
                    _isSpeaking = false;
                    SpeechCompleted?.Invoke(this, EventArgs.Empty);
                }
            }, token);

            return Task.CompletedTask;
        }

        /// <summary>
        /// Ajoute une phrase à la file d'attente
        /// </summary>
        public Task EnqueuePhraseAsync(string phrase)
        {
            if (string.IsNullOrWhiteSpace(phrase) || _phraseQueue == null || _phraseQueue.IsAddingCompleted)
                return Task.CompletedTask;

            try
            {
                _phraseQueue.Add(phrase);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PiperTTS] Erreur ajout phrase au stream: {ex.Message}");
            }
            
            return Task.CompletedTask;
        }

        /// <summary>
        /// Finalise la session de streaming
        /// </summary>
        public async Task FinalizeStreamAsync()
        {
            if (_phraseQueue != null && !_phraseQueue.IsAddingCompleted)
            {
                _phraseQueue.CompleteAdding();
            }

            if (_producerTask != null)
            {
                await _producerTask.ConfigureAwait(false);
            }

            if (_consumerTask != null)
            {
                await _consumerTask.ConfigureAwait(false);
            }
        }

        public Task StopAsync()
        {
            Debug.WriteLine("[PiperTTS] StopAsync appelé");

            _stopRequested = true;
            
            try
            {
                _cancellationTokenSource?.Cancel();
                _streamCts?.Cancel();

                _phraseQueue?.CompleteAdding();
                _audioSegmentQueue?.CompleteAdding();

                if (_piperProcess != null && !_piperProcess.HasExited)
                {
                    try { _piperProcess.Kill(); } catch { }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PiperTTS] Erreur StopAsync: {ex.Message}");
            }

            _isSpeaking = false;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            StopAsync().Wait();
            _cancellationTokenSource?.Dispose();
            _piperProcess?.Dispose();

            try
            {
                if (Directory.Exists(_tempFolder))
                {
                    Directory.Delete(_tempFolder, true);
                }
            }
            catch { }
        }
    }
}
