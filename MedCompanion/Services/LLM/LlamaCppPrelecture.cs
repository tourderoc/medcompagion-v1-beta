using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace MedCompanion.Services.LLM
{
    /// <summary>
    /// Garde l'AUTRE modèle llama.cpp prêt dans le cache fichier de Windows, pour que le switch Qwen ↔
    /// Gemma parte d'un fichier déjà en mémoire plutôt que du disque.
    ///
    /// Pourquoi ça marche — mesuré le 12/09/2026 : Qwen (13,5 Go) se charge en ~70 s lu depuis le SSD
    /// SATA, en 4,6 s quand Windows a encore le fichier en cache. llama-server remplit ce cache tout seul
    /// en chargeant un modèle ; le seul cas lent est donc un modèle pas encore lu depuis le démarrage du PC,
    /// ou que Windows a évincé pour faire de la place. C'est exactement ce que couvre cette pré-lecture.
    ///
    /// Pourquoi « l'autre » suffit : Med sert deux modèles, pas plus (décision du 14/09/2026). Quand l'un
    /// est chargé, celui qu'il faudra au prochain switch est toujours connu — pas besoin d'anticiper selon
    /// les étapes de consultation.
    ///
    /// Ce n'est PAS un second modèle chargé en RAM : aucun serveur, aucune mémoire réservée. Le cache est de
    /// la mémoire en veille que Windows reprend dès qu'un programme en a besoin ; il ne peut pas faire
    /// saturer la machine. D'où le garde-fou sur la RAM DISPONIBLE (et non « utilisée ») : on ne pré-lit que
    /// s'il reste de quoi contenir le fichier plus une marge, sinon on chasserait le cache pour rien.
    ///
    /// Voir PLAN_MOTEUR_LLM_LOCAL.md, étape 7.
    /// </summary>
    public static class LlamaCppPrelecture
    {
        private const long MargeOctets = 4L * 1024 * 1024 * 1024;
        private const int  TailleBloc  = 4 * 1024 * 1024;

        /// <summary>Rafraîchissement : relire un fichier déjà en cache ne coûte presque rien, et rattrape une
        /// éviction survenue entre-temps.</summary>
        private static readonly TimeSpan Intervalle = TimeSpan.FromMinutes(30);

        private static readonly string JournalPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MedCompanion", "prelecture-modeles.log");

        private static readonly object _sync = new();
        private static int _enCours;
        private static CancellationTokenSource? _annulation;
        private static Timer? _minuteur;
        private static bool _demarre;

        private static LlamaCppModelProfile? _derniereCible;
        private static DateTime _derniereFin = DateTime.MinValue;
        private static int _dernierChargementJournalise;

        /// <summary>À appeler une fois au démarrage de l'application.</summary>
        public static void Demarrer()
        {
            if (_demarre) return;
            _demarre = true;

            try { if (!AppSettings.Load().LlamaCppPrelectureActive) { Journal("pré-lecture désactivée (réglage LlamaCppPrelectureActive)"); return; } }
            catch { /* réglages illisibles : active par défaut */ }

            LlamaCppServerManager.EtatChange += OnEtatChange;
            _minuteur = new Timer(_ => Lancer(rafraichissement: true), null, Intervalle, Intervalle);
        }

        /// <summary>À appeler à la fermeture : interrompt une lecture en cours.</summary>
        public static void Arreter()
        {
            LlamaCppServerManager.EtatChange -= OnEtatChange;
            _minuteur?.Dispose();
            _minuteur = null;
            lock (_sync) _annulation?.Cancel();
        }

        private static void OnEtatChange()
        {
            switch (LlamaCppServerManager.Etat)
            {
                case EtatMoteurLlm.Chargement:
                    // Le serveur lit son propre fichier : ne pas lui disputer le disque.
                    lock (_sync) _annulation?.Cancel();
                    break;

                case EtatMoteurLlm.Pret:
                    JournaliserChargement();
                    Lancer(rafraichissement: false);
                    break;
            }
        }

        /// <summary>Durée réelle de chaque chargement, une seule fois par chargement : c'est la mesure qui
        /// dit si le switch est court.</summary>
        private static void JournaliserChargement()
        {
            var numero = LlamaCppServerManager.NumeroDernierChargement;
            var profil = LlamaCppServerManager.RunningProfile;
            if (numero == 0 || profil == null || Interlocked.Exchange(ref _dernierChargementJournalise, numero) == numero)
                return;

            Journal($"chargement · {profil.ShortName}{(LlamaCppServerManager.RunningModeIsVision == true ? " (vision)" : "")} " +
                    $"prêt en {LlamaCppServerManager.DureeDernierChargement.TotalSeconds:0.0} s");
        }

        private static void Lancer(bool rafraichissement)
        {
            var charge = LlamaCppServerManager.RunningProfile;
            if (charge == null || LlamaCppServerManager.Etat is not (EtatMoteurLlm.Pret or EtatMoteurLlm.Genere))
                return;

            var autre = LlamaCppProfiles.All.FirstOrDefault(p => !ReferenceEquals(p, charge) && p.IsReady);
            if (autre == null) return;

            lock (_sync)
            {
                // « Prêt » est aussi republié après chaque génération : sans ce filtre, on relirait 13 Go à
                // chaque réponse du modèle. Le rafraîchissement périodique, lui, passe toujours.
                if (!rafraichissement && ReferenceEquals(autre, _derniereCible) && DateTime.Now - _derniereFin < Intervalle)
                    return;

                if (Interlocked.Exchange(ref _enCours, 1) == 1) return;

                _annulation?.Dispose();
                _annulation = new CancellationTokenSource();
                var jeton = _annulation.Token;

                // Fil dédié plutôt que le pool : sa priorité basse ne doit pas se propager à d'autres tâches.
                new Thread(() =>
                {
                    try { Prelire(autre, rafraichissement ? "rafraîchissement" : "modèle prêt", jeton); }
                    catch (Exception ex) { Journal($"erreur · {ex.Message}"); }
                    finally { Volatile.Write(ref _enCours, 0); }
                })
                {
                    IsBackground = true,
                    Priority     = ThreadPriority.BelowNormal,
                    Name         = "Pré-lecture modèle llama.cpp"
                }.Start();
            }
        }

        private static void Prelire(LlamaCppModelProfile profil, string raison, CancellationToken jeton)
        {
            var fichiers = new[] { profil.ModelPath, profil.DraftModelPath, profil.MmprojPath }
                .Where(f => !string.IsNullOrEmpty(f) && File.Exists(f))
                .ToList();

            var tampon = new byte[TailleBloc];

            foreach (var fichier in fichiers)
            {
                var nom    = Path.GetFileName(fichier);
                var taille = new FileInfo(fichier!).Length;

                var disponible = MemoireDisponibleOctets();
                if (disponible < taille + MargeOctets)
                {
                    Journal($"{raison} · {nom} ignoré : {disponible / 1048576:N0} Mo disponibles, " +
                            $"il en faut {(taille + MargeOctets) / 1048576:N0}");
                    return;
                }

                var chrono = Stopwatch.StartNew();
                long lu    = 0;
                var blocs  = 0;

                using (var flux = new FileStream(fichier!, FileMode.Open, FileAccess.Read,
                                                 FileShare.ReadWrite | FileShare.Delete, 1024 * 1024, FileOptions.SequentialScan))
                {
                    PrioriteEntreesSortiesBasse(flux.SafeFileHandle);

                    int n;
                    while ((n = flux.Read(tampon, 0, tampon.Length)) > 0)
                    {
                        lu += n;

                        if (jeton.IsCancellationRequested)
                        {
                            Journal($"{raison} · {nom} interrompu à {lu / 1048576:N0} Mo : un chargement commence");
                            return;
                        }

                        // Tous les ~256 Mo : si la mémoire se tend en cours de route, on s'arrête.
                        if (++blocs % 64 == 0 && MemoireDisponibleOctets() < MargeOctets)
                        {
                            Journal($"{raison} · {nom} interrompu à {lu / 1048576:N0} Mo : mémoire disponible sous la marge");
                            return;
                        }
                    }
                }

                chrono.Stop();
                var secondes = Math.Max(0.001, chrono.Elapsed.TotalSeconds);
                var debit    = lu / 1048576.0 / secondes;
                Journal($"{raison} · {profil.ShortName} · {nom} · {lu / 1048576:N0} Mo en {secondes:0.0} s " +
                        $"({debit:N0} Mo/s){(debit > 1000 ? " — déjà en cache" : "")}");
            }

            lock (_sync)
            {
                _derniereCible = profil;
                _derniereFin   = DateTime.Now;
            }
        }

        // ── Journal ──────────────────────────────────────────────────────────

        private static readonly object _journalSync = new();

        private static void Journal(string ligne)
        {
            try
            {
                lock (_journalSync)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(JournalPath)!);

                    // Borné : au-delà de 512 Ko, l'ancien journal est gardé une fois puis remplacé.
                    var info = new FileInfo(JournalPath);
                    if (info.Exists && info.Length > 512 * 1024)
                        File.Move(JournalPath, JournalPath + ".ancien", overwrite: true);

                    File.AppendAllText(JournalPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {ligne}{Environment.NewLine}");
                }
            }
            catch { /* journal best-effort */ }
        }

        // ── Windows ──────────────────────────────────────────────────────────

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint  dwLength;
            public uint  dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX statut);

        /// <summary>RAM disponible au sens de Windows : libre + en veille (cache repris à la demande).</summary>
        private static long MemoireDisponibleOctets()
        {
            var statut = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            return GlobalMemoryStatusEx(ref statut) ? (long)statut.ullAvailPhys : 0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FILE_IO_PRIORITY_HINT_INFO
        {
            public int PriorityHint;
        }

        private const int FileIoPriorityHintInfo = 12;
        private const int IoPriorityHintLow      = 1;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetFileInformationByHandle(
            SafeFileHandle fichier, int classe, ref FILE_IO_PRIORITY_HINT_INFO info, int taille);

        /// <summary>Priorité d'entrées-sorties basse : Windows sert d'abord les lectures des autres programmes
        /// (Doctolib, dictée...). Sans effet si le système refuse — la lecture continue normalement.</summary>
        private static void PrioriteEntreesSortiesBasse(SafeFileHandle fichier)
        {
            try
            {
                var info = new FILE_IO_PRIORITY_HINT_INFO { PriorityHint = IoPriorityHintLow };
                SetFileInformationByHandle(fichier, FileIoPriorityHintInfo, ref info, Marshal.SizeOf<FILE_IO_PRIORITY_HINT_INFO>());
            }
            catch { /* indication facultative */ }
        }
    }
}
