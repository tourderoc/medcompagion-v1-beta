using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace MedCompanion.Models.Agenda
{
    /// <summary>
    /// Un rendez-vous, importé de l'export CSV Doctolib. Copie locale de lecture :
    /// Doctolib reste la source de vérité, Med ne modifie jamais rien chez lui.
    /// Stocké dans Documents/MedCompanion/agenda/&lt;aaaa-mm&gt;.json.
    /// </summary>
    public class RendezVous
    {
        /// <summary>Identifiant Doctolib du rendez-vous — clé de fusion entre deux imports.</summary>
        public string Id { get; set; } = "";

        /// <summary>Identifiant Doctolib du patient, stable : c'est lui qui évite les homonymes.</summary>
        public string PatientId { get; set; } = "";

        public DateTime Debut { get; set; }
        public int DureeMinutes { get; set; }

        public string Motif { get; set; } = "";

        /// <summary>« Vu », « À venir », « Déplacé »… tel quel, sans réinterprétation.</summary>
        public string Statut { get; set; } = "";

        public string Nom { get; set; } = "";
        public string Prenom { get; set; } = "";
        public DateTime? DateNaissance { get; set; }
        public string Telephone { get; set; } = "";
        public string Email { get; set; } = "";

        public bool NouveauPatient { get; set; }
        public bool PrisSurInternet { get; set; }
        public string Notes { get; set; } = "";

        /// <summary>Fin réelle relevée par Doctolib : dit qu'une consultation a bien eu lieu.</summary>
        public DateTime? HeureDepart { get; set; }

        /// <summary>« csv » (export Doctolib) ou « ecran » (lecture d'une capture). Un rendez-vous
        /// venu de l'écran n'a pas d'identifiant Doctolib : il est moins sûr, et on le sait.</summary>
        public string Origine { get; set; } = "csv";

        /// <summary>Date de la lecture ou de l'import qui a produit cette ligne.</summary>
        public DateTime DerniereLecture { get; set; } = DateTime.Now;

        /// <summary>
        /// Dossier MedCompanion rapproché (NOM_Prénom), vérifié — à ne pas confondre avec
        /// <see cref="DossierPatient"/>, qui n'est que le nom lu mis en forme. Vide tant qu'aucun
        /// dossier n'a été retrouvé : Med ne devine pas.
        /// </summary>
        public string Dossier { get; set; } = "";

        /// <summary>« sur », « a-confirmer », « ambigu » ou « aucun ». Vide = pas encore cherché.</summary>
        public string Certitude { get; set; } = "";

        [JsonIgnore]
        public DateTime Fin => Debut.AddMinutes(DureeMinutes <= 0 ? 30 : DureeMinutes);

        [JsonIgnore]
        public string NomComplet => $"{Nom} {Prenom}".Trim();

        /// <summary>
        /// Nom du dossier patient MedCompanion correspondant (NOM_Prénom). Doctolib rend souvent
        /// les deux en majuscules (« LAMBOLEY NOLAN ») : le prénom est remis en casse normale,
        /// sinon aucun dossier ne serait retrouvé.
        /// </summary>
        [JsonIgnore]
        public string DossierPatient => $"{Nom.Trim().ToUpperInvariant()}_{CasseNormale(Prenom)}";

        private static string CasseNormale(string valeur)
        {
            var mots = (valeur ?? "").Trim().Split(new[] { ' ', '-', '\'' }, StringSplitOptions.None);
            var separateurs = new List<char>();
            foreach (var c in (valeur ?? "").Trim())
                if (c == ' ' || c == '-' || c == '\'') separateurs.Add(c);

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < mots.Length; i++)
            {
                var m = mots[i];
                if (m.Length > 0) sb.Append(char.ToUpperInvariant(m[0])).Append(m[1..].ToLowerInvariant());
                if (i < separateurs.Count) sb.Append(separateurs[i]);
            }
            return sb.ToString();
        }
    }

    /// <summary>État de la copie locale, pour afficher sa fraîcheur — le point le plus important :
    /// un agenda périmé qu'on croit à jour est pire que pas d'agenda du tout.</summary>
    public class AgendaEtat
    {
        public DateTime? DerniereImportation { get; set; }

        /// <summary>Dernière mise à jour par lecture d'une capture d'écran Doctolib.</summary>
        public DateTime? DerniereLectureEcran { get; set; }
        public DateTime? PeriodeDebut { get; set; }
        public DateTime? PeriodeFin { get; set; }
        public int NombreRendezVous { get; set; }
    }
}
