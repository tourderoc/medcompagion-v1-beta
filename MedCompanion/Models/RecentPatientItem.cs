using System;
using System.Windows.Media;

namespace MedCompanion.Models
{
    /// <summary>
    /// Carte d'accès rapide affichée dans le drawer "Patients récents" du Mode Consultation.
    /// Wrap un PatientIndexEntry + propriétés de présentation (avatar coloré, infos formatées).
    /// </summary>
    public class RecentPatientItem
    {
        public PatientIndexEntry Patient { get; set; } = null!;

        public string NomComplet         { get; set; } = "";
        public string Initials           { get; set; } = "";
        public string AgeDisplay         { get; set; } = "";
        public string LastConsultDisplay { get; set; } = "";
        public Brush  AvatarBrush        { get; set; } = Brushes.Gray;

        /// <summary>
        /// Couleur de l'avatar dérivée du nom (hash stable → même couleur à chaque ouverture).
        /// Palette inspirée de Doctolib : pastel saturé, contraste blanc OK.
        /// </summary>
        public static Brush AvatarBrushForName(string name)
        {
            var palette = new[]
            {
                "#E67E22", "#3498DB", "#27AE60", "#9B59B6", "#16A085",
                "#E74C3C", "#2980B9", "#F39C12", "#1ABC9C", "#8E44AD"
            };
            if (string.IsNullOrEmpty(name)) return Brushes.Gray;
            int hash = 0;
            foreach (var c in name) hash = (hash * 31 + c) & 0x7fffffff;
            var color = (Color)ColorConverter.ConvertFromString(palette[hash % palette.Length]);
            return new SolidColorBrush(color);
        }

        public static string InitialsFor(string nom, string prenom)
        {
            char i1 = !string.IsNullOrEmpty(prenom) ? char.ToUpperInvariant(prenom[0]) : ' ';
            char i2 = !string.IsNullOrEmpty(nom)    ? char.ToUpperInvariant(nom[0])    : ' ';
            return $"{i1}{i2}".Trim();
        }
    }
}
