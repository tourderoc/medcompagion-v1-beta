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
        public MCCModel? SelectedMCC { get; set; }
        public string UserRequest { get; set; } = string.Empty;
        public LetterAnalysisResult? Analysis { get; set; }
        public bool UseStandardGeneration { get; set; }
        public LetterGenerationOptions? Options { get; set; }
    }

    public partial class CreateLetterWithAIDialog : Window
    {
        private const int MIN_CHARS = 20;

        private readonly MCCMatchingService _matchingService;
        private readonly PatientContext? _patientContext;

        public CreateLetterResult Result { get; private set; }

        public CreateLetterWithAIDialog(
            PromptReformulationService reformulationService,
            MCCLibraryService mccLibraryService,
            PatientContext? patientContext = null)
        {
            InitializeComponent();

            if (reformulationService == null) throw new ArgumentNullException(nameof(reformulationService));
            if (mccLibraryService == null) throw new ArgumentNullException(nameof(mccLibraryService));

            // Créer le service de matching centralisé
            _matchingService = new MCCMatchingService(reformulationService, mccLibraryService);
            _patientContext = patientContext;

            // Configurer le bouton de dictée vocale pour cibler le TextBox de la demande
            VoiceButton.TargetTextBox = RequestTextBox;

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
        /// Toggle pour afficher/masquer les options avancées
        /// </summary>
        private void AdvancedOptionsToggle_Click(object sender, RoutedEventArgs e)
        {
            if (AdvancedOptionsPanel.Visibility == Visibility.Collapsed)
            {
                AdvancedOptionsPanel.Visibility = Visibility.Visible;
                AdvancedOptionsToggle.Content = "⚙️ Options avancées ▲";
            }
            else
            {
                AdvancedOptionsPanel.Visibility = Visibility.Collapsed;
                AdvancedOptionsToggle.Content = "⚙️ Options avancées ▼";
            }
        }

        /// <summary>
        /// Récupère les options sélectionnées dans les ComboBox
        /// </summary>
        /// <summary>
        /// Récupère les options sélectionnées dans les ComboBox
        /// </summary>
        private LetterGenerationOptions GetSelectedOptions()
        {
            var options = new LetterGenerationOptions();

            // Fonction locale pour extraire le texte (sans emoji) ou retourner null si "Automatique"
            string? ExtractValue(object selectedItem)
            {
                if (selectedItem is not System.Windows.Controls.ComboBoxItem item) return null;
                
                var text = item.Content?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(text) || text == "Automatique") return null;

                // Enlever l'emoji (premier caractère + espace) s'il y en a un
                // On suppose qu'il y a un emoji si on trouve un espace dans les 4 premiers caractères
                int spaceIndex = text.IndexOf(' ');
                if (spaceIndex > 0 && spaceIndex <= 4)
                {
                    return text.Substring(spaceIndex + 1).Trim();
                }
                
                return text;
            }

            // Traitement spécifique pour le destinataire (support du texte libre)
            string? recipient = null;
            if (RecipientCombo.SelectedItem is System.Windows.Controls.ComboBoxItem item)
            {
                recipient = ExtractValue(item);
            }
            else
            {
                // Texte libre saisi par l'utilisateur
                recipient = RecipientCombo.Text.Trim();
                if (string.IsNullOrEmpty(recipient) || recipient == "Automatique")
                {
                    recipient = null;
                }
            }
            options.Recipient = recipient ?? options.Recipient;

            options.Tone = ExtractValue(ToneCombo.SelectedItem) ?? options.Tone;
            options.Length = ExtractValue(LengthCombo.SelectedItem) ?? options.Length;
            options.Format = ExtractValue(FormatCombo.SelectedItem) ?? options.Format;
            options.PrudenceLevel = ExtractValue(PrudenceCombo.SelectedItem) ?? options.PrudenceLevel;
            options.Urgency = ExtractValue(UrgencyCombo.SelectedItem) ?? options.Urgency;

            return options;
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

            // Récupérer les options avancées
            var options = GetSelectedOptions();
            System.Diagnostics.Debug.WriteLine($"[CreateLetter] Options: {options}");

            // Enrichir la demande avec les options
            string enrichedRequest = userRequest + options.ToPromptEnrichment();

            // Vérifier si l'utilisateur veut utiliser la bibliothèque MCC
            bool useMCCLibrary = UseMCCLibraryCheckBox.IsChecked == true;

            if (!useMCCLibrary)
            {
                // Génération directe sans bibliothèque MCC
                System.Diagnostics.Debug.WriteLine("[CreateLetter] 📝 Génération sans bibliothèque MCC (choix utilisateur)");

                Result = new CreateLetterResult
                {
                    Success = true,
                    SelectedMCC = null,
                    UserRequest = enrichedRequest,
                    Analysis = null,
                    UseStandardGeneration = true,
                    Options = options
                };

                DialogResult = true;
                Close();
                return;
            }

            // Désactiver l'interface pendant le traitement
            SetUIBusy(true, "🚀 Analyse et matching MCC en cours...");

            try
            {
                // Utiliser le service centralisé pour toute l'orchestration
                ProgressText.Text = "🔍 Analyse et recherche du meilleur MCC...";
                
                var (success, matchResult, error) = await _matchingService.AnalyzeAndMatchAsync(
                    enrichedRequest,
                    _patientContext,
                    options
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
                _matchingService.PrintMatchingLogs(matchResult!);

                SetUIBusy(false);

                // Vérifier si un match a été trouvé
                if (matchResult!.HasMatch)
                {
                    System.Diagnostics.Debug.WriteLine($"[CreateLetter] ✅ {matchResult.TopMatches.Count} MCC(s) trouvé(s)");

                    MCCModel? selectedMCC = null;

                    // Si plusieurs MCCs sont disponibles, afficher le dialogue de sélection
                    if (matchResult.TopMatches != null && matchResult.TopMatches.Count > 1)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CreateLetter] 🎯 Affichage du dialogue de sélection ({matchResult.TopMatches.Count} MCCs)");

                        var selectionDialog = new MCCSelectionDialog(
                            matchResult.TopMatches,
                            matchResult.Analysis!)
                        {
                            Owner = this
                        };

                        var selectionResult = selectionDialog.ShowDialog();

                        if (selectionResult == true)
                        {
                            selectedMCC = selectionDialog.SelectedMCC;
                            System.Diagnostics.Debug.WriteLine($"[CreateLetter] ✅ Utilisateur a choisi : {selectedMCC?.Name}");
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[CreateLetter] ⚠️ Utilisateur a annulé la sélection");
                            return;
                        }
                    }
                    else
                    {
                        // Un seul MCC trouvé → Afficher la preview classique
                        System.Diagnostics.Debug.WriteLine($"[CreateLetter] ✅ MCC unique trouvé : {matchResult.SelectedMCC?.Name} (score: {matchResult.NormalizedScore:F1}%)");

                        var previewDialog = new MCCMatchResultDialog(
                            matchResult.SelectedMCC!,
                            matchResult.NormalizedScore,
                            matchResult.Analysis!)
                        {
                            Owner = this
                        };

                        var previewResult = previewDialog.ShowDialog();

                        if (previewResult == true)
                        {
                            selectedMCC = matchResult.SelectedMCC;
                        }
                        else
                        {
                            return;
                        }
                    }

                    // Utilisateur a sélectionné un MCC → retourner avec le MCC
                    if (selectedMCC != null)
                    {
                        Result = new CreateLetterResult
                        {
                            Success = true,
                            SelectedMCC = selectedMCC,
                            UserRequest = enrichedRequest,
                            Analysis = matchResult.Analysis,
                            UseStandardGeneration = false,
                            Options = options
                        };

                        DialogResult = true;
                        Close();
                    }
                }
                else if (matchResult.NormalizedScore >= 30.0 && matchResult.SelectedMCC != null)
                {
                    // Match partiel (30% ≤ score < 50%) → Proposer choix utilisateur
                    System.Diagnostics.Debug.WriteLine($"[CreateLetter] ⚠️ Match partiel : {matchResult.SelectedMCC.Name} (score: {matchResult.NormalizedScore:F1}%)");
                    
                    SetUIBusy(false);
                    ShowPartialMatchDialog(matchResult, enrichedRequest, options);
                }
                else
                {
                    // Score < 30% ou aucun MCC → Génération standard
                    System.Diagnostics.Debug.WriteLine($"[CreateLetter] ⚠️ Pas de match pertinent : {matchResult.FailureReason}");
                    
                    SetUIBusy(false);
                    ShowStandardGenerationDialog(matchResult, enrichedRequest, options);
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

        /// <summary>
        /// Affiche une popup pour les matchs partiels (30% ≤ score < 50%)
        /// </summary>
        private void ShowPartialMatchDialog(
            MCCMatchResult matchResult,
            string enrichedRequest,
            LetterGenerationOptions options)
        {
            var message =
                $"⚠️ MCC trouvé avec score partiel\n\n" +
                $"📚 Bibliothèque consultée : {matchResult.TotalMCCsChecked} templates\n" +
                $"🎯 Meilleur MCC : \"{matchResult.SelectedMCC?.Name}\"\n" +
                $"📊 Score : {matchResult.RawScore:F1} pts ({matchResult.NormalizedScore:F1}%)\n" +
                $"⚠️ Raison : {matchResult.FailureReason}\n\n" +
                $"💡 Ce MCC peut servir d'inspiration mais nécessitera adaptation.\n\n" +
                $"Que souhaitez-vous faire ?";

            var dialog = new CustomChoiceDialog(
                "MCC trouvé avec score partiel",
                message,
                "Utiliser ce MCC",
                "Génération standard",
                "Annuler"
            )
            {
                Owner = this
            };

            dialog.ShowDialog();

            switch (dialog.UserChoice)
            {
                case CustomChoiceDialog.Choice.Option1: // Utiliser ce MCC
                    System.Diagnostics.Debug.WriteLine($"[CreateLetter] ✅ Utilisateur a choisi d'utiliser le MCC partiel : {matchResult.SelectedMCC?.Name}");
                    Result = new CreateLetterResult
                    {
                        Success = true,
                        SelectedMCC = matchResult.SelectedMCC,
                        UserRequest = enrichedRequest,
                        Analysis = matchResult.Analysis,
                        UseStandardGeneration = false,
                        Options = options
                    };
                    DialogResult = true;
                    Close();
                    break;

                case CustomChoiceDialog.Choice.Option2: // Génération standard
                    System.Diagnostics.Debug.WriteLine($"[CreateLetter] ℹ️ Utilisateur a choisi la génération standard");
                    Result = new CreateLetterResult
                    {
                        Success = true,
                        SelectedMCC = null,
                        UserRequest = enrichedRequest,
                        Analysis = matchResult.Analysis,
                        UseStandardGeneration = true,
                        Options = options
                    };
                    DialogResult = true;
                    Close();
                    break;

                case CustomChoiceDialog.Choice.Cancel: // Annuler
                    System.Diagnostics.Debug.WriteLine($"[CreateLetter] ⚠️ Utilisateur a annulé (match partiel)");
                    // Ne rien faire, rester sur le dialogue
                    break;
            }
        }

        /// <summary>
        /// Affiche la popup de génération standard (score < 30% ou aucun MCC)
        /// </summary>
        private void ShowStandardGenerationDialog(
            MCCMatchResult matchResult,
            string enrichedRequest,
            LetterGenerationOptions options)
        {
            // Construire les infos d'analyse détaillées
            var analysisInfo = "";
            if (matchResult.Analysis != null)
            {
                var analysis = matchResult.Analysis;
                var keywords = analysis.Keywords != null && analysis.Keywords.Count > 0
                    ? string.Join(", ", analysis.Keywords)
                    : "Aucun";

                analysisInfo = $"📊 ANALYSE DE VOTRE DEMANDE :\n" +
                    $"   👥 Destinataire détecté : {analysis.Audience ?? "Non détecté"}\n" +
                    $"   📄 Type de document : {analysis.DocType ?? "Non détecté"}\n" +
                    $"   ✍️ Ton : {analysis.Tone ?? "Non détecté"}\n" +
                    $"   👶 Tranche d'âge : {analysis.AgeGroup ?? "Non détecté"}\n" +
                    $"   🔑 Mots-clés : {keywords}\n" +
                    $"   🎯 Confiance IA : {analysis.ConfidenceScore:F0}%\n\n";
            }

            var response = MessageBox.Show(
                $"⚠️ Aucun modèle MCC pertinent trouvé\n\n" +
                analysisInfo +
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
                System.Diagnostics.Debug.WriteLine($"[CreateLetter] ✅ Génération standard acceptée");
                Result = new CreateLetterResult
                {
                    Success = true,
                    SelectedMCC = null,
                    UserRequest = enrichedRequest,
                    Analysis = matchResult.Analysis,
                    UseStandardGeneration = true,
                    Options = options
                };
                DialogResult = true;
                Close();
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[CreateLetter] ⚠️ Génération standard refusée");
            }
        }
    }
}
