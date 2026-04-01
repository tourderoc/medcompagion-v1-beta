using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using QRCoder;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service de génération de QR codes pour les tokens patients
    /// </summary>
    public class QRCodeService
    {
        private readonly string _baseUrl;

        /// <summary>
        /// Constructeur avec URL de base Parent'aile
        /// </summary>
        /// <param name="baseUrl">URL de base (ex: "https://parentaile.fr/espace")</param>
        public QRCodeService(string baseUrl = "https://parentaile.fr/espace")
        {
            _baseUrl = baseUrl;
        }

        /// <summary>
        /// Génère un QR code pour un token donné
        /// </summary>
        /// <param name="tokenId">L'identifiant du token</param>
        /// <returns>BitmapImage utilisable dans WPF</returns>
        public BitmapImage GenerateQRCode(string tokenId)
        {
            var url = $"{_baseUrl}?token={tokenId}";
            return GenerateQRCodeFromText(url);
        }

        /// <summary>
        /// Génère un QR code à partir d'un texte quelconque
        /// </summary>
        public BitmapImage GenerateQRCodeFromText(string text)
        {
            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(text, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);

            var qrCodeBytes = qrCode.GetGraphic(20);

            var bitmap = new BitmapImage();
            using (var stream = new MemoryStream(qrCodeBytes))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
            }
            bitmap.Freeze();

            return bitmap;
        }

        /// <summary>
        /// Sauvegarde le QR code en tant que fichier PNG
        /// </summary>
        /// <param name="tokenId">L'identifiant du token</param>
        /// <param name="filePath">Chemin du fichier de sortie</param>
        public void SaveQRCodeToFile(string tokenId, string filePath)
        {
            var url = $"{_baseUrl}?token={tokenId}";

            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);

            var qrCodeBytes = qrCode.GetGraphic(20);
            File.WriteAllBytes(filePath, qrCodeBytes);
        }

        /// <summary>
        /// Copie le QR code dans le presse-papiers
        /// </summary>
        public void CopyQRCodeToClipboard(string tokenId)
        {
            var bitmap = GenerateQRCode(tokenId);
            Clipboard.SetImage(bitmap);
        }

        /// <summary>
        /// Obtient l'URL complète pour un token
        /// </summary>
        public string GetTokenUrl(string tokenId)
        {
            return $"{_baseUrl}?token={tokenId}";
        }
    }
}
