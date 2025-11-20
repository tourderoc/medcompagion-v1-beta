using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using MedCompanion.Models;
using MedCompanion.Services;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Résultat du dialogue de création de courrier avec IA
    /// </summary>
    public class CreateLetterResult
    {
        public bool Success { get; set; }
        public MCCModel SelectedMCC { get; set; }
        public string UserRequest { get; set; }
        public LetterAnalysisResult Analysis { get; set; }
        public bool UseStandardGeneration { get; set; }
    }

    public partial class CreateLetterWithAIDialog : Window
    {
        private const int MIN_CHARS = 20;

        private readonly MCCMatchingService _matchingService;
        private readonly PatientContext _patientContext;

        public CreateLetterResult Result { get; private set; }

        public CreateLetterWithAIDialog(
            PromptReformulationService reformulationService,
            MCCLibraryService mccLibraryService,
            PatientContext patientContext = null)
        {
            InitializeComponent();

            if (reformulationService == null) throw new ArgumentNullException(nameof(reformulationService));
            if (mccLibraryService == null) throw new ArgumentNullException(nameof(mccLibraryService));

            // Créer le service de matching centralisé
            _matchingService = new MCCMatchingService(reformulationService, mccLibraryService);
            _patientContext = patientContext;

            Result = new CreateLetterResult { Success = false };
        }

        /// <summary>
        /// Gestion du changement de texte - validation et compteur
        /// </summary>
        private void RequestTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            var text = RequestTextBox.Text;
            var charCount = text.Length;

            // Mise à jour du compteur
            CharCountText.Text = $"{charCount} caractères";

            // Validation : minimum 20 caractères
            GenerateButton.IsEnabled = charCount >= MIN_CHARS;

            // Indication visuelle
            if (charCount > 0 && charCount < MIN_CHARS)
            {
                CharCountText.Foreground = System.Windows.Media.Brushes.OrangeRed;
                CharCountText.Text = $"{charCount}/{MIN_CHARS} caractères (minimum requis)";
            }
            else if (charCount >= MIN_CHARS)
            {
                CharCountText.Foreground = System.Windows.Media.Brushes.Green;
            }
            else
            {
                CharCountText.Foreground = System.Windows.Media.Brushes.Gray;
            }
        }

        /// <summary>
        /// Annulation du dialogue
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Result.Success = false;
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// Lancement de l'analyse et génération avec le nouveau service MCCMatchingService
        /// </summary>
        private async void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            var userRequest = RequestTextBox.Text.Trim();

            if (string.IsNullOrWhiteSpace(userRequest) || userRequest.Length < MIN_CHARS)
            {
                MessageBox.Show(
                    $"Veuillez décrire votre demande avec au moins {MIN_CHARS} caractères.",
                    "Demande trop courte",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            // Désactiver l'interface pendant le traitement
            SetUIBusy(true, "🚀 Analyse et matching MCC en cours...");

            try
            {
                // Utiliser le service centralisé pour toute l'orchestration
                ProgressText.Text = "🔍 Analyse et recherche du meilleur MCC...";
                
                var (success, matchResult, error) = await _matchingService.AnalyzeAndMatchAsync(
                    userRequest,
                    _patientContext
                );

                if (!success)
                {
                    MessageBox.Show(
                        $"Erreur lors de l'analyse :\n{error}",
                        "Erreur",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    SetUIBusy(false);
                    return;
                }

                // Afficher les logs dans la console de debug
                _matchingService.PrintMatchingLogs(matchResult);

                SetUIBusy(false);

                // Vérifier si un match a été trouvé
                if (matchResult.HasMatch)
                {
                    // MCC trouvé → Afficher la preview
                    System.Diagnostics.Debug.WriteLine($"[CreateLetter] ✅ MCC trouvé : {matchResult.SelectedMCC.Name} (score: {matchResult.NormalizedScore:F1}%)");

                    var previewDialog = new MCCMatchResultDialog(
                        matchResult.SelectedMCC, 
                        matchResult.NormalizedScore,  // Déjà en pourcentage
                        matchResult.Analysis)
                    {
                        Owner = this
                    };

                    var previewResult = previewDialog.ShowDialog();

                    if (previewResult == true)
                    {
                        // Utilisateur a confirmé → retourner avec le MCC
                        Result = new CreateLetterResult
                        {
                            Success = true,
                            SelectedMCC = matchResult.SelectedMCC,
                            UserRequest = userRequest,
                            Analysis = matchResult.Analysis,
                            UseStandardGeneration = false
                        };

                        DialogResult = true;
                        Close();
                    }
                }
                else
                {
                    // Pas de match → Proposer génération standard
                    System.Diagnostics.Debug.WriteLine($"[CreateLetter] ⚠️ Pas de match : {matchResult.FailureReason}");

                    var response = MessageBox.Show(
                        $"⚠️ Aucun modèle MCC pertinent trouvé\n\n" +
                        $"📚 Bibliothèque consultée : {matchResult.TotalMCCsChecked} templates\n" +
                        $"🎯 Meilleur score : {matchResult.RawScore:F1} pts ({matchResult.NormalizedScore:F1}%)\n" +
                        $"❌ Raison : {matchResult.FailureReason}\n\n" +
                        $"💡 L'IA va générer un courrier STANDARD en se basant uniquement sur votre demande et le contexte patient.\n\n" +
                        $"⚙️ Mode : Génération libre (sans template MCC)\n\n" +
                        $"Voulez-vous continuer avec la génération standard ?",
                        "Génération standard",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question
                    );

                    if (response == MessageBoxResult.Yes)
                    {
                        // Génération standard acceptée
                        Result = new CreateLetterResult
                        {
                            Success = true,
                            SelectedMCC = null,
                            UserRequest = userRequest,
                            Analysis = matchResult.Analysis,
                            UseStandardGeneration = true
                        };

                        DialogResult = true;
                        Close();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CreateLetter] ❌ Erreur critique : {ex.Message}");
                
                MessageBox.Show(
                    $"Une erreur est survenue :\n{ex.Message}",
                    "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );

                SetUIBusy(false);
            }
        }

        /// <summary>
        /// Active/désactive l'interface pendant le traitement
        /// </summary>
        private void SetUIBusy(bool isBusy, string progressMessage = "")
        {
            GenerateButton.IsEnabled = !isBusy;
            CancelButton.IsEnabled = !isBusy;
            RequestTextBox.IsReadOnly = isBusy;

            if (isBusy)
            {
                ProgressPanel.Visibility = Visibility.Visible;
                ProgressText.Text = progressMessage;
                ProgressBar.IsIndeterminate = true;
            }
            else
            {
                ProgressPanel.Visibility = Visibility.Collapsed;
                ProgressBar.IsIndeterminate = false;
            }
        }
    }
}
