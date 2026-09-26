using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using MedCompanion.Models.Agenda;
using MedCompanion.Services.Agenda;

namespace MedCompanion.ViewModels.Agenda
{
    /// <summary>
    /// Agenda du Bureau : copie locale de l'agenda Doctolib, en lecture seule, présentée comme
    /// chez Doctolib — grille horaire, une colonne par jour, blocs colorés par motif.
    /// Ouvre sur la journée ; la semaine est à un clic.
    /// </summary>
    public class AgendaViewModel : INotifyPropertyChanged
    {
        /// <summary>Amplitude de repli quand la période affichée n'a aucun rendez-vous.</summary>
        private const int HeureDebutDefaut = 9;
        private const int HeureFinDefaut   = 19;

        /// <summary>Bornes absolues : au-delà, la grille ne s'étire plus.</summary>
        private const int HeureMin = 7;
        private const int HeureMax = 22;

        /// <summary>En dessous, les blocs deviennent illisibles : on rend la main au défilement.</summary>
        private const double EchelleMinimale = 0.40;

        /// <summary>Un jour est découpé en 12 colonnes : 1, 2, 3, 4 ou 6 rendez-vous simultanés
        /// se partagent la largeur sans reste.</summary>
        public const int ColonnesParJour = 12;

        private readonly AgendaService _service = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        public ObservableCollection<JourAgenda> Jours { get; } = new();
        public ObservableCollection<string> Heures { get; } = new();

        /// <summary>
        /// Plage réellement occupée par les rendez-vous affichés, arrondie à l'heure. Inutile de
        /// montrer 8 h quand les consultations commencent à 9 h.
        /// </summary>
        public int HeureDebut { get; private set; } = HeureDebutDefaut;
        public int HeureFin   { get; private set; } = HeureFinDefaut;

        /// <summary>
        /// Échelle d'affichage, recalculée pour que la journée tienne dans la hauteur disponible,
        /// sans défilement. Le défilement ne revient que si la fenêtre devient vraiment petite.
        /// </summary>
        public double PixelsParMinute { get; private set; } = 1.0;

        private double _hauteurDisponible;

        /// <summary>Appelée par la vue à chaque redimensionnement de la zone de grille.</summary>
        public void DefinirHauteurDisponible(double hauteur)
        {
            if (hauteur <= 0 || Math.Abs(hauteur - _hauteurDisponible) < 1) return;
            _hauteurDisponible = hauteur;
            Rafraichir();
        }

        public double HauteurGrille => (HeureFin - HeureDebut) * 60 * PixelsParMinute;
        public double HauteurHeure  => 60 * PixelsParMinute;

        private DateTime _jour = DateTime.Today;
        public DateTime Jour { get => _jour; private set { if (Set(ref _jour, value)) Rafraichir(); } }

        private bool _modeSemaine;
        public bool ModeSemaine
        {
            get => _modeSemaine;
            set { if (Set(ref _modeSemaine, value)) Rafraichir(); }
        }

        public string Titre => _modeSemaine
            ? $"Semaine du {DebutSemaine(_jour):d MMMM} au {DebutSemaine(_jour).AddDays(5):d MMMM yyyy}"
            : Majuscule(_jour.ToString("dddd d MMMM yyyy", Fr));

        private string _statut = "";
        public string Statut { get => _statut; set => Set(ref _statut, value ?? ""); }

        // ── Trait rouge de l'heure courante, comme chez Doctolib ────────────

        public bool HeureCouranteVisible => Jours.Any(j => j.Date == DateTime.Today)
                                            && DateTime.Now.Hour >= HeureDebut && DateTime.Now.Hour < HeureFin;

        public Thickness MargeHeureCourante =>
            new(0, (DateTime.Now.TimeOfDay - TimeSpan.FromHours(HeureDebut)).TotalMinutes * PixelsParMinute, 0, 0);

        // ── Fraîcheur : un agenda périmé qu'on croit à jour est pire que pas d'agenda ──

        private AgendaEtat _etat = new();

        public string Fraicheur => _etat.DerniereImportation == null
            ? "Aucun import — l'agenda est vide."
            : $"À jour au {_etat.DerniereImportation:dd/MM/yyyy à HH'h'mm} · {_etat.NombreRendezVous} rendez-vous";

        /// <summary>Vrai au-delà de deux jours : l'affichage passe en orange.</summary>
        public bool EstPerime => _etat.DerniereImportation == null
                                 || (DateTime.Now - _etat.DerniereImportation.Value).TotalDays > 2;

        public string RappelImport => _etat.DerniereImportation == null
            ? "Exportez l'agenda depuis Doctolib (Paramètres → Données → Exports, type « Agenda »), puis importez le fichier ici."
            : "Cet agenda date de plus de deux jours : réexportez-le depuis Doctolib pour être sûr de ce qui vous attend.";

        // ── Actions ─────────────────────────────────────────────────────────

        public void Aujourdhui() => Jour = DateTime.Today;
        public void Precedent()  => Jour = _modeSemaine ? _jour.AddDays(-7) : JourOuvre(_jour, -1);
        public void Suivant()    => Jour = _modeSemaine ? _jour.AddDays(7)  : JourOuvre(_jour, +1);

        /// <summary>Période actuellement affichée : la journée, ou du lundi au samedi.</summary>
        public (DateTime du, DateTime au) PeriodeAffichee => _modeSemaine
            ? (DebutSemaine(_jour), DebutSemaine(_jour).AddDays(5))
            : (_jour.Date, _jour.Date);

        public string LibelleVider => _modeSemaine ? "🗑 Vider la semaine" : "🗑 Vider la journée";

        /// <summary>Vide la période affichée — outil d'essai de la lecture d'écran.</summary>
        public void ViderPeriodeAffichee()
        {
            var (du, au) = PeriodeAffichee;
            var supprimes = _service.SupprimerPeriode(du, au);
            Statut = supprimes == 0
                ? "Rien à vider sur cette période."
                : $"{supprimes} rendez-vous supprimés de la copie locale ({du:dd/MM}" +
                  (du == au ? ")" : $" au {au:dd/MM})") + ". Doctolib n'est pas touché.";
            EffacerLecture();
            Rafraichir();
        }

        public void Importer(string cheminCsv)
        {
            var (ok, importes, misAJour, err) = _service.ImporterCsv(cheminCsv);
            if (!ok) { Statut = err ?? "Import impossible."; return; }

            Statut = misAJour > 0
                ? $"{importes} rendez-vous ajoutés, {misAJour} mis à jour."
                : $"{importes} rendez-vous importés.";
            Rafraichir();
        }

        public void Rafraichir()
        {
            _etat = _service.ChargerEtat();

            var debut = _modeSemaine ? DebutSemaine(_jour) : _jour.Date;
            var fin   = _modeSemaine ? debut.AddDays(5) : _jour.Date;   // lundi → samedi
            var rdvs  = _service.Charger(debut, fin);

            CalculerPlageEtEchelle(rdvs);

            Heures.Clear();
            for (int h = HeureDebut; h < HeureFin; h++) Heures.Add($"{h}h");

            Jours.Clear();
            for (var d = debut; d <= fin; d = d.AddDays(1))
                Jours.Add(new JourAgenda(d, rdvs.Where(r => r.Debut.Date == d.Date).ToList(),
                                         HeureDebut, PixelsParMinute));

            foreach (var nom in new[] { nameof(Titre), nameof(Fraicheur), nameof(EstPerime), nameof(RappelImport),
                                        nameof(HeureCouranteVisible), nameof(MargeHeureCourante),
                                        nameof(HauteurGrille), nameof(HauteurHeure), nameof(LibelleVider) })
                OnPropertyChanged(nom);
        }

        // ── Lecture du matin : deuxième lecture, jamais la source ────────────

        private readonly AgendaLectureEcranService _lecture = new();
        private readonly AgendaCaptureService _captures = new();
        private readonly AgendaRapprochementService _annuaire = new();

        public ObservableCollection<EcartAgenda> Ecarts { get; } = new();

        private bool _lectureEnCours;
        public bool LectureEnCours { get => _lectureEnCours; private set => Set(ref _lectureEnCours, value); }

        private string _resumeLecture = "";
        public string ResumeLecture { get => _resumeLecture; private set => Set(ref _resumeLecture, value ?? ""); }

        public bool ALecture => Ecarts.Count > 0 || ResumeLecture.Length > 0;

        /// <summary>
        /// Reprend les captures prises aujourd'hui, les fait lire par le modèle vision et met
        /// l'agenda à jour. Écrit dans la copie de Med, jamais dans Doctolib, et ne supprime
        /// jamais : le journal affiche ce qui a changé, le médecin vérifie dans Doctolib.
        /// </summary>
        public async System.Threading.Tasks.Task MettreAJourDepuisCapturesAsync()
        {
            if (LectureEnCours) return;

            var captures = _captures.CapturesDuJour();
            if (captures.Count == 0)
            {
                Ecarts.Clear();
                ResumeLecture = "Aucune capture du jour. Intégrez Doctolib dans le Bureau, puis utilisez " +
                                "« Capturer la semaine » et « Capturer le jour ».";
                OnPropertyChanged(nameof(ALecture));
                return;
            }

            LectureEnCours = true;
            Ecarts.Clear();
            var journal = new List<AgendaService.Changement>();
            var erreurs = new List<string>();
            var joursTouches = new HashSet<DateTime>();
            int lues = 0;

            try
            {
                foreach (var capture in captures)
                {
                    ResumeLecture = $"Med lit la capture « {capture.Vue} » de {capture.Prise:HH'h'mm}…";
                    var image = System.IO.File.ReadAllBytes(capture.Chemin);

                    if (capture.Vue == AgendaCaptureService.VueSemaine)
                    {
                        // Une image par jour : sur la grille entière, le modèle s'arrête après la
                        // première colonne (mesuré le 26/09/2026).
                        var colonnes = AgendaImageService.DecouperColonnes(image);
                        if (colonnes.Count == 0) { erreurs.Add("semaine : découpage impossible"); continue; }

                        for (int i = 0; i < colonnes.Count; i++)
                        {
                            ResumeLecture = $"Med lit la semaine, jour {i + 1} sur {colonnes.Count}…";
                            var (ok, lignes, err) = await _lecture.LireColonneAsync(colonnes[i]);
                            if (!ok) { erreurs.Add($"jour {i + 1} : {err}"); continue; }
                            lues += lignes.Count;

                            // La grille ne montre pas forcément toute la journée : on n'y signale
                            // pas les rendez-vous manquants, on se contente d'ajouter et de corriger.
                            foreach (var groupe in lignes.Where(l => l.Date.HasValue).GroupBy(l => l.Date!.Value.Date))
                            {
                                journal.AddRange(_service.FusionnerLectureEcran(groupe.Key, Convertir(groupe), journeeComplete: false));
                                joursTouches.Add(groupe.Key);
                            }
                        }
                    }
                    else
                    {
                        var (ok, lignes, err) = await _lecture.LireJourAsync(image);
                        if (!ok) { erreurs.Add($"jour : {err}"); continue; }
                        lues += lignes.Count;

                        var jour = lignes.FirstOrDefault(l => l.Date.HasValue)?.Date ?? DateTime.Today;
                        journal.AddRange(_service.FusionnerLectureEcran(jour.Date, Convertir(lignes), journeeComplete: true));
                        joursTouches.Add(jour.Date);

                        // Les annulations : le modèle ne voit pas le trait de rature sur la
                        // capture entière. On le repère par l'image, puis on ne lui fait relire
                        // que ces lignes-là, agrandies (mesuré le 26/09/2026).
                        var bandes = AgendaImageService.BandesRayees(image);
                        if (bandes.Count > 0)
                        {
                            var rayees = new List<(TimeSpan, string)>();
                            for (int i = 0; i < bandes.Count; i++)
                            {
                                ResumeLecture = $"Med relit une ligne barrée, {i + 1} sur {bandes.Count}…";
                                var (okBarre, heure, nom, errBarre) = await _lecture.LireLigneRayeeAsync(bandes[i]);
                                if (okBarre) rayees.Add((heure, nom));
                                else erreurs.Add($"ligne barrée : {errBarre}");
                            }
                            journal.AddRange(_service.MarquerAnnules(jour.Date, rayees));
                        }
                    }
                }

                // Rapprochement avec les dossiers de Med, une fois les lectures fusionnées : on
                // travaille sur des rendez-vous complets, pas sur des lignes en cours de fusion.
                if (joursTouches.Count > 0)
                {
                    ResumeLecture = "Med rapproche les patients de ses dossiers…";
                    await _annuaire.ChargerAsync();
                    foreach (var jour in joursTouches.OrderBy(j => j))
                        journal.AddRange(_service.RapprocherDossiers(jour, _annuaire));
                }

                foreach (var c in journal)
                    Ecarts.Add(new EcartAgenda(c.Type, c.Texte, ""));

                ResumeLecture = journal.Count == 0
                    ? $"Mise à jour du {DateTime.Now:HH'h'mm} : {lues} rendez-vous lus, rien de nouveau."
                    : $"Mise à jour du {DateTime.Now:HH'h'mm} : {lues} rendez-vous lus, {journal.Count} changement(s).";
                if (erreurs.Count > 0) ResumeLecture += " Échecs : " + string.Join(" · ", erreurs);

                Rafraichir();
            }
            catch (Exception ex)
            {
                ResumeLecture = $"Mise à jour impossible : {ex.Message}";
            }
            finally
            {
                LectureEnCours = false;
                OnPropertyChanged(nameof(ALecture));
            }
        }

        private static List<(TimeSpan, string, string, DateTime?, string, bool)> Convertir(
            IEnumerable<AgendaLectureEcranService.LigneLue> lignes)
            => lignes.Select(l => (l.Heure, l.Nom, l.Motif, l.DateNaissance, l.Telephone, l.Barre)).ToList();
        public void EffacerLecture()
        {
            Ecarts.Clear();
            ResumeLecture = "";
            OnPropertyChanged(nameof(ALecture));
        }

        /// <summary>
        /// Cale la grille sur ce qu'il y a à montrer : première heure entamée, dernière heure
        /// terminée, puis une échelle qui fait tenir le tout dans la hauteur disponible.
        /// </summary>
        private void CalculerPlageEtEchelle(List<RendezVous> rdvs)
        {
            if (rdvs.Count > 0)
            {
                HeureDebut = Math.Max(HeureMin, rdvs.Min(r => r.Debut).Hour);
                var derniereFin = rdvs.Max(r => r.Fin);
                HeureFin = Math.Min(HeureMax, derniereFin.Hour + (derniereFin.Minute > 0 ? 1 : 0));
                if (HeureFin <= HeureDebut) HeureFin = HeureDebut + 1;
            }
            else
            {
                HeureDebut = HeureDebutDefaut;
                HeureFin   = HeureFinDefaut;
            }

            var minutes = (HeureFin - HeureDebut) * 60.0;
            PixelsParMinute = _hauteurDisponible > 0
                ? Math.Max(EchelleMinimale, _hauteurDisponible / minutes)
                : 1.0;
        }

        // ── Utilitaires ─────────────────────────────────────────────────────

        internal static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private static DateTime DebutSemaine(DateTime d)
        {
            int delta = (int)d.DayOfWeek - (int)DayOfWeek.Monday;
            if (delta < 0) delta += 7;
            return d.Date.AddDays(-delta);
        }

        /// <summary>Le dimanche est sauté : personne ne consulte le dimanche.</summary>
        private static DateTime JourOuvre(DateTime d, int pas)
        {
            var j = d.AddDays(pas);
            return j.DayOfWeek == DayOfWeek.Sunday ? j.AddDays(pas) : j;
        }

        internal static string Majuscule(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];

        private bool Set<T>(ref T champ, T valeur, [CallerMemberName] string? nom = null)
        {
            if (EqualityComparer<T>.Default.Equals(champ, valeur)) return false;
            champ = valeur;
            OnPropertyChanged(nom);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? nom = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nom));
    }

    /// <summary>Un écart entre la copie locale et ce qui est lu à l'écran Doctolib.</summary>
    public class EcartAgenda
    {
        public EcartAgenda(string type, string titre, string detail)
        {
            Type = type;
            Titre = titre;
            Detail = detail;
        }

        public string Type { get; }
        public string Titre { get; }
        public string Detail { get; }

        public string Icone => Type switch
        {
            "ajout"   => "➕",
            "deplace" => "↔",
            "annule"  => "✖",
            "doublon" => "⇄",
            "inconnu" => "⚠",
            "conflit" => "⚠",
            "sans-dossier" => "🗂",
            _         => "❓"
        };

        public string Couleur => Type switch
        {
            "ajout"   => "#1E8449",
            "deplace" => "#D35400",
            "annule"  => "#C0392B",
            "doublon" => "#7F8C8D",
            "inconnu" => "#D35400",
            "conflit" => "#D35400",
            "sans-dossier" => "#5D6D7E",
            _         => "#7D3C98"
        };
    }

    /// <summary>Une colonne de la grille : un jour et ses blocs, déjà placés.</summary>
    public class JourAgenda
    {
        public JourAgenda(DateTime date, List<RendezVous> rdvs, int heureDebut, double pixelsParMinute)
        {
            Date = date;
            Blocs = new ObservableCollection<BlocRendezVous>(
                Placer(rdvs.OrderBy(r => r.Debut).ToList(), heureDebut, pixelsParMinute));
        }

        public DateTime Date { get; }
        public ObservableCollection<BlocRendezVous> Blocs { get; }

        public string NomJour => AgendaViewModel.Majuscule(Date.ToString("ddd d MMM", AgendaViewModel.Fr));
        public bool EstAujourdhui => Date == DateTime.Today;

        public string Compte => Blocs.Count switch
        {
            0 => "—",
            1 => "1 rdv",
            _ => $"{Blocs.Count} rdv"
        };

        /// <summary>
        /// Place les rendez-vous : ceux qui se chevauchent se partagent la largeur du jour,
        /// comme dans la grille Doctolib. Les blocs sont regroupés par chaîne de chevauchement,
        /// puis rangés dans la première colonne libre du groupe.
        /// </summary>
        private static List<BlocRendezVous> Placer(List<RendezVous> rdvs, int heureDebut, double pixelsParMinute)
        {
            var blocs = new List<BlocRendezVous>();
            int i = 0;
            while (i < rdvs.Count)
            {
                // Un groupe : tant qu'un rendez-vous commence avant la fin du plus tardif du groupe
                var groupe = new List<RendezVous> { rdvs[i] };
                var finGroupe = rdvs[i].Fin;
                int j = i + 1;
                while (j < rdvs.Count && rdvs[j].Debut < finGroupe)
                {
                    groupe.Add(rdvs[j]);
                    if (rdvs[j].Fin > finGroupe) finGroupe = rdvs[j].Fin;
                    j++;
                }

                var finColonnes = new List<DateTime>();
                var colonneDe = new int[groupe.Count];
                for (int k = 0; k < groupe.Count; k++)
                {
                    int libre = finColonnes.FindIndex(f => f <= groupe[k].Debut);
                    if (libre < 0) { finColonnes.Add(groupe[k].Fin); libre = finColonnes.Count - 1; }
                    else finColonnes[libre] = groupe[k].Fin;
                    colonneDe[k] = libre;
                }

                int nb = Math.Max(1, Math.Min(finColonnes.Count, AgendaViewModel.ColonnesParJour));
                int largeur = Math.Max(1, AgendaViewModel.ColonnesParJour / nb);
                for (int k = 0; k < groupe.Count; k++)
                    blocs.Add(new BlocRendezVous(groupe[k], Math.Min(colonneDe[k], nb - 1) * largeur, largeur,
                                                 heureDebut, pixelsParMinute));

                i = j;
            }
            return blocs;
        }
    }

    /// <summary>Un rendez-vous placé dans la grille.</summary>
    public class BlocRendezVous
    {
        private readonly int _heureDebut;
        private readonly double _pixelsParMinute;

        public BlocRendezVous(RendezVous rdv, int colonne, int largeur, int heureDebut, double pixelsParMinute)
        {
            Rdv = rdv;
            Colonne = colonne;
            Largeur = largeur;
            _heureDebut = heureDebut;
            _pixelsParMinute = pixelsParMinute;
        }

        public RendezVous Rdv { get; }
        public int Colonne { get; }
        public int Largeur { get; }

        private double MinutesDepuisDebut =>
            Math.Max(0, (Rdv.Debut.TimeOfDay - TimeSpan.FromHours(_heureDebut)).TotalMinutes);

        public Thickness Marge => new(1, MinutesDepuisDebut * _pixelsParMinute, 1, 0);

        public double Hauteur => Math.Max(14, (Rdv.DureeMinutes <= 0 ? 30 : Rdv.DureeMinutes)
                                              * _pixelsParMinute - 2);

        public string Heure => Rdv.Debut.ToString("HH'h'mm");
        public string NomComplet => Rdv.NomComplet;
        public string Motif => Rdv.Motif;
        public bool MotifVisible => Hauteur >= 34;

        public string Statut => Rdv.Statut;

        /// <summary>Annulé : barré et pâli, comme Doctolib le montre. La case reste visible —
        /// un créneau libéré se voit, et le patient peut rappeler.</summary>
        public bool EstAnnule => Rdv.Statut.StartsWith("Annul", StringComparison.OrdinalIgnoreCase);

        /// <summary>Un rendez-vous passé resté « À venir » n'a jamais été pointé : à vérifier.</summary>
        public bool ADouteStatut => Rdv.Debut < DateTime.Now
                                    && Rdv.Statut.StartsWith("À venir", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Aucun dossier Med rattaché, ou plusieurs possibles. Ce n'est pas une anomalie —
        /// une consultation parents n'a pas de dossier enfant — mais ça se voit d'un coup d'œil.
        /// </summary>
        public bool SansDossier => Rdv.Certitude is "aucun" or "ambigu";

        /// <summary>Dossier à ouvrir au clic, vide quand Med n'a rien de sûr à proposer.</summary>
        public string Dossier => Rdv.Dossier;

        /// <summary>Un clic mène quelque part. Sinon le bloc reste inerte, et le curseur le dit.</summary>
        public bool PeutOuvrir => Rdv.Dossier.Length > 0;

        /// <summary>Âge au jour du rendez-vous, pas au jour d'aujourd'hui.</summary>
        public int Age
        {
            get
            {
                if (!Rdv.DateNaissance.HasValue) return 0;
                var n = Rdv.DateNaissance.Value;
                int age = Rdv.Debut.Year - n.Year;
                if (Rdv.Debut.Date < n.AddYears(age)) age--;
                return age;
            }
        }

        public string InfoBulle
        {
            get
            {
                var l = new List<string> { $"{Heure} · {Rdv.DureeMinutes} min · {NomComplet}", Motif };
                if (Rdv.DateNaissance.HasValue) l.Add($"Né(e) le {Rdv.DateNaissance:dd/MM/yyyy} ({Age} ans)");
                if (!string.IsNullOrWhiteSpace(Rdv.Telephone)) l.Add(Rdv.Telephone);
                l.Add($"Statut Doctolib : {Rdv.Statut}");
                l.Add(Rdv.Certitude switch
                {
                    "sur"         => $"Dossier Med : {Rdv.Dossier}",
                    "a-confirmer" => $"Dossier Med : {Rdv.Dossier} (à confirmer)",
                    "ambigu"      => "Plusieurs dossiers possibles — Med n'en a lié aucun",
                    "aucun"       => "Aucun dossier Med",
                    _             => ""
                });
                if (Rdv.NouveauPatient) l.Add("Nouveau patient");
                if (!string.IsNullOrWhiteSpace(Rdv.Notes)) l.Add("Note : " + Rdv.Notes);
                if (ADouteStatut) l.Add("⚠ Rendez-vous passé encore « À venir » : pointage oublié, ou patient absent ?");
                return string.Join("\n", l.Where(s => !string.IsNullOrWhiteSpace(s)));
            }
        }

        // Couleurs reprises de la légende Doctolib : suivi en clair, première consultation en
        // soutenu, parents en orange, le reste en gris.
        public string CouleurFond => Cle switch
        {
            "enfant-suivi"      => "#D6F5E3",
            "enfant-premiere"   => "#8FD9B6",
            "ado-suivi"         => "#D6EAF8",
            "ado-premiere"      => "#7FB3D5",
            "parents"           => "#FAD7A0",
            _                   => "#E5E8E8"
        };

        public string CouleurBord => Cle switch
        {
            "enfant-suivi"      => "#7DCEA0",
            "enfant-premiere"   => "#27AE60",
            "ado-suivi"         => "#85C1E9",
            "ado-premiere"      => "#2E86C1",
            "parents"           => "#E59866",
            _                   => "#BDC3C7"
        };

        private string Cle
        {
            get
            {
                var m = (Motif ?? "").ToLowerInvariant();
                bool premiere = m.Contains("première") || m.Contains("premiere");
                if (m.Contains("parent")) return "parents";
                if (m.Contains("adolescent")) return premiere ? "ado-premiere" : "ado-suivi";
                if (m.Contains("enfant")) return premiere ? "enfant-premiere" : "enfant-suivi";
                return "autre";
            }
        }
    }
}
