using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using MedCompanion.Models.Evaluations;

namespace MedCompanion.Services.Restitutions
{
    /// <summary>
    /// Vue agrégée et structurée du « dossier bleu » d'un patient au moment où le médecin
    /// démarre la rédaction d'un Dossier de Restitution. Contient uniquement du contenu
    /// déjà validé par le médecin — pas de PDF brut, pas d'OCR, pas de pré-digest LLM.
    ///
    /// Sert d'entrée commune aux 8 préremplissages de blocs : tous les blocs reçoivent
    /// la même base, ce qui assure la cohérence inter-blocs (Med ne se contredit pas).
    /// </summary>
    public class DossierReading
    {
        public string   PatientNomComplet { get; init; } = "";
        public DateTime ReadAt            { get; init; } = DateTime.Now;

        /// <summary>Contenu de info_patient/patient.json (identité administrative).</summary>
        public string PatientJson { get; init; } = "";

        /// <summary>
        /// Note de 1ère consultation (référentiel d'identité riche : école, classe,
        /// année scolaire, motif initial, contexte familial, antécédents). Identifiée
        /// dans le YAML par type == "consultation-premiere".
        /// </summary>
        public string PremiereConsultation { get; init; } = "";

        /// <summary>Notes de consultation suivantes, triées chronologiquement (plus récentes en premier).</summary>
        public List<NoteEntry> NotesConsultation { get; init; } = new();

        /// <summary>Évaluations clôturées (Bilan Final, Cartographies Enfant & Environnement).</summary>
        public List<EvaluationEntry> Evaluations { get; init; } = new();

        /// <summary>
        /// Date du 1er entretien. Elle fait partie de la période d'évaluation : trois blocs du
        /// dossier — contexte familial, antécédents, situation actuelle — n'en viennent que de là.
        /// </summary>
        public DateTime? DatePremierEntretien { get; init; }

        /// <summary>
        /// Dates des séances d'évaluation du nouveau parcours : cartographie de l'enfant,
        /// environnement &amp; évaluation ciblée. Vide pour les dossiers évalués en V1, qui
        /// retombent alors sur <see cref="Evaluations"/>.
        /// </summary>
        public List<DateTime> DatesSeancesEvaluation { get; init; } = new();

        /// <summary>
        /// Les deux séances d'évaluation du nouveau parcours (cartographie de l'enfant,
        /// environnement &amp; évaluation ciblée), déjà mises en texte par
        /// <see cref="Services.Evaluations.EvaluationV2ContextService"/>. Vide si le patient n'a
        /// pas encore ces séances — pas une erreur, un dossier évalué en V1 pur.
        /// </summary>
        public string EvaluationsV2Contexte { get; init; } = "";

        /// <summary>Synthèse globale Med (transversale, fichier synthese/synthese.md).</summary>
        public string SyntheseGlobaleMed { get; init; } = "";

        /// <summary>Dernière Synthèse Globale V0.5 validée (synthese_globale/*.md).</summary>
        public string SyntheseGlobaleV05 { get; init; } = "";

        /// <summary>Dernier Projet Thérapeutique validé (projet_therapeutique/*.md).</summary>
        public string ProjetTherapeutique { get; init; } = "";

        /// <summary>Synthèses Med des documents importés (jamais le PDF brut).</summary>
        public List<string> SynthesesDocuments { get; init; } = new();

        /// <summary>Méta-synthèse Med de l'ensemble des documents importés.</summary>
        public string SyntheseGlobaleDocuments { get; init; } = "";

        /// <summary>
        /// Dernière Étape 3 « Cartographie de l'enfant » de la dernière évaluation clôturée
        /// (ou validée si dispo). Donne directement accès aux scores et niveaux par sphère
        /// pour les sections Cartographie du Dossier de Restitution — sans avoir à reparser
        /// le YAML des fichiers d'évaluation. null si aucune évaluation utilisable.
        /// </summary>
        public CartographieEnfant? LatestCartographieEnfant { get; init; }

        /// <summary>
        /// Dernière Étape 4 « Cartographie de l'environnement » de la dernière évaluation clôturée.
        /// Utilisée pour le rendu SVG des feuilles dans le Dossier de Restitution.
        /// </summary>
        public CartographieEnvironnement? LatestCartographieEnvironnement { get; init; }

        /// <summary>
        /// Bilan Final (Étape 5) de la dernière évaluation clôturée : diagnostics retenus,
        /// éléments en faveur, différentiels écartés, niveau de certitude, synthèse intégrative.
        /// null si aucune évaluation clôturée ou si le BilanFinal est vide.
        /// </summary>
        public BilanFinal? LatestBilanFinal { get; init; }

        /// <summary>
        /// Dernière cartographie V2 (nouveau parcours) VERSÉE AU DOSSIER : les 5 scores du
        /// questionnaire parents (avec leurs réponses item par item et l'informateur) et les
        /// 3 profils observés par le médecin. Source structurée des blocs carto_s* du Dossier
        /// de Restitution — prioritaire sur <see cref="LatestCartographieEnfant"/> (V1).
        /// null si aucune carte versée.
        /// </summary>
        public MedCompanion.Services.Evaluations.CartographieV2? LatestCartographieV2 { get; init; }

        /// <summary>
        /// Dernière séance 3 « Environnement &amp; évaluation ciblée » portant des données
        /// d'environnement (cotations médecin ou réponses de la feuille parents). Source
        /// structurée des blocs env_edu_* — prioritaire sur
        /// <see cref="LatestCartographieEnvironnement"/> (V1). null si aucune séance.
        /// </summary>
        public MedCompanion.Services.Evaluations.SeanceEnvironnement? LatestSeanceEnvironnement { get; init; }

        /// <summary>
        /// Rendu textuel structuré du dossier pour injection dans un prompt LLM.
        /// Ordre clinique : qui est l'enfant → ce qu'on a appris au 1er entretien → suite →
        /// évaluations → synthèses → projet → sources externes.
        /// </summary>
        public string RenderForLlm()
        {
            var sb = new StringBuilder();
            sb.AppendLine("== DOSSIER PATIENT — SOURCES VALIDÉES ==");
            sb.AppendLine();

            AppendSection(sb, "IDENTITÉ ADMIN (patient.json)", PatientJson);
            AppendSection(sb, "1ÈRE CONSULTATION — référentiel d'identité contextuelle (école, motif, famille, antécédents)", PremiereConsultation);

            if (NotesConsultation.Count > 0)
            {
                sb.AppendLine("[CONSULTATIONS SUIVANTES] (chronologique, plus récente en premier)");
                sb.AppendLine();
                foreach (var n in NotesConsultation)
                {
                    sb.AppendLine($"--- Note du {n.Date:dd/MM/yyyy}{(string.IsNullOrEmpty(n.Type) ? "" : $" ({n.Type})")} ---");
                    sb.AppendLine(n.Content.Trim());
                    sb.AppendLine();
                }
            }

            // La plus récente évaluation clôturée est celle qui sert de base à CE dossier de
            // restitution (Cartographie, Bilan Final ci-dessous) — pas un antécédent. On ne
            // la réinjecte pas ici pour éviter que le LLM ne se cite lui-même comme "bilan
            // antérieur" dans la section Antécédents/Bilans réalisés.
            var evaluationsAnterieures = Evaluations.Count > 1 ? Evaluations.Skip(1).ToList() : new List<EvaluationEntry>();
            if (evaluationsAnterieures.Count > 0)
            {
                sb.AppendLine("[ÉVALUATIONS ANTÉRIEURES] (hors évaluation courante, déjà couverte par [BILAN FINAL] ci-dessous)");
                sb.AppendLine();
                foreach (var e in evaluationsAnterieures)
                {
                    sb.AppendLine($"--- Évaluation clôturée {(e.DateCloture.HasValue ? $"le {e.DateCloture.Value:dd/MM/yyyy}" : "")} ---");
                    sb.AppendLine(GetEvaluationBodyWithoutFrontmatter(e.Content).Trim());
                    sb.AppendLine();
                }
            }

            if (LatestBilanFinal != null)
            {
                sb.AppendLine("[BILAN FINAL — Étape 5 (diagnostics, certitude, éléments en faveur, différentiels)]");
                sb.AppendLine();
                if (LatestBilanFinal.DiagnosticsRetenus.Count > 0)
                {
                    sb.AppendLine("Diagnostics retenus :");
                    foreach (var d in LatestBilanFinal.DiagnosticsRetenus)
                        sb.AppendLine($"  • {d.Value}");
                    sb.AppendLine($"  Niveau de certitude : {LatestBilanFinal.Certitude}");
                    sb.AppendLine();
                }
                if (LatestBilanFinal.ElementsEnFaveur.Count > 0)
                {
                    sb.AppendLine("Éléments cliniques en faveur :");
                    foreach (var e in LatestBilanFinal.ElementsEnFaveur)
                        sb.AppendLine($"  • {e.Value}");
                    sb.AppendLine();
                }
                if (LatestBilanFinal.DiagnosticsEcartes.Count > 0)
                {
                    sb.AppendLine("Diagnostics différentiels écartés :");
                    foreach (var ec in LatestBilanFinal.DiagnosticsEcartes)
                        sb.AppendLine($"  • {ec.Label} — {ec.Motif}");
                    sb.AppendLine();
                }
                if (!string.IsNullOrWhiteSpace(LatestBilanFinal.SyntheseIntegrative))
                {
                    sb.AppendLine("Synthèse intégrative (rédigée par le médecin) :");
                    sb.AppendLine(LatestBilanFinal.SyntheseIntegrative.Trim());
                    sb.AppendLine();
                }
            }

            // CARTOGRAPHIES — jusqu'ici absentes du texte transmis au modèle. Elles étaient
            // chargées (LatestCartographieEnfant, LatestCartographieEnvironnement) mais ne
            // servaient qu'aux DESSINS des pages 8-21 : le bloc « Situation actuelle », qui les
            // cite pourtant comme sa source principale, les décrivait sans jamais les voir.
            AppendCartographieEnfantV1(sb, LatestCartographieEnfant);
            AppendCartographieEnvironnementV1(sb, LatestCartographieEnvironnement);
            AppendSection(sb, "SÉANCES D'ÉVALUATION (nouveau parcours — cartographie de l'enfant, environnement & évaluation ciblée)",
                          EvaluationsV2Contexte);

            AppendSection(sb, "SYNTHÈSE GLOBALE MED (synthese.md transversale)",            SyntheseGlobaleMed);
            AppendSection(sb, "SYNTHÈSE GLOBALE V0.5 (dernière version validée)",          SyntheseGlobaleV05);
            AppendSection(sb, "PROJET THÉRAPEUTIQUE (dernière version validée)",            ProjetTherapeutique);
            AppendSection(sb, "MÉTA-SYNTHÈSE DES DOCUMENTS IMPORTÉS",                       SyntheseGlobaleDocuments);

            if (SynthesesDocuments.Count > 0)
            {
                sb.AppendLine("[SYNTHÈSES INDIVIDUELLES DES DOCUMENTS IMPORTÉS]");
                sb.AppendLine();
                for (int i = 0; i < SynthesesDocuments.Count; i++)
                {
                    sb.AppendLine($"--- Document #{i + 1} ---");
                    sb.AppendLine(SynthesesDocuments[i].Trim());
                    sb.AppendLine();
                }
            }

            sb.AppendLine("== FIN DOSSIER ==");
            return sb.ToString();
        }

        /// <summary>
        /// Textifie la cartographie de l'enfant V1 (chenille 6 segments + 3 profils) — même
        /// format que celui déjà éprouvé dans la Synthèse Globale, pour que les deux documents
        /// lisent la même chose de la même façon.
        ///
        /// Rendue dès qu'un segment porte un score, pas seulement si <c>IsValidated</c> : une
        /// cartographie en cours d'observation vaut mieux qu'une absence totale pour le bloc
        /// « Situation actuelle », qui en dépend.
        /// </summary>
        private static void AppendCartographieEnfantV1(StringBuilder sb, Models.Evaluations.CartographieEnfant? c)
        {
            if (c == null) return;
            if (!c.IsValidated && c.Attachement.Score == 0 && c.Langage.Score == 0
                && c.Emotions.Score == 0 && c.Imaginaire.Score == 0 && c.Pensee.Score == 0
                && !c.Temperament.IsRenseigne && !c.Attention.IsRenseigne)
                return;

            sb.AppendLine("[CARTOGRAPHIE DE L'ENFANT — V1 (scores sur 6, sauf tempérament et attention sur 5)]");
            sb.AppendLine();
            sb.AppendLine($"  Attachement : {c.Attachement.Score}/6");
            sb.AppendLine($"  Langage : {c.Langage.Score}/6");
            sb.AppendLine($"  Émotions : {c.Emotions.Score}/6");
            sb.AppendLine($"  Imaginaire : {c.Imaginaire.Score}/6");
            sb.AppendLine($"  Pensée : {c.Pensee.Score}/6");
            if (c.Temperament.IsRenseigne)
                sb.AppendLine($"  Tempérament : activité={c.Temperament.NiveauActivite}/5, régularité={c.Temperament.Regularite}/5, "
                             + $"réactivité={c.Temperament.ReactiviteSensorielle}/5, intensité={c.Temperament.IntensiteEmotionnelle}/5, "
                             + $"adaptabilité={c.Temperament.Adaptabilite}/5");
            if (c.Attention.IsRenseigne)
                sb.AppendLine($"  Attention & FE : soutenue={c.Attention.AttentionSoutenue}/5, sélective={c.Attention.AttentionSelective}/5, "
                             + $"divisée={c.Attention.AttentionDivisee}/5, inhibition={c.Attention.Inhibition}/5, "
                             + $"planification={c.Attention.Planification}/5, flexibilité={c.Attention.FlexibiliteAttentionnelle}/5");
            sb.AppendLine();
        }

        /// <summary>Textifie la cartographie de l'environnement V1 (5 feuilles, couleur calculée).</summary>
        private static void AppendCartographieEnvironnementV1(StringBuilder sb, Models.Evaluations.CartographieEnvironnement? e)
        {
            if (e == null) return;

            bool Renseignee(Models.Evaluations.FeuilleEnvironnement f) => f.NervureCentrale.Score > 0;
            if (!e.IsValidated && !Renseignee(e.Famille) && !Renseignee(e.EcolePairs) && !Renseignee(e.EcransMedias)
                && !Renseignee(e.ValeursSocietales) && !Renseignee(e.CadreEducatif))
                return;

            sb.AppendLine("[CARTOGRAPHIE DE L'ENVIRONNEMENT — V1 (5 feuilles)]");
            sb.AppendLine();
            void Feuille(string label, Models.Evaluations.FeuilleEnvironnement f)
                => sb.AppendLine($"  {label} : {Services.Evaluations.EnvironnementScoringService.CalculerFeuille(f)}");
            Feuille("Famille",           e.Famille);
            Feuille("École & Pairs",     e.EcolePairs);
            Feuille("Écrans & Médias",   e.EcransMedias);
            Feuille("Valeurs sociétales", e.ValeursSocietales);
            Feuille("Cadre éducatif",    e.CadreEducatif);
            sb.AppendLine();
        }

        private static string GetEvaluationBodyWithoutFrontmatter(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return "";
            var trimmed = content.TrimStart();
            if (!trimmed.StartsWith("---")) return content;

            var firstLineEnd = trimmed.IndexOf('\n');
            if (firstLineEnd < 0) return content;

            var secondMarker = trimmed.IndexOf("---", firstLineEnd + 1, StringComparison.Ordinal);
            if (secondMarker < 0) return content;

            return trimmed.Substring(secondMarker + 3).TrimStart('\r', '\n');
        }

        private static void AppendSection(StringBuilder sb, string title, string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return;
            sb.AppendLine($"[{title}]");
            sb.AppendLine();
            sb.AppendLine(content.Trim());
            sb.AppendLine();
        }
    }

    /// <summary>Une note de consultation lue depuis YYYY/notes/*.md.</summary>
    public class NoteEntry
    {
        public DateTime Date    { get; init; }
        public string   Type    { get; init; } = "";   // "consultation-premiere", "suivi", "evaluation", etc.
        public string   Content { get; init; } = "";   // corps sans YAML frontmatter
        public string   FilePath{ get; init; } = "";
    }

    /// <summary>Une évaluation clôturée lue depuis evaluations/*.md.</summary>
    public class EvaluationEntry
    {
        public DateTime? DateCloture { get; init; }
        public string    Content     { get; init; } = "";   // contenu .md complet
        public string    FilePath    { get; init; } = "";
    }
}
