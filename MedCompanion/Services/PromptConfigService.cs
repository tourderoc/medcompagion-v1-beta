using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service de gestion des configurations de prompts
    /// </summary>
    public class PromptConfigService
    {
        private readonly string _configFilePath;
        private PromptsConfiguration _config;
        private readonly AnonymizationService? _anonymizationService;
        
        /// <summary>
        /// Événement déclenché quand les prompts sont modifiés et rechargés
        /// </summary>
        public event EventHandler? PromptsReloaded;
        
        /// <summary>
        /// Constructeur avec injection optionnelle d'AnonymizationService
        /// </summary>
        public PromptConfigService(AnonymizationService? anonymizationService = null)
        {
            _anonymizationService = anonymizationService;
            
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MedCompanion"
            );
            Directory.CreateDirectory(appDataPath);
            _configFilePath = Path.Combine(appDataPath, "prompts-config.json");
            
            _config = LoadOrCreateConfig();
        }
        
        /// <summary>
        /// Charge la configuration existante ou crée une nouvelle avec les valeurs par défaut
        /// </summary>
        private PromptsConfiguration LoadOrCreateConfig()
        {
            if (File.Exists(_configFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_configFilePath);
                    var config = JsonSerializer.Deserialize<PromptsConfiguration>(json);
                    if (config != null)
                    {
                        // ✨ MIGRATION AUTOMATIQUE : Rétrocompatibilité avec anciens fichiers
                        bool needsMigration = MigrateConfigIfNeeded(config);
                        
                        // ✨ NOUVEAU : Ajouter les prompts manquants
                        bool needsNewPrompts = AddMissingPrompts(config);
                        
                        // Sauvegarder si migration effectuée
                        if (needsMigration || needsNewPrompts)
                        {
                            try
                            {
                                var options = new JsonSerializerOptions 
                                { 
                                    WriteIndented = true,
                                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                                };
                                var migratedJson = JsonSerializer.Serialize(config, options);
                                File.WriteAllText(_configFilePath, migratedJson);
                                System.Diagnostics.Debug.WriteLine("[PromptConfigService] Migration automatique effectuée");
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[PromptConfigService] Erreur sauvegarde migration: {ex.Message}");
                            }
                        }
                        
                        return config;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PromptConfigService] Erreur chargement: {ex.Message}");
                }
            }
            
            // Créer configuration par défaut
            return CreateDefaultConfiguration();
        }
        
        /// <summary>
        /// Migre automatiquement les anciens fichiers de configuration
        /// </summary>
        private bool MigrateConfigIfNeeded(PromptsConfiguration config)
        {
            bool migrated = false;
            
            foreach (var prompt in config.Prompts.Values)
            {
                // Si OriginalPrompt est vide, c'est un ancien fichier
                if (string.IsNullOrEmpty(prompt.OriginalPrompt) && !string.IsNullOrEmpty(prompt.DefaultPrompt))
                {
                    // Le DefaultPrompt actuel EST la version d'origine
                    prompt.OriginalPrompt = prompt.DefaultPrompt;
                    migrated = true;
                    System.Diagnostics.Debug.WriteLine($"[PromptConfigService] Migration: {prompt.Id} - OriginalPrompt initialisé");
                }
            }
            
            return migrated;
        }
        
        /// <summary>
        /// Ajoute les prompts manquants depuis la configuration par défaut
        /// </summary>
        private bool AddMissingPrompts(PromptsConfiguration config)
        {
            bool added = false;
            var defaultConfig = CreateDefaultConfiguration();
            
            foreach (var kvp in defaultConfig.Prompts)
            {
                if (!config.Prompts.ContainsKey(kvp.Key))
                {
                    // Ajouter le prompt manquant
                    config.Prompts[kvp.Key] = kvp.Value;
                    added = true;
                    System.Diagnostics.Debug.WriteLine($"[PromptConfigService] Prompt ajouté: {kvp.Key} - {kvp.Value.Name}");
                }
            }
            
            return added;
        }
        
        /// <summary>
        /// Crée la configuration par défaut avec tous les prompts du système
        /// </summary>
        private PromptsConfiguration CreateDefaultConfiguration()
        {
            var config = new PromptsConfiguration();
            
            // PROMPT SYSTÈME GLOBAL
            var systemGlobalPrompt = @"Rôle et perspective :
- Tu es l'assistant clinique du Dr {{Medecin}}. L'UTILISATEUR est le pédopsychiatre.
- Tu t'adresses au clinicien (pas aux parents). Ton destinataire par défaut est le praticien.
- Pour le patient/enfant, utilise la 3ᵉ personne (il/elle, l'enfant, le patient).

Comportement :
- Réponds en français naturel, sans invention. Si l'info manque, dis-le et propose quoi vérifier.
- Analyse clinique : hypothèses non exclusives + différentiel bref, facteurs ±, drapeaux rouges, feuille de route praticien.
- Courriers : uniquement à la demande. Écris EN PREMIÈRE PERSONNE au nom du Dr {{Medecin}}.
- Interdits : ne jamais écrire des tournures type ""pour mon fils"", ""votre enfant"". Toujours 3ᵉ personne pour le patient.

Style :
- Professionnel, concis, orienté pratique. Titres/puces si utile.";
            
            config.Prompts["system_global"] = new PromptConfig
            {
                Id = "system_global",
                Name = "Prompt système global",
                Description = "Prompt système utilisé pour toutes les interactions IA",
                Module = "OpenAI",
                OriginalPrompt = systemGlobalPrompt,
                DefaultPrompt = systemGlobalPrompt,
                IsCustomActive = false
            };
            
            // PROMPT STRUCTURATION DE NOTES
            var noteStructurationPrompt = @"Patient: {{Nom_Complet}}
Tâche: Structure la note suivante en un compte-rendu clinique clair (titres et puces si utile).{{Date_Instruction}}
Note brute:
{{Note_Brute}}";
            
            config.Prompts["note_structuration"] = new PromptConfig
            {
                Id = "note_structuration",
                Name = "Structuration de notes cliniques",
                Description = "Prompt pour transformer une note brute en compte-rendu structuré",
                Module = "OpenAI",
                OriginalPrompt = noteStructurationPrompt,
                DefaultPrompt = noteStructurationPrompt,
                IsCustomActive = false
            };
            
            // PROMPT CHAT INTERACTION
            var chatInteractionPrompt = @"Réponses et style :
- Concis et direct, sans fioritures
- Utilise des listes à puces pour structurer
- Vocabulaire professionnel mais accessible";
            
            config.Prompts["chat_interaction"] = new PromptConfig
            {
                Id = "chat_interaction",
                Name = "Chat - Interaction assistant",
                Description = "Prompt spécifique pour les réponses dans le chat IA",
                Module = "Chat",
                OriginalPrompt = chatInteractionPrompt,
                DefaultPrompt = chatInteractionPrompt,
                IsCustomActive = false
            };
            
            // PROMPT GÉNÉRATION COURRIERS - CONTEXTE
            var letterWithContextPrompt = @"CONTEXTE (extraits)
----
{{Contexte}}

DEMANDE
----
{{User_Request}}

STRUCTURE OBLIGATOIRE
----
À l'attention de : {{{{Destinataire}}}}
École : {{{{Ecole}}}}
Classe : {{{{Classe}}}}

# Objet : [Titre descriptif du courrier]

[Corps du courrier]

⚠️ RÈGLE ABSOLUE pour École et Classe :
→ Si EXPLICITEMENT mentionnés dans le CONTEXTE : Remplace par la valeur EXACTE trouvée
→ Si NON trouvés dans le contexte : Laisse {{{{Ecole}}}} et {{{{Classe}}}} EXACTEMENT tels quels

CONTRAINTES DE STYLE
----
- **EN-TÊTE** : TOUJOURS inclure les 3 lignes d'en-tête
- **Objet** : Titre (# en Markdown) court et descriptif
- **Corps** : Longueur adaptée à la complexité. Style professionnel naturel.
- **Exclusions** : NE PAS inclure de date ni de signature
- ⚠️ **IMPORTANT** : Sois précis, évite toute redondance";
            
            config.Prompts["letter_generation_with_context"] = new PromptConfig
            {
                Id = "letter_generation_with_context",
                Name = "Génération courriers (avec contexte)",
                Description = "Prompt pour générer un courrier quand le contexte patient est disponible",
                Module = "Letter",
                OriginalPrompt = letterWithContextPrompt,
                DefaultPrompt = letterWithContextPrompt,
                IsCustomActive = false
            };
            
            // PROMPT GÉNÉRATION COURRIERS - SANS CONTEXTE
            var letterNoContextPrompt = @"DEMANDE
----
{{User_Request}}

STRUCTURE OBLIGATOIRE
----
À l'attention de : {{{{Destinataire}}}}
École : {{{{Ecole}}}}
Classe : {{{{Classe}}}}

# Objet : [Titre descriptif du courrier]

[Corps du courrier]

CONTRAINTES DE STYLE
----
- **Objet** : Titre (# en Markdown) court et descriptif
- **Corps** : Longueur adaptée, ton professionnel
- **Note** : Contexte patient limité, utilise les placeholders pour les infos manquantes

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
À l'attention de : {{{{Destinataire}}}}
École : {{{{Ecole}}}}
Classe : {{{{Classe}}}}

# Objet : [Titre]
[Corps du courrier - contenu médical uniquement]
[FIN - ne rien ajouter après]";
            
            config.Prompts["letter_generation_no_context"] = new PromptConfig
            {
                Id = "letter_generation_no_context",
                Name = "Génération courriers (sans contexte)",
                Description = "Prompt pour générer un courrier sans contexte patient",
                Module = "Letter",
                OriginalPrompt = letterNoContextPrompt,
                DefaultPrompt = letterNoContextPrompt,
                IsCustomActive = false
            };
            
            // PROMPT ADAPTATION TEMPLATE
            var templateAdaptationPrompt = @"CONTEXTE PATIENT (extraits récents)
----
{{Contexte}}

TYPE DE COURRIER
----
{{Template_Name}}

MODÈLE DE RÉFÉRENCE
----
{{Template_Markdown}}

CONSIGNE
----
Rédige en 12–15 lignes maximum, ton professionnel.
- Adapte les aménagements/recommandations au motif principal
- Format Markdown avec titre (# Objet : ...) et corps UNIQUEMENT
- Personnalise selon le contexte patient
- IMPORTANT : Sois concis, évite toute redondance

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
[FIN - ne rien ajouter après]";
            
            config.Prompts["template_adaptation"] = new PromptConfig
            {
                Id = "template_adaptation",
                Name = "Adaptation de templates",
                Description = "Prompt pour adapter un template de courrier au contexte patient",
                Module = "Letter",
                OriginalPrompt = templateAdaptationPrompt,
                DefaultPrompt = templateAdaptationPrompt,
                IsCustomActive = false
            };
            
            // PROMPT GÉNÉRATION ATTESTATIONS IA PERSONNALISÉES
            var attestationCustomPrompt = @"Tu es l'assistant du Dr {{Medecin}}, pédopsychiatre.
Tu génères des attestations médicales SIMPLES et COURTES.

RÈGLES ABSOLUES :
- Format : Markdown avec titre (# Titre) et corps
- Ton : professionnel, factuel, neutre
- Longueur : MAXIMUM 5-6 lignes de corps
- Structure standard : ""Je soussigné Dr {{Medecin}}, pédopsychiatre, atteste que...""
- Terminer TOUJOURS par : ""Cette attestation est délivrée pour valoir ce que de droit.""
- NE PAS inclure en-tête, date, signature (ajoutés automatiquement)
- NE JAMAIS utiliser les vrais noms/dates - UTILISER UNIQUEMENT les placeholders
- PAS de mentions médicales sensibles (diagnostic précis, traitement, etc.)

⚠️ PLACEHOLDERS OBLIGATOIRES ⚠️
Tu dois IMPÉRATIVEMENT utiliser ces placeholders EXACTS (avec accolades doubles) :
- Dr {{{{Medecin}}}} pour le nom du médecin (PAS {{{{Dr Medecin}}}})
- {{{{Nom_Prenom}}}} pour le nom et prénom du patient
- {{{{Date_Naissance}}}} pour la date de naissance
- {{{{Ne_Nee}}}} pour accord grammatical (né/née)

❌ N'ÉCRIS JAMAIS de vrais noms comme ""DUPONT Jean"", ""Dr Martin"", ""15/06/2013""
✅ UTILISE TOUJOURS {{{{Nom_Prenom}}}}, {{{{Date_Naissance}}}}, etc.

---

{{Patient_Info}}

DEMANDE DE L'UTILISATEUR
----
{{Consigne}}

CONSIGNES DE GÉNÉRATION
----
Génère une attestation SIMPLE et COURTE (5-6 lignes maximum) selon la demande.

⚠️ FORMAT OBLIGATOIRE (copie exactement cette structure) :

# [Titre de l'attestation selon la demande]

Je soussigné Dr {{{{Medecin}}}}, pédopsychiatre, atteste que **{{{{Nom_Prenom}}}}**, {{{{Ne_Nee}}}} le {{{{Date_Naissance}}}}, [contenu adapté à la demande].

Cette attestation est délivrée pour valoir ce que de droit.

EXEMPLES CORRECTS :

Exemple 1 (aptitude piscine) :
# Attestation d'aptitude à la pratique de la natation

Je soussigné Dr {{{{Medecin}}}}, pédopsychiatre, atteste que **{{{{Nom_Prenom}}}}**, {{{{Ne_Nee}}}} le {{{{Date_Naissance}}}}, est apte à pratiquer la natation.

Cette attestation est délivrée pour valoir ce que de droit.

Exemple 2 (contre-indication sport) :
# Contre-indication temporaire aux activités sportives collectives

Je soussigné Dr {{{{Medecin}}}}, pédopsychiatre, atteste que **{{{{Nom_Prenom}}}}**, {{{{Ne_Nee}}}} le {{{{Date_Naissance}}}}, présente une contre-indication temporaire aux activités sportives en collectivité.

Cette attestation est délivrée pour valoir ce que de droit.

⚠️ RAPPEL CRITIQUE :
- Utilise EXACTEMENT Dr {{{{Medecin}}}} (pas ""Dr Lassoued Nair"")
- Utilise EXACTEMENT {{{{Nom_Prenom}}}} (pas ""FRANCHITTI Diego"")
- Utilise EXACTEMENT {{{{Date_Naissance}}}} (pas ""15/06/2013"")
- Utilise EXACTEMENT {{{{Ne_Nee}}}} (pas ""né(e)"")
- Les accolades doubles sont OBLIGATOIRES : {{{{ }}}}";
            
            config.Prompts["attestation_custom_generation"] = new PromptConfig
            {
                Id = "attestation_custom_generation",
                Name = "Génération attestations IA personnalisées",
                Description = "Prompt pour générer des attestations personnalisées avec l'IA",
                Module = "Attestation",
                OriginalPrompt = attestationCustomPrompt,
                DefaultPrompt = attestationCustomPrompt,
                IsCustomActive = false
            };

            // PROMPT SYNTHÈSE COMPLÈTE
            var synthesisCompletePrompt = @"Tu es un médecin pédopsychiatre expérimenté.

MISSION: Créer une synthèse clinique complète et structurée pour le patient {{Patient_Name}}.

CONTENU DU DOSSIER:
{{Patient_Content}}

INSTRUCTIONS:
1. Analyse TOUT le contenu fourni (notes, courriers, attestations, formulaires, ordonnances, documents, discussions)
2. Crée une synthèse structurée en Markdown avec ces sections:

# Synthèse Globale - {{Patient_Name}}

## 📊 Vue d'Ensemble
[Résumé exécutif: diagnostic principal, âge, contexte familial/scolaire]

## 📝 Historique Clinique
[Chronologie des consultations et évolution]

## 🎯 Diagnostics et Problématiques
[Diagnostics établis, comorbidités, symptomatologie]

## 💊 Traitements et Interventions
[Médications actuelles et passées, psychothérapies, autres prises en charge]

## 🏫 Scolarité et Adaptations
[Parcours scolaire, aménagements, PAI/PAP/MDPH]

## 👨‍👩‍👧 Contexte Familial et Social
[Dynamique familiale, environnement, facteurs de risque/protection]

## 📈 Évolution et Pronostic
[Tendances observées, points d'amélioration, défis persistants]

## 🎯 Objectifs Thérapeutiques
[Priorités actuelles, plan de soins]

## 📋 Points de Vigilance
[Éléments nécessitant surveillance particulière]

STYLE:
- Professionnel mais lisible
- Chronologique quand pertinent
- Concis mais complet
- Intègre les informations clés de TOUS les documents

FORMAT DE RÉPONSE — RÈGLE ABSOLUE:
- Commence DIRECTEMENT par le titre ""# Synthèse Globale - {{Patient_Name}}""
- AUCUNE salutation (""Bonjour Docteur"", ""Cher confrère"", etc.)
- AUCUN préambule (""Voici la synthèse"", ""Voici la synthèse clinique mise à jour..."", ""Voici une analyse..."", etc.)
- AUCUN commentaire de méta-niveau sur ton travail
- AUCUN texte avant le premier ""#""
- Le document est destiné à être lu comme un document clinique, pas comme un message de chat";

            config.Prompts["synthesis_complete"] = new PromptConfig
            {
                Id = "synthesis_complete",
                Name = "Synthèse patient complète",
                Description = "Prompt pour générer une synthèse complète du dossier patient",
                Module = "Synthesis",
                OriginalPrompt = synthesisCompletePrompt,
                DefaultPrompt = synthesisCompletePrompt,
                IsCustomActive = false
            };

            // PROMPT MISE À JOUR INCRÉMENTALE
            var synthesisIncrementalPrompt = @"Tu es un médecin pédopsychiatre expérimenté.

MISSION: Mettre à jour la synthèse clinique existante avec les nouveaux éléments.

SYNTHÈSE ACTUELLE:
{{Existing_Synthesis}}

NOUVEAUX ÉLÉMENTS À INTÉGRER:
{{New_Content}}

INSTRUCTIONS:
1. Analyse les nouveaux éléments
2. Intègre-les dans la synthèse existante de manière cohérente
3. Mets à jour les sections pertinentes (ne réécris pas tout)
4. Ajoute de nouvelles informations significatives
5. Mets à jour la chronologie si nécessaire
6. Conserve la structure existante

IMPORTANT:
- Ne supprime AUCUNE information existante
- Enrichis et complète la synthèse avec les nouveaux éléments
- Maintiens la cohérence narrative
- Garde le format Markdown structuré

FORMAT DE RÉPONSE — RÈGLE ABSOLUE:
- Commence DIRECTEMENT par le titre ""# Synthèse Globale""
- AUCUNE salutation (""Bonjour Docteur"", ""Cher confrère"", etc.)
- AUCUN préambule (""Voici la synthèse mise à jour"", ""Voici la nouvelle version..."", etc.)
- AUCUN commentaire de méta-niveau sur ton travail
- AUCUN texte avant le premier ""#""

Retourne la synthèse COMPLÈTE mise à jour, directement en Markdown.";

            config.Prompts["synthesis_incremental"] = new PromptConfig
            {
                Id = "synthesis_incremental",
                Name = "Mise à jour incrémentale synthèse",
                Description = "Prompt pour mettre à jour une synthèse existante avec de nouveaux éléments",
                Module = "Synthesis",
                OriginalPrompt = synthesisIncrementalPrompt,
                DefaultPrompt = synthesisIncrementalPrompt,
                IsCustomActive = false
            };

            // PROMPT FORMULAIRE MDPH COMPLET
            var mdphCompleteFormPrompt = @"Génère un dossier MDPH complet au format JSON pour le CERFA 15695*01.

CONTEXTE PATIENT :
{{CONTEXTE}}

DEMANDES FORMULÉES PAR LE MÉDECIN :
{{DEMANDES}}

INSTRUCTIONS STRICTES :
1. Retourne un objet JSON valide avec TOUTES les sections ci-dessous
2. Style : télégraphique, factuel, professionnel
3. UNIQUEMENT basé sur le contexte patient fourni - NE RIEN INVENTER
4. Si information manquante : ""Non renseigné"" ou tableau vide []
5. Les remarques complémentaires doivent JUSTIFIER les demandes formulées

FORMAT JSON OBLIGATOIRE :
{
  ""pathologie_principale"": ""Diagnostic principal + code CIM-10 (ex: Trouble du spectre autistique F84.0)"",
  ""autres_pathologies"": ""Liste séparée par virgules avec codes CIM-10, ou 'Aucune'"",
  ""elements_essentiels"": [
    ""Ligne 1: retentissement principal"",
    ""Ligne 2: gravité et facteurs"",
    ""Ligne 3: besoins urgents""
  ],
  ""antecedents_medicaux"": [
    ""Antécédent 1 (ex: Prématurité 32 SA)"",
    ""Antécédent 2""
  ],
  ""retards_developpementaux"": [
    ""Retard 1 (ex: Retard langage oral: premiers mots 24 mois)"",
    ""Retard 2"",
    ""Retard 3""
  ],
  ""description_clinique"": [
    ""Signes groupe 1 (max 20 mots)"",
    ""Signes groupe 2 (max 20 mots)"",
    ""Signes groupe 3 (max 20 mots)""
  ],
  ""traitements"": {
    ""medicaments"": ""Liste avec posologie (ex: Méthylphénidate 18mg/jour, Sertraline 50mg/jour) ou 'Aucun traitement médicamenteux'"",
    ""effets_indesirables"": ""Liste effets avec intensité ou 'Aucun effet indésirable signalé'"",
    ""autres_prises_en_charge"": ""Psychologue, orthophoniste, etc. avec fréquence ou 'Aucune autre prise en charge'""
  },
  ""retentissements"": {
    ""mobilite"": ""1 ligne max 25 mots: marche, déplacement, motricité"",
    ""communication"": ""1 ligne max 25 mots: expression, compréhension, téléphone"",
    ""cognition"": [
      ""Ligne 1: attention, concentration, mémoire"",
      ""Ligne 2: raisonnement, orientation, sécurité"",
      ""Ligne 3: capacités scolaires comparé âge""
    ],
    ""conduite_emotionnelle"": [
      ""Ligne 1: relation autrui, empathie"",
      ""Ligne 2: gestion émotions, colères, anxiété"",
      ""Ligne 3: troubles comportement spécifiques""
    ],
    ""autonomie"": ""1 ligne max 25 mots: toilette, habillage, alimentation, continence"",
    ""vie_quotidienne"": ""1 ligne max 25 mots: repas, courses, budget, démarches"",
    ""social_scolaire"": ""1 ligne max 25 mots: scolarité, aménagements, vie sociale, relations""
  },
  ""remarques_complementaires"": ""Courrier de justification (max 15 lignes) expliquant pourquoi les demandes formulées (AESH, AEEH, etc.) sont nécessaires. Justifier CHAQUE demande avec arguments du contexte. Ton professionnel mais humain.""
}

RÈGLES CRITIQUES :
- Format JSON strict et valide
- Respecter EXACTEMENT les noms de propriétés ci-dessus
- Tableaux vides [] si pas d'information
- Pas de texte avant ou après le JSON
- Les remarques doivent être un texte fluide (pas de tirets), justifiant les demandes";

            config.Prompts["mdph_complete_form"] = new PromptConfig
            {
                Id = "mdph_complete_form",
                Name = "MDPH - Formulaire complet",
                Description = "Génère les 19 sections du formulaire MDPH en une seule fois au format JSON",
                Module = "Formulaire",
                OriginalPrompt = mdphCompleteFormPrompt,
                DefaultPrompt = mdphCompleteFormPrompt,
                IsCustomActive = false
            };

            // PROMPT PAI GENERATION
            var paiGenerationPrompt = @"Génère une réponse pour le formulaire PAI (Projet d'Accueil Individualisé) basée sur l'instruction suivante.

INSTRUCTION UTILISATEUR :
""{{INSTRUCTION}}""

CONTRAINTES DE FORME :
- Style : {{STYLE}}
- Longueur : {{LENGTH}}

INSTRUCTIONS DE RÉDACTION :
- Utilise UNIQUEMENT les informations du contexte patient fourni ci-dessous.
- NE RIEN INVENTER. Si l'information n'est pas dans le contexte, dis-le clairement.
- Adapte le ton pour un document officiel scolaire/médical.
- Sois précis et factuel.

CONTEXTE PATIENT :
{{CONTEXTE}}";

            config.Prompts["pai_generation"] = new PromptConfig
            {
                Id = "pai_generation",
                Name = "Génération PAI",
                Description = "Prompt pour générer le contenu du formulaire PAI",
                Module = "Formulaire",
                OriginalPrompt = paiGenerationPrompt,
                DefaultPrompt = paiGenerationPrompt,
                IsCustomActive = false
            };
            
            // PROMPT PAI GENERATION V2 (Avec contraintes de longueur strictes)
            var paiGenerationPromptV2 = @"Génère une réponse pour le formulaire PAI (Projet d'Accueil Individualisé) basée sur l'instruction suivante.

INSTRUCTION UTILISATEUR :
""{{INSTRUCTION}}""

CONTRAINTES DE FORME :
- Style : {{STYLE}}
- Longueur : {{LENGTH}}

INSTRUCTIONS DE RÉDACTION :
- Utilise UNIQUEMENT les informations du contexte patient fourni ci-dessous.
- NE RIEN INVENTER. Si l'information n'est pas dans le contexte, dis-le clairement.
- Adapte le ton pour un document officiel scolaire/médical.
- Sois précis et factuel.

⚠️ RÈGLE DE LONGUEUR STRICTE :
Si la Longueur est ""1 ligne"", ""2 lignes"" ou ""3 lignes"", tu dois impérativement respecter cette limite.
- 1 ligne = 1 phrase concise.
- 2 lignes = 2 phrases maximum.

CONTEXTE PATIENT :
{{CONTEXTE}}";

            config.Prompts["pai_generation_v2"] = new PromptConfig
            {
                Id = "pai_generation_v2",
                Name = "Génération PAI (V2 Strict)",
                Description = "Prompt pour générer le contenu du formulaire PAI avec contrôle strict de la longueur",
                Module = "Formulaire",
                OriginalPrompt = paiGenerationPromptV2,
                DefaultPrompt = paiGenerationPromptV2,
                IsCustomActive = false
            };

            return config;
        }
        
        /// <summary>
        /// Retourne tous les prompts configurés
        /// </summary>
        public Dictionary<string, PromptConfig> GetAllPrompts()
        {
            return _config.Prompts;
        }
        
        /// <summary>
        /// Retourne un prompt spécifique par son ID
        /// </summary>
        public PromptConfig? GetPrompt(string promptId)
        {
            return _config.Prompts.TryGetValue(promptId, out var prompt) ? prompt : null;
        }
        
        /// <summary>
        /// Retourne le prompt actif (custom ou default) pour un ID donné
        /// </summary>
        public string GetActivePrompt(string promptId)
        {
            var prompt = GetPrompt(promptId);
            return prompt?.ActivePrompt ?? string.Empty;
        }
        
        /// <summary>
        /// Retourne le prompt actif avec anonymisation automatique si nécessaire.
        /// Délègue la décision d'anonymisation à AnonymizationService (qui vérifie si LLM local ou cloud).
        /// Architecture centralisée : Functionality → PromptConfigService → AnonymizationService → LLM
        /// </summary>
        /// <param name="promptId">ID du prompt à récupérer</param>
        /// <param name="patientData">Métadonnées patient pour l'anonymisation (optionnel)</param>
        /// <param name="replacements">Dictionnaire de remplacements pour les placeholders du template (ex: {{CONTEXTE}}, {{DEMANDES}})</param>
        /// <param name="skipAnonymization">Si true, désactive l'anonymisation même pour les LLM cloud (ex: MDPH où le LLM invente des pseudonymes)</param>
        /// <returns>Tuple (prompt final, contexte d'anonymisation)</returns>
        public async System.Threading.Tasks.Task<(string prompt, AnonymizationContext? context)> GetAnonymizedPromptAsync(
            string promptId,
            PatientMetadata? patientData = null,
            Dictionary<string, string>? replacements = null,
            bool skipAnonymization = false)
        {
            // 1. Récupérer le prompt actif (template)
            var prompt = GetActivePrompt(promptId);

            // 2. Remplacer les placeholders du template si fournis (ex: {{CONTEXTE}}, {{DEMANDES}})
            if (replacements != null)
            {
                foreach (var kvp in replacements)
                {
                    // Remplacer {{KEY}} par VALUE
                    prompt = prompt.Replace($"{{{{{kvp.Key}}}}}", kvp.Value);
                }
            }

            // 3. Si anonymisation désactivée explicitement ou pas de service/données → retour direct
            if (skipAnonymization || _anonymizationService == null || patientData == null)
            {
                if (skipAnonymization)
                {
                    System.Diagnostics.Debug.WriteLine($"[PromptConfigService] Anonymisation désactivée pour '{promptId}' (skipAnonymization=true)");
                }
                return (prompt, null);
            }

            // 📊 LOG: Prompt AVANT anonymisation
            System.Diagnostics.Debug.WriteLine($"[PromptConfigService] ========== PROMPT AVANT ANONYMISATION ==========");
            System.Diagnostics.Debug.WriteLine($"[PromptConfigService] Longueur: {prompt.Length} caractères");
            System.Diagnostics.Debug.WriteLine($"[PromptConfigService] Extrait (premiers 800 chars):");
            System.Diagnostics.Debug.WriteLine(prompt.Substring(0, Math.Min(800, prompt.Length)));
            System.Diagnostics.Debug.WriteLine($"[PromptConfigService] ================================================");

            // 4. Déléguer l'anonymisation à AnonymizationService
            // C'est lui qui décide (ShouldAnonymize) et qui anonymise si nécessaire
            var (anonymizedPrompt, context) = await _anonymizationService.AnonymizeAsync(
                prompt,
                patientData
            );

            // 📊 LOG: Prompt APRÈS anonymisation
            System.Diagnostics.Debug.WriteLine($"[PromptConfigService] ========== PROMPT APRÈS ANONYMISATION ==========");
            System.Diagnostics.Debug.WriteLine($"[PromptConfigService] Anonymisé: {context?.WasAnonymized ?? false}");
            if (context?.WasAnonymized == true)
            {
                System.Diagnostics.Debug.WriteLine($"[PromptConfigService] Nombre de remplacements: {context.Replacements.Count}");
                foreach (var kvp in context.Replacements)
                {
                    System.Diagnostics.Debug.WriteLine($"[PromptConfigService]   '{kvp.Key}' → '{kvp.Value}'");
                }
                System.Diagnostics.Debug.WriteLine($"[PromptConfigService] Extrait (premiers 800 chars):");
                System.Diagnostics.Debug.WriteLine(anonymizedPrompt.Substring(0, Math.Min(800, anonymizedPrompt.Length)));
            }
            System.Diagnostics.Debug.WriteLine($"[PromptConfigService] ================================================");

            return (anonymizedPrompt, context);
        }
        
        /// <summary>
        /// Met à jour le prompt personnalisé
        /// </summary>
        public (bool success, string message) UpdateCustomPrompt(string promptId, string customPrompt)
        {
            if (!_config.Prompts.ContainsKey(promptId))
                return (false, "Prompt introuvable");
            
            _config.Prompts[promptId].CustomPrompt = customPrompt;
            var result = SaveConfig();
            
            // Déclencher l'événement de rechargement
            if (result.success)
            {
                PromptsReloaded?.Invoke(this, EventArgs.Empty);
            }
            
            return result;
        }
        
        /// <summary>
        /// Active ou désactive le prompt personnalisé
        /// </summary>
        public (bool success, string message) SetCustomPromptActive(string promptId, bool isActive)
        {
            if (!_config.Prompts.ContainsKey(promptId))
                return (false, "Prompt introuvable");
            
            _config.Prompts[promptId].IsCustomActive = isActive;
            var result = SaveConfig();
            
            // Déclencher l'événement de rechargement
            if (result.success)
            {
                PromptsReloaded?.Invoke(this, EventArgs.Empty);
            }
            
            return result;
        }
        
        /// <summary>
        /// Restaure le prompt par défaut (supprime la version personnalisée)
        /// </summary>
        public (bool success, string message) RestoreDefault(string promptId)
        {
            if (!_config.Prompts.ContainsKey(promptId))
                return (false, "Prompt introuvable");
            
            _config.Prompts[promptId].CustomPrompt = null;
            _config.Prompts[promptId].IsCustomActive = false;
            var result = SaveConfig();
            
            // Déclencher l'événement de rechargement
            if (result.success)
            {
                PromptsReloaded?.Invoke(this, EventArgs.Empty);
            }
            
            return result;
        }
        
        /// <summary>
        /// Sauvegarde la configuration
        /// </summary>
        private (bool success, string message) SaveConfig()
        {
            try
            {
                var options = new JsonSerializerOptions 
                { 
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                };
                var json = JsonSerializer.Serialize(_config, options);
                File.WriteAllText(_configFilePath, json);
                return (true, "Configuration sauvegardée");
            }
            catch (Exception ex)
            {
                return (false, $"Erreur sauvegarde: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Recharge la configuration depuis le fichier
        /// </summary>
        public void ReloadConfig()
        {
            _config = LoadOrCreateConfig();
        }
        
        /// <summary>
        /// Promeut le prompt personnalisé comme nouveau prompt par défaut
        /// (amélioration continue validée)
        /// </summary>
        public (bool success, string message) PromoteCustomToDefault(string promptId)
        {
            if (!_config.Prompts.ContainsKey(promptId))
                return (false, "Prompt introuvable");
            
            var prompt = _config.Prompts[promptId];
            
            if (string.IsNullOrEmpty(prompt.CustomPrompt))
                return (false, "Aucun prompt personnalisé à promouvoir");
            
            // Le prompt personnalisé devient le nouveau défaut
            prompt.DefaultPrompt = prompt.CustomPrompt;
            
            // Réinitialiser le custom
            prompt.CustomPrompt = null;
            prompt.IsCustomActive = false;
            
            var result = SaveConfig();
            
            // Déclencher l'événement de rechargement
            if (result.success)
            {
                PromptsReloaded?.Invoke(this, EventArgs.Empty);
            }
            
            return result;
        }
        
        /// <summary>
        /// Restaure le prompt original d'usine (reset complet)
        /// </summary>
        public (bool success, string message) RestoreToOriginal(string promptId)
        {
            if (!_config.Prompts.ContainsKey(promptId))
                return (false, "Prompt introuvable");
            
            var prompt = _config.Prompts[promptId];
            
            if (string.IsNullOrEmpty(prompt.OriginalPrompt))
                return (false, "Prompt original introuvable");
            
            // Restaurer l'original comme défaut
            prompt.DefaultPrompt = prompt.OriginalPrompt;
            
            // Supprimer le custom
            prompt.CustomPrompt = null;
            prompt.IsCustomActive = false;
            
            var result = SaveConfig();
            
            // Déclencher l'événement de rechargement
            if (result.success)
            {
                PromptsReloaded?.Invoke(this, EventArgs.Empty);
            }
            
            return result;
        }
    }
}
