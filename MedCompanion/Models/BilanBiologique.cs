using System;
using System.Collections.Generic;
using System.Linq;

namespace MedCompanion.Models;

/// <summary>
/// Représente un examen biologique avec son état (coché/décoché)
/// </summary>
public class ExamenBiologique
{
    public string Nom { get; set; } = string.Empty;
    public bool EstCoche { get; set; }
    public string? Description { get; set; } // Pour ajouter des détails si nécessaire

    public ExamenBiologique() { }

    public ExamenBiologique(string nom, bool estCoche = true, string? description = null)
    {
        Nom = nom;
        EstCoche = estCoche;
        Description = description;
    }
}

/// <summary>
/// Représente un preset de bilan biologique (standard pédiatrique, sans jeûne, thyroïdien)
/// </summary>
public class BilanBiologiquePreset
{
    public string Nom { get; set; } = string.Empty;
    public List<ExamenBiologique> Examens { get; set; } = new();
    public string? Note { get; set; } // Note affichée sur l'ordonnance
    public string? Description { get; set; } // Description du preset

    public BilanBiologiquePreset() { }

    public BilanBiologiquePreset(string nom, string? description = null, string? note = null)
    {
        Nom = nom;
        Description = description;
        Note = note;
    }

    /// <summary>
    /// Presets prédéfinis
    /// </summary>
    public static class Presets
    {
        /// <summary>
        /// Bilan standard pédiatrique
        /// </summary>
        public static BilanBiologiquePreset BilanStandardPediatrique => new("Bilan standard pédiatrique", "Bilan complet avec exploration hématologique, métabolique et hépatique")
        {
            Examens = new List<ExamenBiologique>
            {
                new("Glycémie à jeun", true),
                new("NFS–Plaquettes", true),
                new("CRP", true),
                new("Bilan martial : Ferritine, Fer sérique, CST si nécessaire", true),
                new("Ionogramme sanguin : Na, K, Cl, HCO₃⁻", true),
                new("Fonction rénale : Urée, Créatinine", true),
                new("Bilan hépatique : ASAT (TGO), ALAT (TGP), PAL, GGT, Bilirubine totale", true),
                new("Calcémie, Phosphorémie, Magnésémie", true),
                new("Vitamine D (25-OH)", false, "optionnel") // Décoché par défaut
            },
            Note = "Bilan à jeun"
        };

        /// <summary>
        /// Bilan biologique sans jeûne (enfants)
        /// </summary>
        public static BilanBiologiquePreset BilanSansJeune => new("Bilan biologique – sans jeûne (enfants)", "Même bilan que le standard mais sans jeûne requis")
        {
            Examens = new List<ExamenBiologique>
            {
                new("HbA1c (remplace la glycémie à jeun)", true),
                new("NFS–Plaquettes", true),
                new("CRP", true),
                new("Bilan martial : Ferritine, Fer sérique, CST si nécessaire", true),
                new("Ionogramme sanguin : Na, K, Cl, HCO₃⁻", true),
                new("Fonction rénale : Urée, Créatinine", true),
                new("Bilan hépatique : ASAT (TGO), ALAT (TGP), PAL, GGT, Bilirubine totale", true),
                new("Calcémie, Phosphorémie, Magnésémie", true),
                new("Vitamine D (25-OH)", false, "optionnel") // Décoché par défaut
            },
            Note = "Prélèvement possible sans jeûne (pas de glycémie à jeun demandée)."
        };

        /// <summary>
        /// Bilan thyroïdien
        /// </summary>
        public static BilanBiologiquePreset BilanThyroidien => new("Bilan thyroïdien", "Exploration de la fonction thyroïdienne")
        {
            Examens = new List<ExamenBiologique>
            {
                new("TSH ultra-sensibles (TSHus)", true),
                new("T4 libre (FT4)", true),
                new("Anticorps anti-TPO", false, "si goitre / suspicion auto-immune"),
                new("Anticorps anti-Tg", false, "si goitre / suspicion auto-immune")
            },
            Note = null // Aucune note spécifique
        };

        /// <summary>
        /// Retourne tous les presets disponibles
        /// </summary>
        public static List<BilanBiologiquePreset> TousLesPresets => new()
        {
            BilanStandardPediatrique,
            BilanSansJeune,
            BilanThyroidien
        };
    }
}

/// <summary>
/// Représente une ordonnance de biologie complète
/// </summary>
public class OrdonnanceBiologie
{
    public string PresetNom { get; set; } = string.Empty;
    public List<ExamenBiologique> ExamensCoches { get; set; } = new();
    public string? Note { get; set; }
    public DateTime DateCreation { get; set; } = DateTime.Now;

    // Informations patient (héritées du contexte)
    public string? PatientNom { get; set; }
    public string? PatientPrenom { get; set; }
    public string? PatientDateNaissance { get; set; }

    public OrdonnanceBiologie() { }

    public OrdonnanceBiologie(BilanBiologiquePreset preset, string patientNom, string patientPrenom, string patientDob)
    {
        PresetNom = preset.Nom;
        ExamensCoches = preset.Examens.Where(e => e.EstCoche).ToList();
        Note = preset.Note;
        PatientNom = patientNom;
        PatientPrenom = patientPrenom;
        PatientDateNaissance = patientDob;
    }

    /// <summary>
    /// Génère un aperçu court pour affichage dans la liste
    /// </summary>
    public string GeneratePreview()
    {
        if (ExamensCoches.Count == 0)
            return "Aucun examen sélectionné";

        if (ExamensCoches.Count == 1)
            return ExamensCoches[0].Nom;

        if (ExamensCoches.Count == 2)
            return $"{ExamensCoches[0].Nom}, {ExamensCoches[1].Nom}";

        return $"{ExamensCoches[0].Nom}, {ExamensCoches[1].Nom} et {ExamensCoches.Count - 2} autre(s)";
    }
}
