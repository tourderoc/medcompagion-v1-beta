using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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
                        ClinicalContext = contextBundle?.ClinicalContext ?? string.Empty
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

                // 4. NETTOYER et RÉPARER le JSON (corrige accolades/crochets inversés, clés au singulier, virgules, etc.)
                var cleanedJson = CleanJsonResponse(jsonResult);
                var repairedJson = RepairJson(cleanedJson);

                // 5. NORMALISER le JSON (harmonise arrays/strings et unwraps si nécessaire)
                var normalizedJson = NormalizeJsonArrayFields(repairedJson);

                System.Diagnostics.Debug.WriteLine($"[FormulaireAssistantService] JSON normalisé (premiers 500 chars): {normalizedJson.Substring(0, Math.Min(500, normalizedJson.Length))}");

                // 6. Parser JSON avec options tolérantes
                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true,
                    ReadCommentHandling = JsonCommentHandling.Skip
                };

                MDPHFormData? formData = null;
                try
                {
                    formData = System.Text.Json.JsonSerializer.Deserialize<MDPHFormData>(normalizedJson, jsonOptions);
                }
                catch (System.Text.Json.JsonException jsonEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[FormulaireAssistantService] Erreur parsing JSON: {jsonEx.Message}. Utilisation du fallback...");
                }

                // 7. Si le parsing a échoué ou que les données sont vides, extraction de secours par Regex
                if (formData == null || formData.IsEmpty())
                {
                    System.Diagnostics.Debug.WriteLine("[FormulaireAssistantService] Extraction de secours (Fallback Regex) activée");
                    formData = FallbackExtractFormData(cleanedJson);
                }

                formData.EnsureNotNull();

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
        /// Répare les erreurs de syntaxe JSON couramment produites par les LLM locaux :
        /// - Délimiteurs croisés ({ ... ] au lieu de { ... })
        /// - Clés au singulier (retentissement -> retentissements, traitement -> traitements)
        /// - Tableaux ouverts contenant des paires clé:valeur ({ au lieu de [)
        /// - Virgules trainantes avant } ou ]
        /// - Retours à la ligne littéraux dans les chaînes
        /// - Conteneurs non fermés en cas de troncature
        /// </summary>
        private static string RepairJson(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson)) return "{}";

            var json = rawJson.Trim();

            // 1. Normaliser les noms de propriétés courants (singulier -> pluriel)
            json = Regex.Replace(json, @"\""retentissement\""\s*:", "\"retentissements\":", RegexOptions.IgnoreCase);
            json = Regex.Replace(json, @"\""traitement\""\s*:", "\"traitements\":", RegexOptions.IgnoreCase);
            json = Regex.Replace(json, @"\""element_essentiel\""\s*:", "\"elements_essentiels\":", RegexOptions.IgnoreCase);
            json = Regex.Replace(json, @"\""(antecedent_medical|antecedents)\""\s*:", "\"antecedents_medicaux\":", RegexOptions.IgnoreCase);
            json = Regex.Replace(json, @"\""(retard_developpemental|retards_developpement)\""\s*:", "\"retards_developpementaux\":", RegexOptions.IgnoreCase);
            json = Regex.Replace(json, @"\""(remarques|remarque_complementaire)\""\s*:", "\"remarques_complementaires\":", RegexOptions.IgnoreCase);
            json = Regex.Replace(json, @"\""autre_pathologie\""\s*:", "\"autres_pathologies\":", RegexOptions.IgnoreCase);

            // 2. Si retentissements ou traitements a été ouvert avec [ mais contient des propriétés "clé":
            // Remplacer [ par {
            json = Regex.Replace(json, @"(\""retentissements\""\s*:\s*)\[(\s*\""[a-zA-Z0-9_]+\""\s*:)", "$1{$2", RegexOptions.IgnoreCase);
            json = Regex.Replace(json, @"(\""traitements\""\s*:\s*)\[(\s*\""[a-zA-Z0-9_]+\""\s*:)", "$1{$2", RegexOptions.IgnoreCase);

            // 3. Supprimer les virgules traînantes avant } ou ]
            json = Regex.Replace(json, @",\s*([\]}])", "$1");

            // 4. Équilibrer les accolades et crochets, échapper les sauts de ligne littéraux
            json = RepairJsonBracesAndBrackets(json);

            // 5. Supprimer à nouveau les éventuelles virgules traînantes résiduelles
            json = Regex.Replace(json, @",\s*([\]}])", "$1");

            return json;
        }

        private static string RepairJsonBracesAndBrackets(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return "{}";

            var sb = new StringBuilder(json.Length + 64);
            var stack = new Stack<char>();
            bool inString = false;
            bool isEscaped = false;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];

                if (inString)
                {
                    if (isEscaped)
                    {
                        isEscaped = false;
                        sb.Append(c);
                    }
                    else if (c == '\\')
                    {
                        isEscaped = true;
                        sb.Append(c);
                    }
                    else if (c == '"')
                    {
                        inString = false;
                        sb.Append(c);
                    }
                    else if (c == '\r')
                    {
                        // Ignorer \r littéral dans une string JSON
                    }
                    else if (c == '\n')
                    {
                        // Remplacer saut de ligne littéral par \n échappé
                        sb.Append("\\n");
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
                else
                {
                    if (c == '"')
                    {
                        inString = true;
                        sb.Append(c);
                    }
                    else if (c == '{')
                    {
                        stack.Push('{');
                        sb.Append(c);
                    }
                    else if (c == '[')
                    {
                        stack.Push('[');
                        sb.Append(c);
                    }
                    else if (c == '}')
                    {
                        if (stack.Count > 0)
                        {
                            char open = stack.Peek();
                            if (open == '{')
                            {
                                stack.Pop();
                                sb.Append('}');
                            }
                            else if (open == '[')
                            {
                                // Ouvert avec [ mais fermé avec } -> corriger en ]
                                stack.Pop();
                                sb.Append(']');
                            }
                        }
                    }
                    else if (c == ']')
                    {
                        if (stack.Count > 0)
                        {
                            char open = stack.Peek();
                            if (open == '[')
                            {
                                stack.Pop();
                                sb.Append(']');
                            }
                            else if (open == '{')
                            {
                                // Ouvert avec { mais fermé avec ] (ex: Path: $.retentissement) -> corriger en }
                                stack.Pop();
                                sb.Append('}');
                            }
                        }
                    }
                    else
                    {
                        sb.Append(c);
                    }
                }
            }

            // Si la chaîne était restée ouverte
            if (inString)
            {
                sb.Append('"');
            }

            // Fermer les conteneurs restés ouverts (ex: troncature LLM)
            while (stack.Count > 0)
            {
                char open = stack.Pop();
                sb.Append(open == '{' ? '}' : ']');
            }

            return sb.ToString();
        }

        /// <summary>
        /// Normalise le JSON pour s'assurer que les types correspondent à MDPHFormData :
        /// - Tableaux racines : convertit strings en tableaux de lignes si nécessaire
        /// - Strings racines : convertit tableaux en texte multiligne
        /// - Retentissements & Traitements : unwraps si dans un tableau et normalise sous-champs
        /// </summary>
        private string NormalizeJsonArrayFields(string jsonString)
        {
            try
            {
                var docOptions = new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                };

                using var doc = System.Text.Json.JsonDocument.Parse(jsonString, docOptions);
                using var stream = new MemoryStream();
                using var writer = new System.Text.Json.Utf8JsonWriter(stream, new System.Text.Json.JsonWriterOptions { Indented = false });

                var arrayFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "elements_essentiels",
                    "antecedents_medicaux",
                    "retards_developpementaux",
                    "description_clinique"
                };

                var stringFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "pathologie_principale",
                    "autres_pathologies",
                    "remarques_complementaires"
                };

                writer.WriteStartObject();

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var propName = prop.Name;
                    if (string.Equals(propName, "retentissement", StringComparison.OrdinalIgnoreCase))
                        propName = "retentissements";
                    else if (string.Equals(propName, "traitement", StringComparison.OrdinalIgnoreCase))
                        propName = "traitements";

                    writer.WritePropertyName(propName);

                    // 1. Champs racines qui doivent être des tableaux
                    if (arrayFields.Contains(propName))
                    {
                        if (prop.Value.ValueKind == JsonValueKind.String)
                        {
                            var s = prop.Value.GetString() ?? string.Empty;
                            var lines = s.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                         .Select(l => l.Trim().TrimStart('-', '•', '*').Trim())
                                         .Where(l => !string.IsNullOrWhiteSpace(l))
                                         .ToList();
                            writer.WriteStartArray();
                            if (lines.Count > 0)
                            {
                                foreach (var line in lines) writer.WriteStringValue(line);
                            }
                            else if (!string.IsNullOrWhiteSpace(s))
                            {
                                writer.WriteStringValue(s);
                            }
                            writer.WriteEndArray();
                        }
                        else
                        {
                            prop.Value.WriteTo(writer);
                        }
                    }
                    // 2. Champs racines qui doivent être des strings
                    else if (stringFields.Contains(propName))
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Array)
                        {
                            var list = prop.Value.EnumerateArray()
                                .Select(x => x.ToString())
                                .Where(s => !string.IsNullOrWhiteSpace(s));
                            writer.WriteStringValue(string.Join("\n", list));
                        }
                        else
                        {
                            prop.Value.WriteTo(writer);
                        }
                    }
                    // 3. Retentissements (doit être un objet)
                    else if (string.Equals(propName, "retentissements", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteNormalizedRetentissements(writer, prop.Value);
                    }
                    // 4. Traitements (doit être un objet)
                    else if (string.Equals(propName, "traitements", StringComparison.OrdinalIgnoreCase))
                    {
                        WriteNormalizedTraitements(writer, prop.Value);
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
                return jsonString;
            }
        }

        private static void WriteNormalizedRetentissements(Utf8JsonWriter writer, JsonElement value)
        {
            JsonElement targetObj = value;
            if (value.ValueKind == JsonValueKind.Array)
            {
                var firstObj = value.EnumerateArray().FirstOrDefault(x => x.ValueKind == JsonValueKind.Object);
                if (firstObj.ValueKind == JsonValueKind.Object)
                    targetObj = firstObj;
                else
                {
                    writer.WriteStartObject();
                    writer.WriteEndObject();
                    return;
                }
            }

            if (targetObj.ValueKind != JsonValueKind.Object)
            {
                writer.WriteStartObject();
                writer.WriteEndObject();
                return;
            }

            var arrayProps = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "cognition", "conduite_emotionnelle" };

            writer.WriteStartObject();
            foreach (var prop in targetObj.EnumerateObject())
            {
                writer.WritePropertyName(prop.Name);
                if (arrayProps.Contains(prop.Name))
                {
                    if (prop.Value.ValueKind == JsonValueKind.String)
                    {
                        var s = prop.Value.GetString() ?? string.Empty;
                        var lines = s.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                     .Select(l => l.Trim().TrimStart('-', '•', '*').Trim())
                                     .Where(l => !string.IsNullOrWhiteSpace(l))
                                     .ToList();
                        writer.WriteStartArray();
                        if (lines.Count > 0)
                        {
                            foreach (var line in lines) writer.WriteStringValue(line);
                        }
                        else if (!string.IsNullOrWhiteSpace(s))
                        {
                            writer.WriteStringValue(s);
                        }
                        writer.WriteEndArray();
                    }
                    else
                    {
                        prop.Value.WriteTo(writer);
                    }
                }
                else
                {
                    if (prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        var list = prop.Value.EnumerateArray().Select(x => x.ToString()).Where(s => !string.IsNullOrWhiteSpace(s));
                        writer.WriteStringValue(string.Join("\n", list));
                    }
                    else
                    {
                        prop.Value.WriteTo(writer);
                    }
                }
            }
            writer.WriteEndObject();
        }

        private static void WriteNormalizedTraitements(Utf8JsonWriter writer, JsonElement value)
        {
            JsonElement targetObj = value;
            if (value.ValueKind == JsonValueKind.Array)
            {
                var firstObj = value.EnumerateArray().FirstOrDefault(x => x.ValueKind == JsonValueKind.Object);
                if (firstObj.ValueKind == JsonValueKind.Object)
                    targetObj = firstObj;
                else
                {
                    writer.WriteStartObject();
                    writer.WriteEndObject();
                    return;
                }
            }

            if (targetObj.ValueKind != JsonValueKind.Object)
            {
                writer.WriteStartObject();
                writer.WriteEndObject();
                return;
            }

            writer.WriteStartObject();
            foreach (var prop in targetObj.EnumerateObject())
            {
                writer.WritePropertyName(prop.Name);
                if (prop.Value.ValueKind == JsonValueKind.Array)
                {
                    var list = prop.Value.EnumerateArray().Select(x => x.ToString()).Where(s => !string.IsNullOrWhiteSpace(s));
                    writer.WriteStringValue(string.Join("\n", list));
                }
                else
                {
                    prop.Value.WriteTo(writer);
                }
            }
            writer.WriteEndObject();
        }

        /// <summary>
        /// Extraction de secours basée sur Regex si le parsing JSON échoue complètement.
        /// Permet de ne jamais bloquer l'utilisateur avec une erreur rouge.
        /// </summary>
        private static MDPHFormData FallbackExtractFormData(string text)
        {
            var data = new MDPHFormData();
            if (string.IsNullOrWhiteSpace(text)) return data;

            data.PathologiePrincipale = ExtractJsonString(text, "pathologie_principale", "pathologie");
            data.AutresPathologies = ExtractJsonString(text, "autres_pathologies", "autre_pathologie");
            data.RemarquesComplementaires = ExtractJsonString(text, "remarques_complementaires", "remarques", "remarque_complementaire");

            data.ElementsEssentiels = ExtractJsonArrayOrList(text, "elements_essentiels", "element_essentiel");
            data.AntecedentsMedicaux = ExtractJsonArrayOrList(text, "antecedents_medicaux", "antecedents", "antecedent_medical");
            data.RetardsDeveloppementaux = ExtractJsonArrayOrList(text, "retards_developpementaux", "retards_developpement", "retard_developpemental");
            data.DescriptionClinique = ExtractJsonArrayOrList(text, "description_clinique");

            data.Traitements = new TraitementsData
            {
                Medicaments = ExtractJsonString(text, "medicaments", "medicament"),
                EffetsIndesirables = ExtractJsonString(text, "effets_indesirables", "effets", "effet_indesirable"),
                AutresPrisesEnCharge = ExtractJsonString(text, "autres_prises_en_charge", "prises_en_charge")
            };

            data.Retentissements = new RetentissementsData
            {
                Mobilite = ExtractJsonString(text, "mobilite", "mobilité"),
                Communication = ExtractJsonString(text, "communication"),
                Cognition = ExtractJsonArrayOrList(text, "cognition", "cognitif"),
                ConduiteEmotionnelle = ExtractJsonArrayOrList(text, "conduite_emotionnelle", "conduite", "emotion", "emotions"),
                Autonomie = ExtractJsonString(text, "autonomie"),
                VieQuotidienne = ExtractJsonString(text, "vie_quotidienne", "quotidien"),
                SocialScolaire = ExtractJsonString(text, "social_scolaire", "scolaire", "social")
            };

            data.EnsureNotNull();
            return data;
        }

        private static string ExtractJsonString(string text, params string[] propertyNames)
        {
            foreach (var name in propertyNames)
            {
                var match = Regex.Match(text, $@"""{name}""\s*:\s*""((?:\\.|[^""\\])*)""", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    return UnescapeJsonString(match.Groups[1].Value);
                }

                var arrayMatch = Regex.Match(text, $@"""{name}""\s*:\s*\[([^\]]*)\]", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (arrayMatch.Success)
                {
                    var items = Regex.Matches(arrayMatch.Groups[1].Value, @"""((?:\\.|[^""\\])*)""")
                                     .Cast<Match>()
                                     .Select(m => UnescapeJsonString(m.Groups[1].Value))
                                     .Where(s => !string.IsNullOrWhiteSpace(s));
                    var joined = string.Join("\n", items);
                    if (!string.IsNullOrWhiteSpace(joined)) return joined;
                }
            }
            return string.Empty;
        }

        private static List<string> ExtractJsonArrayOrList(string text, params string[] propertyNames)
        {
            var result = new List<string>();
            foreach (var name in propertyNames)
            {
                var match = Regex.Match(text, $@"""{name}""\s*:\s*\[([^\]]*)\]", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    var items = Regex.Matches(match.Groups[1].Value, @"""((?:\\.|[^""\\])*)""")
                                     .Cast<Match>()
                                     .Select(m => UnescapeJsonString(m.Groups[1].Value))
                                     .Where(s => !string.IsNullOrWhiteSpace(s))
                                     .ToList();
                    if (items.Count > 0) return items;
                }

                var stringMatch = Regex.Match(text, $@"""{name}""\s*:\s*""((?:\\.|[^""\\])*)""", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (stringMatch.Success)
                {
                    var val = UnescapeJsonString(stringMatch.Groups[1].Value);
                    if (!string.IsNullOrWhiteSpace(val))
                    {
                        var lines = val.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                       .Select(l => l.Trim().TrimStart('-', '•', '*').Trim())
                                       .Where(l => !string.IsNullOrWhiteSpace(l))
                                       .ToList();
                        return lines.Count > 0 ? lines : new List<string> { val };
                    }
                }
            }
            return result;
        }

        private static string UnescapeJsonString(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\\"", "\"")
                    .Replace("\\\\", "\\")
                    .Replace("\\n", "\n")
                    .Replace("\\r", "\r")
                    .Replace("\\t", "\t");
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

