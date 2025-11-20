using System;
using System.Collections.Generic;

namespace MedCompanion.Models;

/// <summary>
/// Règles de calcul des poids par défaut pour différents types de contenu
/// </summary>
public static class ContentWeightRules
{
    /// <summary>
    /// Retourne le poids par défaut selon le type de document
    /// Retourne null si une évaluation IA est nécessaire
    /// </summary>
    /// <param name="itemType">Type de document</param>
    /// <param name="metadata">Métadonnées optionnelles pour affiner le calcul</param>
    /// <returns>Poids entre 0.0 et 1.0, ou null si IA requise</returns>
    public static double? GetDefaultWeight(string itemType, Dictionary<string, object>? metadata = null)
    {
        return itemType.ToLower() switch
        {
            // ATTESTATIONS (poids fixes)
            "attestation_presence" => 0.1,
            "attestation_suivi" => 0.1,
            "attestation_amenagement" => 0.5,
            "attestation_amenagement_scolaire" => 0.5,
            "attestation_scolarite" => 0.3,

            // ORDONNANCES (logique conditionnelle)
            "ordonnance" => EvaluateOrdonnanceWeight(metadata),

            // DOCUMENTS ADMINISTRATIFS
            "certificat_medical_simple" => 0.2,
            "courrier_administratif" => 0.1,

            // DOCUMENTS IMPORTANTS (poids fixes élevés)
            "formulaire_mdph" => 0.8,
            "bilan_psychologique" => 0.9,
            "compte_rendu_hospitalisation" => 0.9,

            // NÉCESSITE IA (retourne null)
            "note_clinique" => null,        // IA évalue pendant structuration
            "synthese_document" => null,    // IA évalue pendant synthèse
            "courrier_medical" => null,     // IA évalue si généré

            // Valeur par défaut: demander à l'IA
            _ => null
        };
    }

    /// <summary>
    /// Évalue le poids d'une ordonnance selon le contexte
    /// </summary>
    private static double EvaluateOrdonnanceWeight(Dictionary<string, object>? metadata)
    {
        if (metadata == null)
            return 0.5; // Poids moyen par défaut

        // Vérifier si c'est un renouvellement
        if (metadata.ContainsKey("is_renewal") && metadata["is_renewal"] is bool isRenewal && isRenewal)
            return 0.2; // Renouvellement à l'identique (faible impact)

        // Vérifier si nouveau traitement
        if (metadata.ContainsKey("new_medication") && metadata["new_medication"] is bool isNew && isNew)
            return 1.0; // Nouvelle prescription (fort impact)

        // Vérifier si changement de posologie
        if (metadata.ContainsKey("dosage_change") && metadata["dosage_change"] is bool hasDosageChange && hasDosageChange)
            return 0.6; // Ajustement posologie (impact modéré)

        // Par défaut: ordonnance standard
        return 0.5;
    }

    /// <summary>
    /// Retourne une description textuelle du poids
    /// </summary>
    public static string GetWeightDescription(double weight)
    {
        return weight switch
        {
            >= 0.9 => "Critique",
            >= 0.7 => "Très important",
            >= 0.5 => "Important",
            >= 0.3 => "Modéré",
            >= 0.1 => "Mineur",
            _ => "Négligeable"
        };
    }
}
