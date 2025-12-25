using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace MedCompanion.Services
{
    /// <summary>
    /// Résultat de l'analyse d'une demande de courrier
    /// </summary>
    public class LetterAnalysisResult
    {
        [JsonPropertyName("keywords")]
        public List<string> Keywords { get; set; } = new List<string>();
        
        [JsonPropertyName("doc_type")]
        public string DocType { get; set; } = "";
        
        [JsonPropertyName("audience")]
        public string Audience { get; set; } = "";
        
        [JsonPropertyName("tone")]
        public string Tone { get; set; } = "";
        
        [JsonPropertyName("age_group")]
        public string AgeGroup { get; set; } = "";
        
        [JsonPropertyName("confidence_score")]
        public double ConfidenceScore { get; set; } = 0.0;
    }

    /// <summary>
    /// Service d'assistance à la reformulation de prompts via IA
    /// </summary>
    public class PromptReformulationService
    {
        private readonly OpenAIService _openAIService;

        public PromptReformulationService(OpenAIService openAIService)
        {
            _openAIService = openAIService;
        }

        /// <summary>
        /// Reformule un prompt selon une demande utilisateur
        /// </summary>
        /// <param name="currentPrompt">Le prompt actuel à améliorer</param>
        /// <param name="userRequest">La demande de modification (ex: "Rends-le plus concis")</param>
        /// <returns>(success, reformulatedPrompt, error)</returns>
        public async Task<(bool success, string reformulatedPrompt, string? error)> ReformulatePromptAsync(
            string currentPrompt, 
            string userRequest)
        {
            if (string.IsNullOrWhiteSpace(currentPrompt))
                return (false, "", "Le prompt actuel est vide");

            if (string.IsNullOrWhiteSpace(userRequest))
                return (false, "", "Veuillez décrire votre demande de modification");

            try
            {
                var systemPrompt = @"Tu es un expert en prompt engineering médical.

Ton rôle : Reformuler des prompts pour un assistant IA médical selon les demandes de l'utilisateur.

RÈGLES IMPORTANTES :
1. Garde la structure générale du prompt
2. Conserve les placeholders {{Variables}} tels quels
3. Améliore selon la demande sans dénaturer l'intention originale
4. Reste dans un ton professionnel médical
5. Retourne UNIQUEMENT le prompt reformulé, sans commentaire ni explication";

                var userPrompt = $@"PROMPT ACTUEL
--------------
{currentPrompt}

DEMANDE DE MODIFICATION
-----------------------
{userRequest}

TÂCHE
-----
Reformule le prompt selon la demande. Retourne UNIQUEMENT le nouveau prompt, sans introduction ni conclusion.";

                var (success, result, error) = await _openAIService.GenerateTextAsync(
                    $"{systemPrompt}\n\n{userPrompt}",
                    maxTokens: 2000
                );

                if (!success)
                    return (false, "", error ?? "Erreur lors de la reformulation");

                return (true, result.Trim(), null);
            }
            catch (Exception ex)
            {
                return (false, "", $"Erreur inattendue: {ex.Message}");
            }
        }

        /// <summary>
        /// Analyse une demande de courrier utilisateur pour extraire les métadonnées
        /// Version enrichie avec contexte patient pour améliorer la précision
        /// </summary>
        public async Task<(bool success, LetterAnalysisResult result, string? error)> AnalyzeLetterRequestAsync(
            string userRequest,
            Models.PatientContext patientContext = null,
            string? pseudonym = null,
            AnonymizationContext? anonContext = null,
            string? explicitRecipient = null)
        {
            if (string.IsNullOrWhiteSpace(userRequest))
                return (false, null, "La demande est vide");

            try
            {
                // Construire le prompt système de base
                var systemPrompt = @"Tu es un expert en analyse sémantique de demandes médicales.

Ton rôle : Analyser une demande de courrier médical et extraire des métadonnées structurées EN FRANÇAIS.

IMPORTANT : Tu DOIS répondre UNIQUEMENT avec des valeurs EN FRANÇAIS comme spécifié ci-dessous.

TYPES DE DOCUMENTS (doc_type) - UTILISE CES VALEURS EXACTES :
- courrier_admin : Courriers administratifs généraux
- courrier_ecole : Courriers pour établissements scolaires (PAI, PAP, MDPH)
- courrier_confrere : Courriers d'adressage à un confrère
- certificat : Certificats médicaux
- compte_rendu : Comptes-rendus médicaux
- explication_ordonnance : Explications d'ordonnance

AUDIENCES (audience) :
- ecole : École/établissement scolaire
- medecin : Médecin spécialiste/confrère
- administration : Administration/institution (MDPH, CPAM, etc.)
- parents : Famille/parents du patient
- assurance : Assurance/mutuelle

TONS (tone) - UTILISE CES VALEURS EXACTES :
- formel : Professionnel formel
- clinique : Technique médical
- bienveillant : Accessible et empathique
- administratif : Administratif officiel

TRANCHES D'ÂGE (age_group) - UTILISE CES PLAGES NUMÉRIQUES EXACTES :
- 0-3 : Petite enfance (0 à 3 ans)
- 3-6 : Maternelle (3 à 6 ans)
- 6-11 : Primaire (6 à 11 ans)
- 12-15 : Collège (12 à 15 ans)
- 16+ : Lycée et plus (16 ans et plus)

INSTRUCTIONS :
1. Extrais les mots-clés médicaux/cliniques principaux de la demande
2. Déduis le doc_type EN FRANÇAIS (ex: courrier_ecole, courrier_confrere)
3. Identifie l'audience EN FRANÇAIS (ex: ecole, medecin, parents)
4. Détermine le ton EN FRANÇAIS (ex: bienveillant, clinique, administratif)
5. Si l'âge du patient est connu, convertis-le en plage numérique (ex: 7 ans → 6-11)
6. Calcule un score de confiance (0-100)

Réponds UNIQUEMENT avec ce JSON (sans markdown, sans backticks) :
{
  ""keywords"": [""mot1"", ""mot2""],
  ""doc_type"": ""courrier_ecole"",
  ""audience"": ""ecole"",
  ""tone"": ""bienveillant"",
  ""age_group"": ""6-11"",
  ""confidence_score"": 85
}";

                // Injection du destinataire explicite si fourni
                if (!string.IsNullOrWhiteSpace(explicitRecipient))
                {
                    systemPrompt += $"\n\n🚨 CONSIGNE PRIORITAIRE : L'utilisateur a spécifié le destinataire suivant : \"{explicitRecipient}\".\n" +
                                   $"Tu DOIS utiliser cette valeur pour le champ 'audience', en corrigeant les éventuelles fautes d'orthographe (ex: 'psycologue' -> 'psychologue').\n" +
                                   $"IGNORE les catégories standards pour l'audience.";
                }

                // Construire le prompt avec contexte patient si disponible
                var userPrompt = new System.Text.StringBuilder();
                userPrompt.AppendLine($"Demande utilisateur : {userRequest}");

                if (patientContext != null)
                {
                    userPrompt.AppendLine();
                    userPrompt.AppendLine("CONTEXTE PATIENT :");
                    userPrompt.AppendLine(patientContext.ToPromptText(pseudonym, anonContext));  // ✅ Passer les paramètres d'anonymisation
                    userPrompt.AppendLine();
                    userPrompt.AppendLine("IMPORTANT : Utilise ce contexte patient pour :");
                    userPrompt.AppendLine("1. Extraire des mots-clés plus précis (diagnostics, troubles mentionnés)");
                    userPrompt.AppendLine("2. Déduire la tranche d'âge (age_group) à partir de l'âge réel");
                    userPrompt.AppendLine("3. Identifier l'audience et le ton appropriés selon le contexte");
                }

                var (success, result, error) = await _openAIService.GenerateTextAsync(
                    $"{systemPrompt}\n\n{userPrompt.ToString()}",
                    maxTokens: 500
                );

                if (!success)
                    return (false, null, error ?? "Erreur lors de l'analyse");

                // Parser le JSON retourné
                try
                {
                    var cleanJson = result.Trim()
                        .Replace("```json", "")
                        .Replace("```", "")
                        .Trim();

                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    };

                    var analysisResult = JsonSerializer.Deserialize<LetterAnalysisResult>(cleanJson, options);
                    
                    if (analysisResult == null)
                        return (false, null, "Format de réponse invalide");

                    return (true, analysisResult, null);
                }
                catch (JsonException ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PromptReformulation] Erreur parsing JSON: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[PromptReformulation] Réponse IA: {result}");
                    
                    // Fallback: extraction manuelle des mots-clés
                    var fallbackResult = new LetterAnalysisResult
                    {
                        Keywords = ExtractKeywordsManually(userRequest),
                        DocType = "administrative_letter",
                        Audience = "administration",
                        Tone = "formal",
                        AgeGroup = "all",
                        ConfidenceScore = 50
                    };
                    
                    return (true, fallbackResult, "Analyse partielle (format IA invalide)");
                }
            }
            catch (Exception ex)
            {
                return (false, null, $"Erreur inattendue: {ex.Message}");
            }
        }

        /// <summary>
        /// Extraction manuelle de mots-clés (fallback)
        /// </summary>
        private List<string> ExtractKeywordsManually(string text)
        {
            var keywords = new List<string>();
            var lowerText = text.ToLower();

            // Détecter mots-clés courants
            var commonKeywords = new Dictionary<string, string[]>
            {
                ["pai"] = new[] { "pai", "projet d'accueil", "allergie", "chronique" },
                ["pap"] = new[] { "pap", "parcours personnalisé", "trouble", "attention", "dys" },
                ["mdph"] = new[] { "mdph", "handicap", "aah", "reconnaissance" },
                ["certificat"] = new[] { "certificat", "aptitude", "sport", "absence" },
                ["courrier"] = new[] { "courrier", "lettre", "adressage" }
            };

            foreach (var (key, terms) in commonKeywords)
            {
                if (terms.Any(term => lowerText.Contains(term)))
                {
                    keywords.Add(key);
                }
            }

            return keywords.Any() ? keywords : new List<string> { "courrier" };
        }
    }
}
