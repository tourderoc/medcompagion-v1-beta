using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Media.Imaging;
using MedCompanion.Models.Infographies;
using MedCompanion.Services.Infographies;

namespace MedCompanion.ViewModels.Infographies
{
    /// <summary>
    /// Bibliothèque d'infographies, partagée par les deux écrans :
    /// - le mode Bureau « Infographies » (import, classement, validation) ;
    /// - la fenêtre de consultation (choix et impression, fiches validées seulement).
    /// </summary>
    public class InfographiesViewModel : INotifyPropertyChanged
    {
        public const string Tous = "Tous";

        private readonly InfographieService _service = new();
        private InfographieAnalyseService? _analyse; // créé au premier usage : inutile en consultation
        private readonly Queue<string> _aAnalyser = new();
        private readonly bool _seulementValidees;
        private readonly string? _patientDirectory;
        private List<InfographieCarte> _toutes = new();
        private Dictionary<string, DateTime> _remises = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <param name="seulementValidees">Vrai en consultation : une fiche non relue n'est jamais proposée.</param>
        /// <param name="patientDirectory">Dossier du patient en consultation, pour tracer les remises.</param>
        public InfographiesViewModel(bool seulementValidees, string? patientDirectory = null)
        {
            _seulementValidees = seulementValidees;
            _patientDirectory = string.IsNullOrWhiteSpace(patientDirectory) ? null : patientDirectory;

            // « Tous » présent dès le départ : un ComboBox dont la valeur liée manque à sa liste
            // n'affiche rien, même quand la valeur arrive ensuite.
            TypesFiltre.Add(Tous);
            SujetsFiltre.Add(Tous);
            PublicsFiltre.Add(Tous);
        }

        public InfographieService Service => _service;
        public string? PatientDirectory => _patientDirectory;

        public ObservableCollection<InfographieCarte> Cartes { get; } = new();
        public ObservableCollection<string> TypesFiltre { get; } = new();
        public ObservableCollection<string> SujetsFiltre { get; } = new();
        public ObservableCollection<string> PublicsFiltre { get; } = new();

        /// <summary>Choix proposés dans les listes modifiables de la fiche (défauts + valeurs déjà utilisées).</summary>
        public ObservableCollection<string> TypesChoix { get; } = new();
        public ObservableCollection<string> PublicsChoix { get; } = new();

        private string _filtreType = Tous;
        public string FiltreType { get => _filtreType; set => ChangerFiltre(ref _filtreType, value); }

        private string _filtreSujet = Tous;
        public string FiltreSujet { get => _filtreSujet; set => ChangerFiltre(ref _filtreSujet, value); }

        private string _filtrePublic = Tous;
        public string FiltrePublic { get => _filtrePublic; set => ChangerFiltre(ref _filtrePublic, value); }

        private string _recherche = "";
        public string Recherche { get => _recherche; set { if (Set(ref _recherche, value ?? "")) AppliquerFiltres(); } }

        private InfographieCarte? _selection;
        public InfographieCarte? Selection
        {
            get => _selection;
            set
            {
                if (!Set(ref _selection, value)) return;
                OnPropertyChanged(nameof(ASelection));
                OnPropertyChanged(nameof(ImageSelection));
            }
        }

        public bool ASelection => _selection != null;
        public BitmapImage? ImageSelection => _selection == null ? null : InfographieImpression.Charger(_selection.Fiche.ImagePath);

        private string _statut = "";
        public string Statut { get => _statut; set => Set(ref _statut, value ?? ""); }

        public bool EstVide => _toutes.Count == 0;
        public bool AucunResultat => _toutes.Count > 0 && Cartes.Count == 0;

        public string MessageVide => _seulementValidees
            ? "Aucune infographie validée pour l'instant.\nImportez et validez vos fiches depuis le mode Bureau → Infographies."
            : "La bibliothèque est vide.\nCliquez sur « Importer » ou déposez vos images ici.";

        // ── Chargement ──────────────────────────────────────────────────────

        public void Charger(string? idASelectionner = null)
        {
            var (ok, fiches, err) = _service.ListFiches();
            if (!ok) Statut = err ?? "";

            if (_patientDirectory != null)
                _remises = _service.ListRemises(_patientDirectory)
                    .GroupBy(r => r.Id)
                    .ToDictionary(g => g.Key, g => g.Max(r => r.Date));

            _toutes = fiches
                .Where(f => !_seulementValidees || f.EstValidee)
                .Select(f => new InfographieCarte(f, this))
                .ToList();

            RafraichirListes();
            AppliquerFiltres(idASelectionner ?? _selection?.Fiche.Id);
            OnPropertyChanged(nameof(EstVide));
        }

        /// <returns>Vrai si un filtre a dû revenir à « Tous » : la liste affichée est à refiltrer.</returns>
        private bool RafraichirListes()
        {
            Remplir(TypesFiltre, new[] { Tous }.Concat(Distincts(_toutes.Select(c => c.Fiche.Type))));
            Remplir(SujetsFiltre, new[] { Tous }.Concat(Distincts(_toutes.SelectMany(c => c.Fiche.Sujets))));
            Remplir(PublicsFiltre, new[] { Tous }.Concat(Distincts(_toutes.Select(c => c.Fiche.Public))));
            Remplir(TypesChoix, Distincts(InfographieService.TypesParDefaut.Concat(_toutes.Select(c => c.Fiche.Type))));
            Remplir(PublicsChoix, Distincts(InfographieService.PublicsParDefaut.Concat(_toutes.Select(c => c.Fiche.Public))));

            // Un filtre dont la valeur a disparu (dernière fiche reclassée) revient à « Tous ».
            bool reinitialise = false;
            if (!TypesFiltre.Contains(_filtreType)) { _filtreType = Tous; reinitialise = true; }
            if (!SujetsFiltre.Contains(_filtreSujet)) { _filtreSujet = Tous; reinitialise = true; }
            if (!PublicsFiltre.Contains(_filtrePublic)) { _filtrePublic = Tous; reinitialise = true; }
            OnPropertyChanged(nameof(FiltreType));
            OnPropertyChanged(nameof(FiltreSujet));
            OnPropertyChanged(nameof(FiltrePublic));
            return reinitialise;
        }

        private void AppliquerFiltres(string? idASelectionner = null)
        {
            idASelectionner ??= _selection?.Fiche.Id;
            var mots = Normaliser(_recherche).Split(' ', StringSplitOptions.RemoveEmptyEntries);

            var resultat = _toutes.Where(c =>
                (_filtreType == Tous || Egal(c.Fiche.Type, _filtreType)) &&
                (_filtreSujet == Tous || c.Fiche.Sujets.Any(s => Egal(s, _filtreSujet))) &&
                (_filtrePublic == Tous || Egal(c.Fiche.Public, _filtrePublic)) &&
                mots.All(m => c.TexteRecherche.Contains(m)));

            Cartes.Clear();
            foreach (var c in resultat) Cartes.Add(c);

            Selection = Cartes.FirstOrDefault(c => c.Fiche.Id == idASelectionner) ?? Cartes.FirstOrDefault();
            OnPropertyChanged(nameof(AucunResultat));
        }

        // ── Actions (mode Bureau) ───────────────────────────────────────────

        public void Importer(IEnumerable<string> chemins)
        {
            var importees = new List<string>();
            var erreurs = new List<string>();
            foreach (var chemin in chemins)
            {
                var (ok, fiche, err) = _service.Importer(chemin);
                if (ok && fiche != null) importees.Add(fiche.Id);
                else if (err != null) erreurs.Add(err);
            }
            int n = importees.Count;
            string? dernierId = importees.LastOrDefault();

            // Une fiche importée est « à relire » : on retire les filtres pour qu'elle soit visible
            _filtreType = _filtreSujet = _filtrePublic = Tous;
            _recherche = "";
            OnPropertyChanged(nameof(FiltreType)); OnPropertyChanged(nameof(FiltreSujet));
            OnPropertyChanged(nameof(FiltrePublic)); OnPropertyChanged(nameof(Recherche));
            Charger(dernierId);

            Statut = n > 0
                ? $"{n} infographie(s) importée(s), à classer et à relire."
                : "Aucune image importée.";
            if (erreurs.Count > 0) Statut += " " + string.Join(" ", erreurs);

            if (n > 0) _ = ProposerAsync(importees);
        }

        private bool _analyseEnCours;
        public bool AnalyseEnCours
        {
            get => _analyseEnCours;
            private set { if (Set(ref _analyseEnCours, value)) OnPropertyChanged(nameof(PeutProposer)); }
        }
        public bool PeutProposer => !_analyseEnCours;

        private const string EnteteAVerifier = "À vérifier (relevé par Med) :";

        /// <summary>
        /// Fait lire les fiches par le modèle vision et pré-remplit titre, type, sujets et public.
        /// Les affirmations chiffrées ou réglementaires sont recopiées dans les notes, à vérifier.
        /// </summary>
        public async System.Threading.Tasks.Task ProposerAsync(IEnumerable<string> ids)
        {
            // Un import pendant une analyse rejoint la file au lieu d'être oublié
            foreach (var id in ids) if (!_aAnalyser.Contains(id)) _aAnalyser.Enqueue(id);
            if (AnalyseEnCours) return;
            AnalyseEnCours = true;
            _analyse ??= new InfographieAnalyseService();
            int faites = 0;
            var erreurs = new List<string>();
            try
            {
                while (_aAnalyser.Count > 0)
                {
                    var id = _aAnalyser.Dequeue();
                    var carte = _toutes.FirstOrDefault(c => c.Fiche.Id == id);
                    if (carte == null) continue;
                    Statut = _aAnalyser.Count > 0
                        ? $"Med lit « {carte.Titre} »… (encore {_aAnalyser.Count} en attente)"
                        : $"Med lit « {carte.Titre} »…";

                    var (ok, p, err) = await _analyse.AnalyserAsync(carte.Fiche,
                        TypesChoix, SujetsFiltre.Where(s => s != Tous), PublicsChoix);
                    if (!ok || p == null) { erreurs.Add($"« {carte.Titre} » : {err}"); continue; }

                    // Un import pendant la lecture a rechargé la liste : appliquer à la carte affichée
                    carte = _toutes.FirstOrDefault(c => c.Fiche.Id == id);
                    if (carte == null) continue;
                    AppliquerProposition(carte, p);
                    faites++;
                }
            }
            catch (Exception ex)
            {
                erreurs.Add(ex.Message);
            }
            finally
            {
                AnalyseEnCours = false;
            }

            Statut = faites > 0
                ? $"Med a pré-rempli {faites} fiche(s). Relisez le classement et les points à vérifier avant de valider."
                : "Med n'a pas pu lire l'infographie.";
            if (erreurs.Count > 0) Statut += " " + string.Join(" ", erreurs);
        }

        private void AppliquerProposition(InfographieCarte carte, InfographieAnalyseService.Proposition p)
        {
            var f = carte.Fiche;
            if (p.Titre.Length > 0) f.Titre = p.Titre;
            if (p.Type.Length > 0) f.Type = Orthographe(p.Type, TypesChoix);
            if (p.Public.Length > 0) f.Public = Orthographe(p.Public, PublicsChoix);
            if (p.Sujets.Count > 0)
                f.Sujets = p.Sujets.Select(s => Orthographe(s, SujetsFiltre))
                    .GroupBy(Normaliser).Select(g => g.First()).ToList();

            if (p.AVerifier.Count > 0)
            {
                // Relancer l'analyse remplace le relevé précédent, sans toucher aux notes du médecin
                var notes = f.Notes;
                int k = notes.IndexOf(EnteteAVerifier, StringComparison.Ordinal);
                if (k >= 0) notes = notes[..k].TrimEnd();
                var bloc = EnteteAVerifier + "\n" + string.Join("\n", p.AVerifier.Select(a => "• " + a));
                f.Notes = notes.Length == 0 ? bloc : notes + "\n\n" + bloc;
            }

            FicheModifiee(carte);
            carte.RafraichirTout();
        }

        /// <summary>Reprend l'orthographe d'une valeur déjà connue (« tdah » → « TDAH »).</summary>
        private static string Orthographe(string valeur, IEnumerable<string> connues)
            => connues.FirstOrDefault(c => c != Tous && Normaliser(c) == Normaliser(valeur)) ?? valeur.Trim();

        public void RemplacerImage(string chemin)
        {
            if (_selection == null) return;
            var (ok, err) = _service.RemplacerImage(_selection.Fiche, chemin);
            Statut = ok ? "Image remplacée. La fiche repasse « à relire »." : err ?? "";
            _selection.RafraichirTout(imageChangee: true);
            OnPropertyChanged(nameof(ImageSelection));
        }

        public void BasculerValidation()
        {
            if (_selection == null) return;
            var f = _selection.Fiche;
            f.DateValidation = f.EstValidee ? null : DateTime.Now;
            var (ok, err) = _service.Enregistrer(f);
            Statut = !ok ? err ?? ""
                : f.EstValidee ? $"Fiche validée le {f.DateValidation:dd/MM/yyyy}. Elle est proposée en consultation."
                : "Fiche repassée « à relire ». Elle n'est plus proposée en consultation.";
            _selection.RafraichirTout();
        }

        public void Supprimer()
        {
            if (_selection == null) return;
            var titre = _selection.Fiche.Titre;
            var (ok, err) = _service.Supprimer(_selection.Fiche);
            Selection = null; // par la propriété : l'aperçu et la fiche doivent se vider
            Charger();
            Statut = ok ? $"« {titre} » déplacée dans la corbeille de la bibliothèque." : err ?? "";
        }

        /// <summary>Appelé par une carte après modification d'un champ : enregistre et remet à jour les filtres.</summary>
        internal void FicheModifiee(InfographieCarte carte)
        {
            var (ok, err) = _service.Enregistrer(carte.Fiche);
            if (!ok) Statut = err ?? "";
            if (RafraichirListes()) AppliquerFiltres();
        }

        // ── Actions (consultation) ──────────────────────────────────────────

        public void Imprimer()
        {
            if (_selection == null) return;
            var f = _selection.Fiche;
            var (imprime, err) = InfographieImpression.Imprimer(f.ImagePath, f.Titre);
            if (err != null) { Statut = err; return; }
            if (!imprime) return;

            if (_patientDirectory != null)
            {
                var (ok, errRemise) = _service.EnregistrerRemise(_patientDirectory, f);
                if (ok) _remises[f.Id] = DateTime.Now;
                _selection.RafraichirTout();
                Statut = ok ? $"« {f.Titre} » imprimée et notée dans le dossier du patient." : errRemise ?? "";
            }
            else
            {
                Statut = $"« {f.Titre} » envoyée à l'imprimante.";
            }
        }

        internal DateTime? DateRemise(string id) => _remises.TryGetValue(id, out var d) ? d : null;

        // ── Utilitaires ─────────────────────────────────────────────────────

        private static IEnumerable<string> Distincts(IEnumerable<string> valeurs) => valeurs
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .GroupBy(v => Normaliser(v))
            .Select(g => g.First())
            .OrderBy(v => v, StringComparer.CurrentCultureIgnoreCase);

        /// <summary>
        /// Met la liste à jour sans la vider : une valeur sélectionnée qui reste dans la liste
        /// n'est jamais retirée, et le ComboBox garde sa sélection.
        /// </summary>
        private static void Remplir(ObservableCollection<string> cible, IEnumerable<string> valeurs)
        {
            var liste = valeurs.ToList();
            for (int i = cible.Count - 1; i >= 0; i--)
                if (!liste.Contains(cible[i])) cible.RemoveAt(i);
            for (int i = 0; i < liste.Count; i++)
            {
                if (i < cible.Count && cible[i] == liste[i]) continue;
                int j = cible.IndexOf(liste[i]);
                if (j >= 0) cible.Move(j, i);
                else cible.Insert(i, liste[i]);
            }
        }

        /// <summary>
        /// null = le ComboBox a perdu sa sélection (valeur retirée de la liste). On garde le filtre
        /// et on le lui renvoie après coup : une notification pendant son propre changement est ignorée.
        /// </summary>
        private void ChangerFiltre(ref string champ, string? valeur, [CallerMemberName] string? nom = null)
        {
            if (valeur == null)
            {
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() => OnPropertyChanged(nom)));
                return;
            }
            if (Set(ref champ, valeur, nom)) AppliquerFiltres();
        }

        private static bool Egal(string? a, string? b) => Normaliser(a) == Normaliser(b);

        /// <summary>Minuscules sans accents : « Ecole » trouve « École ».</summary>
        internal static string Normaliser(string? texte)
        {
            if (string.IsNullOrWhiteSpace(texte)) return "";
            var sb = new StringBuilder();
            foreach (var c in texte.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD))
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
            return sb.ToString();
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Une fiche telle qu'affichée : miniature, badges, champs modifiables.</summary>
    public class InfographieCarte : INotifyPropertyChanged
    {
        private readonly InfographiesViewModel _parent;
        private BitmapImage? _miniature;

        public event PropertyChangedEventHandler? PropertyChanged;

        public InfographieCarte(Infographie fiche, InfographiesViewModel parent)
        {
            Fiche = fiche;
            _parent = parent;
        }

        public Infographie Fiche { get; }

        public BitmapImage? Miniature => _miniature ??= InfographieImpression.Charger(Fiche.ImagePath, 320);

        public string Titre
        {
            get => Fiche.Titre;
            set { var v = (value ?? "").Trim(); if (v.Length == 0 || v == Fiche.Titre) return; Fiche.Titre = v; Modifiee(); }
        }

        public string Type
        {
            get => Fiche.Type;
            set { var v = (value ?? "").Trim(); if (v == Fiche.Type) return; Fiche.Type = v; Modifiee(); }
        }

        /// <summary>Sujets séparés par des virgules : « TDAH, Méthylphénidate ».</summary>
        public string SujetsTexte
        {
            get => string.Join(", ", Fiche.Sujets);
            set
            {
                var sujets = (value ?? "").Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim()).Where(s => s.Length > 0).Distinct(StringComparer.CurrentCultureIgnoreCase).ToList();
                if (sujets.SequenceEqual(Fiche.Sujets)) return;
                Fiche.Sujets = sujets;
                Modifiee();
            }
        }

        public string Public
        {
            get => Fiche.Public;
            set { var v = (value ?? "").Trim(); if (v == Fiche.Public) return; Fiche.Public = v; Modifiee(); }
        }

        public string Notes
        {
            get => Fiche.Notes;
            set { var v = value ?? ""; if (v == Fiche.Notes) return; Fiche.Notes = v; Modifiee(); }
        }

        public bool EstValidee => Fiche.EstValidee;

        public string StatutTexte => Fiche.EstValidee
            ? $"✓ Validée le {Fiche.DateValidation:dd/MM/yyyy}"
            : "À relire";

        public string BoutonValidation => Fiche.EstValidee ? "↺ Repasser à relire" : "✓ Valider la fiche";

        public string Resume
        {
            get
            {
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(Fiche.Type)) parts.Add(Fiche.Type);
                if (Fiche.Sujets.Count > 0) parts.Add(string.Join(", ", Fiche.Sujets));
                if (!string.IsNullOrWhiteSpace(Fiche.Public)) parts.Add(Fiche.Public);
                return parts.Count > 0 ? string.Join(" · ", parts) : "Non classée";
            }
        }

        public string RemiseTexte
        {
            get
            {
                var d = _parent.DateRemise(Fiche.Id);
                return d.HasValue ? $"Déjà remise le {d:dd/MM/yyyy}" : "";
            }
        }

        public bool DejaRemise => _parent.DateRemise(Fiche.Id).HasValue;

        internal string TexteRecherche => InfographiesViewModel.Normaliser(
            $"{Fiche.Titre} {Fiche.Type} {string.Join(" ", Fiche.Sujets)} {Fiche.Public} {Fiche.Notes}");

        private void Modifiee()
        {
            _parent.FicheModifiee(this);
            RafraichirTout();
        }

        public void RafraichirTout(bool imageChangee = false)
        {
            if (imageChangee) _miniature = null;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }
}
