using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MedCompanion.Services;
using MedCompanion.ViewModels.Restitutions;
using Microsoft.Web.WebView2.Core;

namespace MedCompanion.Views.Restitutions
{
    public partial class RestitutionEditorView : UserControl
    {
        private string? _tempHtmlPath;
        private bool _webViewReady;
        private RestitutionEditorViewModel? _boundVm;

        public RestitutionEditorView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            await EnsureWebViewInitializedAsync();
            // Premier rendu une fois la WebView prête.
            RefreshPreview();
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Détacher l'ancien VM si on est ré-attribué.
            if (_boundVm != null)
            {
                _boundVm.PreviewRefreshRequested -= RefreshPreview;
                _boundVm = null;
            }
            if (DataContext is RestitutionEditorViewModel vm)
            {
                _boundVm = vm;
                vm.PreviewRefreshRequested += RefreshPreview;
                // La mesure des pages passe par le WebView2, que seule la vue détient. C'est
                // le même moteur que l'export PDF : ce qu'il mesure est ce qui sera imprimé.
                vm.MesurerPagesDansApercu = MesurerPagesAsync;
                if (_webViewReady) RefreshPreview();
            }
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

        private async void ExportPdfButton_Click(object sender, RoutedEventArgs e)
        {
            if (_boundVm == null) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title    = "Enregistrer le Dossier de Restitution en PDF",
                Filter   = "Fichier PDF|*.pdf",
                FileName = $"Dossier_Restitution_{_boundVm.PatientName.Replace(" ", "_")}_{DateTime.Now:yyyyMMdd}"
            };
            if (dialog.ShowDialog() != true) return;

            var btn = (Button)sender;
            btn.IsEnabled = false;
            btn.Content   = "⏳ Génération...";

            try
            {
                var html    = _boundVm.BuildPreviewHtml();
                var tmpHtml = Path.Combine(Path.GetTempPath(), $"restitution_export_{Guid.NewGuid():N}.html");
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
                {
                    // Persiste le chemin PDF dans le .md et notifie le hub pour qu'il affiche le bouton PDF.
                    await _boundVm.OnPdfExportedAsync(dialog.FileName);
                    Process.Start(new ProcessStartInfo { FileName = dialog.FileName, UseShellExecute = true });
                }
                else
                {
                    MessageBox.Show("La conversion PDF a échoué. Réessayez ou vérifiez les permissions du dossier cible.",
                        "Export PDF", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'export : {ex.Message}",
                    "Export PDF", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                btn.IsEnabled = true;
                btn.Content   = "🖨 Exporter PDF";
            }
        }


        /// <summary>
        /// Mesure la hauteur réelle du contenu de chaque page de l'aperçu, et la renvoie en JSON.
        ///
        /// Le plancher <c>min-height: 297mm</c> est neutralisé le temps de la mesure : sans ça,
        /// toute page non débordante mesurerait exactement 297 mm et on ne saurait jamais
        /// combien de marge il reste.
        /// Le script est SYNCHRONE — voir le commentaire dans le corps.
        /// </summary>
        private async System.Threading.Tasks.Task<string?> MesurerPagesAsync()
        {
            if (!_webViewReady || PreviewWebView.CoreWebView2 == null) return null;

            // SYNCHRONE, ET PAS async : ExecuteScriptAsync sérialise la valeur de l'expression
            // sans attendre les promesses. Une fonction async évaluait vers une Promise, que
            // WebView2 rendait sous forme d'objet vide — d'où « la mesure n'a rien renvoyé ».
            // On ne peut donc pas attendre document.fonts.ready ici ; ce n'est pas un problème,
            // le médecin ne clique qu'une fois l'aperçu affiché à l'écran, donc polices déjà
            // chargées et retours à la ligne stabilisés.
            const string script = @"
(function () {
  function txt(e) { return e ? e.textContent.trim().replace(/\s+/g, ' ') : ''; }

  var pages = document.querySelectorAll('.page');
  var out = [];

  for (var i = 0; i < pages.length; i++) {
    var p = pages[i];
    var titre = [
      txt(p.querySelector('.pc-header-left h1, .draft-title, .an-header h1, .ac-header h1, h1')),
      txt(p.querySelector('.pc-subtitle'))
    ].filter(Boolean).join(' / ');
    if (titre.length > 70) titre = titre.substring(0, 69) + '…';

    // Le plancher min-height: 297mm est neutralisé le temps de la mesure : sans ça, toute
    // page non débordante mesurerait exactement 297 mm et la marge restante serait invisible.
    var mh = p.style.minHeight, hh = p.style.height, ov = p.style.overflow;
    p.style.minHeight = '0'; p.style.height = 'auto'; p.style.overflow = 'visible';
    var contenu = Math.round(p.getBoundingClientRect().height);
    p.style.minHeight = mh; p.style.height = hh; p.style.overflow = ov;

    // Une page dont le contenu ne dépend pas de l'enfant n'a pas de marge à surveiller : la
    // couverture est un gabarit à champs, l'annexe méthodologique une ressource figée. Les
    // signaler « à 95 % » chaque fois apprend à ignorer le rapport. Un DÉBORDEMENT y reste
    // signalé — sur une page invariante, ce serait un défaut du gabarit, pas du dossier.
    var fixe = p.classList.contains('cover-page') || p.classList.contains('annexe-page');

    out.push({ i: i + 1, h: contenu, t: titre, x: fixe ? 1 : 0 });
  }

  return JSON.stringify(out);
})()";

            try
            {
                return await PreviewWebView.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[RestitutionEditorView] Mesure des pages échouée : {ex.Message}");
                return null;
            }
        }

        private void RefreshPreview()
        {
            if (!_webViewReady || _boundVm == null) return;
            try
            {
                var html = _boundVm.BuildPreviewHtml();

                // Écrire dans un fichier temporaire (NavigateToString a une limite ~2 Mo
                // que le tree.png + fonts dépassent largement).
                if (string.IsNullOrEmpty(_tempHtmlPath))
                {
                    var dir = Path.Combine(Path.GetTempPath(), "MedCompanion_WebView2_Preview");
                    Directory.CreateDirectory(dir);
                    _tempHtmlPath = Path.Combine(dir, $"preview_{Guid.NewGuid():N}.html");
                }
                File.WriteAllText(_tempHtmlPath, html, Encoding.UTF8);

                PreviewWebView.CoreWebView2.Navigate(new Uri(_tempHtmlPath).AbsoluteUri);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[RestitutionEditorView] RefreshPreview échec : {ex.Message}");
            }
        }
    }
}
