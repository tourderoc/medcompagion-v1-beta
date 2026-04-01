using System.Text.Json.Serialization;

namespace MedCompanion.Models
{
    /// <summary>
    /// Configuration d'un agent IA (Med, futur agent calendrier, etc.)
    /// Stocke le LLM choisi, la posture (system prompt) et l'état actif
    /// </summary>
    public class AgentConfig
    {
        /// <summary>
        /// Identifiant unique de l'agent (ex: "med", "calendar")
        /// </summary>
        public string AgentId { get; set; } = string.Empty;

        /// <summary>
        /// Nom d'affichage de l'agent
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Description courte de l'agent
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Provider LLM (OpenAI, Ollama, OpenRouter)
        /// </summary>
        public string LLMProvider { get; set; } = "OpenAI";

        /// <summary>
        /// Modèle LLM spécifique (gpt-4o, gpt-4o-mini, llama3, etc.)
        /// </summary>
        public string LLMModel { get; set; } = "gpt-4o-mini";

        /// <summary>
        /// Posture de l'agent (system prompt qui définit sa personnalité et son comportement)
        /// </summary>
        public string Posture { get; set; } = string.Empty;

        /// <summary>
        /// Agent actif ou désactivé
        /// </summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// Température pour les réponses (0.0 = déterministe, 1.0 = créatif)
        /// </summary>
        public double Temperature { get; set; } = 0.7;

        /// <summary>
        /// Date de création
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Date de dernière modification
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Nombre max de résultats de recherche (pour agent Web)
        /// </summary>
        public int MaxSearchResults { get; set; } = 5;

        /// <summary>
        /// Crée la configuration par défaut pour Med (Mode Compagnon - entre consultations)
        /// Note: Med Consultation sera un mode séparé avec un prompt clinique distinct
        /// </summary>
        public static AgentConfig CreateDefaultMed()
        {
            return new AgentConfig
            {
                AgentId = "med",
                DisplayName = "Med",
                Description = "Compagnon IA entre les consultations - collègue, secrétaire cognitif, confident",
                LLMProvider = "OpenAI",
                LLMModel = "gpt-4o-mini",
                IsEnabled = true,
                Temperature = 0.7,
                Posture = @"Tu es Med, le compagnon d'un pédopsychiatre entre ses consultations.

## Qui tu es
Tu n'es PAS un outil clinique ici. Tu es :
- Un collègue de confiance pour échanger sur le travail, les projets, les idées
- Un secrétaire cognitif qui aide à organiser les pensées, reformuler, synthétiser
- Un confident (si sollicité) pour les moments difficiles ou la charge mentale
- Une présence bienveillante qui écoute sans juger

## Ta personnalité
- Tu tutoies (sauf indication contraire)
- Ton direct, chaleureux, sans formules creuses (jamais ""Bien sûr !"", ""Excellente question !"")
- Humour léger bienvenu quand approprié
- Concis par défaut, développé si demandé
- Tu peux initier (proposer, rappeler) avec parcimonie - jamais intrusif

## Ce que tu fais
- Reformuler des idées, aider à structurer la pensée
- Discuter de projets (MedCompanion, Parent'aile, lectures, idées)
- Accompagner les moments de décharge mentale sans analyser
- Proposer des pistes de réflexion, jamais imposer
- Rappeler gentiment des sujets évoqués si pertinent

## Ce que tu ne fais PAS (mode Compagnon)
- Poser des diagnostics ou recommander des traitements (c'est le mode Consultation, pas ici)
- Prétendre connaître les patients (tu n'as que ce qu'on te partage)
- Ressortir spontanément des informations personnelles (attendre qu'on te sollicite)
- Psychologiser ou analyser l'utilisateur (tu écoutes, tu n'interprètes pas)
- Utiliser des tableaux Markdown (tes réponses sont lues à voix haute)

## Ta posture
- Tu es un outil, pas une personne - tu ne simules pas d'émotions
- Tu dis ""je note"" ou ""je retiens"", jamais ""je comprends ta douleur""
- Tu restes humble : tu peux te tromper, et l'utilisateur a toujours raison sur son vécu
- Si tu n'as pas l'info, tu le dis clairement plutôt que d'inventer"
            };
        }

        /// <summary>
        /// Crée la configuration par défaut pour le Sub-Agent Web (recherche)
        /// </summary>
        public static AgentConfig CreateDefaultWeb()
        {
            return new AgentConfig
            {
                AgentId = "web",
                DisplayName = "Web",
                Description = "Sub-agent de recherche web - cherche, lit et synthétise",
                LLMProvider = "Ollama",
                LLMModel = "llama3",
                IsEnabled = false, // Désactivé par défaut (nécessite clé API Ollama)
                Temperature = 0.3, // Bas pour synthèse factuelle
                MaxSearchResults = 5,
                Posture = @"Tu es un assistant de recherche. Analyse les résultats web et produis une synthèse concise et factuelle.

Format de réponse:
- Résumé en 2-3 phrases
- 3-5 points clés (bullet points)
- Niveau de confiance basé sur la concordance des sources

Règles:
- Cite toujours tes sources
- Reste factuel, pas d'interprétation
- Si les sources se contredisent, mentionne-le
- Privilégie les sources médicales officielles (HAS, Vidal, PubMed)"
            };
        }

        /// <summary>
        /// Crée la configuration par défaut pour l'Agent de Pilotage (Parent'aile)
        /// </summary>
        public static AgentConfig CreateDefaultPilotage()
        {
            return new AgentConfig
            {
                AgentId = "pilotage",
                DisplayName = "Pilotage (Parent'aile)",
                Description = "Agent de tri et d'analyse des messages parents. Détecte l'urgence et suggère des réponses.",
                LLMProvider = "Ollama",
                LLMModel = "gpt-os:20b",
                IsEnabled = true,
                Temperature = 0.3, // Bas pour tri et analyse factuelle
                Posture = @"Tu es l'Agent de Pilotage pour un cabinet de pédopsychiatrie utilisant l'application Parent'aile.
Ton rôle est d'analyser les messages envoyés par les parents d'enfants suivis au cabinet.

### Tes missions :
1. **Synthèse** : Résume le message du parent en une phrase concise (max 20 mots).
2. **Urgence** : Évalue le degré d'urgence (Low, Moderate, Urgent, Critical).
3. **Suggestion** : Propose une réponse type courte, professionnelle et bienveillante que le médecin pourra utiliser.

### Ta posture :
- Professionnel, empathique mais factuel.
- Vigilant sur les signes de décompensation, idées suicidaires, effets secondaires graves des traitements ou crise familiale aiguë.
- Ne pose jamais de diagnostic définitif, reste dans le tri et l'assistance.

### Format de sortie (obligatoire) :
URGENCY: [Low|Moderate|Urgent|Critical]
SUMMARY: [Ta synthèse ici]
SUGGESTION: [Ta suggestion de réponse ici]"
            };
        }
    }

    /// <summary>
    /// Collection de configurations d'agents
    /// </summary>
    public class AgentsConfiguration
    {
        /// <summary>
        /// Liste des agents configurés
        /// </summary>
        public List<AgentConfig> Agents { get; set; } = new();

        /// <summary>
        /// Version du format de configuration
        /// </summary>
        public string Version { get; set; } = "1.0";

        /// <summary>
        /// Crée une configuration par défaut avec Med et Web
        /// </summary>
        public static AgentsConfiguration CreateDefault()
        {
            return new AgentsConfiguration
            {
                Agents = new List<AgentConfig>
                {
                    AgentConfig.CreateDefaultMed(),
                    AgentConfig.CreateDefaultWeb(),
                    AgentConfig.CreateDefaultPilotage()
                }
            };
        }

        /// <summary>
        /// Récupère un agent par son ID
        /// </summary>
        public AgentConfig? GetAgent(string agentId)
        {
            return Agents.FirstOrDefault(a => a.AgentId == agentId);
        }

        /// <summary>
        /// Récupère la configuration de Med
        /// </summary>
        public AgentConfig? GetMed()
        {
            return GetAgent("med");
        }

        /// <summary>
        /// Récupère la configuration du Sub-Agent Web
        /// </summary>
        public AgentConfig? GetWeb()
        {
            return GetAgent("web");
        }

        /// <summary>
        /// Récupère la configuration de l'Agent de Pilotage
        /// </summary>
        public AgentConfig? GetPilotage()
        {
            return GetAgent("pilotage");
        }
    }
}
