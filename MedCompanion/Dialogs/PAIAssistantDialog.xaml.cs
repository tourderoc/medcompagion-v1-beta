using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MedCompanion.Models;
using MedCompanion.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MedCompanion.Dialogs
{
    public partial class PAIAssistantDialog : Window
    {
        private readonly PatientIndexEntry _selectedPatient;
        private readonly PatientIndexService _patientIndex;
        private readonly FormulaireAssistantService _formulaireService;
        private readonly PathService _pathService = new PathService();

        private WebView2? _webView;
        private bool _webViewInitialized = false;
        private string _pdfPath = "";
        private double _currentZoom = 1.0;

        private string _motif;

        public PAIAssistantDialog(
            PatientIndexEntry selectedPatient,
            PatientIndexService patientIndex,
            FormulaireAssistantService formulaireService,
            string initialMotif = "")
        {
            InitializeComponent();

            _selectedPatient = selectedPatient;
            _patientIndex = patientIndex;
            _formulaireService = formulaireService;
            _motif = initialMotif;

            Loaded += PAIAssistantDialog_Loaded;
        }

        private async void PAIAssistantDialog_Loaded(object sender, RoutedEventArgs e)
        {
            LoadPatientInfo();
            InitializeMotif();
            await InitializeWebView2Async();
        }

        private void InitializeMotif()
        {
            if (!string.IsNullOrEmpty(_motif))
            {
                MotifComboBox.Text = _motif;
            }
        }

        private void MotifComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MotifComboBox.SelectedItem is ComboBoxItem item)
            {
                _motif = item.Content.ToString();
            }
        }

        private void MotifComboBox_LostFocus(object sender, RoutedEventArgs e)
        {
            _motif = MotifComboBox.Text;
        }

        private void SaveMotifButton_Click(object sender, RoutedEventArgs e)
        {
            _motif = MotifComboBox.Text;
            var formulairesDir = _pathService.GetFormulairesDirectory(_selectedPatient.NomComplet);
            var pdfFileName = Path.GetFileName(_pdfPath);
            SaveMetadata(formulairesDir, pdfFileName);
            MessageBox.Show("Motif sauvegardé !", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void LoadPatientInfo()
        {
            var metadata = _patientIndex.GetMetadata(_selectedPatient.Id);
            if (metadata != null)
            {
                PatientPrenomText.Text = metadata.Prenom ?? "Non renseigné";
                PatientNomText.Text = metadata.Nom ?? "Non renseigné";
                
                if (!string.IsNullOrEmpty(metadata.Dob) && DateTime.TryParse(metadata.Dob, out var dob))
                {
                    PatientDobText.Text = dob.ToString("dd/MM/yyyy");
                }
                else
                {
                    PatientDobText.Text = "Non renseignée";
                }
            }
            else
            {
                PatientPrenomText.Text = _selectedPatient.Prenom;
                PatientNomText.Text = _selectedPatient.Nom;
                PatientDobText.Text = "Non renseignée";
            }
        }

        private async Task InitializeWebView2Async()
        {
            try
            {
                var assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Formulaires");
                var templatePath = Path.Combine(assetsPath, "Dossier PAI.pdf");

                if (!File.Exists(templatePath))
                {
                    PdfFallbackMessage.Text = "❌ PDF PAI introuvable dans Assets/Formulaires";
                    PdfFallbackMessage.Foreground = Brushes.Red;
                    return;
                }

                var formulairesDir = _pathService.GetFormulairesDirectory(_selectedPatient.NomComplet);
                Directory.CreateDirectory(formulairesDir);

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var pdfFileName = $"PAI_{_selectedPatient.Nom}_{_selectedPatient.Prenom}_{timestamp}.pdf";
                _pdfPath = Path.Combine(formulairesDir, pdfFileName);

                File.Copy(templatePath, _pdfPath, overwrite: true);

                SaveMetadata(formulairesDir, pdfFileName);

                _webView = new WebView2();
                PdfViewerContainer.Children.Add(_webView);

                var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Path.GetTempPath(), "MedCompanion_WebView2"));
                await _webView.EnsureCoreWebView2Async(env);

                _webViewInitialized = true;
                _webView.CoreWebView2.Navigate(_pdfPath);

                PdfFallbackMessage.Visibility = Visibility.Collapsed;
                PdfZoomInButton.IsEnabled = true;
                PdfZoomOutButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                PdfFallbackMessage.Text = $"⚠ Erreur WebView2 : {ex.Message}";
                PdfFallbackMessage.Foreground = Brushes.Orange;
            }
        }

        private void SaveMetadata(string directory, string pdfFileName)
        {
            try
            {
                var synthesis = new PAISynthesis
                {
                    Type = "PAI",
                    DateCreation = DateTime.Now,
                    Patient = _selectedPatient.NomComplet,
                    Motif = _motif,
                    FileName = pdfFileName
                };

                var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                var json = System.Text.Json.JsonSerializer.Serialize(synthesis, jsonOptions);
                
                var jsonPath = Path.Combine(directory, Path.ChangeExtension(pdfFileName, ".json"));
                File.WriteAllText(jsonPath, json, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la sauvegarde des métadonnées : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            var instruction = InstructionTextBox.Text.Trim();
            if (string.IsNullOrEmpty(instruction))
            {
                MessageBox.Show("Veuillez entrer une instruction pour l'IA.", "Instruction manquante", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                GenerateButton.IsEnabled = false;
                GenerateButton.Content = "⏳ Génération en cours...";
                StatusText.Text = "";
                ResponseTextBox.Text = "";

                var style = (StyleComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Standard";
                var length = (LengthComboBox.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "Moyen";

                var metadata = _patientIndex.GetMetadata(_selectedPatient.Id);
                if (metadata == null)
                {
                    MessageBox.Show("Impossible de charger les données du patient.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var response = await _formulaireService.GenerateCustomContent(metadata, instruction, style, length);

                ResponseTextBox.Text = response;
                CopyResponseButton.IsEnabled = true;
                StatusText.Text = "✅ Réponse générée !";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la génération : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                GenerateButton.IsEnabled = true;
                GenerateButton.Content = "✨ Générer avec l'IA";
            }
        }

        private void CopyResponseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(ResponseTextBox.Text))
            {
                Clipboard.SetText(ResponseTextBox.Text);
                StatusText.Text = "✅ Copié dans le presse-papier !";
                
                Task.Delay(2000).ContinueWith(_ => 
                {
                    Dispatcher.Invoke(() => StatusText.Text = "✅ Réponse générée !");
                });
            }
        }

        private void CopyPrenomButton_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(PatientPrenomText.Text);
        }

        private void CopyNomButton_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(PatientNomText.Text);
        }

        private void CopyDobButton_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(PatientDobText.Text);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void PdfZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            if (_webViewInitialized && _webView != null)
            {
                _currentZoom += 0.1;
                _webView.ZoomFactor = _currentZoom;
            }
        }

        private void PdfZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            if (_webViewInitialized && _webView != null && _currentZoom > 0.2)
            {
                _currentZoom -= 0.1;
                _webView.ZoomFactor = _currentZoom;
            }
        }
    }
}
