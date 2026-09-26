using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using MedCompanion.Models.Infographies;
using MedCompanion.Models.Restitutions;
using MedCompanion.Services.Infographies;

namespace MedCompanion.Services.Restitutions
{
    /// <summary>
    /// Annexe Infographies — les fiches d'information de la bibliothèque que le médecin a
    /// jointes au dossier, une par page, entre l'annexe contacts et l'annexe méthodologique.
    ///
    /// Rien n'est ajouté tout seul : le dossier ne porte que les fiches cochées dans l'éditeur.
    /// Une fiche retirée de la bibliothèque, ou repassée « à relire » depuis qu'elle a été
    /// cochée, est simplement ignorée — le dossier ne doit pas sortir avec une page non relue.
    /// </summary>
    public partial class RestitutionHtmlPreviewService
    {
        private readonly InfographieService _infographieService = new();

        private string BuildAnnexesInfographiesPages(DossierRestitutionInitial dossier)
        {
            if (dossier.InfographiesIds.Count == 0) return "";

            var fiches = FichesJointes(dossier);
            if (fiches.Count == 0) return "";

            var sb = new StringBuilder();
            sb.AppendLine(BuildInfographiesCss());
            foreach (var fiche in fiches) sb.Append(RendrePageInfographie(fiche));
            return sb.ToString();
        }

        /// <summary>Les fiches cochées qui existent encore et sont toujours validées, dans l'ordre choisi.</summary>
        internal List<Infographie> FichesJointes(DossierRestitutionInitial dossier)
        {
            var (ok, toutes, _) = _infographieService.ListFiches();
            if (!ok) return new List<Infographie>();

            var parId = toutes.Where(f => f.EstValidee).ToDictionary(f => f.Id);
            return dossier.InfographiesIds
                .Where(parId.ContainsKey)
                .Select(id => parId[id])
                .Where(f => File.Exists(f.ImagePath))
                .ToList();
        }

        private static string RendrePageInfographie(Infographie fiche)
        {
            var base64 = LoadBase64(fiche.ImagePath);
            if (base64.Length == 0) return "";

            var mime = Path.GetExtension(fiche.Fichier).ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".bmp"            => "image/bmp",
                _                 => "image/png"
            };

            var sousTitre = new List<string>();
            if (!string.IsNullOrWhiteSpace(fiche.Type)) sousTitre.Add(fiche.Type);
            if (fiche.Sujets.Count > 0) sousTitre.Add(string.Join(", ", fiche.Sujets));

            // Une infographie paysage n'occuperait qu'un tiers d'une A4 portrait. Toute la page
            // bascule alors — en-tête et pied compris — pour que le lecteur tourne la feuille
            // une fois et lise l'ensemble dans le même sens.
            var paysage = EstPaysage(fiche.ImagePath);

            var sb = new StringBuilder();
            sb.AppendLine(paysage ? "<div class='page ai-page ai-land'><div class='ai-land-inner'>"
                                  : "<div class='page ai-page'>");
            sb.AppendLine("  <div class='ai-header'>");
            sb.AppendLine("    <div>");
            sb.AppendLine("      <span class='ai-badge'>Annexe — Fiche d'information</span>");
            sb.AppendLine($"      <h1>{WebUtility.HtmlEncode(fiche.Titre)}</h1>");
            if (sousTitre.Count > 0)
                sb.AppendLine($"      <div class='ai-header-sub'>{WebUtility.HtmlEncode(string.Join(" · ", sousTitre))}</div>");
            sb.AppendLine("    </div>");
            sb.AppendLine("  </div>");
            sb.AppendLine("  <hr class='ai-hr'>");
            sb.AppendLine("  <div class='ai-image-zone'>");
            sb.AppendLine($"    <img class='ai-image' src='data:{mime};base64,{base64}' alt='{WebUtility.HtmlEncode(fiche.Titre)}'>");
            sb.AppendLine("  </div>");
            sb.AppendLine("  <div class='ai-footer'>");
            sb.AppendLine("    Fiche d'information remise aux parents — elle accompagne la consultation et ne remplace pas l'avis du médecin.<br>");
            sb.AppendLine("    Approche systémique et développementale Dr Lassoued Nair, Pédopsychiatre");
            sb.AppendLine("  </div>");
            sb.AppendLine(paysage ? "</div></div>" : "</div>");
            return sb.ToString();
        }

        /// <summary>Vrai si l'image est plus large que haute. Faux si elle est illisible.</summary>
        private static bool EstPaysage(string chemin)
        {
            var bmp = Services.Infographies.InfographieImpression.Charger(chemin, 64);
            return bmp != null && bmp.PixelWidth > bmp.PixelHeight;
        }

        // La zone d'image occupe toute la hauteur restante et l'image s'y inscrit entière
        // (`object-fit: contain`) : une infographie paysage de NotebookLM tient en largeur,
        // une portrait en hauteur, sans jamais déborder ni forcer une seconde page.
        private static string BuildInfographiesCss() => @"<style>
.ai-page {
  /* hauteur ferme : sans elle, la zone d'image n'a pas de hauteur à remplir */
  height: 297mm;
  box-sizing: border-box;
  padding: 26px 40px 30px 40px;
  font-family: 'Segoe UI', Arial, sans-serif;
  color: #112240;
  display: flex;
  flex-direction: column;
}
.ai-badge {
  display: inline-block;
  background: #1A3A6A;
  color: white;
  font-size: 8.5px;
  font-weight: 700;
  letter-spacing: 1.1px;
  text-transform: uppercase;
  padding: 3px 9px;
  border-radius: 3px;
}
.ai-header h1 {
  font-size: 19px;
  font-weight: 700;
  margin: 7px 0 0 0;
  color: #1A3A6A;
}
.ai-header-sub {
  font-size: 11px;
  color: #5D6D7E;
  margin-top: 3px;
}
.ai-hr {
  border: none;
  border-top: 1px solid #D6DEE8;
  margin: 12px 0 14px 0;
}
.ai-image-zone {
  flex: 1 1 auto;
  min-height: 0;
  display: flex;
  align-items: center;
  justify-content: center;
}
.ai-image {
  max-width: 100%;
  max-height: 100%;
  object-fit: contain;
}
/* Page basculée : le contenu est mis en page en 297×210 mm puis pivoté d'un quart de tour
   au centre de la feuille. La rotation ne change pas la place occupée dans le flux, d'où le
   positionnement absolu — la page hôte est en position:relative et masque ce qui dépasse. */
.ai-land { padding: 0; }
.ai-land-inner {
  position: absolute;
  top: 50%;
  left: 50%;
  width: 297mm;
  height: 210mm;
  transform: translate(-50%, -50%) rotate(90deg);
  box-sizing: border-box;
  padding: 22px 34px 24px 34px;
  display: flex;
  flex-direction: column;
}
.ai-footer {
  margin-top: 14px;
  font-size: 8.5px;
  color: #7A8A9A;
  text-align: center;
  line-height: 1.5;
}
</style>";
    }
}
