using System;
using System.Collections.Generic;

namespace MedCompanion.Models;

/// <summary>
/// Tracker pour gérer l'accumulation des poids et déterminer quand mettre à jour la synthèse
/// </summary>
public class SynthesisUpdateTracker
{
    /// <summary>
    /// Poids accumulé des items non encore intégrés dans la synthèse
    /// </summary>
    public double AccumulatedWeight { get; set; }

    /// <summary>
    /// Date de la dernière mise à jour de la synthèse
    /// </summary>
    public DateTime? LastSynthesisUpdate { get; set; }

    /// <summary>
    /// Liste des items en attente d'intégration
    /// </summary>
    public List<ContentRelevanceScore> PendingItems { get; set; } = new();

    /// <summary>
    /// Nombre total d'items ajoutés depuis la dernière synthèse
    /// </summary>
    public int TotalItemsSinceLastUpdate { get; set; }

    /// <summary>
    /// Détermine si une mise à jour de la synthèse est recommandée
    /// </summary>
    /// <param name="threshold">Seuil de poids pour déclencher la mise à jour (par défaut: 1.0)</param>
    /// <returns>True si le poids accumulé dépasse le seuil</returns>
    public bool ShouldUpdateSynthesis(double threshold = 1.0)
    {
        return AccumulatedWeight >= threshold;
    }
}
