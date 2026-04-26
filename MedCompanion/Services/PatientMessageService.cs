using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service d'archivage et lecture des messages parents par patient.
    /// Stocke les messages localement dans le dossier patient/messages/
    /// </summary>
    public class PatientMessageService
    {
        private readonly PathService _pathService;
        private readonly TokenService _tokenService;
        private readonly FirebaseService _firebaseService;
        private readonly PatientIndexService _patientIndex;
        private readonly VpsBridgeService _vpsBridge = new();

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public PatientMessageService(PathService pathService, TokenService tokenService, FirebaseService firebaseService, PatientIndexService patientIndex)
        {
            _pathService = pathService;
            _tokenService = tokenService;
            _firebaseService = firebaseService;
            _patientIndex = patientIndex;
        }

        /// <summary>
        /// Archive ou met à jour un message Firebase dans le dossier local du patient.
        /// Si le message existe déjà, met à jour statut/réponse. Sinon, crée le fichier.
        /// </summary>
        public (bool success, string? error) ArchiveMessage(string patientNomComplet, PatientMessage message)
        {
            try
            {
                var messagesDir = _pathService.GetMessagesDirectory(patientNomComplet);
                _pathService.EnsureDirectoryExists(messagesDir);

                var yearDir = _pathService.GetMessagesYearDirectory(patientNomComplet, message.CreatedAt.Year);
                _pathService.EnsureDirectoryExists(yearDir);

                var sanitizedId = SanitizeFileName(message.Id);

                // Chercher si le message existe déjà
                var existingFile = FindMessageFile(yearDir, sanitizedId);
                if (existingFile != null)
                {
                    // Mettre à jour le fichier existant (statut, réponse)
                    var existingJson = File.ReadAllText(existingFile);
                    var existing = JsonSerializer.Deserialize<ArchivedMessage>(existingJson, _jsonOptions);
                    if (existing != null)
                    {
                        bool changed = false;
                        if (message.HasReply && !existing.HasReply)
                        {
                            existing.ReplyContent = message.ReplyContent;
                            existing.RepliedAt = message.RepliedAt;
                            existing.Status = "replied";
                            changed = true;
                        }
                        if (message.Status == "replied" && existing.Status != "replied")
                        {
                            existing.Status = "replied";
                            changed = true;
                        }
                        if (!string.IsNullOrEmpty(message.AISummary) && string.IsNullOrEmpty(existing.AISummary))
                        {
                            existing.AISummary = message.AISummary;
                            changed = true;
                        }
                        if (changed)
                        {
                            File.WriteAllText(existingFile, JsonSerializer.Serialize(existing, _jsonOptions));
                        }
                        return (true, null);
                    }
                }

                // Nouveau message → créer
                var archived = new ArchivedMessage
                {
                    FirebaseMessageId = message.Id,
                    PatientId = message.PatientId,
                    PatientName = message.PatientName,
                    ParentName = message.ChildNickname,
                    ParentEmail = message.ParentEmail,
                    Content = message.Content,
                    ReceivedAt = message.CreatedAt,
                    Urgency = message.Urgency,
                    AISummary = message.AISummary,
                    DetectedKeywords = message.DetectedKeywords,
                    ReplyContent = message.ReplyContent,
                    RepliedAt = message.RepliedAt,
                    Status = message.HasReply ? "replied" : "received"
                };

                var fileName = $"msg_{message.CreatedAt:yyyy-MM-dd_HHmm}_{sanitizedId}.json";
                var filePath = Path.Combine(yearDir, fileName);
                File.WriteAllText(filePath, JsonSerializer.Serialize(archived, _jsonOptions));

                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, $"Erreur archivage message: {ex.Message}");
            }
        }

        /// <summary>
        /// Charge tous les messages archivés d'un patient
        /// </summary>
        public (bool success, List<ArchivedMessage> messages, string? error) LoadMessagesForPatient(string patientNomComplet, int? year = null)
        {
            try
            {
                var messages = new List<ArchivedMessage>();
                var messagesDir = _pathService.GetMessagesDirectory(patientNomComplet);

                if (!Directory.Exists(messagesDir))
                    return (true, messages, null);

                // Si année spécifiée, chercher uniquement dans ce dossier
                var searchDirs = new List<string>();
                if (year.HasValue)
                {
                    var yearDir = _pathService.GetMessagesYearDirectory(patientNomComplet, year.Value);
                    if (Directory.Exists(yearDir))
                        searchDirs.Add(yearDir);
                }
                else
                {
                    // Toutes les années
                    searchDirs.AddRange(
                        Directory.GetDirectories(messagesDir)
                            .Where(d => int.TryParse(Path.GetFileName(d), out _))
                    );
                }

                foreach (var dir in searchDirs)
                {
                    foreach (var file in Directory.GetFiles(dir, "msg_*.json"))
                    {
                        try
                        {
                            var json = File.ReadAllText(file);
                            var msg = JsonSerializer.Deserialize<ArchivedMessage>(json, _jsonOptions);
                            if (msg != null)
                                messages.Add(msg);
                        }
                        catch
                        {
                            // Ignorer les fichiers corrompus
                        }
                    }
                }

                return (true, messages.OrderByDescending(m => m.ReceivedAt).ToList(), null);
            }
            catch (Exception ex)
            {
                return (false, new List<ArchivedMessage>(), $"Erreur chargement messages: {ex.Message}");
            }
        }

        /// <summary>
        /// Met à jour le statut d'un message archivé (après réponse)
        /// </summary>
        public (bool success, string? error) MarkAsReplied(string patientNomComplet, string firebaseMessageId, string replyContent)
        {
            try
            {
                var messagesDir = _pathService.GetMessagesDirectory(patientNomComplet);
                if (!Directory.Exists(messagesDir))
                    return (false, "Dossier messages introuvable");

                // Chercher le fichier dans toutes les années
                foreach (var yearDir in Directory.GetDirectories(messagesDir))
                {
                    foreach (var file in Directory.GetFiles(yearDir, $"*{firebaseMessageId}*.json"))
                    {
                        var json = File.ReadAllText(file);
                        var msg = JsonSerializer.Deserialize<ArchivedMessage>(json, _jsonOptions);
                        if (msg != null && msg.FirebaseMessageId == firebaseMessageId)
                        {
                            msg.ReplyContent = replyContent;
                            msg.RepliedAt = DateTime.Now;
                            msg.Status = "replied";

                            var updatedJson = JsonSerializer.Serialize(msg, _jsonOptions);
                            File.WriteAllText(file, updatedJson);
                            return (true, null);
                        }
                    }
                }

                return (false, "Message non trouvé dans les archives");
            }
            catch (Exception ex)
            {
                return (false, $"Erreur mise à jour message: {ex.Message}");
            }
        }

        /// <summary>
        /// Vérifie si un message Firebase est déjà archivé localement
        /// </summary>
        public bool IsMessageArchived(string patientNomComplet, string firebaseMessageId)
        {
            var messagesDir = _pathService.GetMessagesDirectory(patientNomComplet);
            if (!Directory.Exists(messagesDir))
                return false;

            foreach (var yearDir in Directory.GetDirectories(messagesDir))
            {
                if (Directory.GetFiles(yearDir, $"*{firebaseMessageId}*.json").Length > 0)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Compte les messages non traités pour un patient
        /// </summary>
        public int GetUnreadCount(string patientNomComplet)
        {
            var (success, messages, _) = LoadMessagesForPatient(patientNomComplet);
            if (!success) return 0;
            return messages.Count(m => m.Status != "replied" && m.Status != "archived");
        }

        /// <summary>
        /// Compte les messages non traités pour TOUS les patients (pour le badge global)
        /// </summary>
        public int GetGlobalUnreadCount()
        {
            int total = 0;
            var basePatientsDir = _pathService.GetBasePatientsDirectory();
            if (!Directory.Exists(basePatientsDir)) return 0;

            foreach (var patientDir in Directory.GetDirectories(basePatientsDir))
            {
                var patientName = Path.GetFileName(patientDir);
                total += GetUnreadCount(patientName);
            }
            return total;
        }

        /// <summary>
        /// Fetch Firebase → résoudre tokens → archiver TOUS les messages localement → retourner les non traités.
        /// Bouton "Messages" du header utilise cette méthode.
        /// </summary>
        public async Task<(bool success, List<PatientMessage> unreadMessages, string? error)> FetchAndSyncMessagesAsync()
        {
            try
            {
                // 1. Lire depuis VPS bridge (source de vérité)
                var allMessages = new List<PatientMessage>();
                var seenIds = new HashSet<string>();

                var (vpsMessages, vpsErr) = await _vpsBridge.FetchMessagesAsync();
                if (vpsErr == null && vpsMessages.Count > 0)
                {
                    foreach (var vm in vpsMessages)
                    {
                        seenIds.Add(vm.id);
                        allMessages.Add(new PatientMessage
                        {
                            Id = vm.id,
                            TokenId = vm.token_id ?? "",
                            Content = vm.content ?? "",
                            ParentEmail = vm.parent_email,
                            ChildNickname = vm.child_nickname ?? "",
                            Status = vm.status ?? "sent",
                            CreatedAt = DateTime.TryParse(vm.created_at, out var dt) ? dt : DateTime.UtcNow,
                            ReplyContent = vm.reply_content,
                            RepliedAt = DateTime.TryParse(vm.replied_at, out var rdt) ? rdt : null,
                        });
                    }
                }

                // 2. Merger avec Firebase (dual-read, messages antérieurs à la migration VPS)
                // Dédup par contenu+token pour éviter les doublons entre Firebase et VPS
                var seenContentKeys = allMessages
                    .Select(m => $"{m.TokenId}|{m.Content?.Trim()}")
                    .ToHashSet();

                if (_firebaseService.IsConfigured)
                {
                    var (firebaseMessages, _) = await _firebaseService.FetchMessagesAsync();
                    if (firebaseMessages != null)
                    {
                        foreach (var fm in firebaseMessages)
                        {
                            var key = $"{fm.TokenId}|{fm.Content?.Trim()}";
                            if (!seenIds.Contains(fm.Id) && !seenContentKeys.Contains(key))
                                allMessages.Add(fm);
                        }
                    }
                }

                var firebaseMessages2 = allMessages; // alias pour compatibilité ci-dessous

                var tokens = await _tokenService.GetAllTokensAsync();
                var tokenMap = tokens.ToDictionary(t => t.TokenId, t => t);

                var unreadMessages = new List<PatientMessage>();

                foreach (var msg in firebaseMessages2)
                {
                    // Résoudre le patient via le token
                    if (tokenMap.TryGetValue(msg.TokenId, out var token))
                    {
                        msg.PatientId = token.PatientId;
                        msg.PatientName = token.PatientDisplayName;

                        if (string.IsNullOrEmpty(msg.ChildNickname) && !string.IsNullOrEmpty(token.Pseudo))
                            msg.ChildNickname = token.Pseudo;

                        // Archiver localement dans le dossier du patient
                        var patientNomComplet = ResolvePatientNomComplet(token.PatientId);
                        if (!string.IsNullOrEmpty(patientNomComplet))
                        {
                            ArchiveMessage(patientNomComplet, msg);
                        }
                    }
                    else
                    {
                        // Token introuvable : message orphelin (token supprimé localement,
                        // ou créé sur une autre install). On le garde quand même pour permettre
                        // une assignation manuelle depuis le dialog.
                        msg.PatientId = null;
                        msg.PatientName = $"\u26A0 Token inconnu ({msg.TokenId})";
                        System.Diagnostics.Debug.WriteLine(
                            $"[Messages] Orphelin: msgId={msg.Id} tokenId={msg.TokenId} childNickname={msg.ChildNickname}");
                    }

                    // Pour le header : ne retourner que les non traités
                    if (msg.Status != "replied")
                    {
                        unreadMessages.Add(msg);
                    }
                }

                return (true, unreadMessages.OrderByDescending(m => m.CreatedAt).ToList(), null);
            }
            catch (Exception ex)
            {
                return (false, new List<PatientMessage>(), $"Erreur synchronisation messages: {ex.Message}");
            }
        }

        // ===== UTILITAIRES =====

        /// <summary>
        /// Résout un PatientId (ex: "SEITZ_Margot") vers le NomComplet pour le chemin dossier
        /// </summary>
        private string? ResolvePatientNomComplet(string patientId)
        {
            if (string.IsNullOrEmpty(patientId)) return null;
            var patient = _patientIndex.GetAllPatients().FirstOrDefault(p => p.Id == patientId);
            return patient?.NomComplet;
        }

        /// <summary>
        /// Cherche un fichier message par son ID sanitisé dans un dossier
        /// </summary>
        private static string? FindMessageFile(string directory, string sanitizedId)
        {
            if (!Directory.Exists(directory)) return null;
            var files = Directory.GetFiles(directory, $"*{sanitizedId}*.json");
            return files.Length > 0 ? files[0] : null;
        }

        /// <summary>
        /// Nettoie un identifiant pour l'utiliser dans un nom de fichier
        /// </summary>
        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }

    /// <summary>
    /// Message archivé localement dans le dossier patient
    /// </summary>
    public class ArchivedMessage
    {
        public string FirebaseMessageId { get; set; } = string.Empty;
        public string? PatientId { get; set; }
        public string? PatientName { get; set; }
        public string ParentName { get; set; } = string.Empty;
        public string? ParentEmail { get; set; }
        public string Content { get; set; } = string.Empty;
        public DateTime ReceivedAt { get; set; }
        public MessageUrgency Urgency { get; set; } = MessageUrgency.Unknown;
        public string? AISummary { get; set; }
        public List<string> DetectedKeywords { get; set; } = new();

        // Réponse
        public string? ReplyContent { get; set; }
        public DateTime? RepliedAt { get; set; }
        public string Status { get; set; } = "received"; // "received", "read", "replied", "archived"

        // Propriétés calculées
        public bool HasReply => !string.IsNullOrEmpty(ReplyContent);
        public string Summary => AISummary ?? (Content.Length > 80 ? Content.Substring(0, 77) + "..." : Content);

        public string StatusDisplay => Status switch
        {
            "received" => "Non lu",
            "read" => "Lu",
            "replied" => "Répondu",
            "archived" => "Archivé",
            _ => Status
        };

        public string RelativeTimeString
        {
            get
            {
                var diff = DateTime.Now - ReceivedAt;
                if (diff.TotalMinutes < 1) return "À l'instant";
                if (diff.TotalMinutes < 60) return $"Il y a {(int)diff.TotalMinutes}m";
                if (diff.TotalHours < 24) return $"Il y a {(int)diff.TotalHours}h";
                if (diff.TotalDays < 7) return $"Il y a {(int)diff.TotalDays}j";
                return ReceivedAt.ToString("dd/MM/yyyy");
            }
        }
    }
}
