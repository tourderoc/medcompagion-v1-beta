using System;

namespace MedCompanion.Models
{
    /// <summary>
    /// Représente un retour utilisateur (feedback) sur un document généré par l'IA
    /// Utilisé pour l'apprentissage et l'amélioration continue du système MCC
    /// </summary>
    public class GenerationFeedback
    {
        /// <summary>
        /// Identifiant unique du feedback
        /// </summary>
        public string Id { get; set; }
        
        /// <summary>
        /// Identifiant de la génération concernée
        /// Permet de tracer quel document a été noté
        /// </summary>
        public string GenerationId { get; set; }
        
        /// <summary>
        /// Identifiant du MCC utilisé pour cette génération
        /// Permet d'associer la note au bon MCC
        /// </summary>
        public string MCCUsed { get; set; }
        
        /// <summary>
        /// Note attribuée par l'utilisateur (1-5 étoiles)
        /// 1 = Très mauvais, 5 = Excellent
        /// </summary>
        public int Rating { get; set; }
        
        /// <summary>
        /// Commentaire optionnel de l'utilisateur
        /// Permet de comprendre pourquoi une note a été donnée
        /// </summary>
        public string Comment { get; set; }
        
        /// <summary>
        /// Date et heure du feedback
        /// </summary>
        public DateTime Timestamp { get; set; }
        
        /// <summary>
        /// Hash anonymisé du contexte patient (pour analyse sans identifier le patient)
        /// Permet d'analyser les feedbacks par type de situation sans violer la confidentialité
        /// </summary>
        public string PatientContext { get; set; }
        
        /// <summary>
        /// Constructeur par défaut
        /// </summary>
        public GenerationFeedback()
        {
            Id = Guid.NewGuid().ToString();
            Timestamp = DateTime.Now;
        }
        
        /// <summary>
        /// Constructeur avec paramètres essentiels
        /// </summary>
        public GenerationFeedback(string generationId, string mccUsed, int rating)
        {
            Id = Guid.NewGuid().ToString();
            GenerationId = generationId;
            MCCUsed = mccUsed;
            Rating = rating;
            Timestamp = DateTime.Now;
        }
    }
}
