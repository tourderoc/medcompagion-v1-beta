using System.Windows;
using Microsoft.Win32;

namespace MedCompanion.Dialogs
{
    public partial class PatientPhotoDialog : Window
    {
        public string? SelectedImagePath { get; private set; }

        public PatientPhotoDialog(string? existingPhotoPath = null)
        {
            InitializeComponent();

            if (!string.IsNullOrEmpty(existingPhotoPath) && System.IO.File.Exists(existingPhotoPath))
            {
                ConsentPanel.Visibility = Visibility.Collapsed;
                ExistingPhotoImg.Visibility = Visibility.Visible;
                
                try 
                {
                    var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bitmap.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache;
                    bitmap.UriSource = new Uri(existingPhotoPath, UriKind.Absolute);
                    bitmap.EndInit();
                    ExistingPhotoImg.Source = bitmap;
                }
                catch { }

                HeaderTextBlock.Text = "Photo du patient";
                ImportFileBtn.Content = "Remplacer (fichier)";
                CameraBtn.Content = "Reprendre photo";

                ImportFileBtn.IsEnabled = true;
                CameraBtn.IsEnabled = true;
            }
        }

        private void ConsentCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool isChecked = ConsentCheckBox.IsChecked ?? false;
            ImportFileBtn.IsEnabled = isChecked;
            CameraBtn.IsEnabled = isChecked;
        }

        private void ImportFileBtn_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Sélectionnez une photo",
                Filter = "Fichiers image (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                SelectedImagePath = openFileDialog.FileName;
                DialogResult = true;
                Close();
            }
        }

        private void CameraBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cameraRollPath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Camera Roll");
                
                // Heure avant la capture (avec une petite marge)
                var beforeCapture = DateTime.Now.AddSeconds(-5);

                // Lancer l'application Caméra native de Windows
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("microsoft.windows.camera:") { UseShellExecute = true });

                MessageBox.Show("L'application Caméra s'est ouverte en arrière-plan ou par-dessus cette fenêtre.\n\n1. Prenez la photo.\n2. Fermez l'application Caméra.\n3. Cliquez sur 'OK' ici pour la récupérer automatiquement.", "Prendre une photo", MessageBoxButton.OK, MessageBoxImage.Information);

                if (System.IO.Directory.Exists(cameraRollPath))
                {
                    var directoryInfo = new System.IO.DirectoryInfo(cameraRollPath);
                    // Chercher la photo JPG ou PNG la plus récente
                    var newestFile = directoryInfo.GetFiles("*.*")
                        .Where(f => f.Extension.ToLower() == ".jpg" || f.Extension.ToLower() == ".jpeg" || f.Extension.ToLower() == ".png")
                        .OrderByDescending(f => f.CreationTime)
                        .FirstOrDefault();

                    if (newestFile != null && newestFile.CreationTime >= beforeCapture)
                    {
                        SelectedImagePath = newestFile.FullName;
                        DialogResult = true;
                        Close();
                    }
                    else
                    {
                        MessageBox.Show("Aucune nouvelle photo détectée. Assurez-vous d'avoir bien cliqué sur le bouton de capture dans l'application Caméra.", "Photo non trouvée", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    MessageBox.Show("Le dossier 'Pellicule' (Camera Roll) est introuvable sur votre système.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Impossible de lancer l'application Caméra : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelBtn_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
