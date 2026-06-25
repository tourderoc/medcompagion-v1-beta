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
