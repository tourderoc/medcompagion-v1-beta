using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MedCompanion.Models;
using MedCompanion.Models.Restitutions;

namespace MedCompanion.Services.Restitutions
{
    public class RestitutionService
    {
        private readonly PathService _pathService;

        public RestitutionService(PathService pathService)
        {
            _pathService = pathService;
        }

        public string GetRootDirectory(string patientNomComplet, int year)
        {
            var dir = Path.Combine(_pathService.GetPatientRootDirectory(patientNomComplet), year.ToString(), "restitutions");
            _pathService.EnsureDirectoryExists(dir);
            return dir;
        }

        public Task<List<RestitutionBase>> ListRestitutionsAsync(string patientNomComplet)
        {
            var results = new List<RestitutionBase>();
            try
            {
                var patientRoot = _pathService.GetPatientRootDirectory(patientNomComplet);
                if (!Directory.Exists(patientRoot)) return Task.FromResult(results);

                var yearDirs = Directory.GetDirectories(patientRoot);
                foreach (var yearDir in yearDirs)
                {
                    var restitutionsDir = Path.Combine(yearDir, "restitutions");
                    if (Directory.Exists(restitutionsDir))
                    {
                        var mdFiles = Directory.GetFiles(restitutionsDir, "restitution_*.md");
                        foreach (var f in mdFiles)
                        {
                            var r = Load(f);
                            if (r != null) results.Add(r);
                        }
                    }
                }

                results = results.OrderByDescending(r => r.DateCreation).ToList();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RestitutionService] Erreur lors de la liste des restitutions : {ex.Message}");
            }

            return Task.FromResult(results);
        }

        public RestitutionBase? Load(string filePath)
        {
            if (!File.Exists(filePath)) return null;
            try
            {
                var raw = File.ReadAllText(filePath, Encoding.UTF8);
                var (yaml, body) = SplitFrontmatter(raw);
                if (yaml == null) return null;

                var typeStr = ParseString(yaml, "type") ?? "";
                RestitutionBase r;
                if (typeStr.Equals("PremierEntretien", StringComparison.OrdinalIgnoreCase))
                {
                    // Placeholder pour l'instant si on a besoin d'une classe spécifique,
                    // mais pour la V1 on peut l'ignorer ou juste renvoyer null si non géré.
                    // On ne va pas la coder maintenant.
                    return null;
                }
                else if (typeStr.Equals("DossierInitial", StringComparison.OrdinalIgnoreCase))
                {
                    r = new DossierRestitutionInitial();
                }
                else
                {
                    return null;
                }

                r.PatientNomComplet = ParseString(yaml, "patient") ?? "";
                r.Version = ParseInt(yaml, "version") ?? 1;
                r.VersionPrecedenteFichier = ParseString(yaml, "version_precedente_fichier");
                var statutStr = ParseString(yaml, "statut") ?? "Brouillon";
                r.Statut = statutStr.Equals("Validee", StringComparison.OrdinalIgnoreCase) ? RestitutionStatut.Validee : RestitutionStatut.Brouillon;
                r.DateCreation = ParseDate(yaml, "date_creation") ?? File.GetCreationTime(filePath);
                r.DateValidation = ParseDate(yaml, "date_validation");
                r.GeneratedPdfPath = ParseString(yaml, "pdf_path");

                // Parse blocs from YAML section
                var blocsSection = ParseBlocsMetadata(yaml);
                foreach (var b in r.Blocs)
                {
                    if (blocsSection.TryGetValue(b.Key, out var meta))
                    {
                        b.IsIncludedInPdf = meta.isIncluded;
                        b.IsValidated = meta.isValidated;
                        b.SourceCliniqueFichier = meta.source;
                    }
                }

                ApplySections(r, body);
                
                // Enregistrer le chemin d'origine pour pouvoir le supprimer ou le mettre à jour
                r.Id = filePath;

                return r;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[RestitutionService] Load échec {filePath} : {ex.Message}");
                return null;
            }
        }

        public void SaveBrouillon(RestitutionBase r)
        {
            if (r.Statut == RestitutionStatut.Validee)
                throw new InvalidOperationException("Impossible de sauvegarder par-dessus une version validée.");

            var dir = GetRootDirectory(r.PatientNomComplet, r.DateCreation.Year);
            var filePath = Path.Combine(dir, $"restitution_{r.Type}_v{r.Version}_brouillon.md");
            
            File.WriteAllText(filePath, Serialize(r), Encoding.UTF8);
            r.Id = filePath; // On utilise Id comme FilePath temporaire
        }

        public string Validate(RestitutionBase r)
        {
            if (r.Statut == RestitutionStatut.Validee)
                throw new InvalidOperationException("Restitution déjà validée.");
            
            r.DateValidation = DateTime.Now;
            r.Statut = RestitutionStatut.Validee;

            var dir = GetRootDirectory(r.PatientNomComplet, r.DateCreation.Year);
            var dateSlug = r.DateValidation.Value.ToString("yyyyMMdd");
            var newFilePath = Path.Combine(dir, $"restitution_{r.Type}_v{r.Version}_{dateSlug}.md");
            
            File.WriteAllText(newFilePath, Serialize(r), Encoding.UTF8);

            // Supprimer le brouillon s'il existe
            if (!string.IsNullOrEmpty(r.Id) && File.Exists(r.Id) && !r.Id.Equals(newFilePath, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(r.Id); } catch { }
            }
            
            r.Id = newFilePath;
            return newFilePath;
        }

        private static string Serialize(RestitutionBase r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("---");
            sb.AppendLine($"type: {r.Type}");
            sb.AppendLine($"version: {r.Version}");
            sb.AppendLine($"statut: {r.Statut}");
            sb.AppendLine($"patient: {r.PatientNomComplet}");
            sb.AppendLine($"date_creation: {r.DateCreation.ToString("o", CultureInfo.InvariantCulture)}");
            if (r.DateValidation.HasValue)
                sb.AppendLine($"date_validation: {r.DateValidation.Value.ToString("o", CultureInfo.InvariantCulture)}");
            if (!string.IsNullOrWhiteSpace(r.VersionPrecedenteFichier))
                sb.AppendLine($"version_precedente_fichier: {r.VersionPrecedenteFichier}");
            if (!string.IsNullOrWhiteSpace(r.GeneratedPdfPath))
                sb.AppendLine($"pdf_path: {r.GeneratedPdfPath.Replace("\\", "/")}");
            
            sb.AppendLine("blocs:");
            foreach (var b in r.Blocs)
            {
                sb.AppendLine($"  - key: {b.Key}");
                sb.AppendLine($"    is_included_in_pdf: {b.IsIncludedInPdf.ToString().ToLowerInvariant()}");
                sb.AppendLine($"    is_validated: {b.IsValidated.ToString().ToLowerInvariant()}");
                if (!string.IsNullOrWhiteSpace(b.SourceCliniqueFichier))
                    sb.AppendLine($"    source_clinique: {b.SourceCliniqueFichier}");
            }
            sb.AppendLine("---");
            sb.AppendLine();

            foreach (var b in r.Blocs)
            {
                sb.AppendLine($"## Bloc — {b.Titre} [{b.Key}]");
                sb.AppendLine();
                sb.AppendLine(string.IsNullOrWhiteSpace(b.ContenuValide) ? "_(à compléter)_" : b.ContenuValide.TrimEnd());
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static (string? yaml, string body) SplitFrontmatter(string raw)
        {
            if (!raw.TrimStart().StartsWith("---")) return (null, raw);
            var firstEnd = raw.IndexOf('\n');
            if (firstEnd < 0) return (null, raw);
            var secondMarker = raw.IndexOf("---", firstEnd + 1, StringComparison.Ordinal);
            if (secondMarker < 0) return (null, raw);
            var yaml = raw.Substring(firstEnd + 1, secondMarker - firstEnd - 1);
            var body = raw.Substring(secondMarker + 3).TrimStart('\r', '\n');
            return (yaml, body);
        }

        private static void ApplySections(RestitutionBase r, string body)
        {
            var lines = body.Replace("\r\n", "\n").Split('\n');
            string currentKey = "";
            var bufText = new StringBuilder();

            void FlushText()
            {
                if (string.IsNullOrEmpty(currentKey)) return;
                var t = bufText.ToString().TrimEnd();
                if (t == "_(à compléter)_") t = "";
                var bloc = r.Blocs.FirstOrDefault(b => b.Key == currentKey);
                if (bloc != null)
                {
                    bloc.ContenuValide = t;
                    bloc.ContenuPreremplit = t; // by default
                }
                bufText.Clear();
            }

            foreach (var line in lines)
            {
                if (line.StartsWith("## Bloc —"))
                {
                    FlushText();
                    var m = Regex.Match(line, @"\[(.*?)\]");
                    if (m.Success)
                    {
                        currentKey = m.Groups[1].Value.Trim();
                    }
                    else
                    {
                        currentKey = "";
                    }
                }
                else
                {
                    bufText.AppendLine(line);
                }
            }
            FlushText();
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

        private static Dictionary<string, (bool isIncluded, bool isValidated, string? source)> ParseBlocsMetadata(string yaml)
        {
            var dict = new Dictionary<string, (bool, bool, string?)>();
            var mBlocsSection = Regex.Match(yaml, @"blocs:\s*\n(.*?)(?=\n[A-Za-z_-]+:|\z)", RegexOptions.Singleline);
            if (mBlocsSection.Success)
            {
                var blocksListStr = mBlocsSection.Groups[1].Value;
                var blockChunks = Regex.Split(blocksListStr, @"\n\s*-\s*key:\s*").Where(c => !string.IsNullOrWhiteSpace(c));
                foreach (var chunk in blockChunks)
                {
                    var lines = chunk.Split('\n');
                    var key = lines[0].Trim();
                    var isIncluded = true;
                    var isValidated = false;
                    string? source = null;

                    var mIncluded = Regex.Match(chunk, @"is_included_in_pdf:\s*(true|false)", RegexOptions.IgnoreCase);
                    if (mIncluded.Success) isIncluded = mIncluded.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);

                    var mValidated = Regex.Match(chunk, @"is_validated:\s*(true|false)", RegexOptions.IgnoreCase);
                    if (mValidated.Success) isValidated = mValidated.Groups[1].Value.Equals("true", StringComparison.OrdinalIgnoreCase);

                    var mSource = Regex.Match(chunk, @"source_clinique:\s*(.+)");
                    if (mSource.Success) source = mSource.Groups[1].Value.Trim();

                    dict[key] = (isIncluded, isValidated, source);
                }
            }
            return dict;
        }
    }
}
