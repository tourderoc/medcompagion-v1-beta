using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.Services.LLM;

namespace MedCompanion
{
    public class OpenAIService
    {
        private readonly LLMServiceFactory _llmFactory;
        private AppSettings _settings; // ⚠️ NON readonly pour permettre ReloadSettings()
        private readonly PromptConfigService _promptConfig;
        private readonly AnonymizationService _anonymizationService;
        private readonly PromptTrackerService? _promptTracker;  // ✅ NOUVEAU - Tracking des prompts

        // Cache des prompts pour éviter les appels répétés
        private string _cachedSystemPrompt;
        private string _cachedNoteStructurationPrompt;
        private string _cachedChatInteractionPrompt;

        public OpenAIService(LLMServiceFactory llmFactory, PromptConfigService promptConfig, AnonymizationService anonymizationService, PromptTrackerService? promptTracker = null)
        {
            _llmFactory = llmFactory;
            _settings = AppSettings.Load();
            _promptConfig = promptConfig; // ✅ Utiliser l'instance partagée
            _anonymizationService = anonymizationService;
            _promptTracker = promptTracker;  // ✅ NOUVEAU - Stocker le tracker

            // Charger les prompts initialement
            LoadPrompts();

            // S'abonner à l'événement de rechargement des prompts (si _promptConfig n'est pas null)
            if (_promptConfig != null)
            {
                _promptConfig.PromptsReloaded += OnPromptsReloaded;
            }
        }
        
        /// <summary>
        /// Charge les prompts depuis le service de configuration
        /// </summary>
        private void LoadPrompts()
        {
            // ✅ Vérifier si _promptConfig est null
            if (_promptConfig == null)
            {
                _cachedSystemPrompt = "";
                _cachedNoteStructurationPrompt = "";
                _cachedChatInteractionPrompt = "";
                System.Diagnostics.Debug.WriteLine("[OpenAIService] Prompts non chargés (PromptConfig null)");
                return;
            }

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
        /// Recharge les settings depuis le fichier de configuration (après modification des paramètres)
        /// ✅ IMPORTANT: À appeler après toute sauvegarde des paramètres dans ParametresDialog
        /// </summary>
        public void ReloadSettings()
        {
            _settings = AppSettings.Load();
            System.Diagnostics.Debug.WriteLine($"[OpenAIService] ✅ Settings rechargés - AnonymizationModel: {_settings.AnonymizationModel}");
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

        public async Task<(bool success, string result, double relevanceWeight)> StructurerNoteAsync(
            string nomComplet,
            string sexe,
            string noteBrute,
            CancellationToken cancellationToken = default)
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
                // Vérifier l'annulation
                cancellationToken.ThrowIfCancellationRequested();

                // ✅ ÉTAPE 1 : Créer métadonnées pour l'anonymisation
                var parts = nomComplet.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var prenom = parts.Length > 0 ? parts[0] : "";
                var nom = parts.Length > 1 ? string.Join(" ", parts.Skip(1)) : "";
                
                // Si un seul mot, on le considère comme Nom
                if (string.IsNullOrEmpty(nom) && !string.IsNullOrEmpty(prenom))
                {
                    nom = prenom;
                    prenom = "";
                }

                var patientMeta = new PatientMetadata 
                { 
                    Nom = nom, 
                    Prenom = prenom, 
                    Sexe = sexe 
                };

                // Anonymiser le nom du patient (attendre le résultat async)
                var (nomAnonymise, anonContext) = await _anonymizationService.AnonymizeAsync(nomComplet, patientMeta);

                // Vérifier si une date est déjà présente dans la note brute
                var hasDate = System.Text.RegularExpressions.Regex.IsMatch(
                    noteBrute,
                    @"(date|entretien|consultation|rendez-vous|rdv).*\d{1,2}[\/\-\.]\d{1,2}[\/\-\.]\d{2,4}",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );

                var dateInstruction = hasDate
                    ? ""
                    : $"\n\nIMPORTANT: Aucune date de consultation n'est mentionnée dans la note brute. Utilise automatiquement la date d'aujourd'hui ({DateTime.Now:dd/MM/yyyy}) comme date de l'entretien dans le compte-rendu.";

                // ✅ ÉTAPE 2 : Utiliser le pseudonyme dans le prompt
                var basePrompt = _cachedNoteStructurationPrompt
                    .Replace("{{Nom_Complet}}", nomAnonymise)  // ✅ Pseudonyme au lieu du vrai nom
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

                // ✅ UTILISER LE MODÈLE LOCAL OLLAMA pour la structuration (sécurité des données)
                ILLMService structurationProvider = new OllamaLLMProvider(_settings.OllamaBaseUrl, _settings.AnonymizationModel);
                System.Diagnostics.Debug.WriteLine($"[OpenAIService] ✅ Structuration note via modèle LOCAL Ollama : {_settings.AnonymizationModel}");

                var systemPrompt = BuildSystemPrompt();
                var messages = new List<(string role, string content)>
                {
                    ("user", userPrompt)
                };
                var (success, result, error) = await structurationProvider.ChatAsync(systemPrompt, messages);

                if (!success)
                {
                    return (false, error ?? "Erreur lors de la structuration.", 0.0);
                }

                // Extraire le poids de la réponse
                double weight = ExtractWeightFromResponse(result ?? "");

                // Retirer la ligne POIDS_SYNTHESE du markdown
                string cleanedMarkdown = RemoveWeightLine(result ?? "");

                // ✅ ÉTAPE 3 : Désanonymiser le résultat
                string deanonymizedMarkdown = _anonymizationService.Deanonymize(cleanedMarkdown, anonContext);

                // ✅ ÉTAPE 4 : Logger le prompt (si tracker disponible)
                if (_promptTracker != null)
                {
                    try
                    {
                        // Récupérer les infos du provider LLM actuel
                        var llmProvider = GetCurrentLLM();
                        var providerType = llmProvider.GetType().Name;
                        var providerName = providerType.Replace("LLMProvider", ""); // Ex: "OpenAI" ou "Ollama"

                        // Déterminer le nom du modèle (simplifié)
                        string modelName = providerType.Contains("OpenAI") ? "GPT-4" : "Ollama";

                        _promptTracker.LogPrompt(new PromptLogEntry
                        {
                            Timestamp = DateTime.Now,
                            Module = "Note",  // ✅ Correspond au filtre dans l'UI
                            SystemPrompt = systemPrompt,  // Prompt système (pas de données patient)
                            UserPrompt = userPrompt,      // ⚠️ Contient le PSEUDONYME (anonymisé)
                            AIResponse = deanonymizedMarkdown,  // ✅ Réponse DÉSANONYMISÉE (vrai nom)
                            TokensUsed = 0,  // TODO: récupérer depuis la réponse LLM si disponible
                            LLMProvider = providerName,
                            ModelName = modelName,
                            Success = true,
                            Error = null
                        });
                    }
                    catch (Exception logEx)
                    {
                        // Ne pas bloquer la structuration si le logging échoue
                        System.Diagnostics.Debug.WriteLine($"[OpenAI] Erreur logging prompt: {logEx.Message}");
                    }
                }

                return (true, deanonymizedMarkdown, weight);
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
            string? customSystemPrompt = null,
            string? compactedMemory = null,
            List<ChatExchange>? recentSavedExchanges = null,
            int maxTokens = 1500)
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

                // Construire le userPrompt avec contexte et mémoire intelligente
                var userPromptBuilder = new StringBuilder();

                // 1. Ajouter le contexte patient (TOUJOURS EN PREMIER)
                if (!string.IsNullOrWhiteSpace(contexte))
                {
                    userPromptBuilder.AppendLine("CONTEXTE PATIENT");
                    userPromptBuilder.AppendLine("================");
                    userPromptBuilder.AppendLine(contexte);
                    userPromptBuilder.AppendLine();
                }

                // 2. Ajouter la mémoire compactée (résumé des anciens échanges)
                if (!string.IsNullOrWhiteSpace(compactedMemory))
                {
                    userPromptBuilder.AppendLine("MÉMOIRE COMPACTÉE (anciens échanges)");
                    userPromptBuilder.AppendLine("====================================");
                    userPromptBuilder.AppendLine(compactedMemory);
                    userPromptBuilder.AppendLine();
                }

                // 3. Ajouter les échanges récents sauvegardés (10 derniers)
                if (recentSavedExchanges != null && recentSavedExchanges.Count > 0)
                {
                    userPromptBuilder.AppendLine("ÉCHANGES RÉCENTS SAUVEGARDÉS");
                    userPromptBuilder.AppendLine("============================");
                    foreach (var exchange in recentSavedExchanges)
                    {
                        userPromptBuilder.AppendLine($"[{exchange.Timestamp:dd/MM/yyyy HH:mm}] {exchange.Etiquette}");
                        userPromptBuilder.AppendLine($"Q: {exchange.Question}");
                        userPromptBuilder.AppendLine($"R: {exchange.Response}");
                        userPromptBuilder.AppendLine();
                    }
                }

                // 4. Ajouter l'historique temporaire des 3 derniers échanges (session en cours)
                if (historique != null && historique.Count > 0)
                {
                    var recentHistory = historique.TakeLast(3);
                    userPromptBuilder.AppendLine("HISTORIQUE DE LA SESSION EN COURS");
                    userPromptBuilder.AppendLine("==================================");
                    foreach (var exchange in recentHistory)
                    {
                        userPromptBuilder.AppendLine($"Q: {exchange.Question}");
                        userPromptBuilder.AppendLine($"R: {exchange.Response}");
                        userPromptBuilder.AppendLine();
                    }
                }

                // 5. Ajouter la question actuelle
                userPromptBuilder.AppendLine("QUESTION ACTUELLE");
                userPromptBuilder.AppendLine("=================");
                userPromptBuilder.AppendLine(question);

                var userPrompt = userPromptBuilder.ToString();

                // Utiliser le LLM service unifié (provider actuel)
                var currentLLM = GetCurrentLLM();
                var llmName = currentLLM.GetType().Name;
                var modelName = currentLLM.GetModelName();

                // LOG DÉTAILLÉ POUR DEBUG
                System.Diagnostics.Debug.WriteLine("┌─────────────────────────────────────────────────────────────┐");
                System.Diagnostics.Debug.WriteLine("│ [OpenAIService] ChatAvecContexteAsync - Appel LLM          │");
                System.Diagnostics.Debug.WriteLine("└─────────────────────────────────────────────────────────────┘");
                System.Diagnostics.Debug.WriteLine($"🤖 Provider: {llmName}");
                System.Diagnostics.Debug.WriteLine($"🏷️  Modèle: {modelName}");
                System.Diagnostics.Debug.WriteLine($"📊 SystemPrompt: {systemPrompt?.Length ?? 0} caractères");
                System.Diagnostics.Debug.WriteLine($"📝 UserPrompt: {userPrompt?.Length ?? 0} caractères");
                System.Diagnostics.Debug.WriteLine("─────────────────────────────────────────────────────────────");

                var messages = new List<(string role, string content)>
                {
                    ("user", userPrompt)
                };
                var (success, result, error) = await currentLLM.ChatAsync(systemPrompt, messages, maxTokens);

                System.Diagnostics.Debug.WriteLine($"✅ Succès: {success}");
                System.Diagnostics.Debug.WriteLine($"📤 Résultat: {result?.Length ?? 0} caractères");
                System.Diagnostics.Debug.WriteLine($"❌ Erreur: {error ?? "Aucune"}");
                System.Diagnostics.Debug.WriteLine("└─────────────────────────────────────────────────────────────┘");

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
        /// <summary>
        /// Extrait les entités sensibles (PII) d'un texte via le LLM actif.
        /// </summary>
        public async Task<PIIExtractionResult> ExtractPIIAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new PIIExtractionResult();

            if (!IsApiKeyConfigured())
            {
                System.Diagnostics.Debug.WriteLine("[OpenAIService] LLM non configuré pour l'extraction PII");
                return new PIIExtractionResult();
            }

            try
            {
                var textToAnalyze = text.Length > 2000 ? text.Substring(0, 2000) : text;

                var prompt = $@"Tu es un expert en confidentialité des données médicales.
Ta tâche est d'analyser le texte OCR suivant pour identifier TOUTES les informations identifiantes (PII).

Texte à analyser :
""{textToAnalyze}""

Instructions :
1. Extrais TOUS les noms de personnes (Patient, Médecin, Proche).
2. Extrais TOUTES les dates (Naissance, Consultation, Courrier).
3. Extrais TOUS les lieux (Villes, Adresses précises, Cliniques).
4. Extrais TOUTES les organisations (Hôpitaux, Laboratoires).
5. Réponds UNIQUEMENT au format JSON strict. Pas de markdown, pas d'explications.

Format JSON attendu :
{{
    ""noms"": [""M. Dupont"", ""Dr. House""],
    ""dates"": [""12/05/2022"", ""10 janvier 2023""],
    ""lieux"": [""Paris"", ""10 rue de la Paix""],
    ""organisations"": [""Clinique des Lilas""]
}}";

                var systemPrompt = "Tu es un extracteur d'entités JSON strict. Tu ne réponds jamais autre chose que du JSON.";
                
                var messages = new List<(string role, string content)>
                {
                    ("user", prompt)
                };

                // ✅ SÉCURITÉ : L'extraction PII doit TOUJOURS utiliser un modèle Ollama local
                // pour éviter d'envoyer des données sensibles au cloud AVANT anonymisation
                if (string.IsNullOrEmpty(_settings.AnonymizationModel))
                {
                    System.Diagnostics.Debug.WriteLine("[OpenAIService] ❌ ERREUR : Modèle d'anonymisation non configuré");
                    throw new InvalidOperationException(
                        "❌ SÉCURITÉ : Modèle d'anonymisation local (Ollama) non configuré.\n\n" +
                        "L'extraction PII ne peut PAS utiliser OpenAI (cloud) pour des raisons de confidentialité.\n" +
                        "Les données sensibles du patient doivent rester locales.\n\n" +
                        "Solution : Configurez 'AnonymizationModel' dans Paramètres > Anonymisation.\n" +
                        "Exemple : llama3.2, mistral, phi3"
                    );
                }

                // Créer un provider Ollama avec le modèle dédié (toujours local)
                ILLMService piiProvider = new OllamaLLMProvider(_settings.OllamaBaseUrl, _settings.AnonymizationModel);
                System.Diagnostics.Debug.WriteLine($"[OpenAIService] ✅ Extraction PII via modèle LOCAL Ollama : {_settings.AnonymizationModel}");

                var (success, result, error) = await piiProvider.ChatAsync(systemPrompt, messages);

                if (!success || string.IsNullOrWhiteSpace(result))
                {
                    System.Diagnostics.Debug.WriteLine($"[OpenAIService] Erreur extraction PII : {error}");
                    return new PIIExtractionResult();
                }

                var json = result.Trim();
                if (json.StartsWith("```json")) json = json.Replace("```json", "").Replace("```", "");
                if (json.StartsWith("```")) json = json.Replace("```", "");
                
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var extraction = JsonSerializer.Deserialize<PIIExtractionResult>(json, options);

                return extraction ?? new PIIExtractionResult();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OpenAIService] Exception lors de l'extraction PII : {ex.Message}");
                return new PIIExtractionResult();
            }
        }
    }
}
