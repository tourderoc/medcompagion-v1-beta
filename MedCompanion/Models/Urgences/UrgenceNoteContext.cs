using System;

namespace MedCompanion.Models.Urgences
{
    /// <summary>
    /// Contexte fourni à chaque IUrgenceDetector pour analyser une note de consultation.
    /// Volontairement minimal : juste ce qu'il faut pour décider si une alerte est pertinente.
    /// </summary>
    public class UrgenceNoteContext
    {
        public string   PatientNomComplet   { get; set; } = "";
        public int?     PatientAge          { get; set; }       // âge confirmé (V0b)
        public string   ConsultationType    { get; set; } = ""; // "consultation-premiere" | "consultation-suivi"
        public DateTime ConsultationDate    { get; set; }
        public string   NoteContent         { get; set; } = ""; // markdown brut, YAML header retiré
        public string   NoteFilePath        { get; set; } = ""; // chemin absolu
        public string   MotifConsultation   { get; set; } = ""; // si disponible (motif détecté V0b)
    }
}
