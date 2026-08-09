using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MedCompanion.Services.Vision;
using Microsoft.Win32;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Banc d'essai du <see cref="GlmOcrService"/> : on charge une image, on déclenche l'une des
    /// trois méthodes et on lit la sortie brute du modèle.
    ///
    /// But : valider GLM-OCR sur du français réel (manuscrit, formulaires, pages scannées) AVANT
    /// de bâtir la lecture du formulaire parents dessus.
    /// </summary>
    public partial class OcrTestDialog : Window
    {
        private readonly GlmOcrService _ocr;
        private byte[]? _imageBytes;
        private bool _busy;

        private const string DefaultSchema =
            "{\n  \"nom\": null,\n  \"prenom\": null,\n  \"dateNaissance\": null,\n  \"telephone\": null\n}";

        public OcrTestDialog(AppSettings settings)
        {
            InitializeComponent();
            _ocr = new GlmOcrService(settings);
            SchemaBox.Text = DefaultSchema;
            Loaded += async (_, _) => await CheckModelAsync();
        }

        // ──────────────────────────────────────────────
        //  ÉTAT DU MODÈLE
        // ──────────────────────────────────────────────

        private async Task CheckModelAsync()
        {
            var (ready, error) = await _ocr.IsReadyAsync();

            if (ready)
            {
                SetBanner("✅", $"Modèle « {_ocr.ModelName} » prêt — exécution 100 % locale.", "#E8F6EF");
            }
            else
            {
                SetBanner("⚠️", error ?? "Modèle indisponible.", "#FDEBD0");
            }

            UpdateButtons();
        }

        private void SetBanner(string icon, string text, string hexBackground)
        {
            StatusIcon.Text = icon;
            StatusText.Text = text;
            StatusBanner.Background = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(hexBackground));
        }

        // ──────────────────────────────────────────────
        //  CHARGEMENT DE L'IMAGE
        // ──────────────────────────────────────────────

        private void LoadBtn_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Choisir une image à lire",
                Filter = "Images (png, jpg, bmp, tif)|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|Tous les fichiers|*.*"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                _imageBytes = File.ReadAllBytes(dlg.FileName);

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = new MemoryStream(_imageBytes);
                bmp.EndInit();
                PreviewImage.Source = bmp;
                NoImageHint.Visibility = Visibility.Collapsed;

                var (w, h) = ImageOps.GetSize(_imageBytes);
                FooterText.Text = $"{Path.GetFileName(dlg.FileName)} — {w}×{h} px, " +
                                  $"{_imageBytes.Length / 1024} Ko";
                OutputBox.Clear();
                UpdateButtons();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Impossible de charger l'image : {ex.Message}",
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ──────────────────────────────────────────────
        //  LES TROIS MÉTHODES
        // ──────────────────────────────────────────────

        private async void TextBtn_Click(object sender, RoutedEventArgs e) =>
            await RunAsync("Texte complet", ct => _ocr.ExtractTextAsync(_imageBytes!, ct));

        private async void JsonBtn_Click(object sender, RoutedEventArgs e)
        {
            var schema = SchemaBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(schema))
            {
                MessageBox.Show("Renseignez un schéma JSON.", "Schéma manquant",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            await RunAsync("Extraction JSON", ct => _ocr.ExtractJsonAsync(_imageBytes!, schema, null, ct));
        }

        private async void StripBtn_Click(object sender, RoutedEventArgs e)
        {
            var charset = string.IsNullOrWhiteSpace(CharsetBox.Text)
                ? "lettres majuscules A-Z"
                : CharsetBox.Text.Trim();

            int? length = int.TryParse(LengthBox.Text.Trim(), out var n) && n > 0 ? n : null;

            await RunAsync("Bande contrainte",
                ct => _ocr.ExtractConstrainedAsync(_imageBytes!, charset, length, ct));
        }

        private async Task RunAsync(
            string label,
            Func<System.Threading.CancellationToken,
                 Task<(bool success, string result, string? error)>> call)
        {
            if (_busy || _imageBytes == null) return;

            _busy = true;
            UpdateButtons();
            OutputBox.Text = $"[{label}] lecture en cours...";
            FooterText.Text = "Le premier appel inclut le chargement du modèle en VRAM (quelques secondes).";

            var chrono = Stopwatch.StartNew();
            var (ok, result, error) = await call(default);
            chrono.Stop();

            OutputBox.Text = ok ? result : $"❌ {error}";
            FooterText.Text = ok
                ? $"[{label}] terminé en {chrono.Elapsed.TotalSeconds:N1} s — {result.Length} caractères."
                : $"[{label}] échec après {chrono.Elapsed.TotalSeconds:N1} s.";

            _busy = false;
            UpdateButtons();
        }

        // ──────────────────────────────────────────────
        //  DIVERS
        // ──────────────────────────────────────────────

        private void UpdateButtons()
        {
            bool can = !_busy && _imageBytes != null;
            TextBtn.IsEnabled = can;
            JsonBtn.IsEnabled = can;
            StripBtn.IsEnabled = can;
            LoadBtn.IsEnabled = !_busy;
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
    }
}
