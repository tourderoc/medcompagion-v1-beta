using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using MedCompanion.Dialogs;
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.ViewModels;

namespace MedCompanion.Views.Courriers;

/// <summary>
/// Code-behind MVVM pour CourriersControl
/// Contient uniquement l'initialisation et les gestionnaires d'événements UI
/// </summary>
public partial class CourriersControl : UserControl
{
    private CourriersViewModel? _viewModel;
    private bool _isUpdatingFlowDocument = false; // Flag pour éviter les boucles infinies
    private RegenerationService? _regenerationService; // ✅ NOUVEAU : Pour régénération IA
    private PathService? _pathService; // ✅ NOUVEAU : Pour charger métadonnées patient

    // Events pour communiquer avec MainWindow
    public event EventHandler<string>? StatusChanged;
    public event EventHandler? CreateLetterWithAIRequested;
    public event EventHandler? LetterSaved; // NOUVEAU : Pour rafraîchir le badge de synthèse
    public event EventHandler<string>? NavigateToTemplatesWithLetter; // NOUVEAU : Pour rediriger vers Templates avec courrier

    public CourriersControl()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Initialise le contrôle avec le ViewModel
    /// </summary>
    public void Initialize(
        LetterService letterService,
        PathService pathService,
        PatientIndexService patientIndex,
        MCCLibraryService mccLibrary,
        LetterRatingService letterRatingService,
        LetterReAdaptationService reAdaptationService,
        SynthesisWeightTracker synthesisWeightTracker,
        RegenerationService? regenerationService = null) // ✅ NOUVEAU : Pour régénération IA
    {
        // ✅ Stocker les services pour utilisation dans les dialogues
        _regenerationService = regenerationService;
        _pathService = pathService;

        // Créer le ViewModel
        _viewModel = new CourriersViewModel(
            letterService,
            pathService,
            patientIndex,
            mccLibrary,
            letterRatingService,
            reAdaptationService
        );

        // Initialiser le tracker de poids
        _viewModel.InitializeSynthesisWeightTracker(synthesisWeightTracker);

        // Lier le ViewModel au DataContext
        DataContext = _viewModel;

        // S'abonner aux événements du ViewModel
        _viewModel.StatusMessageChanged += OnViewModelStatusChanged;
        _viewModel.LetterContentLoaded += OnLetterContentLoaded;
        _viewModel.ErrorOccurred += OnViewModelError;
        _viewModel.InfoMessageRequested += OnViewModelInfo;
        _viewModel.ConfirmationRequested += OnViewModelConfirmation;
        _viewModel.FilePrintRequested += OnFilePrintRequested;
        _viewModel.CreateLetterWithAIRequested += OnCreateLetterWithAI;
        _viewModel.RatingDialogRequested += OnRatingDialogRequested;
        _viewModel.MissingInfoDialogRequested += OnMissingInfoDialogRequested;
        _viewModel.DetailedViewRequested += OnDetailedViewRequested;
        _viewModel.LetterSaved += OnLetterSaved; // NOUVEAU : Propager l'événement vers MainWindow
        _viewModel.NavigateToTemplatesWithLetter += OnNavigateToTemplatesWithLetter; // NOUVEAU : Propager vers MainWindow

        // S'abonner aux changements de propriétés pour IsReadOnly (RichTextBox ne supporte pas bien le binding direct)
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // Charger les templates MCC
        _viewModel.LoadMCCTemplates();
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CourriersViewModel.IsReadOnly))
        {
            if (_viewModel != null)
            {
                LetterEditText.IsReadOnly = _viewModel.IsReadOnly;
            }
        }
    }

    /// <summary>
    /// Définit le patient courant
    /// </summary>
    public void SetCurrentPatient(PatientIndexEntry? patient)
    {
        if (_viewModel != null)
        {
            _viewModel.CurrentPatient = patient;
        }
    }

    /// <summary>
    /// Recharge les templates MCC
    /// </summary>
    public void ReloadTemplates()
    {
        _viewModel?.LoadMCCTemplates();
    }

    /// <summary>
    /// Définit un brouillon de courrier (appelé depuis le Chat)
    /// </summary>
    public void SetDraft(string markdown, string? mccId = null, string? mccName = null)
    {
        _viewModel?.SetDraft(markdown, mccId, mccName);
    }

    /// <summary>
    /// Affiche un courrier généré (alias pour SetDraft pour compatibilité)
    /// </summary>
    public void DisplayGeneratedLetter(string markdown, string? mccId = null, string? mccName = null)
    {
        SetDraft(markdown, mccId, mccName);
    }

    /// <summary>
    /// Réinitialise le contrôle
    /// </summary>
    public void Reset()
    {
        if (_viewModel != null)
        {
            _viewModel.CurrentPatient = null;
        }
    }

    #region Gestionnaires d'événements du ViewModel

    private void OnViewModelStatusChanged(object? sender, string status)
    {
        StatusChanged?.Invoke(this, status);
    }

    private void OnLetterContentLoaded(object? sender, string markdown)
    {
        // Éviter la boucle infinie lors de la synchronisation
        _isUpdatingFlowDocument = true;
        try
        {
            // Convertir le markdown en FlowDocument pour l'affichage
            LetterEditText.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(markdown);
        }
        finally
        {
            _isUpdatingFlowDocument = false;
        }
    }

    private void OnViewModelError(object? sender, (string title, string message) args)
    {
        MessageBox.Show(
            args.message,
            args.title,
            MessageBoxButton.OK,
            MessageBoxImage.Error
        );
    }

    private void OnViewModelInfo(object? sender, (string title, string message) args)
    {
        MessageBox.Show(
            args.message,
            args.title,
            MessageBoxButton.OK,
            MessageBoxImage.Information
        );
    }

    private void OnViewModelConfirmation(object? sender, (string title, string message, Action onConfirm) args)
    {
        var result = MessageBox.Show(
            args.message,
            args.title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question
        );

        if (result == MessageBoxResult.Yes)
        {
            args.onConfirm?.Invoke();
        }
    }

    private void OnFilePrintRequested(object? sender, string filePath)
    {
        try
        {
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true, Verb = "print" });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Impossible d'imprimer le fichier:\n{ex.Message}",
                "Erreur",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    private void OnCreateLetterWithAI(object? sender, EventArgs e)
    {
        CreateLetterWithAIRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnRatingDialogRequested(object? sender, (string mccId, string mccName, string letterPath, Action<int> onRatingReceived) args)
    {
        var dialog = new RateLetterDialog(args.mccId, args.mccName);
        dialog.Owner = Window.GetWindow(this);

        if (dialog.ShowDialog() == true)
        {
            var rating = dialog.Rating?.Rating ?? 0;
            args.onRatingReceived?.Invoke(rating);
        }
        else
        {
            args.onRatingReceived?.Invoke(0); // 0 = annulé
        }
    }

    private void OnMissingInfoDialogRequested(object? sender, (List<MissingFieldInfo> missingFields, Action<Dictionary<string, string>?> onInfoCollected) args)
    {
        var dialog = new MissingInfoDialog(args.missingFields);
        dialog.Owner = Window.GetWindow(this);

        if (dialog.ShowDialog() == true)
        {
            args.onInfoCollected?.Invoke(dialog.CollectedInfo);
        }
        else
        {
            args.onInfoCollected?.Invoke(null); // null = annulé
        }
    }

    private void OnDetailedViewRequested(object? sender, (string filePath, string patientName, Action onContentSaved) args)
    {
        try
        {
            var dialog = new DetailedViewDialog();
            dialog.LoadContent(args.filePath, ContentType.Letter, args.patientName);

            // ✅ NOUVEAU : Initialiser le service de régénération IA si disponible
            if (_regenerationService != null)
            {
                // Charger les métadonnées patient pour améliorer l'anonymisation
                PatientMetadata? patientMetadata = null;
                if (_pathService != null && !string.IsNullOrEmpty(args.patientName))
                {
                    try
                    {
                        var patientJsonPath = _pathService.GetPatientJsonPath(args.patientName);
                        if (System.IO.File.Exists(patientJsonPath))
                        {
                            var json = System.IO.File.ReadAllText(patientJsonPath, System.Text.Encoding.UTF8);
                            patientMetadata = System.Text.Json.JsonSerializer.Deserialize<PatientMetadata>(json);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[CourriersControl] Erreur chargement métadonnées patient: {ex.Message}");
                    }
                }

                dialog.InitializeRegenerationService(_regenerationService, patientMetadata);
            }

            // S'abonner à l'événement de sauvegarde
            dialog.ContentSaved += (s, e) =>
            {
                args.onContentSaved?.Invoke();
            };

            dialog.Owner = Window.GetWindow(this);
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Erreur lors de l'ouverture de la vue détaillée :\n{ex.Message}",
                "Erreur",
                MessageBoxButton.OK,
                MessageBoxImage.Error
            );
        }
    }

    #endregion

    private void OnLetterSaved(object? sender, EventArgs e)
    {
        // Propager l'événement vers MainWindow pour rafraîchir le badge de synthèse
        LetterSaved?.Invoke(this, EventArgs.Empty);
    }

    private void OnNavigateToTemplatesWithLetter(object? sender, string letterPath)
    {
        // Propager l'événement vers MainWindow pour redirection vers Templates
        NavigateToTemplatesWithLetter?.Invoke(this, letterPath);
    }

    #region Événements UI (bindings FlowDocument)

    /// <summary>
    /// Double-clic sur l'aperçu du courrier pour ouvrir en vue détaillée
    /// </summary>
    private void LetterEditText_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Déclencher la commande OpenDetailedView du ViewModel
        if (_viewModel?.OpenDetailedViewCommand.CanExecute(null) == true)
        {
            _viewModel.OpenDetailedViewCommand.Execute(null);
        }
    }

    /// <summary>
    /// Double-clic sur un courrier dans la liste pour l'ouvrir dans Word/LibreOffice
    /// </summary>
    private void LettersList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel?.SelectedLetter != null)
        {
            var docxPath = _viewModel.SelectedLetter.DocxPath;
            if (System.IO.File.Exists(docxPath))
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = docxPath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Impossible d'ouvrir le fichier : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }

    /// <summary>
    /// Synchronise le contenu du FlowDocument vers le ViewModel lors de l'édition
    /// </summary>
    private void LetterEditText_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Éviter la boucle infinie : ne pas synchroniser si on est en train de mettre à jour depuis le ViewModel
        if (_isUpdatingFlowDocument)
            return;

        if (_viewModel != null && !_viewModel.IsReadOnly)
        {
            // Convertir le FlowDocument en markdown
            var markdown = MarkdownFlowDocumentConverter.FlowDocumentToMarkdown(LetterEditText.Document);

            // Ne pas déclencher LetterContentLoaded en retour
            if (_viewModel.LetterMarkdown != markdown)
            {
                _viewModel.LetterMarkdown = markdown;
            }
        }
    }

    #endregion
}
