using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MedCompanion.Models;
using MedCompanion.Services;

namespace MedCompanion.Dialogs
{
    public partial class VpsAccountsDialog : Window
    {
        private readonly VpsAccountService _accountService;
        private List<VpsAccount> _allAccounts = new List<VpsAccount>();

        public VpsAccountsDialog(AppSettings settings)
        {
            InitializeComponent();
            _accountService = new VpsAccountService(settings);
            
            Loaded += async (s, e) => await LoadAccountsAsync();
        }

        private async System.Threading.Tasks.Task LoadAccountsAsync()
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            SearchBox.IsEnabled = false;

            try
            {
                var (success, accounts, error) = await _accountService.GetAllAccountsAsync();
                
                if (success && accounts != null)
                {
                    _allAccounts = accounts;
                    UpdateDisplay();
                    SearchBox.IsEnabled = true;
                }
                else
                {
                    MessageBox.Show($"Impossible de charger les comptes :\n{error}", "Erreur VPS", MessageBoxButton.OK, MessageBoxImage.Error);
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
                ? _allAccounts 
                : _allAccounts.Where(a => 
                    (a.Pseudo?.ToLower().Contains(filter) ?? false) || 
                    (a.Email?.ToLower().Contains(filter) ?? false) ||
                    (a.Uid?.ToLower().Contains(filter) ?? false)
                  ).ToList();

            AccountsGrid.ItemsSource = filtered;
            CountText.Text = $"{filtered.Count} compte(s) affiché(s)";
            EmptyText.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            await LoadAccountsAsync();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateDisplay();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
