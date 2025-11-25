using System;
using System.Text;

namespace MedCompanion.Models
{
    /// <summary>
    /// Bundle contenant TOUT le contexte patient pour injection dans les prompts IA
    /// </summary>
    public class PatientContextBundle
    {
        // === Métadonnées Patient ===
        public PatientMetadata? Metadata { get; set; }

        // === Contexte Clinique ===
        /// <summary>
        /// Contenu complet de la synthèse OU des notes (SANS LIMITE)
        /// </summary>
        public string ClinicalContext { get; set; } = "";

        /// <summary>
        /// Type de contexte : "synthèse" ou "notes"
        /// </summary>
        public string ContextType { get; set; } = "";

        // === Demande Utilisateur ===
        /// <summary>
        /// Demande/instruction utilisateur optionnelle
        /// </summary>
        public string? UserRequest { get; set; }

        // === Métadonnées de génération ===
        public DateTime GeneratedAt { get; set; } = DateTime.Now;

        // === Méthodes ===

        /// <summary>
        /// Génère le texte formaté pour injection dans les prompts IA
        /// Format : Infos Patient + Contexte Clinique + Demande Utilisateur
        /// </summary>
        public string ToPromptText()
        {
            var builder = new StringBuilder();

            // 1. Informations Patient
            builder.AppendLine("═══════════════════════════════════════");
            builder.AppendLine("INFORMATIONS PATIENT");
            builder.AppendLine("═══════════════════════════════════════");

            if (Metadata != null)
            {
                builder.AppendLine($"- Nom complet : {Metadata.NomComplet}");

                if (Metadata.Age.HasValue)
                    builder.AppendLine($"- Âge actuel : {Metadata.Age} ans");

                if (!string.IsNullOrEmpty(Metadata.DobFormatted))
                    builder.AppendLine($"- Date de naissance : {Metadata.DobFormatted}");

                if (!string.IsNullOrEmpty(Metadata.Sexe))
                    builder.AppendLine($"- Sexe : {Metadata.Sexe}");

                if (!string.IsNullOrEmpty(Metadata.Ecole))
                    builder.AppendLine($"- École : {Metadata.Ecole}");

                if (!string.IsNullOrEmpty(Metadata.Classe))
                    builder.AppendLine($"- Classe : {Metadata.Classe}");

                // Adresse complète
                if (!string.IsNullOrEmpty(Metadata.AdresseRue) || 
                    !string.IsNullOrEmpty(Metadata.AdresseVille))
                {
                    var adresse = new StringBuilder();
                    if (!string.IsNullOrEmpty(Metadata.AdresseRue))
                        adresse.Append(Metadata.AdresseRue);
                    if (!string.IsNullOrEmpty(Metadata.AdresseCodePostal) || 
                        !string.IsNullOrEmpty(Metadata.AdresseVille))
                    {
                        if (adresse.Length > 0) adresse.Append(", ");
                        if (!string.IsNullOrEmpty(Metadata.AdresseCodePostal))
                            adresse.Append($"{Metadata.AdresseCodePostal} ");
                        if (!string.IsNullOrEmpty(Metadata.AdresseVille))
                            adresse.Append(Metadata.AdresseVille);
                    }
                    builder.AppendLine($"- Adresse : {adresse}");
                }

                // Accompagnant
                if (!string.IsNullOrEmpty(Metadata.AccompagnantNom))
                {
                    var accompagnant = $"{Metadata.AccompagnantPrenom} {Metadata.AccompagnantNom}";
                    if (!string.IsNullOrEmpty(Metadata.AccompagnantLien))
                        accompagnant += $" ({Metadata.AccompagnantLien})";
                    builder.AppendLine($"- Accompagnant : {accompagnant}");
                    
                    if (!string.IsNullOrEmpty(Metadata.AccompagnantTelephone))
                        builder.AppendLine($"- Téléphone accompagnant : {Metadata.AccompagnantTelephone}");
                    
                    if (!string.IsNullOrEmpty(Metadata.AccompagnantEmail))
                        builder.AppendLine($"- Email accompagnant : {Metadata.AccompagnantEmail}");
                }
            }

            builder.AppendLine();

            // 2. Contexte Clinique
            builder.AppendLine("═══════════════════════════════════════");
            builder.AppendLine($"CONTEXTE CLINIQUE ({ContextType.ToUpper()})");
            builder.AppendLine("═══════════════════════════════════════");
            builder.AppendLine(ClinicalContext);
            builder.AppendLine();

            // 3. Demande Utilisateur (si fournie)
            if (!string.IsNullOrEmpty(UserRequest))
            {
                builder.AppendLine("═══════════════════════════════════════");
                builder.AppendLine("DEMANDE UTILISATEUR");
                builder.AppendLine("═══════════════════════════════════════");
                builder.AppendLine(UserRequest);
                builder.AppendLine();
            }

            return builder.ToString();
        }

        /// <summary>
        /// Génère un résumé pour debug/logs
        /// </summary>
        public string ToDebugText()
        {
            var builder = new StringBuilder();
            builder.AppendLine($"[PatientContextBundle - {GeneratedAt:yyyy-MM-dd HH:mm:ss}]");
            builder.AppendLine($"Patient: {Metadata?.NomComplet ?? "N/A"}");
            builder.AppendLine($"Contexte: {ContextType}");
            builder.AppendLine($"Taille contexte clinique: {ClinicalContext?.Length ?? 0} caractères");
            builder.AppendLine($"Demande utilisateur: {(string.IsNullOrEmpty(UserRequest) ? "Non" : "Oui")}");
            return builder.ToString();
        }
    }
}
