using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MedCompanion.Services.Agenda
{
    /// <summary>
    /// Découpe une capture de la vue Semaine en une image par jour.
    ///
    /// Mesuré le 26/09/2026 : sur la grille entière, le modèle vision s'arrête après la première
    /// colonne — il rend un JSON complet mais ne contient qu'un jour. Colonne par colonne, il lit
    /// les six jours, en 12 s chacun, et les noms sont justes. Le découpage n'est donc pas un
    /// confort : c'est ce qui rend la lecture de la semaine possible.
    /// </summary>
    public static class AgendaImageService
    {
        private const int JoursAffiches = 6;   // lundi → samedi

        /// <summary>Agrandissement avant lecture : le texte de la grille fait ~9 px de haut.</summary>
        private const int Agrandissement = 2;

        /// <summary>
        /// Une image par jour, de gauche à droite. Le bord gauche de la grille est trouvé en
        /// cherchant le premier trait vertical continu — c'est la limite du panneau latéral de
        /// Doctolib, ou celle de la règle des heures quand le panneau est replié.
        /// </summary>
        public static List<byte[]> DecouperColonnes(byte[] capture)
        {
            var colonnes = new List<byte[]>();
            var source = Charger(capture);
            if (source == null) return colonnes;

            int x0 = BordGaucheGrille(source);
            int largeur = (source.PixelWidth - x0) / JoursAffiches;
            if (largeur < 40) return colonnes;   // capture trop étroite : on renonce au découpage

            for (int i = 0; i < JoursAffiches; i++)
            {
                var rect = new Int32Rect(x0 + i * largeur, 0, largeur, source.PixelHeight);
                var crop = new CroppedBitmap(source, rect);

                var agrandie = new TransformedBitmap(crop, new ScaleTransform(Agrandissement, Agrandissement));
                colonnes.Add(EnPng(agrandie));
            }
            return colonnes;
        }

        /// <summary>
        /// Découpe une capture de la vue Liste d'une journée en bandes horizontales, une par rendez-vous.
        /// Chaque ligne est isolée entre deux séparateurs horizontaux du tableau Doctolib,
        /// puis agrandie pour faciliter la lecture des petits caractères par le modèle vision.
        /// Élimine tout risque d'hallucination de créneaux inexistants par le modèle.
        /// </summary>
        public static List<byte[]> DecouperLignesJour(byte[] capture)
            => DecouperLignesJourDetail(capture).Lignes.Select(l => l.Image).ToList();

        /// <summary>Une ligne de la vue Liste. <see cref="Annulee"/> vient des pixels, pas du modèle.</summary>
        /// <summary><see cref="Sombres"/> : pixels comptés dans la colonne du ▷, pour recalibrer le
        /// seuil si Doctolib change d'apparence.</summary>
        public record LigneDecoupee(byte[] Image, bool Annulee, int Sombres);

        /// <summary>Les lignes, et sous le tableau la zone « Nombre de rendez-vous : N ».</summary>
        public record DecoupageListe(List<LigneDecoupee> Lignes, byte[]? Pied);

        /// <summary>
        /// Découpe la vue Liste ligne par ligne et dit, pour chacune, si le rendez-vous est annulé.
        ///
        /// Le signe retenu n'est pas le trait de rature — un pixel d'épaisseur, que le modèle vision
        /// ne voit pas (mesuré le 26/09/2026, et encore le 14/10 : 4 annulations manquées sur 4) —
        /// mais le petit ▷ que Doctolib met devant chaque rendez-vous valide et retire des annulés.
        /// Il est toujours au même endroit, gris foncé sur blanc : les pixels le trouvent sans erreur.
        /// </summary>
        public static DecoupageListe DecouperLignesJourDetail(byte[] capture)
        {
            var bandes = new List<LigneDecoupee>();
            var vide = new DecoupageListe(bandes, null);
            var source = Charger(capture);
            if (source == null) return vide;

            int w = source.PixelWidth, h = source.PixelHeight;
            if (w < 400 || h < 300) return vide;

            var converti = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int stride = w * 4;
            var pixels = new byte[stride * h];
            converti.CopyPixels(pixels, stride, 0);

            int yMin = (int)(h * 0.15);
            int yMax = (int)(h * 0.90);
            int xStart = Math.Max(120, (int)(w * 0.065));
            int xEnd = Math.Min(w - 70, (int)(w * 0.70));

            var separateurs = new List<int>();

            for (int y = yMin; y < yMax; y++)
            {
                int grisCount = 0;
                int total = 0;
                for (int x = xStart; x < xEnd; x += 6)
                {
                    int idx = y * stride + x * 4;
                    byte b = pixels[idx], g = pixels[idx + 1], r = pixels[idx + 2];
                    total++;
                    if (r >= 215 && r <= 248 && Math.Abs(r - g) <= 6 && Math.Abs(g - b) <= 6)
                        grisCount++;
                }

                if (total > 0 && (double)grisCount / total >= 0.65)
                    separateurs.Add(y);
            }

            if (separateurs.Count == 0) return vide;

            // Regrouper les séparateurs consécutifs (distance <= 6 px)
            var groupes = new List<int>();
            int prev = -100;
            foreach (var y in separateurs)
            {
                if (y - prev > 6)
                {
                    groupes.Add(y);
                    prev = y;
                }
            }

            int largeurLigne = Math.Min(w - xStart - 20, Math.Max((int)(w * 0.72), 1350));
            int derniereLigneBas = 0;

            for (int i = 0; i < groupes.Count; i++)
            {
                int yTop = groupes[i];
                int yBottom = (i + 1 < groupes.Count) ? groupes[i + 1] : yTop + (groupes.Count > 1 ? groupes[1] - groupes[0] : 26);
                int rowH = yBottom - yTop;

                if (rowH >= 18 && rowH <= 48)
                {
                    // Vérifier la présence d'encre (texte)
                    int ink = 0;
                    for (int x = xStart; x < xStart + largeurLigne; x += 4)
                    {
                        for (int yy = yTop + 2; yy < yBottom - 2; yy += 2)
                        {
                            int idx = yy * stride + x * 4;
                            byte b = pixels[idx], g = pixels[idx + 1], r = pixels[idx + 2];
                            double lum = 0.114 * b + 0.587 * g + 0.299 * r;
                            if (lum < 170) ink++;
                        }
                    }

                    if (ink >= 12)
                    {
                        var rect = new Int32Rect(xStart, yTop, largeurLigne, rowH);
                        var crop = new CroppedBitmap(source, rect);
                        var agrandie = new TransformedBitmap(crop, new ScaleTransform(Agrandissement, Agrandissement));
                        var sombres = PixelsPictogramme(pixels, stride, w, xStart, yTop, yBottom);
                        bandes.Add(new LigneDecoupee(EnPng(agrandie), sombres < SeuilPictogramme, sombres));
                        derniereLigneBas = yBottom;
                    }
                }
            }

            // Sous la dernière ligne : « Nombre de rendez-vous : N », que Doctolib compte sans les
            // annulés. C'est le contrôle de la lecture entière.
            byte[]? pied = null;
            if (derniereLigneBas > 0 && derniereLigneBas + 20 < h)
            {
                int hp = Math.Min(110, h - derniereLigneBas - 2);
                int lp = Math.Min(w - xStart, Math.Max(420, (int)(w * 0.25)));
                var crop = new CroppedBitmap(source, new Int32Rect(xStart, derniereLigneBas + 2, lp, hp));
                pied = EnPng(new TransformedBitmap(crop, new ScaleTransform(2, 2)));
            }

            return new DecoupageListe(bandes, pied);
        }

        /// <summary>
        /// Le ▷ des rendez-vous valides : des pixels gris foncés NEUTRES dans l'étroite colonne qui
        /// précède l'horaire. « Neutres » écarte le liseré vert du motif et la pastille bleue de
        /// l'heure courante, qui débordent dans cette zone.
        /// </summary>
        private static int PixelsPictogramme(byte[] pixels, int stride, int w, int xStart, int yTop, int yBottom)
        {
            int xFin = Math.Min(w - 1, xStart + (int)(w * 0.032));
            int sombres = 0;
            for (int y = yTop + 3; y < yBottom - 2; y++)
                for (int x = xStart; x < xFin; x++)
                {
                    int i = y * stride + x * 4;
                    byte b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];
                    int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
                    double lum = 0.114 * b + 0.587 * g + 0.299 * r;
                    if (lum < 160 && max - min < 40) sombres++;
                }
            return sombres;
        }

        /// <summary>Pixels sombres minimum pour dire qu'un ▷ est là. Mesuré le 14/10/2026 : à
        /// calibrer sur les captures, voir PLAN_JOURNEE_COMPAGNON.md.</summary>
        internal const int SeuilPictogramme = 6;

        // ── Rendez-vous annulés : le trait de rature ────────────────────────
        //
        // Mesuré le 26/09/2026 sur la vue Liste : le modèle vision lit les 19 lignes sans faute
        // mais ne voit aucun des deux traits de rature, même avec une consigne insistante — un
        // trait d'un pixel disparaît quand l'encodeur réduit l'image. L'analyse des pixels, elle,
        // les trouve tous les deux sans aucun faux positif. On repère donc le trait ici, puis on
        // ne fait lire au modèle que la ligne concernée, agrandie.

        /// <summary>Sous ce niveau de luminance, un pixel compte comme encre.</summary>
        private const int SeuilEncre = 150;

        /// <summary>Agrandissement de la bande d'une ligne rayée avant lecture.</summary>
        private const int AgrandissementBande = 3;

        /// <summary>
        /// Les lignes barrées d'une capture, découpées et agrandies : heure et nom, prêts à lire.
        /// Un trait de rature se distingue d'une bordure de champ à deux choses : il est fin, et
        /// il a des morceaux de lettres juste au-dessus et juste au-dessous.
        /// </summary>
        public static List<byte[]> BandesRayees(byte[] capture)
        {
            var bandes = new List<byte[]>();
            var source = Charger(capture);
            if (source == null) return bandes;

            int largeur = source.PixelWidth, hauteur = source.PixelHeight;
            var lum = Luminances(source);

            int xMin  = (int)(largeur * 0.05);   // écarte le bandeau latéral de Doctolib
            int lgMin = (int)(largeur * 0.015);  // tolère les noms courts (3-4 lettres)
            int lgMax = (int)(largeur * 0.25);   // tolère les noms composés longs
            int hauteurLigne = Math.Max(20, hauteur / 28);

            double Ratio(int x0, int lg, int y)
            {
                if (y < 0 || y >= hauteur) return 0;
                int n = 0;
                for (int x = x0; x < x0 + lg; x++) if (lum[y * largeur + x] < SeuilEncre) n++;
                return (double)n / lg;
            }

            // -1 et non int.MinValue : « y - dernierY » déborderait alors et la comparaison serait
            // toujours fausse — aucune rature n'était retenue (constaté le 26/09/2026).
            int dernierY = -1;

            // Épaisseur et étendue verticales adaptées à la hauteur de ligne (basse ou haute résolution)
            int deltaProche = Math.Max(2, (int)(hauteurLigne * 0.08));
            int kMax = Math.Max(6, (int)(hauteurLigne * 0.35));

            for (int y = 0; y < hauteur; y++)
            {
                int suite = 0, debut = 0;
                for (int x = xMin; x <= largeur; x++)
                {
                    if (x < largeur && lum[y * largeur + x] < SeuilEncre)
                    {
                        if (suite == 0) debut = x;
                        suite++;
                        continue;
                    }

                    if (suite >= lgMin && suite <= lgMax && (dernierY < 0 || y - dernierY > hauteurLigne))
                    {
                        double epais = Math.Max(Ratio(debut, suite, y - deltaProche), Ratio(debut, suite, y + deltaProche));
                        double dessus = 0, dessous = 0;
                        for (int k = deltaProche; k <= kMax; k++)
                        {
                            dessus  = Math.Max(dessus,  Ratio(debut, suite, y - k));
                            dessous = Math.Max(dessous, Ratio(debut, suite, y + k));
                        }

                        if (epais <= 0.45 && dessus >= 0.08 && dessous >= 0.08
                                         && dessus <= 0.65 && dessous <= 0.65)
                        {
                            bandes.Add(Bande(source, xMin, debut + suite, y, hauteurLigne));
                            dernierY = y;
                        }
                    }
                    suite = 0;
                }
            }
            return bandes;
        }

        /// <summary>La ligne entière, de la colonne des heures jusqu'à la fin du nom.</summary>
        private static byte[] Bande(BitmapSource source, int xGauche, int xFinNom, int y, int hauteurLigne)
        {
            int xDroite = Math.Min(source.PixelWidth, xFinNom + 80);
            int yHaut = Math.Max(0, y - hauteurLigne / 2);
            int h = Math.Min(source.PixelHeight - yHaut, hauteurLigne);

            var crop = new CroppedBitmap(source, new Int32Rect(xGauche, yHaut, xDroite - xGauche, h));
            return EnPng(new TransformedBitmap(crop,
                new ScaleTransform(AgrandissementBande, AgrandissementBande)));
        }

        private static byte[] Luminances(BitmapSource source)
        {
            var converti = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int largeur = converti.PixelWidth, hauteur = converti.PixelHeight, stride = largeur * 4;
            var pixels = new byte[stride * hauteur];
            converti.CopyPixels(pixels, stride, 0);

            var lum = new byte[largeur * hauteur];
            for (int i = 0, j = 0; j < lum.Length; i += 4, j++)
                lum[j] = (byte)Math.Min(255, 0.114 * pixels[i] + 0.587 * pixels[i + 1] + 0.299 * pixels[i + 2]);
            return lum;
        }

        private static BitmapSource? Charger(byte[] png)
        {
            try
            {
                using var flux = new MemoryStream(png);
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = flux;
                image.EndInit();
                image.Freeze();
                return image;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AgendaImage] capture illisible : {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Première colonne de pixels formant un trait gris continu sur la hauteur utile.
        /// Zéro si aucun trait n'est trouvé — la grille commence alors au bord de l'image.
        /// </summary>
        private static int BordGaucheGrille(BitmapSource source)
        {
            var converti = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            int largeur = converti.PixelWidth, hauteur = converti.PixelHeight;
            int stride = largeur * 4;
            var pixels = new byte[stride * hauteur];
            converti.CopyPixels(pixels, stride, 0);

            int yDebut = (int)(hauteur * 0.25), yFin = (int)(hauteur * 0.95);
            int echantillons = (yFin - yDebut) / 3;
            if (echantillons <= 0) return 0;

            // On s'arrête à la moitié de l'image : au-delà, un trait continu serait une séparation
            // entre deux jours, pas le bord de la grille.
            for (int x = 0; x < largeur / 2; x++)
            {
                int gris = 0;
                for (int y = yDebut; y < yFin; y += 3)
                {
                    int i = y * stride + x * 4;
                    int b = pixels[i], v = pixels[i + 1], r = pixels[i + 2];
                    if (Math.Abs(r - v) < 12 && Math.Abs(v - b) < 12 && r >= 190 && r <= 240) gris++;
                }
                if (gris >= echantillons * 0.85) return x + 1;
            }
            return 0;
        }

        private static byte[] EnPng(BitmapSource image)
        {
            var encodeur = new PngBitmapEncoder();
            encodeur.Frames.Add(BitmapFrame.Create(image));
            using var ms = new MemoryStream();
            encodeur.Save(ms);
            return ms.ToArray();
        }
    }
}
