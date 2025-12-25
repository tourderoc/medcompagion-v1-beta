using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.Dialogs;

namespace MedCompanion.Services;

/// <summary>
/// Service universel de réadaptation de courriers
/// Détecte les variables manquantes, recherche dans le contexte patient, et réadapte avec l'IA
/// </summary>
public class LetterReAdaptationService
{
    private readonly PatientContextService _patientContextService;
    private readonly OpenAIService _openAIService;
    private readonly AnonymizationService _anonymizationService;

    public LetterReAdaptationService(
        PatientContextService patientContextService,
        OpenAIService openAIService,
        AnonymizationService anonymizationService)
    {
        _patientContextService = patientContextService ?? throw new ArgumentNullException(nameof(patientContextService));
        _openAIService = openAIService ?? throw new ArgumentNullException(nameof(openAIService));
        _anonymizationService = anonymizationService ?? throw new ArgumentNullException(nameof(anonymizationService));  // ✅ Injection de dépendance
    }
    
    /// <summary>
    /// Réadapte un courrier en détectant et remplissant les variables manquantes
    /// </summary>
    /// <param name="markdown">Courrier à réadapter</param>
    /// <param name="patientName">Nom complet du patient</param>
    /// <param name="documentTitle">Titre du document (optionnel)</param>
    /// <param name="userRequest">Demande utilisateur (optionnel)</param>
    /// <returns>Résultat de la réadaptation</returns>
    public async Task<ReAdaptationResult> ReAdaptLetterAsync(
        string markdown,
        string patientName,
        string? documentTitle = null,
        string? userRequest = null)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"[ReAdaptationService] Début réadaptation pour {patientName}");
            
            // Étape 1 : Détecter les variables
            var detectedVariables = DetectVariables(markdown);
            System.Diagnostics.Debug.WriteLine($"[ReAdaptationService] Variables détectées : {detectedVariables.Count}");
            
            if (detectedVariables.Count == 0)
            {
                // Pas de variables → Retourner le courrier tel quel
                return new ReAdaptationResult
                {
                    Success = true,
                    DetectedVariables = detectedVariables,
                    NeedsMissingInfo = false,
                    ReAdaptedMarkdown = markdown
                };
            }
            
            // Étape 2 : Charger le contexte patient et générer l'anonymisation
            var patientContext = _patientContextService.GetCompleteContext(patientName, userRequest);
            System.Diagnostics.Debug.WriteLine($"[ReAdaptationService] {patientContext.ToDebugText()}");

            // ✅ Générer le pseudonyme pour anonymisation
            var sexe = patientContext.Metadata?.Sexe ?? "M";
            var (nomAnonymise, anonContext) = _anonymizationService.Anonymize("", patientName, sexe);

            // Étape 3 : Rechercher les variables dans le contexte patient (métadonnées)
            var (availableInfo, missingFields) = SearchInPatientContext(detectedVariables, patientContext, documentTitle);
            System.Diagnostics.Debug.WriteLine($"[ReAdaptationService] Infos disponibles (métadonnées) : {availableInfo.Count}, Manquantes : {missingFields.Count}");

            // Étape 3.5 : Extraire les variables manquantes depuis le contexte clinique avec IA
            if (missingFields.Count > 0 && !string.IsNullOrEmpty(patientContext.ClinicalContext))
            {
                System.Diagnostics.Debug.WriteLine($"[ReAdaptationService] Tentative d'extraction IA pour {missingFields.Count} variables...");

                var missingVariableNames = missingFields.Select(f => f.FieldName).ToList();
                var extractedFromClinical = await ExtractFromClinicalContextAsync(missingVariableNames, patientContext);

                // Fusionner les infos extraites
                foreach (var kvp in extractedFromClinical)
                {
                    availableInfo[kvp.Key] = kvp.Value;
                    System.Diagnostics.Debug.WriteLine($"  ✅ Ajouté depuis contexte clinique: {kvp.Key}");
                }

                // Retirer les champs qui ont été trouvés par l'IA
                missingFields = missingFields
                    .Where(f => !extractedFromClinical.ContainsKey(f.FieldName))
                    .ToList();

                System.Diagnostics.Debug.WriteLine($"[ReAdaptationService] Après extraction IA - Infos totales : {availableInfo.Count}, Manquantes : {missingFields.Count}");
            }

            if (missingFields.Count > 0)
            {
                // Des infos manquent encore → Retourner pour collecte via dialogue
                System.Diagnostics.Debug.WriteLine($"[ReAdaptationService] {missingFields.Count} infos restent à collecter via dialogue");
                return new ReAdaptationResult
                {
                    Success = true,
                    DetectedVariables = detectedVariables,
                    AvailableInfo = availableInfo,
                    MissingFields = missingFields,
                    NeedsMissingInfo = true,
                    State = new ReAdaptationState
                    {
                        OriginalMarkdown = markdown,
                        PatientName = patientName,
                        DocumentTitle = documentTitle,
                        UserRequest = userRequest,
                        PatientContext = patientContext,
                        Pseudonym = nomAnonymise,  // ✅ Sauvegarder pour réutilisation
                        AnonContext = anonContext   // ✅ Sauvegarder pour réutilisation
                    }
                };
            }

            // Étape 4 : Réadapter avec toutes les infos disponibles (✅ avec anonymisation)
            var reAdaptedMarkdown = await ReAdaptWithInfoAsync(markdown, availableInfo, patientContext, nomAnonymise, anonContext);
            
            return new ReAdaptationResult
            {
                Success = true,
                DetectedVariables = detectedVariables,
                AvailableInfo = availableInfo,
                MissingFields = missingFields,
                NeedsMissingInfo = false,
                ReAdaptedMarkdown = reAdaptedMarkdown
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ReAdaptationService] Erreur : {ex.Message}");
            return new ReAdaptationResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }
    
    /// <summary>
    /// Complète la réadaptation après collecte des informations manquantes
    /// </summary>
    public async Task<ReAdaptationResult> CompleteReAdaptationAsync(
        ReAdaptationResult previousResult,
        Dictionary<string, string> collectedInfo)
    {
        try
        {
            if (previousResult.State == null)
            {
                throw new InvalidOperationException("L'état précédent est manquant");
            }
            
            System.Diagnostics.Debug.WriteLine($"[ReAdaptationService] Complétion avec {collectedInfo.Count} infos collectées");
            
            // Fusionner les infos disponibles + collectées
            var allInfo = new Dictionary<string, string>(previousResult.AvailableInfo);
            foreach (var kvp in collectedInfo)
            {
                allInfo[kvp.Key] = kvp.Value;
            }

            // Réadapter avec toutes les infos (✅ avec anonymisation)
            var reAdaptedMarkdown = await ReAdaptWithInfoAsync(
                previousResult.State.OriginalMarkdown,
                allInfo,
                previousResult.State.PatientContext!,
                previousResult.State.Pseudonym,      // ✅ Utiliser le pseudonyme sauvegardé
                previousResult.State.AnonContext     // ✅ Utiliser le contexte d'anonymisation sauvegardé
            );
            
            return new ReAdaptationResult
            {
                Success = true,
                DetectedVariables = previousResult.DetectedVariables,
                AvailableInfo = allInfo,
                MissingFields = new List<MissingFieldInfo>(),
                NeedsMissingInfo = false,
                ReAdaptedMarkdown = reAdaptedMarkdown
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ReAdaptationService] Erreur complétion : {ex.Message}");
            return new ReAdaptationResult
            {
                Success = false,
                Error = ex.Message
            };
        }
    }
    
    /// <summary>
    /// Étape 1 : Détecte toutes les variables {{Variable}} dans le texte
    /// </summary>
    private List<string> DetectVariables(string markdown)
    {
        var variables = new List<string>();
        var regex = new Regex(@"\{\{([^}]+)\}\}", RegexOptions.Compiled);
        var matches = regex.Matches(markdown);

        foreach (Match match in matches)
        {
            var variableName = match.Groups[1].Value.Trim();
            if (!variables.Contains(variableName))
            {
                variables.Add(variableName);
            }
        }

        return variables;
    }
    
    /// <summary>
    /// Étape 2 : Recherche les variables dans le contexte patient
    /// </summary>
    private (Dictionary<string, string> availableInfo, List<MissingFieldInfo> missingFields) 
        SearchInPatientContext(
            List<string> variables, 
            PatientContextBundle context,
            string? documentTitle)
    {
        var availableInfo = new Dictionary<string, string>();
        var missingFields = new List<MissingFieldInfo>();
        var metadata = context.Metadata;
        
        // Mapping des variables vers les métadonnées patient (case-insensitive)
        var variableMapping = new Dictionary<string, Func<PatientMetadata?, string?>>(StringComparer.OrdinalIgnoreCase)
        {
            // Nom et identité
            ["Nom_Patient"] = m => m?.NomComplet,
            ["NomPatient"] = m => m?.NomComplet,
            ["NOM Prénom"] = m => m?.NomComplet,
            ["NOM Prenom"] = m => m?.NomComplet,
            ["Nom Prénom"] = m => m?.NomComplet,
            ["Nom Prenom"] = m => m?.NomComplet,
            ["Nom"] = m => m?.Nom,
            ["Prenom"] = m => m?.Prenom,
            ["Prénom"] = m => m?.Prenom,
            ["Nom_Prenom"] = m => m?.NomComplet,

            // Âge et date de naissance
            ["Age"] = m => m?.Age?.ToString(),
            ["Âge"] = m => m?.Age?.ToString(),
            ["Age_Patient"] = m => m?.Age?.ToString(),
            ["Date_Naissance"] = m => m?.DobFormatted,
            ["Date de naissance"] = m => m?.DobFormatted,
            ["DateNaissance"] = m => m?.DobFormatted,
            ["DDN"] = m => m?.DobFormatted,

            // Sexe
            ["Sexe"] = m => m?.Sexe,

            // Scolarité
            ["Ecole"] = m => m?.Ecole,
            ["École"] = m => m?.Ecole,
            ["Nom de l'école"] = m => m?.Ecole,
            ["Classe"] = m => m?.Classe,

            // Adresse
            ["Adresse"] = m => !string.IsNullOrEmpty(m?.AdresseRue)
                ? $"{m.AdresseRue}, {m.AdresseCodePostal} {m.AdresseVille}"
                : null,
            ["Adresse_Rue"] = m => m?.AdresseRue,
            ["Code_Postal"] = m => m?.AdresseCodePostal,
            ["Ville"] = m => m?.AdresseVille,

            // Accompagnant
            ["Accompagnant"] = m => m?.AccompagnantNom,
            ["Accompagnant_Nom"] = m => m?.AccompagnantNom,
            ["Accompagnant_Lien"] = m => m?.AccompagnantLien,
            ["Accompagnant_Telephone"] = m => m?.AccompagnantTelephone,
            ["Accompagnant_Email"] = m => m?.AccompagnantEmail,

            // Date courante
            ["Date"] = m => DateTime.Now.ToString("dd/MM/yyyy"),
        };
        
        foreach (var variable in variables)
        {
            System.Diagnostics.Debug.WriteLine($"[ReAdaptationService] Traitement variable : '{variable}'");

            // Essayer de trouver la variable dans le mapping
            if (variableMapping.TryGetValue(variable, out var getter))
            {
                var value = getter(metadata);
                if (!string.IsNullOrEmpty(value))
                {
                    System.Diagnostics.Debug.WriteLine($"  ✅ Trouvée dans contexte : {value}");
                    availableInfo[variable] = value;
                    continue;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"  ⚠️ Existe dans mapping mais valeur vide/null");
                }
            }

            // Variable non trouvée → Ajouter aux champs manquants
            System.Diagnostics.Debug.WriteLine($"  ❌ Variable manquante, ajout au dialogue");
            var prompt = GeneratePromptForVariable(variable, documentTitle);
            missingFields.Add(new MissingFieldInfo
            {
                FieldName = variable,
                Prompt = prompt,
                IsRequired = true  // Marquer tous les champs comme requis
            });
        }
        
        return (availableInfo, missingFields);
    }

    /// <summary>
    /// Extrait les valeurs des variables manquantes depuis le contexte clinique avec l'IA
    /// </summary>
    private async Task<Dictionary<string, string>> ExtractFromClinicalContextAsync(
        List<string> missingVariables,
        PatientContextBundle context)
    {
        var extractedInfo = new Dictionary<string, string>();

        if (missingVariables.Count == 0 || string.IsNullOrEmpty(context.ClinicalContext))
        {
            return extractedInfo;
        }

        System.Diagnostics.Debug.WriteLine($"[ReAdaptationService] Extraction IA pour {missingVariables.Count} variables");

        // Construire la liste des variables à extraire
        var variablesList = string.Join("\n", missingVariables.Select(v => $"- {v}"));

        var systemPrompt = @"Tu es un assistant médical expert en extraction d'informations depuis des dossiers patients.
Ton rôle est d'extraire des informations spécifiques depuis le contexte clinique fourni.

RÈGLES STRICTES :
- Extrais UNIQUEMENT les informations demandées qui sont CLAIREMENT présentes dans le contexte
- Si une information n'est PAS présente ou n'est PAS claire, réponds ""NON_TROUVE"" pour cette variable
- Sois PRÉCIS et CONCIS (maximum 2-3 phrases par variable)
- N'INVENTE RIEN, ne fais PAS de déductions hasardeuses
- Utilise le vocabulaire médical approprié";

        var userPrompt = $@"CONTEXTE CLINIQUE
----
{context.ClinicalContext}

VARIABLES À EXTRAIRE
----
{variablesList}

CONSIGNE
----
Pour CHAQUE variable listée ci-dessus, extrais sa valeur depuis le contexte clinique.
Réponds au format JSON strict suivant (un objet avec les variables comme clés) :

{{
  ""Trouble_Principal"": ""valeur ou NON_TROUVE"",
  ""Description_Symptomes"": ""valeur ou NON_TROUVE"",
  ...
}}

IMPORTANT : Retourne UNIQUEMENT le JSON, sans commentaire ni texte supplémentaire.";

        try
        {
            var (success, result) = await _openAIService.ChatAvecContexteAsync(
                string.Empty,
                userPrompt,
                null,
                systemPrompt
            );

            if (success && !string.IsNullOrEmpty(result))
            {
                // Parser le JSON retourné
                var jsonResult = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(result);

                if (jsonResult != null)
                {
                    foreach (var kvp in jsonResult)
                    {
                        // Ajouter seulement si trouvé (pas "NON_TROUVE")
                        if (!string.IsNullOrEmpty(kvp.Value) &&
                            !kvp.Value.Equals("NON_TROUVE", StringComparison.OrdinalIgnoreCase))
                        {
                            extractedInfo[kvp.Key] = kvp.Value;
                            System.Diagnostics.Debug.WriteLine($"  ✅ Extrait '{kvp.Key}': {kvp.Value}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"  ❌ Non trouvé '{kvp.Key}'");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ReAdaptationService] Erreur extraction IA: {ex.Message}");
        }

        return extractedInfo;
    }

    /// <summary>
    /// Génère un prompt utilisateur pour une variable manquante
    /// </summary>
    private string GeneratePromptForVariable(string variableName, string? documentTitle)
    {
        // Prompts spécifiques selon le nom de la variable
        var specificPrompts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Destinataires et correspondance
            ["Destinataire"] = "Destinataire du courrier (ex: Dr. Martin, pédiatre)",
            ["Direction de l'établissement"] = "Destinataire du courrier (ex: M. Dupont, Directeur)",
            ["Nom du cardiologue"] = "Nom du cardiologue destinataire",
            ["Nom du médecin"] = "Nom du médecin signataire",

            // Dates
            ["Date_Courrier"] = "Date du courrier",
            ["Date"] = "Date du courrier",
            ["Date_RDV"] = "Date du rendez-vous",
            ["Date_Prochain_RDV"] = "Date du prochain rendez-vous",
            ["Date de naissance"] = "Date de naissance du patient",
            ["Heure_RDV"] = "Heure du rendez-vous (ex: 14h30)",
            ["Délai Réévaluation"] = "Délai Réévaluation",

            // Lieux
            ["Lieu_RDV"] = "Lieu du rendez-vous",
            ["École"] = "École",
            ["Ecole"] = "École",
            ["Nom de l'école"] = "Nom de l'école",

            // Médical
            ["Motif"] = "Motif de la consultation",
            ["Diagnostic"] = "Diagnostic",
            ["Traitement"] = "Traitement prescrit ou actuel",
            ["Antécédents"] = "Antécédents médicaux",
            ["Recommandations"] = "Recommandations",
            ["Observations"] = "Observations complémentaires",

            // Aménagements PAP
            ["Autres aménagements spécifiques"] = "Liste_Amenagements",
            ["Liste_Amenagements"] = "Liste_Amenagements",
            ["Aménagements recommandés"] = "Liste_Amenagements",

            // Objectifs (Feuille de route)
            ["Objectif 1"] = "Premier objectif thérapeutique",
            ["Objectif 2"] = "Deuxième objectif thérapeutique",
            ["Objectif 3"] = "Troisième objectif thérapeutique",
            ["Autres consignes"] = "Autres consignes pour la maison",
            ["Stratégies spécifiques"] = "Stratégies de renforcement spécifiques",
        };

        if (specificPrompts.TryGetValue(variableName, out var specificPrompt))
        {
            return specificPrompt;
        }

        // Prompt générique basé sur le nom de la variable
        var readableName = variableName.Replace("_", " ").Replace("  ", " ");
        return readableName;
    }
    
    /// <summary>
    /// Étape 4 : Réadapte le courrier avec l'IA en utilisant toutes les infos disponibles
    /// </summary>
    private async Task<string> ReAdaptWithInfoAsync(
        string markdown,
        Dictionary<string, string> allInfo,
        PatientContextBundle patientContext,
        string? pseudonym = null,
        AnonymizationContext? anonContext = null)
    {
        System.Diagnostics.Debug.WriteLine($"[ReAdaptationService] Réadaptation avec {allInfo.Count} informations");
        
        // Construire le texte des informations
        var infoText = string.Join("\n", allInfo.Select(kvp => $"- {kvp.Key}: {kvp.Value}"));
        
        // Prompt système
        var systemPrompt = @"Tu es un assistant médical qui aide à compléter des courriers médicaux.
Ton rôle est de RÉADAPTER un courrier en remplaçant les variables {{Variable}} par les valeurs fournies.

RÈGLES STRICTES :
- Remplace UNIQUEMENT les variables {{Variable}} par les valeurs exactes fournies
- Garde le reste du texte INTACT (style, structure, contenu)
- Ne modifie PAS le ton ou la formulation
- Ne rajoute PAS d'informations non fournies
- Sois précis et fidèle aux informations données";

        // Prompt utilisateur
        var userPrompt = $@"CONTEXTE PATIENT COMPLET
----
{patientContext.ToPromptText(pseudonym, anonContext)}  // ✅ Passer les paramètres d'anonymisation

COURRIER À RÉADAPTER
----
{markdown}

INFORMATIONS À INTÉGRER
----
{infoText}

CONSIGNE
----
Réadapte le courrier en remplaçant les variables {{{{Variable}}}} par les valeurs fournies.
Garde le reste du texte strictement intact.
Retourne uniquement le courrier réadapté, sans commentaire.";

        // Appel à l'IA
        var (success, result) = await _openAIService.ChatAvecContexteAsync(
            string.Empty,
            userPrompt,
            null,
            systemPrompt
        );

        if (success)
        {
            System.Diagnostics.Debug.WriteLine($"[ReAdaptationService] Réadaptation réussie");

            // ✅ Désanonymiser si contexte fourni
            if (anonContext != null && !string.IsNullOrEmpty(anonContext.Pseudonym))
            {
                var deanonymizedResult = _anonymizationService.Deanonymize(result, anonContext);
                return deanonymizedResult;
            }

            return result;
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[ReAdaptationService] Erreur IA : {result}");
            throw new Exception($"Erreur lors de la réadaptation : {result}");
        }
    }
}
