using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace MedCompanion.ViewModels
{
    public class ProfileAxisItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropChanged([CallerMemberName] string? n = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

        public string Label { get; init; } = "";

        private int _value;
        public int Value
        {
            get => _value;
            set
            {
                if (_value != value)
                {
                    _value = value;
                    OnPropChanged();
                    OnPropChanged(nameof(ValueLabel));
                }
            }
        }

        public string ValueLabel => Value == 0 ? "—" : $"{Value}/5";
    }

    public class ProfileEvaluationViewModel
    {

        public string SphereTitle { get; }
        public ObservableCollection<ProfileAxisItem> Axes { get; }
        private readonly Action<List<int>> _applyBack;

        public ProfileEvaluationViewModel(
            string title,
            List<(string label, int value)> axes,
            Action<List<int>> applyBack)
        {
            SphereTitle = title;
            Axes = new ObservableCollection<ProfileAxisItem>(
                axes.Select(a => new ProfileAxisItem { Label = a.label, Value = a.value }));
            _applyBack = applyBack;
        }

        public void ApplyToProfile()
            => _applyBack(Axes.Select(a => a.Value).ToList());
    }
}
