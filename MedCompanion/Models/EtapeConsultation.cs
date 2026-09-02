using System.Collections.Generic;

namespace MedCompanion.Models
{
    /// <summary>
    /// Une étape du parcours de consultation à laquelle un modèle peut être affecté.
    ///
    /// Le catalogue est FIXE et défini ici plutôt que déduit du code : les points d'appel LLM sont
    /// nombreux et de granularité inégale (un suggester, une passe de relecture, une extraction),
    /// et tous ne méritent pas un réglage. On n'expose que les étapes où le choix du modèle change
    /// réellement le résultat.
    /// </summary>
    public class EtapeConsultation
    {
        public required string Id { get; init; }
        public required string Libelle { get; init; }

        /// <summary>Regroupement d'affichage — sert les colonnes du schéma.</summary>
        public required string Phase { get; init; }

        /// <summary>Ce que fait l'étape, en une ligne, pour l'infobulle du schéma.</summary>
        public string Description { get; init; } = "";

        /// <summary>
        /// L'étape s'exécute sans être attendue (passe qualité lancée après l'extraction, par ex.).
        ///
        /// Une telle étape N'A PAS de modèle propre et hérite du modèle courant. La raison n'est pas
        /// esthétique : lui affecter un modèle différent la ferait redémarrer llama-server pendant
        /// que le médecin travaille sur autre chose. C'est exactement la course qui avait laissé deux
        /// modèles en VRAM et gelé la machine.
        /// </summary>
        public bool EnArrierePlan { get; init; }
    }

    /// <summary>Catalogue des étapes affectables, dans l'ordre du parcours.</summary>
    public static class EtapesConsultation
    {
        public const string PhasePremiere    = "1er entretien";
        public const string PhaseCartographie = "Cartographie de l'enfant";
        public const string PhaseEnvironnement = "Environnement & évaluation ciblée";
        public const string PhaseSuivi       = "Suivi";

        public static readonly IReadOnlyList<EtapeConsultation> Toutes = new[]
        {
            new EtapeConsultation
            {
                Id = "interrogatoire_extraction", Phase = PhasePremiere,
                Libelle = "Extraction interrogatoire",
                Description = "Transcription + notes → blocs cliniques et mots gardés. Gros volume d'entrée, sortie structurée."
            },
            // Pas d'entrée pour l'extraction « au fil de l'eau » : IncrementalExtractorService
            // existe mais n'est appelé nulle part. Lui offrir un réglage donnerait un contrôle sans
            // effet, ce qui est pire qu'une absence — on croit avoir configuré quelque chose.
            new EtapeConsultation
            {
                Id = "qualite", Phase = PhasePremiere,
                Libelle = "Passe qualité",
                Description = "Relit la note produite. Lancée en tâche de fond : hérite du modèle courant.",
                EnArrierePlan = true
            },
            new EtapeConsultation
            {
                Id = "observations_suggestions", Phase = PhasePremiere,
                Libelle = "Suggestions observations",
                Description = "Propose un intitulé et 3-4 qualificatifs adaptés à l'âge pour chacun des 9 axes d'observation clinique."
            },
            new EtapeConsultation
            {
                Id = "synthese_initiale", Phase = PhasePremiere,
                Libelle = "Synthèse initiale",
                Description = "Intègre l'ensemble du premier entretien. Étape de raisonnement."
            },
            new EtapeConsultation
            {
                Id = "restitution", Phase = PhasePremiere,
                Libelle = "Restitution 1er entretien",
                Description = "Rédaction destinée aux parents. Étape de raisonnement et de ton."
            },
            new EtapeConsultation
            {
                Id = "cartographie_synthese", Phase = PhaseCartographie,
                Libelle = "Synthèse de la cartographie",
                Description = "Présente et qualifie les deux moitiés — questionnaire parent et profils observés — "
                            + "en tenant compte des fiabilités déclarées. Étape de raisonnement, sans conclusion."
            },
            // Pas d'entrée pour la lecture des cases de la feuille : c'est une tâche de VISION,
            // servie par LlamaCppProfiles.VisionCapable et choisie dans la fenêtre de
            // dépouillement elle-même. La mêler aux modèles de texte laisserait croire qu'un
            // modèle sans projecteur peut lire une image.

            new EtapeConsultation
            {
                Id = "orientation_diagnostique", Phase = PhaseEnvironnement,
                Libelle = "Orientation diagnostique",
                Description = "Lit tout le dossier bleu (synthèse, dernière note, synthèses de bilans, cartographie) "
                            + "et propose au plus 3 éléments par rubrique à observer dans la séance. "
                            + "Longue entrée, sortie courte et contrainte."
            },
            new EtapeConsultation
            {
                Id = "evaluation_ciblee", Phase = PhaseEnvironnement,
                Libelle = "Évaluation ciblée",
                Description = "Dérive de l'orientation les axes que le médecin ira observer, puis les constats "
                            + "cochables de chaque axe. Un appel court par axe, tous sur le même modèle."
            },
            new EtapeConsultation
            {
                Id = "seance3_synthese", Phase = PhaseEnvironnement,
                Libelle = "Synthèse de la séance",
                Description = "Présente et qualifie les deux blocs — environnement réuni, évaluation ciblée — "
                            + "selon leurs fiabilités déclarées, puis les met en regard. "
                            + "Étape de raisonnement, sans conclusion."
            },

            new EtapeConsultation
            {
                Id = "suivi_extraction", Phase = PhaseSuivi,
                Libelle = "Extraction consultation de suivi",
                Description = "Segment de suivi → puces cliniques et mots gardés."
            },
        };

        public static EtapeConsultation? Par(string id)
        {
            foreach (var e in Toutes) if (e.Id == id) return e;
            return null;
        }
    }
}
