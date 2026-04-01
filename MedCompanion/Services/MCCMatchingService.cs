using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service centralisé pour le matching MCC
    /// Orchestre l'analyse IA, la recherche de template et le scoring
    /// </summary>
    public class MCCMatchingService
    {
        private readonly PromptReformulationService _reformulationService;
        private readonly MCCLibraryService _libraryService;
        
        private const double MIN_CONFIDENCE_SCORE = 105.0; // 50% du score maximum (105/210)
        private const double MAX_SCORE = 210.0;

        /// <summary>
        /// Dictionnaire de mapping bilingue pour les types de documents
        /// Permet à l'IA de retourner des types en anglais tout en matchant avec des MCC en français
        /// </summary>
        private static readonly Dictionary<string, List<string>> DOC_TYPE_ALIASES = new()
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
        /// AMÉLIORATION ÉTAPE 2 : Enrichi avec termes courants psychiatrie
        /// </summary>
        private static readonly Dictionary<string, List<string>> AUDIENCE_ALIASES = new()
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
        private static readonly Dictionary<string, List<string>> TONE_ALIASES = new()
        {
            ["caring"] = new() { "caring", "bienveillant", "empathique", "chaleureux" },
            ["clinical"] = new() { "clinical", "clinique", "medical", "technique" },
            ["administrative"] = new() { "administrative", "administratif", "formel", "officiel", "formal" },
            ["educational"] = new() { "educational", "pedagogique", "educatif" },
            ["neutral"] = new() { "neutral", "neutre", "objectif" }
        };

        /// <summary>
        /// Convertit un âge en plage d'âge compatible MCC
        /// </summary>
        private string ConvertAgeToRange(int age)
        {
            if (age >= 0 && age <= 3) return "0-3";
            if (age >= 3 && age <= 6) return "3-6";
            if (age >= 6 && age <= 11) return "6-11";
            if (age >= 12 && age <= 15) return "12-15";
            if (age >= 16) return "16+";
            return "";
        }

        /// <summary>
        /// Vérifie si un âge correspond à une plage d'âge MCC
        /// </summary>
        private bool AgeMatchesRange(string ageValue, string mccAgeRange)
        {
            if (string.IsNullOrEmpty(ageValue) || string.IsNullOrEmpty(mccAgeRange))
                return false;

            // Si c'est déjà une plage, comparer directement
            if (ageValue == mccAgeRange)
                return true;

            // Si c'est un âge numérique, convertir en plage
            if (int.TryParse(ageValue, out int age))
            {
                var ageRange = ConvertAgeToRange(age);
                return ageRange == mccAgeRange;
            }

            // Cas génériques (child, adolescent, etc.)
            var normalized = ageValue.ToLower().Trim();
            switch (normalized)
            {
                case "child":
                case "enfant":
                    return mccAgeRange == "0-3" || mccAgeRange == "3-6" || mccAgeRange == "6-11";
                case "adolescent":
                case "ado":
                    return mccAgeRange == "12-15" || mccAgeRange == "16+";
                case "adult":
                case "adulte":
                    return mccAgeRange == "16+";
                default:
                    return false;
            }
        }

        public MCCMatchingService(
            PromptReformulationService reformulationService,
            MCCLibraryService libraryService)
        {
            _reformulationService = reformulationService ?? throw new ArgumentNullException(nameof(reformulationService));
            _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        }

        /// <summary>
        /// Normalise un type de document en trouvant tous ses alias possibles
        /// </summary>
        private List<string> GetDocTypeAliases(string docType)
        {
            if (string.IsNullOrEmpty(docType))
                return new List<string>();

            var normalized = docType.ToLower().Trim();

            // Chercher dans les alias connus
            foreach (var (key, aliases) in DOC_TYPE_ALIASES)
            {
                if (aliases.Any(a => a.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
                {
                    return aliases;
                }
            }

            // Si pas trouvé, retourner le type original
            return new List<string> { normalized };
        }

        /// <summary>
        /// Vérifie si deux valeurs correspondent (avec support multilingue)
        /// </summary>
        private bool ValuesMatch(string value1, string value2, Dictionary<string, List<string>> aliasDict)
        {
            if (string.IsNullOrEmpty(value1) || string.IsNullOrEmpty(value2))
                return false;

            var normalized1 = value1.ToLower().Trim();
            var normalized2 = value2.ToLower().Trim();

            // Correspondance directe
            if (normalized1 == normalized2)
                return true;

            // Chercher si les deux valeurs appartiennent au même groupe d'alias
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
        /// Dictionnaire de synonymes médicaux pour améliorer le matching des mots-clés
        /// </summary>
        private static readonly Dictionary<string, List<string>> MEDICAL_SYNONYMS = new()
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
            ["trouble"] = new() { "trouble", "difficulte", "probleme", "pathologie", "dysfonction" }
        };

        /// <summary>
        /// Vérifie si deux mots-clés sont synonymes
        /// </summary>
        private bool AreSynonyms(string keyword1, string keyword2)
        {
            if (string.IsNullOrEmpty(keyword1) || string.IsNullOrEmpty(keyword2))
                return false;

            var normalized1 = keyword1.ToLower().Trim();
            var normalized2 = keyword2.ToLower().Trim();

            // Vérifier si les deux mots appartiennent au même groupe de synonymes
            // AMÉLIORATION ÉTAPE 5 : Matching exact dans les synonymes (pas Contains)
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
        /// AMÉLIORATION ÉTAPE 5 : Vérifie si un mot-clé utilisateur correspond à un mot-clé MCC
        /// Matching plus intelligent : mots entiers, préfixes, ou synonymes
        /// Évite les faux positifs comme "attention" matchant "inattention"
        /// </summary>
        private bool KeywordMatches(string userKeyword, string mccKeyword)
        {
            if (string.IsNullOrEmpty(userKeyword) || string.IsNullOrEmpty(mccKeyword))
                return false;

            var user = userKeyword.ToLower().Trim();
            var mcc = mccKeyword.ToLower().Trim();

            // 1. Match exact
            if (user.Equals(mcc, StringComparison.OrdinalIgnoreCase))
                return true;

            // 2. Match par mots composés (ex: "trouble attention" vs "tdah")
            var userWords = user.Split(new[] { ' ', '-', '_', '\'' }, StringSplitOptions.RemoveEmptyEntries);
            var mccWords = mcc.Split(new[] { ' ', '-', '_', '\'' }, StringSplitOptions.RemoveEmptyEntries);

            // Si un mot du user correspond exactement à un mot du MCC
            if (userWords.Any(uw => mccWords.Any(mw => uw.Equals(mw, StringComparison.OrdinalIgnoreCase))))
                return true;

            // 3. Match par préfixe significatif (min 4 caractères pour éviter faux positifs)
            // Ex: "scolarité" matche "scolaire"
            if (user.Length >= 4 && mcc.Length >= 4)
            {
                var minLen = Math.Min(user.Length, mcc.Length);
                var prefixLen = Math.Max(4, minLen - 2); // Au moins 4 chars, ou longueur - 2
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
        /// Analyse une demande utilisateur et trouve le meilleur MCC correspondant
        /// </summary>
        /// <param name="userRequest">Demande de l'utilisateur</param>
        /// <param name="patientContext">Contexte patient optionnel</param>
        /// <returns>Résultat complet du matching avec logs détaillés</returns>
        public async Task<(bool success, MCCMatchResult? result, string? error)> AnalyzeAndMatchAsync(
            string userRequest,
            PatientContext? patientContext = null,
            LetterGenerationOptions? options = null)
        {
            var logs = new List<string>();
            
            try
            {
                logs.Add($"[{DateTime.Now:HH:mm:ss}] 🚀 DÉBUT DU MATCHING MCC");
                logs.Add($"[{DateTime.Now:HH:mm:ss}] 📝 Demande utilisateur : {userRequest}");
                
                if (patientContext != null)
                {
                    var ageInfo = patientContext.Age.HasValue ? $"{patientContext.Age} ans" : "âge non renseigné";
                    logs.Add($"[{DateTime.Now:HH:mm:ss}] 👤 Contexte patient disponible : {patientContext.NomComplet}, {ageInfo}");
                }

                if (options != null)
                {
                    logs.Add($"[{DateTime.Now:HH:mm:ss}] ⚙️ Options fournies - Destinataire: {options.Recipient ?? "Auto"}");
                }

                // ÉTAPE 1 : Analyse sémantique de la demande
                logs.Add($"[{DateTime.Now:HH:mm:ss}] 🧠 Analyse sémantique en cours...");
                
                var (analysisSuccess, analysisResult, analysisError) = 
                    await _reformulationService.AnalyzeLetterRequestAsync(
                        userRequest, 
                        patientContext, 
                        pseudonym: null, 
                        anonContext: null,
                        explicitRecipient: options?.Recipient);

                if (!analysisSuccess || analysisResult == null)
                {
                    logs.Add($"[{DateTime.Now:HH:mm:ss}] ❌ Échec de l'analyse IA : {analysisError}");
                    return (false, null, $"Erreur lors de l'analyse : {analysisError}");
                }

                logs.Add($"[{DateTime.Now:HH:mm:ss}] ✅ Analyse réussie :");
                logs.Add($"    • Type de document : {analysisResult.DocType}");
                logs.Add($"    • Audience : {analysisResult.Audience}");
                logs.Add($"    • Ton : {analysisResult.Tone}");
                logs.Add($"    • Tranche d'âge : {analysisResult.AgeGroup}");
                logs.Add($"    • Mots-clés : {string.Join(", ", analysisResult.Keywords)}");
                logs.Add($"    • Confiance IA : {analysisResult.ConfidenceScore}%");

                // ÉTAPE 2 : Recherche dans la bibliothèque MCC avec support multilingue
                logs.Add($"[{DateTime.Now:HH:mm:ss}] 🔍 Recherche dans la bibliothèque MCC...");
                
                var totalMCCs = _libraryService.GetCount();
                logs.Add($"[{DateTime.Now:HH:mm:ss}] 📚 Nombre total de MCC : {totalMCCs}");

                // Récupérer tous les alias du type de document (support bilingue)
                var docTypeAliases = GetDocTypeAliases(analysisResult.DocType);
                logs.Add($"[{DateTime.Now:HH:mm:ss}] 🌐 Recherche avec alias : {string.Join(", ", docTypeAliases)}");

                var metadata = new Dictionary<string, string>
                {
                    ["audience"] = analysisResult.Audience,
                    ["tone"] = analysisResult.Tone,
                    ["age_group"] = analysisResult.AgeGroup
                };

                // Rechercher avec tous les alias possibles
                var allMatchingMCCs = new List<(MCCModel mcc, double score)>();
                
                foreach (var alias in docTypeAliases)
                {
                    var results = _libraryService.FindBestMatchingMCCs(
                        alias,
                        metadata,
                        analysisResult.Keywords,
                        maxResults: 10 // Plus large pour fusionner ensuite
                    );
                    
                    allMatchingMCCs.AddRange(results);
                }

                // Dédupliquer et trier par score
                var matchingMCCs = allMatchingMCCs
                    .GroupBy(x => x.mcc.Id)
                    .Select(g => g.OrderByDescending(x => x.score).First())
                    .OrderByDescending(x => x.score)
                    .Take(3)
                    .ToList();

                logs.Add($"[{DateTime.Now:HH:mm:ss}] 📊 Candidats trouvés : {matchingMCCs.Count}");

                if (!matchingMCCs.Any())
                {
                    logs.Add($"[{DateTime.Now:HH:mm:ss}] ⚠️ Aucun MCC trouvé pour le type '{analysisResult.DocType}' (alias: {string.Join(", ", docTypeAliases)})");
                    
                    return (true, MCCMatchResult.Failure(
                        $"Aucun MCC disponible pour le type de document '{analysisResult.DocType}'",
                        0,
                        analysisResult,
                        totalMCCs,
                        logs
                    ), null);
                }

                // ÉTAPE 3 : Analyse détaillée des scores pour TOUS les top 3
                logs.Add($"[{DateTime.Now:HH:mm:ss}] 🎯 Analyse des scores (Top 3) :");

                // Construire la liste des top matches avec détails complets
                var topMatches = new List<MCCWithScore>();

                foreach (var (mcc, score) in matchingMCCs)
                {
                    var scorePercent = (score / MAX_SCORE) * 100;
                    var breakdown = CalculateDetailedScore(mcc, metadata, analysisResult.Keywords);

                    logs.Add($"    • {mcc.Name} : {score:F1} pts ({scorePercent:F1}%)");

                    // Log détaillé pour tous les top 3
                    logs.Add($"      Détail du scoring :");
                    foreach (var (criterion, points) in breakdown.OrderByDescending(x => x.Value))
                    {
                        logs.Add($"        - {criterion}: {points:F1} pts");
                    }

                    // Ajouter à la liste des top matches
                    topMatches.Add(new MCCWithScore
                    {
                        MCC = mcc,
                        RawScore = score,
                        NormalizedScore = scorePercent,
                        ScoreBreakdown = breakdown
                    });
                }

                var (bestMCC, bestScore) = matchingMCCs[0];
                var normalizedScore = (bestScore / MAX_SCORE) * 100;

                // ÉTAPE 4 : Vérification du seuil
                logs.Add($"[{DateTime.Now:HH:mm:ss}] 🎲 Vérification du seuil :");
                logs.Add($"    • Score obtenu : {bestScore:F1} pts ({normalizedScore:F1}%)");
                logs.Add($"    • Seuil minimum : {MIN_CONFIDENCE_SCORE} pts ({(MIN_CONFIDENCE_SCORE/MAX_SCORE)*100:F1}%)");

                if (bestScore >= MIN_CONFIDENCE_SCORE)
                {
                    logs.Add($"[{DateTime.Now:HH:mm:ss}] ✅ MATCH RÉUSSI - {topMatches.Count} MCC(s) au-dessus du seuil");

                    var scoreBreakdown = CalculateDetailedScore(bestMCC, metadata, analysisResult.Keywords);

                    return (true, MCCMatchResult.Success(
                        bestMCC,
                        bestScore,
                        analysisResult,
                        scoreBreakdown,
                        logs,
                        topMatches  // ✅ Inclure les 3 meilleurs MCCs
                    ), null);
                }
                else
                {
                    logs.Add($"[{DateTime.Now:HH:mm:ss}] ⚠️ Score insuffisant ({normalizedScore:F1}% < {(MIN_CONFIDENCE_SCORE/MAX_SCORE)*100:F1}%)");
                    logs.Add($"[{DateTime.Now:HH:mm:ss}] 💡 Suggestion : Génération standard sans template MCC");
                    
                    return (true, MCCMatchResult.Failure(
                        $"Score insuffisant : {normalizedScore:F1}% (seuil : {(MIN_CONFIDENCE_SCORE/MAX_SCORE)*100:F1}%)",
                        bestScore,
                        analysisResult,
                        totalMCCs,
                        logs,
                        bestMCC  // 🆕 Passer le meilleur MCC trouvé pour permettre à l'utilisateur de le choisir
                    ), null);
                }
            }
            catch (Exception ex)
            {
                logs.Add($"[{DateTime.Now:HH:mm:ss}] ❌ ERREUR CRITIQUE : {ex.Message}");
                return (false, null, $"Erreur inattendue : {ex.Message}");
            }
        }

        /// <summary>
        /// Calcule le score détaillé pour le debug
        /// </summary>
        private Dictionary<string, double> CalculateDetailedScore(
            MCCModel mcc, 
            Dictionary<string, string> metadata,
            List<string> keywords)
        {
            var breakdown = new Dictionary<string, double>();

            // Type de document (déjà filtré, toujours 50)
            breakdown["Type de document"] = 50;

            // Correspondance mots-clés (avec support de synonymes)
            // AMÉLIORATION ÉTAPE 5 : Utilise KeywordMatches pour éviter faux positifs
            if (keywords != null && keywords.Any())
            {
                var mccKeywords = (mcc.Keywords ?? new List<string>())
                    .Select(k => k.ToLower())
                    .ToList();

                var matchingKeywords = 0;
                foreach (var keyword in keywords)
                {
                    // Matching intelligent : exact, mots composés, préfixes, ou synonymes
                    if (mccKeywords.Any(mk => KeywordMatches(keyword, mk)))
                    {
                        matchingKeywords++;
                    }
                }

                if (mccKeywords.Any())
                {
                    var keywordMatchRatio = (double)matchingKeywords / Math.Max(keywords.Count, mccKeywords.Count);
                    breakdown["Mots-clés"] = keywordMatchRatio * 40;
                }
                else
                {
                    breakdown["Mots-clés"] = 0;
                }
            }

            // Correspondance audience (avec support multilingue)
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

            // Correspondance tranche d'âge (avec support de plages numériques)
            if (metadata.TryGetValue("age_group", out var ageGroup) && 
                !string.IsNullOrEmpty(mcc.Semantic?.AgeGroup))
            {
                if (AgeMatchesRange(ageGroup, mcc.Semantic.AgeGroup))
                {
                    breakdown["Tranche d'âge"] = 20;
                }
                else
                {
                    breakdown["Tranche d'âge"] = 0;
                }
            }
            else
            {
                breakdown["Tranche d'âge"] = 0;
            }

            // Correspondance ton (avec support multilingue)
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

            // Qualité (rating moyen) - AMÉLIORATION ÉTAPE 6 : Moyenne bayésienne
            // Évite qu'un MCC avec 1 seul vote 5★ soit favorisé sur un MCC avec 100 votes 4.5★
            const double PRIOR_RATING = 3.5;  // Note "neutre" a priori
            const int PRIOR_WEIGHT = 5;       // Équivalent à 5 votes de confiance

            if (mcc.TotalRatings > 0)
            {
                // Bayesian average : plus de votes = plus de confiance dans la note
                double bayesianRating = (mcc.AverageRating * mcc.TotalRatings + PRIOR_RATING * PRIOR_WEIGHT)
                                        / (mcc.TotalRatings + PRIOR_WEIGHT);
                breakdown["Qualité (notes)"] = (bayesianRating / 5.0) * 30;
            }
            else
            {
                // Pas de votes → note neutre (prior)
                breakdown["Qualité (notes)"] = (PRIOR_RATING / 5.0) * 30; // = 21 pts
            }

            // Popularité (usage) - AMÉLIORATION ÉTAPE 7 : Courbe plus progressive
            // Sqrt au lieu de Log pour mieux différencier les MCCs très utilisés
            // Log: 20 usages → 15 pts (max atteint trop vite)
            // Sqrt: 20 usages → 6.7 pts, 100 usages → 15 pts
            if (mcc.UsageCount > 0)
            {
                breakdown["Popularité (usage)"] = Math.Min(Math.Sqrt(mcc.UsageCount) * 1.5, 15);
            }
            else
            {
                breakdown["Popularité (usage)"] = 0;
            }

            // Bonus pour statut Validated
            if (mcc.Status == MCCStatus.Validated)
            {
                breakdown["Statut validé"] = 10;
            }
            else
            {
                breakdown["Statut validé"] = 0;
            }

            return breakdown;
        }

        /// <summary>
        /// Affiche les logs de matching dans la console de debug
        /// </summary>
        public void PrintMatchingLogs(MCCMatchResult result)
        {
            if (result?.MatchingLogs == null) return;

            foreach (var log in result.MatchingLogs)
            {
                System.Diagnostics.Debug.WriteLine(log);
            }
        }
    }
}
