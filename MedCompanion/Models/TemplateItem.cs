using System;

namespace MedCompanion.Models
{
    /// <summary>
    /// Modèle pour un template MCC dans le ComboBox
    /// Utilisé pour le binding XAML dans CourriersControl
    /// </summary>
    public class TemplateItem
    {
        /// <summary>
        /// Nom du template affiché (ex: "[MCC] PAP TDAH école primaire")
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Contenu markdown du template
        /// </summary>
        public string MarkdownContent { get; set; } = string.Empty;

        /// <summary>
        /// Indique si c'est un MCC (vs un template manuel)
        /// </summary>
        public bool IsMCC { get; set; }

        /// <summary>
        /// ID du MCC (si IsMCC = true)
        /// </summary>
        public string? MCCId { get; set; }

        /// <summary>
        /// Nom du MCC sans le préfixe "[MCC]" (si IsMCC = true)
        /// </summary>
        public string? MCCName { get; set; }

        /// <summary>
        /// Type de document (ex: "school_letter", "medical_letter")
        /// </summary>
        public string? DocType { get; set; }

        /// <summary>
        /// Audience cible (ex: "school", "doctor", "administration")
        /// </summary>
        public string? Audience { get; set; }

        /// <summary>
        /// Tranche d'âge (ex: "child", "teen", "adult")
        /// </summary>
        public string? AgeGroup { get; set; }

        /// <summary>
        /// Ton du courrier (ex: "formal", "empathetic", "neutral")
        /// </summary>
        public string? Tone { get; set; }

        /// <summary>
        /// Note moyenne du MCC (1-5 étoiles)
        /// </summary>
        public double? Rating { get; set; }

        /// <summary>
        /// Nombre d'utilisations du MCC
        /// </summary>
        public int? UsageCount { get; set; }

        /// <summary>
        /// Statut validé (pour afficher badge ✅)
        /// </summary>
        public bool IsValidated { get; set; }

        /// <summary>
        /// Mots-clés cliniques associés (ex: ["tdah", "attention", "école"])
        /// </summary>
        public string[]? Keywords { get; set; }

        /// <summary>
        /// Affichage formaté pour le ComboBox
        /// Ex: "[MCC] PAP TDAH école primaire ⭐4.8"
        /// </summary>
        public string DisplayName
        {
            get
            {
                if (!IsMCC)
                    return Name;

                var display = Name;

                // Ajouter la note si disponible
                if (Rating.HasValue && Rating.Value > 0)
                {
                    display += $" ⭐{Rating.Value:F1}";
                }

                // Ajouter badge validé
                if (IsValidated)
                {
                    display += " ✅";
                }

                return display;
            }
        }

        /// <summary>
        /// Catégorie pour groupement dans le ComboBox
        /// </summary>
        public string Category
        {
            get
            {
                if (!IsMCC)
                    return "Templates personnels";

                return Audience switch
                {
                    "school" => "École/Éducation",
                    "doctor" => "Correspondance médicale",
                    "administration" => "Administration",
                    _ => "Autres"
                };
            }
        }
    }
}
