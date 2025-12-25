using System;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using MedCompanion.Services;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Assistant de configuration initiale (première utilisation)
    /// Permet de créer mot de passe + PIN ou de désactiver la sécurité
    /// </summary>
    public partial class SetupWizardWindow : Window
    {
        private readonly AuthenticationService _authService;

        /// <summary>
        /// Indique si la configuration a été complétée
        /// </summary>
        public bool IsSetupComplete { get; private set; }

        public SetupWizardWindow(AuthenticationService authService)
        {
            InitializeComponent();
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));

            Loaded += (s, e) => PasswordBox.Focus();
        }

        private void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            ValidateForm();
        }

        private void PinBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            ValidateForm();
        }

        private void PinBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // N'accepter que les chiffres
            e.Handled = !Regex.IsMatch(e.Text, @"^[0-9]+$");
        }

        private void ValidateForm()
        {
            ErrorText.Text = "";

            var password = PasswordBox.Password;
            var confirmPassword = ConfirmPasswordBox.Password;
            var pin = PinBox.Text;

            bool isValid = true;

            // Vérifier le mot de passe
            if (string.IsNullOrWhiteSpace(password))
            {
                isValid = false;
            }
            else if (password.Length < 6)
            {
                isValid = false;
            }
            else if (password != confirmPassword)
            {
                isValid = false;
                if (!string.IsNullOrEmpty(confirmPassword))
                {
                    ErrorText.Text = "Les mots de passe ne correspondent pas.";
                }
            }

            // Vérifier le PIN
            if (string.IsNullOrWhiteSpace(pin) || pin.Length != 4)
            {
                isValid = false;
            }

            CreateButton.IsEnabled = isValid;
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            var password = PasswordBox.Password;
            var confirmPassword = ConfirmPasswordBox.Password;
            var pin = PinBox.Text;

            // Validation finale
            if (password != confirmPassword)
            {
                ErrorText.Text = "Les mots de passe ne correspondent pas.";
                return;
            }

            if (password.Length < 6)
            {
                ErrorText.Text = "Le mot de passe doit contenir au moins 6 caractères.";
                return;
            }

            if (pin.Length != 4 || !int.TryParse(pin, out _))
            {
                ErrorText.Text = "Le code PIN doit contenir exactement 4 chiffres.";
                return;
            }

            // Créer les credentials avec sécurité activée
            var (success, error) = _authService.SetupCredentials(password, pin, enableAuth: true);

            if (success)
            {
                IsSetupComplete = true;
                DialogResult = true;
                Close();
            }
            else
            {
                ErrorText.Text = error ?? "Erreur lors de la configuration.";
            }
        }

        private void SkipButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Voulez-vous vraiment désactiver la sécurité ?\n\n" +
                "L'application s'ouvrira sans demander de mot de passe ni de code PIN.\n\n" +
                "Vous pourrez activer la sécurité plus tard dans les paramètres.",
                "Désactiver la sécurité",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // Créer des credentials par défaut mais désactiver l'auth
                // On utilise des valeurs par défaut pour permettre une activation future
                var (success, error) = _authService.SetupCredentials("medcomp", "0000", enableAuth: false);

                if (success)
                {
                    IsSetupComplete = true;
                    DialogResult = true;
                    Close();
                }
                else
                {
                    ErrorText.Text = error ?? "Erreur lors de la configuration.";
                }
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Key == Key.Enter && CreateButton.IsEnabled)
            {
                CreateButton_Click(null!, null!);
            }
        }
    }
}
