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
    
    public LetterReAdaptationService(
        PatientContextService patientContextService,
        OpenAIService openAIService)
    {
        _patientContextService = patientContextService ?? throw new ArgumentNullException(nameof(patientContextService));
        _openAIService = openAIService ?? throw new ArgumentNullException(nameof(openAIService));
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
            
            // Étape 2 : Charger le contexte patient
            var patientContext = _patientContextService.GetCompleteContext(patientName, userRequest);
            System.Diagnostics.Debug.WriteLine($"[ReAdaptationService] {patientContext.ToDebugText()}");
            
            // Étape 3 : Rechercher les variables dans le contexte patient
            var (availableInfo, missingFields) = SearchInPatientContext(detectedVariables, patientContext, documentTitle);
            System.Diagnostics.Debug.WriteLine($"[ReAdaptationService] Infos disponibles : {availableInfo.Count}, Manquantes : {missingFields.Count}");
            
            if (missingFields.Count > 0)
            {
                // Des infos manquent → Retourner pour collecte
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
                        PatientContext = patientContext
                    }
                };
            }
            
            // Étape 4 : Réadapter avec toutes les infos disponibles
            var reAdaptedMarkdown = await ReAdaptWithInfoAsync(markdown, availableInfo, patientContext);
            
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
            
            // Réadapter avec toutes les infos
            var reAdaptedMarkdown = await ReAdaptWithInfoAsync(
                previousResult.State.OriginalMarkdown,
                allInfo,
                previousResult.State.PatientContext!
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
        
        // Mapping des variables vers les métadonnées patient
        var variableMapping = new Dictionary<string, Func<PatientMetadata?, string?>>
        {
            // Nom et identité
            ["Nom_Patient"] = m => m?.NomComplet,
            ["NomPatient"] = m => m?.NomComplet,
            ["Nom"] = m => m?.Nom,
            ["Prenom"] = m => m?.Prenom,
            ["Nom_Prenom"] = m => m?.NomComplet,
            
            // Âge et date de naissance
            ["Age"] = m => m?.Age?.ToString(),
            ["Age_Patient"] = m => m?.Age?.ToString(),
            ["Date_Naissance"] = m => m?.DobFormatted,
            ["DateNaissance"] = m => m?.DobFormatted,
            ["DDN"] = m => m?.DobFormatted,
            
            // Sexe
            ["Sexe"] = m => m?.Sexe,
            
            // Scolarité
            ["Ecole"] = m => m?.Ecole,
            ["École"] = m => m?.Ecole,
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
        };
        
        foreach (var variable in variables)
        {
            // Essayer de trouver la variable dans le mapping
            if (variableMapping.TryGetValue(variable, out var getter))
            {
                var value = getter(metadata);
                if (!string.IsNullOrEmpty(value))
                {
                    availableInfo[variable] = value;
                    continue;
                }
            }
            
            // Variable non trouvée → Ajouter aux champs manquants
            var prompt = GeneratePromptForVariable(variable, documentTitle);
            missingFields.Add(new MissingFieldInfo
            {
                FieldName = variable,
                Prompt = prompt
            });
        }
        
        return (availableInfo, missingFields);
    }
    
    /// <summary>
    /// Génère un prompt utilisateur pour une variable manquante
    /// </summary>
    private string GeneratePromptForVariable(string variableName, string? documentTitle)
    {
        // Prompts spécifiques selon le nom de la variable
        var specificPrompts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Destinataire"] = "Destinataire du courrier (ex: Dr. Martin, pédiatre)",
            ["Date_Courrier"] = "Date du courrier (ex: 25/11/2024)",
            ["Date_RDV"] = "Date du rendez-vous (ex: 15/12/2024)",
            ["Date_Prochain_RDV"] = "Date du prochain rendez-vous",
            ["Heure_RDV"] = "Heure du rendez-vous (ex: 14h30)",
            ["Lieu_RDV"] = "Lieu du rendez-vous",
            ["Motif"] = "Motif de la consultation",
            ["Diagnostic"] = "Diagnostic",
            ["Traitement"] = "Traitement prescrit",
            ["Recommandations"] = "Recommandations",
            ["Observations"] = "Observations complémentaires",
        };
        
        if (specificPrompts.TryGetValue(variableName, out var specificPrompt))
        {
            return specificPrompt;
        }
        
        // Prompt générique basé sur le nom de la variable
        var readableName = variableName.Replace("_", " ").Replace("  ", " ");
        return $"{readableName}";
    }
    
    /// <summary>
    /// Étape 4 : Réadapte le courrier avec l'IA en utilisant toutes les infos disponibles
    /// </summary>
    private async Task<string> ReAdaptWithInfoAsync(
        string markdown,
        Dictionary<string, string> allInfo,
        PatientContextBundle patientContext)
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
{patientContext.ToPromptText()}

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
            return result;
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[ReAdaptationService] Erreur IA : {result}");
            throw new Exception($"Erreur lors de la réadaptation : {result}");
        }
    }
}
