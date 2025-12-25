using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Services;

namespace MedCompanion.Views
{
    /// <summary>
    /// UserControl qui affiche un overlay modal pendant les opérations longues.
    /// Se bind automatiquement au BusyService singleton.
    /// </summary>
    public partial class BusyOverlay : UserControl
    {
        public BusyOverlay()
        {
            InitializeComponent();

            // Bind au service singleton
            DataContext = new BusyOverlayViewModel();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            BusyService.Instance.Cancel();
        }
    }

    /// <summary>
    /// ViewModel pour le BusyOverlay, wrapper autour du BusyService
    /// </summary>
    public class BusyOverlayViewModel : ViewModels.ViewModelBase
    {
        private readonly BusyService _busyService;

        public BusyOverlayViewModel()
        {
            _busyService = BusyService.Instance;

            // Relayer les changements de propriétés du service
            _busyService.PropertyChanged += (s, e) =>
            {
                OnPropertyChanged(e.PropertyName ?? string.Empty);
            };

            CancelCommand = new RelayCommand(
                execute: _ => _busyService.Cancel(),
                canExecute: _ => _busyService.CanCancel && !_busyService.IsCancellationRequested
            );
        }

        // Propriétés relayées depuis BusyService
        public bool IsBusy => _busyService.IsBusy;
        public string Message => _busyService.Message;
        public string Step => _busyService.Step;
        public double Progress => _busyService.Progress;
        public bool CanCancel => _busyService.CanCancel;
        public bool IsCancellationRequested => _busyService.IsCancellationRequested;

        public ICommand CancelCommand { get; }
    }
}
