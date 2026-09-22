using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

namespace MedCompanion.Models.Infographies
{
    /// <summary>
    /// Une fiche de la bibliothèque d'infographies (ressources remises aux familles).
    /// Stockée dans Documents/MedCompanion/bibliotheque/infographies/&lt;id&gt;/fiche.json,
    /// à côté de l'image elle-même.
    /// </summary>
    public class Infographie
    {
        public string Titre { get; set; } = "";

        /// <summary>Trouble, Traitement, Conseils pratiques, Démarches, Hygiène de vie…</summary>
        public string Type { get; set; } = "";

        /// <summary>TDAH, Méthylphénidate, Anxiété… Une fiche peut porter plusieurs sujets.</summary>
        public List<string> Sujets { get; set; } = new();

        /// <summary>Parents, Enfant, Adolescent, École.</summary>
        public string Public { get; set; } = "Parents";

        public string Source { get; set; } = "NotebookLM";
        public string Notes { get; set; } = "";

        /// <summary>Nom du fichier image, relatif au dossier de la fiche.</summary>
        public string Fichier { get; set; } = "";

        public DateTime DateImport { get; set; } = DateTime.Now;
        public DateTime DateModification { get; set; } = DateTime.Now;

        /// <summary>
        /// Date de relecture par le médecin. Null = « à relire » : la fiche n'apparaît pas
        /// en consultation. Remise à null quand l'image est remplacée.
        /// </summary>
        public DateTime? DateValidation { get; set; }

        [JsonIgnore]
        public bool EstValidee => DateValidation.HasValue;

        /// <summary>Nom du dossier de la fiche — renseigné au chargement, jamais persisté.</summary>
        [JsonIgnore]
        public string Id { get; set; } = "";

        /// <summary>Chemin absolu du dossier de la fiche — renseigné au chargement, jamais persisté.</summary>
        [JsonIgnore]
        public string DossierPath { get; set; } = "";

        [JsonIgnore]
        public string ImagePath => Path.Combine(DossierPath, Fichier);
    }

    /// <summary>
    /// Trace d'une fiche imprimée pour un patient.
    /// Stockée dans &lt;patient&gt;/info_patient/infographies_remises.json.
    /// </summary>
    public class RemiseInfographie
    {
        public string Id { get; set; } = "";
        public string Titre { get; set; } = "";
        public DateTime Date { get; set; } = DateTime.Now;
    }
}
