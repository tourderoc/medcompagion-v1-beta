using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// Une carte de la frise chronologique des consultations.
    /// Contenu volontairement minimal : date + type (la carte mentale viendra plus tard).
    /// </summary>
    public class ConsultationCardViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public string   Title    { get; }
        public string   FilePath { get; }
        public DateTime Date     { get; }
        public string   Type     { get; }   // "1ère consultation" | "Suivi" | "Note"
        public string   Icon     { get; }   // emoji selon le type
        public bool     IsCloturee { get; set; }

        /// <summary>Date formatée pour affichage compact (ex: 02/12/2025).</summary>
        public string DateText => Date == DateTime.MinValue ? "" : Date.ToString("dd/MM/yyyy");

        private bool _isActive;
        public bool IsActive
        {
            get => _isActive;
            set { if (_isActive != value) { _isActive = value; OnPropertyChanged(); } }
        }

        public ConsultationCardViewModel(string title, string filePath)
        {
            Title      = title ?? "";
            FilePath   = filePath ?? "";
            Date       = ParseDate(Title);
            Type       = ParseType(Title);
            Icon       = Type switch
            {
                "1ère consultation" => "🩺",
                "Suivi"             => "🔄",
                _                   => "📝"
            };
            IsCloturee = CheckIfCloturee(FilePath, Type);
        }

        /// <summary>Carte d'une consultation en cours de création (pas encore sauvegardée).</summary>
        public static ConsultationCardViewModel CreateNew(string type, DateTime date)
        {
            var icon = type switch
            {
                "1ère consultation" => "🩺",
                "Suivi"             => "🔄",
                _                   => "📝"
            };
            return new ConsultationCardViewModel(
                $"{type} – {date:dd/MM/yyyy}", "") { IsActive = true };
        }

        private static string ParseType(string title)
        {
            var t = title.ToLowerInvariant();
            if (t.Contains("interrogatoire") || t.Contains("1ère") || t.Contains("1re") || t.Contains("première") || t.Contains("1er"))
                return "1ère consultation";
            if (t.Contains("suivi"))
                return "Suivi";
            return "Note";
        }

        public static DateTime ParseDate(string title)
        {
            // Cherche un motif JJ/MM/AAAA dans le titre
            var m = Regex.Match(title, @"(\d{1,2})[/\-.](\d{1,2})[/\-.](\d{4})");
            if (m.Success &&
                int.TryParse(m.Groups[1].Value, out var d) &&
                int.TryParse(m.Groups[2].Value, out var mo) &&
                int.TryParse(m.Groups[3].Value, out var y))
            {
                try { return new DateTime(y, mo, d); } catch { }
            }
            return DateTime.MinValue;
        }

        private static bool CheckIfCloturee(string filePath, string type)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return false;

            if (type != "1ère consultation")
                return true;

            try
            {
                using var reader = new StreamReader(filePath, Encoding.UTF8);
                string? line;
                bool inYaml = false;
                bool hasExplicitTag = false;
                bool isCloturee = false;
                int lineCount = 0;

                while ((line = reader.ReadLine()) != null && lineCount++ < 50)
                {
                    var trimmed = line.Trim();
                    if (trimmed == "---")
                    {
                        if (!inYaml) inYaml = true;
                        else break;
                        continue;
                    }

                    if (inYaml)
                    {
                        if (trimmed.StartsWith("cloturee:", StringComparison.OrdinalIgnoreCase))
                        {
                            hasExplicitTag = true;
                            var val = trimmed.Substring(9).Trim().Trim('"').ToLowerInvariant();
                            isCloturee = val == "true" || val == "oui" || val == "1";
                        }
                        else if (trimmed.StartsWith("statut:", StringComparison.OrdinalIgnoreCase))
                        {
                            hasExplicitTag = true;
                            var val = trimmed.Substring(7).Trim().Trim('"').ToLowerInvariant();
                            isCloturee = val.Contains("clotur") || val.Contains("clos") || val == "validee";
                        }
                    }
                }

                if (hasExplicitTag)
                    return isCloturee;

                // Fichier sans tag explicite de statut :
                // Vérifier si un brouillon actif existe dans ce dossier patient
                var notesDir = Path.GetDirectoryName(filePath);
                var patientDir = Path.GetDirectoryName(notesDir);
                if (!string.IsNullOrEmpty(patientDir))
                {
                    var draftPath = Path.Combine(patientDir, "notes", "premiere_draft.json");
                    if (File.Exists(draftPath))
                        return false;
                }

                // Pour les fichiers historiques créés il y a plus de 24h sans brouillon : réputés clôturés
                if (File.GetLastWriteTime(filePath) < DateTime.Now.AddDays(-1))
                    return true;

                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
