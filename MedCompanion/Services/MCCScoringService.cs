using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Globalization;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    /// <summary>
    /// AMÉLIORATION ÉTAPE 4 : Service centralisé pour le scoring MCC
    /// Source unique de vérité pour les calculs de score et les dictionnaires d'alias
    /// </summary>
    public class MCCScoringService
    {
        #region Constants

        /// <summary>
        /// Score maximum possible (210 points)
        /// </summary>
        public const double MAX_SCORE = 210.0;

        /// <summary>
        /// Seuil minimum de confiance (50% = 105 points)
        /// </summary>
        public const double MIN_CONFIDENCE_SCORE = 105.0;

        /// <summary>
        /// Paramètres pour la moyenne bayésienne des ratings
        /// </summary>
        private const double PRIOR_RATING = 3.5;
        private const int PRIOR_WEIGHT = 5;

        #endregion

        #region Alias Dictionaries

        /// <summary>
        /// Dictionnaire de mapping bilingue pour les types de documents
        /// </summary>
        public static readonly Dictionary<string, List<string>> DOC_TYPE_ALIASES = new()
        {
            ["school_letter"] = new() { "school_letter", "courrier", "ecole", "scolaire", "courrier_ecole" },
            ["specialist_referral"] = new() { "specialist_referral", "adressage", "confrere", "courrier_confrere" },
            ["certificate"] = new() { "certificate", "certificat", "attestation" },
            ["administrative_letter"] = new() { "administrative_letter", "administratif", "courrier_admin" },
            ["report"] = new() { "report", "compte_rendu", "compte-rendu", "cr" },
            ["prescription_explanation"] = new() { "prescription_explanation", "ordonnance", "explication_ordonnance" }
        };

        /// <summary>
        /// Dictionnaire de mapping bilingue pour les audiences
        /// </summary>
        public static readonly Dictionary<string, List<string>> AUDIENCE_ALIASES = new()
        {
            ["school"] = new() {
                // Termes de base
                "school", "ecole", "scolaire", "enseignant", "professeur",
                // Établissements
                "college", "lycee", "maternelle", "primaire", "etablissement scolaire", "etablissement",
                // Personnel éducatif
                "directeur", "directrice", "instituteur", "institutrice", "maitre", "maitresse",
                "equipe educative", "equipe pedagogique", "corps enseignant",
                // Personnel spécialisé école
                "avs", "aesh", "cpe", "conseiller principal", "vie scolaire",
                "psychologue scolaire", "medecin scolaire", "infirmiere scolaire",
                "rased", "coordonnateur", "referent handicap", "referent"
            },
            ["parents"] = new() {
                "parents", "famille", "parent", "family",
                "mere", "pere", "maman", "papa",
                "tuteur", "tutrice", "representant legal", "responsable legal"
            },
            ["doctor"] = new() {
                // Termes génériques
                "doctor", "medecin", "confrere", "specialiste", "physician", "praticien",
                // Spécialités psy
                "psychiatre", "pedopsychiatre", "psychologue", "neuropsychologue",
                "psychomotricien", "psychomotricienne",
                // Spécialités rééducation
                "orthophoniste", "ergotherapeute", "orthoptiste", "kinesitherapeute",
                // Autres spécialités
                "neurologue", "neuropediatre", "pediatre", "generaliste", "medecin traitant",
                "therapeute", "neuropsychiatre"
            },
            ["institution"] = new() {
                "institution", "administratif", "administration",
                // Organismes handicap/social
                "mdph", "cdaph", "maison departementale",
                // Sécurité sociale
                "cpam", "caf", "securite sociale", "assurance maladie",
                // Autres administrations
                "prefecture", "mairie", "conseil departemental", "ars",
                "organisme", "service", "commission"
            },
            ["judge"] = new() {
                "judge", "juge", "tribunal", "justice", "legal",
                "avocat", "magistrat", "procureur", "greffier",
                "expert judiciaire", "juge des enfants", "juge aux affaires familiales"
            },
            ["mixed"] = new() { "mixed", "mixte", "multiple", "plusieurs destinataires" },
            ["assurance"] = new() { "assurance", "insurance", "mutuelle", "complementaire sante", "prevoyance" }
        };

        /// <summary>
        /// Dictionnaire de mapping bilingue pour les tons
        /// </summary>
        public static readonly Dictionary<string, List<string>> TONE_ALIASES = new()
        {
            ["caring"] = new() { "caring", "bienveillant", "empathique", "chaleureux" },
            ["clinical"] = new() { "clinical", "clinique", "medical", "technique" },
            ["administrative"] = new() { "administrative", "administratif", "formel", "officiel", "formal" },
            ["educational"] = new() { "educational", "pedagogique", "educatif" },
            ["neutral"] = new() { "neutral", "neutre", "objectif" }
        };

        /// <summary>
        /// Patterns partiels pour matching flexible des audiences
        /// </summary>
        public static readonly Dictionary<string, List<string>> AUDIENCE_PATTERNS = new()
        {
            ["school"] = new() { "educa", "scola", "enseign", "ecole", "college", "lycee", "classe", "pedagogiq" },
            ["doctor"] = new() { "medecin", "docteur", "psychiatr", "psycholog", "orthophon", "neurolog", "pediatr", "therapeut" },
            ["institution"] = new() { "mdph", "cpam", "caf", "administ", "prefecture", "mairie", "commission" },
            ["parents"] = new() { "parent", "famille", "tuteur", "mere", "pere", "maman", "papa" },
            ["judge"] = new() { "juge", "justice", "avocat", "tribunal", "legal", "judiciaire" }
        };

        /// <summary>
        /// Dictionnaire de synonymes médicaux pour le matching de mots-clés
        /// </summary>
        public static readonly Dictionary<string, List<string>> MEDICAL_SYNONYMS = new()
        {
            ["ecole"] = new() { "ecole", "scolaire", "scolarite", "etablissement", "classe", "instituteur", "enseignant" },
            ["attention"] = new() { "attention", "concentration", "tdah", "hyperactivite", "inattention" },
            ["anxiete"] = new() { "anxiete", "angoisse", "stress", "peur", "inquietude" },
            ["emotion"] = new() { "emotion", "emotionnel", "affectif", "sentiment", "regulation" },
            ["comportement"] = new() { "comportement", "conduite", "attitude", "agir", "reaction" },
            ["enfant"] = new() { "enfant", "pediatrique", "jeune", "petit", "eleve" },
            ["famille"] = new() { "famille", "familial", "parental", "parent", "foyer" },
            ["accompagnement"] = new() { "accompagnement", "suivi", "soutien", "aide", "prise en charge" },
            ["psychologique"] = new() { "psychologique", "psychique", "mental", "emotionnel", "psy" },
            ["trouble"] = new() { "trouble", "difficulte", "probleme", "pathologie", "dysfonction" },
            ["tsa"] = new() { "tsa", "autisme", "autistique", "spectre autistique", "ted" },
            ["dys"] = new() { "dys", "dyslexie", "dyspraxie", "dyscalculie", "dysgraphie", "dysorthographie" }
        };

        #endregion

        #region Score Calculation

        /// <summary>
        /// Calcule le score détaillé d'un MCC par rapport à une analyse
        /// </summary>
        public Dictionary<string, double> CalculateDetailedScore(
            MCCModel mcc,
            Dictionary<string, string> metadata,
            List<string> keywords)
        {
            var breakdown = new Dictionary<string, double>();

            // Type de document (déjà filtré, toujours 50)
            breakdown["Type de document"] = 50;

            // Correspondance mots-clés (jusqu'à 40 pts)
            breakdown["Mots-clés"] = CalculateKeywordScore(mcc.Keywords, keywords);

            // Correspondance audience (30 pts)
            if (metadata.TryGetValue("audience", out var audience) &&
                !string.IsNullOrEmpty(mcc.Semantic?.Audience) &&
                ValuesMatch(audience, mcc.Semantic.Audience, AUDIENCE_ALIASES))
            {
                breakdown["Audience"] = 30;
            }
            else
            {
                breakdown["Audience"] = 0;
            }

            // Correspondance tranche d'âge (20 pts)
            if (metadata.TryGetValue("age_group", out var ageGroup) &&
                !string.IsNullOrEmpty(mcc.Semantic?.AgeGroup))
            {
                breakdown["Tranche d'âge"] = AgeMatchesRange(ageGroup, mcc.Semantic.AgeGroup) ? 20 : 0;
            }
            else
            {
                breakdown["Tranche d'âge"] = 0;
            }

            // Correspondance ton (15 pts)
            if (metadata.TryGetValue("tone", out var tone) &&
                !string.IsNullOrEmpty(mcc.Semantic?.Tone) &&
                ValuesMatch(tone, mcc.Semantic.Tone, TONE_ALIASES))
            {
                breakdown["Ton"] = 15;
            }
            else
            {
                breakdown["Ton"] = 0;
            }

            // Qualité - Rating bayésien (jusqu'à 30 pts)
            breakdown["Qualité (notes)"] = CalculateRatingScore(mcc.AverageRating, mcc.TotalRatings);

            // Popularité - Sqrt (jusqu'à 15 pts)
            breakdown["Popularité (usage)"] = CalculateUsageScore(mcc.UsageCount);

            // Bonus statut validé (10 pts)
            breakdown["Statut validé"] = mcc.Status == MCCStatus.Validated ? 10 : 0;

            return breakdown;
        }

        /// <summary>
        /// Calcule le score total à partir du breakdown
        /// </summary>
        public double CalculateTotalScore(Dictionary<string, double> breakdown)
        {
            return breakdown.Values.Sum();
        }

        /// <summary>
        /// Calcule le score de mots-clés (jusqu'à 40 pts)
        /// </summary>
        public double CalculateKeywordScore(List<string>? mccKeywords, List<string>? userKeywords)
        {
            if (userKeywords == null || !userKeywords.Any())
                return 0;

            var mccKw = (mccKeywords ?? new List<string>()).Select(k => k.ToLower()).ToList();
            if (!mccKw.Any())
                return 0;

            var matchingKeywords = userKeywords.Count(uk => mccKw.Any(mk => KeywordMatches(uk, mk)));
            var keywordMatchRatio = (double)matchingKeywords / Math.Max(userKeywords.Count, mccKw.Count);

            return keywordMatchRatio * 40;
        }

        /// <summary>
        /// Calcule le score de rating avec moyenne bayésienne (jusqu'à 30 pts)
        /// </summary>
        public double CalculateRatingScore(double averageRating, int totalRatings)
        {
            if (totalRatings > 0)
            {
                double bayesianRating = (averageRating * totalRatings + PRIOR_RATING * PRIOR_WEIGHT)
                                        / (totalRatings + PRIOR_WEIGHT);
                return (bayesianRating / 5.0) * 30;
            }
            else
            {
                // Pas de votes → note neutre
                return (PRIOR_RATING / 5.0) * 30;
            }
        }

        /// <summary>
        /// Calcule le score de popularité avec Sqrt (jusqu'à 15 pts)
        /// </summary>
        public double CalculateUsageScore(int usageCount)
        {
            if (usageCount > 0)
            {
                return Math.Min(Math.Sqrt(usageCount) * 1.5, 15);
            }
            return 0;
        }

        #endregion

        #region Matching Helpers

        /// <summary>
        /// Vérifie si un mot-clé utilisateur correspond à un mot-clé MCC
        /// </summary>
        public bool KeywordMatches(string userKeyword, string mccKeyword)
        {
            if (string.IsNullOrEmpty(userKeyword) || string.IsNullOrEmpty(mccKeyword))
                return false;

            var user = userKeyword.ToLower().Trim();
            var mcc = mccKeyword.ToLower().Trim();

            // 1. Match exact
            if (user.Equals(mcc, StringComparison.OrdinalIgnoreCase))
                return true;

            // 2. Match par mots composés
            var userWords = user.Split(new[] { ' ', '-', '_', '\'' }, StringSplitOptions.RemoveEmptyEntries);
            var mccWords = mcc.Split(new[] { ' ', '-', '_', '\'' }, StringSplitOptions.RemoveEmptyEntries);

            if (userWords.Any(uw => mccWords.Any(mw => uw.Equals(mw, StringComparison.OrdinalIgnoreCase))))
                return true;

            // 3. Match par préfixe significatif (min 4 caractères)
            if (user.Length >= 4 && mcc.Length >= 4)
            {
                var minLen = Math.Min(user.Length, mcc.Length);
                var prefixLen = Math.Max(4, minLen - 2);
                if (user.Substring(0, Math.Min(prefixLen, user.Length))
                        .Equals(mcc.Substring(0, Math.Min(prefixLen, mcc.Length)), StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            // 4. Match par synonymes médicaux
            if (AreSynonyms(user, mcc))
                return true;

            return false;
        }

        /// <summary>
        /// Vérifie si deux mots-clés sont synonymes
        /// </summary>
        public bool AreSynonyms(string keyword1, string keyword2)
        {
            if (string.IsNullOrEmpty(keyword1) || string.IsNullOrEmpty(keyword2))
                return false;

            var normalized1 = keyword1.ToLower().Trim();
            var normalized2 = keyword2.ToLower().Trim();

            foreach (var synonyms in MEDICAL_SYNONYMS.Values)
            {
                var has1 = synonyms.Any(s => s.Equals(normalized1, StringComparison.OrdinalIgnoreCase) ||
                                             normalized1.StartsWith(s, StringComparison.OrdinalIgnoreCase) ||
                                             s.StartsWith(normalized1, StringComparison.OrdinalIgnoreCase));
                var has2 = synonyms.Any(s => s.Equals(normalized2, StringComparison.OrdinalIgnoreCase) ||
                                             normalized2.StartsWith(s, StringComparison.OrdinalIgnoreCase) ||
                                             s.StartsWith(normalized2, StringComparison.OrdinalIgnoreCase));

                if (has1 && has2)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Vérifie si deux valeurs correspondent via les alias
        /// </summary>
        public bool ValuesMatch(string value1, string value2, Dictionary<string, List<string>> aliasDict)
        {
            if (string.IsNullOrEmpty(value1) || string.IsNullOrEmpty(value2))
                return false;

            var normalized1 = value1.ToLower().Trim();
            var normalized2 = value2.ToLower().Trim();

            if (normalized1 == normalized2)
                return true;

            foreach (var aliases in aliasDict.Values)
            {
                var hasValue1 = aliases.Any(a => a.Equals(normalized1, StringComparison.OrdinalIgnoreCase));
                var hasValue2 = aliases.Any(a => a.Equals(normalized2, StringComparison.OrdinalIgnoreCase));

                if (hasValue1 && hasValue2)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Vérifie si un âge correspond à une plage d'âge MCC
        /// </summary>
        public bool AgeMatchesRange(string ageValue, string mccAgeRange)
        {
            if (string.IsNullOrEmpty(ageValue) || string.IsNullOrEmpty(mccAgeRange))
                return false;

            var normalizedAge = ageValue.ToLower().Trim();
            var normalizedMcc = mccAgeRange.ToLower().Trim();

            // Match exact
            if (normalizedAge == normalizedMcc)
                return true;

            // Termes génériques
            var genericMappings = new Dictionary<string, List<string>>
            {
                ["enfant"] = new() { "0-3", "3-6", "6-11" },
                ["adolescent"] = new() { "12-15", "16+" },
                ["adulte"] = new() { "16+" }
            };

            if (genericMappings.TryGetValue(normalizedAge, out var ranges))
            {
                return ranges.Contains(normalizedMcc);
            }

            // Essayer de parser comme un nombre
            if (int.TryParse(normalizedAge, out var age))
            {
                return mccAgeRange switch
                {
                    "0-3" => age >= 0 && age <= 3,
                    "3-6" => age >= 3 && age <= 6,
                    "6-11" => age >= 6 && age <= 11,
                    "12-15" => age >= 12 && age <= 15,
                    "16+" => age >= 16,
                    _ => false
                };
            }

            return false;
        }

        /// <summary>
        /// Vérifie si une audience est compatible via patterns
        /// </summary>
        public bool AudienceMatchesPattern(string targetAudience, string mccAudience)
        {
            // Trouver la catégorie du MCC
            string? mccCategory = null;
            foreach (var (category, aliases) in AUDIENCE_ALIASES)
            {
                if (aliases.Any(a => a.Equals(mccAudience, StringComparison.OrdinalIgnoreCase)))
                {
                    mccCategory = category;
                    break;
                }
            }

            if (mccCategory == null)
            {
                if (AUDIENCE_PATTERNS.ContainsKey(mccAudience))
                    mccCategory = mccAudience;
                else
                    return false;
            }

            if (AUDIENCE_PATTERNS.TryGetValue(mccCategory, out var patterns))
            {
                var normalizedTarget = RemoveAccents(targetAudience.ToLower());
                return patterns.Any(p => normalizedTarget.Contains(p, StringComparison.OrdinalIgnoreCase));
            }

            return false;
        }

        /// <summary>
        /// Retire les accents d'une chaîne
        /// </summary>
        public static string RemoveAccents(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            var normalized = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();

            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                {
                    sb.Append(c);
                }
            }

            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>
        /// Récupère tous les alias d'un type de document
        /// </summary>
        public List<string> GetDocTypeAliases(string docType)
        {
            var normalized = docType.ToLower().Trim();

            foreach (var (key, aliases) in DOC_TYPE_ALIASES)
            {
                if (key == normalized || aliases.Contains(normalized))
                {
                    return aliases;
                }
            }

            return new List<string> { normalized };
        }

        #endregion
    }
}
