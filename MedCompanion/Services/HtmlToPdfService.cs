using System;
using System.Diagnostics;
using System.IO;

namespace MedCompanion.Services
{
    public class HtmlToPdfService
    {
        private readonly string _libreOfficePath = string.Empty;

        public HtmlToPdfService()
        {
            var possiblePaths = new[]
            {
                @"C:\Program Files\LibreOffice\program\soffice.exe",
                @"C:\Program Files (x86)\LibreOffice\program\soffice.exe",
                @"C:\Program Files\LibreOffice 24\program\soffice.exe",
                @"C:\Program Files\LibreOffice 7\program\soffice.exe"
            };

            foreach (var path in possiblePaths)
            {
                if (File.Exists(path))
                {
                    _libreOfficePath = path;
                    break;
                }
            }
        }

        public bool IsAvailable => !string.IsNullOrEmpty(_libreOfficePath);

        public bool ConvertHtmlToPdf(string htmlPath, string pdfOutputPath)
        {
            if (!IsAvailable || !File.Exists(htmlPath)) return false;

            try
            {
                var outputDir = Path.GetDirectoryName(pdfOutputPath) ?? Path.GetTempPath();
                Directory.CreateDirectory(outputDir);

                var psi = new ProcessStartInfo
                {
                    FileName = _libreOfficePath,
                    Arguments = $"--headless --convert-to pdf --outdir \"{outputDir}\" \"{htmlPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return false;
                proc.WaitForExit(30000);
                if (!proc.HasExited) { proc.Kill(); return false; }

                // LibreOffice nomme le PDF d'après le HTML, dans outputDir
                var expectedPdf = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(htmlPath) + ".pdf");
                if (!File.Exists(expectedPdf)) return false;

                if (!string.Equals(expectedPdf, pdfOutputPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(pdfOutputPath)) File.Delete(pdfOutputPath);
                    File.Move(expectedPdf, pdfOutputPath);
                }

                return File.Exists(pdfOutputPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[HtmlToPdfService] {ex.Message}");
                return false;
            }
        }
    }
}
