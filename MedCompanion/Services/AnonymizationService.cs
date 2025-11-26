using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace MedCompanion.Services
{
    /// <summary>
    /// Contexte d'anonymisation pour permettre la désanonymisation ultérieure
    /// </summary>
    public class AnonymizationContext
    {
        public string RealName { get; set; }
        public string Pseudonym { get; set; }
        public string Gender { get; set; } // "M" ou "F"
        public Dictionary<string, string> Replacements { get; set; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Service responsable de l'anonymisation des données patient avant envoi à l'IA
    /// </summary>
    public class AnonymizationService
    {
        // Pseudonymes réalistes pour maintenir la cohérence grammaticale
        private static readonly List<string> MaleNames = new() 
        { 
            "Alexandre MARTIN", "Thomas BERNARD", "Lucas PETIT", "Maxime ROBERT", "Louis RICHARD" 
        };

        private static readonly List<string> FemaleNames = new() 
        { 
            "Marie DUBOIS", "Sophie MOREAU", "Camille LAURENT", "Léa SIMON", "Chloé MICHEL" 
        };

        private readonly Random _random = new Random();

        /// <summary>
        /// Anonymise un texte en remplaçant le nom du patient par un pseudonyme
        /// </summary>
        public (string anonymizedText, AnonymizationContext context) Anonymize(string text, string patientName, string gender)
        {
            // ✅ CORRECTION : Ne pas bloquer si text est vide, on peut vouloir juste générer un pseudonyme
            if (string.IsNullOrWhiteSpace(patientName))
            {
                return (text, new AnonymizationContext { RealName = "", Pseudonym = "" });
            }

            // 1. Choisir un pseudonyme adapté au genre
            string pseudonym;
            if (string.Equals(gender, "F", StringComparison.OrdinalIgnoreCase) || 
                string.Equals(gender, "Femme", StringComparison.OrdinalIgnoreCase))
            {
                pseudonym = FemaleNames[_random.Next(FemaleNames.Count)];
            }
            else
            {
                pseudonym = MaleNames[_random.Next(MaleNames.Count)];
            }

            // 2. Préparer les variations du nom à remplacer
            var context = new AnonymizationContext
            {
                RealName = patientName,
                Pseudonym = pseudonym,
                Gender = gender
            };

            // Nettoyage basique du nom (enlever M., Mme, etc.)
            string cleanName = Regex.Replace(patientName, @"^(M\.|Mme|Monsieur|Madame)\s+", "", RegexOptions.IgnoreCase).Trim();
            
            // Variations possibles : "Nom Prénom", "Prénom Nom"
            // Note : C'est une heuristique simple. Pour une robustesse totale, il faudrait parser le nom.
            var parts = cleanName.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);


            // ✅ Anonymiser le texte seulement s'il n'est pas vide
            string anonymizedText = text ?? "";

            if (!string.IsNullOrWhiteSpace(anonymizedText))
            {
                // Remplacement du nom complet (prioritaire)
                if (anonymizedText.Contains(cleanName, StringComparison.OrdinalIgnoreCase))
                {
                    anonymizedText = ReplaceCaseInsensitive(anonymizedText, cleanName, pseudonym);
                    context.Replacements[cleanName] = pseudonym;
                }

                // Si le nom a plusieurs parties (ex: Jean DUPONT), essayer de remplacer les parties individuelles
                // ATTENTION : Risqué si le prénom est commun (ex: "Pierre").
                // Pour l'instant, on se concentre sur le nom complet pour éviter les faux positifs.
            }

            return (anonymizedText, context);
        }

        /// <summary>
        /// Restaure le nom réel dans le texte généré
        /// </summary>
        public string Deanonymize(string text, AnonymizationContext context)
        {
            if (string.IsNullOrWhiteSpace(text) || context == null || string.IsNullOrEmpty(context.Pseudonym))
            {
                return text;
            }

            string deanonymizedText = text;

            // Remplacer le pseudonyme par le vrai nom
            deanonymizedText = ReplaceCaseInsensitive(deanonymizedText, context.Pseudonym, context.RealName);

            // Tenter de remplacer les parties du pseudonyme (ex: juste "M. MARTIN" -> "M. DUPONT")
            var pseudoParts = context.Pseudonym.Split(' ');
            var realParts = context.RealName.Split(' ');

            if (pseudoParts.Length >= 2 && realParts.Length >= 2)
            {
                // Remplacer le nom de famille seul (souvent utilisé avec M. ou Mme)
                // Martin -> Dupont
                string pseudoLastName = pseudoParts.Last();
                string realLastName = realParts.Last();
                
                // On remplace seulement si ce n'est pas un mot trop commun, mais les noms de famille choisis sont assez distincts
                deanonymizedText = ReplaceCaseInsensitive(deanonymizedText, pseudoLastName, realLastName);
                
                // Remplacer le prénom seul
                // Alexandre -> Lucas
                string pseudoFirstName = pseudoParts.First();
                string realFirstName = realParts.First();
                deanonymizedText = ReplaceCaseInsensitive(deanonymizedText, pseudoFirstName, realFirstName);
            }

            return deanonymizedText;
        }

        private string ReplaceCaseInsensitive(string input, string oldValue, string newValue)
        {
            return Regex.Replace(input, Regex.Escape(oldValue), newValue, RegexOptions.IgnoreCase);
        }
    }
}
