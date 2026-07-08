using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MedCompanion.Models.Livres
{
    /// <summary>
    /// Un livre de l'Atelier d'écriture (mode Bureau).
    /// Stocké dans Documents/MedCompanion/livres/&lt;slug&gt;/livre.json,
    /// avec un fichier Markdown par chapitre dans chapitres/ et une mémoire
    /// narrative (personnages, intrigue, ton) dans memoire.md.
    /// </summary>
    public class Livre
    {
        public string Titre { get; set; } = "";
        public string Auteur { get; set; } = "";
        public MiseEnPageLivre MiseEnPage { get; set; } = new();
        public List<ChapitreLivre> Chapitres { get; set; } = new();
        public DateTime DateCreation { get; set; } = DateTime.Now;
        public DateTime DateModification { get; set; } = DateTime.Now;

        /// <summary>Chemin absolu du dossier du livre — renseigné au chargement, jamais persisté.</summary>
        [JsonIgnore]
        public string DossierPath { get; set; } = "";
    }

    public class ChapitreLivre
    {
        public string Titre { get; set; } = "";

        /// <summary>Nom du fichier Markdown, relatif au sous-dossier chapitres/.</summary>
        public string Fichier { get; set; } = "";

        public int Ordre { get; set; }

        public override string ToString() => Titre;
    }

    /// <summary>
    /// Caractéristiques de mise en page façon LibreOffice, appliquées au HTML
    /// d'aperçu et au PDF exporté (via @page CSS).
    /// </summary>
    public class MiseEnPageLivre
    {
        /// <summary>"A4", "A5" ou "Poche" (110×180 mm).</summary>
        public string Format { get; set; } = "A5";

        public double MargeHautMm { get; set; } = 20;
        public double MargeBasMm { get; set; } = 20;
        public double MargeGaucheMm { get; set; } = 18;
        public double MargeDroiteMm { get; set; } = 18;

        public string Police { get; set; } = "Georgia";
        public double TaillePt { get; set; } = 11.5;
        public double Interligne { get; set; } = 1.6;

        public bool Justifie { get; set; } = true;
        public bool RetraitPremiereLigne { get; set; } = true;

        public (double largeurMm, double hauteurMm) GetDimensionsMm() => Format switch
        {
            "A4"    => (210, 297),
            "Poche" => (110, 180),
            _       => (148, 210) // A5
        };
    }
}
