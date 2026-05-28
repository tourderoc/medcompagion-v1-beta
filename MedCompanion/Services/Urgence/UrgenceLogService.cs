using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MedCompanion.Models.Urgences;

namespace MedCompanion.Services.Urgence
{
    /// <summary>
    /// Persistance immuable des signaux et évaluations d'urgence.
    /// Co-localisé avec les notes : `{patient}/{année}/notes/`.
    /// Convention :
    ///   - {date}_{time}_signal_{type}.md       ← détection automatique
    ///   - {date}_{time}_evaluation_{type}.md   ← évaluation médecin (étape ultérieure)
    /// </summary>
    public class UrgenceLogService
    {
        /// <summary>
        /// Écrit le signal détecté à côté de sa note source. Met à jour signal.SignalFilePath.
        /// </summary>
        public void WriteSignal(UrgenceSignal signal)
        {
            if (string.IsNullOrEmpty(signal.NoteSourcePath))
                throw new InvalidOperationException("NoteSourcePath obligatoire pour WriteSignal.");

            var notesDir = Path.GetDirectoryName(signal.NoteSourcePath)
                           ?? throw new InvalidOperationException("Impossible de localiser le dossier de la note source.");

            Directory.CreateDirectory(notesDir);

            var stamp    = signal.DetectionDate.ToString("yyyy-MM-dd_HHmmss");
            var fileName = $"{stamp}_signal_{Sanitize(signal.Type)}.md";
            var path     = Path.Combine(notesDir, fileName);

            // Si collision (même seconde), suffixer
            int n = 1;
            while (File.Exists(path))
            {
                fileName = $"{stamp}_signal_{Sanitize(signal.Type)}_{n}.md";
                path     = Path.Combine(notesDir, fileName);
                n++;
            }

            var sb = new StringBuilder();
            sb.AppendLine("---");
            sb.AppendLine("type: signal");
            sb.AppendLine($"urgence: {signal.Type}");
            sb.AppendLine($"patient: \"{Escape(signal.PatientNomComplet)}\"");
            sb.AppendLine($"detection_date: {signal.DetectionDate.ToString("o", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"detecteur: {signal.DetecteurName}");
            sb.AppendLine($"confidence: {signal.Confidence.ToString("0.000", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"note_source: \"{Escape(Path.GetFileName(signal.NoteSourcePath))}\"");
            sb.AppendLine($"medecin_action: pending");
            sb.AppendLine("passages:");
            foreach (var p in signal.Passages)
                sb.AppendLine($"  - \"{Escape(p)}\"");
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine($"# Signal d'urgence — {signal.Type}");
            sb.AppendLine();
            sb.AppendLine($"**Confidence détecteur :** {signal.Confidence:0.00}");
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(signal.Motif))
            {
                sb.AppendLine("## Motif");
                sb.AppendLine();
                sb.AppendLine(signal.Motif);
                sb.AppendLine();
            }
            if (signal.Passages.Any())
            {
                sb.AppendLine("## Passages relevés");
                sb.AppendLine();
                foreach (var p in signal.Passages)
                    sb.AppendLine($"> {p}");
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            signal.SignalFilePath = path;
        }

        /// <summary>
        /// Met à jour le YAML d'un signal existant pour tracer la décision médecin
        /// (ouvert_evaluation / ecarte / timeout). Réécrit le fichier en place.
        /// Reste immuable au sens "le contenu de détection ne change pas" — on ne touche que l'action.
        /// </summary>
        public void UpdateMedecinAction(string signalFilePath, UrgenceUserAction action, string motif = "")
        {
            if (!File.Exists(signalFilePath)) return;

            var text   = File.ReadAllText(signalFilePath, Encoding.UTF8);
            var actStr = action.ToString().ToLowerInvariant() switch
            {
                "ouvertevaluation" => "ouvert_evaluation",
                "ecarte"           => "ecarte",
                "timeout"          => "timeout",
                _                  => "pending"
            };

            var dateLine = $"medecin_action_date: {DateTime.Now.ToString("o", CultureInfo.InvariantCulture)}";
            var motifLn  = $"medecin_action_motif: \"{Escape(motif)}\"";

            text = ReplaceOrInsertYamlField(text, "medecin_action",      actStr);
            text = ReplaceOrInsertYamlField(text, "medecin_action_date", DateTime.Now.ToString("o", CultureInfo.InvariantCulture));
            text = ReplaceOrInsertYamlField(text, "medecin_action_motif", motif);

            File.WriteAllText(signalFilePath, text, Encoding.UTF8);
        }

        private static string ReplaceOrInsertYamlField(string text, string key, string value)
        {
            var lines    = text.Replace("\r\n", "\n").Split('\n').ToList();
            int dashes   = 0;
            int yamlEnd  = -1;
            int existing = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Trim() == "---")
                {
                    dashes++;
                    if (dashes == 2) { yamlEnd = i; break; }
                    continue;
                }
                if (dashes == 1 && lines[i].StartsWith(key + ":")) existing = i;
            }
            var newLine = $"{key}: \"{Escape(value)}\"";
            if (existing >= 0) lines[existing] = newLine;
            else if (yamlEnd >= 0) lines.Insert(yamlEnd, newLine);
            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// Persiste une évaluation médecin (immuable) à côté de la note source du signal.
        /// Renvoie le chemin du fichier créé.
        /// </summary>
        public string WriteEvaluation(
            UrgenceSignal signal,
            UrgenceRiskLevel riskLevel,
            System.Collections.Generic.IEnumerable<UrgenceEvaluationSection> sections,
            System.Collections.Generic.IEnumerable<UrgenceActionItem> planActions,
            int revoyureJours,
            string notesLibres,
            string medecin)
        {
            if (string.IsNullOrEmpty(signal.NoteSourcePath))
                throw new InvalidOperationException("Signal.NoteSourcePath manquant.");

            var notesDir = Path.GetDirectoryName(signal.NoteSourcePath)
                           ?? throw new InvalidOperationException("Dossier note source introuvable.");

            var now      = DateTime.Now;
            var stamp    = now.ToString("yyyy-MM-dd_HHmmss");
            var fileName = $"{stamp}_evaluation_{Sanitize(signal.Type)}.md";
            var path     = Path.Combine(notesDir, fileName);

            int n = 1;
            while (File.Exists(path))
            {
                fileName = $"{stamp}_evaluation_{Sanitize(signal.Type)}_v{n}.md";
                path     = Path.Combine(notesDir, fileName);
                n++;
            }

            var sb = new StringBuilder();
            sb.AppendLine("---");
            sb.AppendLine("type: evaluation");
            sb.AppendLine($"urgence: {signal.Type}");
            sb.AppendLine($"patient: \"{Escape(signal.PatientNomComplet)}\"");
            sb.AppendLine($"evaluation_date: {now.ToString("o", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"signal_lie: \"{Escape(Path.GetFileName(signal.SignalFilePath))}\"");
            sb.AppendLine($"medecin: \"{Escape(medecin)}\"");
            sb.AppendLine($"niveau_risque: {riskLevel.ToString().ToLowerInvariant()}");
            sb.AppendLine("checklist:");
            foreach (var s in sections)
                sb.AppendLine($"  {s.Key}: {s.SelectedChoiceCode ?? "non_renseigne"}");
            sb.AppendLine("plan_action:");
            foreach (var a in planActions)
                if (a.IsChecked) sb.AppendLine($"  - {a.Key}");
            sb.AppendLine($"revoyure_jours: {revoyureJours}");
            sb.AppendLine("---");
            sb.AppendLine();
            sb.AppendLine("# Évaluation — Risque suicidaire");
            sb.AppendLine();
            sb.AppendLine($"**Niveau de risque :** {FormatRisk(riskLevel)}");
            sb.AppendLine();

            sb.AppendLine("## Checklist clinique");
            sb.AppendLine();
            foreach (var s in sections)
            {
                var sel = s.Choices.Find(c => c.Code == s.SelectedChoiceCode);
                sb.AppendLine($"### {s.Title}");
                sb.AppendLine($"**Réponse :** {sel?.Label ?? "(non renseignée)"}");
                if (!string.IsNullOrWhiteSpace(s.FreeText))
                {
                    sb.AppendLine();
                    sb.AppendLine("**Précisions cliniques :**");
                    sb.AppendLine();
                    sb.AppendLine(s.FreeText.Trim());
                }
                sb.AppendLine();
            }

            sb.AppendLine("## Plan d'action");
            sb.AppendLine();
            bool anyAction = false;
            foreach (var a in planActions)
            {
                if (a.IsChecked) { sb.AppendLine($"- [x] {a.Label}"); anyAction = true; }
            }
            if (revoyureJours > 0) { sb.AppendLine($"- [x] Revoyure dans **{revoyureJours} jour(s)**"); anyAction = true; }
            if (!anyAction) sb.AppendLine("_Aucune action cochée._");

            if (!string.IsNullOrWhiteSpace(notesLibres))
            {
                sb.AppendLine();
                sb.AppendLine("## Notes libres du médecin");
                sb.AppendLine();
                sb.AppendLine(notesLibres.Trim());
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
            return path;
        }

        private static string FormatRisk(UrgenceRiskLevel r) => r switch
        {
            UrgenceRiskLevel.Faible       => "Faible",
            UrgenceRiskLevel.Modere       => "Modéré",
            UrgenceRiskLevel.Eleve        => "Élevé",
            _                             => "Non renseigné"
        };

        private static string Sanitize(string s) =>
            new string(s.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());

        private static string Escape(string s) =>
            (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
