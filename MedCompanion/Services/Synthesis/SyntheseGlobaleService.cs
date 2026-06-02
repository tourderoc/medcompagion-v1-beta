using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MedCompanion.Models.Synthesis;

namespace MedCompanion.Services.Synthesis
{
    /// <summary>
    /// Persistance des Synthèses Globales d'un patient. Stocke chaque version dans un
    /// fichier markdown indépendant avec frontmatter YAML :
    ///
    ///   patients/X_Y/synthese_globale/
    ///     synthese_v1_2026-06-02.md     (validée)
    ///     synthese_v2_2026-09-15.md     (validée)
    ///     synthese_v3_brouillon.md      (en cours)
    ///
    /// Règles :
    /// - 1 seul brouillon à la fois par patient (fichier `synthese_v{N}_brouillon.md`).
    /// - À la validation : renommé en `synthese_v{N}_{date}.md` + statut figé.
    /// - Versions validées : immuables (toute modif crée un nouveau brouillon v(N+1)).
    /// </summary>
    public class SyntheseGlobaleService
    {
        private readonly PathService _pathService;

        public SyntheseGlobaleService(PathService pathService)
        {
            _pathService = pathService;
        }

        // ─── Chemins ────────────────────────────────────────────────────────

        /// <summary>Racine du dossier de synthèses globales pour un patient.</summary>
        public string GetRootDirectory(string patientNomComplet)
        {
            var dir = Path.Combine(_pathService.GetPatientRootDirectory(patientNomComplet), "synthese_globale");
            _pathService.EnsureDirectoryExists(dir);
            return dir;
        }

        // ─── Listing ────────────────────────────────────────────────────────

        /// <summary>
        /// Liste toutes les versions (validées + brouillon) du patient, triées par version
        /// décroissante (la plus récente en premier). Lit le frontmatter sans charger le
        /// contenu complet.
        /// </summary>
        public List<SyntheseGlobaleVersion> ListVersions(string patientNomComplet)
        {
            var result = new List<SyntheseGlobaleVersion>();
            var dir = GetRootDirectory(patientNomComplet);
            if (!Directory.Exists(dir)) return result;

            foreach (var path in Directory.GetFiles(dir, "synthese_v*.md"))
            {
                var v = LoadMetadataOnly(path);
                if (v != null) result.Add(v);
            }
            return result.OrderByDescending(v => v.Version).ToList();
        }

        /// <summary>Retourne le brouillon courant du patient, ou null s'il n'y en a pas.</summary>
        public SyntheseGlobaleVersion? GetBrouillon(string patientNomComplet)
            => ListVersions(patientNomComplet).FirstOrDefault(v => v.IsBrouillon);

        /// <summary>Retourne la dernière version validée (source de vérité), ou null.</summary>
        public SyntheseGlobaleVersion? GetDerniereValidee(string patientNomComplet)
            => ListVersions(patientNomComplet)
                .Where(v => v.IsValidee)
                .OrderByDescending(v => v.Version)
                .FirstOrDefault();

        // ─── Load ───────────────────────────────────────────────────────────

        /// <summary>Charge une synthèse complète depuis un fichier .md (frontmatter + sections).</summary>
        public SyntheseGlobale? Load(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            try
            {
                var raw = File.ReadAllText(filePath, Encoding.UTF8);
                var (yaml, body) = SplitFrontmatter(raw);
                if (yaml == null) return null;

                var syn = new SyntheseGlobale { FilePath = filePath };
                ApplyMetadata(syn, yaml);
                ApplySections(syn, body);
                return syn;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SyntheseGlobaleService] Load échec {filePath} : {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Lit uniquement les métadonnées (frontmatter) — utilisé par ListVersions
        /// pour éviter de charger tout le markdown.
        /// </summary>
        private SyntheseGlobaleVersion? LoadMetadataOnly(string filePath)
        {
            try
            {
                var raw = File.ReadAllText(filePath, Encoding.UTF8);
                var (yaml, _) = SplitFrontmatter(raw);
                if (yaml == null) return null;

                var v = new SyntheseGlobaleVersion
                {
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                };
                v.Version        = ParseInt(yaml, "version")       ?? 1;
                v.DateRedaction  = ParseDate(yaml, "date_redaction") ?? File.GetCreationTime(filePath);
                v.DateValidation = ParseDate(yaml, "date_validation");
                v.Psychiatre    = ParseString(yaml, "psychiatre") ?? "";
                var statut = ParseString(yaml, "statut") ?? "brouillon";
                v.Statut = statut.Equals("validee", StringComparison.OrdinalIgnoreCase)
                    ? SyntheseStatut.Validee : SyntheseStatut.Brouillon;
                return v;
            }
            catch { return null; }
        }

        // ─── Save ───────────────────────────────────────────────────────────

        /// <summary>
        /// Sauvegarde un brouillon. Si la synthèse n'a pas encore de FilePath, calcule
        /// un nom de fichier `synthese_v{N}_brouillon.md` dans le dossier du patient.
        /// Une version VALIDÉE ne doit pas être re-saved par cette méthode — utiliser Validate().
        /// </summary>
        public void SaveBrouillon(SyntheseGlobale synthese)
        {
            if (synthese.IsValidee)
                throw new InvalidOperationException("Une version validée est immuable. Créez un nouveau brouillon v(N+1).");

            if (string.IsNullOrEmpty(synthese.FilePath))
            {
                var dir = GetRootDirectory(synthese.PatientNomComplet);
                synthese.FilePath = Path.Combine(dir, $"synthese_v{synthese.Version}_brouillon.md");
            }

            File.WriteAllText(synthese.FilePath, Serialize(synthese), Encoding.UTF8);
        }

        /// <summary>
        /// Valide un brouillon : fige la date de validation, change le statut, et renomme
        /// le fichier en `synthese_v{N}_{YYYY-MM-DD}.md`. Retourne le nouveau chemin.
        /// </summary>
        public string Validate(SyntheseGlobale synthese)
        {
            if (synthese.IsValidee)
                throw new InvalidOperationException("Synthèse déjà validée.");

            synthese.DateValidation = DateTime.Now;
            synthese.Statut         = SyntheseStatut.Validee;

            var dir         = GetRootDirectory(synthese.PatientNomComplet);
            var dateSlug    = synthese.DateValidation.Value.ToString("yyyy-MM-dd");
            var newFilePath = Path.Combine(dir, $"synthese_v{synthese.Version}_{dateSlug}.md");

            // Écrire le nouveau fichier avec les métadonnées validées
            File.WriteAllText(newFilePath, Serialize(synthese), Encoding.UTF8);

            // Supprimer l'ancien fichier brouillon s'il existait sous un nom différent
            if (!string.IsNullOrEmpty(synthese.FilePath) &&
                !synthese.FilePath.Equals(newFilePath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(synthese.FilePath))
            {
                try { File.Delete(synthese.FilePath); } catch { /* meilleur effort */ }
            }

            synthese.FilePath = newFilePath;
            return newFilePath;
        }

        // ─── Sérialisation ──────────────────────────────────────────────────

        private static string Serialize(SyntheseGlobale s)
        {
            var sb = new StringBuilder();
            sb.AppendLine("---");
            sb.AppendLine($"version: {s.Version}");
            sb.AppendLine($"patient: {s.PatientNomComplet}");
            sb.AppendLine($"date_redaction: {s.DateRedaction.ToString("o", CultureInfo.InvariantCulture)}");
            if (s.DateValidation.HasValue)
                sb.AppendLine($"date_validation: {s.DateValidation.Value.ToString("o", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"statut: {(s.IsValidee ? "validee" : "brouillon")}");
            if (!string.IsNullOrWhiteSpace(s.Psychiatre))
                sb.AppendLine($"psychiatre: {EscapeYamlString(s.Psychiatre)}");
            if (!string.IsNullOrWhiteSpace(s.VersionPrecedenteFichier))
                sb.AppendLine($"version_precedente: {s.VersionPrecedenteFichier}");
            sb.AppendLine($"increments_depuis_revision_majeure: {s.IncrementsDepuisRevisionMajeure}");
            sb.AppendLine("sources:");
            sb.Append("  evaluations: [");
            sb.Append(string.Join(", ", s.SourcesEvaluations.Select(e => $"\"{e}\"")));
            sb.AppendLine("]");
            sb.AppendLine($"  notes_consultations: {s.SourcesNombreNotes}");
            sb.AppendLine($"  documents: {s.SourcesNombreDocuments}");
            sb.AppendLine("---");
            sb.AppendLine();
            foreach (var section in s.Sections)
            {
                sb.AppendLine($"## {section.Titre}");
                sb.AppendLine();
                if (string.IsNullOrWhiteSpace(section.Contenu))
                    sb.AppendLine("_(Section vide — à compléter)_");
                else
                    sb.AppendLine(section.Contenu.TrimEnd());
                sb.AppendLine();
            }
            return sb.ToString();
        }

        private static string EscapeYamlString(string s)
        {
            if (s.Contains(':') || s.Contains('#') || s.Contains('\n'))
                return $"\"{s.Replace("\"", "\\\"")}\"";
            return s;
        }

        // ─── Parsing frontmatter ────────────────────────────────────────────

        private static (string? yaml, string body) SplitFrontmatter(string raw)
        {
            if (!raw.TrimStart().StartsWith("---")) return (null, raw);
            var firstEnd  = raw.IndexOf('\n');
            if (firstEnd < 0) return (null, raw);
            var secondMarker = raw.IndexOf("---", firstEnd, StringComparison.Ordinal);
            if (secondMarker < 0) return (null, raw);
            var yaml = raw.Substring(firstEnd + 1, secondMarker - firstEnd - 1);
            var body = raw.Substring(secondMarker + 3).TrimStart('\r', '\n');
            return (yaml, body);
        }

        private static void ApplyMetadata(SyntheseGlobale s, string yaml)
        {
            s.Version           = ParseInt(yaml, "version") ?? 1;
            s.PatientNomComplet = ParseString(yaml, "patient") ?? "";
            s.DateRedaction     = ParseDate(yaml, "date_redaction") ?? DateTime.Now;
            s.DateValidation    = ParseDate(yaml, "date_validation");
            s.Psychiatre        = ParseString(yaml, "psychiatre") ?? "";
            var statut          = ParseString(yaml, "statut") ?? "brouillon";
            s.Statut = statut.Equals("validee", StringComparison.OrdinalIgnoreCase)
                ? SyntheseStatut.Validee : SyntheseStatut.Brouillon;
            s.VersionPrecedenteFichier = ParseString(yaml, "version_precedente");
            s.IncrementsDepuisRevisionMajeure = ParseInt(yaml, "increments_depuis_revision_majeure") ?? 0;
            s.SourcesNombreNotes      = ParseInt(yaml, "notes_consultations") ?? 0;
            s.SourcesNombreDocuments  = ParseInt(yaml, "documents")           ?? 0;
            s.SourcesEvaluations      = ParseStringList(yaml, "evaluations");
        }

        private static void ApplySections(SyntheseGlobale s, string body)
        {
            // Parser markdown : chaque section commence par "## {Titre}"
            var lines = body.Replace("\r\n", "\n").Split('\n');
            SyntheseSection? current = null;
            var buf = new StringBuilder();

            void FlushCurrent()
            {
                if (current == null) return;
                var contenu = buf.ToString().TrimEnd('\r', '\n');
                if (contenu == "_(Section vide — à compléter)_") contenu = "";
                current.Contenu = contenu;
                buf.Clear();
            }

            foreach (var line in lines)
            {
                if (line.StartsWith("## "))
                {
                    FlushCurrent();
                    var titre = line.Substring(3).Trim();
                    current = s.Sections.FirstOrDefault(sec =>
                        sec.Titre.Equals(titre, StringComparison.OrdinalIgnoreCase));
                }
                else if (current != null)
                {
                    buf.AppendLine(line);
                }
            }
            FlushCurrent();
        }

        private static string? ParseString(string yaml, string key)
        {
            var m = Regex.Match(yaml, $@"^\s*{Regex.Escape(key)}\s*:\s*(.+?)\s*$",
                                RegexOptions.Multiline);
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

        private static List<string> ParseStringList(string yaml, string key)
        {
            // Format : "  evaluations: [\"2026-05-18\", \"2026-08-12\"]"
            var m = Regex.Match(yaml, $@"^\s*{Regex.Escape(key)}\s*:\s*\[(.+?)\]\s*$",
                                RegexOptions.Multiline);
            if (!m.Success) return new();
            var inner = m.Groups[1].Value;
            var items = new List<string>();
            foreach (Match im in Regex.Matches(inner, "\"([^\"]*)\""))
                items.Add(im.Groups[1].Value);
            return items;
        }
    }
}
