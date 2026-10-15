using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using MedCompanion.Commands;

using System.Windows.Media;

namespace MedCompanion.ViewModels
{
    public class ConsultationNoteViewModel : INotifyPropertyChanged
    {
        private static readonly BrushConverter _brushConv = new();
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
        public DateTime Date   { get; }
        public string DateText => Date == DateTime.MinValue ? "" : Date.ToString("dd MMMM yyyy", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"));
        public string DateShortText => Date == DateTime.MinValue ? "" : Date.ToString("dd/MM/yyyy");
        public string Type     { get; }

        /// <summary>Le type pour l'écran : « consultation-premiere » → « 1ère consultation ».
        /// <see cref="Type"/> reste le code, sur lequel s'appuie la logique.</summary>
        public string TypeAffiche => LibelleType(Type);

        public static string LibelleType(string? type) => (type ?? "").Trim().ToLowerInvariant() switch
        {
            "consultation-premiere" => "1ère consultation",
            "consultation-suivi"    => "Consultation de suivi",
            "note-structuree"       => "Note structurée",
            "note"                  => "Note",
            var t when t.Length == 0 => "Note",
            var t when t.Contains('-') || t.Contains('_')
                => char.ToUpper(t[0]) + t[1..].Replace('-', ' ').Replace('_', ' '),
            _ => type!
        };
        public string Statut   { get; }
        public string StatutBadge { get; }
        public string StatutColor { get; }
        public string StatutBgColor { get; }
        public Brush StatutBrush => (Brush)(_brushConv.ConvertFromString(StatutColor) ?? Brushes.SlateGray);
        public Brush StatutBgBrush => (Brush)(_brushConv.ConvertFromString(StatutBgColor) ?? Brushes.Transparent);
        public string Icon     { get; }
        public string PreviewText { get; }
        public bool IsConsultationMedicale { get; }

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
        public ConsultationNoteViewModel(
            string title, 
            string filePath, 
            string displayContent,
            DateTime? date = null,
            string? type = null,
            string? statut = null)
        {
            Title           = title ?? "";
            FilePath        = filePath ?? "";
            _displayContent = displayContent ?? "";

            // Résolution de la date
            if (date.HasValue && date.Value > DateTime.MinValue)
                Date = date.Value;
            else
                Date = ExtraireDate(Title, FilePath);

            // Résolution du type
            Type = !string.IsNullOrWhiteSpace(type) 
                ? type 
                : DeterminerType(Title, FilePath);

            // Résolution du statut
            Statut = !string.IsNullOrWhiteSpace(statut) 
                ? statut 
                : DeterminerStatut(Title, _displayContent, Type);

            // Badge, icône et couleur selon statut & type
            (StatutBadge, StatutColor, StatutBgColor, Icon, IsConsultationMedicale) = EvaluerStatutEtStyle(Statut, Type);

            // Aperçu textuel nettoyé pour le cartouche
            PreviewText = NettoyerApercu(_displayContent);

            StartEditCommand  = new RelayCommand(_ => StartEdit());
            SaveEditCommand   = new RelayCommand(_ => SaveEdit(),  _ => IsEditing);
            CancelEditCommand = new RelayCommand(_ => CancelEdit());
            DeleteCommand     = new RelayCommand(_ => Delete());
        }

        private static DateTime ExtraireDate(string title, string filePath)
        {
            // 1. Chercher JJ/MM/AAAA dans le titre
            var m = System.Text.RegularExpressions.Regex.Match(title, @"(\d{1,2})[/\-.](\d{1,2})[/\-.](\d{4})");
            if (m.Success &&
                int.TryParse(m.Groups[1].Value, out var d) &&
                int.TryParse(m.Groups[2].Value, out var mo) &&
                int.TryParse(m.Groups[3].Value, out var y))
            {
                try { return new DateTime(y, mo, d); } catch { }
            }

            // 2. Chercher YYYY-MM-DD dans le nom de fichier
            if (!string.IsNullOrEmpty(filePath))
            {
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                var mIso = System.Text.RegularExpressions.Regex.Match(fileName, @"(\d{4})[_\-](\d{2})[_\-](\d{2})");
                if (mIso.Success &&
                    int.TryParse(mIso.Groups[1].Value, out var yIso) &&
                    int.TryParse(mIso.Groups[2].Value, out var mIsoVal) &&
                    int.TryParse(mIso.Groups[3].Value, out var dIso))
                {
                    try { return new DateTime(yIso, mIsoVal, dIso); } catch { }
                }

                if (File.Exists(filePath))
                    return File.GetLastWriteTime(filePath);
            }

            return DateTime.MinValue;
        }

        private static string DeterminerType(string title, string filePath)
        {
            var t = (title + " " + Path.GetFileName(filePath)).ToLowerInvariant();
            if (t.Contains("1ère") || t.Contains("1re") || t.Contains("première") || t.Contains("interrogatoire"))
                return "1ère consultation";
            if (t.Contains("urgence") || t.Contains("crise"))
                return "Urgence";
            if (t.Contains("restitution") || t.Contains("bilan"))
                return "Restitution";
            if (t.Contains("liaison") || t.Contains("ecole") || t.Contains("école") || t.Contains("appel") || t.Contains("téléphone") || t.Contains("telephone"))
                return "Note de liaison";
            if (t.Contains("absence") || t.Contains("absent") || t.Contains("annule") || t.Contains("annulé"))
                return "Absence / Annulation";
            if (t.Contains("suivi"))
                return "Consultation de suivi";

            return "Consultation";
        }

        private static string DeterminerStatut(string title, string content, string type)
        {
            var texte = (title + " " + content.Length switch { > 300 => content.Substring(0, 300), _ => content }).ToLowerInvariant();
            if (texte.Contains("statut: absent_excuse") || texte.Contains("absent excusé") || texte.Contains("absence excusée"))
                return "Absent excusé";
            if (texte.Contains("statut: absent") || texte.Contains("ne s'est pas présenté") || texte.Contains("absent non excusé") || texte.Contains("non honoré") || type == "Absence / Annulation")
                return "Absent";
            if (texte.Contains("statut: deplace") || texte.Contains("déplacé") || texte.Contains("annulé") || texte.Contains("statut: annule"))
                return "Déplacé / Annulé";
            if (type == "Note de liaison" || texte.Contains("statut: liaison") || texte.Contains("appel téléphonique") || texte.Contains("contact école"))
                return "Note de liaison";

            return "Présent";
        }

        private static (string badge, string color, string bgColor, string icon, bool isConsult) EvaluerStatutEtStyle(string statut, string type)
        {
            return statut switch
            {
                "Absent" => ("✕ Absent", "#C0392B", "#FDEDEC", "❌", false),
                "Absent excusé" => ("⏱ Excusé", "#D35400", "#FEF5E7", "⚠️", false),
                "Déplacé / Annulé" => ("📅 Reporté / Annulé", "#7F8C8D", "#F2F4F4", "⚪", false),
                "Note de liaison" => ("📞 Liaison / Coordination", "#2980B9", "#EBF5FB", "📞", false),
                _ => (
                    type switch
                    {
                        "1ère consultation" => ("✓ 1ère consultation", "#1E8449", "#EAFAF1", "🩺", true),
                        "Urgence" => ("✓ Consultation d'urgence", "#96281B", "#FADBD8", "🚨", true),
                        "Restitution" => ("✓ Restitution", "#2E86C1", "#EBF5FB", "📊", true),
                        _ => ("✓ Présent", "#1E8449", "#EAFAF1", "🔄", true)
                    }
                )
            };
        }

        private static string NettoyerApercu(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return "Aucune note rédigée.";
            var clean = System.Text.RegularExpressions.Regex.Replace(content, @"[#*_\-\[\]`>]", " ");
            clean = System.Text.RegularExpressions.Regex.Replace(clean, @"\s+", " ").Trim();
            return clean.Length > 150 ? clean.Substring(0, 147) + "..." : clean;
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
