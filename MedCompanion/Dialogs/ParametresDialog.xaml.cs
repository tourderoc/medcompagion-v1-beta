using System;
using System.Windows;
using System.Windows.Controls;
using MedCompanion.Services;
using MedCompanion.Services.LLM;
using MedCompanion.ViewModels;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Logique d'interaction pour ParametresDialog.xaml
    /// </summary>
    public partial class ParametresDialog : Window
    {
        private readonly ParametresViewModel _viewModel;
        public bool SettingsSaved { get; private set; }

        public ParametresDialog(
            SecureStorageService secureStorage,
            WindowStateService windowStateService,
            LLMServiceFactory? llmFactory = null)
        {
            InitializeComponent();

            _viewModel = new ParametresViewModel(secureStorage, windowStateService, llmFactory);
            DataContext = _viewModel;

            // Synchroniser PasswordBox avec ViewModel (binding ne fonctionne pas directement)
            OpenAIPasswordBox.PasswordChanged += (s, e) =>
            {
                _viewModel.OpenAIApiKey = OpenAIPasswordBox.Password;
            };

            // Initialiser le PasswordBox avec la valeur existante
            if (!string.IsNullOrEmpty(_viewModel.OpenAIApiKey))
            {
                OpenAIPasswordBox.Password = _viewModel.OpenAIApiKey;
            }

            SettingsSaved = false;
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _viewModel.SaveSettings();
                SettingsSaved = true;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Erreur lors de la sauvegarde des paramètres :\n{ex.Message}",
                    "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            SettingsSaved = false;
            DialogResult = false;
            Close();
        }
    }
}
