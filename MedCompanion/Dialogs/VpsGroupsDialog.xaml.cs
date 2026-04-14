using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MedCompanion.Models;
using MedCompanion.Services;

namespace MedCompanion.Dialogs
{
    public partial class VpsGroupsDialog : Window
    {
        private readonly VpsGroupService _service;
        private List<VpsGroup> _allGroups = new();

        public VpsGroupsDialog(AppSettings settings)
        {
            InitializeComponent();
            _service = new VpsGroupService(settings);
            Loaded += async (s, e) => await LoadGroupsAsync();
        }

        private async System.Threading.Tasks.Task LoadGroupsAsync()
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            SearchBox.IsEnabled = false;

            try
            {
                var (success, groups, error) = await _service.GetAllGroupsAsync(IncludeEndedBox.IsChecked == true);
                if (success && groups != null)
                {
                    _allGroups = groups;
                    UpdateDisplay();
                    SearchBox.IsEnabled = true;
                }
                else
                {
                    MessageBox.Show($"Impossible de charger les groupes :\n{error}", "Erreur VPS", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Exception : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void UpdateDisplay()
        {
            var filter = SearchBox.Text.ToLower().Trim();
            var filtered = string.IsNullOrEmpty(filter)
                ? _allGroups
                : _allGroups.Where(g =>
                    (g.Titre?.ToLower().Contains(filter) ?? false) ||
                    (g.CreateurPseudo?.ToLower().Contains(filter) ?? false) ||
                    (g.Id?.ToLower().Contains(filter) ?? false) ||
                    (g.Theme?.ToLower().Contains(filter) ?? false)
                  ).ToList();

            GroupsGrid.ItemsSource = filtered;
            CountText.Text = $"{filtered.Count} groupe(s) affiché(s)";
            EmptyText.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e) => await LoadGroupsAsync();

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateDisplay();

        private async void IncludeEndedBox_Changed(object sender, RoutedEventArgs e)
        {
            if (IsLoaded) await LoadGroupsAsync();
        }

        private void GroupsGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            OpenEditor();
        }

        private void EditBtn_Click(object sender, RoutedEventArgs e) => OpenEditor();

        private async void DeleteBtn_Click(object sender, RoutedEventArgs e)
        {
            if (GroupsGrid.SelectedItem is not VpsGroup selected) return;

            var confirm = MessageBox.Show(
                $"Supprimer définitivement le groupe :\n\n{selected.Titre}\n({selected.Id})\n\nCette action est irréversible.",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            var (ok, err) = await _service.DeleteGroupAsync(selected.Id);
            if (ok)
            {
                MessageBox.Show("Groupe supprimé.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadGroupsAsync();
            }
            else
            {
                MessageBox.Show($"Erreur : {err}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void OpenEditor()
        {
            if (GroupsGrid.SelectedItem is not VpsGroup selected) return;

            var editor = new VpsGroupEditDialog(selected) { Owner = this };
            if (editor.ShowDialog() == true && editor.Patch != null && editor.Patch.Count > 0)
            {
                var (ok, err) = await _service.UpdateGroupAsync(selected.Id, editor.Patch);
                if (ok)
                {
                    await LoadGroupsAsync();
                }
                else
                {
                    MessageBox.Show($"Erreur : {err}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
