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

            if (Evaluations.Count > 0)
            {
                sb.AppendLine("[ÉVALUATIONS] Bilan Final, Cartographies Enfant & Environnement");
                sb.AppendLine();
                foreach (var e in Evaluations)
                {
                    sb.AppendLine($"--- Évaluation clôturée {(e.DateCloture.HasValue ? $"le {e.DateCloture.Value:dd/MM/yyyy}" : "")} ---");
                    sb.AppendLine(GetEvaluationBodyWithoutFrontmatter(e.Content).Trim());
                    sb.AppendLine();
                }
            }

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
