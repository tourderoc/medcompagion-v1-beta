using System;
using System.IO;
using System.Windows;
using MedCompanion.Models;
using MedCompanion.Services.Consultation;
using Microsoft.Web.WebView2.Core;

namespace MedCompanion.Dialogs
{
    public partial class FormulaireSaisieDialog : Window
    {
        private readonly string? _pdfPath;
        private readonly string? _patientDir;
        private readonly FormulaireDataService _service = new();

        public FormulaireSaisieDialog(string? pdfPath, string? patientDir)
        {
            InitializeComponent();
            _pdfPath = pdfPath;
            _patientDir = patientDir;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Load existing data
            if (!string.IsNullOrEmpty(_patientDir))
                PopulateForm(_service.Load(_patientDir));

            // Init PDF viewer
            if (!string.IsNullOrEmpty(_pdfPath) && File.Exists(_pdfPath))
            {
                try
                {
                    var env = await CoreWebView2Environment.CreateAsync();
                    await PdfWebView.EnsureCoreWebView2Async(env);
                    PdfWebView.CoreWebView2.Navigate("file:///" + _pdfPath.Replace('\\', '/'));
                    PdfWebView.Visibility = Visibility.Visible;
                    PdfFallback.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    PdfFallback.Text = "Impossible d'afficher le PDF dans le visualiseur.";
                }
            }
            else
            {
                PdfFallback.Text = "Aucun PDF associé à ce document.";
            }
        }

        private void PopulateForm(FormulaireData d)
        {
            PerePrenom.Text  = d.PerePrenom;
            PereNom.Text     = d.PereNom;
            PereTel.Text     = d.PereTel;
            PereEmail.Text   = d.PereEmail;

            MerePrenom.Text  = d.MerePrenom;
            MereNom.Text     = d.MereNom;
            MereTel.Text     = d.MereTel;
            MereEmail.Text   = d.MereEmail;

            Adresse.Text    = d.Adresse;
            CodePostal.Text = d.CodePostal;
            Ville.Text      = d.Ville;
            SetRadio(d.GardeAdresse1, ("mere", GardeAdr1Mere), ("pere", GardeAdr1Pere), ("autre", GardeAdr1Autre));

            Adresse2.Text    = d.Adresse2;
            CodePostal2.Text = d.CodePostal2;
            Ville2.Text      = d.Ville2;
            SetRadio(d.GardeAdresse2, ("mere", GardeAdr2Mere), ("pere", GardeAdr2Pere), ("autre", GardeAdr2Autre));

            SetRadio(d.SituationFamiliale, ("ensemble", SitEnsemble), ("separes", SitSepares),
                ("divorces", SitDivorces), ("garde_alternee", SitGardeAlt),
                ("recomposee", SitRecomposee), ("autre", SitAutre));

            SetRadio(d.GardePrincipale, ("parents", GardeParents), ("mere", GardeMere),
                ("pere", GardePere), ("autre", GardeAutre));

            SetOuiNonNsp(d.Tdah,            TdahOui, TdahNon, TdahNsp);
            SetOuiNonNsp(d.Dyslexie,        DyslexieOui, DyslexieNon, DyslexieNsp);
            SetOuiNonNsp(d.Tsa,             TsaOui, TsaNon, TsaNsp);
            SetOuiNonNsp(d.TroublesAnxieux, AnxieuxOui, AnxieuxNon, AnxieuxNsp);
            SetOuiNonNsp(d.Depression,      DepOui, DepNon, DepNsp);
            SetOuiNonNsp(d.Bipolarite,      BipoOui, BipoNon, BipoNsp);
            SetOuiNonNsp(d.Addictions,      AddOui, AddNon, AddNsp);
            SetOuiNonNsp(d.TentativeSuicide, TsOui, TsNon, TsNsp);

            AntecedentsAutreLabel.Text = d.AntecedentsAutreLabel;
            SetOuiNonNsp(d.AntecedentsAutre, AutreAtcdOui, AutreAtcdNon, AutreAtcdNsp);

            SetOuiNon(d.AutorCommunicationEcole, Autor1Oui, Autor1Non);
            SetOuiNon(d.AutorPartageConfreres,   Autor2Oui, Autor2Non);
            SetOuiNon(d.AutorRechercheEtudes,    Autor3Oui, Autor3Non);
        }

        private FormulaireData CollectForm() => new()
        {
            PerePrenom  = PerePrenom.Text.Trim(),
            PereNom     = PereNom.Text.Trim(),
            PereTel     = PereTel.Text.Trim(),
            PereEmail   = PereEmail.Text.Trim(),

            MerePrenom  = MerePrenom.Text.Trim(),
            MereNom     = MereNom.Text.Trim(),
            MereTel     = MereTel.Text.Trim(),
            MereEmail   = MereEmail.Text.Trim(),

            Adresse       = Adresse.Text.Trim(),
            CodePostal    = CodePostal.Text.Trim(),
            Ville         = Ville.Text.Trim(),
            GardeAdresse1 = GetRadio(("mere", GardeAdr1Mere), ("pere", GardeAdr1Pere), ("autre", GardeAdr1Autre)),

            Adresse2      = Adresse2.Text.Trim(),
            CodePostal2   = CodePostal2.Text.Trim(),
            Ville2        = Ville2.Text.Trim(),
            GardeAdresse2 = GetRadio(("mere", GardeAdr2Mere), ("pere", GardeAdr2Pere), ("autre", GardeAdr2Autre)),

            SituationFamiliale = GetRadio(("ensemble", SitEnsemble), ("separes", SitSepares),
                ("divorces", SitDivorces), ("garde_alternee", SitGardeAlt),
                ("recomposee", SitRecomposee), ("autre", SitAutre)),

            GardePrincipale = GetRadio(("parents", GardeParents), ("mere", GardeMere),
                ("pere", GardePere), ("autre", GardeAutre)),

            Tdah             = GetOuiNonNsp(TdahOui, TdahNon, TdahNsp),
            Dyslexie         = GetOuiNonNsp(DyslexieOui, DyslexieNon, DyslexieNsp),
            Tsa              = GetOuiNonNsp(TsaOui, TsaNon, TsaNsp),
            TroublesAnxieux  = GetOuiNonNsp(AnxieuxOui, AnxieuxNon, AnxieuxNsp),
            Depression       = GetOuiNonNsp(DepOui, DepNon, DepNsp),
            Bipolarite       = GetOuiNonNsp(BipoOui, BipoNon, BipoNsp),
            Addictions       = GetOuiNonNsp(AddOui, AddNon, AddNsp),
            TentativeSuicide       = GetOuiNonNsp(TsOui, TsNon, TsNsp),
            AntecedentsAutreLabel  = AntecedentsAutreLabel.Text.Trim(),
            AntecedentsAutre       = GetOuiNonNsp(AutreAtcdOui, AutreAtcdNon, AutreAtcdNsp),

            AutorCommunicationEcole = GetOuiNon(Autor1Oui, Autor1Non),
            AutorPartageConfreres   = GetOuiNon(Autor2Oui, Autor2Non),
            AutorRechercheEtudes    = GetOuiNon(Autor3Oui, Autor3Non),

            LinkedDocumentPath = _pdfPath,
        };

        private void BtnValider_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_patientDir)) { Close(); return; }
            var data = CollectForm();
            _service.Save(_patientDir, data);
            StatusLabel.Text = "Enregistré ✓";
            DialogResult = true;
            Close();
        }

        private void BtnAnnuler_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ── Helpers ──────────────────────────────────────────────

        private static void SetRadio(string value, params (string key, System.Windows.Controls.RadioButton rb)[] pairs)
        {
            foreach (var (key, rb) in pairs)
                rb.IsChecked = key == value;
        }

        private static string GetRadio(params (string key, System.Windows.Controls.RadioButton rb)[] pairs)
        {
            foreach (var (key, rb) in pairs)
                if (rb.IsChecked == true) return key;
            return "";
        }

        private static void SetOuiNonNsp(string value,
            System.Windows.Controls.RadioButton oui,
            System.Windows.Controls.RadioButton non,
            System.Windows.Controls.RadioButton nsp)
        {
            oui.IsChecked = value == "oui";
            non.IsChecked = value == "non";
            nsp.IsChecked = value == "nsp";
        }

        private static string GetOuiNonNsp(
            System.Windows.Controls.RadioButton oui,
            System.Windows.Controls.RadioButton non,
            System.Windows.Controls.RadioButton nsp)
            => oui.IsChecked == true ? "oui" : non.IsChecked == true ? "non" : nsp.IsChecked == true ? "nsp" : "";

        private static void SetOuiNon(string value,
            System.Windows.Controls.RadioButton oui,
            System.Windows.Controls.RadioButton non)
        {
            oui.IsChecked = value == "oui";
            non.IsChecked = value == "non";
        }

        private static string GetOuiNon(
            System.Windows.Controls.RadioButton oui,
            System.Windows.Controls.RadioButton non)
            => oui.IsChecked == true ? "oui" : non.IsChecked == true ? "non" : "";
    }
}
