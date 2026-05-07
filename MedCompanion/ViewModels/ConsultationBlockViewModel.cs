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
            OnPropertyChanged(nameof(CoveredThemes));
            OnPropertyChanged(nameof(ProgressPct));
            OnPropertyChanged(nameof(ProgressBarColor));
            OnPropertyChanged(nameof(IsEmpty));
        }

        public static ConsultationBlockViewModel FromModel(ConsultationBlock model) => new()
        {
            Key = model.Key,
            Title = model.Title,
            ExpectedThemes = new List<string>(model.ExpectedThemes)
        };
    }
}
