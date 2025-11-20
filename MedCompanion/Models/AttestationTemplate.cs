using System.Collections.Generic;

namespace MedCompanion.Models
{
    /// <summary>
    /// Modèle de données pour un template d'attestation
    /// </summary>
    public class AttestationTemplate
    {
        /// <summary>
        /// Type d'attestation (Présence, Suivi, Arrêt scolaire, Aménagement)
        /// </summary>
        public string Type { get; set; } = string.Empty;
        
        /// <summary>
        /// Nom affiché dans l'interface
        /// </summary>
        public string DisplayName { get; set; } = string.Empty;
        
        /// <summary>
        /// Description courte de l'attestation
        /// </summary>
        public string Description { get; set; } = string.Empty;
        
        /// <summary>
        /// Champs obligatoires pour cette attestation
        /// </summary>
        public List<string> RequiredFields { get; set; } = new();
        
        /// <summary>
        /// Champs optionnels
        /// </summary>
        public List<string> OptionalFields { get; set; } = new();
        
        /// <summary>
        /// Template Markdown avec placeholders {{Variable}}
        /// </summary>
        public string Markdown { get; set; } = string.Empty;
        
        /// <summary>
        /// Longueur maximale autorisée (en caractères)
        /// </summary>
        public int MaxLength { get; set; } = 800;
        
        /// <summary>
        /// Mots interdits (diagnostic, traitement, etc.)
        /// </summary>
        public List<string> ForbiddenWords { get; set; } = new()
        {
            "diagnostic", "diagnostique", "trouble", "pathologie",
            "traitement", "médicament", "prescription",
            "antécédent", "symptôme", "syndrome"
        };
    }
    
    /// <summary>
    /// Informations collectées pour générer une attestation
    /// </summary>
    public class AttestationData
    {
        public string Type { get; set; } = string.Empty;
        public Dictionary<string, string> Fields { get; set; } = new();
    }
}
