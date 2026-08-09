using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace MedCompanion.Services
{
    /// <summary>
    /// Convertit HTML → PDF via Microsoft Edge en mode headless (Chromium, rendu CSS parfait).
    /// Edge est préinstallé sur Windows 10/11 — aucune dépendance externe.
    /// </summary>
    public class EdgeHeadlessPdfService
    {
        private readonly string _edgePath;

        public EdgeHeadlessPdfService()
        {
            _edgePath = FindEdge();
        }

        public bool IsAvailable => !string.IsNullOrEmpty(_edgePath);

        /// <summary>
        /// Profil Edge dédié, réutilisé d'un appel à l'autre.
        /// Sans lui, Edge headless partage le profil par défaut : si le médecin a son navigateur
        /// ouvert, le verrou de profil peut faire échouer la génération sans message d'erreur.
        /// </summary>
        private static string HeadlessProfileDir
        {
            get
            {
                var dir = Path.Combine(Path.GetTempPath(), "MedCompanion", "edge-headless-profile");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private static string FindEdge()
        {
            var candidates = new[]
            {
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Microsoft\Edge\Application\msedge.exe")
            };

            foreach (var p in candidates)
                if (File.Exists(p)) return p;

            return string.Empty;
        }

        /// <summary>
        /// Convertit un fichier HTML local en PDF.
        /// </summary>
        public async Task<bool> ConvertAsync(string htmlPath, string pdfPath)
        {
            if (!IsAvailable || !File.Exists(htmlPath)) return false;

            try
            {
                var outputDir = Path.GetDirectoryName(pdfPath) ?? Path.GetTempPath();
                Directory.CreateDirectory(outputDir);

                // Edge headless écrit le PDF dans le dossier courant avec le nom print.pdf
                // On lui indique --print-to-pdf=chemin_absolu pour éviter l'ambiguïté.
                var absHtml = Path.GetFullPath(htmlPath);
                var absPdf  = Path.GetFullPath(pdfPath);

                var psi = new ProcessStartInfo
                {
                    FileName  = _edgePath,
                    Arguments = $"--headless --disable-gpu --no-sandbox " +
                                $"--user-data-dir=\"{HeadlessProfileDir}\" --no-first-run " +
                                $"--print-to-pdf=\"{absPdf}\" " +
                                $"--no-pdf-header-footer " +
                                $"\"file:///{absHtml.Replace('\\', '/')}\"",
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true
                };

                using var proc = Process.Start(psi);
                if (proc == null) return false;

                await proc.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));

                if (!proc.HasExited) { proc.Kill(); return false; }

                return File.Exists(absPdf);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[EdgeHeadlessPdf] {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Exécute le HTML dans Edge et récupère la <b>carte de coordonnées</b> que sa page produit
        /// dans <c>&lt;div id="coordmap"&gt;</c> : position en millimètres de chaque champ nommé
        /// (<c>data-field</c>) et des repères de calage.
        ///
        /// Le calcul est fait par la page elle-même, après rendu des cases de lettres : la carte
        /// reste donc toujours en phase avec la maquette, sans coordonnée codée en dur.
        ///
        /// Note : <c>msedge.exe</c> est une application GUI, sa sortie standard n'est pas capturable
        /// par une redirection de shell — il faut un tube explicite, ce que fait
        /// <see cref="ProcessStartInfo.RedirectStandardOutput"/>.
        /// </summary>
        /// <returns>Le JSON de la carte, à parser par l'appelant.</returns>
        public async Task<(bool success, string json, string? error)> ExtractCoordMapAsync(string htmlPath)
        {
            if (!IsAvailable) return (false, "", "Microsoft Edge introuvable.");
            if (!File.Exists(htmlPath)) return (false, "", $"HTML introuvable : {htmlPath}");

            try
            {
                var absHtml = Path.GetFullPath(htmlPath);

                var psi = new ProcessStartInfo
                {
                    FileName  = _edgePath,
                    Arguments = $"--headless=new --disable-gpu --no-sandbox " +
                                $"--user-data-dir=\"{HeadlessProfileDir}\" --no-first-run " +
                                // Laisse le window.onload s'exécuter avant la capture du DOM.
                                $"--virtual-time-budget=5000 --dump-dom " +
                                $"\"file:///{absHtml.Replace('\\', '/')}\"",
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8
                };

                using var proc = Process.Start(psi);
                if (proc == null) return (false, "", "Impossible de démarrer Edge.");

                // Lire avant d'attendre la sortie : sinon le tube peut saturer et bloquer.
                var readDom = proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
                var dom = await readDom;

                if (string.IsNullOrWhiteSpace(dom))
                    return (false, "", "Edge n'a renvoyé aucun DOM.");

                var match = System.Text.RegularExpressions.Regex.Match(
                    dom,
                    "<div id=\"coordmap\"[^>]*>(.*?)</div>",
                    System.Text.RegularExpressions.RegexOptions.Singleline);

                if (!match.Success || string.IsNullOrWhiteSpace(match.Groups[1].Value))
                    return (false, "", "Carte de coordonnées absente du DOM : le template a-t-il bien un <div id=\"coordmap\"> et son script ?");

                return (true, match.Groups[1].Value.Trim(), null);
            }
            catch (Exception ex)
            {
                return (false, "", $"Erreur extraction carte : {ex.Message}");
            }
        }
    }
}
