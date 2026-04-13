using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MedCompanion.Models
{
    public class VpsAccount
    {
        [JsonPropertyName("uid")]
        public string Uid { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("pseudo")]
        public string Pseudo { get; set; } = string.Empty;

        [JsonPropertyName("points")]
        public int Points { get; set; }

        [JsonPropertyName("badge")]
        public string? Badge { get; set; }

        [JsonPropertyName("avatar")]
        public object? Avatar { get; set; }

        [JsonPropertyName("role")]
        public string? Role { get; set; }

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("last_activity")]
        public DateTime? LastActivity { get; set; }

        [JsonPropertyName("avatar_gen_count")]
        public int AvatarGenCount { get; set; }

        [JsonPropertyName("fcm_token")]
        public string? FcmToken { get; set; }
        
        // Display helpers
        public string DisplayName => string.IsNullOrEmpty(Pseudo) ? Uid : Pseudo;
        public string CreationDateDisplay => CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
        public string RoleDisplay => Role?.ToUpper() ?? "USER";
    }
}
