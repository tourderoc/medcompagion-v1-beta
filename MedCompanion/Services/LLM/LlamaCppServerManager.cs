using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MedCompanion.Services.LLM
{
    /// <summary>
    /// Gère le cycle de vie du process llama-server.exe, utilisé uniquement pour le modèle
    /// Qwen3.8-27B : ce modèle a besoin du cache KV compressé (q8_0) pour tenir un contexte de
    /// 32768 tokens sans déborder de la VRAM — réglage qu'Ollama n'expose pas dans son API. Tous
    /// les autres modèles (Gemma, gpt-oss...) continuent de tourner sur Ollama.
    /// Config figée après tests comparatifs : MTP activé avec les réglages corrects (gpu-layers-draft,
    /// parallel 1, reasoning-effort medium — sans ces trois réglages précis, le MTP est contre-productif
    /// pour ce modèle, mesuré ~4x plus lent ; avec, mesuré 27-34 t/s contre 26 t/s sans MTP). Contexte
    /// 32768 (meilleur compromis vitesse/contexte mesuré) avec cache KV compressé q8_0.
    /// Vision activée via le fichier mmproj officiel Unsloth (le GGUF texte de cette quantization
    /// communautaire n'inclut pas la vision) — testé fiable sur la lecture de cases à cocher, contrairement
    /// à GLM-OCR. Coût mesuré : +~460 Mo de VRAM seulement.
    /// </summary>
    public static class LlamaCppServerManager
    {
        private const string ExePath = @"C:\Users\nair\llama.cpp\build\bin\Release\llama-server.exe";
        private const int    Port    = 8899;

        /// <summary>
        /// Modèle actuellement servi. Le changer ne fait rien par lui-même : le redémarrage a lieu
        /// au prochain <see cref="EnsureRunningAsync"/>, qui détecte que le profil du process en
        /// cours diffère de celui demandé. Un seul modèle à la fois — les deux ne tiennent pas
        /// ensemble en VRAM.
        /// </summary>
        public static LlamaCppModelProfile CurrentProfile { get; set; } = LlamaCppProfiles.Qwen;

        /// <summary>Profil du process en cours (null si rien ne tourne).</summary>
        private static LlamaCppModelProfile? _runningProfile;

        /// <summary>Journal du process (démarrage, vitesse par requête...) — utile pour diagnostiquer
        /// une lenteur signalée après coup, écrasé à chaque redémarrage du serveur.</summary>
        public static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MedCompanion", "llama-server.log");

        public static string BaseUrl => $"http://127.0.0.1:{Port}";

        private static Process? _process;
        private static readonly SemaphoreSlim _lock = new(1, 1);

        // Incrémenté à chaque arrêt demandé. Le verrou seul ne suffit pas à ordonner arrêt et
        // démarrage : EnsureRunningAsync le retient pendant TOUT le chargement (jusqu'à 150 s) alors
        // que Stop() ne l'attend que 5 s avant de passer outre. Un démarrage lancé juste avant une
        // bascule de modèle pouvait donc se terminer APRÈS l'arrêt et laisser un serveur vivant que
        // plus personne ne suivait — Qwen (14,6 Go) et le modèle Ollama chargé dans la foulée
        // (12,4 Go) se retrouvaient en VRAM en même temps : saturation puis gel complet de la machine.
        private static int _stopGeneration;
        private static readonly HttpClient _healthClient = new() { Timeout = TimeSpan.FromSeconds(3) };

        // Déchargement automatique après inactivité, même principe que le keep_alive d'Ollama : sans
        // ça, le modèle reste chargé indéfiniment (VRAM/RAM occupées) même quand il n'est plus utilisé.
        private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(5);
        private static DateTime _lastActivity = DateTime.MinValue;
        private static Timer? _idleTimer;

        public static bool IsRunning => _process != null && !_process.HasExited;

        /// <summary>PID du serveur en cours, ou null. Utilisé pour lire son occupation VRAM.</summary>
        public static int? RunningProcessId
        {
            get
            {
                try { return IsRunning ? _process!.Id : null; }
                catch { return null; }
            }
        }

        /// <summary>Profil effectivement chargé, qui peut différer de <see cref="CurrentProfile"/>
        /// tant que le redémarrage n'a pas eu lieu.</summary>
        public static LlamaCppModelProfile? RunningProfile => _runningProfile;

        /// <summary>
        /// Nettoie tout llama-server.exe orphelin dès le démarrage de l'app, avant même que
        /// l'utilisateur ait sélectionné un modèle. Sans cet appel, un orphelin d'une session
        /// précédente mal fermée (crash, fermeture forcée) reste invisible tant que l'app ne
        /// bascule pas vers llama.cpp (le nettoyage habituel n'a lieu qu'à ce moment-là).
        /// À appeler une fois, tôt dans l'initialisation de MainWindow.
        /// </summary>
        public static void CleanupOrphansAtStartup() => KillOrphanProcesses(keepTracked: false);

        /// <summary>Mode actif du process en cours (null si rien ne tourne) : true = vision (sans
        /// MTP), false = texte (avec MTP). Voir <see cref="EnsureRunningAsync"/>.</summary>
        private static bool? _runningModeIsVision;

        /// <summary>
        /// Raisonnement activé (true) ou coupé net (false). Contrairement au NIVEAU de réflexion
        /// (`reasoning_effort`, réglable par requête), l'interrupteur `--reasoning off` est un drapeau
        /// de démarrage : le changer impose un redémarrage du serveur (~25-50 s). Réglé par
        /// LlamaCppProvider selon le choix de l'utilisateur, avant l'appel à EnsureRunningAsync.
        /// </summary>
        public static bool ReasoningEnabled { get; set; } = true;

        /// <summary>Valeur effective au démarrage du process en cours (null si rien ne tourne).</summary>
        private static bool? _runningReasoningEnabled;

        /// <summary>
        /// Démarre llama-server si nécessaire et attend qu'il réponde. Sans effet si déjà actif
        /// dans le mode demandé (appel bon marché, peut être fait avant chaque requête).
        /// </summary>
        /// <param name="forVision">
        /// true pour une requête image (formulaire...). Mesuré : le MTP rend le traitement d'image
        /// catastrophique (280s de traitement de prompt au lieu de quelques secondes, avec erreurs
        /// internes "non-consecutive token position") — MTP et vision sont incompatibles ensemble
        /// sur ce modèle. On redémarre donc sans MTP le temps de la requête image, puis on repasse
        /// en mode texte+MTP à la prochaine utilisation texte. Coût : un rechargement (~30-50s) à
        /// chaque bascule entre les deux usages — acceptable, la lecture d'image est occasionnelle
        /// (une fois par formulaire), pas le flux principal.
        /// </param>
        public static async Task<(bool success, string message)> EnsureRunningAsync(bool forVision = false)
        {
            await _lock.WaitAsync();
            try
            {
                _lastActivity = DateTime.Now;

                // Une fois démarré, on ne refait PAS de vérification HTTP à chaque appel : avec un
                // seul créneau de traitement (-np 1), un serveur occupé à générer une réponse peut
                // légitimement ne pas répondre vite à /health — le prendre pour "mort" et en
                // relancer un second a déjà causé un doublon (deux process, GPU/RAM saturés). Le
                // process vivant (HasExited == false) est un signal bien plus fiable ici — mais si le
                // mode demandé diffère de celui en cours (texte↔vision), il faut redémarrer.
                // Le mode couvre trois choses fixées au démarrage : le modèle servi, la vision
                // (mmproj) et l'interrupteur de raisonnement. Changer l'une impose un redémarrage.
                // La lecture d'image peut utiliser un AUTRE modèle que celui sélectionné pour le
                // texte : Qwen en vision occupe 13,8 Go et déborde, Gemma 4 tient en 7,6 Go. Comme
                // le mode vision impose déjà un redémarrage (MTP incompatible), changer de modèle au
                // passage ne coûte rien de plus.
                var wantedProfile = forVision ? LlamaCppProfiles.VisionProfile : CurrentProfile;

                bool sameMode = _runningModeIsVision == forVision
                             && _runningReasoningEnabled == ReasoningEnabled
                             && _runningProfile == wantedProfile;

                if (IsRunning && sameMode)
                    return (true, "llama-server déjà actif");

                if (IsRunning && !sameMode)
                    StopInternal();

                if (_process != null && _process.HasExited)
                    _process = null;

                // Nettoyer tout llama-server.exe orphelin d'une session précédente (crash, fermeture
                // forcée sans passer par Window_Closing...) : sans ça, on démarrerait un second
                // process en plus de l'orphelin, doublant la charge GPU/RAM et dégradant la vitesse.
                KillOrphanProcesses();

                var profile = wantedProfile;

                if (!File.Exists(ExePath))
                    return (false, $"llama-server.exe introuvable : {ExePath}");
                // IsReady et non File.Exists : pendant un téléchargement le fichier existe déjà mais
                // tronqué, et llama-server échoue alors sur un GGUF invalide sans message clair.
                if (!profile.IsReady)
                {
                    var progress = profile.DownloadProgress;
                    return (false, progress is double pct
                        ? $"{profile.ShortName} : téléchargement en cours ({pct * 100:0} %)."
                        : $"Modèle GGUF introuvable ({profile.DisplayName}) : {profile.ModelPath}");
                }
                if (forVision && !profile.HasVision)
                    return (false, $"{profile.DisplayName} n'a pas de projecteur de vision configuré.");
                if (forVision && !File.Exists(profile.MmprojPath))
                    return (false, $"Fichier vision (mmproj) introuvable : {profile.MmprojPath}");
                if (profile.MtpEffective && !string.IsNullOrEmpty(profile.DraftModelPath)
                    && !File.Exists(profile.DraftModelPath))
                    return (false, $"Brouillon MTP introuvable : {profile.DraftModelPath}");

                // spec-draft-n-max = tokens que le brouillon MTP propose par passe de vérification.
                // MESURÉ sur ce modèle, ne pas remonter sans nouvelle mesure :
                //   n-max 3 → acceptation 0,84 · 48 t/s
                //   n-max 5 → acceptation 0,37 · 34 t/s
                // Au-delà de 3, le brouillon MTP part trop loin : les tokens refusés sont générés
                // puis jetés, et leur coût dépasse le gain. La longueur moyenne acceptée (3,53 à
                // n-max 3) ne signalait donc pas un plafond bridant le brouillon, mais son optimum.
                // Conditionné au profil : le gemma4:12b standard n'embarque pas les tenseurs `nextn`
                // (vérifié par inspection du GGUF), passer ces drapeaux ferait échouer son démarrage.
                var mtpArgs = "";
                if (!forVision && profile.MtpEffective)
                {
                    mtpArgs = $"--spec-type draft-mtp --spec-draft-n-max {profile.DraftTokens} --gpu-layers-draft all ";

                    // Gemma 4 livre son brouillon dans un fichier séparé ; Qwen l'embarque dans le
                    // modèle et n'a donc rien à désigner ici.
                    if (!string.IsNullOrEmpty(profile.DraftModelPath))
                        mtpArgs += $"--spec-draft-model \"{profile.DraftModelPath}\" ";
                }

                // Le mmproj (885 Mo) n'est chargé QUE pour la vision. Le garder résident en mode
                // texte saturait la carte : llama.cpp lui-même refusait de tenir le budget
                // ("failed to fit params to free device memory") et le pilote débordait sur la
                // mémoire partagée. Le mode vision provoque déjà un redémarrage (MTP incompatible),
                // donc charger le mmproj à la demande ne coûte aucun redémarrage supplémentaire.
                var visionArgs = forVision ? $"--mmproj \"{profile.MmprojPath}\" " : "";

                // `--reasoning off` coupe la réflexion à la source. `--chat-template-kwargs
                // enable_thinking=false` seul ne suffit pas sur cette famille de modèles (le bloc de
                // réflexion réapparaît) : les deux sont posés ensemble, comme recommandé.
                // Sans objet sur un modèle qui ne raisonne pas (Gemma) : rien à couper.
                var reasoningArgs = (ReasoningEnabled || !profile.SupportsReasoning)
                    ? ""
                    : "--reasoning off --reasoning-budget 0 ";

                // Niveau de réflexion par défaut du serveur : uniquement pour les modèles dont le
                // template l'accepte. L'envoyer à Gemma ferait échouer le rendu du template.
                var effortArgs = profile.SupportsReasoning ? "--reasoning-effort medium " : "";

                // Cache KV compressé : c'est lui qui rend les contextes longs possibles sans
                // déborder. Le désactiver double l'empreinte du cache — réglable, mais rarement
                // souhaitable ici.
                var kvArgs = profile.KvQuantized ? "-ctk q8_0 -ctv q8_0 " : "";

                var psi = new ProcessStartInfo
                {
                    FileName               = ExePath,
                    // Vision et MTP s'excluent : chaque mode a son jeu d'arguments (voir forVision).
                    Arguments              = $"-m \"{profile.ModelPath}\" --no-mmap " +
                                              visionArgs +
                                              reasoningArgs +
                                              mtpArgs +
                                              $"-ngl 99 -np 1 -kvu " +
                                              $"-fa on " + kvArgs +
                                              $"-c {profile.ContextSize} " +
                                              effortArgs +
                                              $"--port {Port}",
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true
                };

                _process = Process.Start(psi);
                if (_process == null)
                    return (false, "Impossible de démarrer llama-server.exe");

                // Drainer la sortie vers un fichier de log : indispensable, sinon le tampon du pipe
                // se remplit (llama-server est très verbeux) et le process peut se bloquer en
                // attendant qu'on le lise.
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                    var logWriter = new StreamWriter(LogPath, append: false) { AutoFlush = true };
                    _process.OutputDataReceived += (_, e) => { if (e.Data != null) SafeWriteLog(logWriter, e.Data); };
                    _process.ErrorDataReceived  += (_, e) => { if (e.Data != null) SafeWriteLog(logWriter, e.Data); };
                    _process.BeginOutputReadLine();
                    _process.BeginErrorReadLine();
                }
                catch
                {
                    // Best-effort : l'absence de log ne doit pas empêcher le serveur de démarrer.
                }

                // Chargement mesuré ~6-7 s en pratique (3 démarrages consécutifs relevés dans le
                // journal) ; --no-mmap lit le fichier
                // intégralement au démarrage (au lieu d'un mapping paresseux) donc un peu plus lent
                // à charger — marge portée à 150s par sécurité.
                // Capturé APRÈS le StopInternal() de changement de mode plus haut, sinon on
                // s'auto-annulerait. Référence locale sur le process : _process peut être remis à
                // null par un Stop() concurrent (qui n'a pas le verrou), ce qui ferait planter la
                // boucle ci-dessous sur un null.
                var startGeneration = Volatile.Read(ref _stopGeneration);
                var proc            = _process;

                var deadline = DateTime.Now.AddSeconds(150);
                while (DateTime.Now < deadline)
                {
                    // Un arrêt a-t-il été demandé pendant qu'on chargeait (bascule vers Gemma...) ?
                    // Si oui, ce serveur ne doit pas survivre : on le tue nous-mêmes et on échoue.
                    if (Volatile.Read(ref _stopGeneration) != startGeneration)
                    {
                        ForceKill(proc, 5000);
                        if (ReferenceEquals(_process, proc)) _process = null;
                        return (false, "Démarrage annulé : changement de modèle demandé pendant le chargement.");
                    }

                    if (proc.HasExited)
                        return (false, $"llama-server s'est arrêté immédiatement (code {proc.ExitCode}).");
                    if (await IsHealthyAsync())
                    {
                        // Dernière vérification avant de le déclarer prêt : un arrêt a pu arriver
                        // pendant l'appel /health.
                        if (Volatile.Read(ref _stopGeneration) != startGeneration)
                        {
                            ForceKill(proc, 5000);
                            if (ReferenceEquals(_process, proc)) _process = null;
                            return (false, "Démarrage annulé : changement de modèle demandé pendant le chargement.");
                        }
                        _runningModeIsVision      = forVision;
                        _runningReasoningEnabled  = ReasoningEnabled;
                        _runningProfile           = profile;
                        StartIdleWatcher();
                        return (true, "llama-server démarré et prêt.");
                    }
                    await Task.Delay(1000);
                }

                return (false, "Timeout : llama-server n'a pas répondu après 150s.");
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>Tue tout llama-server.exe issu de NOTRE build qui tournerait sans qu'on le suive
        /// (orphelin d'une session précédente mal fermée — crash, fermeture forcée). Celui qu'on suit
        /// nous-même (<see cref="_process"/>) est épargné si <paramref name="keepTracked"/> est vrai.
        /// Attend la terminaison effective de chaque processus tué : sans ça, un orphelin encore en
        /// cours d'arrêt peut cohabiter brièvement avec le nouveau process qu'on s'apprête à démarrer.
        ///
        /// IMPORTANT — filtrage par chemin d'exécutable : Ollama embarque llama.cpp et lance SON PROPRE
        /// llama-server.exe (depuis %LOCALAPPDATA%\Programs\Ollama\lib\ollama\) dès qu'un modèle y est
        /// chargé. Tuer par nom seul couperait donc Gemma & co. en pleine utilisation.</summary>
        private static void KillOrphanProcesses(bool keepTracked = true)
        {
            try
            {
                foreach (var proc in Process.GetProcessesByName("llama-server"))
                {
                    if (keepTracked && _process != null && proc.Id == _process.Id) continue;
                    if (!IsOurServer(proc)) continue;
                    ForceKill(proc, 3000);
                }
            }
            catch { /* best-effort */ }
        }

        /// <summary>Vrai si ce process est bien notre llama-server (celui de <see cref="ExePath"/>) et
        /// non celui embarqué par Ollama. En cas de doute (chemin illisible), on ne touche pas.</summary>
        private static bool IsOurServer(Process proc)
        {
            try
            {
                var path = proc.MainModule?.FileName;
                return path != null && string.Equals(path, ExePath, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Tue un process avec repli forcé : un llama-server interrompu en pleine initialisation GPU
        /// (ex. arrêt demandé pendant le chargement CUDA) peut rester bloqué dans un état "Unknown"
        /// que <see cref="Process.Kill"/> seul ne débloque pas toujours — constaté en pratique. Si le
        /// process n'a pas terminé après le délai, on tente un second passage via <c>taskkill /F</c>
        /// (plus agressif que l'API .NET dans ce cas précis).
        /// </summary>
        private static void ForceKill(Process proc, int waitMs)
        {
            try
            {
                proc.Kill(entireProcessTree: true);
                if (proc.WaitForExit(waitMs)) return;

                using var taskkill = Process.Start(new ProcessStartInfo
                {
                    FileName        = "taskkill",
                    Arguments       = $"/F /T /PID {proc.Id}",
                    UseShellExecute = false,
                    CreateNoWindow  = true
                });
                taskkill?.WaitForExit(3000);
            }
            catch { /* déjà arrêté, ou pas les droits — best-effort */ }
        }

        private static readonly object _logSync = new();
        private static void SafeWriteLog(StreamWriter writer, string line)
        {
            lock (_logSync)
            {
                try { writer.WriteLine(line); } catch { /* best-effort */ }
            }
        }

        private static async Task<bool> IsHealthyAsync()
        {
            try
            {
                var resp = await _healthClient.GetAsync($"{BaseUrl}/health");
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Démarre la surveillance d'inactivité (une seule instance à la fois — sans effet
        /// si déjà en cours). Vérifie toutes les minutes si <see cref="IdleTimeout"/> est dépassé.</summary>
        private static void StartIdleWatcher()
        {
            _idleTimer?.Dispose();
            _idleTimer = new Timer(_ =>
            {
                if (IsRunning && DateTime.Now - _lastActivity > IdleTimeout)
                    Stop();
            }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        /// <summary>
        /// Arrête llama-server et libère la VRAM — à appeler quand on bascule vers un autre modèle,
        /// pour laisser la place aux modèles Ollama sur la même carte graphique.
        /// Se synchronise avec <see cref="EnsureRunningAsync"/> (attente bornée à 5s) : sans ça, un
        /// démarrage encore en cours (ex. warm-up au lancement de l'app sur un modèle persisté) et un
        /// arrêt demandé en parallèle (bascule immédiate vers un autre modèle) pouvaient s'entrelacer
        /// sans se voir — le serveur démarré par le premier survivait malgré l'appel à Stop() du
        /// second. Constaté : llama-server toujours présent après bascule vers Gemma juste après le
        /// démarrage de l'app.
        /// </summary>
        public static void Stop()
        {
            bool acquired = _lock.Wait(TimeSpan.FromSeconds(5));
            try
            {
                StopInternal();
            }
            finally
            {
                if (acquired) _lock.Release();
            }
        }

        /// <summary>Implémentation sans verrou : appelée par <see cref="Stop"/> (verrou pris avant
        /// l'appel) et par <see cref="EnsureRunningAsync"/> (déjà tenu par le verrou — un second wait
        /// dessus bloquerait indéfiniment).</summary>
        private static void StopInternal()
        {
            // Signale à un démarrage éventuellement en cours qu'il doit se saborder (voir _stopGeneration).
            Interlocked.Increment(ref _stopGeneration);

            _idleTimer?.Dispose();
            _idleTimer = null;
            _runningModeIsVision     = null;
            _runningReasoningEnabled = null;
            _runningProfile          = null;

            try
            {
                if (_process != null && !_process.HasExited)
                {
                    // Attend la terminaison effective (avec repli forcé si besoin, voir ForceKill) :
                    // sans ça, le pilote GPU n'a pas forcément eu le temps de récupérer la VRAM avant
                    // qu'un autre modèle (Ollama) ne tente de charger juste après, provoquant un
                    // dépassement mémoire temporaire.
                    ForceKill(_process, 5000);
                }

                // Ne tuer QUE _process laissait survivre tout orphelin déjà présent avant qu'on ait
                // nous-mêmes démarré un serveur (ex. session précédente mal fermée) : constaté — il
                // survivait à un aller-retour complet Qwen → autre modèle → Qwen. En quittant
                // llama.cpp complètement, on nettoie tout llama-server.exe qui traînerait.
                KillOrphanProcesses(keepTracked: false);
            }
            catch
            {
                // Best-effort : si le kill échoue, le process sera de toute façon nettoyé à la fermeture de l'app.
            }
            finally
            {
                _process = null;
            }
        }
    }
}
