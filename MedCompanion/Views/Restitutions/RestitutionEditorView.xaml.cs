using System;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
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
