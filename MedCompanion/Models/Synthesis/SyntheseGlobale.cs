using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace MedCompanion.Models.Synthesis
{
    /// <summary>
    /// Statut d'une Synthèse Globale dans son cycle de vie.
    /// - Brouillon : en cours d'édition, modifiable, NON utilisée comme source de vérité.
    /// - Validee   : figée, immuable, source de vérité pour les LLM jusqu'à la prochaine version validée.
    /// </summary>
    public enum SyntheseStatut
    {
        Brouillon = 0,
        Validee   = 1
    }

    /// <summary>
    /// Une Synthèse Globale = document structuré, versionné, qui sert de source de vérité
    /// pour le contexte patient (alimente les prompts LLM des consultations et évaluations
    /// suivantes). Construction incrémentale : chaque version (v1, v2, …) est un patch sur
    /// la précédente, déclenché par accumulation de poids (SynthesisWeightTracker, seuil 1.0).
    ///
    /// 6 sections cliniques fixes :
    /// 1. Hypothèses diagnostiques retenues
    /// 2. Compréhension du fonctionnement de l'enfant
    /// 3. Compréhension de l'environnement
    /// 4. Articulation clinique
    /// 5. Conclusion intégrative globale
    /// 6. Évolution depuis la dernière synthèse (v2+ uniquement, vide pour v1)
    /// </summary>
    public class SyntheseGlobale : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        // ── Métadonnées ──────────────────────────────────────────────────────

        /// <summary>Numéro de version (1, 2, 3…). v0 n'existe pas — la première = v1.</summary>
        public int Version { get; set; } = 1;

        /// <summary>Nom complet du patient (ex: "LISERON_Evan").</summary>
        public string PatientNomComplet { get; set; } = "";

        /// <summary>Date de rédaction (création du brouillon).</summary>
        public DateTime DateRedaction { get; set; } = DateTime.Now;

        /// <summary>Date de validation (figée, immuable). Null tant qu'en brouillon.</summary>
        public DateTime? DateValidation { get; set; }

        /// <summary>Nom du psychiatre signataire.</summary>
        public string Psychiatre { get; set; } = "";

        private SyntheseStatut _statut = SyntheseStatut.Brouillon;
        public SyntheseStatut Statut
        {
            get => _statut;
            set
            {
                if (_statut != value)
                {
                    _statut = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsValidee));
                    OnPropertyChanged(nameof(IsBrouillon));
                }
            }
        }
        public bool IsValidee  => _statut == SyntheseStatut.Validee;
        public bool IsBrouillon => _statut == SyntheseStatut.Brouillon;

        /// <summary>Nom de fichier de la version précédente (null pour v1).</summary>
        public string? VersionPrecedenteFichier { get; set; }

        /// <summary>Chemin absolu du fichier sur disque (vide tant que non sauvegardé).</summary>
        public string FilePath { get; set; } = "";

        // ── Sources qui ont alimenté cette version (traçabilité) ─────────────

        /// <summary>Dates des évaluations clôturées utilisées (ISO yyyy-MM-dd).</summary>
        public List<string> SourcesEvaluations { get; set; } = new();

        /// <summary>Nombre de notes de consultation utilisées.</summary>
        public int SourcesNombreNotes { get; set; }

        /// <summary>Nombre de documents importés utilisés.</summary>
        public int SourcesNombreDocuments { get; set; }

        // ── Garde-fou drift incrémental ──────────────────────────────────────

        /// <summary>
        /// Compteur d'incréments depuis la dernière révision majeure (refonte complète).
        /// Une révision majeure remet ce compteur à 0. Au-delà de 5 ou après 6 mois,
        /// l'UI propose au psy de faire une révision majeure plutôt qu'un nouveau patch.
        /// </summary>
        public int IncrementsDepuisRevisionMajeure { get; set; }

        // ── Sections cliniques ───────────────────────────────────────────────

        public SyntheseSection Hypotheses    { get; }
        public SyntheseSection Enfant        { get; }
        public SyntheseSection Environnement { get; }
        public SyntheseSection Articulation  { get; }
        public SyntheseSection Conclusion    { get; }
        public SyntheseSection Evolution     { get; }

        /// <summary>
        /// Sections affichées et sérialisées. "Évolution depuis la dernière synthèse" n'apparaît
        /// qu'à partir de la v2 (elle est vide et inutile pour une synthèse initiale).
        /// </summary>
        public IReadOnlyList<SyntheseSection> Sections => Version > 1
            ? new[] { Hypotheses, Enfant, Environnement, Articulation, Conclusion, Evolution }
            : new[] { Hypotheses, Enfant, Environnement, Articulation, Conclusion };

        public SyntheseGlobale()
        {
            Hypotheses    = new SyntheseSection("hypotheses",    "Hypothèses diagnostiques retenues");
            Enfant        = new SyntheseSection("enfant",        "Compréhension du fonctionnement de l'enfant");
            Environnement = new SyntheseSection("environnement", "Compréhension de l'environnement");
            Articulation  = new SyntheseSection("articulation",  "Articulation clinique");
            Conclusion    = new SyntheseSection("conclusion",    "Conclusion intégrative globale");
            Evolution     = new SyntheseSection("evolution",     "Évolution depuis la dernière synthèse");
        }

        /// <summary>Récupère une section par sa clé technique, ou null si introuvable.</summary>
        public SyntheseSection? GetSection(string key)
            => Sections.FirstOrDefault(s => s.Key == key);

        /// <summary>True si au moins une section a du contenu (utile pour HasAnyContenu UI).</summary>
        public bool HasAnyContenu => Sections.Any(s => s.HasContenu);
    }
}
