using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MedCompanion.Services;

namespace MedCompanion.Dialogs
{
    public partial class PatientPhotoImportDialog : Window
    {
        private readonly PatientPhotoService _photoService;
        private readonly string _nomComplet;
        private readonly string _initials;
        
        public string? SelectedPhotoPath { get; private set; }
        public bool HasConsent { get; private set; }

        public PatientPhotoImportDialog(PatientPhotoService photoService, string nomComplet, string initials, string? currentPhotoPath = null, bool currentConsent = false)
        {
            InitializeComponent();
            _photoService = photoService;
            _nomComplet = nomComplet;
            _initials = initials;

            InitialsText.Text = _initials;
            ConsentCheckBox.IsChecked = currentConsent;
            HasConsent = currentConsent;

            if (!string.IsNullOrEmpty(currentPhotoPath) && File.Exists(currentPhotoPath))
            {
                LoadPreview(currentPhotoPath);
            }
        }

        private void ConsentCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool isChecked = ConsentCheckBox.IsChecked == true;
            HasConsent = isChecked;
            ImportFileButton.IsEnabled = isChecked;
            CameraFileButton.IsEnabled = isChecked;
        }

        private async void ImportFileButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Sélectionner une photo",
                Filter = "Images (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            };

            if (openFileDialog.ShowDialog() == true)
            {
                await ProcessSelectedPhoto(openFileDialog.FileName);
            }
        }

        private async void CameraFileButton_Click(object sender, RoutedEventArgs e)
        {
            // Lancer l'application Caméra native de Windows 10/11
            try
            {
                Process.Start(new ProcessStartInfo("microsoft.windows.camera:") { UseShellExecute = true });
            }
            catch
            {
                MessageBox.Show("Impossible de lancer l'application Caméra de Windows. Veuillez ouvrir l'application Caméra manuellement.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            // Ouvrir directement le dossier "Pellicule" (Camera Roll) où Windows sauvegarde les photos
            string cameraRollPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Camera Roll");
            if (!Directory.Exists(cameraRollPath))
            {
                cameraRollPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            }

            MessageBox.Show("Prenez la photo avec l'application Caméra, puis sélectionnez-la dans la fenêtre qui va s'ouvrir.", "Prendre une photo", MessageBoxButton.OK, MessageBoxImage.Information);

            var openFileDialog = new OpenFileDialog
            {
                Title = "Sélectionnez la photo que vous venez de prendre",
                Filter = "Images (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
                InitialDirectory = cameraRollPath
            };

            if (openFileDialog.ShowDialog() == true)
            {
                await ProcessSelectedPhoto(openFileDialog.FileName);
            }
        }

        private async Task ProcessSelectedPhoto(string sourcePath)
        {
            try
            {
                // UI feedback
                ImportFileButton.IsEnabled = false;
                CameraFileButton.IsEnabled = false;
                ImportFileButton.Content = "Importation...";

                // Sauvegarde via le service
                string? savedFileName = await _photoService.ImportPhotoAsync(sourcePath, _nomComplet);

                if (savedFileName != null)
                {
                    SelectedPhotoPath = savedFileName;
                    DialogResult = true;
                    Close();
                }
                else
                {
                    MessageBox.Show("Une erreur est survenue lors de l'import de la photo.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    ImportFileButton.IsEnabled = true;
                    CameraFileButton.IsEnabled = true;
                    ImportFileButton.Content = "Importer du PC";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur: {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                ImportFileButton.IsEnabled = true;
                CameraFileButton.IsEnabled = true;
                ImportFileButton.Content = "Importer du PC";
            }
        }

        private void LoadPreview(string imagePath)
        {
            try
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(imagePath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                
                PhotoBrush.ImageSource = bitmap;
                InitialsText.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur preview image: {ex.Message}");
            }
        }
    }
}
