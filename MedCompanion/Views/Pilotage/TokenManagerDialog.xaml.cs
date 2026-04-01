using System;
using System.Collections.Generic;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MedCompanion.Models;
using MedCompanion.Services;

namespace MedCompanion.Views.Pilotage
{
    public partial class TokenManagerDialog : Window
    {
        private readonly TokenService _tokenService;
        private readonly QRCodeService _qrCodeService;
        private readonly List<PatientInfo> _availablePatients;
        private PatientToken? _selectedToken;

        /// <summary>
        /// Info patient simple pour la sélection
        /// </summary>
        public class PatientInfo
        {
            public string PatientId { get; set; } = string.Empty;
            public string DisplayName { get; set; } = string.Empty;
        }

        public TokenManagerDialog(TokenService tokenService, QRCodeService qrCodeService, List<PatientInfo> availablePatients)
        {
            InitializeComponent();
            _tokenService = tokenService;
            _qrCodeService = qrCodeService;
            _availablePatients = availablePatients;

            Loaded += async (s, e) => await RefreshTokenList();
        }

        /// <summary>
        /// Rafraîchit la liste des tokens
        /// </summary>
        private async System.Threading.Tasks.Task RefreshTokenList()
        {
            try
            {
                var tokens = await _tokenService.GetAllTokensAsync();
                TokensListBox.ItemsSource = tokens;

                EmptyStatePanel.Visibility = tokens.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                TokensListBox.Visibility = tokens.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du chargement des tokens:\n{ex.Message}",
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Création d'un nouveau token
        /// </summary>
        private async void NewTokenBtn_Click(object sender, RoutedEventArgs e)
        {
            // Dialog simple pour sélectionner un patient
            var dialog = new SelectPatientDialog(_availablePatients, _tokenService);
            dialog.Owner = this;

            if (dialog.ShowDialog() == true && dialog.SelectedPatient != null)
            {
                try
                {
                    var (token, firebaseOk, firebaseError) = await _tokenService.CreateTokenAsync(
                        dialog.SelectedPatient.PatientId,
                        dialog.SelectedPatient.DisplayName);

                    await RefreshTokenList();

                    // Sélectionner le nouveau token
                    TokensListBox.SelectedItem = token;

                    // Message avec info Firebase
                    var message = $"Token créé avec succès pour {dialog.SelectedPatient.DisplayName}!\n\n" +
                        "Vous pouvez maintenant imprimer le QR code ou le montrer au parent.";

                    if (!firebaseOk)
                    {
                        message += $"\n\n⚠️ Firebase: {firebaseError}\n" +
                            "Le token fonctionne localement mais ne sera pas validable sur Parent'aile.\n" +
                            $"Configurez Firebase dans: {_tokenService.GetFirebaseConfigPath()}";
                    }
                    else
                    {
                        message += "\n\n✅ Token synchronisé avec Parent'aile";
                    }

                    MessageBox.Show(message, "Token créé", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erreur lors de la création du token:\n{ex.Message}",
                        "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Sélection d'un token dans la liste
        /// </summary>
        private void TokensListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _selectedToken = TokensListBox.SelectedItem as PatientToken;

            if (_selectedToken == null)
            {
                NoSelectionPanel.Visibility = Visibility.Visible;
                TokenDetailPanel.Visibility = Visibility.Collapsed;
                return;
            }

            NoSelectionPanel.Visibility = Visibility.Collapsed;
            TokenDetailPanel.Visibility = Visibility.Visible;

            // Afficher les infos
            DetailPatientName.Text = _selectedToken.PatientDisplayName;
            DetailPseudo.Text = _selectedToken.IsActivated
                ? $"Pseudo: {_selectedToken.Pseudo}"
                : "(En attente d'activation par le parent)";
            DetailTokenId.Text = _selectedToken.TokenId;
            DetailUrl.Text = _qrCodeService.GetTokenUrl(_selectedToken.TokenId);

            // Générer le QR code
            try
            {
                var qrImage = _qrCodeService.GenerateQRCode(_selectedToken.TokenId);
                QRCodeImage.Source = qrImage;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la génération du QR code:\n{ex.Message}",
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // Ajuster le bouton révoquer selon le statut
            RevokeBtn.IsEnabled = _selectedToken.Active;
            RevokeBtn.Content = _selectedToken.Active ? "🗑️ Révoquer" : "❌ Révoqué";
        }

        /// <summary>
        /// Copie le token ID dans le presse-papiers
        /// </summary>
        private void CopyTokenBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedToken != null)
            {
                Clipboard.SetText(_selectedToken.TokenId);
                MessageBox.Show("Token ID copié dans le presse-papiers!",
                    "Copié", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// Génère et ouvre le PDF avec le QR code
        /// </summary>
        private void PrintQRBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedToken == null) return;

            try
            {
                // Utiliser le TokenPdfService pour générer le PDF
                var pdfService = new TokenPdfService();

                if (!pdfService.TemplateExists())
                {
                    // Fallback : impression simple si le template n'existe pas
                    PrintSimple();
                    return;
                }

                // Générer le PDF
                var pdfPath = pdfService.GenerateTokenPdf(
                    _selectedToken.TokenId,
                    _selectedToken.PatientDisplayName,
                    _selectedToken.CreatedAt
                );

                // Ouvrir le PDF avec l'application par défaut
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = pdfPath,
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(psi);

                MessageBox.Show(
                    $"PDF généré avec succès!\n\nFichier: {pdfPath}\n\nVous pouvez maintenant l'imprimer depuis votre lecteur PDF.",
                    "PDF généré",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la génération du PDF:\n{ex.Message}",
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Impression simple (fallback si pas de template)
        /// </summary>
        private void PrintSimple()
        {
            if (_selectedToken == null) return;

            var printDialog = new PrintDialog();
            if (printDialog.ShowDialog() == true)
            {
                var printDoc = CreatePrintDocument(_selectedToken);
                printDialog.PrintDocument(((IDocumentPaginatorSource)printDoc).DocumentPaginator,
                    $"Token Parent'aile - {_selectedToken.PatientDisplayName}");

                MessageBox.Show("QR code envoyé à l'imprimante!",
                    "Impression", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// Crée le document FlowDocument pour l'impression
        /// </summary>
        private FlowDocument CreatePrintDocument(PatientToken token)
        {
            var doc = new FlowDocument
            {
                PagePadding = new Thickness(50),
                FontFamily = new FontFamily("Segoe UI")
            };

            // Titre
            var title = new Paragraph(new Run("Parent'aile"))
            {
                FontSize = 28,
                FontWeight = FontWeights.Bold,
                TextAlignment = TextAlignment.Center,
                Foreground = new SolidColorBrush(Color.FromRgb(230, 126, 34))
            };
            doc.Blocks.Add(title);

            // Sous-titre
            var subtitle = new Paragraph(new Run("Votre accès personnel"))
            {
                FontSize = 16,
                TextAlignment = TextAlignment.Center,
                Foreground = Brushes.Gray
            };
            doc.Blocks.Add(subtitle);

            // Espace
            doc.Blocks.Add(new Paragraph { FontSize = 20 });

            // QR Code
            var qrImage = _qrCodeService.GenerateQRCode(token.TokenId);
            var image = new System.Windows.Controls.Image
            {
                Source = qrImage,
                Width = 250,
                Height = 250
            };
            var container = new BlockUIContainer(image) { TextAlignment = TextAlignment.Center };
            doc.Blocks.Add(container);

            // Instructions
            doc.Blocks.Add(new Paragraph { FontSize = 20 });

            var instructions = new Paragraph
            {
                FontSize = 12,
                TextAlignment = TextAlignment.Center
            };
            instructions.Inlines.Add(new Run("Scannez ce QR code avec votre téléphone\n"));
            instructions.Inlines.Add(new Run("ou rendez-vous sur:\n\n"));
            instructions.Inlines.Add(new Run(_qrCodeService.GetTokenUrl(token.TokenId))
            {
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(230, 126, 34))
            });
            doc.Blocks.Add(instructions);

            // Note en bas
            doc.Blocks.Add(new Paragraph { FontSize = 30 });

            var note = new Paragraph(new Run("Ce code est personnel et confidentiel.\nNe le partagez pas."))
            {
                FontSize = 10,
                TextAlignment = TextAlignment.Center,
                Foreground = Brushes.Gray,
                FontStyle = FontStyles.Italic
            };
            doc.Blocks.Add(note);

            return doc;
        }

        /// <summary>
        /// Révoque un token
        /// </summary>
        private async void RevokeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedToken == null) return;

            var result = MessageBox.Show(
                $"Êtes-vous sûr de vouloir révoquer le token de {_selectedToken.PatientDisplayName}?\n\n" +
                "Le parent ne pourra plus envoyer de messages via Parent'aile.\n" +
                "L'historique sera conservé.",
                "Confirmer la révocation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    var (firebaseOk, firebaseError) = await _tokenService.RevokeTokenAsync(_selectedToken.TokenId);
                    await RefreshTokenList();

                    var message = "Token révoqué avec succès.";
                    if (!firebaseOk)
                    {
                        message += $"\n\n⚠️ Firebase: {firebaseError}";
                    }

                    MessageBox.Show(message, "Révoqué", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erreur lors de la révocation:\n{ex.Message}",
                        "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }

    /// <summary>
    /// Dialog simple pour sélectionner un patient
    /// </summary>
    public partial class SelectPatientDialog : Window
    {
        private readonly TokenService _tokenService;
        public TokenManagerDialog.PatientInfo? SelectedPatient { get; private set; }

        public SelectPatientDialog(List<TokenManagerDialog.PatientInfo> patients, TokenService tokenService)
        {
            _tokenService = tokenService;

            Title = "Sélectionner un patient";
            Width = 400;
            Height = 500;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
            ResizeMode = ResizeMode.NoResize;

            var grid = new Grid { Margin = new Thickness(20) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Titre
            var title = new TextBlock
            {
                Text = "Sélectionner un patient",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 15)
            };
            Grid.SetRow(title, 0);
            grid.Children.Add(title);

            // Recherche
            var searchBox = new TextBox
            {
                Background = new SolidColorBrush(Color.FromRgb(61, 61, 61)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(searchBox, 1);
            grid.Children.Add(searchBox);

            // Liste patients
            var listBox = new ListBox
            {
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                BorderThickness = new Thickness(0),
                Foreground = Brushes.White
            };
            listBox.ItemsSource = patients;
            listBox.DisplayMemberPath = "DisplayName";
            Grid.SetRow(listBox, 2);
            grid.Children.Add(listBox);

            // Filtrage
            searchBox.TextChanged += (s, e) =>
            {
                var filter = searchBox.Text.ToLower();
                listBox.ItemsSource = string.IsNullOrEmpty(filter)
                    ? patients
                    : patients.FindAll(p => p.DisplayName.ToLower().Contains(filter));
            };

            // Boutons
            var buttonsPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 15, 0, 0)
            };

            var cancelBtn = new Button
            {
                Content = "Annuler",
                Padding = new Thickness(15, 8, 15, 8),
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush(Color.FromRgb(61, 61, 61)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            cancelBtn.Click += (s, e) => { DialogResult = false; Close(); };
            buttonsPanel.Children.Add(cancelBtn);

            var selectBtn = new Button
            {
                Content = "Générer token",
                Padding = new Thickness(15, 8, 15, 8),
                Background = new SolidColorBrush(Color.FromRgb(230, 126, 34)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0)
            };
            selectBtn.Click += async (s, e) =>
            {
                var selected = listBox.SelectedItem as TokenManagerDialog.PatientInfo;
                if (selected == null)
                {
                    MessageBox.Show("Veuillez sélectionner un patient.", "Sélection requise",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Vérifier si le patient a déjà un token actif
                if (await _tokenService.HasActiveTokenAsync(selected.PatientId))
                {
                    MessageBox.Show(
                        $"{selected.DisplayName} a déjà un token actif.\n\nRévoquez-le d'abord si vous souhaitez en créer un nouveau.",
                        "Token existant",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                SelectedPatient = selected;
                DialogResult = true;
                Close();
            };
            buttonsPanel.Children.Add(selectBtn);

            Grid.SetRow(buttonsPanel, 3);
            grid.Children.Add(buttonsPanel);

            Content = grid;
        }
    }
}
