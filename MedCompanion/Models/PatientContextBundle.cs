using System;
using System.Text;
using System.Text.RegularExpressions;
using MedCompanion.Services;

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

        /// <summary>
        /// Pseudonyme optionnel pour anonymisation (remplace le vrai nom dans le contexte)
        /// </summary>
        public string? Pseudonym { get; set; }

        // === Métadonnées de génération ===
        public DateTime GeneratedAt { get; set; } = DateTime.Now;

        // === Méthodes ===

        /// <summary>
        /// Génère le texte formaté pour injection dans les prompts IA
        /// Format : Infos Patient + Contexte Clinique + Demande Utilisateur
        /// </summary>
        /// <param name="pseudonym">Pseudonyme optionnel pour anonymisation (prioritaire sur Pseudonym stocké)</param>
        /// <param name="anonContext">Contexte d'anonymisation pour anonymiser aussi le contenu clinique</param>
        public string ToPromptText(string? pseudonym = null, AnonymizationContext? anonContext = null)
        {
            var builder = new StringBuilder();

            // 1. Informations Patient
            builder.AppendLine("═══════════════════════════════════════");
            builder.AppendLine("INFORMATIONS PATIENT");
            builder.AppendLine("═══════════════════════════════════════");

            if (Metadata != null)
            {
                // ✅ Utiliser le pseudonyme si fourni (pour anonymisation)
                var displayName = pseudonym ?? Pseudonym ?? Metadata.NomComplet;
                builder.AppendLine($"- Nom complet : {displayName}");

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

            // 2. Contexte Clinique (✅ ANONYMISÉ si contexte fourni)
            var clinicalContent = ClinicalContext;

            // ✅ Anonymiser le contenu clinique (synthèse/notes) si contexte d'anonymisation fourni
            if (anonContext != null && !string.IsNullOrEmpty(anonContext.RealName))
            {
                clinicalContent = AnonymizeClinicalContent(clinicalContent, anonContext.RealName, anonContext.Pseudonym);
            }

            builder.AppendLine("═══════════════════════════════════════");
            builder.AppendLine($"CONTEXTE CLINIQUE ({ContextType.ToUpper()})");
            builder.AppendLine("═══════════════════════════════════════");
            builder.AppendLine(clinicalContent);  // ✅ Contenu anonymisé
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

        /// <summary>
        /// Anonymise le contenu clinique en remplaçant le nom réel par le pseudonyme
        /// </summary>
        private string AnonymizeClinicalContent(string content, string realName, string pseudonym)
        {
            if (string.IsNullOrWhiteSpace(content) || string.IsNullOrWhiteSpace(realName))
            {
                return content;
            }

            // Nettoyer le nom réel (enlever M., Mme, etc.)
            string cleanRealName = Regex.Replace(realName, @"^(M\.|Mme|Monsieur|Madame)\s+", "", RegexOptions.IgnoreCase).Trim();

            var realParts = cleanRealName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var pseudoParts = pseudonym.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            string anonymizedContent = content;

            // ✅ AMÉLIORATION : Remplacer toutes les variantes possibles du nom
            if (realParts.Length >= 2)
            {
                // Format 1 : "Nom Prénom" exact (ex: "RIOS Marie Astrid")
                anonymizedContent = Regex.Replace(anonymizedContent, Regex.Escape(cleanRealName), pseudonym, RegexOptions.IgnoreCase);

                // Format 2 : "Prénom Nom" inversé (ex: "Marie Astrid RIOS")
                string reversedName = string.Join(" ", realParts.Reverse());
                anonymizedContent = Regex.Replace(anonymizedContent, Regex.Escape(reversedName), pseudonym, RegexOptions.IgnoreCase);

                // Format 3 : Parties individuelles du nom (prénom et nom de famille)
                if (pseudoParts.Length >= 2)
                {
                    // Remplacer le nom de famille (dernier élément)
                    string realLastName = realParts.Last();
                    string pseudoLastName = pseudoParts.Last();
                    anonymizedContent = Regex.Replace(anonymizedContent, $@"\b{Regex.Escape(realLastName)}\b", pseudoLastName, RegexOptions.IgnoreCase);

                    // Remplacer le prénom (premier élément)
                    string realFirstName = realParts.First();
                    string pseudoFirstName = pseudoParts.First();
                    anonymizedContent = Regex.Replace(anonymizedContent, $@"\b{Regex.Escape(realFirstName)}\b", pseudoFirstName, RegexOptions.IgnoreCase);

                    // Si nom composé (ex: "Marie Astrid"), remplacer aussi les prénoms intermédiaires
                    for (int i = 1; i < realParts.Length - 1; i++)
                    {
                        if (i < pseudoParts.Length - 1)
                        {
                            anonymizedContent = Regex.Replace(
                                anonymizedContent,
                                $@"\b{Regex.Escape(realParts[i])}\b",
                                pseudoParts.Length > i ? pseudoParts[i] : pseudoParts[0],
                                RegexOptions.IgnoreCase);
                        }
                    }
                }
            }
            else
            {
                // Nom simple (un seul mot)
                anonymizedContent = Regex.Replace(anonymizedContent, $@"\b{Regex.Escape(cleanRealName)}\b", pseudonym, RegexOptions.IgnoreCase);
            }

            return anonymizedContent;
        }
    }
}
