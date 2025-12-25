using System;
using System.Windows;
using MedCompanion.Services;
using MedCompanion.Services.LLM;
using MedCompanion.ViewModels;

namespace MedCompanion.Dialogs
{
    public partial class PromptsAnalysisDialog : Window
    {
        public PromptsAnalysisDialog(PromptConfigService promptService, PromptReformulationService reformulationService)
        {
            InitializeComponent();

            try
            {
                // ✅ Utiliser les instances partagées passées en paramètre
                var viewModel = new PromptsAnalysisViewModel(promptService, reformulationService);

                // Connecter l'événement RequestClose pour fermer la fenêtre
                viewModel.RequestClose += () => Close();

                // Définir le DataContext
                DataContext = viewModel;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'initialisation : {ex.Message}\n\nDétails : {ex.StackTrace}",
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
