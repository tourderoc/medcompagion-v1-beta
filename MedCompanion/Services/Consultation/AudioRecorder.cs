using System;
using System.IO;
using NAudio.Wave;

namespace MedCompanion.Services.Consultation
{
    /// <summary>
    /// Sauvegarde l'audio par chunk dans un dossier de session pour diagnostic.
    /// Chaque chunk produit :
    ///  - chunk_NNN.wav  (audio)
    ///  - chunk_NNN.txt  (transcription Whisper du chunk)
    ///
    /// Pour désactiver complètement : <c>SaveAudioEnabled = false</c> dans WhisperStreamingService.
    /// </summary>
    public class AudioRecorder : IDisposable
    {
        public string SessionFolder { get; }
        public int    ChunkCount    { get; private set; }

        private readonly WaveFormat _format;
        private bool _isDisposed;

        public AudioRecorder(int sampleRate = 16000, int channels = 1)
        {
            _format = new WaveFormat(sampleRate, 16, channels);

            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MedCompanion", "recordings");
            SessionFolder = Path.Combine(baseDir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(SessionFolder);
        }

        /// <summary>
        /// Enregistre un chunk audio (samples float [-1, 1]) en WAV PCM 16 bits.
        /// </summary>
        public string SaveChunk(float[] samples)
        {
            ChunkCount++;
            var path = Path.Combine(SessionFolder, $"chunk_{ChunkCount:D3}.wav");

            try
            {
                using var writer = new WaveFileWriter(path, _format);
                var pcm = new byte[samples.Length * 2];
                for (int i = 0; i < samples.Length; i++)
                {
                    var s   = Math.Clamp(samples[i], -1f, 1f);
                    var i16 = (short)(s * short.MaxValue);
                    pcm[i * 2]     = (byte)(i16 & 0xff);
                    pcm[i * 2 + 1] = (byte)((i16 >> 8) & 0xff);
                }
                writer.Write(pcm, 0, pcm.Length);
            }
            catch
            {
                // Si l'écriture échoue, on ne casse pas la transcription
            }

            return path;
        }

        /// <summary>
        /// Sauvegarde la transcription du chunk juste à côté du .wav (même nom, .txt).
        /// </summary>
        public void SaveTranscription(string text)
        {
            if (ChunkCount == 0) return;
            var path = Path.Combine(SessionFolder, $"chunk_{ChunkCount:D3}.txt");
            try { File.WriteAllText(path, text ?? "", System.Text.Encoding.UTF8); }
            catch { }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;
        }
    }
}
