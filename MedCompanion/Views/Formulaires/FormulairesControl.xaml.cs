using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MedCompanion.Models;
using MedCompanion.Dialogs;
using MedCompanion.Services;

namespace MedCompanion.Views.Formulaires
{
    /// <summary>
    /// UserControl pour la gestion des formulaires (MDPH, PAI)
    /// </summary>
    public partial class FormulairesControl : UserControl
    {
        private Services.FormulaireAssistantService? _formulaireService;
        private LetterService? _letterService;
        private Services.PatientIndexService? _patientIndex;
        private DocumentService? _documentService;
        private PathService? _pathService;
        private PatientIndexEntry? _currentPatient;

        public event EventHandler<string>? StatusChanged;

        public FormulairesControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initialise le contrôle avec les services nécessaires
        /// </summary>
        public void Initialize(
            Services.FormulaireAssistantService formulaireService,
            LetterService letterService,
            Services.PatientIndexService patientIndex,
            DocumentService documentService,
            PathService pathService)
        {
            _formulaireService = formulaireService;
            _letterService = letterService;
            _patientIndex = patientIndex;
            _documentService = documentService;
            _pathService = pathService;
        }

        /// <summary>
        /// Définit le patient courant et charge ses formulaires
        /// </summary>
        public void SetCurrentPatient(PatientIndexEntry? patient)
        {
            _currentPatient = patient;
            LoadPatientFormulaires();

            // Réinitialiser l'UI
            FormulaireTypeCombo.SelectedIndex = 0;
            PreremplirFormulaireButton.Visibility = Visibility.Collapsed;
            PreremplirFormulaireButton.IsEnabled = false;
            TestRemplirPdfButton.Visibility = Visibility.Collapsed;
            TestRemplirPdfButton.IsEnabled = false;
            OuvrirModelePAIButton.Visibility = Visibility.Collapsed;
            OuvrirModelePAIButton.IsEnabled = false;
        }

        private void FormulaireTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // SÉCURITÉ : Vérifier que tous les contrôles sont initialisés (évite NullRef pendant le chargement XAML)
            if (PreremplirFormulaireButton == null || TestRemplirPdfButton == null || OuvrirModelePAIButton == null)
                return;

            if (FormulaireTypeCombo.SelectedIndex <= 0 || _currentPatient == null)
            {
                // Aucun formulaire sélectionné ou pas de patient → Tout masquer
                PreremplirFormulaireButton.Visibility = Visibility.Collapsed;
                PreremplirFormulaireButton.IsEnabled = false;
                TestRemplirPdfButton.Visibility = Visibility.Collapsed;
                TestRemplirPdfButton.IsEnabled = false;
                OuvrirModelePAIButton.Visibility = Visibility.Collapsed;
                OuvrirModelePAIButton.IsEnabled = false;
                return;
            }

            var selectedItem = FormulaireTypeCombo.SelectedItem as ComboBoxItem;
            if (selectedItem?.Tag is string formulaireType)
            {
                if (formulaireType == "PAI")
                {
                    // PAI sélectionné → Afficher bouton "Ouvrir modèle", masquer boutons MDPH
                    PreremplirFormulaireButton.Visibility = Visibility.Collapsed;
                    PreremplirFormulaireButton.IsEnabled = false;
                    TestRemplirPdfButton.Visibility = Visibility.Collapsed;
                    TestRemplirPdfButton.IsEnabled = false;

                    OuvrirModelePAIButton.Visibility = Visibility.Visible;
                    OuvrirModelePAIButton.IsEnabled = true;

                    StatusChanged?.Invoke(this, "🏫 PAI sélectionné - Cliquez pour ouvrir le modèle PDF");
                }
                else if (formulaireType == "MDPH")
                {
                    // MDPH sélectionné → Afficher boutons IA et Test, masquer bouton Ouvrir PAI
                    OuvrirModelePAIButton.Visibility = Visibility.Collapsed;
                    OuvrirModelePAIButton.IsEnabled = false;

                    PreremplirFormulaireButton.Visibility = Visibility.Visible;
                    PreremplirFormulaireButton.IsEnabled = true;
                    TestRemplirPdfButton.Visibility = Visibility.Visible;
                    TestRemplirPdfButton.IsEnabled = true;

                    StatusChanged?.Invoke(this, "📋 MDPH sélectionné - Cliquez sur 'Pré-remplir avec l'IA' ou 'Tester remplissage PDF'");
                }
            }
        }

        private void PreremplirFormulaireButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPatient == null)
            {
                MessageBox.Show("Veuillez d'abord sélectionner un patient.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (FormulaireTypeCombo.SelectedIndex <= 0)
            {
                MessageBox.Show("Veuillez sélectionner un type de formulaire.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // Ouvrir le nouveau dialog Assistant MDPH avec vue split-view
                var dialog = new Dialogs.MDPHAssistantDialog(
                    _currentPatient,
                    _patientIndex!,
                    _formulaireService!,
                    _letterService!
                );

                dialog.Owner = Window.GetWindow(this);
                var result = dialog.ShowDialog();

                // Si l'utilisateur a sauvegardé, recharger la liste
                if (result == true)
                {
                    LoadPatientFormulaires();
                    StatusChanged?.Invoke(this, "✅ Formulaire MDPH généré et sauvegardé avec succès");
                }
                else
                {
                    StatusChanged?.Invoke(this, "Assistant MDPH fermé");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Erreur lors de l'ouverture de l'assistant MDPH :\n\n{ex.Message}",
                    "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );

                StatusChanged?.Invoke(this, $"❌ Erreur : {ex.Message}");
            }
        }

        /// <summary>
        /// Charge la liste des formulaires sauvegardés du patient
        /// </summary>
        private void LoadPatientFormulaires()
        {
            // Réinitialiser l'aperçu de la synthèse
            FormulaireSynthesisPreview.Text = "Sélectionnez un formulaire pour voir la synthèse.";
            FormulaireSynthesisPreview.Foreground = new SolidColorBrush(Colors.Gray);
            FormulaireSynthesisPreview.FontWeight = FontWeights.Normal;

            if (_currentPatient == null)
            {
                FormulairesList.ItemsSource = null;
                FormulairesCountLabel.Text = "0 formulaires";
                return;
            }

            try
            {
                var directoriesToScan = new List<string>();

                // 1. Dossier "legacy" (à la racine du patient) - utilisé par PAI
                var legacyDir = Path.Combine(_currentPatient.DirectoryPath, "formulaires");
                if (Directory.Exists(legacyDir))
                {
                    directoriesToScan.Add(legacyDir);
                }

                // 2. Dossier "nouveau" (par année) - utilisé par MDPH
                if (_pathService != null)
                {
                    var newDir = _pathService.GetFormulairesDirectory(_currentPatient.NomComplet);
                    // Éviter les doublons si c'est le même dossier
                    if (Directory.Exists(newDir) && !directoriesToScan.Contains(newDir, StringComparer.OrdinalIgnoreCase))
                    {
                        directoriesToScan.Add(newDir);
                    }
                }

                if (directoriesToScan.Count == 0)
                {
                    FormulairesList.ItemsSource = null;
                    FormulairesCountLabel.Text = "0 formulaires";
                    return;
                }

                var pdfFiles = new List<string>();
                // var docxFiles = new List<string>(); // DOCX masqués à la demande de l'utilisateur

                foreach (var dir in directoriesToScan)
                {
                    pdfFiles.AddRange(Directory.GetFiles(dir, "*.pdf", SearchOption.TopDirectoryOnly));
                    // docxFiles.AddRange(Directory.GetFiles(dir, "*.docx", SearchOption.TopDirectoryOnly));
                }

                var formulaires = new List<object>();

                // Traiter les PDF
                foreach (var pdfPath in pdfFiles)
                {
                    var fileName = Path.GetFileName(pdfPath);
                    var fileInfo = new FileInfo(pdfPath);

                    // Détecter le type (PAI ou MDPH)
                    string typeLabel;
                    if (fileName.StartsWith("PAI_", StringComparison.OrdinalIgnoreCase))
                    {
                        typeLabel = "🏫 PAI";
                    }
                    else if (fileName.StartsWith("MDPH_", StringComparison.OrdinalIgnoreCase))
                    {
                        typeLabel = "📋 MDPH";
                    }
                    else
                    {
                        typeLabel = "📄 Autre";
                    }

                    formulaires.Add(new
                    {
                        TypeLabel = typeLabel,
                        DateLabel = fileInfo.LastWriteTime.ToString("dd/MM/yyyy HH:mm"),
                        FileName = fileName,
                        FilePath = pdfPath,
                        Date = fileInfo.LastWriteTime
                    });
                }

                // DOCX masqués
                /*
                foreach (var docxPath in docxFiles)
                {
                    // ... (code masqué)
                }
                */

                // Trier par date décroissante
                var sortedFormulaires = formulaires.OrderByDescending(f =>
                    f.GetType().GetProperty("Date")?.GetValue(f) as DateTime?
                ).ToList();

                FormulairesList.ItemsSource = sortedFormulaires;

                // Mettre à jour le compteur
                var count = sortedFormulaires.Count;
                FormulairesCountLabel.Text = count == 0 ? "0 formulaires" :
                                            count == 1 ? "1 formulaire" :
                                            $"{count} formulaires";
            }
            catch (Exception ex)
            {
                StatusChanged?.Invoke(this, $"❌ Erreur chargement formulaires: {ex.Message}");
                FormulairesList.ItemsSource = null;
                FormulairesCountLabel.Text = "0 formulaires";
            }
        }

        /// <summary>
        /// Gestionnaire du double-clic sur un formulaire pour l'ouvrir
        /// </summary>
        private void FormulairesList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FormulairesList.SelectedItem == null)
                return;

            var item = FormulairesList.SelectedItem;
            var filePathProp = item.GetType().GetProperty("FilePath");

            if (filePathProp != null)
            {
                var filePath = filePathProp.GetValue(item) as string;

                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = filePath,
                            UseShellExecute = true
                        };
                        System.Diagnostics.Process.Start(psi);

                        StatusChanged?.Invoke(this, $"📄 Formulaire ouvert : {Path.GetFileName(filePath)}");
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Erreur ouverture : {ex.Message}", "Erreur",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    MessageBox.Show("Fichier introuvable.", "Erreur",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        /// <summary>
        /// Gestionnaire de sélection pour activer/désactiver le bouton Supprimer et afficher la synthèse
        /// </summary>
        private void FormulairesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            SupprimerFormulaireButton.IsEnabled = FormulairesList.SelectedItem != null;

            if (FormulairesList.SelectedItem == null || _currentPatient == null)
            {
                return;
            }

            var item = FormulairesList.SelectedItem;
            var filePathProp = item.GetType().GetProperty("FilePath");

            if (filePathProp != null)
            {
                var filePath = filePathProp.GetValue(item) as string;

                if (!string.IsNullOrEmpty(filePath))
                {
                    // Chercher le fichier .json correspondant
                    var jsonPath = Path.ChangeExtension(filePath, ".json");

                    if (File.Exists(jsonPath))
                    {
                        try
                        {
                            // Charger et désérialiser le JSON
                            var jsonContent = File.ReadAllText(jsonPath);

                            // Essayer de détecter si c'est un PAI ou MDPH
                            if (Path.GetFileName(filePath).StartsWith("MDPH_", StringComparison.OrdinalIgnoreCase))
                            {
                                var synthesis = System.Text.Json.JsonSerializer.Deserialize<MDPHSynthesis>(jsonContent);
                                if (synthesis != null)
                                {
                                    var demandesStr = synthesis.Demandes != null && synthesis.Demandes.Any() 
                                        ? string.Join("\n• ", synthesis.Demandes) 
                                        : "Aucune demande spécifique cochée";
                                    
                                    if (!string.IsNullOrWhiteSpace(synthesis.AutresDemandes))
                                    {
                                        demandesStr += $"\n\n📝 Autres demandes :\n{synthesis.AutresDemandes}";
                                    }

                                    var synthesisText = $"📋 SYNTHÈSE MDPH\n\n" +
                                                      $"📄 Fichier : {Path.GetFileName(filePath)}\n" +
                                                      $"📅 Date : {synthesis.DateCreation:dd/MM/yyyy HH:mm}\n" +
                                                      $"👤 Patient : {synthesis.Patient}\n\n" +
                                                      $"📌 Demandes formulées :\n" +
                                                      $"• {demandesStr}\n\n" +
                                                      $"━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
                                                      $"💡 Note : Double-cliquez sur le formulaire pour l'ouvrir.";

                                    FormulaireSynthesisPreview.Text = synthesisText;
                                    FormulaireSynthesisPreview.Foreground = new SolidColorBrush(Colors.Black);
                                    FormulaireSynthesisPreview.FontWeight = FontWeights.Normal;
                                    
                                    StatusChanged?.Invoke(this, $"✓ Synthèse MDPH affichée");
                                    return;
                                }
                            }
                            else
                            {
                                // Cas PAI existant
                                var synthesis = System.Text.Json.JsonSerializer.Deserialize<PAISynthesis>(jsonContent);

                                if (synthesis != null)
                                {
                                    // Construire la synthèse formatée avec emojis et formatage
                                    var synthesisText = $"📋 SYNTHÈSE PAI\n\n" +
                                                      $"📄 Fichier : {Path.GetFileName(filePath)}\n" +
                                                      $"📅 Date de création : {synthesis.DateCreation:dd/MM/yyyy HH:mm}\n" +
                                                      $"👤 Patient : {synthesis.Patient}\n\n" +
                                                      $"🎯 Motif du PAI :\n\n" +
                                                      $"{synthesis.Motif}\n\n" +
                                                      $"━━━━━━━━━━━━━━━━━━━━━━━━━\n\n" +
                                                      $"💡 Note : Double-cliquez sur le formulaire dans la liste pour l'ouvrir dans votre lecteur PDF.";

                                    // Afficher dans le TextBlock d'aperçu
                                    FormulaireSynthesisPreview.Text = synthesisText;
                                    FormulaireSynthesisPreview.Foreground = new SolidColorBrush(Colors.Black);
                                    FormulaireSynthesisPreview.FontWeight = FontWeights.Normal;

                                    StatusChanged?.Invoke(this, $"✓ Synthèse PAI affichée - Motif : {synthesis.Motif}");
                                    return;
                                }
                            }
                            }
                        catch (Exception ex)
                        {
                            StatusChanged?.Invoke(this, $"⚠️ Erreur lecture synthèse : {ex.Message}");
                        }
                    }
                    else
                    {
                        // Pas de synthèse disponible
                        StatusChanged?.Invoke(this, "⚠️ Aucune synthèse disponible pour ce formulaire");
                    }
                }
            }
        }

        /// <summary>
        /// Supprime un formulaire sélectionné
        /// </summary>
        private void SupprimerFormulaireButton_Click(object sender, RoutedEventArgs e)
        {
            if (FormulairesList.SelectedItem == null)
            {
                MessageBox.Show("Veuillez sélectionner un formulaire à supprimer.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var item = FormulairesList.SelectedItem;
            var filePathProp = item.GetType().GetProperty("FilePath");
            var fileNameProp = item.GetType().GetProperty("FileName");

            if (filePathProp != null && fileNameProp != null)
            {
                var filePath = filePathProp.GetValue(item) as string;
                var fileName = fileNameProp.GetValue(item) as string;

                if (string.IsNullOrEmpty(filePath))
                    return;

                var result = MessageBox.Show(
                    $"Êtes-vous sûr de vouloir supprimer ce formulaire ?\n\n{fileName}",
                    "Confirmer la suppression",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning
                );

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        // Supprimer le fichier
                        if (File.Exists(filePath))
                        {
                            File.Delete(filePath);
                        }

                        // Si c'est un PDF, chercher le .md et .json correspondants
                        if (filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                        {
                            var mdPath = Path.ChangeExtension(filePath, ".md");
                            if (File.Exists(mdPath))
                            {
                                File.Delete(mdPath);
                            }

                            var jsonPath = Path.ChangeExtension(filePath, ".json");
                            if (File.Exists(jsonPath))
                            {
                                File.Delete(jsonPath);
                            }
                        }

                        // Si c'est un DOCX, chercher le .md correspondant
                        if (filePath.EndsWith(".docx", StringComparison.OrdinalIgnoreCase))
                        {
                            var mdPath = Path.ChangeExtension(filePath, ".md");
                            if (File.Exists(mdPath))
                            {
                                File.Delete(mdPath);
                            }
                        }

                        StatusChanged?.Invoke(this, "✅ Formulaire supprimé");

                        // Recharger la liste
                        LoadPatientFormulaires();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Erreur suppression : {ex.Message}", "Erreur",
                            MessageBoxButton.OK, MessageBoxImage.Error);

                        StatusChanged?.Invoke(this, $"❌ Erreur : {ex.Message}");
                    }
                }
            }
        }

        private void OuvrirModelePAIButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPatient == null)
            {
                MessageBox.Show(
                    "Veuillez d'abord sélectionner un patient.",
                    "Aucun patient sélectionné",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            try
            {
                // Construire le chemin vers le PDF modèle
                var appDirectory = AppDomain.CurrentDomain.BaseDirectory;
                var paiModelPath = Path.Combine(appDirectory, "Assets", "Formulaires", "Dossier PAI.pdf");

                // Vérifier que le fichier modèle existe
                if (!File.Exists(paiModelPath))
                {
                    MessageBox.Show(
                        $"Le modèle PAI est introuvable :\n\n{paiModelPath}\n\n" +
                        "Veuillez vérifier que le fichier existe dans le dossier Assets/Formulaires/",
                        "Fichier introuvable",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );

                    StatusChanged?.Invoke(this, "❌ Modèle PAI introuvable");
                    return;
                }

                // Créer le dossier formulaires dans le dossier du patient
                var formulairesDir = Path.Combine(_currentPatient.DirectoryPath, "formulaires");
                if (!Directory.Exists(formulairesDir))
                {
                    Directory.CreateDirectory(formulairesDir);
                }

                // Générer le nom du fichier avec timestamp
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var paiFileName = $"PAI_{_currentPatient.Prenom}_{_currentPatient.Nom}_{timestamp}.pdf";
                var paiDestPath = Path.Combine(formulairesDir, paiFileName);

                // Copier le modèle vers le dossier du patient
                File.Copy(paiModelPath, paiDestPath, overwrite: false);

                // Ouvrir le PDF copié avec le lecteur par défaut
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = paiDestPath,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);

                // Ouvrir le dialogue pour renseigner le motif du PAI
                var motifDialog = new PAIMotifDialog();
                motifDialog.Owner = Window.GetWindow(this);

                if (motifDialog.ShowDialog() == true && !string.IsNullOrEmpty(motifDialog.Motif))
                {
                    // Créer et sauvegarder la synthèse en JSON
                    var synthesis = new PAISynthesis
                    {
                        Type = "PAI",
                        DateCreation = DateTime.Now,
                        Patient = _currentPatient.NomComplet,
                        Motif = motifDialog.Motif,
                        FileName = paiFileName
                    };

                    // Sauvegarder le JSON à côté du PDF
                    var jsonPath = Path.ChangeExtension(paiDestPath, ".json");
                    var jsonContent = System.Text.Json.JsonSerializer.Serialize(synthesis, new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                    File.WriteAllText(jsonPath, jsonContent, Encoding.UTF8);

                    StatusChanged?.Invoke(this, $"✅ PAI créé avec motif : {motifDialog.Motif}");
                }
                else
                {
                    // L'utilisateur a annulé ou n'a pas renseigné de motif
                    StatusChanged?.Invoke(this, "⚠️ PAI créé sans motif enregistré");
                }

                // Rafraîchir la liste des formulaires
                LoadPatientFormulaires();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Erreur lors de la création du formulaire PAI :\n\n{ex.Message}",
                    "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );

                StatusChanged?.Invoke(this, $"❌ Erreur : {ex.Message}");
            }
        }

        /// <summary>
        /// Test de remplissage automatique du PDF MDPH
        /// </summary>
        private void TestRemplirPdfButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. Vérifier qu'un patient est sélectionné
                if (_currentPatient == null)
                {
                    MessageBox.Show(
                        "Veuillez d'abord sélectionner un patient.",
                        "Aucun patient sélectionné",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning
                    );
                    return;
                }

                StatusChanged?.Invoke(this, "🧪 Remplissage du PDF test en cours...");

                // 2. Chemins
                string templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Formulaires", "mdph_test.pdf");

                if (!File.Exists(templatePath))
                {
                    MessageBox.Show(
                        $"Le template PDF de test est introuvable:\n{templatePath}",
                        "Fichier manquant",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    StatusChanged?.Invoke(this, "❌ Template PDF introuvable");
                    return;
                }

                // 3. Créer le dossier de destination si nécessaire
                if (_pathService == null)
                {
                    MessageBox.Show("Service PathService non initialisé.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string formulairesFolder = _pathService.GetFormulairesDirectory(_currentPatient.NomComplet);

                if (!Directory.Exists(formulairesFolder))
                {
                    Directory.CreateDirectory(formulairesFolder);
                }

                // 4. Générer le nom du fichier de sortie
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string outputFileName = $"MDPH_Test_{timestamp}.pdf";
                string outputPath = Path.Combine(formulairesFolder, outputFileName);

                // 5. Créer le service et remplir le formulaire
                var pdfFillerService = new PDFFormFillerService();
                var (success, filledPath, error) = pdfFillerService.FillMDPHTestForm(
                    _currentPatient,
                    templatePath,
                    outputPath
                );

                if (!success)
                {
                    MessageBox.Show(
                        $"Erreur lors du remplissage du PDF:\n\n{error}",
                        "Erreur",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    StatusChanged?.Invoke(this, $"❌ Erreur: {error}");
                    return;
                }

                // 6. Succès - Proposer d'ouvrir le PDF
                StatusChanged?.Invoke(this, $"✅ PDF test créé: {outputFileName}");

                var result = MessageBox.Show(
                    $"PDF test créé avec succès!\n\nEmplacement:\n{filledPath}\n\nVoulez-vous ouvrir le PDF?",
                    "Succès",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Information
                );

                if (result == MessageBoxResult.Yes)
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = filledPath,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception openEx)
                    {
                        MessageBox.Show(
                            $"Impossible d'ouvrir le PDF:\n\n{openEx.Message}",
                            "Erreur",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning
                        );
                    }
                }

                // 7. Rafraîchir la liste des formulaires
                LoadPatientFormulaires();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Erreur inattendue lors du test de remplissage PDF:\n\n{ex.Message}",
                    "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                StatusChanged?.Invoke(this, $"❌ Erreur: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[TestRemplirPdfButton_Click] ERREUR: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
