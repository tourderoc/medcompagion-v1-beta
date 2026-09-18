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
        /// <summary>Emplacement du moteur si aucun réglage n'est posé : le dossier de sortie de la
        /// compilation locale de llama.cpp, historique de la machine de développement.</summary>
        private const string ExePathParDefaut = @"C:\Users\nair\llama.cpp\build\bin\Release\llama-server.exe";

        /// <summary>Chemin réel du moteur : réglage <c>LlamaCppExePath</c> de appsettings.json s'il
        /// est renseigné, sinon <see cref="ExePathParDefaut"/>. Résolu une seule fois au chargement
        /// du type — changer le réglage suppose donc de redémarrer l'application. Sert aussi de
        /// critère d'identité dans <see cref="IsOurServer"/>, d'où l'importance qu'il soit stable.</summary>
        private static readonly string ExePath = ResoudreExePath();

        /// <summary>
        /// Restreint llama-server à une seule carte graphique, si AppSettings.LlamaCppGpuUuid
        /// est renseigné. La variable n'est posée que sur ce processus : au niveau de la session
        /// elle masquerait l'autre carte à Whisper, qui doit précisément tourner dessus.
        ///
        /// GGML_VK_VISIBLE_DEVICES accompagne CUDA_VISIBLE_DEVICES : sans elle, un moteur compilé
        /// avec le support Vulkan retrouve la carte écartée par cet autre chemin et recommence à
        /// répartir le modèle — constaté sur Ollama 0.33 le 11/09/2026.
        /// </summary>
        private static void AppliquerCarteGraphique(ProcessStartInfo psi)
        {
            try
            {
                var uuid = AppSettings.Load().LlamaCppGpuUuid?.Trim();
                if (string.IsNullOrWhiteSpace(uuid)) return;

                psi.EnvironmentVariables["CUDA_VISIBLE_DEVICES"] = uuid;
                psi.EnvironmentVariables["GGML_VK_VISIBLE_DEVICES"] = uuid;
            }
            catch
            {
                // Impossible de lire les réglages : on démarre sans restriction plutôt que
                // de priver l'application de son moteur local.
            }
        }

        /// <summary>« mmap » ou « no-mmap » (défaut, et repli si le réglage est illisible ou inconnu).</summary>
        private static string LireModeChargement()
        {
            try
            {
                var mode = AppSettings.Load().LlamaCppLoadMode?.Trim().ToLowerInvariant();
                return mode == "mmap" ? "mmap" : "no-mmap";
            }
            catch { return "no-mmap"; }
        }

        private static string ResoudreExePath()
        {
            try
            {
                var configure = AppSettings.Load().LlamaCppExePath;
                return string.IsNullOrWhiteSpace(configure) ? ExePathParDefaut : configure.Trim();
            }
            catch
            {
                // Réglages illisibles ou corrompus : on ne bloque pas le démarrage du moteur.
                return ExePathParDefaut;
            }
        }

        private const int    Port    = 8899;

        /// <summary>
        /// Modèle actuellement servi. Le changer ne fait rien par lui-même : le redémarrage a lieu
        /// au prochain <see cref="EnsureRunningAsync"/>, qui détecte que le profil du process en
        /// cours diffère de celui demandé. Un seul modèle à la fois — les deux ne tiennent pas
        /// ensemble en VRAM.
        /// </summary>
        public static LlamaCppModelProfile CurrentProfile { get; set; } = LlamaCppProfiles.Gemma4Qat;

        /// <summary>Profil du process en cours (null si rien ne tourne).</summary>
        private static LlamaCppModelProfile? _runningProfile;

        /// <summary>
        /// Dossier des journaux du serveur : UN fichier par chargement, horodaté et nommé d'après le
        /// modèle. Il n'y avait qu'un fichier, écrasé à chaque démarrage — et jamais refermé : au
        /// lancement suivant, il était encore tenu par l'ancien serveur, l'ouverture échouait sans
        /// rien dire, et le nouveau chargement n'avait AUCUN journal. Après une première bascule, on
        /// ne pouvait donc plus mesurer le chargement de l'autre modèle.
        /// </summary>
        public static readonly string LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MedCompanion", "logs");

        /// <summary>Journal du chargement en cours, ou du dernier (null avant le premier démarrage).</summary>
        public static string? LogPath { get; private set; }

        /// <summary>Nombre de journaux de chargement conservés — assez pour couvrir une journée de bascules.</summary>
        private const int JournauxConserves = 40;

        public static string BaseUrl => $"http://127.0.0.1:{Port}";

        /// <summary>
        /// Libération de la VRAM tenue par l'autre moteur, appelée juste avant de démarrer
        /// llama-server. Branchée par <see cref="LLMServiceFactory"/> sur le déchargement des
        /// modèles Ollama résidents. Laissée nulle, le démarrage se fait sans précaution — c'est
        /// l'ancien comportement.
        /// </summary>
        public static Func<Task>? LibererVramConcurrente { get; set; }

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

        // Déchargement automatique après inactivité. Il était de 5 minutes quand la carte était partagée
        // avec Ollama et Whisper ; depuis que la 5060 Ti est réservée au LLM (14/09/2026), un déchargement
        // aussi court ne libérait plus rien pour personne et faisait payer un rechargement après chaque
        // pause de consultation. Réglage LlamaCppIdleUnloadMinutes, 2 h par défaut ; 0 = jamais.
        private static readonly int IdleUnloadMinutes = LireDelaiInactivite();
        private static DateTime _lastActivity = DateTime.MinValue;
        private static Timer? _idleTimer;

        private static int LireDelaiInactivite()
        {
            try { return Math.Max(0, AppSettings.Load().LlamaCppIdleUnloadMinutes); }
            catch { return 120; }
        }

        // ── État publié ────────────────────────────────────────────────────────
        // Source unique de l'état du moteur : le voyant de l'en-tête ne fait que l'afficher. Avant, quatre
        // endroits écrivaient sa couleur à la main (warm-up, bascule, bouton décharger, paramètres), et
        // aucun ne voyait la veille, la bascule vision, l'arrêt depuis Pilotage ni un serveur mort :
        // le voyant restait vert sur un modèle déchargé.
        private static EtatMoteurLlm _etat = EtatMoteurLlm.Arrete;
        private static string _etatMessage = "Aucun modèle chargé.";
        private static int _requetesEnCours;
        private static int _echecsSante;
        private static int _surveillanceEnCours;

        /// <summary>État courant du moteur. Voir <see cref="EtatChange"/>.</summary>
        public static EtatMoteurLlm Etat => _etat;

        /// <summary>Précision sur l'état : cause d'une erreur, raison d'un déchargement.</summary>
        public static string EtatMessage => _etatMessage;

        /// <summary>Levé à chaque changement d'état, depuis n'importe quel thread.</summary>
        public static event Action? EtatChange;

        private static void PublierEtat(EtatMoteurLlm etat, string message)
        {
            _etat        = etat;
            _etatMessage = message;
            try { EtatChange?.Invoke(); } catch { /* un abonné défaillant ne doit pas bloquer le moteur */ }
        }

        /// <summary>
        /// À encadrer autour de chaque requête envoyée au serveur (<c>using var _ = SuivreRequete();</c>).
        /// Fait passer l'état à « génère » tant qu'au moins une requête est en cours, et date la fin de
        /// la dernière — c'est d'elle, et non du début, que se mesure l'inactivité.
        /// </summary>
        public static IDisposable SuivreRequete()
        {
            if (Interlocked.Increment(ref _requetesEnCours) == 1 && ServeurPret)
                PublierEtat(EtatMoteurLlm.Genere, "Génération en cours.");
            return new FinDeRequete();
        }

        /// <summary>Un modèle est chargé ET a répondu — et non un process encore en chargement. Sans cette
        /// nuance, une requête texte interrompue par un redémarrage vision republiait « prêt » à sa fin,
        /// pendant que le modèle de vision chargeait encore.</summary>
        private static bool ServeurPret =>
            IsRunning && _runningProfile != null && (_etat is EtatMoteurLlm.Pret or EtatMoteurLlm.Genere);

        private sealed class FinDeRequete : IDisposable
        {
            private int _terminee;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _terminee, 1) == 1) return;
                _lastActivity = DateTime.Now;
                if (Interlocked.Decrement(ref _requetesEnCours) == 0 && ServeurPret && _etat == EtatMoteurLlm.Genere)
                    PublierEtat(EtatMoteurLlm.Pret, "Prêt.");
            }
        }

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

        /// <summary>Le process en cours sert la vision (mmproj chargé, sans MTP) ; null si rien ne tourne.</summary>
        public static bool? RunningModeIsVision => _runningModeIsVision;

        private static bool _chargementVision;

        private static readonly Stopwatch _chronoChargement = new();

        /// <summary>Durée du dernier chargement réussi, de la décision de démarrer à la première réponse du
        /// serveur (préparatifs compris). Mesure de référence du switch — voir LlamaCppPrelecture.</summary>
        public static TimeSpan DureeDernierChargement { get; private set; }

        /// <summary>Incrémenté à chaque chargement réussi : permet de ne journaliser chaque chargement qu'une fois.</summary>
        public static int NumeroDernierChargement { get; private set; }

        /// <summary>Le modèle chargé — ou en cours de chargement — est le modèle de vision. Contrairement à
        /// <see cref="RunningModeIsVision"/>, vaut aussi pendant le chargement, pour que le voyant l'annonce
        /// dès le début d'une lecture de formulaire.</summary>
        public static bool ModeVisionEnCours =>
            _etat == EtatMoteurLlm.Chargement ? _chargementVision : _runningModeIsVision == true;

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
            var resultat = await EnsureRunningCoreAsync(forVision);

            if (resultat.success)
            {
                // « Déjà actif » compris. Sans écraser « génère » : une requête peut être en cours.
                if (IsRunning && Volatile.Read(ref _requetesEnCours) == 0 && _etat != EtatMoteurLlm.Pret)
                    PublierEtat(EtatMoteurLlm.Pret, "Prêt.");
            }
            else if (!resultat.message.StartsWith("Démarrage annulé", StringComparison.Ordinal))
            {
                // Un démarrage annulé vient d'un arrêt demandé, qui a déjà publié « arrêté ».
                PublierEtat(EtatMoteurLlm.Erreur, resultat.message);
            }

            return resultat;
        }

        private static async Task<(bool success, string message)> EnsureRunningCoreAsync(bool forVision)
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
                // Testé avant IsReady : sans dossier, le message « modèle introuvable » afficherait un
                // chemin vide et enverrait chercher un fichier manquant au lieu d'un réglage manquant.
                if (!LlamaCppProfiles.ModelsDirConfigure)
                    return (false, "Dossier des modèles llama.cpp non renseigné (réglage LlamaCppModelsDir) : aucun modèle ne peut être chargé.");
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

                // Plafond de la réflexion quand elle N'EST PAS coupée. Distinct de --reasoning-effort,
                // qui règle l'intensité et non la durée : mesuré en effort « low », Qwen délibérait
                // encore ~2 000 tokens pour ~640 de réponse. Le compteur est côté serveur, donc il
                // s'applique aussi aux modèles qui ne déclarent pas la réflexion mais en produisent.
                // Posé seulement si la réflexion reste active, pour ne pas contredire le
                // `--reasoning-budget 0` ci-dessus, qui serait alors écrit deux fois.
                var budgetArgs = (reasoningArgs.Length == 0 && profile.ReasoningBudget > 0)
                    ? $"--reasoning-budget {profile.ReasoningBudget} " +
                      "--reasoning-budget-message \"Je dispose de ce que je sais ; je rédige maintenant la réponse.\" "
                    : "";

                // Niveau de réflexion par défaut du serveur : uniquement pour les modèles dont le
                // template l'accepte. L'envoyer à Gemma ferait échouer le rendu du template.
                //
                // ATTENTION EN DIAGNOSTIC : ce « medium » est un DÉFAUT DE REPLI, presque jamais
                // celui qui s'applique. Chaque requête porte son propre `reasoning_effort`, issu du
                // sélecteur de l'interface (voir LlamaCppProvider.BuildRequestBody), et il prime.
                // Le journal de démarrage affiche donc « medium » alors que les générations tournent
                // au niveau choisi — ne pas en conclure le niveau réel.
                var effortArgs = profile.SupportsReasoning ? "--reasoning-effort medium " : "";

                // Cache KV compressé : c'est lui qui rend les contextes longs possibles sans
                // déborder. Le désactiver double l'empreinte du cache — réglable, mais rarement
                // souhaitable ici.
                var kvArgs = profile.KvQuantized ? "-ctk q8_0 -ctv q8_0 " : "";

                // Mode de lecture du fichier, réglable pour comparer (voir AppSettings.LlamaCppLoadMode).
                var loadArgs = LireModeChargement() == "mmap" ? "--load-mode mmap " : "--no-mmap ";

                var psi = new ProcessStartInfo
                {
                    FileName               = ExePath,
                    // Vision et MTP s'excluent : chaque mode a son jeu d'arguments (voir forVision).
                    Arguments              = $"-m \"{profile.ModelPath}\" " + loadArgs +
                                              visionArgs +
                                              reasoningArgs +
                                              mtpArgs +
                                              $"-ngl 99 -np 1 -kvu " +
                                              $"-fa on " + kvArgs +
                                              $"-c {profile.ContextSize} " +
                                              effortArgs + budgetArgs +
                                              $"--port {Port}",
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true
                };

                AppliquerCarteGraphique(psi);

                // Le démarrage est décidé : publié AVANT les préparatifs, dont la libération d'Ollama peut
                // durer jusqu'à 15 s. Les marqueurs du process précédent sont effacés ici — s'il était mort
                // seul, rien ne l'avait fait, et la surveillance d'arrêt ci-dessous l'aurait pris pour
                // celui qu'on démarre.
                _runningModeIsVision     = null;
                _runningReasoningEnabled = null;
                _runningProfile          = null;
                _chargementVision        = forVision;
                _chronoChargement.Restart();
                PublierEtat(EtatMoteurLlm.Chargement,
                    $"Chargement de {profile.ShortName}{(forVision ? " (vision)" : "")}…");

                // Dernier verrou avant de démarrer : personne d'autre ne doit tenir le port. Les
                // nettoyages ci-dessus raisonnent sur ce qu'on CROIT suivre (_process) et sur la liste
                // des process ; celui-ci part de l'état réel du système et rattrape donc les cas où
                // l'on s'est trompé — serveur d'une session précédente, process perdu de vue après un
                // timeout, bascule concurrente.
                LibererLePort();
                var apresPort = _chronoChargement.Elapsed;

                // Rendre la VRAM tenue par l'AUTRE moteur avant d'allouer la nôtre. La fabrique le
                // fait déjà lors d'une bascule de provider, mais trois écrans instancient leur
                // propre LlamaCppProvider pour la vision (formulaires, cartographies) et démarrent
                // le serveur sans passer par elle : là, personne ne prévenait Ollama. Posé ici,
                // c'est-à-dire au seul endroit par lequel TOUT démarrage passe.
                if (LibererVramConcurrente != null)
                {
                    try { await LibererVramConcurrente(); }
                    catch { /* best-effort : ne doit jamais empêcher le moteur local de démarrer */ }
                }

                // Temps passé AVANT de lancer le process : arrêt de l'ancien serveur, port, Ollama. Le
                // « prêt en X s » publié l'inclut ; sans cette ligne, on ne saurait pas distinguer un
                // chargement lent d'un arrêt lent.
                var avantLancement = _chronoChargement.Elapsed;

                _process = Process.Start(psi);
                if (_process == null)
                    return (false, "Impossible de démarrer llama-server.exe");

                // Mort du serveur hors de tout arrêt demandé (plantage, carte perdue au réveil d'une mise
                // en veille...) : sans ce signal, le voyant resterait vert sur un process disparu.
                // Un arrêt voulu passe par StopInternal, qui incrémente _stopGeneration avant de tuer ;
                // un échec pendant le chargement laisse _runningProfile à null et se signale lui-même.
                var generationSuivie = Volatile.Read(ref _stopGeneration);
                var processSuivi     = _process;
                try
                {
                    processSuivi.EnableRaisingEvents = true;
                    processSuivi.Exited += (_, _) =>
                    {
                        if (Volatile.Read(ref _stopGeneration) == generationSuivie
                            && ReferenceEquals(_process, processSuivi)
                            && _runningProfile != null)
                        {
                            PublierEtat(EtatMoteurLlm.Erreur,
                                "Le serveur llama.cpp s'est arrêté de lui-même. Il redémarrera au prochain appel.");
                        }
                    };
                }
                catch { /* surveillance best-effort */ }

                // Drainer la sortie vers un fichier de log : indispensable, sinon le tampon du pipe
                // se remplit (llama-server est très verbeux) et le process peut se bloquer en
                // attendant qu'on le lise.
                StreamWriter? journal = null;
                try
                {
                    Directory.CreateDirectory(LogDir);
                    NettoyerAnciensJournaux();

                    LogPath = Path.Combine(LogDir,
                        $"llama-server_{DateTime.Now:yyyyMMdd_HHmmss}_{NomDeFichierSur(profile.Id)}{(forVision ? "_vision" : "")}.log");
                    var logWriter = new StreamWriter(LogPath, append: false) { AutoFlush = true };
                    journal = logWriter;

                    // En-tête : les horodatages de llama.cpp partent du lancement du process, pas de
                    // l'heure ni de la demande de chargement. On pose les deux repères.
                    SafeWriteLog(logWriter, $"# {DateTime.Now:yyyy-MM-dd HH:mm:ss} · {profile.ShortName}{(forVision ? " (vision)" : "")}");
                    SafeWriteLog(logWriter, $"# process lancé {avantLancement.TotalSeconds:0.0} s après la demande de chargement " +
                                            $"(port {apresPort.TotalSeconds:0.0} s · Ollama {(avantLancement - apresPort).TotalSeconds:0.0} s)");
                    SafeWriteLog(logWriter, $"# llama-server {psi.Arguments}");

                    // Fermé quand les DEUX flux sont taris — pas à l'événement Exited, qui peut arriver
                    // avant les dernières lignes. Un journal resté ouvert est précisément la panne corrigée.
                    var fluxOuverts = 2;
                    void FinDeFlux()
                    {
                        lock (_logSync)
                        {
                            if (--fluxOuverts > 0) return;
                            try { logWriter.Dispose(); } catch { /* best-effort */ }
                        }
                    }

                    _process.OutputDataReceived += (_, e) => { if (e.Data != null) SafeWriteLog(logWriter, e.Data); else FinDeFlux(); };
                    _process.ErrorDataReceived  += (_, e) => { if (e.Data != null) SafeWriteLog(logWriter, e.Data); else FinDeFlux(); };
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
                        DureeDernierChargement    = _chronoChargement.Elapsed;
                        NumeroDernierChargement++;

                        // Pied du journal : la durée vue par Med, décomposée. /health est interrogé
                        // chaque seconde, d'où une précision d'environ 1 s.
                        if (journal != null)
                            SafeWriteLog(journal,
                                $"# serveur prêt · {DureeDernierChargement.TotalSeconds:0.0} s après la demande · " +
                                $"{(DureeDernierChargement - avantLancement).TotalSeconds:0.0} s après le lancement du process");
                        StartIdleWatcher();
                        return (true, "llama-server démarré et prêt.");
                    }
                    await Task.Delay(1000);
                }

                // Le tuer avant d'abandonner. Sans ça, un serveur trop lent à charger restait vivant
                // sans que personne ne le suive : il finissait son chargement pour rien, gardait son
                // modèle en VRAM et en mémoire engagée, et l'appel suivant en démarrait un SECOND
                // par-dessus.
                ForceKill(proc, 5000);
                if (ReferenceEquals(_process, proc)) _process = null;
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
        /// <summary>
        /// Tue le détenteur du port s'il en reste un, et seulement si c'est NOTRE llama-server.
        ///
        /// Pourquoi ce filet en plus des autres : llama-server ne se met à écouter qu'APRÈS avoir
        /// chargé le modèle (visible dans son journal — « model loaded » puis « listening on »). Un
        /// second serveur démarré par erreur consomme donc plusieurs Go et plusieurs secondes avant
        /// de découvrir que le port est pris. Pire, pendant tout ce temps c'est l'ANCIEN serveur qui
        /// répond à /health : la boucle d'attente le prend pour le nouveau et déclare la bascule
        /// réussie, alors que le modèle qui répond n'est pas celui qu'on croit.
        ///
        /// On part de l'état réel du système (qui tient le port) plutôt que de nos propres
        /// références, précisément pour rattraper les cas où celles-ci sont fausses. Le filtre
        /// <see cref="IsOurServer"/> reste indispensable : le llama-server d'Ollama ne doit jamais
        /// être tué ici.
        /// </summary>
        private static void LibererLePort()
        {
            try
            {
                using var netstat = Process.Start(new ProcessStartInfo
                {
                    FileName               = "netstat",
                    Arguments              = "-ano -p tcp",
                    UseShellExecute        = false,
                    CreateNoWindow         = true,
                    RedirectStandardOutput = true
                });
                if (netstat == null) return;

                var sortie = netstat.StandardOutput.ReadToEnd();
                netstat.WaitForExit(3000);

                foreach (var ligne in sortie.Split('\n'))
                {
                    // Pas de filtre sur l'état de la connexion : son libellé dépend de la langue de
                    // Windows. C'est IsOurServer qui décide, et lui ne se trompe pas de cible.
                    if (!ligne.Contains($":{Port}")) continue;

                    var champs = ligne.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (champs.Length == 0 || !int.TryParse(champs[champs.Length - 1], out var pid)) continue;
                    if (_process != null && pid == _process.Id) continue;

                    try
                    {
                        using var detenteur = Process.GetProcessById(pid);
                        if (!IsOurServer(detenteur)) continue;
                        ForceKill(detenteur, 5000);
                    }
                    catch { /* process déjà parti entre netstat et ici */ }
                }
            }
            catch { /* best-effort : ne doit jamais empêcher le démarrage */ }
        }

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

                // Attendre la mort de la CIBLE, et non celle de taskkill : c'est elle qui tient le
                // modèle. taskkill rend la main dès qu'il a POSÉ la demande d'arrêt, en quelques
                // dizaines de millisecondes ; le serveur, lui, met plusieurs secondes à rendre 10 Go
                // de VRAM et de mémoire engagée. On repartait donc démarrer le modèle suivant pendant
                // que le précédent occupait encore la carte — les deux llama-server visibles côte à
                // côte dans le moniteur de ressources, le nouveau allouant ses 7 Go pendant que
                // l'ancien en tenait encore 10. C'est le seul chemin par lequel ça pouvait arriver :
                // partout ailleurs, l'arrêt précède le démarrage.
                proc.WaitForExit(15000);
            }
            catch { /* déjà arrêté, ou pas les droits — best-effort */ }
        }

        private static readonly object _logSync = new();
        private static void SafeWriteLog(StreamWriter writer, string line)
        {
            lock (_logSync)
            {
                // Un écrit sur un journal déjà refermé lève ObjectDisposedException : ignoré.
                try { writer.WriteLine(line); } catch { /* best-effort */ }
            }
        }

        /// <summary>Garde les <see cref="JournauxConserves"/> journaux de chargement les plus récents.</summary>
        private static void NettoyerAnciensJournaux()
        {
            try
            {
                var anciens = new DirectoryInfo(LogDir).GetFiles("llama-server_*.log")
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Skip(JournauxConserves - 1);   // -1 : place pour celui qu'on ouvre
                foreach (var f in anciens)
                {
                    try { f.Delete(); } catch { /* encore ouvert ou verrouillé : il partira la fois suivante */ }
                }
            }
            catch { /* best-effort */ }
        }

        private static string NomDeFichierSur(string s)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '-');
            return s;
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

        /// <summary>Démarre la surveillance du serveur chargé (une seule instance à la fois). Toutes les
        /// minutes : déchargement après <see cref="IdleUnloadMinutes"/> sans requête, et contrôle que le
        /// serveur répond toujours.</summary>
        private static void StartIdleWatcher()
        {
            _echecsSante = 0;
            _idleTimer?.Dispose();
            _idleTimer = new Timer(_ => _ = SurveillerAsync(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        private static async Task SurveillerAsync()
        {
            if (Interlocked.Exchange(ref _surveillanceEnCours, 1) == 1) return;
            try
            {
                if (!IsRunning || Volatile.Read(ref _requetesEnCours) > 0) return;

                if (IdleUnloadMinutes > 0 && DateTime.Now - _lastActivity > TimeSpan.FromMinutes(IdleUnloadMinutes))
                {
                    Stop();
                    PublierEtat(EtatMoteurLlm.Arrete, IdleUnloadMinutes >= 60
                        ? $"Déchargé après {IdleUnloadMinutes / 60.0:0.#} h sans utilisation."
                        : $"Déchargé après {IdleUnloadMinutes} min sans utilisation.");
                    return;
                }

                // Un process vivant peut ne plus servir : c'est le cas typique au réveil d'une mise en veille,
                // où la carte graphique a été réinitialisée sous lui. Contrôlé seulement au repos — avec un
                // seul créneau (-np 1), un serveur occupé peut légitimement tarder à répondre à /health — et
                // sur deux échecs consécutifs, pour ne pas abattre un serveur sur un simple hoquet.
                if (_etat != EtatMoteurLlm.Pret) return;

                if (await IsHealthyAsync())
                {
                    _echecsSante = 0;
                    return;
                }

                if (++_echecsSante >= 2)
                {
                    Stop();
                    PublierEtat(EtatMoteurLlm.Erreur,
                        "Le serveur llama.cpp ne répondait plus (réveil de mise en veille ?). Arrêté : il redémarrera au prochain appel.");
                }
            }
            catch { /* surveillance best-effort */ }
            finally { Volatile.Write(ref _surveillanceEnCours, 0); }
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
                PublierEtat(EtatMoteurLlm.Arrete, "Aucun modèle chargé.");
            }
        }
    }

    /// <summary>État du moteur llama.cpp, publié par <see cref="LlamaCppServerManager"/>.</summary>
    public enum EtatMoteurLlm
    {
        /// <summary>Aucun modèle chargé ; le prochain appel en chargera un.</summary>
        Arrete,
        /// <summary>Serveur en cours de démarrage.</summary>
        Chargement,
        /// <summary>Modèle chargé en VRAM, au repos.</summary>
        Pret,
        /// <summary>Au moins une requête en cours.</summary>
        Genere,
        /// <summary>Démarrage impossible ou serveur perdu.</summary>
        Erreur
    }
}
