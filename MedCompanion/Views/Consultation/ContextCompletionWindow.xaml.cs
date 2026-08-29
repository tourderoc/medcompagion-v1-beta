using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MedCompanion.Services;
using MedCompanion.Services.Restitutions;

namespace MedCompanion.Views.Consultation
{
    public partial class ContextCompletionWindow : Window
    {
        public PatientContextDetails CompletedDetails { get; private set; }
        public bool IsSaved { get; private set; } = false;

        private readonly EcoleAnnuaireService _ecoleService = new();      // repli réseau
        private readonly EcoleLocaleService    _ecoleLocale = new();      // socle hors ligne
        private readonly EcoleSurcoucheService _surcouche   = new();      // alias + corrections
        private EcoleAnnuaireResult? _selectedEcole;

        /// <summary>
        /// Saisie du médecin au moment de la recherche, conservée pour pouvoir l'enregistrer comme
        /// nom d'usage si elle ne correspondait pas au nom officiel de l'établissement retenu.
        /// </summary>
        private string _saisieRecherche = "";

        public ContextCompletionWindow(PatientContextDetails prefilledDetails)
        {
            InitializeComponent();
            CompletedDetails = prefilledDetails ?? new PatientContextDetails();
            ConfigureSections();
            PopulateFields();
            SetupWatermarks();
            RafraichirEtatAnnuaire();
        }

        private void ConfigureSections()
        {
            var d = CompletedDetails;

            // Section âge : visible si DDN absente OU discordance
            bool showAge = d.NeedsDobEntry || d.HasAgeDiscrepancy;
            AgeSectionBorder.Visibility = showAge ? Visibility.Visible : Visibility.Collapsed;

            // Sections contexte complet : uniquement pour 3-11 ans
            FullContextPanel.Visibility = d.ShowFullContext ? Visibility.Visible : Visibility.Collapsed;
        }

        private void PopulateFields()
        {
            var d = CompletedDetails;

            // Section âge
            if (d.AgeCalcule.HasValue)
                TxtAgeCalculeDisplay.Text = $"{d.AgeCalcule} ans";
            else
                TxtAgeCalculeDisplay.Text = "Non calculé (DDN absente)";

            if (d.AgeInterrogatoire.HasValue)
                TxtAgeInterrDisplay.Text = $"{d.AgeInterrogatoire} ans";
            else
                TxtAgeInterrDisplay.Text = "—";

            if (!string.IsNullOrEmpty(d.DateNaissanceActuelle))
            {
                // Afficher en dd/MM/yyyy
                if (DateTime.TryParse(d.DateNaissanceActuelle, out var dob))
                    TxtDobActuelleInfo.Text = $"DDN actuellement enregistrée : {dob:dd/MM/yyyy}";
                else
                    TxtDobActuelleInfo.Text = $"DDN actuellement enregistrée : {d.DateNaissanceActuelle}";
                // Pré-remplir le champ de correction avec la valeur actuelle
                TxtDobCorrigee.Text = TxtDobActuelleInfo.Text.Replace("DDN actuellement enregistrée : ", "");
            }
            else
            {
                TxtDobActuelleInfo.Text = "Aucune date de naissance dans le dossier.";
                TxtDobCorrigee.Text = "";
            }

            // Sections contexte complet
            if (d.ShowFullContext)
            {
                TxtEcole.Text = d.Ecole ?? "";
                TxtEcoleLieu.Text = d.EcoleLieu ?? "";
                TxtClasse.Text = d.Classe ?? "";

                // Coordonnées école déjà connues → préremplir et afficher le bloc
                TxtEcoleAdresse.Text = d.EcoleAdresse ?? "";
                TxtEcoleTel.Text     = d.EcoleTelephone ?? "";
                TxtEcoleEmail.Text   = d.EcoleEmail ?? "";
                if (!string.IsNullOrWhiteSpace(d.EcoleAdresse) ||
                    !string.IsNullOrWhiteSpace(d.EcoleTelephone) ||
                    !string.IsNullOrWhiteSpace(d.EcoleEmail))
                {
                    PanelCoordonnees.Visibility = Visibility.Visible;
                }
                TxtMereNom.Text = d.MereNom ?? "";
                TxtMereAge.Text = d.MereAge ?? "";
                TxtMereJob.Text = d.MereJob ?? "";
                TxtPereNom.Text = d.PereNom ?? "";
                TxtPereAge.Text = d.PereAge ?? "";
                TxtPereJob.Text = d.PereJob ?? "";
                TxtFratrie.Text = d.Fratrie ?? "";
                TxtMarche.Text = d.MarcheAge ?? "";
                TxtLangage.Text = d.LangageAcq ?? "";
                TxtProprete.Text = d.PropreteAcq ?? "";
            }
        }

        private void SetupWatermarks()
        {
            if (!CompletedDetails.ShowFullContext) return;

            AddWatermark(TxtMereNom, "Prénom");
            AddWatermark(TxtMereAge, "Âge");
            AddWatermark(TxtMereJob, "Profession");
            AddWatermark(TxtPereNom, "Prénom");
            AddWatermark(TxtPereAge, "Âge");
            AddWatermark(TxtPereJob, "Profession");
        }

        private void AddWatermark(TextBox textBox, string watermarkText)
        {
            if (string.IsNullOrWhiteSpace(textBox.Text))
            {
                textBox.Text = watermarkText;
                textBox.Foreground = Brushes.LightGray;
            }

            textBox.GotFocus += (s, e) =>
            {
                if (textBox.Text == watermarkText && textBox.Foreground == Brushes.LightGray)
                {
                    textBox.Text = "";
                    textBox.Foreground = Brushes.Black;
                }
            };

            textBox.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(textBox.Text))
                {
                    textBox.Text = watermarkText;
                    textBox.Foreground = Brushes.LightGray;
                }
            };
        }

        private string GetCleanText(TextBox textBox, string watermark)
        {
            var text = textBox.Text.Trim();
            if (text == watermark && textBox.Foreground == Brushes.LightGray)
                return "";
            return text;
        }

        // ── Recherche de l'école dans l'Annuaire Éducation Nationale ─────────────

        private async void BtnRechercheEcole_Click(object sender, RoutedEventArgs e)
        {
            var nom    = TxtEcole.Text.Trim();
            var commune = TxtEcoleLieu.Text.Trim();

            if (string.IsNullOrWhiteSpace(nom))
            {
                ShowRechercheStatut("Renseignez d'abord le nom de l'école.", isError: true);
                return;
            }

            BtnRechercheEcole.IsEnabled = false;
            PanelResultats.Visibility = Visibility.Collapsed;
            _saisieRecherche = nom;

            try
            {
                // 1. Nom d'usage déjà appris — court-circuite tout le reste.
                var uaiConnu = _surcouche.ResoudreAlias(nom);
                if (uaiConnu != null)
                {
                    var connue = _ecoleLocale.TrouverParUai(uaiConnu);
                    if (connue != null)
                    {
                        FillCoordonnees(_surcouche.Appliquer(connue));
                        ShowRechercheStatut("✓ École reconnue (nom d'usage enregistré).", isError: false);
                        return;
                    }
                }

                // 2. Socle local, tolérant aux fautes sur le nom ET sur la ville.
                if (_ecoleLocale.AssurerCharge())
                {
                    var locaux = _ecoleLocale.Rechercher(nom, commune);
                    if (locaux.Count > 0)
                    {
                        AfficherResultats(locaux, SuffixeFraicheur());
                        return;
                    }
                }

                // 3. Repli réseau : annuaire jamais téléchargé, ou établissement absent du socle
                //    (annuaire local vieilli, école récente).
                ShowRechercheStatut(_ecoleLocale.EstDisponible
                    ? "Rien en local — recherche en ligne..."
                    : "Annuaire local absent — recherche en ligne...", isError: false);

                var (ok, results, error) = await _ecoleService.SearchAsync(nom, commune);

                // Le filtre commune de l'API distante est littéral : une faute de frappe sur la
                // ville y ramène zéro résultat alors que l'école existe. On relance sans elle
                // plutôt que d'annoncer un échec.
                if (ok && results.Count == 0 && !string.IsNullOrWhiteSpace(commune))
                {
                    var (ok2, results2, _) = await _ecoleService.SearchAsync(nom, null);
                    if (ok2 && results2.Count > 0)
                    {
                        AfficherResultats(results2, " — ville ignorée, vérifiez le code postal");
                        return;
                    }
                }

                if (!ok)
                {
                    ShowRechercheStatut(error ?? "Recherche impossible.", isError: true);
                    return;
                }

                if (results.Count == 0)
                {
                    ShowRechercheStatut("Aucun établissement trouvé. Vérifiez le nom ou saisissez les coordonnées manuellement.", isError: true);
                    return;
                }

                AfficherResultats(results, "");
            }
            catch (Exception ex)
            {
                ShowRechercheStatut($"Erreur : {ex.Message}", isError: true);
            }
            finally
            {
                BtnRechercheEcole.IsEnabled = true;
            }
        }

        /// <summary>
        /// Présente les résultats. Même avec un seul, la liste reste visible : la recherche étant
        /// désormais approximative, un résultat unique n'est plus une certitude — le médecin doit
        /// pouvoir voir sur quoi il est tombé avant de valider les coordonnées.
        /// </summary>
        private void AfficherResultats(System.Collections.Generic.List<EcoleAnnuaireResult> resultats, string suffixe)
        {
            // La liste reste en fiches OFFICIELLES ; les corrections ne sont appliquées qu'à
            // l'affichage des coordonnées (FillCoordonnees), pour garder de quoi comparer au moment
            // d'enregistrer ce que le médecin a réellement changé.
            FillCoordonnees(resultats[0]);

            if (resultats.Count > 1)
            {
                CmbEcoleResultats.ItemsSource   = resultats;
                CmbEcoleResultats.SelectedIndex = 0;
                PanelResultats.Visibility       = Visibility.Visible;
                ShowRechercheStatut($"{resultats.Count} établissements possibles — vérifiez la sélection{suffixe}.", isError: false);
            }
            else
            {
                ShowRechercheStatut($"✓ Établissement trouvé — vérifiez les coordonnées{suffixe}.", isError: false);
            }
        }

        // ── Annuaire local : état et actualisation ──────────────────────────────

        /// <summary>
        /// Affiche l'état de la copie locale. Appelé à l'ouverture et après chaque téléchargement.
        /// </summary>
        private void RafraichirEtatAnnuaire()
        {
            var date = _ecoleLocale.DateTelechargement;

            if (date == null)
            {
                TxtAnnuaireEtat.Text = "Annuaire local absent — les recherches passent par internet.";
                return;
            }

            var jours = (int)(DateTime.Now - date.Value).TotalDays;
            var perso = _surcouche.NombreAlias + _surcouche.NombreCorrections;
            var suffixePerso = perso > 0 ? $" · {perso} entrée(s) personnelle(s)" : "";

            TxtAnnuaireEtat.Text = jours > 180
                ? $"Annuaire local du {date:dd/MM/yyyy} — {jours} jours, une actualisation serait utile{suffixePerso}"
                : $"Annuaire local du {date:dd/MM/yyyy}{suffixePerso}";
        }

        private async void BtnTelechargerAnnuaire_Click(object sender, RoutedEventArgs e)
        {
            BtnTelechargerAnnuaire.IsEnabled = false;
            BtnRechercheEcole.IsEnabled = false;

            try
            {
                TxtAnnuaireEtat.Text = "Téléchargement de l'annuaire (~25 Mo)...";

                var (ok, nombre, erreur) = await _ecoleLocale.TelechargerAsync(octets =>
                {
                    // Le téléchargement se fait hors du thread UI : repasser par le Dispatcher.
                    Dispatcher.InvokeAsync(() =>
                        TxtAnnuaireEtat.Text = $"Téléchargement... {octets / 1024 / 1024} Mo");
                });

                if (!ok)
                {
                    TxtAnnuaireEtat.Text = erreur ?? "Téléchargement impossible.";
                    return;
                }

                // Les alias et corrections vivent dans un fichier distinct : remplacer l'annuaire
                // ne les touche pas, ils se réappliquent sur les fiches fraîches.
                RafraichirEtatAnnuaire();
                ShowRechercheStatut($"✓ Annuaire local à jour — {nombre:N0} établissements, recherche hors ligne.", isError: false);
            }
            catch (Exception ex)
            {
                TxtAnnuaireEtat.Text = $"Échec : {ex.Message}";
            }
            finally
            {
                BtnTelechargerAnnuaire.IsEnabled = true;
                BtnRechercheEcole.IsEnabled = true;
            }
        }

        /// <summary>Âge de la copie locale, à afficher : elle vieillit en silence.</summary>
        private string SuffixeFraicheur()
        {
            var date = _ecoleLocale.DateTelechargement;
            if (date == null) return "";

            var jours = (int)(DateTime.Now - date.Value).TotalDays;
            return jours > 180
                ? $" — annuaire local du {date:dd/MM/yyyy}, pensez à l'actualiser"
                : $" — annuaire local du {date:dd/MM/yyyy}";
        }

        private void CmbEcoleResultats_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbEcoleResultats.SelectedItem is EcoleAnnuaireResult result)
                FillCoordonnees(result);
        }

        private void FillCoordonnees(EcoleAnnuaireResult r)
        {
            // On retient la fiche OFFICIELLE : c'est elle qui sert de référence pour distinguer,
            // à l'enregistrement, ce que le médecin a corrigé de ce qui venait de l'annuaire.
            _selectedEcole = r;

            // ... mais on affiche la version corrigée, s'il a déjà rectifié cet établissement.
            var affiche = _surcouche.Appliquer(r);

            // Aligner nom/commune sur les valeurs officielles
            if (!string.IsNullOrWhiteSpace(affiche.Nom))     TxtEcole.Text     = affiche.Nom;
            if (!string.IsNullOrWhiteSpace(affiche.Commune)) TxtEcoleLieu.Text = affiche.Commune;

            TxtEcoleAdresse.Text = affiche.Adresse;
            TxtEcoleTel.Text     = affiche.Telephone;
            TxtEcoleEmail.Text   = affiche.Email;
            PanelCoordonnees.Visibility = Visibility.Visible;
        }

        private void ShowRechercheStatut(string message, bool isError)
        {
            TxtRechercheStatut.Text = message;
            TxtRechercheStatut.Foreground = isError ? Brushes.IndianRed : new SolidColorBrush(Color.FromRgb(0x7F, 0x8C, 0x8D));
            TxtRechercheStatut.Visibility = Visibility.Visible;
        }

        private void BtnIgnore_Click(object sender, RoutedEventArgs e)
        {
            IsSaved = false;
            DialogResult = false;
            Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            var d = CompletedDetails;

            // DDN corrigée (toujours collectée si la section est visible)
            string? dobCorrigee = null;
            if (AgeSectionBorder.Visibility == Visibility.Visible)
                dobCorrigee = TxtDobCorrigee.Text.Trim();

            CompletedDetails = new PatientContextDetails
            {
                // Préserver les flags
                ShowFullContext    = d.ShowFullContext,
                AgeCalcule         = d.AgeCalcule,
                AgeInterrogatoire  = d.AgeInterrogatoire,
                DateNaissanceActuelle = d.DateNaissanceActuelle,
                HasAgeDiscrepancy  = d.HasAgeDiscrepancy,
                NeedsDobEntry      = d.NeedsDobEntry,

                // DDN corrigée par le médecin
                DateNaissanceCorrigee = string.IsNullOrWhiteSpace(dobCorrigee) ? null : dobCorrigee,

                // Contexte complet (3-11 ans)
                Ecole     = d.ShowFullContext ? TxtEcole.Text.Trim() : null,
                EcoleLieu = d.ShowFullContext ? TxtEcoleLieu.Text.Trim() : null,
                Classe    = d.ShowFullContext ? TxtClasse.Text.Trim() : null,

                // Coordonnées école (annuaire EN ou saisie manuelle)
                EcoleAdresse    = d.ShowFullContext ? TxtEcoleAdresse.Text.Trim() : null,
                EcoleTelephone  = d.ShowFullContext ? TxtEcoleTel.Text.Trim()     : null,
                EcoleEmail      = d.ShowFullContext ? TxtEcoleEmail.Text.Trim()   : null,
                EcoleCodePostal = d.ShowFullContext ? (_selectedEcole?.CodePostal ?? "") : null,
                EcoleUai        = d.ShowFullContext ? (_selectedEcole?.Uai ?? "")        : null,
                MereNom  = d.ShowFullContext ? GetCleanText(TxtMereNom, "Prénom") : null,
                MereAge  = d.ShowFullContext ? GetCleanText(TxtMereAge, "Âge") : null,
                MereJob  = d.ShowFullContext ? GetCleanText(TxtMereJob, "Profession") : null,
                PereNom  = d.ShowFullContext ? GetCleanText(TxtPereNom, "Prénom") : null,
                PereAge  = d.ShowFullContext ? GetCleanText(TxtPereAge, "Âge") : null,
                PereJob  = d.ShowFullContext ? GetCleanText(TxtPereJob, "Profession") : null,
                Fratrie  = d.ShowFullContext ? TxtFratrie.Text.Trim() : null,
                MarcheAge  = d.ShowFullContext ? TxtMarche.Text.Trim() : null,
                LangageAcq = d.ShowFullContext ? TxtLangage.Text.Trim() : null,
                PropreteAcq = d.ShowFullContext ? TxtProprete.Text.Trim() : null,
            };

            ApprendreDeLaSaisie();

            IsSaved = true;
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Retient ce que cette consultation vient d'apprendre sur l'école — la seule source
        /// possible pour ces deux informations, absentes de l'annuaire officiel :
        ///  • la façon dont le médecin nomme l'établissement, quand elle diffère du nom officiel ;
        ///  • les coordonnées qu'il a rectifiées à la main.
        /// Silencieux : rien à valider, l'apprentissage se fait en enregistrant la consultation.
        /// </summary>
        private void ApprendreDeLaSaisie()
        {
            if (_selectedEcole == null || string.IsNullOrWhiteSpace(_selectedEcole.Uai)) return;

            try
            {
                // Nom d'usage : uniquement si la saisie ne retombe pas déjà sur l'officiel, sinon on
                // encombrerait la table d'alias inutiles à chaque recherche réussie.
                var saisieN   = EcoleLocaleService.Normaliser(_saisieRecherche);
                var officielN = EcoleLocaleService.Normaliser(_selectedEcole.Nom);
                if (saisieN.Length > 2 && !officielN.Contains(saisieN, StringComparison.Ordinal))
                    _surcouche.EnregistrerAlias(_saisieRecherche, _selectedEcole.Uai);

                _surcouche.EnregistrerCorrections(
                    _selectedEcole,
                    TxtEcoleEmail.Text.Trim(),
                    TxtEcoleTel.Text.Trim(),
                    TxtEcoleAdresse.Text.Trim(),
                    null);
            }
            catch { /* l'apprentissage est un bonus : il ne doit jamais empêcher l'enregistrement */ }
        }
    }
}
