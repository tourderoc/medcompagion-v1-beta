using System;
using System.Collections.Generic;

namespace MedCompanion.Models;

/// <summary>
/// Représente un médicament de la base BDPM
/// </summary>
public class Medicament
{
    /// <summary>
    /// Code CIS (Code Identifiant de Spécialité)
    /// </summary>
    public string CIS { get; set; } = string.Empty;

    /// <summary>
    /// Dénomination du médicament
    /// </summary>
    public string Denomination { get; set; } = string.Empty;

    /// <summary>
    /// Forme pharmaceutique (comprimé, solution, etc.)
    /// </summary>
    public string Forme { get; set; } = string.Empty;

    /// <summary>
    /// Voie d'administration
    /// </summary>
    public string VoieAdministration { get; set; } = string.Empty;

    /// <summary>
    /// Statut AMM (Autorisation de Mise sur le Marché)
    /// </summary>
    public string StatutAMM { get; set; } = string.Empty;

    /// <summary>
    /// Type de procédure AMM
    /// </summary>
    public string TypeProcedure { get; set; } = string.Empty;

    /// <summary>
    /// Commercialisé (oui/non)
    /// </summary>
    public string Commercialisation { get; set; } = string.Empty;

    /// <summary>
    /// Date AMM (format JJ/MM/AAAA)
    /// </summary>
    public string DateAMM { get; set; } = string.Empty;

    /// <summary>
    /// Statut BDM (Base de Données Médicaments)
    /// </summary>
    public string StatutBDM { get; set; } = string.Empty;

    /// <summary>
    /// Numéro d'autorisation européen
    /// </summary>
    public string NumAutorisationEU { get; set; } = string.Empty;

    /// <summary>
    /// Titulaire(s) de l'AMM
    /// </summary>
    public string Titulaires { get; set; } = string.Empty;

    /// <summary>
    /// Surveillance renforcée (triangle noir)
    /// </summary>
    public string SurveillanceRenforcee { get; set; } = string.Empty;

    /// <summary>
    /// Présentations disponibles (dosages)
    /// </summary>
    public List<MedicamentPresentation> Presentations { get; set; } = new();

    /// <summary>
    /// Compositions (DCI/substances actives)
    /// </summary>
    public List<MedicamentComposition> Compositions { get; set; } = new();

    /// <summary>
    /// Génère une description complète pour l'affichage
    /// </summary>
    public string GetDisplayText()
    {
        if (Presentations.Count > 0)
        {
            return $"{Denomination} {Presentations[0].Libelle}";
        }
        return Denomination;
    }

    /// <summary>
    /// Génère une description courte pour l'autocomplétion
    /// </summary>
    public string GetSearchText()
    {
        return $"{Denomination} {Forme}".ToLowerInvariant();
    }
}

/// <summary>
/// Représente une présentation d'un médicament (dosage, conditionnement)
/// </summary>
public class MedicamentPresentation
{
    public string CIS { get; set; } = string.Empty;
    public string CIP7 { get; set; } = string.Empty;
    public string CIP13 { get; set; } = string.Empty;
    public string Libelle { get; set; } = string.Empty;
    public string StatutAdministratif { get; set; } = string.Empty;
    public string EtatCommercialisation { get; set; } = string.Empty;
    public string DateDeclaration { get; set; } = string.Empty;

    /// <summary>
    /// Prix (optionnel)
    /// </summary>
    public string Prix { get; set; } = string.Empty;

    /// <summary>
    /// Taux de remboursement (optionnel)
    /// </summary>
    public string TauxRemboursement { get; set; } = string.Empty;
}

/// <summary>
/// Représente la composition d'un médicament (DCI/substances actives)
/// </summary>
public class MedicamentComposition
{
    public string CIS { get; set; } = string.Empty;

    /// <summary>
    /// Désignation de l'élément pharmaceutique
    /// </summary>
    public string DesignationElement { get; set; } = string.Empty;

    /// <summary>
    /// Code substance
    /// </summary>
    public string CodeSubstance { get; set; } = string.Empty;

    /// <summary>
    /// Dénomination de la substance (DCI)
    /// </summary>
    public string DenominationSubstance { get; set; } = string.Empty;

    /// <summary>
    /// Dosage de la substance
    /// </summary>
    public string Dosage { get; set; } = string.Empty;

    /// <summary>
    /// Référence du dosage
    /// </summary>
    public string ReferenceDosage { get; set; } = string.Empty;

    /// <summary>
    /// Nature du composant
    /// </summary>
    public string NatureComposant { get; set; } = string.Empty;

    /// <summary>
    /// Numéro de liaison
    /// </summary>
    public string NumeroLiaison { get; set; } = string.Empty;
}

/// <summary>
/// Représente un médicament ajouté à une ordonnance
/// </summary>
public class MedicamentPrescrit
{
    /// <summary>
    /// Médicament de référence
    /// </summary>
    public Medicament Medicament { get; set; } = null!;

    /// <summary>
    /// Présentation choisie (dosage)
    /// </summary>
    public MedicamentPresentation? Presentation { get; set; }

    /// <summary>
    /// Posologie saisie par le médecin
    /// </summary>
    public string Posologie { get; set; } = string.Empty;

    /// <summary>
    /// Durée du traitement
    /// </summary>
    public string Duree { get; set; } = string.Empty;

    /// <summary>
    /// Nombre de boîtes/unités
    /// </summary>
    public int Quantite { get; set; } = 1;

    /// <summary>
    /// Renouvellement autorisé
    /// </summary>
    public bool Renouvelable { get; set; }

    /// <summary>
    /// Nombre de renouvellements
    /// </summary>
    public int NombreRenouvellements { get; set; } = 0;

    /// <summary>
    /// Génère le texte pour l'ordonnance
    /// </summary>
    public string GetOrdonnanceText()
    {
        var text = Medicament.Denomination;

        if (Presentation != null && !string.IsNullOrEmpty(Presentation.Libelle))
        {
            text += $" - {Presentation.Libelle}";
        }

        if (!string.IsNullOrEmpty(Posologie))
        {
            text += $"\n{Posologie}";
        }

        if (!string.IsNullOrEmpty(Duree))
        {
            text += $"\nDurée : {Duree}";
        }

        if (Quantite > 1)
        {
            text += $"\nQuantité : {Quantite}";
        }

        if (Renouvelable && NombreRenouvellements > 0)
        {
            text += $"\nRenouvellement : {NombreRenouvellements} fois";
        }

        return text;
    }
}

/// <summary>
/// Représente une ordonnance de médicaments complète
/// </summary>
public class OrdonnanceMedicaments
{
    public DateTime DateCreation { get; set; } = DateTime.Now;

    /// <summary>
    /// Patient concerné
    /// </summary>
    public string PatientNom { get; set; } = string.Empty;
    public string PatientPrenom { get; set; } = string.Empty;
    public string? PatientDateNaissance { get; set; }

    /// <summary>
    /// Liste des médicaments prescrits
    /// </summary>
    public List<MedicamentPrescrit> Medicaments { get; set; } = new();

    /// <summary>
    /// Notes additionnelles
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Génère un aperçu court pour la liste
    /// </summary>
    public string GeneratePreview()
    {
        if (Medicaments.Count == 0)
            return "Aucun médicament prescrit";

        if (Medicaments.Count == 1)
            return Medicaments[0].Medicament.Denomination;

        if (Medicaments.Count == 2)
            return $"{Medicaments[0].Medicament.Denomination}, {Medicaments[1].Medicament.Denomination}";

        return $"{Medicaments[0].Medicament.Denomination}, {Medicaments[1].Medicament.Denomination} et {Medicaments.Count - 2} autre(s)";
    }
}
