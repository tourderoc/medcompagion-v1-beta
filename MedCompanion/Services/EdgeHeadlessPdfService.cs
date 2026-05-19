using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace MedCompanion.Services
{
    /// <summary>
    /// Convertit HTML → PDF via Microsoft Edge en mode headless (Chromium, rendu CSS parfait).
    /// Edge est préinstallé sur Windows 10/11 — aucune dépendance externe.
    /// </summary>
    public class EdgeHeadlessPdfService
    {
        private readonly string _edgePath;

        public EdgeHeadlessPdfService()
        {
            _edgePath = FindEdge();
        }

        public bool IsAvailable => !string.IsNullOrEmpty(_edgePath);

        private static string FindEdge()
        {
            var candidates = new[]
            {
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\Edge\Application\msedge.exe")
            };

            foreach (var p in candidates)
                if (File.Exists(p)) return p;

            return string.Empty;
        }

        /// <summary>
        /// Convertit un fichier HTML local en PDF.
        /// </summary>
        public async Task<bool> ConvertAsync(string htmlPath, string pdfPath)
        {
            if (!IsAvailable || !File.Exists(htmlPath)) return false;

            try
            {
                var outputDir = Path.GetDirectoryName(pdfPath) ?? Path.GetTempPath();
                Directory.CreateDirectory(outputDir);

                // Edge headless écrit le PDF dans le dossier courant avec le nom print.pdf
                // On lui indique --print-to-pdf=chemin_absolu pour éviter l'ambiguïté.
                var absHtml = Path.GetFullPath(htmlPath);
                var absPdf  = Path.GetFullPath(pdfPath);

                var psi = new ProcessStartInfo
                {
                    FileName  = _edgePath,
                    Arguments = $"--headless --disable-gpu --no-sandbox " +
                                $"--print-to-pdf=\"{absPdf}\" " +
                                $"--no-pdf-header-footer " +
                                $"\"file:///{absHtml.Replace('\\', '/')}\"",
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return false;

                await proc.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));

                if (!proc.HasExited) { proc.Kill(); return false; }

                return File.Exists(absPdf);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EdgeHeadlessPdf] {ex.Message}");
                return false;
            }
        }
    }
}
