using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MedCompanion.Models.Evaluations;
using MedCompanion.Services.Evaluations;

namespace MedCompanion.Services.Restitutions
{
    /// <summary>
    /// Répartition des cartouches sur les pages A4, quand leur nombre est fixe mais leur
    /// hauteur dépend de l'enfant.
    ///
    /// POURQUOI. Les huit sphères de la Cartographie étaient réparties en dur — 3, puis 2,
    /// puis 3. Or une cartouche mesure de 180 à 465 px selon la longueur des observations :
    /// sur un enfant très décrit, la troisième page dépassait les 297 mm. Et un dépassement
    /// ne se voit pas : à l'écran <c>.page</c> est en <c>min-height</c> et s'allonge, mais en
    /// impression elle passe en <c>height: 297mm; overflow: hidden</c> — Edge imprime 297 mm
    /// et coupe le reste, sans trait ni avertissement. Le médecin relit un aperçu complet et
    /// exporte un PDF amputé.
    ///
    /// Mesuré le 17/09/2026 sur les sept dossiers remplis du poste : 165 pages, deux
    /// dépassements (Cartographie +21 mm, Synthèse +12 mm) — mais 38 pages à moins de 10 %
    /// du bord. Le problème n'était pas rare, il était latent.
    ///
    /// COMMENT. La hauteur d'une page de cartographie suit exactement
    /// <c>306 + Σ cartouches + 10 × (n − 1)</c> — vérifié au pixel près sur les 21 pages
    /// mesurées. Il reste donc à estimer la hauteur d'une cartouche sans navigateur, et à
    /// remplir les pages tant que le budget le permet.
    ///
    /// L'estimation est volontairement PESSIMISTE : surestimer coûte une page de plus,
    /// sous-estimer coupe du texte clinique dans le PDF remis aux parents.
    /// </summary>
    public partial class RestitutionHtmlPreviewService
    {
        /// <summary>Hauteur utile d'une A4 à 96 dpi, moins l'ossature d'une page de cartographie
        /// (en-tête 106 px, légende 38 px, marges 162 px), moins une garde de 16 px.</summary>
        private const int BudgetCartouchesParPage = 1122 - 306 - 16;

        /// <summary>Marge entre deux cartouches (<c>.ce-card { margin-bottom: 10px }</c>).</summary>
        private const int MargeEntreCartouches = 10;

        /// <summary>
        /// Hauteur estimée d'une cartouche de sphère.
        ///
        /// Deux colonnes côte à côte : la roue, de hauteur fixe, et le texte, qui grandit avec
        /// les observations. La cartouche fait la plus haute des deux — d'où le <c>max</c> et
        /// non une somme.
        ///
        /// Les coefficients sont CALIBRÉS, pas devinés : 56 cartouches mesurées dans Edge sur
        /// les sept dossiers remplis du poste (RestitutionBench). Le terme par bloc compte les
        /// fins de ligne perdues — une puce courte occupe autant de hauteur qu'une pleine.
        /// L'écart résiduel est de +12 à +30 px, toujours dans le sens de la prudence.
        /// </summary>
        private static int EstimerHauteurCartouche(string? observations)
        {
            const int    PlancherRoue  = 186;    // mesuré : 180-184 px quand le texte est court
            const int    Ossature      = 120;
            const double ParCaractere  = 0.26;
            const int    ParBloc       = 4;

            var caracteres = 0;
            var blocs      = 0;

            foreach (var brute in (observations ?? "").Split('\n'))
            {
                var t = brute.Trim().TrimStart('-', '*', '•', ' ').Trim();
                if (t.Length == 0) continue;
                blocs++;
                caracteres += t.Length;
            }

            var hauteurTexte = Ossature + (int)Math.Ceiling(caracteres * ParCaractere) + blocs * ParBloc;
            return Math.Max(PlancherRoue, hauteurTexte);
        }

        /// <summary>
        /// Répartit des cartouches sur autant de pages qu'il en faut, sans jamais en couper une.
        /// Une cartouche plus haute que le budget occupe sa page à elle seule : mieux vaut une
        /// page trop remplie qu'un découpage au milieu d'une observation clinique.
        /// </summary>
        private static List<List<int>> RepartirCartouches(IReadOnlyList<int> hauteurs, int budget)
        {
            var pages   = new List<List<int>>();
            var page    = new List<int>();
            var utilise = 0;

            for (int i = 0; i < hauteurs.Count; i++)
            {
                var cout = hauteurs[i] + (page.Count > 0 ? MargeEntreCartouches : 0);

                if (page.Count > 0 && utilise + cout > budget)
                {
                    pages.Add(page);
                    page    = new List<int>();
                    utilise = 0;
                    cout    = hauteurs[i];
                }

                page.Add(i);
                utilise += cout;
            }

            if (page.Count > 0) pages.Add(page);
            return pages;
        }

        /// <summary>
        /// Les pages de la Cartographie de l'enfant — autant qu'il en faut pour les huit
        /// sphères. Remplace la répartition figée 3/2/3, qui ne tenait que sur les dossiers
        /// où les observations étaient courtes.
        /// </summary>
        private string BuildCartoEnfantPages(
            CartographieEnfant? carto,
            CartographieV2? cartoV2,
            Dictionary<int, CeSphereContent> perSphere)
        {
            // 1. Rendre les huit cartouches, et estimer la hauteur de chacune.
            var cartouches = new List<string>();
            var hauteurs   = new List<int>();

            foreach (var sphere in _ceSpheres)
            {
                cartouches.Add(BuildCeSphereCard(sphere, carto, cartoV2, perSphere));

                perSphere.TryGetValue(sphere.Num, out var contenu);
                hauteurs.Add(EstimerHauteurCartouche(contenu?.Observations));
            }

            // 2. Les répartir, puis émettre une page par groupe.
            var groupes = RepartirCartouches(hauteurs, BudgetCartouchesParPage);
            var sb = new StringBuilder();

            for (int p = 0; p < groupes.Count; p++)
            {
                sb.AppendLine("<div class='page ce-page'>");
                sb.Append(BuildPcHeader(
                    "CARTOGRAPHIE DE L'ENFANT",
                    "4.1 Vue d'ensemble — Cette section évalue les sphères de développement,<br>afin de mieux comprendre son fonctionnement interne et ses besoins spécifiques.",
                    $"{p + 1}/{groupes.Count}", 0, 0));

                foreach (var idx in groupes[p]) sb.Append(cartouches[idx]);

                sb.Append(BuildCeLegende());
                sb.AppendLine("</div>");
            }

            return sb.ToString();
        }

        // ── Synthèse 5.2 : trois cartes, deux mises en colonnes ─────────────
        //
        // La carte « Intégration des cartographies » est un flex à deux colonnes étirées :
        // sa hauteur vaut max(colonne enfant, colonne environnement), PAS une fonction du
        // texte total. Les mesures le montrent nettement — 842, 931, 1012 puis 1220
        // caractères donnent tous 338 px, et 1362 en donne 459. Un plateau, puis un saut.
        // Un estimateur bâti sur le volume total se trompe donc des deux côtés : il coupe
        // sur les dossiers denses et découpe pour rien sur les autres.
        //
        // On estime donc chaque colonne séparément, et on garde la plus haute.

        /// <summary>
        /// Budget de hauteur pour les trois cartes de la page 5.2.
        ///
        /// La géométrie donne 1122 − 200 = 922 px (A4 moins l'en-tête 94 px et les marges
        /// 106 px, mesurés). Mais l'estimation par colonne SOUS-ÉVALUE de 0 à 21 % selon les
        /// dossiers — confronté aux hauteurs réelles des sept dossiers du poste, via
        /// <see cref="EstimationsSynthese52"/>. On réserve donc 25 % : un débordement coupe du
        /// texte clinique dans le PDF, un découpage de trop ne coûte qu'une page.
        /// </summary>
        private const int BudgetCartesSynthese = (1122 - 200) * 100 / 125;

        /// <summary>Hauteur d'une liste à puces dans une colonne étroite, titre compris.</summary>
        private static int HauteurSousSection(IReadOnlyList<string> items, int caracteresParLigne)
        {
            if (items.Count == 0) return 0;
            var h = 20 + 14;                       // titre + marges de la sous-section
            foreach (var it in items)
                h += Math.Max(1, (int)Math.Ceiling((it ?? "").Trim().Length / (double)caracteresParLigne)) * 15;
            return h;
        }

        private static int EstimerCarteIntegration(DiagS4Data? d)
        {
            const int EnteteCarte = 40;
            if (d == null) return EnteteCarte + 40;

            // Deux colonnes côte à côte, environ 350 px de large chacune.
            var enfant = 30 + HauteurSousSection(d.Enfant.Forces, 52)
                            + HauteurSousSection(d.Enfant.Fragilites, 52);
            var env    = 30 + HauteurSousSection(d.Environnement.Protecteurs, 52)
                            + HauteurSousSection(d.Environnement.Aggravants, 52);

            return EnteteCarte + Math.Max(enfant, env);
        }

        private static int EstimerCarteEcartes(List<DiagS3Item>? items)
        {
            const int EnteteCarte = 40;
            if (items == null || items.Count == 0) return EnteteCarte + 40;

            // Jusqu'à trois colonnes : chacune fait environ 225 px de large.
            var pire = 0;
            foreach (var ec in items.Take(3))
            {
                var h = 26;
                foreach (var a in ec.Arguments)
                    h += Math.Max(1, (int)Math.Ceiling((a ?? "").Trim().Length / 38.0)) * 14;
                if (!string.IsNullOrWhiteSpace(ec.Conclusion))
                    h += 6 + Math.Max(1, (int)Math.Ceiling(ec.Conclusion.Trim().Length / 38.0)) * 14;
                pire = Math.Max(pire, h);
            }
            return EnteteCarte + pire;
        }

        private static int EstimerCarteConclusion(string? texte)
        {
            const int EnteteCarte = 40;
            var lignes = 0;
            foreach (var brute in (texte ?? "").Split('\n'))
            {
                var t = brute.Trim().TrimStart('-', '*', '•', ' ').Trim();
                if (t.Length == 0) continue;
                lignes += Math.Max(1, (int)Math.Ceiling(t.Length / 95.0));
            }
            return EnteteCarte + Math.Max(1, lignes) * 16;
        }

        /// <summary>
        /// Les pages 5.2 — une, ou deux quand les trois cartes ne tiennent pas ensemble.
        /// Une carte n'est jamais coupée : elle part entière sur la page suivante.
        /// </summary>
        private string BuildSyntheseDiagPage2Pages(
            Dictionary<string, string> blocs,
            BilanFinal? bilan,
            CoverFields cover)
        {
            blocs.TryGetValue("synthese_diag_s3", out var s3Text);
            blocs.TryGetValue("synthese_diag_s4", out var s4Text);
            blocs.TryGetValue("synthese_diag_s5", out var s5Text);

            var hauteurs = new[]
            {
                EstimerCarteEcartes(TryParseDiagS3Json(s3Text)),
                EstimerCarteIntegration(TryParseDiagS4Json(s4Text)),
                EstimerCarteConclusion(s5Text),
            };

            var groupes = RepartirCartouches(hauteurs, BudgetCartesSynthese);
            var sb = new StringBuilder();

            for (int p = 0; p < groupes.Count; p++)
            {
                // Les cartes sont numérotées 4, 5, 6 dans le document.
                var sections = groupes[p].Select(i => i + 4).ToList();
                var badge    = groupes.Count == 1 ? "2/2" : $"2.{p + 1}/2";
                sb.Append(BuildSyntheseDiagPage2(blocs, bilan, cover, sections, badge));
            }

            return sb.ToString();
        }

        /// <summary>
        /// Les hauteurs estimées des trois cartes de la page 5.2, dans l'ordre 4/5/6.
        /// Exposée au banc de mesure (RestitutionBench) pour confronter l'estimation à la
        /// hauteur réellement rendue par Edge — c'est ce qui a permis de calibrer les
        /// coefficients au lieu de les deviner.
        /// </summary>
        internal IReadOnlyList<int> EstimationsSynthese52(MedCompanion.Models.Restitutions.RestitutionBase dossier)
        {
            var blocs = BuildSyntheseDiagBlocsDict(dossier.Blocs);
            blocs.TryGetValue("synthese_diag_s3", out var s3);
            blocs.TryGetValue("synthese_diag_s4", out var s4);
            blocs.TryGetValue("synthese_diag_s5", out var s5);

            return new[]
            {
                EstimerCarteEcartes(TryParseDiagS3Json(s3)),
                EstimerCarteIntegration(TryParseDiagS4Json(s4)),
                EstimerCarteConclusion(s5),
            };
        }
    }
}
