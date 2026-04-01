using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MedCompanion.Models
{
    public enum MessageUrgency
    {
        Unknown,
        Low,
        Moderate,
        Urgent,
        Critical
    }

    public class PatientMessage
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("tokenId")]
        public string TokenId { get; set; } = string.Empty;

        [JsonPropertyName("childNickname")]
        public string ChildNickname { get; set; } = string.Empty;

        [JsonPropertyName("parentEmail")]
        public string? ParentEmail { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; set; } = "sent";

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }

        // --- Couche 1 : Heuristique ---
        public List<string> DetectedKeywords { get; set; } = new();
        public List<string> DetectedMedicaments { get; set; } = new();
        public List<string> TemporalMarkers { get; set; } = new();
        public bool HasCriticalKeyword { get; set; }

        // --- Couche 2 : Analyse IA ---
        public MessageUrgency Urgency { get; set; } = MessageUrgency.Unknown;
        public string? AISummary { get; set; }
        public string? SuggestedResponse { get; set; }

        // --- Réponse du médecin ---
        public string? ReplyContent { get; set; }
        public DateTime? RepliedAt { get; set; }

        [JsonIgnore]
        public bool HasReply => !string.IsNullOrEmpty(ReplyContent);

        // --- Liaison locale ---
        [JsonIgnore]
        public string? PatientId { get; set; }
        [JsonIgnore]
        public string? PatientName { get; set; }

        [JsonIgnore]
        public string UrgencyLevel => Urgency.ToString();

        [JsonIgnore]
        public string Summary => AISummary ?? (Content.Length > 50 ? Content.Substring(0, 47) + "..." : Content);

        [JsonIgnore]
        public string RelativeTimeString 
        {
            get 
            {
                var diff = DateTime.Now - CreatedAt;
                if (diff.TotalMinutes < 1) return "À l'instant";
                if (diff.TotalMinutes < 60) return $"Il y a {(int)diff.TotalMinutes}m";
                if (diff.TotalHours < 24) return $"Il y a {(int)diff.TotalHours}h";
                return CreatedAt.ToString("dd/MM");
            }
        }

        [JsonIgnore]
        public DateTime ReceivedAt => CreatedAt;

        [JsonIgnore]
        public string ParentPseudo => ChildNickname;
    }

    public class FirebaseMessageResponse
    {
        public List<FirebaseDocument> documents { get; set; } = new();
    }

    public class FirebaseDocument
    {
        public string name { get; set; } = string.Empty;
        public FirebaseFields fields { get; set; } = new();
        public DateTime createTime { get; set; }
    }

    public class FirebaseFields
    {
        public FirebaseValue? tokenId { get; set; }
        public FirebaseValue? content { get; set; }
        public FirebaseValue? parentEmail { get; set; }
        public FirebaseValue? childNickname { get; set; }
        public FirebaseValue? status { get; set; }
        public FirebaseValue? createdAt { get; set; }
        public FirebaseValue? replyContent { get; set; }
        public FirebaseValue? repliedAt { get; set; }
    }

    public class FirebaseValue
    {
        public string? stringValue { get; set; }
        public string? timestampValue { get; set; }
    }
}
