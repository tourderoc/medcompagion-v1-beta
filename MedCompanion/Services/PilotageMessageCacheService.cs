using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service de cache local pour les messages Pilotage
    /// Évite de retraiter les messages déjà analysés par le LLM à chaque démarrage
    /// </summary>
    public class PilotageMessageCacheService
    {
        private readonly string _cacheDir;
        private readonly string _cacheFile;
        private Dictionary<string, CachedMessage> _cache = new();

        public PilotageMessageCacheService(PathService pathService)
        {
            var basePatientsDir = pathService.GetBasePatientsDirectory();
            var medCompanionDir = Path.GetDirectoryName(basePatientsDir) ?? basePatientsDir;
            _cacheDir = Path.Combine(medCompanionDir, "pilotage");
            _cacheFile = Path.Combine(_cacheDir, "messages_cache.json");

            if (!Directory.Exists(_cacheDir))
            {
                Directory.CreateDirectory(_cacheDir);
            }

            LoadCache();
        }

        /// <summary>
        /// Vérifie si un message est déjà dans le cache (donc déjà traité)
        /// </summary>
        public bool IsMessageCached(string messageId)
        {
            return _cache.ContainsKey(messageId);
        }

        /// <summary>
        /// Récupère un message du cache
        /// </summary>
        public PatientMessage? GetCachedMessage(string messageId)
        {
            if (_cache.TryGetValue(messageId, out var cached))
            {
                return cached.ToPatientMessage();
            }
            return null;
        }

        /// <summary>
        /// Ajoute ou met à jour un message dans le cache
        /// </summary>
        public void CacheMessage(PatientMessage message)
        {
            if (string.IsNullOrEmpty(message.Id)) return;

            _cache[message.Id] = CachedMessage.FromPatientMessage(message);
            SaveCache();

            System.Diagnostics.Debug.WriteLine($"[MessageCache] 💾 Message mis en cache: {message.Id}");
        }

        /// <summary>
        /// Met à jour le statut d'un message (ex: replied)
        /// </summary>
        public void UpdateMessageStatus(string messageId, string status)
        {
            if (_cache.TryGetValue(messageId, out var cached))
            {
                cached.Status = status;
                SaveCache();
            }
        }

        /// <summary>
        /// Supprime un message du cache
        /// </summary>
        public void RemoveMessage(string messageId)
        {
            if (_cache.Remove(messageId))
            {
                SaveCache();
                System.Diagnostics.Debug.WriteLine($"[MessageCache] 🗑️ Message supprimé du cache: {messageId}");
            }
        }

        /// <summary>
        /// Supprime les messages plus vieux que X jours (nettoyage)
        /// </summary>
        public void CleanOldMessages(int daysToKeep = 90)
        {
            var cutoff = DateTime.Now.AddDays(-daysToKeep);
            var toRemove = _cache.Where(kv => kv.Value.ReceivedAt < cutoff).Select(kv => kv.Key).ToList();

            foreach (var id in toRemove)
            {
                _cache.Remove(id);
            }

            if (toRemove.Count > 0)
            {
                SaveCache();
                System.Diagnostics.Debug.WriteLine($"[MessageCache] 🧹 Nettoyé {toRemove.Count} anciens messages");
            }
        }

        /// <summary>
        /// Retourne les IDs de tous les messages en cache
        /// </summary>
        public HashSet<string> GetCachedMessageIds()
        {
            return _cache.Keys.ToHashSet();
        }

        /// <summary>
        /// Compte les messages non lus (status != replied)
        /// </summary>
        public int GetUnreadCount()
        {
            return _cache.Values.Count(m => m.Status != "replied");
        }

        private void LoadCache()
        {
            try
            {
                if (File.Exists(_cacheFile))
                {
                    var json = File.ReadAllText(_cacheFile);
                    var loaded = JsonSerializer.Deserialize<List<CachedMessage>>(json);
                    _cache = loaded?.ToDictionary(m => m.Id, m => m) ?? new Dictionary<string, CachedMessage>();

                    System.Diagnostics.Debug.WriteLine($"[MessageCache] 📂 Cache chargé: {_cache.Count} messages");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MessageCache] ⚠️ Erreur chargement cache: {ex.Message}");
                _cache = new Dictionary<string, CachedMessage>();
            }
        }

        private void SaveCache()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(_cache.Values.ToList(), options);
                File.WriteAllText(_cacheFile, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MessageCache] ⚠️ Erreur sauvegarde cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Structure simplifiée pour le stockage JSON
        /// </summary>
        private class CachedMessage
        {
            public string Id { get; set; } = string.Empty;
            public string TokenId { get; set; } = string.Empty;
            public string Content { get; set; } = string.Empty;
            public string ChildNickname { get; set; } = string.Empty;
            public string? ParentEmail { get; set; }
            public string Status { get; set; } = "sent";
            public DateTime ReceivedAt { get; set; }

            // Données patient liées
            public string? PatientId { get; set; }
            public string? PatientName { get; set; }

            // Résultats de l'analyse LLM (ce qu'on veut éviter de recalculer)
            public string? AISummary { get; set; }
            public string? SuggestedResponse { get; set; }
            public MessageUrgency Urgency { get; set; }
            public List<string> DetectedKeywords { get; set; } = new();
            public List<string> DetectedMedicaments { get; set; } = new();
            public List<string> TemporalMarkers { get; set; } = new();
            public bool HasCriticalKeyword { get; set; }

            // Timestamp du traitement LLM
            public DateTime? ProcessedAt { get; set; }

            public static CachedMessage FromPatientMessage(PatientMessage msg)
            {
                return new CachedMessage
                {
                    Id = msg.Id,
                    TokenId = msg.TokenId,
                    Content = msg.Content,
                    ChildNickname = msg.ChildNickname,
                    ParentEmail = msg.ParentEmail,
                    Status = msg.Status,
                    ReceivedAt = msg.ReceivedAt,
                    PatientId = msg.PatientId,
                    PatientName = msg.PatientName,
                    AISummary = msg.AISummary,
                    SuggestedResponse = msg.SuggestedResponse,
                    Urgency = msg.Urgency,
                    DetectedKeywords = msg.DetectedKeywords,
                    DetectedMedicaments = msg.DetectedMedicaments,
                    TemporalMarkers = msg.TemporalMarkers,
                    HasCriticalKeyword = msg.HasCriticalKeyword,
                    ProcessedAt = DateTime.Now
                };
            }

            public PatientMessage ToPatientMessage()
            {
                return new PatientMessage
                {
                    Id = Id,
                    TokenId = TokenId,
                    Content = Content,
                    ChildNickname = ChildNickname,
                    ParentEmail = ParentEmail,
                    Status = Status,
                    CreatedAt = ReceivedAt,
                    PatientId = PatientId,
                    PatientName = PatientName,
                    AISummary = AISummary,
                    SuggestedResponse = SuggestedResponse,
                    Urgency = Urgency,
                    DetectedKeywords = DetectedKeywords,
                    DetectedMedicaments = DetectedMedicaments,
                    TemporalMarkers = TemporalMarkers,
                    HasCriticalKeyword = HasCriticalKeyword
                };
            }
        }
    }
}
