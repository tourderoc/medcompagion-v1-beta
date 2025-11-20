using System;

namespace MedCompanion.Models;

/// <summary>
/// Représente le score de pertinence d'un contenu pour la mise à jour de la synthèse
/// </summary>
public class ContentRelevanceScore
{
    /// <summary>
    /// Identifiant unique de l'item
    /// </summary>
    public string ItemId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Type de contenu (note, attestation, courrier, ordonnance, etc.)
    /// </summary>
    public string ItemType { get; set; } = "";

    /// <summary>
    /// Chemin complet du fichier
    /// </summary>
    public string FilePath { get; set; } = "";

    /// <summary>
    /// Date d'ajout du contenu
    /// </summary>
    public DateTime DateAdded { get; set; } = DateTime.Now;

    /// <summary>
    /// Poids de pertinence (0.0 à 1.0)
    /// </summary>
    public double RelevanceWeight { get; set; }

    /// <summary>
    /// Justification du poids attribué
    /// </summary>
    public string Justification { get; set; } = "";

    /// <summary>
    /// Indique si l'item a déjà été intégré dans une synthèse
    /// </summary>
    public bool IncludedInSynthesis { get; set; }
}
