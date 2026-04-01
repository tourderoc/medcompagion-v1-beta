using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MedCompanion.Models;
using MedCompanion.Services.LLM;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service de régénération de contenu via LLM
    /// Permet de régénérer notes, synthèses, courriers avec un modèle LLM choisi
    /// Utilise le système d'anonymisation Phase 1+2+3 pour les providers cloud
    /// </summary>
    public class RegenerationService
    {
        private readonly AppSettings _settings;
        private readonly AnonymizationService _anonymizationService;
        private readonly PromptConfigService _promptConfig;
        private readonly OpenAIService? _openAIService; // Pour Phase 3 (extraction PII via LLM local)

        public RegenerationService(
            AppSettings settings,
            AnonymizationService anonymizationService,
            PromptConfigService promptConfig,
            OpenAIService? openAIService = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _anonymizationService = anonymizationService ?? throw new ArgumentNullException(nameof(anonymizationService));
            _promptConfig = promptConfig ?? throw new ArgumentNullException(nameof(promptConfig));
            _openAIService = openAIService; // Optionnel - si null, Phase 3 sera sautée
        }

        /// <summary>
        /// Régénère un contenu avec les instructions fournies
        /// Utilise Phase 1+2+3 pour anonymisation complète avec les providers cloud
        /// </summary>
        /// <param name="originalContent">Contenu original à modifier</param>
        /// <param name="instructions">Instructions de modification de l'utilisateur</param>
        /// <param name="contentType">Type de contenu (Note, Synthesis, Letter)</param>
        /// <param name="providerName">Nom du provider (Ollama ou OpenAI)</param>
        /// <param name="modelName">Nom du modèle à utiliser</param>
        /// <param name="patientMetadata">Métadonnées patient pour anonymisation (optionnel)</param>
        /// <param name="cancellationToken">Token d'annulation</param>
        /// <returns>Tuple (succès, contenu régénéré, message d'erreur)</returns>
        public async Task<(bool success, string result, string? error)> RegenerateAsync(
            string originalContent,
            string instructions,
            string contentType,
            string providerName,
            string modelName,
            PatientMetadata? patientMetadata = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(originalContent))
            {
                return (false, "", "Le contenu original est vide.");
            }

            if (string.IsNullOrWhiteSpace(instructions))
            {
                return (false, "", "Les instructions de modification sont requises.");
            }

            try
            {
                System.Diagnostics.Debug.WriteLine($"[RegenerationService] Début régénération - Type: {contentType}, Provider: {providerName}, Modèle: {modelName}");

                // Vérifier l'annulation au début
                cancellationToken.ThrowIfCancellationRequested();

                // Étape 1 : Préparer le contenu (Anonymisation si nécessaire)
                string contentToProcess = RemoveYamlHeader(originalContent);
                AnonymizationContext? anonContext = null;

                if (providerName.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)) // Assuming this is the intended condition for anonymization
                {
                    System.Diagnostics.Debug.WriteLine($"[RegenerationService] Provider cloud détecté → Anonymisation Phase 1+2+3");

                    // Vérifier l'annulation avant Phase 3
                    cancellationToken.ThrowIfCancellationRequested();

                    // Phase 3 : Extraction des entités sensibles via LLM local (si disponible)
                    PIIExtractionResult? extractedPii = null;
                    if (_openAIService != null)
                    {
                        try
                        {
                            System.Diagnostics.Debug.WriteLine($"[RegenerationService] Phase 3: Extraction PII via LLM local...");
                            extractedPii = await _openAIService.ExtractPIIAsync(originalContent, cancellationToken);
                            System.Diagnostics.Debug.WriteLine($"[RegenerationService] Phase 3: {extractedPii?.GetAllEntities().Count() ?? 0} entités extraites");
                        }
                        catch (OperationCanceledException)
                        {
                            throw; // Propager l'annulation
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"[RegenerationService] Phase 3 échouée (continue avec Phase 1+2): {ex.Message}");
                            // Continue sans Phase 3 - Phase 1+2 seront toujours appliquées
                        }
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[RegenerationService] Phase 3 non disponible (OpenAIService null) - Phase 1+2 uniquement");
                    }

                    // Anonymisation hybride : Phase 1+2+3
                    anonContext = new AnonymizationContext { WasAnonymized = true };
                    contentToProcess = _anonymizationService.AnonymizeWithExtractedData(
                        originalContent,
                        extractedPii,      // Phase 3 : Entités détectées par LLM local
                        patientMetadata,   // Phase 1 : Données patient.json
                        anonContext        // Phase 2 : Regex appliqué automatiquement
                    );

                    System.Diagnostics.Debug.WriteLine($"[RegenerationService] Anonymisation terminée - {anonContext.Replacements.Count} remplacements");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[RegenerationService] Provider local (Ollama) → Pas d'anonymisation");
                }

                // Étape 2 : Construire le prompt via le système de prompts existant
                string systemPrompt = BuildSystemPrompt(contentType);
                string userPrompt = BuildUserPrompt(contentToProcess, instructions, contentType);

                // Vérifier l'annulation avant l'appel LLM
                cancellationToken.ThrowIfCancellationRequested();

                // Étape 3 : Créer le provider LLM approprié
                ILLMService llmProvider = CreateProvider(providerName, modelName);

                System.Diagnostics.Debug.WriteLine($"[RegenerationService] Appel LLM: {providerName}/{modelName}");

                // Étape 4 : Appeler le LLM
                var messages = new List<(string role, string content)>
                {
                    ("user", userPrompt)
                };

                var (success, result, error) = await llmProvider.ChatAsync(systemPrompt, messages, 4000, cancellationToken);

                if (!success)
                {
                    return (false, "", error ?? "Erreur lors de la régénération.");
                }

                // Vérifier l'annulation après l'appel LLM
                cancellationToken.ThrowIfCancellationRequested();

                // Nettoyage du résultat (blocs de code markdown, etc.)
                string finalResult = CleanLLMResponse(result ?? "");

                // Étape 5 : Désanonymiser si nécessaire (restaurer les vraies données)
                if (anonContext != null && anonContext.WasAnonymized)
                {
                    finalResult = _anonymizationService.Deanonymize(finalResult, anonContext);
                    System.Diagnostics.Debug.WriteLine($"[RegenerationService] Désanonymisation effectuée");
                }

                // Étape 6 : Post-traitement - Restaurer les sauts de ligne si le LLM les a supprimés
                finalResult = RestoreMarkdownLineBreaks(finalResult);

                // Étape 7 : Restaurer le YAML original si présent
                string yamlHeader = ExtractYamlHeader(originalContent);
                if (!string.IsNullOrEmpty(yamlHeader))
                {
                    finalResult = yamlHeader + "\n\n" + finalResult;
                    System.Diagnostics.Debug.WriteLine($"[RegenerationService] YAML restauré");
                }

                System.Diagnostics.Debug.WriteLine($"[RegenerationService] Régénération réussie - {finalResult.Length} caractères");

                return (true, finalResult, null);
            }
            catch (OperationCanceledException)
            {
                System.Diagnostics.Debug.WriteLine($"[RegenerationService] Régénération annulée par l'utilisateur");
                throw; // Propager pour que l'appelant puisse gérer l'annulation
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RegenerationService] Erreur: {ex.Message}");
                return (false, "", $"Erreur lors de la régénération: {ex.Message}");
            }
        }

        /// <summary>
        /// Crée le provider LLM approprié selon le nom
        /// </summary>
        private ILLMService CreateProvider(string providerName, string modelName)
        {
            if (providerName.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
            {
                return new OllamaLLMProvider(_settings.OllamaBaseUrl, modelName);
            }
            else if (providerName.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
            {
                // Récupérer la clé API depuis le stockage sécurisé
                var secureStorage = new SecureStorageService();
                string? apiKey = secureStorage.HasApiKey("OpenAI") ? secureStorage.GetApiKey("OpenAI") : null;
                return new OpenAILLMProvider(apiKey, modelName);
            }
            else
            {
                throw new ArgumentException($"Provider inconnu: {providerName}");
            }
        }

        /// <summary>
        /// Construit le prompt système via le service de prompts existant
        /// </summary>
        private string BuildSystemPrompt(string contentType)
        {
            // Utiliser le prompt système global existant du PromptConfigService
            string basePrompt = _promptConfig.GetActivePrompt("system_global");

            if (string.IsNullOrEmpty(basePrompt))
            {
                basePrompt = "Tu es un assistant médical spécialisé en psychiatrie. Tu aides à rédiger et modifier des documents médicaux.";
            }

            // Ajouter contexte spécifique selon le type de contenu
            string typeSpecific = contentType.ToLower() switch
            {
                "note" => "\n\nTu travailles actuellement sur une NOTE CLINIQUE. Conserve le format markdown structuré avec les sections existantes.",
                "synthesis" => "\n\nTu travailles actuellement sur une SYNTHÈSE PATIENT. Conserve la structure et les sections existantes.",
                "letter" => "\n\nTu travailles actuellement sur un COURRIER MÉDICAL. Conserve le ton professionnel et la mise en forme.",
                _ => "\n\nConserve le format et la structure du document original."
            };

            return basePrompt + typeSpecific;
        }

        /// <summary>
        /// Construit le prompt utilisateur avec le contenu et les instructions
        /// </summary>
        private string BuildUserPrompt(string content, string instructions, string contentType)
        {
            string typeName = contentType.ToLower() switch
            {
                "note" => "note clinique",
                "synthesis" => "synthèse patient",
                "letter" => "courrier médical",
                _ => "document"
            };

            return $@"Voici une {typeName} à modifier :

<document>
{content}
</document>

INSTRUCTIONS DE MODIFICATION :
{instructions}

CONSIGNES IMPORTANTES :
- Applique les modifications demandées tout en conservant le format et la structure
- CONSERVE ABSOLUMENT les sauts de ligne et la mise en forme markdown originale
- Chaque titre (# ou ##) doit être sur sa propre ligne
- Chaque élément de liste (- ou *) doit être sur sa propre ligne
- Ne change que ce qui est explicitement demandé dans les instructions
- Conserve les informations médicales importantes non concernées par les modifications
- Retourne UNIQUEMENT le contenu modifié en markdown valide, sans commentaires ni explications
- INTERDICTION ABSOLUE : ne rajoute AUCUNE ligne de métadonnées au début (ex: patient: ..., date: ..., type: ..., status: ...). Commence directement par le contenu ou le titre.
- Ne rajoute pas de texte avant ou après le document
- NE PAS inclure les balises <document></document> dans ta réponse";
        }

        /// <summary>
        /// Récupère la liste des modèles disponibles (Ollama + OpenAI)
        /// </summary>
        public async Task<List<(string provider, string model, string displayName)>> GetAvailableModelsAsync()
        {
            var models = new List<(string provider, string model, string displayName)>();

            // Modèles Ollama locaux
            try
            {
                var ollamaProvider = new OllamaLLMProvider(_settings.OllamaBaseUrl);
                var ollamaModels = await ollamaProvider.DetectAvailableModelsAsync();
                foreach (var model in ollamaModels)
                {
                    models.Add(("Ollama", model, $"🖥️ {model} (Local)"));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RegenerationService] Erreur détection Ollama: {ex.Message}");
            }

            // Modèles OpenAI (si clé configurée)
            var secureStorage = new SecureStorageService();
            if (secureStorage.HasApiKey("OpenAI"))
            {
                models.Add(("OpenAI", "gpt-4o-mini", "☁️ GPT-4o Mini (Cloud)"));
                models.Add(("OpenAI", "gpt-4o", "☁️ GPT-4o (Cloud)"));
                models.Add(("OpenAI", "gpt-4-turbo", "☁️ GPT-4 Turbo (Cloud)"));
            }

            return models;
        }

        /// <summary>
        /// Restaure les sauts de ligne dans le markdown si le LLM les a supprimés
        /// Ajoute un saut de ligne AVANT les marqueurs markdown courants
        /// </summary>
        /// <summary>
        /// Nettoie la réponse du LLM (supprime les blocs de code markdown ```)
        /// </summary>
        private string CleanLLMResponse(string response)
        {
            if (string.IsNullOrWhiteSpace(response)) return response;

            var cleaned = response.Trim();

            // 1. Supprimer les blocs de code markdown style ```markdown ... ```
            if (cleaned.StartsWith("```"))
            {
                int firstLineEnd = cleaned.IndexOf('\n');
                if (firstLineEnd > 0)
                {
                    cleaned = cleaned.Substring(firstLineEnd).Trim();
                }

                if (cleaned.EndsWith("```"))
                {
                    cleaned = cleaned.Substring(0, cleaned.Length - 3).Trim();
                }
            }

            // 2. Nettoyage agressif des métadonnées (si le LLM n'a pas écouté les instructions)
            cleaned = RemoveYamlHeader(cleaned);

            return cleaned;
        }

        private string ExtractYamlHeader(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return "";
            
            // On ne prend que le PREMIER bloc YAML valide s'il existe
            var yamlRegex = new Regex(@"^\s*(---\s*[\s\S]*?---)", RegexOptions.Multiline);
            var match = yamlRegex.Match(content);
            
            return match.Success ? match.Groups[1].Value : "";
        }

        private string RemoveYamlHeader(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return content;

            var result = content;

            // 1. Supprimer TOUS les blocs délimités par ---
            var blockRegex = new Regex(@"^\s*---\s*[\s\S]*?---\s*", RegexOptions.Multiline);
            while (blockRegex.IsMatch(result))
            {
                result = blockRegex.Replace(result, "");
            }

            // 2. Supprimer les lignes de métadonnées "orphelines" au début (key: value)
            var lines = result.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var metadataRegex = new Regex(@"^\s*[\w\s-]+:\s*[""'].*?[""']|^\s*[\w\s-]+:\s*[^#\s]+", RegexOptions.IgnoreCase);
            
            int skipLines = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (string.IsNullOrWhiteSpace(line))
                {
                    skipLines++;
                    continue;
                }

                if (metadataRegex.IsMatch(line))
                {
                    skipLines++;
                    continue;
                }

                break;
            }

            if (skipLines > 0)
            {
                result = string.Join("\n", lines.Skip(skipLines));
            }

            return result.TrimStart();
        }

        private string RestoreMarkdownLineBreaks(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return content;

            // Compter les lignes pour détecter si le contenu est "aplati"
            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var lineCount = lines.Length;
            var contentLength = content.Length;

            // Calculer la longueur moyenne par ligne
            double avgLineLength = lineCount > 0 ? (double)contentLength / lineCount : contentLength;

            // Heuristique améliorée : Le contenu est probablement aplati si :
            // - Moins de 5 lignes pour plus de 500 caractères
            // - OU la longueur moyenne par ligne est > 200 (lignes trop longues)
            bool needsRestore = (lineCount < 5 && contentLength > 500) || avgLineLength > 200;

            if (!needsRestore)
            {
                System.Diagnostics.Debug.WriteLine($"[RegenerationService] Markdown semble bien formaté ({lineCount} lignes, avg={avgLineLength:F0} chars/ligne)");
                return content;
            }

            System.Diagnostics.Debug.WriteLine($"[RegenerationService] Markdown aplati détecté ({lineCount} lignes, avg={avgLineLength:F0} chars/ligne) - Restauration des sauts de ligne...");

            var result = content;

            // Ajouter un saut de ligne AVANT les titres (# ## ### ####)
            result = System.Text.RegularExpressions.Regex.Replace(result, @"(?<!\n)\s*(#{1,4}\s+)", "\n\n$1");

            // Ajouter un saut de ligne AVANT les éléments de liste avec gras (- **texte**)
            result = System.Text.RegularExpressions.Regex.Replace(result, @"(?<!\n)\s*(-\s+\*\*)", "\n$1");

            // Ajouter un saut de ligne AVANT les listes à puces simples (- Texte)
            result = System.Text.RegularExpressions.Regex.Replace(result, @"(?<!\n)(\s*-\s+[A-Z])", "\n$1");

            // Ajouter un saut de ligne AVANT "**•" ou "• **" (format bullet point avec gras)
            result = System.Text.RegularExpressions.Regex.Replace(result, @"(?<!\n)\s*(\*\*•|\•\s*\*\*)", "\n$1");

            // Ajouter un saut de ligne AVANT les sections numérotées (1. 2. 3. etc.)
            result = System.Text.RegularExpressions.Regex.Replace(result, @"(?<!\n)\s*(\d+\.\s+)", "\n$1");

            // Nettoyer les sauts de ligne multiples (plus de 2 consécutifs)
            result = System.Text.RegularExpressions.Regex.Replace(result, @"\n{3,}", "\n\n");

            // Nettoyer le début du document
            result = result.TrimStart('\n', '\r');

            var newLineCount = result.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).Length;
            System.Diagnostics.Debug.WriteLine($"[RegenerationService] Sauts de ligne restaurés: {lineCount} → {newLineCount} lignes");

            return result;
        }
    }
}
