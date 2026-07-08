using System;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace MedCompanion.Services.Livres
{
    /// <summary>
    /// Import d'un texte existant (essai commencé ailleurs) dans l'Atelier
    /// d'écriture : .docx (Word/Google Docs) et .odt (LibreOffice) avec
    /// gras/italique/titres convertis en markdown, .txt/.md tels quels,
    /// .pdf en dernier recours (PdfPig, structure des paragraphes non garantie).
    /// </summary>
    public class LivreImportService
    {
        public static readonly string FiltreDialog =
            "Textes (odt, docx, txt, md, pdf)|*.odt;*.docx;*.txt;*.md;*.markdown;*.pdf|Tous les fichiers|*.*";

        public (bool success, string texte, string? error) ExtraireTexte(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return (false, "", "Fichier introuvable.");

                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                return ext switch
                {
                    ".txt" or ".md" or ".markdown" => (true, File.ReadAllText(filePath, Encoding.UTF8), null),
                    ".docx" => ExtraireDocx(filePath),
                    ".odt"  => ExtraireOdt(filePath),
                    ".pdf"  => ExtrairePdf(filePath),
                    _ => (false, "", $"Format non pris en charge : {ext}. Utilisez .odt, .docx, .txt, .md ou .pdf.")
                };
            }
            catch (Exception ex)
            {
                return (false, "", $"Erreur import : {ex.Message}");
            }
        }

        // ── DOCX → markdown léger ───────────────────────────────────────────

        private static (bool, string, string?) ExtraireDocx(string filePath)
        {
            try
            {
                using var doc = WordprocessingDocument.Open(filePath, false);
                var body = doc.MainDocumentPart?.Document?.Body;
                if (body == null) return (false, "", "Document Word vide ou illisible.");

                var sb = new StringBuilder();
                foreach (var para in body.Elements<Paragraph>())
                {
                    var ligne = RenderParagraphe(para);
                    sb.AppendLine(ligne);
                    sb.AppendLine(); // un paragraphe Word = un paragraphe markdown
                }

                // Compacter les lignes vides multiples laissées par les paragraphes vides
                var texte = System.Text.RegularExpressions.Regex.Replace(
                    sb.ToString().Replace("\r\n", "\n").Trim(), @"\n{3,}", "\n\n");

                return (true, texte, null);
            }
            catch (Exception ex)
            {
                return (false, "", $"Erreur lecture .docx : {ex.Message}");
            }
        }

        private static string RenderParagraphe(Paragraph para)
        {
            // Titres Word/Google Docs → sous-titres markdown (## / ###)
            var styleId = para.ParagraphProperties?.ParagraphStyleId?.Val?.Value ?? "";
            string prefix = styleId switch
            {
                "Heading1" or "Titre1" or "Title" or "Titre" => "## ",
                "Heading2" or "Titre2" => "## ",
                "Heading3" or "Titre3" => "### ",
                _ => ""
            };

            var sb = new StringBuilder(prefix);
            foreach (var run in para.Elements<Run>())
            {
                var texte = string.Concat(run.Elements<Text>().Select(t => t.Text));
                if (texte.Length == 0) continue;

                bool gras     = EstActif(run.RunProperties?.Bold);
                bool italique = EstActif(run.RunProperties?.Italic);

                // Le marqueur markdown ne colle pas aux espaces de bord du run
                var (avant, coeur, apres) = DecouperEspaces(texte);
                if (coeur.Length > 0)
                {
                    if (gras) coeur = $"**{coeur}**";
                    else if (italique) coeur = $"*{coeur}*";
                }
                sb.Append(avant).Append(coeur).Append(apres);
            }
            return sb.ToString().TrimEnd();
        }

        private static bool EstActif(DocumentFormat.OpenXml.Wordprocessing.OnOffType? prop)
            => prop != null && (prop.Val == null || prop.Val.Value);

        private static (string avant, string coeur, string apres) DecouperEspaces(string texte)
        {
            int debut = 0, fin = texte.Length;
            while (debut < fin && char.IsWhiteSpace(texte[debut])) debut++;
            while (fin > debut && char.IsWhiteSpace(texte[fin - 1])) fin--;
            return (texte[..debut], texte[debut..fin], texte[fin..]);
        }

        // ── ODT (LibreOffice) → markdown léger ─────────────────────────────
        // Un .odt est un zip contenant content.xml (format OpenDocument).
        // On lit les paragraphes (text:p), titres (text:h) et spans formatés
        // en résolvant les styles automatiques (gras/italique).

        private static readonly System.Xml.Linq.XNamespace NsOffice = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
        private static readonly System.Xml.Linq.XNamespace NsText   = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
        private static readonly System.Xml.Linq.XNamespace NsStyle  = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
        private static readonly System.Xml.Linq.XNamespace NsFo     = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";

        private static (bool, string, string?) ExtraireOdt(string filePath)
        {
            try
            {
                using var zip = System.IO.Compression.ZipFile.OpenRead(filePath);
                var entry = zip.GetEntry("content.xml");
                if (entry == null) return (false, "", "content.xml introuvable — fichier .odt invalide.");

                System.Xml.Linq.XDocument xml;
                using (var stream = entry.Open())
                    xml = System.Xml.Linq.XDocument.Load(stream);

                // 1. Styles (automatiques + communs) → gras/italique par nom de style
                var stylesFormat = new System.Collections.Generic.Dictionary<string, (bool gras, bool italique)>();
                foreach (var style in xml.Descendants(NsStyle + "style"))
                {
                    var nom = style.Attribute(NsStyle + "name")?.Value;
                    if (nom == null) continue;
                    var props = style.Element(NsStyle + "text-properties");
                    if (props == null) continue;
                    bool gras     = props.Attribute(NsFo + "font-weight")?.Value == "bold";
                    bool italique = props.Attribute(NsFo + "font-style")?.Value == "italic";
                    if (gras || italique) stylesFormat[nom] = (gras, italique);
                }

                // 2. Corps du texte
                var body = xml.Root?.Element(NsOffice + "body")?.Element(NsOffice + "text");
                if (body == null) return (false, "", "Corps du document introuvable.");

                var sb = new StringBuilder();
                foreach (var elem in body.Elements())
                {
                    if (elem.Name == NsText + "h")
                    {
                        int niveau = int.TryParse(elem.Attribute(NsText + "outline-level")?.Value, out var n) ? n : 1;
                        sb.AppendLine((niveau >= 3 ? "### " : "## ") + RenderOdtInline(elem, stylesFormat, ignorerFormat: true));
                        sb.AppendLine();
                    }
                    else if (elem.Name == NsText + "p")
                    {
                        sb.AppendLine(RenderOdtInline(elem, stylesFormat, ignorerFormat: false));
                        sb.AppendLine();
                    }
                    // listes, tableaux, etc. : ignorés en V1 (texte littéraire)
                }

                var texte = System.Text.RegularExpressions.Regex.Replace(
                    sb.ToString().Replace("\r\n", "\n").Trim(), @"\n{3,}", "\n\n");
                return (true, texte, null);
            }
            catch (Exception ex)
            {
                return (false, "", $"Erreur lecture .odt : {ex.Message}");
            }
        }

        private static string RenderOdtInline(
            System.Xml.Linq.XElement parent,
            System.Collections.Generic.Dictionary<string, (bool gras, bool italique)> styles,
            bool ignorerFormat)
        {
            var sb = new StringBuilder();
            foreach (var node in parent.Nodes())
            {
                if (node is System.Xml.Linq.XText txt)
                {
                    sb.Append(txt.Value);
                }
                else if (node is System.Xml.Linq.XElement el)
                {
                    if (el.Name == NsText + "span")
                    {
                        var contenu = RenderOdtInline(el, styles, ignorerFormat);
                        var styleNom = el.Attribute(NsText + "style-name")?.Value ?? "";
                        if (!ignorerFormat && styles.TryGetValue(styleNom, out var fmt))
                        {
                            var (avant, coeur, apres) = DecouperEspaces(contenu);
                            if (coeur.Length > 0)
                            {
                                if (fmt.gras) coeur = $"**{coeur}**";
                                else if (fmt.italique) coeur = $"*{coeur}*";
                            }
                            contenu = avant + coeur + apres;
                        }
                        sb.Append(contenu);
                    }
                    else if (el.Name == NsText + "s")
                    {
                        int count = int.TryParse(el.Attribute(NsText + "c")?.Value, out var c) ? c : 1;
                        sb.Append(new string(' ', count));
                    }
                    else if (el.Name == NsText + "tab")
                    {
                        sb.Append('\t');
                    }
                    else if (el.Name == NsText + "line-break")
                    {
                        sb.Append('\n');
                    }
                    else
                    {
                        // notes, liens… : on garde le texte brut
                        sb.Append(RenderOdtInline(el, styles, ignorerFormat));
                    }
                }
            }
            return sb.ToString();
        }

        // ── PDF (secours) ───────────────────────────────────────────────────

        private static (bool, string, string?) ExtrairePdf(string filePath)
        {
            try
            {
                using var document = UglyToad.PdfPig.PdfDocument.Open(filePath);
                var sb = new StringBuilder();
                foreach (var page in document.GetPages())
                {
                    sb.AppendLine(page.Text);
                    sb.AppendLine();
                }
                return (true, sb.ToString().Trim(), null);
            }
            catch (Exception ex)
            {
                return (false, "", $"Erreur lecture PDF : {ex.Message}");
            }
        }
    }
}
