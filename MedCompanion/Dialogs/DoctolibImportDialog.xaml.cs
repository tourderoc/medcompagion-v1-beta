using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.Services.LLM;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Dialog d'import Doctolib : 2 captures manuelles → OCR Tesseract → Ollama → révision → sauvegarde.
    /// Toutes les données restent 100% locales.
    /// </summary>
    public partial class DoctolibImportDialog : Window
    {
        private readonly Func<byte[]> _captureCallback;
        private readonly PatientIndexEntry _patient;
        private readonly PathService _pathService;
        private readonly DoctolibImportService _importService;

        private byte[]? _capture1;
        private byte[]? _capture2;
        private bool _isAnalyzing;

        public DoctolibImportDialog(
            Func<byte[]> captureCallback,
            PatientIndexEntry patient,
            PathService pathService,
            ILLMService llmService)
        {
            InitializeComponent();

            _captureCallback = captureCallback;
            _patient = patient;
            _pathService = pathService;
            _importService = new DoctolibImportService(new TesseractOCRService(), llmService);
        }

        // ──────────────────────────────────────────────
        //  ÉTAPE CAPTURE
        // ──────────────────────────────────────────────

        private void Capture1Btn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _capture1 = _captureCallback();
                if (_capture1 == null || _capture1.Length == 0)
                {
                    SetStatus("Capture 1 vide — vérifiez que Doctolib est affiché dans la zone Bureau.");
                    return;
                }

                Capture1StatusIcon.Text = "✓";
                Capture1StatusText.Text = "Capturé";
                Capture1StatusText.Foreground = System.Windows.Media.Brushes.Green;
                Capture1Btn.Content = "🔄  Re-capturer";
                Step1BadgeBorder.Background = System.Windows.Media.Brushes.Green;

                // Débloquer capture 2
                Capture2Btn.IsEnabled = true;
                Capture2Btn.Style = (Style)FindResource("BtnPrimary");

                // Débloquer Analyser
                AnalyzeBtn.IsEnabled = true;

                SetStatus("Capture 1 OK. Vous pouvez capturer la section Médecin traitant ou cliquer directement sur Analyser.");
            }
            catch (Exception ex)
            {
                SetStatus($"Erreur capture 1 : {ex.Message}");
            }
        }

        private void Capture2Btn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _capture2 = _captureCallback();
                if (_capture2 == null || _capture2.Length == 0)
                {
                    SetStatus("Capture 2 vide — naviguez d'abord vers la section Médecin traitant dans Doctolib.");
                    return;
                }

                Capture2StatusIcon.Text = "✓";
                Capture2StatusText.Text = "Capturé";
                Capture2StatusText.Foreground = System.Windows.Media.Brushes.Green;
                Capture2Btn.Content = "🔄  Re-capturer";

                SetStatus("Captures 1 et 2 OK. Cliquez sur Analyser pour extraire les informations.");
            }
            catch (Exception ex)
            {
                SetStatus($"Erreur capture 2 : {ex.Message}");
            }
        }

        // ──────────────────────────────────────────────
        //  ANALYSE (OCR + Ollama)
        // ──────────────────────────────────────────────

        private async void AnalyzeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_isAnalyzing || _capture1 == null) return;
            _isAnalyzing = true;
            AnalyzeBtn.IsEnabled = false;
            SetStatus("OCR en cours (Tesseract local)...");

            try
            {
                SetStatus("Extraction par le modèle local (Ollama)...");
                var (success, result, error) = await _importService.ImportAsync(_capture1, _capture2);

                if (!success)
                {
                    SetStatus($"Erreur : {error}");
                    return;
                }

                // Afficher le panneau de révision avec les champs pré-remplis
                FillReviewPanel(result);
                ShowReviewPanel();

                string warn = result.OllamaError != null ? $" ⚠️ Ollama : {result.OllamaError}" : "";
                SetStatus($"Extraction terminée. Vérifiez et corrigez les champs avant d'appliquer.{warn}");
            }
            catch (Exception ex)
            {
                SetStatus($"Erreur analyse : {ex.Message}");
                AnalyzeBtn.IsEnabled = true;
            }
            finally
            {
                _isAnalyzing = false;
            }
        }

        // ──────────────────────────────────────────────
        //  PANNEAU DE RÉVISION
        // ──────────────────────────────────────────────

        private void FillReviewPanel(DoctolibImportResult r)
        {
            RevPrenom.Text = r.Prenom ?? "";
            RevNom.Text = r.Nom ?? "";
            RevDob.Text = r.DateNaissance ?? "";
            RevSexe.Text = r.Sexe ?? "";
            RevAdresse.Text = r.AdresseRue ?? "";
            RevCP.Text = r.CodePostal ?? "";
            RevVille.Text = r.Ville ?? "";
            RevTelephone.Text = r.Telephone ?? "";
            RevEmail.Text = r.Email ?? "";
            RevNIR.Text = r.NumeroSecuriteSociale ?? "";
            RevLieuNaissance.Text = r.LieuNaissance ?? "";

            RevMTNom.Text = r.MTNom ?? "";
            RevMTPrenom.Text = r.MTPrenom ?? "";
            RevMTAdresse.Text = r.MTAdresse ?? "";
            RevMTCP.Text = r.MTCodePostal ?? "";
            RevMTVille.Text = r.MTVille ?? "";
            RevMTTelephone.Text = r.MTTelephone ?? "";
            RevMTAdeli.Text = r.MTAdeli ?? "";
        }

        private void ShowReviewPanel()
        {
            CapturePanel.Visibility = Visibility.Collapsed;
            ReviewPanel.Visibility = Visibility.Visible;
            BackBtn.Visibility = Visibility.Visible;
            AnalyzeBtn.Visibility = Visibility.Collapsed;
            ApplyBtn.Visibility = Visibility.Visible;
            HeaderSubtitle.Text = "Vérifiez les informations extraites avant de les appliquer au dossier.";
        }

        private void BackBtn_Click(object sender, RoutedEventArgs e)
        {
            CapturePanel.Visibility = Visibility.Visible;
            ReviewPanel.Visibility = Visibility.Collapsed;
            BackBtn.Visibility = Visibility.Collapsed;
            AnalyzeBtn.Visibility = Visibility.Visible;
            AnalyzeBtn.IsEnabled = _capture1 != null;
            ApplyBtn.Visibility = Visibility.Collapsed;
            HeaderSubtitle.Text = "Capturez les informations patient pour les importer dans le dossier.";
            SetStatus("Prêt. Recapturez si nécessaire, puis cliquez Analyser.");
        }

        // ──────────────────────────────────────────────
        //  APPLICATION AU DOSSIER
        // ──────────────────────────────────────────────

        private void ApplyBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var metadata = LoadExistingMetadata();

                // Appliquer uniquement les champs non vides (ne pas écraser ce qui existe déjà)
                ApplyIfNotEmpty(RevPrenom.Text, v => metadata.Prenom = v);
                ApplyIfNotEmpty(RevNom.Text, v => metadata.Nom = v);
                ApplyIfNotEmpty(ParseDob(RevDob.Text), v => metadata.Dob = v);
                ApplyIfNotEmpty(NormalizeSexe(RevSexe.Text), v => metadata.Sexe = v);
                ApplyIfNotEmpty(RevAdresse.Text, v => metadata.AdresseRue = v);
                ApplyIfNotEmpty(RevCP.Text, v => metadata.AdresseCodePostal = v);
                ApplyIfNotEmpty(RevVille.Text, v => metadata.AdresseVille = v);
                ApplyIfNotEmpty(RevTelephone.Text, v => metadata.AccompagnantTelephone = v);
                ApplyIfNotEmpty(RevEmail.Text, v => metadata.AccompagnantEmail = v);
                ApplyIfNotEmpty(CleanNir(RevNIR.Text), v => metadata.NumeroSecuriteSociale = v);
                ApplyIfNotEmpty(RevLieuNaissance.Text, v => metadata.LieuNaissance = v);
                ApplyIfNotEmpty(RevMTNom.Text, v => metadata.MedecinTraitantNom = v);
                ApplyIfNotEmpty(RevMTPrenom.Text, v => metadata.MedecinTraitantPrenom = v);
                ApplyIfNotEmpty(RevMTAdresse.Text, v => metadata.MedecinTraitantAdresse = v);
                ApplyIfNotEmpty(RevMTCP.Text, v => metadata.MedecinTraitantCodePostal = v);
                ApplyIfNotEmpty(RevMTVille.Text, v => metadata.MedecinTraitantVille = v);
                ApplyIfNotEmpty(RevMTTelephone.Text, v => metadata.MedecinTraitantTelephone = v);
                ApplyIfNotEmpty(RevMTAdeli.Text, v => metadata.MedecinTraitantAdeli = v);

                SaveMetadata(metadata);

                MessageBox.Show(
                    "Les informations ont été appliquées au dossier patient.",
                    "Import réussi",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                SetStatus($"Erreur lors de la sauvegarde : {ex.Message}");
            }
        }

        // ──────────────────────────────────────────────
        //  HELPERS
        // ──────────────────────────────────────────────

        private PatientMetadata LoadExistingMetadata()
        {
            var infoDir = Path.Combine(_patient.DirectoryPath, "info_patient");
            var jsonPath = Path.Combine(infoDir, "patient.json");

            if (File.Exists(jsonPath))
            {
                try
                {
                    var json = File.ReadAllText(jsonPath, System.Text.Encoding.UTF8);
                    return JsonSerializer.Deserialize<PatientMetadata>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new PatientMetadata { Prenom = _patient.Prenom, Nom = _patient.Nom };
                }
                catch { /* fallback */ }
            }

            return new PatientMetadata { Prenom = _patient.Prenom, Nom = _patient.Nom };
        }

        private void SaveMetadata(PatientMetadata metadata)
        {
            var infoDir = Path.Combine(_patient.DirectoryPath, "info_patient");
            Directory.CreateDirectory(infoDir);
            var jsonPath = Path.Combine(infoDir, "patient.json");

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            var json = JsonSerializer.Serialize(metadata, options);
            File.WriteAllText(jsonPath, json, System.Text.Encoding.UTF8);
        }

        private static void ApplyIfNotEmpty(string? value, Action<string> apply)
        {
            if (!string.IsNullOrWhiteSpace(value))
                apply(value.Trim());
        }

        private static string? ParseDob(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            // Accepte dd/MM/yyyy ou yyyy-MM-dd
            if (DateTime.TryParseExact(raw.Trim(), new[] { "dd/MM/yyyy", "yyyy-MM-dd", "d/M/yyyy", "dd-MM-yyyy" },
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var dt))
                return dt.ToString("yyyy-MM-dd");

            // Essai générique
            if (DateTime.TryParse(raw.Trim(), out dt))
                return dt.ToString("yyyy-MM-dd");

            return null;
        }

        private static string? NormalizeSexe(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var s = raw.Trim().ToUpperInvariant();
            if (s == "M" || s.StartsWith("H") || s.StartsWith("M")) return "H";
            if (s == "F" || s.StartsWith("F")) return "F";
            return null;
        }

        private static string? CleanNir(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var digits = new System.Text.StringBuilder();
            foreach (var c in raw)
                if (char.IsDigit(c)) digits.Append(c);
            var s = digits.ToString();
            return (s.Length == 13 || s.Length == 15) ? s : null;
        }

        private void SetStatus(string msg) => StatusText.Text = msg;

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
