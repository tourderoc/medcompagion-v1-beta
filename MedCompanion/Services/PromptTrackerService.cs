using System;
using System.Collections.Generic;
using System.Linq;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service de tracking des prompts envoyés à l'IA
    /// Conserve les 10 derniers prompts en mémoire
    /// </summary>
    public class PromptTrackerService
    {
        private readonly List<Models.PromptLogEntry> _history = new();
        private const int MAX_HISTORY = 10; // Limité à 10 prompts
        
        /// <summary>
        /// Événement déclenché quand un nouveau prompt est loggé
        /// </summary>
        public event EventHandler? PromptLogged;
        
        /// <summary>
        /// Enregistre un nouveau prompt dans l'historique
        /// </summary>
        public void LogPrompt(Models.PromptLogEntry entry)
        {
            _history.Insert(0, entry); // Ajouter au début (plus récent)
            
            // Limiter à 10 prompts
            if (_history.Count > MAX_HISTORY)
                _history.RemoveAt(_history.Count - 1);
            
            // Notifier les observateurs
            PromptLogged?.Invoke(this, EventArgs.Empty);
        }
        
        /// <summary>
        /// Récupère le dernier prompt loggé
        /// </summary>
        public Models.PromptLogEntry? GetLastPrompt() => _history.FirstOrDefault();
        
        /// <summary>
        /// Récupère l'historique complet ou filtré par module
        /// </summary>
        public List<Models.PromptLogEntry> GetHistory(string? filterByModule = null)
        {
            if (string.IsNullOrEmpty(filterByModule))
                return _history.ToList();
            
            return _history.Where(e => e.Module.Equals(filterByModule, StringComparison.OrdinalIgnoreCase))
                          .ToList();
        }
        
        /// <summary>
        /// Récupère les statistiques globales
        /// </summary>
        public (int totalPrompts, int totalTokens, double successRate) GetStatistics()
        {
            var total = _history.Count;
            var tokens = _history.Sum(e => e.TokensUsed);
            var successCount = _history.Count(e => e.Success);
            var rate = total > 0 ? (double)successCount / total : 0;
            
            return (total, tokens, rate);
        }
        
        /// <summary>
        /// Vide l'historique
        /// </summary>
        public void Clear()
        {
            _history.Clear();
            PromptLogged?.Invoke(this, EventArgs.Empty);
        }
    }
}
