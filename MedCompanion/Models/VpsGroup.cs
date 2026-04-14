using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MedCompanion.Models
{
    public class VpsGroup
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("titre")]
        public string Titre { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("theme")]
        public string? Theme { get; set; }

        [JsonPropertyName("createur_uid")]
        public string CreateurUid { get; set; } = string.Empty;

        [JsonPropertyName("createur_pseudo")]
        public string CreateurPseudo { get; set; } = string.Empty;

        [JsonPropertyName("date_vocal")]
        public DateTime DateVocal { get; set; }

        [JsonPropertyName("date_expiration")]
        public DateTime DateExpiration { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("participants_max")]
        public int ParticipantsMax { get; set; }

        [JsonPropertyName("message_count")]
        public int MessageCount { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("structure_type")]
        public string? StructureType { get; set; }

        [JsonPropertyName("participants")]
        public List<VpsGroupParticipant>? Participants { get; set; }

        public int ParticipantCount => Participants?.Count ?? 0;
        public string DateVocalDisplay => DateVocal.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        public string CreatedAtDisplay => CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        public string ParticipantsDisplay => $"{ParticipantCount}/{ParticipantsMax}";
        public string StatusDisplay => Status?.ToUpper() ?? "?";
    }

    public class VpsGroupParticipant
    {
        [JsonPropertyName("user_uid")]
        public string UserUid { get; set; } = string.Empty;

        [JsonPropertyName("pseudo")]
        public string? Pseudo { get; set; }

        [JsonPropertyName("inscrit_vocal")]
        public bool InscritVocal { get; set; }

        [JsonPropertyName("banni")]
        public bool Banni { get; set; }
    }
}
