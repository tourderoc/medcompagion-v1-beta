using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MedCompanion
{
    /// <summary>
    /// Service de parsing pour extraire les informations patients depuis différents formats
    /// </summary>
    public class ParsingService
    {
        /// <summary>
        /// Résultat du parsing d'un bloc Doctolib
        /// </summary>
        public class DoctolibParseResult
        {
            public bool Success { get; set; }
            public string? Prenom { get; set; }
            public string? Nom { get; set; }
            public string? Sex { get; set; }
            public string? Dob { get; set; }
            public string? AgeText { get; set; }
            public string? RemainingText { get; set; }
        }

        /// <summary>
        /// Parse un bloc de texte au format Doctolib
        /// Format attendu:
        /// Line 1: Prénom
        /// Line 2: né(e) NOM
        /// Line 3: H/F/M, DD/MM/YYYY (âge optionnel)
        /// </summary>
        public DoctolibParseResult ParseDoctolibBlock(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return new DoctolibParseResult { Success = false };
            }

            // Nettoyer agressivement les caractères invisibles (espaces insécables, zero-width, etc.)
            var cleanedInput = CleanInvisibleCharacters(input);
            cleanedInput = cleanedInput.Replace("\r\n", "\n").Replace("\r", "\n").Trim();
            
            // ESSAYER D'ABORD LE FORMAT EN LIGNE UNIQUE AVEC "né(e)"
            // Format 1: "Prénom né(e) NOM H/F/M, DD/MM/YYYY (âge optionnel)"
            var singleLineRegex = new Regex(
                @"^(.+?)\s+n[eé]\(e\)\s+([A-Z\s]+?)\s+([HFM])\s*,\s*(\d{2}[/-]\d{2}[/-]\d{4})(?:\s*\(([^)]+)\))?\s*$",
                RegexOptions.IgnoreCase
            );
            
            var singleLineMatch = singleLineRegex.Match(cleanedInput);
            if (singleLineMatch.Success)
            {
                var result = new DoctolibParseResult { Success = true };
                
                // Groupe 1: Prénom
                result.Prenom = CapitalizeFirstLetter(singleLineMatch.Groups[1].Value.Trim());
                
                // Groupe 2: Nom
                result.Nom = singleLineMatch.Groups[2].Value.Trim().ToUpper();
                
                // Groupe 3: Sexe (mapper M -> H)
                var sex = singleLineMatch.Groups[3].Value.ToUpper();
                result.Sex = (sex == "M") ? "H" : sex;
                
                // Groupe 4: Date (normaliser / ou -)
                result.Dob = singleLineMatch.Groups[4].Value.Replace("-", "/");
                
                // Groupe 5: Âge optionnel
                if (singleLineMatch.Groups[5].Success)
                {
                    result.AgeText = singleLineMatch.Groups[5].Value.Trim();
                }
                
                return result;
            }
            
            // ESSAYER LE FORMAT SANS "né(e)"
            // Format 2: "NOM Prénom H/F/M, DD/MM/YYYY (âge optionnel)"
            var singleLineNoNeeRegex = new Regex(
                @"^([A-Z][A-Z\-]+)\s+([A-Za-zÀ-ÿ][A-Za-zÀ-ÿ\-\s]+?)\s+([HFM])\s*,\s*(\d{2}[/-]\d{2}[/-]\d{4})(?:\s*\(([^)]+)\))?\s*$",
                RegexOptions.None
            );
            
            var singleLineNoNeeMatch = singleLineNoNeeRegex.Match(cleanedInput);
            if (singleLineNoNeeMatch.Success)
            {
                var result = new DoctolibParseResult { Success = true };

                // Groupe 1: NOM (tout en majuscules)
                result.Nom = singleLineNoNeeMatch.Groups[1].Value.Trim().ToUpper();

                // Groupe 2: Prénom
                result.Prenom = CapitalizeFirstLetter(singleLineNoNeeMatch.Groups[2].Value.Trim());

                // Groupe 3: Sexe (mapper M -> H)
                var sex = singleLineNoNeeMatch.Groups[3].Value.ToUpper();
                result.Sex = (sex == "M") ? "H" : sex;

                // Groupe 4: Date (normaliser / ou -)
                result.Dob = singleLineNoNeeMatch.Groups[4].Value.Replace("-", "/");

                // Groupe 5: Âge optionnel
                if (singleLineNoNeeMatch.Groups[5].Success)
                {
                    result.AgeText = singleLineNoNeeMatch.Groups[5].Value.Trim();
                }

                return result;
            }

            // ESSAYER LE FORMAT 2 LIGNES (Doctolib simple)
            // Format 3: "NOM Prénom\nH/F/M, DD/MM/YYYY (âge optionnel)"
            // Exemple: "ABDELKADER Zakaria\nH, 23/09/2015 (10 ans)"
            var twoLineRegex = new Regex(
                @"^([A-Z][A-Z\-]+)\s+([A-Za-zÀ-ÿ][A-Za-zÀ-ÿ\-\s]+?)\s*\n\s*([HFM])\s*,\s*(\d{2}[/-]\d{2}[/-]\d{4})(?:\s*\(([^)]+)\))?\s*$",
                RegexOptions.Multiline
            );

            var twoLineMatch = twoLineRegex.Match(cleanedInput);
            if (twoLineMatch.Success)
            {
                var result = new DoctolibParseResult { Success = true };

                // Groupe 1: NOM (tout en majuscules)
                result.Nom = twoLineMatch.Groups[1].Value.Trim().ToUpper();

                // Groupe 2: Prénom
                result.Prenom = CapitalizeFirstLetter(twoLineMatch.Groups[2].Value.Trim());

                // Groupe 3: Sexe (mapper M -> H)
                var sex = twoLineMatch.Groups[3].Value.ToUpper();
                result.Sex = (sex == "M") ? "H" : sex;

                // Groupe 4: Date (normaliser / ou -)
                result.Dob = twoLineMatch.Groups[4].Value.Replace("-", "/");

                // Groupe 5: Âge optionnel
                if (twoLineMatch.Groups[5].Success)
                {
                    result.AgeText = twoLineMatch.Groups[5].Value.Trim();
                }

                return result;
            }

            // Supprimer les lignes vides en début et fin
            var lines = cleanedInput.Split('\n');
            int firstNonEmpty = 0;
            int lastNonEmpty = lines.Length - 1;
            
            for (int i = 0; i < lines.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(lines[i]))
                {
                    firstNonEmpty = i;
                    break;
                }
            }
            
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                if (!string.IsNullOrWhiteSpace(lines[i]))
                {
                    lastNonEmpty = i;
                    break;
                }
            }
            
            if (firstNonEmpty > lastNonEmpty)
            {
                return new DoctolibParseResult { Success = false };
            }
            
            var relevantLines = new string[lastNonEmpty - firstNonEmpty + 1];
            Array.Copy(lines, firstNonEmpty, relevantLines, 0, relevantLines.Length);

            // On a besoin d'au moins 2 lignes pour le prénom et nom
            if (relevantLines.Length < 2)
            {
                return new DoctolibParseResult { Success = false };
            }

            var multiLineResult = new DoctolibParseResult { Success = true };

            // Line 1: Prénom
            multiLineResult.Prenom = CapitalizeFirstLetter(relevantLines[0].Trim());

            // Line 2: né(e) NOM (avec variantes: ne(e), née, etc.)
            var line2 = relevantLines[1];
            var nomRegex = new Regex(@"^\s*n[eé]\(e\)\s+(.+?)\s*$", RegexOptions.IgnoreCase);
            var nomMatch = nomRegex.Match(line2);
            
            if (!nomMatch.Success)
            {
                // Essayer sans parenthèses: "nee NOM", "née NOM", "ne NOM"
                var nomRegex2 = new Regex(@"^\s*n[eé]{1,2}\s+(.+?)\s*$", RegexOptions.IgnoreCase);
                nomMatch = nomRegex2.Match(line2);
                
                if (!nomMatch.Success)
                {
                    return new DoctolibParseResult { Success = false };
                }
            }
            
            multiLineResult.Nom = nomMatch.Groups[1].Value.Trim().ToUpper();

            // Line 3: H/F/M, DD/MM/YYYY (âge optionnel)
            if (relevantLines.Length >= 3)
            {
                var line3 = relevantLines[2];
                // Regex pour capturer: Sexe, Date (avec / ou -), Âge optionnel
                var dateRegex = new Regex(@"^\s*([HFM])\s*,\s*(\d{2}[/-]\d{2}[/-]\d{4})(?:\s*\(([^)]+)\))?\s*$", RegexOptions.IgnoreCase);
                var dateMatch = dateRegex.Match(line3);
                
                if (dateMatch.Success)
                {
                    var sex = dateMatch.Groups[1].Value.ToUpper();
                    // Mapper M -> H
                    if (sex == "M")
                    {
                        sex = "H";
                    }
                    multiLineResult.Sex = sex;
                    
                    // Normaliser la date au format DD/MM/YYYY
                    var dateStr = dateMatch.Groups[2].Value.Replace("-", "/");
                    multiLineResult.Dob = dateStr;
                    
                    // Âge optionnel
                    if (dateMatch.Groups[3].Success)
                    {
                        multiLineResult.AgeText = dateMatch.Groups[3].Value.Trim();
                    }
                }
            }

            // Collecter le texte restant (lignes après la 3ème)
            if (relevantLines.Length > 3)
            {
                var remainingLines = new string[relevantLines.Length - 3];
                Array.Copy(relevantLines, 3, remainingLines, 0, remainingLines.Length);
                multiLineResult.RemainingText = string.Join("\n", remainingLines).Trim();
            }

            return multiLineResult;
        }

        /// <summary>
        /// Parse un format simple "Prénom Nom" ou "NOM Prénom" depuis la barre de saisie
        /// Détecte automatiquement l'ordre en se basant sur les majuscules
        /// </summary>
        public (string? prenom, string? nom) ParseSimpleFormat(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return (null, null);
            }

            var parts = input.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            
            if (parts.Length >= 2)
            {
                // Détecter l'ordre basé sur les majuscules du premier mot
                var firstWord = parts[0];
                var isFirstWordUpperCase = firstWord == firstWord.ToUpper() && firstWord.Length > 1;
                
                if (isFirstWordUpperCase)
                {
                    // Format "NOM Prenom" (français standard: FROMENTIN David)
                    var nom = parts[0];
                    var prenom = string.Join(" ", parts.Skip(1));
                    return (CapitalizeFirstLetter(prenom), nom.ToUpper());
                }
                else
                {
                    // Format "Prenom Nom" (inversé: David Fromentin)
                    var prenom = string.Join(" ", parts.Take(parts.Length - 1));
                    var nom = parts.Last();
                    return (CapitalizeFirstLetter(prenom), nom.ToUpper());
                }
            }
            
            // Si un seul mot, on considère que c'est le nom
            if (parts.Length == 1)
            {
                return (null, parts[0].ToUpper());
            }

            return (null, null);
        }

        /// <summary>
        /// Met en majuscule la première lettre de chaque mot
        /// </summary>
        private string CapitalizeFirstLetter(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return text;
            }

            var words = text.Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
            var capitalizedWords = new string[words.Length];

            for (int i = 0; i < words.Length; i++)
            {
                var word = words[i];
                if (word.Length > 0)
                {
                    capitalizedWords[i] = char.ToUpper(word[0]) + (word.Length > 1 ? word.Substring(1).ToLower() : "");
                }
            }

            // Reconstruire en préservant les séparateurs d'origine
            if (text.Contains('-'))
            {
                return string.Join("-", capitalizedWords);
            }
            return string.Join(" ", capitalizedWords);
        }

        /// <summary>
        /// Nettoie agressivement les caractères invisibles du clipboard
        /// (espaces insécables, zero-width, balises HTML, etc.)
        /// </summary>
        public static string CleanInvisibleCharacters(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return input;
            }

            // Normalisation Unicode (décomposition puis recomposition)
            var normalized = input.Normalize(System.Text.NormalizationForm.FormC);

            // Supprimer les caractères invisibles courants:
            // \u00A0 = espace insécable (non-breaking space)
            // \u200B = zero-width space
            // \u200C = zero-width non-joiner
            // \u200D = zero-width joiner
            // \u200E = left-to-right mark
            // \u200F = right-to-left mark
            // \uFEFF = zero-width no-break space (BOM)
            // \u2060 = word joiner
            var cleaned = Regex.Replace(normalized, @"[\u00A0\u200B\u200C\u200D\u200E\u200F\uFEFF\u2060]+", " ");

            // Supprimer les espaces multiples (y compris tabs)
            cleaned = Regex.Replace(cleaned, @"[ \t]+", " ");

            // Supprimer les lignes vides multiples
            cleaned = Regex.Replace(cleaned, @"\n\s*\n\s*\n+", "\n\n");

            return cleaned.Trim();
        }
    }
}
