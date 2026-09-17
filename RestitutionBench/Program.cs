using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using MedCompanion.Models.Restitutions;
using MedCompanion.Services;
using MedCompanion.Services.Restitutions;

// ─────────────────────────────────────────────────────────────────────────────
// Banc de mesure — étape 0 du service qualité.
//
// Mesure la HAUTEUR RÉELLE de chaque page d'un Dossier de Restitution, dans le
// moteur qui produira le PDF (Edge). Objectif : savoir quelles pages dépassent
// les 297 mm d'une A4, de combien, et sur quels dossiers.
//
// Pourquoi ça compte : à l'écran, .page est en « min-height: 297mm » — la page
// s'allonge et tout reste visible. En impression, elle passe en « height: 297mm »
// avec « overflow: hidden » : ce qui dépasse est COUPÉ, sans aucun signe. Le
// médecin relit un aperçu complet et exporte un PDF amputé.
//
// Il ne sort d'ici que des mesures : aucun contenu de dossier n'est affiché.
// ─────────────────────────────────────────────────────────────────────────────

const double HauteurA4Px = 297.0 / 25.4 * 96.0;   // 1122,5 px à 96 dpi

var chemin  = new PathService();
var service = new RestitutionService(chemin);
var apercu  = new RestitutionHtmlPreviewService(chemin);

var racine = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
    "MedCompanion", "patients");

if (!Directory.Exists(racine))
{
    Console.WriteLine($"Dossier patients introuvable : {racine}");
    return 1;
}

// Les dossiers initiaux les plus volumineux = ceux qui sont réellement remplis.
int combien = args.Length > 0 && int.TryParse(args[0], out var n) ? n : 7;

var fichiers = Directory
    .EnumerateFiles(racine, "restitution_DossierInitial*.md", SearchOption.AllDirectories)
    .Select(f => new FileInfo(f))
    .OrderByDescending(f => f.Length)
    .Take(combien)
    .ToList();

Console.WriteLine($"{fichiers.Count} dossier(s) à mesurer — seuil A4 = {HauteurA4Px:F0} px");
Console.WriteLine();

var edge = TrouverEdge();
if (edge == null) { Console.WriteLine("Edge introuvable."); return 1; }

var travail = Path.Combine(Path.GetTempPath(), "MedRestitutionBench");
Directory.CreateDirectory(travail);

var debordements  = new List<(string Dossier, int Page, string Titre, double Haut)>();
var parTypeDePage = new Dictionary<string, (int total, int deborde, double pire)>();
int totalPages = 0, dossiersOk = 0;
var toutesHauteurs = new List<double>();
var toutesPages    = new List<(int Index, double Hauteur, double Contenu, string Type)>();

foreach (var f in fichiers)
{
    var charge = service.Load(f.FullName);
    if (charge is not DossierRestitutionInitial dossier)
    {
        Console.WriteLine($"  (illisible : {f.Name})");
        continue;
    }

    // Anonymisé à l'affichage : on n'a besoin que de distinguer les dossiers.
    var etiquette = $"dossier #{dossiersOk + 1} ({f.Length / 1024} Ko)";

    string html;
    try { html = apercu.BuildPreviewHtml(dossier, dossier.PatientNomComplet); }
    catch (Exception ex) { Console.WriteLine($"  {etiquette} : rendu impossible ({ex.Message})"); continue; }

    try {
        var est = apercu.EstimationsSynthese52(dossier);
        Console.WriteLine($"      EST 5.2 : ecartes={est[0]} integration={est[1]} conclusion={est[2]} total={est[0]+est[1]+est[2]+20} budget=906");
    } catch { }

    var mesures = MesurerPages(html, edge, travail);
    if (mesures.Count == 0) { Console.WriteLine($"  {etiquette} : aucune mesure"); continue; }

    dossiersOk++;
    totalPages += mesures.Count;

    toutesHauteurs.AddRange(mesures.Select(m => m.Contenu));
    toutesPages.AddRange(mesures);

    var depasse = mesures.Where(m => m.Hauteur > HauteurA4Px + 1).ToList();
    Console.WriteLine($"{etiquette} — {mesures.Count} pages, {depasse.Count} en depassement");

    foreach (var m in mesures)
    {
        parTypeDePage.TryGetValue(m.Type, out var stat);
        var trop = Math.Max(0, m.Hauteur - HauteurA4Px);
        parTypeDePage[m.Type] = (stat.total + 1,
                                 stat.deborde + (trop > 1 ? 1 : 0),
                                 Math.Max(stat.pire, trop));
    }

    foreach (var m in depasse)
    {
        var mm = (m.Hauteur - HauteurA4Px) * 25.4 / 96.0;
        Console.WriteLine($"    page {m.Index,2} — {Tronque(m.Type, 46),-46} +{mm,6:F0} mm");
        debordements.Add((etiquette, m.Index, m.Type, m.Hauteur));
    }
}

Console.WriteLine();
Console.WriteLine("══════════ SYNTHESE ══════════");
Console.WriteLine($"{dossiersOk} dossiers · {totalPages} pages mesurees · {debordements.Count} en depassement");
Console.WriteLine();
Console.WriteLine($"{"Type de page",-75}{"vues",6}{"deborde",9}{"pire",10}");
foreach (var kv in parTypeDePage.OrderByDescending(k => k.Value.deborde).ThenByDescending(k => k.Value.pire))
{
    var mm = kv.Value.pire * 25.4 / 96.0;
    Console.WriteLine($"{Tronque(kv.Key, 74),-75}{kv.Value.total,6}{kv.Value.deborde,9}{(mm > 1 ? $"+{mm:F0} mm" : "—"),10}");
}

Console.WriteLine();
Console.WriteLine("══════════ MARGE RESTANTE ══════════");
var tranches = new (string Label, Func<double, bool> Test)[]
{
    ("déborde (> 100 %)",        h => h > HauteurA4Px + 1),
    ("au bord (90-100 %)",       h => h > HauteurA4Px * 0.90 && h <= HauteurA4Px + 1),
    ("confortable (70-90 %)",    h => h > HauteurA4Px * 0.70 && h <= HauteurA4Px * 0.90),
    ("à moitié vide (40-70 %)",  h => h > HauteurA4Px * 0.40 && h <= HauteurA4Px * 0.70),
    ("presque vide (< 40 %)",    h => h <= HauteurA4Px * 0.40),
};
foreach (var t in tranches)
{
    var n2 = toutesHauteurs.Count(t.Test);
    Console.WriteLine($"{t.Label,-26}{n2,5} pages   {(toutesHauteurs.Count > 0 ? n2 * 100.0 / toutesHauteurs.Count : 0),5:F1} %");
}
Console.WriteLine();
Console.WriteLine("Pages les plus proches du bord (hors débordements) :");
foreach (var m in toutesPages.Where(p => p.Contenu <= HauteurA4Px + 1)
                             .OrderByDescending(p => p.Contenu).Take(8))
    Console.WriteLine($"   {m.Contenu * 100.0 / HauteurA4Px,5:F0} %   {Tronque(m.Type, 50)}");

return 0;

// ── Mesure d'un dossier dans Edge ────────────────────────────────────────────

static List<(int Index, double Hauteur, double Contenu, string Type)> MesurerPages(string html, string edge, string travail)
{
    // Le script mesure chaque .page APRÈS chargement des polices (elles changent
    // les retours à la ligne, donc les hauteurs), puis remplace tout le document
    // par ses seules mesures : le --dump-dom reste minuscule au lieu des ~6 Mo
    // d'images encodées en base64.
    var sonde = new StringBuilder();
    sonde.AppendLine("<script>");
    sonde.AppendLine("(function(){");
    sonde.AppendLine("  function mesurer(){");
    sonde.AppendLine("    var pages = document.querySelectorAll('.page');");
    sonde.AppendLine("    var out = [];");
    sonde.AppendLine("    for (var i = 0; i < pages.length; i++) {");
    sonde.AppendLine("      var p = pages[i];");
    sonde.AppendLine("      var h = p.querySelector('.pc-header-left h1, .draft-title, .an-header h1, .ac-header h1, h1');");
    sonde.AppendLine("      var s = p.querySelector('.pc-subtitle');");
    sonde.AppendLine(@"      function txt(e){ return e ? e.textContent.trim().replace(/\s+/g, ' ') : ''; }");
    sonde.AppendLine("      var titre = [txt(h), txt(s)].filter(Boolean).join(' / ') || p.className;");
    sonde.AppendLine("      var hBoite = Math.round(p.getBoundingClientRect().height);");
    sonde.AppendLine("      // min-height: 297mm donne un plancher : toute page non debordante mesure");
    sonde.AppendLine("      // exactement 297 mm. On le neutralise le temps d'une mesure pour connaitre");
    sonde.AppendLine("      // la hauteur REELLE du contenu, donc la marge qui reste avant de couper.");
    sonde.AppendLine("      var mh = p.style.minHeight, hh = p.style.height, ov = p.style.overflow;");
    sonde.AppendLine("      p.style.minHeight = '0'; p.style.height = 'auto'; p.style.overflow = 'visible';");
    sonde.AppendLine("      var hContenu = Math.round(p.getBoundingClientRect().height);");
    sonde.AppendLine("      p.style.minHeight = mh; p.style.height = hh; p.style.overflow = ov;");
    sonde.AppendLine(@"      out.push((i + 1) + '\t' + hBoite + '\t' + hContenu + '\t' + titre);");
    sonde.AppendLine("    }");
    sonde.AppendLine("    // Hauteur des cartouches de sphere + budget reellement disponible sur");
    sonde.AppendLine("    // leur page : de quoi calibrer une repartition au lieu de la deviner.");
    sonde.AppendLine("    var lignesCartes = [];");
    sonde.AppendLine("    var cePages = document.querySelectorAll('.page.ce-page');");
    sonde.AppendLine("    for (var k = 0; k < cePages.length; k++) {");
    sonde.AppendLine("      var pg = cePages[k];");
    sonde.AppendLine("      var cartes = pg.querySelectorAll('.ce-card');");
    sonde.AppendLine("      if (cartes.length === 0) continue;");
    sonde.AppendLine("      var head = pg.querySelector('.pc-header');");
    sonde.AppendLine("      var leg  = pg.querySelector('.ce-legende, .ce-legend');");
    sonde.AppendLine("      var hs = [];");
    sonde.AppendLine("      for (var c = 0; c < cartes.length; c++) {");
    sonde.AppendLine("        var ca = cartes[c];");
    sonde.AppendLine("        var num = ca.querySelector('.ce-num');");
    sonde.AppendLine("        var obs = ca.querySelector('.ce-obs-body');");
    sonde.AppendLine("        var niv = ca.querySelector('.ce-niveau-body');");
    sonde.AppendLine("        var blocs = obs ? obs.querySelectorAll('p, li') : [];");
    sonde.AppendLine(@"        var nCar = obs ? obs.textContent.replace(/\s+/g, ' ').trim().length : 0;");
    sonde.AppendLine("        hs.push(Math.round(ca.getBoundingClientRect().height)");
    sonde.AppendLine(@"          + ':s' + (num ? num.textContent.replace(/\D/g, '') : '?')");
    sonde.AppendLine("          + ':b' + blocs.length");
    sonde.AppendLine("          + ':c' + nCar");
    sonde.AppendLine("          + ':n' + (niv ? Math.round(niv.getBoundingClientRect().height) : 0));");
    sonde.AppendLine("      }");
    sonde.AppendLine("      var mh2 = pg.style.minHeight, hh2 = pg.style.height, ov2 = pg.style.overflow;");
    sonde.AppendLine("      pg.style.minHeight = '0'; pg.style.height = 'auto'; pg.style.overflow = 'visible';");
    sonde.AppendLine("      var hPage = Math.round(pg.getBoundingClientRect().height);");
    sonde.AppendLine("      pg.style.minHeight = mh2; pg.style.height = hh2; pg.style.overflow = ov2;");
    sonde.AppendLine("      lignesCartes.push('CE\t' + (k + 1)");
    sonde.AppendLine("        + '\tpage=' + hPage");
    sonde.AppendLine("        + '\thead=' + (head ? Math.round(head.getBoundingClientRect().height) : 0)");
    sonde.AppendLine("        + '\tleg='  + (leg  ? Math.round(leg.getBoundingClientRect().height)  : 0)");
    sonde.AppendLine("        + '\tcartes=' + hs.join(','));");
    sonde.AppendLine("    }");
    sonde.AppendLine("    // Cartes de la Synthese : meme calibration que les cartouches de sphere.");
    sonde.AppendLine("    var sdPages = document.querySelectorAll('.page');");
    sonde.AppendLine("    for (var q = 0; q < sdPages.length; q++) {");
    sonde.AppendLine("      var pq = sdPages[q];");
    sonde.AppendLine("      var sdc = pq.querySelectorAll('.sd-card');");
    sonde.AppendLine("      if (sdc.length === 0) continue;");
    sonde.AppendLine("      var hd = pq.querySelector('.pc-header');");
    sonde.AppendLine("      var ls = [];");
    sonde.AppendLine("      for (var d = 0; d < sdc.length; d++) {");
    sonde.AppendLine("        var cd = sdc[d];");
    sonde.AppendLine("        var bl = cd.querySelectorAll('p, li, .sd-carto-sub-title, .sd-ecarte-col-hdr');");
    sonde.AppendLine(@"        var nc = cd.textContent.replace(/\s+/g, ' ').trim().length;");
    sonde.AppendLine("        ls.push(Math.round(cd.getBoundingClientRect().height) + ':b' + bl.length + ':c' + nc);");
    sonde.AppendLine("      }");
    sonde.AppendLine("      var m3 = pq.style.minHeight, h3 = pq.style.height, o3 = pq.style.overflow;");
    sonde.AppendLine("      pq.style.minHeight = '0'; pq.style.height = 'auto'; pq.style.overflow = 'visible';");
    sonde.AppendLine("      var hp3 = Math.round(pq.getBoundingClientRect().height);");
    sonde.AppendLine("      pq.style.minHeight = m3; pq.style.height = h3; pq.style.overflow = o3;");
    sonde.AppendLine("    lignesCartes.push('REPAG\tetat=' + (document.documentElement.getAttribute('data-repagine') || 'PAS EXECUTE'));");
    sonde.AppendLine("      lignesCartes.push('SD\tpage=' + hp3 + '\thead=' + (hd ? Math.round(hd.getBoundingClientRect().height) : 0) + '\tcartes=' + ls.join(','));");
    sonde.AppendLine("    }");
    sonde.AppendLine("    var pre = document.createElement('pre');");
    sonde.AppendLine("    pre.id = 'mesures';");
    sonde.AppendLine(@"    pre.textContent = out.join('\n') + '\n##CARTES##\n' + lignesCartes.join('\n');");
    sonde.AppendLine("    document.body.innerHTML = '';");
    sonde.AppendLine("    document.body.appendChild(pre);");
    sonde.AppendLine("  }");
    sonde.AppendLine("  if (document.fonts && document.fonts.ready) document.fonts.ready.then(mesurer);");
    sonde.AppendLine("  else window.addEventListener('load', mesurer);");
    sonde.AppendLine("})();");
    sonde.AppendLine("</script>");

    var fichier = Path.Combine(travail, $"m_{Guid.NewGuid():N}.html");
    File.WriteAllText(fichier, html.Replace("</body>", sonde + "</body>"), Encoding.UTF8);

    var psi = new ProcessStartInfo
    {
        FileName  = edge,
        Arguments = "--headless --disable-gpu --no-sandbox --no-first-run " +
                    $"--user-data-dir=\"{Path.Combine(travail, "profil")}\" " +
                    "--virtual-time-budget=15000 --dump-dom " +
                    $"\"file:///{Path.GetFullPath(fichier).Replace('\\', '/')}\"",
        UseShellExecute        = false,
        RedirectStandardOutput = true,
        RedirectStandardError  = true,
        CreateNoWindow         = true,
        StandardOutputEncoding = Encoding.UTF8
    };

    var resultats = new List<(int, double, double, string)>();
    try
    {
        using var proc = Process.Start(psi);
        if (proc == null) return resultats;
        var dom = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(90000);

        var m = Regex.Match(dom, "<pre id=\"mesures\">(.*?)</pre>", RegexOptions.Singleline);
        if (!m.Success) return resultats;

        var brut = m.Groups[1].Value;
        var coupe = brut.IndexOf("##CARTES##", StringComparison.Ordinal);
        if (coupe >= 0)
        {
            // Les mesures de cartouches ne servent qu'à calibrer la répartition : on les
            // affiche telles quelles, elles ne portent aucun contenu de dossier.
            foreach (var l in brut.Substring(coupe + 10).Split('\n', StringSplitOptions.RemoveEmptyEntries))
                Console.WriteLine("      " + l.Replace("\t", "  "));
            brut = brut.Substring(0, coupe);
        }

        foreach (var ligne in brut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = ligne.Split('\t');
            if (p.Length < 4) continue;
            if (int.TryParse(p[0], out var idx) && double.TryParse(p[1], out var h) && double.TryParse(p[2], out var hc))
                resultats.Add((idx, h, hc, System.Net.WebUtility.HtmlDecode(p[3]).Trim()));
        }
    }
    catch { /* dossier ignoré */ }
    finally { try { File.Delete(fichier); } catch { } }

    return resultats;
}

static string Tronque(string s, int n) => s.Length <= n ? s : s.Substring(0, n - 1) + "…";

static string? TrouverEdge()
{
    foreach (var p in new[]
    {
        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files\Microsoft\Edge\Application\msedge.exe"
    })
        if (File.Exists(p)) return p;
    return null;
}
