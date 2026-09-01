using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using MedCompanion.Models.Evaluations;
using MedCompanion.Services.LLM;
using MedCompanion.ViewModels;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Fenêtre de dépouillement du questionnaire parent, ouverte APRÈS la séance.
    ///
    /// Elle est la contrepartie de la règle « rien ne s'affiche pendant la séance » : comme le
    /// résultat n'est jamais montré à la famille au moment du scan, le contrôle est déplacé ici.
    /// Sans elle, archiver l'image ne servirait à rien — il n'y aurait aucun moyen de corriger
    /// une lecture fausse.
    ///
    /// La lecture automatique des cases se branchera sur <see cref="CartographieSaisieViewModel.Prefill"/> :
    /// elle pré-remplira les 30 réponses, que le médecin vérifiera sur l'image affichée à gauche.
    /// La fenêtre est donc utile avant même que la détection existe.
    /// </summary>
    public partial class CartographieSaisieDialog : Window
    {
        private readonly CartographieSaisieViewModel _vm;

        /// <summary>Les 5 scores 0-6, disponibles après un enregistrement (DialogResult == true).</summary>
        public Dictionary<string, int> Scores { get; private set; } = new();

        public CartographieSaisieDialog(string? imagePath, BandeAgeCarto bande,
                                        IReadOnlyDictionary<string, int>? scoresExistants = null)
        {
            InitializeComponent();
            _vm = new CartographieSaisieViewModel(imagePath, bande, scoresExistants);
            DataContext = _vm;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            PopulateVisionModels();
            await AfficherFeuilleAsync();
        }

        /// <summary>
        /// Affiche la feuille. Un PDF passe par le visualiseur d'Edge — un contrôle Image WPF ne
        /// sait pas rendre un PDF et laissait le volet blanc. Une image s'affiche directement.
        /// </summary>
        private async System.Threading.Tasks.Task AfficherFeuilleAsync()
        {
            var chemin = _vm.ImagePath;
            if (string.IsNullOrEmpty(chemin) || !File.Exists(chemin))
            {
                PdfFallback.Text = "Aucune feuille scannée associée à cette séance.";
                return;
            }

            if (chemin.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var env = await CoreWebView2Environment.CreateAsync();
                    await PdfWebView.EnsureCoreWebView2Async(env);
                    // #zoom=page-width : sans consigne, le visualiseur reprend le dernier zoom
                    // retenu et peut afficher une vignette perdue au milieu du volet. C'est de
                    // l'écriture manuscrite qu'on relit ici.
                    PdfWebView.CoreWebView2.Navigate(
                        "file:///" + chemin.Replace('\\', '/') + "#zoom=page-width");
                    PdfWebView.Visibility  = Visibility.Visible;
                    PdfFallback.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    PdfFallback.Text = "Impossible d'afficher le PDF dans le visualiseur.";
                }
                return;
            }

            try
            {
                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption  = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource    = new Uri(chemin);
                bmp.EndInit();
                ScanImage.Source     = bmp;
                ScanImage.Visibility = Visibility.Visible;
                PdfFallback.Visibility = Visibility.Collapsed;
            }
            catch
            {
                PdfFallback.Text = "Image de la feuille illisible.";
            }
        }

        private bool _suspendVisionChange;

        /// <summary>
        /// Alimente le sélecteur de modèle de lecture. Mêmes profils que le formulaire — seuls
        /// ceux réellement installés sont listés : proposer un modèle absent ne produirait qu'un
        /// échec au moment de lire.
        /// </summary>
        private void PopulateVisionModels()
        {
            _suspendVisionChange = true;
            try
            {
                VisionModelCombo.Items.Clear();
                var current = LlamaCppProfiles.VisionProfile;

                foreach (var profile in LlamaCppProfiles.VisionCapable)
                {
                    var item = new System.Windows.Controls.ComboBoxItem
                    {
                        Content = profile.ShortName,
                        Tag     = profile
                    };
                    VisionModelCombo.Items.Add(item);
                    if (profile.Id == current.Id) VisionModelCombo.SelectedItem = item;
                }

                VisionModelCombo.Visibility = VisionModelCombo.Items.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                if (VisionModelCombo.SelectedItem == null && VisionModelCombo.Items.Count > 0)
                    VisionModelCombo.SelectedIndex = 0;
            }
            finally { _suspendVisionChange = false; }
        }

        private void VisionModelCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_suspendVisionChange) return;
            if (VisionModelCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
            if (item.Tag is not LlamaCppModelProfile profile) return;
            if (profile.Id == LlamaCppProfiles.VisionProfile.Id) return;

            LlamaCppProfiles.SetVisionProfile(profile);
            PiedTb.Text = $"Lecture : {profile.ShortName} (appliqué à la prochaine lecture).";
        }

        private void Enregistrer_Click(object sender, RoutedEventArgs e)
        {
            // Un axe partiellement rempli sous-estime mécaniquement l'enfant : son score compte
            // les « oui » sur six items dont certains n'ont pas de réponse. On le signale avant
            // d'enregistrer plutôt que de laisser un score creux devenir une couleur.
            var incomplets = new List<string>();
            foreach (var axe in _vm.Axes)
                if (axe.NbRepondus > 0 && !axe.EstComplet)
                    incomplets.Add($"{axe.Label} ({axe.NbRepondus}/6)");

            if (incomplets.Count > 0)
            {
                var r = MessageBox.Show(
                    "Ces axes sont incomplets — leur score sous-estimera l'enfant :\n\n  • "
                    + string.Join("\n  • ", incomplets)
                    + "\n\nEnregistrer quand même ?",
                    "Axes incomplets",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (r != MessageBoxResult.Yes) return;
            }

            Scores = _vm.ToScores();
            DialogResult = true;
            Close();
        }

        private void Annuler_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// Lecture automatique : découpe la feuille bloc par bloc et fait lire les cases par le
        /// modèle vision — la méthode du formulaire de complétion.
        ///
        /// Le résultat PRÉ-REMPLIT la saisie, il ne la remplace pas. Rien n'est enregistré tant
        /// que le médecin n'a pas vérifié : c'est le sens même de cette fenêtre, puisque rien
        /// n'est montré au moment du scan.
        /// </summary>
        private async void Lire_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_vm.ImagePath) || !System.IO.File.Exists(_vm.ImagePath))
            {
                MessageBox.Show("Aucune feuille scannée à lire.",
                    "Lecture automatique", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var service = new MedCompanion.Services.Evaluations.CartographieLectureService();
            if (!service.EstDisponible)
            {
                MessageBox.Show(
                    "Lecture indisponible : Microsoft Edge ou le gabarit du questionnaire est introuvable.",
                    "Lecture automatique", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            LireBtn.IsEnabled = false;
            PiedTb.Text = "⏳ Lecture des cases en cours — un bloc à la fois…";

            try
            {
                var (ok, lecture, message) = await service.LireAsync(_vm.ImagePath!);

                if (!ok)
                {
                    PiedTb.Text = $"❌ {message} — saisissez les réponses à la main.";
                    return;
                }

                _vm.Prefill(lecture);

                var lus = lecture.Count;
                PiedTb.Text = message == null
                    ? $"⚡ {lus} axes lus — VÉRIFIEZ chaque ligne sur l'image avant d'enregistrer."
                    : $"⚡ {lus} axes lus. {message}. Vérifiez chaque ligne sur l'image.";
            }
            catch (System.Exception ex)
            {
                PiedTb.Text = $"❌ Lecture impossible : {ex.Message}";
            }
            finally
            {
                LireBtn.IsEnabled = true;
            }
        }
    }
}
