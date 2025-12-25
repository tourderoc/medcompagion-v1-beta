using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service pour extraire automatiquement un template à partir d'un exemple de courrier
    /// Utilise LLMGatewayService pour l'anonymisation automatique des données patient
    /// </summary>
    public class TemplateExtractorService
    {
        private readonly OpenAIService _openAIService;
        private LLMGatewayService? _llmGatewayService;

        public TemplateExtractorService(OpenAIService openAIService)
        {
            _openAIService = openAIService;
        }

        /// <summary>
        /// Configure le service LLMGateway pour l'anonymisation automatique
        /// </summary>
        public void SetLLMGatewayService(LLMGatewayService llmGatewayService)
        {
            _llmGatewayService = llmGatewayService;
        }

        /// <summary>
        /// Extrait un template à partir d'un exemple de courrier
        /// </summary>
        public async Task<(bool success, string templateMarkdown, string suggestedName, List<string> variables, string error)> 
            ExtractTemplateFromExample(string exampleLetter)
        {
            if (string.IsNullOrWhiteSpace(exampleLetter))
            {
                return (false, string.Empty, string.Empty, new List<string>(), "Le courrier exemple est vide.");
            }

            try
            {
                var systemPrompt = @"Tu es un expert en analyse de documents médicaux. Ta tâche est d'analyser un exemple de courrier médical et d'en extraire un template réutilisable.

INSTRUCTIONS :
1. Identifie TOUTES les parties variables du courrier (noms, prénoms, dates, âges, diagnostics, établissements, etc.)
2. Remplace chaque partie variable par un placeholder au format {{Nom_Variable}}
3. Conserve EXACTEMENT la structure, mise en forme et ponctuation du courrier original
4. Utilise des noms de variables CLAIRS et DESCRIPTIFS en français
5. Propose un nom court et pertinent pour ce type de courrier

FORMAT DE RÉPONSE ATTENDU :
```TEMPLATE_NAME
[Nom court du template, ex: ""Demande d'évaluation orthophonique""]
```

```TEMPLATE_CONTENT
[Le template avec les placeholders {{Variable}}]
```

```VARIABLES
{{Variable1}}, {{Variable2}}, {{Variable3}}, ...
```

EXEMPLES DE VARIABLES :
- {{Nom_Prenom}} pour un nom complet
- {{Age}} pour l'âge
- {{Date_Naissance}} pour une date de naissance
- {{Ecole}} pour un établissement scolaire
- {{Classe}} pour une classe
- {{Diagnostic}} pour un diagnostic médical
- {{Symptomes}} pour une description de symptômes
- {{Destinataire}} pour le destinataire du courrier";

                var userPrompt = $@"Analyse ce courrier médical et extrait-en un template réutilisable :

{exampleLetter}";

                string result;
                bool success;

                // Utiliser LLMGatewayService si disponible (anonymisation automatique)
                if (_llmGatewayService != null)
                {
                    var messages = new List<(string role, string content)>
                    {
                        ("user", userPrompt)
                    };

                    var (gatewaySuccess, gatewayResult, error) = await _llmGatewayService.ChatAsync(
                        systemPrompt,
                        messages,
                        null,  // Pas de nom de patient spécifique, mais l'anonymisation par patterns sera appliquée
                        maxTokens: 2000
                    );

                    success = gatewaySuccess;
                    result = gatewaySuccess ? gatewayResult : (error ?? "Erreur inconnue");
                }
                else
                {
                    // Fallback sur OpenAIService (sans anonymisation)
                    var (directSuccess, directResult) = await _openAIService.ChatAvecContexteAsync(
                        string.Empty,
                        userPrompt,
                        null,
                        systemPrompt
                    );
                    success = directSuccess;
                    result = directResult;
                }

                if (!success)
                {
                    return (false, string.Empty, string.Empty, new List<string>(), result);
                }

                // Parser la réponse de l'IA
                var (parsedSuccess, templateName, templateContent, variables) = ParseAIResponse(result);

                if (!parsedSuccess)
                {
                    return (false, string.Empty, string.Empty, new List<string>(), 
                        "Impossible d'analyser la réponse de l'IA. Réessayez.");
                }

                return (true, templateContent, templateName, variables, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, string.Empty, string.Empty, new List<string>(), 
                    $"Erreur lors de l'extraction : {ex.Message}");
            }
        }

        /// <summary>
        /// Parse la réponse de l'IA pour extraire le nom, le contenu et les variables
        /// </summary>
        private (bool success, string name, string content, List<string> variables) ParseAIResponse(string aiResponse)
        {
            try
            {
                // Extraire TEMPLATE_NAME
                var nameMatch = Regex.Match(aiResponse, @"```TEMPLATE_NAME\s*\n(.+?)\n```", RegexOptions.Singleline);
                var name = nameMatch.Success ? nameMatch.Groups[1].Value.Trim() : "Template personnalisé";

                // Extraire TEMPLATE_CONTENT
                var contentMatch = Regex.Match(aiResponse, @"```TEMPLATE_CONTENT\s*\n(.+?)\n```", RegexOptions.Singleline);
                if (!contentMatch.Success)
                {
                    return (false, string.Empty, string.Empty, new List<string>());
                }
                var content = contentMatch.Groups[1].Value.Trim();

                // Extraire VARIABLES
                var variablesMatch = Regex.Match(aiResponse, @"```VARIABLES\s*\n(.+?)\n```", RegexOptions.Singleline);
                var variables = new List<string>();
                
                if (variablesMatch.Success)
                {
                    var variablesStr = variablesMatch.Groups[1].Value;
                    variables = variablesStr
                        .Split(new[] { ',', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(v => v.Trim().Replace("{{", "").Replace("}}", ""))
                        .Where(v => !string.IsNullOrWhiteSpace(v))
                        .Distinct()
                        .ToList();
                }
                else
                {
                    // Fallback : extraire les variables directement du contenu
                    variables = ExtractVariablesFromContent(content);
                }

                return (true, name, content, variables);
            }
            catch
            {
                return (false, string.Empty, string.Empty, new List<string>());
            }
        }

        /// <summary>
        /// Extrait les variables d'un template en analysant les {{placeholders}}
        /// </summary>
        private List<string> ExtractVariablesFromContent(string templateContent)
        {
            var matches = Regex.Matches(templateContent, @"\{\{([^}]+)\}\}");
            return matches
                .Cast<Match>()
                .Select(m => m.Groups[1].Value.Trim())
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// Valide qu'un template est bien formé
        /// </summary>
        public (bool isValid, string error) ValidateTemplate(string templateMarkdown)
        {
            if (string.IsNullOrWhiteSpace(templateMarkdown))
            {
                return (false, "Le template est vide.");
            }

            // Vérifier que les placeholders sont bien formés
            var invalidPlaceholders = Regex.Matches(templateMarkdown, @"\{[^{]|[^}]\}");
            if (invalidPlaceholders.Count > 0)
            {
                return (false, "Le template contient des accolades mal formées. Utilisez {{Variable}}.");
            }

            // Vérifier qu'il y a au moins une variable
            var variables = ExtractVariablesFromContent(templateMarkdown);
            if (variables.Count == 0)
            {
                return (false, "Le template doit contenir au moins une variable {{Variable}}.");
            }

            return (true, string.Empty);
        }

        // ========== NOUVELLES MÉTHODES POUR LE SYSTÈME MCC ==========

        /// <summary>
        /// Analyse la sémantique d'un document médical avec l'IA
        /// Identifie le ton, le public cible, la tranche d'âge, le type de document, les mots-clés et la structure
        /// </summary>
        public async Task<(bool success, SemanticAnalysis analysis, string error)> 
            AnalyzeDocumentSemantic(string documentText)
        {
            if (string.IsNullOrWhiteSpace(documentText))
            {
                return (false, null, "Le document est vide.");
            }

            try
            {
                var systemPrompt = @"Analyse attentivement ce document médical ou éducatif et extrait un maximum de mots-clés pertinents.

Renvoie un JSON structuré contenant les champs suivants :

{
  ""doc_type"": ""courrier | attestation | note | synthese | rapport"",
  ""public"": ""parents | ecole | medecin | institution | juge | mixte"",
  ""tone"": ""bienveillant | clinique | administratif | pedagogique | formel"",
  ""age_group"": ""0-3 | 3-6 | 6-11 | 12-15 | 16+"",
  ""detail_level"": ""bref | complet | analytique"",
  ""context_summary"": ""Résumé en 2 phrases du contexte global (famille, école, difficultés)"",
  ""themes"": [15-20 thèmes MINIMUM couvrant TOUS les aspects du document : aspects cognitifs, émotionnels, comportementaux, éducatifs, sociaux, familiaux, médicaux, développementaux...],
  ""keywords"": {
      ""a_utiliser"": [20-30 mots-clés MINIMUM à utiliser : termes techniques, concepts pédagogiques, qualités, compétences, stratégies, outils, approches positives, forces, capacités...],
      ""a_eviter"": [15-20 mots MINIMUM à éviter : termes stigmatisants, pathologiques, négatifs, diagnostics réducteurs...]
  },
  ""style"": {
      ""longueur"": ""court | moyen | long"",
      ""phrases_moyennes"": ""nombre approximatif de mots par phrase"",
      ""structure_richesse"": ""faible | moyenne | elevee""
  },
  ""meta"": {
      ""semantic_confidence"": ""0.0 - 1.0"",
      ""detected_language"": ""fr"",
      ""source"": ""TemplateExtractor""
  }
}

INSTRUCTIONS IMPORTANTES pour les mots-clés :
- Sois EXHAUSTIF : extrais au minimum 15-20 thèmes et 20-30 mots-clés à utiliser
- Catégories à couvrir : cognitif, émotionnel, comportemental, social, familial, scolaire, médical, développemental
- Pour ""a_utiliser"" : inclus termes techniques (attention soutenue, mémoire de travail...), concepts pédagogiques (différenciation, PAI...), qualités (persévérance, créativité...), compétences, stratégies d'adaptation
- Pour ""a_eviter"" : inclus termes stigmatisants, diagnostics réducteurs, vocabulaire négatif

Réponds uniquement en JSON valide et complet, sans texte explicatif.";

                var userPrompt = $@"Analyse attentivement ce document médical ou éducatif.

Renvoie un JSON structuré contenant les champs suivants :

{{
  ""doc_type"": ""courrier | attestation | note | synthese | rapport"",
  ""public"": ""parents | ecole | medecin | institution | juge | mixte"",
  ""tone"": ""bienveillant | clinique | administratif | pedagogique | formel"",
  ""age_group"": ""0-3 | 3-6 | 6-11 | 12-15 | 16+"",
  ""detail_level"": ""bref | complet | analytique"",
  ""context_summary"": ""Résumé en 2 phrases du contexte global (famille, école, difficultés)"",
  ""themes"": [""attention"", ""anxiete"", ""regulation_emotionnelle"", ""socialisation"", ...],
  ""keywords"": {{
      ""a_utiliser"": [""bienveillance"", ""cadre"", ""progression"", ...],
      ""a_eviter"": [""trouble"", ""pathologie"", ""deficience"", ...]
  }},
  ""style"": {{
      ""longueur"": ""court | moyen | long"",
      ""phrases_moyennes"": ""nombre approximatif de mots par phrase"",
      ""structure_richesse"": ""faible | moyenne | elevee""
  }},
  ""meta"": {{
      ""semantic_confidence"": ""0.0 - 1.0"",
      ""detected_language"": ""fr"",
      ""source"": ""TemplateExtractor""
  }}
}}

Réponds uniquement en JSON valide et complet, sans texte explicatif.

DOCUMENT À ANALYSER :
{documentText}";

                string result;
                bool success;

                // Utiliser LLMGatewayService si disponible (anonymisation automatique)
                if (_llmGatewayService != null)
                {
                    var messages = new List<(string role, string content)>
                    {
                        ("user", userPrompt)
                    };

                    var (gatewaySuccess, gatewayResult, error) = await _llmGatewayService.ChatAsync(
                        systemPrompt,
                        messages,
                        null,  // Pas de nom de patient spécifique
                        maxTokens: 2000
                    );

                    success = gatewaySuccess;
                    result = gatewaySuccess ? gatewayResult : (error ?? "Erreur inconnue");
                }
                else
                {
                    // Fallback sur OpenAIService (sans anonymisation)
                    var (directSuccess, directResult) = await _openAIService.ChatAvecContexteAsync(
                        string.Empty,
                        userPrompt,
                        null,
                        systemPrompt
                    );
                    success = directSuccess;
                    result = directResult;
                }

                if (!success)
                {
                    return (false, null, result);
                }

                // Nettoyer la réponse (enlever les ```json si présents)
                var cleanedResult = result.Trim();
                if (cleanedResult.StartsWith("```json"))
                {
                    cleanedResult = cleanedResult.Substring(7);
                }
                if (cleanedResult.StartsWith("```"))
                {
                    cleanedResult = cleanedResult.Substring(3);
                }
                if (cleanedResult.EndsWith("```"))
                {
                    cleanedResult = cleanedResult.Substring(0, cleanedResult.Length - 3);
                }
                cleanedResult = cleanedResult.Trim();

                // Parser le JSON
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var analysis = JsonSerializer.Deserialize<SemanticAnalysis>(cleanedResult, options);

                if (analysis == null)
                {
                    return (false, null, "Impossible de parser l'analyse sémantique.");
                }

                return (true, analysis, string.Empty);
            }
            catch (JsonException ex)
            {
                return (false, null, $"Erreur de parsing JSON : {ex.Message}");
            }
            catch (Exception ex)
            {
                return (false, null, $"Erreur lors de l'analyse sémantique : {ex.Message}");
            }
        }

        /// <summary>
        /// Analyse la structure d'un template médical avec l'IA
        /// Identifie les sections logiques et leur contenu typique
        /// Cette analyse se fait sur le TEMPLATE (avec {{Variables}}) pour éviter les biais liés aux données spécifiques
        /// </summary>
        public async Task<(bool success, Dictionary<string, string> sections, string error)> 
            AnalyzeTemplateStructure(string templateContent)
        {
            if (string.IsNullOrWhiteSpace(templateContent))
            {
                return (false, null, "Le template est vide.");
            }

            try
            {
                var systemPrompt = @"Tu es un expert en analyse de documents médicaux. 
                
Analyse ce TEMPLATE de document médical (avec des {{Variables}}) et identifie sa STRUCTURE LOGIQUE.

Renvoie un JSON avec un objet ""sections"" contenant les grandes sections du document :

{
  ""sections"": {
    ""Nom_Section_1"": ""Description détaillée du rôle et du contenu typique de cette section"",
    ""Nom_Section_2"": ""Description détaillée du rôle et du contenu typique de cette section""
  }
}

INSTRUCTIONS IMPORTANTES :
1. Identifie les grandes sections logiques (Introduction, Contexte familial, Observations cliniques, Diagnostic, Recommandations, Suivi pédagogique, Conclusion, etc.)
2. Adapte les noms de sections au type de document analysé
3. Pour chaque section, fournis une description détaillée (2-3 phrases) expliquant :
   - Son rôle dans le document
   - Le type d'informations qu'elle contient
   - Son importance pour le lecteur
4. Ignore les {{Variables}} - concentre-toi sur la STRUCTURE et l'ORGANISATION
5. Liste les sections dans l'ordre où elles apparaissent dans le template

EXEMPLES de descriptions détaillées :
- ""Contexte familial"": ""Présente la situation familiale et l'environnement de l'enfant. Décrit la composition de la famille, les relations entre membres, et les facteurs contextuels importants. Cette section aide à comprendre le cadre de vie et les ressources disponibles.""
- ""Observations cliniques"": ""Détaille les observations faites lors des consultations. Inclut les comportements observés, les interactions sociales, les capacités cognitives et émotionnelles. Ces observations constituent la base factuelle du diagnostic.""

Réponds uniquement en JSON valide et complet, sans texte explicatif.";

                var userPrompt = $@"Analyse la structure de ce template médical et identifie ses sections logiques :

{templateContent}

Réponds en JSON avec l'objet ""sections"" contenant les sections identifiées et leur description détaillée.";

                string result;
                bool success;

                // Utiliser LLMGatewayService si disponible (anonymisation automatique)
                if (_llmGatewayService != null)
                {
                    var messages = new List<(string role, string content)>
                    {
                        ("user", userPrompt)
                    };

                    var (gatewaySuccess, gatewayResult, error) = await _llmGatewayService.ChatAsync(
                        systemPrompt,
                        messages,
                        null,  // Pas de nom de patient spécifique
                        maxTokens: 2000
                    );

                    success = gatewaySuccess;
                    result = gatewaySuccess ? gatewayResult : (error ?? "Erreur inconnue");
                }
                else
                {
                    // Fallback sur OpenAIService (sans anonymisation)
                    var (directSuccess, directResult) = await _openAIService.ChatAvecContexteAsync(
                        string.Empty,
                        userPrompt,
                        null,
                        systemPrompt
                    );
                    success = directSuccess;
                    result = directResult;
                }

                if (!success)
                {
                    return (false, null, result);
                }

                // Nettoyer la réponse
                var cleanedResult = result.Trim();
                if (cleanedResult.StartsWith("```json"))
                {
                    cleanedResult = cleanedResult.Substring(7);
                }
                if (cleanedResult.StartsWith("```"))
                {
                    cleanedResult = cleanedResult.Substring(3);
                }
                if (cleanedResult.EndsWith("```"))
                {
                    cleanedResult = cleanedResult.Substring(0, cleanedResult.Length - 3);
                }
                cleanedResult = cleanedResult.Trim();

                // Parser le JSON
                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                };

                var jsonDoc = JsonDocument.Parse(cleanedResult);
                var sections = new Dictionary<string, string>();
                
                if (jsonDoc.RootElement.TryGetProperty("sections", out var sectionsElement))
                {
                    foreach (var section in sectionsElement.EnumerateObject())
                    {
                        sections[section.Name] = section.Value.GetString() ?? "";
                    }
                }

                if (sections.Count == 0)
                {
                    return (false, null, "Aucune section détectée dans le template.");
                }

                return (true, sections, string.Empty);
            }
            catch (JsonException ex)
            {
                return (false, null, $"Erreur de parsing JSON : {ex.Message}");
            }
            catch (Exception ex)
            {
                return (false, null, $"Erreur lors de l'analyse structurelle : {ex.Message}");
            }
        }

        /// <summary>
        /// Génère un MCC complet à partir d'un exemple de document
        /// Combine l'extraction de template ET l'analyse sémantique
        /// </summary>
        public async Task<(bool success, MCCModel mcc, string error)>
            GenerateMCCFromExample(string exampleDocument)
        {
            if (string.IsNullOrWhiteSpace(exampleDocument))
            {
                return (false, null, "Le document exemple est vide.");
            }

            try
            {
                // PHASE 1 : ANALYSE SÉMANTIQUE (sur document original)
                // Identifie le ton, public cible, mots-clés, etc.
                var (semanticSuccess, semantic, semanticError) = 
                    await AnalyzeDocumentSemantic(exampleDocument);

                if (!semanticSuccess)
                {
                    return (false, null, $"Erreur Phase 1 (sémantique) : {semanticError}");
                }

                // PHASE 2 : EXTRACTION TEMPLATE (sur document original)
                // Extrait les variables et crée le template avec {{Variables}}
                var (templateSuccess, template, name, variables, templateError) = 
                    await ExtractTemplateFromExample(exampleDocument);

                if (!templateSuccess)
                {
                    return (false, null, $"Erreur Phase 2 (template) : {templateError}");
                }

                // PHASE 3 : ANALYSE STRUCTURELLE (sur le TEMPLATE créé)
                // Identifie les sections logiques sans biais des données spécifiques
                var (structureSuccess, sections, structureError) = 
                    await AnalyzeTemplateStructure(template);

                if (!structureSuccess)
                {
                    return (false, null, $"Erreur Phase 3 (structure) : {structureError}");
                }

                // FUSION : Ajouter les sections détectées dans l'analyse sémantique
                semantic.Sections = sections;

                // Créer le MCC complet en combinant les trois analyses
                var mcc = new MCCModel
                {
                    Id = GenerateMCCId(name, semantic),
                    Name = name,
                    Version = 1,
                    Created = DateTime.Now,
                    LastModified = DateTime.Now,
                    Semantic = semantic,
                    TemplateMarkdown = template,
                    PromptTemplate = GeneratePromptFromTemplate(template, semantic),
                    Keywords = semantic.ClinicalKeywords ?? new List<string>(),
                    Status = MCCStatus.Active,
                    UsageCount = 0,
                    AverageRating = 0.0,
                    TotalRatings = 0
                };

                return (true, mcc, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, null, $"Erreur lors de la génération MCC : {ex.Message}");
            }
        }

        /// <summary>
        /// Génère un ID unique pour un MCC basé sur son nom et sa sémantique
        /// </summary>
        private string GenerateMCCId(string name, SemanticAnalysis semantic)
        {
            // Format: doctype_audience_agegroup_randomid
            var docType = semantic?.DocType ?? "document";
            var audience = semantic?.Audience ?? "general";
            var ageGroup = semantic?.AgeGroup ?? "all";
            var randomPart = Guid.NewGuid().ToString("N").Substring(0, 8);

            var sanitizedDocType = SanitizeForId(docType);
            var sanitizedAudience = SanitizeForId(audience);
            var sanitizedAgeGroup = SanitizeForId(ageGroup);

            return $"{sanitizedDocType}_{sanitizedAudience}_{sanitizedAgeGroup}_{randomPart}";
        }

        /// <summary>
        /// Nettoie une chaîne pour l'utiliser dans un ID
        /// </summary>
        private string SanitizeForId(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "unknown";

            return Regex.Replace(input.ToLower(), @"[^a-z0-9]", "");
        }

        /// <summary>
        /// Génère un prompt optimisé à partir d'un template et de son analyse sémantique
        /// Ce prompt sera utilisé pour générer des documents similaires
        /// </summary>
        private string GeneratePromptFromTemplate(string template, SemanticAnalysis semantic)
        {
            var promptBuilder = new System.Text.StringBuilder();

            promptBuilder.AppendLine("CONTEXTE PATIENT (extraits récents)");
            promptBuilder.AppendLine("----");
            promptBuilder.AppendLine("{{Contexte}}");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("DEMANDE");
            promptBuilder.AppendLine("----");
            promptBuilder.AppendLine("{{User_Request}}");
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("STRUCTURE OBLIGATOIRE");
            promptBuilder.AppendLine("----");

            // Ajouter des informations sémantiques dans le prompt
            if (semantic != null)
            {
                promptBuilder.AppendLine($"Type de document : {semantic.DocType}");
                promptBuilder.AppendLine($"Public cible : {semantic.Audience}");
                promptBuilder.AppendLine($"Ton à adopter : {semantic.Tone}");
                promptBuilder.AppendLine();
            }

            promptBuilder.AppendLine("Template à suivre :");
            promptBuilder.AppendLine(template);
            promptBuilder.AppendLine();
            promptBuilder.AppendLine("CONTRAINTES DE STYLE");
            promptBuilder.AppendLine("----");
            promptBuilder.AppendLine("- Adapter le contenu au contexte patient fourni");
            promptBuilder.AppendLine("- Conserver la structure et le format du template");
            promptBuilder.AppendLine("- NE PAS inclure de date ni de signature");
            promptBuilder.AppendLine("- Être précis et éviter les redondances");

            return promptBuilder.ToString();
        }
    }
}
