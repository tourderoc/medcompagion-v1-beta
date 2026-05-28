using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MedCompanion.Models.Evaluations
{
    /// <summary>
    /// Wrapper minimal pour permettre l'édition two-way d'une chaîne dans une
    /// ObservableCollection (les string en C# étant immutables, on ne peut pas
    /// les éditer directement via binding TextBox).
    /// </summary>
    public class EditableString : INotifyPropertyChanged
    {
        private string _value = "";
        public string Value
        {
            get => _value;
            set
            {
                if (_value == value) return;
                _value = value ?? "";
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        public EditableString() { }
        public EditableString(string v) { _value = v ?? ""; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public override string ToString() => _value;
    }
}
