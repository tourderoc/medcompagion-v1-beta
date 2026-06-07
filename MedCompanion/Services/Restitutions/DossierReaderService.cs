using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MedCompanion.Services.Synthesis;
using MedCompanion.Services.Therapeutique;

namespace MedCompanion.Services.Restitutions
{
    /// <summary>
    /// Lecture mécanique du « dossier bleu » d'un patient. Produit un DossierReading
    /// agrégé qui sert d'entrée commune aux 8 préremplissages de blocs du Dossier
    /// de Restitution.
    ///
    /// Aucune dépendance LLM ici : on lit des fichiers déjà validés par le médecin.
    /// Le pré-digest LLM serait redondant (et risque d'hallucination) car tout le
    /// contenu lu est déjà rédigé et validé.
    /// </summary>
    public class DossierReaderService
    {
        private readonly PathService _pathService;
        private readonly SyntheseGlobaleService? _syntheseGlobaleService;
        private readonly ProjetTherapeutiqueService? _projetService;

        public DossierReaderService(
            PathService pathService,
            SyntheseGlobaleService? syntheseGlobaleService = null,
            ProjetTherapeutiqueService? projetService = null)
        {
            _pathService           = pathService;
            _syntheseGlobaleService = syntheseGlobaleService;
            _projetService          = projetService;
        }

        /// <summary>
        /// Lit le dossier bleu en entier. Renvoie un DossierReading peuplé avec
        /// tout le contenu validé que Med peut consulter pour rédiger la restitution.
        /// </summary>
        public Task<DossierReading> ReadAsync(string patientNomComplet)
            => Task.Run(() => Read(patientNomComplet));

        private DossierReading Read(string patientNomComplet)
        {
            var notes  = ReadAllConsultationNotes(patientNomComplet);
            var (premiere, autres) = SplitPremiereConsultation(notes);

            return new DossierReading
            {
                PatientNomComplet = patientNomComplet,
                ReadAt            = DateTime.Now,

                PatientJson              = ReadPatientJson(patientNomComplet),
                PremiereConsultation     = premiere,
                NotesConsultation        = autres,
                Evaluations              = ReadAllEvaluationsCloturees(patientNomComplet),
                SyntheseGlobaleMed       = ReadSyntheseGlobaleMed(patientNomComplet),
                SyntheseGlobaleV05       = ReadSyntheseGlobaleV05(patientNomComplet),
                ProjetTherapeutique      = ReadProjetTherapeutique(patientNomComplet),
                SynthesesDocuments       = ReadAllSynthesesDocuments(patientNomComplet),
                SyntheseGlobaleDocuments = ReadSyntheseGlobaleDocuments(patientNomComplet),
            };
        }

        // ── Identité ────────────────────────────────────────────────────────

        private string ReadPatientJson(string patient)
        {
            var path = _pathService.GetPatientJsonPath(patient);
            return SafeReadAllText(path);
        }

        // ── Notes de consultation ───────────────────────────────────────────

        private List<NoteEntry> ReadAllConsultationNotes(string patient)
        {
            var result = new List<NoteEntry>();
            var patientRoot = _pathService.GetPatientRootDirectory(patient);
            if (!Directory.Exists(patientRoot)) return result;

            foreach (var yearDir in EnumerateYearDirectories(patientRoot))
            {
                var notesDir = Path.Combine(yearDir, "notes");
                if (!Directory.Exists(notesDir)) continue;

                foreach (var file in Directory.GetFiles(notesDir, "*.md"))
                {
                    var raw = SafeReadAllText(file);
                    if (string.IsNullOrWhiteSpace(raw)) continue;

                    var (yaml, body) = SplitFrontmatter(raw);
                    var date = ParseDate(yaml, "date") ?? File.GetLastWriteTime(file);
                    var type = ParseQuotedString(yaml, "type") ?? "";

                    result.Add(new NoteEntry
                    {
                        Date     = date,
                        Type     = type,
                        Content  = body.Trim(),
                        FilePath = file,
                    });
                }
            }

            return result.OrderByDescending(n => n.Date).ToList();
        }

        private static (string premiere, List<NoteEntry> autres) SplitPremiereConsultation(List<NoteEntry> notes)
        {
            // La 1ère consultation est identifiée par YAML type == "consultation-premiere".
            var premiere = notes.FirstOrDefault(n =>
                n.Type.Equals("consultation-premiere", StringComparison.OrdinalIgnoreCase));

            if (premiere == null)
                return ("", notes);

            var autres = notes.Where(n => n != premiere).ToList();
            return (premiere.Content, autres);
        }

        // ── Évaluations ─────────────────────────────────────────────────────

        private List<EvaluationEntry> ReadAllEvaluationsCloturees(string patient)
        {
            var result = new List<EvaluationEntry>();
            var dir = Path.Combine(_pathService.GetPatientRootDirectory(patient), "evaluations");
            if (!Directory.Exists(dir)) return result;

            foreach (var file in Directory.GetFiles(dir, "*_evaluation_*.md"))
            {
                var raw = SafeReadAllText(file);
                if (string.IsNullOrWhiteSpace(raw)) continue;

                var (yaml, _) = SplitFrontmatter(raw);
                var dateCloture = ParseDate(yaml, "date_cloture");

                // Une évaluation sans date_cloture est en cours — on l'ignore pour la restitution.
                if (!dateCloture.HasValue) continue;

                result.Add(new EvaluationEntry
                {
                    DateCloture = dateCloture,
                    Content     = raw,
                    FilePath    = file,
                });
            }

            return result.OrderByDescending(e => e.DateCloture ?? DateTime.MinValue).ToList();
        }

        // ── Synthèse Globale Med (transversale, synthese/synthese.md) ───────

        private string ReadSyntheseGlobaleMed(string patient)
        {
            var dir = _pathService.GetSyntheseDirectory(patient);
            var path = Path.Combine(dir, "synthese.md");
            return SafeReadAllText(path);
        }

        // ── Synthèse Globale V0.5 (dernière validée) ────────────────────────

        private string ReadSyntheseGlobaleV05(string patient)
        {
            if (_syntheseGlobaleService == null) return "";
            try
            {
                var version = _syntheseGlobaleService.GetDerniereValidee(patient);
                if (version == null || string.IsNullOrWhiteSpace(version.FilePath)) return "";
                return SafeReadAllText(version.FilePath);
            }
            catch { return ""; }
        }

        // ── Projet Thérapeutique (dernière validée) ─────────────────────────

        private string ReadProjetTherapeutique(string patient)
        {
            if (_projetService == null) return "";
            try
            {
                var version = _projetService.GetDerniereValidee(patient);
                if (version == null || string.IsNullOrWhiteSpace(version.FilePath)) return "";
                return SafeReadAllText(version.FilePath);
            }
            catch { return ""; }
        }

        // ── Documents (synthèses Med, jamais les PDFs bruts) ────────────────

        private List<string> ReadAllSynthesesDocuments(string patient)
        {
            var result = new List<string>();
            var patientRoot = _pathService.GetPatientRootDirectory(patient);
            if (!Directory.Exists(patientRoot)) return result;

            foreach (var yearDir in EnumerateYearDirectories(patientRoot))
            {
                var synthDir = Path.Combine(yearDir, "documents", "syntheses_documents");
                if (!Directory.Exists(synthDir)) continue;

                foreach (var file in Directory.GetFiles(synthDir, "*.md"))
                {
                    var txt = SafeReadAllText(file);
                    if (!string.IsNullOrWhiteSpace(txt))
                        result.Add(txt.Trim());
                }
            }
            return result;
        }

        private string ReadSyntheseGlobaleDocuments(string patient)
        {
            var patientRoot = _pathService.GetPatientRootDirectory(patient);
            if (!Directory.Exists(patientRoot)) return "";

            // Dernière année avec un fichier documents/synthese-globale.md
            foreach (var yearDir in EnumerateYearDirectories(patientRoot))
            {
                var path = Path.Combine(yearDir, "documents", "synthese-globale.md");
                if (File.Exists(path)) return SafeReadAllText(path);
            }
            return "";
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static IEnumerable<string> EnumerateYearDirectories(string patientRoot)
        {
            // Sous-dossiers nommés par une année (4 chiffres), triés décroissant.
            return Directory.GetDirectories(patientRoot)
                .Where(d => int.TryParse(Path.GetFileName(d), out var y) && y >= 1900 && y < 3000)
                .OrderByDescending(d => Path.GetFileName(d));
        }

        private static string SafeReadAllText(string path)
        {
            try
            {
                return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : "";
            }
            catch { return ""; }
        }

        private static (string yaml, string body) SplitFrontmatter(string raw)
        {
            if (!raw.TrimStart().StartsWith("---")) return ("", raw);
            var firstEnd = raw.IndexOf('\n');
            if (firstEnd < 0) return ("", raw);
            var secondMarker = raw.IndexOf("---", firstEnd + 1, StringComparison.Ordinal);
            if (secondMarker < 0) return ("", raw);
            var yaml = raw.Substring(firstEnd + 1, secondMarker - firstEnd - 1);
            var body = raw.Substring(secondMarker + 3).TrimStart('\r', '\n');
            return (yaml, body);
        }

        private static string? ParseQuotedString(string yaml, string key)
        {
            if (string.IsNullOrEmpty(yaml)) return null;
            var m = Regex.Match(yaml, $@"^\s*{Regex.Escape(key)}\s*:\s*(.+?)\s*$", RegexOptions.Multiline);
            if (!m.Success) return null;
            var val = m.Groups[1].Value.Trim();
            if (val.StartsWith("\"") && val.EndsWith("\"") && val.Length >= 2)
                val = val.Substring(1, val.Length - 2).Replace("\\\"", "\"");
            return val;
        }

        private static DateTime? ParseDate(string yaml, string key)
        {
            var s = ParseQuotedString(yaml, key);
            if (string.IsNullOrEmpty(s) || s == "null") return null;
            return DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var d) ? d : null;
        }
    }
}
