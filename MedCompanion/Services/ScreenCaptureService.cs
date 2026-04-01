using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service pour capturer une zone spécifique de l'écran
    /// </summary>
    public class ScreenCaptureService
    {
        /// <summary>
        /// Capture une zone rectangulaire de l'écran et retourne les données en format byte[] (PNG)
        /// </summary>
        /// <param name="rect">Coordonnées absolues à l'écran</param>
        public byte[] CaptureRegion(Rect rect)
        {
            try
            {
                int width = (int)rect.Width;
                int height = (int)rect.Height;
                int left = (int)rect.Left;
                int top = (int)rect.Top;

                using (Bitmap bitmap = new Bitmap(width, height))
                {
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        g.CopyFromScreen(left, top, 0, 0, new System.Drawing.Size(width, height));
                    }

                    using (MemoryStream ms = new MemoryStream())
                    {
                        bitmap.Save(ms, ImageFormat.Png);
                        return ms.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ScreenCapture] Erreur capture : {ex.Message}");
                return Array.Empty<byte>();
            }
        }

        /// <summary>
        /// Sauvegarde une capture pour débug
        /// </summary>
        public void SaveCapture(byte[] data, string path)
        {
            File.WriteAllBytes(path, data);
        }
    }
}
