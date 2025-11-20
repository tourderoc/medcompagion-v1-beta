using System;

namespace MedCompanion.Models;

/// <summary>
/// Modèle pour une ordonnance de soins infirmiers à domicile (IDE)
/// </summary>
public class OrdonnanceIDE
{
    public DateTime DateCreation { get; set; }
    public string Patient { get; set; } = string.Empty;
    public string DateNaissance { get; set; } = string.Empty;
    public string SoinsPrescrits { get; set; } = string.Empty;
    public string Duree { get; set; } = string.Empty;
    public string Renouvelable { get; set; } = string.Empty;
}
