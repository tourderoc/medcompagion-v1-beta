using System;
using System.IO;
using System.Linq;
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

        /// <summary>Racine des sessions enregistrées.</summary>
        public static string RecordingsRoot => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MedCompanion", "recordings");

        /// <summary>
        /// Nombre de sessions conservées. Au-delà, les plus anciennes sont supprimées.
        ///
        /// Ce ne sont pas des données cliniques mais des enregistrements audio bruts de
        /// consultations, conservés pour diagnostiquer la transcription (voir
        /// WhisperStreamingService.SaveAudioEnabled). La transcription utile, elle, est déjà dans le
        /// dossier patient. Les accumuler sans limite ne sert donc à rien et fait grossir
        /// indéfiniment un stock de données très sensibles — 379 sessions en trois mois avant
        /// l'introduction de cette rotation.
        /// </summary>
        public const int MaxSessionsKept = 50;

        public AudioRecorder(int sampleRate = 16000, int channels = 1)
        {
            _format = new WaveFormat(sampleRate, 16, channels);

            SessionFolder = Path.Combine(RecordingsRoot, $"session_{DateTime.Now:yyyyMMdd_HHmmss}");
            Directory.CreateDirectory(SessionFolder);
        }

        /// <summary>
        /// Supprime les sessions les plus anciennes au-delà de <see cref="MaxSessionsKept"/>.
        ///
        /// Opération destructive et irréversible : trois garde-fous la bornent — on ne descend
        /// jamais sous la racine des enregistrements, on n'efface que des dossiers dont le nom suit
        /// le motif "session_", et la session en cours est toujours épargnée. Tout échec est ignoré :
        /// un fichier verrouillé ne doit pas interrompre une consultation.
        /// </summary>
        public static void PruneOldSessions(string? currentSessionFolder = null)
        {
            try
            {
                if (!Directory.Exists(RecordingsRoot)) return;

                var sessions = new DirectoryInfo(RecordingsRoot)
                    .GetDirectories("session_*")          // jamais autre chose que nos sessions
                    .OrderByDescending(d => d.CreationTimeUtc)
                    .Skip(MaxSessionsKept)
                    .ToList();

                foreach (var dir in sessions)
                {
                    if (currentSessionFolder != null &&
                        string.Equals(dir.FullName, currentSessionFolder, StringComparison.OrdinalIgnoreCase))
                        continue;

                    try { dir.Delete(recursive: true); }
                    catch { /* dossier verrouillé : il repassera au prochain nettoyage */ }
                }
            }
            catch { /* best-effort : ne doit jamais gêner l'enregistrement */ }
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
