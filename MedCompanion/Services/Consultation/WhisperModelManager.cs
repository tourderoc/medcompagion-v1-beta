using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net.Ggml;

namespace MedCompanion.Services.Consultation
{
    public enum WhisperModelSize
    {
        Tiny,
        Small,
        Medium,
        LargeV3
    }

    public class WhisperModelManager
    {
        private static readonly string ModelsFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MedCompanion", "models");

        // Tailles approximatives en bytes pour afficher une progression estimée
        private static readonly long[] ModelEstimatedBytes =
        {
            75_000_000L,    // Tiny    ~75 MB
            242_000_000L,   // Small   ~242 MB
            1_528_000_000L, // Medium  ~1.5 GB
            3_094_000_000L  // LargeV3 ~3.1 GB
        };

        public WhisperModelSize ModelSize { get; set; } = WhisperModelSize.Medium;

        public string ModelPath => Path.Combine(ModelsFolder, GetModelFileName(ModelSize));

        public bool IsModelAvailable => File.Exists(ModelPath);

        public static string GetModelFileName(WhisperModelSize size) => size switch
        {
            WhisperModelSize.Tiny    => "ggml-tiny.bin",
            WhisperModelSize.Small   => "ggml-small.bin",
            WhisperModelSize.Medium  => "ggml-medium.bin",
            WhisperModelSize.LargeV3 => "ggml-large-v3.bin",
            _                        => "ggml-medium.bin"
        };

        private static GgmlType ToGgmlType(WhisperModelSize size) => size switch
        {
            WhisperModelSize.Tiny    => GgmlType.Tiny,
            WhisperModelSize.Small   => GgmlType.Small,
            WhisperModelSize.Medium  => GgmlType.Medium,
            WhisperModelSize.LargeV3 => GgmlType.LargeV3,
            _                        => GgmlType.Medium
        };

        /// <summary>
        /// Télécharge le modèle si absent. Progress : 0-100 (estimation).
        /// </summary>
        public async Task EnsureModelAsync(IProgress<int>? progress = null,
                                           CancellationToken ct = default)
        {
            if (IsModelAvailable) return;

            Directory.CreateDirectory(ModelsFolder);

            var tempPath    = ModelPath + ".tmp";
            var estimated   = ModelEstimatedBytes[(int)ModelSize];

            try
            {
                using var modelStream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(
                    ToGgmlType(ModelSize), QuantizationType.NoQuantization, ct);

                var buffer     = new byte[81920];
                var downloaded = 0L;

                await using var fileStream = File.Create(tempPath);

                int read;
                while ((read = await modelStream.ReadAsync(buffer, ct)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    downloaded += read;

                    // Progression estimée — le stream HTTP ne supporte pas .Length
                    if (progress != null && estimated > 0)
                    {
                        var pct = (int)Math.Min(99, downloaded * 100 / estimated);
                        progress.Report(pct);
                    }
                }
            }
            catch
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
                throw;
            }

            File.Move(tempPath, ModelPath, overwrite: true);
            progress?.Report(100);
        }
    }
}
