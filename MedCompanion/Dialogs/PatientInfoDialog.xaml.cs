using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MedCompanion.Models;
using MedCompanion.Services;
using Microsoft.Win32;

namespace MedCompanion.Dialogs;

public partial class PatientInfoDialog : Window
{
    private readonly PatientIndexEntry _patient;
    private readonly PatientIndexService _patientIndex;
    private readonly PathService _pathService = new PathService();
    private PatientMetadata? _metadata;
    private bool _hasUnsavedChanges = false;

    // Pour l'import depuis le presse-papier
    private readonly List<string> _pastedImagePaths = new List<string>();
    private int _pasteCount = 0;

    public PatientInfoDialog(PatientIndexEntry patient, PatientIndexService patientIndex)
    {
        InitializeComponent();

        _patient = patient;
        _patientIndex = patientIndex;

        Loaded += PatientInfoDialog_Loaded;
        Closing += PatientInfoDialog_Closing;
    }

    private void PatientInfoDialog_Loaded(object sender, RoutedEventArgs e)
    {
        // Charger les métadonnées existantes
        _metadata = _patientIndex.GetMetadata(_patient.Id);

        if (_metadata == null)
        {
            // Créer nouvelles métadonnées avec les infos de base
            _metadata = new PatientMetadata
            {
                Nom = _patient.Nom,
                Prenom = _patient.Prenom,
                Dob = _patient.Dob,
                Sexe = _patient.Sexe
            };
        }

        // Afficher nom patient dans le header
        PatientNameText.Text = $"{_metadata.Prenom} {_metadata.Nom}";

        // Attacher les handlers AVANT de charger les données
        AttachChangeHandlers();

        // Charger les données dans le formulaire (cela déclenchera les handlers)
        LoadDataToForm();

        // Remettre à false car le chargement initial n'est pas une modification
        _hasUnsavedChanges = false;
    }

    private void LoadDataToForm()
    {
        if (_metadata == null) return;

        // === Identité ===
        NomTextBox.Text = _metadata.Nom;
        PrenomTextBox.Text = _metadata.Prenom;

        if (!string.IsNullOrEmpty(_metadata.Dob) && DateTime.TryParse(_metadata.Dob, out var dob))
        {
            DobDatePicker.SelectedDate = dob;
        }

        // Sexe
        if (!string.IsNullOrEmpty(_metadata.Sexe))
        {
            foreach (ComboBoxItem item in SexeComboBox.Items)
            {
                if (item.Tag?.ToString() == _metadata.Sexe)
                {
                    SexeComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        LieuNaissanceTextBox.Text = _metadata.LieuNaissance ?? "";

        // === Adresse ===
        AdresseRueTextBox.Text = _metadata.AdresseRue ?? "";
        AdresseCodePostalTextBox.Text = _metadata.AdresseCodePostal ?? "";
        AdresseVilleTextBox.Text = _metadata.AdresseVille ?? "";
        AdressePaysTextBox.Text = _metadata.AdressePays ?? "France";

        // === Sécurité sociale ===
        NumeroSecuriteSocialeTextBox.Text = _metadata.NumeroSecuriteSociale ?? "";
        NumeroINSTextBox.Text = _metadata.NumeroINS ?? "";

        // === Accompagnant ===
        AccompagnantNomTextBox.Text = _metadata.AccompagnantNom ?? "";
        AccompagnantPrenomTextBox.Text = _metadata.AccompagnantPrenom ?? "";
        AccompagnantTelephoneTextBox.Text = _metadata.AccompagnantTelephone ?? "";
        AccompagnantEmailTextBox.Text = _metadata.AccompagnantEmail ?? "";

        // Lien accompagnant
        if (!string.IsNullOrEmpty(_metadata.AccompagnantLien))
        {
            foreach (ComboBoxItem item in AccompagnantLienComboBox.Items)
            {
                if (item.Content?.ToString() == _metadata.AccompagnantLien)
                {
                    AccompagnantLienComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        // === Situation ===
        if (!string.IsNullOrEmpty(_metadata.SituationAccueil))
        {
            foreach (ComboBoxItem item in SituationAccueilComboBox.Items)
            {
                if (item.Content?.ToString() == _metadata.SituationAccueil)
                {
                    SituationAccueilComboBox.SelectedItem = item;
                    break;
                }
            }
        }

        // === Scolarité ===
        EcoleTextBox.Text = _metadata.Ecole ?? "";
        ClasseTextBox.Text = _metadata.Classe ?? "";
    }

    private void AttachChangeHandlers()
    {
        // Attacher handlers pour tracker les modifications
        NomTextBox.TextChanged += (s, e) => _hasUnsavedChanges = true;
        PrenomTextBox.TextChanged += (s, e) => _hasUnsavedChanges = true;
        DobDatePicker.SelectedDateChanged += (s, e) => _hasUnsavedChanges = true;
        SexeComboBox.SelectionChanged += (s, e) => _hasUnsavedChanges = true;
        LieuNaissanceTextBox.TextChanged += (s, e) => _hasUnsavedChanges = true;
        AdresseRueTextBox.TextChanged += (s, e) => _hasUnsavedChanges = true;
        AdresseCodePostalTextBox.TextChanged += (s, e) => _hasUnsavedChanges = true;
        AdresseVilleTextBox.TextChanged += (s, e) => _hasUnsavedChanges = true;
        AdressePaysTextBox.TextChanged += (s, e) => _hasUnsavedChanges = true;
        NumeroSecuriteSocialeTextBox.TextChanged += (s, e) => _hasUnsavedChanges = true;
        NumeroINSTextBox.TextChanged += (s, e) => _hasUnsavedChanges = true;
        AccompagnantNomTextBox.TextChanged += (s, e) => _hasUnsavedChanges = true;
        AccompagnantPrenomTextBox.TextChanged += (s, e) => _hasUnsavedChanges = true;
        AccompagnantLienComboBox.SelectionChanged += (s, e) => _hasUnsavedChanges = true;
        AccompagnantTelephoneTextBox.TextChanged += (s, e) => _hasUnsavedChanges = true;
        AccompagnantEmailTextBox.TextChanged += (s, e) => _hasUnsavedChanges = true;
        SituationAccueilComboBox.SelectionChanged += (s, e) => _hasUnsavedChanges = true;
        EcoleTextBox.TextChanged += (s, e) => _hasUnsavedChanges = true;
        ClasseTextBox.TextChanged += (s, e) => _hasUnsavedChanges = true;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // Validation
        if (string.IsNullOrWhiteSpace(NomTextBox.Text) || string.IsNullOrWhiteSpace(PrenomTextBox.Text))
        {
            MessageBox.Show(
                "Le nom et le prénom sont obligatoires.",
                "Validation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!DobDatePicker.SelectedDate.HasValue)
        {
            MessageBox.Show(
                "La date de naissance est obligatoire.",
                "Validation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (SexeComboBox.SelectedItem == null)
        {
            MessageBox.Show(
                "Le sexe est obligatoire.",
                "Validation",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            // Récupérer les données du formulaire
            if (_metadata == null)
                _metadata = new PatientMetadata();

            // === Identité ===
            _metadata.Nom = NomTextBox.Text.Trim();
            _metadata.Prenom = PrenomTextBox.Text.Trim();
            _metadata.Dob = DobDatePicker.SelectedDate.Value.ToString("yyyy-MM-dd");
            _metadata.Sexe = (SexeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString();
            _metadata.LieuNaissance = string.IsNullOrWhiteSpace(LieuNaissanceTextBox.Text)
                ? null : LieuNaissanceTextBox.Text.Trim();

            // === Adresse ===
            _metadata.AdresseRue = string.IsNullOrWhiteSpace(AdresseRueTextBox.Text)
                ? null : AdresseRueTextBox.Text.Trim();
            _metadata.AdresseCodePostal = string.IsNullOrWhiteSpace(AdresseCodePostalTextBox.Text)
                ? null : AdresseCodePostalTextBox.Text.Trim();
            _metadata.AdresseVille = string.IsNullOrWhiteSpace(AdresseVilleTextBox.Text)
                ? null : AdresseVilleTextBox.Text.Trim();
            _metadata.AdressePays = string.IsNullOrWhiteSpace(AdressePaysTextBox.Text)
                ? "France" : AdressePaysTextBox.Text.Trim();

            // === Sécurité sociale ===
            _metadata.NumeroSecuriteSociale = string.IsNullOrWhiteSpace(NumeroSecuriteSocialeTextBox.Text)
                ? null : CleanNIR(NumeroSecuriteSocialeTextBox.Text);
            _metadata.NumeroINS = string.IsNullOrWhiteSpace(NumeroINSTextBox.Text)
                ? null : NumeroINSTextBox.Text.Trim();

            // === Accompagnant ===
            _metadata.AccompagnantNom = string.IsNullOrWhiteSpace(AccompagnantNomTextBox.Text)
                ? null : AccompagnantNomTextBox.Text.Trim();
            _metadata.AccompagnantPrenom = string.IsNullOrWhiteSpace(AccompagnantPrenomTextBox.Text)
                ? null : AccompagnantPrenomTextBox.Text.Trim();
            _metadata.AccompagnantLien = (AccompagnantLienComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            _metadata.AccompagnantTelephone = string.IsNullOrWhiteSpace(AccompagnantTelephoneTextBox.Text)
                ? null : CleanPhoneNumber(AccompagnantTelephoneTextBox.Text);
            _metadata.AccompagnantEmail = string.IsNullOrWhiteSpace(AccompagnantEmailTextBox.Text)
                ? null : AccompagnantEmailTextBox.Text.Trim();

            // === Situation ===
            _metadata.SituationAccueil = (SituationAccueilComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();

            // === Scolarité ===
            _metadata.Ecole = string.IsNullOrWhiteSpace(EcoleTextBox.Text)
                ? null : EcoleTextBox.Text.Trim();
            _metadata.Classe = string.IsNullOrWhiteSpace(ClasseTextBox.Text)
                ? null : ClasseTextBox.Text.Trim();

            // Sauvegarder dans patient.json
            SaveMetadataToJson();

            _hasUnsavedChanges = false;

            MessageBox.Show(
                "Les informations du patient ont été sauvegardées avec succès.",
                "Sauvegarde réussie",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Erreur lors de la sauvegarde :\n\n{ex.Message}",
                "Erreur",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void SaveMetadataToJson()
    {
        if (_metadata == null) return;

        // Chemin vers patient.json
        var infoPatientDir = Path.Combine(_patient.DirectoryPath, "info_patient");
        Directory.CreateDirectory(infoPatientDir);

        var jsonPath = Path.Combine(infoPatientDir, "patient.json");

        // Sérialiser avec indentation
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        var json = JsonSerializer.Serialize(_metadata, options);
        File.WriteAllText(jsonPath, json, Encoding.UTF8);

        // Note: L'index sera rechargé automatiquement lors du prochain GetMetadata()
    }

    private string CleanNIR(string nir)
    {
        // Nettoyer le NIR : enlever espaces et ne garder que les chiffres
        var cleaned = new string(nir.Where(char.IsDigit).ToArray());

        // Valider longueur (13 ou 15 chiffres)
        if (cleaned.Length != 13 && cleaned.Length != 15)
        {
            MessageBox.Show(
                $"Le numéro de sécurité sociale doit contenir 13 ou 15 chiffres.\nActuel : {cleaned.Length} chiffres",
                "Validation NIR",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        return cleaned;
    }

    private string CleanPhoneNumber(string phone)
    {
        // Nettoyer le numéro de téléphone : enlever espaces, points, tirets
        var cleaned = new string(phone.Where(c => char.IsDigit(c) || c == '+').ToArray());
        return cleaned;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void PatientInfoDialog_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_hasUnsavedChanges)
        {
            var result = MessageBox.Show(
                "Vous avez des modifications non sauvegardées.\n\nVoulez-vous vraiment fermer sans sauvegarder ?",
                "Modifications non sauvegardées",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.No)
            {
                e.Cancel = true;
                return;
            }
        }

        // Nettoyer les fichiers temporaires des images collées avant de fermer
        CleanupTempFiles();
    }

    // ========== IMPORT DOCTOLIB ==========

    private void PasteFromClipboardButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Vérifier si le presse-papier contient une image
            if (!Clipboard.ContainsImage())
            {
                MessageBox.Show(
                    "Aucune image dans le presse-papier.\n\n" +
                    "Procédure :\n" +
                    "1. Faites une capture d'écran Doctolib (partie haute)\n" +
                    "2. Copiez l'image (Ctrl+C ou clic droit > Copier)\n" +
                    "3. Cliquez sur ce bouton pour coller\n" +
                    "4. Répétez pour la capture partie basse (optionnel)\n" +
                    "5. Au 2e clic, l'OCR se lance automatiquement",
                    "Presse-papier vide",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            // Récupérer l'image du presse-papier
            var image = Clipboard.GetImage();
            if (image == null)
            {
                MessageBox.Show("Erreur lors de la récupération de l'image.", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Sauvegarder l'image dans un fichier temporaire
            var tempPath = Path.Combine(Path.GetTempPath(), $"doctolib_{_pasteCount + 1}_{Guid.NewGuid()}.png");
            SaveBitmapSourceToFile(image, tempPath);

            _pastedImagePaths.Add(tempPath);
            _pasteCount++;

            // Afficher l'image dans la zone d'aperçu
            if (_pasteCount == 1)
            {
                PreviewImage1.Source = image;
                PreviewBorder.Visibility = Visibility.Visible;
                ClearImagesButton.Visibility = Visibility.Visible;

                PasteFromClipboardButton.Content = $"✅ Image 1 OK - Coller image 2";
                PasteFromClipboardButton.Background = new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22)); // Orange

                MessageBox.Show(
                    "✅ Image 1 collée avec succès !\n\n" +
                    "📸 Prochaine étape :\n" +
                    "1. Copiez la capture partie basse (Ctrl+C)\n" +
                    "2. Cliquez à nouveau sur ce bouton\n\n" +
                    "Ou cliquez sur 'Parcourir' pour traiter uniquement cette image.",
                    "Image 1 collée",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else if (_pasteCount == 2)
            {
                PreviewImage2.Source = image;

                // Lancer l'OCR avec les 2 images
                ProcessPastedImages();

                // Réinitialiser (mais garder l'aperçu visible jusqu'à la fermeture du dialog)
                ResetPasteState();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Erreur lors du collage :\n\n{ex.Message}",
                "Erreur",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Sauvegarde une BitmapSource dans un fichier PNG
    /// </summary>
    private void SaveBitmapSourceToFile(BitmapSource image, string filePath)
    {
        using (var fileStream = new FileStream(filePath, FileMode.Create))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            encoder.Save(fileStream);
        }
    }

    /// <summary>
    /// Traite les images collées avec l'OCR
    /// </summary>
    private void ProcessPastedImages()
    {
        if (_pastedImagePaths.Count == 0)
            return;

        var ocrService = new TesseractOCRService();

        try
        {
            // Extraire le texte de la première image
            var (success1, text1, confidence1, error1) = ocrService.ExtractTextFromImage(_pastedImagePaths[0]);
            if (!success1)
            {
                MessageBox.Show(error1, "Erreur OCR", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Extraire le texte de la deuxième image (si présente)
            string text2 = "";
            float confidence2 = 0f;
            if (_pastedImagePaths.Count == 2)
            {
                var (success2, txt2, conf2, error2) = ocrService.ExtractTextFromImage(_pastedImagePaths[1]);
                if (!success2)
                {
                    MessageBox.Show(error2, "Erreur OCR", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                text2 = txt2;
                confidence2 = conf2;
            }

            // Parser les données Doctolib
            var parsedData = ocrService.ParseDoctolibData(text1, text2);

            // Afficher résumé de la confiance
            float avgConfidence = _pastedImagePaths.Count == 2
                ? (confidence1 + confidence2) / 2
                : confidence1;

            var confirmMessage = $"📸 Import Doctolib - Confiance OCR : {avgConfidence:P0}\n\n" +
                                 $"✅ {parsedData.Count} champ(s) détecté(s)\n\n" +
                                 $"Les champs seront pré-remplis avec code couleur :\n" +
                                 $"🟩 Vert = Confiance élevée (>80%)\n" +
                                 $"🟧 Orange = À vérifier (50-80%)\n" +
                                 $"🟥 Rouge = Incertain (<50%)\n\n" +
                                 $"⚠️ Vérifiez toutes les données avant de sauvegarder.\n\n" +
                                 $"Continuer ?";

            var result = MessageBox.Show(confirmMessage, "Import Doctolib",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            // Pré-remplir les champs avec code couleur
            ApplyDoctolibData(parsedData);

            MessageBox.Show(
                $"✅ Import terminé !\n\n" +
                $"{parsedData.Count} champ(s) pré-rempli(s).\n" +
                $"Vérifiez les données (code couleur) avant de sauvegarder.\n\n" +
                $"💡 Les aperçus restent visibles. Cliquez sur '🗑️ Effacer' pour les supprimer.",
                "Import réussi",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Erreur lors de l'import Doctolib :\n\n{ex.Message}",
                "Erreur",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        // Note: Les fichiers temporaires seront nettoyés quand l'utilisateur clique sur "Effacer" ou ferme le dialog
    }

    /// <summary>
    /// Réinitialise l'état du collage
    /// </summary>
    private void ResetPasteState()
    {
        _pasteCount = 0;
        PasteFromClipboardButton.Content = "📋 Coller captures (Ctrl+V)";
        PasteFromClipboardButton.Background = new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60)); // Vert
    }

    /// <summary>
    /// Nettoie les fichiers temporaires
    /// </summary>
    private void CleanupTempFiles()
    {
        foreach (var path in _pastedImagePaths)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // Ignorer les erreurs de suppression
            }
        }
        _pastedImagePaths.Clear();
    }

    /// <summary>
    /// Efface les images collées et masque la zone d'aperçu
    /// </summary>
    private void ClearImagesButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Nettoyer les fichiers temporaires
            CleanupTempFiles();

            // Effacer les aperçus d'images
            PreviewImage1.Source = null;
            PreviewImage2.Source = null;

            // Masquer la zone d'aperçu
            PreviewBorder.Visibility = Visibility.Collapsed;
            ClearImagesButton.Visibility = Visibility.Collapsed;

            // Réinitialiser l'état du bouton de collage
            ResetPasteState();

            MessageBox.Show(
                "Images effacées avec succès.",
                "Effacement",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Erreur lors de l'effacement :\n\n{ex.Message}",
                "Erreur",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenTessDataFolderButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Créer une instance du service pour obtenir le chemin
            var ocrService = new TesseractOCRService();
            var tessDataPath = ocrService.GetTessDataPath();

            // Ouvrir le dossier dans l'explorateur Windows
            if (Directory.Exists(tessDataPath))
            {
                System.Diagnostics.Process.Start("explorer.exe", tessDataPath);
            }
            else
            {
                MessageBox.Show(
                    $"Le dossier Tesseract sera créé automatiquement ici :\n\n{tessDataPath}\n\n" +
                    $"Accès rapide : Windows+R puis tapez :\n%APPDATA%\\MedCompanion\\tessdata\n\n" +
                    $"Placez le fichier 'fra.traineddata' dans ce dossier pour activer l'import OCR.",
                    "Dossier Tesseract",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Créer le dossier après avoir affiché le message
                Directory.CreateDirectory(tessDataPath);
                System.Diagnostics.Process.Start("explorer.exe", tessDataPath);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Erreur lors de l'ouverture du dossier :\n\n{ex.Message}",
                "Erreur",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ImportDoctolibButton_Click(object sender, RoutedEventArgs e)
    {
        // Dialogue de sélection de fichiers
        var openFileDialog = new OpenFileDialog
        {
            Title = "Sélectionner les captures d'écran Doctolib",
            Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
            Multiselect = true
        };

        if (openFileDialog.ShowDialog() != true)
            return;

        var selectedFiles = openFileDialog.FileNames;
        if (selectedFiles.Length == 0 || selectedFiles.Length > 2)
        {
            MessageBox.Show(
                "Veuillez sélectionner 1 ou 2 captures d'écran Doctolib.",
                "Sélection incorrecte",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Initialiser le service OCR
        var ocrService = new TesseractOCRService();

        try
        {
            // Extraire le texte de la première image
            var (success1, text1, confidence1, error1) = ocrService.ExtractTextFromImage(selectedFiles[0]);
            if (!success1)
            {
                MessageBox.Show(error1, "Erreur OCR", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Extraire le texte de la deuxième image (si présente)
            string text2 = "";
            float confidence2 = 0f;
            if (selectedFiles.Length == 2)
            {
                var (success2, txt2, conf2, error2) = ocrService.ExtractTextFromImage(selectedFiles[1]);
                if (!success2)
                {
                    MessageBox.Show(error2, "Erreur OCR", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                text2 = txt2;
                confidence2 = conf2;
            }

            // Parser les données Doctolib
            var parsedData = ocrService.ParseDoctolibData(text1, text2);

            // Afficher résumé de la confiance
            float avgConfidence = selectedFiles.Length == 2
                ? (confidence1 + confidence2) / 2
                : confidence1;

            var confirmMessage = $"📸 Import Doctolib - Confiance OCR : {avgConfidence:P0}\n\n" +
                                 $"✅ {parsedData.Count} champ(s) détecté(s)\n\n" +
                                 $"Les champs seront pré-remplis avec code couleur :\n" +
                                 $"🟩 Vert = Confiance élevée (>80%)\n" +
                                 $"🟧 Orange = À vérifier (50-80%)\n" +
                                 $"🟥 Rouge = Incertain (<50%)\n\n" +
                                 $"⚠️ Vérifiez toutes les données avant de sauvegarder.\n\n" +
                                 $"Continuer ?";

            var result = MessageBox.Show(confirmMessage, "Import Doctolib",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            // Pré-remplir les champs avec code couleur
            ApplyDoctolibData(parsedData);

            MessageBox.Show(
                $"✅ Import terminé !\n\n" +
                $"{parsedData.Count} champ(s) pré-rempli(s).\n" +
                $"Vérifiez les données (code couleur) avant de sauvegarder.",
                "Import réussi",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Erreur lors de l'import Doctolib :\n\n{ex.Message}",
                "Erreur",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Applique les données parsées aux champs du formulaire avec code couleur
    /// </summary>
    private void ApplyDoctolibData(Dictionary<string, DoctolibField> data)
    {
        // === NIR ===
        if (data.ContainsKey("NIR"))
        {
            NumeroSecuriteSocialeTextBox.Text = data["NIR"].Value;
            SetFieldBackgroundColor(NumeroSecuriteSocialeTextBox, data["NIR"].Confidence);
        }

        // === Adresse ===
        if (data.ContainsKey("AdresseRue"))
        {
            AdresseRueTextBox.Text = data["AdresseRue"].Value;
            SetFieldBackgroundColor(AdresseRueTextBox, data["AdresseRue"].Confidence);
        }

        if (data.ContainsKey("CodePostal"))
        {
            AdresseCodePostalTextBox.Text = data["CodePostal"].Value;
            SetFieldBackgroundColor(AdresseCodePostalTextBox, data["CodePostal"].Confidence);
        }

        if (data.ContainsKey("Ville"))
        {
            AdresseVilleTextBox.Text = data["Ville"].Value;
            SetFieldBackgroundColor(AdresseVilleTextBox, data["Ville"].Confidence);
        }

        // === Lieu de naissance ===
        if (data.ContainsKey("LieuNaissance"))
        {
            LieuNaissanceTextBox.Text = data["LieuNaissance"].Value;
            SetFieldBackgroundColor(LieuNaissanceTextBox, data["LieuNaissance"].Confidence);
        }

        // === Téléphone accompagnant (si détecté) ===
        if (data.ContainsKey("Telephone"))
        {
            AccompagnantTelephoneTextBox.Text = data["Telephone"].Value;
            SetFieldBackgroundColor(AccompagnantTelephoneTextBox, data["Telephone"].Confidence);
        }

        // === Email accompagnant (si détecté) ===
        if (data.ContainsKey("Email"))
        {
            AccompagnantEmailTextBox.Text = data["Email"].Value;
            SetFieldBackgroundColor(AccompagnantEmailTextBox, data["Email"].Confidence);
        }
    }

    /// <summary>
    /// Change la couleur de fond du champ selon le niveau de confiance
    /// </summary>
    private void SetFieldBackgroundColor(TextBox textBox, ConfidenceLevel confidence)
    {
        switch (confidence)
        {
            case ConfidenceLevel.High:
                // 🟩 Vert - Confiance élevée
                textBox.Background = new SolidColorBrush(Color.FromRgb(0xD4, 0xED, 0xDA)); // Vert clair
                break;

            case ConfidenceLevel.Medium:
                // 🟧 Orange - À vérifier
                textBox.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xE0, 0xB2)); // Orange clair
                break;

            case ConfidenceLevel.Low:
                // 🟥 Rouge - Incertain
                textBox.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xCC, 0xBC)); // Rouge clair
                break;
        }
    }
}
