namespace MedCompanion
{
    public class AppSettings
    {
        // Informations du médecin
        public string Medecin { get; set; } = "Dr Lassoued Nair";
        public string Specialite { get; set; } = "Pédopsychiatre (conventionné secteur 1)";
        public string Rpps { get; set; } = "10100386167";
        public string Finess { get; set; } = "831018791";
        public string Telephone { get; set; } = "0752758732";
        public string Email { get; set; } = "pedopsy.lassoued@gmail.com";
        
        // Adresse du cabinet
        public string Adresse { get; set; } = "390 1er DFL Le Pradet 83220";
        public string Ville { get; set; } = "Le Pradet";
        
        // Signature numérique
        public bool EnableDigitalSignature { get; set; } = true;
        public string SignatureImagePath { get; set; } = "Assets/signature.png";
        
        // Configuration LLM
        public string LLMProvider { get; set; } = "OpenAI"; // "OpenAI" ou "Ollama"
        public string OllamaBaseUrl { get; set; } = "http://localhost:11434";
        public string OllamaModel { get; set; } = "llama3.2:latest";
        public string OpenAIModel { get; set; } = "gpt-4o-mini";
        
        // Modèle dédié pour l'anonymisation (Local uniquement par sécurité)
        public string AnonymizationModel { get; set; } = "llama3.2:latest";

        // Modèle dédié à l'OCR / vision (GlmOcrService).
        // Épinglé pour que l'OCR ne dépende jamais du modèle de conversation sélectionné.
        // Local obligatoire : le service refuse de démarrer sur un modèle -cloud.
        public string OcrModel { get; set; } = "glm-ocr:latest";

        // Dernier modèle utilisé pour la régénération
        public string LastRegenerationModel { get; set; } = "deepseek-r1:8b";

        // Configuration Agent de Pilotage
        public bool IsPilotageAgentActive { get; set; } = true;
        public string PilotageAgentProvider { get; set; } = "Ollama";
        public string PilotageAgentModel { get; set; } = "gpt-oss:20b";  // ✅ Corrigé: gpt-oss (avec 2 's')
        public double PilotageAgentTemperature { get; set; } = 0.3;

        // Modèle Whisper sélectionné ("Medium" | "LargeV3")
        public string WhisperModel { get; set; } = "Medium";

        /// <summary>Dossier des modèles Whisper (ggml-*.bin). Vide = dossier de l'application dans
        /// AppData, où Med télécharge ses modèles — utile sur un poste neuf. Renseigné, c'est la seule
        /// source : aucun autre dossier n'est consulté. Lu au démarrage de l'application.</summary>
        public string WhisperModelsDir { get; set; } = "";

        /// <summary>
        /// Index CUDA de la carte sur laquelle charger Whisper. -1 = laisser ggml choisir
        /// (comportement d'origine, la première carte).
        ///
        /// Sur un poste à deux cartes, réserver la petite à Whisper laisse toute la VRAM de la
        /// grande au modèle de langage : les deux cohabitent alors sans se disputer la mémoire,
        /// et une dictée ne fait plus décharger le LLM.
        ///
        /// Attention, il s'agit bien d'un index CUDA et non de celui affiché par nvidia-smi :
        /// CUDA classe les cartes de la plus rapide à la plus lente, nvidia-smi les classe par
        /// bus PCI. Les deux listes peuvent donc être inversées.
        /// </summary>
        public int WhisperGpuDevice { get; set; } = -1;

        /// <summary>
        /// Carte réservée à Whisper, désignée par son UUID (nvidia-smi --query-gpu=uuid --format=csv).
        /// Renseigné, il PRIME sur <see cref="WhisperGpuDevice"/>, qui n'est plus lu.
        ///
        /// Pourquoi l'UUID : un numéro de carte dépend de l'ordre d'énumération, et sur le poste du
        /// cabinet l'ordre CUDA (la plus rapide d'abord) est l'inverse de celui de nvidia-smi. Un
        /// numéro juste aujourd'hui peut envoyer Whisper sur la carte du LLM après un changement de
        /// pilote ou de slot, sans aucun message. Avec l'UUID, Med aligne l'ordre CUDA de son propre
        /// processus sur le bus PCI et retrouve le bon numéro à chaque chargement.
        /// </summary>
        public string WhisperGpuUuid { get; set; } = "";

        // Micro sélectionné pour la dictée (nom du périphérique, ex: "Amazon Basics USB Microphone").
        // Stocké par nom (pas par index) car l'index Windows n'est pas stable entre deux sessions
        // (branchement/débranchement USB) — vide = périphérique par défaut du système.
        public string MicrophoneDeviceName { get; set; } = "";

        // Niveau de réflexion ("low" | "medium" | "high") pour les modèles Ollama qui exposent un
        // "reasoning_effort" graduable (gpt-oss, hybrides Qwen3 calqués sur ce format). Vide = défaut.
        public string OllamaReasoningEffort { get; set; } = "";

        /// <summary>Réglages par profil llama.cpp, format compact "id=contexte,mtp,kv;..." —
        /// voir LlamaCppProfiles.LoadSettings. Vide = valeurs par défaut de chaque profil.</summary>
        public string LlamaCppProfileSettings { get; set; } = "";

        /// <summary>Interrupteur général du moteur local llama.cpp. À false, tous les modèles
        /// repassent par Ollama — filet de sécurité, l'app reste utilisable sans llama.cpp.</summary>
        public bool LlamaCppEnabled { get; set; } = true;

        /// <summary>Id du profil llama.cpp utilisé pour les requêtes image, indépendant du modèle
        /// de texte sélectionné. Vide = premier profil capable de vision.</summary>
        public string LlamaCppVisionProfile { get; set; } = "";

        /// <summary>Chemin complet du llama-server.exe. Vide = emplacement historique
        /// (C:\Users\nair\llama.cpp\build\bin\Release). À renseigner sur une machine où le moteur
        /// est ailleurs, plutôt que de recompiler. Lu au démarrage de l'application.</summary>
        public string LlamaCppExePath { get; set; } = "";

        /// <summary>Dossier contenant les modèles GGUF. Tous les chemins de modèles des profils en
        /// découlent. Vide = aucun modèle chargeable, et Med le dit : il n'y a plus d'emplacement de
        /// repli. Lu au démarrage de l'application.</summary>
        public string LlamaCppModelsDir { get; set; } = "";

        /// <summary>Minutes sans requête avant de décharger le modèle llama.cpp. 0 = jamais tant que Med
        /// est ouvert. 120 par défaut : couvre une consultation et une pause déjeuner, la 5060 Ti étant
        /// réservée au LLM. Lu au démarrage de l'application.</summary>
        public int LlamaCppIdleUnloadMinutes { get; set; } = 120;

        /// <summary>Pré-lecture de l'autre modèle llama.cpp dans le cache Windows, pour que le switch Qwen ↔
        /// Gemma parte d'un fichier déjà en mémoire (~4-5 s) plutôt que du disque (~70 s). Voir
        /// LlamaCppPrelecture. Lu au démarrage de l'application.</summary>
        public bool LlamaCppPrelectureActive { get; set; } = true;

        /// <summary>Mode de lecture du modèle au démarrage de llama-server : « no-mmap » (historique : le
        /// fichier est lu dans une copie en mémoire puis envoyé à la carte) ou « mmap » (le fichier est
        /// projeté en mémoire, sans copie quand il est déjà dans le cache Windows). En essai le 15/09/2026 :
        /// comparer la phase de lecture des poids dans logs\llama-server_*.log. Relu à chaque démarrage
        /// du serveur.</summary>
        public string LlamaCppLoadMode { get; set; } = "no-mmap";

        /// <summary>
        /// Carte graphique réservée à llama-server, désignée par son UUID CUDA
        /// (nvidia-smi --query-gpu=uuid --format=csv). Vide = comportement d'origine, llama.cpp
        /// utilise toutes les cartes visibles.
        ///
        /// Sur un poste à deux cartes inégales, laisser llama.cpp répartir un modèle sur les deux
        /// fait tomber le débit au niveau de la plus lente : mesuré 38 tok/s réparti contre
        /// 98 tok/s sur la seule RTX 5060 Ti (11/09/2026). La petite carte reste alors libre pour
        /// l'affichage et Whisper.
        ///
        /// L'UUID plutôt que l'index : il ne change pas si une carte change de slot ou si l'ordre
        /// d'énumération CUDA change. Posé uniquement sur le processus llama-server, jamais au
        /// niveau de la session, qui masquerait l'autre carte à Whisper.
        /// </summary>
        public string LlamaCppGpuUuid { get; set; } = "";

        // Configuration Handy (transcription vocale)
        public string HandyHotkey { get; set; } = "Ctrl+Space";
        public bool HandyEnabled { get; set; } = true;

        // Configuration VPS Monitoring (Parent'aile)
        public string VpsMonitoringUrl { get; set; } = "http://145.223.117.145:5050";
        public bool VpsMonitoringEnabled { get; set; } = false;

        // Configuration SMTP Pilotage (Gmail)
        public string SmtpHost { get; set; } = "smtp.gmail.com";
        public int SmtpPort { get; set; } = 587;
        public string SmtpUsername { get; set; } = "parentaile.lassoued@gmail.com";
        public string SmtpPassword { get; set; } = "";  // Mot de passe d'application Gmail
        public string SmtpFromEmail { get; set; } = "parentaile.lassoued@gmail.com";
        public string SmtpFromName { get; set; } = "Parent'aile - Cabinet Dr Lassoued";

        private static readonly string SettingsFilePath = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MedCompanion",
            "appsettings.json"
        );

        public static AppSettings Load()
        {
            try
            {
                if (System.IO.File.Exists(SettingsFilePath))
                {
                    var json = System.IO.File.ReadAllText(SettingsFilePath);
                    return System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppSettings] Erreur chargement : {ex.Message}");
            }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                var directory = System.IO.Path.GetDirectoryName(SettingsFilePath);
                if (!string.IsNullOrEmpty(directory) && !System.IO.Directory.Exists(directory))
                {
                    System.IO.Directory.CreateDirectory(directory);
                }

                var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                var json = System.Text.Json.JsonSerializer.Serialize(this, options);
                System.IO.File.WriteAllText(SettingsFilePath, json);
                System.Diagnostics.Debug.WriteLine($"[AppSettings] Sauvegardé : {SettingsFilePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AppSettings] Erreur sauvegarde : {ex.Message}");
                throw;
            }
        }
    }
}
