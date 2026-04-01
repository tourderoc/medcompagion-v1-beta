using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using MedCompanion.Dialogs;
using MedCompanion.Models;
using MedCompanion.Services;

namespace MedCompanion
{
    public class LetterService
    {
        private readonly OpenAIService _openAIService;
        private readonly ContextLoader _contextLoader;
        private readonly StorageService _storageService;
        private readonly AppSettings _settings;
        private readonly PromptConfigService _promptConfig = null!;
        private readonly PatientContextService _patientContextService;
        private readonly AnonymizationService _anonymizationService;
        private readonly LLMGatewayService _llmGatewayService; // ✅ Gateway centralisé pour anonymisation automatique
        
        // Cache des prompts pour éviter les appels répétés
        private string _cachedSystemPrompt = string.Empty;
        private string _cachedLetterWithContextPrompt = string.Empty;
        private string _cachedLetterNoContextPrompt = string.Empty;
        private string _cachedTemplateAdaptationPrompt = string.Empty;

        public LetterService(
            OpenAIService openAIService,
            ContextLoader contextLoader,
            StorageService storageService,
            PatientContextService patientContextService,
            AnonymizationService anonymizationService,
            PromptConfigService promptConfig,
            LLMGatewayService llmGatewayService) // ✅ Gateway centralisé
        {
            _openAIService = openAIService;
            _contextLoader = contextLoader;
            _storageService = storageService;
            _patientContextService = patientContextService;
            _anonymizationService = anonymizationService;
            _settings = AppSettings.Load();
            _promptConfig = promptConfig;
            _llmGatewayService = llmGatewayService; // ✅ Gateway centralisé

            // Configure QuestPDF License
            QuestPDF.Settings.License = LicenseType.Community;

            // Charger les prompts initialement
            LoadPrompts();

            // S'abonner à l'événement de rechargement des prompts (si _promptConfig n'est pas null)
            if (_promptConfig != null)
            {
                _promptConfig.PromptsReloaded += OnPromptsReloaded;
            }
        }
        
        /// <summary>
        /// Charge les prompts depuis le service de configuration
        /// </summary>
        private void LoadPrompts()
        {
            // ✅ Vérifier si _promptConfig est null (cas où le service est créé pour export uniquement)
            if (_promptConfig == null)
            {
                _cachedSystemPrompt = "";
                _cachedLetterWithContextPrompt = "";
                _cachedLetterNoContextPrompt = "";
                _cachedTemplateAdaptationPrompt = "";
                System.Diagnostics.Debug.WriteLine("[LetterService] Prompts non chargés (PromptConfig null)");
                return;
            }

            _cachedSystemPrompt = _promptConfig.GetActivePrompt("system_global");
            _cachedLetterWithContextPrompt = _promptConfig.GetActivePrompt("letter_generation_with_context");
            _cachedLetterNoContextPrompt = _promptConfig.GetActivePrompt("letter_generation_no_context");
            _cachedTemplateAdaptationPrompt = _promptConfig.GetActivePrompt("template_adaptation");

            System.Diagnostics.Debug.WriteLine("[LetterService] Prompts chargés depuis la configuration");
        }
        
        /// <summary>
        /// Gestionnaire d'événement pour le rechargement des prompts
        /// </summary>
        private void OnPromptsReloaded(object? sender, EventArgs e)
        {
            LoadPrompts();
            System.Diagnostics.Debug.WriteLine("[LetterService] ✅ Prompts rechargés automatiquement suite à une modification");
        }

        /// <summary>
        /// Détecte si un message demande un courrier
        /// </summary>
        public bool IsLetterIntent(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            // Normaliser le message
            var normalized = RemoveAccents(message.ToLower());

            // Mots-clés pour détecter une demande de courrier
            string[] keywords = {
                "courrier", "lettre", "attestation", "papier", "certificat",
                "vie sco", "ecole", "college", "lycee", "pap", "amenagement",
                "psychomot", "cr parents", "medecin traitant", "mdph"
            };

            return keywords.Any(keyword => normalized.Contains(keyword));
        }

        /// <summary>
        /// Vérifie si le texte contient une mention de médicament/traitement
        /// </summary>
        private bool ContientMedicament(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;
            
            var textLower = text.ToLower();
            
            // Liste de mots-clés liés aux médicaments psychotropes
            string[] medicamentKeywords = {
                "méthylphénidate", "methylphenidate", "ritaline", "concerta", "quasym", "medikinet",
                "atomoxétine", "atomoxetine", "strattera",
                "antidépresseur", "antidepresseur", "prozac", "zoloft", "sertraline", "fluoxétine", "fluoxetine",
                "anxiolytique", "benzodiazépine", "benzodiazepine",
                "neuroleptique", "antipsychotique", "rispéridone", "risperidone", "aripiprazole",
                "traitement médicamenteux", "traitement envisagé", "prescription",
                "psychostimulant", "psychotrope"
            };
            
            // Vérifier si au moins un mot-clé est présent
            return medicamentKeywords.Any(keyword => textLower.Contains(keyword));
        }
        
        /// <summary>
        /// Extrait toutes les variables d'un template ({{Variable}}, {Variable}, [Variable])
        /// </summary>
        private HashSet<string> ExtractVariablesFromTemplate(string template)
        {
            var variables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var regexVariables = new Regex(@"(\{+([^}]+)\}+|\[([^\]]+)\])", RegexOptions.IgnoreCase);
            var matches = regexVariables.Matches(template);
            
            foreach (Match match in matches)
            {
                var variableName = !string.IsNullOrEmpty(match.Groups[2].Value) 
                    ? match.Groups[2].Value.Trim() 
                    : match.Groups[3].Value.Trim();
                
                variableName = variableName.Trim('{', '}', '[', ']');
                
                if (!string.IsNullOrWhiteSpace(variableName))
                {
                    variables.Add(variableName);
                }
            }
            
            return variables;
        }
        

        
        /// <summary>
        /// Adapte un modèle de courrier avec l'IA en fonction du contexte patient
        /// ✅ REFACTORISÉ : Utilise LLMGatewayService avec anonymisation automatique
        /// </summary>
        public async Task<(bool success, string markdown, string error)> AdaptTemplateWithAIAsync(
            string nomComplet,
            string templateName,
            string templateMarkdown,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // ✅ Créer le contexte avec le VRAI nom (le gateway anonymisera si cloud)
                var contextBundle = _patientContextService.GetCompleteContext(
                    nomComplet,
                    userRequest: null,
                    pseudonym: null  // Pas de pseudonyme manuel, le gateway gère
                );

                var contextText = contextBundle.ToPromptText(nomComplet, null);
                var metadata = contextBundle.Metadata;

                System.Diagnostics.Debug.WriteLine($"[AdaptTemplateWithAIAsync] {contextBundle.ToDebugText()}");

                var medecin = _settings.Medecin;

                // Détecter si c'est une Feuille de route pour adapter le style
                bool isFeuilleRoute = templateName.Contains("Feuille de route", StringComparison.OrdinalIgnoreCase);

                // Utiliser le prénom du patient (sera anonymisé automatiquement si cloud)
                var prenom = nomComplet.Split('_').LastOrDefault()?.Split(' ').FirstOrDefault() ?? "l'enfant";

                var systemPrompt = isFeuilleRoute
                    ? $@"Tu es l'assistant du {medecin}, pédopsychiatre.
Tu rédiges un document chaleureux et bienveillant DESTINÉ AUX PARENTS.
- Ton : empathique, pratique, non médical, rassurant
- Tu t'adresses AUX PARENTS mais parles DE L'ENFANT en 3ᵉ personne (il/elle, {prenom})
- Style : guidance parentale simple et concrète, pas de jargon clinique"
                    : $@"Tu es l'assistant du {medecin}, pédopsychiatre.
- L'UTILISATEUR est le clinicien. Tu rédiges EN PREMIÈRE PERSONNE au nom du {medecin}.
- Pour le patient/enfant: toujours 3ᵉ personne (il/elle, l'enfant, le patient).
- INTERDITS: jamais ""votre enfant"", ""mon fils"", ""pour mon fils"". Toujours 3ᵉ personne.
- Style: professionnel, concis, respectueux.";

                var userPrompt = isFeuilleRoute
                    ? $@"CONTEXTE PATIENT COMPLET
----
{contextText}

TYPE DE DOCUMENT
----
{templateName}

REGLE ABSOLUE - PLACEHOLDERS Variables :
Si tu TROUVES l'information EXACTE dans le contexte : Remplace par la valeur reelle
Si tu NE TROUVES PAS l'information : TU DOIS laisser le placeholder intact avec doubles accolades

INTERDICTIONS :
- NE JAMAIS remplacer par des crochets [Variable]
- NE JAMAIS remplacer par du texte vague
- NE JAMAIS inventer une information manquante
- FORMAT OBLIGATOIRE : doubles accolades

CONSIGNE SPECIALE FEUILLE DE ROUTE
----
Génère une feuille de route CHALEUREUSE et PRATIQUE pour les parents de {prenom}.

1. **Motif principal** : Identifie en 1-2 phrases le motif de consultation principal depuis le contexte (ex: ""difficultés de sommeil"", ""anxiété importante"", ""opposition"", ""trop d'écrans"", etc.)

2. **Axes de travail** : Sélectionne intelligemment 2-3 axes pertinents selon le profil (Sommeil, Écrans, Émotions, Concentration, Opposition, Autonomie, Alimentation, etc.)

3. Pour chaque axe, génère 2-5 conseils CONCRETS et SIMPLES avec **☐** devant chaque conseil
   - Ton bienveillant et pratique
   - Conseils applicables au quotidien
   - Personnalisés selon l'âge et le contexte

4. **Message du pédopsy** : Court message chaleureux (2-3 lignes) rappelant que ""l'important n'est pas de tout faire parfaitement""

Structure Markdown :
```
# Feuille de route pour les parents de {prenom}

**Motif principal :**
[Texte ici]

**Axes de travail :**

1️⃣ [Nom de l'axe] :
☐ Conseil 1
☐ Conseil 2
☐ Conseil 3

2️⃣ [Nom de l'axe] :
☐ Conseil 1
☐ Conseil 2

**Message du pédopsy :**
[Message bienveillant]

**Prochain point :** {{{{Date_Prochain_RDV}}}}
```

⚠️ IMPORTANT :
- Personnalise avec le prénom {prenom} partout
- Ton chaleureux, NON médical
- Conseils concrets et applicables"
                    : $@"CONTEXTE PATIENT COMPLET
----
{contextText}

TYPE DE COURRIER
----
{templateName}

MODELE DE REFERENCE
----
{templateMarkdown}

REGLE : Remplace UNIQUEMENT les informations trouvees EXPLICITEMENT dans le contexte. Tout le reste doit rester en placeholder avec doubles accolades. Si information absente du contexte, GARDER le placeholder intact.

CONSIGNE
----
Redige en 12-15 lignes maximum, ton professionnel.
- Le patient s'appelle : {nomComplet}
- Adapte les amenagements selon le motif principal
- Format Markdown avec titre et corps uniquement
- NE PAS inclure en-tete, date, signature, pied de page
- Personnalise selon le contexte patient
- IMPORTANT : Sois concis, evite redondance";

                // ✅ Construire les messages pour le gateway
                var messages = new List<(string role, string content)>
                {
                    ("user", userPrompt)
                };

                // Vérifier l'annulation avant l'appel LLM
                cancellationToken.ThrowIfCancellationRequested();

                // ✅ Appel via LLMGatewayService - anonymisation automatique si cloud
                var (success, result, error) = await _llmGatewayService.ChatAsync(
                    systemPrompt: systemPrompt,
                    messages: messages,
                    patientName: nomComplet,
                    maxTokens: 2000,
                    cancellationToken: cancellationToken
                );

                // Vérifier l'annulation après l'appel LLM
                cancellationToken.ThrowIfCancellationRequested();

                if (success)
                {
                    return (true, result, string.Empty);
                }
                else
                {
                    return (false, string.Empty, error ?? result);
                }
            }
            catch (OperationCanceledException)
            {
                return (false, string.Empty, "Opération annulée par l'utilisateur");
            }
            catch (Exception ex)
            {
                return (false, string.Empty, $"Erreur lors de l'adaptation: {ex.Message}");
            }
        }

        /// <summary>
        /// Génère un brouillon de courrier
        /// ✅ REFACTORISÉ : Utilise LLMGatewayService avec anonymisation automatique
        /// </summary>
        public async Task<(bool success, string markdown, string error)> GenerateLetterAsync(
            string nomComplet,
            string userRequest)
        {
            try
            {
                // ✅ Créer le contexte avec le VRAI nom (le gateway anonymisera si cloud)
                var contextBundle = _patientContextService.GetCompleteContext(
                    nomComplet,
                    userRequest: null,
                    pseudonym: null  // Pas de pseudonyme manuel, le gateway gère
                );

                var contextText = contextBundle.ToPromptText(nomComplet, null);

                var medecin = _settings.Medecin;

                // Utiliser le prompt système en cache (rechargé automatiquement via événement)
                var systemPrompt = _cachedSystemPrompt.Replace("{{Medecin}}", medecin);

                // Utiliser le prompt en cache (rechargé automatiquement via événement)
                var userPromptTemplate = !string.IsNullOrEmpty(contextText) ? _cachedLetterWithContextPrompt : _cachedLetterNoContextPrompt;

                // Remplacer les variables
                var userPrompt = userPromptTemplate
                    .Replace("{{Contexte}}", contextText)
                    .Replace("{{User_Request}}", userRequest);

                // Ajouter l'instruction sur le nom (sera anonymisé automatiquement si cloud)
                userPrompt += $"\n\nIMPORTANT : Le patient s'appelle {nomComplet}.";

                // ✅ Construire les messages pour le gateway
                var messages = new List<(string role, string content)>
                {
                    ("user", userPrompt)
                };

                // ✅ Appel via LLMGatewayService - anonymisation automatique si cloud
                var (success, result, error) = await _llmGatewayService.ChatAsync(
                    systemPrompt: systemPrompt,
                    messages: messages,
                    patientName: nomComplet,
                    maxTokens: 2000
                );

                if (success)
                {
                    return (true, result, string.Empty);
                }
                else
                {
                    return (false, string.Empty, error ?? result);
                }
            }
            catch (Exception ex)
            {
                return (false, string.Empty, $"Erreur lors de la génération: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Génère un courrier à partir d'une conversation sauvegardée
        /// ✅ REFACTORISÉ : Utilise LLMGatewayService avec anonymisation automatique
        /// </summary>
        public async Task<(bool success, string markdown, string error)> GenerateLetterFromChatAsync(
            string nomComplet,
            string conversationContext,
            string userRequest)
        {
            try
            {
                var medecin = _settings.Medecin;

                // Utiliser le prompt système en cache (rechargé automatiquement via événement)
                var systemPrompt = _cachedSystemPrompt.Replace("{{Medecin}}", medecin);

                // Construire le prompt utilisateur enrichi avec la conversation
                // ✅ Utiliser le VRAI nom (le gateway anonymisera si cloud)
                var userPrompt = $@"CONTEXTE DE LA CONVERSATION
----
{conversationContext}

DEMANDE DE COURRIER
----
{userRequest}

CONSIGNE
----
Rédige un courrier professionnel en te basant sur :
1. Le contexte patient fourni (notes cliniques)
2. La conversation précédente (échange sauvegardé)
3. La demande spécifique de l'utilisateur

IMPORTANT : Le patient s'appelle {nomComplet}.

FORMAT ATTENDU :
- Titre avec # Objet : [titre du courrier]
- Corps du courrier (12-15 lignes maximum, ton professionnel)
- Format Markdown
- NE PAS inclure d'en-tête, date, signature (gérés automatiquement)
- Utilise les informations de la conversation pour enrichir le courrier
- Sois concis et évite les redondances";

                // ✅ Construire les messages pour le gateway
                var messages = new List<(string role, string content)>
                {
                    ("user", userPrompt)
                };

                // ✅ Appel via LLMGatewayService - anonymisation automatique si cloud
                var (success, result, error) = await _llmGatewayService.ChatAsync(
                    systemPrompt: systemPrompt,
                    messages: messages,
                    patientName: nomComplet,
                    maxTokens: 2000
                );

                if (success)
                {
                    return (true, result, string.Empty);
                }
                else
                {
                    return (false, string.Empty, error ?? result);
                }
            }
            catch (Exception ex)
            {
                return (false, string.Empty, $"Erreur lors de la génération: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Génère un courrier à partir d'un template MCC avec analyse sémantique
        /// ✅ REFACTORISÉ : Utilise LLMGatewayService avec anonymisation automatique
        /// </summary>
        public async Task<(bool success, string markdown, string error)> GenerateLetterFromMCCAsync(
            string nomComplet,
            MCCModel mcc)
        {
            try
            {
                // Construire le contexte enrichi du patient
                var (hasContext, contextText, contextInfo) = _contextLoader.GetContextBundle(nomComplet, null);

                // Récupérer les métadonnées pour injecter l'âge calculé
                var patientDir = _storageService.GetPatientDirectory(nomComplet);
                var patientJsonPath = Path.Combine(patientDir, "patient.json");
                PatientMetadata? metadata = null;

                if (File.Exists(patientJsonPath))
                {
                    try
                    {
                        var json = File.ReadAllText(patientJsonPath);
                        metadata = System.Text.Json.JsonSerializer.Deserialize<PatientMetadata>(json);
                    }
                    catch { }
                }

                // ✅ Enrichir le contexte avec le VRAI nom (le gateway anonymisera si cloud)
                var enrichedContext = new StringBuilder();
                if (metadata != null)
                {
                    enrichedContext.AppendLine("INFORMATIONS PATIENT");
                    enrichedContext.AppendLine("----");
                    enrichedContext.AppendLine($"- Nom complet : {nomComplet}");

                    if (metadata.Age.HasValue)
                    {
                        enrichedContext.AppendLine($"- Âge actuel : {metadata.Age} ans");
                    }

                    if (!string.IsNullOrEmpty(metadata.DobFormatted))
                    {
                        enrichedContext.AppendLine($"- Date de naissance : {metadata.DobFormatted}");
                    }

                    if (!string.IsNullOrEmpty(metadata.Sexe))
                    {
                        enrichedContext.AppendLine($"- Sexe : {metadata.Sexe}");
                    }

                    if (!string.IsNullOrEmpty(metadata.Ecole))
                    {
                        enrichedContext.AppendLine($"- École : {metadata.Ecole}");
                    }

                    if (!string.IsNullOrEmpty(metadata.Classe))
                    {
                        enrichedContext.AppendLine($"- Classe : {metadata.Classe}");
                    }
                    enrichedContext.AppendLine();
                }

                // Ajouter le contexte des notes si disponible
                if (hasContext)
                {
                    enrichedContext.AppendLine("NOTES CLINIQUES RÉCENTES");
                    enrichedContext.AppendLine("----");
                    enrichedContext.AppendLine(contextText);
                    enrichedContext.AppendLine();
                }

                var medecin = _settings.Medecin;
                var enrichedContextStr = enrichedContext.ToString();

                // Utiliser le prompt système en cache (rechargé automatiquement via événement)
                var systemPrompt = _cachedSystemPrompt.Replace("{{Medecin}}", medecin);

                // ✅ Construire le prompt avec le VRAI nom (le gateway anonymisera si cloud)
                var userPrompt = $@"🎯 TEMPLATE MCC OPTIMISÉ : ""{mcc.Name}""

{ExplainTemplateNotation()}

---
CONTEXTE PATIENT
----
{enrichedContextStr}

---
MÉTADONNÉES SÉMANTIQUES DU MCC
----
Type de document : {mcc.Semantic?.DocType ?? "Non spécifié"}
Audience cible : {mcc.Semantic?.Audience ?? "Non spécifiée"}
Ton requis : {mcc.Semantic?.Tone ?? "professionnel"}
Tranche d'âge : {mcc.Semantic?.AgeGroup ?? "Non spécifiée"}
{GenerateKeywordsDirective(mcc.Semantic?.ClinicalKeywords)}

{GenerateSectionsDirective(mcc.Semantic?.Sections)}

---
TEMPLATE MCC (avec notation [GÉNÉRER:]) :
----
{mcc.TemplateMarkdown}

---
🚨 RÈGLE ABSOLUE - GESTION DES VARIABLES {{{{Variable}}}} 🚨
----
Pour CHAQUE variable {{{{Variable}}}} du template :

✅ SI l'information EST dans le contexte patient → Remplace par la valeur EXACTE
❌ SI l'information N'EST PAS dans le contexte → GARDE le placeholder {{{{Variable}}}} INTACT

EXEMPLES CONCRETS :
- {{{{Nom_Prenom}}}} → TOUJOURS disponible dans contexte → Remplacer par ""{nomComplet}""
- {{{{Age}}}} → TOUJOURS disponible dans contexte → Remplacer
- {{{{Ecole}}}} → SI présent dans contexte → Remplacer, SINON garder {{{{Ecole}}}}
- {{{{Etablissement}}}} → SI présent dans contexte → Remplacer, SINON garder {{{{Etablissement}}}}
- {{{{Destinataire}}}} → Presque JAMAIS dans contexte → GARDER {{{{Destinataire}}}}
- {{{{Specialite}}}} → Presque JAMAIS dans contexte → GARDER {{{{Specialite}}}}

⛔ INTERDICTIONS ABSOLUES :
- NE JAMAIS remplacer par [Variable entre crochets]
- NE JAMAIS remplacer par ""Non spécifié"" ou ""Non renseigné""
- NE JAMAIS inventer une information manquante
- NE JAMAIS laisser une description vague

FORMAT OBLIGATOIRE pour variables manquantes : {{{{Variable}}}} (doubles accolades)

---
⚠️ RÈGLES CRITIQUES :
----
1. Ton résultat final NE DOIT PAS contenir de marqueurs [GÉNÉRER:]
2. Chaque zone doit être remplacée par du contenu médical réel et personnalisé
3. Respecte la logique narrative des sections (ordre et objectifs)
4. Omets les sections [OPTIONNELLE] non pertinentes pour CE patient (pas de contenu creux)
5. N'invente pas d'informations manquantes → Garde les placeholders {{{{Variable}}}}

---
🚫 EXCLUSIONS ABSOLUES - À NE JAMAIS INCLURE 🚫
----
NE GÉNÈRE JAMAIS les éléments suivants (ils sont gérés automatiquement par le système) :
❌ En-tête avec coordonnées du médecin
❌ Date du courrier (""Le [date]"", ""Fait au..."")
❌ Signature (""Dr..."", nom du médecin)
❌ Spécialité du médecin (""Pédopsychiatre"")
❌ Lieu et date (""Le Pradel, le..."", ""[Ville], le..."")
❌ Formule de politesse finale (""Cordialement"", ""Bien à vous"")
❌ Pied de page avec adresse ou RPPS

⚠️ RÈGLE CRITIQUE : Ton courrier doit se terminer immédiatement après le dernier paragraphe de contenu médical/clinique. AUCUNE signature, AUCUNE date, AUCUNE formule de clôture.

✅ STRUCTURE AUTORISÉE :
# Objet : [Titre]
[Corps du courrier - contenu médical uniquement]
[FIN - ne rien ajouter après]

---
🎯 MISSION : Crée un courrier INSPIRÉ de ce MCC, adapté à la situation unique de ce patient.
- Le patient s'appelle : {nomComplet}
- Respecte le TON ({mcc.Semantic?.Tone ?? "professionnel"})
- Adapte à l'AUDIENCE ({mcc.Semantic?.Audience ?? "destinataire"})
- Personnalise selon l'ÂGE du patient ({metadata?.Age ?? 0} ans)
- Format Markdown avec titre # Objet : [titre]
- Concision : 12-15 lignes maximum, évite la redondance";

                // ✅ Construire les messages pour le gateway
                var messages = new List<(string role, string content)>
                {
                    ("user", userPrompt)
                };

                // ✅ Appel via LLMGatewayService - anonymisation automatique si cloud
                var (success, result, error) = await _llmGatewayService.ChatAsync(
                    systemPrompt: systemPrompt,
                    messages: messages,
                    patientName: nomComplet,
                    maxTokens: 2500
                );

                if (success)
                {
                    return (true, result, string.Empty);
                }
                else
                {
                    return (false, string.Empty, error ?? result);
                }
            }
            catch (Exception ex)
            {
                return (false, string.Empty, $"Erreur lors de la génération depuis MCC: {ex.Message}");
            }
        }

        #region MCC Generation Helper Functions

        /// <summary>
        /// Génère l'explication de la notation du template MCC
        /// </summary>
        private string ExplainTemplateNotation()
        {
            return @"Le template ci-dessous est un MODÈLE optimisé pour génération.
Il utilise une notation spéciale que tu DOIS interpréter :

📋 NOTATION DU TEMPLATE :
1. Les titres ""## Section"" définissent la STRUCTURE à respecter
2. Les blocs ""[GÉNÉRER: instruction]"" sont des ZONES À GÉNÉRER
   → Remplace chaque ""[GÉNÉRER: ...]"" par du contenu médical réel
   → L'instruction entre crochets t'indique QUOI écrire dans cette zone

🎨 MODE GÉNÉRATION : INSPIRATION (pas copie stricte)
- Respecte la STRUCTURE (titres ##)
- GÉNÈRE du contenu ORIGINAL pour chaque zone [GÉNÉRER: ...]
- ADAPTE au contexte SPÉCIFIQUE de CE patient
- Ne COPIE PAS les marqueurs [GÉNÉRER:] dans le résultat final

EXEMPLE DE TRANSFORMATION :

❌ MAUVAIS (copie le template) :
## Objet
[GÉNÉRER: Objet précis du courrier...]

✅ BON (génère du contenu) :
## Objet
Courrier concernant l'accompagnement scolaire de Lucas Dupont, 8 ans,
suivi pour trouble déficit de l'attention avec hyperactivité (TDAH)";
        }

        /// <summary>
        /// Génère les directives pour les mots-clés cliniques
        /// </summary>
        private string GenerateKeywordsDirective(List<string>? keywords)
        {
            if (keywords == null || !keywords.Any())
                return "";

            return $@"
🔑 THÉMATIQUES CLINIQUES OBLIGATOIRES
----
Ce courrier DOIT aborder ces concepts cliniques :
{string.Join("\n", keywords.Select(k => $"- {k}"))}

⚠️ IMPORTANT :
- Intègre naturellement ces mots-clés dans le contenu médical
- Ne force pas leur présence si non pertinent pour CE patient
- Utilise-les pour guider le ton clinique du courrier
";
        }

        /// <summary>
        /// Génère les directives pour les sections avec indication essentielle/optionnelle
        /// </summary>
        private string GenerateSectionsDirective(Dictionary<string, string>? sections)
        {
            if (sections == null || !sections.Any())
                return "Aucune structure spécifique définie.";

            var directive = new StringBuilder();
            directive.AppendLine("🎯 STRUCTURE CIBLE (adapter selon pertinence clinique) :");
            directive.AppendLine();

            int i = 1;
            foreach (var section in sections)
            {
                // Déterminer si section est essentielle
                var isEssential = IsEssentialSection(section.Key);
                var marker = isEssential ? "[ESSENTIELLE]" : "[OPTIONNELLE]";

                directive.AppendLine($"{i}. ## {section.Key} {marker}");
                directive.AppendLine($"   → Objectif : {section.Value}");

                if (isEssential)
                {
                    directive.AppendLine($"   → Cette section est CRUCIALE, toujours l'inclure si possible");
                }
                else
                {
                    directive.AppendLine($"   → Omettre si non pertinent pour ce patient");
                }

                directive.AppendLine();
                i++;
            }

            directive.AppendLine("⚠️ RÈGLE D'ADAPTATION :");
            directive.AppendLine("- Sections [ESSENTIELLE] = toujours inclure si possible");
            directive.AppendLine("- Sections [OPTIONNELLE] = omettre si redondant ou non pertinent");
            directive.AppendLine("- Garde l'ORDRE des sections conservées");

            return directive.ToString();
        }

        /// <summary>
        /// Détermine si une section est essentielle (doit toujours être incluse)
        /// </summary>
        private bool IsEssentialSection(string sectionName)
        {
            // Sections toujours essentielles
            var essential = new[] {
                "objet",
                "recommandations",
                "recommandation",
                "aménagements",
                "aménagement",
                "conclusion"
            };

            return essential.Any(e =>
                sectionName.Contains(e, StringComparison.OrdinalIgnoreCase));
        }

        #endregion

        /// <summary>
        /// Sauvegarde un brouillon de courrier
        /// </summary>
        public (bool success, string message, string filePath) SaveDraft(string nomComplet, string markdown, string slug = "courrier")
        {
            try
            {
                var patientDir = _storageService.GetPatientDirectory(nomComplet);
                var courrierDir = Path.Combine(patientDir, "courriers");
                Directory.CreateDirectory(courrierDir);

                var now = DateTime.Now;
                var fileName = $"{now:yyyy-MM-dd_HHmm}_{slug}.md";
                var filePath = Path.Combine(courrierDir, fileName);

                // Gérer les doublons
                int version = 2;
                while (File.Exists(filePath))
                {
                    fileName = $"{now:yyyy-MM-dd_HHmm}_{slug}-v{version}.md";
                    filePath = Path.Combine(courrierDir, fileName);
                    version++;
                }

                // Créer le contenu avec en-tête YAML
                var content = new StringBuilder();
                content.AppendLine("---");
                content.AppendLine($"patient: \"{nomComplet}\"");
                content.AppendLine($"date: \"{now:yyyy-MM-ddTHH:mm}\"");
                content.AppendLine("type: \"courrier\"");
                content.AppendLine("status: \"brouillon\"");
                content.AppendLine("---");
                content.AppendLine();
                content.Append(markdown);

                File.WriteAllText(filePath, content.ToString(), Encoding.UTF8);

                return (true, $"Brouillon sauvegardé: {fileName}", filePath);
            }
            catch (Exception ex)
            {
                return (false, $"Erreur sauvegarde: {ex.Message}", string.Empty);
            }
        }

        /// <summary>
        /// Valide un brouillon de courrier (change status à "validé")
        /// </summary>
        public (bool success, string message) ValidateLetter(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return (false, "Fichier introuvable");

                var content = File.ReadAllText(filePath);
                
                // Changer le status
                content = content.Replace("status: \"brouillon\"", "status: \"validé\"");
                
                File.WriteAllText(filePath, content, Encoding.UTF8);

                return (true, "Courrier validé et finalisé");
            }
            catch (Exception ex)
            {
                return (false, $"Erreur validation: {ex.Message}");
            }
        }

        /// <summary>
        /// Export en .docx (LibreOffice/Word) format A4 professionnel avec logo et en-tête
        /// </summary>
        public (bool success, string message, string docxPath) ExportToDocx(string nomComplet, string markdown, string markdownFilePath)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[ExportToDocx] DÉBUT - Patient: {nomComplet}, MD Path: {markdownFilePath}");
                
                // Détecter le type de document (attestation, ordonnance, ou courrier)
                bool isAttestation = markdownFilePath.Contains(Path.DirectorySeparatorChar + "attestations" + Path.DirectorySeparatorChar);
                bool isOrdonnance = markdownFilePath.Contains(Path.DirectorySeparatorChar + "ordonnances" + Path.DirectorySeparatorChar);

                System.Diagnostics.Debug.WriteLine($"[ExportToDocx] Type détecté - Attestation: {isAttestation}, Ordonnance: {isOrdonnance}");

                // Normaliser le chemin et le nom (enlever les espaces de fin potentiels venant de TextRange.Text)
                markdownFilePath = markdownFilePath.Trim();
                markdown = markdown.Trim();

                string courrierDir = Path.GetDirectoryName(markdownFilePath) 
                    ?? throw new InvalidOperationException("Impossible d'extraire le dossier du fichier markdown");
                
                System.Diagnostics.Debug.WriteLine($"[ExportToDocx] Dossier cible: {courrierDir}");
                Directory.CreateDirectory(courrierDir);

                var baseName = Path.GetFileNameWithoutExtension(markdownFilePath);
                var docxFileName = $"{baseName}.docx";
                var docxPath = Path.Combine(courrierDir, docxFileName);

                System.Diagnostics.Debug.WriteLine($"[ExportToDocx] Nom de fichier: {docxFileName}, Chemin complet: {docxPath}");

                // Gérer les doublons : NON, on veut écraser le fichier existant lors d'une modification
                // Si le fichier existe déjà, on tente de le supprimer pour le recréer
                if (File.Exists(docxPath))
                {
                    try
                    {
                        File.Delete(docxPath);
                        System.Diagnostics.Debug.WriteLine($"[ExportToDocx] Fichier existant supprimé pour mise à jour");
                    }
                    catch (IOException ex)
                    {
                        // Probablement ouvert dans Word
                        return (false, $"Le fichier est ouvert dans un autre programme. Veuillez le fermer et réessayer.\n{ex.Message}", docxPath);
                    }
                }

                System.Diagnostics.Debug.WriteLine($"[ExportToDocx] Création du document Word...");
                
                // Créer le document Word avec format A4 professionnel
                using (WordprocessingDocument wordDoc = WordprocessingDocument.Create(docxPath, WordprocessingDocumentType.Document))
                {
                    System.Diagnostics.Debug.WriteLine($"[ExportToDocx] WordprocessingDocument créé");
                    MainDocumentPart mainPart = wordDoc.AddMainDocumentPart();
                    mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document();
                    Body body = mainPart.Document.AppendChild(new Body());

                    // ===== CONFIGURATION PAGE A4 =====
                    // Section properties pour format A4 avec marges
                    var sectionProps = new SectionProperties();
                    
                    // Format A4 : 21cm × 29.7cm = 11906 × 16838 TWIPs (1 cm = 567 TWIPs)
                    var pageSize = new DocumentFormat.OpenXml.Wordprocessing.PageSize() 
                    { 
                        Width = (UInt32Value)11906U,  // 21 cm
                        Height = (UInt32Value)16838U   // 29.7 cm
                    };
                    
                    // Marges optimisées : 1.5 cm = 850 TWIPs
                    // Pour maximiser l'espace sur 1 page A4
                    var pageMargin = new PageMargin()
                    {
                        Top = 850,
                        Right = (UInt32Value)850U,
                        Bottom = 850,
                        Left = (UInt32Value)850U,
                        Header = (UInt32Value)720U,
                        Footer = (UInt32Value)720U,
                        Gutter = (UInt32Value)0U
                    };
                    
                    sectionProps.Append(pageSize);
                    sectionProps.Append(pageMargin);

                    // ===== EN-TÊTE AVEC LOGO =====
                    // Créer une table pour placer logo à gauche et coordonnées à droite
                    var headerTable = new Table();
                    
                    // Propriétés de la table
                    var tableProps = new TableProperties();
                    var tableWidth = new TableWidth() { Width = "0", Type = TableWidthUnitValues.Auto };
                    tableProps.Append(tableWidth);
                    var tableBorders = new TableBorders(
                        new TopBorder { Val = BorderValues.None },
                        new BottomBorder { Val = BorderValues.None },
                        new LeftBorder { Val = BorderValues.None },
                        new RightBorder { Val = BorderValues.None },
                        new InsideHorizontalBorder { Val = BorderValues.None },
                        new InsideVerticalBorder { Val = BorderValues.None }
                    );
                    tableProps.Append(tableBorders);
                    headerTable.Append(tableProps);
                    
                    // Définir les colonnes (logo gauche + coordonnées droite)
                    var tableGrid = new TableGrid();
                    tableGrid.Append(new GridColumn() { Width = "2500" }); // Logo
                    tableGrid.Append(new GridColumn() { Width = "6000" }); // Coordonnées
                    headerTable.Append(tableGrid);
                    
                    // Ligne du header
                    var headerRow = new TableRow();
                    
                    // Cellule logo (gauche)
                    var logoCell = new TableCell();
                    var logoCellProps = new TableCellProperties();
                    logoCellProps.Append(new TableCellVerticalAlignment() { Val = TableVerticalAlignmentValues.Center });
                    logoCell.Append(logoCellProps);
                    
                    var logoPara = new Paragraph();
                    
                    // Essayer de charger le logo depuis Assets
                    // Chercher dans le sous-dossier logo.png/
                    var logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "logo.png", "ChatGPT Image Oct 14, 2025, 02_22_45 PM.png");
                    
                    // Si pas trouvé, chercher n'importe quel .png dans logo.png/
                    if (!File.Exists(logoPath))
                    {
                        var logoDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "logo.png");
                        if (Directory.Exists(logoDir))
                        {
                            var pngFiles = Directory.GetFiles(logoDir, "*.png");
                            if (pngFiles.Length > 0)
                            {
                                logoPath = pngFiles[0];
                            }
                        }
                    }
                    
                    if (File.Exists(logoPath))
                    {
                        try
                        {
                            // Ajouter l'image au document
                            ImagePart imagePart = mainPart.AddImagePart(ImagePartType.Png);
                            using (FileStream stream = new FileStream(logoPath, FileMode.Open, FileAccess.Read))
                            {
                                imagePart.FeedData(stream);
                            }
                            
                            // Créer le Drawing pour l'image (4cm × 4cm = 1524000 EMUs)
                            AddImageToCell(logoPara, mainPart.GetIdOfPart(imagePart), 1524000, 1524000);
                        }
                        catch (Exception ex)
                        {
                            // Si échec, ajouter texte de remplacement
                            System.Diagnostics.Debug.WriteLine($"Erreur chargement logo: {ex.Message}");
                            var logoRun = logoPara.AppendChild(new Run(new Text("🦋")));
                            var logoRunProps = logoRun.InsertBefore(new RunProperties(), logoRun.FirstChild);
                            logoRunProps.AppendChild(new FontSize() { Val = "48" });
                        }
                    }
                    else
                    {
                        // Pas de logo, ajouter emoji de remplacement
                        var logoRun = logoPara.AppendChild(new Run(new Text("🦋")));
                        var logoRunProps = logoRun.InsertBefore(new RunProperties(), logoRun.FirstChild);
                        logoRunProps.AppendChild(new FontSize() { Val = "48" });
                    }
                    
                    logoCell.Append(logoPara);
                    headerRow.Append(logoCell);
                    
                    // Cellule coordonnées (droite)
                    var coordCell = new TableCell();
                    var coordCellProps = new TableCellProperties();
                    coordCellProps.Append(new TableCellVerticalAlignment() { Val = TableVerticalAlignmentValues.Center });
                    coordCell.Append(coordCellProps);
                    
                    // Paragraphe coordonnées avec toutes les infos
                    var coordLines = new[]
                    {
                        _settings.Medecin,
                        _settings.Specialite,
                        $"RPPS : {_settings.Rpps}",
                        $"FINESS : {_settings.Finess}",
                        $"Tél : {_settings.Telephone}",
                        $"Courriel : {_settings.Email}"
                    };
                    
                    foreach (var line in coordLines)
                    {
                        var p = new Paragraph();
                        var run = p.AppendChild(new Run(new Text(line)));
                        var runProps = run.InsertBefore(new RunProperties(), run.FirstChild);
                        runProps.AppendChild(new FontSize() { Val = "18" }); // 9pt
                        runProps.AppendChild(new RunFonts() { Ascii = "Arial" });
                        
                        // Première ligne en gras
                        if (line == _settings.Medecin)
                        {
                            runProps.AppendChild(new Bold());
                        }
                        
                        coordCell.Append(p);
                    }
                    
                    headerRow.Append(coordCell);
                    headerTable.Append(headerRow);
                    body.Append(headerTable);
                    
                    // Espace après en-tête
                    body.AppendChild(new Paragraph());
                    body.AppendChild(new Paragraph());

                    // ===== CORPS DU COURRIER =====
                    // Parser le Markdown et créer le document avec styles
                    ParseMarkdownToWordProfessional(markdown, body);
                    
                    // ===== SIGNATURE =====
                    body.AppendChild(new Paragraph());
                    
                    var signaturePara = new Paragraph();
                    var signatureProps = signaturePara.AppendChild(new ParagraphProperties());
                    signatureProps.AppendChild(new Justification() { Val = JustificationValues.Right });
                    
                    var signatureRun = signaturePara.AppendChild(new Run());
                    signatureRun.AppendChild(new Text($"Fait au {_settings.Ville}, le {DateTime.Now:dd/MM/yyyy}"));
                    var signatureRunProps = signatureRun.InsertBefore(new RunProperties(), signatureRun.FirstChild);
                    signatureRunProps.AppendChild(new FontSize() { Val = "22" }); // 11pt
                    
                    body.AppendChild(signaturePara);
                    
                    // Nom du médecin
                    var doctorPara = new Paragraph();
                    var doctorProps = doctorPara.AppendChild(new ParagraphProperties());
                    doctorProps.AppendChild(new Justification() { Val = JustificationValues.Right });
                    
                    var doctorRun = doctorPara.AppendChild(new Run());
                    doctorRun.AppendChild(new Text(_settings.Medecin));
                    var doctorRunProps = doctorRun.InsertBefore(new RunProperties(), doctorRun.FirstChild);
                    doctorRunProps.AppendChild(new FontSize() { Val = "22" });
                    doctorRunProps.AppendChild(new Bold());
                    
                    body.AppendChild(doctorPara);
                    
                    // Spécialité
                    var specialitePara = new Paragraph();
                    var specialiteProps = specialitePara.AppendChild(new ParagraphProperties());
                    specialiteProps.AppendChild(new Justification() { Val = JustificationValues.Right });
                    
                    var specialiteRun = specialitePara.AppendChild(new Run());
                    specialiteRun.AppendChild(new Text("Pédopsychiatre"));
                    var specialiteRunProps = specialiteRun.InsertBefore(new RunProperties(), specialiteRun.FirstChild);
                    specialiteRunProps.AppendChild(new FontSize() { Val = "22" });
                    
                    body.AppendChild(specialitePara);
                    
                    // ===== SIGNATURE NUMÉRIQUE =====
                    if (_settings.EnableDigitalSignature)
                    {
                        body.AppendChild(new Paragraph()); // Espace avant signature
                        AddSignatureImage(body, mainPart);
                        AddTimestamp(body);
                    }

                    // ===== PIED DE PAGE =====
                    // Ajouter des espaces pour pousser le footer vers le bas
                    for (int i = 0; i < 3; i++)
                    {
                        body.AppendChild(new Paragraph());
                    }
                    
                    // Footer avec adresse
                    var footerPara = new Paragraph();
                    var footerProps = footerPara.AppendChild(new ParagraphProperties());
                    footerProps.AppendChild(new Justification() { Val = JustificationValues.Center });
                    
                    var footerRun = footerPara.AppendChild(new Run());
                    footerRun.AppendChild(new Text(_settings.Adresse));
                    var footerRunProps = footerRun.InsertBefore(new RunProperties(), footerRun.FirstChild);
                    footerRunProps.AppendChild(new FontSize() { Val = "18" }); // 9pt
                    footerRunProps.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Color() { Val = "666666" }); // Gris
                    
                    body.AppendChild(footerPara);
                    
                    // Ajouter les propriétés de section à la fin
                    body.Append(sectionProps);
                    
                    System.Diagnostics.Debug.WriteLine($"[ExportToDocx] Document Word construit, sauvegarde...");
                }

                System.Diagnostics.Debug.WriteLine($"[ExportToDocx] Document Word sauvegardé");

                // ===== EMPREINTE SHA-256 =====
                if (_settings.EnableDigitalSignature)
                {
                    var hash = CalculateSHA256(docxPath);
                    if (!string.IsNullOrEmpty(hash))
                    {
                        AddHashToDocument(docxPath, hash);
                    }
                }

                // Marquer le contenu comme validé (le markdown passé contient déjà les modifs)
                // On met à jour le fichier .md avec le status "validé" si besoin
                if (File.Exists(markdownFilePath))
                {
                    try
                    {
                        // On réutilise le markdown passé en paramètre pour être sûr de ne rien perdre
                        // Si le markdown ne contient pas déjà le status validé, on le met à jour
                        if (markdown.Contains("status: \"brouillon\""))
                        {
                            var updatedMarkdown = markdown.Replace("status: \"brouillon\"", "status: \"validé\"");
                            File.WriteAllText(markdownFilePath, updatedMarkdown, Encoding.UTF8);
                        }
                    }
                    catch (Exception mdEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ExportToDocx] ⚠️ Erreur maj status MD: {mdEx.Message}");
                    }
                }

                return (true, $"✅ Document créé: {docxFileName} ({markdown.Length} chars)", docxPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ExportToDocx] ❌ EXCEPTION: {ex.Message}");
                return (false, $"❌ Erreur export .docx: {ex.Message}", string.Empty);
            }
        }
        
        /// <summary>
        /// Ajoute une image à un paragraphe
        /// </summary>
        private void AddImageToCell(Paragraph paragraph, string relationshipId, long width, long height)
        {
            var element = new Drawing(
                new DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline(
                    new DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent() { Cx = width, Cy = height },
                    new DocumentFormat.OpenXml.Drawing.Wordprocessing.EffectExtent()
                    {
                        LeftEdge = 0L,
                        TopEdge = 0L,
                        RightEdge = 0L,
                        BottomEdge = 0L
                    },
                    new DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties()
                    {
                        Id = (UInt32Value)1U,
                        Name = "Logo"
                    },
                    new DocumentFormat.OpenXml.Drawing.Wordprocessing.NonVisualGraphicFrameDrawingProperties(
                        new DocumentFormat.OpenXml.Drawing.GraphicFrameLocks() { NoChangeAspect = true }),
                    new DocumentFormat.OpenXml.Drawing.Graphic(
                        new DocumentFormat.OpenXml.Drawing.GraphicData(
                            new DocumentFormat.OpenXml.Drawing.Pictures.Picture(
                                new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualPictureProperties(
                                    new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualDrawingProperties()
                                    {
                                        Id = (UInt32Value)0U,
                                        Name = "Logo.png"
                                    },
                                    new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualPictureDrawingProperties()),
                                new DocumentFormat.OpenXml.Drawing.Pictures.BlipFill(
                                    new DocumentFormat.OpenXml.Drawing.Blip(
                                        new DocumentFormat.OpenXml.Drawing.BlipExtensionList(
                                            new DocumentFormat.OpenXml.Drawing.BlipExtension()
                                            {
                                                Uri = "{28A0092B-C50C-407E-A947-70E740481C1C}"
                                            })
                                    )
                                    {
                                        Embed = relationshipId,
                                        CompressionState = DocumentFormat.OpenXml.Drawing.BlipCompressionValues.Print
                                    },
                                    new DocumentFormat.OpenXml.Drawing.Stretch(
                                        new DocumentFormat.OpenXml.Drawing.FillRectangle())),
                                new DocumentFormat.OpenXml.Drawing.Pictures.ShapeProperties(
                                    new DocumentFormat.OpenXml.Drawing.Transform2D(
                                        new DocumentFormat.OpenXml.Drawing.Offset() { X = 0L, Y = 0L },
                                        new DocumentFormat.OpenXml.Drawing.Extents() { Cx = width, Cy = height }),
                                    new DocumentFormat.OpenXml.Drawing.PresetGeometry(
                                        new DocumentFormat.OpenXml.Drawing.AdjustValueList()
                                    )
                                    { Preset = DocumentFormat.OpenXml.Drawing.ShapeTypeValues.Rectangle }))
                        )
                        { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })
                )
                {
                    DistanceFromTop = (UInt32Value)0U,
                    DistanceFromBottom = (UInt32Value)0U,
                    DistanceFromLeft = (UInt32Value)0U,
                    DistanceFromRight = (UInt32Value)0U,
                    EditId = "50D07946"
                });

            paragraph.AppendChild(new Run(element));
        }
        
        /// <summary>
        /// Ajoute l'image de signature numérique au document
        /// </summary>
        private void AddSignatureImage(Body body, MainDocumentPart mainPart)
        {
            try
            {
                var signaturePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _settings.SignatureImagePath);
                
                if (!File.Exists(signaturePath))
                {
                    System.Diagnostics.Debug.WriteLine($"[AddSignatureImage] Fichier signature introuvable: {signaturePath}");
                    return;
                }
                
                // Ajouter l'image au document
                ImagePart imagePart = mainPart.AddImagePart(ImagePartType.Png);
                using (FileStream stream = new FileStream(signaturePath, FileMode.Open, FileAccess.Read))
                {
                    imagePart.FeedData(stream);
                }
                
                // Créer un paragraphe aligné à droite pour la signature
                var signaturePara = new Paragraph();
                var signatureProps = signaturePara.AppendChild(new ParagraphProperties());
                signatureProps.AppendChild(new Justification() { Val = JustificationValues.Right });
                
                // Taille de l'image : 3cm × 1.5cm = 1143000 × 571500 EMUs
                AddImageToCell(signaturePara, mainPart.GetIdOfPart(imagePart), 1143000, 571500);
                
                body.AppendChild(signaturePara);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AddSignatureImage] Erreur: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Ajoute l'horodatage de la signature numérique
        /// </summary>
        private void AddTimestamp(Body body)
        {
            var timestampPara = new Paragraph();
            var timestampProps = timestampPara.AppendChild(new ParagraphProperties());
            timestampProps.AppendChild(new Justification() { Val = JustificationValues.Right });
            
            var timestampRun = timestampPara.AppendChild(new Run());
            var timestamp = $"Signé numériquement le {DateTime.Now:dd/MM/yyyy} à {DateTime.Now:HH:mm:ss}";
            timestampRun.AppendChild(new Text(timestamp));
            
            // Style : 9pt, italique, gris
            var runProps = timestampRun.InsertBefore(new RunProperties(), timestampRun.FirstChild);
            runProps.AppendChild(new FontSize() { Val = "18" }); // 9pt
            runProps.AppendChild(new Italic());
            runProps.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Color() { Val = "666666" });
            runProps.AppendChild(new RunFonts() { Ascii = "Arial" });
            
            body.AppendChild(timestampPara);
        }
        
        /// <summary>
        /// Calcule l'empreinte SHA-256 d'un fichier
        /// </summary>
        private string CalculateSHA256(string filePath)
        {
            try
            {
                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                using (var stream = File.OpenRead(filePath))
                {
                    byte[] hash = sha256.ComputeHash(stream);
                    return BitConverter.ToString(hash).Replace("-", "").ToLower();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CalculateSHA256] Erreur: {ex.Message}");
                return string.Empty;
            }
        }
        
        /// <summary>
        /// Ajoute l'empreinte SHA-256 au document
        /// </summary>
        private void AddHashToDocument(string docxPath, string hash)
        {
            try
            {
                if (string.IsNullOrEmpty(hash))
                    return;
                
                using (WordprocessingDocument doc = WordprocessingDocument.Open(docxPath, true))
                {
                    var mainPart = doc.MainDocumentPart;
                    if (mainPart?.Document?.Body == null)
                        return;
                    
                    var body = mainPart.Document.Body;
                    
                    // Trouver le dernier paragraphe (footer avec adresse)
                    var lastPara = body.Elements<Paragraph>().LastOrDefault();
                    
                    // Ajouter le hash juste après
                    var hashPara = new Paragraph();
                    var hashProps = hashPara.AppendChild(new ParagraphProperties());
                    hashProps.AppendChild(new Justification() { Val = JustificationValues.Center });
                    
                    var hashRun = hashPara.AppendChild(new Run());
                    // Afficher les 32 premiers caractères du hash (sur 64 total)
                    hashRun.AppendChild(new Text($"Empreinte SHA-256: {hash.Substring(0, 32)}..."));
                    
                    // Style : 7pt, gris clair
                    var runProps = hashRun.InsertBefore(new RunProperties(), hashRun.FirstChild);
                    runProps.AppendChild(new FontSize() { Val = "14" }); // 7pt
                    runProps.AppendChild(new DocumentFormat.OpenXml.Wordprocessing.Color() { Val = "AAAAAA" });
                    runProps.AppendChild(new RunFonts() { Ascii = "Arial" });
                    
                    if (lastPara != null)
                    {
                        body.InsertAfter(hashPara, lastPara);
                    }
                    else
                    {
                        body.AppendChild(hashPara);
                    }
                    
                    mainPart.Document.Save();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AddHashToDocument] Erreur: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Parse le Markdown vers Word avec formatage professionnel (titres centrés, corps justifié)
        /// </summary>
        private void ParseMarkdownToWordProfessional(string markdown, Body body)
        {
            // Retirer l'en-tête YAML si présent
            var cleanMarkdown = markdown;
            if (markdown.TrimStart().StartsWith("---"))
            {
                var allLines = markdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                bool inYaml = false;
                int yamlEndIndex = 0;
                
                for (int i = 0; i < allLines.Length; i++)
                {
                    if (i == 0 && allLines[i].Trim() == "---")
                    {
                        inYaml = true;
                        continue;
                    }
                    if (inYaml && allLines[i].Trim() == "---")
                    {
                        yamlEndIndex = i + 1;
                        break;
                    }
                }
                
                if (yamlEndIndex > 0)
                {
                    cleanMarkdown = string.Join("\n", allLines.Skip(yamlEndIndex));
                }
            }
            
            var lines = cleanMarkdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];

                if (string.IsNullOrWhiteSpace(line))
                {
                    body.AppendChild(new Paragraph());
                    continue;
                }

                // ===== DETECTION DES TABLEAUX MARKDOWN =====
                var trimmedLine = line.Trim();
                if (trimmedLine.StartsWith("|") || IsTableSeparatorLine(trimmedLine))
                {
                    // Collecter toutes les lignes du tableau
                    var tableLines = new List<string>();
                    while (i < lines.Length)
                    {
                        var currentLine = lines[i].Trim();
                        if (string.IsNullOrWhiteSpace(currentLine))
                            break;
                        if (!currentLine.StartsWith("|") && !IsTableSeparatorLine(currentLine))
                            break;
                        tableLines.Add(currentLine);
                        i++;
                    }
                    i--; // Reculer d'une position car la boucle for va incrémenter

                    // Créer le tableau Word
                    if (tableLines.Count > 0)
                    {
                        var wordTable = CreateWordTableFromMarkdown(tableLines);
                        body.AppendChild(wordTable);

                        // Ajouter un petit espacement après le tableau
                        var spacingPara = body.AppendChild(new Paragraph());
                        var spacingProps = spacingPara.AppendChild(new ParagraphProperties());
                        spacingProps.AppendChild(new SpacingBetweenLines() { After = "100" });
                    }
                    continue;
                }

                // Titre H1 (# Titre) - CENTRÉ ET GRAS
                if (line.StartsWith("# "))
                {
                    var titleText = line.Substring(2).Trim();
                    var para = body.AppendChild(new Paragraph());

                    // Propriétés du paragraphe : centré
                    var paraProps = para.AppendChild(new ParagraphProperties());
                    paraProps.AppendChild(new Justification() { Val = JustificationValues.Center });

                    var run = para.AppendChild(new Run());
                    run.AppendChild(new Text(titleText));

                    // Style titre: 14pt, gras, majuscules
                    var runProps = run.InsertBefore(new RunProperties(), run.FirstChild);
                    runProps.AppendChild(new Bold());
                    runProps.AppendChild(new FontSize() { Val = "28" }); // 14pt
                    runProps.AppendChild(new RunFonts() { Ascii = "Arial" });
                    continue;
                }

                // Titre H2 (## Sous-titre)
                if (line.StartsWith("## "))
                {
                    var subtitleText = line.Substring(3).Trim();
                    var para = body.AppendChild(new Paragraph());
                    var run = para.AppendChild(new Run());
                    run.AppendChild(new Text(subtitleText));

                    var runProps = run.InsertBefore(new RunProperties(), run.FirstChild);
                    runProps.AppendChild(new Bold());
                    runProps.AppendChild(new FontSize() { Val = "24" }); // 12pt
                    runProps.AppendChild(new RunFonts() { Ascii = "Arial" });
                    continue;
                }

                // Paragraphe normal - JUSTIFIÉ avec interligne simple + pas d'espacement
                var paragraph = body.AppendChild(new Paragraph());

                // Propriétés du paragraphe : justifié + interligne simple + AUCUN espacement
                var paragraphProps = paragraph.AppendChild(new ParagraphProperties());
                paragraphProps.AppendChild(new Justification() { Val = JustificationValues.Both });
                paragraphProps.AppendChild(new SpacingBetweenLines()
                {
                    Line = "240",  // 1.0 (simple) = 240
                    LineRule = LineSpacingRuleValues.Auto,
                    After = "0",    // 0pt après paragraphe pour maximiser l'espace
                    Before = "0"    // Pas d'espace avant
                });

                ParseInlineMarkdownProfessional(line, paragraph);
            }
        }
        
        /// <summary>
        /// Parse les styles inline Markdown avec formatage professionnel
        /// </summary>
        private void ParseInlineMarkdownProfessional(string text, Paragraph paragraph)
        {
            var pattern = @"(\*\*[^*]+\*\*)|(\*[^*]+\*)";
            var regex = new Regex(pattern);
            
            int lastIndex = 0;
            
            foreach (Match match in regex.Matches(text))
            {
                if (match.Index > lastIndex)
                {
                    var normalText = text.Substring(lastIndex, match.Index - lastIndex);
                    var run = paragraph.AppendChild(new Run());
                    run.AppendChild(new Text(normalText) { Space = SpaceProcessingModeValues.Preserve });
                    
                    // Style par défaut : 10pt, Arial (optimisé pour 1 page)
                    var runProps = run.InsertBefore(new RunProperties(), run.FirstChild);
                    runProps.AppendChild(new FontSize() { Val = "20" }); // 10pt
                    runProps.AppendChild(new RunFonts() { Ascii = "Arial" });
                }

                var matchedText = match.Value;
                var run2 = paragraph.AppendChild(new Run());
                
                if (matchedText.StartsWith("**") && matchedText.EndsWith("**"))
                {
                    var boldText = matchedText.Substring(2, matchedText.Length - 4);
                    run2.AppendChild(new Text(boldText) { Space = SpaceProcessingModeValues.Preserve });
                    var runProps = run2.InsertBefore(new RunProperties(), run2.FirstChild);
                    runProps.AppendChild(new Bold());
                    runProps.AppendChild(new FontSize() { Val = "20" }); // 10pt
                    runProps.AppendChild(new RunFonts() { Ascii = "Arial" });
                }
                else if (matchedText.StartsWith("*") && matchedText.EndsWith("*"))
                {
                    var italicText = matchedText.Substring(1, matchedText.Length - 2);
                    run2.AppendChild(new Text(italicText) { Space = SpaceProcessingModeValues.Preserve });
                    var runProps = run2.InsertBefore(new RunProperties(), run2.FirstChild);
                    runProps.AppendChild(new Italic());
                    runProps.AppendChild(new FontSize() { Val = "20" }); // 10pt
                    runProps.AppendChild(new RunFonts() { Ascii = "Arial" });
                }

                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < text.Length)
            {
                var remainingText = text.Substring(lastIndex);
                var run = paragraph.AppendChild(new Run());
                run.AppendChild(new Text(remainingText) { Space = SpaceProcessingModeValues.Preserve });
                
                var runProps = run.InsertBefore(new RunProperties(), run.FirstChild);
                runProps.AppendChild(new FontSize() { Val = "20" }); // 10pt
                runProps.AppendChild(new RunFonts() { Ascii = "Arial" });
            }
        }

        /// <summary>
        /// Vérifie si une ligne est un séparateur de tableau markdown (ex: |---|---|)
        /// </summary>
        private bool IsTableSeparatorLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var trimmed = line.Trim();
            // Doit contenir au moins un tiret et des pipes ou uniquement des tirets/pipes
            if (!trimmed.Contains("-"))
                return false;

            // Vérifier que la ligne ne contient que des caractères valides pour un séparateur
            foreach (char c in trimmed)
            {
                if (c != '|' && c != '-' && c != ':' && c != ' ')
                    return false;
            }

            return trimmed.Length >= 3;
        }

        /// <summary>
        /// Crée un tableau Word à partir des lignes markdown
        /// </summary>
        private Table CreateWordTableFromMarkdown(List<string> tableLines)
        {
            var table = new Table();

            // Propriétés du tableau avec bordures
            var tableProps = new TableProperties();
            var tableWidth = new TableWidth() { Width = "5000", Type = TableWidthUnitValues.Pct }; // 100% de la largeur
            tableProps.Append(tableWidth);

            // Bordures du tableau (style professionnel)
            var tableBorders = new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 8, Color = "2980B9" },
                new BottomBorder { Val = BorderValues.Single, Size = 8, Color = "2980B9" },
                new LeftBorder { Val = BorderValues.Single, Size = 8, Color = "2980B9" },
                new RightBorder { Val = BorderValues.Single, Size = 8, Color = "2980B9" },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "BDC3C7" },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = "BDC3C7" }
            );
            tableProps.Append(tableBorders);
            table.Append(tableProps);

            // Parser les lignes et créer les rangées
            bool isFirstDataRow = true;
            int columnCount = 0;

            foreach (var line in tableLines)
            {
                // Ignorer les lignes séparatrices
                if (IsTableSeparatorLine(line))
                    continue;

                var cells = ParseTableRow(line);
                if (cells.Count == 0)
                    continue;

                // Déterminer le nombre de colonnes (première ligne)
                if (columnCount == 0)
                    columnCount = cells.Count;

                var tableRow = new TableRow();

                for (int colIndex = 0; colIndex < cells.Count; colIndex++)
                {
                    var cellContent = cells[colIndex];
                    var tableCell = new TableCell();

                    // Propriétés de la cellule
                    var cellProps = new TableCellProperties();

                    // Padding des cellules
                    cellProps.Append(new TableCellMargin(
                        new LeftMargin() { Width = "100", Type = TableWidthUnitValues.Dxa },
                        new RightMargin() { Width = "100", Type = TableWidthUnitValues.Dxa }
                    ));

                    // Style pour l'en-tête (première ligne de données = en-tête)
                    if (isFirstDataRow)
                    {
                        // Fond bleu clair pour l'en-tête
                        cellProps.Append(new Shading()
                        {
                            Val = ShadingPatternValues.Clear,
                            Fill = "E8F4FD" // Bleu très clair
                        });
                    }

                    tableCell.Append(cellProps);

                    // Contenu de la cellule
                    var para = new Paragraph();
                    var paraProps = new ParagraphProperties();
                    paraProps.AppendChild(new SpacingBetweenLines() { After = "0", Before = "60" });
                    para.AppendChild(paraProps);

                    var run = para.AppendChild(new Run());
                    run.AppendChild(new Text(cellContent) { Space = SpaceProcessingModeValues.Preserve });

                    var runProps = run.InsertBefore(new RunProperties(), run.FirstChild);
                    runProps.AppendChild(new FontSize() { Val = "18" }); // 9pt pour les tableaux
                    runProps.AppendChild(new RunFonts() { Ascii = "Arial" });

                    // En-tête en gras
                    if (isFirstDataRow)
                    {
                        runProps.AppendChild(new Bold());
                    }

                    tableCell.Append(para);
                    tableRow.Append(tableCell);
                }

                table.Append(tableRow);
                isFirstDataRow = false;
            }

            return table;
        }

        /// <summary>
        /// Parse une ligne de tableau markdown et retourne les cellules
        /// </summary>
        private List<string> ParseTableRow(string line)
        {
            var cells = new List<string>();

            if (string.IsNullOrWhiteSpace(line))
                return cells;

            // Retirer les pipes au début et à la fin
            var trimmed = line.Trim();
            if (trimmed.StartsWith("|"))
                trimmed = trimmed.Substring(1);
            if (trimmed.EndsWith("|"))
                trimmed = trimmed.Substring(0, trimmed.Length - 1);

            // Séparer par les pipes
            var parts = trimmed.Split('|');
            foreach (var part in parts)
            {
                cells.Add(part.Trim());
            }

            return cells;
        }

        /// <summary>
        /// Parse le Markdown et le convertit en éléments Word avec styles préservés
        /// </summary>
        private void ParseMarkdownToWord(string markdown, Body body)
        {
            // Retirer l'en-tête YAML si présent (entre --- et ---)
            var cleanMarkdown = markdown;
            if (markdown.TrimStart().StartsWith("---"))
            {
                var allLines = markdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                bool inYaml = false;
                int yamlEndIndex = 0;
                
                for (int i = 0; i < allLines.Length; i++)
                {
                    if (i == 0 && allLines[i].Trim() == "---")
                    {
                        inYaml = true;
                        continue;
                    }
                    if (inYaml && allLines[i].Trim() == "---")
                    {
                        yamlEndIndex = i + 1;
                        break;
                    }
                }
                
                if (yamlEndIndex > 0)
                {
                    cleanMarkdown = string.Join("\n", allLines.Skip(yamlEndIndex));
                }
            }
            
            var lines = cleanMarkdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    // Ligne vide → Saut de paragraphe
                    body.AppendChild(new Paragraph());
                    continue;
                }

                // Titre H1 (# Titre)
                if (line.StartsWith("# "))
                {
                    var titleText = line.Substring(2).Trim();
                    var para = body.AppendChild(new Paragraph());
                    var run = para.AppendChild(new Run());
                    run.AppendChild(new Text(titleText));
                    
                    // Style titre: 18pt, gras
                    var runProps = run.AppendChild(new RunProperties());
                    runProps.AppendChild(new Bold());
                    runProps.AppendChild(new FontSize() { Val = "36" }); // 18pt = 36 half-points
                    continue;
                }

                // Titre H2 (## Sous-titre)
                if (line.StartsWith("## "))
                {
                    var subtitleText = line.Substring(3).Trim();
                    var para = body.AppendChild(new Paragraph());
                    var run = para.AppendChild(new Run());
                    run.AppendChild(new Text(subtitleText));
                    
                    // Style sous-titre: 14pt, gras
                    var runProps = run.AppendChild(new RunProperties());
                    runProps.AppendChild(new Bold());
                    runProps.AppendChild(new FontSize() { Val = "28" }); // 14pt = 28 half-points
                    continue;
                }

                // Paragraphe normal (avec gras/italique inline)
                var paragraph = body.AppendChild(new Paragraph());
                ParseInlineMarkdown(line, paragraph);
            }
        }

        /// <summary>
        /// Parse les styles inline Markdown (**gras**, *italique*, etc.)
        /// </summary>
        private void ParseInlineMarkdown(string text, Paragraph paragraph)
        {
            // Pattern pour détecter **gras** et *italique*
            var pattern = @"(\*\*[^*]+\*\*)|(\*[^*]+\*)";
            var regex = new Regex(pattern);
            
            int lastIndex = 0;
            
            foreach (Match match in regex.Matches(text))
            {
                // Texte avant le match (normal)
                if (match.Index > lastIndex)
                {
                    var normalText = text.Substring(lastIndex, match.Index - lastIndex);
                    var run = paragraph.AppendChild(new Run());
                    run.AppendChild(new Text(normalText));
                }

                // Texte avec style
                var matchedText = match.Value;
                var run2 = paragraph.AppendChild(new Run());
                
                if (matchedText.StartsWith("**") && matchedText.EndsWith("**"))
                {
                    // Gras
                    var boldText = matchedText.Substring(2, matchedText.Length - 4);
                    run2.AppendChild(new Text(boldText));
                    var runProps = run2.AppendChild(new RunProperties());
                    runProps.AppendChild(new Bold());
                }
                else if (matchedText.StartsWith("*") && matchedText.EndsWith("*"))
                {
                    // Italique
                    var italicText = matchedText.Substring(1, matchedText.Length - 2);
                    run2.AppendChild(new Text(italicText));
                    var runProps = run2.AppendChild(new RunProperties());
                    runProps.AppendChild(new Italic());
                }

                lastIndex = match.Index + match.Length;
            }

            // Texte restant après le dernier match
            if (lastIndex < text.Length)
            {
                var remainingText = text.Substring(lastIndex);
                var run = paragraph.AppendChild(new Run());
                run.AppendChild(new Text(remainingText));
            }

            // Si aucun match, ajouter tout le texte normalement
            if (!regex.IsMatch(text))
            {
                var run = paragraph.AppendChild(new Run());
                run.AppendChild(new Text(text));
            }
        }

        /// <summary>
        /// Liste tous les courriers d'un patient
        /// </summary>
        public (List<LetterInfo> drafts, List<LetterInfo> validated) GetLetters(string nomComplet)
        {
            var drafts = new List<LetterInfo>();
            var validated = new List<LetterInfo>();

            try
            {
                var patientDir = _storageService.GetPatientDirectory(nomComplet);
                var courrierDir = Path.Combine(patientDir, "courriers");

                if (!Directory.Exists(courrierDir))
                    return (drafts, validated);

                var mdFiles = Directory.GetFiles(courrierDir, "*.md", SearchOption.TopDirectoryOnly);
                var pdfFiles = Directory.GetFiles(courrierDir, "*.pdf", SearchOption.TopDirectoryOnly);

                foreach (var file in mdFiles)
                {
                    var content = File.ReadAllText(file);
                    var isDraft = content.Contains("status: \"brouillon\"");
                    var date = File.GetLastWriteTime(file);
                    var title = ExtractTitleFromMarkdown(content);

                    var letterInfo = new LetterInfo
                    {
                        FilePath = file,
                        Title = title,
                        Date = date,
                        Type = DetermineLetterType(title, content),
                        IsDraft = isDraft
                    };

                    if (isDraft)
                        drafts.Add(letterInfo);
                    else
                        validated.Add(letterInfo);
                }

                // Ajouter les PDF sans .md correspondant
                foreach (var pdfFile in pdfFiles)
                {
                    var mdFile = Path.ChangeExtension(pdfFile, ".md");
                    if (!File.Exists(mdFile))
                    {
                        validated.Add(new LetterInfo
                        {
                            FilePath = pdfFile,
                            Title = Path.GetFileNameWithoutExtension(pdfFile),
                            Date = File.GetLastWriteTime(pdfFile),
                            Type = "PDF",
                            IsDraft = false
                        });
                    }
                }

                drafts = drafts.OrderByDescending(l => l.Date).ToList();
                validated = validated.OrderByDescending(l => l.Date).ToList();
            }
            catch
            {
                // Ignorer les erreurs
            }

            return (drafts, validated);
        }

        private string ExtractTitleFromMarkdown(string markdown)
        {
            var lines = markdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                if (line.StartsWith("# "))
                    return line.Substring(2).Trim();
            }
            return "Sans titre";
        }

        private string DetermineLetterType(string title, string content)
        {
            var combined = (title + " " + content).ToLower();
            
            if (combined.Contains("pap") || combined.Contains("aménagement"))
                return "PAP";
            if (combined.Contains("école") || combined.Contains("vie scolaire"))
                return "Vie scolaire";
            if (combined.Contains("psychomot"))
                return "Psychomotricité";
            if (combined.Contains("parent"))
                return "CR Parents";
            if (combined.Contains("mdph"))
                return "MDPH";
            if (combined.Contains("médecin traitant"))
                return "Médecin traitant";
            
            return "Courrier";
        }

        private string RemoveAccents(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return text;

            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder();

            foreach (var c in normalizedString)
            {
                var unicodeCategory = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }
    }

    public class LetterInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string Type { get; set; } = string.Empty;
        public bool IsDraft { get; set; }
        
        public string DisplayText => $"{Date:dd/MM/yyyy HH:mm} - {Title} ({Type})";
    }
}
