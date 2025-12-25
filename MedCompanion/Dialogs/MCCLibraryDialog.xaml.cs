using System;
using System.Linq;
using System.Windows;
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.ViewModels;

namespace MedCompanion.Dialogs;

/// <summary>
/// Dialogue de bibliothèque MCC - Architecture MVVM
/// ViewModel = MCCLibraryViewModel
/// Migré le 05/12/2025 - Backup : MCCLibraryDialog.xaml.cs.bak
/// </summary>
public partial class MCCLibraryDialog : Window
{
    private readonly MCCLibraryService _mccLibrary;
    private MCCLibraryViewModel? _viewModel;

    public MCCModel? SelectedMCC => _viewModel?.SelectedMCC;
    public bool ShouldGenerate => _viewModel?.ShouldGenerate ?? false;

    public MCCLibraryDialog(MCCLibraryService mccLibrary, LetterRatingService? ratingService = null)
    {
        InitializeComponent();

        _mccLibrary = mccLibrary;
        _viewModel = new MCCLibraryViewModel(mccLibrary, ratingService);

        // S'abonner aux événements ViewModel
        _viewModel.EditRequested += OnEditRequested;
        _viewModel.OptimizeRequested += OnOptimizeRequested;
        _viewModel.DeleteConfirmationRequested += OnDeleteConfirmation;
        _viewModel.CloseDialogRequested += OnCloseDialog;
        _viewModel.ErrorOccurred += OnError;

        DataContext = _viewModel;

        Loaded += async (s, e) => await _viewModel.LoadMCCsAsync();
    }

    private async void OnEditRequested(object? sender, MCCModel mcc)
    {
        try
        {
            // Sauvegarder l'ID avant d'ouvrir le dialogue
            var mccId = mcc.Id;
            
            // Ouvrir le dialogue d'édition
            var editDialog = new EditMCCDialog(mcc, _mccLibrary)
            {
                Owner = this
            };

            var result = editDialog.ShowDialog();

            if (result == true)
            {
                // Rafraîchir la liste pour afficher les modifications
                await _viewModel!.LoadMCCsAsync();

                // Resélectionner le MCC édité en utilisant l'ID sauvegardé
                var displayItem = _viewModel.FilteredMCCs.FirstOrDefault(d => d.MCC != null && d.MCC.Id == mccId);
                if (displayItem != null && displayItem.MCC != null)
                {
                    _viewModel.SelectedMCCItem = displayItem;
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors de l'édition:\n\n{ex.Message}\n\nStack: {ex.StackTrace}", "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void OnOptimizeRequested(object? sender, MCCModel mcc)
    {
        // Demander confirmation
        var result = MessageBox.Show(
            $"Êtes-vous sûr de vouloir optimiser ce MCC avec l'IA ?\n\n" +
            $"Nom : {mcc.Name}\n" +
            $"Version actuelle : {mcc.Version}\n\n" +
            "L'optimisation va :\n" +
            "• Nettoyer et améliorer le template\n" +
            "• Régénérer les 5 mots-clés\n" +
            "• Affiner l'analyse sémantique\n" +
            "• Incrémenter la version\n\n" +
            "Cette action est irréversible.",
            "Confirmer l'optimisation IA",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question
        );

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            // Obtenir le service LLM depuis la fenêtre principale
            var mainWindow = Application.Current.MainWindow as MainWindow;
            if (mainWindow == null)
            {
                MessageBox.Show("Erreur: Fenêtre principale non trouvée.", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var llmService = mainWindow.GetCurrentLLMService();
            if (llmService == null)
            {
                MessageBox.Show("Service IA non disponible. Vérifiez la configuration.", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Optimiser le MCC
            var (success, message, optimizationResponse) = await _mccLibrary.OptimizeMCCAsync(mcc.Id, llmService);

            if (!success || optimizationResponse == null)
            {
                MessageBox.Show($"❌ Erreur lors de l'optimisation:\n\n{message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Demander confirmation avant d'appliquer
            var applyResult = MessageBox.Show(
                $"✅ Optimisation réussie !\n\n{message}\n\n" +
                $"Nouveaux mots-clés: {string.Join(", ", optimizationResponse.Keywords)}\n\n" +
                "Voulez-vous appliquer ces optimisations au MCC ?",
                "Appliquer l'optimisation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question
            );

            if (applyResult == MessageBoxResult.Yes)
            {
                var mccId = mcc.Id;
                
                var (applySuccess, applyMessage) = _mccLibrary.ApplyOptimization(mccId, optimizationResponse);

                if (applySuccess)
                {
                    MessageBox.Show($"✅ {applyMessage}", "Succès",
                        MessageBoxButton.OK, MessageBoxImage.Information);

                    // Rafraîchir l'affichage
                    await _viewModel!.LoadMCCsAsync();

                    // Resélectionner le MCC optimisé
                    var displayItem = _viewModel.FilteredMCCs.FirstOrDefault(d => d.MCC != null && d.MCC.Id == mccId);
                    if (displayItem != null && displayItem.MCC != null)
                    {
                        _viewModel.SelectedMCCItem = displayItem;
                    }
                }
                else
                {
                    MessageBox.Show($"❌ Erreur lors de l'application:\n\n{applyMessage}", "Erreur",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur inattendue:\n\n{ex.Message}", "Erreur",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnDeleteConfirmation(object? sender, MCCModel mcc)
    {
        var result = MessageBox.Show(
            $"Voulez-vous vraiment supprimer le MCC '{mcc.Name}' ?\n\n" +
            $"Cette action est irréversible.",
            "Confirmation de suppression",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _viewModel.ConfirmDelete();
        }
    }

    private void OnCloseDialog(object? sender, bool shouldGenerate)
    {
        DialogResult = shouldGenerate;
        Close();
    }

    private void OnError(object? sender, (string title, string message) error)
    {
        MessageBox.Show(error.message, error.title,
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
