using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MedCompanion.Models;

namespace MedCompanion.ViewModels
{
    public class ConsultationBlockViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private bool SetProp<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        // ── Données du bloc ──────────────────────────────────────────────────

        public string Key { get; init; } = "";
        public string Title { get; init; } = "";

        private string _freeText = "";
        public string FreeText
        {
            get => _freeText;
            set
            {
                if (SetProp(ref _freeText, value))
                    OnPropertyChanged(nameof(IsEmpty));
            }
        }

        private List<string> _expectedThemes = new();
        public List<string> ExpectedThemes
        {
            get => _expectedThemes;
            set => SetProp(ref _expectedThemes, value);
        }

        private List<string> _coveredThemes = new();
        public List<string> CoveredThemes
        {
            get => _coveredThemes;
            set
            {
                if (SetProp(ref _coveredThemes, value))
                {
                    OnPropertyChanged(nameof(ProgressPct));
                    OnPropertyChanged(nameof(ProgressBarColor));
                }
            }
        }

        // ── Computed ──────────────────────────────────────────────────────────

        public int ProgressPct => ExpectedThemes.Count == 0
            ? 0
            : Math.Min(100, (int)(100.0 * CoveredThemes.Count / ExpectedThemes.Count));

        public string ProgressBarColor => ProgressPct switch
        {
            0        => "#CCCCCC",
            <= 50    => "#E67E22",
            < 100    => "#F1C40F",
            _        => "#27AE60"
        };

        public bool IsEmpty => string.IsNullOrWhiteSpace(FreeText);

        public bool IsNotEmpty => !IsEmpty;

        public bool IsCompleted => ProgressPct >= 100;

        public string CompactPreview
        {
            get
            {
                if (string.IsNullOrWhiteSpace(FreeText)) return "";
                var lines = FreeText.Split('\n');
                var first = lines[0];
                return first.Length > 50 ? first.Substring(0, 47) + "..." : first;
            }
        }

        private bool _isHidden = false;
        public bool IsHidden
        {
            get => _isHidden;
            set => SetProp(ref _isHidden, value);
        }

        /// <summary>Vrai pendant une opération LLM sur ce bloc (ex: reformulation) — désactive le bouton.</summary>
        private bool _isBusy = false;
        public bool IsBusy
        {
            get => _isBusy;
            set => SetProp(ref _isBusy, value);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        public void AddTheme(string theme)
        {
            if (!_coveredThemes.Contains(theme))
            {
                _coveredThemes.Add(theme);
                OnPropertyChanged(nameof(CoveredThemes));
                OnPropertyChanged(nameof(ProgressPct));
                OnPropertyChanged(nameof(ProgressBarColor));
            }
        }

        public void AppendText(string text)
        {
            FreeText = string.IsNullOrWhiteSpace(FreeText)
                ? text.Trim()
                : FreeText + "\n" + text.Trim();
        }

        public void Reset()
        {
            FreeText = "";
            _coveredThemes = new List<string>();
            _isHidden = false;
            OnPropertyChanged(nameof(CoveredThemes));
            OnPropertyChanged(nameof(ProgressPct));
            OnPropertyChanged(nameof(ProgressBarColor));
            OnPropertyChanged(nameof(IsEmpty));
            OnPropertyChanged(nameof(IsHidden));
        }

        public void ToggleHidden()
        {
            IsHidden = !IsHidden;
        }

        public static ConsultationBlockViewModel FromModel(ConsultationBlock model) => new()
        {
            Key = model.Key,
            Title = model.Title,
            ExpectedThemes = new List<string>(model.ExpectedThemes)
        };

        /// <summary>
        /// V0b : Crée un ViewModel depuis une BlockDefinition (block_library.json)
        /// </summary>
        public static ConsultationBlockViewModel FromDefinition(BlockDefinition def) => new()
        {
            Key = def.Key,
            Title = def.Title,
            ExpectedThemes = new List<string>(def.ExpectedThemes)
        };
    }
}
