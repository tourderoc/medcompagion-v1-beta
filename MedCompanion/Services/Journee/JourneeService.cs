using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MedCompanion.Models.Agenda;
using MedCompanion.Models.Journee;
using MedCompanion.Services.Agenda;

namespace MedCompanion.Services.Journee
{
    /// <summary>
    /// Le fil de la journée (PLAN_JOURNEE_COMPAGNON.md, étape 1). Pour chaque rendez-vous de
    /// l'agenda, Med déduit où il en est à partir de ce que le médecin a fait dans le dossier —
    /// jamais d'une saisie de plus. Et il tient le registre des passages : qui est venu, quand,
    /// sans rien de clinique.
    ///
    /// Les états se RECALCULENT depuis le disque à chaque lecture : rien n'est figé, une note
    /// écrite le soir corrige d'elle-même la consultation de 14 h. Seuls les signaux de dictée,
    /// qui ne laissent pas de fichier, sont notés à part.
    ///
    /// Structure disque : Documents/MedCompanion/journee/
    ///   registre_passages.json           — la mémoire de Med
    ///   activites_&lt;aaaa-mm-jj&gt;.json   — dictées démarrées, par dossier et par heure
    /// </summary>
    public sealed class JourneeService
    {
        public static JourneeService Instance { get; } = new();

        /// <summary>Fenêtre d'un rendez-vous : de 15 min avant à 45 min après le début (ou
        /// 15 min après la fin s'il est plus long). À ajuster à l'usage — ce sont des règles.</summary>
        private const int MargeAvantMinutes = 15;
        private const int MargeApresDebutMinutes = 45;
        private const int MargeApresFinMinutes = 15;

        /// <summary>Un patient sort du registre après 6 mois sans venue ET sans rendez-vous à venir.</summary>
        private const int MoisAvantOubli = 6;

        /// <summary>Dossiers du patient dont l'écriture ne prouve pas une consultation : la
        /// conversation avec Med, la fiche administrative, la synthèse régénérée, les messages.</summary>
        private static readonly HashSet<string> DossiersSansValeurDePreuve =
            new(StringComparer.OrdinalIgnoreCase) { "chat", "info_patient", "synthese", "messages" };

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private readonly string _racine;
        private readonly string _patients;
        private readonly AgendaService _agenda = new();
        private readonly object _verrou = new();
        private DateTime _dernierRapprochement = DateTime.MinValue;

        /// <summary>Le registre a changé : l'écran d'accueil se recharge.</summary>
        public event Action? RegistreMisAJour;

        private JourneeService()
        {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _racine = Path.Combine(documents, "MedCompanion", "journee");
            _patients = new PathService().GetBasePatientsDirectory();
        }

        // ── Fenêtre ─────────────────────────────────────────────────────────

        public static (DateTime debut, DateTime fin) Fenetre(RendezVous r)
        {
            var fin = r.Debut.AddMinutes(MargeApresDebutMinutes);
            var finLongue = r.Fin.AddMinutes(MargeApresFinMinutes);
            return (r.Debut.AddMinutes(-MargeAvantMinutes), finLongue > fin ? finLongue : fin);
        }

        // ── Signaux de dictée ───────────────────────────────────────────────

        private sealed class SignalActivite
        {
            public string Dossier { get; set; } = "";
            public DateTime Heure { get; set; }
        }

        /// <summary>
        /// Une dictée démarre dans un dossier. C'est la seule trace qui ne laisse pas de
        /// fichier tant que la note n'est pas enregistrée : on la note ici, sinon une consultation
        /// dictée mais pas encore extraite passerait pour « aucune trace ».
        /// </summary>
        public void SignalerActivite(string? dossierPatient)
        {
            if (string.IsNullOrWhiteSpace(dossierPatient)) return;
            var dossier = Path.GetFileName(dossierPatient.TrimEnd('\\', '/'));
            if (dossier.Length == 0) return;

            try
            {
                lock (_verrou)
                {
                    var signaux = ChargerSignaux(DateTime.Today);
                    signaux.Add(new SignalActivite { Dossier = dossier, Heure = DateTime.Now });
                    Directory.CreateDirectory(_racine);
                    File.WriteAllText(CheminSignaux(DateTime.Today),
                        JsonSerializer.Serialize(signaux, JsonOptions), new UTF8Encoding(false));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[JourneeService] Signal non noté : {ex.Message}");
            }
        }

        private string CheminSignaux(DateTime jour) => Path.Combine(_racine, $"activites_{jour:yyyy-MM-dd}.json");

        private List<SignalActivite> ChargerSignaux(DateTime jour)
        {
            try
            {
                var p = CheminSignaux(jour);
                if (!File.Exists(p)) return new List<SignalActivite>();
                return JsonSerializer.Deserialize<List<SignalActivite>>(File.ReadAllText(p, Encoding.UTF8))
                       ?? new List<SignalActivite>();
            }
            catch { return new List<SignalActivite>(); }
        }

        // ── Calcul du fil ───────────────────────────────────────────────────

        /// <summary>
        /// Le fil d'un jour, recalculé depuis le disque. <paramref name="rdvs"/> évite de relire
        /// l'agenda quand l'appelant l'a déjà ; <paramref name="avecHorsAgenda"/> parcourt tous les
        /// dossiers (quelques dizaines de millisecondes) — inutile pour une vue semaine.
        /// </summary>
        public FilDuJour Calculer(DateTime jour, List<RendezVous>? rdvs = null, bool avecHorsAgenda = true)
            => Calculer(jour, rdvs, avecHorsAgenda, new Lecteur(_patients));

        private FilDuJour Calculer(DateTime jour, List<RendezVous>? rdvs, bool avecHorsAgenda, Lecteur lecteur)
        {
            jour = jour.Date;
            var fil = new FilDuJour { Jour = jour };
            rdvs ??= _agenda.ChargerJour(jour);
            var signaux = ChargerSignaux(jour);
            var maintenant = DateTime.Now;

            var annuaire = _annuaire;
            foreach (var rdv in rdvs.Where(r => r.Debut.Date == jour).OrderBy(r => r.Debut))
            {
                var rv = new RendezVousDuJour(rdv);

                // Les rendez-vous importés par CSV n'ont jamais été rapprochés (seule la lecture
                // d'écran le fait) : sans ça, leur note passait pour un passage hors agenda. Le
                // lien se fait en mémoire, sans réécrire l'agenda, et seulement s'il est sûr.
                if (rv.Dossier.Length == 0 && annuaire != null && rdv.Certitude is "" or "aucun")
                {
                    var r = annuaire.Resoudre(rdv.NomComplet, rdv.DateNaissance);
                    if (r.Verdict == AgendaRapprochementService.Verdict.Sur) rv.Dossier = r.Dossier;
                }

                DeduireEtat(rv, jour, maintenant, lecteur, signaux);
                fil.RendezVous.Add(rv);
            }

            if (avecHorsAgenda)
            {
                var attendus = new HashSet<string>(fil.RendezVous.Select(r => r.Dossier).Where(d => d.Length > 0),
                                                   StringComparer.OrdinalIgnoreCase);
                foreach (var (dossier, notes) in lecteur.NotesDuJour(jour))
                {
                    if (attendus.Contains(dossier)) continue;
                    var premiere = notes.OrderBy(n => n.Heure).First();
                    fil.HorsAgenda.Add(new PassageHorsAgenda
                    {
                        Dossier = dossier,
                        Nom = NomDepuisDossier(dossier),
                        Heure = premiere.Heure,
                        Quoi = string.Join(", ", notes.Select(n => LibelleNote(n.Type)).Distinct())
                    });
                }
            }

            return fil;
        }

        private static void DeduireEtat(RendezVousDuJour rv, DateTime jour, DateTime maintenant,
                                        Lecteur lecteur, List<SignalActivite> signaux)
        {
            var rdv = rv.Rdv;
            var statut = (rdv.Statut ?? "").Trim().ToLowerInvariant();
            var (fd, ff) = Fenetre(rdv);
            bool fenetrePassee = jour < DateTime.Today || maintenant > ff;

            if (statut.StartsWith("annul"))
            {
                rv.Etat = EtatRendezVous.Annule;
                rv.Preuves.Add("Doctolib : annulé");
                return;
            }

            var etatDoctolib = EtatDepuisDoctolib(statut);

            // Sans dossier sûr, Med ne lit aucun dossier : il pourrait lire celui d'un autre enfant.
            if (rv.Dossier.Length == 0)
            {
                rv.Etat = etatDoctolib ?? EtatRendezVous.DossierIncertain;
                rv.Preuves.Add(rdv.Certitude == "a-confirmer" ? "Dossier à confirmer" : "Aucun dossier sûr");
                return;
            }

            var notes = lecteur.NotesDuDossier(rv.Dossier, jour);

            // 1. Un événement posé par le médecin l'emporte sur tout.
            var evenement = notes.Where(n => n.Type is "absent" or "excuse" or "deplace")
                                 .OrderBy(n => n.Heure).LastOrDefault();
            if (evenement != null)
            {
                rv.Etat = evenement.Type switch
                {
                    "absent"  => EtatRendezVous.Absent,
                    "excuse"  => EtatRendezVous.Excuse,
                    _         => EtatRendezVous.Deplace
                };
                rv.Preuves.Add($"{LibelleNote(evenement.Type)} noté à {evenement.Heure:HH'h'mm}");
                return;
            }

            // 2. Une note ou une séance consignée ce jour-là.
            var ecrits = notes.Where(n => n.Type is "note" or "seance").OrderBy(n => n.Heure).ToList();
            if (ecrits.Count > 0)
            {
                rv.Etat = EtatRendezVous.Termine;
                rv.NoteRattrapee = ecrits[0].Heure > ff;
                foreach (var e in ecrits)
                    rv.Preuves.Add($"{LibelleNote(e.Type)} à {e.Heure:HH'h'mm}");
                return;
            }

            // 3. Une trace de présence : écriture dans le dossier ou dictée pendant la fenêtre,
            //    ou Doctolib qui dit « vu ». L'ouverture seule du dossier n'écrit rien : elle ne compte pas.
            var ecriture = lecteur.EcrituresDuDossier(rv.Dossier, jour).Where(t => t >= fd && t <= ff).ToList();
            var dictee = signaux.Where(s => string.Equals(s.Dossier, rv.Dossier, StringComparison.OrdinalIgnoreCase)
                                            && s.Heure >= fd && s.Heure <= ff).ToList();
            bool vuDoctolib = statut.StartsWith("vu") || rdv.HeureDepart.HasValue;

            if (ecriture.Count > 0 || dictee.Count > 0 || vuDoctolib)
            {
                rv.Etat = fenetrePassee ? EtatRendezVous.SansNote : EtatRendezVous.EnCours;
                if (dictee.Count > 0)   rv.Preuves.Add($"Dictée à {dictee.Min(s => s.Heure):HH'h'mm}");
                if (ecriture.Count > 0) rv.Preuves.Add($"Dossier modifié à {ecriture.Min():HH'h'mm}");
                if (vuDoctolib)         rv.Preuves.Add("Doctolib : vu");
                return;
            }

            // 4. Rien dans le dossier : ce que dit Doctolib, sinon le doute une fois l'heure passée.
            if (etatDoctolib.HasValue)
            {
                rv.Etat = etatDoctolib.Value;
                rv.Preuves.Add($"Doctolib : {rdv.Statut}");
                return;
            }

            rv.Etat = fenetrePassee ? EtatRendezVous.SansNouvelles : EtatRendezVous.AVenir;
        }

        private static EtatRendezVous? EtatDepuisDoctolib(string statut)
        {
            if (statut.Contains("excus")) return EtatRendezVous.Excuse;
            if (statut.Contains("absent") || statut.Contains("lapin")) return EtatRendezVous.Absent;
            if (statut.StartsWith("déplac") || statut.StartsWith("deplac")) return EtatRendezVous.Deplace;
            return null;
        }

        private static string LibelleNote(string type) => type switch
        {
            "note"    => "Note",
            "seance"  => "Séance",
            "absent"  => "Absence",
            "excuse"  => "Absence excusée",
            "deplace" => "RDV déplacé",
            "liaison" => "Note de liaison",
            _         => type
        };

        /// <summary>NOM_Prénom → « Prénom NOM ».</summary>
        private static string NomDepuisDossier(string dossier)
        {
            var i = dossier.IndexOf('_');
            return i <= 0 ? dossier : $"{dossier[(i + 1)..].Replace('_', ' ')} {dossier[..i]}";
        }

        // ── Lecture des dossiers, avec cache le temps d'un calcul ───────────

        private sealed record NoteDatee(string Type, DateTime Heure);

        /// <summary>
        /// Lit les dossiers patients une seule fois par calcul : une consolidation parcourt des
        /// semaines d'agenda, et relire chaque dossier à chaque rendez-vous serait inutilement lent.
        /// </summary>
        private sealed class Lecteur
        {
            private readonly string _patients;
            private readonly Dictionary<string, List<(string chemin, DateTime ecrit)>> _fichiers = new(StringComparer.OrdinalIgnoreCase);
            private Dictionary<DateTime, Dictionary<string, List<NoteDatee>>>? _index;
            private readonly HashSet<int> _anneesIndexees = new();

            public Lecteur(string patients) => _patients = patients;

            private List<(string chemin, DateTime ecrit)> Fichiers(string dossier)
            {
                if (_fichiers.TryGetValue(dossier, out var l)) return l;
                l = new List<(string, DateTime)>();
                var racine = Path.Combine(_patients, dossier);
                try
                {
                    if (Directory.Exists(racine))
                    {
                        foreach (var f in Directory.EnumerateFiles(racine, "*", SearchOption.AllDirectories))
                        {
                            var relatif = f.Substring(racine.Length).TrimStart('\\', '/');
                            var premier = relatif.Split('\\', '/')[0];
                            if (DossiersSansValeurDePreuve.Contains(premier)) continue;
                            // Index, réglages et copies de sécurité s'écrivent tout seuls : pas une preuve.
                            if (f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                                || f.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)
                                || f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) continue;
                            l.Add((f, File.GetLastWriteTime(f)));
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[JourneeService] {dossier} illisible : {ex.Message}");
                }
                _fichiers[dossier] = l;
                return l;
            }

            public IEnumerable<DateTime> EcrituresDuDossier(string dossier, DateTime jour)
                => Fichiers(dossier).Where(f => f.ecrit.Date == jour).Select(f => f.ecrit);

            public List<NoteDatee> NotesDuDossier(string dossier, DateTime jour)
            {
                Indexer(jour.Year);
                return _index!.TryGetValue(jour, out var parDossier) && parDossier.TryGetValue(dossier, out var n)
                    ? n : new List<NoteDatee>();
            }

            public IEnumerable<(string dossier, List<NoteDatee> notes)> NotesDuJour(DateTime jour)
            {
                Indexer(jour.Year);
                if (!_index!.TryGetValue(jour, out var parDossier)) yield break;
                foreach (var kv in parDossier)
                {
                    // La note de liaison seule n'est pas un passage : un appel, un échange avec l'école.
                    var utiles = kv.Value.Where(n => n.Type != "liaison").ToList();
                    if (utiles.Count > 0) yield return (kv.Key, utiles);
                }
            }

            /// <summary>
            /// Toutes les notes et séances datées d'une année, rangées par jour puis par dossier.
            /// La date vient du NOM du fichier (aaaa-mm-jj…), pas de sa date d'écriture : une note
            /// retouchée le lendemain reste celle du jour où elle a été prise.
            /// </summary>
            private void Indexer(int annee)
            {
                _index ??= new Dictionary<DateTime, Dictionary<string, List<NoteDatee>>>();
                if (!_anneesIndexees.Add(annee)) return;

                try
                {
                    if (!Directory.Exists(_patients)) return;
                    foreach (var racine in Directory.EnumerateDirectories(_patients))
                    {
                        var dossier = Path.GetFileName(racine);
                        var dirAnnee = Path.Combine(racine, annee.ToString());
                        if (!Directory.Exists(dirAnnee)) continue;

                        Ajouter(dossier, Path.Combine(dirAnnee, "notes"), estSeance: false);
                        Ajouter(dossier, Path.Combine(dirAnnee, "cartographies"), estSeance: true);
                        Ajouter(dossier, Path.Combine(dirAnnee, "environnement"), estSeance: true);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[JourneeService] Index {annee} : {ex.Message}");
                }
            }

            private void Ajouter(string dossier, string dir, bool estSeance)
            {
                if (!Directory.Exists(dir)) return;
                foreach (var f in Directory.EnumerateFiles(dir, "*.md"))
                {
                    var nom = Path.GetFileNameWithoutExtension(f);
                    if (nom.Length < 10 || !DateTime.TryParseExact(nom[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture,
                                                                    DateTimeStyles.None, out var date))
                        continue;

                    string type;
                    DateTime heure;
                    if (estSeance)
                    {
                        type = "seance";
                        var ecrit = File.GetLastWriteTime(f);
                        heure = ecrit.Date == date ? ecrit : date;
                    }
                    else
                    {
                        // aaaa-mm-jj_HHmm_suffixe : l'heure du nom est celle du début de la note
                        heure = nom.Length >= 15 && TimeSpan.TryParseExact(nom.Substring(11, 4), "hhmm",
                                    CultureInfo.InvariantCulture, out var h)
                            ? date + h
                            : File.GetLastWriteTime(f);
                        type = nom.EndsWith("_absence_excusee", StringComparison.OrdinalIgnoreCase) ? "excuse"
                             : nom.EndsWith("_absence", StringComparison.OrdinalIgnoreCase)        ? "absent"
                             : nom.EndsWith("_rdv_deplace", StringComparison.OrdinalIgnoreCase)    ? "deplace"
                             : nom.EndsWith("_liaison", StringComparison.OrdinalIgnoreCase)        ? "liaison"
                             : "note";
                    }

                    if (!_index!.TryGetValue(date, out var parDossier))
                        _index[date] = parDossier = new Dictionary<string, List<NoteDatee>>(StringComparer.OrdinalIgnoreCase);
                    if (!parDossier.TryGetValue(dossier, out var liste))
                        parDossier[dossier] = liste = new List<NoteDatee>();
                    liste.Add(new NoteDatee(type, heure));
                }
            }
        }

        // ── Aujourd'hui : rapprochement, calcul, registre ───────────────────

        /// <summary>
        /// Le fil d'aujourd'hui, à jour. Refait d'abord le lien agenda → dossier pour les
        /// rendez-vous qui n'en avaient pas : un nouveau patient a souvent son dossier créé
        /// pendant la journée, après la lecture de l'agenda du matin.
        /// </summary>
        public async Task<FilDuJour> ActualiserAujourdhuiAsync()
        {
            var jour = DateTime.Today;
            try
            {
                var rdvs = _agenda.ChargerJour(jour);
                if (rdvs.Any(r => r.Certitude != "sur") && (DateTime.Now - _dernierRapprochement).TotalMinutes >= 2)
                {
                    _dernierRapprochement = DateTime.Now;
                    var annuaire = await AnnuaireAsync(forcer: true);
                    if (annuaire != null)
                        await Task.Run(() => _agenda.RapprocherDossiers(jour, annuaire));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[JourneeService] Rapprochement : {ex.Message}");
            }

            var fil = await Task.Run(() => Calculer(jour));
            await Task.Run(() => Consolider(jusquAujourdhui: true));
            return fil;
        }

        // ── Registre des passages ───────────────────────────────────────────

        private string CheminRegistre => Path.Combine(_racine, "registre_passages.json");

        public RegistrePassages ChargerRegistre()
        {
            lock (_verrou)
            {
                try
                {
                    if (!File.Exists(CheminRegistre)) return new RegistrePassages();
                    return JsonSerializer.Deserialize<RegistrePassages>(File.ReadAllText(CheminRegistre, Encoding.UTF8))
                           ?? new RegistrePassages();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[JourneeService] Registre illisible : {ex.Message}");
                    return new RegistrePassages();
                }
            }
        }

        private void EnregistrerRegistre(RegistrePassages registre)
        {
            lock (_verrou)
            {
                Directory.CreateDirectory(_racine);
                var tmp = CheminRegistre + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(registre, JsonOptions), new UTF8Encoding(false));
                File.Move(tmp, CheminRegistre, overwrite: true);
            }
        }

        /// <summary>Consolide en arrière-plan : à l'ouverture, les jours manqués depuis la dernière fois.</summary>
        public async Task ConsoliderAsync()
        {
            await AnnuaireAsync(forcer: false);
            await Task.Run(() => Consolider(jusquAujourdhui: true));
        }

        // ── Annuaire des dossiers, pour le rapprochement en mémoire ─────────

        private AgendaRapprochementService? _annuaire;
        private DateTime _annuaireCharge = DateTime.MinValue;

        /// <summary>L'annuaire des dossiers, rechargé au plus toutes les 10 minutes (ou sur demande,
        /// quand un dossier vient peut-être d'être créé). Null s'il ne se charge pas : le fil
        /// fonctionne alors sur les seuls liens déjà présents dans l'agenda.</summary>
        private async Task<AgendaRapprochementService?> AnnuaireAsync(bool forcer)
        {
            if (_annuaire != null && !forcer && (DateTime.Now - _annuaireCharge).TotalMinutes < 10)
                return _annuaire;
            try
            {
                var a = new AgendaRapprochementService();
                await a.ChargerAsync();
                _annuaire = a;
                _annuaireCharge = DateTime.Now;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[JourneeService] Annuaire : {ex.Message}");
            }
            return _annuaire;
        }

        /// <summary>
        /// Verse dans le registre les jours pas encore consolidés (jusqu'à hier, définitivement),
        /// puis aujourd'hui (provisoirement : il sera relu jusqu'au soir). Au premier passage,
        /// reprend tout l'agenda disponible. Purge ensuite selon la règle des 6 mois.
        /// </summary>
        private readonly object _verrouConsolidation = new();

        private void Consolider(bool jusquAujourdhui)
        {
            lock (_verrouConsolidation)
            try
            {
                var registre = ChargerRegistre();
                var hier = DateTime.Today.AddDays(-1);
                var debut = registre.ConsolideJusquau?.AddDays(1) ?? PremierJourAgenda() ?? DateTime.Today;

                var lecteur = new Lecteur(_patients);
                if (debut <= hier)
                {
                    var rdvs = _agenda.Charger(debut, hier);
                    for (var j = debut.Date; j <= hier; j = j.AddDays(1))
                        Verser(registre, Calculer(j, rdvs.Where(r => r.Debut.Date == j).ToList(), true, lecteur));
                    registre.ConsolideJusquau = hier;
                }

                if (jusquAujourdhui)
                    Verser(registre, Calculer(DateTime.Today, null, true, lecteur));

                CalculerProchainsRdv(registre);
                Purger(registre);
                EnregistrerRegistre(registre);
                RegistreMisAJour?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[JourneeService] Consolidation : {ex.Message}");
            }
        }

        private DateTime? PremierJourAgenda()
        {
            try
            {
                if (!Directory.Exists(_agenda.Racine)) return null;
                var mois = Directory.EnumerateFiles(_agenda.Racine, "????-??.json")
                    .Select(f => DateTime.TryParseExact(Path.GetFileNameWithoutExtension(f), "yyyy-MM",
                                     CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : (DateTime?)null)
                    .Where(d => d.HasValue).Select(d => d!.Value).ToList();
                return mois.Count == 0 ? null : mois.Min();
            }
            catch { return null; }
        }

        /// <summary>
        /// Ne verse que des FAITS : venu, absent, excusé, déplacé. « Aucune trace » est un doute,
        /// pas un fait — il reste dans le fil du jour, jamais dans la mémoire de Med.
        /// </summary>
        private static void Verser(RegistrePassages registre, FilDuJour fil)
        {
            foreach (var rv in fil.RendezVous)
            {
                if (rv.Dossier.Length == 0) continue;
                if (rv.Etat is not (EtatRendezVous.EnCours or EtatRendezVous.Termine or EtatRendezVous.SansNote
                                 or EtatRendezVous.Absent or EtatRendezVous.Excuse or EtatRendezVous.Deplace))
                    continue;

                var e = Entree(registre, rv.Dossier, $"{rv.Rdv.Prenom} {rv.Rdv.Nom}".Trim());
                if (e.DernierRdv == null || rv.Rdv.Debut >= e.DernierRdv)
                {
                    e.DernierRdv = rv.Rdv.Debut;
                    e.DernierEtat = rv.Libelle;
                }
                if (rv.EstVenu && (e.DerniereVenue == null || fil.Jour > e.DerniereVenue))
                    e.DerniereVenue = fil.Jour;
            }

            foreach (var h in fil.HorsAgenda)
            {
                var e = Entree(registre, h.Dossier, h.Nom);
                if (e.DerniereVenue == null || fil.Jour > e.DerniereVenue) e.DerniereVenue = fil.Jour;
                // Pas « hors agenda » dans la mémoire : sur les jours anciens, l'agenda peut
                // simplement être incomplet. Le registre ne garde que ce qui est sûr.
                if (e.DernierRdv == null || h.Heure >= e.DernierRdv)
                {
                    e.DernierRdv = h.Heure;
                    e.DernierEtat = $"{h.Quoi} au dossier";
                }
            }
        }

        private static EntreePassage Entree(RegistrePassages registre, string dossier, string nom)
        {
            var e = registre.Entrees.FirstOrDefault(x => string.Equals(x.Dossier, dossier, StringComparison.OrdinalIgnoreCase));
            if (e == null)
            {
                e = new EntreePassage { Dossier = dossier, Nom = nom };
                registre.Entrees.Add(e);
            }
            else if (nom.Length > 0) e.Nom = nom;
            return e;
        }

        private void CalculerProchainsRdv(RegistrePassages registre)
        {
            var futurs = _agenda.Charger(DateTime.Today, DateTime.Today.AddMonths(MoisAvantOubli))
                // À partir de demain : le rendez-vous du jour est déjà dans le fil, pas « à venir ».
                .Where(r => r.Debut.Date > DateTime.Today && r.Certitude == "sur" && r.Dossier.Length > 0
                            && !(r.Statut ?? "").StartsWith("Annul", StringComparison.OrdinalIgnoreCase))
                .GroupBy(r => r.Dossier, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Min(r => r.Debut), StringComparer.OrdinalIgnoreCase);

            foreach (var e in registre.Entrees)
                e.ProchainRdv = futurs.TryGetValue(e.Dossier, out var d) ? d : null;
        }

        /// <summary>6 mois sans venue ET sans rendez-vous à venir : le patient sort de la mémoire de Med.
        /// Son dossier, lui, ne bouge pas.</summary>
        private static void Purger(RegistrePassages registre)
        {
            var limite = DateTime.Today.AddMonths(-MoisAvantOubli);
            registre.Entrees.RemoveAll(e => e.ProchainRdv == null
                                            && (e.DerniereVenue ?? e.DernierRdv ?? DateTime.MinValue) < limite);
            registre.Entrees = registre.Entrees.OrderByDescending(e => e.DernierRdv ?? DateTime.MinValue).ToList();
        }
    }
}
