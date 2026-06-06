using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Models.Restitutions;
using MedCompanion.Services.Restitutions;

namespace MedCompanion.ViewModels.Restitutions
{
    public class RestitutionBlocViewModel : INotifyPropertyChanged
    {
        public RestitutionBloc Model { get; }
        
        public string Title => Model.Titre;
        public string SectionType => Model.Key;
        
        public string Contenu
        {
            get => Model.ContenuValide;
            set
            {
                if (Model.ContenuValide != value)
                {
                    Model.ContenuValide = value;
                    OnPropertyChanged();
                }
            }
        }

        private bool _isGenerating;
        public bool IsGenerating
        {
            get => _isGenerating;
            set
            {
                if (_isGenerating != value)
                {
                    _isGenerating = value;
                    OnPropertyChanged();
                }
            }
        }

        public ICommand GenerateCommand { get; }

        public RestitutionBlocViewModel(RestitutionBloc model, Func<RestitutionBlocViewModel, Task> generateAction)
        {
            Model = model;
            GenerateCommand = new RelayCommand(async _ => await generateAction(this), _ => !IsGenerating);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class RestitutionEditorViewModel : INotifyPropertyChanged
    {
        private readonly RestitutionService _restitutionService;
        private readonly RestitutionSuggesterService _suggesterService;
        private readonly string _patientName;
        private DossierRestitutionInitial _dossier;

        public ObservableCollection<RestitutionBlocViewModel> Blocs { get; } = new();

        public string PatientName => _patientName;
        
        private string _statusMessage = "";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; OnPropertyChanged(); }
        }

        public ICommand SaveCommand { get; }
        public ICommand GenerateAllCommand { get; }
        public ICommand CloseCommand { get; }

        public event Action? RequestClose;

        public RestitutionEditorViewModel(
            DossierRestitutionInitial dossier,
            string patientName,
            RestitutionService restitutionService,
            RestitutionSuggesterService suggesterService)
        {
            _dossier = dossier;
            _patientName = patientName;
            _restitutionService = restitutionService;
            _suggesterService = suggesterService;

            foreach (var bloc in _dossier.Blocs)
            {
                Blocs.Add(new RestitutionBlocViewModel(bloc, GenerateBlocAsync));
            }

            SaveCommand = new RelayCommand(async _ => await SaveAsync());
            GenerateAllCommand = new RelayCommand(async _ => await GenerateAllAsync());
            CloseCommand = new RelayCommand(_ => RequestClose?.Invoke());
        }

        private async Task GenerateBlocAsync(RestitutionBlocViewModel blocVm)
        {
            blocVm.IsGenerating = true;
            try
            {
                var result = await _suggesterService.PrefillBlocAsync(_patientName, blocVm.Model);
                if (!result.Suggestion.StartsWith("(Erreur"))
                {
                    blocVm.Contenu = result.Suggestion;
                    await SaveAsync(); // Auto-save after generation
                }
                else
                {
                    StatusMessage = $"Erreur gén. {blocVm.Title}: {result.Suggestion}";
                }
            }
            finally
            {
                blocVm.IsGenerating = false;
            }
        }

        private async Task GenerateAllAsync()
        {
            foreach (var bloc in Blocs)
            {
                if (string.IsNullOrWhiteSpace(bloc.Contenu))
                {
                    await GenerateBlocAsync(bloc);
                }
            }
        }

        private async Task SaveAsync()
        {
            try
            {
                StatusMessage = "Enregistrement en cours...";
                await Task.Run(() => _restitutionService.SaveBrouillon(_dossier));
                StatusMessage = "Enregistré le " + DateTime.Now.ToString("HH:mm");
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur sauvegarde : {ex.Message}";
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
