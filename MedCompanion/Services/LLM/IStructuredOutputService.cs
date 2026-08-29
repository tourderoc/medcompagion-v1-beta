using System.Threading;
using System.Threading.Tasks;

namespace MedCompanion.Services.LLM
{
    /// <summary>
    /// Génération contrainte par un schéma JSON.
    ///
    /// Différence de nature avec un prompt qui *demande* du JSON : ici le moteur filtre les tokens
    /// candidats à chaque pas, et n'a donc PAS la possibilité d'émettre autre chose que du JSON
    /// conforme au schéma. Ce n'est pas une consigne, c'est une contrainte de décodage.
    ///
    /// Motivé par un cas réel (29/08/2026) : l'extraction de l'interrogatoire demandait du JSON en
    /// fin de prompt, après 130 lignes d'exemples au format « [bloc] / texte ». Qwen 27B faisait la
    /// part des choses ; Gemma 4 12B imitait le format dominant, ne produisait aucune accolade, et
    /// déroulait les blocs jusqu'à épuiser le plafond de tokens. Contraindre le décodage supprime
    /// cette classe d'erreur pour tous les modèles, quel que soit leur niveau de suivi d'instruction.
    ///
    /// Tous les providers ne savent pas le faire : tester <see cref="SupportsStructuredOutput"/>
    /// et retomber sur <see cref="ILLMService.GenerateTextAsync"/> sinon.
    /// </summary>
    public interface IStructuredOutputService
    {
        /// <summary>
        /// Le provider ET le modèle actuellement servi acceptent une contrainte de schéma.
        /// À réinterroger après un changement de modèle.
        /// </summary>
        bool SupportsStructuredOutput { get; }

        /// <summary>
        /// Génère une réponse dont la validité JSON est garantie par construction.
        /// </summary>
        /// <param name="schemaName">Nom du schéma, repris tel quel dans la requête (traçabilité).</param>
        /// <param name="jsonSchema">Le schéma lui-même, en JSON (JSON Schema draft 7).</param>
        /// <param name="maxTokens">
        /// Budget de génération. Pas de marge de raisonnement à prévoir ici : sous contrainte de
        /// schéma, le modèle ne peut pas produire de bloc de réflexion hors JSON.
        /// </param>
        Task<(bool success, string result, string? error)> GenerateJsonAsync(
            string prompt,
            string schemaName,
            string jsonSchema,
            int maxTokens = 1500,
            CancellationToken cancellationToken = default);
    }
}
