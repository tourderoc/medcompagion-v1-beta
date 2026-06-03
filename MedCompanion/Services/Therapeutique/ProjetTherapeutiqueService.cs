using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MedCompanion.Models.Therapeutique;

namespace MedCompanion.Services.Therapeutique
{
    /// <summary>
    /// Persistance des Projets Thérapeutiques d'un patient. Stocke chaque version dans
    /// un fichier markdown avec frontmatter YAML :
    ///
    ///   patients/X_Y/projet_therapeutique/
    ///     projet_v1_2026-06-03.md     (validé)
    ///     projet_v2_2026-09-15.md     (validé)
    ///     projet_v3_brouillon.md      (en cours)
    ///
    /// Règles :
    /// - 1 seul brouillon à la fois par patient.
    /// - Validation : renomme `_brouillon.md` → `_YYYY-MM-DD.md` et fige le statut.
    /// - Versions validées : immuables (toute modif crée un nouveau brouillon v(N+1)).
    /// </summary>
    public class ProjetTherapeutiqueService
    {
        private readonly PathService _pathService;

        public ProjetTherapeutiqueService(PathService pathService)
        {
            _pathService = pathService;
        }

        public string GetRootDirectory(string patientNomComplet)
        {
            var dir = Path.Combine(_pathService.GetPatientRootDirectory(patientNomComplet), "projet_therapeutique");
            _pathService.EnsureDirectoryExists(dir);
            return dir;
        }

        // ─── Listing ────────────────────────────────────────────────────────

        public List<ProjetTherapeutiqueVersion> ListVersions(string patientNomComplet)
        {
            var result = new List<ProjetTherapeutiqueVersion>();
            var dir = GetRootDirectory(patientNomComplet);
            if (!Directory.Exists(dir)) return result;
            foreach (var path in Directory.GetFiles(dir, "projet_v*.md"))
            {
                var v = LoadMetadataOnly(path);
                if (v != null) result.Add(v);
            }
            return result.OrderByDescending(v => v.Version).ToList();
        }

        public ProjetTherapeutiqueVersion? GetBrouillon(string patientNomComplet)
            => ListVersions(patientNomComplet).FirstOrDefault(v => v.IsBrouillon);

        public ProjetTherapeutiqueVersion? GetDerniereValidee(string patientNomComplet)
            => ListVersions(patientNomComplet)
                .Where(v => v.IsValidee)
                .OrderByDescending(v => v.Version)
                .FirstOrDefault();

        private ProjetTherapeutiqueVersion? LoadMetadataOnly(string filePath)
        {
            try
            {
                var raw = File.ReadAllText(filePath, Encoding.UTF8);
                var (yaml, _) = SplitFrontmatter(raw);
                if (yaml == null) return null;

                var v = new ProjetTherapeutiqueVersion
                {
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                };
                v.Version        = ParseInt(yaml, "version") ?? 1;
                v.DateRedaction  = ParseDate(yaml, "date_redaction") ?? File.GetCreationTime(filePath);
                v.DateValidation = ParseDate(yaml, "date_validation");
                v.Psychiatre    = ParseString(yaml, "psychiatre") ?? "";
                v.DateReevaluationPrevue = ParseDate(yaml, "date_reevaluation_prevue");
                var statut = ParseString(yaml, "statut") ?? "brouillon";
                v.Statut = statut.Equals("validee", StringComparison.OrdinalIgnoreCase)
                    ? ProjetStatut.Validee : ProjetStatut.Brouillon;
                return v;
            }
            catch { return null; }
        }

        // ─── Load complet ───────────────────────────────────────────────────

        public ProjetTherapeutique? Load(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            try
            {
                var raw = File.ReadAllText(filePath, Encoding.UTF8);
                var (yaml, body) = SplitFrontmatter(raw);
                if (yaml == null) return null;

                var p = new ProjetTherapeutique { FilePath = filePath };
                ApplyMetadata(p, yaml);
                ApplySections(p, body);
                return p;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProjetTherapeutiqueService] Load échec {filePath} : {ex.Message}");
                return null;
            }
        }

        // ─── Save ───────────────────────────────────────────────────────────

        public void SaveBrouillon(ProjetTherapeutique p)
        {
            if (p.IsValidee)
                throw new InvalidOperationException("Une version validée est immuable. Créez un nouveau brouillon v(N+1).");

            if (string.IsNullOrEmpty(p.FilePath))
            {
                var dir = GetRootDirectory(p.PatientNomComplet);
                p.FilePath = Path.Combine(dir, $"projet_v{p.Version}_brouillon.md");
            }
            File.WriteAllText(p.FilePath, Serialize(p), Encoding.UTF8);
        }

        public string Validate(ProjetTherapeutique p)
        {
            if (p.IsValidee) throw new InvalidOperationException("Projet déjà validé.");
            p.DateValidation = DateTime.Now;
            p.Statut         = ProjetStatut.Validee;

            var dir         = GetRootDirectory(p.PatientNomComplet);
            var dateSlug    = p.DateValidation.Value.ToString("yyyy-MM-dd");
            var newFilePath = Path.Combine(dir, $"projet_v{p.Version}_{dateSlug}.md");
            File.WriteAllText(newFilePath, Serialize(p), Encoding.UTF8);

            if (!string.IsNullOrEmpty(p.FilePath)
                && !p.FilePath.Equals(newFilePath, StringComparison.OrdinalIgnoreCase)
                && File.Exists(p.FilePath))
            {
                try { File.Delete(p.FilePath); } catch { }
            }
            p.FilePath = newFilePath;
            return newFilePath;
        }

        // ─── Sérialisation ──────────────────────────────────────────────────

        private static string Serialize(ProjetTherapeutique p)
        {
            var sb = new StringBuilder();
            sb.AppendLine("---");
            sb.AppendLine($"version: {p.Version}");
            sb.AppendLine($"patient: {p.PatientNomComplet}");
            sb.AppendLine($"date_redaction: {p.DateRedaction.ToString("o", CultureInfo.InvariantCulture)}");
            if (p.DateValidation.HasValue)
                sb.AppendLine($"date_validation: {p.DateValidation.Value.ToString("o", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"statut: {(p.IsValidee ? "validee" : "brouillon")}");
            if (!string.IsNullOrWhiteSpace(p.Psychiatre))
                sb.AppendLine($"psychiatre: {EscapeYaml(p.Psychiatre)}");
            if (!string.IsNullOrWhiteSpace(p.VersionPrecedenteFichier))
                sb.AppendLine($"version_precedente: {p.VersionPrecedenteFichier}");
            if (!string.IsNullOrWhiteSpace(p.SyntheseGlobaleSourceFichier))
                sb.AppendLine($"synthese_globale_source: {p.SyntheseGlobaleSourceFichier}");
            if (p.DateReevaluationPrevue.HasValue)
                sb.AppendLine($"date_reevaluation_prevue: {p.DateReevaluationPrevue.Value:yyyy-MM-dd}");
            sb.AppendLine("---");
            sb.AppendLine();

            sb.AppendLine("## Objectifs prioritaires");
            sb.AppendLine();
            sb.AppendLine(string.IsNullOrWhiteSpace(p.ObjectifsPrioritaires) ? "_(à compléter)_" : p.ObjectifsPrioritaires.TrimEnd());
            sb.AppendLine();

            sb.AppendLine("## Prise en charge médicale");
            sb.AppendLine();
            AppendActionsMd(sb, p.ActionsMedicales);
            sb.AppendLine();

            sb.AppendLine("## Prise en charge psychologique");
            sb.AppendLine();
            AppendActionsMd(sb, p.ActionsPsychologiques);
            sb.AppendLine();

            sb.AppendLine("## Accompagnement développemental");
            sb.AppendLine();
            AppendActionsMd(sb, p.ActionsDeveloppementales);
            sb.AppendLine();

            sb.AppendLine("## Actions sur l'environnement");
            sb.AppendLine();
            AppendActionsMd(sb, p.ActionsEnvironnementales);
            sb.AppendLine();

            sb.AppendLine("## Ressources à soutenir");
            sb.AppendLine();
            sb.AppendLine(string.IsNullOrWhiteSpace(p.RessourcesASoutenir) ? "_(à compléter)_" : p.RessourcesASoutenir.TrimEnd());
            sb.AppendLine();

            sb.AppendLine("## Réévaluation prévue");
            sb.AppendLine();
            if (p.DateReevaluationPrevue.HasValue)
                sb.AppendLine($"**Date :** {p.DateReevaluationPrevue.Value:dd/MM/yyyy}");
            sb.AppendLine();
            sb.AppendLine(string.IsNullOrWhiteSpace(p.ReevaluationChecklist) ? "_(checklist à compléter)_" : p.ReevaluationChecklist.TrimEnd());
            sb.AppendLine();

            sb.AppendLine("## Co-construction avec la famille");
            sb.AppendLine();
            sb.AppendLine(string.IsNullOrWhiteSpace(p.CoConstructionFamille) ? "_(à compléter)_" : p.CoConstructionFamille.TrimEnd());
            sb.AppendLine();

            return sb.ToString();
        }

        private static void AppendActionsMd(StringBuilder sb, IEnumerable<ProjetAction> actions)
        {
            var list = actions.ToList();
            if (list.Count == 0)
            {
                sb.AppendLine("_(aucune action)_");
                return;
            }
            foreach (var a in list)
            {
                sb.AppendLine($"### {a.StatutIcon} {a.Libelle}");
                sb.AppendLine($"<!-- id: {a.Id} | statut: {a.Statut} | décidée: {a.DateDecision:yyyy-MM-dd} | dernier_changement: {a.DateDernierStatut:yyyy-MM-dd} -->");
                if (!string.IsNullOrWhiteSpace(a.Description))
                    sb.AppendLine($"- **Description :** {a.Description.Trim()}");
                if (!string.IsNullOrWhiteSpace(a.IndicateurReussite))
                    sb.AppendLine($"- **Indicateur de réussite :** {a.IndicateurReussite.Trim()}");
                if (!string.IsNullOrWhiteSpace(a.LienSyntheseSection))
                    sb.AppendLine($"- **Lien synthèse :** {a.LienSyntheseSection}");
                if (!string.IsNullOrWhiteSpace(a.MotifDernierChangement))
                    sb.AppendLine($"- **Motif dernier changement :** {a.MotifDernierChangement.Trim()}");
                sb.AppendLine();
            }
        }

        private static string EscapeYaml(string s)
        {
            if (s.Contains(':') || s.Contains('#') || s.Contains('\n'))
                return $"\"{s.Replace("\"", "\\\"")}\"";
            return s;
        }

        // ─── Parsing ────────────────────────────────────────────────────────

        private static (string? yaml, string body) SplitFrontmatter(string raw)
        {
            if (!raw.TrimStart().StartsWith("---")) return (null, raw);
            var firstEnd = raw.IndexOf('\n');
            if (firstEnd < 0) return (null, raw);
            var secondMarker = raw.IndexOf("---", firstEnd, StringComparison.Ordinal);
            if (secondMarker < 0) return (null, raw);
            var yaml = raw.Substring(firstEnd + 1, secondMarker - firstEnd - 1);
            var body = raw.Substring(secondMarker + 3).TrimStart('\r', '\n');
            return (yaml, body);
        }

        private static void ApplyMetadata(ProjetTherapeutique p, string yaml)
        {
            p.Version           = ParseInt(yaml, "version") ?? 1;
            p.PatientNomComplet = ParseString(yaml, "patient") ?? "";
            p.DateRedaction     = ParseDate(yaml, "date_redaction") ?? DateTime.Now;
            p.DateValidation    = ParseDate(yaml, "date_validation");
            p.Psychiatre        = ParseString(yaml, "psychiatre") ?? "";
            var statut          = ParseString(yaml, "statut") ?? "brouillon";
            p.Statut = statut.Equals("validee", StringComparison.OrdinalIgnoreCase)
                ? ProjetStatut.Validee : ProjetStatut.Brouillon;
            p.VersionPrecedenteFichier     = ParseString(yaml, "version_precedente");
            p.SyntheseGlobaleSourceFichier = ParseString(yaml, "synthese_globale_source");
            p.DateReevaluationPrevue       = ParseDate(yaml, "date_reevaluation_prevue");
        }

        private static void ApplySections(ProjetTherapeutique p, string body)
        {
            var lines = body.Replace("\r\n", "\n").Split('\n');
            string currentSection = "";
            string currentActionTitre = "";
            var bufText = new StringBuilder();
            ProjetAction? currentAction = null;
            var actionsBuffer = new List<ProjetAction>();

            void FlushText(string section)
            {
                var t = bufText.ToString().TrimEnd();
                if (t == "_(à compléter)_") t = "";
                if (t == "_(aucune action)_") t = "";
                if (t == "_(checklist à compléter)_") t = "";
                switch (section)
                {
                    case "Objectifs prioritaires":           p.ObjectifsPrioritaires   = t; break;
                    case "Ressources à soutenir":            p.RessourcesASoutenir     = t; break;
                    case "Réévaluation prévue":              p.ReevaluationChecklist   = ExtractChecklistText(t); break;
                    case "Co-construction avec la famille":  p.CoConstructionFamille   = t; break;
                }
                bufText.Clear();
            }

            void FlushActionsTo(ObservableCollection<ProjetAction> target)
            {
                FlushPendingAction();
                foreach (var a in actionsBuffer) target.Add(a);
                actionsBuffer.Clear();
            }

            void FlushPendingAction()
            {
                if (currentAction != null && !string.IsNullOrWhiteSpace(currentAction.Libelle))
                    actionsBuffer.Add(currentAction);
                currentAction = null;
            }

            foreach (var line in lines)
            {
                if (line.StartsWith("## "))
                {
                    // Flush selon la section qui se termine
                    if (IsActionsSection(currentSection))
                    {
                        switch (currentSection)
                        {
                            case "Prise en charge médicale":         FlushActionsTo(p.ActionsMedicales); break;
                            case "Prise en charge psychologique":    FlushActionsTo(p.ActionsPsychologiques); break;
                            case "Accompagnement développemental":   FlushActionsTo(p.ActionsDeveloppementales); break;
                            case "Actions sur l'environnement":      FlushActionsTo(p.ActionsEnvironnementales); break;
                        }
                    }
                    else if (!string.IsNullOrEmpty(currentSection))
                    {
                        FlushText(currentSection);
                    }
                    currentSection = line.Substring(3).Trim();
                    bufText.Clear();
                    actionsBuffer.Clear();
                    currentAction = null;
                    continue;
                }

                if (IsActionsSection(currentSection))
                {
                    if (line.StartsWith("### "))
                    {
                        FlushPendingAction();
                        currentActionTitre = line.Substring(4).Trim();
                        currentAction = new ProjetAction();
                        // Le titre peut commencer par une icône emoji statut → on l'enlève si présente
                        var libelle = currentActionTitre;
                        foreach (var emoji in new[] { "⚪", "🟡", "✅", "⛔" })
                        {
                            if (libelle.StartsWith(emoji)) { libelle = libelle.Substring(emoji.Length).TrimStart(); break; }
                        }
                        currentAction.Libelle = libelle;
                    }
                    else if (currentAction != null && line.TrimStart().StartsWith("<!-- id:"))
                    {
                        // Parser le commentaire HTML : <!-- id: ... | statut: ... | décidée: ... | dernier_changement: ... -->
                        var meta = line.Trim();
                        var idMatch       = Regex.Match(meta, @"id:\s*([0-9a-fA-F]+)");
                        var statutMatch   = Regex.Match(meta, @"statut:\s*(\w+)");
                        var decideeMatch  = Regex.Match(meta, @"décidée:\s*(\d{4}-\d{2}-\d{2})");
                        var dernierMatch  = Regex.Match(meta, @"dernier_changement:\s*(\d{4}-\d{2}-\d{2})");
                        if (idMatch.Success)      currentAction.Id = idMatch.Groups[1].Value;
                        if (statutMatch.Success)
                        {
                            if (Enum.TryParse<ActionStatut>(statutMatch.Groups[1].Value, true, out var st))
                            {
                                // bypass setter side-effects (date)
                                typeof(ProjetAction).GetField("_statut", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.SetValue(currentAction, st);
                            }
                        }
                        if (decideeMatch.Success && DateTime.TryParse(decideeMatch.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d1)) currentAction.DateDecision = d1;
                        if (dernierMatch.Success && DateTime.TryParse(dernierMatch.Groups[1].Value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d2)) currentAction.DateDernierStatut = d2;
                    }
                    else if (currentAction != null && line.TrimStart().StartsWith("- **"))
                    {
                        var clean = line.TrimStart();
                        if (clean.StartsWith("- **Description :**"))             currentAction.Description            = clean.Substring("- **Description :**".Length).Trim();
                        else if (clean.StartsWith("- **Indicateur de réussite :**")) currentAction.IndicateurReussite = clean.Substring("- **Indicateur de réussite :**".Length).Trim();
                        else if (clean.StartsWith("- **Lien synthèse :**"))      currentAction.LienSyntheseSection    = clean.Substring("- **Lien synthèse :**".Length).Trim();
                        else if (clean.StartsWith("- **Motif dernier changement :**")) currentAction.MotifDernierChangement = clean.Substring("- **Motif dernier changement :**".Length).Trim();
                    }
                }
                else
                {
                    bufText.AppendLine(line);
                }
            }

            // Final flush
            if (IsActionsSection(currentSection))
            {
                switch (currentSection)
                {
                    case "Prise en charge médicale":         FlushActionsTo(p.ActionsMedicales); break;
                    case "Prise en charge psychologique":    FlushActionsTo(p.ActionsPsychologiques); break;
                    case "Accompagnement développemental":   FlushActionsTo(p.ActionsDeveloppementales); break;
                    case "Actions sur l'environnement":      FlushActionsTo(p.ActionsEnvironnementales); break;
                }
            }
            else if (!string.IsNullOrEmpty(currentSection))
            {
                FlushText(currentSection);
            }
        }

        private static bool IsActionsSection(string s)
            => s == "Prise en charge médicale"
            || s == "Prise en charge psychologique"
            || s == "Accompagnement développemental"
            || s == "Actions sur l'environnement";

        /// <summary>
        /// Le bloc "Réévaluation prévue" contient une ligne "**Date :** ..." puis la
        /// checklist en texte libre. On retourne uniquement la checklist (le reste est
        /// déjà dans les métadonnées).
        /// </summary>
        private static string ExtractChecklistText(string s)
        {
            var lines = s.Replace("\r\n", "\n").Split('\n');
            var sb = new StringBuilder();
            foreach (var l in lines)
            {
                if (l.TrimStart().StartsWith("**Date :**")) continue;
                sb.AppendLine(l);
            }
            return sb.ToString().Trim();
        }

        private static string? ParseString(string yaml, string key)
        {
            var m = Regex.Match(yaml, $@"^\s*{Regex.Escape(key)}\s*:\s*(.+?)\s*$", RegexOptions.Multiline);
            if (!m.Success) return null;
            var val = m.Groups[1].Value.Trim();
            if (val.StartsWith("\"") && val.EndsWith("\"") && val.Length >= 2)
                val = val.Substring(1, val.Length - 2).Replace("\\\"", "\"");
            return val;
        }

        private static int? ParseInt(string yaml, string key)
        {
            var s = ParseString(yaml, key);
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
        }

        private static DateTime? ParseDate(string yaml, string key)
        {
            var s = ParseString(yaml, key);
            return DateTime.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : null;
        }
    }
}
