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

            "patient_contexte"
                => "1ère consultation et notes de consultation suivantes : identité, scolarité, motif, contexte familial, antécédents médicaux et développementaux, situation actuelle (école / maison / pairs), forces et activités.",

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
