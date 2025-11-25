using System;

namespace MedCompanion.Models
{
    /// <summary>
    /// Options de génération pour personnaliser les courriers générés par l'IA
    /// </summary>
    public class LetterGenerationOptions
    {
        public string Recipient { get; set; }
        public string Tone { get; set; }
        public string Length { get; set; }
        public string Format { get; set; }
        public string PrudenceLevel { get; set; }
        public string Urgency { get; set; }

        /// <summary>
        /// Constructeur avec valeurs par défaut
        /// </summary>
        public LetterGenerationOptions()
        {
            Recipient = "Confrère médecin";
            Tone = "Bienveillant et empathique";
            Length = "Moyen (10-20 lignes)";
            Format = "Paragraphes narratifs";
            PrudenceLevel = "Standard (langage direct)";
            Urgency = "Standard (délai normal)";
        }

        /// <summary>
        /// Génère le texte d'enrichissement du prompt pour l'IA
        /// </summary>
        public string ToPromptEnrichment()
        {
            return $@"

--- Options de génération ---
Destinataire : {Recipient}
Ton : {Tone}
Longueur souhaitée : {Length}
Format de rédaction : {Format}
Niveau de prudence : {PrudenceLevel}
Urgence : {Urgency}
";
        }

        /// <summary>
        /// Retourne une représentation textuelle des options
        /// </summary>
        public override string ToString()
        {
            return $"Destinataire: {Recipient}, Ton: {Tone}, Longueur: {Length}, Format: {Format}, Prudence: {PrudenceLevel}, Urgence: {Urgency}";
        }
    }
}
