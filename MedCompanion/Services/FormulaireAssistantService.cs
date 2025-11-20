using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    public class FormulaireAssistantService
    {
        private readonly OpenAIService _openAIService;
        private readonly string _patientsBasePath;

        public FormulaireAssistantService(OpenAIService openAIService)
        {
            _openAIService = openAIService;
            _patientsBasePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "MedCompanion",
                "patients"
            );
        }

        public async Task<string> GeneratePathologieSection(PatientMetadata patient)
        {
            var context = await LoadPatientContext(patient);

            var prompt = @"Génère UNIQUEMENT le diagnostic principal pour la section ""PATHOLOGIE MOTIVANT LA DEMANDE"" du CERFA 15695*01.

INSTRUCTIONS STRICTES:
- Une seule ligne
- Format: Diagnostic principal + code CIM-10
- SANS date de diagnostic
- Style télégraphique

EXEMPLE:
Trouble du spectre autistique (F84.0)";

            var (success, result) = await _openAIService.ChatAvecContexteAsync(context, prompt);
            return success ? result : $"Erreur: {result}";
        }

        public async Task<string> GenerateAutresPathologiesSection(PatientMetadata patient)
        {
            var context = await LoadPatientContext(patient);

            var prompt = @"Génère la liste des autres pathologies associées pour le CERFA 15695*01.

INSTRUCTIONS STRICTES:
- Une seule ligne
- Si plusieurs pathologies : les séparer par des virgules
- Si aucune pathologie associée : écrire ""Aucune""
- Format: Pathologie (code CIM-10)

EXEMPLE:
Anxiété sociale (F40.1) modérée, troubles de l'humeur associés";

            var (success, result) = await _openAIService.ChatAvecContexteAsync(context, prompt);
            return success ? result : $"Erreur: {result}";
        }

        public async Task<string> GenerateElementsEssentielsSection(PatientMetadata patient)
        {
            var context = await LoadPatientContext(patient);

            var prompt = @"Génère les éléments essentiels à retenir (diagnostic, facteurs de gravité) pour le CERFA 15695*01.

INSTRUCTIONS STRICTES:
- EXACTEMENT 3 lignes avec tirets « - » (pas plus, pas moins)
- Style télégraphique, factuel
- Focus sur: retentissement, gravité, besoins urgents

EXEMPLE:
- Retentissement majeur sur la communication sociale et les interactions
- Comportements répétitifs et intérêts restreints sévères, rituels quotidiens
- Retard scolaire significatif, besoin accompagnement AESH temps plein";

            var (success, result) = await _openAIService.ChatAvecContexteAsync(context, prompt);
            return success ? result : $"Erreur: {result}";
        }

        public async Task<string> GenerateAntecedentsMedicauxSection(PatientMetadata patient)
        {
            var context = await LoadPatientContext(patient);

            var prompt = @"Génère la liste des antécédents médicaux et périnataux pour le CERFA 15695*01.

INSTRUCTIONS STRICTES:
- Format multilignes (selon les antécédents du patient)
- Chaque antécédent sur une ligne avec « - »
- Style télégraphique
- Inclure: prématurité, complications périnatales, hospitalisations, chirurgies, maladies chroniques

EXEMPLES:
- Prématurité 32 SA, hospitalisation néonatale 3 semaines
- Césarienne en urgence, détresse respiratoire à la naissance
- Appendicectomie 8 ans
- Épilepsie diagnostiquée 6 ans, crises contrôlées sous traitement
- Asthme modéré intermittent

Si aucun antécédent significatif : écrire ""Aucun antécédent médical significatif""";

            var (success, result) = await _openAIService.ChatAvecContexteAsync(context, prompt);
            return success ? result : $"Erreur: {result}";
        }

        public async Task<string> GenerateRetardsDeveloppementauxSection(PatientMetadata patient)
        {
            var context = await LoadPatientContext(patient);

            var prompt = @"Génère la liste des retards développementaux pour le CERFA 15695*01.

INSTRUCTIONS STRICTES - LIMITE IMPÉRATIVE:
- MAXIMUM 3 LIGNES (PAS PLUS DE 3, JAMAIS 4 OU PLUS)
- Chaque retard sur une ligne avec « - »
- Si plus de 3 retards : regrouper sur 3 lignes maximum
- Style télégraphique
- Priorité: retard psychomoteur, langage, propreté, autonomie

EXEMPLES CONFORMES (3 lignes max):
- Retard langage oral: premiers mots 24 mois, phrases simples 4 ans
- Retard psychomoteur: marche acquise 20 mois, coordination difficile
- Autonomie et propreté: dépendance activités quotidiennes, énurésie nocturne persistante

Si aucun retard : écrire ""Aucun retard développemental significatif""

RAPPEL: Ne JAMAIS générer plus de 3 lignes avec tirets.";

            var (success, result) = await _openAIService.ChatAvecContexteAsync(context, prompt);
            return success ? result : $"Erreur: {result}";
        }

        public async Task<string> GenerateDescriptionClinique1Section(PatientMetadata patient)
        {
            var context = await LoadPatientContext(patient);

            var prompt = @"Génère la première ligne de signes cliniques invalidants pour le CERFA 15695*01.

INSTRUCTIONS STRICTES:
- UNE SEULE LIGNE (pas de tiret « - » au début)
- Décrire les signes cliniques invalidants (groupe 1)
- Style télégraphique, factuel
- Maximum 20 mots

EXEMPLE:
Crises d'angoisse quotidiennes, troubles concentration marqués, insomnie chronique sévère";

            var (success, result) = await _openAIService.ChatAvecContexteAsync(context, prompt);
            return success ? result : $"Erreur: {result}";
        }

        public async Task<string> GenerateDescriptionClinique2Section(PatientMetadata patient)
        {
            var context = await LoadPatientContext(patient);

            var prompt = @"Génère la deuxième ligne de signes cliniques invalidants pour le CERFA 15695*01.

INSTRUCTIONS STRICTES:
- UNE SEULE LIGNE (pas de tiret « - » au début)
- Décrire d'autres signes cliniques invalidants (groupe 2, différents de la ligne 1)
- Style télégraphique, factuel
- Maximum 20 mots

EXEMPLE:
Retrait social marqué, difficultés communication verbale, stéréotypies motrices fréquentes";

            var (success, result) = await _openAIService.ChatAvecContexteAsync(context, prompt);
            return success ? result : $"Erreur: {result}";
        }

        public async Task<string> GenerateDescriptionClinique3Section(PatientMetadata patient)
        {
            var context = await LoadPatientContext(patient);

            var prompt = @"Génère la troisième ligne de signes cliniques invalidants pour le CERFA 15695*01.

INSTRUCTIONS STRICTES:
- UNE SEULE LIGNE (pas de tiret « - » au début)
- Décrire d'autres signes cliniques invalidants (groupe 3, différents des lignes 1 et 2)
- Style télégraphique, factuel
- Maximum 20 mots

EXEMPLE:
Troubles alimentaires sélectivité importante, rituels quotidiens rigides, intolérance aux changements";

            var (success, result) = await _openAIService.ChatAvecContexteAsync(context, prompt);
            return success ? result : $"Erreur: {result}";
        }

        public async Task<string> GenerateTraitements1Section(PatientMetadata patient)
        {
            var context = await LoadPatientContext(patient);

            var prompt = @"Génère la liste des médicaments en cours pour le CERFA 15695*01.

INSTRUCTIONS STRICTES - IMPORTANT:
- UNIQUEMENT les médicaments mentionnés dans le contexte patient (notes, synthèse, ordonnances)
- NE RIEN INVENTER - Si aucun médicament mentionné, écrire ""Aucun traitement médicamenteux""
- Séparer par des VIRGULES (pas de tirets « - »)
- Inclure: nom du médicament + posologie si mentionnée
- Maximum 3-4 lignes
- Style télégraphique

EXEMPLE:
Méthylphénidate 18mg/jour, Sertraline 50mg/jour, Rispéridone 0,5mg matin et soir";

            var (success, result) = await _openAIService.ChatAvecContexteAsync(context, prompt);
            return success ? result : $"Erreur: {result}";
        }

        public async Task<string> GenerateTraitements2Section(PatientMetadata patient)
        {
            var context = await LoadPatientContext(patient);

            var prompt = @"Génère la liste des effets indésirables du traitement pour le CERFA 15695*01.

INSTRUCTIONS STRICTES - IMPORTANT:
- UNIQUEMENT les effets indésirables mentionnés dans le contexte patient (notes, synthèse)
- NE RIEN INVENTER - Si aucun effet indésirable mentionné, écrire ""Aucun effet indésirable signalé""
- Séparer par des VIRGULES (pas de tirets « - »)
- Préciser l'intensité si mentionnée (léger, modéré, sévère)
- Maximum 2-3 lignes
- Style télégraphique

EXEMPLE:
Insomnie modérée sous méthylphénidate, Somnolence diurne légère, Prise de poids 3kg sous rispéridone";

            var (success, result) = await _openAIService.ChatAvecContexteAsync(context, prompt);
            return success ? result : $"Erreur: {result}";
        }

        public async Task<string> GenerateTraitements3Section(PatientMetadata patient)
        {
            var context = await LoadPatientContext(patient);

            var prompt = @"Génère la liste des autres prises en charge (non médicamenteuses) pour le CERFA 15695*01.

INSTRUCTIONS STRICTES - IMPORTANT:
- UNIQUEMENT les prises en charge mentionnées dans le contexte patient (notes, synthèse)
- NE RIEN INVENTER - Si aucune prise en charge mentionnée, écrire ""Aucune autre prise en charge""
- Séparer par des VIRGULES (pas de tirets « - »)
- Préciser la fréquence si mentionnée dans le contexte
- Maximum 3-4 lignes
- Style télégraphique

EXEMPLE:
Psychologue hebdomadaire (TCC), Orthophoniste 2 fois par semaine, Suivi CMP mensuel, Psychomotricien hebdomadaire";

            var (success, result) = await _openAIService.ChatAvecContexteAsync(context, prompt);
            return success ? result : $"Erreur: {result}";
        }

        public async Task<string> GenerateRetentissementMobiliteSection(PatientMetadata patient)
        {
            var context = await LoadPatientContext(patient);

            var prompt = @"Génère le contenu pour la section ""RETENTISSEMENT FONCTIONNEL - MOBILITÉ"" du CERFA 15695*01.

INSTRUCTIONS STRICTES:
- UNE SEULE LIGNE (pas de tiret « - » au début)
- Décrire: capacités de marche, déplacement, motricité, aides nécessaires
- UNIQUEMENT ce qui est mentionné dans le contexte patient (synthèse, notes)
- NE RIEN INVENTER
- Style télégraphique, factuel
- Maximum 25 mots

EXEMPLE:
Marche autonome courte distance, fatigue rapide, préhension correcte, motricité fine altérée, accompagnement nécessaire déplacements extérieurs";

            var (success, result) = await _openAIService.ChatAvecContexteAsync(context, prompt);
            return success ? result : $"Erreur: {result}";
        }

        public async Task<string> GenerateRetentissementCommunicationSection(PatientMetadata patient)
        {
            var context = await LoadPatientContext(patient);

            var prompt = @"Génère le contenu pour la section ""RETENTISSEMENT FONCTIONNEL - COMMUNICATION"" du CERFA 15695*01.

INSTRUCTIONS STRICTES:
- UNE SEULE LIGNE (pas de tiret « - » au début)
- Décrire: expression orale, compréhension, utilisation téléphone, adaptations nécessaires
- UNIQUEMENT ce qui est mentionné dans le contexte patient (synthèse, notes)
- NE RIEN INVENTER
- Style télégraphique, factuel
- Maximum 25 mots

EXEMPLE:
Expression orale limitée, vocabulaire restreint, difficultés compréhension consignes, téléphone impossible, communication via pictogrammes, besoin reformulation";

            var (success, result) = await _openAIService.ChatAvecContexteAsync(context, prompt);
            return success ? result : $"Erreur: {result}";
        }

        public async Task<string> GenerateRetentissementCognitionSection(PatientMetadata patient)
        {
            var context = await LoadPatientContext(patient);

            var prompt = @"Génère le contenu pour la section ""RETENTISSEMENT FONCTIONNEL - COGNITION"" du CERFA 15695*01.

INSTRUCTIONS STRICTES - FORMAT ULTRA-COURT:
- Maximum 3 lignes
- Chaque ligne commence par « - »
- Maximum 20 mots par ligne
- Style télégraphique, préciser atteintes

CONTENU À GÉNÉRER:
Ligne 1: Attention, concentration, mémoire (court/long terme)
Ligne 2: Raisonnement, orientation, sécurité personnelle, comportement
Ligne 3: Capacités scolaires (lecture, écriture, calcul) comparé âge

EXEMPLE:
- Attention dispersée 5min max, mémoire travail déficitaire, oublis fréquents consignes
- Difficultés résolution problèmes simples, impulsivité majeure, gestion sécurité limitée supervision
- Lecture niveau CE1 âge 12 ans, écriture phonétique, calcul mental impossible";

            var (success, result) = await _openAIService.ChatAvecContexteAsync(context, prompt);
            return success ? result : $"Erreur: {result}";
        }

        public async Task<string> GenerateConduiteEmotionnelleSection(PatientMetadata patient)
        {
            var context = await LoadPatientContext(patient);

            var prompt = @"Génère le contenu pour la section ""CONDUITE ÉMOTIONNELLE ET COMPORTEMENTALE"" du CERFA 15695*01.

INSTRUCTIONS STRICTES - FORMAT ULTRA-COURT:
- Maximum 3 lignes
- Chaque ligne commence par « - »
- Maximum 20 mots par ligne
- Style télégraphique, préciser les aspects émotionnels et comportementaux
- UNIQUEMENT ce qui est mentionné dans le contexte patient (synthèse, notes)
- NE RIEN INVENTER

CONTENU À GÉNÉRER:
Ligne 1: Relation avec autrui (interactions sociales, empathie, adaptabilité)
Ligne 2: Gestion émotions et comportements (colères, anxiété, auto/hétéro-agressivité)
Ligne 3: Troubles du comportement spécifiques (impulsivité, opposition, rituels)

EXEMPLE:
- Difficultés contact visuel, empathie limitée, incompréhension codes sociaux, jeu solitaire préféré
- Crises colère quotidiennes frustration, anxiété anticipation changements, auto-agressivité (morsures mains)
- Impulsivité majeure, opposition passive consignes, rituels alimentaires rigides obligatoires";

            var (success, result) = await _openAIService.ChatAvecContexteAsync(context, prompt);
            return success ? result : $"Erreur: {result}";
        }

        public async Task<string> GenerateRetentissementAutonomieSection(PatientMetadata patient)
        {
            var context = await LoadPatientContext(patient);

            var prompt = @"Génère le contenu pour la section ""RETENTISSEMENT FONCTIONNEL - ENTRETIEN PERSONNEL"" du CERFA 15695*01.

INSTRUCTIONS STRICTES:
- UNE SEULE LIGNE (pas de tiret « - » au début)
- Décrire: toilette, habillage, alimentation, continence
- UNIQUEMENT ce qui est mentionné dans le contexte patient (synthèse, notes)
- NE RIEN INVENTER
- Style télégraphique, factuel
- Maximum 25 mots

EXEMPLE:
Toilette supervision constante, habillage aide partielle, alimentation autonome couverts adaptés, énurésie nocturne quotidienne";

            var (success, result) = await _openAIService.ChatAvecContexteAsync(context, prompt);
            return success ? result : $"Erreur: {result}";
        }

        public async Task<string> GenerateRetentissementVieQuotidienneSection(PatientMetadata patient)
        {
            var context = await LoadPatientContext(patient);

            var prompt = @"Génère le contenu pour la section ""RETENTISSEMENT FONCTIONNEL - VIE QUOTIDIENNE ET DOMESTIQUE"" du CERFA 15695*01.

INSTRUCTIONS STRICTES:
- UNE SEULE LIGNE (pas de tiret « - » au début)
- Décrire: repas, courses, tâches ménagères, gestion budget, démarches administratives, traitement médical
- UNIQUEMENT ce qui est mentionné dans le contexte patient (synthèse, notes)
- NE RIEN INVENTER
- Style télégraphique, factuel
- Maximum 25 mots

EXEMPLE:
Repas simples supervision, courses impossible seul, budget impossible, démarches prises charge parents, traitement rappels quotidiens obligatoires";

            var (success, result) = await _openAIService.ChatAvecContexteAsync(context, prompt);
            return success ? result : $"Erreur: {result}";
        }

        public async Task<string> GenerateRetentissementSocialScolaireSection(PatientMetadata patient)
        {
            var context = await LoadPatientContext(patient);

            var prompt = @"Génère le contenu pour la section ""RETENTISSEMENT SUR VIE SOCIALE, SCOLAIRE ET EMPLOI"" du CERFA 15695*01.

INSTRUCTIONS STRICTES:
- UNE SEULE LIGNE (pas de tiret « - » au début)
- Décrire: scolarité/emploi, aménagements, vie sociale, relations, vie familiale
- UNIQUEMENT ce qui est mentionné dans le contexte patient (synthèse, notes)
- NE RIEN INVENTER
- Style télégraphique, factuel
- Maximum 25 mots

EXEMPLE:
Scolarité temps partiel ULIS, AESH 24h/semaine, isolement social majeur, pas d'amis, relations familiales tendues épuisement parental";

            var (success, result) = await _openAIService.ChatAvecContexteAsync(context, prompt);
            return success ? result : $"Erreur: {result}";
        }

        /// <summary>
        /// Génère les remarques complémentaires (version courte, pas encore générée)
        /// </summary>
        public async Task<string> GenerateRemarquesComplementairesSection(PatientMetadata patient)
        {
            // Version par défaut : message indiquant qu'il faut cliquer sur "Générer"
            return "⏳ Cliquez sur '📝 Générer les remarques' pour créer cette section après avoir coché vos demandes.";
        }

        /// <summary>
        /// Génère les remarques complémentaires avec justification des demandes cochées
        /// </summary>
        /// <param name="patient">Métadonnées du patient</param>
        /// <param name="demandes">Liste des demandes cochées (AESH, AEEH, PCH, etc.)</param>
        /// <returns>Courrier de justification des demandes</returns>
        public async Task<string> GenerateRemarquesComplementairesSection(PatientMetadata patient, string demandes)
        {
            var context = await LoadPatientContext(patient);

            // Si aucune demande cochée, message par défaut
            if (string.IsNullOrWhiteSpace(demandes))
            {
                demandes = "Aucune demande spécifique cochée";
            }

            var prompt = $@"Rédige un courrier de justification pour les REMARQUES COMPLÉMENTAIRES du CERFA 15695*01.

========== DEMANDES FORMULÉES ==========
{demandes}
========== FIN DEMANDES ==========

INSTRUCTIONS STRICTES - STYLE COURRIER:
- MAXIMUM 15 LIGNES de texte rédigé (PAS de tirets « - »)
- Style courrier fluide, naturel, persuasif mais factuel
- COMMENCER par le prénom de l'enfant (ex: ""Lucas présente..."", ""Pour Léa, il est indispensable..."")
- JUSTIFIER SPÉCIFIQUEMENT chaque demande formulée ci-dessus
- Reprendre les éléments du contexte patient (diagnostic, retentissements, traitements) pour argumenter
- INSISTER sur les besoins de l'enfant et l'urgence de la situation
- Évoquer l'impact sur la famille si pertinent
- Ton professionnel mais humain, empathique
- UNIQUEMENT ce qui est mentionné dans le contexte patient - NE RIEN INVENTER

STRUCTURE ATTENDUE:
- Paragraphe 1-2: Présentation de l'enfant et de son handicap (prénom, âge, diagnostic)
- Paragraphe 3-8: Justification de CHAQUE demande formulée avec arguments concrets tirés du contexte
- Paragraphe 9-12: Impact sur la vie quotidienne, familiale, scolaire
- Paragraphe 13-15: Conclusion insistant sur l'urgence et la nécessité des aides demandées

EXEMPLE (si demandes = AESH + AEEH):
Lucas, 8 ans, présente un trouble du spectre autistique sévère diagnostiqué à l'âge de 4 ans, accompagné d'une déficience intellectuelle modérée. Malgré les prises en charge régulières (orthophonie, psychomotricité, suivi CMP), son handicap impacte significativement tous les aspects de sa vie quotidienne et nécessite un accompagnement constant.

L'AESH à temps plein demandée est absolument indispensable pour assurer le maintien de sa scolarisation. Lucas présente des crises d'angoisse quotidiennes en milieu scolaire, nécessitant une gestion immédiate pour éviter les comportements auto-agressifs. Sans accompagnement permanent, sa sécurité ne peut être garantie. L'AESH est également essentielle pour adapter les consignes, le rassurer face aux changements et maintenir son attention lors des apprentissages. Sans cet accompagnement, la poursuite de sa scolarité serait gravement compromise.

L'allocation AEEH est pleinement justifiée par les surcoûts importants liés au handicap. La famille assume des frais médicaux élevés : consultations spécialisées hebdomadaires non remboursées, matériel pédagogique adapté, pictogrammes, et transports fréquents vers les structures de soins. Les parents ont dû réduire leur temps de travail pour assurer la surveillance constante nécessaire. L'épuisement parental est critique, avec des troubles du sommeil dus aux réveils nocturnes fréquents de Lucas.

Au regard de la sévérité du handicap et de son retentissement majeur sur tous les domaines de vie, les aides demandées apparaissent indispensables pour permettre à Lucas de poursuivre son développement dans les meilleures conditions possibles.";

            var (success, result) = await _openAIService.ChatAvecContexteAsync(context, prompt);
            return success ? result : $"Erreur: {result}";
        }

        private async Task<string> LoadPatientContext(PatientMetadata patient)
        {
            var patientFolder = Path.Combine(_patientsBasePath, $"{patient.Nom}_{patient.Prenom}");
            var contextParts = new List<string>();

            // Informations de base
            contextParts.Add($"Patient: {patient.Prenom} {patient.Nom}");

            if (!string.IsNullOrEmpty(patient.Dob) && DateTime.TryParse(patient.Dob, out var dob))
            {
                contextParts.Add($"Date de naissance: {dob:dd/MM/yyyy}");
            }

            // Charger la SYNTHÈSE PATIENT (IMPORTANT - contient toutes les infos consolidées)
            var synthesisPath = Path.Combine(patientFolder, "synthese", "synthese.md");
            if (File.Exists(synthesisPath))
            {
                try
                {
                    var synthesisContent = await File.ReadAllTextAsync(synthesisPath, Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(synthesisContent))
                    {
                        contextParts.Add("");
                        contextParts.Add("========== SYNTHÈSE PATIENT (RÉFÉRENCE PRINCIPALE) ==========");
                        contextParts.Add(synthesisContent);
                        contextParts.Add("========== FIN SYNTHÈSE PATIENT ==========");
                        contextParts.Add("");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erreur lecture synthèse: {ex.Message}");
                }
            }
            
            if (patient.Age.HasValue)
            {
                contextParts.Add($"Âge: {patient.Age} ans");
            }
            
            if (!string.IsNullOrEmpty(patient.Sexe))
            {
                contextParts.Add($"Sexe: {patient.Sexe}");
            }
            
            contextParts.Add("");

            // Charger les notes si disponibles
            var notesPath = Path.Combine(patientFolder, "notes.json");
            if (File.Exists(notesPath))
            {
                try
                {
                    var notesJson = await File.ReadAllTextAsync(notesPath);
                    var notes = JsonSerializer.Deserialize<List<Dictionary<string, object>>>(notesJson);
                    
                    if (notes != null && notes.Any())
                    {
                        contextParts.Add("NOTES CLINIQUES:");
                        var recentNotes = notes.OrderByDescending(n => n.ContainsKey("date") ? n["date"] : "").Take(5);
                        foreach (var note in recentNotes)
                        {
                            if (note.ContainsKey("structured") && note["structured"] != null)
                            {
                                contextParts.Add(note["structured"].ToString());
                                contextParts.Add("");
                            }
                            else if (note.ContainsKey("raw") && note["raw"] != null)
                            {
                                contextParts.Add(note["raw"].ToString());
                                contextParts.Add("");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erreur lecture notes: {ex.Message}");
                }
            }

            // Charger les échanges IA si disponibles
            var exchangesPath = Path.Combine(patientFolder, "chat-exchanges.json");
            if (File.Exists(exchangesPath))
            {
                try
                {
                    var exchangesJson = await File.ReadAllTextAsync(exchangesPath);
                    var exchanges = JsonSerializer.Deserialize<List<ChatExchange>>(exchangesJson);
                    
                    if (exchanges != null && exchanges.Any())
                    {
                        contextParts.Add("ÉCHANGES IA RÉCENTS:");
                        var recentExchanges = exchanges.OrderByDescending(e => e.Timestamp).Take(3);
                        foreach (var exchange in recentExchanges)
                        {
                            contextParts.Add($"Question: {exchange.Question}");
                            contextParts.Add($"Réponse: {exchange.Response}");
                            contextParts.Add("");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Erreur lecture échanges: {ex.Message}");
                }
            }

            return string.Join("\n", contextParts);
        }

        public async Task<Dictionary<string, string>> GenerateAllSections(PatientMetadata patient)
        {
            var sections = new Dictionary<string, string>();

            try
            {
                sections["pathologie"] = await GeneratePathologieSection(patient);
                sections["autresPathologies"] = await GenerateAutresPathologiesSection(patient);
                sections["elementsEssentiels"] = await GenerateElementsEssentielsSection(patient);
                sections["antecedentsMedicaux"] = await GenerateAntecedentsMedicauxSection(patient);
                sections["retardsDeveloppementaux"] = await GenerateRetardsDeveloppementauxSection(patient);
                sections["clinique1"] = await GenerateDescriptionClinique1Section(patient);
                sections["clinique2"] = await GenerateDescriptionClinique2Section(patient);
                sections["clinique3"] = await GenerateDescriptionClinique3Section(patient);
                sections["traitements1"] = await GenerateTraitements1Section(patient);
                sections["traitements2"] = await GenerateTraitements2Section(patient);
                sections["traitements3"] = await GenerateTraitements3Section(patient);
                sections["mobilite"] = await GenerateRetentissementMobiliteSection(patient);
                sections["communication"] = await GenerateRetentissementCommunicationSection(patient);
                sections["cognition"] = await GenerateRetentissementCognitionSection(patient);
                sections["conduiteEmotionnelle"] = await GenerateConduiteEmotionnelleSection(patient);
                sections["autonomie"] = await GenerateRetentissementAutonomieSection(patient);
                sections["vieQuotidienne"] = await GenerateRetentissementVieQuotidienneSection(patient);
                sections["socialScolaire"] = await GenerateRetentissementSocialScolaireSection(patient);
                sections["remarques"] = await GenerateRemarquesComplementairesSection(patient);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Erreur génération sections: {ex.Message}");
            }

            return sections;
        }
    }
}
