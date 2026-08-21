using System;

namespace MedCompanion.Services.LLM
{
    /// <summary>
    /// Débit de génération de la dernière requête LLM, pour affichage dans l'en-tête.
    ///
    /// Point de collecte unique et statique : les providers rapportent leur mesure ici, l'UI s'y
    /// abonne une fois. Passer par un événement évite de faire remonter la mesure à travers toute la
    /// chaîne d'appels (services de synthèse, courriers, consultation...), qui retournent des tuples
    /// (succès, texte, erreur) et n'ont aucune raison de transporter une métrique d'affichage.
    ///
    /// Les chiffres viennent du serveur quand il les fournit (Ollama : eval_count/eval_duration ;
    /// llama.cpp : usage.completion_tokens), donc ils excluent le temps de traitement du prompt et
    /// correspondent bien au débit de génération — le même que celui du journal llama-server.
    /// </summary>
    public static class LlmThroughputMonitor
    {
        /// <summary>Mesure d'une génération.</summary>
        /// <param name="Model">Modèle actif au moment de la requête.</param>
        /// <param name="Tokens">Tokens générés (hors prompt).</param>
        /// <param name="Seconds">Durée de la génération seule.</param>
        public record Sample(string Model, int Tokens, double Seconds)
        {
            public double TokensPerSecond => Seconds > 0 ? Tokens / Seconds : 0;

            /// <summary>Libellé court pour le badge d'en-tête, ex. « 38,5 t/s ».</summary>
            public string ShortLabel => $"{TokensPerSecond:0.#} t/s";

            public string Tooltip =>
                $"Dernière génération : {Tokens} tokens en {Seconds:0.0} s ({TokensPerSecond:0.#} t/s)\nModèle : {Model}";
        }

        /// <summary>Dernière mesure connue (null tant qu'aucune génération n'a eu lieu).</summary>
        public static Sample? Last { get; private set; }

        /// <summary>Levé après chaque génération. Déclenché depuis un thread de fond : l'abonné UI
        /// doit repasser par le Dispatcher.</summary>
        public static event Action<Sample>? Measured;

        /// <summary>
        /// Enregistre une mesure. Ignore silencieusement les valeurs inexploitables (0 token, durée
        /// nulle) : une requête interrompue ou triviale ne doit pas remplacer une mesure valide.
        /// </summary>
        public static void Report(string model, int tokens, double seconds)
        {
            if (tokens <= 0 || seconds <= 0) return;

            var sample = new Sample(model, tokens, seconds);
            Last = sample;
            try { Measured?.Invoke(sample); }
            catch { /* un abonné défaillant ne doit pas casser la génération */ }
        }
    }
}
