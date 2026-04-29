using System.Text.Json.Serialization;

namespace MedCompanion.Models
{
    public class AdminInboxStats
    {
        [JsonPropertyName("unread_total")]
        public int UnreadTotal { get; set; }

        [JsonPropertyName("unread_feedback")]
        public int UnreadFeedback { get; set; }

        [JsonPropertyName("unread_appeals")]
        public int UnreadAppeals { get; set; }

        [JsonPropertyName("unread_reports")]
        public int UnreadReports { get; set; }
    }
}
