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
    /// Service de gestion des évaluations de courriers
    /// </summary>
    public class LetterRatingService
    {
        private readonly string _ratingsFilePath;
        private LetterRatingsCollection _ratingsCollection;
        private readonly object _lock = new object();

        public LetterRatingService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MedCompanion"
            );
            Directory.CreateDirectory(appDataPath);
            _ratingsFilePath = Path.Combine(appDataPath, "letter-ratings.json");
            
            _ratingsCollection = LoadRatings();
        }

        /// <summary>
        /// Charge les évaluations depuis le fichier
        /// </summary>
        private LetterRatingsCollection LoadRatings()
        {
            lock (_lock)
            {
                if (File.Exists(_ratingsFilePath))
                {
                    try
                    {
                        var json = File.ReadAllText(_ratingsFilePath);
                        var collection = JsonSerializer.Deserialize<LetterRatingsCollection>(json);
                        if (collection != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"[LetterRatingService] {collection.Ratings.Count} évaluations chargées");
                            return collection;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LetterRatingService] Erreur chargement: {ex.Message}");
                    }
                }
                
                return new LetterRatingsCollection();
            }
        }

        /// <summary>
        /// Sauvegarde les évaluations dans le fichier
        /// </summary>
        private (bool success, string error) SaveRatings()
        {
            lock (_lock)
            {
                try
                {
                    _ratingsCollection.LastUpdated = DateTime.Now;
                    
                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true
                    };
                    
                    var json = JsonSerializer.Serialize(_ratingsCollection, options);
                    File.WriteAllText(_ratingsFilePath, json);
                    
                    System.Diagnostics.Debug.WriteLine($"[LetterRatingService] ✅ {_ratingsCollection.Ratings.Count} évaluations sauvegardées");
                    return (true, string.Empty);
                }
                catch (Exception ex)
                {
                    var error = $"Erreur sauvegarde: {ex.Message}";
                    System.Diagnostics.Debug.WriteLine($"[LetterRatingService] ❌ {error}");
                    return (false, error);
                }
            }
        }

        /// <summary>
        /// Ajoute ou met à jour une évaluation de courrier
        /// </summary>
        public (bool success, string error) AddOrUpdateRating(LetterRating rating)
        {
            if (rating == null)
                return (false, "Évaluation null");

            if (string.IsNullOrEmpty(rating.LetterPath))
                return (false, "Chemin du courrier requis");

            if (rating.Rating < 1 || rating.Rating > 5)
                return (false, "Note invalide (doit être entre 1 et 5)");

            lock (_lock)
            {
                // Vérifier si une évaluation existe déjà pour ce courrier
                var existing = _ratingsCollection.Ratings
                    .FirstOrDefault(r => r.LetterPath == rating.LetterPath);

                if (existing != null)
                {
                    // Mise à jour
                    existing.Rating = rating.Rating;
                    existing.Comment = rating.Comment;
                    existing.RatingDate = DateTime.Now;
                    existing.MCCId = rating.MCCId;
                    existing.MCCName = rating.MCCName;
                    existing.UserRequest = rating.UserRequest;
                    existing.PatientContext = rating.PatientContext;
                    existing.PatientName = rating.PatientName;
                    
                    System.Diagnostics.Debug.WriteLine($"[LetterRatingService] ♻️ Évaluation mise à jour: {rating.LetterPath}");
                }
                else
                {
                    // Nouvelle évaluation
                    _ratingsCollection.Ratings.Add(rating);
                    System.Diagnostics.Debug.WriteLine($"[LetterRatingService] ➕ Nouvelle évaluation: {rating.LetterPath}");
                }

                return SaveRatings();
            }
        }

        /// <summary>
        /// Récupère l'évaluation d'un courrier
        /// </summary>
        public LetterRating? GetRatingForLetter(string letterPath)
        {
            if (string.IsNullOrEmpty(letterPath))
                return null;

            lock (_lock)
            {
                return _ratingsCollection.Ratings
                    .FirstOrDefault(r => r.LetterPath == letterPath);
            }
        }

        /// <summary>
        /// Récupère toutes les évaluations
        /// </summary>
        public List<LetterRating> GetAllRatings()
        {
            lock (_lock)
            {
                return new List<LetterRating>(_ratingsCollection.Ratings);
            }
        }

        /// <summary>
        /// Récupère toutes les évaluations pour un MCC spécifique
        /// </summary>
        public List<LetterRating> GetRatingsForMCC(string mccId)
        {
            if (string.IsNullOrEmpty(mccId))
                return new List<LetterRating>();

            lock (_lock)
            {
                return _ratingsCollection.Ratings
                    .Where(r => r.MCCId == mccId)
                    .OrderByDescending(r => r.RatingDate)
                    .ToList();
            }
        }

        /// <summary>
        /// Calcule la note moyenne d'un MCC
        /// </summary>
        public (double average, int count) GetMCCAverageRating(string mccId)
        {
            var ratings = GetRatingsForMCC(mccId);
            if (ratings.Count == 0)
                return (0, 0);

            var average = ratings.Average(r => r.Rating);
            return (average, ratings.Count);
        }

        /// <summary>
        /// Récupère tous les courriers candidats pour devenir MCC (5 étoiles sans MCC)
        /// </summary>
        public List<LetterRating> GetMCCCandidates()
        {
            lock (_lock)
            {
                return _ratingsCollection.Ratings
                    .Where(r => r.IsMCCCandidate)
                    .OrderByDescending(r => r.RatingDate)
                    .ToList();
            }
        }

        /// <summary>
        /// Récupère tous les MCC nécessitant une révision (≤3 étoiles)
        /// </summary>
        public List<string> GetMCCsNeedingReview()
        {
            lock (_lock)
            {
                return _ratingsCollection.Ratings
                    .Where(r => r.NeedsMCCReview)
                    .Select(r => r.MCCId!)
                    .Distinct()
                    .ToList();
            }
        }

        /// <summary>
        /// Récupère les statistiques d'un MCC
        /// </summary>
        public MCCStatistics GetMCCStatistics(string mccId)
        {
            var ratings = GetRatingsForMCC(mccId);
            
            var stats = new MCCStatistics
            {
                MCCId = mccId,
                TotalRatings = ratings.Count
            };

            if (ratings.Count > 0)
            {
                stats.AverageRating = ratings.Average(r => r.Rating);
                stats.LastRatingDate = ratings.Max(r => r.RatingDate);
                
                // Distribution des notes
                stats.FiveStars = ratings.Count(r => r.Rating == 5);
                stats.FourStars = ratings.Count(r => r.Rating == 4);
                stats.ThreeStars = ratings.Count(r => r.Rating == 3);
                stats.TwoStars = ratings.Count(r => r.Rating == 2);
                stats.OneStar = ratings.Count(r => r.Rating == 1);
                
                // Taux de satisfaction (4-5 étoiles)
                stats.SatisfactionRate = (double)(stats.FiveStars + stats.FourStars) / stats.TotalRatings * 100;
            }

            return stats;
        }

        /// <summary>
        /// Supprime une évaluation
        /// </summary>
        public (bool success, string error) DeleteRating(string letterPath)
        {
            if (string.IsNullOrEmpty(letterPath))
                return (false, "Chemin invalide");

            lock (_lock)
            {
                var rating = _ratingsCollection.Ratings
                    .FirstOrDefault(r => r.LetterPath == letterPath);

                if (rating != null)
                {
                    _ratingsCollection.Ratings.Remove(rating);
                    System.Diagnostics.Debug.WriteLine($"[LetterRatingService] 🗑️ Évaluation supprimée: {letterPath}");
                    return SaveRatings();
                }

                return (false, "Évaluation introuvable");
            }
        }

        /// <summary>
        /// Recharge les évaluations depuis le disque
        /// </summary>
        public void ReloadRatings()
        {
            _ratingsCollection = LoadRatings();
        }
    }

    /// <summary>
    /// Statistiques d'un MCC
    /// </summary>
    public class MCCStatistics
    {
        public string MCCId { get; set; } = string.Empty;
        public int TotalRatings { get; set; }
        public double AverageRating { get; set; }
        public DateTime? LastRatingDate { get; set; }
        public double SatisfactionRate { get; set; }
        
        // Distribution des notes
        public int FiveStars { get; set; }
        public int FourStars { get; set; }
        public int ThreeStars { get; set; }
        public int TwoStars { get; set; }
        public int OneStar { get; set; }
    }
}
