using System.IO;
using System.Threading.Tasks;
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
        private ConsultationModeViewModel?    _viewModel;
        private HandyChunkedRecordingService? _handyService;

        public ConsultationModeControl()
        {
            InitializeComponent();
            _viewModel = DataContext as ConsultationModeViewModel;
        }

        public void Initialize(ILLMService llmService, StorageService storageService,
                               HandyChunkedRecordingService? handyService = null)
        {
            _viewModel    ??= DataContext as ConsultationModeViewModel;
            _handyService   = handyService;

            if (handyService != null)
            {
                // Focus TextBox AVANT chaque stop/restart Handy (Handy tape là où est le focus)
                handyService.FocusRequired += async () =>
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                        TranscriptionTextBox.Focus();
                        TranscriptionTextBox.CaretIndex = TranscriptionTextBox.Text?.Length ?? 0;
                    });
                    await Task.Delay(80); // marge pour que le focus soit effectif
                };
            }

            _viewModel?.InjectServices(llmService, storageService, handyService);
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

        // ── Dictée Handy ────────────────────────────────────────────────────

        private async void DicterBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_handyService == null || _viewModel == null) return;

            // Pattern identique à FocusMedControl : focus TextBox → délai → toggle Handy
            TranscriptionTextBox.Focus();
            TranscriptionTextBox.CaretIndex = TranscriptionTextBox.Text?.Length ?? 0;
            await Task.Delay(50);

            await _handyService.StartAsync();
            _viewModel.IsRecording = true;
        }

        private async void ArreterBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_handyService == null || _viewModel == null) return;

            // Re-focus avant stop : Handy tape dans notre TextBox, pas ailleurs
            TranscriptionTextBox.Focus();
            TranscriptionTextBox.CaretIndex = TranscriptionTextBox.Text?.Length ?? 0;
            await Task.Delay(100);

            await _handyService.StopAsync();
            _viewModel.IsRecording  = false;
            _viewModel.IsInCutover  = false;
        }

        // ── Autres handlers ─────────────────────────────────────────────────

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
                Title     = "Importer une transcription",
                Filter    = "Fichiers texte (*.txt)|*.txt|Tous les fichiers (*.*)|*.*",
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
    }
}
