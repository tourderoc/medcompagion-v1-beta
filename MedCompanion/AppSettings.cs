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
        public bool EnableAutoWarmup { get; set; } = true;
        public int WarmupTimeoutSeconds { get; set; } = 10;

        // Dernier modèle utilisé pour la régénération
        public string LastRegenerationModel { get; set; } = "deepseek-r1:8b";

        // Configuration Agent de Pilotage
        public bool IsPilotageAgentActive { get; set; } = true;
        public string PilotageAgentProvider { get; set; } = "Ollama";
        public string PilotageAgentModel { get; set; } = "gpt-oss:20b";  // ✅ Corrigé: gpt-oss (avec 2 's')
        public double PilotageAgentTemperature { get; set; } = 0.3;

        // Modèle Whisper sélectionné ("Medium" | "LargeV3")
        public string WhisperModel { get; set; } = "Medium";

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
