using System.Collections.Generic;
using System.Linq;

namespace MedCompanion.Models
{
    /// <summary>
    /// Résultat temporaire de l'extraction d'entités sensibles par le LLM.
    /// Ces données sont utilisées pour l'anonymisation puis immédiatement détruites.
    /// </summary>
    public class PIIExtractionResult
    {
        public List<string> Noms { get; set; } = new();
        public List<string> Dates { get; set; } = new();
        public List<string> Lieux { get; set; } = new();
        public List<string> Organisations { get; set; } = new();
        
        /// <summary>
        /// Retourne toutes les entités extraites sous forme de liste plate.
        /// </summary>
        public List<string> GetAllEntities()
        {
            return Noms
                .Concat(Dates)
                .Concat(Lieux)
                .Concat(Organisations)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList();
        }
    }
}
