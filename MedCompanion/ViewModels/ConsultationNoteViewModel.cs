using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using MedCompanion.Commands;

namespace MedCompanion.ViewModels
{
    public class ConsultationNoteViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public event Action<ConsultationNoteViewModel>? DeleteRequested;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private bool SetProp<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        // ── Données ────────────────────────────────────────────────────────────
        public string Title    { get; }
        public string FilePath { get; }

        private string _displayContent;
        public string DisplayContent
        {
            get => _displayContent;
            private set
            {
                if (SetProp(ref _displayContent, value))
                    OnPropertyChanged(nameof(DisplayDocument));
            }
        }

        public FlowDocument DisplayDocument =>
            MarkdownFlowDocumentConverter.MarkdownToFlowDocument(_displayContent);

        private string _editContent = "";
        public string EditContent
        {
            get => _editContent;
            set => SetProp(ref _editContent, value);
        }

        private bool _isEditing;
        public bool IsEditing
        {
            get => _isEditing;
            set
            {
                if (SetProp(ref _isEditing, value))
                    OnPropertyChanged(nameof(IsNotEditing));
            }
        }

        public bool IsNotEditing => !IsEditing;

        // ── Commands ───────────────────────────────────────────────────────────
        public ICommand StartEditCommand  { get; }
        public ICommand SaveEditCommand   { get; }
        public ICommand CancelEditCommand { get; }
        public ICommand DeleteCommand     { get; }

        // ── Constructor ────────────────────────────────────────────────────────
        public ConsultationNoteViewModel(string title, string filePath, string displayContent)
        {
            Title           = title;
            FilePath        = filePath;
            _displayContent = displayContent;

            StartEditCommand  = new RelayCommand(_ => StartEdit());
            SaveEditCommand   = new RelayCommand(_ => SaveEdit(),  _ => IsEditing);
            CancelEditCommand = new RelayCommand(_ => CancelEdit());
            DeleteCommand     = new RelayCommand(_ => Delete());
        }

        private void StartEdit()
        {
            EditContent = DisplayContent;
            IsEditing   = true;
        }

        private void SaveEdit()
        {
            if (string.IsNullOrEmpty(FilePath)) return;
            try
            {
                // Préserver l'en-tête YAML, remplacer le corps
                var existing  = File.ReadAllText(FilePath, Encoding.UTF8);
                int closeYaml = existing.IndexOf("---", 3);
                string header = closeYaml >= 0 ? existing[..(closeYaml + 3)] : "";

                var newContent = string.IsNullOrEmpty(header)
                    ? EditContent.Trim()
                    : header + "\n\n" + EditContent.Trim();

                File.WriteAllText(FilePath, newContent, Encoding.UTF8);
                DisplayContent = EditContent.Trim();
                IsEditing      = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur sauvegarde : {ex.Message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void CancelEdit()
        {
            EditContent = DisplayContent;
            IsEditing   = false;
        }

        private void Delete()
        {
            var result = MessageBox.Show(
                $"Supprimer définitivement cette note ?\n\n{Title}",
                "Confirmer la suppression",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
                DeleteRequested?.Invoke(this);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur suppression : {ex.Message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }
}
