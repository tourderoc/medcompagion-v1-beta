using System.IO;
using System.Windows;
using System.Windows.Controls;
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.Services.Consultation;
using MedCompanion.Services.LLM;
using MedCompanion.ViewModels;
using Microsoft.Win32;

namespace MedCompanion.Views.Consultation
{
    public partial class ConsultationModeControl : UserControl
    {
        private ConsultationModeViewModel? _viewModel;

        public ConsultationModeControl()
        {
            InitializeComponent();
            _viewModel = DataContext as ConsultationModeViewModel;
        }

        public void Initialize(ILLMService llmService, StorageService storageService,
                               WhisperStreamingService? whisperService = null)
        {
            _viewModel ??= DataContext as ConsultationModeViewModel;
            _viewModel?.InjectServices(llmService, storageService, whisperService);
        }

        public void LoadPatient(PatientIndexEntry patient)
        {
            _viewModel ??= DataContext as ConsultationModeViewModel;
            if (ConsultationTypeCombo.Items.Count > 0)
                ConsultationTypeCombo.SelectedIndex = 0;
            _viewModel?.LoadPatient(patient);
        }

        public void SetViewState(ConsultationViewState state)
        {
            _viewModel ??= DataContext as ConsultationModeViewModel;
            if (_viewModel != null)
                _viewModel.CurrentState = state;
        }

        public ConsultationModeViewModel? GetViewModel() => _viewModel;

        // ── Handlers ─────────────────────────────────────────────────────────

        private void ConsultationTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _viewModel ??= DataContext as ConsultationModeViewModel;
            if (_viewModel == null) return;

            var item = ConsultationTypeCombo.SelectedItem as ComboBoxItem;
            var tag  = item?.Tag?.ToString() ?? "Normal";

            _viewModel.ConsultationType = tag switch
            {
                "PremiereConsultation" => ConsultationType.PremiereConsultation,
                _                      => ConsultationType.Normal
            };
        }

        private void ImportTxtBtn_Click(object sender, RoutedEventArgs e)
        {
            _viewModel ??= DataContext as ConsultationModeViewModel;
            if (_viewModel == null) return;

            var dlg = new OpenFileDialog
            {
                Title       = "Importer une transcription",
                Filter      = "Fichiers texte (*.txt)|*.txt|Tous les fichiers (*.*)|*.*",
                Multiselect = false
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                _viewModel.TranscriptionInput = File.ReadAllText(dlg.FileName, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lecture fichier : {ex.Message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void VocabBtn_Click(object sender, RoutedEventArgs e)
        {
            var vocabService = new WhisperVocabService();
            var dialog = new Dialogs.WhisperVocabDialog(vocabService)
            {
                Owner = Window.GetWindow(this)
            };
            dialog.ShowDialog();
        }
    }
}
