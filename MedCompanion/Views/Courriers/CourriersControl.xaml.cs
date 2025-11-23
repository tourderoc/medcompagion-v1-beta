using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.Dialogs;

namespace MedCompanion.Views.Courriers;

public partial class CourriersControl : UserControl
{
    // Services
    private LetterService? _letterService;
    private PathService? _pathService;
    private PatientIndexService? _patientIndex;
    private MCCLibraryService? _mccLibrary;
    private LetterRatingService? _letterRatingService;

    // État
    private PatientIndexEntry? _selectedPatient;
    private string? _currentEditingFilePath;
    private Dictionary<string, string> _letterTemplates = new();

    // Métadonnées de génération pour évaluation
    private string? _lastGeneratedLetterMCCId = null;
    private string? _lastGeneratedLetterMCCName = null;

    // Events
    public event EventHandler<string>? StatusChanged;
    public event Action<string, string, Color>? AddChatMessageRequested;
    public event EventHandler? CreateLetterWithAIRequested;

    public CourriersControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initialise le contrôle avec les services nécessaires
    /// </summary>
    public void Initialize(
        LetterService letterService,
        PathService pathService,
        PatientIndexService patientIndex,
        MCCLibraryService mccLibrary,
        LetterRatingService letterRatingService,
        Dictionary<string, string> letterTemplates)
    {
        _letterService = letterService;
        _pathService = pathService;
        _patientIndex = patientIndex;
        _mccLibrary = mccLibrary;
        _letterRatingService = letterRatingService;
        _letterTemplates = letterTemplates;

        // Charger les templates MCC dans le ComboBox
        LoadCustomTemplates();
    }

    /// <summary>
    /// Définit le patient courant et recharge les courriers
    /// </summary>
    public void SetCurrentPatient(PatientIndexEntry? patient)
    {
        _selectedPatient = patient;
        RefreshLettersList();

        // Réinitialiser l'interface
        LetterEditText.Document = new FlowDocument();
        LetterEditText.IsReadOnly = true;
        _currentEditingFilePath = null;

        // Cacher tous les boutons
        NoterLetterButton.Visibility = Visibility.Collapsed;
        ModifierLetterButton.Visibility = Visibility.Collapsed;
        SupprimerLetterButton.Visibility = Visibility.Collapsed;
        ImprimerLetterButton.Visibility = Visibility.Collapsed;
        SauvegarderLetterButton.Visibility = Visibility.Collapsed;
        AnnulerLetterButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Recharge les templates MCC
    /// </summary>
    public void ReloadTemplates()
    {
        LoadCustomTemplates();
    }

    /// <summary>
    /// Réinitialise complètement le contrôle (appelé lors du changement de patient)
    /// </summary>
    public void Reset()
    {
        _selectedPatient = null;
        _currentEditingFilePath = null;
        _lastGeneratedLetterMCCId = null;
        _lastGeneratedLetterMCCName = null;

        // Vider la liste des courriers
        LettersList.SelectedItem = null;
        LettersList.ItemsSource = null;

        // Réinitialiser l'éditeur
        LetterEditText.Document = new FlowDocument();
        LetterEditText.IsReadOnly = true;
        LetterEditText.Background = new SolidColorBrush(Color.FromRgb(250, 250, 250));

        // Cacher tous les boutons
        NoterLetterButton.Visibility = Visibility.Collapsed;
        ModifierLetterButton.Visibility = Visibility.Collapsed;
        SupprimerLetterButton.Visibility = Visibility.Collapsed;
        ImprimerLetterButton.Visibility = Visibility.Collapsed;
        SauvegarderLetterButton.Visibility = Visibility.Collapsed;
        SauvegarderLetterButton.IsEnabled = false;
        AnnulerLetterButton.Visibility = Visibility.Collapsed;

        // Réinitialiser le ComboBox
        TemplateLetterCombo.SelectedIndex = 0;
    }

    /// <summary>
    /// Affiche un brouillon de courrier dans l'éditeur (appelé depuis le Chat)
    /// </summary>
    public void SetDraft(string markdown, string? mccId = null, string? mccName = null)
    {
        // Désélectionner tout courrier existant
        LettersList.SelectedItem = null;
        _currentEditingFilePath = null;

        // Stocker les métadonnées MCC si fournies
        _lastGeneratedLetterMCCId = mccId;
        _lastGeneratedLetterMCCName = mccName;

        // Convertir le markdown en FlowDocument et l'afficher
        LetterEditText.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(markdown);
        LetterEditText.IsReadOnly = false;
        LetterEditText.Background = new SolidColorBrush(Colors.White);

        // Afficher les boutons appropriés pour un nouveau brouillon
        NoterLetterButton.Visibility = Visibility.Collapsed;
        ModifierLetterButton.Visibility = Visibility.Collapsed;
        SupprimerLetterButton.Visibility = Visibility.Collapsed;
        ImprimerLetterButton.Visibility = Visibility.Collapsed;
        SauvegarderLetterButton.Visibility = Visibility.Visible;
        SauvegarderLetterButton.IsEnabled = true;
        SauvegarderLetterButton.Background = new SolidColorBrush(Color.FromRgb(39, 174, 96)); // Vert
        AnnulerLetterButton.Visibility = Visibility.Visible;

        RaiseStatus("✅ Brouillon généré - Vous pouvez le modifier puis sauvegarder");
    }

    private void RaiseStatus(string message)
    {
        StatusChanged?.Invoke(this, message);
    }

    // ===== HANDLERS COURRIERS =====

    private async void TemplateLetterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TemplateLetterCombo.SelectedIndex <= 0)
            return;

        try
        {
            var selectedItem = TemplateLetterCombo.SelectedItem as ComboBoxItem;
            if (selectedItem == null)
                return;

            var templateName = selectedItem.Content as string;
            if (string.IsNullOrEmpty(templateName) || !_letterTemplates.ContainsKey(templateName))
                return;

            // Détecter si c'est un MCC et stocker les métadonnées
            if (templateName.StartsWith("[MCC]"))
            {
                var mccName = templateName.Substring(6).Trim();
                var mcc = _mccLibrary?.GetAllMCCs().FirstOrDefault(m => m.Name == mccName);
                if (mcc != null)
                {
                    _lastGeneratedLetterMCCId = mcc.Id;
                    _lastGeneratedLetterMCCName = mcc.Name;
                }
            }
            else
            {
                _lastGeneratedLetterMCCId = null;
                _lastGeneratedLetterMCCName = null;
            }

            var templateMarkdown = _letterTemplates[templateName];

            // Afficher le modèle brut
            LetterEditText.IsReadOnly = false;
            LetterEditText.Background = new SolidColorBrush(Colors.White);
            LetterEditText.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(templateMarkdown);

            _currentEditingFilePath = null;

            // Masquer boutons de lecture, activer Sauvegarder
            NoterLetterButton.Visibility = Visibility.Collapsed;
            ModifierLetterButton.Visibility = Visibility.Collapsed;
            SupprimerLetterButton.Visibility = Visibility.Collapsed;
            ImprimerLetterButton.Visibility = Visibility.Collapsed;
            SauvegarderLetterButton.Visibility = Visibility.Visible;
            SauvegarderLetterButton.IsEnabled = true;

            // Si toggle activé ET patient sélectionné → Adapter avec IA
            if (AutoAdaptAIToggle.IsChecked == true && _selectedPatient != null && _letterService != null)
            {
                RaiseStatus("⏳ Adaptation IA en cours...");

                TemplateLetterCombo.IsEnabled = false;
                SauvegarderLetterButton.IsEnabled = false;
                SauvegarderLetterButton.Background = new SolidColorBrush(Color.FromRgb(189, 195, 199));

                try
                {
                    var patientMetadata = _patientIndex?.GetMetadata(_selectedPatient.Id);

                    var (success, adaptedMarkdown, error) = await _letterService.AdaptTemplateWithAIAsync(
                        _selectedPatient.NomComplet,
                        templateName,
                        templateMarkdown
                    );

                    if (success && !string.IsNullOrEmpty(adaptedMarkdown))
                    {
                        try
                        {
                            var newDocument = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(adaptedMarkdown);
                            LetterEditText.Document = newDocument;
                        }
                        catch
                        {
                            var fallbackDoc = new FlowDocument();
                            fallbackDoc.Blocks.Add(new Paragraph(new Run(adaptedMarkdown)));
                            LetterEditText.Document = fallbackDoc;
                        }

                        // Détecter les informations manquantes
                        var (hasMissing, missingFields, availableInfo) = _letterService.DetectMissingInfo(
                            templateName,
                            adaptedMarkdown,
                            patientMetadata
                        );

                        if (hasMissing)
                        {
                            RaiseStatus("❓ Informations manquantes...");

                            var dialog = new MissingInfoDialog(missingFields);
                            dialog.Owner = Window.GetWindow(this);

                            if (dialog.ShowDialog() == true && dialog.CollectedInfo != null)
                            {
                                var allInfo = new Dictionary<string, string>(availableInfo);
                                foreach (var kvp in dialog.CollectedInfo)
                                {
                                    allInfo[kvp.Key] = kvp.Value;
                                }

                                RaiseStatus("⏳ Ré-adaptation avec infos complètes...");

                                var (success2, updatedMarkdown, error2) =
                                    await _letterService.AdaptTemplateWithMissingInfoAsync(
                                        _selectedPatient.NomComplet,
                                        templateName,
                                        templateMarkdown,
                                        allInfo
                                    );

                                if (success2 && !string.IsNullOrEmpty(updatedMarkdown))
                                {
                                    LetterEditText.Document = MarkdownFlowDocumentConverter
                                        .MarkdownToFlowDocument(updatedMarkdown);

                                    SauvegarderLetterButton.IsEnabled = true;
                                    SauvegarderLetterButton.Background = new SolidColorBrush(Color.FromRgb(39, 174, 96));

                                    RaiseStatus("✅ Courrier complété avec toutes les informations");
                                }
                                else
                                {
                                    SauvegarderLetterButton.IsEnabled = true;
                                    SauvegarderLetterButton.Background = new SolidColorBrush(Color.FromRgb(39, 174, 96));
                                    RaiseStatus($"⚠️ Erreur ré-adaptation : {error2}");
                                }
                            }
                            else
                            {
                                SauvegarderLetterButton.IsEnabled = true;
                                SauvegarderLetterButton.Background = new SolidColorBrush(Color.FromRgb(39, 174, 96));
                                RaiseStatus("⚠️ Infos manquantes - Complétez manuellement les placeholders");
                            }
                        }
                        else
                        {
                            SauvegarderLetterButton.IsEnabled = true;
                            SauvegarderLetterButton.Background = new SolidColorBrush(Color.FromRgb(39, 174, 96));
                            RaiseStatus("✅ Adaptation IA terminée - Vous pouvez modifier puis sauvegarder");
                        }
                    }
                    else
                    {
                        SauvegarderLetterButton.IsEnabled = true;
                        SauvegarderLetterButton.Background = new SolidColorBrush(Color.FromRgb(39, 174, 96));
                        RaiseStatus($"⚠️ Adaptation échouée - Modèle brut affiché : {error}");
                    }
                }
                catch (Exception adaptEx)
                {
                    RaiseStatus($"⚠️ Erreur adaptation IA - Modèle brut affiché : {adaptEx.Message}");
                }
                finally
                {
                    TemplateLetterCombo.IsEnabled = true;
                }
            }
            else if (AutoAdaptAIToggle.IsChecked == true && _selectedPatient == null)
            {
                RaiseStatus("⚠️ Modèle brut affiché - Sélectionnez un patient pour l'adaptation IA");
            }
            else
            {
                RaiseStatus($"📝 Modèle '{templateName}' chargé - Modifiez-le puis sauvegardez");
            }

            TemplateLetterCombo.SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            RaiseStatus($"❌ Erreur chargement modèle: {ex.Message}");
        }
    }

    private void LettersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LettersList.SelectedItem != null)
        {
            var item = LettersList.SelectedItem;
            var filePathProp = item.GetType().GetProperty("FilePath");
            var filePath = (string)filePathProp!.GetValue(item)!;

            try
            {
                var markdown = File.ReadAllText(filePath);
                _currentEditingFilePath = filePath;

                // Recharger les métadonnées MCC
                var metaPath = filePath + ".meta.json";
                if (File.Exists(metaPath))
                {
                    try
                    {
                        var metaJson = File.ReadAllText(metaPath);
                        var metadata = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(metaJson);

                        if (metadata.TryGetProperty("mccId", out var mccIdProp))
                            _lastGeneratedLetterMCCId = mccIdProp.GetString();

                        if (metadata.TryGetProperty("mccName", out var mccNameProp))
                            _lastGeneratedLetterMCCName = mccNameProp.GetString();
                    }
                    catch
                    {
                        _lastGeneratedLetterMCCId = null;
                        _lastGeneratedLetterMCCName = null;
                    }
                }
                else
                {
                    _lastGeneratedLetterMCCId = null;
                    _lastGeneratedLetterMCCName = null;
                }

                LetterEditText.IsReadOnly = true;
                LetterEditText.Background = new SolidColorBrush(Color.FromRgb(250, 250, 250));
                LetterEditText.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(markdown);

                NoterLetterButton.Visibility = Visibility.Visible;
                NoterLetterButton.Tag = filePath;
                ModifierLetterButton.Visibility = Visibility.Visible;
                SupprimerLetterButton.Visibility = Visibility.Visible;
                ImprimerLetterButton.Visibility = Visibility.Visible;
                SauvegarderLetterButton.Visibility = Visibility.Collapsed;
                AnnulerLetterButton.Visibility = Visibility.Collapsed;

                RaiseStatus("📄 Courrier chargé (lecture seule)");
            }
            catch (Exception ex)
            {
                RaiseStatus($"❌ Erreur lecture: {ex.Message}");
            }
        }
        else
        {
            NoterLetterButton.Visibility = Visibility.Collapsed;
            ModifierLetterButton.Visibility = Visibility.Collapsed;
            SupprimerLetterButton.Visibility = Visibility.Collapsed;
            ImprimerLetterButton.Visibility = Visibility.Collapsed;
            SauvegarderLetterButton.Visibility = Visibility.Visible;
        }
    }

    private void LettersList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (LettersList.SelectedItem != null)
        {
            var item = LettersList.SelectedItem;
            var filePathProp = item.GetType().GetProperty("FilePath");
            var filePath = (string)filePathProp!.GetValue(item)!;

            try
            {
                var docxPath = Path.ChangeExtension(filePath, ".docx");

                if (File.Exists(docxPath))
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = docxPath,
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(psi);
                    RaiseStatus("📄 Courrier ouvert dans LibreOffice");
                }
                else
                {
                    RaiseStatus("⚠️ Fichier .docx introuvable. Sauvegardez d'abord le courrier.");
                }
            }
            catch (Exception ex)
            {
                RaiseStatus($"❌ Erreur ouverture: {ex.Message}");
            }
        }
    }

    private void ImprimerLetterButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentEditingFilePath == null)
        {
            RaiseStatus("⚠️ Aucun courrier sélectionné");
            return;
        }

        try
        {
            var docxPath = Path.ChangeExtension(_currentEditingFilePath, ".docx");

            if (!File.Exists(docxPath))
            {
                RaiseStatus("⚠️ Fichier .docx introuvable. Sauvegardez d'abord le courrier.");
                return;
            }

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = docxPath,
                Verb = "print",
                UseShellExecute = true,
                CreateNoWindow = true
            };

            System.Diagnostics.Process.Start(psi);
            RaiseStatus("🖨️ Document envoyé à l'imprimante par défaut");
        }
        catch (Exception ex)
        {
            RaiseStatus($"❌ Erreur impression: {ex.Message}");
        }
    }

    private void LetterEditText_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!LetterEditText.IsReadOnly && LetterEditText.Document != null && LetterEditText.Document.Blocks.Count > 0)
        {
            SauvegarderLetterButton.IsEnabled = true;
        }
    }

    private void ModifierLetterButton_Click(object sender, RoutedEventArgs e)
    {
        LetterEditText.IsReadOnly = false;
        LetterEditText.Background = new SolidColorBrush(Colors.White);

        NoterLetterButton.Visibility = Visibility.Collapsed;
        ModifierLetterButton.Visibility = Visibility.Collapsed;
        SupprimerLetterButton.Visibility = Visibility.Collapsed;
        ImprimerLetterButton.Visibility = Visibility.Collapsed;

        AnnulerLetterButton.Visibility = Visibility.Visible;
        SauvegarderLetterButton.Visibility = Visibility.Visible;
        SauvegarderLetterButton.IsEnabled = true;

        LetterEditText.Focus();
        RaiseStatus("✏️ Édition courrier activée");
    }

    private void AnnulerLetterButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            LettersList.SelectedItem = null;
            LetterEditText.Document = new FlowDocument();
            LetterEditText.IsReadOnly = true;
            LetterEditText.Background = new SolidColorBrush(Color.FromRgb(250, 250, 250));
            _currentEditingFilePath = null;

            AnnulerLetterButton.Visibility = Visibility.Collapsed;
            SauvegarderLetterButton.Visibility = Visibility.Collapsed;
            ModifierLetterButton.Visibility = Visibility.Collapsed;
            SupprimerLetterButton.Visibility = Visibility.Collapsed;
            ImprimerLetterButton.Visibility = Visibility.Collapsed;

            RaiseStatus("❌ Modifications annulées");
        }
        catch (Exception ex)
        {
            RaiseStatus($"❌ Erreur: {ex.Message}");
        }
    }

    private void SupprimerLetterButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentEditingFilePath == null)
        {
            RaiseStatus("⚠️ Aucun courrier sélectionné");
            return;
        }

        var result = MessageBox.Show(
            $"Êtes-vous sûr de vouloir supprimer ce courrier ?\n\n{Path.GetFileName(_currentEditingFilePath)}",
            "Confirmer la suppression",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning
        );

        if (result == MessageBoxResult.Yes)
        {
            try
            {
                File.Delete(_currentEditingFilePath);

                var docxPath = Path.ChangeExtension(_currentEditingFilePath, ".docx");
                if (File.Exists(docxPath))
                {
                    File.Delete(docxPath);
                }

                _currentEditingFilePath = null;
                LetterEditText.Document = new FlowDocument();
                LetterEditText.IsReadOnly = true;
                LetterEditText.Background = new SolidColorBrush(Color.FromRgb(250, 250, 250));

                ModifierLetterButton.Visibility = Visibility.Collapsed;
                SupprimerLetterButton.Visibility = Visibility.Collapsed;
                SauvegarderLetterButton.IsEnabled = false;
                SauvegarderLetterButton.Visibility = Visibility.Collapsed;

                RaiseStatus("✅ Courrier supprimé (.md + .docx)");
                RefreshLettersList();
            }
            catch (Exception ex)
            {
                RaiseStatus($"❌ Erreur: {ex.Message}");
            }
        }
    }

    private void SauvegarderLetterButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPatient == null || _letterService == null || LetterEditText.Document == null || LetterEditText.Document.Blocks.Count == 0)
        {
            RaiseStatus("⚠️ Rien à sauvegarder");
            return;
        }

        try
        {
            var markdown = MarkdownFlowDocumentConverter.FlowDocumentToMarkdown(LetterEditText.Document);

            bool success;
            string message;
            string? mdFilePath = _currentEditingFilePath;

            if (_currentEditingFilePath != null)
            {
                File.WriteAllText(_currentEditingFilePath, markdown);
                success = true;
                message = "Courrier mis à jour";
            }
            else
            {
                (success, message, mdFilePath) = _letterService.SaveDraft(
                    _selectedPatient.NomComplet,
                    markdown
                );

                if (success && mdFilePath != null)
                {
                    _currentEditingFilePath = mdFilePath;
                }
            }

            if (success && mdFilePath != null)
            {
                var (exportSuccess, exportMessage, docxPath) = _letterService.ExportToDocx(
                    _selectedPatient.NomComplet,
                    markdown,
                    mdFilePath
                );

                if (exportSuccess)
                {
                    message = $"✅ Courrier sauvegardé et exporté (.docx)";
                }
                else
                {
                    message = $"⚠️ Sauvegardé mais erreur export: {exportMessage}";
                }

                // Persister les métadonnées MCC
                if (!string.IsNullOrEmpty(_lastGeneratedLetterMCCId))
                {
                    try
                    {
                        var metaPath = mdFilePath + ".meta.json";
                        var metadata = new
                        {
                            mccId = _lastGeneratedLetterMCCId,
                            mccName = _lastGeneratedLetterMCCName,
                            generatedDate = DateTime.Now
                        };
                        var metaJson = System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(metaPath, metaJson);
                    }
                    catch { }
                }
            }

            RaiseStatus(message);

            LetterEditText.IsReadOnly = true;
            LetterEditText.Background = new SolidColorBrush(Color.FromRgb(250, 250, 250));

            AnnulerLetterButton.Visibility = Visibility.Collapsed;
            SauvegarderLetterButton.Visibility = Visibility.Collapsed;

            RefreshLettersList();

            LettersList.SelectedItem = null;

            NoterLetterButton.Visibility = Visibility.Collapsed;
            ModifierLetterButton.Visibility = Visibility.Collapsed;
            SupprimerLetterButton.Visibility = Visibility.Collapsed;
            ImprimerLetterButton.Visibility = Visibility.Collapsed;

            LetterEditText.Document = new FlowDocument();
            _currentEditingFilePath = null;
        }
        catch (Exception ex)
        {
            RaiseStatus($"❌ {ex.Message}");
        }
    }

    private void CreateLetterWithAIButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPatient == null)
        {
            MessageBox.Show("Veuillez d'abord sélectionner un patient.", "Information",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // Déléguer au MainWindow qui a le code complet pour gérer CreateLetterWithAIDialog
        CreateLetterWithAIRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Affiche un courrier généré depuis le MainWindow
    /// </summary>
    public void DisplayGeneratedLetter(string markdown, string? mccId, string? mccName)
    {
        LetterEditText.IsReadOnly = false;
        LetterEditText.Background = new SolidColorBrush(Colors.White);
        LetterEditText.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(markdown);

        _currentEditingFilePath = null;
        _lastGeneratedLetterMCCId = mccId;
        _lastGeneratedLetterMCCName = mccName;

        NoterLetterButton.Visibility = Visibility.Collapsed;
        ModifierLetterButton.Visibility = Visibility.Collapsed;
        SupprimerLetterButton.Visibility = Visibility.Collapsed;
        ImprimerLetterButton.Visibility = Visibility.Collapsed;
        SauvegarderLetterButton.Visibility = Visibility.Visible;
        SauvegarderLetterButton.IsEnabled = true;
        SauvegarderLetterButton.Background = new SolidColorBrush(Color.FromRgb(39, 174, 96));

        RaiseStatus("✅ Courrier généré avec l'IA - Modifiez-le puis sauvegardez");
    }

    private void RateLetterButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string letterPath)
        {
            MessageBox.Show("Impossible de trouver le courrier à évaluer.", "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!File.Exists(letterPath))
        {
            MessageBox.Show("Le fichier du courrier n'existe plus.", "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            ShowRateLetterDialog(letterPath, _lastGeneratedLetterMCCId, _lastGeneratedLetterMCCName);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors de l'ouverture du dialogue d'évaluation:\n\n{ex.Message}",
                "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowRateLetterDialog(string letterPath, string? mccId, string? mccName)
    {
        var dialog = new RateLetterDialog(letterPath, mccId, mccName)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true && dialog.Rating != null && _letterRatingService != null)
        {
            _letterRatingService.AddOrUpdateRating(dialog.Rating);
            HandleRatingActions(dialog.Rating);
            RefreshLettersList();
            RaiseStatus($"✅ Évaluation enregistrée : {dialog.Rating.Rating}⭐");
        }
    }

    private void HandleRatingActions(LetterRating rating)
    {
        if (!string.IsNullOrEmpty(rating.MCCId) && _mccLibrary != null && _letterRatingService != null)
        {
            _mccLibrary.UpdateMCCRatingStats(rating.MCCId, _letterRatingService);
        }

        bool isFromMCC = !string.IsNullOrEmpty(rating.MCCId);

        if (rating.Rating <= 3 && isFromMCC)
        {
            MessageBox.Show(
                $"⚠️ Ce MCC a été noté {rating.Rating}★\n\n" +
                $"MCC : {rating.MCCName}\n\n" +
                $"Cette note indique que le MCC pourrait nécessiter une révision.",
                "MCC à revoir",
                MessageBoxButton.OK,
                MessageBoxImage.Warning
            );
        }
        else if (rating.Rating == 5 && isFromMCC)
        {
            MessageBox.Show(
                $"🌟 Excellent ! Ce MCC a été noté 5★\n\n" +
                $"MCC : {rating.MCCName}",
                "Excellente notation",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
        else if (rating.Rating == 5 && !isFromMCC)
        {
            MessageBox.Show(
                $"🌟 Excellent courrier noté 5★ !\n\n" +
                $"Ce courrier n'utilise pas de MCC.\n" +
                $"Vous pouvez le transformer en MCC depuis l'onglet Templates.",
                "Excellente notation",
                MessageBoxButton.OK,
                MessageBoxImage.Information
            );
        }
    }

    private void RefreshLettersList()
    {
        if (_selectedPatient == null || _pathService == null)
        {
            LettersList.ItemsSource = null;
            return;
        }

        var lettresDir = _pathService.GetCourriersDirectory(_selectedPatient.NomComplet);

        if (!Directory.Exists(lettresDir))
        {
            LettersList.ItemsSource = null;
            return;
        }

        var letters = Directory.GetFiles(lettresDir, "*.md")
            .Select(f => new
            {
                Date = File.GetLastWriteTime(f),
                DateLabel = File.GetLastWriteTime(f).ToString("dd/MM/yyyy HH:mm"),
                Preview = Path.GetFileNameWithoutExtension(f),
                FilePath = f
            })
            .OrderByDescending(l => l.Date)
            .ToList();

        LettersList.ItemsSource = letters;
    }

    private void LoadCustomTemplates()
    {
        if (_mccLibrary == null)
            return;

        try
        {
            // Vider les MCC du dictionnaire
            var keysToRemove = _letterTemplates.Keys.Where(k => k.StartsWith("[MCC]")).ToList();
            foreach (var key in keysToRemove)
            {
                _letterTemplates.Remove(key);
            }

            // Compter les items NON-MCC dans le ComboBox
            int itemsToKeep = 0;
            for (int i = 0; i < TemplateLetterCombo.Items.Count; i++)
            {
                if (TemplateLetterCombo.Items[i] is ComboBoxItem item)
                {
                    var content = item.Content as string ?? "";
                    if (!content.StartsWith("[MCC]"))
                    {
                        itemsToKeep++;
                    }
                }
                else
                {
                    itemsToKeep++;
                }
            }

            // Vider le ComboBox (garder seulement les items prédéfinis)
            while (TemplateLetterCombo.Items.Count > itemsToKeep)
            {
                TemplateLetterCombo.Items.RemoveAt(itemsToKeep);
            }

            // Charger les MCC depuis MCCLibrary
            var allMCCs = _mccLibrary.GetAllMCCs();

            foreach (var mcc in allMCCs)
            {
                var displayName = $"[MCC] {mcc.Name}";
                var comboItem = new ComboBoxItem { Content = displayName };
                TemplateLetterCombo.Items.Add(comboItem);
                _letterTemplates[displayName] = mcc.TemplateMarkdown;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[LoadCustomTemplates] Erreur: {ex.Message}");
        }
    }
}
