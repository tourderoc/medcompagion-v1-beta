using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service de gestion des attestations médicales
    /// Génération locale sans IA, formulaires simples, export DOCX avec signature
    /// </summary>
    public class AttestationService
    {
        private readonly AppSettings _settings;
        private readonly StorageService _storageService;
        private readonly PathService _pathService;
        private readonly LetterService _letterService;
        private readonly LLMGatewayService _llmGatewayService; // ✅ NOUVEAU : Gateway centralisé
        private readonly PromptConfigService _promptConfigService;
        private readonly PatientContextService _patientContextService;
        private readonly PromptTrackerService? _promptTracker;
        private readonly List<AttestationTemplate> _templates;

        // Cache des prompts pour éviter les appels répétés
        private string _cachedAttestationPrompt;
        private string _cachedSystemPrompt;

        public AttestationService(
            StorageService storageService,
            PathService pathService,
            LetterService letterService,
            LLMGatewayService llmGatewayService, // ✅ MODIFIÉ
            PromptConfigService promptConfigService,
            PatientContextService patientContextService,
            PromptTrackerService? promptTracker = null) // ✅ Optionnel
        {
            _settings = AppSettings.Load();
            _storageService = storageService;
            _pathService = pathService;
            _letterService = letterService;
            _llmGatewayService = llmGatewayService; // ✅ MODIFIÉ
            _promptConfigService = promptConfigService;
            _patientContextService = patientContextService;
            _promptTracker = promptTracker;
            _templates = InitializeTemplates();

            // Charger les prompts initialement
            LoadPrompts();

            // S'abonner à l'événement de rechargement des prompts
            _promptConfigService.PromptsReloaded += OnPromptsReloaded;
        }
        
        /// <summary>
        /// Charge les prompts depuis le service de configuration
        /// </summary>
        private void LoadPrompts()
        {
            _cachedAttestationPrompt = _promptConfigService.GetActivePrompt("attestation_custom_generation");
            _cachedSystemPrompt = _promptConfigService.GetActivePrompt("system_global");
            
            System.Diagnostics.Debug.WriteLine("[AttestationService] Prompts chargés depuis la configuration");
        }
        
        /// <summary>
        /// Gestionnaire d'événement pour le rechargement des prompts
        /// </summary>
        private void OnPromptsReloaded(object? sender, EventArgs e)
        {
            LoadPrompts();
            System.Diagnostics.Debug.WriteLine("[AttestationService] ✅ Prompts rechargés automatiquement suite à une modification");
        }

        /// <summary>
        /// Initialise les 4 modèles d'attestations de base
        /// </summary>
        private List<AttestationTemplate> InitializeTemplates()
        {
            return new List<AttestationTemplate>
            {
                // 1️⃣ ATTESTATION DE PRÉSENCE
                new AttestationTemplate
                {
                    Type = "Presence",
                    DisplayName = "Attestation de présence",
                    Description = "Atteste que l'enfant a été reçu en consultation ce jour",
                    RequiredFields = new List<string> { "Nom_Prenom", "Date_Naissance" },
                    OptionalFields = new List<string> { "Accompagnateur" },
                    Markdown = @"# Attestation de présence

Je soussigné {{Medecin}}, pédopsychiatre, atteste que **{{Nom_Prenom}}**, {{Ne_Nee}} le {{Date_Naissance}}{{Accompagnateur_Text}}, a été reçu(e) en consultation ce jour.

Cette attestation est délivrée pour valoir ce que de droit."
                },

                // 2️⃣ ATTESTATION DE SUIVI
                new AttestationTemplate
                {
                    Type = "Suivi",
                    DisplayName = "Attestation de suivi",
                    Description = "Confirme un suivi pédopsychiatrique en cours",
                    RequiredFields = new List<string> { "Nom_Prenom", "Date_Naissance" },
                    OptionalFields = new List<string> { "Date_Debut_Suivi", "Frequence_Suivi" },
                    Markdown = @"# Attestation de suivi

Je soussigné {{Medecin}}, pédopsychiatre, atteste que **{{Nom_Prenom}}**, {{Ne_Nee}} le {{Date_Naissance}}, bénéficie d'un suivi pédopsychiatrique régulier{{Date_Debut_Suivi_Text}}{{Frequence_Suivi_Text}}.

Cette attestation est délivrée pour valoir ce que de droit."
                },

                // 3️⃣ ARRÊT SCOLAIRE
                new AttestationTemplate
                {
                    Type = "Arret",
                    DisplayName = "Arrêt scolaire",
                    Description = "Justifie un repos ou une absence scolaire",
                    RequiredFields = new List<string> { "Nom_Prenom", "Date_Naissance", "Date_Debut", "Date_Fin" },
                    OptionalFields = new List<string> { "Motif_Arret" },
                    Markdown = @"# Arrêt scolaire

Je soussigné {{Medecin}}, pédopsychiatre, atteste que **{{Nom_Prenom}}**, {{Ne_Nee}} le {{Date_Naissance}}, nécessite un repos scolaire du **{{Date_Debut}}** au **{{Date_Fin}}**{{Motif_Arret_Text}}.

Cette attestation est délivrée pour valoir ce que de droit."
                },

                // 4️⃣ AMÉNAGEMENTS SCOLAIRES
                new AttestationTemplate
                {
                    Type = "Amenagement",
                    DisplayName = "Aménagements scolaires",
                    Description = "Liste d'adaptations scolaires recommandées",
                    RequiredFields = new List<string> { "Nom_Prenom", "Date_Naissance", "Liste_Amenagements" },
                    OptionalFields = new List<string> { "Duree_Amenagements" },
                    Markdown = @"# Aménagements scolaires recommandés

Je soussigné {{Medecin}}, pédopsychiatre, recommande les aménagements suivants pour **{{Nom_Prenom}}**, {{Ne_Nee}} le {{Date_Naissance}} :

{{Liste_Amenagements}}

Ces aménagements visent à favoriser la réussite scolaire et le bien-être de l'enfant{{Duree_Amenagements_Text}}.

Cette attestation est délivrée pour valoir ce que de droit."
                }
            };
        }

        /// <summary>
        /// Récupère tous les types d'attestations disponibles
        /// </summary>
        public List<AttestationTemplate> GetAvailableTemplates()
        {
            return _templates;
        }

        /// <summary>
        /// Récupère un template par son type
        /// </summary>
        public AttestationTemplate? GetTemplate(string type)
        {
            return _templates.FirstOrDefault(t => t.Type.Equals(type, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Détecte les champs manquants pour une attestation donnée
        /// </summary>
        public (List<string> missingRequired, List<string> missingOptional) DetectMissingFields(
            string type,
            PatientMetadata? metadata)
        {
            var template = GetTemplate(type);
            if (template == null)
                return (new List<string>(), new List<string>());

            var missingRequired = new List<string>();
            var missingOptional = new List<string>();

            // Champs toujours disponibles depuis PatientMetadata
            var availableFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            
            if (metadata != null)
            {
                // Construire Nom_Prenom automatiquement si Nom ET Prenom sont disponibles
                if (!string.IsNullOrEmpty(metadata.Nom) && !string.IsNullOrEmpty(metadata.Prenom))
                    availableFields.Add("Nom_Prenom");
                
                if (!string.IsNullOrEmpty(metadata.DobFormatted))
                    availableFields.Add("Date_Naissance");
                
                // Vérifier si le sexe est disponible
                if (!string.IsNullOrEmpty(metadata.Sexe))
                    availableFields.Add("Sexe");
            }

            // Champs toujours disponibles depuis AppSettings
            availableFields.Add("Medecin");
            availableFields.Add("Ville");
            availableFields.Add("Date_Jour");

            // Vérifier les champs requis
            foreach (var field in template.RequiredFields)
            {
                if (!availableFields.Contains(field))
                {
                    missingRequired.Add(field);
                }
            }

            // Vérifier les champs optionnels
            foreach (var field in template.OptionalFields)
            {
                if (!availableFields.Contains(field))
                {
                    missingOptional.Add(field);
                }
            }
            
            // ✅ AJOUT CRUCIAL : Si le sexe n'est pas disponible, l'ajouter comme champ requis
            // Cela permettra au dialogue AttestationInfoDialog de le demander
            if (metadata != null && string.IsNullOrEmpty(metadata.Sexe))
            {
                if (!missingRequired.Contains("Sexe"))
                {
                    missingRequired.Add("Sexe");
                }
            }

            return (missingRequired, missingOptional);
        }

        /// <summary>
        /// Génère une attestation en remplaçant les placeholders
        /// </summary>
        public (bool success, string markdown, string error) GenerateAttestation(
            string type,
            Dictionary<string, string> userFields,
            PatientMetadata? metadata)
        {
            try
            {
                var template = GetTemplate(type);
                if (template == null)
                    return (false, string.Empty, $"Type d'attestation inconnu : {type}");

                // Fusionner tous les champs disponibles
                var allFields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // 1. Champs depuis AppSettings (toujours disponibles)
                allFields["Medecin"] = _settings.Medecin;
                allFields["Ville"] = _settings.Ville;
                allFields["Date_Jour"] = DateTime.Now.ToString("dd/MM/yyyy");

                // 2. Champs depuis PatientMetadata
                if (metadata != null)
                {
                    // Construire Nom_Prenom au format "NOM Prénom" (français standard)
                    if (!string.IsNullOrEmpty(metadata.Nom) && !string.IsNullOrEmpty(metadata.Prenom))
                        allFields["Nom_Prenom"] = $"{metadata.Nom.ToUpper()} {metadata.Prenom}";
                    
                    if (!string.IsNullOrEmpty(metadata.DobFormatted))
                        allFields["Date_Naissance"] = metadata.DobFormatted;
                    
                    // Déterminer "né" ou "née" selon le sexe
                    if (!string.IsNullOrEmpty(metadata.Sexe))
                    {
                        allFields["Ne_Nee"] = metadata.Sexe.ToUpper() == "F" ? "née" : "né";
                    }
                    else
                    {
                        // Par défaut si sexe non renseigné (sera géré par MainWindow avant l'appel)
                        allFields["Ne_Nee"] = "né(e)";
                    }
                }
                else
                {
                    // Pas de métadonnées, utiliser la forme neutre
                    allFields["Ne_Nee"] = "né(e)";
                }

                // 3. Champs fournis par l'utilisateur (priorité la plus haute)
                foreach (var kvp in userFields)
                {
                    allFields[kvp.Key] = kvp.Value;
                }

                // Remplacer les placeholders dans le markdown
                var markdown = template.Markdown;
                
                // Traitement spécial pour les champs optionnels avec texte conditionnel
                markdown = ProcessConditionalFields(markdown, allFields);
                
                // Remplacer les placeholders standards
                foreach (var kvp in allFields)
                {
                    var placeholder = $"{{{{{kvp.Key}}}}}";
                    markdown = markdown.Replace(placeholder, kvp.Value);
                }

                // Vérifier qu'il ne reste pas de placeholders non remplacés
                var remainingPlaceholders = Regex.Matches(markdown, @"\{\{([^}]+)\}\}");
                if (remainingPlaceholders.Count > 0)
                {
                    var missing = string.Join(", ", remainingPlaceholders.Cast<Match>()
                        .Select(m => m.Groups[1].Value));
                    return (false, string.Empty, $"Champs manquants : {missing}");
                }

                // Contrôles qualité
                var (isValid, validationError) = ValidateAttestation(markdown, template);
                if (!isValid)
                    return (false, string.Empty, validationError);

                return (true, markdown, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, string.Empty, $"Erreur lors de la génération : {ex.Message}");
            }
        }

        /// <summary>
        /// Traite les champs conditionnels (ex: "{{Date_Debut_Suivi_Text}}")
        /// </summary>
        private string ProcessConditionalFields(string markdown, Dictionary<string, string> fields)
        {
            // Pattern pour détecter les champs conditionnels (suffixe _Text)
            var conditionalPattern = @"\{\{(\w+)_Text\}\}";
            var matches = Regex.Matches(markdown, conditionalPattern);

            foreach (Match match in matches)
            {
                var baseFieldName = match.Groups[1].Value;
                var placeholder = match.Value;

                // Vérifier si le champ de base existe
                if (fields.ContainsKey(baseFieldName) && !string.IsNullOrWhiteSpace(fields[baseFieldName]))
                {
                    // Générer le texte conditionnel selon le champ
                    string conditionalText = baseFieldName switch
                    {
                        "Date_Debut_Suivi" => $" depuis le {fields[baseFieldName]}",
                        "Frequence_Suivi" => $" ({fields[baseFieldName]})",
                        "Motif_Arret" => $" ({fields[baseFieldName]})",
                        "Duree_Amenagements" => $" pour une durée de {fields[baseFieldName]}",
                        "Accompagnateur" => $", accompagné(e) de {fields[baseFieldName]}",
                        _ => string.Empty
                    };

                    markdown = markdown.Replace(placeholder, conditionalText);
                }
                else
                {
                    // Champ absent ou vide → Supprimer le placeholder
                    markdown = markdown.Replace(placeholder, string.Empty);
                }
            }

            return markdown;
        }

        /// <summary>
        /// Valide une attestation (longueur, mots interdits)
        /// </summary>
        private (bool isValid, string error) ValidateAttestation(string markdown, AttestationTemplate template)
        {
            // Vérifier la longueur
            if (markdown.Length > template.MaxLength)
            {
                return (false, $"L'attestation dépasse la longueur maximale autorisée ({template.MaxLength} caractères)");
            }

            // Vérifier les mots interdits
            var lowerMarkdown = markdown.ToLower();
            foreach (var forbiddenWord in template.ForbiddenWords)
            {
                if (lowerMarkdown.Contains(forbiddenWord.ToLower()))
                {
                    return (false, $"Mot interdit détecté : '{forbiddenWord}'. Les attestations ne doivent contenir aucune mention médicale sensible.");
                }
            }

            return (true, string.Empty);
        }

        /// <summary>
        /// Sauvegarde une attestation et exporte en DOCX avec signature
        /// </summary>
        public (bool success, string message, string mdPath, string docxPath) SaveAndExportAttestation(
            string nomComplet,
            string type,
            string markdown)
        {
            try
            {
                // Créer le dossier attestations avec PathService
                var attestationsDir = _pathService.GetAttestationsDirectory(nomComplet);
                _pathService.EnsureDirectoryExists(attestationsDir);

                // Générer le nom de fichier
                var now = DateTime.Now;
                var typeSlug = type.ToLower().Replace(" ", "_");
                var fileName = $"{now:yyyy-MM-dd_HHmm}_{typeSlug}.md";
                var mdPath = Path.Combine(attestationsDir, fileName);

                // Gérer les doublons
                int version = 2;
                while (File.Exists(mdPath))
                {
                    fileName = $"{now:yyyy-MM-dd_HHmm}_{typeSlug}_v{version}.md";
                    mdPath = Path.Combine(attestationsDir, fileName);
                    version++;
                }

                // Sauvegarder le markdown
                var content = new StringBuilder();
                content.AppendLine("---");
                content.AppendLine($"patient: \"{nomComplet}\"");
                content.AppendLine($"type: \"attestation_{type}\"");
                content.AppendLine($"date: \"{now:yyyy-MM-ddTHH:mm}\"");
                content.AppendLine("---");
                content.AppendLine();
                content.Append(markdown);

                File.WriteAllText(mdPath, content.ToString(), Encoding.UTF8);

                // Exporter en DOCX avec signature numérique
                var (exportSuccess, exportMessage, docxPath) = _letterService.ExportToDocx(
                    nomComplet,
                    markdown,
                    mdPath
                );

                if (exportSuccess)
                {
                    return (true, $"✅ Attestation sauvegardée et exportée (.md + .docx)", mdPath, docxPath);
                }
                else
                {
                    return (false, $"⚠️ Attestation sauvegardée (.md) mais erreur export DOCX : {exportMessage}", mdPath, string.Empty);
                }
            }
            catch (Exception ex)
            {
                return (false, $"❌ Erreur lors de la sauvegarde : {ex.Message}", string.Empty, string.Empty);
            }
        }

        /// <summary>
        /// Met à jour une attestation existante (écrase le fichier)
        /// </summary>
        public async System.Threading.Tasks.Task<(bool success, string error)> UpdateExistingAttestationAsync(
            string mdPath,
            string newMarkdown)
        {
            try
            {
                if (!File.Exists(mdPath))
                {
                    return (false, "Le fichier d'attestation n'existe plus");
                }

                // Lire les métadonnées existantes (frontmatter YAML)
                var lines = await File.ReadAllLinesAsync(mdPath);
                var frontmatterEnd = -1;
                for (int i = 1; i < lines.Length; i++)
                {
                    if (lines[i].Trim() == "---")
                    {
                        frontmatterEnd = i;
                        break;
                    }
                }

                // Reconstruire le fichier avec les métadonnées originales + nouveau contenu
                var content = new StringBuilder();
                if (frontmatterEnd > 0)
                {
                    // Garder le frontmatter existant
                    for (int i = 0; i <= frontmatterEnd; i++)
                    {
                        content.AppendLine(lines[i]);
                    }
                }
                content.AppendLine();
                content.Append(newMarkdown);

                // Écraser le fichier .md existant
                await File.WriteAllTextAsync(mdPath, content.ToString(), Encoding.UTF8);

                // Régénérer le DOCX
                var docxPath = mdPath.Replace(".md", ".docx");
                var nomComplet = Path.GetFileName(Path.GetDirectoryName(mdPath)!.Replace("_", " "));
                var (exportSuccess, exportMessage, _) = _letterService.ExportToDocx(
                    nomComplet,
                    newMarkdown,
                    mdPath
                );

                if (!exportSuccess)
                {
                    return (false, $"Fichier .md sauvegardé mais erreur DOCX : {exportMessage}");
                }

                return (true, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, $"Erreur lors de la mise à jour : {ex.Message}");
            }
        }

        /// <summary>
        /// Génère une attestation personnalisée avec l'IA à partir d'une consigne
        /// </summary>
        public async System.Threading.Tasks.Task<(bool success, string markdown, string error)> GenerateCustomAttestationAsync(
            string consigne,
            PatientMetadata? metadata)
        {
            try
            {
                // ✅ ÉTAPE 1 : Préparer l'identité du patient pour la Gateway (si connue)
                string patientName = "";
                if (metadata != null && !string.IsNullOrEmpty(metadata.Nom) && !string.IsNullOrEmpty(metadata.Prenom))
                {
                    patientName = $"{metadata.Prenom} {metadata.Nom}";
                }

                // ✅ ÉTAPE 2 : Récupérer le contexte complet (métadonnées + synthèse/notes)
                var contextBundle = _patientContextService.GetCompleteContext(
                    patientName,
                    userRequest: consigne
                );

                // ✅ ÉTAPE 3 : Construire le bloc d'informations patient (AVEC VRAIES DONNÉES)
                // La gateway se chargera d'anonymiser ce bloc avant l'envoi au cloud.
                var contextBuilder = new StringBuilder();
                contextBuilder.AppendLine("INFORMATIONS PATIENT");
                contextBuilder.AppendLine("====================");
                contextBuilder.AppendLine();

                if (contextBundle.Metadata != null)
                {
                    var meta = contextBundle.Metadata;
                    
                    if (!string.IsNullOrEmpty(patientName))
                        contextBuilder.AppendLine($"- Nom complet : {patientName}");

                    if (meta.Age.HasValue)
                        contextBuilder.AppendLine($"- Âge : {meta.Age} ans");

                    if (!string.IsNullOrEmpty(meta.DobFormatted))
                        contextBuilder.AppendLine($"- Date de naissance : {meta.DobFormatted}");

                    if (!string.IsNullOrEmpty(meta.Sexe))
                        contextBuilder.AppendLine($"- Sexe : {meta.Sexe}");
                }

                contextBuilder.AppendLine();

                // ✅ ÉTAPE 4 : Ajouter le contexte clinique (VRAIES DONNÉES)
                if (!string.IsNullOrEmpty(contextBundle.ClinicalContext))
                {
                    contextBuilder.AppendLine("CONTEXTE CLINIQUE");
                    contextBuilder.AppendLine("=================");
                    contextBuilder.AppendLine($"Source : {contextBundle.ContextType}");
                    contextBuilder.AppendLine();
                    contextBuilder.AppendLine(contextBundle.ClinicalContext); 
                    contextBuilder.AppendLine();
                }

                // Vérifier les prompts en cache
                if (string.IsNullOrEmpty(_cachedAttestationPrompt))
                {
                    return (false, string.Empty, "Prompt d'attestation non configuré.");
                }
                
                // Remplacer les placeholders dans le prompt (VRAIES DONNÉES)
                var userPrompt = _cachedAttestationPrompt
                    .Replace("{{Medecin}}", _settings.Medecin)
                    .Replace("{{Patient_Info}}", contextBuilder.ToString())
                    .Replace("{{Consigne}}", consigne);

                var systemPrompt = _cachedSystemPrompt
                    .Replace("{{Medecin}}", _settings.Medecin);
                
                // ✅ ÉTAPE 5 : Utiliser la GATEWAY pour l'appel LLM
                // Elle s'occupe de l'anonymisation 3 phases (si Cloud) et de la désanonymisation
                var messages = new List<(string role, string content)>
                {
                    ("user", userPrompt)
                };

                var (success, result, error) = await _llmGatewayService.ChatAsync(
                    systemPrompt,
                    messages,
                    patientName: patientName,
                    maxTokens: 2000
                );

                // ✅ NOUVEAU : Logger le prompt (si tracker disponible)
                if (_promptTracker != null)
                {
                    _promptTracker.LogPrompt(new PromptLogEntry
                    {
                        Timestamp = DateTime.Now,
                        Module = "Attestation",
                        SystemPrompt = systemPrompt,
                        UserPrompt = userPrompt,
                        AIResponse = success ? result : error ?? "",
                        TokensUsed = EstimateTokens(systemPrompt, userPrompt, result ?? error ?? ""),
                        LLMProvider = _llmGatewayService.GetActiveProviderName(),
                        ModelName = "gpt-4o-mini", // TODO: dynamique
                        Success = success,
                        Error = success ? null : error
                    });
                }

                if (!success)
                {
                    return (false, string.Empty, $"Erreur Gateway : {error}");
                }

                // ✅ ÉTAPE 6 : Nettoyer le résultat désanonymisé par la Gateway
                var markdown = result.Trim();
                if (markdown.StartsWith("```markdown"))
                    markdown = markdown.Substring(11);
                if (markdown.StartsWith("```"))
                    markdown = markdown.Substring(3);
                if (markdown.EndsWith("```"))
                    markdown = markdown.Substring(0, markdown.Length - 3);
                markdown = markdown.Trim();


                // Remplacer les placeholders par les vraies données du patient
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                // Champs globaux depuis AppSettings
                string medecinName = _settings.Medecin;
                fields["Medecin"] = medecinName;
                fields["Dr Medecin"] = "Dr " + medecinName;  // Pour gérer {{Dr Medecin}}
                fields["Dr " + medecinName] = "Dr " + medecinName;  // Pour gérer {{Dr Lassoued Nair}}
                fields["Ville"] = _settings.Ville;
                fields["Date_Jour"] = DateTime.Now.ToString("dd/MM/yyyy");

                // Variables pour le genre
                string neNee = "né(e)";
                string nomPrenom = "";
                string dateNaissance = "";

                // Champs patient
                if (metadata != null)
                {
                    // Nom complet du patient
                    if (!string.IsNullOrEmpty(metadata.Nom) && !string.IsNullOrEmpty(metadata.Prenom))
                    {
                        nomPrenom = $"{metadata.Nom.ToUpper()} {metadata.Prenom}";
                        fields["Nom_Prenom"] = nomPrenom;
                        // L'IA peut utiliser directement le nom au lieu du placeholder
                        fields[nomPrenom] = nomPrenom;
                    }

                    // Date de naissance
                    if (!string.IsNullOrEmpty(metadata.DobFormatted))
                    {
                        dateNaissance = metadata.DobFormatted;
                        fields["Date_Naissance"] = dateNaissance;
                        // L'IA peut utiliser directement la date au lieu du placeholder
                        fields[dateNaissance] = dateNaissance;
                    }

                    // Déterminer le genre
                    if (!string.IsNullOrEmpty(metadata.Sexe))
                    {
                        neNee = metadata.Sexe.ToUpper() == "F" ? "née" : "né";
                        fields["Ne_Nee"] = neNee;
                    }
                    else
                    {
                        fields["Ne_Nee"] = "né(e)";
                    }
                }

                // Remplacer tous les placeholders entre accolades {{...}}
                foreach (var kvp in fields)
                {
                    var placeholder = $"{{{{{kvp.Key}}}}}";
                    markdown = markdown.Replace(placeholder, kvp.Value);
                }

                // Remplacer avec regex pour capturer tous les placeholders restants
                markdown = System.Text.RegularExpressions.Regex.Replace(
                    markdown,
                    @"\{\{([^}]+)\}\}",
                    match => {
                        string key = match.Groups[1].Value.Trim();
                        if (fields.TryGetValue(key, out string? value))
                            return value;

                        // Cas spéciaux pour gérer les variations
                        if (key.StartsWith("Dr ") && fields.TryGetValue(key, out string? drValue))
                            return drValue;

                        // Si la clé n'est pas trouvée, retourner sans les accolades
                        return key;
                    }
                );

                // Remplacer "né(e)" par le genre approprié si connu
                if (metadata != null && !string.IsNullOrEmpty(metadata.Sexe))
                {
                    markdown = markdown.Replace("né(e)", neNee);
                    markdown = markdown.Replace("Né(e)", neNee.Substring(0, 1).ToUpper() + neNee.Substring(1));
                }

                return (true, markdown, string.Empty);
            }
            catch (Exception ex)
            {
                return (false, string.Empty, $"Erreur : {ex.Message}");
            }
        }

        /// <summary>
        /// Estime le nombre de tokens utilisés (approximation simple)
        /// </summary>
        private int EstimateTokens(string systemPrompt, string userPrompt, string response)
        {
            // Approximation : 1 token ≈ 4 caractères en français
            var totalChars = (systemPrompt?.Length ?? 0) + (userPrompt?.Length ?? 0) + (response?.Length ?? 0);
            return totalChars / 4;
        }

        /// <summary>
        /// Récupère la liste des attestations d'un patient
        /// </summary>
        public List<(DateTime date, string type, string preview, string mdPath, string docxPath)> GetAttestations(string nomComplet)
        {
            var result = new List<(DateTime, string, string, string, string)>();

            try
            {
                var attestationsDir = _pathService.GetAttestationsDirectory(nomComplet);

                if (!Directory.Exists(attestationsDir))
                    return result;

                var mdFiles = Directory.GetFiles(attestationsDir, "*.md", SearchOption.TopDirectoryOnly);

                foreach (var mdFile in mdFiles)
                {
                    var docxFile = Path.ChangeExtension(mdFile, ".docx");
                    var date = File.GetLastWriteTime(mdFile);
                    
                    // Extraire le type depuis le nom de fichier
                    var fileName = Path.GetFileNameWithoutExtension(mdFile);
                    var parts = fileName.Split('_');
                    var type = parts.Length > 2 ? parts[2] : "attestation";
                    
                    // Lire le contenu pour générer un aperçu
                    var content = File.ReadAllText(mdFile);
                    var lines = content.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    var preview = lines.FirstOrDefault(l => !l.StartsWith("---") && !l.StartsWith("#") && l.Length > 10)
                        ?? "Attestation";
                    
                    if (preview.Length > 50)
                        preview = preview.Substring(0, 47) + "...";

                    result.Add((date, type, preview, mdFile, docxFile));
                }

                return result.OrderByDescending(r => r.Item1).ToList();
            }
            catch
            {
                return result;
            }
        }
    }
}
