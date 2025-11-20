using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using MedCompanion.Dialogs;
using MedCompanion.ViewModels;

namespace MedCompanion.Views.Attestations
{
    /// <summary>
    /// UserControl pour la gestion des attestations
    /// </summary>
    public partial class AttestationsControl : UserControl
    {
        // Événements pour communiquer avec MainWindow
        public event EventHandler<string>? StatusChanged;

        // Attestation en attente de sauvegarde (preview seulement)
        private string? _pendingAttestationMarkdown;
        private string? _pendingAttestationType;

        public AttestationsControl()
        {
            InitializeComponent();

            // S'abonner aux événements du ViewModel quand le DataContext change
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // Se désabonner de l'ancien ViewModel
            if (e.OldValue is AttestationViewModel oldViewModel)
            {
                oldViewModel.StatusMessageChanged -= OnStatusMessageChanged;
                oldViewModel.AttestationContentLoaded -= OnAttestationContentLoaded;
                oldViewModel.ErrorOccurred -= OnErrorOccurred;
                oldViewModel.InfoMessageRequested -= OnInfoMessageRequested;
                oldViewModel.ConfirmationRequested -= OnConfirmationRequested;
                oldViewModel.FileOpenRequested -= OnFileOpenRequested;
                oldViewModel.FilePrintRequested -= OnFilePrintRequested;
                oldViewModel.AttestationInfoDialogRequested -= OnAttestationInfoDialogRequested;
                oldViewModel.CustomAttestationDialogRequested -= OnCustomAttestationDialogRequested;
            }

            // S'abonner au nouveau ViewModel
            if (e.NewValue is AttestationViewModel newViewModel)
            {
                newViewModel.StatusMessageChanged += OnStatusMessageChanged;
                newViewModel.AttestationContentLoaded += OnAttestationContentLoaded;
                newViewModel.ErrorOccurred += OnErrorOccurred;
                newViewModel.InfoMessageRequested += OnInfoMessageRequested;
                newViewModel.ConfirmationRequested += OnConfirmationRequested;
                newViewModel.FileOpenRequested += OnFileOpenRequested;
                newViewModel.FilePrintRequested += OnFilePrintRequested;
                newViewModel.AttestationInfoDialogRequested += OnAttestationInfoDialogRequested;
                newViewModel.CustomAttestationDialogRequested += OnCustomAttestationDialogRequested;
            }
        }

        #region Gestionnaires d'événements du ViewModel

        private void OnStatusMessageChanged(object? sender, string message)
        {
            StatusChanged?.Invoke(this, message);
        }

        private void OnAttestationContentLoaded(object? sender, string markdown)
        {
            // Afficher le contenu dans le RichTextBox
            AttestationPreviewText.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(markdown);

            // Stocker le markdown pour sauvegarde ultérieure
            _pendingAttestationMarkdown = markdown;

            // Afficher le bouton Sauvegarder
            SauvegarderPreviewButton.Visibility = Visibility.Visible;

            // Masquer les boutons d'action (puisqu'on est en mode preview, pas encore sauvegardé)
            AttestationReadButtonsGrid.Visibility = Visibility.Collapsed;
            AttestationEditButtonsGrid.Visibility = Visibility.Collapsed;
            ImprimerAttestationButton.Visibility = Visibility.Collapsed;
        }

        private void OnErrorOccurred(object? sender, (string title, string message) args)
        {
            MessageBox.Show(args.message, args.title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        private void OnInfoMessageRequested(object? sender, (string title, string message) args)
        {
            MessageBox.Show(args.message, args.title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OnConfirmationRequested(object? sender, (string title, string message, Action onConfirm) args)
        {
            var result = MessageBox.Show(args.message, args.title, MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                args.onConfirm?.Invoke();
            }
        }

        private void OnFileOpenRequested(object? sender, string filePath)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'ouverture du fichier :\n\n{ex.Message}",
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnFilePrintRequested(object? sender, string filePath)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true,
                    Verb = "print"
                };
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'impression :\n\n{ex.Message}",
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnAttestationInfoDialogRequested(object? sender, AttestationInfoDialog dialog)
        {
            var mainWindow = Window.GetWindow(this);
            if (mainWindow != null)
                dialog.Owner = mainWindow;

            if (dialog.ShowDialog() == true)
            {
                // Le ViewModel gère la génération via les événements
            }
        }

        private void OnCustomAttestationDialogRequested(object? sender, CustomAttestationDialog dialog)
        {
            var mainWindow = Window.GetWindow(this);
            if (mainWindow != null)
                dialog.Owner = mainWindow;

            dialog.ShowDialog();
        }

        #endregion

        #region Gestionnaires d'événements UI

        /// <summary>
        /// Gère le changement de sélection du type d'attestation
        /// </summary>
        private void AttestationTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not AttestationViewModel viewModel)
                return;

            // Activer le bouton "Générer attestation" si un template valide est sélectionné
            // (index > 0 pour ignorer le placeholder "-- Choisir un modèle --")
            if (viewModel.SelectedTemplate != null &&
                !string.IsNullOrEmpty(viewModel.SelectedTemplate.Type) &&
                viewModel.CurrentPatient != null)
            {
                GenererAttestationButton.IsEnabled = true;
            }
            else
            {
                GenererAttestationButton.IsEnabled = false;
            }
        }

        /// <summary>
        /// Gère le clic sur le bouton Générer attestation (standard)
        /// </summary>
        private void GenererAttestationButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not AttestationViewModel viewModel)
                return;

            // Stocker le type d'attestation pour la sauvegarde ultérieure
            if (viewModel.SelectedTemplate != null)
            {
                _pendingAttestationType = viewModel.SelectedTemplate.Type;
            }

            // La commande du ViewModel gère la génération et affiche le preview
            // L'événement AttestationContentLoaded sera déclenché pour afficher le bouton Sauvegarder
            viewModel.GenerateCommand?.Execute(null);
        }

        /// <summary>
        /// Sauvegarde l'attestation après preview
        /// </summary>
        private void SauvegarderPreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not AttestationViewModel viewModel)
                return;

            if (string.IsNullOrEmpty(_pendingAttestationMarkdown))
            {
                MessageBox.Show("Aucune attestation à sauvegarder.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (viewModel.CurrentPatient == null)
            {
                MessageBox.Show("Aucun patient sélectionné.", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                StatusChanged?.Invoke(this, "⏳ Sauvegarde de l'attestation...");

                // Utiliser la méthode du ViewModel pour sauvegarder
                var (success, message, mdPath, docxPath) = viewModel.SaveGeneratedAttestation(
                    _pendingAttestationType ?? "Autre",
                    _pendingAttestationMarkdown
                );

                if (success)
                {
                    // Masquer le bouton Sauvegarder
                    SauvegarderPreviewButton.Visibility = Visibility.Collapsed;

                    // Afficher les boutons d'action
                    AttestationReadButtonsGrid.Visibility = Visibility.Visible;
                    ImprimerAttestationButton.Visibility = Visibility.Visible;

                    // Réinitialiser les variables
                    _pendingAttestationMarkdown = null;
                    _pendingAttestationType = null;

                    StatusChanged?.Invoke(this, message);

                    MessageBox.Show("Attestation sauvegardée avec succès !", "Succès",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusChanged?.Invoke(this, $"❌ {message}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur : {ex.Message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusChanged?.Invoke(this, $"❌ Erreur: {ex.Message}");
            }
        }

        /// <summary>
        /// Gère la sélection d'une attestation dans la liste
        /// </summary>
        private void AttestationsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DataContext is not AttestationViewModel viewModel)
                return;

            if (viewModel.SelectedAttestation == null)
            {
                // Masquer tous les boutons si aucune sélection
                AttestationReadButtonsGrid.Visibility = Visibility.Collapsed;
                AttestationEditButtonsGrid.Visibility = Visibility.Collapsed;
                ImprimerAttestationButton.Visibility = Visibility.Collapsed;
                SauvegarderPreviewButton.Visibility = Visibility.Collapsed;
                return;
            }

            // Une attestation est sélectionnée
            // Charger le contenu et afficher les boutons
            var attestation = viewModel.SelectedAttestation;

            if (!string.IsNullOrEmpty(attestation.MdPath) && System.IO.File.Exists(attestation.MdPath))
            {
                try
                {
                    var markdown = System.IO.File.ReadAllText(attestation.MdPath);
                    AttestationPreviewText.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(markdown);
                }
                catch
                {
                    AttestationPreviewText.Document = new System.Windows.Documents.FlowDocument();
                }
            }

            // Masquer le bouton Sauvegarder (on affiche une attestation existante)
            SauvegarderPreviewButton.Visibility = Visibility.Collapsed;

            // Afficher les boutons de lecture
            AttestationReadButtonsGrid.Visibility = Visibility.Visible;
            ImprimerAttestationButton.Visibility = Visibility.Visible;
            AttestationEditButtonsGrid.Visibility = Visibility.Collapsed;
        }

        private void GenerateCustomAttestationButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not AttestationViewModel viewModel)
                return;

            // Stocker le type d'attestation pour la sauvegarde ultérieure
            _pendingAttestationType = "personnalisee";

            // Déclencher la commande du ViewModel
            viewModel.GenerateCustomCommand?.Execute(null);
        }

        private void AttestationsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DataContext is not AttestationViewModel viewModel)
                return;

            if (viewModel.SelectedAttestation != null)
            {
                // Déclencher la commande d'ouverture du fichier
                viewModel.OpenFileCommand?.Execute(null);
            }
        }

        private void SauvegarderAttestationButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not AttestationViewModel viewModel)
                return;

            // Récupérer le contenu modifié du RichTextBox
            var markdown = MarkdownFlowDocumentConverter.FlowDocumentToMarkdown(AttestationPreviewText.Document);

            // Déclencher la sauvegarde via le ViewModel
            viewModel.SaveModifiedCommand?.Execute(markdown);

            // Masquer les boutons d'édition, afficher les boutons de lecture
            AttestationEditButtonsGrid.Visibility = Visibility.Collapsed;
            AttestationReadButtonsGrid.Visibility = Visibility.Visible;
            ImprimerAttestationButton.Visibility = Visibility.Visible;

            // Remettre le RichTextBox en lecture seule
            AttestationPreviewText.IsReadOnly = true;
        }

        private void AnnulerAttestationButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not AttestationViewModel viewModel)
                return;

            // Annuler l'édition
            viewModel.CancelModifyCommand?.Execute(null);

            // Masquer les boutons d'édition, afficher les boutons de lecture
            AttestationEditButtonsGrid.Visibility = Visibility.Collapsed;
            AttestationReadButtonsGrid.Visibility = Visibility.Visible;
            ImprimerAttestationButton.Visibility = Visibility.Visible;

            // Remettre le RichTextBox en lecture seule
            AttestationPreviewText.IsReadOnly = true;
        }

        private void ModifierAttestationButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not AttestationViewModel viewModel)
                return;

            // Passer en mode édition
            viewModel.ModifyCommand?.Execute(null);

            // Afficher les boutons d'édition, masquer les boutons de lecture
            AttestationReadButtonsGrid.Visibility = Visibility.Collapsed;
            ImprimerAttestationButton.Visibility = Visibility.Collapsed;
            AttestationEditButtonsGrid.Visibility = Visibility.Visible;

            // Rendre le RichTextBox éditable
            AttestationPreviewText.IsReadOnly = false;
            AttestationPreviewText.Focus();
        }

        private void SupprimerAttestationButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not AttestationViewModel viewModel)
                return;

            // Déclencher la commande de suppression
            viewModel.DeleteCommand?.Execute(null);

            // Réinitialiser l'UI
            AttestationPreviewText.Document = new System.Windows.Documents.FlowDocument();
            AttestationEditButtonsGrid.Visibility = Visibility.Collapsed;
            AttestationReadButtonsGrid.Visibility = Visibility.Collapsed;
            ImprimerAttestationButton.Visibility = Visibility.Collapsed;
            SauvegarderPreviewButton.Visibility = Visibility.Collapsed;
        }

        private void ImprimerAttestationButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not AttestationViewModel viewModel)
                return;

            // Déclencher la commande d'impression
            viewModel.PrintCommand?.Execute(null);
        }

        #endregion
    }
}
