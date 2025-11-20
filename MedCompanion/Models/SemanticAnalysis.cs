using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MedCompanion.Models
{
    /// <summary>
    /// Représente l'analyse sémantique d'un document médical
    /// Utilisé pour classifier et catégoriser les documents
    /// </summary>
    public class SemanticAnalysis
    {
        /// <summary>
        /// Type de document (ex: "courrier", "attestation", "note", "synthese", "rapport")
        /// </summary>
        [JsonPropertyName("doc_type")]
        public string DocType { get; set; }
        
        /// <summary>
        /// Public cible (ex: "parents", "ecole", "medecin", "institution", "juge", "mixte")
        /// </summary>
        [JsonPropertyName("public")]
        public string Public { get; set; }
        
        /// <summary>
        /// Public cible - propriété de compatibilité pour ancien code
        /// </summary>
        [JsonIgnore]
        public string Audience 
        { 
            get => Public; 
            set => Public = value; 
        }
        
        /// <summary>
        /// Ton du document (ex: "bienveillant", "clinique", "administratif", "pedagogique", "formel")
        /// </summary>
        [JsonPropertyName("tone")]
        public string Tone { get; set; }
        
        /// <summary>
        /// Tranche d'âge concernée (ex: "0-3", "3-6", "6-11", "12-15", "16+")
        /// </summary>
        [JsonPropertyName("age_group")]
        public string AgeGroup { get; set; }
        
        /// <summary>
        /// Niveau de détail (ex: "bref", "complet", "analytique")
        /// </summary>
        [JsonPropertyName("detail_level")]
        public string DetailLevel { get; set; }
        
        /// <summary>
        /// Résumé du contexte en 2 phrases (famille, école, difficultés)
        /// </summary>
        [JsonPropertyName("context_summary")]
        public string ContextSummary { get; set; }
        
        /// <summary>
        /// Thèmes identifiés (ex: "attention", "anxiete", "regulation_emotionnelle", "socialisation")
        /// </summary>
        [JsonPropertyName("themes")]
        public List<string> Themes { get; set; }
        
        /// <summary>
        /// Mots-clés à utiliser et à éviter
        /// </summary>
        [JsonPropertyName("keywords")]
        public KeywordsSet Keywords { get; set; }
        
        /// <summary>
        /// Informations sur le style du document
        /// </summary>
        [JsonPropertyName("style")]
        public StyleInfo Style { get; set; }
        
        /// <summary>
        /// Métadonnées sur l'analyse
        /// </summary>
        [JsonPropertyName("meta")]
        public MetaInfo Meta { get; set; }
        
        /// <summary>
        /// Mots-clés cliniques - propriété de compatibilité pour ancien code
        /// </summary>
        [JsonIgnore]
        public List<string> ClinicalKeywords 
        { 
            get => Themes ?? new List<string>(); 
            set => Themes = value; 
        }
        
        /// <summary>
        /// Sections détectées dans le document avec leur description
        /// </summary>
        [JsonPropertyName("sections")]
        public Dictionary<string, string> Sections { get; set; }
        
        /// <summary>
        /// Constructeur par défaut
        /// </summary>
        public SemanticAnalysis()
        {
            Themes = new List<string>();
            Keywords = new KeywordsSet();
            Style = new StyleInfo();
            Meta = new MetaInfo();
            Sections = new Dictionary<string, string>();
        }
    }
    
    /// <summary>
    /// Ensemble de mots-clés à utiliser et à éviter
    /// </summary>
    public class KeywordsSet
    {
        /// <summary>
        /// Mots-clés à privilégier
        /// </summary>
        [JsonPropertyName("a_utiliser")]
        public List<string> AUtiliser { get; set; }
        
        /// <summary>
        /// Mots-clés à éviter
        /// </summary>
        [JsonPropertyName("a_eviter")]
        public List<string> AEviter { get; set; }
        
        public KeywordsSet()
        {
            AUtiliser = new List<string>();
            AEviter = new List<string>();
        }
    }
    
    /// <summary>
    /// Informations sur le style du document
    /// </summary>
    public class StyleInfo
    {
        /// <summary>
        /// Longueur du document (ex: "court", "moyen", "long")
        /// </summary>
        [JsonPropertyName("longueur")]
        public string Longueur { get; set; }
        
        private object _phrasesMoyennes;
        
        /// <summary>
        /// Nombre approximatif de mots par phrase (accepte string ou nombre)
        /// </summary>
        [JsonPropertyName("phrases_moyennes")]
        public object PhrasesMoyennesRaw
        {
            get => _phrasesMoyennes;
            set
            {
                _phrasesMoyennes = value;
                // Convertir automatiquement en string
                PhrasesMoyennes = value?.ToString() ?? string.Empty;
            }
        }
        
        /// <summary>
        /// Nombre approximatif de mots par phrase (version string)
        /// </summary>
        [JsonIgnore]
        public string PhrasesMoyennes { get; set; }
        
        /// <summary>
        /// Richesse de la structure (ex: "faible", "moyenne", "elevee")
        /// </summary>
        [JsonPropertyName("structure_richesse")]
        public string StructureRichesse { get; set; }
    }
    
    /// <summary>
    /// Métadonnées sur l'analyse
    /// </summary>
    public class MetaInfo
    {
        private object _semanticConfidence;
        
        /// <summary>
        /// Niveau de confiance de l'analyse sémantique (0.0 - 1.0) - Raw (accepte string ou nombre)
        /// </summary>
        [JsonPropertyName("semantic_confidence")]
        public object SemanticConfidenceRaw
        {
            get => _semanticConfidence;
            set
            {
                _semanticConfidence = value;
                // Convertir automatiquement en double
                if (value is double d)
                {
                    SemanticConfidence = d;
                }
                else if (value is string s && double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double result))
                {
                    SemanticConfidence = result;
                }
                else
                {
                    SemanticConfidence = 0.95; // Valeur par défaut
                }
            }
        }
        
        /// <summary>
        /// Niveau de confiance de l'analyse sémantique (0.0 - 1.0)
        /// </summary>
        [JsonIgnore]
        public double SemanticConfidence { get; set; }
        
        /// <summary>
        /// Langue détectée (ex: "fr")
        /// </summary>
        [JsonPropertyName("detected_language")]
        public string DetectedLanguage { get; set; }
        
        /// <summary>
        /// Source de l'analyse (ex: "TemplateExtractor")
        /// </summary>
        [JsonPropertyName("source")]
        public string Source { get; set; }
    }
}
