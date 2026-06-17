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

        private readonly EcoleAnnuaireService _ecoleService = new();
        private EcoleAnnuaireResult? _selectedEcole;

        public ContextCompletionWindow(PatientContextDetails prefilledDetails)
        {
            InitializeComponent();
            CompletedDetails = prefilledDetails ?? new PatientContextDetails();
            ConfigureSections();
            PopulateFields();
            SetupWatermarks();
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
            ShowRechercheStatut("Recherche dans l'annuaire officiel...", isError: false);
            PanelResultats.Visibility = Visibility.Collapsed;

            try
            {
                var (ok, results, error) = await _ecoleService.SearchAsync(nom, commune);

                if (!ok)
                {
                    ShowRechercheStatut(error ?? "Recherche impossible.", isError: true);
                    return;
                }

                if (results.Count == 0)
                {
                    ShowRechercheStatut("Aucun établissement trouvé. Vérifiez le nom/la ville ou saisissez les coordonnées manuellement.", isError: true);
                    return;
                }

                if (results.Count == 1)
                {
                    FillCoordonnees(results[0]);
                    ShowRechercheStatut("✓ Établissement trouvé — vérifiez les coordonnées ci-dessous.", isError: false);
                    return;
                }

                // Plusieurs résultats → laisser le médecin choisir
                CmbEcoleResultats.ItemsSource = results;
                PanelResultats.Visibility = Visibility.Visible;
                ShowRechercheStatut($"{results.Count} établissements trouvés — sélectionnez le bon.", isError: false);
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

        private void CmbEcoleResultats_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbEcoleResultats.SelectedItem is EcoleAnnuaireResult result)
                FillCoordonnees(result);
        }

        private void FillCoordonnees(EcoleAnnuaireResult r)
        {
            _selectedEcole = r;

            // Aligner nom/commune sur les valeurs officielles
            if (!string.IsNullOrWhiteSpace(r.Nom))     TxtEcole.Text     = r.Nom;
            if (!string.IsNullOrWhiteSpace(r.Commune)) TxtEcoleLieu.Text = r.Commune;

            TxtEcoleAdresse.Text = r.Adresse;
            TxtEcoleTel.Text     = r.Telephone;
            TxtEcoleEmail.Text   = r.Email;
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

            IsSaved = true;
            DialogResult = true;
            Close();
        }
    }
}
