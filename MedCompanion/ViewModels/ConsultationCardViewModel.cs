using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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
            Title    = title ?? "";
            FilePath = filePath ?? "";
            Date     = ParseDate(Title);
            Type     = ParseType(Title);
            Icon     = Type switch
            {
                "1ère consultation" => "🩺",
                "Suivi"             => "🔄",
                _                   => "📝"
            };
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
            if (t.Contains("interrogatoire") || t.Contains("1ère") || t.Contains("1re") || t.Contains("première"))
                return "1ère consultation";
            if (t.Contains("suivi"))
                return "Suivi";
            return "Note";
        }

        private static DateTime ParseDate(string title)
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
    }
}
