using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service de gestion de la mémoire intelligente du Chat avec compaction
    /// </summary>
    public class ChatMemoryService
    {
        private readonly OpenAIService _openAIService;

        public ChatMemoryService(OpenAIService openAIService)
        {
            _openAIService = openAIService ?? throw new ArgumentNullException(nameof(openAIService));
        }

        /// <summary>
        /// Compacte les échanges sauvegardés si nécessaire
        /// </summary>
        /// <param name="exchanges">Liste complète des échanges sauvegardés</param>
        /// <param name="threshold">Seuil de caractères pour déclencher la compaction</param>
        /// <param name="keepRecentCount">Nombre d'échanges récents à garder intacts</param>
        /// <returns>(bool compacté, string résumé, List échanges récents)</returns>
        public async Task<(bool wasCompacted, string compactedSummary, List<ChatExchange> recentExchanges)> CompactIfNeededAsync(
            List<ChatExchange> exchanges,
            int threshold)
        {
            if (exchanges == null || exchanges.Count == 0)
            {
                return (false, string.Empty, new List<ChatExchange>());
            }

            // Trier par date (du plus ancien au plus récent)
            var sortedExchanges = exchanges.OrderBy(e => e.Timestamp).ToList();

            // Calculer la taille totale
            var totalSize = CalculateTotalSize(sortedExchanges);

            System.Diagnostics.Debug.WriteLine($"[ChatMemoryService] Total échanges: {sortedExchanges.Count}, Taille: {totalSize} chars, Seuil: {threshold}");

            // Pas besoin de compacter si sous le seuil
            if (totalSize <= threshold)
            {
                System.Diagnostics.Debug.WriteLine($"[ChatMemoryService] Pas de compaction nécessaire (taille < seuil)");
                return (false, string.Empty, sortedExchanges);
            }

            // Compacter TOUS les échanges
            System.Diagnostics.Debug.WriteLine($"[ChatMemoryService] Compaction: {sortedExchanges.Count} échanges → résumé");

            // Générer le résumé de tous les échanges
            var compactedSummary = await CompactExchangesAsync(sortedExchanges);

            // Retourner une liste vide pour les échanges récents (car tout est compacté)
            return (true, compactedSummary, new List<ChatExchange>());
        }

        /// <summary>
        /// Génère un résumé compact des anciens échanges
        /// </summary>
        private async Task<string> CompactExchangesAsync(List<ChatExchange> exchanges)
        {
            if (exchanges.Count == 0)
                return string.Empty;

            // Construire le texte à résumer
            var exchangesText = new StringBuilder();
            exchangesText.AppendLine("ANCIENS ÉCHANGES À RÉSUMER");
            exchangesText.AppendLine("===========================");
            exchangesText.AppendLine();

            foreach (var exchange in exchanges)
            {
                exchangesText.AppendLine($"📅 {exchange.Timestamp:dd/MM/yyyy HH:mm}");
                exchangesText.AppendLine($"❓ Question : {exchange.Question}");
                exchangesText.AppendLine($"💬 Réponse : {exchange.Response}");
                exchangesText.AppendLine();
                exchangesText.AppendLine("─────────────────────────────────────");
                exchangesText.AppendLine();
            }

            // Prompt de SYNTHÈSE CLINIQUE (pas un simple résumé)
            var compactionPrompt = @"Tu es un psychiatre qui doit créer une SYNTHÈSE CLINIQUE GLOBALE à partir de plusieurs échanges avec un patient.

OBJECTIF : Créer un document de synthèse unique et cohérent (PAS une liste d'échanges résumés)

STRUCTURE OBLIGATOIRE :

## 📋 Vue d'Ensemble
- Problématiques principales identifiées
- Contexte global du patient

## 📈 Évolution Observée
- Changements notables au fil du temps
- Progression ou régression des symptômes

## 🔑 Points Clés à Retenir
- Diagnostics confirmés ou évoqués (avec dates exactes)
- Traitements prescrits (médicaments + dosages exacts + dates)
- Événements médicaux importants (hospitalisations, crises, etc.)
- Résultats d'examens ou bilans

## 💡 Recommandations et Suivi
- Éléments nécessitant une attention particulière
- Points à réévaluer lors des prochaines consultations

RÈGLES STRICTES :
- Conserver TOUTES les dates exactes (format DD/MM/YYYY)
- Conserver TOUS les dosages et médicaments avec unités (ex: Risperdal 2mg)
- Être FACTUEL et SYNTHÉTIQUE
- NE PAS répéter les échanges un par un
- Créer une vision GLOBALE et COHÉRENTE

ÉCHANGES SOURCE :

" + exchangesText.ToString();

            try
            {
                var (success, result, error) = await _openAIService.GenerateTextAsync(compactionPrompt, maxTokens: 2000);

                if (success)
                {
                    System.Diagnostics.Debug.WriteLine($"[ChatMemoryService] Résumé généré: {result.Length} chars");
                    return result;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ChatMemoryService] Erreur compaction: {error}");
                    // Fallback : retourner un résumé basique
                    return GenerateFallbackSummary(exchanges);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ChatMemoryService] Exception compaction: {ex.Message}");
                return GenerateFallbackSummary(exchanges);
            }
        }

        /// <summary>
        /// Génère un résumé basique en cas d'erreur de l'IA
        /// </summary>
        private string GenerateFallbackSummary(List<ChatExchange> exchanges)
        {
            var summary = new StringBuilder();
            summary.AppendLine($"📚 RÉSUMÉ ({exchanges.Count} échanges archivés)");
            summary.AppendLine();

            foreach (var exchange in exchanges.Take(5))  // Limiter à 5 premiers
            {
                summary.AppendLine($"• {exchange.Timestamp:dd/MM/yyyy} - {TruncateText(exchange.Question, 100)}");
            }

            if (exchanges.Count > 5)
            {
                summary.AppendLine($"... et {exchanges.Count - 5} autres échanges");
            }

            return summary.ToString();
        }

        /// <summary>
        /// Calcule la taille totale des échanges (en caractères)
        /// </summary>
        private int CalculateTotalSize(List<ChatExchange> exchanges)
        {
            return exchanges.Sum(e => (e.Question?.Length ?? 0) + (e.Response?.Length ?? 0));
        }

        /// <summary>
        /// Tronque un texte à une longueur maximale
        /// </summary>
        private string TruncateText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text;

            return text.Substring(0, maxLength) + "...";
        }
    }
}
