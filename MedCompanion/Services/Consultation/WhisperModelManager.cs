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
        /// <summary>
        /// Dossier des modèles : réglage <c>WhisperModelsDir</c> s'il est renseigné, sinon le dossier
        /// de l'application dans AppData, où Med télécharge ses modèles.
        ///
        /// Ce n'est pas un repli : le fichier n'est jamais cherché à deux endroits. Renseigné, le
        /// réglage est la seule source ; le dossier AppData ne sert que s'il est vide, pour qu'un poste
        /// neuf puisse encore télécharger son modèle sans configuration. Sur le poste du cabinet, les
        /// modèles vivent sur la partition dédiée du SSD (M:\whisper) depuis le 14/09/2026.
        /// </summary>
        private static readonly string ModelsFolder = ResoudreDossier();

        private static string ResoudreDossier()
        {
            var dossierApplication = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MedCompanion", "models");

            var configure = AppSettings.Load().WhisperModelsDir;
            return string.IsNullOrWhiteSpace(configure) ? dossierApplication : configure.Trim();
        }

        // Tailles approximatives en bytes pour afficher une progression estimée
        private static readonly long[] ModelEstimatedBytes =
        {
            75_000_000L,    // Tiny    ~75 MB
            242_000_000L,   // Small   ~242 MB
            1_528_000_000L, // Medium  ~1.5 GB
            3_094_000_000L  // LargeV3 ~3.1 GB
        };

        public WhisperModelSize ModelSize { get; set; } = WhisperModelSize.Medium;

        /// <summary>
        /// Modèle imposé par le réglage <c>WhisperModelPath</c>, ou null. Il permet de servir un modèle
        /// hors catalogue — le large-v3 spécialisé français, par exemple — que le téléchargeur officiel
        /// ne connaît pas. Lu à chaque accès : changer de modèle ne demande que de rouvrir Med.
        /// </summary>
        private static string? CheminImpose
        {
            get
            {
                try
                {
                    var c = AppSettings.Load().WhisperModelPath;
                    return string.IsNullOrWhiteSpace(c) ? null : c.Trim();
                }
                catch { return null; }
            }
        }

        public string ModelPath => CheminImpose ?? Path.Combine(ModelsFolder, GetModelFileName(ModelSize));

        /// <summary>
        /// Chemin attendu du large-v3 spécialisé français, dans le dossier des modèles. Il ne fait pas
        /// partie du catalogue officiel : il se sert par <see cref="AppSettings.WhisperModelPath"/>.
        /// </summary>
        public static string CheminModeleFrancais => Path.Combine(ModelsFolder, "ggml-large-v3-french.bin");

        /// <summary>Vrai si le modèle français est présent sur le disque.</summary>
        public static bool ModeleFrancaisDisponible => File.Exists(CheminModeleFrancais);

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

            // Modèle imposé mais absent : on ne télécharge RIEN. Le téléchargeur ne connaît que les
            // quatre tailles officielles ; il rapporterait un large-v3 générique sous le nom demandé,
            // et la dictée tournerait sur un autre modèle que celui qu'on croit servir.
            var impose = CheminImpose;
            if (impose != null)
                throw new FileNotFoundException(
                    $"Modèle Whisper introuvable : {impose}. Corrigez le réglage WhisperModelPath, " +
                    "ou videz-le pour revenir aux modèles standard.", impose);

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
