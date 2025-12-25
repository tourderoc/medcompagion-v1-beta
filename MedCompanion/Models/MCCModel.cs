using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MedCompanion.Models
{
    /// <summary>
    /// Statut d'un Modèle de Communication Clinique (MCC)
    /// </summary>
    public enum MCCStatus
    {
        /// <summary>
        /// En cours de création, non utilisable
        /// </summary>
        Draft,
        
        /// <summary>
        /// Actif et utilisable pour les générations
        /// </summary>
        Active,
        
        /// <summary>
        /// Validé par de bonnes notes utilisateur (promu)
        /// </summary>
        Validated,
        
        /// <summary>
        /// Obsolète ou de mauvaise qualité (déprécié)
        /// </summary>
        Deprecated
    }
    
    /// <summary>
    /// Modèle de Communication Clinique (MCC)
    /// Représente un template enrichi avec analyse sémantique et métriques d'utilisation
    /// </summary>
    public class MCCModel
    {
        /// <summary>
        /// Identifiant unique du MCC
        /// </summary>
        public string Id { get; set; }
        
        /// <summary>
        /// Nom descriptif du MCC
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// Version du MCC (incrémenté à chaque modification)
        /// </summary>
        public int Version { get; set; }
        
        /// <summary>
        /// Date de création du MCC
        /// </summary>
        public DateTime Created { get; set; }
        
        /// <summary>
        /// Date de dernière modification
        /// </summary>
        public DateTime LastModified { get; set; }
        
        // ========== STATISTIQUES D'UTILISATION ==========
        
        /// <summary>
        /// Nombre de fois que ce MCC a été utilisé
        /// </summary>
        public int UsageCount { get; set; }
        
        /// <summary>
        /// Note moyenne (1-5 étoiles)
        /// </summary>
        public double AverageRating { get; set; }
        
        /// <summary>
        /// Nombre total de notes reçues
        /// </summary>
        public int TotalRatings { get; set; }
        
        // ========== ANALYSE SÉMANTIQUE ==========
        
        /// <summary>
        /// Analyse sémantique du document source
        /// </summary>
        public SemanticAnalysis Semantic { get; set; }
        
        // ========== CONTENU ==========
        
        /// <summary>
        /// Template au format Markdown avec placeholders
        /// </summary>
        public string TemplateMarkdown { get; set; }
        
        /// <summary>
        /// Prompt optimisé pour générer un document à partir de ce template
        /// </summary>
        public string PromptTemplate { get; set; }
        
        /// <summary>
        /// Mots-clés pour la recherche et le matching
        /// </summary>
        public List<string> Keywords { get; set; }
        
        // ========== ÉTAT ==========

        /// <summary>
        /// Statut actuel du MCC
        /// </summary>
        public MCCStatus Status { get; set; }

        /// <summary>
        /// Indique si ce MCC est ajouté à la liste des courriers (combobox Courriers)
        /// </summary>
        public bool IsInCourriersList { get; set; }

        /// <summary>
        /// Constructeur par défaut
        /// </summary>
        public MCCModel()
        {
            Keywords = new List<string>();
            Version = 1;
            Status = MCCStatus.Active;
            Created = DateTime.Now;
            LastModified = DateTime.Now;
            UsageCount = 0;
            AverageRating = 0.0;
            TotalRatings = 0;
            TemplateMarkdown = string.Empty;
            PromptTemplate = string.Empty;
            IsInCourriersList = false; // Par défaut, pas dans la liste Courriers
        }
    }

    /// <summary>
    /// Réponse d'optimisation d'un MCC
    /// </summary>
    public class MCCOptimizationResponse
    {
        /// <summary>
        /// Template Markdown optimisé
        /// </summary>
        [JsonPropertyName("template_markdown")]
        public string TemplateMarkdown { get; set; } = string.Empty;

        /// <summary>
        /// Liste des 5 mots-clés cliniques
        /// </summary>
        [JsonPropertyName("keywords")]
        public List<string> Keywords { get; set; } = new();

        /// <summary>
        /// Analyse sémantique optimisée
        /// </summary>
        [JsonPropertyName("semantic")]
        public SemanticAnalysis Semantic { get; set; } = new();
    }
}
