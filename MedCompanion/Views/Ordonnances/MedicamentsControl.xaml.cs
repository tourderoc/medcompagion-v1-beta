using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MedCompanion.Models;
using MedCompanion.Services;

namespace MedCompanion.Views.Ordonnances
{
    public partial class MedicamentsControl : UserControl
    {
        private BDPMService? _bdpmService;
        private Medicament? _selectedMedicament;
        private readonly ObservableCollection<MedicamentPrescrit> _medicamentsPrescrits = new();

        // Services et patient
        private OrdonnanceService? _ordonnanceService;
        private PatientMetadata? _selectedPatient;

        // Tracker pour renouvellement
        private bool _isRenewal = false;

        // Événements
        public event EventHandler<string>? StatusChanged;
        public event EventHandler? OrdonnanceGenerated; // Notifie le parent qu'une ordonnance a été générée
        public event EventHandler? CancelRequested; // Notifie le parent que l'utilisateur veut annuler

        public MedicamentsControl()
        {
            InitializeComponent();

            MedicamentsPrescritsList.ItemsSource = _medicamentsPrescrits;

            // Vérifier si la base BDPM est initialisée
            CheckBDPMInitialization();
        }

        /// <summary>
        /// Initialise le contrôle avec les services nécessaires
        /// </summary>
        public void Initialize(OrdonnanceService ordonnanceService)
        {
            _ordonnanceService = ordonnanceService;
        }

        /// <summary>
        /// Définit le patient sélectionné et met à jour l'état du bouton Renouveler
        /// </summary>
        public void SetCurrentPatient(PatientMetadata? patient)
        {
            _selectedPatient = patient;
            UpdateRenewButtonState();
        }

        /// <summary>
        /// Met à jour l'état du bouton Renouveler selon la disponibilité d'ordonnances précédentes
        /// </summary>
        private void UpdateRenewButtonState()
        {
            if (_selectedPatient == null || _ordonnanceService == null)
            {
                RenewOrdonnanceButton.IsEnabled = false;
                return;
            }

            try
            {
                // Vérifier s'il existe une ordonnance médicaments précédente
                var (found, _, _) = _ordonnanceService.GetLastOrdonnanceMedicaments(_selectedPatient.NomComplet);
                RenewOrdonnanceButton.IsEnabled = found;
            }
            catch
            {
                // En cas d'erreur, désactiver le bouton
                RenewOrdonnanceButton.IsEnabled = false;
            }
        }

        /// <summary>
        /// Vérifie si la base BDPM est initialisée et affiche le bon panneau
        /// </summary>
        private async void CheckBDPMInitialization()
        {
            _bdpmService = new BDPMService();

            if (_bdpmService.IsDatabaseInitialized())
            {
                // Base OK, afficher l'interface principale
                WarningBDPMBorder.Visibility = Visibility.Collapsed;
                MainInterfaceGrid.Visibility = Visibility.Visible;

                // Afficher les stats
                var count = await _bdpmService.GetMedicamentsCountAsync();
                var lastUpdate = _bdpmService.GetLastUpdateDate();

                if (lastUpdate.HasValue)
                {
                    StatusChanged?.Invoke(this,
                        $"✅ Base BDPM chargée : {count:N0} médicaments (màj {lastUpdate.Value:dd/MM/yyyy})");
                }
            }
            else
            {
                // Base non initialisée, afficher le warning
                WarningBDPMBorder.Visibility = Visibility.Visible;
                MainInterfaceGrid.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Bouton pour vérifier/importer la base BDPM
        /// </summary>
        private async void InitBDPMButton_Click(object sender, RoutedEventArgs e)
        {
            InitBDPMButton.IsEnabled = false;
            InitBDPMButton.Content = "⏳ Vérification...";

            try
            {
                if (_bdpmService == null)
                    _bdpmService = new BDPMService();

                StatusChanged?.Invoke(this, "⏳ Vérification des fichiers BDPM...");

                // Vérifier la présence des fichiers
                var (filesPresent, checkMessage) = await _bdpmService.DownloadBDPMAsync();

                if (!filesPresent)
                {
                    // Afficher les instructions de téléchargement manuel
                    MessageBox.Show(checkMessage, "Fichiers BDPM requis",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    StatusChanged?.Invoke(this, "❌ Fichiers BDPM manquants - Téléchargement manuel requis");
                    InitBDPMButton.Content = "📥 Vérifier / Importer la base BDPM";
                    InitBDPMButton.IsEnabled = true;

                    // Ouvrir le dossier dans l'explorateur pour faciliter le dépôt des fichiers
                    try
                    {
                        var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                        var bdpmDirectory = Path.Combine(documentsPath, "MedCompanion", "bdpm");

                        if (!Directory.Exists(bdpmDirectory))
                            Directory.CreateDirectory(bdpmDirectory);

                        System.Diagnostics.Process.Start("explorer.exe", bdpmDirectory);
                    }
                    catch { }

                    return;
                }

                // Fichiers présents, afficher confirmation et passer à l'import
                StatusChanged?.Invoke(this, "✅ Fichiers BDPM trouvés");

                InitBDPMButton.Content = "⏳ Import des données en cours...";
                StatusChanged?.Invoke(this, "⏳ Import des données BDPM...");

                // Parser et importer
                var (importSuccess, importMessage, count) = await _bdpmService.ParseAndImportAsync();

                if (!importSuccess)
                {
                    MessageBox.Show(importMessage, "Erreur d'import",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusChanged?.Invoke(this, $"❌ {importMessage}");
                    InitBDPMButton.Content = "📥 Réessayer l'import";
                    InitBDPMButton.IsEnabled = true;
                    return;
                }

                // Succès !
                MessageBox.Show(
                    $"Base BDPM initialisée avec succès !\n\n{count:N0} médicaments importés.",
                    "Succès",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                StatusChanged?.Invoke(this, $"✅ {count:N0} médicaments importés");

                // Recharger l'interface
                CheckBDPMInitialization();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur inattendue :\n\n{ex.Message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusChanged?.Invoke(this, $"❌ Erreur: {ex.Message}");
                InitBDPMButton.Content = "📥 Réessayer";
                InitBDPMButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Recherche de médicaments (autocomplétion)
        /// </summary>
        private async void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = SearchTextBox.Text.Trim();

            if (query.Length < 3)
            {
                SearchResultsBorder.Visibility = Visibility.Collapsed;
                SearchResultsList.ItemsSource = null;
                return;
            }

            if (_bdpmService == null)
                return;

            try
            {
                StatusChanged?.Invoke(this, $"🔍 Recherche de '{query}'...");

                var results = await _bdpmService.SearchMedicamentsAsync(query, 10);

                if (results.Count > 0)
                {
                    SearchResultsList.ItemsSource = results;
                    SearchResultsBorder.Visibility = Visibility.Visible;
                    StatusChanged?.Invoke(this, $"✅ {results.Count} résultat(s) trouvé(s)");
                }
                else
                {
                    SearchResultsBorder.Visibility = Visibility.Collapsed;
                    SearchResultsList.ItemsSource = null;
                    StatusChanged?.Invoke(this, "❌ Aucun médicament trouvé");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MedicamentsControl] Erreur recherche: {ex.Message}");
                StatusChanged?.Invoke(this, $"❌ Erreur de recherche: {ex.Message}");
            }
        }

        /// <summary>
        /// Sélection d'un médicament dans les résultats
        /// </summary>
        private void SearchResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SearchResultsList.SelectedItem is not Medicament medicament)
                return;

            _selectedMedicament = medicament;

            // Afficher le formulaire d'ajout
            AddMedicamentBorder.Visibility = Visibility.Visible;

            // Afficher le nom complet
            var displayText = medicament.Denomination;
            if (medicament.Presentations.Count > 0)
            {
                displayText += $" - {medicament.Presentations[0].Libelle}";
            }

            SelectedMedicamentLabel.Text = $"💊 {displayText}";

            // Masquer les résultats
            SearchResultsBorder.Visibility = Visibility.Collapsed;

            // Focus sur la posologie
            PosologieTextBox.Focus();

            StatusChanged?.Invoke(this, $"✅ Médicament sélectionné : {medicament.Denomination}");
        }

        /// <summary>
        /// Efface la recherche
        /// </summary>
        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            SearchTextBox.Clear();
            SearchResultsBorder.Visibility = Visibility.Collapsed;
            AddMedicamentBorder.Visibility = Visibility.Collapsed;
            _selectedMedicament = null;
        }

        /// <summary>
        /// Annule l'ajout en cours
        /// </summary>
        private void CancelAddButton_Click(object sender, RoutedEventArgs e)
        {
            AddMedicamentBorder.Visibility = Visibility.Collapsed;
            _selectedMedicament = null;
            PosologieTextBox.Clear();
            DureeTextBox.Clear();
            QuantiteTextBox.Text = "1";
            RenouvelableCheckBox.IsChecked = false;
            NombreRenouvellementTextBox.Text = "0";
        }

        /// <summary>
        /// Gère l'affichage du panneau de renouvellement
        /// </summary>
        private void RenouvelableCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            RenouvellementPanel.Visibility = Visibility.Visible;
        }

        private void RenouvelableCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            RenouvellementPanel.Visibility = Visibility.Collapsed;
            NombreRenouvellementTextBox.Text = "0";
        }

        /// <summary>
        /// Ajoute le médicament à l'ordonnance
        /// </summary>
        private void AddToOrdonnanceButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedMedicament == null)
            {
                MessageBox.Show("Erreur : aucun médicament sélectionné.", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Validation
            if (string.IsNullOrWhiteSpace(PosologieTextBox.Text))
            {
                MessageBox.Show("Veuillez saisir la posologie.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                PosologieTextBox.Focus();
                return;
            }

            if (string.IsNullOrWhiteSpace(DureeTextBox.Text))
            {
                MessageBox.Show("Veuillez saisir la durée du traitement.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                DureeTextBox.Focus();
                return;
            }

            if (!int.TryParse(QuantiteTextBox.Text, out int quantite) || quantite < 1)
            {
                MessageBox.Show("La quantité doit être un nombre supérieur ou égal à 1.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                QuantiteTextBox.Focus();
                return;
            }

            int nombreRenouvellements = 0;
            if (RenouvelableCheckBox.IsChecked == true)
            {
                if (!int.TryParse(NombreRenouvellementTextBox.Text, out nombreRenouvellements) || nombreRenouvellements < 0)
                {
                    MessageBox.Show("Le nombre de renouvellements doit être un nombre positif.", "Information",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    NombreRenouvellementTextBox.Focus();
                    return;
                }
            }

            // Créer le médicament prescrit
            var medicamentPrescrit = new MedicamentPrescrit
            {
                Medicament = _selectedMedicament,
                Presentation = _selectedMedicament.Presentations.FirstOrDefault(),
                Posologie = PosologieTextBox.Text.Trim(),
                Duree = DureeTextBox.Text.Trim(),
                Quantite = quantite,
                Renouvelable = RenouvelableCheckBox.IsChecked == true,
                NombreRenouvellements = nombreRenouvellements
            };

            // Ajouter à la liste
            _medicamentsPrescrits.Add(medicamentPrescrit);

            // Activer le bouton de génération
            GenererOrdonnanceButton.IsEnabled = _medicamentsPrescrits.Count > 0;

            // Réinitialiser le formulaire
            CancelAddButton_Click(sender, e);
            SearchTextBox.Clear();

            StatusChanged?.Invoke(this, $"✅ Médicament ajouté ({_medicamentsPrescrits.Count} au total)");
        }

        /// <summary>
        /// Supprime un médicament de l'ordonnance
        /// </summary>
        private void RemoveMedicamentButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not MedicamentPrescrit medicament)
                return;

            var result = MessageBox.Show(
                $"Supprimer ce médicament de l'ordonnance ?\n\n{medicament.Medicament.Denomination}",
                "Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _medicamentsPrescrits.Remove(medicament);

                // Désactiver le bouton si plus de médicaments
                GenererOrdonnanceButton.IsEnabled = _medicamentsPrescrits.Count > 0;

                StatusChanged?.Invoke(this, $"✅ Médicament supprimé ({_medicamentsPrescrits.Count} restant(s))");
            }
        }

        /// <summary>
        /// Génère l'ordonnance
        /// </summary>
        private void GenererOrdonnanceButton_Click(object sender, RoutedEventArgs e)
        {
            if (_medicamentsPrescrits.Count == 0)
            {
                MessageBox.Show("Aucun médicament à prescrire.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_ordonnanceService == null)
            {
                MessageBox.Show("Erreur : service d'ordonnance non initialisé.", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (_selectedPatient == null)
            {
                MessageBox.Show("Veuillez sélectionner un patient avant de générer l'ordonnance.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                StatusChanged?.Invoke(this, "⏳ Génération de l'ordonnance...");

                // Créer l'objet OrdonnanceMedicaments
                var ordonnance = new OrdonnanceMedicaments
                {
                    DateCreation = DateTime.Now,
                    PatientNom = _selectedPatient.NomComplet,
                    Medicaments = _medicamentsPrescrits.ToList(),
                    Notes = "" // Optionnel : ajouter un champ pour les notes dans l'UI
                };

                // Préparer les métadonnées pour le système de poids
                Dictionary<string, object>? metadata = null;
                if (_isRenewal)
                {
                    metadata = new Dictionary<string, object>
                    {
                        { "is_renewal", true }
                    };
                }

                // Sauvegarder l'ordonnance (Markdown + DOCX + PDF)
                // ✅ FIX: Passer le PatientMetadata pour inclure la date de naissance et l'âge
                var (success, message, mdPath, docxPath, pdfPath) = _ordonnanceService.SaveOrdonnanceMedicaments(
                    _selectedPatient.NomComplet,
                    ordonnance,
                    _selectedPatient,  // PatientMetadata avec date de naissance
                    metadata
                );

                if (success)
                {
                    StatusChanged?.Invoke(this, message);

                    MessageBox.Show(
                        $"Ordonnance de médicaments générée avec succès !\n\n{_medicamentsPrescrits.Count} médicament(s) prescrit(s).",
                        "Succès",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );

                    // Notifier le parent qu'une ordonnance a été générée
                    OrdonnanceGenerated?.Invoke(this, EventArgs.Empty);

                    // Réinitialiser la liste et le flag de renouvellement
                    _medicamentsPrescrits.Clear();
                    _isRenewal = false;
                    GenererOrdonnanceButton.IsEnabled = false;

                    // Ouvrir le PDF si disponible, sinon le DOCX
                    var fileToOpen = !string.IsNullOrEmpty(pdfPath) && System.IO.File.Exists(pdfPath) ? pdfPath : docxPath;
                    if (!string.IsNullOrEmpty(fileToOpen) && System.IO.File.Exists(fileToOpen))
                    {
                        var result = MessageBox.Show(
                            "Voulez-vous ouvrir l'ordonnance maintenant ?",
                            "Ouvrir l'ordonnance",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question
                        );

                        if (result == MessageBoxResult.Yes)
                        {
                            try
                            {
                                var psi = new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = fileToOpen,
                                    UseShellExecute = true
                                };
                                System.Diagnostics.Process.Start(psi);
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show($"Erreur lors de l'ouverture : {ex.Message}", "Erreur",
                                    MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                    }
                }
                else
                {
                    MessageBox.Show(message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    StatusChanged?.Invoke(this, $"❌ {message}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la génération :\n\n{ex.Message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusChanged?.Invoke(this, $"❌ Erreur: {ex.Message}");
            }
        }

        /// <summary>
        /// Charge les médicaments d'une ordonnance précédente dans la liste actuelle
        /// </summary>
        public void LoadMedicamentsFromPreviousOrdonnance(List<MedicamentPrescrit> medicaments)
        {
            try
            {
                // Vider la liste actuelle
                _medicamentsPrescrits.Clear();

                // Marquer comme renouvellement
                _isRenewal = true;

                // Ajouter chaque médicament de la liste fournie
                foreach (var medicament in medicaments)
                {
                    _medicamentsPrescrits.Add(medicament);
                }

                // Activer le bouton "Générer l'ordonnance" si au moins un médicament
                GenererOrdonnanceButton.IsEnabled = _medicamentsPrescrits.Count > 0;

                // Afficher message de succès
                StatusChanged?.Invoke(this, $"✅ {medicaments.Count} médicament(s) chargé(s) depuis la dernière ordonnance");

                System.Diagnostics.Debug.WriteLine(
                    $"[MedicamentsControl] {medicaments.Count} médicaments chargés pour renouvellement");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du chargement :\n\n{ex.Message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusChanged?.Invoke(this, $"❌ Erreur chargement: {ex.Message}");
            }
        }

        /// <summary>
        /// Handler du bouton "Renouveler la dernière ordonnance"
        /// </summary>
        private void RenewOrdonnanceButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_selectedPatient == null)
                {
                    MessageBox.Show("Aucun patient sélectionné.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (_ordonnanceService == null)
                {
                    MessageBox.Show("Service d'ordonnance non initialisé.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                StatusChanged?.Invoke(this, "🔄 Récupération de la dernière ordonnance...");

                // Récupérer la dernière ordonnance médicaments
                var (found, medicaments, error) = _ordonnanceService.GetLastOrdonnanceMedicaments(_selectedPatient.NomComplet);

                if (!found)
                {
                    MessageBox.Show(
                        error ?? "Aucune ordonnance de médicaments trouvée pour ce patient.",
                        "Aucune ordonnance",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );
                    StatusChanged?.Invoke(this, "ℹ️ Aucune ordonnance précédente");
                    return;
                }

                // Ouvrir le dialog de sélection
                var dialog = new Dialogs.RenewOrdonnanceDialog(medicaments)
                {
                    Owner = Window.GetWindow(this)
                };

                if (dialog.ShowDialog() == true)
                {
                    // L'utilisateur a validé - charger les médicaments sélectionnés
                    var selectedMedicaments = dialog.SelectedMedicaments;

                    if (selectedMedicaments.Count > 0)
                    {
                        LoadMedicamentsFromPreviousOrdonnance(selectedMedicaments);
                        StatusChanged?.Invoke(this, $"✅ {selectedMedicaments.Count} médicament(s) chargé(s) - vous pouvez les modifier avant de générer l'ordonnance");
                    }
                }
                else
                {
                    // L'utilisateur a annulé
                    StatusChanged?.Invoke(this, "❌ Renouvellement annulé");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du renouvellement :\n\n{ex.Message}", "Erreur",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusChanged?.Invoke(this, $"❌ Erreur: {ex.Message}");
            }
        }

        /// <summary>
        /// Annule la création d'ordonnance et retourne au menu ordonnances
        /// </summary>
        private void AnnulerOrdonnanceButton_Click(object sender, RoutedEventArgs e)
        {
            // Demander confirmation si des médicaments sont dans la liste
            if (_medicamentsPrescrits.Count > 0)
            {
                var result = MessageBox.Show(
                    "Voulez-vous vraiment annuler ? Les médicaments ajoutés seront perdus.",
                    "Confirmer l'annulation",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            // Vider la liste des médicaments prescrits
            _medicamentsPrescrits.Clear();
            GenererOrdonnanceButton.IsEnabled = false;
            _isRenewal = false;

            // Réinitialiser les champs
            SearchTextBox.Text = "";
            AddMedicamentBorder.Visibility = Visibility.Collapsed;
            SearchResultsBorder.Visibility = Visibility.Collapsed;

            StatusChanged?.Invoke(this, "↩️ Création d'ordonnance annulée");

            // Notifier le parent pour revenir au menu ordonnances
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
