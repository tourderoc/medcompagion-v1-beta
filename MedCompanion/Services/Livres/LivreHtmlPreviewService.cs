using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MedCompanion.Models.Livres;

namespace MedCompanion.Services.Livres
{
    /// <summary>
    /// Produit le HTML d'aperçu d'un livre pour affichage dans un WebView2,
    /// selon le même principe que RestitutionHtmlPreviewService : le HTML de
    /// la preview est identique à celui converti en PDF (Edge headless), ce qui
    /// garantit « ce que je vois = ce que j'imprime ».
    ///
    /// La mise en page (format de page, marges, police, interligne…) vient de
    /// MiseEnPageLivre et est traduite en CSS, y compris @page pour le PDF.
    /// À l'écran chaque chapitre est une « feuille » blanche à la largeur du
    /// format choisi ; à l'impression le texte coule sur autant de pages que
    /// nécessaire, chaque chapitre démarrant sur une nouvelle page.
    /// </summary>
    public class LivreHtmlPreviewService
    {
        private static readonly Regex _boldRx   = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
        private static readonly Regex _italicRx = new(@"(?<!\*)\*([^*\n]+)\*(?!\*)", RegexOptions.Compiled);

        public string BuildPreviewHtml(Livre livre, List<(ChapitreLivre chapitre, string contenu)> chapitres)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html lang='fr'><head><meta charset='UTF-8'/>");
            sb.AppendLine("<style>");
            sb.AppendLine(BuildCss(livre.MiseEnPage));
            sb.AppendLine("</style></head><body>");

            // Page de couverture sobre : titre + auteur
            sb.AppendLine("<div class='page cover'>");
            sb.AppendLine($"  <div class='cover-title'>{WebUtility.HtmlEncode(livre.Titre)}</div>");
            if (!string.IsNullOrWhiteSpace(livre.Auteur))
                sb.AppendLine($"  <div class='cover-author'>{WebUtility.HtmlEncode(livre.Auteur)}</div>");
            sb.AppendLine("</div>");

            foreach (var (chapitre, contenu) in chapitres)
            {
                sb.AppendLine("<div class='page chapitre'>");
                sb.AppendLine($"  <h1 class='chap-title'>{WebUtility.HtmlEncode(chapitre.Titre)}</h1>");
                sb.AppendLine($"  <div class='chap-content'>{RenderMarkdown(contenu)}</div>");
                sb.AppendLine("</div>");
            }

            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        // ── CSS depuis la mise en page ──────────────────────────────────────

        private static string BuildCss(MiseEnPageLivre mep)
        {
            var (w, h) = mep.GetDimensionsMm();
            string align  = mep.Justifie ? "justify" : "left";
            string indent = mep.RetraitPremiereLigne ? "1.5em" : "0";
            // Police avec fallback serif ; guillemets si nom composé
            string police = mep.Police.Contains(' ') ? $"'{mep.Police}', serif" : $"{mep.Police}, serif";

            return $@"
* {{ box-sizing: border-box; }}
html, body {{ margin: 0; padding: 0; background: #DDD; }}
body {{ padding: 20px; }}

.page {{
  background: white;
  width: {w}mm;
  min-height: {h}mm;
  margin: 0 auto 20px auto;
  box-shadow: 0 4px 12px rgba(0,0,0,0.15);
  padding: {mep.MargeHautMm}mm {mep.MargeDroiteMm}mm {mep.MargeBasMm}mm {mep.MargeGaucheMm}mm;
  font-family: {police};
  font-size: {mep.TaillePt.ToString(System.Globalization.CultureInfo.InvariantCulture)}pt;
  line-height: {mep.Interligne.ToString(System.Globalization.CultureInfo.InvariantCulture)};
  color: #1A1A1A;
}}

.cover {{
  display: flex;
  flex-direction: column;
  justify-content: center;
  align-items: center;
  text-align: center;
  height: {h}mm;
}}
.cover-title  {{ font-size: 2.4em; font-weight: bold; letter-spacing: 2px; text-transform: uppercase; }}
.cover-author {{ font-size: 1.1em; font-style: italic; margin-top: 2em; color: #444; }}

.chap-title {{
  font-size: 1.5em;
  font-weight: bold;
  font-style: italic;
  margin: 0 0 1.8em 0;
  text-align: left;
}}
.chap-content p {{
  margin: 0 0 0.6em 0;
  text-align: {align};
  text-indent: {indent};
}}
.chap-content p.dialogue {{ text-indent: 0; }}
.chap-content h2 {{ font-size: 1.2em; margin: 1.4em 0 0.8em 0; }}
.chap-content h3 {{ font-size: 1.05em; margin: 1.2em 0 0.6em 0; }}
.chap-content .separateur {{ text-align: center; text-indent: 0; margin: 1.2em 0; letter-spacing: 8px; }}

/* ── Rendu PDF (Edge headless --print-to-pdf) ──
   Les marges passent par @page (répétées sur chaque page imprimée),
   car le padding d'un bloc ne se répète pas quand un chapitre long
   coule sur plusieurs pages. */
@page {{ size: {w}mm {h}mm; margin: {mep.MargeHautMm}mm {mep.MargeDroiteMm}mm {mep.MargeBasMm}mm {mep.MargeGaucheMm}mm; }}
@media print {{
  html, body {{ background: white !important; padding: 0 !important; margin: 0 !important; }}
  .page {{
    box-shadow: none !important;
    margin: 0 !important;
    padding: 0 !important;
    width: auto;
    min-height: 0;
    break-before: page;
  }}
  .page:first-child {{ break-before: avoid; }}
  .cover {{ height: {h - mep.MargeHautMm - mep.MargeBasMm}mm; }}
}}";
        }

        // ── Rendu Markdown minimal adapté au texte littéraire ───────────────

        /// <summary>
        /// Convertit le texte du chapitre en HTML : paragraphes, **gras**,
        /// *italique*, ## sous-titres, *** séparateur de scène, tirets de
        /// dialogue (— ou -) sans retrait de première ligne.
        /// </summary>
        public static string RenderMarkdown(string? contenu)
        {
            if (string.IsNullOrWhiteSpace(contenu)) return "<p class='placeholder'></p>";

            var sb = new StringBuilder();
            // Un paragraphe = bloc séparé par ligne(s) vide(s) ; un simple retour
            // à la ligne dans le bloc reste un saut de ligne (comme Google Docs).
            var blocs = Regex.Split(contenu.Replace("\r\n", "\n").Trim(), @"\n\s*\n");

            foreach (var bloc in blocs)
            {
                var trimmed = bloc.Trim();
                if (trimmed.Length == 0) continue;

                if (trimmed == "***" || trimmed == "* * *")
                {
                    sb.AppendLine("<p class='separateur'>* * *</p>");
                    continue;
                }
                if (trimmed.StartsWith("### "))
                {
                    sb.AppendLine($"<h3>{Inline(trimmed.Substring(4))}</h3>");
                    continue;
                }
                if (trimmed.StartsWith("## "))
                {
                    sb.AppendLine($"<h2>{Inline(trimmed.Substring(3))}</h2>");
                    continue;
                }

                // Chaque ligne du bloc = un paragraphe (répliques de dialogue successives)
                foreach (var ligne in trimmed.Split('\n'))
                {
                    var l = ligne.Trim();
                    if (l.Length == 0) continue;
                    bool dialogue = l.StartsWith("—") || l.StartsWith("－") || l.StartsWith("- ");
                    sb.AppendLine(dialogue
                        ? $"<p class='dialogue'>{Inline(l)}</p>"
                        : $"<p>{Inline(l)}</p>");
                }
            }

            return sb.ToString();
        }

        private static string Inline(string texte)
        {
            var html = WebUtility.HtmlEncode(texte);
            html = _boldRx.Replace(html, "<strong>$1</strong>");
            html = _italicRx.Replace(html, "<em>$1</em>");
            return html;
        }
    }
}
