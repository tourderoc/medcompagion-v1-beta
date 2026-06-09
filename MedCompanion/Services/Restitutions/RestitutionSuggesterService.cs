using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

            // Les 6 sous-sections : (marqueur Markdown, instruction LLM)
            var subsections = new (string Title, string Instruction)[]
            {
                ("**Ce que nous avons compris**",
                 "Rédige UNIQUEMENT le contenu de la section « Ce que nous avons compris » : " +
                 "1 paragraphe d'introduction bienveillant (2-3 phrases), puis une liste à puces " +
                 "de 3-4 points-clés (- **Mot-clé :** description courte). " +
                 "Commence directement par le paragraphe, sans titre ni introduction."),

                ("**Ses forces et ses réussites**",
                 "Rédige UNIQUEMENT le contenu de la section « Ses forces et ses réussites » : " +
                 "liste de 4-5 points positifs concrets observés chez cet enfant " +
                 "(- **Mot-clé :** description courte). " +
                 "Commence directement par la liste, sans titre ni introduction."),

                ("**Les difficultés actuellement observées**",
                 "Rédige UNIQUEMENT le contenu de la section « Les difficultés actuellement observées » : " +
                 "liste de 3-4 défis principaux, formulés sans culpabiliser les parents " +
                 "(- **Mot-clé :** description courte). " +
                 "Commence directement par la liste, sans titre ni introduction."),

                ("**Ce qui peut aider**",
                 "Rédige UNIQUEMENT le contenu de la section « Ce qui peut aider » : " +
                 "liste de 3-4 actions concrètes pour la maison et l'école " +
                 "(- **Mot-clé :** description courte). " +
                 "Commence directement par la liste, sans titre ni introduction."),

                ("**Notre feuille de route**",
                 "Rédige UNIQUEMENT le contenu de la section « Notre feuille de route » : " +
                 "liste numérotée de 3-5 prochaines étapes concrètes " +
                 "(1. **Étape :** description courte). " +
                 "Commence directement par la liste numérotée, sans titre ni introduction."),

                ("**Son environnement : points clés**",
                 "Rédige UNIQUEMENT le contenu de la section « Son environnement : points clés » : " +
                 "1-2 phrases sur les ressources positives de l'entourage, puis une liste de " +
                 "2-3 points de vigilance (- **Point :** description courte). " +
                 "Commence directement par le texte, sans titre ni introduction.")
            };

            var accumulated = new System.Text.StringBuilder();

            foreach (var (title, instruction) in subsections)
            {
                if (ct.IsCancellationRequested) break;

                var userPrompt = Build1PageSubsectionPrompt(context, instruction);
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

        private static string Build1PageSubsectionPrompt(string dossierContext, string instruction)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(dossierContext);
            sb.AppendLine();
            sb.AppendLine("=================");
            sb.AppendLine("INSTRUCTION STRICTE — génère UNIQUEMENT ce qui est demandé ci-dessous, " +
                          "sans introduction, sans commentaire, sans titre supplémentaire :");
            sb.AppendLine(instruction);
            sb.AppendLine();
            sb.AppendLine("Sois concis (6-10 lignes max). Réponds directement en Markdown.");
            return sb.ToString();
        }

        // ── Génération progressive Contexte familial (1 narrative + 4 cards) ─

        /// <summary>
        /// Génère le bloc « Contexte familial » en 5 appels LLM séquentiels (récit narratif +
        /// 4 cartes Père / Mère / Fratrie / Points à retenir). Chaque appel reçoit le dossier
        /// complet, les marqueurs `**Titre**` sont concaténés au fil de l'eau pour permettre
        /// au rendu HTML de découper les sections.
        /// </summary>
        public async Task SuggestContexteFamilialProgressiveAsync(
            DossierReading reading,
            Action<string> onSectionReady,
            CancellationToken ct = default)
        {
            var context = reading.RenderForLlm();
            if (string.IsNullOrWhiteSpace(context)) { onSectionReady("(Aucun contenu source disponible.)"); return; }

            var blocCf = new RestitutionBloc("patient_contexte_familial", "Contexte familial", 5, "clinique");
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

                ("**Points à retenir**",
                 "Rédige UNIQUEMENT 2-4 lignes pointant les éléments du contexte familial susceptibles " +
                 "d'éclairer le tableau clinique (insécurité affective, conflit de loyauté, parent absent, " +
                 "parentification…). Style bienveillant, sans jugement. Pas de listes — un paragraphe court. " +
                 "Commence directement par le paragraphe.")
            };

            await RunProgressiveSubsectionsAsync(systemPrompt, context, subsections, onSectionReady, 500, ct);
        }

        // ── Génération progressive Antécédents (4 sous-sections) ────────────

        /// <summary>
        /// Génère le bloc « Antécédents » en 4 appels LLM séquentiels (médicaux,
        /// développementaux, familiaux, parcours de soins). Le résultat est concaténé
        /// avec marqueurs `**Titre**` pour parsing HTML ultérieur.
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

                ("**Parcours de soins et interventions**",
                 "Rédige UNIQUEMENT 2 sous-sections imbriquées :\n" +
                 "`**Déjà réalisés**` puis liste à puces des suivis antérieurs " +
                 "(psy, ortho, psychomot, neuropsy…) ou « Aucun suivi spécialisé régulier à ce jour. »\n" +
                 "`**Bilans déjà réalisés**` puis liste à puces des bilans diagnostiques antérieurs " +
                 "(QI, langage, attention…) ou « Aucun bilan formel réalisé à ce jour. »")
            };

            await RunProgressiveSubsectionsAsync(systemPrompt, context, subsections, onSectionReady, 500, ct);
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

            await RunProgressiveSubsectionsAsync(systemPrompt, context, subsections, onSectionReady, 400, ct);
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
            (string Title, string Instruction)[] subsections,
            Action<string> onSectionReady,
            int maxTokensPerSection,
            CancellationToken ct)
        {
            var accumulated = new System.Text.StringBuilder();

            foreach (var (title, instruction) in subsections)
            {
                if (ct.IsCancellationRequested) break;

                var userPrompt = Build1PageSubsectionPrompt(context, instruction);
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
            // structurées (Père / Mère / Fratrie / Points à retenir) rendues en HTML via parseur.
            // Le LLM est appelé en mode progressif : 1 narrative + 4 cards (cf. SuggestContexteFamilialProgressiveAsync).
            "patient_contexte_familial"
                => @"1ère consultation + Synthèse Globale Med. Produis un récit narratif court SUIVI de 4 cartes structurées.

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

**Points à retenir** : 2-4 lignes pointant les éléments du contexte familial susceptibles d'éclairer le tableau clinique (insécurité affective, conflit de loyauté, parent absent, parentification…). Bienveillant, sans jugement.

Pour chaque carte, si une info manque, écris « Non renseigné » au lieu d'inventer. N'écris RIEN d'autre que ces 5 sections labellisées.",

            // ── Bloc 6 : Antécédents ────────────────────────────────────────
            // Bloc composite : 4 sous-blocs cliniques (médicaux, développementaux, familiaux,
            // parcours de soins). Génération progressive 4×LLM via SuggestAntecedentsProgressiveAsync.
            "patient_antecedents"
                => @"1ère consultation (anamnèse) + Notes de consultation. Produis 4 sous-sections distinctes d'antécédents.

FORMAT STRICT (les marqueurs `**Titre**` sont obligatoires) :

**Antécédents médicaux** : liste à puces de 5-6 items concernant grossesse, accouchement, période néonatale, maladies chroniques, hospitalisations, traitements en cours. Format : `- **Item :** valeur.` Exemples : `- **Grossesse :** sans particularité déclarée.`

**Antécédents développementaux** : liste à puces de 3-5 items sur les acquisitions clés (marche, langage, propreté diurne, propreté nocturne, latéralisation…). Format identique. Si une acquisition est dans les normes, écrire « dans les délais ».

**Antécédents familiaux** : liste à puces de 3-5 items sur les troubles connus dans la famille (TDAH, troubles anxieux, troubles de l'humeur, troubles du neurodéveloppement, addictions, suicide). Format identique. Si non connu, écrire « non connu ».

**Parcours de soins et interventions** : 1-2 sous-titres en gras + liste à puces sous chacun.
  - `**Déjà réalisés**` : suivis spécialisés antérieurs (psy, ortho, psychomot, neuropsy…).
  - `**Bilans déjà réalisés**` : bilans diagnostiques antérieurs (QI, langage, attention…).
  Si aucun, écrire « Aucun suivi spécialisé régulier à ce jour. » / « Aucun bilan formel réalisé à ce jour. ».

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
    }
}
