using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.IO;
using QRCoder;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service de génération de PDF avec QR code pour les tokens Parent'aile
    /// Utilise un template PDF avec champs de formulaire (AcroForms) et superpose le QR code
    /// Champs attendus dans le template: nom_prenom, date_creation, token_id
    /// </summary>
    public class TokenPdfService
    {
        private readonly string _templatePath;
        private readonly string _baseUrl;
        private static bool _fontResolverInitialized = false;

        /// <summary>
        /// Initialise le FontResolver pour PDFsharp (une seule fois)
        /// </summary>
        private static void EnsureFontResolverInitialized()
        {
            if (!_fontResolverInitialized)
            {
                if (GlobalFontSettings.FontResolver == null)
                {
                    GlobalFontSettings.FontResolver = new TokenFontResolver();
                }
                _fontResolverInitialized = true;
            }
        }

        /// <summary>
        /// FontResolver simple pour les polices système Windows
        /// </summary>
        private class TokenFontResolver : IFontResolver
        {
            public byte[]? GetFont(string faceName)
            {
                var fontPath = faceName.ToLowerInvariant() switch
                {
                    "arial" => @"C:\Windows\Fonts\arial.ttf",
                    "arial bold" => @"C:\Windows\Fonts\arialbd.ttf",
                    "helvetica" => @"C:\Windows\Fonts\arial.ttf",
                    "calibri" => @"C:\Windows\Fonts\calibri.ttf",
                    _ => @"C:\Windows\Fonts\arial.ttf"
                };

                return File.Exists(fontPath) ? File.ReadAllBytes(fontPath) : null;
            }

            public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
            {
                var fontName = familyName.ToLowerInvariant();
                if (bold) fontName += " bold";
                if (italic) fontName += " italic";
                return new FontResolverInfo(fontName);
            }
        }

        // Coordonnées en points (1 cm = 28.35 points)
        private const double CM_TO_POINTS = 28.35;

        // Position QR Code (depuis le coin supérieur gauche)
        // Ajusté selon le template
        private double QrCodeX = 0.8 * CM_TO_POINTS;   // 1.8 cm du bord gauche (0.3 + 1.5)
        private double QrCodeY = 8.13 * CM_TO_POINTS;  // 8.13 cm du haut
        private double QrCodeSize = 3.5 * CM_TO_POINTS; // 3.5 cm (taille du QR)

        public TokenPdfService(string? templatePath = null, string baseUrl = "https://parentaile.fr/espace")
        {
            EnsureFontResolverInitialized();

            _baseUrl = baseUrl;

            if (templatePath != null && File.Exists(templatePath))
            {
                _templatePath = templatePath;
            }
            else
            {
                var appDir = AppDomain.CurrentDomain.BaseDirectory;
                _templatePath = Path.Combine(appDir, "Assets", "Templates", "TokenTemplate.pdf");
            }
        }

        /// <summary>
        /// Génère un PDF personnalisé avec le QR code et les infos patient
        /// Remplit les champs de formulaire: nom_prenom, date_creation, token_id
        /// Et superpose l'image QR code à la position définie
        /// </summary>
        public string GenerateTokenPdf(string tokenId, string patientName, DateTime creationDate, string? outputPath = null)
        {
            if (!File.Exists(_templatePath))
            {
                throw new FileNotFoundException($"Template PDF non trouvé: {_templatePath}");
            }

            // Déterminer le chemin de sortie
            if (string.IsNullOrEmpty(outputPath))
            {
                var tempDir = Path.GetTempPath();
                outputPath = Path.Combine(tempDir, $"Token_{tokenId}_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
            }

            // Copier le template vers la destination
            File.Copy(_templatePath, outputPath, overwrite: true);

            // Ouvrir le PDF copié en mode modification
            var document = PdfReader.Open(outputPath, PdfDocumentOpenMode.Modify);

            try
            {
                // 1. Remplir les champs de formulaire (AcroForms)
                if (document.AcroForm != null)
                {
                    // Activer la régénération des apparences
                    if (document.AcroForm.Elements.ContainsKey("/NeedAppearances"))
                    {
                        document.AcroForm.Elements.SetBoolean("/NeedAppearances", true);
                    }
                    else
                    {
                        document.AcroForm.Elements.Add("/NeedAppearances", new PdfBoolean(true));
                    }

                    // Remplir les champs
                    SetFieldValue(document, "nom_et_prénom", patientName);
                    SetFieldValue(document, "date_creation", creationDate.ToString("dd/MM/yyyy"));
                    SetFieldValue(document, "token_id", tokenId);

                    System.Diagnostics.Debug.WriteLine($"[TokenPdfService] Champs remplis: nom_prenom={patientName}, date={creationDate:dd/MM/yyyy}, token={tokenId}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[TokenPdfService] Le PDF ne contient pas de formulaire AcroForm");
                }

                // 2. Ajouter le QR code - chercher le champ qr_code d'abord
                var page = document.Pages[0];

                // Lister tous les champs pour debug
                if (document.AcroForm != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[TokenPdfService] Champs du template ({document.AcroForm.Fields.Count}):");
                    foreach (var name in document.AcroForm.Fields.Names)
                    {
                        var f = document.AcroForm.Fields[name];
                        var ft = f?.Elements.GetString("/FT") ?? "?";
                        var r = f?.Elements.GetArray("/Rect");
                        System.Diagnostics.Debug.WriteLine($"  - '{name}' type={ft} rect={r}");
                    }
                }

                double qrX = QrCodeX, qrY = QrCodeY, qrW = QrCodeSize, qrH = QrCodeSize;

                // Chercher le champ qr_code et récupérer sa position
                var qrField = FindField(document, "qr_code");
                if (qrField != null)
                {
                    var rect = GetFieldRect(qrField, page);
                    if (rect.HasValue)
                    {
                        qrX = rect.Value.x;
                        qrY = rect.Value.y;
                        qrW = rect.Value.width;
                        qrH = rect.Value.height;
                        System.Diagnostics.Debug.WriteLine($"[TokenPdfService] Champ qr_code trouvé: x={qrX / CM_TO_POINTS:F2}cm, y={qrY / CM_TO_POINTS:F2}cm, w={qrW / CM_TO_POINTS:F2}cm, h={qrH / CM_TO_POINTS:F2}cm");

                        // Masquer le champ pour supprimer les bordures interactives
                        HideField(qrField);
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine("[TokenPdfService] Champ qr_code trouvé mais sans Rect, utilisation des coordonnées par défaut");
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[TokenPdfService] Champ qr_code non trouvé, utilisation des coordonnées par défaut");
                }

                using var gfx = XGraphics.FromPdfPage(page);
                AddQrCode(gfx, tokenId, qrX, qrY, qrW, qrH);

                // Sauvegarder
                document.Save(outputPath);
            }
            finally
            {
                document.Close();
            }

            return outputPath;
        }

        /// <summary>
        /// Remplit un champ de formulaire avec police auto-size et gras
        /// </summary>
        private void SetFieldValue(PdfDocument document, string fieldName, string value)
        {
            try
            {
                var field = document.AcroForm?.Fields[fieldName];

                // Si le champ n'est pas trouvé, essayer avec des espaces trailing
                if (field == null && document.AcroForm != null)
                {
                    foreach (var name in document.AcroForm.Fields.Names)
                    {
                        if (name.Trim() == fieldName || name.TrimEnd() == fieldName)
                        {
                            field = document.AcroForm.Fields[name];
                            System.Diagnostics.Debug.WriteLine($"[TokenPdfService] Champ '{fieldName}' trouvé sous le nom '{name}'");
                            break;
                        }
                    }
                }

                if (field != null && !string.IsNullOrWhiteSpace(value))
                {
                    // Configurer la police: Helvetica, taille auto (0), couleur noire
                    field.Elements.SetString("/DA", "/Helv 0 Tf 0 g");

                    // Centrer le texte (Q: 0=gauche, 1=centre, 2=droite)
                    field.Elements.SetInteger("/Q", 1);

                    // Définir la valeur
                    field.Value = new PdfString(value);
                    System.Diagnostics.Debug.WriteLine($"[TokenPdfService] ✓ Champ '{fieldName}' = {value}");
                }
                else if (field == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[TokenPdfService] ⚠ Champ '{fieldName}' introuvable");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TokenPdfService] ✗ Erreur champ '{fieldName}': {ex.Message}");
            }
        }

        /// <summary>
        /// Ajoute le QR code sur la page à la position spécifiée
        /// Le QR code est centré et carré dans la zone donnée
        /// </summary>
        private void AddQrCode(XGraphics gfx, string tokenId, double x, double y, double width, double height)
        {
            var url = $"{_baseUrl}?token={tokenId}";

            // Générer le QR code avec QRCoder
            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new QRCode(qrCodeData);
            using var qrBitmap = qrCode.GetGraphic(20);

            // Sauvegarder en BMP temporaire
            var tempQrPath = Path.Combine(Path.GetTempPath(), $"qr_{tokenId}_{DateTime.Now.Ticks}.bmp");
            qrBitmap.Save(tempQrPath, ImageFormat.Bmp);

            try
            {
                var qrImage = XImage.FromFile(tempQrPath);

                // Le QR code doit être carré - utiliser la plus petite dimension
                var size = Math.Min(width, height);
                // Centrer dans la zone du champ
                var drawX = x + (width - size) / 2;
                var drawY = y + (height - size) / 2;

                gfx.DrawImage(qrImage, drawX, drawY, size, size);
                System.Diagnostics.Debug.WriteLine($"[TokenPdfService] QR code ajouté à ({drawX / CM_TO_POINTS:F2}cm, {drawY / CM_TO_POINTS:F2}cm, {size / CM_TO_POINTS:F2}cm)");
            }
            finally
            {
                try { File.Delete(tempQrPath); } catch { }
            }
        }

        /// <summary>
        /// Cherche un champ de formulaire par nom (insensible à la casse et aux espaces)
        /// </summary>
        private PdfAcroField? FindField(PdfDocument document, string fieldName)
        {
            if (document.AcroForm == null) return null;

            // Recherche directe
            var field = document.AcroForm.Fields[fieldName];
            if (field != null) return field;

            // Recherche insensible à la casse et aux espaces
            foreach (var name in document.AcroForm.Fields.Names)
            {
                if (name.Trim().Equals(fieldName, StringComparison.OrdinalIgnoreCase) ||
                    name.Replace("_", "").Trim().Equals(fieldName.Replace("_", ""), StringComparison.OrdinalIgnoreCase))
                {
                    return document.AcroForm.Fields[name];
                }
            }
            return null;
        }

        /// <summary>
        /// Récupère le rectangle (position + taille) d'un champ de formulaire
        /// Convertit des coordonnées PDF (origine bas-gauche) vers XGraphics (origine haut-gauche)
        /// </summary>
        private (double x, double y, double width, double height)? GetFieldRect(PdfAcroField field, PdfPage page)
        {
            // Essayer le Rect directement sur le champ
            var rectArray = field.Elements.GetArray("/Rect");

            // Si pas trouvé, chercher dans les annotations widgets (Kids)
            if (rectArray == null)
            {
                var kids = field.Elements.GetArray("/Kids");
                if (kids != null && kids.Elements.Count > 0)
                {
                    var widget = kids.Elements.GetDictionary(0);
                    rectArray = widget?.Elements.GetArray("/Rect");
                }
            }

            if (rectArray == null || rectArray.Elements.Count < 4) return null;

            // Coordonnées PDF: [llx, lly, urx, ury] (origine bas-gauche)
            double llx = rectArray.Elements.GetReal(0);
            double lly = rectArray.Elements.GetReal(1);
            double urx = rectArray.Elements.GetReal(2);
            double ury = rectArray.Elements.GetReal(3);

            double width = Math.Abs(urx - llx);
            double height = Math.Abs(ury - lly);

            // Convertir vers XGraphics (origine haut-gauche)
            double pageHeight = page.Height.Point;
            double x = Math.Min(llx, urx);
            double y = pageHeight - Math.Max(lly, ury);

            System.Diagnostics.Debug.WriteLine($"[TokenPdfService] Rect PDF: [{llx:F1}, {lly:F1}, {urx:F1}, {ury:F1}] -> XGraphics: ({x:F1}, {y:F1}, {width:F1}x{height:F1})");

            return (x, y, width, height);
        }

        /// <summary>
        /// Masque un champ de formulaire (supprime bordures interactives)
        /// </summary>
        private void HideField(PdfAcroField field)
        {
            // Flag bit 2 = Hidden dans les annotations
            var currentFlags = field.Elements.GetInteger("/F");
            field.Elements.SetInteger("/F", currentFlags | 2);

            // Aussi masquer les widgets enfants
            var kids = field.Elements.GetArray("/Kids");
            if (kids != null)
            {
                for (int i = 0; i < kids.Elements.Count; i++)
                {
                    var widget = kids.Elements.GetDictionary(i);
                    if (widget != null)
                    {
                        var wFlags = widget.Elements.GetInteger("/F");
                        widget.Elements.SetInteger("/F", wFlags | 2);
                    }
                }
            }
        }

        /// <summary>
        /// Configure la position du QR code (en cm depuis le coin supérieur gauche)
        /// </summary>
        public void SetQrCodePosition(double x, double y, double size)
        {
            QrCodeX = x * CM_TO_POINTS;
            QrCodeY = y * CM_TO_POINTS;
            QrCodeSize = size * CM_TO_POINTS;
        }

        /// <summary>
        /// Vérifie si le template existe
        /// </summary>
        public bool TemplateExists() => File.Exists(_templatePath);

        /// <summary>
        /// Obtient le chemin du template
        /// </summary>
        public string GetTemplatePath() => _templatePath;

        /// <summary>
        /// Liste les champs de formulaire disponibles dans le template (pour debug)
        /// Format: "nom_champ [type] (rect)"
        /// </summary>
        public string[] ListFormFields()
        {
            if (!File.Exists(_templatePath))
                return Array.Empty<string>();

            try
            {
                var document = PdfReader.Open(_templatePath, PdfDocumentOpenMode.Import);
                if (document.AcroForm == null)
                {
                    document.Close();
                    return Array.Empty<string>();
                }

                var fieldNames = new string[document.AcroForm.Fields.Count];
                for (int i = 0; i < document.AcroForm.Fields.Count; i++)
                {
                    var name = document.AcroForm.Fields.Names[i];
                    var field = document.AcroForm.Fields[name];
                    var ft = field?.Elements.GetString("/FT") ?? "?";
                    var rect = field?.Elements.GetArray("/Rect");
                    fieldNames[i] = $"{name} [{ft}] rect={rect}";
                }

                document.Close();
                return fieldNames;
            }
            catch
            {
                return Array.Empty<string>();
            }
        }
    }
}
