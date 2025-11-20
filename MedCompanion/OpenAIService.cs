using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.Services.LLM;

namespace MedCompanion
{
    public class OpenAIService
    {
        private readonly LLMServiceFactory _llmFactory;
        private readonly AppSettings _settings;
        private readonly PromptConfigService _promptConfig;
        
        // Cache des prompts pour éviter les appels répétés
        private string _cachedSystemPrompt;
        private string _cachedNoteStructurationPrompt;
        private string _cachedChatInteractionPrompt;

        public OpenAIService(LLMServiceFactory llmFactory)
        {
            _llmFactory = llmFactory;
            _settings = new AppSettings();
            _promptConfig = new PromptConfigService();
            
            // Charger les prompts initialement
            LoadPrompts();
            
            // S'abonner à l'événement de rechargement des prompts
            _promptConfig.PromptsReloaded += OnPromptsReloaded;
        }
        
        /// <summary>
        /// Charge les prompts depuis le service de configuration
        /// </summary>
        private void LoadPrompts()
        {
            _cachedSystemPrompt = _promptConfig.GetActivePrompt("system_global");
            _cachedNoteStructurationPrompt = _promptConfig.GetActivePrompt("note_structuration");
            _cachedChatInteractionPrompt = _promptConfig.GetActivePrompt("chat_interaction");
            
            System.Diagnostics.Debug.WriteLine("[OpenAIService] Prompts chargés depuis la configuration");
        }
        
        /// <summary>
        /// Gestionnaire d'événement pour le rechargement des prompts
        /// </summary>
        private void OnPromptsReloaded(object? sender, EventArgs e)
        {
            LoadPrompts();
            System.Diagnostics.Debug.WriteLine("[OpenAIService] ✅ Prompts rechargés automatiquement suite à une modification");
        }
        
        /// <summary>
        /// Récupère le provider LLM actuellement actif (permet le changement dynamique)
        /// </summary>
        private ILLMService GetCurrentLLM()
        {
            return _llmFactory.GetCurrentProvider();
        }

        private string BuildSystemPrompt()
        {
            var medecin = _settings.Medecin;
            var ville = _settings.Ville;
            
            // Utiliser le prompt en cache (rechargé automatiquement via événement)
            var prompt = _cachedSystemPrompt
                .Replace("{{Medecin}}", medecin)
                .Replace("{{Ville}}", ville);

            return prompt;
        }

        public bool IsApiKeyConfigured()
        {
            // Déléguer la vérification au LLM service actuel
            return GetCurrentLLM().IsConfigured();
        }

        public async Task<(bool success, string result, double relevanceWeight)> StructurerNoteAsync(string nomComplet, string noteBrute)
        {
            if (!IsApiKeyConfigured())
            {
                return (false, "LLM non configuré. Vérifiez la configuration dans la barre LLM.", 0.0);
            }

            if (string.IsNullOrWhiteSpace(nomComplet) || string.IsNullOrWhiteSpace(noteBrute))
            {
                return (false, "Le nom complet et la note brute sont requis.", 0.0);
            }

            try
            {
                // Vérifier si une date est déjà présente dans la note brute
                var hasDate = System.Text.RegularExpressions.Regex.IsMatch(
                    noteBrute,
                    @"(date|entretien|consultation|rendez-vous|rdv).*\d{1,2}[\/\-\.]\d{1,2}[\/\-\.]\d{2,4}",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );

                var dateInstruction = hasDate
                    ? ""
                    : $"\n\nIMPORTANT: Aucune date de consultation n'est mentionnée dans la note brute. Utilise automatiquement la date d'aujourd'hui ({DateTime.Now:dd/MM/yyyy}) comme date de l'entretien dans le compte-rendu.";

                // Utiliser le prompt en cache (rechargé automatiquement via événement)
                var basePrompt = _cachedNoteStructurationPrompt
                    .Replace("{{Nom_Complet}}", nomComplet)
                    .Replace("{{Date_Instruction}}", dateInstruction)
                    .Replace("{{Note_Brute}}", noteBrute);

                // NOUVEAU : Ajouter évaluation du poids de pertinence
                var userPrompt = basePrompt + @"

---
ÉVALUATION IMPORTANCE (pour mise à jour synthèse patient) :

Après avoir structuré la note, évalue son importance pour mettre à jour la synthèse patient (0.0 à 1.0) :

**Poids 1.0 (critique)** : Nouveau diagnostic posé, changement majeur de traitement (initiation/arrêt), événement grave (hospitalisation, crise, TS), note fondatrice (première consultation)
**Poids 0.7-0.9 (très important)** : Évolution significative des symptômes, ajustement posologique important, nouveau trouble associé
**Poids 0.4-0.6 (modéré)** : Note de suivi standard avec informations cliniques pertinentes
**Poids 0.1-0.3 (mineur)** : Suivi de routine sans nouvelle information majeure

À la fin de ta réponse, après le markdown de la note structurée, ajoute une ligne :
POIDS_SYNTHESE: X.X
";

                // Utiliser le LLM service unifié (provider actuel)
                var systemPrompt = BuildSystemPrompt();
                var messages = new List<(string role, string content)>
                {
                    ("user", userPrompt)
                };
                var (success, result, error) = await GetCurrentLLM().ChatAsync(systemPrompt, messages);

                if (!success)
                {
                    return (false, error ?? "Erreur lors de la structuration.", 0.0);
                }

                // Extraire le poids de la réponse
                double weight = ExtractWeightFromResponse(result ?? "");

                // Retirer la ligne POIDS_SYNTHESE du markdown
                string cleanedMarkdown = RemoveWeightLine(result ?? "");

                return (true, cleanedMarkdown, weight);
            }
            catch (Exception ex)
            {
                return (false, $"Erreur inattendue: {ex.Message}", 0.0);
            }
        }

        /// <summary>
        /// Extrait le poids de pertinence depuis la réponse de l'IA
        /// </summary>
        private double ExtractWeightFromResponse(string response)
        {
            try
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    response,
                    @"POIDS_SYNTHESE:\s*(\d+\.?\d*)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );

                if (match.Success && double.TryParse(match.Groups[1].Value.Replace(',', '.'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double weight))
                {
                    return Math.Clamp(weight, 0.0, 1.0); // Sécurité: entre 0 et 1
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OpenAI] Erreur extraction poids: {ex.Message}");
            }

            return 0.5; // Valeur par défaut si parsing échoue
        }

        /// <summary>
        /// Retire la ligne POIDS_SYNTHESE du markdown
        /// </summary>
        private string RemoveWeightLine(string markdown)
        {
            try
            {
                return System.Text.RegularExpressions.Regex.Replace(
                    markdown,
                    @"POIDS_SYNTHESE:\s*\d+\.?\d*\s*",
                    "",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                ).Trim();
            }
            catch
            {
                return markdown;
            }
        }

        public async Task<(bool success, string result)> ChatAvecContexteAsync(
            string contexte, 
            string question, 
            List<ChatExchange>? historique = null,
            string? customSystemPrompt = null)
        {
            if (!IsApiKeyConfigured())
            {
                return (false, "LLM non configuré. Vérifiez la configuration dans la barre LLM.");
            }

            if (string.IsNullOrWhiteSpace(question))
            {
                return (false, "La question est requise.");
            }

            try
            {
                // Construire le prompt système
                string systemPrompt;
                if (customSystemPrompt != null)
                {
                    // Un prompt personnalisé est fourni (ex: attestations, lettres, etc.)
                    systemPrompt = customSystemPrompt;
                }
                else
                {
                    // Mode chat interactif : combiner system_global + chat_interaction
                    var baseSystemPrompt = BuildSystemPrompt();
                    
                    // Ajouter les instructions spécifiques au chat
                    if (!string.IsNullOrEmpty(_cachedChatInteractionPrompt))
                    {
                        systemPrompt = baseSystemPrompt + "\n\n" + _cachedChatInteractionPrompt;
                    }
                    else
                    {
                        systemPrompt = baseSystemPrompt;
                    }
                }

                // Construire le userPrompt avec contexte et historique
                var userPromptBuilder = new StringBuilder();

                // Ajouter l'historique des 3 derniers échanges (max)
                if (historique != null && historique.Count > 0)
                {
                    var recentHistory = historique.TakeLast(3);
                    userPromptBuilder.AppendLine("HISTORIQUE RÉCENT");
                    userPromptBuilder.AppendLine("-------------------");
                    foreach (var exchange in recentHistory)
                    {
                        userPromptBuilder.AppendLine($"Q: {exchange.Question}");
                        userPromptBuilder.AppendLine($"R: {exchange.Response}");
                        userPromptBuilder.AppendLine();
                    }
                }

                // Ajouter le contexte
                if (!string.IsNullOrWhiteSpace(contexte))
                {
                    userPromptBuilder.AppendLine("CONTEXTE (extraits)");
                    userPromptBuilder.AppendLine("-------------------");
                    userPromptBuilder.AppendLine(contexte);
                    userPromptBuilder.AppendLine();
                }

                // Ajouter la question actuelle
                userPromptBuilder.AppendLine("QUESTION");
                userPromptBuilder.AppendLine("-------------------");
                userPromptBuilder.AppendLine(question);

                var userPrompt = userPromptBuilder.ToString();

                // Utiliser le LLM service unifié (provider actuel)
                var messages = new List<(string role, string content)>
                {
                    ("user", userPrompt)
                };
                var (success, result, error) = await GetCurrentLLM().ChatAsync(systemPrompt, messages);

                return (success, result ?? error ?? "Aucun contenu retourné.");
            }
            catch (Exception ex)
            {
                return (false, $"Erreur inattendue: {ex.Message}");
            }
        }

        /// <summary>
        /// Génère du texte à partir d'un prompt personnalisé (pour synthèses, etc.)
        /// </summary>
        public async Task<(bool success, string result, string? error)> GenerateTextAsync(string prompt, int maxTokens = 3000)
        {
            if (!IsApiKeyConfigured())
            {
                return (false, "", "LLM non configuré. Vérifiez la configuration dans la barre LLM.");
            }

            if (string.IsNullOrWhiteSpace(prompt))
            {
                return (false, "", "Le prompt est requis.");
            }

            try
            {
                var systemPrompt = BuildSystemPrompt();
                
                // Utiliser le LLM service unifié (provider actuel)
                var messages = new List<(string role, string content)>
                {
                    ("user", prompt)
                };
                var (success, result, error) = await GetCurrentLLM().ChatAsync(systemPrompt, messages);

                if (success)
                {
                    return (true, result ?? "", null);
                }
                else
                {
                    return (false, "", error ?? result ?? "Erreur inconnue");
                }
            }
            catch (Exception ex)
            {
                return (false, "", $"Erreur inattendue: {ex.Message}");
            }
        }
    }
}
