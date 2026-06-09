using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MedCompanion.Services.Restitutions;

namespace MedCompanion.Views.Consultation
{
    public partial class ContextCompletionWindow : Window
    {
        public PatientContextDetails CompletedDetails { get; private set; }
        public bool IsSaved { get; private set; } = false;

        public ContextCompletionWindow(PatientContextDetails prefilledDetails)
        {
            InitializeComponent();
            CompletedDetails = prefilledDetails ?? new PatientContextDetails();
            PopulateFields();
            SetupWatermarks();
        }

        private void PopulateFields()
        {
            TxtEcole.Text = CompletedDetails.Ecole ?? "";
            TxtClasse.Text = CompletedDetails.Classe ?? "";
            TxtMereNom.Text = CompletedDetails.MereNom ?? "";
            TxtMereAge.Text = CompletedDetails.MereAge ?? "";
            TxtMereJob.Text = CompletedDetails.MereJob ?? "";
            TxtPereNom.Text = CompletedDetails.PereNom ?? "";
            TxtPereAge.Text = CompletedDetails.PereAge ?? "";
            TxtPereJob.Text = CompletedDetails.PereJob ?? "";
            TxtFratrie.Text = CompletedDetails.Fratrie ?? "";
            TxtMarche.Text = CompletedDetails.MarcheAge ?? "";
            TxtLangage.Text = CompletedDetails.LangageAcq ?? "";
            TxtProprete.Text = CompletedDetails.PropreteAcq ?? "";
        }

        private void SetupWatermarks()
        {
            // Ajouter un comportement de placeholder simple par code pour les champs Tagués
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

        private void BtnIgnore_Click(object sender, RoutedEventArgs e)
        {
            IsSaved = false;
            DialogResult = false;
            Close();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            CompletedDetails = new PatientContextDetails
            {
                Ecole = TxtEcole.Text.Trim(),
                Classe = TxtClasse.Text.Trim(),
                MereNom = GetCleanText(TxtMereNom, "Prénom"),
                MereAge = GetCleanText(TxtMereAge, "Âge"),
                MereJob = GetCleanText(TxtMereJob, "Profession"),
                PereNom = GetCleanText(TxtPereNom, "Prénom"),
                PereAge = GetCleanText(TxtPereAge, "Âge"),
                PereJob = GetCleanText(TxtPereJob, "Profession"),
                Fratrie = TxtFratrie.Text.Trim(),
                MarcheAge = TxtMarche.Text.Trim(),
                LangageAcq = TxtLangage.Text.Trim(),
                PropreteAcq = TxtProprete.Text.Trim()
            };

            IsSaved = true;
            DialogResult = true;
            Close();
        }
    }
}
