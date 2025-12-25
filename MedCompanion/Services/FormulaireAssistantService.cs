using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    public class FormulaireAssistantService
    {
        private readonly LLMGatewayService _llmGatewayService;
        private readonly PromptConfigService _promptConfigService;
        private readonly PatientContextService _patientContextService;
        private readonly AnonymizationService _anonymizationService;
        private readonly LLM.LLMServiceFactory _llmFactory;
        private readonly AppSettings _appSettings;
        private readonly string _patientsBasePath;

        public FormulaireAssistantService(
            LLMGatewayService llmGatewayService,
            PromptConfigService promptConfigService,
            PatientContextService patientContextService,
            AnonymizationService anonymizationService,
            LLM.LLMServiceFactory llmFactory,
            AppSettings appSettings)
        {
            _llmGatewayService = llmGatewayService;
            _promptConfigService = promptConfigService;
            _patientContextService = patientContextService;
            _anonymizationService = anonymizationService;
            _llmFactory = llmFactory;
            _appSettings = appSettings;
            _patientsBasePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "MedCompanion",
                "patients"
            );
        }

 

        /// <summary>
        /// Génère du contenu personnalisé sur demande pour l'assistant PAI.
        /// Architecture : PatientContextService -> PromptConfigService -> AnonymizationService -> LLM
        /// </summary>
        public async Task<string> GenerateCustomContent(PatientMetadata patient, string instruction, string style, string length)
        {
            try
            {
                // 1. Charger TOUT le contexte patient via PatientContextService
                var nomComplet = $"{patient.Prenom} {patient.Nom}";
                var contextBundle = _patientContextService.GetCompleteContext(nomComplet);

                if (contextBundle?.Metadata == null)
                {
                    // Fallback si metadata null, mais on a déjà patient en entrée
                    contextBundle = new PatientContextBundle 
                    { 
                        Metadata = patient,
                        ClinicalContext = contextBundle?.ClinicalContext
                    };
                }

                // 2. Préparer les remplacements pour le template
                var replacements = new Dictionary<string, string>
                {
                    { "INSTRUCTION", instruction },
                    { "STYLE", style },
                    { "LENGTH", length },
                    { "CONTEXTE", contextBundle.ClinicalContext ?? "Aucun contexte clinique disponible" }
                };

                // 3. ✨ ARCHITECTURE CENTRALISÉE : Récupérer le prompt via PromptConfigService (SANS anonymisation ici)
                // On délègue l'anonymisation au LLMGatewayService
                var (populatedPrompt, _) = await _promptConfigService.GetAnonymizedPromptAsync(
                    "pai_generation_v2",
                    patient,
                    replacements,
                    skipAnonymization: true // ✅ Gateway s'en chargera
                );

                // 4. Appel LLM via Gateway
                var results = await _llmGatewayService.ChatAsync(
                    systemPrompt: _promptConfigService.GetActivePrompt("system_global"), 
                    messages: new List<(string role, string content)> 
                    { 
                        ("user", populatedPrompt) 
                    },
                    patientName: nomComplet,
                    maxTokens: 2000 // Augmenté pour éviter les troncatures
                );

                if (!results.success)
                {
                    return $"Erreur lors de la génération : {results.error}";
                }

                return results.result;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FormulaireAssistantService] Erreur GenerateCustomContent: {ex.Message}");
                return $"Une erreur est survenue : {ex.Message}";
            }
        }



      

        /// <summary>
        /// NOUVELLE MÉTHODE : Génère TOUTES les sections MDPH en une seule fois via un appel LLM unique
        /// Architecture : PatientContextService → PromptConfigService → AnonymizationService → LLM
        /// </summary>
        /// <param name="nomComplet">Nom complet du patient</param>
        /// <param name="demandes">Demandes cochées (AESH, AEEH, etc.)</param>
        /// <returns>Objet MDPHFormData avec toutes les sections remplies</returns>
        public async Task<MDPHFormData> GenerateCompleteFormAsync(string nomComplet, string demandes)
        {
            try
            {
                // 1. Charger TOUT le contexte patient via PatientContextService
                var contextBundle = _patientContextService.GetCompleteContext(nomComplet);

                if (contextBundle?.Metadata == null)
                {
                    throw new Exception("Impossible de charger les métadonnées du patient");
                }


                // 2. Créer le dictionnaire de remplacements pour les placeholders du template
                var replacements = new Dictionary<string, string>
                {
                    { "CONTEXTE", contextBundle.ClinicalContext ?? "Aucun contexte clinique disponible" },
                    { "DEMANDES", string.IsNullOrWhiteSpace(demandes) ? "Aucune demande spécifique" : demandes }
                };

                // 3. ✨ ARCHITECTURE CENTRALISÉE : Récupérer le prompt via PromptConfigService (SANS anonymisation ici)
                var (populatedPrompt, _) = await _promptConfigService.GetAnonymizedPromptAsync(
                    "mdph_complete_form",
                    contextBundle.Metadata,
                    replacements,
                    skipAnonymization: true // ✅ Gateway s'en chargera
                );

                // 4. Appel LLM via Gateway
                var results = await _llmGatewayService.ChatAsync(
                    systemPrompt: _promptConfigService.GetActivePrompt("system_global"), 
                    messages: new List<(string role, string content)> 
                    { 
                        ("user", populatedPrompt) 
                    },
                    patientName: nomComplet,
                    maxTokens: 4000 // Augmenté car le JSON MDPH est volumineux
                );
                
                System.Diagnostics.Debug.WriteLine($"[FormulaireAssistantService] MDPH Template utilisé (Populated length: {populatedPrompt.Length})");

                if (!results.success)
                {
                    throw new Exception($"Erreur lors de la génération : {results.error}");
                }

                var jsonResult = results.result;

                // 4. NETTOYER le JSON (le LLM peut retourner du texte avant/après ou wrapper le JSON)
                var cleanedJson = CleanJsonResponse(jsonResult);

                // 5. NORMALISER le JSON (convertir strings en arrays si nécessaire)
                var normalizedJson = NormalizeJsonArrayFields(cleanedJson);

                System.Diagnostics.Debug.WriteLine($"[FormulaireAssistantService] JSON normalisé (premiers 500 chars): {normalizedJson.Substring(0, Math.Min(500, normalizedJson.Length))}");

                // 6. Parser JSON
                MDPHFormData? formData;
                try
                {
                    formData = System.Text.Json.JsonSerializer.Deserialize<MDPHFormData>(normalizedJson);
                }
                catch (System.Text.Json.JsonException jsonEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[FormulaireAssistantService] Erreur parsing JSON: {jsonEx.Message}");
                    throw new Exception($"Erreur de parsing JSON: {jsonEx.Message}");
                }

                if (formData == null)
                {
                    throw new Exception("Le JSON parsé est null");
                }

                // 8. 🔧 POST-PROCESSING : Réécrire les remarques avec le LLM local
                // Problème : Le LLM cloud génère aléatoirement différentes formes (L'enfant, prénom réel, [PRENOM_PATIENT], pseudonyme)
                // Solution : Utiliser le LLM local (Ollama) pour réécrire de manière fluide avec le vrai prénom
                System.Diagnostics.Debug.WriteLine($"[FormulaireAssistantService] POST-PROCESSING : Réécriture des remarques (AVANT): {formData.RemarquesComplementaires?.Substring(0, Math.Min(100, formData.RemarquesComplementaires?.Length ?? 0))}...");

                formData.RemarquesComplementaires = await RewriteRemarquesWithLocalLLMAsync(
                    formData.RemarquesComplementaires,
                    contextBundle.Metadata
                );

                System.Diagnostics.Debug.WriteLine($"[FormulaireAssistantService] POST-PROCESSING : Réécriture des remarques (APRÈS): {formData.RemarquesComplementaires?.Substring(0, Math.Min(100, formData.RemarquesComplementaires?.Length ?? 0))}...");

                return formData;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FormulaireAssistantService] Erreur génération formulaire: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Nettoie la réponse JSON du LLM (enlève texte superflu, code blocks, guillemets)
        /// </summary>
        private string CleanJsonResponse(string rawResponse)
        {
            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                return "{}";
            }

            var cleaned = rawResponse.Trim();

            // 1. Enlever les code blocks markdown (```json ... ``` ou ``` ... ```)
            if (cleaned.StartsWith("```"))
            {
                // Trouver la fin du premier ```
                var firstNewline = cleaned.IndexOf('\n');
                if (firstNewline > 0)
                {
                    cleaned = cleaned.Substring(firstNewline + 1);
                }

                // Enlever le ``` final
                var lastTripleBacktick = cleaned.LastIndexOf("```");
                if (lastTripleBacktick > 0)
                {
                    cleaned = cleaned.Substring(0, lastTripleBacktick);
                }

                cleaned = cleaned.Trim();
            }

            // 2. Si la réponse est wrappée dans des guillemets (ex: "{ ... }")
            if (cleaned.StartsWith("\"") && cleaned.EndsWith("\"") && cleaned.Length > 2)
            {
                cleaned = cleaned.Substring(1, cleaned.Length - 2);
                // Unescaper les guillemets internes si nécessaire
                cleaned = cleaned.Replace("\\\"", "\"");
            }

            // 3. Extraire le JSON s'il y a du texte avant/après
            var firstBrace = cleaned.IndexOf('{');
            var lastBrace = cleaned.LastIndexOf('}');

            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                cleaned = cleaned.Substring(firstBrace, lastBrace - firstBrace + 1);
            }

            return cleaned.Trim();
        }

        /// <summary>
        /// Normalise le JSON pour s'assurer que les champs array sont bien des arrays
        /// (le LLM peut parfois retourner des strings au lieu de listes)
        /// </summary>
        private string NormalizeJsonArrayFields(string jsonString)
        {
            try
            {
                // Parser le JSON en JsonDocument pour le manipuler
                using var doc = System.Text.Json.JsonDocument.Parse(jsonString);
                using var stream = new MemoryStream();
                using var writer = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = false });

                // Champs qui doivent être des arrays
                var arrayFields = new HashSet<string>
                {
                    "elements_essentiels",
                    "antecedents_medicaux",
                    "retards_developpementaux",
                    "description_clinique"
                };

                var nestedArrayFields = new Dictionary<string, HashSet<string>>
                {
                    { "retentissements", new HashSet<string> { "cognition", "conduite_emotionnelle" } }
                };

                writer.WriteStartObject();

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    writer.WritePropertyName(prop.Name);

                    // Vérifier si c'est un champ qui doit être un array
                    if (arrayFields.Contains(prop.Name))
                    {
                        if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            // Convertir string en array avec un seul élément
                            writer.WriteStartArray();
                            writer.WriteStringValue(prop.Value.GetString());
                            writer.WriteEndArray();
                        }
                        else
                        {
                            prop.Value.WriteTo(writer);
                        }
                    }
                    // Vérifier si c'est un objet avec des nested arrays
                    else if (nestedArrayFields.ContainsKey(prop.Name) && prop.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
                    {
                        writer.WriteStartObject();
                        foreach (var nestedProp in prop.Value.EnumerateObject())
                        {
                            writer.WritePropertyName(nestedProp.Name);

                            if (nestedArrayFields[prop.Name].Contains(nestedProp.Name))
                            {
                                if (nestedProp.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                                {
                                    writer.WriteStartArray();
                                    writer.WriteStringValue(nestedProp.Value.GetString());
                                    writer.WriteEndArray();
                                }
                                else
                                {
                                    nestedProp.Value.WriteTo(writer);
                                }
                            }
                            else
                            {
                                nestedProp.Value.WriteTo(writer);
                            }
                        }
                        writer.WriteEndObject();
                    }
                    else
                    {
                        prop.Value.WriteTo(writer);
                    }
                }

                writer.WriteEndObject();
                writer.Flush();

                return Encoding.UTF8.GetString(stream.ToArray());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FormulaireAssistantService] Erreur normalisation JSON: {ex.Message}");
                return jsonString; // Retourner le JSON original en cas d'erreur
            }
        }

        /// <summary>
        /// Réécrire les remarques complémentaires avec le LLM local (Ollama) pour un texte plus fluide.
        /// Utilise le vrai prénom de l'enfant car le LLM local n'a pas besoin d'anonymisation.
        /// Le LLM garde le même contenu mais reformule de manière plus naturelle avec max 7 lignes.
        /// </summary>
        private async Task<string> RewriteRemarquesWithLocalLLMAsync(string? remarques, PatientMetadata metadata)
        {
            System.Diagnostics.Debug.WriteLine($"[FormulaireAssistantService] ========== DÉBUT RÉÉCRITURE REMARQUES ==========");
            System.Diagnostics.Debug.WriteLine($"[FormulaireAssistantService] Prénom patient: {metadata?.Prenom ?? "NON RENSEIGNÉ"}");

            if (string.IsNullOrWhiteSpace(remarques))
            {
                System.Diagnostics.Debug.WriteLine("[FormulaireAssistantService] ⚠️ Remarques vides, pas de réécriture");
                return remarques ?? string.Empty;
            }

            try
            {
                // ✅ Utiliser le MÊME modèle que celui configuré pour l'anonymisation
                var anonymizationModel = _appSettings.AnonymizationModel;
                System.Diagnostics.Debug.WriteLine($"[FormulaireAssistantService] Utilisation du modèle d'anonymisation: {anonymizationModel}");

                // Créer un provider Ollama avec le modèle d'anonymisation
                var ollamaProvider = new LLM.OllamaLLMProvider(
                    _appSettings.OllamaBaseUrl,
                    anonymizationModel
                );

                System.Diagnostics.Debug.WriteLine($"[FormulaireAssistantService] Vérification connexion Ollama ({anonymizationModel})...");

                // Vérifier la connexion
                var (isConnected, connectionMessage) = await ollamaProvider.CheckConnectionAsync();

                System.Diagnostics.Debug.WriteLine($"[FormulaireAssistantService] Connexion Ollama: {isConnected} - Message: {connectionMessage}");

                if (!isConnected)
                {
                    System.Diagnostics.Debug.WriteLine("[FormulaireAssistantService] ❌ Ollama non disponible, retour des remarques originales");
                    System.Diagnostics.Debug.WriteLine($"[FormulaireAssistantService] Détails: {connectionMessage}");
                    return remarques;
                }

                var llmProvider = ollamaProvider;

                // Créer le prompt de réécriture
                var prenom = metadata?.Prenom ?? "l'enfant";
                var prompt = $@"Tu es un rédacteur médical expert. Réécris le texte suivant de manière plus fluide et professionnelle.

RÈGLES STRICTES :
1. Utilise le prénom ""{prenom}"" au lieu de ""L'enfant"" ou autres formes
2. NE CHANGE PAS les informations médicales (garde les diagnostics, troubles, demandes exactement comme dans le texte original)
3. Rends le texte plus fluide et naturel
4. Maximum 7 lignes
5. Ton professionnel mais humain
6. Retourne UNIQUEMENT le texte réécrit, sans introduction ni explication

TEXTE ORIGINAL :
{remarques}

TEXTE RÉÉCRIT :";

                System.Diagnostics.Debug.WriteLine($"[FormulaireAssistantService] 📝 Réécriture des remarques avec LLM (prénom: {prenom})");
                System.Diagnostics.Debug.WriteLine($"[FormulaireAssistantService] Remarques originales ({remarques.Length} chars): {remarques.Substring(0, Math.Min(150, remarques.Length))}...");
                System.Diagnostics.Debug.WriteLine($"[FormulaireAssistantService] Prompt complet ({prompt.Length} chars)");

                // Appeler le LLM (max 500 tokens pour 7 lignes)
                System.Diagnostics.Debug.WriteLine($"[FormulaireAssistantService] Appel GenerateTextAsync (maxTokens: 500)...");
                var (success, result, error) = await llmProvider.GenerateTextAsync(prompt, maxTokens: 500);

                System.Diagnostics.Debug.WriteLine($"[FormulaireAssistantService] Résultat appel LLM:");
                System.Diagnostics.Debug.WriteLine($"  - Success: {success}");
                System.Diagnostics.Debug.WriteLine($"  - Result length: {result?.Length ?? 0}");
                System.Diagnostics.Debug.WriteLine($"  - Error: '{error ?? "NULL"}'");
                if (!string.IsNullOrWhiteSpace(result))
                {
                    System.Diagnostics.Debug.WriteLine($"  - Result preview: {result.Substring(0, Math.Min(200, result.Length))}...");
                }

                if (success && !string.IsNullOrWhiteSpace(result))
                {
                    var rewritten = result.Trim();
                    System.Diagnostics.Debug.WriteLine($"[FormulaireAssistantService] ✅ Réécriture réussie ({rewritten.Length} chars): {rewritten.Substring(0, Math.Min(150, rewritten.Length))}...");
                    System.Diagnostics.Debug.WriteLine($"[FormulaireAssistantService] ========== FIN RÉÉCRITURE (SUCCÈS) ==========");
                    return rewritten;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[FormulaireAssistantService] ❌ Échec réécriture: '{error ?? "PAS D'ERREUR MAIS SUCCESS=FALSE"}'");
                    System.Diagnostics.Debug.WriteLine($"[FormulaireAssistantService] ========== FIN RÉÉCRITURE (ÉCHEC) ==========");
                    return remarques; // Fallback vers original
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FormulaireAssistantService] ❌ Erreur réécriture remarques: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[FormulaireAssistantService] StackTrace: {ex.StackTrace}");
                System.Diagnostics.Debug.WriteLine($"[FormulaireAssistantService] ========== FIN RÉÉCRITURE (EXCEPTION) ==========");
                return remarques; // Fallback vers original
            }
        }


    }
}

