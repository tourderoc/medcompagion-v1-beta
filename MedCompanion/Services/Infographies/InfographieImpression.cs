using System;
using System.IO;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MedCompanion.Services.Infographies
{
    /// <summary>
    /// Chargement et impression des images de la bibliothèque d'infographies.
    /// </summary>
    public static class InfographieImpression
    {
        /// <summary>Marge autour de l'image, en unités WPF (1/96 po) : ~8 mm, hors zone non imprimable.</summary>
        private const double Marge = 30;

        /// <summary>
        /// Charge l'image sans garder le fichier ouvert (sinon « Remplacer l'image » échoue).
        /// largeurDecodage &gt; 0 réduit l'image en mémoire, pour les miniatures.
        /// </summary>
        public static BitmapImage? Charger(string chemin, int largeurDecodage = 0)
        {
            try
            {
                if (!File.Exists(chemin)) return null;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.UriSource = new Uri(chemin);
                if (largeurDecodage > 0) bmp.DecodePixelWidth = largeurDecodage;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[InfographieImpression] image illisible {chemin} : {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Ouvre la boîte d'impression et imprime l'image sur une page, orientée selon sa forme
        /// (paysage pour les exports NotebookLM). Retourne faux si l'utilisateur annule.
        /// </summary>
        public static (bool imprime, string? error) Imprimer(string cheminImage, string titre)
        {
            try
            {
                var bmp = Charger(cheminImage);
                if (bmp == null) return (false, "Image introuvable ou illisible.");

                bool paysage = bmp.PixelWidth > bmp.PixelHeight;
                var dlg = new PrintDialog();
                dlg.PrintTicket.PageOrientation = paysage ? PageOrientation.Landscape : PageOrientation.Portrait;
                if (dlg.ShowDialog() != true) return (false, null);

                // Dimensions du papier choisi (données en portrait), A4 par défaut
                double largeur = dlg.PrintTicket.PageMediaSize?.Width ?? 793.7;
                double hauteur = dlg.PrintTicket.PageMediaSize?.Height ?? 1122.5;
                bool paysageChoisi = dlg.PrintTicket.PageOrientation == PageOrientation.Landscape;
                if (paysageChoisi != (largeur > hauteur))
                    (largeur, hauteur) = (hauteur, largeur);

                var image = new Image
                {
                    Source = bmp,
                    Stretch = Stretch.Uniform,
                    Width = largeur - 2 * Marge,
                    Height = hauteur - 2 * Marge
                };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);

                var page = new FixedPage { Width = largeur, Height = hauteur, Background = Brushes.White };
                FixedPage.SetLeft(image, Marge);
                FixedPage.SetTop(image, Marge);
                page.Children.Add(image);

                var contenu = new PageContent();
                ((System.Windows.Markup.IAddChild)contenu).AddChild(page);
                var document = new FixedDocument();
                document.DocumentPaginator.PageSize = new Size(largeur, hauteur);
                document.Pages.Add(contenu);

                dlg.PrintDocument(document.DocumentPaginator, titre);
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, $"Erreur impression : {ex.Message}");
            }
        }
    }
}
