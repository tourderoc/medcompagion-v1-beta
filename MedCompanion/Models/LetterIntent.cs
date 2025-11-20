using System.Collections.Generic;

namespace MedCompanion.Models
{
    /// <summary>
    /// Types d'intents détectables pour la génération de courriers
    /// </summary>
    public enum IntentType
    {
        None,
        LetterRequest,      // Demande générique de courrier (ouvre sélecteur)
        CourrierEcole,
        MessageParents,
        Attestation
    }

    /// <summary>
    /// Résultat de la détection d'intent avec métadonnées
    /// </summary>
    public class IntentDetectionResult
    {
        public IntentType Type { get; set; } = IntentType.None;
        public float Confidence { get; set; } = 0.0f;
        public List<string> MatchedKeywords { get; set; } = new List<string>();
        public string ExplanationText { get; set; } = string.Empty;
        public List<string> SuggestedTemplates { get; set; } = new List<string>();
    }
}
