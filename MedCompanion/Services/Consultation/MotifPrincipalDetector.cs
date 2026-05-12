using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using MedCompanion.Models;

namespace MedCompanion.Services.Consultation
{
    /// <summary>
    /// Phase C — MotifPrincipalDetector
    /// Détecte le motif principal à partir des thèmes couverts dans les blocs actifs.
    /// Événement ONE-SHOT : se déclenche une seule fois après le 1er batch Whisper (~90s).
    /// </summary>
    public class MotifPrincipalDetector
    {
        /// <summary>
        /// Événement déclenché une seule fois quand le motif principal est détecté.
        /// Fournit le motif détecté en string.
        /// </summary>
        public event Action<string>? MotifDetected;

        private bool _hasFired;

        /// <summary>
        /// Le motif principal détecté (null si pas encore détecté)
        /// </summary>
        public string? DetectedMotif { get; private set; }

        /// <summary>
        /// Vérifie si le motif principal a été détecté dans les blocs mis à jour.
        /// Appelé après chaque extraction incrémentale.
        /// Se déclenche UNE SEULE FOIS (one-shot).
        /// </summary>
        /// <param name="blocks">Liste des blocs avec thèmes couverts</param>
        public void CheckForMotif(IEnumerable<ConsultationBlock> blocks)
        {
            if (_hasFired) return;

            // Cherche si "motif_principal" est dans les CoveredThemes du bloc histoire_maladie
            var histoireBlock = blocks.FirstOrDefault(b =>
                b.Key == "histoire_maladie" || b.Key == "motif");

            if (histoireBlock == null) return;

            // Le motif est détecté quand le thème "motif_principal" est couvert
            if (!histoireBlock.CoveredThemes.Contains("motif_principal")) return;

            // Extraire le motif du texte libre
            var motif = ExtractMotifFromText(histoireBlock.FreeText);

            if (string.IsNullOrWhiteSpace(motif)) return;

            _hasFired = true;
            DetectedMotif = motif;
            Debug.WriteLine($"[MotifDetector] Motif détecté : {motif}");
            MotifDetected?.Invoke(motif);
        }

        /// <summary>
        /// Vérifie si le motif principal a été détecté dans les blocs ViewModel.
        /// Surcharge pour travailler directement avec les ViewModels.
        /// </summary>
        public void CheckForMotif(IEnumerable<ViewModels.ConsultationBlockViewModel> blockVMs)
        {
            if (_hasFired) return;

            var blocks = blockVMs.Select(vm => new ConsultationBlock
            {
                Key = vm.Key,
                FreeText = vm.FreeText,
                CoveredThemes = new List<string>(vm.CoveredThemes)
            });

            CheckForMotif(blocks);
        }

        /// <summary>
        /// Détection directe avec un motif explicite (ex: saisi manuellement).
        /// </summary>
        public void SetMotifManually(string motif)
        {
            if (_hasFired || string.IsNullOrWhiteSpace(motif)) return;

            _hasFired = true;
            DetectedMotif = motif.Trim();
            Debug.WriteLine($"[MotifDetector] Motif manuel : {DetectedMotif}");
            MotifDetected?.Invoke(DetectedMotif);
        }

        /// <summary>
        /// Réinitialise le détecteur (nouveau patient / nouvelle session).
        /// </summary>
        public void Reset()
        {
            _hasFired = false;
            DetectedMotif = null;
        }

        // ── Extraction du motif depuis le texte ────────────────────────────────

        /// <summary>
        /// Extrait le motif principal du texte libre du bloc histoire_maladie.
        /// Prend la première ligne non vide comme résumé du motif.
        /// </summary>
        private static string ExtractMotifFromText(string freeText)
        {
            if (string.IsNullOrWhiteSpace(freeText))
                return "";

            // Prend la première ligne non vide comme motif
            var lines = freeText.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var firstLine = lines.FirstOrDefault()?.Trim() ?? "";

            // Tronque si trop long (garder juste le motif principal)
            return firstLine.Length > 150 ? firstLine[..150] : firstLine;
        }
    }
}
