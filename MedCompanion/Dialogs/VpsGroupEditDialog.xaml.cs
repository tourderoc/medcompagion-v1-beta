using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MedCompanion.Models;

namespace MedCompanion.Dialogs
{
    public partial class VpsGroupEditDialog : Window
    {
        private readonly VpsGroup _original;
        public Dictionary<string, object?>? Patch { get; private set; }

        public VpsGroupEditDialog(VpsGroup group)
        {
            InitializeComponent();
            _original = group;

            IdBox.Text = group.Id;
            TitreBox.Text = group.Titre;
            MaxBox.Text = group.ParticipantsMax.ToString();

            var localDate = group.DateVocal.ToLocalTime();
            DatePickerVocal.SelectedDate = localDate.Date;
            TimeBox.Text = localDate.ToString("HH:mm");

            SelectCombo(ThemeBox, group.Theme);
            SelectCombo(StatusBox, group.Status);
        }

        private static void SelectCombo(ComboBox combo, string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            foreach (var item in combo.Items.OfType<ComboBoxItem>())
            {
                if (string.Equals(item.Content?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = item;
                    return;
                }
            }
        }

        private static string? GetCombo(ComboBox combo) =>
            (combo.SelectedItem as ComboBoxItem)?.Content?.ToString();

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var patch = new Dictionary<string, object?>();

            if (TitreBox.Text.Trim() != (_original.Titre ?? string.Empty))
                patch["titre"] = TitreBox.Text.Trim();

            var theme = GetCombo(ThemeBox);
            if (!string.IsNullOrEmpty(theme) && theme != _original.Theme)
                patch["theme"] = theme;

            var status = GetCombo(StatusBox);
            if (!string.IsNullOrEmpty(status) && status != _original.Status)
                patch["status"] = status;

            if (int.TryParse(MaxBox.Text.Trim(), out int max) && max != _original.ParticipantsMax)
                patch["participants_max"] = max;

            if (DatePickerVocal.SelectedDate.HasValue &&
                TimeSpan.TryParseExact(TimeBox.Text.Trim(), "hh\\:mm", CultureInfo.InvariantCulture, out var time))
            {
                var newDateLocal = DatePickerVocal.SelectedDate.Value.Date + time;
                var newDateUtc = newDateLocal.ToUniversalTime();
                if (Math.Abs((newDateUtc - _original.DateVocal.ToUniversalTime()).TotalMinutes) > 0.5)
                {
                    patch["date_vocal"] = newDateUtc.ToString("o");
                }
            }

            if (patch.Count == 0)
            {
                MessageBox.Show("Aucun changement.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = false;
                return;
            }

            Patch = patch;
            DialogResult = true;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
