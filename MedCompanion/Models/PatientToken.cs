using System;
using System.Text.Json.Serialization;

namespace MedCompanion.Models
{
    /// <summary>
    /// Token permettant à un patient d'accéder à l'espace Parent'aile
    /// Le token est généré dans MedCompanion et remis au parent sous forme de QR code
    /// </summary>
    public class PatientToken
    {
        /// <summary>
        /// Identifiant unique du token (ex: "abc123xyz")
        /// </summary>
        [JsonPropertyName("tokenId")]
        public string TokenId { get; set; } = string.Empty;

        /// <summary>
        /// Identifiant du patient dans MedCompanion (ex: "DUPONT_Martin")
        /// </summary>
        [JsonPropertyName("patientId")]
        public string PatientId { get; set; } = string.Empty;

        /// <summary>
        /// Nom d'affichage du patient (ex: "DUPONT Martin")
        /// </summary>
        [JsonPropertyName("patientDisplayName")]
        public string PatientDisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Pseudo choisi par le parent sur Parent'aile (ex: "MamanDeThéo")
        /// Null si le token n'a pas encore été activé
        /// </summary>
        [JsonPropertyName("pseudo")]
        public string? Pseudo { get; set; }

        /// <summary>
        /// Date de création du token
        /// </summary>
        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }

        /// <summary>
        /// Date de dernière activité (dernier message envoyé/reçu)
        /// </summary>
        [JsonPropertyName("lastActivity")]
        public DateTime? LastActivity { get; set; }

        /// <summary>
        /// Indique si le token est actif (peut être révoqué)
        /// </summary>
        [JsonPropertyName("active")]
        public bool Active { get; set; } = true;

        /// <summary>
        /// Indique si le parent a activé son compte (choisi un pseudo)
        /// </summary>
        [JsonIgnore]
        public bool IsActivated => !string.IsNullOrEmpty(Pseudo);

        /// <summary>
        /// Statut d'affichage pour l'interface
        /// </summary>
        [JsonIgnore]
        public string StatusDisplay => Active
            ? (IsActivated ? "Actif" : "En attente d'activation")
            : "Révoqué";
    }

    /// <summary>
    /// Container pour la liste des tokens (stockage JSON)
    /// </summary>
    public class TokenStorage
    {
        [JsonPropertyName("tokens")]
        public List<PatientToken> Tokens { get; set; } = new();

        [JsonPropertyName("lastUpdated")]
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}
