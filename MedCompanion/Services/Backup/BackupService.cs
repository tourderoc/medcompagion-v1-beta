using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MedCompanion.Models;

namespace MedCompanion.Services.Backup
{
    /// <summary>
    /// Sauvegarde incrémentale des données MedCompanion vers un disque distinct.
    ///
    /// Trois partis pris, qui sont le cœur du service :
    ///
    /// 1. <b>Rien n'est jamais supprimé à la destination.</b> Une suppression accidentelle à la
    ///    source, ou un rançongiciel, ne se propage pas. C'est exactement là que les miroirs
    ///    classiques trahissent leur propriétaire.
    ///
    /// 2. <b>Avant d'écraser un fichier, l'ancien est mis de côté</b> dans _versions\&lt;date&gt;.
    ///    Sans cela, un fichier abîmé à la source — écriture interrompue, coupure de courant —
    ///    écraserait sa dernière version correcte, ce qui revient à l'avoir perdue.
    ///
    /// 3. <b>La destination est vérifiée avant la moindre écriture</b> : disque présent, nom de
    ///    volume attendu, et surtout disque physiquement différent de la source.
    ///
    /// L'exécution a lieu sur un thread du pool : l'interface reste libre pendant la copie.
    /// </summary>
    public sealed class BackupService
    {
        public const string DossierVersions = "_versions";
        public const string DossierJournal = "_journal";

        /// <summary>Empêche qu'une sauvegarde manuelle et une sauvegarde programmée se chevauchent.</summary>
        private static readonly SemaphoreSlim Verrou = new(1, 1);

        public static bool EnCours { get; private set; }

        // Dossiers volumineux et reconstructibles : ils ne méritent pas de place sur le disque de
        // sauvegarde. models = modèles Whisper (4,4 Go), recordings = audio brut des dictées (1,8 Go),
        // bdpm et piper = bases et voix retéléchargeables.
        private static readonly string[] DossiersExclusDonnees = { "patients", "bdpm", "piper" };
        private static readonly string[] DossiersExclusAppData = { "logs", "recordings", "models", "tessdata" };

        // appsettings.json diffère légitimement d'une machine à l'autre (chemins llama.cpp,
        // micro, modèles) : le restaurer écraserait la configuration de la machine d'accueil.
        private static readonly string[] PrefixesExclusAppData = { "appsettings.json", "llama-server.log", "crash.log" };

        private readonly PathService _paths;

        public BackupService(PathService? paths = null) => _paths = paths ?? new PathService();

        // =====================================================================
        // Vérification de la destination
        // =====================================================================

        /// <summary>
        /// Contrôle que l'on peut écrire, et surtout que l'on écrit au bon endroit.
        /// Appelée avant chaque sauvegarde et par la fenêtre Paramètres pour valider la saisie.
        /// </summary>
        public (bool Ok, string? Erreur) VerifierDestination(BackupSettings reglages)
        {
            if (string.IsNullOrWhiteSpace(reglages.DestinationRoot))
                return (false, "Aucun dossier de sauvegarde n'est configuré.");

            string racine;
            try { racine = Path.GetFullPath(reglages.DestinationRoot); }
            catch { return (false, "Le chemin de sauvegarde est invalide."); }

            var lettre = Path.GetPathRoot(racine);
            if (string.IsNullOrEmpty(lettre))
                return (false, "Le chemin de sauvegarde doit être un chemin complet (ex. E:\\Sauvegardes\\MedCompanion).");

            DriveInfo disque;
            try { disque = new DriveInfo(lettre); }
            catch { return (false, $"Le disque {lettre} est introuvable."); }

            if (!disque.IsReady)
                return (false, $"Le disque {lettre} n'est pas accessible. Vérifiez qu'il est bien branché.");

            // Garde-fou contre la lettre de lecteur qui change : une clé USB peut très bien
            // récupérer E: et se retrouver à recevoir les dossiers patients.
            if (!string.IsNullOrWhiteSpace(reglages.ExpectedVolumeLabel))
            {
                var nomReel = (disque.VolumeLabel ?? "").Trim();
                if (!string.Equals(nomReel, reglages.ExpectedVolumeLabel.Trim(), StringComparison.OrdinalIgnoreCase))
                    return (false,
                        $"Le disque {lettre} s'appelle « {nomReel} » et non « {reglages.ExpectedVolumeLabel} ». " +
                        "Sauvegarde annulée : ce n'est pas le bon disque.");
            }

            var source = _paths.GetBasePatientsDirectory();
            var lettreSource = Path.GetPathRoot(Path.GetFullPath(source));

            if (string.Equals(lettre, lettreSource, StringComparison.OrdinalIgnoreCase))
                return (false,
                    "La sauvegarde pointe sur le même disque que les dossiers patients. " +
                    "Elle ne protégerait de rien : choisissez un autre disque.");

            if (racine.StartsWith(Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase))
                return (false, "Le dossier de sauvegarde ne peut pas se trouver à l'intérieur des données à sauvegarder.");

            return (true, null);
        }

        // =====================================================================
        // Exécution
        // =====================================================================

        /// <summary>
        /// Lance la sauvegarde sur un thread de fond. L'appelant reste libre — l'interface
        /// de MedCompanion ne se fige à aucun moment.
        /// </summary>
        public async Task<BackupResult> RunAsync(
            BackupSettings reglages,
            IProgress<BackupProgress>? avancement = null,
            CancellationToken ct = default)
        {
            // Attente non bloquante : si une sauvegarde programmée tourne déjà, la manuelle patiente.
            await Verrou.WaitAsync(ct).ConfigureAwait(false);
            EnCours = true;
            try
            {
                var resultat = await Task.Run(() => Executer(reglages, avancement, ct), ct).ConfigureAwait(false);

                // Un seul endroit inscrit la date de dernière sauvegarde : impossible qu'un appelant
                // l'oublie et fasse repartir le compteur des sauvegardes automatiques.
                if (resultat.Success)
                    BackupSettings.MarkRun(DateTime.UtcNow, resultat.Resume());

                return resultat;
            }
            catch (OperationCanceledException)
            {
                return new BackupResult { Success = false, Cancelled = true };
            }
            finally
            {
                EnCours = false;
                Verrou.Release();
            }
        }

        private BackupResult Executer(BackupSettings reglages, IProgress<BackupProgress>? avancement, CancellationToken ct)
        {
            var chrono = Stopwatch.StartNew();
            var resultat = new BackupResult();

            try
            {
                // --- 1. Vérification -------------------------------------------------
                Signaler(avancement, BackupPhase.Verification, "Vérification du disque de sauvegarde…", chrono);

                var (ok, erreur) = VerifierDestination(reglages);
                if (!ok)
                {
                    resultat.Success = false;
                    resultat.Error = erreur;
                    resultat.Duration = chrono.Elapsed;
                    return resultat;
                }

                var racine = Path.GetFullPath(reglages.DestinationRoot);
                Directory.CreateDirectory(racine);

                // --- 2. Analyse ------------------------------------------------------
                Signaler(avancement, BackupPhase.Analyse, "Comparaison des fichiers…", chrono);

                var seuil = DateTime.Now.AddSeconds(-Math.Max(0, reglages.IgnoreFilesModifiedWithinSeconds));
                var taches = new List<Tache>();
                var dossiersVides = new List<string>();
                long octetsTotal = 0;

                foreach (var jeu in ConstruireJeux())
                {
                    ct.ThrowIfCancellationRequested();
                    if (!Directory.Exists(jeu.Racine)) continue;

                    var exclus = new HashSet<string>(jeu.DossiersExclus, StringComparer.OrdinalIgnoreCase);
                    var videsDuJeu = new List<string>();

                    foreach (var fichier in Parcourir(jeu.Racine, exclus, videsDuJeu, ct))
                    {
                        if (FichierExclu(fichier.Name, jeu.PrefixesExclus)) continue;
                        if (fichier.LastWriteTime > seuil) continue;   // sans doute en cours d'écriture

                        var relatif = Path.Combine(jeu.SousDossier, RelatifDepuis(jeu.Racine, fichier.FullName));
                        var destination = Path.Combine(racine, relatif);

                        var info = new FileInfo(destination);
                        bool ecrase;

                        if (!info.Exists)
                        {
                            ecrase = false;
                        }
                        else if (info.Length != fichier.Length ||
                                 fichier.LastWriteTimeUtc > info.LastWriteTimeUtc.AddSeconds(2))
                        {
                            ecrase = true;
                        }
                        else
                        {
                            resultat.FilesUpToDate++;
                            continue;
                        }

                        taches.Add(new Tache(fichier.FullName, destination, relatif, fichier.Length, ecrase));
                        octetsTotal += fichier.Length;
                    }

                    foreach (var vide in videsDuJeu)
                        dossiersVides.Add(Path.Combine(jeu.SousDossier, vide));
                }

                // --- 3. Place disponible --------------------------------------------
                var disque = new DriveInfo(Path.GetPathRoot(racine)!);
                var requis = (long)(octetsTotal * 1.1) + 50L * 1024 * 1024;
                if (disque.AvailableFreeSpace < requis)
                {
                    resultat.Success = false;
                    resultat.Error =
                        $"Espace insuffisant sur {disque.Name} : {octetsTotal / 1024d / 1024d:N0} Mo à copier, " +
                        $"{disque.AvailableFreeSpace / 1024d / 1024d:N0} Mo disponibles.";
                    resultat.Duration = chrono.Elapsed;
                    return resultat;
                }

                // Squelette de l'arborescence : les dossiers de la destination naissent en
                // accueillant un fichier, donc un dossier vide à la source n'existerait jamais ici.
                CreerDossiersVides(racine, dossiersVides, resultat);

                if (taches.Count == 0)
                {
                    resultat.Success = true;
                    resultat.Duration = chrono.Elapsed;
                    Signaler(avancement, BackupPhase.Termine, "Tout est déjà à jour.", chrono);
                    EcrireJournal(racine, resultat, reglages, Array.Empty<Tache>());
                    return resultat;
                }

                // --- 4. Copie --------------------------------------------------------
                var dossierVersions = Path.Combine(racine, DossierVersions, DateTime.Now.ToString("yyyy-MM-dd"));
                long octetsFaits = 0;
                var dernierEnvoi = TimeSpan.Zero;
                var copies = new List<Tache>();

                for (int i = 0; i < taches.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var tache = taches[i];

                    // Signalement limité à 10 par seconde : inonder le thread graphique le
                    // ralentirait plus que la copie elle-même.
                    if (chrono.Elapsed - dernierEnvoi > TimeSpan.FromMilliseconds(100) || i == taches.Count - 1)
                    {
                        dernierEnvoi = chrono.Elapsed;
                        avancement?.Report(new BackupProgress
                        {
                            Phase = BackupPhase.Copie,
                            Message = $"Copie en cours… {i + 1} / {taches.Count}",
                            CurrentFile = tache.Relatif,
                            FilesDone = i,
                            FilesTotal = taches.Count,
                            BytesDone = octetsFaits,
                            BytesTotal = octetsTotal,
                            Elapsed = chrono.Elapsed,
                            Eta = EstimerRestant(octetsFaits, octetsTotal, chrono.Elapsed)
                        });
                    }

                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(tache.Destination)!);

                        if (tache.Ecrase && ArchiverVersionPrecedente(tache, dossierVersions))
                            resultat.FilesVersioned++;

                        CopierAvecReprises(tache.Source, tache.Destination);

                        resultat.FilesCopied++;
                        resultat.BytesCopied += tache.Taille;
                        octetsFaits += tache.Taille;
                        copies.Add(tache);
                    }
                    catch (IOException ex) when (EstVerrouille(ex))
                    {
                        // Fichier ouvert par Word, LibreOffice ou MedCompanion : on ne force pas,
                        // on le reprendra à la sauvegarde suivante plutôt que d'en copier une moitié.
                        resultat.FilesInUse++;
                        resultat.Warnings.Add($"occupé, repris plus tard : {tache.Relatif}");
                        octetsFaits += tache.Taille;
                    }
                    catch (Exception ex)
                    {
                        resultat.FilesFailed++;
                        resultat.Warnings.Add($"échec : {tache.Relatif} — {ex.Message}");
                        octetsFaits += tache.Taille;
                    }
                }

                // --- 5. Purge des anciennes versions ---------------------------------
                Signaler(avancement, BackupPhase.Nettoyage, "Nettoyage des anciennes versions…", chrono);
                PurgerVersions(racine, reglages.KeepVersionsDays, resultat);

                resultat.Success = true;
                resultat.Duration = chrono.Elapsed;
                resultat.JournalPath = EcrireJournal(racine, resultat, reglages, copies);

                Signaler(avancement, BackupPhase.Termine, resultat.Resume(), chrono);
                return resultat;
            }
            catch (OperationCanceledException)
            {
                resultat.Success = false;
                resultat.Cancelled = true;
                resultat.Duration = chrono.Elapsed;
                return resultat;
            }
            catch (Exception ex)
            {
                resultat.Success = false;
                resultat.Error = ex.Message;
                resultat.Duration = chrono.Elapsed;
                return resultat;
            }
        }

        // =====================================================================
        // Sources
        // =====================================================================

        private sealed record JeuSource(string Racine, string SousDossier, string[] DossiersExclus, string[] PrefixesExclus);

        /// <summary>
        /// Les trois ensembles à sauvegarder. Les dossiers patients ne suffisent pas : sans les
        /// templates, la bibliothèque MCC, les prompts et les écoles saisies à la main, une
        /// restauration rendrait les dossiers mais ferait perdre tout ce qui a été construit autour.
        /// </summary>
        private List<JeuSource> ConstruireJeux()
        {
            var patients = Path.GetFullPath(_paths.GetBasePatientsDirectory());
            var donnees = Directory.GetParent(patients)?.FullName ?? patients;
            var appData = Path.GetFullPath(_paths.GetAppDataPath());

            return new List<JeuSource>
            {
                new(patients, "patients", Array.Empty<string>(), Array.Empty<string>()),
                new(donnees,  "donnees",  DossiersExclusDonnees, Array.Empty<string>()),
                new(appData,  "appdata",  DossiersExclusAppData, PrefixesExclusAppData),
            };
        }

        /// <summary>
        /// Parcours récursif avec élagage à la racine : les dossiers exclus ne sont pas même
        /// ouverts, ce qui évite de réénumérer les 8 000 fichiers patients une seconde fois.
        /// </summary>
        /// <param name="dossiersVidesRelatifs">
        /// Reçoit les dossiers terminaux sans aucun contenu. Un dossier patient entièrement vide
        /// — il en existe, restes de doublons — ne serait jamais créé à la destination, puisque
        /// les dossiers n'y naissent qu'en accueillant un fichier. Une restauration ferait alors
        /// disparaître ces entrées de la liste des patients. Recréer les dossiers terminaux vides
        /// suffit à reconstituer toute l'arborescence, leurs parents étant créés au passage.
        /// </param>
        private static IEnumerable<FileInfo> Parcourir(string racine, HashSet<string> dossiersExclus,
                                                       ICollection<string> dossiersVidesRelatifs, CancellationToken ct)
        {
            var pile = new Stack<DirectoryInfo>();
            pile.Push(new DirectoryInfo(racine));

            while (pile.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var dossier = pile.Pop();

                FileInfo[] fichiers;
                DirectoryInfo[] sousDossiers;
                try
                {
                    fichiers = dossier.GetFiles();
                    sousDossiers = dossier.GetDirectories();
                }
                catch (UnauthorizedAccessException) { continue; }
                catch (DirectoryNotFoundException) { continue; }
                catch (IOException) { continue; }

                if (fichiers.Length == 0 && sousDossiers.Length == 0)
                    dossiersVidesRelatifs.Add(RelatifDepuis(racine, dossier.FullName));

                foreach (var f in fichiers) yield return f;

                var estRacine = string.Equals(dossier.FullName.TrimEnd('\\'), racine.TrimEnd('\\'),
                                              StringComparison.OrdinalIgnoreCase);

                foreach (var d in sousDossiers)
                {
                    if (estRacine && dossiersExclus.Contains(d.Name)) continue;
                    if (d.Name.StartsWith("$", StringComparison.Ordinal)) continue;
                    if ((d.Attributes & FileAttributes.ReparsePoint) != 0) continue;   // jonctions : risque de boucle
                    pile.Push(d);
                }
            }
        }

        private static void CreerDossiersVides(string racine, IEnumerable<string> relatifs, BackupResult resultat)
        {
            foreach (var relatif in relatifs)
            {
                try
                {
                    var chemin = Path.Combine(racine, relatif);
                    if (!Directory.Exists(chemin)) Directory.CreateDirectory(chemin);
                }
                catch (Exception ex)
                {
                    resultat.Warnings.Add($"dossier non recréé : {relatif} — {ex.Message}");
                }
            }
        }

        private static bool FichierExclu(string nom, string[] prefixesExclus)
        {
            // Verrous LibreOffice : présents seulement pendant l'édition, aucun intérêt à les copier.
            if (nom.StartsWith(".~lock", StringComparison.OrdinalIgnoreCase)) return true;
            if (nom.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) return true;
            if (nom.Equals("Thumbs.db", StringComparison.OrdinalIgnoreCase)) return true;
            if (nom.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) return true;

            foreach (var p in prefixesExclus)
                if (nom.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        private static string RelatifDepuis(string racine, string chemin)
        {
            if (string.Equals(chemin.TrimEnd('\\'), racine.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                return "";

            var r = racine.TrimEnd('\\') + "\\";
            return chemin.StartsWith(r, StringComparison.OrdinalIgnoreCase)
                ? chemin.Substring(r.Length)
                : Path.GetFileName(chemin);
        }

        // =====================================================================
        // Copie et versions
        // =====================================================================

        /// <summary>
        /// Déplace la version en place vers _versions\&lt;date&gt; avant de la remplacer.
        /// Renvoie faux si rien n'a été archivé (fichier déjà disparu, ou archivage impossible —
        /// auquel cas on préfère laisser la copie se faire plutôt que de bloquer la sauvegarde).
        /// </summary>
        private static bool ArchiverVersionPrecedente(Tache tache, string dossierVersions)
        {
            try
            {
                if (!File.Exists(tache.Destination)) return false;

                var cible = Path.Combine(dossierVersions, tache.Relatif);
                Directory.CreateDirectory(Path.GetDirectoryName(cible)!);

                // Même fichier modifié deux fois dans la journée : on suffixe par l'heure.
                if (File.Exists(cible))
                {
                    var sansExt = Path.Combine(
                        Path.GetDirectoryName(cible)!,
                        Path.GetFileNameWithoutExtension(cible));
                    cible = $"{sansExt}_{DateTime.Now:HHmmss}{Path.GetExtension(cible)}";
                }

                File.Move(tache.Destination, cible);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Backup] Archivage impossible pour {tache.Relatif} : {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Trois tentatives espacées : un fichier peut être verrouillé une fraction de seconde
        /// pendant que MedCompanion termine de l'écrire.
        /// </summary>
        private static void CopierAvecReprises(string source, string destination)
        {
            const int tentatives = 3;
            for (int essai = 1; ; essai++)
            {
                try
                {
                    File.Copy(source, destination, overwrite: true);
                    return;
                }
                catch (IOException) when (essai < tentatives)
                {
                    Thread.Sleep(250 * essai);
                }
                catch (UnauthorizedAccessException) when (essai < tentatives)
                {
                    Thread.Sleep(250 * essai);
                }
            }
        }

        private static bool EstVerrouille(IOException ex)
        {
            // ERROR_SHARING_VIOLATION (32) et ERROR_LOCK_VIOLATION (33)
            var code = ex.HResult & 0xFFFF;
            return code == 32 || code == 33;
        }

        /// <summary>
        /// Supprime les dossiers de versions plus anciens que la durée de conservation.
        /// C'est la seule suppression que le service s'autorise, et elle ne touche jamais
        /// la copie courante des données — uniquement des versions antérieures déjà remplacées.
        /// </summary>
        private static void PurgerVersions(string racine, int joursConserves, BackupResult resultat)
        {
            if (joursConserves <= 0) return;

            var dossier = Path.Combine(racine, DossierVersions);
            if (!Directory.Exists(dossier)) return;

            var limite = DateTime.Today.AddDays(-joursConserves);

            foreach (var sousDossier in Directory.GetDirectories(dossier))
            {
                var nom = Path.GetFileName(sousDossier);
                if (!DateTime.TryParseExact(nom, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                            DateTimeStyles.None, out var date))
                    continue;

                if (date >= limite) continue;

                try { Directory.Delete(sousDossier, recursive: true); }
                catch (Exception ex) { resultat.Warnings.Add($"purge impossible : {nom} — {ex.Message}"); }
            }

            // Les journaux suivent la même durée de conservation.
            var journaux = Path.Combine(racine, DossierJournal);
            if (!Directory.Exists(journaux)) return;

            foreach (var fichier in Directory.GetFiles(journaux, "*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(fichier) < limite) File.Delete(fichier);
                }
                catch { /* un journal non supprimé ne justifie pas de signaler une anomalie */ }
            }
        }

        // =====================================================================
        // Journal
        // =====================================================================

        /// <summary>
        /// Trace datée de ce qui a été sauvegardé. Sur des données médicales, pouvoir établir
        /// ce qui était protégé et à quelle date a une valeur propre.
        /// </summary>
        private static string? EcrireJournal(string racine, BackupResult resultat, BackupSettings reglages, IReadOnlyList<Tache> copies)
        {
            try
            {
                var dossier = Path.Combine(racine, DossierJournal);
                Directory.CreateDirectory(dossier);
                var chemin = Path.Combine(dossier, $"{DateTime.Now:yyyy-MM-dd_HHmmss}.log");

                var sb = new StringBuilder();
                sb.AppendLine($"Sauvegarde MedCompanion — {DateTime.Now:dd/MM/yyyy HH:mm:ss}");
                sb.AppendLine($"Destination      : {racine}");
                sb.AppendLine($"Durée            : {resultat.Duration.TotalSeconds:N1} s");
                sb.AppendLine($"Fichiers copiés  : {resultat.FilesCopied}  ({resultat.BytesCopied / 1024d / 1024d:N1} Mo)");
                sb.AppendLine($"Versions rangées : {resultat.FilesVersioned}");
                sb.AppendLine($"Déjà à jour      : {resultat.FilesUpToDate}");
                sb.AppendLine($"Occupés          : {resultat.FilesInUse}");
                sb.AppendLine($"Échecs           : {resultat.FilesFailed}");
                sb.AppendLine($"Conservation     : {reglages.KeepVersionsDays} jours de versions");
                sb.AppendLine();

                if (resultat.Warnings.Count > 0)
                {
                    sb.AppendLine("--- Anomalies ---");
                    foreach (var a in resultat.Warnings) sb.AppendLine("  " + a);
                    sb.AppendLine();
                }

                if (copies.Count > 0)
                {
                    sb.AppendLine("--- Fichiers copiés ---");
                    const int plafond = 5000;
                    for (int i = 0; i < copies.Count && i < plafond; i++)
                        sb.AppendLine("  " + copies[i].Relatif);
                    if (copies.Count > plafond)
                        sb.AppendLine($"  … et {copies.Count - plafond} autres (liste tronquée)");
                }

                File.WriteAllText(chemin, sb.ToString(), Encoding.UTF8);
                return chemin;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Backup] Journal non écrit : {ex.Message}");
                return null;
            }
        }

        // =====================================================================
        // Divers
        // =====================================================================

        private static void Signaler(IProgress<BackupProgress>? avancement, BackupPhase phase, string message, Stopwatch chrono)
        {
            avancement?.Report(new BackupProgress
            {
                Phase = phase,
                Message = message,
                Elapsed = chrono.Elapsed
            });
        }

        private static TimeSpan? EstimerRestant(long faits, long total, TimeSpan ecoule)
        {
            if (faits <= 0 || total <= 0 || ecoule.TotalSeconds < 1) return null;
            var debit = faits / ecoule.TotalSeconds;
            if (debit <= 0) return null;
            return TimeSpan.FromSeconds((total - faits) / debit);
        }

        private sealed record Tache(string Source, string Destination, string Relatif, long Taille, bool Ecrase);
    }
}
