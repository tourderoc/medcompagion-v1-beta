using System;
using System.Text.Json.Serialization;

namespace MedCompanion.Models
{
    /// <summary>
    /// Représente l'évaluation d'un courrier généré
    /// </summary>
    public class LetterRating
    {
        /// <summary>
        /// Identifiant unique de l'évaluation
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; } = Guid.NewGuid().ToString();

        /// <summary>
        /// Chemin du fichier .docx évalué
        /// </summary>
        [JsonPropertyName("letter_path")]
        public string LetterPath { get; set; } = string.Empty;

        /// <summary>
        /// Note de 1 à 5 étoiles
        /// </summary>
        [JsonPropertyName("rating")]
        public int Rating { get; set; }

        /// <summary>
        /// Commentaire optionnel de l'utilisateur
        /// </summary>
        [JsonPropertyName("comment")]
        public string? Comment { get; set; }

        /// <summary>
        /// Date et heure de l'évaluation
        /// </summary>
        [JsonPropertyName("rating_date")]
        public DateTime RatingDate { get; set; } = DateTime.Now;

        /// <summary>
        /// ID du MCC utilisé (null si pas de MCC)
        /// </summary>
        [JsonPropertyName("mcc_id")]
        public string? MCCId { get; set; }

        /// <summary>
        /// Nom du MCC utilisé (null si pas de MCC)
        /// </summary>
        [JsonPropertyName("mcc_name")]
        public string? MCCName { get; set; }

        /// <summary>
        /// Demande originale de l'utilisateur
        /// </summary>
        [JsonPropertyName("user_request")]
        public string? UserRequest { get; set; }

        /// <summary>
        /// Contexte patient utilisé (extrait)
        /// </summary>
        [JsonPropertyName("patient_context")]
        public string? PatientContext { get; set; }

        /// <summary>
        /// Nom du patient
        /// </summary>
        [JsonPropertyName("patient_name")]
        public string? PatientName { get; set; }

        /// <summary>
        /// Indique si ce courrier est un candidat pour créer un MCC
        /// (5 étoiles sans MCC existant)
        /// </summary>
        [JsonPropertyName("is_mcc_candidate")]
        public bool IsMCCCandidate => Rating == 5 && string.IsNullOrEmpty(MCCId);

        /// <summary>
        /// Indique si le MCC utilisé nécessite une révision
        /// (note ≤ 3 étoiles avec MCC existant)
        /// </summary>
        [JsonPropertyName("needs_mcc_review")]
        public bool NeedsMCCReview => Rating <= 3 && !string.IsNullOrEmpty(MCCId);
    }

    /// <summary>
    /// Conteneur pour toutes les évaluations
    /// </summary>
    public class LetterRatingsCollection
    {
        [JsonPropertyName("ratings")]
        public List<LetterRating> Ratings { get; set; } = new List<LetterRating>();

        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0";

        [JsonPropertyName("last_updated")]
        public DateTime LastUpdated { get; set; } = DateTime.Now;
    }
}
