using System;
using System.Collections.Generic;

namespace MedCompanion.Models.Carte
{
    /// <summary>
    /// Une ligne d'un bloc de la carte. <see cref="Vide"/> : l'information manque, elle s'affiche
    /// en gris — c'est ce qu'il reste à demander (PLAN_CARTE_PATIENT.md, décision 4).
    /// </summary>
    public class LigneCarte
    {
        public LigneCarte(string libelle, string valeur, bool vide = false, string? source = null)
        {
            Libelle = libelle;
            Valeur = valeur;
            Vide = vide;
            Source = source;
        }

        /// <summary>« Garde », « Fratrie »… Vide pour une ligne sans intitulé.</summary>
        public string Libelle { get; }
        public string Valeur { get; }
        public bool Vide { get; }

        /// <summary>Document d'où vient l'information, ouvert au clic (étape 3 du plan).</summary>
        public string? Source { get; }

        public bool ALibelle => Libelle.Length > 0;
        public bool ASource => !string.IsNullOrEmpty(Source);
        public string LibelleAvecDeuxPoints => ALibelle ? Libelle + " : " : "";
    }

    /// <summary>Un bloc de la carte, à place fixe autour de l'enfant.</summary>
    public class BlocCarte
    {
        public BlocCarte(string icone, string titre, string origine)
        {
            Icone = icone;
            Titre = titre;
            Origine = origine;
        }

        public string Icone { get; }
        public string Titre { get; }

        /// <summary>D'où le bloc tient ses informations (« admin + 1ère consult. »).</summary>
        public string Origine { get; }

        public List<LigneCarte> Lignes { get; } = new();

        public void Ajouter(string libelle, string valeur, string? source = null)
            => Lignes.Add(new LigneCarte(libelle, valeur, false, source));

        public void Manque(string libelle, string valeur)
            => Lignes.Add(new LigneCarte(libelle, valeur, true));
    }

    public enum EtatEtape { Fait, EnCours, Prochaine, AVenir }

    /// <summary>Une étape du parcours 3-11 ans, reprise de la frise du mode Consultation.</summary>
    public class EtapeParcours
    {
        public string Libelle { get; init; } = "";
        public EtatEtape Etat { get; init; }
        public DateTime? Date { get; init; }
        public bool Derniere { get; set; }

        public string Texte => Etat switch
        {
            EtatEtape.Fait      => $"✓ {Libelle}{(Date.HasValue ? $" · {Date:dd/MM}" : "")}",
            EtatEtape.EnCours   => $"▶ {Libelle} — en cours",
            EtatEtape.Prochaine => $"○ {Libelle} — prochaine étape",
            _                   => Libelle
        };
    }

    /// <summary>Une valeur extraite des notes, avec la date de la note d'où elle vient.</summary>
    public class ChampCarte
    {
        public string Valeur { get; set; } = "";

        /// <summary>« 05/01/2026 » : la note source. Une classe lue l'an dernier se reconnaît ainsi.</summary>
        public string Note { get; set; } = "";
    }

    /// <summary>
    /// Ce que Med a extrait des notes et que le médecin a VALIDÉ (PLAN_CARTE_PATIENT.md, étape 2).
    /// Stocké dans info_patient/carte.json. Ne remplace jamais l'admin ni la 1ère consultation
    /// structurée : ne sert qu'à combler ce qu'elles ne disent pas.
    /// </summary>
    public class CompletionCarte
    {
        public DateTime ValideLe { get; set; } = DateTime.Now;
        public Dictionary<string, ChampCarte> Champs { get; set; } = new();

        /// <summary>Les champs, dans l'ordre de la fenêtre de validation, avec leur intitulé.</summary>
        public static readonly (string cle, string libelle)[] Liste =
        {
            ("situation",   "Situation des parents"),
            ("garde",       "Mode de garde"),
            ("pere",        "Père"),
            ("mere",        "Mère"),
            ("fratrie",     "Fratrie"),
            ("motif",       "Motif de consultation"),
            ("oriente_par", "Orienté par"),
            ("ecole",       "École / établissement"),
            ("classe",      "Classe"),
        };

        public ChampCarte? Champ(string cle)
            => Champs.TryGetValue(cle, out var c) && !string.IsNullOrWhiteSpace(c.Valeur) ? c : null;
    }

    /// <summary>La carte d'un patient : ce qu'il faut voir d'un coup d'œil (PLAN_CARTE_PATIENT.md).</summary>
    public class CartePatient
    {
        /// <summary>Les champs que ni l'admin ni la 1ère consultation ne renseignent : Med peut
        /// proposer de les compléter depuis les notes.</summary>
        public List<string> ChampsManquants { get; } = new();
        public bool PeutCompleter => ChampsManquants.Count > 0;

        public string Nom { get; set; } = "";
        public string AgeNaissance { get; set; } = "";
        public string Scolarite { get; set; } = "";
        public bool ScolariteVide { get; set; }

        public BlocCarte Famille { get; set; } = new("👪", "Famille", "");
        public BlocCarte Motif { get; set; } = new("🎯", "Motif", "");
        public BlocCarte Suivi { get; set; } = new("📅", "Suivi", "");
        public BlocCarte Bilans { get; set; } = new("🧪", "Bilans", "");
        public BlocCarte Courriers { get; set; } = new("✉️", "Courriers", "");
        public BlocCarte Traitement { get; set; } = new("💊", "Traitement", "");
        public BlocCarte Autour { get; set; } = new("🤝", "Autour de l'enfant", "");

        public List<EtapeParcours> Parcours { get; } = new();
        public bool AParcours => Parcours.Count > 0;

        public DateTime CalculeeLe { get; set; } = DateTime.Now;
    }
}
