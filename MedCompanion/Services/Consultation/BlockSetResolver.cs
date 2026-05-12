using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MedCompanion.Models;

namespace MedCompanion.Services.Consultation
{
    /// <summary>
    /// Phase B — BlockSetResolver
    /// Résout l'ensemble des blocs actifs pour une consultation selon l'âge du patient.
    /// Règle : noyau fixe (6 blocs) + 1 macro-bloc contextuel par âge.
    /// Structure gelée après résolution (~90s).
    /// </summary>
    public class BlockSetResolver
    {
        private readonly List<BlockDefinition> _library;

        public BlockSetResolver()
        {
            _library = LoadLibrary();
        }

        /// <summary>
        /// Résout les blocs actifs pour un patient d'un âge donné.
        /// Retourne les blocs core_fixed + le macro-bloc age_automatic correspondant.
        /// </summary>
        /// <param name="ageYears">Âge confirmé du patient en années</param>
        /// <returns>Liste ordonnée de BlockDefinition à instancier comme ConsultationBlock</returns>
        public List<BlockDefinition> Resolve(int ageYears)
        {
            var result = new List<BlockDefinition>();

            // 1. Noyau fixe (toujours présent)
            var coreBlocks = _library
                .Where(b => b.TriggerTypeEnum == BlockTriggerType.CoreFixed)
                .OrderBy(b => b.Order)
                .ToList();

            result.AddRange(coreBlocks);

            // 2. Macro-bloc contextuel par âge (un seul)
            var ageBlock = _library
                .Where(b => b.TriggerTypeEnum == BlockTriggerType.AgeAutomatic)
                .FirstOrDefault(b => ageYears >= (b.AgeMin ?? 0) && ageYears < (b.AgeMax ?? 99));

            if (ageBlock != null)
                result.Add(ageBlock);

            return result;
        }

        /// <summary>
        /// Résout les blocs en mode "âge inconnu" — retourne uniquement le noyau fixe.
        /// Le macro-bloc sera ajouté dynamiquement après confirmation de l'âge.
        /// </summary>
        public List<BlockDefinition> ResolveWithoutAge()
        {
            return _library
                .Where(b => b.TriggerTypeEnum == BlockTriggerType.CoreFixed)
                .OrderBy(b => b.Order)
                .ToList();
        }

        /// <summary>
        /// Retourne le macro-bloc contextuel pour un âge donné.
        /// Utilisé pour ajouter dynamiquement le bloc après confirmation de l'âge.
        /// </summary>
        public BlockDefinition? GetAgeBlock(int ageYears)
        {
            return _library
                .Where(b => b.TriggerTypeEnum == BlockTriggerType.AgeAutomatic)
                .FirstOrDefault(b => ageYears >= (b.AgeMin ?? 0) && ageYears < (b.AgeMax ?? 99));
        }

        /// <summary>
        /// Retourne tous les blocs motif_chip disponibles (pour référence).
        /// Le filtrage par motif se fait dans ContextualBlockSuggester.
        /// </summary>
        public List<BlockDefinition> GetAllChipBlocks()
        {
            return _library
                .Where(b => b.TriggerTypeEnum == BlockTriggerType.MotifChip)
                .OrderBy(b => b.Order)
                .ToList();
        }

        /// <summary>
        /// Retourne les blocs motif_chip éligibles pour un âge donné.
        /// Filtre par ageMin/ageMax si définis.
        /// </summary>
        public List<BlockDefinition> GetEligibleChipBlocks(int ageYears)
        {
            return _library
                .Where(b => b.TriggerTypeEnum == BlockTriggerType.MotifChip)
                .Where(b => ageYears >= (b.AgeMin ?? 0) && ageYears < (b.AgeMax ?? 99))
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
            new() { Key = "identite",          Title = "Identité",                         ExpectedThemes = new() { "age", "accompagnant", "classe", "ecole" },                  TriggerType = "core_fixed", Order = 1 },
            new() { Key = "motif",             Title = "Motif de consultation",            ExpectedThemes = new() { "motif_principal" },                                         TriggerType = "core_fixed", Order = 2 },
            new() { Key = "histoire_maladie",  Title = "Histoire de la maladie",           ExpectedThemes = new() { "anciennete", "retentissement", "parcours_soins" },           TriggerType = "core_fixed", Order = 3 },
            new() { Key = "famille",           Title = "Famille & contexte social",        ExpectedThemes = new() { "statut_parental", "pere", "mere" },                         TriggerType = "core_fixed", Order = 4 },
            new() { Key = "fratrie",           Title = "Fratrie",                          ExpectedThemes = new() { "presence", "noms_ages" },                                   TriggerType = "core_fixed", Order = 5 },
            new() { Key = "atcds",             Title = "ATCDs médicaux & psychiatriques",  ExpectedThemes = new() { "medical", "allergies" },                                    TriggerType = "core_fixed", Order = 6 },
            new() { Key = "sommeil_ecrans",    Title = "Sommeil, rythmes & écrans",        ExpectedThemes = new() { "sommeil", "ecrans" },                                       TriggerType = "core_fixed", Order = 7 },

            new() { Key = "petite_enfance",       Title = "Petite enfance",       ExpectedThemes = new() { "grossesse", "accouchement", "marche", "langage_precoce" }, TriggerType = "age_automatic", AgeMin = 0, AgeMax = 3,  Order = 8 },
            new() { Key = "scolarite_activites",  Title = "Scolarité & activités", ExpectedThemes = new() { "ecole", "comportement_classe", "amis" },                  TriggerType = "age_automatic", AgeMin = 3, AgeMax = 99, Order = 8 },
        };
    }
}
