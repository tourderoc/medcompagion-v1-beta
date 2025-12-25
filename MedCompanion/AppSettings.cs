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
        public bool EnableAutoWarmup { get; set; } = true;
        public int WarmupTimeoutSeconds { get; set; } = 10;

        // Dernier modèle utilisé pour la régénération
        public string LastRegenerationModel { get; set; } = "deepseek-r1:8b";

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
