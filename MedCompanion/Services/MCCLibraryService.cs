using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MedCompanion.Models;
using MedCompanion.Services.LLM;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service pour gérer la bibliothèque de Modèles de Communication Clinique (MCC)
    /// Gestion du stockage, recherche intelligente, statistiques
    /// </summary>
    public class MCCLibraryService
    {
        private readonly string _libraryPath;
        private Dictionary<string, MCCModel> _library;
        private readonly JsonSerializerOptions _jsonOptions;

        public MCCLibraryService()
        {
            // Initialiser le chemin de la bibliothèque
            var appData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MedCompanion"
            );
            
            if (!Directory.Exists(appData))
            {
                Directory.CreateDirectory(appData);
            }

            _libraryPath = Path.Combine(appData, "mcc-library.json");
            
            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
            };

            _library = new Dictionary<string, MCCModel>();
            LoadLibrary();
        }

        #region Chargement et Sauvegarde

        /// <summary>
        /// Charge la bibliothèque MCC depuis le fichier JSON
        /// </summary>
        private void LoadLibrary()
        {
            try
            {
                if (!File.Exists(_libraryPath))
                {
                    System.Diagnostics.Debug.WriteLine("[MCCLibrary] Fichier bibliothèque non trouvé, création d'une nouvelle bibliothèque");
                    _library = new Dictionary<string, MCCModel>();
                    SaveLibrary();
                    return;
                }

                var json = File.ReadAllText(_libraryPath);
                var loadedLibrary = JsonSerializer.Deserialize<Dictionary<string, MCCModel>>(json, _jsonOptions);
                
                _library = loadedLibrary ?? new Dictionary<string, MCCModel>();
                
                System.Diagnostics.Debug.WriteLine($"[MCCLibrary] {_library.Count} MCC chargés depuis {_libraryPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MCCLibrary] Erreur chargement : {ex.Message}");
                _library = new Dictionary<string, MCCModel>();
            }
        }

        /// <summary>
        /// Sauvegarde la bibliothèque MCC dans le fichier JSON
        /// </summary>
        private (bool success, string message) SaveLibrary()
        {
            try
            {
                var json = JsonSerializer.Serialize(_library, _jsonOptions);
                File.WriteAllText(_libraryPath, json);
                
                System.Diagnostics.Debug.WriteLine($"[MCCLibrary] {_library.Count} MCC sauvegardés dans {_libraryPath}");
                return (true, "Bibliothèque sauvegardée avec succès");
            }
            catch (Exception ex)
            {
                var errorMsg = $"Erreur de sauvegarde : {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"[MCCLibrary] {errorMsg}");
                return (false, errorMsg);
            }
        }

        #endregion

        #region CRUD Operations

        /// <summary>
        /// Ajoute un nouveau MCC à la bibliothèque
        /// </summary>
        public (bool success, string message) AddMCC(MCCModel mcc)
        {
            try
            {
                if (string.IsNullOrEmpty(mcc.Id))
                {
                    return (false, "ID du MCC invalide");
                }

                if (_library.ContainsKey(mcc.Id))
                {
                    return (false, $"Un MCC avec l'ID '{mcc.Id}' existe déjà");
                }

                _library[mcc.Id] = mcc;
                var saveResult = SaveLibrary();
                
                if (saveResult.success)
                {
                    System.Diagnostics.Debug.WriteLine($"[MCCLibrary] MCC ajouté : {mcc.Name} ({mcc.Id})");
                    return (true, "MCC ajouté avec succès");
                }
                
                return saveResult;
            }
            catch (Exception ex)
            {
                return (false, $"Erreur lors de l'ajout : {ex.Message}");
            }
        }

        /// <summary>
        /// Récupère un MCC par son ID
        /// </summary>
        public MCCModel GetMCC(string mccId)
        {
            return _library.TryGetValue(mccId, out var mcc) ? mcc : null;
        }

        /// <summary>
        /// Récupère tous les MCC de la bibliothèque
        /// </summary>
        public List<MCCModel> GetAllMCCs()
        {
            // ✅ CORRECTION : Recharger depuis le fichier pour avoir les dernières données
            LoadLibrary();
            return _library.Values.ToList();
        }

        /// <summary>
        /// Met à jour un MCC existant
        /// </summary>
        public (bool success, string message) UpdateMCC(MCCModel mcc)
        {
            try
            {
                if (!_library.ContainsKey(mcc.Id))
                {
                    return (false, "MCC introuvable");
                }

                mcc.LastModified = DateTime.Now;
                _library[mcc.Id] = mcc;
                
                var saveResult = SaveLibrary();
                
                if (saveResult.success)
                {
                    System.Diagnostics.Debug.WriteLine($"[MCCLibrary] MCC mis à jour : {mcc.Name} ({mcc.Id})");
                    return (true, "MCC mis à jour avec succès");
                }
                
                return saveResult;
            }
            catch (Exception ex)
            {
                return (false, $"Erreur lors de la mise à jour : {ex.Message}");
            }
        }

        /// <summary>
        /// Supprime un MCC de la bibliothèque
        /// </summary>
        public (bool success, string message) DeleteMCC(string mccId)
        {
            try
            {
                if (!_library.ContainsKey(mccId))
                {
                    return (false, "MCC introuvable");
                }

                var mcc = _library[mccId];
                _library.Remove(mccId);
                
                var saveResult = SaveLibrary();
                
                if (saveResult.success)
                {
                    System.Diagnostics.Debug.WriteLine($"[MCCLibrary] MCC supprimé : {mcc.Name} ({mccId})");
                    return (true, "MCC supprimé avec succès");
                }
                
                return saveResult;
            }
            catch (Exception ex)
            {
                return (false, $"Erreur lors de la suppression : {ex.Message}");
            }
        }

        #endregion

        #region Recherche Intelligente

        /// <summary>
        /// Trouve le meilleur MCC selon le type de document et les métadonnées
        /// </summary>
        public MCCModel FindBestMCC(string docType, Dictionary<string, string> metadata)
        {
            try
            {
                // Filtrer par type de document et statut actif
                var candidates = _library.Values
                    .Where(m => m.Semantic?.DocType == docType && 
                               (m.Status == MCCStatus.Active || m.Status == MCCStatus.Validated))
                    .ToList();

                if (!candidates.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"[MCCLibrary] Aucun MCC trouvé pour type '{docType}'");
                    return null;
                }

                // Scorer chaque candidat
                var scored = candidates.Select(mcc => new
                {
                    MCC = mcc,
                    Score = CalculateMatchScore(mcc, metadata)
                })
                .OrderByDescending(x => x.Score)
                .ToList();

                var best = scored.FirstOrDefault();
                
                if (best != null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[MCCLibrary] Meilleur MCC trouvé : {best.MCC.Name} (score: {best.Score:F2})"
                    );
                }

                return best?.MCC;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MCCLibrary] Erreur recherche : {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Calcule le score de correspondance entre un MCC et les métadonnées
        /// Algorithme de scoring pondéré
        /// </summary>
        private double CalculateMatchScore(MCCModel mcc, Dictionary<string, string> metadata)
        {
            double score = 0;

            // Type de document exact → +50 points (obligatoire, déjà filtré)
            score += 50;

            // Correspondance audience → +30 points
            if (metadata.TryGetValue("audience", out var audience) && 
                mcc.Semantic?.Audience == audience)
            {
                score += 30;
            }

            // Correspondance tranche d'âge → +20 points
            if (metadata.TryGetValue("age_group", out var ageGroup) && 
                mcc.Semantic?.AgeGroup == ageGroup)
            {
                score += 20;
            }

            // Correspondance ton → +15 points
            if (metadata.TryGetValue("tone", out var tone) && 
                mcc.Semantic?.Tone == tone)
            {
                score += 15;
            }

            // Qualité (rating moyen) → jusqu'à +50 points
            if (mcc.TotalRatings > 0)
            {
                score += (mcc.AverageRating / 5.0) * 50; // Normaliser sur 50 points max
            }

            // Popularité (usage) → jusqu'à +20 points
            // Utiliser log pour éviter biais des très utilisés
            if (mcc.UsageCount > 0)
            {
                score += Math.Log(mcc.UsageCount + 1) * 5;
            }

            // Bonus pour statut Validated → +10 points
            if (mcc.Status == MCCStatus.Validated)
            {
                score += 10;
            }

            return score;
        }

        /// <summary>
        /// Trouve les meilleurs MCC correspondant à une demande utilisateur analysée
        /// Utilisé par le système de courriers intelligents
        /// </summary>
        public List<(MCCModel mcc, double score)> FindBestMatchingMCCs(
            string docType, 
            Dictionary<string, string> metadata, 
            List<string> keywords,
            int maxResults = 3)
        {
            try
            {
                // Filtrer par type de document et statut actif
                var candidates = _library.Values
                    .Where(m => m.Semantic?.DocType == docType && 
                               (m.Status == MCCStatus.Active || m.Status == MCCStatus.Validated))
                    .ToList();

                if (!candidates.Any())
                {
                    System.Diagnostics.Debug.WriteLine($"[MCCLibrary] Aucun MCC trouvé pour type '{docType}'");
                    return new List<(MCCModel, double)>();
                }

                // Scorer chaque candidat avec les mots-clés
                var scored = candidates.Select(mcc => new
                {
                    MCC = mcc,
                    Score = CalculateMatchScoreWithKeywords(mcc, metadata, keywords)
                })
                .OrderByDescending(x => x.Score)
                .Take(maxResults)
                .Select(x => (x.MCC, x.Score))
                .ToList();

                foreach (var (mcc, score) in scored)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[MCCLibrary] MCC candidat : {mcc.Name} (score: {score:F2})"
                    );
                }

                return scored;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MCCLibrary] Erreur recherche : {ex.Message}");
                return new List<(MCCModel, double)>();
            }
        }

        /// <summary>
        /// Dictionnaires de mapping bilingue (copiés depuis MCCMatchingService pour cohérence)
        /// </summary>
        private static readonly Dictionary<string, List<string>> AUDIENCE_ALIASES = new()
        {
            ["school"] = new() { "school", "ecole", "scolaire", "enseignant", "professeur" },
            ["parents"] = new() { "parents", "famille", "parent" },
            ["doctor"] = new() { "doctor", "medecin", "confrere", "specialiste", "physician" },
            ["institution"] = new() { "institution", "administratif", "administration", "mdph", "cpam" },
            ["judge"] = new() { "judge", "juge", "tribunal", "justice", "legal" },
            ["mixed"] = new() { "mixed", "mixte", "multiple" }
        };

        private static readonly Dictionary<string, List<string>> TONE_ALIASES = new()
        {
            ["caring"] = new() { "caring", "bienveillant", "empathique", "chaleureux" },
            ["clinical"] = new() { "clinical", "clinique", "medical", "technique" },
            ["administrative"] = new() { "administrative", "administratif", "formel", "officiel", "formal" },
            ["educational"] = new() { "educational", "pedagogique", "educatif" },
            ["neutral"] = new() { "neutral", "neutre", "objectif" }
        };

        /// <summary>
        /// Vérifie si deux valeurs correspondent (avec support multilingue)
        /// </summary>
        private bool ValuesMatch(string value1, string value2, Dictionary<string, List<string>> aliasDict)
        {
            if (string.IsNullOrEmpty(value1) || string.IsNullOrEmpty(value2))
                return false;

            var normalized1 = value1.ToLower().Trim();
            var normalized2 = value2.ToLower().Trim();

            // Correspondance directe
            if (normalized1 == normalized2)
                return true;

            // Chercher si les deux valeurs appartiennent au même groupe d'alias
            foreach (var aliases in aliasDict.Values)
            {
                var hasValue1 = aliases.Any(a => a.Equals(normalized1, StringComparison.OrdinalIgnoreCase));
                var hasValue2 = aliases.Any(a => a.Equals(normalized2, StringComparison.OrdinalIgnoreCase));
                
                if (hasValue1 && hasValue2)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Calcule le score de correspondance avec mots-clés et métadonnées
        /// </summary>
        private double CalculateMatchScoreWithKeywords(
            MCCModel mcc, 
            Dictionary<string, string> metadata,
            List<string> keywords)
        {
            double score = 0;

            // Type de document exact → +50 points (obligatoire, déjà filtré)
            score += 50;

            // Correspondance mots-clés → jusqu'à +40 points
            if (keywords != null && keywords.Any())
            {
                var mccKeywords = (mcc.Keywords ?? new List<string>())
                    .Select(k => k.ToLower())
                    .ToList();

                var matchingKeywords = keywords
                    .Select(k => k.ToLower())
                    .Count(k => mccKeywords.Any(mk => mk.Contains(k) || k.Contains(mk)));

                if (mccKeywords.Any())
                {
                    var keywordMatchRatio = (double)matchingKeywords / Math.Max(keywords.Count, mccKeywords.Count);
                    score += keywordMatchRatio * 40;
                }
            }

            // Correspondance audience → +30 points (avec support multilingue)
            if (metadata.TryGetValue("audience", out var audience) && 
                !string.IsNullOrEmpty(mcc.Semantic?.Audience) &&
                ValuesMatch(audience, mcc.Semantic.Audience, AUDIENCE_ALIASES))
            {
                score += 30;
            }

            // Correspondance tranche d'âge → +20 points
            if (metadata.TryGetValue("age_group", out var ageGroup) && 
                !string.IsNullOrEmpty(mcc.Semantic?.AgeGroup) &&
                ageGroup.Equals(mcc.Semantic.AgeGroup, StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
            }

            // Correspondance ton → +15 points (avec support multilingue)
            if (metadata.TryGetValue("tone", out var tone) && 
                !string.IsNullOrEmpty(mcc.Semantic?.Tone) &&
                ValuesMatch(tone, mcc.Semantic.Tone, TONE_ALIASES))
            {
                score += 15;
            }

            // Qualité (rating moyen) → jusqu'à +30 points
            if (mcc.TotalRatings > 0)
            {
                score += (mcc.AverageRating / 5.0) * 30;
            }

            // Popularité (usage) → jusqu'à +15 points
            if (mcc.UsageCount > 0)
            {
                score += Math.Min(Math.Log(mcc.UsageCount + 1) * 5, 15);
            }

            // Bonus pour statut Validated → +10 points
            if (mcc.Status == MCCStatus.Validated)
            {
                score += 10;
            }

            return score;
        }

        #endregion

        #region Statistiques et Filtres

        /// <summary>
        /// Incrémente le compteur d'utilisation d'un MCC
        /// </summary>
        public void IncrementUsage(string mccId)
        {
            if (_library.TryGetValue(mccId, out var mcc))
            {
                mcc.UsageCount++;
                mcc.LastModified = DateTime.Now;
                SaveLibrary();
                
                System.Diagnostics.Debug.WriteLine($"[MCCLibrary] Usage incrémenté : {mcc.Name} (total: {mcc.UsageCount})");
            }
        }

        /// <summary>
        /// Récupère les statistiques globales de la bibliothèque
        /// </summary>
        public Dictionary<string, object> GetStatistics()
        {
            var stats = new Dictionary<string, object>
            {
                ["total_mccs"] = _library.Count,
                ["active_mccs"] = _library.Values.Count(m => m.Status == MCCStatus.Active),
                ["validated_mccs"] = _library.Values.Count(m => m.Status == MCCStatus.Validated),
                ["deprecated_mccs"] = _library.Values.Count(m => m.Status == MCCStatus.Deprecated),
                ["draft_mccs"] = _library.Values.Count(m => m.Status == MCCStatus.Draft),
                ["total_usage"] = _library.Values.Sum(m => m.UsageCount)
            };

            // Calculer rating moyen (uniquement MCC avec ratings)
            var ratedMccs = _library.Values.Where(m => m.TotalRatings > 0).ToList();
            if (ratedMccs.Any())
            {
                stats["average_rating"] = ratedMccs.Average(m => m.AverageRating);
                stats["total_ratings"] = ratedMccs.Sum(m => m.TotalRatings);
            }
            else
            {
                stats["average_rating"] = 0.0;
                stats["total_ratings"] = 0;
            }

            return stats;
        }

        /// <summary>
        /// Récupère les MCC par statut
        /// </summary>
        public List<MCCModel> GetMCCsByStatus(MCCStatus status)
        {
            return _library.Values
                .Where(m => m.Status == status)
                .OrderByDescending(m => m.LastModified)
                .ToList();
        }

        /// <summary>
        /// Récupère les MCC les plus utilisés
        /// </summary>
        public List<MCCModel> GetTopUsedMCCs(int count = 10)
        {
            return _library.Values
                .OrderByDescending(m => m.UsageCount)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// Récupère les MCC les mieux notés
        /// </summary>
        public List<MCCModel> GetTopRatedMCCs(int count = 10, int minRatings = 3)
        {
            return _library.Values
                .Where(m => m.TotalRatings >= minRatings)
                .OrderByDescending(m => m.AverageRating)
                .ThenByDescending(m => m.TotalRatings)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// Récupère les MCC récemment créés
        /// </summary>
        public List<MCCModel> GetRecentMCCs(int count = 10)
        {
            return _library.Values
                .OrderByDescending(m => m.Created)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// Recherche de MCC par nom (partiel)
        /// </summary>
        public List<MCCModel> SearchByName(string searchTerm)
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
            {
                return GetAllMCCs();
            }

            return _library.Values
                .Where(m => m.Name.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                .OrderBy(m => m.Name)
                .ToList();
        }

        /// <summary>
        /// Obtient le nombre total de MCC dans la bibliothèque
        /// </summary>
        public int GetCount()
        {
            return _library.Count;
        }

        /// <summary>
        /// Vérifie si un ID de MCC existe
        /// </summary>
        public bool Exists(string mccId)
        {
            return _library.ContainsKey(mccId);
        }

        /// <summary>
        /// Met à jour les statistiques de notation d'un MCC depuis les évaluations de courriers
        /// </summary>
        public (bool success, string message) UpdateMCCRatingStats(string mccId, LetterRatingService ratingService)
        {
            try
            {
                if (!_library.TryGetValue(mccId, out var mcc))
                {
                    return (false, "MCC introuvable");
                }

                if (ratingService == null)
                {
                    return (false, "Service d'évaluation non disponible");
                }

                // Récupérer toutes les évaluations pour ce MCC
                var ratings = ratingService.GetRatingsForMCC(mccId);

                if (ratings.Count == 0)
                {
                    // Aucune évaluation, réinitialiser les stats
                    mcc.AverageRating = 0.0;
                    mcc.TotalRatings = 0;
                }
                else
                {
                    // Calculer les nouvelles statistiques
                    mcc.AverageRating = ratings.Average(r => r.Rating);
                    mcc.TotalRatings = ratings.Count;
                }

                mcc.LastModified = DateTime.Now;

                // Sauvegarder
                var saveResult = SaveLibrary();

                if (saveResult.success)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[MCCLibrary] Stats MCC mises à jour : {mcc.Name} " +
                        $"(moyenne: {mcc.AverageRating:F2}⭐, total: {mcc.TotalRatings} évaluations)"
                    );
                    return (true, $"Statistiques mises à jour : {mcc.AverageRating:F2}⭐ ({mcc.TotalRatings} évaluations)");
                }

                return saveResult;
            }
            catch (Exception ex)
            {
                return (false, $"Erreur lors de la mise à jour des stats : {ex.Message}");
            }
        }

        #endregion

        #region Optimisation IA

        /// <summary>
        /// Optimise un MCC existant avec l'IA
        /// </summary>
        public async Task<(bool success, string message, MCCOptimizationResponse? response)> OptimizeMCCAsync(
            string mccId, ILLMService llmService)
        {
            try
            {
                if (!_library.TryGetValue(mccId, out var mcc))
                {
                    return (false, "MCC introuvable", null);
                }

                if (llmService == null)
                {
                    return (false, "Service IA non disponible", null);
                }

                // Construire le prompt d'optimisation
                var prompt = BuildOptimizationPrompt(mcc);

                System.Diagnostics.Debug.WriteLine($"[MCCLibrary] Envoi du prompt d'optimisation pour MCC '{mcc.Name}'");
                System.Diagnostics.Debug.WriteLine($"[MCCLibrary] Template original length: {mcc.TemplateMarkdown?.Length ?? 0} caractères");

                // Appeler l'IA
                var (success, response, error) = await llmService.GenerateTextAsync(prompt, maxTokens: 3000);

                if (!success || string.IsNullOrEmpty(response))
                {
                    return (false, $"Erreur IA: {error}", null);
                }

                System.Diagnostics.Debug.WriteLine($"[MCCLibrary] Réponse IA reçue, length: {response.Length} caractères");
                System.Diagnostics.Debug.WriteLine($"[MCCLibrary] Premiers 200 caractères: {response.Substring(0, Math.Min(200, response.Length))}");

                // Parser la réponse JSON
                var optimizationResponse = ParseOptimizationResponse(response, mcc);
                if (optimizationResponse == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[MCCLibrary] Échec du parsing de la réponse JSON");
                    return (false, "Impossible de parser la réponse JSON de l'IA", null);
                }

                System.Diagnostics.Debug.WriteLine($"[MCCLibrary] Parsing réussi, template optimisé length: {optimizationResponse.TemplateMarkdown?.Length ?? 0} caractères");

                // Valider la réponse
                var validationResult = ValidateOptimizationResponse(optimizationResponse);
                if (!validationResult.isValid)
                {
                    System.Diagnostics.Debug.WriteLine($"[MCCLibrary] Validation échouée: {validationResult.errorMessage}");
                    return (false, $"Réponse invalide: {validationResult.errorMessage}", null);
                }

                System.Diagnostics.Debug.WriteLine($"[MCCLibrary] Optimisation réussie pour MCC '{mcc.Name}'");

                return (true, "MCC optimisé avec succès", optimizationResponse);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MCCLibrary] Erreur optimisation: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[MCCLibrary] Stack trace: {ex.StackTrace}");
                return (false, $"Erreur lors de l'optimisation: {ex.Message}", null);
            }
        }

        /// <summary>
        /// Applique les optimisations à un MCC
        /// </summary>
        public (bool success, string message) ApplyOptimization(string mccId, MCCOptimizationResponse optimization)
        {
            try
            {
                if (!_library.TryGetValue(mccId, out var mcc))
                {
                    return (false, "MCC introuvable");
                }

                // Appliquer les changements
                mcc.TemplateMarkdown = optimization.TemplateMarkdown;
                mcc.Keywords = optimization.Keywords ?? new List<string>();
                mcc.Semantic = optimization.Semantic ?? new SemanticAnalysis();

                // Incrémenter la version
                mcc.Version++;
                mcc.LastModified = DateTime.Now;

                // Sauvegarder
                var saveResult = SaveLibrary();

                if (saveResult.success)
                {
                    System.Diagnostics.Debug.WriteLine($"[MCCLibrary] Optimisation appliquée: {mcc.Name} (v{mcc.Version})");
                    return (true, $"MCC optimisé avec succès (version {mcc.Version})");
                }

                return saveResult;
            }
            catch (Exception ex)
            {
                return (false, $"Erreur lors de l'application: {ex.Message}");
            }
        }

        /// <summary>
        /// Construit le prompt d'optimisation pour l'IA
        /// </summary>
        private string BuildOptimizationPrompt(MCCModel mcc)
        {
            var placeholders = ExtractPlaceholders(mcc.TemplateMarkdown);
            var placeholdersExample = placeholders.Any()
                ? $"ex. {{{string.Join("}}, {{", placeholders)}}}"
                : "ex. {{Nom_Patient}}, {{Diagnostic}}";

            var prompt = "[INSTRUCTIONS CRITIQUES - OPTIMISATION DE TEMPLATE MÉDICAL]\n\n" +
                "Tu es un assistant spécialisé dans l'optimisation de templates médicaux pour génération automatique.\n\n" +
                "🎯 OBJECTIF PRINCIPAL :\n" +
                "Transformer le template en un format optimisé pour la génération automatique par IA, en marquant clairement les zones où le contenu doit être généré dynamiquement.\n\n" +
                "⚠️ RÈGLES DE NETTOYAGE OBLIGATOIRES :\n" +
                "1. SUPPRIMER TOTALEMENT :\n" +
                "   - Toutes les coordonnées du médecin (adresse, téléphone, email, RPPS, FINESS, etc.)\n" +
                "   - Toutes les dates (date du courrier, date de signature)\n" +
                "   - Toutes les signatures (\"Dr. Nom\", \"Cordialement\", etc.)\n" +
                "   - Tous les en-têtes et pieds de page formatés\n" +
                "   → L'application gère automatiquement ces éléments\n\n" +
                "2. MARQUER LES ZONES DE GÉNÉRATION :\n" +
                "   Remplacer les blocs de texte qui doivent être personnalisés par des marqueurs clairs :\n" +
                "   - [GÉNÉRER: Introduction personnalisée]\n" +
                "   - [GÉNÉRER: Description de la situation actuelle de l'enfant]\n" +
                "   - [GÉNÉRER: Recommandations spécifiques]\n" +
                "   - [GÉNÉRER: Objectifs thérapeutiques]\n" +
                "   - [GÉNÉRER: Conclusion adaptée]\n\n" +
                "3. CONSERVER :\n" +
                $"   - Les placeholders de variables : {placeholdersExample}\n" +
                "   - La structure en sections (## Objet, ## Contexte, etc.)\n" +
                "   - L'objet du courrier s'il est clairement défini\n" +
                "   - Les formules de politesse de début uniquement (\"Madame, Monsieur,\")\n\n" +
                "📝 EXEMPLE DE TRANSFORMATION :\n\n" +
                "AVANT (à nettoyer) :\n" +
                "```\n" +
                "Docteur Jean Dupont\n" +
                "Pédopsychiatre\n" +
                "123 rue Exemple, Paris\n" +
                "Tel: 01.02.03.04.05\n" +
                "RPPS: 12345678\n\n" +
                "Le 15/11/2025\n\n" +
                "Madame, Monsieur,\n" +
                "Je vous écris concernant Lucas, 8 ans...\n" +
                "Cordialement,\n" +
                "Dr. Dupont\n" +
                "```\n\n" +
                "APRÈS (nettoyé et optimisé) :\n" +
                "```\n" +
                "Madame, Monsieur,\n\n" +
                "## Objet\n" +
                "[GÉNÉRER: Objet précis du courrier en fonction du contexte de {{Nom_Prenom_Enfant}}]\n\n" +
                "## Contexte et situation actuelle\n" +
                "[GÉNÉRER: Description détaillée de la situation de {{Nom_Prenom_Enfant}}, âgé(e) de {{Age_Enfant}} ans, incluant le diagnostic {{Diagnostic}} et les difficultés actuelles]\n\n" +
                "## Recommandations\n" +
                "[GÉNÉRER: Liste des recommandations adaptées à la situation, incluant les aménagements suggérés]\n\n" +
                "## Objectifs\n" +
                "[GÉNÉRER: Objectifs thérapeutiques ou éducatifs à atteindre]\n" +
                "```\n\n" +
                "FORMAT DE RÉPONSE JSON OBLIGATOIRE :\n" +
                "{\n" +
                "  \"template_markdown\": \"ICI LE TEMPLATE NETTOYÉ ET OPTIMISÉ\",\n" +
                "  \"keywords\": [\"mot1\", \"mot2\", \"mot3\", \"mot4\", \"mot5\"],\n" +
                "  \"semantic\": {\n" +
                "    \"doc_type\": \"courrier/attestation/compte-rendu\",\n" +
                "    \"audience\": \"school/parents/doctor/institution/judge\",\n" +
                "    \"age_group\": \"0-5/6-11/12-15/16-18\",\n" +
                "    \"tone\": \"caring/clinical/administrative\"\n" +
                "  }\n" +
                "}\n\n" +
                "✅ CRITÈRES DE VALIDATION :\n" +
                "- Template sans aucune coordonnée médicale\n" +
                "- Template sans date ni signature\n" +
                "- Au moins 3 marqueurs [GÉNÉRER: ...] présents\n" +
                "- Tous les placeholders {{Variable}} conservés\n" +
                "- Structure en sections (##) claire\n" +
                "- Exactement 5 mots-clés pertinents\n\n" +
                "TEMPLATE À OPTIMISER :\n\n" +
                $"NOM DU TEMPLATE : {mcc.Name}\n\n" +
                "TEMPLATE ACTUEL (À NETTOYER) :\n" +
                $"{mcc.TemplateMarkdown}\n\n" +
                "MÉTADONNÉES ACTUELLES :\n" +
                $"- Type : {mcc.Semantic?.DocType ?? "Non spécifié"}\n" +
                $"- Audience : {mcc.Semantic?.Audience ?? "Non spécifiée"}\n" +
                $"- Tranche d'âge : {mcc.Semantic?.AgeGroup ?? "Non spécifiée"}\n" +
                $"- Ton : {mcc.Semantic?.Tone ?? "Non spécifié"}\n\n" +
                $"MOTS-CLÉS ACTUELS : {string.Join(", ", mcc.Keywords ?? new List<string>())}\n\n" +
                "⚡ COMMENCE L'OPTIMISATION MAINTENANT !";

            return prompt;
        }

        /// <summary>
        /// Extrait les placeholders du template
        /// </summary>
        private List<string> ExtractPlaceholders(string template)
        {
            if (string.IsNullOrWhiteSpace(template))
                return new List<string>();

            var matches = System.Text.RegularExpressions.Regex.Matches(template, @"\{\{([^}]+)\}\}");
            return matches
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(m => m.Groups[1].Value.Trim())
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// Parse la réponse JSON de l'IA avec fallback sur le template et métadonnées originales
        /// </summary>
        private MCCOptimizationResponse? ParseOptimizationResponse(string response, MCCModel originalMcc)
        {
            try
            {
                // Nettoyer la réponse (enlever les ```json si présents)
                var cleanResponse = response.Trim();
                if (cleanResponse.StartsWith("```json"))
                {
                    cleanResponse = cleanResponse.Substring(7);
                }
                if (cleanResponse.EndsWith("```"))
                {
                    cleanResponse = cleanResponse.Substring(0, cleanResponse.Length - 3);
                }
                cleanResponse = cleanResponse.Trim();

                System.Diagnostics.Debug.WriteLine($"[MCCLibrary] JSON nettoyé pour parsing, length: {cleanResponse.Length}");

                var optimizationResponse = JsonSerializer.Deserialize<MCCOptimizationResponse>(cleanResponse, _jsonOptions);
                
                if (optimizationResponse == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[MCCLibrary] ❌ Parsing JSON a retourné null");
                    return null;
                }

                System.Diagnostics.Debug.WriteLine($"[MCCLibrary] ✅ Parsing JSON réussi");

                // LOGS DÉTAILLÉS pour debugging
                System.Diagnostics.Debug.WriteLine($"[MCCLibrary] Template reçu de l'IA - Length: {optimizationResponse.TemplateMarkdown?.Length ?? 0}");
                if (!string.IsNullOrWhiteSpace(optimizationResponse.TemplateMarkdown))
                {
                    var preview = optimizationResponse.TemplateMarkdown.Substring(0, Math.Min(300, optimizationResponse.TemplateMarkdown.Length));
                    System.Diagnostics.Debug.WriteLine($"[MCCLibrary] Template preview:\n{preview}...");
                }

                // FALLBACK MODIFIÉ : Ne remplacer que si VRAIMENT vide OU trop court (moins de 50 caractères)
                // Un template optimisé acceptable doit avoir au moins une structure minimale
                if (string.IsNullOrWhiteSpace(optimizationResponse.TemplateMarkdown) || 
                    optimizationResponse.TemplateMarkdown.Length < 50)
                {
                    System.Diagnostics.Debug.WriteLine($"[MCCLibrary] ⚠️ FALLBACK activé : template vide ou trop court ({optimizationResponse.TemplateMarkdown?.Length ?? 0} chars), utilisation du template original");
                    optimizationResponse.TemplateMarkdown = originalMcc.TemplateMarkdown;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[MCCLibrary] ✅ Template optimisé accepté ({optimizationResponse.TemplateMarkdown.Length} chars), pas de fallback");
                }

                // FALLBACK : Si les métadonnées sémantiques sont manquantes ou incomplètes, utiliser celles du MCC original
                if (optimizationResponse.Semantic == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[MCCLibrary] FALLBACK activé : semantic null, utilisation des métadonnées originales");
                    optimizationResponse.Semantic = originalMcc.Semantic ?? new SemanticAnalysis();
                }
                else
                {
                    bool hasFallback = false;

                    if (string.IsNullOrWhiteSpace(optimizationResponse.Semantic.DocType))
                    {
                        optimizationResponse.Semantic.DocType = originalMcc.Semantic?.DocType ?? "courrier";
                        hasFallback = true;
                    }

                    if (string.IsNullOrWhiteSpace(optimizationResponse.Semantic.Audience))
                    {
                        optimizationResponse.Semantic.Audience = originalMcc.Semantic?.Audience ?? "mixte";
                        hasFallback = true;
                    }

                    if (string.IsNullOrWhiteSpace(optimizationResponse.Semantic.AgeGroup))
                    {
                        optimizationResponse.Semantic.AgeGroup = originalMcc.Semantic?.AgeGroup ?? "tous";
                        hasFallback = true;
                    }

                    if (string.IsNullOrWhiteSpace(optimizationResponse.Semantic.Tone))
                    {
                        optimizationResponse.Semantic.Tone = originalMcc.Semantic?.Tone ?? "bienveillant";
                        hasFallback = true;
                    }

                    if (hasFallback)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MCCLibrary] FALLBACK activé : métadonnées sémantiques incomplètes, utilisation des valeurs originales");
                    }
                }

                // FALLBACK : Si les mots-clés sont manquants, utiliser ceux du MCC original
                if (optimizationResponse.Keywords == null || optimizationResponse.Keywords.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine($"[MCCLibrary] FALLBACK activé : keywords manquants, utilisation des keywords originaux");
                    optimizationResponse.Keywords = originalMcc.Keywords ?? new List<string> { "clinique", "medical", "pédiatrique", "soin", "santé" };
                }
                else if (optimizationResponse.Keywords.Count < 5)
                {
                    // Compléter avec les keywords originaux si moins de 5
                    var missingCount = 5 - optimizationResponse.Keywords.Count;
                    var originalKeywords = originalMcc.Keywords ?? new List<string>();
                    
                    foreach (var keyword in originalKeywords)
                    {
                        if (!optimizationResponse.Keywords.Contains(keyword))
                        {
                            optimizationResponse.Keywords.Add(keyword);
                            missingCount--;
                            if (missingCount == 0) break;
                        }
                    }

                    // Si toujours pas assez, ajouter des keywords génériques
                    var genericKeywords = new List<string> { "clinique", "médical", "pédiatrique", "soin", "santé", "patient", "consultation" };
                    foreach (var keyword in genericKeywords)
                    {
                        if (missingCount == 0) break;
                        if (!optimizationResponse.Keywords.Contains(keyword))
                        {
                            optimizationResponse.Keywords.Add(keyword);
                            missingCount--;
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"[MCCLibrary] FALLBACK activé : keywords complétés pour atteindre 5");
                }

                return optimizationResponse;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MCCLibrary] Erreur parsing JSON: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Valide la réponse d'optimisation
        /// </summary>
        private (bool isValid, string errorMessage) ValidateOptimizationResponse(MCCOptimizationResponse response)
        {
            if (response == null)
            {
                return (false, "Réponse nulle");
            }

            if (string.IsNullOrWhiteSpace(response.TemplateMarkdown))
            {
                return (false, "Template manquant");
            }

            if (response.Keywords == null || response.Keywords.Count != 5)
            {
                return (false, "Doit contenir exactement 5 mots-clés");
            }

            if (response.Semantic == null)
            {
                return (false, "Analyse sémantique manquante");
            }

            if (string.IsNullOrWhiteSpace(response.Semantic.DocType) ||
                string.IsNullOrWhiteSpace(response.Semantic.Audience) ||
                string.IsNullOrWhiteSpace(response.Semantic.AgeGroup) ||
                string.IsNullOrWhiteSpace(response.Semantic.Tone))
            {
                return (false, "Métadonnées sémantiques incomplètes");
            }

            return (true, string.Empty);
        }

        #endregion
    }
}
