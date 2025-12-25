using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MedCompanion.Models
{
    /// <summary>
    /// Données complètes du formulaire MDPH CERFA 15695*01
    /// Généré en une seule fois par le LLM au format JSON
    /// </summary>
    public class MDPHFormData
    {
        [JsonPropertyName("pathologie_principale")]
        public string PathologiePrincipale { get; set; } = string.Empty;

        [JsonPropertyName("autres_pathologies")]
        public string AutresPathologies { get; set; } = string.Empty;

        [JsonPropertyName("elements_essentiels")]
        public List<string> ElementsEssentiels { get; set; } = new();

        [JsonPropertyName("antecedents_medicaux")]
        public List<string> AntecedentsMedicaux { get; set; } = new();

        [JsonPropertyName("retards_developpementaux")]
        public List<string> RetardsDeveloppementaux { get; set; } = new();

        [JsonPropertyName("description_clinique")]
        public List<string> DescriptionClinique { get; set; } = new();

        [JsonPropertyName("traitements")]
        public TraitementsData Traitements { get; set; } = new();

        [JsonPropertyName("retentissements")]
        public RetentissementsData Retentissements { get; set; } = new();

        [JsonPropertyName("remarques_complementaires")]
        public string RemarquesComplementaires { get; set; } = string.Empty;
    }

    /// <summary>
    /// Données des traitements (médicaments, effets, prises en charge)
    /// </summary>
    public class TraitementsData
    {
        [JsonPropertyName("medicaments")]
        public string Medicaments { get; set; } = string.Empty;

        [JsonPropertyName("effets_indesirables")]
        public string EffetsIndesirables { get; set; } = string.Empty;

        [JsonPropertyName("autres_prises_en_charge")]
        public string AutresPrisesEnCharge { get; set; } = string.Empty;
    }

    /// <summary>
    /// Données des retentissements fonctionnels
    /// </summary>
    public class RetentissementsData
    {
        [JsonPropertyName("mobilite")]
        public string Mobilite { get; set; } = string.Empty;

        [JsonPropertyName("communication")]
        public string Communication { get; set; } = string.Empty;

        [JsonPropertyName("cognition")]
        public List<string> Cognition { get; set; } = new();

        [JsonPropertyName("conduite_emotionnelle")]
        public List<string> ConduiteEmotionnelle { get; set; } = new();

        [JsonPropertyName("autonomie")]
        public string Autonomie { get; set; } = string.Empty;

        [JsonPropertyName("vie_quotidienne")]
        public string VieQuotidienne { get; set; } = string.Empty;

        [JsonPropertyName("social_scolaire")]
        public string SocialScolaire { get; set; } = string.Empty;
    }
}
