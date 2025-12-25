using System;
using System.Diagnostics;
using System.IO;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service de conversion DOCX vers PDF utilisant LibreOffice
    /// </summary>
    public class DocxToPdfService
    {
        private readonly string _libreOfficePath;

        public DocxToPdfService()
        {
            // Chemins possibles pour LibreOffice
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
                    System.Diagnostics.Debug.WriteLine($"[DocxToPdfService] LibreOffice trouvé: {path}");
                    break;
                }
            }

            if (string.IsNullOrEmpty(_libreOfficePath))
            {
                System.Diagnostics.Debug.WriteLine("[DocxToPdfService] ⚠️ LibreOffice non trouvé aux emplacements standards");
            }
        }

        /// <summary>
        /// Convertit un fichier DOCX en PDF avec LibreOffice
        /// </summary>
        /// <param name="docxPath">Chemin du fichier DOCX source</param>
        /// <param name="pdfPath">Chemin du fichier PDF destination</param>
        /// <returns>True si la conversion a réussi, False sinon</returns>
        public bool ConvertDocxToPdf(string docxPath, string pdfPath)
        {
            try
            {
                if (string.IsNullOrEmpty(_libreOfficePath))
                {
                    System.Diagnostics.Debug.WriteLine("[DocxToPdfService] LibreOffice non disponible");
                    return false;
                }

                if (!File.Exists(docxPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[DocxToPdfService] Fichier DOCX introuvable: {docxPath}");
                    return false;
                }

                // Vérifier que le dossier de destination existe
                var directory = Path.GetDirectoryName(pdfPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Préparer les arguments pour LibreOffice
                var outputDir = Path.GetDirectoryName(pdfPath);
                var arguments = $"--headless --convert-to pdf --outdir \"{outputDir}\" \"{docxPath}\"";

                System.Diagnostics.Debug.WriteLine($"[DocxToPdfService] Commande: {_libreOfficePath} {arguments}");

                // Lancer LibreOffice
                var processInfo = new ProcessStartInfo
                {
                    FileName = _libreOfficePath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = Process.Start(processInfo))
                {
                    if (process == null)
                    {
                        System.Diagnostics.Debug.WriteLine("[DocxToPdfService] Impossible de démarrer LibreOffice");
                        return false;
                    }

                    process.WaitForExit(30000); // Timeout 30 secondes

                    if (!process.HasExited)
                    {
                        process.Kill();
                        System.Diagnostics.Debug.WriteLine("[DocxToPdfService] Timeout - LibreOffice tué");
                        return false;
                    }

                    var output = process.StandardOutput.ReadToEnd();
                    var error = process.StandardError.ReadToEnd();

                    if (!string.IsNullOrEmpty(output))
                        System.Diagnostics.Debug.WriteLine($"[DocxToPdfService] Output: {output}");
                    if (!string.IsNullOrEmpty(error))
                        System.Diagnostics.Debug.WriteLine($"[DocxToPdfService] Error: {error}");
                }

                // Vérifier que le PDF a été créé
                // LibreOffice crée le PDF avec le même nom que le DOCX
                var expectedPdfPath = Path.Combine(outputDir ?? "", Path.GetFileNameWithoutExtension(docxPath) + ".pdf");

                if (File.Exists(expectedPdfPath))
                {
                    // Si le nom attendu est différent du nom souhaité, renommer
                    if (expectedPdfPath != pdfPath && File.Exists(expectedPdfPath))
                    {
                        if (File.Exists(pdfPath))
                            File.Delete(pdfPath);
                        File.Move(expectedPdfPath, pdfPath);
                    }

                    System.Diagnostics.Debug.WriteLine($"[DocxToPdfService] ✅ Conversion réussie: {pdfPath}");
                    return true;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[DocxToPdfService] ❌ PDF non créé: {expectedPdfPath}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DocxToPdfService] Erreur conversion: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Convertit un fichier DOCX en PDF et supprime le DOCX après conversion
        /// </summary>
        /// <param name="docxPath">Chemin du fichier DOCX source</param>
        /// <param name="pdfPath">Chemin du fichier PDF destination</param>
        /// <param name="deleteDocxAfterConversion">Si true, supprime le DOCX après conversion réussie</param>
        /// <returns>True si la conversion a réussi, False sinon</returns>
        public bool ConvertDocxToPdf(string docxPath, string pdfPath, bool deleteDocxAfterConversion)
        {
            var success = ConvertDocxToPdf(docxPath, pdfPath);

            if (success && deleteDocxAfterConversion)
            {
                try
                {
                    File.Delete(docxPath);
                    System.Diagnostics.Debug.WriteLine($"[DocxToPdfService] Fichier DOCX supprimé: {docxPath}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[DocxToPdfService] Erreur suppression DOCX: {ex.Message}");
                }
            }

            return success;
        }
    }
}
