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

            // Initialiser le MedicamentsControl avec les services lorsque le DataContext est défini
            DataContextChanged += OrdonnancesControl_DataContextChanged;
        }

        private void OrdonnancesControl_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (DataContext is OrdonnanceViewModel viewModel)
            {
                // Initialiser le MedicamentsControl avec les services nécessaires
                var letterService = new MedCompanion.LetterService(null!, null!, null!, null!);
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
                bool success = false;
                string message = "";

                // Cas 1: Ordonnance IDE
                if (_pendingOrdonnance != null)
                {
                    StatusChanged?.Invoke(this, "⏳ Sauvegarde de l'ordonnance IDE...");
                    (success, message, mdPath, docxPath) = viewModel.SaveOrdonnanceIDE(_pendingOrdonnance);
                    _pendingOrdonnance = null;
                }
                // Cas 2: Ordonnance Biologie
                else if (_pendingOrdonnanceBiologie != null)
                {
                    StatusChanged?.Invoke(this, "⏳ Sauvegarde de l'ordonnance biologie...");
                    (success, message, mdPath, docxPath) = viewModel.SaveOrdonnanceBiologie(_pendingOrdonnanceBiologie);
                    _pendingOrdonnanceBiologie = null;
                }

                if (success)
                {
                    // Recharger la liste
                    viewModel.LoadOrdonnances();

                    // Masquer le bouton Sauvegarder
                    SauvegarderOrdonnanceButton.Visibility = Visibility.Collapsed;

                    // Afficher le bouton Ouvrir si DOCX disponible
                    if (!string.IsNullOrEmpty(docxPath) && File.Exists(docxPath))
                    {
                        ImprimerOrdonnanceButton.Visibility = Visibility.Visible;
                        ImprimerOrdonnanceButton.Tag = docxPath;
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
        /// Double-clic pour ouvrir l'ordonnance dans le programme par défaut
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

                if (!string.IsNullOrEmpty(docxPath) && File.Exists(docxPath))
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = docxPath,
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);

                    StatusChanged?.Invoke(this, "📄 Ordonnance ouverte");
                }
                else
                {
                    MessageBox.Show("Fichier DOCX introuvable.", "Erreur",
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

                // 4. Supprimer l'ancien DOCX pour éviter les doublons (important!)
                var oldDocxPath = Path.ChangeExtension(_editingOrdonnance.MdPath, ".docx");
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

                // 5. Régénérer le DOCX
                if (DataContext is OrdonnanceViewModel viewModel)
                {
                    var patientName = viewModel.SelectedPatient?.NomComplet;

                    if (!string.IsNullOrEmpty(patientName))
                    {
                        // Utiliser LetterService pour régénérer le DOCX
                        var letterService = new MedCompanion.LetterService(
                            null!,  // OpenAIService non nécessaire pour export
                            null!,  // ContextLoader non nécessaire pour export
                            null!,  // StorageService non nécessaire pour export
                            null!   // PatientContextService non nécessaire pour export
                        );

                        var (exportSuccess, exportMessage, docxPath) = letterService.ExportToDocx(
                            patientName,
                            editedText,
                            _editingOrdonnance.MdPath
                        );

                        System.Diagnostics.Debug.WriteLine($"[SaveEditedOrdonnance] Export DOCX - Success: {exportSuccess}, Message: {exportMessage}");

                        // Sauvegarder le chemin MD pour resélectionner après rechargement
                        var editedMdPath = _editingOrdonnance.MdPath;

                        // 5. Quitter le mode édition
                        ExitEditMode();

                        // 6. Recharger la liste des ordonnances
                        viewModel.LoadOrdonnances();

                        // 7. Resélectionner l'ordonnance modifiée pour afficher l'aperçu
                        var modifiedOrdonnance = viewModel.Ordonnances.FirstOrDefault(o => o.MdPath == editedMdPath);
                        if (modifiedOrdonnance != null)
                        {
                            viewModel.SelectedOrdonnance = modifiedOrdonnance;
                            System.Diagnostics.Debug.WriteLine($"[SaveEditedOrdonnance] Ordonnance resélectionnée: {editedMdPath}");
                        }

                        // 8. Afficher le message de succès
                        if (exportSuccess)
                        {
                            StatusChanged?.Invoke(this, "✅ Modifications enregistrées et document régénéré");
                            MessageBox.Show(
                                "Les modifications ont été enregistrées avec succès.\nLe document DOCX a été régénéré.",
                                "Sauvegarde réussie",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information
                            );
                        }
                        else
                        {
                            StatusChanged?.Invoke(this, $"⚠️ Modifications enregistrées mais erreur DOCX: {exportMessage}");
                            MessageBox.Show(
                                $"Les modifications ont été enregistrées mais il y a eu une erreur lors de la régénération du DOCX:\n\n{exportMessage}",
                                "Sauvegarde partielle",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning
                            );
                        }
                    }
                    else
                    {
                        // Pas de patient sélectionné, juste sauvegarder le MD
                        ExitEditMode();
                        viewModel.LoadOrdonnances();
                        StatusChanged?.Invoke(this, "✅ Modifications enregistrées (DOCX non régénéré)");
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
        /// Demande un avis IA sur l'ordonnance sélectionnée
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

                StatusChanged?.Invoke(this, "🔍 Analyse de l'ordonnance en cours...");

                // 4. Récupérer les services nécessaires
                var letterService = mainWindow.LetterService;
                var storageService = mainWindow.StorageService;
                var pathService = mainWindow.PathService;

                // 5. Parser le fichier markdown pour extraire les médicaments
                string mdContent = File.ReadAllText(selectedOrdonnance.MdPath, Encoding.UTF8);
                var ordonnanceService = new OrdonnanceService(letterService, storageService, pathService);
                var medicaments = ordonnanceService.ParseMedicamentsFromMarkdown(mdContent);

                if (medicaments == null || medicaments.Count == 0)
                {
                    MessageBox.Show(
                        "Aucun médicament trouvé dans cette ordonnance.",
                        "Ordonnance vide",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                    StatusChanged?.Invoke(this, "⚠️ Ordonnance vide");
                    return;
                }

                // 6. Formater les médicaments pour l'IA
                string medicamentsFormatted = FormatMedicamentsForAI(medicaments);

                // 7. Récupérer le contexte patient
                string patientContext = "Aucun contexte disponible";
                var selectedPatient = mainWindow.PatientIndex.GetAllPatients()
                    .FirstOrDefault(p => selectedOrdonnance.MdPath.Contains($"{p.Nom}_{p.Prenom}"));

                if (selectedPatient != null)
                {
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

                // 8. Construire le prompt pour l'IA
                string systemPrompt = @"Tu es un psychiatre expérimenté qui aide un confrère en donnant un AVIS CONSULTATIF sur une ordonnance.

🚨 IMPORTANT:
- Tu n'es PAS un système de validation officielle
- Tu n'es PAS une autorité qui approuve ou rejette les prescriptions
- Tu es un COLLÈGUE qui partage son regard clinique
- Tu ne prescris JAMAIS de médicaments
- Tu ne remplaces JAMAIS le jugement du médecin prescripteur

Ton rôle est de pointer:
- 🟥 Points critiques (interactions dangereuses, posologies hors AMM, contre-indications)
- 🟧 Points de vigilance (associations à surveiller, effets secondaires fréquents)
- 🟨 À surveiller / à expliquer (choix thérapeutiques qui peuvent sembler étonnants)
- 💬 Remarques contextuelles (pistes d'optimisation, alternatives possibles)

Réponds de manière structurée, bienveillante et concise. Utilise les emojis ci-dessus pour chaque section.
Si tout est cohérent, dis-le clairement. Reste humble et professionnel.";

                string userPrompt = $@"Voici l'ordonnance à analyser:

{medicamentsFormatted}

---

Contexte patient:
{patientContext}

---

Donne ton avis consultatif en tant que confrère.";

                // 9. Basculer vers l'onglet Discussion/Chat (AssistantTabControl)
                mainWindow.AssistantTabControl.SelectedIndex = 0; // Index 0 = onglet Discussion (Chat)

                // 10. Afficher un message d'introduction dans le chat
                mainWindow.AddChatMessage(
                    "Système",
                    $"📋 Analyse de l'ordonnance du {selectedOrdonnance.Date:dd/MM/yyyy}\n" +
                    $"Nombre de médicaments: {medicaments.Count}\n" +
                    $"Demande d'avis IA en cours...",
                    Colors.Gray
                );

                // 11. Appeler l'IA avec le contexte et le prompt utilisateur
                var (success, response) = await mainWindow.OpenAIService.ChatAvecContexteAsync(
                    string.Empty,  // contexte (déjà inclus dans userPrompt)
                    userPrompt,
                    null,  // historique
                    systemPrompt  // system prompt personnalisé
                );

                if (!success || string.IsNullOrEmpty(response))
                {
                    MessageBox.Show(
                        $"Erreur lors de la génération de l'avis IA:\n\n{response}",
                        "Erreur",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    StatusChanged?.Invoke(this, "❌ Erreur lors de l'analyse");
                    mainWindow.AddChatMessage(
                        "Système",
                        $"❌ Erreur: {response}",
                        Colors.Red
                    );
                    return;
                }

                // 12. Afficher la réponse dans le chat
                mainWindow.AddChatMessage(
                    "IA (Avis consultatif)",
                    response,
                    Color.FromRgb(155, 89, 182) // Violet #9B59B6
                );

                StatusChanged?.Invoke(this, "✅ Avis IA généré avec succès");

                // 13. Message de rappel
                mainWindow.AddChatMessage(
                    "Système",
                    "💡 Vous pouvez continuer la conversation dans ce chat pour approfondir l'analyse.",
                    Colors.Gray
                );
            }
            catch (Exception ex)
            {
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
    }
}
