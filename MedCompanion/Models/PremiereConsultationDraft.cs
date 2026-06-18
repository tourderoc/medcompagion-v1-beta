using System;
using System.Collections.Generic;

namespace MedCompanion.Models
{
    /// <summary>
    /// Brouillon persistant de la 1ère consultation — sauvegardé sur disque pour permettre
    /// de reprendre à n'importe quelle étape (interrogatoire, observations, synthèse initiale)
    /// entre deux sessions ou lors d'une pause intra-séance.
    /// </summary>
    public class PremiereConsultationDraft
    {
        public DateTime DateConsultation  { get; set; }
        public string?  MotifDetecte      { get; set; }
        public int?     AgeConfirme       { get; set; }
        public bool     IsStructureFrozen { get; set; }

        /// <summary>"Saisie" | "Extraction" | "FinalNote"</summary>
        public string InterrogatoireState { get; set; } = "Saisie";

        /// <summary>"saisie" | "clinical" | "synthesis"</summary>
        public string EtapeActive { get; set; } = "saisie";

        public string? TranscriptionInput     { get; set; }
        public string? NoteContent            { get; set; }
        public string? ObservationsNarrative  { get; set; }
        public string? SynthesisContent       { get; set; }

        public List<BlocDraft> Blocs { get; set; } = new();

        public DateTime LastModified { get; set; }
    }

    public class BlocDraft
    {
        public string       Key           { get; set; } = "";
        public string       FreeText      { get; set; } = "";
        public List<string> CoveredThemes { get; set; } = new();
        public bool         IsHidden      { get; set; }
    }
}
