using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    /// <summary>
    /// Contexte d'anonymisation pour permettre la désanonymisation ultérieure
    /// Stocke TOUS les mappings : nom, prénom, adresse, ville, école, téléphone, email, etc.
    /// </summary>
    public class AnonymizationContext
    {
        public string RealName { get; set; } = string.Empty;
        public string Pseudonym { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty; // "M" ou "F"

        /// <summary>
        /// Tous les remplacements effectués : clé = texte réel, valeur = placeholder
        /// Ex: "15 rue Victor Hugo" → "[ADRESSE]", "Marseille" → "[VILLE]"
        /// </summary>
        public Dictionary<string, string> Replacements { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Indique si l'anonymisation a été effectuée (true) ou si les données sont restées réelles (false pour Ollama)
        /// </summary>
        public bool WasAnonymized { get; set; } = false;
    }

    /// <summary>
    /// Service responsable de l'anonymisation des données patient avant envoi à l'IA
    /// UNIQUEMENT pour OpenAI (données cloud) - PAS pour LLM locaux (Ollama)
    /// Architecture en 3 phases :
    /// - Phase 1 : Données patient.json (nom, adresse, ville, école, téléphone)
    /// - Phase 2 : Patterns regex (emails, téléphones, codes postaux non connus)
    /// - Phase 3 : LLM local (Ollama) pour détecter entités restantes (optionnel, sur demande)
    /// </summary>
    public class AnonymizationService
    {
        private readonly AppSettings? _settings;
        private readonly string _logLevel = "Debug"; // None, Errors, Info, Debug - CHANGÉ pour debugging

        /// <summary>
        /// Constructeur par défaut (injection optionnelle de settings pour les logs)
        /// </summary>
        public AnonymizationService(AppSettings? settings = null)
        {
            _settings = settings;
        }

        /// <summary>
        /// Détermine si l'anonymisation est nécessaire selon le provider LLM actif.
        ///
        /// Logique :
        /// - Provider OpenAI (cloud) → Anonymiser (true)
        /// - Provider Ollama (local) → Ne pas anonymiser (false)
        /// - Settings non disponibles → Anonymiser par défaut (true - sécurité)
        /// </summary>
        /// <returns>True si l'anonymisation doit être activée, False sinon</returns>
        public bool ShouldAnonymize()
        {
            // Si pas de settings, on anonymise par défaut (principe de sécurité)
            if (_settings == null)
            {
                Log("Info", "Settings non disponibles → Anonymisation activée par défaut (sécurité)");
                return true;
            }

            // Vérifier le provider LLM
            string provider = _settings.LLMProvider ?? "OpenAI";

            if (provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
            {
                // Provider local (Ollama) → Pas besoin d'anonymiser
                Log("Info", $"Provider LOCAL détecté ({provider}) → Anonymisation DÉSACTIVÉE");
                return false;
            }
            else
            {
                // Provider cloud (OpenAI, etc.) → Anonymiser
                Log("Info", $"Provider CLOUD détecté ({provider}) → Anonymisation ACTIVÉE");
                return true;
            }
        }

        /// <summary>
        /// Événement pour remonter les logs à l'UI
        /// </summary>
        public event Action<string, string>? LogMessage;

        /// <summary>
        /// Log un message selon le niveau configuré
        /// </summary>
        private void Log(string level, string message)
        {
            // Propager l'événement pour l'UI
            LogMessage?.Invoke(level, message);

            if (_logLevel == "None") return;

            if (level == "Error" && _logLevel == "Errors")
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                System.Diagnostics.Debug.WriteLine($"[{timestamp}] [Anonymization] [{level}] {message}");
            }
            else if (level == "Info" && (_logLevel == "Info" || _logLevel == "Debug"))
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                System.Diagnostics.Debug.WriteLine($"[{timestamp}] [Anonymization] [{level}] {message}");
            }
            else if (level == "Debug" && _logLevel == "Debug")
            {
                var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
                System.Diagnostics.Debug.WriteLine($"[{timestamp}] [Anonymization] [{level}] {message}");
            }
        }



        /// <summary>
        /// Anonymise un texte (méthode synchrone simple pour rétrocompatibilité)
        /// ⚠️ Cette méthode est simplifiée et n'utilise que Phase 1+2, sans données patient
        /// Pour une anonymisation complète, utilisez AnonymizeAsync() avec PatientMetadata
        /// </summary>
        public (string anonymizedText, AnonymizationContext context) Anonymize(string text, string patientName, string gender)
        {


            var context = new AnonymizationContext { WasAnonymized = true };
            string anonymizedText = text ?? "";

            if (string.IsNullOrWhiteSpace(anonymizedText) || string.IsNullOrWhiteSpace(patientName))
                return (anonymizedText, context);

            // Remplacer le nom du patient (simple)
            anonymizedText = ReplaceWithFuzzy(anonymizedText, patientName, "[NOM_PATIENT]", context);

            // Phase 2 : Patterns regex
            anonymizedText = AnonymizePhase2_Regex(anonymizedText, context);

            return (anonymizedText, context);
        }

        /// <summary>
        /// Anonymise un texte avec les 2 phases :
        /// Phase 1 : Données patient.json (nom, adresse, ville, école, téléphone)
        /// Phase 2 : Patterns regex (emails, téléphones, codes postaux)
        /// </summary>
        public async Task<(string anonymizedText, AnonymizationContext context)> AnonymizeAsync(
            string text,
            PatientMetadata? patientData,
            CancellationToken cancellationToken = default)
        {
            // ✅ VALIDATION des inputs
            if (string.IsNullOrWhiteSpace(text))
            {
                Log("Info", "Texte vide fourni à AnonymizeAsync");
                return (text ?? "", new AnonymizationContext { WasAnonymized = false });
            }

            Log("Info", $"Début anonymisation - Texte: {text.Length} chars");



            var context = new AnonymizationContext { WasAnonymized = true };
            Log("Debug", $"Contexte créé - ID: {context.GetHashCode()}, Replacements.Count={context.Replacements.Count}");

            // Pré-traitement du texte OCR pour corriger les erreurs de reconnaissance
            Log("Debug", "Pré-traitement OCR du texte...");
            string anonymizedText = PreprocessOCRText(text);

            // Phase 1 : Anonymiser les données patient.json
            Log("Info", "Phase 1 : Anonymisation données patient.json");
            Log("Debug", $"PatientData fourni: {(patientData == null ? "NULL" : $"OK (Nom={patientData.Nom}, Prenom={patientData.Prenom})")}");
            anonymizedText = AnonymizePhase1_PatientData(anonymizedText, patientData, context);
            Log("Debug", $"Après Phase 1 - Replacements.Count={context.Replacements.Count}");

            // Phase 2 : Anonymiser avec patterns regex
            Log("Info", "Phase 2 : Anonymisation patterns regex");
            anonymizedText = AnonymizePhase2_Regex(anonymizedText, context);
            Log("Debug", $"Après Phase 2 - Replacements.Count={context.Replacements.Count}");

            Log("Info", $"Anonymisation terminée - {context.Replacements.Count} remplacements");

            // DEBUG: Lister tous les remplacements
            if (context.Replacements.Count > 0)
            {
                Log("Debug", "Liste des remplacements:");
                foreach (var kvp in context.Replacements)
                {
                    Log("Debug", $"  '{kvp.Key}' → '{kvp.Value}'");
                }
            }

            return (anonymizedText, context);
        }

        /// <summary>
        /// Phase 1 : Anonymise les données connues de patient.json avec fuzzy matching
        /// </summary>
        /// <summary>
        /// Phase 1 : Anonymise les données connues de patient.json avec fuzzy matching amélioré
        /// </summary>
        private string AnonymizePhase1_PatientData(string text, PatientMetadata? patientData, AnonymizationContext context)
        {
            if (patientData == null || string.IsNullOrWhiteSpace(text))
                return text;

            string result = text;

            // Helper local pour simplifier
            void Replace(string? value, string placeholder)
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result = ReplaceWithFuzzy(result, value, placeholder, context);
                }
            }

            // --- IDENTITÉ ---
            Replace(patientData.Nom, "[NOM_PATIENT]");
            Replace(patientData.Prenom, "[PRENOM_PATIENT]");
            Replace(patientData.NomComplet, "[NOM_COMPLET]");
            
            // Variantes Nom/Prénom (Majuscules, etc. gérées par regex case insensitive, mais on peut tenter inversé)
            if (!string.IsNullOrWhiteSpace(patientData.Nom) && !string.IsNullOrWhiteSpace(patientData.Prenom))
            {
                Replace($"{patientData.Nom} {patientData.Prenom}", "[NOM_COMPLET]");
            }

            // --- ADRESSE ---
            Replace(patientData.AdresseRue, "[ADRESSE]");
            Replace(patientData.AdresseVille, "[VILLE]");
            Replace(patientData.AdresseCodePostal, "[CODE_POSTAL]");
            
            // Tentative: Combinaison CP + Ville
            if (!string.IsNullOrWhiteSpace(patientData.AdresseCodePostal) && !string.IsNullOrWhiteSpace(patientData.AdresseVille))
            {
                Replace($"{patientData.AdresseCodePostal} {patientData.AdresseVille}", "[ADRESSE_VILLE]");
            }

            // --- SCOLARITÉ ---
            Replace(patientData.Ecole, "[ECOLE]");
            Replace(patientData.Classe, "[CLASSE]");

            // --- NAISSANCE ---
            Replace(patientData.LieuNaissance, "[LIEU_NAISSANCE]");

            // Date de naissance : Gestion robuste des formats (DD/MM/YYYY, DD-MM-YYYY, etc.)
            if (!string.IsNullOrWhiteSpace(patientData.Dob) && DateTime.TryParse(patientData.Dob, out var dobDate))
            {
                // Format standard ISO (YYYY-MM-DD)
                Replace(patientData.Dob, "[DATE_NAISSANCE]");

                // Formats français courants
                string[] dateFormats = { 
                    "dd/MM/yyyy", 
                    "dd-MM-yyyy", 
                    "dd.MM.yyyy", 
                    "dd MM yyyy",
                    "d/M/yyyy",   // 1/2/2025
                    "dd/MM/yy"    // 01/02/25
                };

                foreach (var fmt in dateFormats)
                {
                    Replace(dobDate.ToString(fmt, System.Globalization.CultureInfo.InvariantCulture), "[DATE_NAISSANCE]");
                }
            }

            // --- SÉCURITÉ SOCIALE ---
            Replace(patientData.NumeroSecuriteSociale, "[NUM_SECU]");
            Replace(patientData.NumeroINS, "[NUM_INS]");

            // --- ACCOMPAGNANT ---
            Replace(patientData.AccompagnantNom, "[NOM_ACCOMPAGNANT]");
            Replace(patientData.AccompagnantPrenom, "[PRENOM_ACCOMPAGNANT]");
            
            if (!string.IsNullOrWhiteSpace(patientData.AccompagnantNom) && !string.IsNullOrWhiteSpace(patientData.AccompagnantPrenom))
            {
                Replace($"{patientData.AccompagnantPrenom} {patientData.AccompagnantNom}", "[ACCOMPAGNANT_COMPLET]");
                Replace($"{patientData.AccompagnantNom} {patientData.AccompagnantPrenom}", "[ACCOMPAGNANT_COMPLET]");
            }

            Replace(patientData.AccompagnantTelephone, "[TEL_ACCOMPAGNANT]");
            Replace(patientData.AccompagnantEmail, "[EMAIL_ACCOMPAGNANT]");

            // --- MÉDECINS ---
            Replace(patientData.MedecinTraitantNom, "[MEDECIN_TRAITANT]");
            Replace(patientData.MedecinTraitantPrenom, "[PRENOM_MEDECIN_TRAITANT]");
            if (!string.IsNullOrWhiteSpace(patientData.MedecinTraitantNom) && !string.IsNullOrWhiteSpace(patientData.MedecinTraitantPrenom))
            {
                Replace($"{patientData.MedecinTraitantPrenom} {patientData.MedecinTraitantNom}", "[MEDECIN_TRAITANT_COMPLET]");
                Replace($"Dr {patientData.MedecinTraitantNom}", "[MEDECIN_TRAITANT_NOM]");
            }

            Replace(patientData.MedecinReferentNom, "[MEDECIN_REFERENT]");
            Replace(patientData.MedecinReferentPrenom, "[PRENOM_MEDECIN_REFERENT]");
            if (!string.IsNullOrWhiteSpace(patientData.MedecinReferentNom) && !string.IsNullOrWhiteSpace(patientData.MedecinReferentPrenom))
            {
                Replace($"{patientData.MedecinReferentPrenom} {patientData.MedecinReferentNom}", "[MEDECIN_REFERENT_COMPLET]");
                Replace($"Dr {patientData.MedecinReferentNom}", "[MEDECIN_REFERENT_NOM]");
            }

            return result;
        }

        /// <summary>
        /// Anonymisation avec les données extraites par le LLM + les données patient connues
        /// C'est la méthode la plus robuste (Hybride)
        /// </summary>
        public string AnonymizeWithExtractedData(string text, PIIExtractionResult? extractedData, PatientMetadata? patientData, AnonymizationContext context)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            // 0. Pré-traitement
            string result = PreprocessOCRText(text);
            
            // 1. D'abord les données patient connues (Phase 1 standard)
            result = AnonymizePhase1_PatientData(result, patientData, context);

            // 2. Ensuite les données extraites dynamiquement par le LLM (Transient)
            if (extractedData != null)
            {
                var allEntities = extractedData.GetAllEntities();
                foreach (var entity in allEntities)
                {
                    // Ignorer les entités trop courtes (bruit)
                    if (string.IsNullOrWhiteSpace(entity) || entity.Length < 3) continue;

                    // Tenter de deviner le type d'entité pour le placeholder
                    string placeholder = "[ENTITE_SENSIBLE]";
                    if (extractedData.Noms.Contains(entity)) placeholder = "[NOM]";
                    else if (extractedData.Dates.Contains(entity)) placeholder = "[DATE]";
                    else if (extractedData.Lieux.Contains(entity)) placeholder = "[LIEU]";
                    else if (extractedData.Organisations.Contains(entity)) placeholder = "[ORGANISATION]";

                    // Appliquer le remplacement fuzzy
                    result = ReplaceWithFuzzy(result, entity, placeholder, context);
                }
            }

            // 3. Enfin Phase 2 (Regex)
            result = AnonymizePhase2_Regex(result, context);

            return result;
        }

        /// <summary>
        /// Phase 2 : Détecte et anonymise avec patterns regex (emails, téléphones, codes postaux)
        /// </summary>
        private string AnonymizePhase2_Regex(string text, AnonymizationContext context)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            string result = text;

            int phase2Count = 0;

            // Pattern email
            var emailPattern = @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b";
            result = Regex.Replace(result, emailPattern, match =>
            {
                var email = match.Value;
                if (!context.Replacements.ContainsKey(email))
                {
                    Log("Info", $"Phase 2 - Email détecté: '{email}'");
                    context.Replacements[email] = "[EMAIL]";
                    phase2Count++;
                }
                return "[EMAIL]";
            });

            // Pattern téléphone français (plusieurs formats)
            var phonePatterns = new[]
            {
                @"\b0[1-9](?:[\s.-]?\d{2}){4}\b",  // 01 23 45 67 89 ou 01.23.45.67.89 ou 0123456789
                @"\b\+33\s?[1-9](?:[\s.-]?\d{2}){4}\b"  // +33 1 23 45 67 89
            };

            foreach (var pattern in phonePatterns)
            {
                result = Regex.Replace(result, pattern, match =>
                {
                    var phone = match.Value;
                    if (!context.Replacements.ContainsKey(phone))
                    {
                        Log("Info", $"Phase 2 - Téléphone détecté: '{phone}'");
                        context.Replacements[phone] = "[TELEPHONE]";
                        phase2Count++;
                    }
                    return "[TELEPHONE]";
                });
            }

            // Pattern code postal français (5 chiffres)
            var postalPattern = @"\b\d{5}\b";
            result = Regex.Replace(result, postalPattern, match =>
            {
                var postal = match.Value;
                // Vérifier que ce n'est pas déjà dans les remplacements (éviter de remplacer des nombres aléatoires)
                if (!context.Replacements.ContainsKey(postal) &&
                    int.TryParse(postal, out int postalCode) &&
                    postalCode >= 1000 && postalCode <= 99999)
                {
                    Log("Info", $"Phase 2 - Code postal détecté: '{postal}'");
                    context.Replacements[postal] = "[CODE_POSTAL]";
                    phase2Count++;
                    return "[CODE_POSTAL]";
                }
                return match.Value;
            });

            // Pattern numéro sécurité sociale (15 chiffres)
            var secuPattern = @"\b[12]\s?\d{2}\s?\d{2}\s?\d{2}\s?\d{3}\s?\d{3}\s?\d{2}\b";
            result = Regex.Replace(result, secuPattern, match =>
            {
                var secu = match.Value;
                if (!context.Replacements.ContainsKey(secu))
                {
                    Log("Info", $"Phase 2 - Numéro sécu détecté: '{secu}'");
                    context.Replacements[secu] = "[NUM_SECU]";
                    phase2Count++;
                }
                return "[NUM_SECU]";
            });

            if (phase2Count == 0)
            {
                Log("Info", "Phase 2 - Aucun pattern (Email/Tel/CP/Sécu) détecté");
            }
            else
            {
                Log("Info", $"Phase 2 - Total {phase2Count} remplacements effectués");
            }

            return result;
        }



        /// <summary>
        /// Remplace un texte avec fuzzy matching (tolère petites variations)
        /// </summary>
        private string ReplaceWithFuzzy(string text, string searchValue, string replacement, AnonymizationContext context)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(searchValue))
                return text;

            // Construction du pattern avec word boundaries (\b) intelligentes
            string pattern = Regex.Escape(searchValue);

            // Si commence par un caractère alphanumérique, on impose une limite de mot au début
            if (Regex.IsMatch(searchValue, @"^\w"))
                pattern = @"\b" + pattern;

            // Si finit par un caractère alphanumérique, on impose une limite de mot à la fin
            if (Regex.IsMatch(searchValue, @"\w$"))
                pattern = pattern + @"\b";

            // Vérifier si le pattern existe (avec boundaries)
            if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase))
            {
                // Uniquement logger si c'est une nouvelle découverte
                if (!context.Replacements.ContainsKey(searchValue))
                {
                    Log("Info", $"Phase 1 - Match trouvé: '{searchValue}' -> '{replacement}'");
                    context.Replacements[searchValue] = replacement;

                    // DEBUG: Vérifier que l'ajout a fonctionné
                    Log("Debug", $"Dictionnaire après ajout: Count={context.Replacements.Count}, Contient clé? {context.Replacements.ContainsKey(searchValue)}");
                }

                return Regex.Replace(text, pattern, replacement, RegexOptions.IgnoreCase);
            }

            // TODO: Ajouter fuzzy matching avec distance de Levenshtein si nécessaire
            // Pour l'instant, on se contente du remplacement exact

            return text;
        }

        /// <summary>
        /// Restaure les vraies données dans le texte généré (ré-identification complète)
        /// </summary>
        public string Deanonymize(string text, AnonymizationContext context)
        {
            if (string.IsNullOrWhiteSpace(text) || context == null || !context.WasAnonymized)
            {
                return text;
            }

            string deanonymizedText = text;

            // Remplacer tous les placeholders par les vraies valeurs
            // On inverse le dictionnaire (placeholder → vraie valeur)
            foreach (var kvp in context.Replacements)
            {
                var realValue = kvp.Key;      // La vraie valeur
                var placeholder = kvp.Value;  // Le placeholder

                // Remplacer le placeholder par la vraie valeur
                deanonymizedText = Regex.Replace(
                    deanonymizedText,
                    Regex.Escape(placeholder),
                    realValue,
                    RegexOptions.IgnoreCase
                );
            }

            return deanonymizedText;
        }

        /// <summary>
        /// Pré-traite le texte OCR pour améliorer la détection des entités
        /// Corrige les erreurs courantes d'OCR qui empêchent l'anonymisation
        /// </summary>
        private string PreprocessOCRText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            string cleaned = text;

            // 1. Corriger les erreurs courantes d'OCR français
            cleaned = cleaned.Replace("Prénom", "Prenom");  // Normaliser les accents qui causent des collages
            cleaned = cleaned.Replace("prénom", "prenom");

            // 2. Ajouter des espaces autour des patterns courants qui sont souvent collés
            
            // Pattern: MinusculeMajuscule (ex: PédopsychiatreDate → Pédopsychiatre Date)
            cleaned = Regex.Replace(cleaned, @"([a-zéèêëàâäôöùûüç])([A-ZÉÈÊËÀÂÄÔÖÙÛÜÇ])", "$1 $2");

            // Pattern: MAJUSCULEMajuscule (ex: ORTHOPHONIQUEDr → ORTHOPHONIQUE Dr)
            // On cherche une séquence de MAJ suivies d'une Majuscule+minuscule
            cleaned = Regex.Replace(cleaned, @"([A-ZÉÈÊËÀÂÄÔÖÙÛÜÇ]{2,})([A-ZÉÈÊËÀÂÄÔÖÙÛÜÇ][a-zéèêëàâäôöùûüç]+)", "$1 $2");

            // Pattern: ChiffreLettre (ex: 2025Nom → 2025 Nom)
            cleaned = Regex.Replace(cleaned, @"(\d)([a-zA-ZéèêëàâäôöùûüçÉÈÊËÀÂÄÔÖÙÛÜÇ])", "$1 $2");
            
            // Pattern: LettreChiffre (ex: Nom2025 → Nom 2025) - moins fréquent mais utile
            cleaned = Regex.Replace(cleaned, @"([a-zA-ZéèêëàâäôöùûüçÉÈÊËÀÂÄÔÖÙÛÜÇ])(\d)", "$1 $2");
            
            // Pattern: " :" (deux points collés au mot précédent) → " :"
            cleaned = Regex.Replace(cleaned, @"([a-zA-Z0-9])(:)", "$1 $2");

            // 3. Corriger les doubles espaces créés
            cleaned = Regex.Replace(cleaned, @"\s{2,}", " ");

            // 4. Normaliser les espaces autour de la ponctuation (Règle FR: espace avant : ; ! ? et après , .)
            // On s'assure qu'il y a un espace après , et .
            cleaned = Regex.Replace(cleaned, @"([,.])([a-zA-Z])", "$1 $2");

            Log("Debug", $"OCR preprocessing - Avant: {text.Length} chars, Après: {cleaned.Length} chars");

            return cleaned;
        }
    }
}
