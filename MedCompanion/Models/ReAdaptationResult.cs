using MedCompanion.Dialogs;
using MedCompanion.Services;

namespace MedCompanion.Models;

/// <summary>
/// Résultat du processus de réadaptation d'un courrier
/// </summary>
public class ReAdaptationResult
{
    /// <summary>
    /// Indique si le processus a réussi
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Message d'erreur si échec
    /// </summary>
    public string? Error { get; set; }
    
    /// <summary>
    /// Variables détectées dans le courrier
    /// </summary>
    public List<string> DetectedVariables { get; set; } = new();
    
    /// <summary>
    /// Informations disponibles dans le contexte patient
    /// </summary>
    public Dictionary<string, string> AvailableInfo { get; set; } = new();
    
    /// <summary>
    /// Champs manquants à collecter auprès de l'utilisateur
    /// </summary>
    public List<MissingFieldInfo> MissingFields { get; set; } = new();
    
    /// <summary>
    /// Indique si des informations manquantes doivent être collectées
    /// </summary>
    public bool NeedsMissingInfo { get; set; }
    
    /// <summary>
    /// Courrier réadapté (disponible si pas d'infos manquantes ou après CompleteReAdaptationAsync)
    /// </summary>
    public string? ReAdaptedMarkdown { get; set; }
    
    /// <summary>
    /// État interne pour reprendre le processus après collecte d'infos
    /// </summary>
    public ReAdaptationState? State { get; set; }
}

/// <summary>
/// État interne du processus de réadaptation
/// </summary>
public class ReAdaptationState
{
    public string OriginalMarkdown { get; set; } = string.Empty;
    public string PatientName { get; set; } = string.Empty;
    public string? DocumentTitle { get; set; }
    public string? UserRequest { get; set; }
    public PatientContextBundle? PatientContext { get; set; }

    // ✅ Ajout pour l'anonymisation
    public string? Pseudonym { get; set; }
    public AnonymizationContext? AnonContext { get; set; }
}
