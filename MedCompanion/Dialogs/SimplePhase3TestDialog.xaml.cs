using System;
using System.Threading.Tasks;
using System.Windows;
using MedCompanion.Models;
using MedCompanion.Services;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Fenêtre simple pour tester l'anonymisation (Phase 1 & 2 uniquement)
    /// (Anciennement Test Phase 3, conservé pour tests manuels)
    /// </summary>
    public partial class SimplePhase3TestDialog : Window
    {
        private readonly AnonymizationService _anonymizationService;
        private string _accumulatedLogs = "";

        public SimplePhase3TestDialog(AnonymizationService anonymizationService)
        {
            InitializeComponent();
            _anonymizationService = anonymizationService;

            // S'abonner aux logs de l'anonymisation
            _anonymizationService.LogMessage += OnAnonymizationLog;

            // Se désabonner à la fermeture
            this.Closed += (s, e) => _anonymizationService.LogMessage -= OnAnonymizationLog;

            // Lancer le test au démarrage
            Loaded += async (s, e) => 
            {
                await RunTestAsync();
            };
        }

        private void OnAnonymizationLog(string level, string message)
        {
            Dispatcher.Invoke(() =>
            {
                var logLine = $"[{DateTime.Now:HH:mm:ss}] [{level}] {message}\n";
                _accumulatedLogs += logLine;
            });
        }

        private async Task RunTestAsync()
        {
            ResultTextBlock.Text = "⏳ Test en cours...\n\n";

            try
            {
                // Texte de test simple
                var testText = @"Le patient Nathan LELEVÉ est suivi par le Dr. Lassoued Nair à l'Hôpital Saint-Joseph.
Il habite au 15 rue Victor Hugo, 13001 Marseille.
Contact: nathan.leleve@email.fr ou 06 12 34 56 78.
L'école Victor Hugo a contacté l'orthophoniste Mme Sophie Martin pour un bilan.";

                // Métadonnées patient
                var patientData = new PatientMetadata
                {
                    Nom = "LELEVÉ",
                    Prenom = "Nathan",
                    Sexe = "M",
                    AdresseRue = "15 rue Victor Hugo",
                    AdresseVille = "Marseille",
                    AdresseCodePostal = "13001",
                    Ecole = "École Victor Hugo",
                    // Ajout des médecins pour test Phase 1
                    MedecinTraitantNom = "Lassoued Nair",
                    MedecinReferentNom = "Martin",
                    MedecinReferentPrenom = "Sophie"
                };

                var result = "";
                _accumulatedLogs = "";

                result += "🔵 TEST COMPLET : Phase 1 & 2 (Déterministe)\n\n";
                
                var startTime = DateTime.Now;

                // APPEL : AnonymizeAsync (Phase 1+2)
                var (anonymizedText, context) = await _anonymizationService.AnonymizeAsync(
                    testText,
                    patientData
                );

                var duration = (DateTime.Now - startTime).TotalMilliseconds;

                result += $"✅ Anonymisation terminée en {duration:F0}ms\n";
                result += $"📊 Total remplacements : {context?.Replacements?.Count ?? 0}\n\n";

                // Afficher les résultats
                result += "═══════════════════════════════════════════════\n";
                result += "📝 TEXTE ORIGINAL :\n";
                result += $"{testText}\n";
                result += "═══════════════════════════════════════════════\n";
                result += "🔒 TEXTE ANONYMISÉ :\n";
                result += $"{anonymizedText}\n";
                result += "═══════════════════════════════════════════════\n\n";

                if (context?.Replacements != null && context.Replacements.Count > 0)
                {
                    result += $"📊 MAPPINGS DÉTECTÉS ({context.Replacements.Count}) :\n";
                    foreach (var kvp in context.Replacements)
                    {
                        result += $"  • \"{kvp.Key}\" → {kvp.Value}\n";
                    }
                    
                    // Test de désanonymisation
                    result += "\n🔄 VÉRIFICATION DÉSANONYMISATION :\n";
                    var deanonymized = _anonymizationService.Deanonymize(anonymizedText, context);
                    if (deanonymized == testText)
                    {
                        result += "✅ SUCCÈS : Le texte original a été parfaitement restauré.";
                    }
                    else
                    {
                        result += "⚠️ ATTENTION : Le texte restauré diffère de l'original.\n";
                        // Comparaison simple pour voir où ça diffère
                        // (Non implémenté ici pour rester simple)
                    }
                }
                else
                {
                    result += "⚠️ AUCUN REMPLACEMENT détecté\n";
                }

                result += "\n\n═══════════════════════════════════════════════\n";
                result += "📋 LOGS DÉTAILLÉS :\n";
                result += _accumulatedLogs;
                result += "\n═══════════════════════════════════════════════\n\n";

                ResultTextBlock.Text = result;
            }
            catch (Exception ex)
            {
                ResultTextBlock.Text = $"❌ ERREUR :\n\n{ex.Message}\n\nTrace: {ex.StackTrace}";
            }
        }

        private async void ImportPdfButton_Click(object sender, RoutedEventArgs e)
        {
            // Ouvrir un dialog pour sélectionner un PDF
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Sélectionner un document PDF à tester",
                Filter = "Documents PDF|*.pdf|Tous les fichiers|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (openFileDialog.ShowDialog() == true)
            {
                await TestPdfFileAsync(openFileDialog.FileName);
            }
        }

        private async Task TestPdfFileAsync(string pdfPath)
        {
            ResultTextBlock.Text = "⏳ Extraction du texte du PDF...\n\n";

            try
            {
                // Extraire le texte du PDF avec PdfPig
                string extractedText = "";
                using (var document = UglyToad.PdfPig.PdfDocument.Open(pdfPath))
                {
                    foreach (var page in document.GetPages())
                    {
                        extractedText += page.Text + "\n\n";
                    }
                }

                if (string.IsNullOrWhiteSpace(extractedText))
                {
                    ResultTextBlock.Text = "❌ ERREUR : Impossible d'extraire le texte du PDF.\n\n";
                    ResultTextBlock.Text += "Le PDF est peut-être scanné (image). Essayez avec un PDF avec du texte sélectionnable.";
                    return;
                }

                ResultTextBlock.Text = $"✅ Texte extrait : {extractedText.Length} caractères\n\n";
                ResultTextBlock.Text += "🔵 TEST PHASE 1 + 2 (Sans données patient)\n";
                ResultTextBlock.Text += "⚠️ Note : Comme aucune métadonnée patient n'est fournie, seule la Phase 2 (Regex) sera active,\n";
                ResultTextBlock.Text += "   sauf si des noms sont détectés de manière heuristique (non implémenté).\n\n";

                var startTime = DateTime.Now;
                _accumulatedLogs = "";

                var (anonymizedText, context) = await _anonymizationService.AnonymizeAsync(
                    extractedText,
                    patientData: null  // Pas de données patient connues pour un fichier test externe
                );

                var duration = (DateTime.Now - startTime).TotalMilliseconds;

                ResultTextBlock.Text += $"✅ Anonymisation terminée en {duration:F0}ms\n";
                ResultTextBlock.Text += $"📊 Total remplacements : {context?.Replacements?.Count ?? 0}\n\n";

                // Afficher les résultats
                ResultTextBlock.Text += "═══════════════════════════════════════════════\n";
                ResultTextBlock.Text += "📝 TEXTE ORIGINAL (Extrait) :\n";
                ResultTextBlock.Text += (extractedText.Length > 500 ? extractedText.Substring(0, 500) + "..." : extractedText) + "\n";
                ResultTextBlock.Text += "═══════════════════════════════════════════════\n";
                ResultTextBlock.Text += "🔒 TEXTE ANONYMISÉ (Extrait) :\n";
                ResultTextBlock.Text += (anonymizedText.Length > 500 ? anonymizedText.Substring(0, 500) + "..." : anonymizedText) + "\n";
                ResultTextBlock.Text += "═══════════════════════════════════════════════\n\n";

                if (context?.Replacements != null && context.Replacements.Count > 0)
                {
                    ResultTextBlock.Text += $"📊 MAPPINGS DÉTECTÉS ({context.Replacements.Count}) :\n";
                    foreach (var kvp in context.Replacements)
                    {
                        ResultTextBlock.Text += $"  • \"{kvp.Key}\" → {kvp.Value}\n";
                    }
                }
                else
                {
                    ResultTextBlock.Text += "⚠️ AUCUN REMPLACEMENT détecté (Normal si pas d'emails/téléphones)\n";
                }

                ResultTextBlock.Text += "\n═══════════════════════════════════════════════\n";
                ResultTextBlock.Text += "📋 LOGS DÉTAILLÉS :\n";
                ResultTextBlock.Text += _accumulatedLogs;
            }
            catch (Exception ex)
            {
                ResultTextBlock.Text = $"❌ ERREUR :\n\n{ex.Message}\n\n";
            }
        }

        private async void RetestButton_Click(object sender, RoutedEventArgs e)
        {
            await RunTestAsync();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
