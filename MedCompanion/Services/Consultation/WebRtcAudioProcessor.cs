using System;
using System.IO;
using SoundFlow.Abstracts;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Enums;
using SoundFlow.Extensions.WebRtc.Apm;
using SoundFlow.Extensions.WebRtc.Apm.Components;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace MedCompanion.Services.Consultation
{
    /// <summary>
    /// Wrapper autour de SoundFlow.Extensions.WebRtc.Apm pour appliquer le module WebRTC APM
    /// (Noise Suppression, le même que LiveKit/Chrome) sur des échantillons float[] in-memory.
    ///
    /// Workflow :
    ///   float[] 16 kHz mono → WAV en mémoire → NoiseSuppressor.ProcessAll() → float[] 16 kHz mono nettoyé
    ///
    /// Le moteur audio est initialisé une fois et réutilisé (instance singleton thread-safe).
    /// </summary>
    public static class WebRtcAudioProcessor
    {
        // Lazy init du moteur — créé au premier usage, réutilisé ensuite.
        private static readonly Lazy<(AudioEngine engine, AudioFormat format, bool ok)> _state =
            new(InitializeEngine, isThreadSafe: true);

        private static readonly object _processLock = new();

        public static bool IsAvailable => _state.Value.ok;

        private static (AudioEngine engine, AudioFormat format, bool ok) InitializeEngine()
        {
            try
            {
                // Format 16 kHz mono — compatible Whisper et supporté par WebRTC APM
                // (qui accepte 8/16/32/48 kHz). On utilise SampleFormat F32 pour matcher nos float[].
                var format = new AudioFormat
                {
                    SampleRate    = 16000,
                    Channels      = 1,
                    Format        = SampleFormat.F32
                };
                var engine = new MiniAudioEngine();
                return (engine, format, true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WebRtcAudioProcessor] Init échouée : {ex.Message}");
                return (null!, default, false);
            }
        }

        /// <summary>
        /// Traite un buffer de samples float (16 kHz mono) à travers WebRTC APM (Noise Suppression).
        /// Retourne le buffer nettoyé. En cas d'échec, retourne l'input intact (fallback transparent).
        /// </summary>
        public static float[] ProcessSamples(float[] inputSamples)
        {
            if (inputSamples == null || inputSamples.Length == 0) return inputSamples ?? Array.Empty<float>();
            if (!IsAvailable) return inputSamples;

            // Sérialisé : SoundFlow n'est pas garantie thread-safe pour l'usage concurrent du même engine
            lock (_processLock)
            {
                try
                {
                    var (engine, format, _) = _state.Value;

                    // Sérialiser float[] → PCM 32-bit float en MemoryStream
                    var ms = new MemoryStream(inputSamples.Length * 4);
                    var writer = new BinaryWriter(ms);
                    for (int i = 0; i < inputSamples.Length; i++)
                        writer.Write(inputSamples[i]);
                    ms.Position = 0;

                    using var dataProvider = new StreamDataProvider(engine, format, ms);
                    using var suppressor = new NoiseSuppressor(
                        dataProvider:      dataProvider,
                        audioFormat:       format,
                        suppressionLevel:  NoiseSuppressionLevel.VeryHigh);

                    var cleaned = suppressor.ProcessAll();
                    if (cleaned == null || cleaned.Length == 0) return inputSamples;
                    return cleaned;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[WebRtcAudioProcessor] Process échec : {ex.Message} — fallback brut");
                    return inputSamples;
                }
            }
        }
    }
}
