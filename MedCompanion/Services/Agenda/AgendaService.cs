using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using MedCompanion.Models.Agenda;

namespace MedCompanion.Services.Agenda
{
    /// <summary>
    /// Copie locale de l'agenda Doctolib, alimentée par l'export CSV (Paramètres → Données →
    /// Exports, type « Agenda »). Lecture seule : Med n'écrit jamais rien dans Doctolib.
    ///
    /// Structure disque : Documents/MedCompanion/agenda/&lt;aaaa-mm&gt;.json + etat.json.
    /// Un fichier par mois — réimporter une période ne réécrit que les mois concernés, et un
    /// fichier abîmé ne coûte qu'un mois. Couvert par la sauvegarde vers E:.
    /// </summary>
    public class AgendaService
    {
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly string _racine;

        public AgendaService()
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _racine = Path.Combine(documents, "MedCompanion", "agenda");
        }

        public string Racine => _racine;

        // ── Lecture ─────────────────────────────────────────────────────────

        public List<RendezVous> Charger(DateTime du, DateTime au)
        {
            var resultat = new List<RendezVous>();
            var mois = new DateTime(du.Year, du.Month, 1);
            var dernier = new DateTime(au.Year, au.Month, 1);

            while (mois <= dernier)
            {
                resultat.AddRange(ChargerMois(mois));
                mois = mois.AddMonths(1);
            }

            return resultat
                .Where(r => r.Debut.Date >= du.Date && r.Debut.Date <= au.Date)
                .OrderBy(r => r.Debut)
                .ToList();
        }

        public List<RendezVous> ChargerJour(DateTime jour) => Charger(jour, jour);

        private List<RendezVous> ChargerMois(DateTime mois)
        {
            try
            {
                var path = CheminMois(mois);
                if (!File.Exists(path)) return new List<RendezVous>();
                return JsonSerializer.Deserialize<List<RendezVous>>(File.ReadAllText(path, Encoding.UTF8))
                       ?? new List<RendezVous>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AgendaService] {mois:yyyy-MM} illisible : {ex.Message}");
                return new List<RendezVous>();
            }
        }

        public AgendaEtat ChargerEtat()
        {
            try
            {
                var path = Path.Combine(_racine, "etat.json");
                if (!File.Exists(path)) return new AgendaEtat();
                return JsonSerializer.Deserialize<AgendaEtat>(File.ReadAllText(path, Encoding.UTF8)) ?? new AgendaEtat();
            }
            catch { return new AgendaEtat(); }
        }

        // ── Import ──────────────────────────────────────────────────────────

        /// <summary>
        /// Importe un export CSV Doctolib. Fusion sur l'identifiant du rendez-vous : réimporter
        /// la même période ne crée pas de doublons et met à jour les statuts.
        /// </summary>
        public (bool success, int importes, int misAJour, string? error) ImporterCsv(string cheminCsv)
        {
            try
            {
                var lignes = LireCsv(cheminCsv);
                if (lignes.Count == 0) return (false, 0, 0, "Le fichier ne contient aucune ligne exploitable.");

                var attendues = new[] { "Id", "Doctolib Patient ID", "Date de début", "Début", "Durée du RDV" };
                var manquantes = attendues.Where(c => !lignes[0].ContainsKey(c)).ToList();
                if (manquantes.Count > 0)
                    return (false, 0, 0, $"Ce CSV n'est pas un export d'agenda Doctolib : colonnes manquantes ({string.Join(", ", manquantes)}).");

                var nouveaux = lignes.Select(Convertir).Where(r => r != null).Select(r => r!).ToList();
                if (nouveaux.Count == 0) return (false, 0, 0, "Aucun rendez-vous lisible dans ce fichier.");

                int importes = 0, misAJour = 0;
                foreach (var groupe in nouveaux.GroupBy(r => new DateTime(r.Debut.Year, r.Debut.Month, 1)))
                {
                    var existants = ChargerMois(groupe.Key).ToDictionary(r => r.Id);
                    foreach (var rdv in groupe)
                    {
                        if (existants.ContainsKey(rdv.Id)) misAJour++;
                        else importes++;
                        existants[rdv.Id] = rdv;
                    }
                    EnregistrerMois(groupe.Key, existants.Values.OrderBy(r => r.Debut).ToList());
                }

                EnregistrerEtat(nouveaux);
                return (true, importes, misAJour, null);
            }
            catch (Exception ex)
            {
                return (false, 0, 0, $"Erreur d'import : {ex.Message}");
            }
        }

        /// <summary>
        /// Vide une période de la copie locale. Sert aux essais de la lecture d'écran : on
        /// repart d'un agenda vide pour voir exactement ce que Med a su lire. Ne touche
        /// évidemment qu'à la copie de Med.
        /// </summary>
        public int SupprimerPeriode(DateTime du, DateTime au)
        {
            int supprimes = 0;
            var mois = new DateTime(du.Year, du.Month, 1);
            var dernier = new DateTime(au.Year, au.Month, 1);

            while (mois <= dernier)
            {
                var rdvs = ChargerMois(mois);
                var restants = rdvs.Where(r => r.Debut.Date < du.Date || r.Debut.Date > au.Date).ToList();
                if (restants.Count != rdvs.Count)
                {
                    supprimes += rdvs.Count - restants.Count;
                    EnregistrerMois(mois, restants);
                }
                mois = mois.AddMonths(1);
            }

            if (supprimes > 0) MettreAJourEtat();
            return supprimes;
        }

        // ── Fusion d'une lecture d'écran ────────────────────────────────────

        /// <summary>Ce qu'une lecture d'écran a changé, pour le journal affiché au médecin.</summary>
        public record Changement(string Type, string Texte);

        /// <summary>
        /// Met l'agenda d'un jour à jour d'après ce qui a été lu à l'écran.
        ///
        /// Ajoute et met à jour sans rien demander — c'est la copie de Med, pas Doctolib. Mais ne
        /// supprime JAMAIS : un rendez-vous connu et absent de la capture peut avoir été annulé,
        /// ou simplement hors cadre, masqué ou mal lu. Il est signalé, pas effacé. Une ligne
        /// barrée, en revanche, est un signal net : le rendez-vous passe « Annulé ».
        /// </summary>
        public List<Changement> FusionnerLectureEcran(
            DateTime jour,
            List<(TimeSpan heure, string nom, string motif, DateTime? naissance, string telephone, bool barre)> lignes,
            bool journeeComplete)
        {
            var changements = new List<Changement>();
            var existants = ChargerMois(jour).ToList();
            var duJour = existants.Where(r => r.Debut.Date == jour.Date).ToList();
            var vus = new HashSet<string>();

            // Doublons déjà en place, venus de deux lectures qui n'ont pas écrit le nom pareil.
            changements.AddRange(FusionnerDoublons(duJour, existants));

            foreach (var l in lignes)
            {
                var debut = jour.Date.Add(l.heure);

                // Même personne, même heure : c'est le même rendez-vous. Sinon, même personne à
                // une autre heure : il a été déplacé, on ne crée pas de doublon.
                var rdv = duJour.FirstOrDefault(r => r.Debut == debut && MemePersonne(r, l.nom, memeCreneau: true))
                       ?? duJour.FirstOrDefault(r => MemePersonne(r, l.nom, memeCreneau: false) && !vus.Contains(r.Id));

                if (rdv == null)
                {
                    rdv = new RendezVous
                    {
                        Id = $"ecran-{jour:yyyyMMdd}-{l.heure:hhmm}-{Identifiant(l.nom)}",
                        Debut = debut,
                        DureeMinutes = 30,
                        Origine = "ecran"
                    };
                    AppliquerLigne(rdv, l, journeeComplete);
                    existants.Add(rdv);
                    duJour.Add(rdv);
                    changements.Add(new Changement(l.barre ? "annule" : "ajout",
                        $"{debut:HH'h'mm} {rdv.NomComplet}{(l.barre ? " — annulé" : "")}"));
                }
                else
                {
                    var avant = (rdv.Debut, rdv.Statut, rdv.Motif);
                    if (rdv.Debut != debut)
                    {
                        changements.Add(new Changement("deplace",
                            $"{rdv.NomComplet} : {rdv.Debut:HH'h'mm} → {debut:HH'h'mm}"));
                        rdv.Debut = debut;
                    }
                    AppliquerLigne(rdv, l, journeeComplete);
                    if (avant.Statut != rdv.Statut && rdv.Statut == "Annulé")
                        changements.Add(new Changement("annule", $"{debut:HH'h'mm} {rdv.NomComplet} — annulé"));
                }

                rdv.DerniereLecture = DateTime.Now;
                vus.Add(rdv.Id);
            }

            // Les rendez-vous que Med connaît et que la capture ne montre pas. Signalés seulement,
            // et uniquement quand la capture couvrait toute la journée (vue Liste).
            if (journeeComplete)
                foreach (var oublie in duJour.Where(r => !vus.Contains(r.Id) && r.Statut != "Annulé"))
                    changements.Add(new Changement("absent",
                        $"{oublie.Debut:HH'h'mm} {oublie.NomComplet} — dans Med, absent de l'écran"));

            EnregistrerMois(jour, existants.OrderBy(r => r.Debut).ToList());
            MettreAJourEtat();
            return changements;
        }

        /// <summary>
        /// Relie les rendez-vous d'une journée aux dossiers de Med. Ne rapproche que ce qui n'est
        /// pas déjà sûr : un lien confirmé ne se rejoue pas. Ce qui reste sans dossier n'est pas
        /// une erreur — consultation parents, patient sans dossier, nouveau patient.
        /// </summary>
        public List<Changement> RapprocherDossiers(DateTime jour, AgendaRapprochementService annuaire)
        {
            var changements = new List<Changement>();
            var existants = ChargerMois(jour).ToList();
            var duJour = existants.Where(r => r.Debut.Date == jour.Date).ToList();
            bool modifie = false;
            int sansDossier = 0;

            foreach (var rdv in duJour)
            {
                if (rdv.Certitude == "sur" && rdv.Dossier.Length > 0) continue;

                var r = annuaire.Resoudre(rdv.NomComplet, rdv.DateNaissance);
                var certitude = r.Verdict switch
                {
                    AgendaRapprochementService.Verdict.Sur        => "sur",
                    AgendaRapprochementService.Verdict.AConfirmer => "a-confirmer",
                    AgendaRapprochementService.Verdict.Ambigu     => "ambigu",
                    _                                             => "aucun"
                };

                if (rdv.Dossier != r.Dossier || rdv.Certitude != certitude)
                {
                    rdv.Dossier = r.Dossier;
                    rdv.Certitude = certitude;
                    modifie = true;
                }

                // Un désaccord de date de naissance se signale : c'est le cas où Med aurait pu
                // ouvrir le dossier d'un autre enfant.
                if (r.Verdict == AgendaRapprochementService.Verdict.Aucun && r.Raison.Contains("date de naissance"))
                    changements.Add(new Changement("conflit",
                        $"{rdv.Debut:HH'h'mm} {rdv.NomComplet} — {r.Raison}, aucun dossier lié"));
                else if (r.Verdict == AgendaRapprochementService.Verdict.Ambigu)
                    changements.Add(new Changement("conflit",
                        $"{rdv.Debut:HH'h'mm} {rdv.NomComplet} — {r.Raison}"));
                else if (r.Verdict == AgendaRapprochementService.Verdict.Aucun)
                    sansDossier++;
            }

            // Le détail des sans-dossier se lit dans la grille ; ici, seul le nombre est utile.
            if (sansDossier > 0)
                changements.Add(new Changement("sans-dossier",
                    $"{sansDossier} rendez-vous sans dossier Med (consultation parents, ou dossier à créer)"));

            if (modifie)
            {
                EnregistrerMois(jour, existants.OrderBy(r => r.Debut).ToList());
                MettreAJourEtat();
            }
            return changements;
        }

        /// <summary>
        /// Passe « Annulé » les rendez-vous dont la ligne a été trouvée barrée à l'écran.
        /// Ne crée jamais rien : une ligne barrée qu'on ne retrouve pas est signalée au médecin,
        /// pas inventée — un faux rendez-vous annulé serait pire qu'un oubli.
        /// </summary>
        public List<Changement> MarquerAnnules(DateTime jour, List<(TimeSpan heure, string nom)> lignes)
        {
            var changements = new List<Changement>();
            if (lignes.Count == 0) return changements;

            var existants = ChargerMois(jour).ToList();
            var duJour = existants.Where(r => r.Debut.Date == jour.Date).ToList();
            bool modifie = false;

            foreach (var (heure, nom) in lignes)
            {
                var debut = jour.Date.Add(heure);
                var memeHeure = duJour.Where(r => r.Debut == debut).ToList();

                var rdv = memeHeure.FirstOrDefault(r => MemePersonne(r, nom, memeCreneau: true))
                       ?? (memeHeure.Count == 1 ? memeHeure[0] : null);

                if (rdv == null)
                {
                    changements.Add(new Changement("inconnu",
                        $"{debut:HH'h'mm} {nom} — barré à l'écran, introuvable dans Med"));
                    continue;
                }

                if (rdv.Statut == "Annulé") continue;
                rdv.Statut = "Annulé";
                rdv.DerniereLecture = DateTime.Now;
                modifie = true;
                changements.Add(new Changement("annule", $"{debut:HH'h'mm} {rdv.NomComplet} — annulé"));
            }

            if (modifie)
            {
                EnregistrerMois(jour, existants.OrderBy(r => r.Debut).ToList());
                MettreAJourEtat();
            }
            return changements;
        }

        private static void AppliquerLigne(RendezVous rdv,
            (TimeSpan heure, string nom, string motif, DateTime? naissance, string telephone, bool barre) l,
            bool nomFiable)
        {
            // Un nom venu du CSV Doctolib ne se corrige pas par une lecture d'écran. Sinon, la vue
            // Liste fait autorité — elle est lisible — et la grille de la semaine ne peut que
            // compléter : elle tronque les noms trop longs pour la case.
            var lu = (l.nom ?? "").Trim();
            if (lu.Length > 0 && rdv.Origine != "csv"
                && (rdv.Nom.Length == 0 || nomFiable || Cle(lu).Length > Cle(rdv.NomComplet).Length))
                DecouperNom(rdv, lu);

            if (l.motif.Length > 0) rdv.Motif = l.motif;
            if (l.naissance.HasValue) rdv.DateNaissance = l.naissance;
            if (l.telephone.Length > 0) rdv.Telephone = l.telephone;
            if (l.barre) rdv.Statut = "Annulé";
            else if (string.IsNullOrWhiteSpace(rdv.Statut)) rdv.Statut = "À venir";
        }

        /// <summary>Découpe « NOM Prénom » tel que Doctolib l'écrit : le nom en majuscules, le prénom suit.</summary>
        private static void DecouperNom(RendezVous rdv, string nom)
        {
            var parts = nom.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;

            var majuscules = parts.TakeWhile(p => p == p.ToUpperInvariant()).ToList();
            if (majuscules.Count == 0 || majuscules.Count == parts.Length)
            {
                rdv.Nom = string.Join(" ", parts.Take(parts.Length - 1));
                rdv.Prenom = parts[^1];
                if (rdv.Nom.Length == 0) { rdv.Nom = parts[0]; rdv.Prenom = ""; }
            }
            else
            {
                rdv.Nom = string.Join(" ", majuscules);
                rdv.Prenom = string.Join(" ", parts.Skip(majuscules.Count));
            }
        }

        /// <summary>Nom réduit à ses lettres, sans accents ni casse : la forme comparable.</summary>
        private static string Cle(string nom)
            => new string((nom ?? "").Normalize(NormalizationForm.FormD)
                .Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

        /// <summary>Clé de nom pour fabriquer un identifiant stable d'un rendez-vous lu à l'écran.</summary>
        private static string Identifiant(string nom)
        {
            var cle = Cle(nom).ToLowerInvariant();
            return cle.Length == 0 ? "inconnu" : (cle.Length > 24 ? cle[..24] : cle);
        }

        /// <summary>
        /// Deux écritures du même patient. La grille de la semaine tronque les noms, d'où les
        /// préfixes ; et deux lectures de la même ligne se distinguent parfois d'une lettre
        /// (HAMMOUCHI / HAMMOUCHE, relevé le 26/09/2026). Cette dernière tolérance ne vaut qu'au
        /// même créneau : ailleurs, elle confondrait deux patients aux noms voisins.
        /// </summary>
        private static bool MemePersonne(RendezVous rdv, string nomLu, bool memeCreneau)
        {
            var a = Cle(rdv.NomComplet);
            var b = Cle(nomLu);
            if (a.Length == 0 || b.Length == 0) return false;

            if (a == b || a.StartsWith(b) || b.StartsWith(a)) return true;
            if ((b.Length >= 6 && a.Contains(b)) || (a.Length >= 6 && b.Contains(a))) return true;

            if (!memeCreneau) return false;
            int court = Math.Min(a.Length, b.Length);
            if (court < 5) return false;
            int tolerance = Math.Max(1, court / 6);
            return Distance(a, b, tolerance) <= tolerance;
        }

        /// <summary>Distance d'édition, abandonnée dès qu'elle dépasse <paramref name="max"/>.</summary>
        private static int Distance(string a, string b, int max)
        {
            if (Math.Abs(a.Length - b.Length) > max) return max + 1;

            var precedente = new int[b.Length + 1];
            var courante = new int[b.Length + 1];
            for (int j = 0; j <= b.Length; j++) precedente[j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                courante[0] = i;
                int meilleur = i;
                for (int j = 1; j <= b.Length; j++)
                {
                    int cout = a[i - 1] == b[j - 1] ? 0 : 1;
                    courante[j] = Math.Min(Math.Min(courante[j - 1] + 1, precedente[j] + 1),
                                           precedente[j - 1] + cout);
                    if (courante[j] < meilleur) meilleur = courante[j];
                }
                if (meilleur > max) return max + 1;
                (precedente, courante) = (courante, precedente);
            }
            return precedente[b.Length];
        }

        /// <summary>
        /// Rassemble les rendez-vous qui occupent le même créneau et désignent la même personne :
        /// c'est un doublon de lecture, pas deux patients. On garde le mieux renseigné — l'export
        /// CSV avant l'écran — et l'autre est retiré, en le disant dans le journal.
        /// </summary>
        private static List<Changement> FusionnerDoublons(List<RendezVous> duJour, List<RendezVous> existants)
        {
            var changements = new List<Changement>();

            foreach (var creneau in duJour.GroupBy(r => r.Debut).Where(g => g.Count() > 1).ToList())
            {
                var lot = creneau.OrderByDescending(Richesse).ToList();
                for (int i = 0; i < lot.Count; i++)
                    for (int j = lot.Count - 1; j > i; j--)
                    {
                        if (!MemePersonne(lot[i], lot[j].NomComplet, memeCreneau: true)) continue;

                        var doublon = lot[j];
                        changements.Add(new Changement("doublon",
                            $"{doublon.Debut:HH'h'mm} {doublon.NomComplet} — doublon de lecture, " +
                            $"fusionné avec {lot[i].NomComplet}"));

                        if (lot[i].DateNaissance == null) lot[i].DateNaissance = doublon.DateNaissance;
                        if (lot[i].Telephone.Length == 0) lot[i].Telephone = doublon.Telephone;
                        if (lot[i].Motif.Length == 0) lot[i].Motif = doublon.Motif;
                        if (doublon.Statut == "Annulé") lot[i].Statut = "Annulé";

                        lot.RemoveAt(j);
                        duJour.Remove(doublon);
                        existants.Remove(doublon);
                    }
            }
            return changements;
        }

        /// <summary>Ce qu'un rendez-vous porte d'information : départage deux doublons.</summary>
        private static int Richesse(RendezVous r)
            => (r.Origine == "csv" ? 8 : 0)
             + (r.PatientId.Length > 0 ? 4 : 0)
             + (r.DateNaissance.HasValue ? 2 : 0)
             + (r.Telephone.Length > 0 ? 1 : 0)
             + (r.Motif.Length > 0 ? 1 : 0);

        private void MettreAJourEtat()
        {
            var etat = ChargerEtat();
            etat.NombreRendezVous = CompterTout();
            etat.DerniereLectureEcran = DateTime.Now;
            Directory.CreateDirectory(_racine);
            File.WriteAllText(Path.Combine(_racine, "etat.json"),
                JsonSerializer.Serialize(etat, _jsonOptions), Encoding.UTF8);
        }

        private void EnregistrerMois(DateTime mois, List<RendezVous> rdvs)
        {
            Directory.CreateDirectory(_racine);
            File.WriteAllText(CheminMois(mois), JsonSerializer.Serialize(rdvs, _jsonOptions), Encoding.UTF8);
        }

        private void EnregistrerEtat(List<RendezVous> importes)
        {
            var etat = ChargerEtat();
            etat.DerniereImportation = DateTime.Now;
            var debut = importes.Min(r => r.Debut).Date;
            var fin = importes.Max(r => r.Debut).Date;
            etat.PeriodeDebut = etat.PeriodeDebut == null || debut < etat.PeriodeDebut ? debut : etat.PeriodeDebut;
            etat.PeriodeFin = etat.PeriodeFin == null || fin > etat.PeriodeFin ? fin : etat.PeriodeFin;
            etat.NombreRendezVous = CompterTout();

            Directory.CreateDirectory(_racine);
            File.WriteAllText(Path.Combine(_racine, "etat.json"),
                JsonSerializer.Serialize(etat, _jsonOptions), Encoding.UTF8);
        }

        private int CompterTout()
        {
            if (!Directory.Exists(_racine)) return 0;
            int total = 0;
            foreach (var f in Directory.GetFiles(_racine, "????-??.json"))
            {
                try { total += JsonSerializer.Deserialize<List<RendezVous>>(File.ReadAllText(f, Encoding.UTF8))?.Count ?? 0; }
                catch { }
            }
            return total;
        }

        private string CheminMois(DateTime mois) => Path.Combine(_racine, $"{mois:yyyy-MM}.json");

        // ── Conversion d'une ligne CSV ──────────────────────────────────────

        private static RendezVous? Convertir(Dictionary<string, string> l)
        {
            var date = LireDate(Champ(l, "Date de début"));      // 03/08/2026
            var heure = LireHeure(Champ(l, "Début"));            // 09h00
            if (date == null || heure == null) return null;

            return new RendezVous
            {
                Id             = Champ(l, "Id"),
                PatientId      = Champ(l, "Doctolib Patient ID"),
                Debut          = date.Value.Add(heure.Value),
                DureeMinutes   = int.TryParse(Champ(l, "Durée du RDV"), out var d) ? d : 30,
                Motif          = Champ(l, "Motif du RDV"),
                Statut         = Champ(l, "Statut"),
                Nom            = Champ(l, "Nom du patient"),
                Prenom         = Champ(l, "Prénom du patient"),
                DateNaissance  = LireDate(Champ(l, "Date de naissance")),
                Telephone      = Champ(l, "Téléphone portable"),
                Email          = Champ(l, "Email du patient"),
                NouveauPatient = Champ(l, "Nouveau patient").Equals("Oui", StringComparison.OrdinalIgnoreCase),
                PrisSurInternet= Champ(l, "RDV Internet").Equals("Oui", StringComparison.OrdinalIgnoreCase),
                Notes          = Champ(l, "Notes"),
                HeureDepart    = LireHorodatage(Champ(l, "Heure de départ"))
            };
        }

        private static string Champ(Dictionary<string, string> l, string nom)
            => l.TryGetValue(nom, out var v) ? v.Trim() : "";

        private static DateTime? LireDate(string valeur)
            => DateTime.TryParseExact(valeur, new[] { "dd/MM/yyyy", "yyyy-MM-dd" },
                   CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

        /// <summary>« 09h00 » ou « 9:00 ».</summary>
        private static TimeSpan? LireHeure(string valeur)
        {
            var m = System.Text.RegularExpressions.Regex.Match(valeur, @"^(\d{1,2})\s*[h:]\s*(\d{2})");
            if (!m.Success) return null;
            return new TimeSpan(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), 0);
        }

        private static DateTime? LireHorodatage(string valeur)
            => DateTimeOffset.TryParse(valeur, CultureInfo.InvariantCulture,
                   DateTimeStyles.None, out var o) ? o.LocalDateTime : null;

        // ── Lecture CSV ─────────────────────────────────────────────────────

        /// <summary>
        /// Lecteur CSV minimal mais correct : séparateur « ; », guillemets doublés, et retours
        /// à la ligne à l'intérieur d'un champ — le champ Notes en contient.
        /// L'export Doctolib est en UTF-8 sans BOM.
        /// </summary>
        private static List<Dictionary<string, string>> LireCsv(string chemin)
        {
            var texte = File.ReadAllText(chemin, new UTF8Encoding(false));
            var lignes = DecouperLignes(texte);
            var resultat = new List<Dictionary<string, string>>();
            if (lignes.Count < 2) return resultat;

            var entetes = lignes[0];
            for (int i = 1; i < lignes.Count; i++)
            {
                var champs = lignes[i];
                if (champs.Count == 1 && string.IsNullOrWhiteSpace(champs[0])) continue;

                var ligne = new Dictionary<string, string>(StringComparer.Ordinal);
                for (int c = 0; c < entetes.Count; c++)
                    ligne[entetes[c].Trim()] = c < champs.Count ? champs[c] : "";
                resultat.Add(ligne);
            }
            return resultat;
        }

        private static List<List<string>> DecouperLignes(string texte)
        {
            var lignes = new List<List<string>>();
            var champs = new List<string>();
            var champ = new StringBuilder();
            bool entreGuillemets = false;

            for (int i = 0; i < texte.Length; i++)
            {
                var c = texte[i];

                if (entreGuillemets)
                {
                    if (c == '"')
                    {
                        if (i + 1 < texte.Length && texte[i + 1] == '"') { champ.Append('"'); i++; }
                        else entreGuillemets = false;
                    }
                    else champ.Append(c);
                    continue;
                }

                switch (c)
                {
                    case '"': entreGuillemets = true; break;
                    case ';': champs.Add(champ.ToString()); champ.Clear(); break;
                    case '\r': break;
                    case '\n':
                        champs.Add(champ.ToString());
                        champ.Clear();
                        lignes.Add(champs);
                        champs = new List<string>();
                        break;
                    default: champ.Append(c); break;
                }
            }

            if (champ.Length > 0 || champs.Count > 0)
            {
                champs.Add(champ.ToString());
                lignes.Add(champs);
            }
            return lignes;
        }
    }
}
