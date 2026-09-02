using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using MedCompanion.Services.LLM;
using MedCompanion.ViewModels;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Dépouillement de la feuille « Cartographie de l'environnement », ouvert APRÈS la séance.
    ///
    /// Contrepartie de la règle « rien ne s'affiche pendant la séance » : comme le résultat n'est
    /// jamais montré à la famille au moment du scan, le contrôle est déplacé ici. Sans cette
    /// fenêtre, archiver l'image ne servirait à rien — aucun moyen de corriger une lecture fausse.
    ///
    /// Jumelle de <see cref="CartographieSaisieDialog"/>, mais séparée plutôt que factorisée : le
    /// volet de droite n'a ni score ni couleur, parce qu'une feuille d'environnement se lit sur ses
    /// deux moitiés et que celle du médecin n'est pas ici. Fondre les deux fenêtres aurait obligé à
    /// masquer ce qui n'a pas de sens dans l'une ou dans l'autre.
    /// </summary>
    public partial class EnvSaisieDialog : Window
    {
        private readonly EnvSaisieViewModel _vm;

        /// <summary>Les 22 réponses du parent, par clé de feuille — « oui », « non », ou vide.</summary>
        public Dictionary<string, string[]> Reponses { get; private set; } = new();

        /// <summary>Qui a rempli la feuille : « mere », « pere », « autre », ou null.</summary>
        public string? Informateur    { get; private set; }
        public string? InformateurNom { get; private set; }

        public EnvSaisieDialog(string? imagePath,
                               IReadOnlyDictionary<string, string[]>? reponsesExistantes = null,
                               string? informateur = null, string? informateurNom = null)
        {
            InitializeComponent();
            _vm = new EnvSaisieViewModel(imagePath, reponsesExistantes);
            _vm.PrefillInformateur(informateur, informateurNom);
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
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource   = new Uri(chemin);
                bmp.EndInit();
                ScanImage.Source       = bmp;
                ScanImage.Visibility   = Visibility.Visible;
                PdfFallback.Visibility = Visibility.Collapsed;
            }
            catch
            {
                PdfFallback.Text = "Image de la feuille illisible.";
            }
        }

        private bool _suspendVisionChange;

        /// <summary>
        /// Alimente le sélecteur de modèle de lecture. Seuls les profils réellement installés sont
        /// listés : proposer un modèle absent ne produirait qu'un échec au moment de lire.
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
            // Pas d'avertissement sur les blocs incomplets, à la différence de la feuille de
            // l'enfant. Là-bas, un axe à demi rempli produisait un SCORE creux qui devenait une
            // couleur. Ici on n'enregistre aucun score : une ligne vide reste une ligne vide, et
            // un retour partiel est un fait clinique en soi — c'est même le cas prévu quand la
            // feuille ne revient pas complète de la salle d'attente.
            Reponses       = _vm.ToReponses();
            Informateur    = _vm.Informateur;
            InformateurNom = _vm.InformateurNom;
            DialogResult   = true;
            Close();
        }

        private void Annuler_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// Lecture automatique : découpe la feuille bloc par bloc et fait lire les cases par le
        /// modèle vision.
        ///
        /// Le résultat PRÉ-REMPLIT la saisie, il ne la remplace pas — et il ne touche que les
        /// lignes encore vides. Rien n'est enregistré tant que le médecin n'a pas vérifié : c'est
        /// le sens même de cette fenêtre.
        /// </summary>
        private async void Lire_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_vm.ImagePath) || !File.Exists(_vm.ImagePath))
            {
                MessageBox.Show("Aucune feuille scannée à lire.",
                    "Lecture automatique", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var service = new MedCompanion.Services.Evaluations.CartographieEnvLectureService();
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
                var (ok, lecture, informateur, message) = await service.LireAsync(_vm.ImagePath!);

                // L'informateur est repris même si les blocs ont échoué : c'est une lecture
                // indépendante, et la perdre obligerait à ressaisir ce que le modèle avait vu.
                if (informateur != null)
                    _vm.PrefillInformateur(informateur.Qui, informateur.Nom);

                if (!ok)
                {
                    PiedTb.Text = $"❌ {message} — saisissez les réponses à la main.";
                    return;
                }

                _vm.Prefill(lecture);

                PiedTb.Text = message == null
                    ? $"⚡ {lecture.Count} blocs lus — VÉRIFIEZ chaque ligne sur l'image avant d'enregistrer."
                    : $"⚡ {lecture.Count} blocs lus. {message}. Vérifiez chaque ligne sur l'image.";
            }
            catch (Exception ex)
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
