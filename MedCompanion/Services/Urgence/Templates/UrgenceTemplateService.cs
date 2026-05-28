using System.Collections.Generic;
using MedCompanion.Models.Urgences;

namespace MedCompanion.Services.Urgence.Templates
{
    /// <summary>
    /// Fabrique les sections du questionnaire de risque suicidaire selon la tranche d'âge.
    /// Inspiré de C-SSRS, adapté en français pédopsy.
    /// </summary>
    public static class UrgenceTemplateService
    {
        public enum AgeTier { JeuneEnfant, Enfant, Adolescent }

        public static AgeTier TierFor(int? age)
        {
            if (!age.HasValue) return AgeTier.Adolescent;  // par défaut C-SSRS complet
            if (age.Value < 7)  return AgeTier.JeuneEnfant;
            if (age.Value < 13) return AgeTier.Enfant;
            return AgeTier.Adolescent;
        }

        public static List<UrgenceEvaluationSection> BuildSuicideRiskSections(int? age)
        {
            var tier = TierFor(age);
            return tier switch
            {
                AgeTier.JeuneEnfant => BuildJeuneEnfant(),
                AgeTier.Enfant      => BuildEnfant(),
                _                   => BuildAdolescent()
            };
        }

        // ── < 7 ans : exploration indirecte, pas de section "scénario" ──────

        private static List<UrgenceEvaluationSection> BuildJeuneEnfant() => new()
        {
            Section("ideation_suicidaire", "1. Idéation / propos de mort",
                "Souvent indirect chez le jeune enfant (jeu, dessin). Distinguer expression émotionnelle vs intentionnalité.",
                ("absente",   "Absente"),
                ("vague",     "Vague / contextuelle (colère, frustration)"),
                ("recurrente","Récurrente / structurée")),

            Section("impulsivite", "2. Impulsivité",
                "Facteur majeur chez le jeune enfant : passage à l'acte impulsif sans planification.",
                ("faible",  "Faible"),
                ("moderee", "Modérée"),
                ("forte",   "Forte")),

            Section("acces_moyens", "3. Accès aux moyens",
                "Médicaments parents, objets tranchants, fenêtres accessibles, etc.",
                ("non",                    "Pas d'accès identifié"),
                ("oui_medicaments_parents","Oui — médicaments parents"),
                ("oui_autres",             "Oui — autres moyens accessibles")),

            Section("antecedents", "4. Antécédents",
                "TS antérieure, automutilations, idéation chronique.",
                ("aucun",          "Aucun"),
                ("automutilation", "Automutilations"),
                ("ts_anterieure",  "Tentative de suicide antérieure")),

            Section("facteurs_protecteurs", "5. Facteurs protecteurs",
                "Alliance familiale, lien thérapeutique, encadrement scolaire, hobbies.",
                ("absents",  "Absents / fragiles"),
                ("partiels", "Partiels"),
                ("solides",  "Solides")),

            Section("dangerosite", "6. Dangerosité globale",
                "Synthèse clinique de l'entretien et du contexte.",
                ("faible",  "Faible"),
                ("moderee", "Modérée"),
                ("elevee",  "Élevée")),
        };

        // ── 7-12 ans : version intermédiaire, scénario simplifié ────────────

        private static List<UrgenceEvaluationSection> BuildEnfant() => new()
        {
            Section("ideation_suicidaire", "1. Idéation suicidaire",
                "Questionnement direct possible avec vocabulaire adapté.",
                ("absente",  "Absente"),
                ("passive",  "Idées passives (\"j'aimerais ne plus être là\")"),
                ("active",   "Idées actives (\"je veux mourir\")")),

            Section("intentionnalite", "2. Intentionnalité",
                "L'enfant a-t-il l'intention de passer à l'acte ?",
                ("absente",     "Absente"),
                ("ambivalente", "Ambivalente"),
                ("affirmee",    "Affirmée")),

            Section("scenario", "3. Scénario / plan",
                "Moyen, lieu, date envisagés.",
                ("absent",   "Absent"),
                ("flou",     "Évoqué mais flou"),
                ("precise",  "Précisé (moyen / lieu / date)")),

            Section("acces_moyens", "4. Accès aux moyens",
                "Médicaments, objets tranchants, etc.",
                ("non",                    "Pas d'accès identifié"),
                ("oui_medicaments_parents","Oui — médicaments parents"),
                ("oui_autres",             "Oui — autres moyens accessibles")),

            Section("impulsivite", "5. Impulsivité",
                "Tempérament, troubles du contrôle des impulsions.",
                ("faible",  "Faible"),
                ("moderee", "Modérée"),
                ("forte",   "Forte")),

            Section("antecedents", "6. Antécédents",
                "TS, automutilations, idéation chronique, antécédents familiaux.",
                ("aucun",           "Aucun"),
                ("automutilation",  "Automutilations"),
                ("ts_anterieure",   "TS antérieure"),
                ("familiaux",       "Antécédents familiaux de suicide")),

            Section("facteurs_protecteurs", "7. Facteurs protecteurs",
                "Alliance familiale, lien thérapeutique, projets, soutiens.",
                ("absents",  "Absents / fragiles"),
                ("partiels", "Partiels"),
                ("solides",  "Solides")),

            Section("dangerosite", "8. Dangerosité globale",
                "Synthèse clinique : combinaison des facteurs ci-dessus.",
                ("faible",  "Faible"),
                ("moderee", "Modérée"),
                ("elevee",  "Élevée")),
        };

        // ── ≥ 13 ans : C-SSRS pleine version ────────────────────────────────

        private static List<UrgenceEvaluationSection> BuildAdolescent() => new()
        {
            Section("ideation_suicidaire", "1. Idéation suicidaire (C-SSRS)",
                "Du désir vague de \"ne plus être là\" jusqu'à l'idée active avec intention.",
                ("absente",          "Absente"),
                ("souhait_mort",     "Souhait de mort sans idée active"),
                ("idee_active",      "Idée suicidaire active sans plan"),
                ("idee_avec_plan",   "Idée active avec plan")),

            Section("intentionnalite", "2. Intentionnalité",
                "Intention de passer à l'acte (et non simple idée).",
                ("absente",     "Absente"),
                ("ambivalente", "Ambivalente"),
                ("affirmee",    "Affirmée")),

            Section("scenario", "3. Scénario / plan",
                "Moyen, lieu, date, séquence envisagée. Recherches internet ?",
                ("absent",                "Absent"),
                ("flou",                  "Évoqué, non spécifié"),
                ("precise",               "Précis (moyen / lieu / date)"),
                ("preparation_concrete",  "Préparation concrète (matériel, lettre, ...)")),

            Section("acces_moyens", "4. Accès aux moyens",
                "Médicaments accumulés, armes, points hauts, etc.",
                ("non",                  "Pas d'accès identifié"),
                ("oui_medicaments",      "Oui — médicaments accessibles"),
                ("oui_arme_objet",       "Oui — arme ou objet létal accessible"),
                ("oui_lieu",             "Oui — lieu (pont, train, hauteur)")),

            Section("impulsivite", "5. Impulsivité / consommations",
                "Toxiques, alcool, troubles du contrôle, conduites à risque.",
                ("faible",            "Faible"),
                ("moderee",           "Modérée"),
                ("forte",             "Forte"),
                ("forte_avec_toxiques","Forte + consommations actuelles")),

            Section("antecedents", "6. Antécédents",
                "TS, automutilations, idéation chronique, antécédents familiaux.",
                ("aucun",          "Aucun"),
                ("automutilation", "Automutilations"),
                ("ts_anterieure",  "TS antérieure"),
                ("ts_multiples",   "TS multiples"),
                ("familiaux",      "Antécédents familiaux de suicide")),

            Section("facteurs_protecteurs", "7. Facteurs protecteurs",
                "Alliance, soutiens familiaux et pairs, projets, lien thérapeutique.",
                ("absents",  "Absents / fragiles"),
                ("partiels", "Partiels"),
                ("solides",  "Solides")),

            Section("dangerosite", "8. Dangerosité globale",
                "Synthèse clinique intégrant l'ensemble des dimensions.",
                ("faible",  "Faible"),
                ("moderee", "Modérée"),
                ("elevee",  "Élevée"),
                ("imminent","Imminente")),
        };

        public static List<UrgenceActionItem> BuildPlanActions() => new()
        {
            new() { Key = "information_parents",  Label = "Information / convocation des parents" },
            new() { Key = "contrat_securite",     Label = "Contrat de sécurité (no-harm)" },
            new() { Key = "orientation_cmp",      Label = "Orientation CMP / consultation urgente" },
            new() { Key = "samu_urgences_ped",    Label = "SAMU / urgences pédiatriques" },
            new() { Key = "hospitalisation",      Label = "Hospitalisation" },
            new() { Key = "retrait_moyens",       Label = "Retrait des moyens accessibles (médic., armes, etc.)" },
            new() { Key = "suivi_rapproche",      Label = "Suivi rapproché (revoyure programmée)" },
        };

        // ── helpers ─────────────────────────────────────────────────────────

        private static UrgenceEvaluationSection Section(string key, string title, string help, params (string code, string label)[] choices)
        {
            var s = new UrgenceEvaluationSection
            {
                Key      = key,
                Title    = title,
                HelpText = help
            };
            foreach (var (code, label) in choices)
                s.Choices.Add(new UrgenceChoice { Code = code, Label = label });
            return s;
        }
    }
}
