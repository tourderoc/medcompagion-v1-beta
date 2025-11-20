using System;
using System.Threading.Tasks;

namespace MedCompanion.Services.LLM
{
    /// <summary>
    /// Service de warm-up automatique des modèles LLM au démarrage
    /// </summary>
    public class LLMWarmupService
    {
        private readonly LLMServiceFactory _factory;
        private readonly AppSettings _settings;

        public LLMWarmupService(LLMServiceFactory factory, AppSettings settings)
        {
            _factory = factory;
            _settings = settings;
        }

        /// <summary>
        /// Event déclenché lors des changements de statut
        /// </summary>
        public event EventHandler<WarmupStatusEventArgs>? StatusChanged;

        /// <summary>
        /// Effectue le warm-up automatique du provider LLM
        /// </summary>
        public async Task<(bool success, string message)> WarmupAsync()
        {
            try
            {
                // Phase 1 : Initialisation
                OnStatusChanged("initializing", "⏳ Initialisation du provider LLM...");

                var provider = await _factory.InitializeAsync();

                // Phase 2 : Vérification de la connexion
                OnStatusChanged("checking", "🔍 Vérification de la connexion...");

                var (isConnected, connectionMessage) = await provider.CheckConnectionAsync();

                if (!isConnected)
                {
                    // Si échec et que c'est Ollama, essayer de basculer vers OpenAI
                    if (_settings.LLMProvider == "Ollama")
                    {
                        OnStatusChanged("fallback", "⚠️ Ollama indisponible, basculement vers OpenAI...");

                        var (switchSuccess, switchMessage) = await _factory.SwitchProviderAsync("OpenAI");

                        if (!switchSuccess)
                        {
                            OnStatusChanged("error", $"❌ Erreur: {switchMessage}");
                            return (false, $"Échec du fallback: {switchMessage}");
                        }

                        provider = _factory.GetCurrentProvider();
                    }
                    else
                    {
                        OnStatusChanged("error", $"❌ {connectionMessage}");
                        return (false, connectionMessage);
                    }
                }

                // Phase 3 : Warm-up du modèle
                if (_settings.EnableAutoWarmup)
                {
                    OnStatusChanged("warming", "🔥 Warm-up du modèle en cours...");

                    var (warmupSuccess, warmupMessage) = await provider.WarmupAsync();

                    if (!warmupSuccess)
                    {
                        OnStatusChanged("warning", $"⚠️ Warm-up échoué: {warmupMessage}");
                        return (false, warmupMessage);
                    }
                }

                // Phase 4 : Succès
                var providerName = provider.GetProviderName();
                var modelName = provider.GetModelName();
                var successMessage = providerName == "Ollama" 
                    ? $"🟢 {providerName} prêt - {modelName}"
                    : $"🟢 {providerName} prêt - {modelName}";

                OnStatusChanged("ready", successMessage);

                return (true, successMessage);
            }
            catch (Exception ex)
            {
                var errorMessage = $"❌ Erreur inattendue: {ex.Message}";
                OnStatusChanged("error", errorMessage);
                return (false, errorMessage);
            }
        }

        /// <summary>
        /// Déclenche l'événement de changement de statut
        /// </summary>
        private void OnStatusChanged(string status, string message)
        {
            StatusChanged?.Invoke(this, new WarmupStatusEventArgs
            {
                Status = status,
                Message = message,
                Timestamp = DateTime.Now
            });
        }
    }

    /// <summary>
    /// Arguments d'événement pour les changements de statut du warm-up
    /// </summary>
    public class WarmupStatusEventArgs : EventArgs
    {
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
