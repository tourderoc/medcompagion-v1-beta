using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
        [JsonConverter(typeof(LenientStringConverter))]
        public string PathologiePrincipale { get; set; } = string.Empty;

        [JsonPropertyName("pathologie")]
        [JsonConverter(typeof(LenientStringConverter))]
        public string? PathologieFallback
        {
            get => null;
            set
            {
                if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(PathologiePrincipale))
                    PathologiePrincipale = value;
            }
        }

        [JsonPropertyName("autres_pathologies")]
        [JsonConverter(typeof(LenientStringConverter))]
        public string AutresPathologies { get; set; } = string.Empty;

        [JsonPropertyName("autre_pathologie")]
        [JsonConverter(typeof(LenientStringConverter))]
        public string? AutrePathologieFallback
        {
            get => null;
            set
            {
                if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(AutresPathologies))
                    AutresPathologies = value;
            }
        }

        [JsonPropertyName("elements_essentiels")]
        [JsonConverter(typeof(LenientStringListConverter))]
        public List<string> ElementsEssentiels { get; set; } = new();

        [JsonPropertyName("element_essentiel")]
        [JsonConverter(typeof(LenientStringListConverter))]
        public List<string>? ElementEssentielFallback
        {
            get => null;
            set
            {
                if (value != null && value.Count > 0 && ElementsEssentiels.Count == 0)
                    ElementsEssentiels = value;
            }
        }

        [JsonPropertyName("antecedents_medicaux")]
        [JsonConverter(typeof(LenientStringListConverter))]
        public List<string> AntecedentsMedicaux { get; set; } = new();

        [JsonPropertyName("antecedents")]
        [JsonConverter(typeof(LenientStringListConverter))]
        public List<string>? AntecedentsFallback
        {
            get => null;
            set
            {
                if (value != null && value.Count > 0 && AntecedentsMedicaux.Count == 0)
                    AntecedentsMedicaux = value;
            }
        }

        [JsonPropertyName("antecedent_medical")]
        [JsonConverter(typeof(LenientStringListConverter))]
        public List<string>? AntecedentMedicalFallback
        {
            get => null;
            set
            {
                if (value != null && value.Count > 0 && AntecedentsMedicaux.Count == 0)
                    AntecedentsMedicaux = value;
            }
        }

        [JsonPropertyName("retards_developpementaux")]
        [JsonConverter(typeof(LenientStringListConverter))]
        public List<string> RetardsDeveloppementaux { get; set; } = new();

        [JsonPropertyName("retards_developpement")]
        [JsonConverter(typeof(LenientStringListConverter))]
        public List<string>? RetardsDeveloppementFallback
        {
            get => null;
            set
            {
                if (value != null && value.Count > 0 && RetardsDeveloppementaux.Count == 0)
                    RetardsDeveloppementaux = value;
            }
        }

        [JsonPropertyName("retard_developpemental")]
        [JsonConverter(typeof(LenientStringListConverter))]
        public List<string>? RetardDeveloppementalFallback
        {
            get => null;
            set
            {
                if (value != null && value.Count > 0 && RetardsDeveloppementaux.Count == 0)
                    RetardsDeveloppementaux = value;
            }
        }

        [JsonPropertyName("description_clinique")]
        [JsonConverter(typeof(LenientStringListConverter))]
        public List<string> DescriptionClinique { get; set; } = new();

        [JsonPropertyName("traitements")]
        [JsonConverter(typeof(LenientTraitementsConverter))]
        public TraitementsData Traitements { get; set; } = new();

        [JsonPropertyName("traitement")]
        [JsonConverter(typeof(LenientTraitementsConverter))]
        public TraitementsData? TraitementFallback
        {
            get => null;
            set
            {
                if (value != null && (Traitements == null || Traitements.IsEmpty()))
                    Traitements = value;
            }
        }

        [JsonPropertyName("retentissements")]
        [JsonConverter(typeof(LenientRetentissementsConverter))]
        public RetentissementsData Retentissements { get; set; } = new();

        [JsonPropertyName("retentissement")]
        [JsonConverter(typeof(LenientRetentissementsConverter))]
        public RetentissementsData? RetentissementFallback
        {
            get => null;
            set
            {
                if (value != null && (Retentissements == null || Retentissements.IsEmpty()))
                    Retentissements = value;
            }
        }

        [JsonPropertyName("remarques_complementaires")]
        [JsonConverter(typeof(LenientStringConverter))]
        public string RemarquesComplementaires { get; set; } = string.Empty;

        [JsonPropertyName("remarques")]
        [JsonConverter(typeof(LenientStringConverter))]
        public string? RemarquesFallback
        {
            get => null;
            set
            {
                if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(RemarquesComplementaires))
                    RemarquesComplementaires = value;
            }
        }

        [JsonPropertyName("remarque_complementaire")]
        [JsonConverter(typeof(LenientStringConverter))]
        public string? RemarqueComplementaireFallback
        {
            get => null;
            set
            {
                if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(RemarquesComplementaires))
                    RemarquesComplementaires = value;
            }
        }

        public bool IsEmpty()
        {
            return string.IsNullOrWhiteSpace(PathologiePrincipale)
                && string.IsNullOrWhiteSpace(AutresPathologies)
                && (ElementsEssentiels == null || ElementsEssentiels.Count == 0)
                && (AntecedentsMedicaux == null || AntecedentsMedicaux.Count == 0)
                && (RetardsDeveloppementaux == null || RetardsDeveloppementaux.Count == 0)
                && (DescriptionClinique == null || DescriptionClinique.Count == 0)
                && (Traitements == null || Traitements.IsEmpty())
                && (Retentissements == null || Retentissements.IsEmpty())
                && string.IsNullOrWhiteSpace(RemarquesComplementaires);
        }

        public void EnsureNotNull()
        {
            ElementsEssentiels ??= new();
            AntecedentsMedicaux ??= new();
            RetardsDeveloppementaux ??= new();
            DescriptionClinique ??= new();
            Traitements ??= new();
            Retentissements ??= new();
            Retentissements.Cognition ??= new();
            Retentissements.ConduiteEmotionnelle ??= new();
        }
    }

    /// <summary>
    /// Données des traitements (médicaments, effets, prises en charge)
    /// </summary>
    public class TraitementsData
    {
        [JsonPropertyName("medicaments")]
        [JsonConverter(typeof(LenientStringConverter))]
        public string Medicaments { get; set; } = string.Empty;

        [JsonPropertyName("effets_indesirables")]
        [JsonConverter(typeof(LenientStringConverter))]
        public string EffetsIndesirables { get; set; } = string.Empty;

        [JsonPropertyName("autres_prises_en_charge")]
        [JsonConverter(typeof(LenientStringConverter))]
        public string AutresPrisesEnCharge { get; set; } = string.Empty;

        public bool IsEmpty() =>
            string.IsNullOrWhiteSpace(Medicaments) &&
            string.IsNullOrWhiteSpace(EffetsIndesirables) &&
            string.IsNullOrWhiteSpace(AutresPrisesEnCharge);
    }

    /// <summary>
    /// Données des retentissements fonctionnels
    /// </summary>
    public class RetentissementsData
    {
        [JsonPropertyName("mobilite")]
        [JsonConverter(typeof(LenientStringConverter))]
        public string Mobilite { get; set; } = string.Empty;

        [JsonPropertyName("communication")]
        [JsonConverter(typeof(LenientStringConverter))]
        public string Communication { get; set; } = string.Empty;

        [JsonPropertyName("cognition")]
        [JsonConverter(typeof(LenientStringListConverter))]
        public List<string> Cognition { get; set; } = new();

        [JsonPropertyName("conduite_emotionnelle")]
        [JsonConverter(typeof(LenientStringListConverter))]
        public List<string> ConduiteEmotionnelle { get; set; } = new();

        [JsonPropertyName("autonomie")]
        [JsonConverter(typeof(LenientStringConverter))]
        public string Autonomie { get; set; } = string.Empty;

        [JsonPropertyName("vie_quotidienne")]
        [JsonConverter(typeof(LenientStringConverter))]
        public string VieQuotidienne { get; set; } = string.Empty;

        [JsonPropertyName("social_scolaire")]
        [JsonConverter(typeof(LenientStringConverter))]
        public string SocialScolaire { get; set; } = string.Empty;

        public bool IsEmpty() =>
            string.IsNullOrWhiteSpace(Mobilite) &&
            string.IsNullOrWhiteSpace(Communication) &&
            (Cognition == null || Cognition.Count == 0) &&
            (ConduiteEmotionnelle == null || ConduiteEmotionnelle.Count == 0) &&
            string.IsNullOrWhiteSpace(Autonomie) &&
            string.IsNullOrWhiteSpace(VieQuotidienne) &&
            string.IsNullOrWhiteSpace(SocialScolaire);
    }

    #region Convertisseurs JSON Tolérants

    /// <summary>
    /// Convertisseur qui accepte une string ou un tableau de strings, convertissant un tableau en string multiligne.
    /// </summary>
    public class LenientStringConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                return reader.GetString() ?? string.Empty;
            }
            if (reader.TokenType == JsonTokenType.StartArray)
            {
                var list = new List<string>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        var s = reader.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                    }
                    else if (reader.TokenType == JsonTokenType.Number || reader.TokenType == JsonTokenType.True || reader.TokenType == JsonTokenType.False)
                    {
                        using var doc = JsonDocument.ParseValue(ref reader);
                        list.Add(doc.RootElement.ToString());
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
                return string.Join("\n", list);
            }
            if (reader.TokenType == JsonTokenType.Null)
            {
                return string.Empty;
            }
            if (reader.TokenType == JsonTokenType.Number || reader.TokenType == JsonTokenType.True || reader.TokenType == JsonTokenType.False)
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                return doc.RootElement.ToString();
            }

            using var fallbackDoc = JsonDocument.ParseValue(ref reader);
            return fallbackDoc.RootElement.ToString();
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }

    /// <summary>
    /// Convertisseur qui accepte un tableau de strings OU une string simple (découpée en lignes).
    /// </summary>
    public class LenientStringListConverter : JsonConverter<List<string>>
    {
        public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.StartArray)
            {
                var list = new List<string>();
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        var s = reader.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                    }
                    else if (reader.TokenType == JsonTokenType.Number || reader.TokenType == JsonTokenType.True || reader.TokenType == JsonTokenType.False)
                    {
                        using var doc = JsonDocument.ParseValue(ref reader);
                        list.Add(doc.RootElement.ToString());
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
                return list;
            }
            if (reader.TokenType == JsonTokenType.String)
            {
                var str = reader.GetString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(str)) return new List<string>();

                var lines = str.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(l => l.Trim().TrimStart('-', '•', '*').Trim())
                               .Where(l => !string.IsNullOrWhiteSpace(l))
                               .ToList();
                return lines.Count > 0 ? lines : new List<string> { str };
            }
            if (reader.TokenType == JsonTokenType.Null)
            {
                return new List<string>();
            }

            reader.Skip();
            return new List<string>();
        }

        public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            if (value != null)
            {
                foreach (var item in value)
                {
                    writer.WriteStringValue(item);
                }
            }
            writer.WriteEndArray();
        }
    }

    /// <summary>
    /// Convertisseur tolérant pour RetentissementsData (accepte objet ou tableau d'objets, gère variantes de noms).
    /// </summary>
    public class LenientRetentissementsConverter : JsonConverter<RetentissementsData>
    {
        public override RetentissementsData Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.StartArray)
            {
                RetentissementsData? result = null;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.StartObject && result == null)
                    {
                        using var doc = JsonDocument.ParseValue(ref reader);
                        result = ParseFromElement(doc.RootElement);
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
                return result ?? new RetentissementsData();
            }

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                return ParseFromElement(doc.RootElement);
            }

            if (reader.TokenType == JsonTokenType.Null)
            {
                return new RetentissementsData();
            }

            reader.Skip();
            return new RetentissementsData();
        }

        private static RetentissementsData ParseFromElement(JsonElement element)
        {
            var data = new RetentissementsData();
            if (element.ValueKind != JsonValueKind.Object) return data;

            foreach (var prop in element.EnumerateObject())
            {
                var name = prop.Name.ToLowerInvariant();
                switch (name)
                {
                    case "mobilite":
                    case "mobilité":
                        data.Mobilite = ReadStringOrArray(prop.Value);
                        break;
                    case "communication":
                        data.Communication = ReadStringOrArray(prop.Value);
                        break;
                    case "cognition":
                    case "cognitif":
                        data.Cognition = ReadStringList(prop.Value);
                        break;
                    case "conduite_emotionnelle":
                    case "conduite":
                    case "emotion":
                    case "emotions":
                        data.ConduiteEmotionnelle = ReadStringList(prop.Value);
                        break;
                    case "autonomie":
                        data.Autonomie = ReadStringOrArray(prop.Value);
                        break;
                    case "vie_quotidienne":
                    case "quotidien":
                    case "vie_domestique":
                        data.VieQuotidienne = ReadStringOrArray(prop.Value);
                        break;
                    case "social_scolaire":
                    case "scolaire":
                    case "social":
                    case "scolarite":
                        data.SocialScolaire = ReadStringOrArray(prop.Value);
                        break;
                }
            }
            return data;
        }

        private static string ReadStringOrArray(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
                return element.GetString() ?? string.Empty;
            if (element.ValueKind == JsonValueKind.Array)
            {
                var items = element.EnumerateArray()
                    .Select(x => x.ToString())
                    .Where(s => !string.IsNullOrWhiteSpace(s));
                return string.Join("\n", items);
            }
            return element.ToString();
        }

        private static List<string> ReadStringList(JsonElement element)
        {
            var list = new List<string>();
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                }
            }
            else if (element.ValueKind == JsonValueKind.String)
            {
                var s = element.GetString();
                if (!string.IsNullOrWhiteSpace(s))
                {
                    var lines = s.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(l => l.Trim().TrimStart('-', '•', '*').Trim())
                                 .Where(l => !string.IsNullOrWhiteSpace(l));
                    list.AddRange(lines);
                }
            }
            return list;
        }

        public override void Write(Utf8JsonWriter writer, RetentissementsData value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("mobilite", value.Mobilite);
            writer.WriteString("communication", value.Communication);

            writer.WriteStartArray("cognition");
            foreach (var c in value.Cognition) writer.WriteStringValue(c);
            writer.WriteEndArray();

            writer.WriteStartArray("conduite_emotionnelle");
            foreach (var ce in value.ConduiteEmotionnelle) writer.WriteStringValue(ce);
            writer.WriteEndArray();

            writer.WriteString("autonomie", value.Autonomie);
            writer.WriteString("vie_quotidienne", value.VieQuotidienne);
            writer.WriteString("social_scolaire", value.SocialScolaire);
            writer.WriteEndObject();
        }
    }

    /// <summary>
    /// Convertisseur tolérant pour TraitementsData (accepte objet ou tableau d'objets, gère variantes).
    /// </summary>
    public class LenientTraitementsConverter : JsonConverter<TraitementsData>
    {
        public override TraitementsData Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.StartArray)
            {
                TraitementsData? result = null;
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.StartObject && result == null)
                    {
                        using var doc = JsonDocument.ParseValue(ref reader);
                        result = ParseFromElement(doc.RootElement);
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
                return result ?? new TraitementsData();
            }

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                using var doc = JsonDocument.ParseValue(ref reader);
                return ParseFromElement(doc.RootElement);
            }

            if (reader.TokenType == JsonTokenType.Null)
            {
                return new TraitementsData();
            }

            reader.Skip();
            return new TraitementsData();
        }

        private static TraitementsData ParseFromElement(JsonElement element)
        {
            var data = new TraitementsData();
            if (element.ValueKind != JsonValueKind.Object) return data;

            foreach (var prop in element.EnumerateObject())
            {
                var name = prop.Name.ToLowerInvariant();
                switch (name)
                {
                    case "medicaments":
                    case "medicament":
                    case "médicaments":
                        data.Medicaments = ReadStringOrArray(prop.Value);
                        break;
                    case "effets_indesirables":
                    case "effets":
                    case "effet_indesirable":
                    case "effets_secondaires":
                        data.EffetsIndesirables = ReadStringOrArray(prop.Value);
                        break;
                    case "autres_prises_en_charge":
                    case "prises_en_charge":
                    case "autre_prise_en_charge":
                    case "prises_en_charges":
                        data.AutresPrisesEnCharge = ReadStringOrArray(prop.Value);
                        break;
                }
            }
            return data;
        }

        private static string ReadStringOrArray(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
                return element.GetString() ?? string.Empty;
            if (element.ValueKind == JsonValueKind.Array)
            {
                var items = element.EnumerateArray()
                    .Select(x => x.ToString())
                    .Where(s => !string.IsNullOrWhiteSpace(s));
                return string.Join("\n", items);
            }
            return element.ToString();
        }

        public override void Write(Utf8JsonWriter writer, TraitementsData value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("medicaments", value.Medicaments);
            writer.WriteString("effets_indesirables", value.EffetsIndesirables);
            writer.WriteString("autres_prises_en_charge", value.AutresPrisesEnCharge);
            writer.WriteEndObject();
        }
    }

    #endregion
}
