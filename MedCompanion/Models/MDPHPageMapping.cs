using System.Collections.Generic;

namespace MedCompanion.Models;

/// <summary>
/// Mapping entre les sections du formulaire MDPH et les numéros de pages
/// dans le PDF officiel CERFA 15695*01.
///
/// NOTE: Les numéros de pages doivent être vérifiés/ajustés selon le PDF réel.
/// Les valeurs ci-dessous sont des estimations basées sur la structure typique du CERFA.
/// </summary>
public static class MDPHPageMapping
{
    /// <summary>
    /// Dictionnaire associant chaque index de section (0-10) au numéro de page
    /// correspondant dans le PDF MDPH officiel.
    /// </summary>
    public static readonly Dictionary<int, int> SectionToPage = new()
    {
        // Section 0 : Pathologie motivant la demande (diagnostic + CIM-10)
        { 0, 7 },

        // Section 1 : Autres pathologies éventuelles
        { 1, 7 },

        // Section 2 : Éléments essentiels à retenir
        { 2, 7 },

        // Section 3 : Antécédents médicaux et périnataux
        { 3, 7 },

        // Section 4 : Retards développementaux
        { 4, 7 },

        // Section 5 : Description clinique actuelle - Ligne 1
        { 5, 8 },

        // Section 6 : Description clinique actuelle - Ligne 2
        { 6, 8 },

        // Section 7 : Description clinique actuelle - Ligne 3
        { 7, 8 },

        // Section 8 : Traitements - Médicaments en cours
        { 8, 8 },

        // Section 9 : Traitements - Effets indésirables
        { 9, 8 },

        // Section 10 : Traitements - Autres prises en charge
        { 10, 8 },

        // Section 11 : Retentissement - Mobilité
        { 11, 9 },

        // Section 12 : Retentissement - Communication
        { 12, 9 },

        // Section 13 : Retentissement - Cognition
        { 13, 10 },

        // Section 14 : Conduite émotionnelle et comportementale
        { 14, 10 },

        // Section 15 : Retentissement - Entretien personnel
        { 15, 10 },

        // Section 16 : Retentissement - Vie quotidienne
        { 16, 11 },

        // Section 17 : Retentissement social/scolaire/emploi
        { 17, 11 },

        // Section 18 : Remarques complémentaires
        { 18, 12 }
    };

    /// <summary>
    /// Noms lisibles des sections MDPH pour l'affichage dans l'interface.
    /// </summary>
    public static readonly Dictionary<int, string> SectionTitles = new()
    {
        { 0, "📋 Pathologie motivant la demande" },
        { 1, "🔬 Autres pathologies éventuelles" },
        { 2, "⚠️ Éléments essentiels à retenir" },
        { 3, "🏥 Antécédents médicaux et périnataux" },
        { 4, "👶 Retards développementaux" },
        { 5, "🔍 Signes cliniques invalidants (1)" },
        { 6, "🔍 Signes cliniques invalidants (2)" },
        { 7, "🔍 Signes cliniques invalidants (3)" },
        { 8, "💊 Médicaments en cours" },
        { 9, "⚠️ Effets indésirables" },
        { 10, "🏥 Autres prises en charge" },
        { 11, "🚶 Retentissement - Mobilité" },
        { 12, "💬 Retentissement - Communication" },
        { 13, "🧠 Retentissement - Cognition" },
        { 14, "😠 Conduite émotionnelle et comportementale" },
        { 15, "🛁 Retentissement - Entretien personnel" },
        { 16, "🏠 Retentissement - Vie quotidienne" },
        { 17, "👥 Retentissement social/scolaire/emploi" },
        { 18, "📝 Remarques complémentaires" }
    };

    /// <summary>
    /// Obtient le numéro de page pour une section donnée.
    /// </summary>
    /// <param name="sectionIndex">Index de la section (0-13)</param>
    /// <returns>Numéro de page dans le PDF, ou 1 si section invalide</returns>
    public static int GetPageForSection(int sectionIndex)
    {
        return SectionToPage.TryGetValue(sectionIndex, out int page) ? page : 1;
    }

    /// <summary>
    /// Obtient le titre formaté d'une section.
    /// </summary>
    /// <param name="sectionIndex">Index de la section (0-13)</param>
    /// <returns>Titre avec emoji, ou "Section inconnue" si invalide</returns>
    public static string GetSectionTitle(int sectionIndex)
    {
        return SectionTitles.TryGetValue(sectionIndex, out string? title)
            ? title
            : "Section inconnue";
    }

    /// <summary>
    /// Nombre total de sections MDPH.
    /// </summary>
    public const int TotalSections = 19;
}
