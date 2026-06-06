using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace MedCompanion.Services
{
    public class PatientPhotoService
    {
        private readonly StorageService _storageService;

        public PatientPhotoService(StorageService storageService)
        {
            _storageService = storageService;
        }

        /// <summary>
        /// Importe une photo depuis le disque vers le dossier du patient
        /// Redimensionne et compresse l'image en JPEG
        /// </summary>
        /// <param name="sourceFilePath">Le chemin d'origine de l'image sélectionnée</param>
        /// <param name="nomComplet">Le nom du patient</param>
        /// <returns>Le nom du fichier sauvegardé (ex: photo.jpg) ou null en cas d'erreur</returns>
        public async Task<string?> ImportPhotoAsync(string sourceFilePath, string nomComplet)
        {
            try
            {
                var patientDir = _storageService.GetPatientDirectory(nomComplet);
                if (!Directory.Exists(patientDir))
                {
                    Directory.CreateDirectory(patientDir);
                }

                string destinationFileName = "photo.jpg";
                string destinationPath = Path.Combine(patientDir, destinationFileName);

                await Task.Run(() =>
                {
                    // Charger l'image
                    BitmapImage bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(sourceFilePath);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze();

                    // Redimensionner (max 800x800 pour préserver l'espace)
                    double scale = 1.0;
                    if (bitmap.PixelWidth > 800 || bitmap.PixelHeight > 800)
                    {
                        scale = Math.Min(800.0 / bitmap.PixelWidth, 800.0 / bitmap.PixelHeight);
                    }

                    TransformedBitmap resizedBitmap = new TransformedBitmap(bitmap, new System.Windows.Media.ScaleTransform(scale, scale));

                    // Encoder en JPEG
                    JpegBitmapEncoder encoder = new JpegBitmapEncoder
                    {
                        QualityLevel = 85
                    };
                    encoder.Frames.Add(BitmapFrame.Create(resizedBitmap));

                    using (FileStream fs = new FileStream(destinationPath, FileMode.Create))
                    {
                        encoder.Save(fs);
                    }
                });

                return destinationFileName;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PatientPhotoService] Erreur lors de l'import : {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Récupère le chemin complet de la photo du patient si elle existe
        /// </summary>
        public string? GetPhotoPath(string nomComplet, string photoFileName)
        {
            if (string.IsNullOrEmpty(photoFileName))
                return null;

            var patientDir = _storageService.GetPatientDirectory(nomComplet);
            var photoPath = Path.Combine(patientDir, photoFileName);

            return File.Exists(photoPath) ? photoPath : null;
        }
    }
}
