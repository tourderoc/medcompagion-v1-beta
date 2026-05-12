using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace MedCompanion.Services.Consultation
{
    /// <summary>
    /// Gère le vocabulaire personnalisé injecté dans le prompt Whisper.
    /// 
    /// Le fichier whisper_vocab_custom.txt contient des mots/phrases à prioriser
    /// dans la transcription (médicaments, sigles, noms d'établissements, etc.).
    /// 
    /// Format du fichier :
    ///   - Un mot/phrase par ligne
    ///   - Les lignes vides et les commentaires (#) sont ignorés
    ///   - Sections optionnelles : [Médicaments], [Sigles], [Établissements], etc.
    /// 
    /// Le vocabulaire est injecté dans le prompt initial de Whisper pour biaiser
    /// le modèle vers les bons termes médicaux.
    /// </summary>
    public class WhisperVocabService
    {
        private static readonly string VocabDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MedCompanion", "Consultation");

        private static readonly string VocabPath = Path.Combine(VocabDir, "whisper_vocab_custom.txt");

        private static readonly string DefaultVocabPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Resources", "Consultation", "whisper_vocab_custom.txt");

        private List<string> _entries = new();

        /// <summary>
        /// Toutes les entrées du vocabulaire (mots/phrases), sans sections ni commentaires.
        /// </summary>
        public IReadOnlyList<string> Entries => _entries.AsReadOnly();

        /// <summary>
        /// Contenu brut du fichier (avec sections et commentaires).
        /// </summary>
        public string RawContent { get; private set; } = "";

        /// <summary>
        /// Nombre total d'entrées de vocabulaire.
        /// </summary>
        public int Count => _entries.Count;

        /// <summary>
        /// Charge le vocabulaire personnalisé.
        /// Priorité : fichier utilisateur AppData > fichier par défaut Resources.
        /// </summary>
        public void Load()
        {
            string path;

            if (File.Exists(VocabPath))
            {
                path = VocabPath;
                Debug.WriteLine($"[WhisperVocab] Chargé depuis AppData : {VocabPath}");
            }
            else if (File.Exists(DefaultVocabPath))
            {
                path = DefaultVocabPath;
                Debug.WriteLine($"[WhisperVocab] Chargé depuis Resources (défaut) : {DefaultVocabPath}");
            }
            else
            {
                Debug.WriteLine("[WhisperVocab] Aucun fichier de vocabulaire trouvé.");
                _entries = new List<string>();
                RawContent = "";
                return;
            }

            try
            {
                RawContent = File.ReadAllText(path, Encoding.UTF8);
                _entries = ParseEntries(RawContent);
                Debug.WriteLine($"[WhisperVocab] {_entries.Count} entrées chargées.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WhisperVocab] Erreur lecture : {ex.Message}");
                _entries = new List<string>();
                RawContent = "";
            }
        }

        /// <summary>
        /// Sauvegarde le vocabulaire personnalisé dans AppData.
        /// </summary>
        public void Save(string rawContent)
        {
            try
            {
                Directory.CreateDirectory(VocabDir);
                File.WriteAllText(VocabPath, rawContent, Encoding.UTF8);
                RawContent = rawContent;
                _entries = ParseEntries(rawContent);
                Debug.WriteLine($"[WhisperVocab] Sauvegardé : {_entries.Count} entrées → {VocabPath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WhisperVocab] Erreur sauvegarde : {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Génère la portion du prompt Whisper à partir du vocabulaire custom.
        /// Format : "Termes spécialisés : terme1, terme2, terme3."
        /// </summary>
        /// <returns>Fragment de prompt, ou vide si pas de vocabulaire</returns>
        public string BuildPromptFragment()
        {
            if (_entries.Count == 0) return "";

            // Regrouper par sections pour un prompt structuré
            var sections = ParseSections(RawContent);

            var parts = new List<string>();
            foreach (var (section, entries) in sections)
            {
                if (entries.Count == 0) continue;

                if (!string.IsNullOrWhiteSpace(section))
                    parts.Add($"{section} : {string.Join(", ", entries)}");
                else
                    parts.Add(string.Join(", ", entries));
            }

            return parts.Count > 0
                ? string.Join(". ", parts) + "."
                : "";
        }

        /// <summary>
        /// Ajoute une entrée au vocabulaire (en mémoire, il faut appeler Save pour persister).
        /// </summary>
        public void AddEntry(string entry)
        {
            entry = entry.Trim();
            if (string.IsNullOrEmpty(entry)) return;
            if (_entries.Contains(entry, StringComparer.OrdinalIgnoreCase)) return;

            _entries.Add(entry);
            RawContent = RawContent.TrimEnd() + Environment.NewLine + entry + Environment.NewLine;
        }

        /// <summary>
        /// Supprime une entrée du vocabulaire.
        /// </summary>
        public void RemoveEntry(string entry)
        {
            _entries.RemoveAll(e => string.Equals(e, entry, StringComparison.OrdinalIgnoreCase));

            // Reconstruire le contenu brut sans cette entrée
            var lines = RawContent.Split('\n').ToList();
            lines.RemoveAll(l => string.Equals(l.Trim(), entry.Trim(), StringComparison.OrdinalIgnoreCase));
            RawContent = string.Join('\n', lines);
        }

        /// <summary>
        /// Réinitialise vers le vocabulaire par défaut.
        /// </summary>
        public void ResetToDefaults()
        {
            if (File.Exists(DefaultVocabPath))
            {
                var content = File.ReadAllText(DefaultVocabPath, Encoding.UTF8);
                Save(content);
            }
        }

        // ── Parsing ──────────────────────────────────────────────────────────────

        private static List<string> ParseEntries(string content)
        {
            var entries = new List<string>();

            foreach (var line in content.Split('\n'))
            {
                var trimmed = line.Trim();

                // Ignorer vides, commentaires, sections
                if (string.IsNullOrEmpty(trimmed)) continue;
                if (trimmed.StartsWith('#')) continue;
                if (trimmed.StartsWith('[') && trimmed.EndsWith(']')) continue;

                entries.Add(trimmed);
            }

            return entries;
        }

        private static List<(string section, List<string> entries)> ParseSections(string content)
        {
            var sections = new List<(string, List<string>)>();
            var currentSection = "";
            var currentEntries = new List<string>();

            foreach (var line in content.Split('\n'))
            {
                var trimmed = line.Trim();

                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                    continue;

                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    // Sauver la section précédente
                    if (currentEntries.Count > 0)
                        sections.Add((currentSection, currentEntries));

                    currentSection = trimmed[1..^1].Trim();
                    currentEntries = new List<string>();
                    continue;
                }

                currentEntries.Add(trimmed);
            }

            // Dernière section
            if (currentEntries.Count > 0)
                sections.Add((currentSection, currentEntries));

            return sections;
        }
    }
}
