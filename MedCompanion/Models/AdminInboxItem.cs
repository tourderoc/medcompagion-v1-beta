using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MedCompanion.Models
{
    public class AdminInboxItem
    {
        [JsonPropertyName("src")]
        public string Src { get; set; } = "";

        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("subject_uid")]
        public string? SubjectUid { get; set; }

        [JsonPropertyName("subject_pseudo")]
        public string? SubjectPseudo { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("context")]
        public JsonElement? Context { get; set; }

        [JsonPropertyName("groupe_id")]
        public string? GroupeId { get; set; }

        [JsonPropertyName("reporter_uid")]
        public string? ReporterUid { get; set; }

        [JsonPropertyName("reported_uid")]
        public string? ReportedUid { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }

        [JsonPropertyName("reviewed")]
        public bool Reviewed { get; set; }

        [JsonPropertyName("reviewed_at")]
        public DateTime? ReviewedAt { get; set; }

        [JsonPropertyName("reviewed_by")]
        public string? ReviewedBy { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        // Computed helpers (used by DataTemplate bindings)
        [JsonIgnore]
        public string TypeIcon => Src switch
        {
            "feedback" => Type switch
            {
                "bug" => "🐛",
                "suggestion" => "💡",
                "question" => "❓",
                _ => "📝"
            },
            "ban_appeal" => "⚠️",
            "ban_report" => "🚨",
            _ => "📬"
        };

        [JsonIgnore]
        public string TypeLabel => Src switch
        {
            "feedback" => Type switch
            {
                "bug" => "Bug",
                "suggestion" => "Suggestion",
                "question" => "Question",
                _ => "Feedback"
            },
            "ban_appeal" => "Recours ban",
            "ban_report" => "Signalement",
            _ => "Inconnu"
        };

        [JsonIgnore]
        public string DisplayPseudo => SubjectPseudo
            ?? (SubjectUid?.Length >= 8 ? SubjectUid[..8] : SubjectUid)
            ?? "—";

        [JsonIgnore]
        public string MessagePreview
        {
            get
            {
                var txt = Message ?? "";
                return txt.Length > 60 ? txt[..60] + "…" : txt;
            }
        }
    }
}
