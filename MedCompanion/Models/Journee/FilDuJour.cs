using System;
using System.Collections.Generic;
using MedCompanion.Models.Agenda;

namespace MedCompanion.Models.Journee
{
    /// <summary>
    /// Où en est un rendez-vous, vu par Med. Distinct du statut Doctolib, qui est recopié tel
    /// quel dans <see cref="RendezVous.Statut"/> : celui-ci se déduit de ce que le médecin a fait
    /// dans le dossier (PLAN_JOURNEE_COMPAGNON.md, « Présence du patient »).
    /// </summary>
    public enum EtatRendezVous
    {
        /// <summary>Pas encore l'heure, ou l'heure est là sans aucune trace pour l'instant.</summary>
        AVenir,
        /// <summary>Dictée ou écriture dans le dossier pendant la fenêtre du rendez-vous.</summary>
        EnCours,
        /// <summary>Une note ou une séance consignée ce jour-là.</summary>
        Termine,
        /// <summary>Le patient est venu (trace d'activité, Doctolib « Vu ») mais rien n'est écrit.</summary>
        SansNote,
        /// <summary>Fenêtre passée sans la moindre trace : absent, ou oubli de pointage ?</summary>
        SansNouvelles,
        Absent,
        Excuse,
        Deplace,
        Annule,
        /// <summary>Aucun dossier sûr : Med ne conclut rien, sauf ce que dit Doctolib.</summary>
        DossierIncertain
    }

    /// <summary>Un rendez-vous de l'agenda et l'état que Med en déduit.</summary>
    public class RendezVousDuJour
    {
        public RendezVousDuJour(RendezVous rdv)
        {
            Rdv = rdv;
            Dossier = rdv.Certitude == "sur" ? rdv.Dossier : "";
        }

        public RendezVous Rdv { get; }
        public EtatRendezVous Etat { get; set; } = EtatRendezVous.AVenir;

        /// <summary>La note a été écrite après la fenêtre du rendez-vous (le soir, par exemple).</summary>
        public bool NoteRattrapee { get; set; }

        /// <summary>Ce sur quoi Med s'appuie, pour que l'état soit explicable au médecin.</summary>
        public List<string> Preuves { get; } = new();

        /// <summary>Le dossier rattaché quand il est sûr — par l'agenda, ou retrouvé par Med au
        /// moment du calcul (nom ET date de naissance concordants) — sinon vide.</summary>
        public string Dossier { get; set; }

        public string Libelle => Etat switch
        {
            EtatRendezVous.AVenir           => "À venir",
            EtatRendezVous.EnCours          => "En cours",
            EtatRendezVous.Termine          => NoteRattrapee ? "Terminé — note écrite après coup" : "Terminé, note écrite",
            EtatRendezVous.SansNote         => "Venu, pas de note",
            EtatRendezVous.SansNouvelles    => "Aucune trace — absent ?",
            EtatRendezVous.Absent           => "Absent",
            EtatRendezVous.Excuse           => "Absent excusé",
            EtatRendezVous.Deplace          => "Déplacé",
            EtatRendezVous.Annule           => "Annulé",
            EtatRendezVous.DossierIncertain => "Dossier à confirmer",
            _                               => ""
        };

        public string Icone => Etat switch
        {
            EtatRendezVous.AVenir           => "",
            EtatRendezVous.EnCours          => "▶",
            EtatRendezVous.Termine          => "✓",
            EtatRendezVous.SansNote         => "✎",
            EtatRendezVous.SansNouvelles    => "?",
            EtatRendezVous.Absent           => "✕",
            EtatRendezVous.Excuse           => "✕",
            EtatRendezVous.Deplace          => "↔",
            EtatRendezVous.Annule           => "",
            EtatRendezVous.DossierIncertain => "🗂",
            _                               => ""
        };

        public string Couleur => Etat switch
        {
            EtatRendezVous.EnCours          => "#2E86C1",
            EtatRendezVous.Termine          => "#1E8449",
            EtatRendezVous.SansNote         => "#D35400",
            EtatRendezVous.SansNouvelles    => "#C0392B",
            EtatRendezVous.Absent           => "#C0392B",
            EtatRendezVous.Excuse           => "#B9770E",
            EtatRendezVous.Deplace          => "#7D3C98",
            EtatRendezVous.DossierIncertain => "#7F8C8D",
            _                               => "#7F8C8D"
        };

        /// <summary>Le patient est venu : c'est ce qui compte comme passage dans le registre.</summary>
        public bool EstVenu => Etat is EtatRendezVous.EnCours or EtatRendezVous.Termine or EtatRendezVous.SansNote;
    }

    /// <summary>Un patient sans rendez-vous ce jour-là, dont le dossier a reçu une note ou une séance.</summary>
    public class PassageHorsAgenda
    {
        public string Dossier { get; set; } = "";
        public string Nom { get; set; } = "";
        public DateTime Heure { get; set; }
        public string Quoi { get; set; } = "";
    }

    /// <summary>La journée vue par Med : chaque rendez-vous avec son état, et ce qui s'est passé hors agenda.</summary>
    public class FilDuJour
    {
        public DateTime Jour { get; set; }
        public List<RendezVousDuJour> RendezVous { get; } = new();
        public List<PassageHorsAgenda> HorsAgenda { get; } = new();
    }

    /// <summary>
    /// Une ligne du registre des passages — la mémoire de Med. Qui, quand, dans quel état :
    /// jamais de contenu clinique, qui reste dans le dossier patient.
    /// </summary>
    public class EntreePassage
    {
        /// <summary>Dossier Med (NOM_Prénom) : la clé.</summary>
        public string Dossier { get; set; } = "";
        public string Nom { get; set; } = "";

        /// <summary>Dernier jour où le patient est venu (vu, avec ou sans note).</summary>
        public DateTime? DerniereVenue { get; set; }

        /// <summary>Dernier rendez-vous passé, venu ou non.</summary>
        public DateTime? DernierRdv { get; set; }

        /// <summary>État de ce dernier rendez-vous, en clair (« Venu, pas de note », « Absent »…).</summary>
        public string DernierEtat { get; set; } = "";

        /// <summary>Prochain rendez-vous connu dans l'agenda, s'il y en a un.</summary>
        public DateTime? ProchainRdv { get; set; }
    }

    /// <summary>Le registre sur disque.</summary>
    public class RegistrePassages
    {
        public string Version { get; set; } = "1";

        /// <summary>Dernier jour entièrement consolidé : les jours suivants seront relus au prochain passage.</summary>
        public DateTime? ConsolideJusquau { get; set; }

        public List<EntreePassage> Entrees { get; set; } = new();
    }
}
