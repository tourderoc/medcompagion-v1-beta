using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using MedCompanion.Models.Evaluations;
using MedCompanion.Models.Restitutions;
using MedCompanion.Services.LLM;
using MedCompanion.Services.Synthesis;
using MedCompanion.Services.Therapeutique;

namespace MedCompanion.Services.Restitutions
{
    /// <summary>
    /// Préremplit chaque bloc d'un dossier de restitution à partir du dossier patient
    /// complet (DossierReading). Med reçoit le même contexte intégral pour chaque bloc :
    /// la cohérence inter-blocs est garantie.
    ///
    /// La voix cible (clinique / livre / mixte) est définie au niveau du bloc et pilote
    /// le ton du LLM. Le pointage prioritaire des sources par type de bloc est fourni
    /// en consigne, mais Med reste libre de piocher partout dans le dossier.
    /// </summary>
    public class RestitutionSuggesterService
    {
        private readonly ILLMService _llmService;
        private readonly DossierReaderService _dossierReader;

        // Conservés pour compat ascendante (méthode legacy PrefillBlocAsync(string, RestitutionBloc)).
        private readonly SyntheseGlobaleService? _syntheseService;
        private readonly ProjetTherapeutiqueService? _projetService;
        private readonly PatientContextService? _contextService;

        public RestitutionSuggesterService(
            ILLMService llmService,
            DossierReaderService dossierReader,
            SyntheseGlobaleService? syntheseService = null,
            ProjetTherapeutiqueService? projetService = null,
            PatientContextService? contextService = null)
        {
            _llmService     = llmService;
            _dossierReader  = dossierReader;
            _syntheseService = syntheseService;
            _projetService   = projetService;
            _contextService  = contextService;
        }

        // ── API principale (utilise DossierReading) ─────────────────────────

        /// <summary>
        /// Préremplit un bloc à partir d'un DossierReading déjà lu. C'est la signature
        /// privilégiée — appeler ReadAsync une fois en amont puis cette méthode 8 fois
        /// en parallèle.
        /// </summary>
        public async Task<(string Suggestion, string SourceContext)> PrefillBlocAsync(
            RestitutionBloc bloc,
            DossierReading reading,
            CancellationToken cancellationToken = default)
        {
            var context = reading.RenderForLlm();
            if (string.IsNullOrWhiteSpace(context))
                return ("(Aucun contenu source disponible pour générer cette section.)", "Aucune source.");

            var systemPrompt = BuildSystemPrompt(bloc);
            var userPrompt   = BuildUserPrompt(bloc, context);

            var messages = new List<(string role, string content)>
            {
                ("user", userPrompt)
            };

            var result = await _llmService.ChatAsync(systemPrompt, messages, 1500, cancellationToken);

            return result.success
                ? (result.result, context)
                : ($"(Erreur lors de la génération : {result.error})", context);
        }

        // ── Génération progressive page 2 (6 sections successives) ─────────

        /// <summary>
        /// Génère la Restitution 1-page parents en 6 appels LLM consécutifs, un par section.
        /// Après chaque section, <paramref name="onSectionReady"/> reçoit le texte Markdown
        /// accumulé jusqu'ici — le ViewModel met à jour l'UI au fil de l'eau.
        /// </summary>
        public async Task SuggestRestitution1PageProgressiveAsync(
            DossierReading reading,
            Action<string> onSectionReady,
            CancellationToken ct = default)
        {
            var context = reading.RenderForLlm();
            if (string.IsNullOrWhiteSpace(context))
            {
                onSectionReady("(Aucun contenu source disponible.)");
                return;
            }

            // Bloc factice pour construire le system prompt (voix livre = parents)
            var blocp2 = new RestitutionBloc("restitution_1page", "Restitution 1-page parents", 2, "livre");
            var systemPrompt = BuildSystemPrompt(blocp2);

            var subsections = GetRestitution1PageSubsections();

            var accumulated = new System.Text.StringBuilder();

            foreach (var (title, instruction) in subsections)
            {
                if (ct.IsCancellationRequested) break;

                var userPrompt = BuildSubsectionPrompt(context, instruction, blocp2.VoixCible);
                var messages   = new List<(string role, string content)> { ("user", userPrompt) };
                var result     = await _llmService.ChatAsync(systemPrompt, messages, 800, ct);

                if (ct.IsCancellationRequested) break;

                if (accumulated.Length > 0) accumulated.AppendLine();
                accumulated.AppendLine(title);
                accumulated.AppendLine();
                accumulated.AppendLine(result.success ? result.result.Trim() : $"(Erreur : {result.error})");

                onSectionReady(accumulated.ToString());
            }
        }

        private static (string Title, string Instruction)[] GetRestitution1PageSubsections() => new[]
        {
            ("**Ce que nous avons compris**",
             "Rédige UNIQUEMENT le contenu de la section « Ce que nous avons compris » en t'adressant directement aux parents, " +
             "avec un ton chaleureux et rassurant — comme un médecin qui explique avec bienveillance, pas un rapport clinique. " +
             "Structure : 1 paragraphe d'introduction accessible (2-3 phrases commençant par « Nous avons… » ou « Votre enfant… »), " +
             "puis une liste de 3-4 points-clés reformulés sans jargon médical (- **Mot simple :** explication courte). " +
             "Interdiction d'utiliser des expressions comme « tableau clinique », « comorbidité », « nosologique ». " +
             "Commence directement par le paragraphe, sans titre."),

            ("**Ses forces et ses réussites**",
             "Rédige UNIQUEMENT le contenu de la section « Ses forces et ses réussites » : " +
             "liste de 4-5 points positifs CONCRETS observés chez cet enfant, en valorisant ses atouts avec chaleur " +
             "(- **Qualité :** description courte en 1 ligne). " +
             "Parle de l'enfant à la 3e personne (« Il/Elle… »). Pas de jargon. " +
             "Commence directement par la liste, sans titre."),

            ("**Les difficultés actuellement observées**",
             "Rédige UNIQUEMENT le contenu de la section « Les difficultés actuellement observées » : " +
             "liste de 3-4 défis principaux formulés simplement, sans culpabiliser les parents et sans termes cliniques " +
             "(- **Difficulté en mots simples :** impact concret au quotidien en 1 ligne). " +
             "Ex. : '**Fatigue le matin :** Il a du mal à se lever et à aller en classe.' " +
             "Commence directement par la liste, sans titre."),

            ("**Ce qui peut aider**",
             "Rédige UNIQUEMENT le contenu de la section « Ce qui peut aider » : " +
             "liste de 3-4 actions pratiques et concrètes que les parents peuvent faire à la maison " +
             "(- **Action :** description courte et actionnable en 1-2 lignes). " +
             "Ton encourageant, positif. Pas de jargon. " +
             "Commence directement par la liste, sans titre."),

            ("**Notre feuille de route**",
             "Rédige UNIQUEMENT le contenu de la section « Notre feuille de route » : " +
             "liste numérotée de 3-5 prochaines étapes concrètes du suivi " +
             "(1. **Étape :** description courte en 1-2 lignes). " +
             "Ton collaboratif (« Nous allons… », « Ensemble nous… »). Sans jargon. " +
             "Commence directement par la liste numérotée, sans titre."),

            ("**Son environnement : points clés**",
             "Rédige UNIQUEMENT le contenu de la section « Son environnement : points clés » : " +
             "1-2 phrases sur les soutiens positifs de l'entourage (famille, école…), " +
             "puis une liste de 2-3 points d'attention à garder en tête " +
             "(- **Point :** description courte). " +
             "Ton bienveillant, sans alarmisme. " +
             "Commence directement par le texte, sans titre.")
        };

        /// <summary>Génère une seule section de la Restitution 1-page parents (index 0-5).</summary>
        public async Task<string> SuggestRestitution1PageSectionAsync(
            int sectionIndex,
            DossierReading reading,
            CancellationToken ct = default)
        {
            var context = reading.RenderForLlm();
            if (string.IsNullOrWhiteSpace(context)) return "(Aucun contenu source disponible.)";

            var subsections = GetRestitution1PageSubsections();
            if (sectionIndex < 0 || sectionIndex >= subsections.Length)
                return $"(Index {sectionIndex} invalide)";

            var blocp2 = new RestitutionBloc("restitution_1page", "Restitution 1-page parents", 2, "livre");
            var systemPrompt = BuildSystemPrompt(blocp2);
            var (_, instruction) = subsections[sectionIndex];
            var userPrompt = BuildSubsectionPrompt(context, instruction, blocp2.VoixCible);
            var messages   = new List<(string role, string content)> { ("user", userPrompt) };
            var result     = await _llmService.ChatAsync(systemPrompt, messages, 800, ct);
            return result.success ? result.result.Trim() : $"(Erreur : {result.error})";
        }

        private static string BuildSubsectionPrompt(string dossierContext, string instruction, string voixCible)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(dossierContext);
            sb.AppendLine();
            sb.AppendLine("=================");
            sb.AppendLine("INSTRUCTION STRICTE — génère UNIQUEMENT ce qui est demandé ci-dessous, " +
                          "sans introduction, sans commentaire, sans titre supplémentaire :");
            sb.AppendLine(instruction);
            sb.AppendLine();

            if (voixCible == "livre")
            {
                sb.AppendLine("RAPPEL TON OBLIGATOIRE : tu t'adresses à des parents, pas à des médecins. " +
                              "Langage simple, chaleureux, empathique. Pas de jargon clinique ni de termes DSM. " +
                              "Concis (6-10 lignes max). Réponds directement en Markdown.");
            }
            else if (voixCible == "clinique")
            {
                sb.AppendLine("RAPPEL TON OBLIGATOIRE : Voix clinique. Utilise la terminologie pédopsychiatrique précise (DSM, troubles, axes). " +
                              "Rigueur et concision clinique. Réponds directement en Markdown.");
            }
            else
            {
                sb.AppendLine("RAPPEL TON OBLIGATOIRE : Voix mixte. Précis sans jargon excessif. " +
                              "Lisible par les parents ET utile aux professionnels. Réponds directement en Markdown.");
            }

            return sb.ToString();
        }

        // ── Génération progressive Contexte familial (1 narrative + 4 cards) ─

        /// <summary>
        /// Génère le bloc « Contexte familial » en 6 appels LLM séquentiels (récit narratif +
        /// 5 cartes : Père / Mère / Fratrie / Autres figures / Points à retenir). Chaque appel
        /// reçoit le dossier complet, les marqueurs `**Titre**` sont concaténés au fil de l'eau
        /// pour permettre au rendu HTML de découper les sections.
        /// </summary>
        public async Task SuggestContexteFamilialProgressiveAsync(
            DossierReading reading,
            Action<string> onSectionReady,
            CancellationToken ct = default)
        {
            var context = reading.RenderForLlm();
            if (string.IsNullOrWhiteSpace(context)) { onSectionReady("(Aucun contenu source disponible.)"); return; }

            var blocCf = new RestitutionBloc("patient_contexte_familial", "Contexte familial", 6, "clinique");
            var systemPrompt = BuildSystemPrompt(blocCf);

            var subsections = new (string Title, string Instruction)[]
            {
                ("**Récit familial**",
                 "Rédige UNIQUEMENT le récit familial (3-5 lignes) décrivant : composition du foyer " +
                 "(parents séparés ou en couple, garde alternée…), climat actuel, événements de vie " +
                 "majeurs (déménagement, séparation, deuil, recomposition…). Style clinique narratif, " +
                 "PAS de listes. Commence directement par le récit, sans titre."),

                ("**Père**",
                 "Rédige UNIQUEMENT la fiche signalétique du père sous forme de liste à puces : " +
                 "prénom et âge si renseignés, activité professionnelle, lieu de vie, statut conjugal/familial " +
                 "(célibataire, en couple avec X, recomposition…). Format : `- Item : valeur.` " +
                 "Si une donnée manque écrire « Non renseigné ». Commence directement par la liste."),

                ("**Mère**",
                 "Rédige UNIQUEMENT la fiche signalétique de la mère sous forme de liste à puces : " +
                 "prénom et âge si renseignés, activité professionnelle, lieu de vie, statut conjugal/familial. " +
                 "Format : `- Item : valeur.` Si une donnée manque écrire « Non renseigné ». " +
                 "Commence directement par la liste."),

                ("**Fratrie**",
                 "Rédige UNIQUEMENT la liste de la fratrie sous forme de puces, une par enfant : " +
                 "prénom, âge, lien (même père et mère / côté mère / côté père). " +
                 "Format : `- Prénom, N ans (lien).` Si pas de fratrie, écrire `- Enfant unique.` " +
                 "Commence directement par la liste."),

                ("**Autres figures**",
                 "Rédige UNIQUEMENT la liste des figures d'attachement TIERCES — c'est-à-dire HORS parents et fratrie " +
                 "(déjà traités dans leurs propres sections). Exemples : grands-parents, oncle/tante, grand-mère paternelle, " +
                 "cousin proche, éducateur référent, assistante maternelle, famille d'accueil, tuteur légal, " +
                 "voisin ou adulte de confiance mentionné. " +
                 "Format : `- Figure (prénom si connu) : rôle dans la vie de l'enfant.` " +
                 "NE PAS répéter le père, la mère ou les frères/sœurs déjà listés. " +
                 "Si aucune figure tierce n'est mentionnée dans le dossier, écrire UNIQUEMENT : `- Aucune figure tierce identifiée.` " +
                 "Commence directement par la liste."),

                ("**Points à retenir**",
                 "Rédige UNIQUEMENT 2-4 lignes pointant les éléments du contexte familial susceptibles " +
                 "d'éclairer le tableau clinique (insécurité affective, conflit de loyauté, parent absent, " +
                 "parentification…). Style bienveillant, sans jugement. Pas de listes — un paragraphe court. " +
                 "Commence directement par le paragraphe.")
            };

            await RunProgressiveSubsectionsAsync(systemPrompt, context, blocCf.VoixCible, subsections, onSectionReady, 500, ct);
        }

        // ── Génération progressive Antécédents (6 sous-sections) ────────────

        /// <summary>
        /// Génère le bloc « Antécédents » en 6 appels LLM séquentiels :
        /// médicaux, développementaux, familiaux, suivi résumé (compact),
        /// bilans résumé (compact), détail complet suivi+bilans (page C conditionnelle).
        /// </summary>
        public async Task SuggestAntecedentsProgressiveAsync(
            DossierReading reading,
            Action<string> onSectionReady,
            CancellationToken ct = default)
        {
            var context = reading.RenderForLlm();
            if (string.IsNullOrWhiteSpace(context)) { onSectionReady("(Aucun contenu source disponible.)"); return; }

            var blocAt = new RestitutionBloc("patient_antecedents", "Antécédents", 6, "clinique");
            var systemPrompt = BuildSystemPrompt(blocAt);

            var subsections = new (string Title, string Instruction)[]
            {
                ("**Antécédents médicaux**",
                 "Rédige UNIQUEMENT la liste à puces des antécédents médicaux : grossesse, " +
                 "accouchement, période néonatale, maladies chroniques, hospitalisations, traitements " +
                 "en cours. Format : `- **Item :** valeur.` Si une donnée manque, écrire " +
                 "« sans particularité déclarée » ou « aucun connu ». Pas de titre, pas de commentaire."),

                ("**Antécédents développementaux**",
                 "Rédige UNIQUEMENT la liste à puces des acquisitions développementales : " +
                 "acquisition de la marche (âge en mois), acquisition du langage (âge / délais), " +
                 "propreté diurne (âge), propreté nocturne (âge ou statut), latéralisation si pertinent. " +
                 "Format : `- **Item :** valeur.` Si une acquisition est dans la norme, écrire « dans les délais »."),

                ("**Antécédents familiaux**",
                 "Rédige UNIQUEMENT la liste à puces des antécédents familiaux : troubles de l'attention (TDAH), " +
                 "troubles anxieux, troubles de l'humeur, troubles du neurodéveloppement, addictions, suicide. " +
                 "Format : `- **Item :** valeur.` Si non connu, écrire « non connu »."),

                ("**Suivi résumé**",
                 "Génère UNIQUEMENT des étiquettes courtes — INTERDIT d'écrire motifs, évolutions, durées, résultats.\n" +
                 "Format STRICT : `- [Nom du suivi] — [statut court]`\n" +
                 "Exemples autorisés : `- Suivi CMP — En cours`, `- Psychomotricité — Terminé 2024`, `- Traitement SLENYTO — Actif`\n" +
                 "Maximum 8 mots par ligne. Une ligne par suivi. Pas de tiret secondaire, pas de parenthèse longue.\n" +
                 "Les détails (motifs, évolution, comptes rendus) sont réservés à la section « Parcours — détail ».\n" +
                 "Si aucun suivi, écrire UNIQUEMENT : `- Aucun suivi spécialisé.`"),

                ("**Bilans résumé**",
                 "Génère UNIQUEMENT des étiquettes courtes — INTERDIT d'écrire résultats, conclusions, scores.\n" +
                 "Format STRICT : `- [Type de bilan] — [année ou statut court]`\n" +
                 "Exemples autorisés : `- Bilan neuropsychologique — 2025`, `- Évaluation pédopsychiatrique — 06/2026`, `- Bilan orthophonique — À programmer`\n" +
                 "Maximum 8 mots par ligne. Une ligne par bilan. Pas de résumé des résultats ici.\n" +
                 "Les résultats et conclusions sont réservés à la section « Parcours — détail ».\n" +
                 "Si aucun bilan, écrire UNIQUEMENT : `- Aucun bilan formel.`"),

                ("**Parcours — détail**",
                 "Rédige le détail COMPLET du parcours de soins — c'est ici ET UNIQUEMENT ici que tu mets " +
                 "les motifs, évolutions, durées, résultats, comptes rendus et conclusions cliniques.\n" +
                 "Structure en 2 sections séparées :\n" +
                 "SECTION 1 — `**Suivi antérieur**` : pour chaque suivi : nom de la structure, " +
                 "période (dates début–fin ou 'en cours'), fréquence, motif de prise en charge, évolution observée.\n" +
                 "SECTION 2 — `**Bilans réalisés**` : pour chaque bilan : type, date, praticien/structure si connu, " +
                 "résultats clés ou conclusions principales.\n" +
                 "Si aucun suivi ET aucun bilan dans le dossier, écrire UNIQUEMENT : " +
                 "`Aucun antécédent de suivi ou de bilan identifié dans le dossier.`")
            };

            await RunProgressiveSubsectionsAsync(systemPrompt, context, blocAt.VoixCible, subsections, onSectionReady, 500, ct);
        }

        // ── Génération progressive Situation actuelle (5 sous-sections) ─────

        /// <summary>
        /// Génère le bloc « Situation actuelle » en 5 appels LLM séquentiels (école, maison,
        /// avec les autres, forces, activités et intérêts). Sources principales : cartographies
        /// d'évaluation + notes de consultation récentes.
        /// </summary>
        public async Task SuggestSituationActuelleProgressiveAsync(
            DossierReading reading,
            Action<string> onSectionReady,
            CancellationToken ct = default)
        {
            var context = reading.RenderForLlm();
            if (string.IsNullOrWhiteSpace(context)) { onSectionReady("(Aucun contenu source disponible.)"); return; }

            var blocSa = new RestitutionBloc("patient_situation_actuelle", "Situation actuelle", 7, "clinique");
            var systemPrompt = BuildSystemPrompt(blocSa);

            var subsections = new (string Title, string Instruction)[]
            {
                ("**À l'école**",
                 "Rédige UNIQUEMENT une liste à puces de 3-5 items sur le fonctionnement scolaire : " +
                 "intégration au groupe classe, agressivité envers maîtresse/camarades, difficultés " +
                 "d'apprentissage, besoin de cadre et de repères renforcés. Format : `- Phrase courte.`"),

                ("**À la maison**",
                 "Rédige UNIQUEMENT une liste à puces de 3-5 items sur le fonctionnement au domicile : " +
                 "crises de colère, oppositions, hyperactivité, surconsommation d'écrans, troubles du " +
                 "sommeil… Format : `- Phrase courte.`"),

                ("**Avec les autres**",
                 "Rédige UNIQUEMENT une liste à puces de 2-4 items sur la sphère relationnelle " +
                 "au-delà de l'école : difficultés relationnelles, tolérance à la frustration, " +
                 "réactions impulsives en cas de conflit. Format : `- Phrase courte.`"),

                ("**Forces observées**",
                 "Rédige UNIQUEMENT une liste à puces de 4-6 items sur les ressources et compétences " +
                 "positives de l'enfant : curiosité, attachement à ses proches, bonne capacité " +
                 "d'apprentissage, imagination, créativité, envie de bien faire et de réussir. " +
                 "Format : `- Phrase courte.`"),

                ("**Activités et intérêts**",
                 "Rédige UNIQUEMENT une liste à puces de 1-3 items sur les activités extra-scolaires, " +
                 "hobbies, sports, centres d'intérêt particuliers. Format : `- Activité : description courte.` " +
                 "Si aucune activité documentée, écrire `- Aucune activité extra-scolaire documentée à ce jour.`")
            };

            await RunProgressiveSubsectionsAsync(systemPrompt, context, blocSa.VoixCible, subsections, onSectionReady, 400, ct);
        }

        // ── Génération progressive Cartographie de l'enfant ─────────────────

        /// <summary>
        /// Génère le bloc « Cartographie de l'enfant » sphère par sphère (V0.2 : sphère 1
        /// Attachement câblée, sphères 2-8 ajoutées progressivement dans les itérations
        /// suivantes). Pour chaque sphère, Med reçoit le niveau numérique calculé par
        /// CartographieScoringService + la lecture émotionnelle canonique, et produit :
        /// (a) 2-3 observations cliniques courtes basées sur le dossier ;
        /// (b) 1 phrase de niveau clinique au format « Mot-clé (qualifier court). ».
        /// Le résultat est concaténé avec marqueurs `## Sphère N — Nom` pour parsing HTML.
        /// </summary>
        public async Task SuggestCartoEnfantProgressiveAsync(
            DossierReading reading,
            Action<string> onSectionReady,
            CancellationToken ct = default)
        {
            var context = reading.RenderForLlm();
            if (string.IsNullOrWhiteSpace(context)) { onSectionReady("(Aucun contenu source disponible.)"); return; }

            var blocCe = new RestitutionBloc("carto_enfant", "Cartographie de l'enfant", 8, "clinique");
            var systemPrompt = BuildSystemPrompt(blocCe);

            // V0.2 : seule la sphère 1 (Attachement) est câblée. On ajoutera les 7 autres
            // sphère par sphère pour valider à chaque étape avec le médecin.
            var subsections = new List<(string Title, string Instruction)>();
            var carto = reading.LatestCartographieEnfant;

            if (carto != null)
            {
                var seg    = carto.Attachement;
                var niveau = Services.Evaluations.CartographieScoringService.Calculer(seg.Score, carto.AgeAuMomentDeLaSaisie);
                if (niveau.HasValue)
                {
                    var niveauLabel = Models.Evaluations.CartographieContent.NiveauLabel(niveau);
                    var lecture     = Models.Evaluations.CartographieContent.LectureEmotionnelle(niveau);
                    var ageTxt      = carto.AgeAuMomentDeLaSaisie?.ToString() ?? "?";

                    var itemLines = string.Join("\n", seg.Items.Select(i => $"{(i.IsChecked ? "✓" : "✗")} {i.Affirmation}"));

                    subsections.Add((
                        "## Sphère 1 — Attachement",
                        $"=== DONNÉES D'ÉVALUATION — ATTACHEMENT (source principale) ===\n" +
                        $"Score : {seg.Score}/6 à {ageTxt} ans → niveau « {niveauLabel} »\n" +
                        $"Items cochés ✓ = comportement présent | ✗ = comportement absent ou insuffisant :\n{itemLines}\n" +
                        $"Lecture canonique du niveau : {lecture}\n\n" +
                        "Croise ces résultats avec le dossier bleu (1ère consultation, notes, synthèse Med) pour rédiger les observations.\n\n" +
                        "Rédige UNIQUEMENT la sphère Attachement dans le format strict suivant :\n\n" +
                        "**Observations**\n" +
                        "- 2 ou 3 puces COURTES (1 ligne chacune), style clinique pédopsychiatrique précis.\n" +
                        "- Appuie-toi D'ABORD sur les items ✓/✗ ci-dessus (ce que le parent a rapporté), en croisant avec le dossier.\n" +
                        "- Reste STRICTEMENT dans le domaine Attachement & sécurité intérieure : anxiété de séparation, besoin de réassurance, qualité du lien, capacité d'apaisement.\n" +
                        "- NE MENTIONNE PAS la régulation émotionnelle, le langage, le comportement ou toute autre sphère — celles-ci feront l'objet de leurs propres sections.\n" +
                        "- N'INVENTE RIEN : si le dossier est silencieux sur l'attachement, écris une seule puce : `- Données limitées concernant la sphère d'attachement dans le dossier.`\n\n" +
                        "**Niveau clinique** : 1 SEULE phrase courte ancrée DANS la sphère Attachement. " +
                        "Format obligatoire : `Mot-clé (qualifier court sur l'attachement uniquement).`\n" +
                        "Exemples selon le niveau :\n" +
                        "- Vert foncé → `Ressource solide (Sécurité intérieure intégrée).`\n" +
                        "- Vert clair → `Satisfaisant (Base sécurisante présente).`\n" +
                        "- Jaune clair → `À surveiller (Équilibre du lien encore fragile).`\n" +
                        "- Jaune foncé → `Fragilisé (Besoin d'étayage dans le lien).`\n" +
                        "- Rouge clair → `Alerte (Anxiété d'attachement marquée).`\n" +
                        "- Rouge foncé → `Très fragilisé (Insécurité affective profonde).`\n" +
                        "IMPORTANT : Le qualifier doit rester dans le champ de l'attachement — n'inclus PAS d'autres sphères.\n\n" +
                        "Commence directement par `**Observations**`, n'ajoute pas de titre de sphère ni de commentaire."));
                }
                else
                {
                    subsections.Add((
                        "## Sphère 1 — Attachement",
                        "L'étape 3 de cartographie de l'enfant n'a pas été clôturée ou l'âge est hors fourchette 3-11 ans. " +
                        "Écris : `**Observations** : non disponibles (étape 3 d'évaluation non clôturée).` puis sur une ligne `**Niveau clinique** : Non évalué.`"));
                }

                // === SPHÈRE 2 — RÉGULATION ÉMOTIONNELLE ===
                var seg2    = carto.Emotions;
                var niveau2 = Services.Evaluations.CartographieScoringService.Calculer(seg2.Score, carto.AgeAuMomentDeLaSaisie);
                if (niveau2.HasValue)
                {
                    var niveauLabel2 = Models.Evaluations.CartographieContent.NiveauLabel(niveau2);
                    var lecture2     = Models.Evaluations.CartographieContent.LectureEmotionnelle(niveau2);
                    var ageTxt2      = carto.AgeAuMomentDeLaSaisie?.ToString() ?? "?";
                    var itemLines2   = string.Join("\n", seg2.Items.Select(i => $"{(i.IsChecked ? "✓" : "✗")} {i.Affirmation}"));

                    subsections.Add((
                        "## Sphère 2 — Régulation émotionnelle",
                        $"=== DONNÉES D'ÉVALUATION — RÉGULATION ÉMOTIONNELLE (source principale) ===\n" +
                        $"Score : {seg2.Score}/6 à {ageTxt2} ans → niveau « {niveauLabel2} »\n" +
                        $"Items cochés ✓ = comportement présent | ✗ = comportement absent ou insuffisant :\n{itemLines2}\n" +
                        $"Lecture canonique du niveau : {lecture2}\n\n" +
                        "Croise ces résultats avec le dossier bleu (1ère consultation, notes, synthèse Med) pour rédiger les observations.\n\n" +
                        "Rédige UNIQUEMENT la sphère Régulation émotionnelle dans le format strict suivant :\n\n" +
                        "**Observations**\n" +
                        "- 2 ou 3 puces COURTES (1 ligne chacune), style clinique pédopsychiatrique précis.\n" +
                        "- Appuie-toi D'ABORD sur les items ✓/✗ ci-dessus (ce que le parent a rapporté), en croisant avec le dossier.\n" +
                        "- Reste STRICTEMENT dans le domaine de la régulation émotionnelle : capacité à nommer les émotions, tolérance à la frustration, retour au calme, débordements émotionnels.\n" +
                        "- NE MENTIONNE PAS l'attachement, le langage, le comportement ou toute autre sphère — celles-ci font l'objet de leurs propres sections.\n" +
                        "- N'INVENTE RIEN : si le dossier est silencieux sur la régulation émotionnelle, écris une seule puce : `- Données limitées concernant la sphère de régulation émotionnelle dans le dossier.`\n\n" +
                        "**Niveau clinique** : 1 SEULE phrase courte ancrée DANS la sphère Régulation émotionnelle. " +
                        "Format obligatoire : `Mot-clé (qualifier court sur la régulation émotionnelle uniquement).`\n" +
                        "Exemples selon le niveau :\n" +
                        "- Vert foncé → `Régulation intégrée (Gestion émotionnelle fluide et autonome).`\n" +
                        "- Vert clair → `Satisfaisant (Régulation émotionnelle fonctionnelle).`\n" +
                        "- Jaune clair → `À surveiller (Régulation encore fragile selon le contexte).`\n" +
                        "- Jaune foncé → `Fragilisé (Débordements fréquents, retour au calme difficile).`\n" +
                        "- Rouge clair → `Alerte (Dysrégulation marquée).`\n" +
                        "- Rouge foncé → `Très fragilisé (Tempêtes émotionnelles, régulation non acquise).`\n" +
                        "IMPORTANT : Le qualifier doit rester dans le champ de la régulation émotionnelle — n'inclus PAS d'autres sphères.\n\n" +
                        "Commence directement par `**Observations**`, n'ajoute pas de titre de sphère ni de commentaire."));
                }
                else
                {
                    subsections.Add((
                        "## Sphère 2 — Régulation émotionnelle",
                        "L'étape 3 de cartographie de l'enfant n'a pas été clôturée ou l'âge est hors fourchette 3-11 ans. " +
                        "Écris : `**Observations** : non disponibles (étape 3 d'évaluation non clôturée).` puis sur une ligne `**Niveau clinique** : Non évalué.`"));
                }

                // === SPHÈRE 3 — LANGAGE ===
                var seg3    = carto.Langage;
                var niveau3 = Services.Evaluations.CartographieScoringService.Calculer(seg3.Score, carto.AgeAuMomentDeLaSaisie);
                if (niveau3.HasValue)
                {
                    var niveauLabel3 = Models.Evaluations.CartographieContent.NiveauLabel(niveau3);
                    var lecture3     = Models.Evaluations.CartographieContent.LectureEmotionnelle(niveau3);
                    var ageTxt3      = carto.AgeAuMomentDeLaSaisie?.ToString() ?? "?";
                    var itemLines3   = string.Join("\n", seg3.Items.Select(i => $"{(i.IsChecked ? "✓" : "✗")} {i.Affirmation}"));

                    subsections.Add((
                        "## Sphère 3 — Langage",
                        $"=== DONNÉES D'ÉVALUATION — LANGAGE & COMMUNICATION (source principale) ===\n" +
                        $"Score : {seg3.Score}/6 à {ageTxt3} ans → niveau « {niveauLabel3} »\n" +
                        $"Items cochés ✓ = comportement présent | ✗ = comportement absent ou insuffisant :\n{itemLines3}\n" +
                        $"Lecture canonique du niveau : {lecture3}\n\n" +
                        "Croise ces résultats avec le dossier bleu (1ère consultation, notes, synthèse Med) pour rédiger les observations.\n\n" +
                        "Rédige UNIQUEMENT la sphère Langage dans le format strict suivant :\n\n" +
                        "**Observations**\n" +
                        "- 2 ou 3 puces COURTES (1 ligne chacune), style clinique pédopsychiatrique précis.\n" +
                        "- Appuie-toi D'ABORD sur les items ✓/✗ ci-dessus (ce que le parent a rapporté), en croisant avec le dossier.\n" +
                        "- Reste STRICTEMENT dans le domaine du langage et de la communication : expression verbale, compréhension, vocabulaire, capacité à raconter, écoute.\n" +
                        "- NE MENTIONNE PAS l'attachement, la régulation émotionnelle, le comportement ou toute autre sphère — celles-ci font l'objet de leurs propres sections.\n" +
                        "- N'INVENTE RIEN : si le dossier est silencieux sur le langage, écris une seule puce : `- Données limitées concernant la sphère langage dans le dossier.`\n\n" +
                        "**Niveau clinique** : 1 SEULE phrase courte ancrée DANS la sphère Langage. " +
                        "Format obligatoire : `Mot-clé (qualifier court sur le langage uniquement).`\n" +
                        "Exemples selon le niveau :\n" +
                        "- Vert foncé → `Langage intégré (Expression et compréhension fluides).`\n" +
                        "- Vert clair → `Satisfaisant (Communication fonctionnelle bien installée).`\n" +
                        "- Jaune clair → `À surveiller (Fluctuant selon le contexte émotionnel).`\n" +
                        "- Jaune foncé → `Fragilisé (Langage limité en situation de stress).`\n" +
                        "- Rouge clair → `Alerte (Difficultés d'expression ou de compréhension marquées).`\n" +
                        "- Rouge foncé → `Très fragilisé (Communication très altérée).`\n" +
                        "IMPORTANT : Le qualifier doit rester dans le champ du langage — n'inclus PAS d'autres sphères.\n\n" +
                        "Commence directement par `**Observations**`, n'ajoute pas de titre de sphère ni de commentaire."));
                }
                else
                {
                    subsections.Add((
                        "## Sphère 3 — Langage",
                        "L'étape 3 de cartographie de l'enfant n'a pas été clôturée ou l'âge est hors fourchette 3-11 ans. " +
                        "Écris : `**Observations** : non disponibles (étape 3 d'évaluation non clôturée).` puis sur une ligne `**Niveau clinique** : Non évalué.`"));
                }

                // === SPHÈRE 4 — TEMPÉRAMENT ===
                var temp = carto.Temperament;
                if (temp.IsRenseigne)
                {
                    var ageTxt4 = carto.AgeAuMomentDeLaSaisie?.ToString() ?? "?";
                    subsections.Add((
                        "## Sphère 4 — Tempérament",
                        $"=== DONNÉES D'ÉVALUATION — TEMPÉRAMENT (source principale) ===\n" +
                        $"Âge : {ageTxt4} ans\n" +
                        $"Scores par axe (1 = très bas / 5 = très élevé) :\n" +
                        $"- Niveau d'activité      : {temp.NiveauActivite}/5\n" +
                        $"- Régularité / Rythme    : {temp.Regularite}/5\n" +
                        $"- Réactivité sensorielle : {temp.ReactiviteSensorielle}/5\n" +
                        $"- Intensité émotionnelle : {temp.IntensiteEmotionnelle}/5\n" +
                        $"- Adaptabilité           : {temp.Adaptabilite}/5\n" +
                        $"- Temps de réaction      : {temp.TempsDeReaction}/5\n\n" +
                        "Croise ces scores avec le dossier bleu (notes, 1ère consultation, synthèse Med).\n\n" +
                        "IMPORTANT : Le tempérament n'est PAS pathologique — c'est le câblage naturel de l'enfant.\n" +
                        "Ton rôle est de décrire la forme intérieure de cet enfant pour aider parents et intervenants à adapter l'environnement.\n\n" +
                        "Rédige UNIQUEMENT la sphère Tempérament dans le format strict suivant :\n\n" +
                        "**Observations**\n\n" +
                        "**Profil global**\n" +
                        "2 à 3 phrases décrivant la forme tempéramentielle globale de cet enfant (ex : enfant à haute intensité, lent à s'adapter, bon rythme régulier…).\n" +
                        "Appuie-toi sur les axes saillants (scores extrêmes 1-2 ou 4-5) ET sur le dossier.\n\n" +
                        "**Points d'appui**\n" +
                        "- 1 ou 2 puces : traits tempéramentaux qui jouent EN FAVEUR de l'enfant (ressources, leviers thérapeutiques ou éducatifs).\n\n" +
                        "**Points d'attention**\n" +
                        "- 1 ou 2 puces : axes extrêmes qui créent des frictions dans l'environnement, avec une piste d'adaptation concrète par puce.\n\n" +
                        "NE MENTIONNE PAS les autres sphères (attachement, langage, régulation).\n" +
                        "Commence directement par `**Observations**` sans titre de sphère ni commentaire."));
                }
                else
                {
                    subsections.Add((
                        "## Sphère 4 — Tempérament",
                        "Le profil de tempérament n'a pas été renseigné. Écris : `**Observations** : non disponibles (profil tempérament non renseigné).`"));
                }

                // === SPHÈRE 5 — PSYCHOMOTRICITÉ ===
                var psycho = carto.Psychomotricite;
                if (psycho.IsRenseigne)
                {
                    var ageTxt5 = carto.AgeAuMomentDeLaSaisie?.ToString() ?? "?";
                    subsections.Add((
                        "## Sphère 5 — Psychomotricité",
                        $"=== DONNÉES D'ÉVALUATION — PSYCHOMOTRICITÉ (source principale) ===\n" +
                        $"Âge : {ageTxt5} ans\n" +
                        $"Scores par axe (1 = très bas / 5 = très élevé) :\n" +
                        $"- Motricité globale    : {psycho.MotriciteGlobale}/5\n" +
                        $"- Motricité fine       : {psycho.MotriciteFine}/5\n" +
                        $"- Tonus                : {psycho.Tonus}/5\n" +
                        $"- Dextérité            : {psycho.Dexterite}/5\n" +
                        $"- Coordination         : {psycho.Coordination}/5\n" +
                        $"- Impulsivité motrice  : {psycho.ImpulsiviteMotrice}/5\n\n" +
                        "Croise ces scores avec le dossier bleu (notes, 1ère consultation, synthèse Med).\n\n" +
                        "IMPORTANT : Le profil psychomoteur n'est PAS pathologique en lui-même — c'est une cartographie du corps de l'enfant.\n" +
                        "Ton rôle est de décrire le profil moteur pour aider parents et intervenants à comprendre les besoins corporels de cet enfant.\n\n" +
                        "Rédige UNIQUEMENT la sphère Psychomotricité dans le format strict suivant :\n\n" +
                        "**Observations**\n\n" +
                        "**Profil global**\n" +
                        "2 à 3 phrases décrivant le profil moteur de cet enfant (ex : enfant au tonus bas, motricité fine fragile, bonne coordination générale…).\n" +
                        "Appuie-toi sur les axes saillants (scores extrêmes 1-2 ou 4-5) ET sur le dossier.\n\n" +
                        "**Points d'appui**\n" +
                        "- 1 ou 2 puces : axes moteurs qui jouent EN FAVEUR de l'enfant (ressources corporelles, leviers thérapeutiques ou éducatifs).\n\n" +
                        "**Points d'attention**\n" +
                        "- 1 ou 2 puces : axes en difficulté avec une piste d'adaptation concrète par puce (aménagement scolaire, thérapie recommandée, adaptation des activités).\n\n" +
                        "NE MENTIONNE PAS les autres sphères (attachement, langage, tempérament, régulation).\n" +
                        "Commence directement par `**Observations**` sans titre de sphère ni commentaire."));
                }
                else
                {
                    subsections.Add((
                        "## Sphère 5 — Psychomotricité",
                        "Le profil psychomoteur n'a pas été renseigné. Écris : `**Observations** : non disponibles (profil psychomoteur non renseigné).`"));
                }

                // === SPHÈRE 6 — IMAGINATION & JEU ===
                var seg6    = carto.Imaginaire;
                var niveau6 = Services.Evaluations.CartographieScoringService.Calculer(seg6.Score, carto.AgeAuMomentDeLaSaisie);
                if (niveau6.HasValue)
                {
                    var niveauLabel6 = Models.Evaluations.CartographieContent.NiveauLabel(niveau6);
                    var lecture6     = Models.Evaluations.CartographieContent.LectureEmotionnelle(niveau6);
                    var ageTxt6      = carto.AgeAuMomentDeLaSaisie?.ToString() ?? "?";
                    var itemLines6   = string.Join("\n", seg6.Items.Select(i => $"{(i.IsChecked ? "✓" : "✗")} {i.Affirmation}"));

                    subsections.Add((
                        "## Sphère 6 — Imagination & Jeu",
                        $"=== DONNÉES D'ÉVALUATION — IMAGINATION & JEU (source principale) ===\n" +
                        $"Score : {seg6.Score}/6 à {ageTxt6} ans → niveau « {niveauLabel6} »\n" +
                        $"Items cochés ✓ = comportement présent | ✗ = comportement absent ou insuffisant :\n{itemLines6}\n" +
                        $"Lecture canonique du niveau : {lecture6}\n\n" +
                        "Croise ces résultats avec le dossier bleu (1ère consultation, notes, synthèse Med).\n\n" +
                        "Rédige UNIQUEMENT la sphère Imagination & Jeu dans le format strict suivant :\n\n" +
                        "**Observations**\n" +
                        "- 2 ou 3 puces COURTES (1 ligne chacune), style clinique pédopsychiatrique précis.\n" +
                        "- Appuie-toi D'ABORD sur les items ✓/✗, en croisant avec le dossier.\n" +
                        "- Reste STRICTEMENT dans le domaine de l'imaginaire et du jeu symbolique : capacité à inventer, jouer « comme si », raconter, se réconforter via l'imaginaire.\n" +
                        "- NE MENTIONNE PAS les autres sphères.\n" +
                        "- N'INVENTE RIEN : si le dossier est silencieux, écris : `- Données limitées concernant la sphère imagination & jeu dans le dossier.`\n\n" +
                        "**Niveau clinique** : 1 SEULE phrase courte ancrée dans la sphère Imagination & Jeu.\n" +
                        "Format : `Mot-clé (qualifier court).`\n" +
                        "Exemples :\n" +
                        "- Vert foncé → `Ressource créative (Imaginaire riche et intégré).`\n" +
                        "- Vert clair → `Satisfaisant (Jeu symbolique fluide et présent).`\n" +
                        "- Jaune clair → `À surveiller (Jeu symbolique irrégulier).`\n" +
                        "- Jaune foncé → `Fragilisé (Imaginaire peu investi ou rigide).`\n" +
                        "- Rouge clair → `Alerte (Accès à l'imaginaire difficile).`\n" +
                        "- Rouge foncé → `Très fragilisé (Jeu symbolique absent ou très limité).`\n\n" +
                        "Commence directement par `**Observations**`, n'ajoute pas de titre de sphère ni de commentaire."));
                }
                else
                {
                    subsections.Add((
                        "## Sphère 6 — Imagination & Jeu",
                        "L'étape 3 de cartographie n'a pas été clôturée ou l'âge est hors fourchette 3-11 ans. " +
                        "Écris : `**Observations** : non disponibles (étape 3 d'évaluation non clôturée).` puis `**Niveau clinique** : Non évalué.`"));
                }

                // === SPHÈRE 7 — PENSÉE & APPRENTISSAGES ===
                var seg7    = carto.Pensee;
                var niveau7 = Services.Evaluations.CartographieScoringService.Calculer(seg7.Score, carto.AgeAuMomentDeLaSaisie);
                if (niveau7.HasValue)
                {
                    var niveauLabel7 = Models.Evaluations.CartographieContent.NiveauLabel(niveau7);
                    var lecture7     = Models.Evaluations.CartographieContent.LectureEmotionnelle(niveau7);
                    var ageTxt7      = carto.AgeAuMomentDeLaSaisie?.ToString() ?? "?";
                    var itemLines7   = string.Join("\n", seg7.Items.Select(i => $"{(i.IsChecked ? "✓" : "✗")} {i.Affirmation}"));

                    subsections.Add((
                        "## Sphère 7 — Pensée & Apprentissages",
                        $"=== DONNÉES D'ÉVALUATION — PENSÉE & APPRENTISSAGES (source principale) ===\n" +
                        $"Score : {seg7.Score}/6 à {ageTxt7} ans → niveau « {niveauLabel7} »\n" +
                        $"Items cochés ✓ = comportement présent | ✗ = comportement absent ou insuffisant :\n{itemLines7}\n" +
                        $"Lecture canonique du niveau : {lecture7}\n\n" +
                        "Croise ces résultats avec le dossier bleu (1ère consultation, notes, synthèse Med).\n\n" +
                        "Rédige UNIQUEMENT la sphère Pensée & Apprentissages dans le format strict suivant :\n\n" +
                        "**Observations**\n" +
                        "- 2 ou 3 puces COURTES (1 ligne chacune), style clinique pédopsychiatrique précis.\n" +
                        "- Appuie-toi D'ABORD sur les items ✓/✗, en croisant avec le dossier.\n" +
                        "- Reste STRICTEMENT dans le domaine de la pensée et des apprentissages : curiosité, compréhension des consignes, concentration, résolution de problèmes, flexibilité cognitive.\n" +
                        "- NE MENTIONNE PAS les autres sphères.\n" +
                        "- N'INVENTE RIEN : si le dossier est silencieux, écris : `- Données limitées concernant la sphère pensée & apprentissages dans le dossier.`\n\n" +
                        "**Niveau clinique** : 1 SEULE phrase courte ancrée dans la sphère Pensée & Apprentissages.\n" +
                        "Format : `Mot-clé (qualifier court).`\n" +
                        "Exemples :\n" +
                        "- Vert foncé → `Pensée intégrée (Organisation cognitive solide et flexible).`\n" +
                        "- Vert clair → `Satisfaisant (Compréhension et adaptation fonctionnelles).`\n" +
                        "- Jaune clair → `À surveiller (Concentration ou flexibilité fragiles).`\n" +
                        "- Jaune foncé → `Fragilisé (Difficultés d'organisation cognitive marquées).`\n" +
                        "- Rouge clair → `Alerte (Pensée rigide ou fragmentée).`\n" +
                        "- Rouge foncé → `Très fragilisé (Organisation cognitive très altérée).`\n\n" +
                        "Commence directement par `**Observations**`, n'ajoute pas de titre de sphère ni de commentaire."));
                }
                else
                {
                    subsections.Add((
                        "## Sphère 7 — Pensée & Apprentissages",
                        "L'étape 3 de cartographie n'a pas été clôturée ou l'âge est hors fourchette 3-11 ans. " +
                        "Écris : `**Observations** : non disponibles (étape 3 d'évaluation non clôturée).` puis `**Niveau clinique** : Non évalué.`"));
                }

                // === SPHÈRE 8 — ATTENTION & FONCTIONS EXÉCUTIVES ===
                var att = carto.Attention;
                if (att.IsRenseigne)
                {
                    var ageTxt8 = carto.AgeAuMomentDeLaSaisie?.ToString() ?? "?";
                    subsections.Add((
                        "## Sphère 8 — Attention & Fonctions exécutives",
                        $"=== DONNÉES D'ÉVALUATION — ATTENTION & FONCTIONS EXÉCUTIVES (source principale) ===\n" +
                        $"Âge : {ageTxt8} ans\n" +
                        $"Scores par axe (1 = très bas / 5 = très élevé) :\n" +
                        $"- Attention soutenue          : {att.AttentionSoutenue}/5\n" +
                        $"- Attention sélective         : {att.AttentionSelective}/5\n" +
                        $"- Attention divisée           : {att.AttentionDivisee}/5\n" +
                        $"- Inhibition (contrôle)       : {att.Inhibition}/5\n" +
                        $"- Planification               : {att.Planification}/5\n" +
                        $"- Flexibilité attentionnelle  : {att.FlexibiliteAttentionnelle}/5\n\n" +
                        "Croise ces scores avec le dossier bleu (notes, 1ère consultation, synthèse Med).\n\n" +
                        "IMPORTANT : Ce profil attentionnel n'est PAS nécessairement un trouble — il décrit le fonctionnement exécutif de cet enfant.\n" +
                        "Ton rôle est de décrire comment cet enfant gère son attention pour aider parents et intervenants à adapter l'environnement.\n\n" +
                        "Rédige UNIQUEMENT la sphère Attention & Fonctions exécutives dans le format strict suivant :\n\n" +
                        "**Observations**\n\n" +
                        "**Profil global**\n" +
                        "2 à 3 phrases décrivant le profil attentionnel de cet enfant (ex : enfant avec bonne attention soutenue, inhibition fragilisée, planification en développement…).\n" +
                        "Appuie-toi sur les axes saillants (scores extrêmes 1-2 ou 4-5) ET sur le dossier.\n\n" +
                        "**Points d'appui**\n" +
                        "- 1 ou 2 puces : axes attentionnels qui jouent EN FAVEUR de l'enfant (ressources, leviers thérapeutiques ou éducatifs).\n\n" +
                        "**Points d'attention**\n" +
                        "- 1 ou 2 puces : axes fragilisés avec une piste d'adaptation concrète (aménagement scolaire, stratégie compensatoire, suivi recommandé).\n\n" +
                        "NE MENTIONNE PAS les autres sphères (attachement, langage, tempérament, psychomotricité).\n" +
                        "Commence directement par `**Observations**` sans titre de sphère ni commentaire."));
                }
                else
                {
                    subsections.Add((
                        "## Sphère 8 — Attention & Fonctions exécutives",
                        "Le profil attentionnel n'a pas été renseigné. Écris : `**Observations** : non disponibles (profil attentionnel non renseigné).`"));
                }
            }
            else
            {
                subsections.Add((
                    "## Sphère 1 — Attachement",
                    "Aucune cartographie enfant disponible. Écris : `**Observations** : non disponibles (aucune évaluation clôturée).` puis sur une ligne `**Niveau clinique** : Non évalué.`"));
                subsections.Add((
                    "## Sphère 2 — Régulation émotionnelle",
                    "Aucune cartographie enfant disponible. Écris : `**Observations** : non disponibles (aucune évaluation clôturée).` puis sur une ligne `**Niveau clinique** : Non évalué.`"));
                subsections.Add((
                    "## Sphère 3 — Langage",
                    "Aucune cartographie enfant disponible. Écris : `**Observations** : non disponibles (aucune évaluation clôturée).` puis sur une ligne `**Niveau clinique** : Non évalué.`"));
                subsections.Add((
                    "## Sphère 4 — Tempérament",
                    "Aucune cartographie enfant disponible. Écris : `**Observations** : non disponibles (aucune évaluation clôturée).`"));
                subsections.Add((
                    "## Sphère 5 — Psychomotricité",
                    "Aucune cartographie enfant disponible. Écris : `**Observations** : non disponibles (aucune évaluation clôturée).`"));
                subsections.Add((
                    "## Sphère 6 — Imagination & Jeu",
                    "Aucune cartographie enfant disponible. Écris : `**Observations** : non disponibles (aucune évaluation clôturée).` puis sur une ligne `**Niveau clinique** : Non évalué.`"));
                subsections.Add((
                    "## Sphère 7 — Pensée & Apprentissages",
                    "Aucune cartographie enfant disponible. Écris : `**Observations** : non disponibles (aucune évaluation clôturée).` puis sur une ligne `**Niveau clinique** : Non évalué.`"));
                subsections.Add((
                    "## Sphère 8 — Attention & Fonctions exécutives",
                    "Aucune cartographie enfant disponible. Écris : `**Observations** : non disponibles (aucune évaluation clôturée).`"));
            }

            // Sphère 8 câblée en V0.8.

            await RunProgressiveSubsectionsAsync(systemPrompt, context, blocCe.VoixCible,
                subsections.ToArray(), onSectionReady, 400, ct);
        }

        // ── Helper commun pour les générations progressives ─────────────────

        /// <summary>
        /// Exécute une série de sous-prompts en séquence, accumule les résultats avec leurs
        /// marqueurs Markdown `**Titre**` et notifie le ViewModel après chaque section.
        /// Factorise la mécanique commune aux générations Contexte familial / Antécédents /
        /// Situation actuelle pour éviter la duplication.
        /// </summary>
        private async Task RunProgressiveSubsectionsAsync(
            string systemPrompt,
            string context,
            string voixCible,
            (string Title, string Instruction)[] subsections,
            Action<string> onSectionReady,
            int maxTokensPerSection,
            CancellationToken ct)
        {
            var accumulated = new System.Text.StringBuilder();

            foreach (var (title, instruction) in subsections)
            {
                if (ct.IsCancellationRequested) break;

                var userPrompt = BuildSubsectionPrompt(context, instruction, voixCible);
                var messages   = new List<(string role, string content)> { ("user", userPrompt) };
                var result     = await _llmService.ChatAsync(systemPrompt, messages, maxTokensPerSection, ct);

                if (ct.IsCancellationRequested) break;

                if (accumulated.Length > 0) accumulated.AppendLine();
                accumulated.AppendLine(title);
                accumulated.AppendLine();
                accumulated.AppendLine(result.success ? result.result.Trim() : $"(Erreur : {result.error})");

                onSectionReady(accumulated.ToString());
            }
        }

        // ── Génération par sphère individuelle (V0.9 — 1 bloc = 1 sphère) ────

        /// <summary>
        /// Génère le contenu LLM pour une seule sphère de la Cartographie de l'enfant.
        /// Le résultat (contenu pur, sans header ## Sphère N) est renvoyé via onSectionReady.
        /// </summary>
        public async Task SuggestCartoSphereAsync(
            int sphereNum,
            DossierReading reading,
            Action<string> onSectionReady,
            CancellationToken ct = default)
        {
            var context = reading.RenderForLlm();
            if (string.IsNullOrWhiteSpace(context)) { onSectionReady("(Aucun contenu source disponible.)"); return; }

            var blocCe = new RestitutionBloc("carto_enfant", "Cartographie de l'enfant", 8, "clinique");
            var systemPrompt = BuildSystemPrompt(blocCe);
            var carto = reading.LatestCartographieEnfant;

            var instruction = BuildCartoSphereInstruction(sphereNum, carto);
            var userPrompt  = BuildSubsectionPrompt(context, instruction, blocCe.VoixCible);
            var messages    = new List<(string role, string content)> { ("user", userPrompt) };
            var result      = await _llmService.ChatAsync(systemPrompt, messages, 450, ct);

            onSectionReady(result.success ? result.result.Trim() : $"(Erreur : {result.error})");
        }

        private static string BuildCartoSphereInstruction(int sphereNum, CartographieEnfant? carto)
        {
            var ageTxt = carto?.AgeAuMomentDeLaSaisie?.ToString() ?? "?";

            return sphereNum switch
            {
                1 when carto != null => BuildSphere1Instruction(carto, ageTxt),
                2 when carto != null => BuildSphere2Instruction(carto, ageTxt),
                3 when carto != null => BuildSphere3Instruction(carto, ageTxt),
                4 when carto != null => BuildSphere4Instruction(carto, ageTxt),
                5 when carto != null => BuildSphere5Instruction(carto, ageTxt),
                6 when carto != null => BuildSphere6Instruction(carto, ageTxt),
                7 when carto != null => BuildSphere7Instruction(carto, ageTxt),
                8 when carto != null => BuildSphere8Instruction(carto, ageTxt),
                4 => "Le profil de tempérament n'a pas été renseigné. Écris : `**Observations** : non disponibles (profil tempérament non renseigné).`",
                5 => "Le profil psychomoteur n'a pas été renseigné. Écris : `**Observations** : non disponibles (profil psychomoteur non renseigné).`",
                8 => "Le profil attentionnel n'a pas été renseigné. Écris : `**Observations** : non disponibles (profil attentionnel non renseigné).`",
                _ => "Aucune cartographie enfant disponible. Écris : `**Observations** : non disponibles (aucune évaluation clôturée).` puis `**Niveau clinique** : Non évalué.`",
            };
        }

        private static string BuildSphere1Instruction(CartographieEnfant carto, string ageTxt)
        {
            var seg = carto.Attachement;
            var niveau = Services.Evaluations.CartographieScoringService.Calculer(seg.Score, carto.AgeAuMomentDeLaSaisie);
            if (!niveau.HasValue)
                return "L'étape 3 de cartographie n'a pas été clôturée ou l'âge est hors fourchette 3-11 ans. Écris : `**Observations** : non disponibles.` puis `**Niveau clinique** : Non évalué.`";
            var niveauLabel = Models.Evaluations.CartographieContent.NiveauLabel(niveau);
            var lecture     = Models.Evaluations.CartographieContent.LectureEmotionnelle(niveau);
            var itemLines   = string.Join("\n", seg.Items.Select(i => $"{(i.IsChecked ? "✓" : "✗")} {i.Affirmation}"));
            return $"=== DONNÉES D'ÉVALUATION — ATTACHEMENT ===\nScore : {seg.Score}/6 à {ageTxt} ans → niveau « {niveauLabel} »\n{itemLines}\nLecture canonique : {lecture}\n\n" +
                "Rédige UNIQUEMENT la sphère Attachement :\n\n**Observations**\n- 2 ou 3 puces COURTES. Appuie-toi D'ABORD sur les items ✓/✗, en croisant avec le dossier.\n- Reste STRICTEMENT dans le domaine Attachement & sécurité intérieure.\n- NE MENTIONNE PAS les autres sphères.\n- N'INVENTE RIEN.\n\n" +
                "**Niveau clinique** : 1 SEULE phrase. Format : `Mot-clé (qualifier court sur l'attachement).`\n" +
                $"Exemples : Vert foncé → `Ressource solide (Sécurité intérieure intégrée).` | Jaune clair → `À surveiller (Équilibre du lien encore fragile).` | Rouge clair → `Alerte (Anxiété d'attachement marquée).`\n\nCommence directement par `**Observations**`.";
        }

        private static string BuildSphere2Instruction(CartographieEnfant carto, string ageTxt)
        {
            var seg = carto.Emotions;
            var niveau = Services.Evaluations.CartographieScoringService.Calculer(seg.Score, carto.AgeAuMomentDeLaSaisie);
            if (!niveau.HasValue)
                return "L'étape 3 non clôturée ou âge hors fourchette. Écris : `**Observations** : non disponibles.` puis `**Niveau clinique** : Non évalué.`";
            var niveauLabel = Models.Evaluations.CartographieContent.NiveauLabel(niveau);
            var lecture     = Models.Evaluations.CartographieContent.LectureEmotionnelle(niveau);
            var itemLines   = string.Join("\n", seg.Items.Select(i => $"{(i.IsChecked ? "✓" : "✗")} {i.Affirmation}"));
            return $"=== DONNÉES D'ÉVALUATION — RÉGULATION ÉMOTIONNELLE ===\nScore : {seg.Score}/6 à {ageTxt} ans → niveau « {niveauLabel} »\n{itemLines}\nLecture canonique : {lecture}\n\n" +
                "Rédige UNIQUEMENT la sphère Régulation émotionnelle :\n\n**Observations**\n- 2 ou 3 puces COURTES. Appuie-toi D'ABORD sur les items ✓/✗.\n- Reste STRICTEMENT dans la régulation émotionnelle : nommer les émotions, tolérance à la frustration, retour au calme.\n- NE MENTIONNE PAS les autres sphères.\n\n" +
                "**Niveau clinique** : 1 SEULE phrase. Format : `Mot-clé (qualifier court sur la régulation).`\n\nCommence directement par `**Observations**`.";
        }

        private static string BuildSphere3Instruction(CartographieEnfant carto, string ageTxt)
        {
            var seg = carto.Langage;
            var niveau = Services.Evaluations.CartographieScoringService.Calculer(seg.Score, carto.AgeAuMomentDeLaSaisie);
            if (!niveau.HasValue)
                return "L'étape 3 non clôturée ou âge hors fourchette. Écris : `**Observations** : non disponibles.` puis `**Niveau clinique** : Non évalué.`";
            var niveauLabel = Models.Evaluations.CartographieContent.NiveauLabel(niveau);
            var lecture     = Models.Evaluations.CartographieContent.LectureEmotionnelle(niveau);
            var itemLines   = string.Join("\n", seg.Items.Select(i => $"{(i.IsChecked ? "✓" : "✗")} {i.Affirmation}"));
            return $"=== DONNÉES D'ÉVALUATION — LANGAGE & COMMUNICATION ===\nScore : {seg.Score}/6 à {ageTxt} ans → niveau « {niveauLabel} »\n{itemLines}\nLecture canonique : {lecture}\n\n" +
                "Rédige UNIQUEMENT la sphère Langage :\n\n**Observations**\n- 2 ou 3 puces COURTES. Appuie-toi D'ABORD sur les items ✓/✗.\n- Reste STRICTEMENT dans le langage : expression verbale, compréhension, vocabulaire, écoute.\n- NE MENTIONNE PAS les autres sphères.\n\n" +
                "**Niveau clinique** : 1 SEULE phrase. Format : `Mot-clé (qualifier court sur le langage).`\n\nCommence directement par `**Observations**`.";
        }

        private static string BuildSphere4Instruction(CartographieEnfant carto, string ageTxt)
        {
            var temp = carto.Temperament;
            if (!temp.IsRenseigne)
                return "Le profil de tempérament n'a pas été renseigné. Écris : `**Observations** : non disponibles (profil tempérament non renseigné).`";
            return $"=== DONNÉES D'ÉVALUATION — TEMPÉRAMENT ===\nÂge : {ageTxt} ans\n" +
                $"- Niveau d'activité : {temp.NiveauActivite}/5\n- Régularité : {temp.Regularite}/5\n- Réactivité sensorielle : {temp.ReactiviteSensorielle}/5\n" +
                $"- Intensité émotionnelle : {temp.IntensiteEmotionnelle}/5\n- Adaptabilité : {temp.Adaptabilite}/5\n- Temps de réaction : {temp.TempsDeReaction}/5\n\n" +
                "IMPORTANT : Le tempérament n'est PAS pathologique. Rédige UNIQUEMENT la sphère Tempérament :\n\n**Observations**\n\n**Profil global**\n2 à 3 phrases sur la forme tempéramentielle globale.\n\n" +
                "**Points d'appui**\n- 1 ou 2 puces : traits favorables.\n\n**Points d'attention**\n- 1 ou 2 puces : axes extrêmes avec piste d'adaptation.\n\nNE MENTIONNE PAS les autres sphères.\nCommence par `**Observations**`.";
        }

        private static string BuildSphere5Instruction(CartographieEnfant carto, string ageTxt)
        {
            var p = carto.Psychomotricite;
            if (!p.IsRenseigne)
                return "Le profil psychomoteur n'a pas été renseigné. Écris : `**Observations** : non disponibles (profil psychomoteur non renseigné).`";
            return $"=== DONNÉES D'ÉVALUATION — PSYCHOMOTRICITÉ ===\nÂge : {ageTxt} ans\n" +
                $"- Motricité globale : {p.MotriciteGlobale}/5\n- Motricité fine : {p.MotriciteFine}/5\n- Tonus : {p.Tonus}/5\n" +
                $"- Dextérité : {p.Dexterite}/5\n- Coordination : {p.Coordination}/5\n- Impulsivité motrice : {p.ImpulsiviteMotrice}/5\n\n" +
                "Rédige UNIQUEMENT la sphère Psychomotricité :\n\n**Observations**\n\n**Profil global**\n2 à 3 phrases sur le profil moteur.\n\n" +
                "**Points d'appui**\n- 1 ou 2 puces : axes moteurs favorables.\n\n**Points d'attention**\n- 1 ou 2 puces : axes en difficulté avec piste concrète.\n\nNE MENTIONNE PAS les autres sphères.\nCommence par `**Observations**`.";
        }

        private static string BuildSphere6Instruction(CartographieEnfant carto, string ageTxt)
        {
            var seg = carto.Imaginaire;
            var niveau = Services.Evaluations.CartographieScoringService.Calculer(seg.Score, carto.AgeAuMomentDeLaSaisie);
            if (!niveau.HasValue)
                return "L'étape 3 non clôturée ou âge hors fourchette. Écris : `**Observations** : non disponibles.` puis `**Niveau clinique** : Non évalué.`";
            var niveauLabel = Models.Evaluations.CartographieContent.NiveauLabel(niveau);
            var lecture     = Models.Evaluations.CartographieContent.LectureEmotionnelle(niveau);
            var itemLines   = string.Join("\n", seg.Items.Select(i => $"{(i.IsChecked ? "✓" : "✗")} {i.Affirmation}"));
            return $"=== DONNÉES D'ÉVALUATION — IMAGINATION & JEU ===\nScore : {seg.Score}/6 à {ageTxt} ans → niveau « {niveauLabel} »\n{itemLines}\nLecture canonique : {lecture}\n\n" +
                "Rédige UNIQUEMENT la sphère Imagination & Jeu :\n\n**Observations**\n- 2 ou 3 puces COURTES. Appuie-toi D'ABORD sur les items ✓/✗.\n- Reste STRICTEMENT dans l'imaginaire : inventer, jouer « comme si », se réconforter via l'imaginaire.\n- NE MENTIONNE PAS les autres sphères.\n\n" +
                "**Niveau clinique** : 1 SEULE phrase. Format : `Mot-clé (qualifier court).`\n\nCommence directement par `**Observations**`.";
        }

        private static string BuildSphere7Instruction(CartographieEnfant carto, string ageTxt)
        {
            var seg = carto.Pensee;
            var niveau = Services.Evaluations.CartographieScoringService.Calculer(seg.Score, carto.AgeAuMomentDeLaSaisie);
            if (!niveau.HasValue)
                return "L'étape 3 non clôturée ou âge hors fourchette. Écris : `**Observations** : non disponibles.` puis `**Niveau clinique** : Non évalué.`";
            var niveauLabel = Models.Evaluations.CartographieContent.NiveauLabel(niveau);
            var lecture     = Models.Evaluations.CartographieContent.LectureEmotionnelle(niveau);
            var itemLines   = string.Join("\n", seg.Items.Select(i => $"{(i.IsChecked ? "✓" : "✗")} {i.Affirmation}"));
            return $"=== DONNÉES D'ÉVALUATION — PENSÉE & APPRENTISSAGES ===\nScore : {seg.Score}/6 à {ageTxt} ans → niveau « {niveauLabel} »\n{itemLines}\nLecture canonique : {lecture}\n\n" +
                "Rédige UNIQUEMENT la sphère Pensée & Apprentissages :\n\n**Observations**\n- 2 ou 3 puces COURTES. Appuie-toi D'ABORD sur les items ✓/✗.\n- Reste STRICTEMENT dans la pensée : curiosité, consignes, concentration, résolution de problèmes, flexibilité cognitive.\n- NE MENTIONNE PAS les autres sphères.\n\n" +
                "**Niveau clinique** : 1 SEULE phrase. Format : `Mot-clé (qualifier court).`\n\nCommence directement par `**Observations**`.";
        }

        private static string BuildSphere8Instruction(CartographieEnfant carto, string ageTxt)
        {
            var a = carto.Attention;
            if (!a.IsRenseigne)
                return "Le profil attentionnel n'a pas été renseigné. Écris : `**Observations** : non disponibles (profil attentionnel non renseigné).`";
            return $"=== DONNÉES D'ÉVALUATION — ATTENTION & FONCTIONS EXÉCUTIVES ===\nÂge : {ageTxt} ans\n" +
                $"- Attention soutenue : {a.AttentionSoutenue}/5\n- Attention sélective : {a.AttentionSelective}/5\n- Attention divisée : {a.AttentionDivisee}/5\n" +
                $"- Inhibition : {a.Inhibition}/5\n- Planification : {a.Planification}/5\n- Flexibilité attentionnelle : {a.FlexibiliteAttentionnelle}/5\n\n" +
                "Rédige UNIQUEMENT la sphère Attention & Fonctions exécutives :\n\n**Observations**\n\n**Profil global**\n2 à 3 phrases sur le profil attentionnel.\n\n" +
                "**Points d'appui**\n- 1 ou 2 puces : axes attentionnels favorables.\n\n**Points d'attention**\n- 1 ou 2 puces : axes fragilisés avec piste d'adaptation.\n\nNE MENTIONNE PAS les autres sphères.\nCommence par `**Observations**`.";
        }

        // ── Branche Éducative — génération par feuille ──────────────────────

        /// <summary>
        /// Génère les observations LLM pour une feuille de la Cartographie de l'environnement.
        /// feuilleIdx : 1=Famille, 2=École & Pairs, 3=Écrans & Médias, 4=Valeurs, 5=Cadre Éducatif.
        /// </summary>
        public async Task SuggestEnvEduFeuilleAsync(
            int feuilleIdx,
            DossierReading reading,
            Action<string> onSectionReady,
            CancellationToken ct = default)
        {
            var context = reading.RenderForLlm();
            if (string.IsNullOrWhiteSpace(context)) { onSectionReady("(Aucun contenu source disponible.)"); return; }

            var blocRef   = new RestitutionBloc("env_edu_f1", "Branche Éducative", 16, "clinique");
            var sysPrompt = BuildSystemPrompt(blocRef);
            var carto     = reading.LatestCartographieEnvironnement;
            var instr     = BuildEnvFeuilleInstruction(feuilleIdx, carto);
            var userPrompt = BuildSubsectionPrompt(context, instr, "clinique");
            var messages  = new List<(string role, string content)> { ("user", userPrompt) };
            var result    = await _llmService.ChatAsync(sysPrompt, messages, 500, ct);
            onSectionReady(result.success ? result.result.Trim() : $"(Erreur : {result.error})");
        }

        /// <summary>
        /// Génère la lecture globale (synthèse) qui croise les 5 feuilles de la Branche Éducative.
        /// </summary>
        public async Task SuggestEnvEduGlobalAsync(
            DossierReading reading,
            Action<string> onSectionReady,
            CancellationToken ct = default)
        {
            var context = reading.RenderForLlm();
            if (string.IsNullOrWhiteSpace(context)) { onSectionReady("(Aucun contenu source disponible.)"); return; }

            var carto = reading.LatestCartographieEnvironnement;
            var instr = BuildEnvGlobalInstruction(carto);
            var blocRef   = new RestitutionBloc("env_edu_global", "Branche Éducative", 21, "clinique");
            var sysPrompt = BuildSystemPrompt(blocRef);
            var userPrompt = BuildSubsectionPrompt(context, instr, "clinique");
            var messages  = new List<(string role, string content)> { ("user", userPrompt) };
            var result    = await _llmService.ChatAsync(sysPrompt, messages, 600, ct);
            onSectionReady(result.success ? result.result.Trim() : $"(Erreur : {result.error})");
        }

        // ── Synthèse Globale et Diagnostique ────────────────────────────────

        /// <summary>
        /// Section 1 — Compréhension globale : 4-5 phrases en prose synthétisant l'état
        /// global de l'enfant, le moteur des difficultés et la dynamique évolutive.
        /// Source prioritaire : BilanFinal.SyntheseIntegrative + cartos enfant + environnement.
        /// </summary>
        public async Task SuggestSyntheseDiagS1Async(
            DossierReading reading,
            Action<string> onSectionReady,
            CancellationToken ct = default)
        {
            var context = reading.RenderForLlm();
            if (string.IsNullOrWhiteSpace(context)) { onSectionReady("(Aucun contenu source disponible.)"); return; }

            var blocRef    = new RestitutionBloc("synthese_diag_s1", "Compréhension globale", 22, "clinique");
            var sysPrompt  = BuildSystemPrompt(blocRef);
            var instr      = BuildSyntheseDiagS1Instruction(reading.LatestBilanFinal);
            var userPrompt = BuildSubsectionPrompt(context, instr, "clinique");
            var messages   = new List<(string role, string content)> { ("user", userPrompt) };
            var result     = await _llmService.ChatAsync(sysPrompt, messages, 500, ct);
            onSectionReady(result.success ? result.result.Trim() : $"(Erreur : {result.error})");
        }

        private static string BuildSyntheseDiagS1Instruction(Models.Evaluations.BilanFinal? bilan)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Rédige la SECTION 1 — COMPRÉHENSION GLOBALE DE LA SITUATION.");
            sb.AppendLine();
            sb.AppendLine("FORMAT : 4 à 5 phrases en prose continue (pas de liste, pas de titre, pas de sous-titre).");
            sb.AppendLine("TON : clinique sobre, troisième personne (« L'état de [prénom] révèle... », « Son fonctionnement... »).");
            sb.AppendLine();
            sb.AppendLine("COUVRIR DANS L'ORDRE :");
            sb.AppendLine("  1. L'état global de l'enfant — fonctionnement interne, ressources et fragilités principales.");
            sb.AppendLine("  2. Le moteur principal des difficultés — lien entre vécu interne et contexte environnemental.");
            sb.AppendLine("  3. La dynamique évolutive — pronostic bienveillant mais réaliste, leviers disponibles.");
            sb.AppendLine();
            sb.AppendLine("RÈGLES :");
            sb.AppendLine("  • Ne pas nommer explicitement les diagnostics ici (réservé Section 2).");
            sb.AppendLine("  • Ne pas commencer par « L'enfant » — varier les amorces.");
            sb.AppendLine("  • Pas de formule vague (« globalement », « de manière générale »).");

            if (bilan != null)
            {
                if (!string.IsNullOrWhiteSpace(bilan.SyntheseIntegrative))
                {
                    sb.AppendLine();
                    sb.AppendLine("SYNTHÈSE INTÉGRATIVE déjà rédigée par le médecin (à reformuler pour la restitution) :");
                    sb.AppendLine(bilan.SyntheseIntegrative.Trim());
                }
                if (bilan.DiagnosticsRetenus.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("DIAGNOSTICS RETENUS (contexte, ne pas nommer dans la section) :");
                    foreach (var d in bilan.DiagnosticsRetenus)
                        sb.AppendLine($"  • {d.Value}");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Section 2+3 — Diagnostics retenus + Éléments en faveur.
        /// Génère du JSON structuré : [{label, certitude, elements[]}] — 1 objet par diagnostic.
        /// Le renderer parse ce JSON pour construire les colonnes de la page.
        /// </summary>
        public async Task SuggestSyntheseDiagS2Async(
            DossierReading reading,
            Action<string> onSectionReady,
            CancellationToken ct = default)
        {
            var context = reading.RenderForLlm();
            if (string.IsNullOrWhiteSpace(context)) { onSectionReady("[]"); return; }

            var blocRef    = new RestitutionBloc("synthese_diag_s2", "Diagnostics retenus", 23, "clinique");
            var sysPrompt  = BuildSystemPrompt(blocRef);
            var instr      = BuildSyntheseDiagS2Instruction(reading.LatestBilanFinal);
            var userPrompt = BuildSubsectionPrompt(context, instr, "clinique");
            var messages   = new List<(string role, string content)> { ("user", userPrompt) };
            var result     = await _llmService.ChatAsync(sysPrompt, messages, 800, ct);
            onSectionReady(result.success ? result.result.Trim() : $"(Erreur : {result.error})");
        }

        private static string BuildSyntheseDiagS2Instruction(Models.Evaluations.BilanFinal? bilan)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Génère les SECTIONS 2+3 — DIAGNOSTICS RETENUS ET ÉLÉMENTS CLINIQUES EN FAVEUR.");
            sb.AppendLine();
            sb.AppendLine("FORMAT STRICT — JSON valide uniquement, aucun texte avant ni après :");
            sb.AppendLine("[");
            sb.AppendLine("  {");
            sb.AppendLine("    \"label\": \"Nom exact du diagnostic\",");
            sb.AppendLine("    \"certitude\": \"Élevée\",");
            sb.AppendLine("    \"elements\": [\"Élément clinique court 1\", \"Élément 2\", \"Élément 3\"]");
            sb.AppendLine("  }");
            sb.AppendLine("]");
            sb.AppendLine();
            sb.AppendLine("RÈGLES :");
            sb.AppendLine("  • certitude : valeur parmi « Hypothèse », « Modérée », « Élevée », « Très élevée »");
            sb.AppendLine("  • elements : 4 à 6 items max, chacun < 10 mots, concis et clinique");
            sb.AppendLine("  • Entre 1 et 4 diagnostics maximum");
            sb.AppendLine("  • Ne pas inventer de diagnostic absent du dossier");

            if (bilan != null)
            {
                if (bilan.DiagnosticsRetenus.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("DIAGNOSTICS RETENUS PAR LE MÉDECIN (à utiliser en priorité) :");
                    foreach (var d in bilan.DiagnosticsRetenus)
                        sb.AppendLine($"  • {d.Value}");
                    sb.AppendLine($"  Certitude globale notée : {bilan.Certitude}");
                }
                if (bilan.ElementsEnFaveur.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("ÉLÉMENTS EN FAVEUR NOTÉS (à répartir par diagnostic dans elements[]) :");
                    foreach (var e in bilan.ElementsEnFaveur)
                        sb.AppendLine($"  • {e.Value}");
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Section 4 — Diagnostics différentiels écartés.
        /// Génère du JSON structuré : [{label, conclusion, arguments[]}] — 1 à 3 items max.
        /// </summary>
        public async Task SuggestSyntheseDiagS3Async(
            DossierReading reading,
            Action<string> onSectionReady,
            CancellationToken ct = default)
        {
            var context = reading.RenderForLlm();
            if (string.IsNullOrWhiteSpace(context)) { onSectionReady("[]"); return; }

            var blocRef    = new RestitutionBloc("synthese_diag_s3", "Diagnostics différentiels écartés", 24, "clinique");
            var sysPrompt  = BuildSystemPrompt(blocRef);
            var instr      = BuildSyntheseDiagS3Instruction(reading.LatestBilanFinal);
            var userPrompt = BuildSubsectionPrompt(context, instr, "clinique");
            var messages   = new List<(string role, string content)> { ("user", userPrompt) };
            var result     = await _llmService.ChatAsync(sysPrompt, messages, 700, ct);
            onSectionReady(result.success ? result.result.Trim() : $"(Erreur : {result.error})");
        }

        private static string BuildSyntheseDiagS3Instruction(Models.Evaluations.BilanFinal? bilan)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Génère la SECTION 4 — DIAGNOSTICS DIFFÉRENTIELS ÉCARTÉS.");
            sb.AppendLine();
            sb.AppendLine("FORMAT STRICT — JSON valide uniquement, aucun texte avant ni après :");
            sb.AppendLine("[");
            sb.AppendLine("  {");
            sb.AppendLine("    \"label\": \"Nom du diagnostic écarté\",");
            sb.AppendLine("    \"conclusion\": \"Une phrase courte (< 20 mots) expliquant pourquoi il est écarté.\",");
            sb.AppendLine("    \"arguments\": [\"Argument clinique 1\", \"Argument 2\", \"Argument 3\"]");
            sb.AppendLine("  }");
            sb.AppendLine("]");
            sb.AppendLine();
            sb.AppendLine("RÈGLES :");
            sb.AppendLine("  • Entre 1 et 3 diagnostics maximum (ne jamais dépasser 3)");
            sb.AppendLine("  • conclusion : 1 phrase < 20 mots, ton clinique sobre");
            sb.AppendLine("  • arguments : 3 à 5 items, chacun < 12 mots");
            sb.AppendLine("  • Ne pas inventer de diagnostic absent du dossier");

            if (bilan != null && bilan.DiagnosticsEcartes.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("DIAGNOSTICS ÉCARTÉS PAR LE MÉDECIN (à utiliser en priorité) :");
                foreach (var d in bilan.DiagnosticsEcartes)
                {
                    sb.Append($"  • {d.Label}");
                    if (!string.IsNullOrWhiteSpace(d.Motif))
                        sb.Append($" — {d.Motif}");
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Section 5 — Intégration des cartographies.
        /// Génère du JSON {enfant:{forces,fragilites}, environnement:{protecteurs,aggravants}}.
        /// </summary>
        public async Task SuggestSyntheseDiagS4Async(
            DossierReading reading,
            Action<string> onSectionReady,
            CancellationToken ct = default)
        {
            var context = reading.RenderForLlm();
            if (string.IsNullOrWhiteSpace(context)) { onSectionReady("{}"); return; }

            var blocRef    = new RestitutionBloc("synthese_diag_s4", "Intégration cartographies", 25, "clinique");
            var sysPrompt  = BuildSystemPrompt(blocRef);
            var instr      = BuildSyntheseDiagS4Instruction(reading);
            var userPrompt = BuildSubsectionPrompt(context, instr, "clinique");
            var messages   = new List<(string role, string content)> { ("user", userPrompt) };
            var result     = await _llmService.ChatAsync(sysPrompt, messages, 700, ct);
            onSectionReady(result.success ? result.result.Trim() : $"(Erreur : {result.error})");
        }

        private static string BuildSyntheseDiagS4Instruction(DossierReading reading)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Génère la SECTION 5 — INTÉGRATION DES CARTOGRAPHIES.");
            sb.AppendLine();
            sb.AppendLine("FORMAT STRICT — JSON valide uniquement, aucun texte avant ni après :");
            sb.AppendLine("{");
            sb.AppendLine("  \"enfant\": {");
            sb.AppendLine("    \"forces\": [\"Force ou ressource 1\", \"Force 2\"],");
            sb.AppendLine("    \"fragilites\": [\"Fragilité principale 1\", \"Fragilité 2\"]");
            sb.AppendLine("  },");
            sb.AppendLine("  \"environnement\": {");
            sb.AppendLine("    \"protecteurs\": [\"Facteur protecteur 1\", \"Facteur 2\"],");
            sb.AppendLine("    \"aggravants\": [\"Facteur aggravant 1\", \"Facteur 2\"]");
            sb.AppendLine("  }");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("RÈGLES :");
            sb.AppendLine("  • Chaque liste : 3 à 6 items, chacun < 12 mots");
            sb.AppendLine("  • Basé strictement sur les cartographies disponibles dans le dossier");
            sb.AppendLine("  • forces/fragilites = ce que révèle la cartographie de l'enfant (sphères 1-8)");
            sb.AppendLine("  • protecteurs/aggravants = ce que révèle la cartographie de l'environnement");
            sb.AppendLine("  • Ton sobre et clinique, pas de jugement sur les parents");

            bool hasCarto = reading.LatestCartographieEnfant != null || reading.LatestCartographieEnvironnement != null;
            if (!hasCarto)
                sb.AppendLine("  • Aucune cartographie disponible — infère depuis les notes et évaluations.");

            return sb.ToString();
        }

        /// <summary>
        /// Section 6 — Conclusion intégrative. Prose clinique, 4-6 phrases.
        /// </summary>
        public async Task SuggestSyntheseDiagS5Async(
            DossierReading reading,
            Action<string> onSectionReady,
            CancellationToken ct = default)
        {
            var context = reading.RenderForLlm();
            if (string.IsNullOrWhiteSpace(context)) { onSectionReady("(Aucun contenu source disponible.)"); return; }

            var blocRef    = new RestitutionBloc("synthese_diag_s5", "Conclusion intégrative", 26, "clinique");
            var sysPrompt  = BuildSystemPrompt(blocRef);
            var instr      = BuildSyntheseDiagS5Instruction(reading.LatestBilanFinal);
            var userPrompt = BuildSubsectionPrompt(context, instr, "clinique");
            var messages   = new List<(string role, string content)> { ("user", userPrompt) };
            var result     = await _llmService.ChatAsync(sysPrompt, messages, 500, ct);
            onSectionReady(result.success ? result.result.Trim() : $"(Erreur : {result.error})");
        }

        private static string BuildSyntheseDiagS5Instruction(Models.Evaluations.BilanFinal? bilan)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Rédige la SECTION 6 — CONCLUSION INTÉGRATIVE.");
            sb.AppendLine();
            sb.AppendLine("FORMAT : 3 à 5 phrases en prose continue (pas de liste, pas de titre).");
            sb.AppendLine("TON : clinique sobre, purement synthétique et descriptif.");
            sb.AppendLine();
            sb.AppendLine("COUVRIR UNIQUEMENT :");
            sb.AppendLine("  1. Synthèse du tableau clinique global — ce qui a été observé et retenu.");
            sb.AppendLine("  2. Cohérence entre les sources (cartographies, bilan, notes) qui valide le diagnostic.");
            sb.AppendLine("  3. Degré de certitude clinique global et ce qui pourrait encore évoluer ou rester à préciser.");
            sb.AppendLine();
            sb.AppendLine("RÈGLES STRICTES :");
            sb.AppendLine("  • AUCUN axe de travail, AUCUNE piste thérapeutique, AUCUN projet de soin — il y a des sections dédiées pour cela.");
            sb.AppendLine("  • AUCUNE recommandation (TCC, suivi, rééducation, etc.).");
            sb.AppendLine("  • Rester sur le plan diagnostique et intégratif uniquement.");
            sb.AppendLine("  • Ne pas ouvrir de nouvelles hypothèses diagnostiques.");
            sb.AppendLine("  • Pas de formule vague (« globalement », « de manière générale »).");
            sb.AppendLine("  • Ne pas commencer par « En conclusion ».");

            if (bilan != null && !string.IsNullOrWhiteSpace(bilan.SyntheseIntegrative))
            {
                sb.AppendLine();
                sb.AppendLine("SYNTHÈSE INTÉGRATIVE du médecin (base principale — à reformuler) :");
                sb.AppendLine(bilan.SyntheseIntegrative.Trim());
            }

            return sb.ToString();
        }

        // ── Projet Thérapeutique ────────────────────────────────────────────

        /// <summary>
        /// 7.1 — Prise en charge médicale. JSON structuré avec 6 clés.
        /// </summary>
        public async Task SuggestPtS1Async(
            DossierReading reading,
            Action<string> onSectionReady,
            CancellationToken ct = default)
        {
            var context = reading.RenderForLlm();
            if (string.IsNullOrWhiteSpace(context)) { onSectionReady("{}"); return; }

            var blocRef    = new RestitutionBloc("pt_s1", "Prise en charge médicale", 27, "clinique");
            var sysPrompt  = BuildSystemPrompt(blocRef);
            var instr      = BuildPtS1Instruction(reading);
            var userPrompt = BuildSubsectionPrompt(context, instr, "clinique");
            var messages   = new List<(string role, string content)> { ("user", userPrompt) };
            var result     = await _llmService.ChatAsync(sysPrompt, messages, 900, ct);
            onSectionReady(result.success ? result.result.Trim() : $"(Erreur : {result.error})");
        }

        private static string BuildPtS1Instruction(DossierReading reading)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Génère la section 7.1 — PRISE EN CHARGE MÉDICALE du projet thérapeutique.");
            sb.AppendLine();
            sb.AppendLine("FORMAT STRICT — JSON valide uniquement, aucun texte avant ni après :");
            sb.AppendLine("{");
            sb.AppendLine("  \"intro\": \"1 phrase décrivant le rôle de la prise en charge médicale pour cet enfant.\",");
            sb.AppendLine("  \"objectifs\": [\"Objectif 1\", \"Objectif 2\", \"Objectif 3\"],");
            sb.AppendLine("  \"traitement\": {");
            sb.AppendLine("    \"situationActuelle\": \"Situation médicamenteuse actuelle — 1 phrase.\",");
            sb.AppendLine("    \"propositions\": [\"Traitement proposé : ...\", \"Objectif attendu : ...\", \"Surveillance prévue : ...\"]");
            sb.AppendLine("  },");
            sb.AppendLine("  \"bilans\": {");
            sb.AppendLine("    \"realises\": [\"Bilan déjà réalisé 1\", \"Bilan 2\"],");
            sb.AppendLine("    \"aEnvisager\": [\"À envisager 1\", \"À envisager 2\"]");
            sb.AppendLine("  },");
            sb.AppendLine("  \"surveillance\": [\"Point de surveillance 1\", \"Point 2\", \"Point 3\"],");
            sb.AppendLine("  \"suivi\": [\"Modalité de suivi 1\", \"Modalité 2\", \"Modalité 3\"],");
            sb.AppendLine("  \"engagement\": \"1 phrase d'engagement médical sobre et bienveillant.\"");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("RÈGLES :");
            sb.AppendLine("  • objectifs : 3 à 4 items, chacun < 15 mots");
            sb.AppendLine("  • traitement.propositions vide [] si aucun traitement médicamenteux n'est indiqué");
            sb.AppendLine("  • traitement.situationActuelle : honnête — préciser si traitement en cours ou absent");
            sb.AppendLine("  • bilans.realises : ce qui a déjà été fait (entretien, bilans, évaluations)");
            sb.AppendLine("  • bilans.aEnvisager : ce qui pourrait être utile selon le tableau clinique");
            sb.AppendLine("  • surveillance : 3 à 5 points de vigilance clinique < 12 mots chacun");
            sb.AppendLine("  • suivi : 3 à 4 modalités pratiques (délai + action), < 10 mots chacune");
            sb.AppendLine("  • NE PAS proposer de projet thérapeutique psychologique ou scolaire ici — section médicale uniquement");

            if (reading.LatestBilanFinal != null)
            {
                var b = reading.LatestBilanFinal;
                if (b.DiagnosticsRetenus.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("DIAGNOSTICS RETENUS (ancrage pour les objectifs médicaux) :");
                    foreach (var d in b.DiagnosticsRetenus)
                        sb.AppendLine($"  • {d.Value}");
                }
            }

            if (!string.IsNullOrWhiteSpace(reading.ProjetTherapeutique))
            {
                sb.AppendLine();
                sb.AppendLine("PROJET THÉRAPEUTIQUE EXISTANT (utiliser la section médicale comme référence) :");
                // Limiter à 600 caractères pour éviter de surcharger le prompt
                var pt = reading.ProjetTherapeutique.Trim();
                sb.AppendLine(pt.Length > 600 ? pt.Substring(0, 600) + "…" : pt);
            }

            return sb.ToString();
        }

        /// <summary>
        /// 7.2 — Accompagnement psychologique. JSON structuré avec 9 clés.
        /// </summary>
        public async Task SuggestPtS2Async(
            DossierReading reading,
            Action<string> onSectionReady,
            CancellationToken ct = default)
        {
            var context = reading.RenderForLlm();
            if (string.IsNullOrWhiteSpace(context)) { onSectionReady("{}"); return; }
            var blocRef    = new RestitutionBloc("pt_s2", "Accompagnement psychologique", 28, "clinique");
            var sysPrompt  = BuildSystemPrompt(blocRef);
            var instr      = BuildPtS2Instruction(reading);
            var userPrompt = BuildSubsectionPrompt(context, instr, "clinique");
            var messages   = new List<(string role, string content)> { ("user", userPrompt) };
            var result     = await _llmService.ChatAsync(sysPrompt, messages, 900, ct);
            onSectionReady(result.success ? result.result.Trim() : $"(Erreur : {result.error})");
        }

        private static string BuildPtS2Instruction(DossierReading reading)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Génère la section 7.2 — ACCOMPAGNEMENT PSYCHOLOGIQUE du projet thérapeutique.");
            sb.AppendLine();
            sb.AppendLine("FORMAT STRICT — JSON valide uniquement, aucun texte avant ni après :");
            sb.AppendLine("{");
            sb.AppendLine("  \"intro\": \"1 phrase décrivant le rôle de l'accompagnement psychologique pour cet enfant.\",");
            sb.AppendLine("  \"objectifs\": [\"Objectif 1\", \"Objectif 2\", \"Objectif 3\"],");
            sb.AppendLine("  \"resultatsAttendus\": [\"Résultat attendu 1\", \"Résultat 2\", \"Résultat 3\"],");
            sb.AppendLine("  \"modalites\": [\"Psychothérapie individuelle\", \"Soutien émotionnel et cognitif\"],");
            sb.AppendLine("  \"pointsTravail\": [\"Point de travail 1\", \"Point 2\", \"Point 3\"],");
            sb.AppendLine("  \"outilsUtilises\": [\"Outil ou approche 1\", \"Outil 2\", \"Outil 3\"],");
            sb.AppendLine("  \"surveillance\": [\"Indicateur à surveiller 1\", \"Indicateur 2\", \"Indicateur 3\"],");
            sb.AppendLine("  \"indicateursPositifs\": [\"Signe positif attendu 1\", \"Signe 2\", \"Signe 3\"],");
            sb.AppendLine("  \"suivi\": [\"Modalité de suivi 1\", \"Modalité 2\", \"Modalité 3\"],");
            sb.AppendLine("  \"engagement\": \"1 phrase d'engagement sobre et bienveillant.\"");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("RÈGLES :");
            sb.AppendLine("  • objectifs : 3 à 4 items, chacun < 15 mots, centrés sur le développement émotionnel/relationnel");
            sb.AppendLine("  • resultatsAttendus : 3 à 4 items, observables et concrets (comportements, relations, régulation)");
            sb.AppendLine("  • modalites : 2 à 4 types de dispositifs psych. pertinents pour ce tableau clinique");
            sb.AppendLine("  • pointsTravail : 3 à 5 axes thérapeutiques prioritaires, < 12 mots chacun");
            sb.AppendLine("  • outilsUtilises : approches/outils cliniques (TCC, EMDR, jeu symbolique, etc.) adaptés à l'âge");
            sb.AppendLine("  • surveillance : 3 à 5 points de vigilance clinique sur l'évolution psychologique");
            sb.AppendLine("  • indicateursPositifs : 3 à 4 signaux concrets d'amélioration attendus");
            sb.AppendLine("  • suivi : 3 à 4 modalités pratiques (fréquence + action), < 10 mots chacune");
            sb.AppendLine("  • NE PAS proposer de traitement médicamenteux ici — section psychologique uniquement");

            if (reading.LatestBilanFinal != null)
            {
                var b = reading.LatestBilanFinal;
                if (b.DiagnosticsRetenus.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("DIAGNOSTICS RETENUS (ancrage pour les objectifs psychologiques) :");
                    foreach (var d in b.DiagnosticsRetenus)
                        sb.AppendLine($"  • {d.Value}");
                }
            }

            if (!string.IsNullOrWhiteSpace(reading.ProjetTherapeutique))
            {
                sb.AppendLine();
                sb.AppendLine("PROJET THÉRAPEUTIQUE EXISTANT (utiliser la section psychologique comme référence) :");
                var pt = reading.ProjetTherapeutique.Trim();
                sb.AppendLine(pt.Length > 600 ? pt.Substring(0, 600) + "…" : pt);
            }

            return sb.ToString();
        }

        /// <summary>
        /// 7.3 — Soutien développemental. JSON structuré avec 7 clés.
        /// </summary>
        public async Task SuggestPtS3Async(
            DossierReading reading,
            Action<string> onSectionReady,
            CancellationToken ct = default)
        {
            var context = reading.RenderForLlm();
            if (string.IsNullOrWhiteSpace(context)) { onSectionReady("{}"); return; }
            var blocRef    = new RestitutionBloc("pt_s3", "Soutien développemental", 29, "clinique");
            var sysPrompt  = BuildSystemPrompt(blocRef);
            var instr      = BuildPtS3Instruction(reading);
            var userPrompt = BuildSubsectionPrompt(context, instr, "clinique");
            var messages   = new List<(string role, string content)> { ("user", userPrompt) };
            var result     = await _llmService.ChatAsync(sysPrompt, messages, 900, ct);
            onSectionReady(result.success ? result.result.Trim() : $"(Erreur : {result.error})");
        }

        private static string BuildPtS3Instruction(DossierReading reading)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Génère la section 7.3 — SOUTIEN DÉVELOPPEMENTAL du projet thérapeutique.");
            sb.AppendLine();
            sb.AppendLine("FORMAT STRICT — JSON valide uniquement, aucun texte avant ni après :");
            sb.AppendLine("{");
            sb.AppendLine("  \"intro\": \"1 phrase décrivant l'objectif global du soutien développemental pour cet enfant.\",");
            sb.AppendLine("  \"objectifs\": [\"Objectif 1\", \"Objectif 2\", \"Objectif 3\"],");
            sb.AppendLine("  \"interventions\": [\"Orthophonie (si nécessaire)\", \"Psychomotricité\", \"Neuropsychologie\"],");
            sb.AppendLine("  \"axesPrioritaires\": [\"Axe prioritaire 1 (domaine + objectif)\", \"Axe 2\", \"Axe 3\"],");
            sb.AppendLine("  \"ressourcesEnfant\": [\"Ressource ou point fort 1\", \"Ressource 2\", \"Ressource 3\"],");
            sb.AppendLine("  \"indicateursEvolution\": [\"Indicateur attendu 1\", \"Indicateur 2\", \"Indicateur 3\"],");
            sb.AppendLine("  \"reevaluation\": [\"Modalité de réévaluation 1\", \"Modalité 2\", \"Modalité 3\"],");
            sb.AppendLine("  \"engagement\": \"1 phrase d'engagement sobre et bienveillant.\"");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("RÈGLES :");
            sb.AppendLine("  • objectifs : 4 à 5 items couvrant les sphères développementales fragilisées, < 15 mots chacun");
            sb.AppendLine("  • interventions : 2 à 4 spécialités pertinentes selon le tableau clinique (pas toutes systématiques)");
            sb.AppendLine("  • axesPrioritaires : 2 à 3 axes issus des évaluations (sphère + objectif clinique), < 20 mots chacun");
            sb.AppendLine("  • ressourcesEnfant : 4 à 5 points forts observés chez l'enfant (appuis thérapeutiques)");
            sb.AppendLine("  • indicateursEvolution : 4 à 5 signes concrets d'amélioration attendus à moyen terme");
            sb.AppendLine("  • reevaluation : 3 à 4 modalités pratiques (délai + type), < 10 mots chacune");
            sb.AppendLine("  • NE PAS proposer de traitement médicamenteux ni de suivi psy ici — section développementale uniquement");

            if (reading.LatestBilanFinal != null)
            {
                var b = reading.LatestBilanFinal;
                if (b.DiagnosticsRetenus.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("DIAGNOSTICS RETENUS (ancrage pour les axes développementaux) :");
                    foreach (var d in b.DiagnosticsRetenus)
                        sb.AppendLine($"  • {d.Value}");
                }
            }

            if (!string.IsNullOrWhiteSpace(reading.ProjetTherapeutique))
            {
                sb.AppendLine();
                sb.AppendLine("PROJET THÉRAPEUTIQUE EXISTANT (utiliser la section développementale comme référence) :");
                var pt = reading.ProjetTherapeutique.Trim();
                sb.AppendLine(pt.Length > 600 ? pt.Substring(0, 600) + "…" : pt);
            }

            return sb.ToString();
        }

        /// <summary>
        /// 7.4 — Accompagnement parental, familial et éducatif. JSON structuré avec 7 clés.
        /// </summary>
        public async Task SuggestPtS4Async(
            DossierReading reading,
            Action<string> onSectionReady,
            CancellationToken ct = default)
        {
            var context = reading.RenderForLlm();
            if (string.IsNullOrWhiteSpace(context)) { onSectionReady("{}"); return; }
            var blocRef    = new RestitutionBloc("pt_s4", "Accompagnement parental, familial et éducatif", 30, "clinique");
            var sysPrompt  = BuildSystemPrompt(blocRef);
            var instr      = BuildPtS4Instruction(reading);
            var userPrompt = BuildSubsectionPrompt(context, instr, "clinique");
            var messages   = new List<(string role, string content)> { ("user", userPrompt) };
            var result     = await _llmService.ChatAsync(sysPrompt, messages, 900, ct);
            onSectionReady(result.success ? result.result.Trim() : $"(Erreur : {result.error})");
        }

        private static string BuildPtS4Instruction(DossierReading reading)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Génère la section 7.4 — ACCOMPAGNEMENT PARENTAL, FAMILIAL ET ÉDUCATIF du projet thérapeutique.");
            sb.AppendLine();
            sb.AppendLine("FORMAT STRICT — JSON valide uniquement, aucun texte avant ni après :");
            sb.AppendLine("{");
            sb.AppendLine("  \"intro\": \"1 phrase décrivant le rôle du soutien parental et familial pour accompagner cet enfant.\",");
            sb.AppendLine("  \"objectifs\": [\"Objectif 1\", \"Objectif 2\", \"Objectif 3\"],");
            sb.AppendLine("  \"axesPrioritaires\": [\"Axe 1 (domaine + objectif concret)\", \"Axe 2\", \"Axe 3\"],");
            sb.AppendLine("  \"outils\": [\"Outil ou ressource 1\", \"Outil 2\", \"Outil 3\"],");
            sb.AppendLine("  \"forcesFamiliales\": [\"Force ou ressource familiale 1\", \"Force 2\", \"Force 3\"],");
            sb.AppendLine("  \"objectifsCourtTerme\": [\"Objectif court terme 1\", \"Objectif 2\", \"Objectif 3\"],");
            sb.AppendLine("  \"modalites\": [\"Modalité d'accompagnement 1\", \"Modalité 2\", \"Modalité 3\"],");
            sb.AppendLine("  \"engagement\": \"1 phrase d'engagement sobre et bienveillant, adressée à la famille.\"");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("RÈGLES :");
            sb.AppendLine("  • objectifs : 4 à 5 items couvrant le rôle parental, la cohésion familiale, le cadre éducatif à domicile, < 15 mots");
            sb.AppendLine("  • axesPrioritaires : 3 à 4 axes issus de la cartographie environnementale (famille, cohérence éducative, valeurs, cadre)");
            sb.AppendLine("    Exemples d'axes : « Fonction parentale — clarifier règles et attentes », « Cohérence éducative — harmoniser les réponses des adultes »");
            sb.AppendLine("    La dimension éducative = cadre familial (routines, règles, repères) — PAS les aménagements scolaires (réservés à 7.5)");
            sb.AppendLine("  • outils : 3 à 5 ressources concrètes (guidance parentale, livret parental, entretiens familiaux, ateliers thématiques)");
            sb.AppendLine("  • forcesFamiliales : 4 à 5 points d'appui observés chez la famille (motivation, liens affectifs, capacité de demande d'aide…)");
            sb.AppendLine("  • objectifsCourtTerme : 4 à 5 objectifs observables à 3 mois (comportements, routines, interactions)");
            sb.AppendLine("  • modalites : 3 à 4 modalités pratiques (fréquence + type), < 10 mots chacune");
            sb.AppendLine("  • NE PAS proposer d'aménagements scolaires ici — réservés à la section 7.5");

            if (reading.LatestBilanFinal != null)
            {
                var b = reading.LatestBilanFinal;
                if (b.DiagnosticsRetenus.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("DIAGNOSTICS RETENUS (ancrage pour les besoins familiaux) :");
                    foreach (var d in b.DiagnosticsRetenus)
                        sb.AppendLine($"  • {d.Value}");
                }
            }

            if (!string.IsNullOrWhiteSpace(reading.ProjetTherapeutique))
            {
                sb.AppendLine();
                sb.AppendLine("PROJET THÉRAPEUTIQUE EXISTANT (utiliser la section parentale/familiale comme référence) :");
                var pt = reading.ProjetTherapeutique.Trim();
                sb.AppendLine(pt.Length > 600 ? pt.Substring(0, 600) + "…" : pt);
            }

            return sb.ToString();
        }

        /// <summary>
        /// 7.5 — École et apprentissages. JSON structuré avec 7 clés.
        /// </summary>
        public async Task SuggestPtS5Async(
            DossierReading reading,
            Action<string> onSectionReady,
            CancellationToken ct = default)
        {
            var context = reading.RenderForLlm();
            if (string.IsNullOrWhiteSpace(context)) { onSectionReady("{}"); return; }
            var blocRef    = new RestitutionBloc("pt_s5", "École et apprentissages", 31, "clinique");
            var sysPrompt  = BuildSystemPrompt(blocRef);
            var instr      = BuildPtS5Instruction(reading);
            var userPrompt = BuildSubsectionPrompt(context, instr, "clinique");
            var messages   = new List<(string role, string content)> { ("user", userPrompt) };
            var result     = await _llmService.ChatAsync(sysPrompt, messages, 900, ct);
            onSectionReady(result.success ? result.result.Trim() : $"(Erreur : {result.error})");
        }

        private static string BuildPtS5Instruction(DossierReading reading)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Génère la section 7.5 — ÉCOLE ET APPRENTISSAGES du projet thérapeutique.");
            sb.AppendLine();
            sb.AppendLine("FORMAT STRICT — JSON valide uniquement, aucun texte avant ni après :");
            sb.AppendLine("{");
            sb.AppendLine("  \"intro\": \"1 phrase décrivant l'objectif global du soutien scolaire pour cet enfant.\",");
            sb.AppendLine("  \"objectifs\": [\"Objectif 1\", \"Objectif 2\", \"Objectif 3\"],");
            sb.AppendLine("  \"amenagements\": [\"Organisation et temps\", \"Environnement et cadre\", \"Apprentissages et supports\", \"Participation et sociale\"],");
            sb.AppendLine("  \"coordination\": [\"Équipe éducative\", \"Information partagée\", \"Suivi commun\", \"Lien avec les intervenants\"],");
            sb.AppendLine("  \"pointsAppui\": [\"Point d'appui scolaire 1\", \"Point 2\", \"Point 3\"],");
            sb.AppendLine("  \"indicateursEvolution\": [\"Indicateur scolaire attendu 1\", \"Indicateur 2\", \"Indicateur 3\"],");
            sb.AppendLine("  \"reevaluation\": [\"Modalité de réévaluation 1\", \"Modalité 2\", \"Modalité 3\"],");
            sb.AppendLine("  \"engagement\": \"1 phrase d'engagement sobre et bienveillant, centrée sur le parcours scolaire.\"");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("RÈGLES :");
            sb.AppendLine("  • objectifs : 4 à 5 items portant sur la réussite scolaire, la participation, la confiance en contexte scolaire, < 15 mots");
            sb.AppendLine("  • amenagements : 3 à 4 catégories d'aménagements pertinents selon le tableau clinique");
            sb.AppendLine("    Exemples : « Organisation et temps (temps supplémentaire, consignes étape par étape) »,");
            sb.AppendLine("              « Environnement et cadre (place calme, repères visuels) »,");
            sb.AppendLine("              « Apprentissages et supports (supports visuels, fractionnement des tâches) »,");
            sb.AppendLine("              « Participation et sociale (guidage des interactions, coopération en petit groupe) »");
            sb.AppendLine("  • coordination : 3 à 4 modalités de lien avec l'école (réunion équipe, cahier de liaison, suivi conjoint…)");
            sb.AppendLine("  • pointsAppui : 4 à 5 points forts de l'enfant observables en contexte scolaire");
            sb.AppendLine("  • indicateursEvolution : 4 à 5 signes concrets d'amélioration scolaire attendus à moyen terme");
            sb.AppendLine("  • reevaluation : 3 à 4 modalités pratiques (délai + type), < 10 mots chacune");
            sb.AppendLine("  • NE PAS proposer de traitement médicamenteux ni de suivi psy ici — section scolaire uniquement");

            if (reading.LatestBilanFinal != null)
            {
                var b = reading.LatestBilanFinal;
                if (b.DiagnosticsRetenus.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("DIAGNOSTICS RETENUS (ancrage pour les besoins scolaires) :");
                    foreach (var d in b.DiagnosticsRetenus)
                        sb.AppendLine($"  • {d.Value}");
                }
            }

            if (!string.IsNullOrWhiteSpace(reading.ProjetTherapeutique))
            {
                sb.AppendLine();
                sb.AppendLine("PROJET THÉRAPEUTIQUE EXISTANT (utiliser la section scolaire comme référence) :");
                var pt = reading.ProjetTherapeutique.Trim();
                sb.AppendLine(pt.Length > 600 ? pt.Substring(0, 600) + "…" : pt);
            }

            return sb.ToString();
        }

        /// <summary>
        /// 8 — Conclusion et perspectives. JSON mixte (voix clinique + chaleur livre).
        /// </summary>
        public async Task SuggestConclusionAsync(
            DossierReading reading,
            Action<string> onSectionReady,
            CancellationToken ct = default)
        {
            var context = reading.RenderForLlm();
            if (string.IsNullOrWhiteSpace(context)) { onSectionReady("{}"); return; }
            var blocRef    = new RestitutionBloc("conclusion", "Conclusion et perspectives", 32, "mixte");
            var sysPrompt  = BuildSystemPrompt(blocRef);
            var instr      = BuildConclusionInstruction(reading);
            var userPrompt = BuildSubsectionPrompt(context, instr, "mixte");
            var messages   = new List<(string role, string content)> { ("user", userPrompt) };
            var result     = await _llmService.ChatAsync(sysPrompt, messages, 900, ct);
            onSectionReady(result.success ? result.result.Trim() : $"(Erreur : {result.error})");
        }

        private static string BuildConclusionInstruction(DossierReading reading)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Génère la section 8 — CONCLUSION ET PERSPECTIVES du dossier de restitution.");
            sb.AppendLine();
            sb.AppendLine("VOIX : mixte — précision clinique sobre + chaleur bienveillante pour les parents.");
            sb.AppendLine("La conclusion est lue par le médecin ET par la famille — trouver le ton juste.");
            sb.AppendLine();
            sb.AppendLine("FORMAT STRICT — JSON valide uniquement, aucun texte avant ni après :");
            sb.AppendLine("{");
            sb.AppendLine("  \"intro\": \"2 à 3 phrases intégratives sur ce que le bilan révèle de cet enfant (difficultés ET ressources).\",");
            sb.AppendLine("  \"forces\": [\"Force 1\", \"Force 2\", \"Force 3\", \"Force 4\"],");
            sb.AppendLine("  \"feuilleDeRoute\": [\"Étape 1 — libellé court\", \"Étape 2\", \"Étape 3\", \"Étape 4\", \"Étape 5\"],");
            sb.AppendLine("  \"messageParents\": [\"Message court aux parents 1\", \"Message 2\", \"Message 3\", \"Message 4\"],");
            sb.AppendLine("  \"prochainsRdv\": [\"Modalité 1\", \"Modalité 2\", \"Modalité 3\", \"Modalité 4\"],");
            sb.AppendLine("  \"engagement\": \"1 phrase finale douce et sobre, centrée sur le chemin à parcourir ensemble.\"");
            sb.AppendLine("}");
            sb.AppendLine();
            sb.AppendLine("RÈGLES :");
            sb.AppendLine("  • intro : 2-3 phrases intégratives — nommer les difficultés SANS les dramatiser, valoriser les ressources");
            sb.AppendLine("    Ne pas résumer le diagnostic ; synthétiser ce qu'on comprend de l'enfant dans sa globalité");
            sb.AppendLine("  • forces : 4 à 6 forces ou ressources de l'enfant (mots courts : « Curiosité », « Attachement aux proches »…)");
            sb.AppendLine("  • feuilleDeRoute : 4 à 6 étapes chronologiques du projet (Aujourd'hui → Mise en place → Réévaluation → Ajustements → Autonomie)");
            sb.AppendLine("  • messageParents : 4 courts messages bienveillants adressés aux parents (15 mots max chacun)");
            sb.AppendLine("    Ton : confiant, partenarial, sans condescendance. Ex : « Vous restez les partenaires essentiels de ce parcours. »");
            sb.AppendLine("  • prochainsRdv : 3 à 4 items concrets (prochaine consultation, réévaluation, bilans, coordination)");
            sb.AppendLine("  • engagement : phrase finale en « nous » — professionnels + famille — sobre et chaleureuse");

            if (reading.LatestBilanFinal != null)
            {
                var b = reading.LatestBilanFinal;
                if (b.DiagnosticsRetenus.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine("DIAGNOSTICS RETENUS (pour l'intro intégrative — ne pas les lister, les intégrer en prose) :");
                    foreach (var d in b.DiagnosticsRetenus)
                        sb.AppendLine($"  • {d.Value}");
                }
            }

            return sb.ToString();
        }

        private static string BuildEnvFeuilleInstruction(int idx, Models.Evaluations.CartographieEnvironnement? carto)
        {
            if (carto == null)
                return "Aucune cartographie de l'environnement n'est disponible. Écris : `**Observations** : non disponibles (aucune évaluation clôturée).`";

            var (feuille, nom) = idx switch
            {
                1 => (carto.Famille,           "Famille"),
                2 => (carto.EcolePairs,        "École & Pairs"),
                3 => (carto.EcransMedias,      "Écrans & Médias"),
                4 => (carto.ValeursSocietales, "Valeurs sociétales"),
                5 => (carto.CadreEducatif,     "Cadre éducatif"),
                _ => (carto.Famille,           "Famille")
            };

            bool feuilleHasScore = feuille.NervureCentrale.Score > 0
                                || feuille.NervuresSecondaires.Any(n => n.Score > 0);
            if (!feuilleHasScore)
                return $"La feuille « {nom} » n'a pas encore été renseignée. Écris : `**Observations** : non disponibles (feuille non complétée).`";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"=== DONNÉES D'ÉVALUATION — {nom.ToUpperInvariant()} ===");
            sb.AppendLine($"Nervure centrale : {feuille.NervureCentrale.Label}");
            sb.AppendLine($"  Score : {feuille.NervureCentrale.Score}/{feuille.NervureCentrale.MaxScore}");
            foreach (var item in feuille.NervureCentrale.Items)
                sb.AppendLine($"  {(item.IsChecked ? "✓" : "✗")} {item.Affirmation}");
            foreach (var n in feuille.NervuresSecondaires)
            {
                sb.AppendLine($"Nervure : {n.Label}  Score : {n.Score}/{n.MaxScore}");
                foreach (var item in n.Items)
                    sb.AppendLine($"  {(item.IsChecked ? "✓" : "✗")} {item.Affirmation}");
            }
            sb.AppendLine();
            sb.AppendLine($"Rédige UNIQUEMENT les observations pour la feuille « {nom} » :");
            sb.AppendLine();
            sb.AppendLine("**Observations**");
            sb.AppendLine("- 3 à 5 puces COURTES. Appuie-toi D'ABORD sur les items ✓/✗ de chaque nervure.");
            sb.AppendLine("- Reste STRICTEMENT dans le domaine de cette feuille.");
            sb.AppendLine("- NE MENTIONNE PAS les autres feuilles.");
            sb.AppendLine("- N'INVENTE RIEN : toute observation doit être ancrée dans les items cochés.");
            sb.AppendLine();
            sb.AppendLine("**Niveau clinique**");
            sb.AppendLine("1 SEULE phrase courte. Format : `Mot-clé (qualifier court).`");
            sb.AppendLine("Exemples : `Fluide (Contexte porteur et cohérent).` | `Mitigé (Tensions repérées sur plusieurs nervures).` | `Fragile (Cadre insuffisant — étayage prioritaire).`");
            sb.AppendLine();
            sb.AppendLine("Commence directement par `**Observations**`.");
            return sb.ToString();
        }

        private static string BuildEnvGlobalInstruction(Models.Evaluations.CartographieEnvironnement? carto)
        {
            if (carto == null)
                return "Aucune cartographie de l'environnement disponible. Écris : `L'environnement de l'enfant n'a pas encore été évalué.`";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== SYNTHÈSE — BRANCHE ÉDUCATIVE ===");
            sb.AppendLine("Niveaux des 5 feuilles :");
            void AppendNiveau(string nom, Models.Evaluations.FeuilleEnvironnement f)
            {
                var n = Services.Evaluations.EnvironnementScoringService.CalculerFeuille(f);
                sb.AppendLine($"  {nom} : {Models.Evaluations.CartographieEnvironnementContent.NiveauLabel(n)}");
            }
            AppendNiveau("Famille", carto.Famille);
            AppendNiveau("École & Pairs", carto.EcolePairs);
            AppendNiveau("Écrans & Médias", carto.EcransMedias);
            AppendNiveau("Valeurs sociétales", carto.ValeursSocietales);
            AppendNiveau("Cadre éducatif", carto.CadreEducatif);
            sb.AppendLine();
            sb.AppendLine("Rédige la LECTURE GLOBALE de la Branche Éducative pour les parents :");
            sb.AppendLine("- 1 paragraphe d'introduction (2-3 phrases) sur l'environnement global.");
            sb.AppendLine("- 1 paragraphe **Axes de soutien prioritaires** avec 2-3 puces ✓ : ce qui est porteur.");
            sb.AppendLine("- 1 paragraphe **Points d'attention** avec 2-3 puces sur ce qui nécessite un étayage.");
            sb.AppendLine("- Ton chaleureux et constructif — voix livre (Tome 2/3), pas clinique.");
            sb.AppendLine("- Ne répète pas les observations de chaque feuille : synthétise et croise.");
            return sb.ToString();
        }

        // ── API legacy (compat ascendante) ──────────────────────────────────

        /// <summary>
        /// Compat : ancienne signature qui ne reçoit que le nom du patient. Lit le dossier
        /// en interne puis délègue à la nouvelle méthode. À retirer quand tous les appelants
        /// auront migré vers PrefillBlocAsync(bloc, reading).
        /// </summary>
        public async Task<(string Suggestion, string SourceContext)> PrefillBlocAsync(
            string patientNomComplet,
            RestitutionBloc bloc,
            CancellationToken cancellationToken = default)
        {
            var reading = await _dossierReader.ReadAsync(patientNomComplet);
            return await PrefillBlocAsync(bloc, reading, cancellationToken);
        }

        // ── Construction des prompts ────────────────────────────────────────

        private static string BuildSystemPrompt(RestitutionBloc bloc)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Tu es un assistant médical spécialisé en pédopsychiatrie. Tu rédiges un bloc d'un Dossier de Restitution Clinique destiné à être remis aux parents et aux intervenants du parcours de soins (école, orthophoniste, psychomotricien, neuropsychologue).");
            sb.AppendLine();
            sb.AppendLine("RÈGLES STRICTES :");
            sb.AppendLine("- Ne génère QUE le contenu du bloc demandé. Pas de salutations, pas de méta-commentaires.");
            sb.AppendLine("- N'invente JAMAIS d'informations médicales. Si une donnée manque, omets-la.");
            sb.AppendLine("- Tout ce que tu écris doit pouvoir être justifié par le dossier fourni.");
            sb.AppendLine("- Formatage Markdown clair (listes à puces, titres, gras pour les diagnostics retenus).");
            sb.AppendLine("- Cohérence stricte avec les autres blocs : tu rédiges un bloc parmi 8, ne te contredis pas avec les sources validées.");
            sb.AppendLine();
            sb.AppendLine(VoixCibleConsigne(bloc.VoixCible));
            return sb.ToString();
        }

        private static string VoixCibleConsigne(string voixCible) => voixCible switch
        {
            "clinique" => "VOIX CIBLE — CLINIQUE : terminologie pédopsychiatrique précise (DSM, troubles, axes), rigueur, concision. Destiné aux professionnels de santé. Pas de vulgarisation excessive.",
            "livre"    => "VOIX CIBLE — PARENTS (voix livre) : ton chaleureux, empathique, déculpabilisant. Reformule les concepts cliniques en mots accessibles. Métaphores organiques bienvenues (ressources, chemins, équilibre). Destiné aux parents inquiets.",
            _          => "VOIX CIBLE — MIXTE : précis sans jargon excessif. Lisible par les parents ET utile aux professionnels. Définis brièvement les termes techniques quand tu en utilises."
        };

        private static string BuildUserPrompt(RestitutionBloc bloc, string dossierContext)
        {
            var focus = FocusHintForBloc(bloc.Key);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(dossierContext);
            sb.AppendLine();
            sb.AppendLine("=================");
            sb.AppendLine($"BLOC À RÉDIGER : « {bloc.Titre} » (clé : {bloc.Key})");
            sb.AppendLine();
            sb.AppendLine($"FOCUS SUGGÉRÉ : {focus}");
            sb.AppendLine();
            sb.AppendLine("Rédige maintenant le contenu de ce bloc en suivant la voix cible et les règles strictes. Tu peux piocher partout dans le dossier ci-dessus, mais reste centré sur le focus.");
            return sb.ToString();
        }

        /// <summary>
        /// Indique au LLM quelles parties du dossier sont les plus pertinentes pour chaque bloc.
        /// Sert d'aide à la rédaction sans interdire l'accès au reste du dossier.
        /// </summary>
        private static string FocusHintForBloc(string key) => key switch
        {
            "couverture"
                => @"Identité + 1ère consultation. Ce bloc alimente directement les champs de la page de couverture du PDF, donc tu DOIS produire EXACTEMENT ce format (une ligne par champ, libellés en gras suivis de deux-points). N'ajoute aucun texte avant ou après le bloc.

**Nom et prénom** : Prénom NOM
**Date de naissance** : JJ/MM/AAAA
**Âge** : N ans
**Établissement** : nom de l'école/établissement (ou « Non renseigné »)
**Classe** : niveau scolaire (ou « Non renseigné »)
**Année scolaire** : 2025-2026 (par défaut si non précisée)
**Motif de consultation** : 3 à 6 MOTS-CLÉS séparés par des virgules, sans phrases complètes. Exemples du bon format : « refus scolaire, crises de colère, stress », « hyperactivité, difficultés d'apprentissage, opposition », « anxiété de séparation, troubles du sommeil ». PAS de phrase narrative ici — seulement des mots-clés.
**Dates d'évaluation** : liste les dates des séances d'évaluation séparées par « – » (en-dash). Cherche dans le YAML des évaluations clôturées, dans cet ordre de priorité :
  1. Si `session_dates: [...]` est présent (format : `[2026-05-15, 2026-05-21]`) → utilise CES dates (converties en JJ/MM/AAAA).
  2. Sinon, si `date_cloture` est présente (format ISO 2026-05-31T...) → utilise UNIQUEMENT cette date (séance unique).
  3. Sinon, utilise `date_debut`.
  4. En dernier recours seulement si AUCUN champ n'est dispo → « Non renseigné(e) ».
Formatage : 1 date → « 20/05/2026 » seule. 2-5 dates → « 15/05/2026 – 21/05/2026 – 28/05/2026 ». > 5 dates → « du 15/05/2026 au 28/06/2026 (8 séances) ».

Source principale : la 1ère consultation (école, classe, motif). L'identité administrative vient de patient.json. Pour les dates d'évaluation, suis strictement la cascade ci-dessus — n'écris JAMAIS « Non renseigné(e) » si une date_cloture est disponible dans les évaluations. Si une autre info manque, écris « Non renseigné(e) » au lieu d'inventer.",

            "restitution_1page"
                => "Synthèse Globale Med + Synthèse Globale V0.5 + Projet Thérapeutique : produire UNE page accessible aux parents avec : ce qu'on a compris, les forces de l'enfant, les difficultés, ce qui peut aider, la feuille de route, l'environnement.",

            // ── Bloc 3 : Identification ─────────────────────────────────────
            // Texte narratif court qui pose le décor clinique. Source : patient.json + 1ère
            // consultation. Le rendu HTML extrait aussi les méta-données structurées
            // (période d'évaluation, date de restitution, évaluateur, lieu) via Pick() sur
            // labels en gras — d'où le format strict imposé.
            "patient_identification"
                => @"1ère consultation + patient.json. Produis un texte clinique court qui présente l'enfant, suivi des méta-données d'évaluation.

FORMAT STRICT :

**Présentation** : 1 phrase de 2-3 lignes commençant par « Il s'agit de l'enfant... », mentionnant : prénom NOM, âge en années + entre parenthèses la date de naissance, scolarité (niveau + nom et ville de l'école + année scolaire), date du 1er entretien, accompagnants présents lors de ce 1er entretien (parents, mère seule, etc.).
**Période d'évaluation** : dates de l'évaluation clôturée (1 date ou « du JJ/MM/AAAA au JJ/MM/AAAA » si plusieurs séances). Non renseigné si pas d'évaluation clôturée.
**Date de restitution** : JJ/MM/AAAA (date du jour si non précisée).
**Évaluateur** : Dr Lassoued Nair, Pédopsychiatre.
**Lieu** : ville du cabinet si déduite, sinon « Cabinet de pédopsychiatrie ».

Si une donnée manque dans le dossier, écris « Non renseigné(e) » à sa place. N'invente rien.",

            // ── Bloc 4 : Motif de consultation ──────────────────────────────
            // Texte narratif tiré de l'interrogatoire initial et étoffé par la synthèse
            // globale Med (qui repère les motifs sous-jacents). Pas de listes — un récit court
            // qui aide le confrère à comprendre POURQUOI l'enfant a été amené.
            "patient_motif"
                => "1ère consultation (champ « motif » de l'interrogatoire) + Synthèse Globale Med + Synthèse Globale V0.5. " +
                   "Rédige UN paragraphe narratif (3-6 lignes) qui explicite le motif de consultation tel qu'il a été formulé par les parents " +
                   "lors du 1er entretien, en intégrant le contexte d'apparition des troubles (déclencheur, ancienneté, retentissement). " +
                   "Style clinique précis, sans listes, sans titres. Si le motif initial diffère de l'analyse synthétique de Med, mentionne brièvement les deux. " +
                   "Commence directement par le récit, pas de phrase d'introduction du type « Voici le motif... ».",

            // ── Bloc 5 : Contexte familial ──────────────────────────────────
            // Narratif court (composition foyer, climat, événements de vie) + 4 colonnes
            // (Père / Mère / Fratrie / Autres figures) + bandeau Points à retenir.
            // Le LLM est appelé en mode progressif : 1 narrative + 5 cards (cf. SuggestContexteFamilialProgressiveAsync).
            "patient_contexte_familial"
                => @"1ère consultation + Synthèse Globale Med. Produis un récit narratif court SUIVI de 5 cartes structurées.

FORMAT STRICT (les marqueurs `**Titre**` permettent au rendu HTML de découper la section) :

**Récit familial** : 3-5 lignes décrivant la composition du foyer (parents séparés/en couple, fratrie, garde alternée…), le climat actuel, les événements de vie majeurs (déménagement, séparation, deuil, recomposition…). Style clinique narratif.

**Père** :
- Prénom, âge (si renseigné).
- Activité professionnelle.
- Lieu de vie.
- Statut conjugal / familial pertinent.

**Mère** :
- Prénom, âge (si renseigné).
- Activité professionnelle.
- Lieu de vie.
- Statut conjugal / familial pertinent.

**Fratrie** :
- Pour chaque frère/sœur (ou demi) : Prénom, âge, lien (même père et mère, côté mère, côté père).

**Autres figures** (HORS parents et fratrie déjà traités ci-dessus) :
- Uniquement les figures tierces : grands-parents, oncle/tante, cousin proche, éducateur référent, assistante maternelle, famille d'accueil, tuteur légal, voisin de confiance… Format : `- Figure (prénom si connu) : rôle bref.` Si aucune figure tierce n'est mentionnée dans le dossier, écrire `- Aucune figure tierce identifiée.`

**Points à retenir** : 2-4 lignes pointant les éléments du contexte familial susceptibles d'éclairer le tableau clinique (insécurité affective, conflit de loyauté, parent absent, parentification…). Bienveillant, sans jugement. Pas de listes.

Pour chaque carte, si une info manque, écris « Non renseigné » au lieu d'inventer. N'écris RIEN d'autre que ces 6 sections labellisées.",

            // ── Bloc 6 : Antécédents ────────────────────────────────────────
            // Bloc composite : 6 sous-sections (médicaux, développementaux, familiaux,
            // suivi résumé, bilans résumé, détail complet). Génération progressive 6×LLM
            // via SuggestAntecedentsProgressiveAsync.
            "patient_antecedents"
                => @"1ère consultation (anamnèse) + Notes de consultation. Produis 6 sous-sections distinctes.

FORMAT STRICT (les marqueurs `**Titre**` sont obligatoires) :

**Antécédents médicaux** : liste à puces de 5-6 items (grossesse, accouchement, néonatal, maladies chroniques, hospitalisations, traitements). Format : `- **Item :** valeur.`

**Antécédents développementaux** : liste à puces de 3-5 items (marche, langage, propreté diurne, propreté nocturne, latéralisation). Format identique.

**Antécédents familiaux** : liste à puces de 3-5 items (TDAH, troubles anxieux, humeur, neurodéveloppement, addictions, suicide). Format identique. Si non connu : « non connu ».

**Suivi résumé** : liste à puces ULTRA-COURTE — une ligne par suivi (CMP, CAMPS, pédopsy libéral, psy, ortho…). Format : `- Structure (dates).` Si aucun : `- Aucun suivi spécialisé.`

**Bilans résumé** : liste à puces ULTRA-COURTE — une ligne par bilan réalisé. Format : `- Type bilan (date si connue).` Si aucun : `- Aucun bilan formel.`

**Parcours — détail** : détail complet en 2 sous-sections.
  `**Suivi antérieur**` : pour chaque suivi, structure + période + fréquence + motif + évolution.
  `**Bilans réalisés**` : pour chaque bilan, type + date + praticien + résultats clés.
  Si aucun suivi ET aucun bilan : écrire UNIQUEMENT `Aucun antécédent de suivi ou de bilan identifié dans le dossier.`

N'invente RIEN. Si une donnée manque, écris « non connu » ou « sans particularité déclarée ».",

            // ── Bloc 7 : Situation actuelle ─────────────────────────────────
            // Bloc composite : 5 sous-blocs (école, maison, autres, forces, activités).
            // Génération progressive 5×LLM via SuggestSituationActuelleProgressiveAsync.
            // Sources : évaluations (Étape 3 cartographie enfant + Étape 5 environnement) + notes récentes.
            "patient_situation_actuelle"
                => @"Évaluations clôturées (Étape 3 cartographie enfant + Étape 5 cartographie environnement) + Notes de consultation récentes + Synthèse Globale Med. Produis 5 sous-sections.

FORMAT STRICT (marqueurs `**Titre**` obligatoires) :

**À l'école** : 3-5 puces, comportements et fonctionnement observés en milieu scolaire (intégration au groupe, agressivité envers maîtresse/camarades, difficultés d'apprentissage, besoin de cadre…).

**À la maison** : 3-5 puces, comportements observés au domicile (crises de colère, oppositions, hyperactivité, surconsommation d'écrans, troubles du sommeil…).

**Avec les autres** : 2-4 puces, sphère relationnelle au-delà de l'école (difficultés relationnelles, tolérance à la frustration, réactions impulsives en cas de conflit…).

**Forces observées** : 4-6 puces, ressources et compétences positives de l'enfant (curiosité, attachement, capacité d'apprentissage, imagination, créativité, envie de réussir…).

**Activités et intérêts** : 1-3 puces, activités extra-scolaires, hobbies, sports, centres d'intérêt particuliers.

Pour chaque puce : phrase courte clinique, sans verbe d'opinion. Si une sphère n'a aucun élément renseigné, écris une seule puce « Aucun élément documenté à ce jour. ».",

            "synthese_diag"
                => "Évaluations clôturées (Bilan Final) + Synthèse Globale V0.5 : compréhension globale, diagnostics retenus avec niveau de confiance, diagnostics différentiels écartés et POURQUOI, intégration des cartographies enfant + environnement.",

            "bilan_final"
                => "Évaluations clôturées : observations détaillées par sphère (attachement, régulation émotionnelle, langage, psychomotricité, etc.), avec niveaux cliniques chiffrés issus des cartographies.",

            "synthese_globale"
                => "Synthèse Globale Med + Synthèse Globale V0.5 : compréhension intégrative reliant fonctionnement interne, environnement et dynamique évolutive.",

            "projet_therapeutique"
                => "Projet Thérapeutique validé : structurer en 5 sous-axes (médical, psychologique, développemental, parental/familial, école). Pour chaque axe : objectifs, modalités, indicateurs de surveillance, modalités de suivi.",

            "conclusion"
                => "Synthèse de tout le dossier : forces clés, feuille de route, message aux parents, prochains rendez-vous. Ton chaleureux et encourageant — c'est la page de clôture du document.",

            _   => "Pioche dans toutes les sources du dossier les éléments les plus pertinents pour ce bloc."
        };

        // ── Bloc couverture : construction déterministe sans LLM ─────────────

        /// <summary>
        /// Variante async qui construit la couverture déterministe, puis remplit le seul
        /// champ « Motif de consultation » via un appel LLM ciblé si l'extraction déterministe
        /// n'a rien trouvé. Tous les autres champs (identité, scolarité, dates) restent
        /// 100 % déterministes — on garde la robustesse anti-hallucination du PR initial.
        /// Le LLM n'a droit qu'à 3-6 mots-clés en sortie (format imposé).
        /// </summary>
        public async Task<string> BuildCouvertureFromDataAsync(
            DossierReading reading,
            CancellationToken ct = default)
        {
            var md = BuildCouvertureFromData(reading);

            // Si le motif est vide / « Non renseigné », on demande au LLM 3-6 mots-clés.
            // L'extraction déterministe (CovExtractField + CovExtractMotif) ne couvre que les
            // notes structurées par section labellisée — pour les autres, le LLM prend le relais.
            var motifRx = new Regex(@"^\*\*Motif de consultation\*\*\s*:\s*(.+?)$", RegexOptions.Multiline);
            var mMotif  = motifRx.Match(md);
            var motifVal = mMotif.Success ? mMotif.Groups[1].Value.Trim() : "";
            bool motifEmpty = string.IsNullOrWhiteSpace(motifVal)
                              || motifVal.Equals("Non renseigné",  StringComparison.OrdinalIgnoreCase)
                              || motifVal.Equals("Non renseignée", StringComparison.OrdinalIgnoreCase);

            if (motifEmpty)
            {
                var keywords = await SuggestMotifKeywordsAsync(reading, ct);
                if (!string.IsNullOrWhiteSpace(keywords))
                {
                    md = motifRx.Replace(md, $"**Motif de consultation** : {keywords}", 1);
                }
            }

            return md;
        }

        /// <summary>
        /// Petit appel LLM qui produit 3 à 6 mots-clés du motif de consultation à partir
        /// du dossier complet. Format strict : mots-clés en minuscules séparés par virgules,
        /// pas de phrases. Utilisé uniquement en fallback quand l'extraction déterministe échoue.
        /// </summary>
        private async Task<string> SuggestMotifKeywordsAsync(DossierReading reading, CancellationToken ct)
        {
            var context = reading.RenderForLlm();
            if (string.IsNullOrWhiteSpace(context)) return "";

            var systemPrompt =
                "Tu es un assistant médical en pédopsychiatrie. Tu produis UNIQUEMENT une liste " +
                "de 3 à 6 mots-clés résumant le motif de consultation. " +
                "RÈGLES STRICTES :\n" +
                "- Mots-clés en minuscules, séparés par des virgules.\n" +
                "- PAS de phrase complète, PAS de verbe conjugué, PAS de titre.\n" +
                "- N'invente JAMAIS : si rien n'est trouvé, réponds exactement « Non renseigné ».\n" +
                "Exemples du bon format : « refus scolaire, crises de colère, stress » ou " +
                "« hyperactivité, difficultés d'apprentissage, opposition » ou " +
                "« anxiété de séparation, troubles du sommeil ».";

            var userPrompt = new StringBuilder()
                .AppendLine(context)
                .AppendLine()
                .AppendLine("=================")
                .AppendLine("Donne UNIQUEMENT 3 à 6 mots-clés du motif de consultation, séparés par des virgules. Rien d'autre.")
                .ToString();

            var messages = new List<(string role, string content)> { ("user", userPrompt) };
            var result   = await _llmService.ChatAsync(systemPrompt, messages, 80, ct);
            if (!result.success) return "";

            // Nettoyage : on garde la 1ère ligne non vide, on retire les guillemets et points finaux.
            var clean = (result.result ?? "")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim().Trim('"', '«', '»').TrimEnd('.'))
                .FirstOrDefault(l => l.Length > 2) ?? "";

            // Sécurité : si le LLM a renvoyé « Non renseigné » ou variante, on laisse vide
            // pour que CovOr() applique son fallback standard.
            if (clean.Equals("Non renseigné",  StringComparison.OrdinalIgnoreCase) ||
                clean.Equals("Non renseignée", StringComparison.OrdinalIgnoreCase))
                return "";

            return clean;
        }

        /// <summary>
        /// Construit le bloc "couverture" directement depuis les données structurées
        /// du DossierReading. Déterministe — jamais de placeholders [xxx].
        /// </summary>
        public static string BuildCouvertureFromData(DossierReading reading)
        {
            // 1. Identité depuis patient.json
            string nomComplet = "", dobFormatted = "", age = "", ecole = "", classe = "";
            try
            {
                using var doc = JsonDocument.Parse(reading.PatientJson);
                var r = doc.RootElement;

                // Deux variantes coexistent : camelCase (nouveaux patients) et PascalCase (anciens)
                string? Str(string key)
                {
                    var pascal = char.ToUpper(key[0]) + key[1..];
                    if (r.TryGetProperty(key,   out var a) && a.ValueKind == JsonValueKind.String) return a.GetString()?.Trim();
                    if (r.TryGetProperty(pascal, out var b) && b.ValueKind == JsonValueKind.String) return b.GetString()?.Trim();
                    return null;
                }

                var prenom = Str("prenom") ?? "";
                var nom    = Str("nom")    ?? "";
                nomComplet   = Str("nomComplet") ?? $"{prenom} {nom}".Trim();
                dobFormatted = Str("dobFormatted") ?? CovFormatDob(Str("dob") ?? "");

                // "age" ou "Age"
                if ((r.TryGetProperty("age", out var ageProp) || r.TryGetProperty("Age", out ageProp))
                    && ageProp.ValueKind == JsonValueKind.Number)
                    age = $"{ageProp.GetInt32()} ans";
                else if (Str("dob") is string dob2 && !string.IsNullOrEmpty(dob2))
                    age = CovComputeAge(dob2);

                ecole  = Str("ecole")  ?? "";
                classe = Str("classe") ?? "";
            }
            catch { /* si JSON invalide, on continue avec les valeurs vides */ }

            // 2. École / classe / motif / année — corpus = PremiereConsultation + toutes les notes
            // (Certains patients n'ont pas de note type "consultation-premiere" : on cherche dans tout le dossier.)
            var corpusSb = new StringBuilder();
            corpusSb.AppendLine(reading.PremiereConsultation);
            foreach (var n in reading.NotesConsultation)
                corpusSb.AppendLine(n.Content);
            var corpus = corpusSb.ToString();

            if (string.IsNullOrEmpty(ecole))  ecole  = CovExtractField(corpus, "école", "ecole", "établissement", "etablissement") ?? "";
            if (string.IsNullOrEmpty(classe)) classe = CovExtractField(corpus, "classe", "niveau scolaire", "niveau") ?? CovExtractClasse(corpus) ?? "";

            var motif    = CovExtractField(corpus, "motif de consultation", "motif") ?? CovExtractMotif(corpus) ?? "";
            var anneeSco = CovExtractAnneeScolaire(corpus);

            // 3. Dates d'évaluation
            var dates = CovBuildDatesEvaluation(reading.Evaluations);

            // 4. Assemblage markdown (même format qu'attendu par ParseCoverFieldsFromBloc)
            var sb = new StringBuilder();
            sb.AppendLine($"**Nom et prénom** : {CovOr(nomComplet, "Non renseigné")}");
            sb.AppendLine($"**Date de naissance** : {CovOr(dobFormatted, "Non renseignée")}");
            sb.AppendLine($"**Âge** : {CovOr(age, "Non renseigné")}");
            sb.AppendLine($"**Établissement** : {CovOr(ecole, "Non renseigné")}");
            sb.AppendLine($"**Classe** : {CovOr(classe, "Non renseignée")}");
            sb.AppendLine($"**Année scolaire** : {CovOr(anneeSco, "2025-2026")}");
            sb.AppendLine($"**Motif de consultation** : {CovOr(motif, "Non renseigné")}");
            sb.AppendLine($"**Dates d'évaluation** : {CovOr(dates, "Non renseignées")}");
            return sb.ToString().Trim();
        }

        private static string? CovExtractField(string corpus, params string[] labels)
        {
            foreach (var lbl in labels)
            {
                var escaped = Regex.Escape(lbl);

                // Format 1 : "**Label** : value" ou "Label : value" (même ligne)
                var m1 = Regex.Match(corpus,
                    $@"\*{{0,2}}{escaped}\*{{0,2}}\s*:\s*(.+?)(?:\r?\n|$)",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline);
                if (m1.Success)
                {
                    var v = m1.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }

                // Format 2 : "## Label\nvalue" (titre markdown, valeur sur la ligne suivante)
                var m2 = Regex.Match(corpus,
                    $@"#+\s+{escaped}\s*\r?\n\s*(.+?)(?:\r?\n|$)",
                    RegexOptions.IgnoreCase | RegexOptions.Multiline);
                if (m2.Success)
                {
                    var v = m2.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(v) && !v.StartsWith("#")) return v;
                }
            }
            return null;
        }

        private static string? CovExtractClasse(string corpus)
        {
            // "élève de 3e (année de collège)" → "3e"
            var patterns = new[]
            {
                @"\bélève\s+de\s+(\d[eème]+(?:\s+(?:année|semestre))?)",
                @"\ben\s+(\d[eème]+)\b",
                @"\b(CE[12]|CM[12]|CP|GS|MS|PS|TPS)\b",
                @"\b(Seconde|Première|Terminale)\b",
                @"\b(\d[eème]+)\s+(?:année|semestre|au\s+collège|au\s+lycée|de\s+collège|de\s+lycée)",
                @"\bclasse\s+de\s+(\w+)",
            };
            foreach (var pat in patterns)
            {
                var m = Regex.Match(corpus, pat, RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    var v = m.Groups[1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }
            }
            return null;
        }

        private static string? CovExtractMotif(string corpus)
        {
            // Cherche la section "Historique médical" ou "HPI" et en extrait la première ligne utile
            var sections = new[] { "Historique médical", "HPI", "Motif principal", "Présentation" };
            foreach (var sec in sections)
            {
                var m = Regex.Match(corpus,
                    $@"{Regex.Escape(sec)}[^\n]*\n(.+?)(?:\n\n|\n[A-Z{{**}}]|$)",
                    RegexOptions.IgnoreCase | RegexOptions.Singleline);
                if (m.Success)
                {
                    var lines = m.Groups[1].Value
                        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(l => l.Length > 5)
                        .Take(2)
                        .Select(l => l.TrimStart('-', '*', '•', ' ').Trim().TrimEnd('.'));
                    var v = string.Join(", ", lines);
                    if (!string.IsNullOrWhiteSpace(v)) return v;
                }
            }
            return null;
        }

        private static string CovExtractAnneeScolaire(string note)
        {
            var m = Regex.Match(note, @"\b(20\d\d)[/\-](20\d\d)\b");
            return m.Success ? $"{m.Groups[1].Value}-{m.Groups[2].Value}" : "";
        }

        private static string CovBuildDatesEvaluation(List<EvaluationEntry> evaluations)
        {
            if (evaluations.Count == 0) return "";
            var eval = evaluations[0]; // plus récente en premier

            // Chercher session_dates: [2026-05-15, 2026-05-21] dans le YAML
            var sdMatch = Regex.Match(eval.Content ?? "", @"session_dates\s*:\s*\[([^\]]+)\]");
            if (sdMatch.Success)
            {
                var parsed = sdMatch.Groups[1].Value
                    .Split(',')
                    .Select(p => p.Trim().Trim('"', '\'', ' '))
                    .Where(p => DateTime.TryParse(p, System.Globalization.CultureInfo.InvariantCulture,
                                                  System.Globalization.DateTimeStyles.RoundtripKind, out _))
                    .Select(p => DateTime.Parse(p, System.Globalization.CultureInfo.InvariantCulture,
                                                System.Globalization.DateTimeStyles.RoundtripKind))
                    .OrderBy(d => d)
                    .ToList();
                if (parsed.Count == 1) return parsed[0].ToString("dd/MM/yyyy");
                if (parsed.Count <= 5) return string.Join(" – ", parsed.Select(d => d.ToString("dd/MM/yyyy")));
                return $"du {parsed.First():dd/MM/yyyy} au {parsed.Last():dd/MM/yyyy} ({parsed.Count} séances)";
            }

            return eval.DateCloture.HasValue ? eval.DateCloture.Value.ToString("dd/MM/yyyy") : "";
        }

        private static string CovFormatDob(string dob)
        {
            if (DateTime.TryParse(dob, out var d)) return d.ToString("dd/MM/yyyy");
            return dob;
        }

        private static string CovComputeAge(string dob)
        {
            if (!DateTime.TryParse(dob, out var d)) return "";
            var today = DateTime.Today;
            var age = today.Year - d.Year;
            if (today < d.AddYears(age)) age--;
            return age >= 0 ? $"{age} ans" : "";
        }

        private static string CovOr(string? val, string fallback)
            => string.IsNullOrWhiteSpace(val) ? fallback : val!;
    }
}
