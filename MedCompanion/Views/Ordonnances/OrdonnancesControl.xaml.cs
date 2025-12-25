using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MedCompanion.Dialogs;
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.ViewModels;

namespace MedCompanion.Views.Ordonnances
{
    public partial class OrdonnancesControl : UserControl
    {
        // Événements pour communiquer avec MainWindow
        public event EventHandler<string>? StatusChanged;

        // Ordonnances temporaires en cours de prévisualisation (avant sauvegarde)
        private OrdonnanceIDE? _pendingOrdonnance;
        private OrdonnanceBiologie? _pendingOrdonnanceBiologie;

        // Mode édition
        private bool _isEditMode = false;
        private FlowDocument? _originalDocument; // Pour annuler les modifications
        private OrdonnanceItem? _editingOrdonnance; // Ordonnance en cours d'édition

        public OrdonnancesControl()
        {
            InitializeComponent();

            // Abonner aux événements du MedicamentsControl
            MedicamentsControlPanel.StatusChanged += (s, msg) =>
            {
                StatusChanged?.Invoke(this, msg);
            };

            MedicamentsControlPanel.OrdonnanceGenerated += (s, e) =>
            {
                // Recharger la liste des ordonnances après génération
                if (DataContext is OrdonnanceViewModel viewModel)
                {
                    viewModel.LoadOrdonnances();
                    StatusChanged?.Invoke(this, "✅ Liste des ordonnances rafraîchie");
                }

                // Retourner à la liste des ordonnances
                MedicamentsPanel.Visibility = Visibility.Collapsed;
                OrdonnancesListGrid.Visibility = Visibility.Visible;
            };

            MedicamentsControlPanel.CancelRequested += (s, e) =>
            {
                // Retourner à la liste des ordonnances sans recharger
                MedicamentsPanel.Visibility = Visibility.Collapsed;
                OrdonnancesListGrid.Visibility = Visibility.Visible;
            };

            // Initialiser le MedicamentsControl avec les services lorsque le DataContext est défini
            DataContextChanged += OrdonnancesControl_DataContextChanged;
        }

        private void OrdonnancesControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (DataContext is OrdonnanceViewModel viewModel)
            {
                // Initialiser le MedicamentsControl avec les services nécessaires
                var letterService = new MedCompanion.LetterService(null!, null!, null!, null!, null!, null!, null!); // ✅ Ajout llmGatewayService
                var pathService = new MedCompanion.Services.PathService();
                var storageService = new MedCompanion.StorageService(pathService);
                var ordonnanceService = new MedCompanion.Services.OrdonnanceService(letterService, storageService, pathService);

                MedicamentsControlPanel.Initialize(ordonnanceService);

                // Propager le patient sélectionné vers MedicamentsControl
                if (viewModel.SelectedPatient != null)
                {
                    MedicamentsControlPanel.SetCurrentPatient(viewModel.SelectedPatient);
                }
            }
        }

        /// <summary>
        /// Gère le clic sur le bouton Médicaments - Affiche/masque le panel de médicaments
        /// </summary>
        private void MedicamentsOrdonnanceButton_Click(object sender, RoutedEventArgs e)
        {
            // Basculer la visibilité du panel médicaments
            if (MedicamentsPanel.Visibility == Visibility.Visible)
            {
                // Masquer le panel Médicaments, afficher la liste des ordonnances
                MedicamentsPanel.Visibility = Visibility.Collapsed;
                OrdonnancesListGrid.Visibility = Visibility.Visible;
                StatusChanged?.Invoke(this, "📋 Retour à la liste des ordonnances");
            }
            else
            {
                // Mettre à jour le patient sélectionné dans MedicamentsControl
                if (DataContext is OrdonnanceViewModel viewModel && viewModel.SelectedPatient != null)
                {
                    MedicamentsControlPanel.SetCurrentPatient(viewModel.SelectedPatient);
                }

                // Afficher le panel Médicaments, masquer la liste
                MedicamentsPanel.Visibility = Visibility.Visible;
                OrdonnancesListGrid.Visibility = Visibility.Collapsed;
                StatusChanged?.Invoke(this, "💊 Création d'ordonnance de médicaments");
            }
        }

        /// <summary>
        /// Gère le clic sur le bouton Générer Biologie - GÉNÈRE UNIQUEMENT LE PREVIEW
        /// </summary>
        private void BiologieOrdonnanceButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not OrdonnanceViewModel viewModel)
            {
                MessageBox.Show("Erreur : ViewModel non initialisé.", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (viewModel.SelectedPatient == null)
            {
                MessageBox.Show("Veuillez d'abord sélectionner un patient.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var dob = viewModel.SelectedPatient.DobFormatted ?? "";

                var dialog = new OrdonnanceBiologieDialog(
                    viewModel.SelectedPatient.Nom,
                    viewModel.SelectedPatient.Prenom,
                    dob
                );

                var mainWindow = Window.GetWindow(this);
                if (mainWindow != null)
                    dialog.Owner = mainWindow;

                if (dialog.ShowDialog() == true && dialog.Result != null)
                {
                    // Stocker l'ordonnance biologie temporairement (ne PAS sauvegarder encore)
                    _pendingOrdonnanceBiologie = dialog.Result;
                    _pendingOrdonnance = null; // Reset IDE ordonnance

                    // Générer le preview Markdown
                    if (DataContext is OrdonnanceViewModel vm)
                    {
                        var ordonnanceService = new MedCompanion.Services.OrdonnanceService(
                            null!, // Pas besoin du LetterService pour juste le markdown
                            null!, // Pas besoin du StorageService pour juste le markdown
                            null!  // Pas besoin du PathService pour juste le markdown
                        );

                        var markdown = ordonnanceService.GenerateOrdonnanceBiologieMarkdown(dialog.Result);

                        // Afficher le preview
                        OrdonnancePreviewText.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(markdown);

                        // Afficher le bouton Sauvegarder
                        SauvegarderOrdonnanceButton.Visibility = Visibility.Visible;

                        // Masquer le bouton Ouvrir (pas encore de DOCX)
                        ImprimerOrdonnanceButton.Visibility = Visibility.Collapsed;

                        StatusChanged?.Invoke(this, "📄 Aperçu généré - Cliquez sur 'Sauvegarder' pour enregistrer");
                    }
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
        /// Gère le clic sur le bouton Générer IDE - GÉNÈRE UNIQUEMENT LE PREVIEW
        /// </summary>
        private void IDEOrdonnanceButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not OrdonnanceViewModel viewModel)
            {
                MessageBox.Show("Erreur : ViewModel non initialisé.", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (viewModel.SelectedPatient == null)
            {
                MessageBox.Show("Veuillez d'abord sélectionner un patient.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var dob = viewModel.SelectedPatient.DobFormatted ?? "";

                var dialog = new OrdonnanceIDEDialog(
                    viewModel.SelectedPatient.Nom,
                    viewModel.SelectedPatient.Prenom,
                    dob
                );

                var mainWindow = Window.GetWindow(this);
                if (mainWindow != null)
                    dialog.Owner = mainWindow;

                if (dialog.ShowDialog() == true && dialog.Result != null)
                {
                    // Stocker l'ordonnance temporairement (ne PAS sauvegarder encore)
                    _pendingOrdonnance = dialog.Result;

                    // Générer le preview Markdown
                    if (DataContext is OrdonnanceViewModel vm)
                    {
                        var ordonnanceService = new MedCompanion.Services.OrdonnanceService(
                            null!, // Pas besoin du LetterService pour juste le markdown
                            null!, // Pas besoin du StorageService pour juste le markdown
                            null!  // Pas besoin du PathService pour juste le markdown
                        );

                        var markdown = ordonnanceService.GenerateOrdonnanceIDEMarkdown(_pendingOrdonnance);

                        // Afficher le preview
                        OrdonnancePreviewText.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(markdown);

                        // Afficher le bouton Sauvegarder
                        SauvegarderOrdonnanceButton.Visibility = Visibility.Visible;

                        // Masquer le bouton Ouvrir (pas encore de DOCX)
                        ImprimerOrdonnanceButton.Visibility = Visibility.Collapsed;

                        StatusChanged?.Invoke(this, "📄 Aperçu généré - Cliquez sur 'Sauvegarder' pour enregistrer");
                    }
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
        /// Sauvegarde l'ordonnance en attente (IDE ou Biologie) OU sauvegarde les modifications
        /// </summary>
        private void SauvegarderOrdonnanceButton_Click(object sender, RoutedEventArgs e)
        {
            // CAS 1: Mode édition - Sauvegarder les modifications
            if (_isEditMode && _editingOrdonnance != null)
            {
                SaveEditedOrdonnance();
                return;
            }

            // CAS 2: Nouvelle ordonnance - Sauvegarde normale
            if (_pendingOrdonnance == null && _pendingOrdonnanceBiologie == null)
            {
                MessageBox.Show("Aucune ordonnance à sauvegarder.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (DataContext is not OrdonnanceViewModel viewModel)
            {
                MessageBox.Show("Erreur : ViewModel non initialisé.", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                string? mdPath = null;
                string? docxPath = null;
                string? pdfPath = null;
                bool success = false;
                string message = "";

                // Cas 1: Ordonnance IDE
                if (_pendingOrdonnance != null)
                {
                    StatusChanged?.Invoke(this, "⏳ Sauvegarde de l'ordonnance IDE...");
                    (success, message, mdPath, docxPath, pdfPath) = viewModel.SaveOrdonnanceIDE(_pendingOrdonnance);
                    _pendingOrdonnance = null;
                }
                // Cas 2: Ordonnance Biologie
                else if (_pendingOrdonnanceBiologie != null)
                {
                    StatusChanged?.Invoke(this, "⏳ Sauvegarde de l'ordonnance biologie...");
                    (success, message, mdPath, docxPath, pdfPath) = viewModel.SaveOrdonnanceBiologie(_pendingOrdonnanceBiologie);
                    _pendingOrdonnanceBiologie = null;
                }

                if (success)
                {
                    // Recharger la liste
                    viewModel.LoadOrdonnances();

                    // Masquer le bouton Sauvegarder
                    SauvegarderOrdonnanceButton.Visibility = Visibility.Collapsed;

                    // Afficher le bouton Ouvrir si DOCX ou PDF disponible (priorité au PDF)
                    var fileToOpen = !string.IsNullOrEmpty(pdfPath) && File.Exists(pdfPath) ? pdfPath : docxPath;
                    if (!string.IsNullOrEmpty(fileToOpen) && File.Exists(fileToOpen))
                    {
                        ImprimerOrdonnanceButton.Visibility = Visibility.Visible;
                        ImprimerOrdonnanceButton.Tag = fileToOpen;
                    }

                    StatusChanged?.Invoke(this, message);

                    MessageBox.Show(
                        "Ordonnance sauvegardée avec succès !",
                        "Sauvegarde réussie",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
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
        /// Gère la sélection d'une ordonnance dans la liste
        /// </summary>
        private void OrdonnancesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (OrdonnancesList.SelectedItem == null)
            {
                // Masquer tous les boutons
                ModifierOrdonnanceButton.Visibility = Visibility.Collapsed;
                ImprimerOrdonnanceButton2.Visibility = Visibility.Collapsed;
                AvisIAOrdonnanceButton.Visibility = Visibility.Collapsed;
                SupprimerOrdonnanceButton.Visibility = Visibility.Collapsed;
                ImprimerOrdonnanceButton.Visibility = Visibility.Collapsed;
                SauvegarderOrdonnanceButton.Visibility = Visibility.Collapsed;
                OrdonnancePreviewText.Document = new FlowDocument();
                return;
            }

            // Afficher les 4 boutons d'action (Modifier, Imprimer, Avis IA, Supprimer)
            ModifierOrdonnanceButton.Visibility = Visibility.Visible;
            ImprimerOrdonnanceButton2.Visibility = Visibility.Visible;
            AvisIAOrdonnanceButton.Visibility = Visibility.Visible;
            SupprimerOrdonnanceButton.Visibility = Visibility.Visible;

            // Masquer le bouton Sauvegarder si on sélectionne une ordonnance existante
            SauvegarderOrdonnanceButton.Visibility = Visibility.Collapsed;
            _pendingOrdonnance = null; // Réinitialiser l'ordonnance IDE en attente
            _pendingOrdonnanceBiologie = null; // Réinitialiser l'ordonnance biologie en attente

            try
            {
                var ordonnanceItem = OrdonnancesList.SelectedItem as OrdonnanceItem;
                if (ordonnanceItem == null) return;

                var filePath = ordonnanceItem.MdPath;
                var docxPath = ordonnanceItem.DocxPath;

                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    var extension = Path.GetExtension(filePath).ToLower();

                    if (extension == ".md")
                    {
                        // Cas normal : fichier .md
                        var markdown = File.ReadAllText(filePath);
                        OrdonnancePreviewText.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(markdown);
                    }
                    else if (extension == ".docx")
                    {
                        // Cas orphelin : fichier .docx uniquement
                        var doc = new FlowDocument();
                        var para = new Paragraph(new Run("📄 Ordonnance IDE (fichier DOCX uniquement)\n\n" +
                            "Cette ordonnance n'a pas de version Markdown.\n" +
                            "Double-cliquez pour ouvrir le document."))
                        {
                            FontSize = 14,
                            Foreground = new SolidColorBrush(Color.FromRgb(52, 73, 94))
                        };
                        doc.Blocks.Add(para);
                        OrdonnancePreviewText.Document = doc;

                        // Pour les orphelins, docxPath est le même que filePath
                        docxPath = filePath;
                    }

                    // Stocker le chemin DOCX pour le bouton Imprimer
                    if (!string.IsNullOrEmpty(docxPath) && File.Exists(docxPath))
                    {
                        ImprimerOrdonnanceButton2.Tag = docxPath;
                    }
                }
                else
                {
                    OrdonnancePreviewText.Document = new FlowDocument();
                }
            }
            catch (Exception ex)
            {
                var errorDoc = new FlowDocument();
                var errorPara = new Paragraph(new Run($"❌ Erreur lors de l'affichage :\n{ex.Message}"))
                {
                    Foreground = new SolidColorBrush(Colors.Red)
                };
                errorDoc.Blocks.Add(errorPara);
                OrdonnancePreviewText.Document = errorDoc;
            }
        }

        /// <summary>
        /// Double-clic pour ouvrir l'ordonnance dans le programme par défaut (PDF en priorité)
        /// </summary>
        private void OrdonnancesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (OrdonnancesList.SelectedItem == null)
                return;

            try
            {
                var ordonnanceItem = OrdonnancesList.SelectedItem as OrdonnanceItem;
                if (ordonnanceItem == null) return;

                var docxPath = ordonnanceItem.DocxPath;
                
                // Essayer d'ouvrir le PDF en priorité
                string? pdfPath = null;
                if (!string.IsNullOrEmpty(docxPath))
                {
                    pdfPath = Path.ChangeExtension(docxPath, ".pdf");
                }

                // Priorité au PDF, sinon DOCX
                if (!string.IsNullOrEmpty(pdfPath) && File.Exists(pdfPath))
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = pdfPath,
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);

                    StatusChanged?.Invoke(this, "📄 PDF ouvert");
                }
                else if (!string.IsNullOrEmpty(docxPath) && File.Exists(docxPath))
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = docxPath,
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);

                    StatusChanged?.Invoke(this, "📄 DOCX ouvert (PDF non disponible)");
                }
                else
                {
                    MessageBox.Show("Fichier PDF/DOCX introuvable.", "Erreur",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'ouverture : {ex.Message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Supprime une ordonnance sélectionnée
        /// </summary>
        private void SupprimerOrdonnanceButton_Click(object sender, RoutedEventArgs e)
        {
            if (OrdonnancesList.SelectedItem == null)
            {
                MessageBox.Show("Veuillez sélectionner une ordonnance à supprimer.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var ordonnanceItem = OrdonnancesList.SelectedItem as OrdonnanceItem;
            if (ordonnanceItem == null)
            {
                MessageBox.Show("Erreur : impossible de récupérer les informations de l'ordonnance.", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var mdPath = ordonnanceItem.MdPath;

            if (string.IsNullOrEmpty(mdPath))
            {
                MessageBox.Show("Erreur : chemin de fichier invalide.", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var result = MessageBox.Show(
                $"Êtes-vous sûr de vouloir supprimer cette ordonnance ?\n\n{Path.GetFileName(mdPath)}",
                "Confirmer la suppression",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning
            );

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    if (DataContext is not OrdonnanceViewModel viewModel)
                    {
                        MessageBox.Show("Erreur : ViewModel non initialisé.", "Erreur",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    var (success, message) = viewModel.DeleteOrdonnance(mdPath);

                    if (success)
                    {
                        // Recharger la liste
                        viewModel.LoadOrdonnances();

                        // Reset preview
                        OrdonnancePreviewText.Document = new FlowDocument();
                        ImprimerOrdonnanceButton.Visibility = Visibility.Collapsed;

                        MessageBox.Show(message, "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                        StatusChanged?.Invoke(this, message);
                    }
                    else
                    {
                        MessageBox.Show(message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                        StatusChanged?.Invoke(this, $"❌ {message}");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erreur inattendue lors de la suppression :\n\n{ex.Message}",
                        "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusChanged?.Invoke(this, $"❌ Erreur: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Ouvre le document DOCX de l'ordonnance (ancien bouton dans preview)
        /// </summary>
        private void ImprimerOrdonnanceButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string docxPath && File.Exists(docxPath))
            {
                try
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = docxPath,
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);

                    StatusChanged?.Invoke(this, "📄 Ordonnance ouverte");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erreur ouverture : {ex.Message}", "Erreur",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Imprime directement le document DOCX de l'ordonnance sélectionnée
        /// </summary>
        private void ImprimerOrdonnanceButton2_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string docxPath && File.Exists(docxPath))
            {
                try
                {
                    StatusChanged?.Invoke(this, "🖨️ Envoi à l'imprimante...");

                    // Utiliser le verbe "print" pour imprimer directement avec l'application par défaut
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = docxPath,
                        Verb = "print",
                        UseShellExecute = true,
                        CreateNoWindow = true,
                        WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                    };

                    var process = System.Diagnostics.Process.Start(psi);

                    if (process != null)
                    {
                        // Attendre que le processus se termine (Word/LibreOffice lance l'impression puis se ferme)
                        // Timeout de 30 secondes pour éviter de bloquer l'UI
                        if (process.WaitForExit(30000))
                        {
                            StatusChanged?.Invoke(this, "✅ Document envoyé à l'imprimante");
                            MessageBox.Show(
                                "Le document a été envoyé à l'imprimante par défaut.",
                                "Impression",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information
                            );
                        }
                        else
                        {
                            StatusChanged?.Invoke(this, "⏳ Impression en cours...");
                            MessageBox.Show(
                                "L'impression est en cours.\nLe processus peut prendre quelques secondes.",
                                "Impression",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information
                            );
                        }
                    }
                    else
                    {
                        StatusChanged?.Invoke(this, "✅ Document envoyé à l'imprimante");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erreur lors de l'impression :\n\n{ex.Message}", "Erreur",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusChanged?.Invoke(this, $"❌ Erreur impression: {ex.Message}");
                    System.Diagnostics.Debug.WriteLine($"[ImprimerOrdonnanceButton2] ERREUR: {ex.Message}\n{ex.StackTrace}");
                }
            }
            else
            {
                MessageBox.Show("Fichier DOCX introuvable.", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// Modifie une ordonnance sélectionnée (active le mode édition)
        /// </summary>
        private void ModifierOrdonnanceButton_Click(object sender, RoutedEventArgs e)
        {
            if (OrdonnancesList.SelectedItem == null)
            {
                MessageBox.Show("Veuillez sélectionner une ordonnance à modifier.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var ordonnanceItem = OrdonnancesList.SelectedItem as OrdonnanceItem;
            if (ordonnanceItem == null)
            {
                MessageBox.Show("Erreur : impossible de récupérer les informations de l'ordonnance.", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Activer le mode édition
            _isEditMode = true;
            _editingOrdonnance = ordonnanceItem;

            // Sauvegarder le document original pour pouvoir annuler
            _originalDocument = CloneFlowDocument(OrdonnancePreviewText.Document);

            // Rendre le RichTextBox éditable
            OrdonnancePreviewText.IsReadOnly = false;
            OrdonnancePreviewText.Background = new SolidColorBrush(Color.FromRgb(255, 255, 224)); // Fond jaune clair

            // Masquer les boutons Modifier/Imprimer/Avis IA/Supprimer
            ModifierOrdonnanceButton.Visibility = Visibility.Collapsed;
            ImprimerOrdonnanceButton2.Visibility = Visibility.Collapsed;
            AvisIAOrdonnanceButton.Visibility = Visibility.Collapsed;
            SupprimerOrdonnanceButton.Visibility = Visibility.Collapsed;

            // Afficher les boutons Sauvegarder/Annuler
            SauvegarderOrdonnanceButton.Visibility = Visibility.Visible;
            SauvegarderOrdonnanceButton.Content = "💾 Enregistrer modifications";
            AnnulerModificationButton.Visibility = Visibility.Visible;

            StatusChanged?.Invoke(this, "✏️ Mode édition activé - Modifiez le texte puis cliquez sur 'Enregistrer'");
        }

        /// <summary>
        /// Annule les modifications en cours
        /// </summary>
        private void AnnulerModificationButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isEditMode || _originalDocument == null)
                return;

            // Restaurer le document original
            OrdonnancePreviewText.Document = _originalDocument;

            // Quitter le mode édition
            ExitEditMode();

            StatusChanged?.Invoke(this, "❌ Modifications annulées");
        }

        /// <summary>
        /// Quitte le mode édition et restaure l'affichage normal
        /// </summary>
        private void ExitEditMode()
        {
            _isEditMode = false;
            _originalDocument = null;
            _editingOrdonnance = null;

            // Rendre le RichTextBox non éditable
            OrdonnancePreviewText.IsReadOnly = true;
            OrdonnancePreviewText.Background = new SolidColorBrush(Color.FromRgb(250, 250, 250)); // Fond gris clair

            // Masquer les boutons Sauvegarder/Annuler
            SauvegarderOrdonnanceButton.Visibility = Visibility.Collapsed;
            SauvegarderOrdonnanceButton.Content = "💾 Sauvegarder"; // Restaurer le contenu original
            AnnulerModificationButton.Visibility = Visibility.Collapsed;

            // Réafficher les boutons Modifier/Imprimer/Avis IA/Supprimer si une ordonnance est sélectionnée
            if (OrdonnancesList.SelectedItem != null)
            {
                ModifierOrdonnanceButton.Visibility = Visibility.Visible;
                ImprimerOrdonnanceButton2.Visibility = Visibility.Visible;
                AvisIAOrdonnanceButton.Visibility = Visibility.Visible;
                SupprimerOrdonnanceButton.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Clone un FlowDocument pour pouvoir l'annuler
        /// </summary>
        private FlowDocument CloneFlowDocument(FlowDocument source)
        {
            var range = new TextRange(source.ContentStart, source.ContentEnd);
            using var stream = new System.IO.MemoryStream();
            range.Save(stream, System.Windows.DataFormats.XamlPackage);
            var clone = new FlowDocument();
            var cloneRange = new TextRange(clone.ContentStart, clone.ContentEnd);
            stream.Seek(0, System.IO.SeekOrigin.Begin);
            cloneRange.Load(stream, System.Windows.DataFormats.XamlPackage);
            return clone;
        }

        /// <summary>
        /// Sauvegarde les modifications effectuées sur une ordonnance en mode édition
        /// </summary>
        private void SaveEditedOrdonnance()
        {
            if (_editingOrdonnance == null)
            {
                MessageBox.Show("Erreur : aucune ordonnance en cours d'édition.", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                StatusChanged?.Invoke(this, "⏳ Sauvegarde des modifications...");

                // 1. Extraire le texte du RichTextBox
                var range = new TextRange(OrdonnancePreviewText.Document.ContentStart,
                                          OrdonnancePreviewText.Document.ContentEnd);
                string editedText = range.Text;

                if (string.IsNullOrWhiteSpace(editedText))
                {
                    MessageBox.Show("Le contenu de l'ordonnance est vide.", "Erreur",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 2. Vérifier que le fichier MD existe
                if (string.IsNullOrEmpty(_editingOrdonnance.MdPath) ||
                    !File.Exists(_editingOrdonnance.MdPath))
                {
                    MessageBox.Show("Fichier source introuvable.", "Erreur",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 3. Sauvegarder le texte modifié dans le fichier .md
                File.WriteAllText(_editingOrdonnance.MdPath, editedText, System.Text.Encoding.UTF8);

                System.Diagnostics.Debug.WriteLine($"[SaveEditedOrdonnance] Fichier MD mis à jour: {_editingOrdonnance.MdPath}");

                // 4. Supprimer les anciens fichiers DOCX et PDF avant régénération
                var oldDocxPath = Path.ChangeExtension(_editingOrdonnance.MdPath, ".docx");
                var oldPdfPath = Path.ChangeExtension(_editingOrdonnance.MdPath, ".pdf");

                if (!string.IsNullOrEmpty(oldDocxPath) && File.Exists(oldDocxPath))
                {
                    try
                    {
                        File.Delete(oldDocxPath);
                        System.Diagnostics.Debug.WriteLine($"[SaveEditedOrdonnance] Ancien DOCX supprimé: {oldDocxPath}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveEditedOrdonnance] Impossible de supprimer l'ancien DOCX: {ex.Message}");
                    }
                }

                if (!string.IsNullOrEmpty(oldPdfPath) && File.Exists(oldPdfPath))
                {
                    try
                    {
                        File.Delete(oldPdfPath);
                        System.Diagnostics.Debug.WriteLine($"[SaveEditedOrdonnance] Ancien PDF supprimé: {oldPdfPath}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveEditedOrdonnance] Impossible de supprimer l'ancien PDF: {ex.Message}");
                    }
                }

                // 5. Régénérer le DOCX ET le PDF via OrdonnanceService
                if (DataContext is OrdonnanceViewModel viewModel)
                {
                    var patientName = viewModel.SelectedPatient?.NomComplet;

                    if (!string.IsNullOrEmpty(patientName))
                    {
                        // Récupérer MainWindow pour accéder aux services
                        var mainWindow = Window.GetWindow(this) as MainWindow;

                        if (mainWindow != null)
                        {
                            // Utiliser OrdonnanceService pour régénérer DOCX + PDF
                            var ordonnanceService = new OrdonnanceService(
                                mainWindow.LetterService,
                                mainWindow.StorageService,
                                mainWindow.PathService
                            );

                            var (convertSuccess, convertMessage, docxPath, pdfPath) = ordonnanceService.ConvertMarkdownToDocxAndPdf(
                                patientName,
                                _editingOrdonnance.MdPath
                            );

                            System.Diagnostics.Debug.WriteLine($"[SaveEditedOrdonnance] Conversion - Success: {convertSuccess}, Message: {convertMessage}");
                            System.Diagnostics.Debug.WriteLine($"[SaveEditedOrdonnance] DOCX: {docxPath ?? "null"}, PDF: {pdfPath ?? "null"}");

                            // Sauvegarder le chemin MD pour resélectionner après rechargement
                            var editedMdPath = _editingOrdonnance.MdPath;

                            // Quitter le mode édition
                            ExitEditMode();

                            // Recharger la liste des ordonnances
                            viewModel.LoadOrdonnances();

                            // Resélectionner l'ordonnance modifiée pour afficher l'aperçu
                            var modifiedOrdonnance = viewModel.Ordonnances.FirstOrDefault(o => o.MdPath == editedMdPath);
                            if (modifiedOrdonnance != null)
                            {
                                viewModel.SelectedOrdonnance = modifiedOrdonnance;
                                System.Diagnostics.Debug.WriteLine($"[SaveEditedOrdonnance] Ordonnance resélectionnée: {editedMdPath}");
                            }

                            // Afficher le message de succès
                            if (convertSuccess)
                            {
                                var successMsg = !string.IsNullOrEmpty(pdfPath)
                                    ? "Les modifications ont été enregistrées avec succès.\nLes documents DOCX et PDF ont été régénérés."
                                    : "Les modifications ont été enregistrées avec succès.\nLe document DOCX a été régénéré (PDF non disponible).";

                                StatusChanged?.Invoke(this, "✅ Modifications enregistrées et documents régénérés");
                                MessageBox.Show(
                                    successMsg,
                                    "Sauvegarde réussie",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Information
                                );
                            }
                            else
                            {
                                StatusChanged?.Invoke(this, $"⚠️ Modifications enregistrées mais erreur conversion: {convertMessage}");
                                MessageBox.Show(
                                    $"Les modifications ont été enregistrées mais il y a eu une erreur lors de la régénération des documents:\n\n{convertMessage}",
                                    "Sauvegarde partielle",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Warning
                                );
                            }
                        }
                        else
                        {
                            // MainWindow non disponible, fallback simple
                            ExitEditMode();
                            viewModel.LoadOrdonnances();
                            StatusChanged?.Invoke(this, "✅ Modifications enregistrées (documents non régénérés)");
                            MessageBox.Show(
                                "Les modifications ont été enregistrées.\n(Documents DOCX/PDF non régénérés)",
                                "Sauvegarde réussie",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information
                            );
                        }
                    }
                    else
                    {
                        // Pas de patient sélectionné, juste sauvegarder le MD
                        ExitEditMode();
                        viewModel.LoadOrdonnances();
                        StatusChanged?.Invoke(this, "✅ Modifications enregistrées (documents non régénérés)");
                        MessageBox.Show(
                            "Les modifications ont été enregistrées.",
                            "Sauvegarde réussie",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information
                        );
                    }
                }
                else
                {
                    // Pas de ViewModel, juste informer
                    ExitEditMode();
                    StatusChanged?.Invoke(this, "✅ Modifications enregistrées");
                    MessageBox.Show(
                        "Les modifications ont été enregistrées.",
                        "Sauvegarde réussie",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la sauvegarde :\n\n{ex.Message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusChanged?.Invoke(this, $"❌ Erreur: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[SaveEditedOrdonnance] ERREUR: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Demande un avis IA sur l'ordonnance sélectionnée (médicaments, IDE, biologie)
        /// Utilise LLMGatewayService pour l'anonymisation automatique
        /// </summary>
        private async void AvisIAOrdonnanceButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. Vérifier qu'une ordonnance est sélectionnée
                if (OrdonnancesList.SelectedItem is not OrdonnanceItem selectedOrdonnance)
                {
                    MessageBox.Show(
                        "Veuillez sélectionner une ordonnance dans la liste.",
                        "Aucune ordonnance sélectionnée",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                    return;
                }

                // 2. Vérifier que le fichier MD existe
                if (string.IsNullOrEmpty(selectedOrdonnance.MdPath) || !File.Exists(selectedOrdonnance.MdPath))
                {
                    MessageBox.Show(
                        "Le fichier source de l'ordonnance est introuvable.",
                        "Fichier manquant",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    return;
                }

                // 3. Récupérer le MainWindow pour accéder aux services
                var mainWindow = Window.GetWindow(this) as MainWindow;
                if (mainWindow == null)
                {
                    MessageBox.Show(
                        "Impossible d'accéder aux services (MainWindow introuvable).",
                        "Erreur",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    return;
                }

                // 4. Démarrer le BusyService
                var busyService = BusyService.Instance;
                var cancellationToken = busyService.Start("Analyse de l'ordonnance par IA", canCancel: true);
                busyService.UpdateStep("Lecture du contenu de l'ordonnance...");

                StatusChanged?.Invoke(this, "🔍 Analyse de l'ordonnance en cours...");

                // 5. Lire le contenu markdown de l'ordonnance
                string mdContent = File.ReadAllText(selectedOrdonnance.MdPath, Encoding.UTF8);

                if (string.IsNullOrWhiteSpace(mdContent))
                {
                    busyService.Stop();
                    MessageBox.Show(
                        "L'ordonnance est vide.",
                        "Ordonnance vide",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                    StatusChanged?.Invoke(this, "⚠️ Ordonnance vide");
                    return;
                }

                // 6. Détecter le type d'ordonnance depuis le contenu
                string ordonnanceType;
                string ordonnanceTypeLabel;
                if (mdContent.Contains("# ORDONNANCE DE SOINS INFIRMIERS") || mdContent.Contains("SOINS INFIRMIERS"))
                {
                    ordonnanceType = "IDE";
                    ordonnanceTypeLabel = "Soins infirmiers (IDE)";
                }
                else if (mdContent.Contains("# ORDONNANCE DE BIOLOGIE") || mdContent.Contains("BIOLOGIE") || mdContent.Contains("Examens demandés"))
                {
                    ordonnanceType = "BIOLOGIE";
                    ordonnanceTypeLabel = "Biologie";
                }
                else
                {
                    ordonnanceType = "MEDICAMENTS";
                    ordonnanceTypeLabel = "Médicaments";
                }

                busyService.UpdateStep("Préparation du contexte patient...");

                // 7. Récupérer le contexte patient (sera anonymisé automatiquement par LLMGatewayService)
                string patientContext = "Aucun contexte disponible";
                string? patientName = null;
                var selectedPatient = mainWindow.PatientIndex.GetAllPatients()
                    .FirstOrDefault(p => selectedOrdonnance.MdPath.Contains($"{p.Nom}_{p.Prenom}"));

                if (selectedPatient != null)
                {
                    patientName = selectedPatient.NomComplet;

                    var (hasContext, contextText, contextInfo) = mainWindow.ContextLoader.GetContextBundle(
                        selectedPatient.NomComplet,
                        null
                    );

                    if (hasContext)
                    {
                        patientContext = $"Patient: {selectedPatient.Prenom} {selectedPatient.Nom}\n" +
                                       $"Âge: {selectedPatient.Age ?? 0} ans\n" +
                                       $"Sexe: {selectedPatient.Sexe ?? "non renseigné"}\n\n" +
                                       $"{contextText}";
                    }
                }

                // 8. Construire le prompt adapté au type d'ordonnance
                string systemPrompt = GetAvisIASystemPrompt(ordonnanceType);

                string userPrompt = $@"Contexte patient:
{patientContext}

Type d'ordonnance: {ordonnanceTypeLabel}

Contenu de l'ordonnance:
{mdContent}

Donne un avis ULTRA-CONCIS (max 4-5 lignes avec pictogrammes).";

                // 9. Vérifier si l'utilisateur a demandé l'annulation
                if (cancellationToken.IsCancellationRequested)
                {
                    busyService.Stop();
                    StatusChanged?.Invoke(this, "❌ Analyse annulée par l'utilisateur");
                    return;
                }

                // 10. Basculer vers l'onglet Discussion/Chat (AssistantTabControl)
                mainWindow.AssistantTabControl.SelectedIndex = 0; // Index 0 = onglet Discussion (Chat)

                // 11. Vérifier que le ChatViewModel est disponible
                var chatViewModel = mainWindow.ChatControlPanel?.ChatViewModel;
                if (chatViewModel == null)
                {
                    busyService.Stop();
                    MessageBox.Show(
                        "Le système de chat n'est pas initialisé.",
                        "Erreur",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    return;
                }

                // 12. Afficher un message d'introduction dans le chat
                chatViewModel.AddSystemMessage(
                    "Système",
                    $"📋 Analyse de l'ordonnance du {selectedOrdonnance.Date:dd/MM/yyyy}\n" +
                    $"Type: {ordonnanceTypeLabel}\n" +
                    $"⏳ Demande d'avis IA en cours...",
                    Colors.Gray,
                    isFromAI: false
                );

                busyService.UpdateStep("Appel à l'IA en cours...");
                StatusChanged?.Invoke(this, "⏳ Analyse de l'ordonnance en cours...");

                // 13. Appeler l'IA via LLMGatewayService (anonymisation automatique)
                var messages = new List<(string role, string content)>
                {
                    ("user", userPrompt)
                };

                var (success, response, error) = await mainWindow.LLMGatewayService.ChatAsync(
                    systemPrompt,
                    messages,
                    patientName,  // Nom du patient pour charger ses métadonnées (anonymisation auto)
                    maxTokens: 1000,
                    cancellationToken  // Passer le token d'annulation
                );

                // Arrêter le BusyService
                busyService.Stop();

                // Vérifier si l'opération a été annulée
                if (cancellationToken.IsCancellationRequested || error == "Opération annulée par l'utilisateur")
                {
                    chatViewModel.AddSystemMessage(
                        "Système",
                        "❌ Analyse annulée par l'utilisateur",
                        Colors.Orange,
                        isFromAI: false
                    );
                    StatusChanged?.Invoke(this, "❌ Analyse annulée par l'utilisateur");
                    return;
                }

                if (!success || string.IsNullOrEmpty(response))
                {
                    chatViewModel.AddSystemMessage(
                        "Système",
                        $"❌ Erreur lors de l'analyse:\n{error ?? response}",
                        Colors.Red,
                        isFromAI: false
                    );
                    StatusChanged?.Invoke(this, "❌ Erreur lors de l'analyse");
                    return;
                }

                // 14. Afficher la réponse dans le chat
                chatViewModel.AddSystemMessage(
                    "🤖 Avis IA",
                    response,
                    Color.FromRgb(155, 89, 182), // Violet #9B59B6
                    isFromAI: true  // Pour le rendu Markdown avec pictogrammes
                );

                StatusChanged?.Invoke(this, "✅ Avis IA généré avec succès");
            }
            catch (Exception ex)
            {
                // S'assurer que le BusyService est arrêté en cas d'erreur
                BusyService.Instance.Stop();

                MessageBox.Show(
                    $"Erreur lors de l'analyse:\n\n{ex.Message}",
                    "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                StatusChanged?.Invoke(this, $"❌ Erreur: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[AvisIAOrdonnanceButton_Click] ERREUR: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Retourne le system prompt adapté au type d'ordonnance
        /// </summary>
        private string GetAvisIASystemPrompt(string ordonnanceType)
        {
            return ordonnanceType switch
            {
                "IDE" => @"Tu es un psychiatre qui donne un AVIS RAPIDE sur une ordonnance de soins infirmiers.

RÉPONSE ULTRA-CONCISE EXIGÉE (MAX 4-5 LIGNES):

Utilise UNIQUEMENT ces pictogrammes:
✅ OK - Prescription cohérente
⚠️ ATTENTION - Point de vigilance
❌ ALERTE - Problème identifié
🏥 SOINS - Recommandation sur les soins
📅 FRÉQUENCE - Remarque sur la fréquence/durée

FORMAT:
- 1 ligne par point important
- Pictogramme + texte très court (max 10 mots)
- Si tout OK: juste ""✅ Ordonnance IDE cohérente""",

                "BIOLOGIE" => @"Tu es un psychiatre qui donne un AVIS RAPIDE sur une ordonnance de biologie.

RÉPONSE ULTRA-CONCISE EXIGÉE (MAX 4-5 LIGNES):

Utilise UNIQUEMENT ces pictogrammes:
✅ OK - Bilan pertinent
⚠️ ATTENTION - Examen manquant ou surveillance
❌ ALERTE - Problème identifié
🔬 EXAMEN - Suggestion d'examen complémentaire
📊 SUIVI - Recommandation de suivi

FORMAT:
- 1 ligne par point important
- Pictogramme + texte très court (max 10 mots)
- Si tout OK: juste ""✅ Bilan biologique cohérent""",

                _ => @"Tu es un psychiatre qui donne un AVIS RAPIDE sur une ordonnance de médicaments.

RÉPONSE ULTRA-CONCISE EXIGÉE (MAX 4-5 LIGNES):

Utilise UNIQUEMENT ces pictogrammes:
✅ OK - Pas de problème majeur
⚠️ ATTENTION - Surveillance nécessaire
❌ ALERTE - Interaction/contre-indication
💊 EFFETS - Effets secondaires à surveiller
🔄 ALTERNATIVE - Suggestion d'alternative si pertinent

FORMAT:
- 1 ligne par point important
- Pictogramme + texte très court (max 10 mots)
- Si tout OK: juste ""✅ Ordonnance cohérente""

Exemple:
✅ Ordonnance cohérente avec le diagnostic
⚠️ Surveiller sédation (association 2 psychotropes)
💊 Prise de poids possible - informer patient"
            };
        }

        /// <summary>
        /// Formate la liste des médicaments pour l'analyse IA
        /// </summary>
        private string FormatMedicamentsForAI(List<MedicamentPrescrit> medicaments)
        {
            var sb = new StringBuilder();
            sb.AppendLine("MÉDICAMENTS PRESCRITS:\n");

            for (int i = 0; i < medicaments.Count; i++)
            {
                var med = medicaments[i];
                sb.AppendLine($"{i + 1}. {med.Medicament.Denomination}");
                sb.AppendLine($"   Présentation: {med.Presentation?.Libelle ?? "Non renseignée"}");
                sb.AppendLine($"   Posologie: {med.Posologie}");
                sb.AppendLine($"   Durée: {med.Duree}");
                sb.AppendLine($"   Quantité: {med.Quantite} boîte(s)");
                sb.AppendLine($"   Renouvelable: {med.NombreRenouvellements} fois");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gère le clic sur le bouton Assistant IA - Génère une ordonnance via IA
        /// Utilise LLMGatewayService pour l'anonymisation automatique
        /// </summary>
        private async void AIAssistedOrdonnanceButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. Vérifier qu'un patient est sélectionné
                if (DataContext is not OrdonnanceViewModel viewModel)
                {
                    MessageBox.Show("Erreur : ViewModel non initialisé.", "Erreur",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (viewModel.SelectedPatient == null)
                {
                    MessageBox.Show("Veuillez d'abord sélectionner un patient.", "Information",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // 2. Ouvrir le dialog pour la demande utilisateur
                var dialog = new AIAssistedOrdonnanceDialog { Owner = Window.GetWindow(this) };
                if (dialog.ShowDialog() != true)
                {
                    return; // Utilisateur a annulé
                }

                string demandeUtilisateur = dialog.Demande;

                // 2b. Démarrer le BusyService avec overlay
                var busyService = BusyService.Instance;
                var cancellationToken = busyService.Start("Génération de l'ordonnance par IA", canCancel: true);
                busyService.UpdateStep("Préparation du contexte patient...");

                StatusChanged?.Invoke(this, "🤖 Génération de l'ordonnance en cours...");

                // 3. Récupérer le MainWindow pour accéder aux services
                var mainWindow = Window.GetWindow(this) as MainWindow;
                if (mainWindow == null)
                {
                    busyService.Stop();
                    MessageBox.Show("Impossible d'accéder aux services (MainWindow introuvable).", "Erreur",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 4. Récupérer le contexte patient (sera anonymisé automatiquement par LLMGatewayService)
                var selectedPatient = viewModel.SelectedPatient;

                var (hasContext, contextText, contextInfo) = mainWindow.ContextLoader.GetContextBundle(
                    selectedPatient.NomComplet,
                    null
                );

                // Construire le contexte patient (informations réelles - LLMGatewayService anonymisera)
                string patientContext = "Aucun contexte disponible";
                if (hasContext)
                {
                    patientContext = $"Patient: {selectedPatient.Prenom} {selectedPatient.Nom}\n" +
                                   $"Âge: {selectedPatient.Age ?? 0} ans\n" +
                                   $"Sexe: {selectedPatient.Sexe ?? "non renseigné"}\n\n" +
                                   $"{contextText}";
                }

                // 5. Construire le prompt pour l'IA
                string systemPrompt = @"Tu es un psychiatre qui génère des ordonnances.

INSTRUCTIONS:
1. Analyse la demande et détermine le type d'ordonnance (médicaments, IDE, ou biologie)
2. Génère l'ordonnance au format markdown approprié
3. Suis STRICTEMENT les formats ci-dessous selon le type

FORMAT MÉDICAMENTS (si médicaments demandés):
```
# ORDONNANCE

Date: [DATE ACTUELLE]

## Médicaments prescrits

### [NOM DU MÉDICAMENT]
- **Présentation**: [forme + dosage]
- **Posologie**: [posologie détaillée]
- **Durée**: [durée du traitement]
- **Quantité**: [nombre de boîtes]
- **Renouvellement**: [nombre de renouvellements ou Non renouvelable]
```

FORMAT IDE (si soins infirmiers demandés):
```
# ORDONNANCE DE SOINS INFIRMIERS

Date: [DATE ACTUELLE]

## Soins prescrits
[Liste à puces des soins demandés]

## Fréquence
[Fréquence des soins]

## Durée
[Durée de la prescription]
```

FORMAT BIOLOGIE (si examens biologiques demandés):
```
# ORDONNANCE DE BIOLOGIE

Date: [DATE ACTUELLE]

## Examens demandés
[Liste à puces des examens]
```

IMPORTANT:
- Commence TOUJOURS par # ORDONNANCE ou # ORDONNANCE DE SOINS INFIRMIERS ou # ORDONNANCE DE BIOLOGIE
- Utilise la date actuelle
- Sois précis et professionnel
- Respecte EXACTEMENT les formats ci-dessus";

                string userPrompt = $@"Contexte patient:
{patientContext}

Demande d'ordonnance:
{demandeUtilisateur}

Génère l'ordonnance complète au format markdown approprié.";

                // 6. Appeler l'IA via LLMGatewayService (anonymisation automatique)
                // Le service détecte automatiquement si provider cloud → anonymisation 3 phases
                busyService.UpdateStep("Appel à l'IA en cours...");

                var messages = new List<(string role, string content)>
                {
                    ("user", userPrompt)
                };

                // Vérifier si l'utilisateur a demandé l'annulation
                if (cancellationToken.IsCancellationRequested)
                {
                    busyService.Stop();
                    StatusChanged?.Invoke(this, "❌ Génération annulée par l'utilisateur");
                    return;
                }

                var (success, response, error) = await mainWindow.LLMGatewayService.ChatAsync(
                    systemPrompt,
                    messages,
                    selectedPatient.NomComplet,  // Nom du patient pour charger ses métadonnées
                    maxTokens: 2000,
                    cancellationToken  // Passer le token d'annulation
                );

                // Vérifier si l'opération a été annulée
                if (cancellationToken.IsCancellationRequested || error == "Opération annulée par l'utilisateur")
                {
                    busyService.Stop();
                    StatusChanged?.Invoke(this, "❌ Génération annulée par l'utilisateur");
                    return;
                }

                if (!success || string.IsNullOrEmpty(response))
                {
                    busyService.Stop();
                    MessageBox.Show($"Erreur lors de la génération:\n\n{error ?? response}", "Erreur",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusChanged?.Invoke(this, "❌ Erreur de génération");
                    return;
                }

                busyService.UpdateStep("Traitement de la réponse...");

                // 7. Détecter le type d'ordonnance depuis la réponse
                string prefix;
                string typeLabel;
                if (response.Contains("# ORDONNANCE DE SOINS INFIRMIERS"))
                {
                    prefix = "IDE_";
                    typeLabel = "IDE";
                }
                else if (response.Contains("# ORDONNANCE DE BIOLOGIE"))
                {
                    prefix = "BIO_";
                    typeLabel = "Biologie";
                }
                else
                {
                    prefix = "MED_";
                    typeLabel = "Médicaments";
                }

                // 8. Sauvegarder l'ordonnance (markdown uniquement pour le MVP)
                var pathService = mainWindow.PathService;
                var ordonnancesDir = pathService.GetOrdonnancesDirectory(selectedPatient.NomComplet);
                Directory.CreateDirectory(ordonnancesDir);

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var fileName = $"{prefix}Ordonnance_{timestamp}";
                var mdPath = Path.Combine(ordonnancesDir, fileName + ".md");


                // 7b. Injecter les informations patient et corriger la date
                // Remplacer la date placeholder ou incorrecte par la date actuelle
                response = System.Text.RegularExpressions.Regex.Replace(
                    response, 
                    @"Date:.*", 
                    $"Date: {DateTime.Now:dd/MM/yyyy}",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                // Insérer les infos patient après le titre
                // Trouver la position après le premier titre (# Titre)
                var lines = response.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
                int insertIndex = -1;
                
                for (int i = 0; i < lines.Count; i++)
                {
                    if (lines[i].StartsWith("# ") && i + 1 < lines.Count)
                    {
                        insertIndex = i + 1;
                        break;
                    }
                }

                if (insertIndex != -1)
                {
                    var patientInfo = new List<string>
                    {
                        "",
                        $"Patient : **{selectedPatient.Nom} {selectedPatient.Prenom}**"
                    };

                    if (!string.IsNullOrEmpty(selectedPatient.DobFormatted))
                    {
                        patientInfo.Add($"Né(e) le : {selectedPatient.DobFormatted}");
                    }
                    
                    patientInfo.Add("");
                    
                    lines.InsertRange(insertIndex, patientInfo);
                    response = string.Join(Environment.NewLine, lines);
                }

                busyService.UpdateStep("Sauvegarde du fichier Markdown...");
                File.WriteAllText(mdPath, response, Encoding.UTF8);

                // Instancier OrdonnanceService pour la conversion
                var letterService = mainWindow.LetterService;
                var storageService = mainWindow.StorageService;
                var ordonnanceService = new OrdonnanceService(letterService, storageService, pathService);

                // Générer DOCX et PDF
                busyService.UpdateStep("Génération des documents DOCX et PDF...");
                StatusChanged?.Invoke(this, "📄 Génération des documents DOCX et PDF...");

                var (convertSuccess, convertMessage, docxPath, pdfPath) = ordonnanceService.ConvertMarkdownToDocxAndPdf(
                    selectedPatient.NomComplet,
                    mdPath
                );

                // Arrêter le BusyService - travail terminé
                busyService.Stop();

                if (convertSuccess)
                {
                     StatusChanged?.Invoke(this, $"✅ {convertMessage}");
                }
                else
                {
                     StatusChanged?.Invoke(this, $"⚠️ {convertMessage}");
                }

                // 9. Rafraîchir la liste
                viewModel.LoadOrdonnances();
                StatusChanged?.Invoke(this, $"✅ Ordonnance {typeLabel} générée avec succès");

                MessageBox.Show(
                    $"Ordonnance {typeLabel} générée avec succès !\n\nFichier: {fileName}",
                    "Succès",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
            }
            catch (Exception ex)
            {
                // S'assurer que le BusyService est arrêté en cas d'erreur
                BusyService.Instance.Stop();

                MessageBox.Show(
                    $"Erreur lors de la génération de l'ordonnance:\n\n{ex.Message}",
                    "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                StatusChanged?.Invoke(this, $"❌ Erreur: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[AIAssistedOrdonnanceButton_Click] ERREUR: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
