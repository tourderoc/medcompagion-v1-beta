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
        public bool EnableAutoWarmup { get; set; } = true;
        public int WarmupTimeoutSeconds { get; set; } = 10;
    }
}
