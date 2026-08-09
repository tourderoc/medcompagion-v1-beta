using System;

namespace MedCompanion.Services.LLM
{
    /// <summary>
    /// Renseignements sur un nom de modèle Ollama, indépendamment de toute UI.
    /// </summary>
    public static class OllamaModelInfo
    {
        /// <summary>
        /// Vrai si le modèle est un modèle « Ollama Cloud » (suffixe -cloud) : aucun poids sur le
        /// disque, l'inférence est exécutée sur les serveurs Ollama. À ne jamais utiliser sur des
        /// données patient — c'est la frontière du secret médical, pas une préférence de confort.
        /// </summary>
        public static bool IsCloudModel(string? modelName) =>
            !string.IsNullOrWhiteSpace(modelName) &&
            modelName.EndsWith("-cloud", StringComparison.OrdinalIgnoreCase);
    }
}
