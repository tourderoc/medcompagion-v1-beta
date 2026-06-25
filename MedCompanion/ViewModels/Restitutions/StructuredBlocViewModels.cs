using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Models.Evaluations;

namespace MedCompanion.ViewModels.Restitutions
{
    // ── Définition d'un champ PT (donnée statique par type de bloc) ───────────
    public record PtFieldDef(string JsonPath, string Label, bool IsList);

    // ── Section éditable d'un bloc PT (vm vivant, lié à PtFieldDef) ──────────
    public class PtFieldViewModel : INotifyPropertyChanged
    {
        public string JsonPath { get; }
        public string Title    { get; }
        public bool   IsList   { get; }

        private string _content = "";
        public string Content
        {
            get => _content;
            set { if (_content == value) return; _content = value ?? ""; OnPropertyChanged(); Flush?.Invoke(); }
        }

        public ObservableCollection<EditableString> Items { get; } = new();

        private bool _isReformulePanelVisible;
        public bool IsReformulePanelVisible
        {
            get => _isReformulePanelVisible;
            set { if (_isReformulePanelVisible == value) return; _isReformulePanelVisible = value; OnPropertyChanged(); }
        }

        private string _userInstruction = "";
        public string UserInstruction
        {
            get => _userInstruction;
            set { if (_userInstruction == value) return; _userInstruction = value ?? ""; OnPropertyChanged(); }
        }

        private bool _isGenerating;
        public bool IsGenerating
        {
            get => _isGenerating;
            set
            {
                if (_isGenerating == value) return;
                _isGenerating = value;
                OnPropertyChanged();
                (RegenerateCommand as RelayCommand)?.RaiseCanExecuteChanged();
            }
        }

        public Action? Flush { get; set; }

        public ICommand ToggleReformulePanelCommand { get; }
        public ICommand CancelReformulePanelCommand { get; }
        public ICommand RegenerateCommand           { get; private set; }
        public ICommand AddItemCommand              { get; }
        public ICommand RemoveItemCommand           { get; }

        public PtFieldViewModel(string jsonPath, string title, bool isList)
        {
            JsonPath = jsonPath;
            Title    = title;
            IsList   = isList;

            ToggleReformulePanelCommand = new RelayCommand(_ => IsReformulePanelVisible = !IsReformulePanelVisible);
            CancelReformulePanelCommand = new RelayCommand(_ => { IsReformulePanelVisible = false; UserInstruction = ""; });
            RegenerateCommand = new RelayCommand(_ => { }, _ => false);

            AddItemCommand = new RelayCommand(_ =>
            {
                var item = new EditableString("");
                item.PropertyChanged += (s, _) => Flush?.Invoke();
                Items.Add(item);
                Flush?.Invoke();
            });

            RemoveItemCommand = new RelayCommand(param =>
            {
                if (param is EditableString es) { Items.Remove(es); Flush?.Invoke(); }
            });
        }

        public void InitRegenerateCommand(Func<PtFieldViewModel, Task> reformulateFieldAsync)
        {
            RegenerateCommand = new RelayCommand(
                async _ => await reformulateFieldAsync(this),
                _ => !IsGenerating);
            OnPropertyChanged(nameof(RegenerateCommand));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }


    // ── Diagnostics retenus (synthese_diag_s2) ───────────────────────────────

    public class DiagRetenuVm : INotifyPropertyChanged
    {
        private string _label = "";
        public string Label
        {
            get => _label;
            set { if (_label == value) return; _label = value ?? ""; OnPropertyChanged(); Flush?.Invoke(); }
        }

        private string _certitude = "Modérée";
        public string Certitude
        {
            get => _certitude;
            set { if (_certitude == value) return; _certitude = value ?? "Modérée"; OnPropertyChanged(); Flush?.Invoke(); }
        }

        public static readonly string[] CertitudeOptions = { "Hypothèse", "Modérée", "Élevée", "Très élevée" };

        public ObservableCollection<EditableString> Elements { get; } = new();

        public Action? Flush      { get; set; }
        public Action? RemoveSelf { get; set; }

        public ICommand AddElementCommand    { get; }
        public ICommand RemoveElementCommand { get; }
        public ICommand RemoveSelfCommand    { get; }

        public DiagRetenuVm()
        {
            AddElementCommand = new RelayCommand(_ =>
            {
                var item = new EditableString("");
                item.PropertyChanged += (s, _) => Flush?.Invoke();
                Elements.Add(item);
                Flush?.Invoke();
            });

            RemoveElementCommand = new RelayCommand(param =>
            {
                if (param is EditableString es) { Elements.Remove(es); Flush?.Invoke(); }
            });

            RemoveSelfCommand = new RelayCommand(_ => RemoveSelf?.Invoke());
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ── Diagnostics écartés (synthese_diag_s3) ───────────────────────────────

    public class DiagEcarteVm : INotifyPropertyChanged
    {
        private string _label = "";
        public string Label
        {
            get => _label;
            set { if (_label == value) return; _label = value ?? ""; OnPropertyChanged(); Flush?.Invoke(); }
        }

        private string _conclusion = "";
        public string Conclusion
        {
            get => _conclusion;
            set { if (_conclusion == value) return; _conclusion = value ?? ""; OnPropertyChanged(); Flush?.Invoke(); }
        }

        public ObservableCollection<EditableString> Arguments { get; } = new();

        public Action? Flush      { get; set; }
        public Action? RemoveSelf { get; set; }

        public ICommand AddArgumentCommand    { get; }
        public ICommand RemoveArgumentCommand { get; }
        public ICommand RemoveSelfCommand     { get; }

        public DiagEcarteVm()
        {
            AddArgumentCommand = new RelayCommand(_ =>
            {
                var item = new EditableString("");
                item.PropertyChanged += (s, _) => Flush?.Invoke();
                Arguments.Add(item);
                Flush?.Invoke();
            });

            RemoveArgumentCommand = new RelayCommand(param =>
            {
                if (param is EditableString es) { Arguments.Remove(es); Flush?.Invoke(); }
            });

            RemoveSelfCommand = new RelayCommand(_ => RemoveSelf?.Invoke());
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    // ── Intégration cartographies (synthese_diag_s4) ─────────────────────────

    public class S4IntegrationVm : INotifyPropertyChanged
    {
        public ObservableCollection<EditableString> Forces      { get; } = new();
        public ObservableCollection<EditableString> Fragilites  { get; } = new();
        public ObservableCollection<EditableString> Protecteurs { get; } = new();
        public ObservableCollection<EditableString> Aggravants  { get; } = new();

        public Action? Flush { get; set; }

        public ICommand AddForceCommand        { get; }
        public ICommand RemoveForceCommand     { get; }
        public ICommand AddFragiliteCommand    { get; }
        public ICommand RemoveFragiliteCommand { get; }
        public ICommand AddProtecteurCommand   { get; }
        public ICommand RemoveProtecteurCommand { get; }
        public ICommand AddAggravantCommand    { get; }
        public ICommand RemoveAggravantCommand { get; }

        public S4IntegrationVm()
        {
            (AddForceCommand,      RemoveForceCommand)      = MakeListCommands(Forces);
            (AddFragiliteCommand,  RemoveFragiliteCommand)  = MakeListCommands(Fragilites);
            (AddProtecteurCommand, RemoveProtecteurCommand) = MakeListCommands(Protecteurs);
            (AddAggravantCommand,  RemoveAggravantCommand)  = MakeListCommands(Aggravants);
        }

        private (ICommand add, ICommand remove) MakeListCommands(ObservableCollection<EditableString> list)
        {
            var add = new RelayCommand(_ =>
            {
                var item = new EditableString("");
                item.PropertyChanged += (s, _) => Flush?.Invoke();
                list.Add(item);
                Flush?.Invoke();
            });
            var remove = new RelayCommand(param =>
            {
                if (param is EditableString es) { list.Remove(es); Flush?.Invoke(); }
            });
            return (add, remove);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
