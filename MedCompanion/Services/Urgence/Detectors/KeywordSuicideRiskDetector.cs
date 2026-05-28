using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MedCompanion.Models.Urgences;

namespace MedCompanion.Services.Urgence.Detectors
{
    /// <summary>
    /// Détecteur de transition : marche par recherche de mots-clés/expressions.
    /// Sert UNIQUEMENT à valider la plomberie (signal → log → chip).
    /// Sera remplacé par SuicideRiskDetector LLM en étape 2.
    /// </summary>
    public class KeywordSuicideRiskDetector : IUrgenceDetector
    {
        public string UrgenceType => "risque_suicidaire";
        public string Name        => "KeywordSuicideRiskDetector_v0";

        // Patterns indicatifs — volontairement larges, le LLM affinera en étape 2.
        private static readonly (string pattern, double weight)[] Indicators = new[]
        {
            (@"\bsuicid(e|aire|er)\b",                 0.85),
            (@"\btentative.{0,15}suicide\b",           0.95),
            (@"\bscarification",                       0.75),
            (@"\bautomutil",                           0.70),
            (@"\bidées?\s+noires?\b",                  0.55),
            (@"\bidées?\s+suicidaires?\b",             0.85),
            (@"\bpassage.{0,5}à.{0,5}l'acte\b",        0.70),
            (@"\bpendaison\b",                         0.80),
            (@"\bsauter.{0,10}(pont|fenêtre|train)\b", 0.85),
            (@"\bplus.{0,5}envie.{0,5}vivre\b",        0.75),
            (@"\baimerais.{0,15}(plus\s+être|disparaître|partir)\b", 0.70),
            (@"\bne\s+plus\s+me\s+réveiller\b",        0.70),
            (@"\bsouffrance.{0,10}intolérable\b",      0.55),
        };

        public Task<UrgenceSignal?> DetectAsync(UrgenceNoteContext context, CancellationToken ct = default)
        {
            var text = context.NoteContent ?? "";
            if (string.IsNullOrWhiteSpace(text)) return Task.FromResult<UrgenceSignal?>(null);

            var passages = new List<string>();
            double maxWeight = 0;

            foreach (var (pattern, weight) in Indicators)
            {
                foreach (Match m in Regex.Matches(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    // Capturer une fenêtre de contexte autour du match
                    var start = Math.Max(0, m.Index - 60);
                    var end   = Math.Min(text.Length, m.Index + m.Length + 80);
                    var snippet = text.Substring(start, end - start)
                                      .Replace("\r", " ").Replace("\n", " ").Trim();
                    if (!passages.Any(p => p == snippet)) passages.Add(snippet);
                    if (weight > maxWeight) maxWeight = weight;
                }
            }

            if (passages.Count == 0 || maxWeight < 0.4)
                return Task.FromResult<UrgenceSignal?>(null);

            var signal = new UrgenceSignal
            {
                Type               = UrgenceType,
                PatientNomComplet  = context.PatientNomComplet,
                DetecteurName      = Name,
                DetectionDate      = DateTime.Now,
                Confidence         = maxWeight,
                Passages           = passages.Take(5).ToList(),
                Motif              = "Mots-clés indicatifs détectés (détecteur de transition, validation à confirmer par LLM en étape 2).",
                NoteSourcePath     = context.NoteFilePath
            };
            return Task.FromResult<UrgenceSignal?>(signal);
        }
    }
}
