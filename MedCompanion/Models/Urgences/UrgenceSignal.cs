using System;
using System.Collections.Generic;

namespace MedCompanion.Models.Urgences
{
    /// <summary>
    /// Signal d'urgence clinique détecté par un IUrgenceDetector.
    /// Un signal = "il pourrait y avoir quelque chose ici" — JAMAIS un diagnostic
    /// ni un niveau de risque. C'est le médecin qui décide ensuite.
    /// </summary>
    public class UrgenceSignal
    {
        public string Type             { get; set; } = "";   // "risque_suicidaire", "maltraitance", ...
        public string PatientNomComplet { get; set; } = "";
        public string DetecteurName    { get; set; } = "";   // ex: "SuicideRiskDetector_v1"
        public DateTime DetectionDate  { get; set; } = DateTime.Now;
        public double Confidence       { get; set; }          // 0.0 à 1.0
        public List<string> Passages   { get; set; } = new(); // citations exactes de la note
        public string Motif            { get; set; } = "";    // explication courte du détecteur
        public string NoteSourcePath   { get; set; } = "";    // chemin de la note analysée
        public string SignalFilePath   { get; set; } = "";    // chemin où le signal a été persisté
    }

    public enum UrgenceUserAction
    {
        Pending,            // aucune action encore prise
        OuvertEvaluation,   // médecin a ouvert l'évaluation
        Ecarte,             // médecin a écarté le signal
        Timeout             // chip auto-dismissé sans action
    }
}
