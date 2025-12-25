using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MedCompanion.Services;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Fenêtre de connexion avec PIN ou mot de passe
    /// </summary>
    public partial class LoginWindow : Window
    {
        private readonly AuthenticationService _authService;
        private string _currentPin = "";
        private readonly bool _requirePassword;

        /// <summary>
        /// Indique si l'authentification a réussi
        /// </summary>
        public bool IsAuthenticated { get; private set; }

        public LoginWindow(AuthenticationService authService)
        {
            InitializeComponent();
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));

            // Déterminer si on demande le mot de passe ou le PIN
            _requirePassword = _authService.IsPasswordRequired;

            if (_requirePassword)
            {
                // Mode mot de passe
                ShowPasswordMode();
                SubtitleText.Text = "Connexion après 3 jours d'absence";
            }
            else
            {
                // Mode PIN
                ShowPinMode();
                SubtitleText.Text = "Entrez votre code PIN";
            }

            // Focus sur le bon contrôle
            Loaded += (s, e) =>
            {
                if (_requirePassword)
                    PasswordBox.Focus();
            };
        }

        #region Mode PIN

        private void ShowPinMode()
        {
            PinPanel.Visibility = Visibility.Visible;
            PasswordPanel.Visibility = Visibility.Collapsed;
            SubtitleText.Text = "Entrez votre code PIN";
            _currentPin = "";
            UpdatePinDots();
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && _currentPin.Length < 4)
            {
                _currentPin += btn.Content.ToString();
                UpdatePinDots();

                // Vérification automatique quand 4 chiffres entrés
                if (_currentPin.Length == 4)
                {
                    VerifyPin();
                }
            }
        }

        private void DeletePin_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPin.Length > 0)
            {
                _currentPin = _currentPin.Substring(0, _currentPin.Length - 1);
                UpdatePinDots();
                PinErrorText.Text = "";
            }
        }

        private void ClearPin_Click(object sender, RoutedEventArgs e)
        {
            _currentPin = "";
            UpdatePinDots();
            PinErrorText.Text = "";
        }

        private void UpdatePinDots()
        {
            var dots = new[] { PinDot1, PinDot2, PinDot3, PinDot4 };
            var filledColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0078D4"));
            var emptyColor = Brushes.Transparent;

            for (int i = 0; i < 4; i++)
            {
                dots[i].Fill = i < _currentPin.Length ? filledColor : emptyColor;
            }
        }

        private void VerifyPin()
        {
            var (success, error) = _authService.VerifyPin(_currentPin);

            if (success)
            {
                IsAuthenticated = true;
                DialogResult = true;
                Close();
            }
            else
            {
                PinErrorText.Text = error ?? "Code PIN incorrect";
                _currentPin = "";
                UpdatePinDots();

                // Animation visuelle d'erreur (optionnel)
                ShakePinDots();
            }
        }

        private async void ShakePinDots()
        {
            // Simple animation de secousse
            var originalMargin = PinDot1.Margin;
            for (int i = 0; i < 3; i++)
            {
                await System.Threading.Tasks.Task.Delay(50);
                PinDot1.Margin = new Thickness(originalMargin.Left + 5, originalMargin.Top, originalMargin.Right, originalMargin.Bottom);
                PinDot2.Margin = new Thickness(originalMargin.Left + 5, originalMargin.Top, originalMargin.Right, originalMargin.Bottom);
                PinDot3.Margin = new Thickness(originalMargin.Left + 5, originalMargin.Top, originalMargin.Right, originalMargin.Bottom);
                PinDot4.Margin = new Thickness(originalMargin.Left + 5, originalMargin.Top, originalMargin.Right, originalMargin.Bottom);

                await System.Threading.Tasks.Task.Delay(50);
                PinDot1.Margin = originalMargin;
                PinDot2.Margin = originalMargin;
                PinDot3.Margin = originalMargin;
                PinDot4.Margin = originalMargin;
            }
        }

        #endregion

        #region Mode Mot de passe

        private void ShowPasswordMode()
        {
            PinPanel.Visibility = Visibility.Collapsed;
            PasswordPanel.Visibility = Visibility.Visible;
            SubtitleText.Text = "Entrez votre mot de passe";
            PasswordBox.Password = "";
            PasswordErrorText.Text = "";
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            VerifyPassword();
        }

        private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                VerifyPassword();
            }
        }

        private void VerifyPassword()
        {
            var password = PasswordBox.Password;
            var (success, error) = _authService.VerifyPassword(password);

            if (success)
            {
                IsAuthenticated = true;
                DialogResult = true;
                Close();
            }
            else
            {
                PasswordErrorText.Text = error ?? "Mot de passe incorrect";
                PasswordBox.Password = "";
                PasswordBox.Focus();
            }
        }

        #endregion

        #region Navigation entre modes

        private void UsePassword_Click(object sender, RoutedEventArgs e)
        {
            ShowPasswordMode();
        }

        private void UsePin_Click(object sender, RoutedEventArgs e)
        {
            // Ne pas permettre de revenir au PIN si mot de passe requis
            if (!_requirePassword)
            {
                ShowPinMode();
            }
            else
            {
                PinErrorText.Text = "Mot de passe requis après 3 jours d'absence";
            }
        }

        #endregion

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            // Support clavier pour le PIN
            if (PinPanel.Visibility == Visibility.Visible)
            {
                if (e.Key >= Key.D0 && e.Key <= Key.D9)
                {
                    int digit = e.Key - Key.D0;
                    if (_currentPin.Length < 4)
                    {
                        _currentPin += digit.ToString();
                        UpdatePinDots();
                        if (_currentPin.Length == 4)
                            VerifyPin();
                    }
                }
                else if (e.Key >= Key.NumPad0 && e.Key <= Key.NumPad9)
                {
                    int digit = e.Key - Key.NumPad0;
                    if (_currentPin.Length < 4)
                    {
                        _currentPin += digit.ToString();
                        UpdatePinDots();
                        if (_currentPin.Length == 4)
                            VerifyPin();
                    }
                }
                else if (e.Key == Key.Back)
                {
                    DeletePin_Click(null!, null!);
                }
                else if (e.Key == Key.Escape)
                {
                    ClearPin_Click(null!, null!);
                }
            }
        }
    }
}
