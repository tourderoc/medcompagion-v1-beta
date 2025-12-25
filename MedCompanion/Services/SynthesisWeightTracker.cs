using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using MedCompanion.Models;

namespace MedCompanion.Services;

/// <summary>
/// Service de gestion des poids de pertinence pour la mise à jour de la synthèse patient
/// </summary>
public class SynthesisWeightTracker
{
    private readonly PathService _pathService;

    public SynthesisWeightTracker(PathService pathService)
    {
        _pathService = pathService;
    }

    /// <summary>
    /// Événement déclenché quand le poids d'un patient est mis à jour
    /// </summary>
    public event EventHandler<string>? WeightUpdated;

    /// <summary>
    /// Enregistre le poids d'un nouveau contenu
    /// </summary>
    public void RecordContentWeight(
        string patientName,
        string itemType,
        string filePath,
        double weight,
        string? justification = null)
    {
        var tracker = LoadTracker(patientName);

        tracker.PendingItems.Add(new ContentRelevanceScore
        {
            ItemId = Guid.NewGuid().ToString(),
            ItemType = itemType,
            FilePath = filePath,
            DateAdded = DateTime.Now,
            RelevanceWeight = weight,
            Justification = justification ?? $"Poids par défaut ({itemType})",
            IncludedInSynthesis = false
        });

        tracker.AccumulatedWeight += weight;
        tracker.TotalItemsSinceLastUpdate++;

        SaveTracker(patientName, tracker);

        System.Diagnostics.Debug.WriteLine(
            $"[SynthesisWeight] {itemType} → +{weight:F1} (total: {tracker.AccumulatedWeight:F1}/1.0)");

        // Notifier les abonnés
        WeightUpdated?.Invoke(this, patientName);
    }

    /// <summary>
    /// Réinitialise le poids après génération de la synthèse
    /// </summary>
    public void ResetAfterSynthesisUpdate(string patientName)
    {
        var tracker = LoadTracker(patientName);

        // Marquer tous les items comme intégrés
        foreach (var item in tracker.PendingItems)
        {
            item.IncludedInSynthesis = true;
        }

        // Reset accumulation
        tracker.AccumulatedWeight = 0.0;
        tracker.LastSynthesisUpdate = DateTime.Now;
        tracker.PendingItems.Clear();
        tracker.TotalItemsSinceLastUpdate = 0;

        SaveTracker(patientName, tracker);

        System.Diagnostics.Debug.WriteLine($"[SynthesisWeight] Reset après mise à jour synthèse");

        // Notifier les abonnés
        WeightUpdated?.Invoke(this, patientName);
    }

    /// <summary>
    /// Vérifie si une mise à jour de la synthèse est recommandée
    /// </summary>
    public (bool shouldUpdate, double currentWeight, List<ContentRelevanceScore> items) CheckUpdateNeeded(
        string patientName,
        double threshold = 1.0)
    {
        var tracker = LoadTracker(patientName);
        bool shouldUpdate = tracker.ShouldUpdateSynthesis(threshold);

        return (shouldUpdate, tracker.AccumulatedWeight, new List<ContentRelevanceScore>(tracker.PendingItems));
    }

    /// <summary>
    /// Récupère le poids accumulé actuel sans vérifier le seuil
    /// </summary>
    public double GetCurrentWeight(string patientName)
    {
        var tracker = LoadTracker(patientName);
        return tracker.AccumulatedWeight;
    }

    /// <summary>
    /// Charge le tracker depuis le fichier JSON
    /// </summary>
    private SynthesisUpdateTracker LoadTracker(string patientName)
    {
        try
        {
            var trackerPath = GetTrackerPath(patientName);

            if (!File.Exists(trackerPath))
            {
                // Créer un nouveau tracker si le fichier n'existe pas
                return new SynthesisUpdateTracker
                {
                    AccumulatedWeight = 0.0,
                    LastSynthesisUpdate = null,
                    PendingItems = new List<ContentRelevanceScore>(),
                    TotalItemsSinceLastUpdate = 0
                };
            }

            var json = File.ReadAllText(trackerPath, Encoding.UTF8);
            var tracker = JsonSerializer.Deserialize<SynthesisUpdateTracker>(json);

            return tracker ?? new SynthesisUpdateTracker();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SynthesisWeight] Erreur chargement tracker: {ex.Message}");
            return new SynthesisUpdateTracker();
        }
    }

    /// <summary>
    /// Sauvegarde le tracker dans le fichier JSON
    /// </summary>
    private void SaveTracker(string patientName, SynthesisUpdateTracker tracker)
    {
        try
        {
            var trackerPath = GetTrackerPath(patientName);

            // S'assurer que le dossier synthese existe
            var syntheseDir = _pathService.GetSyntheseDirectory(patientName);
            _pathService.EnsureDirectoryExists(syntheseDir);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var json = JsonSerializer.Serialize(tracker, options);
            File.WriteAllText(trackerPath, json, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SynthesisWeight] Erreur sauvegarde tracker: {ex.Message}");
        }
    }

    /// <summary>
    /// Retourne le chemin du fichier tracker
    /// </summary>
    private string GetTrackerPath(string patientName)
    {
        var syntheseDir = _pathService.GetSyntheseDirectory(patientName);
        return Path.Combine(syntheseDir, "update_tracker.json");
    }
}
