using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MedCompanion.Models;

namespace MedCompanion.Services.Consultation
{
    /// <summary>
    /// BlockSetResolver — version "soustraction"
    /// Tous les blocs sont chargés dès le début (visibilité totale).
    /// L'âge et le motif servent à proposer un AUTO-MASQUAGE (pas à filtrer l'initialisation).
    /// </summary>
    public class BlockSetResolver
    {
        private readonly List<BlockDefinition> _library;

        public BlockSetResolver()
        {
            _library = LoadLibrary();
        }

        /// <summary>
        /// Retourne TOUS les blocs, ordonnés. La sélection se fait par masquage côté UI.
        /// L'argument <paramref name="ageYears"/> est conservé pour compatibilité mais ignoré.
        /// </summary>
        public List<BlockDefinition> Resolve(int ageYears) => ResolveAll();

        /// <summary>
        /// Retourne TOUS les blocs (âge inconnu ou non — peu importe).
        /// </summary>
        public List<BlockDefinition> ResolveWithoutAge() => ResolveAll();

        private List<BlockDefinition> ResolveAll() => _library
            .OrderBy(b => b.Order)
            .ToList();

        /// <summary>
        /// Retourne les clés des blocs à masquer automatiquement après confirmation
        /// d'âge + motif. Règles conservatrices : on masque uniquement les cas évidents
        /// (incompatibilité d'âge stricte). On ne masque PAS sur seul critère de motif —
        /// le médecin peut toujours masquer manuellement via le bouton 👁.
        /// </summary>
        public HashSet<string> GetAutoHideKeys(int ageYears)
        {
            var hide = new HashSet<string>();
            foreach (var block in _library)
            {
                bool ageMismatch =
                    (block.AgeMin.HasValue && ageYears < block.AgeMin.Value) ||
                    (block.AgeMax.HasValue && ageYears > block.AgeMax.Value);

                if (ageMismatch) hide.Add(block.Key);
            }
            return hide;
        }

        /// <summary>
        /// Conservé pour compatibilité — retourne null (plus de macro-bloc dynamique).
        /// </summary>
        public BlockDefinition? GetAgeBlock(int ageYears) => null;

        /// <summary>
        /// Conservé pour compatibilité — retourne tous les blocs éligibles à l'âge donné.
        /// </summary>
        public List<BlockDefinition> GetEligibleChipBlocks(int ageYears)
        {
            return _library
                .Where(b => ageYears >= (b.AgeMin ?? 0) && ageYears <= (b.AgeMax ?? 99))
                .OrderBy(b => b.Order)
                .ToList();
        }

        /// <summary>
        /// Retrouve une définition de bloc par sa clé.
        /// </summary>
        public BlockDefinition? GetByKey(string key)
        {
            return _library.FirstOrDefault(b =>
                string.Equals(b.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Convertit une liste de BlockDefinition en ConsultationBlock prêts à l'emploi.
        /// </summary>
        public static List<ConsultationBlock> ToConsultationBlocks(List<BlockDefinition> definitions)
        {
            return definitions.Select(d => new ConsultationBlock
            {
                Key = d.Key,
                Title = d.Title,
                ExpectedThemes = new List<string>(d.ExpectedThemes)
            }).ToList();
        }

        // ── Chargement de la bibliothèque ──────────────────────────────────────

        private static readonly string _libraryPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Resources", "Consultation", "block_library.json");

        private static List<BlockDefinition> LoadLibrary()
        {
            try
            {
                if (!File.Exists(_libraryPath))
                    return GetDefaults();

                var json = File.ReadAllText(_libraryPath, System.Text.Encoding.UTF8);
                return JsonSerializer.Deserialize<List<BlockDefinition>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                    ?? GetDefaults();
            }
            catch
            {
                return GetDefaults();
            }
        }

        /// <summary>
        /// Définitions par défaut si block_library.json est absent.
        /// Noyau fixe V0b + 2 macro-blocs contextuels.
        /// </summary>
        private static List<BlockDefinition> GetDefaults() => new()
        {
            new() { Key = "identite",                Title = "Identité",                        ExpectedThemes = new() { "age", "accompagnant", "classe", "ecole" },                TriggerType = "core_fixed", Order = 1 },
            new() { Key = "motif",                   Title = "Motif de consultation",           ExpectedThemes = new() { "motif_principal" },                                       TriggerType = "core_fixed", Order = 2 },
            new() { Key = "histoire_maladie",        Title = "Histoire de la maladie",          ExpectedThemes = new() { "anciennete", "retentissement", "parcours_soins" },        TriggerType = "core_fixed", Order = 3 },
            new() { Key = "grossesse_accouchement",  Title = "Grossesse & accouchement",        ExpectedThemes = new() { "deroulement_grossesse", "terme_accouchement", "mode_accouchement", "complications_perinatales" }, TriggerType = "core_fixed", Order = 4 },
            new() { Key = "developpement",           Title = "Développement psychomoteur",      ExpectedThemes = new() { "marche", "langage_precoce", "proprete" },                 TriggerType = "core_fixed", Order = 5 },
            new() { Key = "famille",                 Title = "Famille & contexte social",       ExpectedThemes = new() { "statut_parental", "pere", "mere" },                       TriggerType = "core_fixed", Order = 6 },
            new() { Key = "fratrie",                 Title = "Fratrie",                         ExpectedThemes = new() { "presence", "noms_ages" },                                 TriggerType = "core_fixed", Order = 7 },
            new() { Key = "atcds",                   Title = "ATCDs médicaux & psychiatriques", ExpectedThemes = new() { "medical", "allergies" },                                  TriggerType = "core_fixed", Order = 8 },
            new() { Key = "sommeil_ecrans",          Title = "Sommeil, rythmes & écrans",       ExpectedThemes = new() { "sommeil", "ecrans" },                                     TriggerType = "core_fixed", Order = 9 },
            new() { Key = "scolarite_activites",     Title = "Scolarité & vie sociale",         ExpectedThemes = new() { "ecole", "comportement_classe", "amis" },                  TriggerType = "core_fixed", Order = 10 },
        };
    }
}
