using System;

namespace MedCompanion.Services.Consultation
{
    /// <summary>
    /// Normaliseur de volume simple ("AGC" par chunk) :
    /// - Mesure le RMS du segment (ignorant les zones de silence)
    /// - Calcule le gain pour amener le RMS à une cible parole standard (-18 dBFS)
    /// - Applique le gain uniformément
    /// - Soft limiter tanh pour éviter le clipping si pics
    ///
    /// Pour notre cas (chunks de 90s d'une consultation, locuteurs stables), un gain
    /// uniforme par chunk est suffisant et évite les artefacts de pumping qu'on aurait
    /// avec un AGC par frame.
    /// </summary>
    public static class AutoGainControl
    {
        // Cible RMS = -18 dBFS (standard broadcast pour la parole)
        private const float TargetRms      = 0.126f;
        // Plage de gain raisonnable : -12 dB à +18 dB
        private const float MinGain        = 0.25f;
        private const float MaxGain        = 8.0f;
        // En-dessous : considéré silence, ne contribue pas au RMS et pas amplifié
        private const float SilenceFloor   = 0.003f;
        // Soft limiter : au-dessus de ce seuil, courbe tanh pour adoucir l'écrêtage
        private const float LimiterKnee    = 0.85f;

        public static float[] Process(float[] samples)
        {
            if (samples == null || samples.Length == 0) return samples ?? Array.Empty<float>();

            // 1. Mesurer le RMS sur les zones non-silencieuses
            double sumSq = 0;
            int    activeCount = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                float abs = samples[i] < 0 ? -samples[i] : samples[i];
                if (abs > SilenceFloor)
                {
                    sumSq += samples[i] * (double)samples[i];
                    activeCount++;
                }
            }

            // Pas assez de signal utile → on retourne tel quel (chunk de silence)
            if (activeCount < samples.Length / 20) return samples;

            float rms = (float)Math.Sqrt(sumSq / activeCount);
            if (rms < SilenceFloor) return samples;

            // 2. Calculer le gain cible (clampé pour rester safe)
            float gain = TargetRms / rms;
            if (gain < MinGain) gain = MinGain;
            else if (gain > MaxGain) gain = MaxGain;

            // 3. Si gain ≈ 1 (déjà bon niveau), inutile de toucher
            if (gain > 0.95f && gain < 1.10f) return samples;

            // 4. Appliquer le gain + soft limiter
            var output = new float[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                float v = samples[i] * gain;
                // Soft limiter symétrique tanh
                if (v > LimiterKnee)
                    v = LimiterKnee + (1f - LimiterKnee) * (float)Math.Tanh((v - LimiterKnee) * 5f);
                else if (v < -LimiterKnee)
                    v = -LimiterKnee + (1f - LimiterKnee) * (float)Math.Tanh((v + LimiterKnee) * 5f);
                output[i] = v;
            }
            return output;
        }
    }
}
