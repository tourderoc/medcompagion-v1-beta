using System;
using System.IO;
using System.Threading.Tasks;
using Tesseract;

namespace MedCompanion.Services
{
    public class OcrService
    {
        private readonly string _tessDataPath;
        private const string Language = "fra";

        public OcrService(string tessDataPath)
        {
            _tessDataPath = tessDataPath;
        }

        public bool IsConfigured()
        {
             return Directory.Exists(_tessDataPath) && File.Exists(Path.Combine(_tessDataPath, $"{Language}.traineddata"));
        }

        public string GetConfigurationError()
        {
            if (!Directory.Exists(_tessDataPath))
                return $"Le dossier {_tessDataPath} est introuvable.";
            if (!File.Exists(Path.Combine(_tessDataPath, $"{Language}.traineddata")))
                return $"Le fichier de langue '{Language}.traineddata' est manquant dans {_tessDataPath}.";
            return string.Empty;
        }

        /// <summary>
        /// Extract text from a specific region of an image
        /// </summary>
        public async Task<string> ExtractTextFromImageRegionAsync(byte[] imageBytes, double x, double y, double width, double height)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using var engine = new TesseractEngine(_tessDataPath, Language, EngineMode.Default);
                    using var pix = Pix.LoadFromMemory(imageBytes);

                    // Create a rectangle for the crop
                    // Ensure bounds are within the image
                    int cropX = Math.Max(0, (int)x);
                    int cropY = Math.Max(0, (int)y);
                    int cropW = Math.Min((int)width, pix.Width - cropX);
                    int cropH = Math.Min((int)height, pix.Height - cropY);

                    if (cropW <= 0 || cropH <= 0) return string.Empty;

                    var cropRect = new Rect(cropX, cropY, cropW, cropH);

                    // Process with Tesseract
                    using var page = engine.Process(pix, cropRect, PageSegMode.Auto);
                    return page.GetText()?.Trim() ?? string.Empty;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[OcrService] Error: {ex.Message}");
                    return $"[Erreur OCR: {ex.Message}]";
                }
            });
        }
    }
}
