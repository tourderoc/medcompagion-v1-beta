using System;
using System.Diagnostics;
using System.IO;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service centralisé pour les opérations sur les fichiers
    /// Utilisable par tous les ViewModels pour ouvrir, imprimer, afficher des fichiers
    /// </summary>
    public class FileOperationService
    {
        /// <summary>
        /// Ouvre un fichier avec l'application par défaut
        /// </summary>
        public void OpenFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Le chemin du fichier ne peut pas être vide", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Le fichier n'existe pas : {filePath}");

            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    }
                };
                process.Start();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Impossible d'ouvrir le fichier : {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Imprime un fichier (ouvre le fichier avec l'action "print")
        /// </summary>
        public void PrintFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Le chemin du fichier ne peut pas être vide", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Le fichier n'existe pas : {filePath}");

            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = filePath,
                        Verb = "print",
                        UseShellExecute = true,
                        CreateNoWindow = true
                    }
                };
                process.Start();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Impossible d'imprimer le fichier : {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Affiche le fichier dans l'explorateur Windows et le sélectionne
        /// </summary>
        public void ShowInExplorer(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Le chemin du fichier ne peut pas être vide", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Le fichier n'existe pas : {filePath}");

            try
            {
                Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Impossible d'afficher le fichier dans l'explorateur : {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Ouvre un dossier dans l'explorateur Windows
        /// </summary>
        public void OpenFolder(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath))
                throw new ArgumentException("Le chemin du dossier ne peut pas être vide", nameof(folderPath));

            if (!Directory.Exists(folderPath))
                throw new DirectoryNotFoundException($"Le dossier n'existe pas : {folderPath}");

            try
            {
                Process.Start("explorer.exe", folderPath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Impossible d'ouvrir le dossier : {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Vérifie si un fichier existe
        /// </summary>
        public bool FileExists(string filePath)
        {
            return !string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath);
        }

        /// <summary>
        /// Vérifie si un dossier existe
        /// </summary>
        public bool FolderExists(string folderPath)
        {
            return !string.IsNullOrWhiteSpace(folderPath) && Directory.Exists(folderPath);
        }

        /// <summary>
        /// Supprime un fichier de manière sécurisée
        /// </summary>
        public void DeleteFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Le chemin du fichier ne peut pas être vide", nameof(filePath));

            if (!File.Exists(filePath))
                return; // Déjà supprimé ou n'existe pas

            try
            {
                File.Delete(filePath);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Impossible de supprimer le fichier : {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Copie un fichier vers une nouvelle destination
        /// </summary>
        public void CopyFile(string sourceFilePath, string destinationFilePath, bool overwrite = false)
        {
            if (string.IsNullOrWhiteSpace(sourceFilePath))
                throw new ArgumentException("Le chemin source ne peut pas être vide", nameof(sourceFilePath));

            if (string.IsNullOrWhiteSpace(destinationFilePath))
                throw new ArgumentException("Le chemin destination ne peut pas être vide", nameof(destinationFilePath));

            if (!File.Exists(sourceFilePath))
                throw new FileNotFoundException($"Le fichier source n'existe pas : {sourceFilePath}");

            try
            {
                File.Copy(sourceFilePath, destinationFilePath, overwrite);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Impossible de copier le fichier : {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Obtient la taille d'un fichier en octets
        /// </summary>
        public long GetFileSize(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                throw new ArgumentException("Le chemin du fichier ne peut pas être vide", nameof(filePath));

            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Le fichier n'existe pas : {filePath}");

            var fileInfo = new FileInfo(filePath);
            return fileInfo.Length;
        }

        /// <summary>
        /// Obtient la taille d'un fichier formatée en Ko, Mo, etc.
        /// </summary>
        public string GetFormattedFileSize(string filePath)
        {
            var sizeInBytes = GetFileSize(filePath);
            return FormatBytes(sizeInBytes);
        }

        /// <summary>
        /// Formate une taille en octets en chaîne lisible (Ko, Mo, Go)
        /// </summary>
        private string FormatBytes(long bytes)
        {
            string[] sizes = { "o", "Ko", "Mo", "Go", "To" };
            double len = bytes;
            int order = 0;
            
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }
    }
}
