using System;

namespace MedCompanion.Models
{
    /// <summary>
    /// Métadonnées d'un patient stockées dans patient.json
    /// </summary>
    public class PatientMetadata
    {
        // === Identité ===
        public string Prenom { get; set; } = string.Empty;
        public string Nom { get; set; } = string.Empty;
        public string? Dob { get; set; }  // Format: YYYY-MM-DD
        public string? Sexe { get; set; }  // H, F, ou NB
        public string? LieuNaissance { get; set; }  // Ville de naissance

        // === Scolarité ===
        public string? Ecole { get; set; }  // École/Établissement
        public string? Classe { get; set; }  // Classe/Niveau

        // === Adresse ===
        public string? AdresseRue { get; set; }
        public string? AdresseCodePostal { get; set; }
        public string? AdresseVille { get; set; }
        public string? AdressePays { get; set; } = "France";

        // === Sécurité sociale ===
        public string? NumeroSecuriteSociale { get; set; }  // NIR (15 chiffres)
        public string? NumeroINS { get; set; }  // Si différent du NIR

        // === Accompagnant ===
        public string? AccompagnantNom { get; set; }
        public string? AccompagnantPrenom { get; set; }
        public string? AccompagnantLien { get; set; }  // Mère, Père, Éducateur, Tuteur, Famille d'accueil, Autre
        public string? AccompagnantTelephone { get; set; }
        public string? AccompagnantEmail { get; set; }

        // === Situation ===
        public string? SituationAccueil { get; set; }  // Domicile, Foyer, Famille d'accueil, Autre

        // === Médecins / Professionnels ===
        public string? MedecinTraitantNom { get; set; }
        public string? MedecinTraitantPrenom { get; set; }
        
        public string? MedecinReferentNom { get; set; }
        public string? MedecinReferentPrenom { get; set; }
        public string? MedecinReferentSpecialite { get; set; }
        
        // Propriétés calculées (non sérialisées)
        public string NomComplet => $"{Prenom} {Nom}";
        
        public string? DobFormatted
        {
            get
            {
                if (string.IsNullOrEmpty(Dob) || !DateTime.TryParse(Dob, out var date))
                    return null;
                return date.ToString("dd/MM/yyyy");
            }
        }
        
        public int? Age
        {
            get
            {
                if (string.IsNullOrEmpty(Dob) || !DateTime.TryParse(Dob, out var dob))
                    return null;
                
                var today = DateTime.Today;
                var age = today.Year - dob.Year;
                if (dob.Date > today.AddYears(-age))
                    age--;
                
                return age;
            }
        }
        
        public string DisplayLabel
        {
            get
            {
                var label = NomComplet;
                if (!string.IsNullOrEmpty(DobFormatted))
                {
                    label += $" – {DobFormatted}";
                    if (Age.HasValue)
                        label += $" ({Age} ans)";
                }
                return label;
            }
        }
    }
    
    /// <summary>
    /// Entrée d'index pour la recherche rapide
    /// </summary>
    public class PatientIndexEntry
    {
        public string Id { get; set; } = string.Empty;  // Nom_Prenom
        public string Prenom { get; set; } = string.Empty;
        public string Nom { get; set; } = string.Empty;
        public string? Dob { get; set; }
        public string? Sexe { get; set; }
        public string DirectoryPath { get; set; } = string.Empty;
        
        public string NomComplet => $"{Prenom} {Nom}";
        
        public int? Age
        {
            get
            {
                if (string.IsNullOrEmpty(Dob) || !DateTime.TryParse(Dob, out var dob))
                    return null;
                
                var today = DateTime.Today;
                var age = today.Year - dob.Year;
                if (dob.Date > today.AddYears(-age))
                    age--;
                
                return age;
            }
        }
        
        public string? DobFormatted
        {
            get
            {
                if (string.IsNullOrEmpty(Dob) || !DateTime.TryParse(Dob, out var date))
                    return null;
                return date.ToString("dd/MM/yyyy");
            }
        }
        
        public string DisplayLabel
        {
            get
            {
                var label = $"{Nom} {Prenom}";
                if (!string.IsNullOrEmpty(DobFormatted))
                {
                    label += $" – {DobFormatted}";
                    if (Age.HasValue)
                        label += $" ({Age} ans)";
                }
                return label;
            }
        }
    }
}
