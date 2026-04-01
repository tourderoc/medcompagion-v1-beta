using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MedCompanion.Models;
using MedCompanion.Services;
using System.Linq;
using System.Threading.Tasks;

namespace MedCompanion.Views.Patient
{
    public partial class TokenDetailDialog : Window
    {
        private readonly PatientToken _token;
        private readonly PatientMetadata _patient;
        private readonly TokenService _tokenService;
        private readonly PatientMessageService _messageService;
        private readonly QRCodeService _qrService;
        private readonly TokenPdfService _pdfService;

        public bool WasModified { get; private set; } = false;

        public TokenDetailDialog(PatientToken token, PatientMetadata patient, 
                                TokenService tokenService, 
                                PatientMessageService messageService,
                                QRCodeService qrService,
                                TokenPdfService pdfService)
        {
            InitializeComponent();
            _token = token;
            _patient = patient;
            _tokenService = tokenService;
            _messageService = messageService;
            _qrService = qrService;
            _pdfService = pdfService;

            LoadData();
        }

        private void LoadData()
        {
            PatientNameTitle.Text = _patient.NomComplet?.ToUpper() ?? "PATIENT INCONNU";
            TokenIdLabel.Text = _token.TokenId;
            PseudoLabel.Text = _token.IsActivated ? _token.Pseudo : "En attente d'activation...";
            CreatedAtLabel.Text = _token.CreatedAt.ToString("g");

            // QR Code
            try
            {
                QRCodeImage.Source = _qrService.GenerateQRCode(_token.TokenId);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TokenDetail] Erreur QR: {ex.Message}");
            }

            // Statut
            UpdateStatusUI();

            // Stats messages
            LoadStats();
        }

        private void UpdateStatusUI()
        {
            if (!_token.Active)
            {
                StatusLabel.Text = "TOKEN RÉVOQUÉ";
                StatusLabel.Foreground = Brushes.Red;
                RevokeBtn.Visibility = Visibility.Collapsed;
            }
            else if (_token.IsActivated)
            {
                StatusLabel.Text = "TOKEN ACTIF (PARENT CONNECTÉ)";
                StatusLabel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E7D32"));
                RevokeBtn.Visibility = Visibility.Visible;
            }
            else
            {
                StatusLabel.Text = "EN ATTENTE D'ACTIVATION";
                StatusLabel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF6C00"));
                RevokeBtn.Visibility = Visibility.Visible;
            }
        }

        private void LoadStats()
        {
            if (string.IsNullOrEmpty(_patient.NomComplet)) return;

            var (success, messages, _) = _messageService.LoadMessagesForPatient(_patient.NomComplet);
            if (success)
            {
                int total = messages.Count;
                int pending = messages.Count(m => m.Status != "replied" && m.Status != "archived");

                TotalMsgLabel.Text = total.ToString();
                PendingMsgLabel.Text = pending.ToString();

                if (pending > 0)
                {
                    PendingStateBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF3E0"));
                    PendingMsgIcon.Text = "⏳ ";
                    PendingMsgIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF6C00"));
                    PendingMsgLabel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF6C00"));
                    PendingMsgSuffix.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF6C00"));
                }
                else
                {
                    PendingStateBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F8E9"));
                    PendingMsgIcon.Text = "✅ ";
                    PendingMsgIcon.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E7D32"));
                    PendingMsgLabel.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E7D32"));
                    // PendingMsgSuffix.Text already has " en attente" in XAML, let's keep it or change it
                    PendingMsgSuffix.Text = " traité" + (total > 1 ? "s" : "");
                    PendingMsgSuffix.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E7D32"));
                }
            }
        }

        private void PrintBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var pdfPath = _pdfService.GenerateTokenPdf(_token.TokenId, _patient.NomComplet ?? "Patient", _token.CreatedAt);
                
                // Ouvrir le PDF
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = pdfPath,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'impression : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void RevokeBtn_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Voulez-vous vraiment révoquer ce token ? Le parent ne pourra plus envoyer de messages.", 
                                       "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            
            if (result == MessageBoxResult.Yes)
            {
                // La révocation locale est faite par le service
                var (firebaseOk, _) = await _tokenService.RevokeTokenAsync(_token.TokenId);
                
                _token.Active = false;
                UpdateStatusUI();
                WasModified = true;
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
