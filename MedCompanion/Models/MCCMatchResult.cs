using System;
using System.Collections.Generic;
using MedCompanion.Services;

namespace MedCompanion.Models
{
    /// <summary>
    /// Représente un MCC avec son score de matching
    /// </summary>
    public class MCCWithScore
    {
        public MCCModel MCC { get; set; }
        public double RawScore { get; set; }
        public double NormalizedScore { get; set; }
        public Dictionary<string, double> ScoreBreakdown { get; set; }
    }

    /// <summary>
    /// Résultat détaillé du matching MCC
    /// </summary>
    public class MCCMatchResult
    {
        /// <summary>
        /// Indique si un MCC pertinent a été trouvé
        /// </summary>
        public bool HasMatch { get; set; }

        /// <summary>
        /// Le MCC sélectionné (null si pas de match)
        /// </summary>
        public MCCModel SelectedMCC { get; set; }

        /// <summary>
        /// Score de matching en points bruts (0-210)
        /// </summary>
        public double RawScore { get; set; }

        /// <summary>
        /// Score normalisé en pourcentage (0-100)
        /// </summary>
        public double NormalizedScore { get; set; }

        /// <summary>
        /// Les 3 meilleurs MCCs trouvés (triés par score décroissant)
        /// </summary>
        public List<MCCWithScore> TopMatches { get; set; } = new List<MCCWithScore>();

        /// <summary>
        /// Métadonnées de l'analyse IA
        /// </summary>
        public LetterAnalysisResult Analysis { get; set; }

        /// <summary>
        /// Logs détaillés du processus de matching
        /// </summary>
        public List<string> MatchingLogs { get; set; } = new List<string>();

        /// <summary>
        /// Détails du scoring (pour debug)
        /// </summary>
        public Dictionary<string, double> ScoreBreakdown { get; set; } = new Dictionary<string, double>();

        /// <summary>
        /// Nombre total de MCC consultés
        /// </summary>
        public int TotalMCCsChecked { get; set; }

        /// <summary>
        /// Raison de l'échec si pas de match
        /// </summary>
        public string FailureReason { get; set; }

        /// <summary>
        /// Constructeur pour un match réussi
        /// </summary>
        public static MCCMatchResult Success(
            MCCModel mcc,
            double rawScore,
            LetterAnalysisResult analysis,
            Dictionary<string, double> scoreBreakdown,
            List<string> logs,
            List<MCCWithScore> topMatches = null)
        {
            return new MCCMatchResult
            {
                HasMatch = true,
                SelectedMCC = mcc,
                RawScore = rawScore,
                NormalizedScore = (rawScore / 210.0) * 100.0,
                Analysis = analysis,
                ScoreBreakdown = scoreBreakdown,
                MatchingLogs = logs,
                TopMatches = topMatches ?? new List<MCCWithScore>()
            };
        }

        /// <summary>
        /// Constructeur pour un échec de matching
        /// </summary>
        public static MCCMatchResult Failure(
            string reason, 
            double bestScore,
            LetterAnalysisResult analysis,
            int totalChecked,
            List<string> logs,
            MCCModel bestMCC = null)  // 🆕 Paramètre optionnel pour le meilleur MCC trouvé
        {
            return new MCCMatchResult
            {
                HasMatch = false,
                SelectedMCC = bestMCC,  // 🆕 Inclure le meilleur MCC même si score insuffisant
                RawScore = bestScore,
                NormalizedScore = (bestScore / 210.0) * 100.0,
                Analysis = analysis,
                FailureReason = reason,
                TotalMCCsChecked = totalChecked,
                MatchingLogs = logs
            };
        }
    }
}
