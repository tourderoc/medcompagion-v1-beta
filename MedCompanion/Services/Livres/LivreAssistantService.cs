using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MedCompanion.Models.Livres;
using MedCompanion.Services.LLM;

namespace MedCompanion.Services.Livres
{
    /// <summary>
    /// Med comme assistant d'écriture littéraire : co-écriture (continuer la
    /// scène), reformulation d'un passage, questions libres — le tout ancré
    /// dans la « mémoire du livre » (personnages, intrigue, ton) stockée dans
    /// memoire.md et régénérable à la demande depuis les chapitres.
    /// </summary>
    public class LivreAssistantService
    {
        private readonly LLMServiceFactory _llmFactory;

        public LivreAssistantService(LLMServiceFactory llmFactory)
        {
            _llmFactory = llmFactory;
        }

        // ── Chat général / co-écriture ──────────────────────────────────────

        /// <summary>
        /// Envoie un message libre à Med avec le contexte du livre.
        /// L'historique de la session est passé pour la continuité de l'échange.
        /// </summary>
        public async Task<(bool success, string result, string? error)> ChatAsync(
            Livre livre,
            string memoire,
            string chapitreTitre,
            string chapitreContenu,
            List<(string role, string content)> historique,
            string message)
        {
            var llm = _llmFactory.GetCurrentProvider();
            if (llm == null || !llm.IsConfigured())
                return (false, "", "Aucun LLM configuré.");

            var systemPrompt = BuildSystemPrompt(livre, memoire, chapitreTitre, chapitreContenu);

            var messages = new List<(string role, string content)>(historique)
            {
                ("user", message)
            };

            return await llm.ChatAsync(systemPrompt, messages, maxTokens: 2500);
        }

        /// <summary>
        /// Demande à Med de continuer le chapitre en cours, dans le style et la
        /// continuité du texte. Consigne optionnelle (ex : « Fred ouvre enfin la porte »).
        /// </summary>
        public Task<(bool success, string result, string? error)> ContinuerAsync(
            Livre livre, string memoire, string chapitreTitre, string chapitreContenu,
            string? consigne = null)
        {
            var demande = new StringBuilder();
            demande.AppendLine("Continue le chapitre là où il s'arrête.");
            demande.AppendLine("Écris la suite directement, sans préambule ni commentaire, dans le même style, le même ton et la même voix narrative que le texte existant.");
            demande.AppendLine("Longueur : quelques paragraphes (une demi-page environ), pour laisser la main à l'auteur.");
            if (!string.IsNullOrWhiteSpace(consigne))
                demande.AppendLine($"Indication de l'auteur pour la suite : {consigne}");

            return ChatAsync(livre, memoire, chapitreTitre, chapitreContenu,
                new List<(string, string)>(), demande.ToString());
        }

        /// <summary>
        /// Reformule un passage sélectionné. Consigne optionnelle (ex : « plus sobre », « plus de tension »).
        /// Retourne uniquement le passage réécrit.
        /// </summary>
        public Task<(bool success, string result, string? error)> ReformulerAsync(
            Livre livre, string memoire, string chapitreTitre, string chapitreContenu,
            string passage, string? consigne = null)
        {
            var demande = new StringBuilder();
            demande.AppendLine("Reformule le passage suivant du chapitre en cours.");
            demande.AppendLine("Réponds UNIQUEMENT avec le passage réécrit, sans introduction, sans guillemets d'encadrement, sans commentaire.");
            demande.AppendLine("Conserve le sens, améliore le style, reste dans la voix du livre.");
            if (!string.IsNullOrWhiteSpace(consigne))
                demande.AppendLine($"Consigne de l'auteur : {consigne}");
            demande.AppendLine();
            demande.AppendLine("PASSAGE À REFORMULER :");
            demande.AppendLine(passage);

            return ChatAsync(livre, memoire, chapitreTitre, chapitreContenu,
                new List<(string, string)>(), demande.ToString());
        }

        // ── Mémoire du livre ────────────────────────────────────────────────

        /// <summary>
        /// (Re)génère la mémoire narrative du livre à partir de tous les chapitres :
        /// personnages, intrigue, lieux, tonalité, fils narratifs ouverts.
        /// </summary>
        public async Task<(bool success, string memoire, string? error)> GenererMemoireAsync(
            Livre livre, List<(ChapitreLivre chapitre, string contenu)> chapitres)
        {
            var llm = _llmFactory.GetCurrentProvider();
            if (llm == null || !llm.IsConfigured())
                return (false, "", "Aucun LLM configuré.");

            var sb = new StringBuilder();
            sb.AppendLine($"Voici le texte complet du livre « {livre.Titre} ». Établis sa mémoire narrative en Markdown avec ces sections :");
            sb.AppendLine("## Personnages (nom, rôle, traits, évolution)");
            sb.AppendLine("## Intrigue (résumé chapitre par chapitre, 2-3 phrases chacun)");
            sb.AppendLine("## Lieux et univers");
            sb.AppendLine("## Ton et style (voix narrative, temps, registre)");
            sb.AppendLine("## Fils ouverts (questions non résolues, tensions à venir)");
            sb.AppendLine("Sois factuel et concis : cette mémoire servira de contexte pour t'aider à co-écrire la suite.");
            sb.AppendLine();

            foreach (var (chapitre, contenu) in chapitres.Where(c => !string.IsNullOrWhiteSpace(c.contenu)))
            {
                sb.AppendLine($"=== CHAPITRE {chapitre.Ordre} : {chapitre.Titre} ===");
                sb.AppendLine(contenu);
                sb.AppendLine();
            }

            var (success, result, error) = await llm.GenerateTextAsync(sb.ToString(), maxTokens: 3000);
            return (success, result, error);
        }

        // ── System prompt ───────────────────────────────────────────────────

        private static string BuildSystemPrompt(Livre livre, string memoire, string chapitreTitre, string chapitreContenu)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Tu es Med, assistant d'écriture littéraire personnel de l'auteur.");
            sb.AppendLine($"Vous travaillez ensemble sur son livre « {livre.Titre} »" +
                          (string.IsNullOrWhiteSpace(livre.Auteur) ? "." : $" de {livre.Auteur}."));
            sb.AppendLine("Tu écris en français, dans un style littéraire soigné, et tu respectes scrupuleusement la voix, le ton et l'univers du livre.");
            sb.AppendLine("Tu es un partenaire d'écriture : tu proposes, l'auteur dispose. Jamais de leçons, pas de commentaires méta inutiles.");
            sb.AppendLine("Typographie française : tirets cadratins (—) pour les dialogues, espaces insécables implicites, pas de guillemets anglais.");

            if (!string.IsNullOrWhiteSpace(memoire))
            {
                sb.AppendLine();
                sb.AppendLine("=== MÉMOIRE DU LIVRE (personnages, intrigue, ton) ===");
                sb.AppendLine(memoire);
            }

            if (!string.IsNullOrWhiteSpace(chapitreContenu))
            {
                sb.AppendLine();
                sb.AppendLine($"=== CHAPITRE EN COURS : {chapitreTitre} ===");
                sb.AppendLine(chapitreContenu);
            }

            return sb.ToString();
        }
    }
}
