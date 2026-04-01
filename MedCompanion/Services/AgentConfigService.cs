using System.IO;
using System.Text.Json;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service pour gérer la configuration des agents IA (Med, futurs agents)
    /// Charge/Sauvegarde depuis agents_config.json dans le dossier MedCompanion
    /// </summary>
    public class AgentConfigService
    {
        private readonly string _configPath;
        private AgentsConfiguration _config;

        public AgentConfigService()
        {
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var basePath = Path.Combine(documentsPath, "MedCompanion");
            _configPath = Path.Combine(basePath, "agents_config.json");
            _config = LoadOrCreateDefault();
        }

        /// <summary>
        /// Configuration actuelle des agents
        /// </summary>
        public AgentsConfiguration Configuration => _config;

        /// <summary>
        /// Récupère la configuration de Med
        /// </summary>
        public AgentConfig? GetMedConfig()
        {
            return _config.GetMed();
        }

        /// <summary>
        /// Récupère la configuration du Sub-Agent Web
        /// </summary>
        public AgentConfig? GetWebConfig()
        {
            return _config.GetWeb();
        }

        /// <summary>
        /// Récupère la configuration de l'Agent de Pilotage
        /// </summary>
        public AgentConfig? GetPilotageConfig()
        {
            return _config.GetPilotage();
        }

        /// <summary>
        /// Récupère un agent par son ID
        /// </summary>
        public AgentConfig? GetAgentConfig(string agentId)
        {
            return _config.GetAgent(agentId);
        }

        /// <summary>
        /// Met à jour la configuration d'un agent
        /// </summary>
        public void UpdateAgentConfig(AgentConfig config)
        {
            var existing = _config.Agents.FirstOrDefault(a => a.AgentId == config.AgentId);
            if (existing != null)
            {
                var index = _config.Agents.IndexOf(existing);
                config.UpdatedAt = DateTime.Now;
                _config.Agents[index] = config;
            }
            else
            {
                config.CreatedAt = DateTime.Now;
                config.UpdatedAt = DateTime.Now;
                _config.Agents.Add(config);
            }
        }

        /// <summary>
        /// Sauvegarde la configuration dans le fichier JSON
        /// </summary>
        public void Save()
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                var json = JsonSerializer.Serialize(_config, options);
                File.WriteAllText(_configPath, json, System.Text.Encoding.UTF8);
                System.Diagnostics.Debug.WriteLine($"[AgentConfigService] Configuration sauvegardée dans {_configPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AgentConfigService] Erreur sauvegarde : {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Recharge la configuration depuis le fichier
        /// </summary>
        public void Reload()
        {
            _config = LoadOrCreateDefault();
        }

        /// <summary>
        /// Charge la configuration ou crée celle par défaut
        /// </summary>
        private AgentsConfiguration LoadOrCreateDefault()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath, System.Text.Encoding.UTF8);
                    var config = JsonSerializer.Deserialize<AgentsConfiguration>(json);
                    if (config != null && config.Agents.Count > 0)
                    {
                        // S'assurer que l'agent Web existe (migration anciennes configs)
                        if (config.GetWeb() == null)
                        {
                            config.Agents.Add(AgentConfig.CreateDefaultWeb());
                            System.Diagnostics.Debug.WriteLine("[AgentConfigService] Agent Web ajouté (migration)");
                        }

                        // S'assurer que l'agent Pilotage existe
                        if (config.GetPilotage() == null)
                        {
                            config.Agents.Add(AgentConfig.CreateDefaultPilotage());
                            System.Diagnostics.Debug.WriteLine("[AgentConfigService] Agent Pilotage ajouté (migration)");
                        }

                        System.Diagnostics.Debug.WriteLine($"[AgentConfigService] Configuration chargée : {config.Agents.Count} agent(s)");
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AgentConfigService] Erreur chargement : {ex.Message}");
            }

            // Créer la configuration par défaut avec Med
            System.Diagnostics.Debug.WriteLine("[AgentConfigService] Création de la configuration par défaut");
            var defaultConfig = AgentsConfiguration.CreateDefault();

            // Sauvegarder immédiatement
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                var json = JsonSerializer.Serialize(defaultConfig, options);

                // S'assurer que le dossier existe
                var directory = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(_configPath, json, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AgentConfigService] Erreur création fichier par défaut : {ex.Message}");
            }

            return defaultConfig;
        }

        /// <summary>
        /// Liste des providers LLM disponibles
        /// </summary>
        public static List<string> GetAvailableLLMProviders()
        {
            return new List<string> { "OpenAI", "Ollama" };
        }

        /// <summary>
        /// Liste des modèles par défaut pour chaque provider
        /// </summary>
        public static List<string> GetDefaultModelsForProvider(string provider)
        {
            return provider switch
            {
                "OpenAI" => new List<string> { "gpt-4o-mini", "gpt-4o", "gpt-4-turbo", "gpt-3.5-turbo" },
                "Ollama" => new List<string> { "llama3", "mistral", "mixtral", "phi3", "qwen2" },
                _ => new List<string>()
            };
        }
    }
}
