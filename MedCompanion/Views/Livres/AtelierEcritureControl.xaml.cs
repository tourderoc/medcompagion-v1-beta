using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MedCompanion.Services;
using MedCompanion.Services.LLM;
using MedCompanion.ViewModels.Livres;
using Microsoft.Web.WebView2.Core;

namespace MedCompanion.Views.Livres
{
    /// <summary>
    /// Atelier d'écriture du mode Bureau : bibliothèque de livres, éditeur de
    /// chapitres avec aperçu HTML paginé (WebView2) et export PDF — même flux
    /// que le Dossier de Restitution Clinique.
    /// </summary>
    public partial class AtelierEcritureControl : UserControl
    {
        private readonly AtelierEcritureViewModel _viewModel;
        private string? _tempHtmlPath;
        private bool _webViewReady;
        private bool _initialized;

        public AtelierEcritureControl()
        {
            InitializeComponent();
            _viewModel = new AtelierEcritureViewModel();
            DataContext = _viewModel;
            _viewModel.PreviewRefreshRequested += RefreshPreview;
        }

        /// <summary>Appelé par BureauMedControl avec la factory LLM (Med).</summary>
        public void Initialize(LLMServiceFactory llmFactory)
        {
            if (_initialized) return;
            _initialized = true;
            _viewModel.Initialize(llmFactory);
        }

        /// <summary>Sauvegarde immédiate (appelé quand l'outil est masqué ou l'app fermée).</summary>
        public void SaveAll() => _viewModel.SauvegarderChapitreCourant();

        private bool _libraryLoaded;

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            // Charger la bibliothèque AVANT l'await : si le WebView2 tarde à
            // s'initialiser (contrôle encore masqué), les combos restent remplis.
            if (!_libraryLoaded)
            {
                _libraryLoaded = true;
                _viewModel.ChargerBibliotheque();
            }
            await EnsureWebViewInitializedAsync();
            RefreshPreview();
        }

        private async System.Threading.Tasks.Task EnsureWebViewInitializedAsync()
        {
            if (_webViewReady) return;
            try
            {
                var userDataFolder = Path.Combine(Path.GetTempPath(), "MedCompanion_WebView2_Preview");
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await PreviewWebView.EnsureCoreWebView2Async(env);
                _webViewReady = true;
                PreviewWebView.Visibility = Visibility.Visible;
                PreviewFallback.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                PreviewFallback.Text = $"⚠ Erreur WebView2 : {ex.Message}";
            }
        }

        private void RefreshPreview()
        {
            if (!_webViewReady) return;
            try
            {
                var html = _viewModel.BuildPreviewHtml();
                if (string.IsNullOrEmpty(_tempHtmlPath))
                {
                    var dir = Path.Combine(Path.GetTempPath(), "MedCompanion_WebView2_Preview");
                    Directory.CreateDirectory(dir);
                    _tempHtmlPath = Path.Combine(dir, $"livre_preview_{Guid.NewGuid():N}.html");
                }
                File.WriteAllText(_tempHtmlPath, html, Encoding.UTF8);
                PreviewWebView.CoreWebView2.Navigate(new Uri(_tempHtmlPath).AbsoluteUri);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AtelierEcriture] RefreshPreview échec : {ex.Message}");
            }
        }

        // ── Bibliothèque ────────────────────────────────────────────────────

        private void NouveauLivre_Click(object sender, RoutedEventArgs e)
        {
            var titre = PromptText("Nouveau livre", "Titre du livre :");
            if (string.IsNullOrWhiteSpace(titre)) return;

            var auteur = PromptText("Nouveau livre", "Auteur (optionnel) :") ?? "";

            var (success, error) = _viewModel.CreerLivre(titre, auteur);
            if (!success)
                MessageBox.Show(error, "Nouveau livre", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void NouveauChapitre_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.SelectedLivre == null)
            {
                MessageBox.Show("Créez ou sélectionnez d'abord un livre.", "Nouveau chapitre",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var titre = PromptText("Nouveau chapitre", "Titre du chapitre :",
                $"Chapitre {_viewModel.Chapitres.Count + 1}");
            if (string.IsNullOrWhiteSpace(titre)) return;

            var (success, error) = _viewModel.CreerChapitre(titre);
            if (!success)
                MessageBox.Show(error, "Nouveau chapitre", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>
        /// Importe un ou plusieurs textes existants (docx, txt, md, pdf) comme
        /// chapitres. Si aucun livre n'est sélectionné, en crée un à partir du
        /// nom du premier fichier.
        /// </summary>
        private void ImporterChapitre_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Importer un texte comme chapitre",
                Filter = Services.Livres.LivreImportService.FiltreDialog,
                Multiselect = true
            };
            if (dialog.ShowDialog() != true || dialog.FileNames.Length == 0) return;

            // Pas de livre ? On en crée un à partir du nom du premier fichier.
            if (_viewModel.SelectedLivre == null)
            {
                var titreLivre = PromptText("Nouveau livre",
                    "Aucun livre sélectionné — titre du livre à créer :",
                    Path.GetFileNameWithoutExtension(dialog.FileNames[0]));
                if (string.IsNullOrWhiteSpace(titreLivre)) return;

                var (okLivre, errLivre) = _viewModel.CreerLivre(titreLivre, "");
                if (!okLivre)
                {
                    MessageBox.Show(errLivre, "Importer", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            var erreurs = new StringBuilder();
            foreach (var fichier in dialog.FileNames)
            {
                var defaut = Path.GetFileNameWithoutExtension(fichier);
                // Un seul fichier : on laisse ajuster le titre ; plusieurs : nom du fichier
                var titre = dialog.FileNames.Length == 1
                    ? PromptText("Importer", "Titre du chapitre :", defaut)
                    : defaut;
                if (string.IsNullOrWhiteSpace(titre)) continue;

                var (success, error) = _viewModel.ImporterChapitre(fichier, titre);
                if (!success)
                    erreurs.AppendLine($"• {Path.GetFileName(fichier)} : {error}");
            }

            if (erreurs.Length > 0)
                MessageBox.Show($"Certains fichiers n'ont pas pu être importés :\n\n{erreurs}",
                    "Importer", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private void SupprimerChapitre_Click(object sender, RoutedEventArgs e)
        {
            var chapitre = _viewModel.SelectedChapitre;
            if (chapitre == null) return;

            var confirm = MessageBox.Show(
                $"Supprimer définitivement le chapitre « {chapitre.Titre} » ?",
                "Supprimer chapitre", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            var (success, error) = _viewModel.SupprimerChapitre(chapitre);
            if (!success)
                MessageBox.Show(error, "Supprimer chapitre", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // ── Med ─────────────────────────────────────────────────────────────

        private async void Envoyer_Click(object sender, RoutedEventArgs e)
            => await _viewModel.EnvoyerChatAsync();

        private async void ChatBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(ChatBox.Text))
                await _viewModel.EnvoyerChatAsync();
        }

        private async void Continuer_Click(object sender, RoutedEventArgs e)
            => await _viewModel.ContinuerAsync();

        private async void Reformuler_Click(object sender, RoutedEventArgs e)
            => await _viewModel.ReformulerAsync(EditorBox.SelectedText);

        private async void Memoire_Click(object sender, RoutedEventArgs e)
            => await _viewModel.MettreAJourMemoireAsync();

        /// <summary>
        /// Insère la proposition de Med : remplace la sélection s'il y en a une,
        /// sinon insère à la position du curseur (avec saut de paragraphe).
        /// </summary>
        private void InsererReponse_Click(object sender, RoutedEventArgs e)
        {
            var texte = _viewModel.MedReponse;
            if (string.IsNullOrWhiteSpace(texte)) return;

            if (EditorBox.SelectionLength > 0)
            {
                EditorBox.SelectedText = texte;
                EditorBox.CaretIndex = EditorBox.SelectionStart + texte.Length;
                EditorBox.SelectionLength = 0;
            }
            else
            {
                int caret = EditorBox.CaretIndex;
                var existant = EditorBox.Text ?? "";
                // Saut de paragraphe si on insère à la suite d'un texte existant
                string prefix = (caret > 0 && !existant.Substring(0, caret).EndsWith("\n\n"))
                    ? (existant.Substring(0, caret).EndsWith("\n") ? "\n" : "\n\n")
                    : "";
                EditorBox.Text = existant.Insert(caret, prefix + texte);
                EditorBox.CaretIndex = caret + prefix.Length + texte.Length;
            }

            _viewModel.MedReponse = "";
            EditorBox.Focus();
        }

        private void FermerReponse_Click(object sender, RoutedEventArgs e)
            => _viewModel.MedReponse = "";

        // ── Export PDF (même flux que la restitution : Edge headless) ───────

        private async void ExporterPdf_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.SelectedLivre == null) return;
            _viewModel.SauvegarderChapitreCourant();

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title    = "Exporter le livre en PDF",
                Filter   = "Fichier PDF|*.pdf",
                FileName = $"{_viewModel.SelectedLivre.Titre.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd}"
            };
            if (dialog.ShowDialog() != true) return;

            var btn = (Button)sender;
            btn.IsEnabled = false;

            try
            {
                var html = _viewModel.BuildPreviewHtml();
                var tmpHtml = Path.Combine(Path.GetTempPath(), $"livre_export_{Guid.NewGuid():N}.html");
                File.WriteAllText(tmpHtml, html, Encoding.UTF8);

                var pdfSvc = new EdgeHeadlessPdfService();
                if (!pdfSvc.IsAvailable)
                {
                    MessageBox.Show("Microsoft Edge est introuvable sur ce poste — l'export PDF nécessite Edge.",
                        "Export PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                bool ok = await pdfSvc.ConvertAsync(tmpHtml, dialog.FileName);
                try { File.Delete(tmpHtml); } catch { }

                if (ok)
                    Process.Start(new ProcessStartInfo { FileName = dialog.FileName, UseShellExecute = true });
                else
                    MessageBox.Show("La conversion PDF a échoué. Réessayez ou vérifiez les permissions du dossier cible.",
                        "Export PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'export : {ex.Message}",
                    "Export PDF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btn.IsEnabled = true;
            }
        }

        // ── Petit dialogue de saisie (titre livre/chapitre) ─────────────────

        private string? PromptText(string titre, string label, string defaut = "")
        {
            var win = new Window
            {
                Title = titre,
                Width = 420,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                Background = System.Windows.Media.Brushes.White
            };

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 8), FontSize = 13 });

            var box = new TextBox { Text = defaut, Padding = new Thickness(8, 6, 8, 6), FontSize = 13 };
            panel.Children.Add(box);

            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 14, 0, 0)
            };
            var ok = new Button { Content = "OK", Width = 80, Padding = new Thickness(0, 6, 0, 6), IsDefault = true };
            var cancel = new Button { Content = "Annuler", Width = 80, Padding = new Thickness(0, 6, 0, 6), IsCancel = true, Margin = new Thickness(8, 0, 0, 0) };
            ok.Click += (s, e) => { win.DialogResult = true; };
            buttons.Children.Add(ok);
            buttons.Children.Add(cancel);
            panel.Children.Add(buttons);

            win.Content = panel;
            box.Focus();
            box.SelectAll();

            return win.ShowDialog() == true ? box.Text : null;
        }
    }
}
