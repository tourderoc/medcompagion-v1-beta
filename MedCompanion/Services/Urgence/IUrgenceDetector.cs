using System.Threading;
using System.Threading.Tasks;
using MedCompanion.Models.Urgences;

namespace MedCompanion.Services.Urgence
{
    /// <summary>
    /// Contrat d'un détecteur d'urgence clinique.
    /// Une instance = une catégorie d'urgence (risque suicidaire, maltraitance, ...).
    /// </summary>
    public interface IUrgenceDetector
    {
        /// <summary>Identifiant stable (ex: "risque_suicidaire"), utilisé pour le typage des fichiers.</summary>
        string UrgenceType { get; }

        /// <summary>Nom + version humainement lisible (ex: "SuicideRiskDetector_v1"), tracé dans le YAML.</summary>
        string Name { get; }

        /// <summary>
        /// Analyse la note. Renvoie un UrgenceSignal si une alerte est pertinente, null sinon.
        /// Le détecteur NE prend AUCUNE décision clinique — il propose, le médecin tranche.
        /// </summary>
        Task<UrgenceSignal?> DetectAsync(UrgenceNoteContext context, CancellationToken ct = default);
    }
}
