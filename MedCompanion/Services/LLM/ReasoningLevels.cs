namespace MedCompanion.Services.LLM
{
    /// <summary>
    /// Valeurs du sélecteur de niveau de réflexion, partagées entre l'UI et les providers.
    ///
    /// <see cref="Off"/> n'est pas un niveau au sens des moteurs : llama.cpp gradue de 'minimal' à
    /// 'max' et coupe la réflexion par un drapeau de démarrage distinct (--reasoning off), tandis
    /// qu'Ollama attend un booléen sur son champ "think". Chaque provider traduit donc cette valeur
    /// à sa manière — d'où une constante commune plutôt qu'une chaîne littérale recopiée.
    ///
    /// Le vocabulaire varie aussi d'un modèle à l'autre, et un niveau inconnu fait échouer le
    /// template Jinja côté serveur (erreur 500, pas de repli silencieux) :
    ///   • gpt-oss (Ollama)      : low | medium | high
    ///   • Qwen3.8-27B (llama.cpp) : low | medium | xhigh   ← ni "high" ni "minimal"
    /// Ces valeurs sont donc les niveaux LOGIQUES de l'interface ; chaque provider les traduit vers
    /// ce que son modèle accepte (voir LlamaCppProvider.ToTemplateEffort).
    /// </summary>
    public static class ReasoningLevels
    {
        public const string Off    = "off";
        public const string Low    = "low";
        public const string Medium = "medium";
        public const string High   = "high";
    }
}
