namespace MedCompanion.Models
{
    /// <summary>
    /// Modèle pour les paramètres sécurisés (clés API)
    /// </summary>
    public class SecureSettings
    {
        public string? OpenAIApiKey { get; set; }
        public string? OpenRouterApiKey { get; set; }
    }
}
