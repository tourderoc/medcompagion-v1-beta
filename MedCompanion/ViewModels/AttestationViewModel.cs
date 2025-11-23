using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Dialogs;
using MedCompanion.Models;
using MedCompanion.Services;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// ViewModel pour la gestion des attestations médicales
    /// Approche simplifiée similaire à NoteViewModel (communication par événements)
    /// </summary>
    public class AttestationViewModel : ViewModelBase
    {
        #region Services

        private readonly AttestationService _attestationService;
        private readonly PathService _pathService;
        private SynthesisWeightTracker? _synthesisWeightTracker;

        #endregion

        #region Événements

        // Événements pour communiquer avec MainWindow (comme NoteViewModel)
        public event EventHandler<string>? StatusMessageChanged;
        public event EventHandler<string>? AttestationContentLoaded;
        public event EventHandler? AttestationListRefreshRequested;
        public event EventHandler<(string title, string message)>? ErrorOccurred;
        public event EventHandler<(string title, string message)>? InfoMessageRequested;
        public event EventHandler<(string title, string message, Action onConfirm)>? ConfirmationRequested;
        public event EventHandler<string>? FileOpenRequested;
        public event EventHandler<string>? FilePrintRequested;
        public event EventHandler<string>? ShowInExplorerRequested;
        public event EventHandler<AttestationInfoDialog>? AttestationInfoDialogRequested;
        public event EventHandler<CustomAttestationDialog>? CustomAttestationDialogRequested;

        #endregion

        #region Propriétés

        private PatientMetadata? _currentPatient;
        public PatientMetadata? CurrentPatient
        {
            get => _currentPatient;
            set
            {
                if (SetProperty(ref _currentPatient, value))
                {
                    OnPatientChanged();
                }
            }
        }

        private ObservableCollection<AttestationTemplate> _availableTemplates;
        public ObservableCollection<AttestationTemplate> AvailableTemplates
        {
            get => _availableTemplates;
            set => SetProperty(ref _availableTemplates, value);
        }

        private AttestationTemplate? _selectedTemplate;
        public AttestationTemplate? SelectedTemplate
        {
            get => _selectedTemplate;
            set
            {
                if (SetProperty(ref _selectedTemplate, value))
                {
                    OnTemplateSelected();
                    UpdateCommandStates();
                }
            }
        }

        private ObservableCollection<AttestationListItem> _attestations;
        public ObservableCollection<AttestationListItem> Attestations
        {
            get => _attestations;
            set => SetProperty(ref _attestations, value);
        }

        private AttestationListItem? _selectedAttestation;
        public AttestationListItem? SelectedAttestation
        {
            get => _selectedAttestation;
            set
            {
                if (SetProperty(ref _selectedAttestation, value))
                {
                    OnAttestationSelected();
                }
            }
        }

        private string _attestationMarkdown;
        public string AttestationMarkdown
        {
            get => _attestationMarkdown;
            set => SetProperty(ref _attestationMarkdown, value);
        }

        private bool _isGenerating;
        public bool IsGenerating
        {
            get => _isGenerating;
            set
            {
                if (SetProperty(ref _isGenerating, value))
                {
                    UpdateCommandStates();
                }
            }
        }

        private bool _isModifying;
        public bool IsModifying
        {
            get => _isModifying;
            set
            {
                if (SetProperty(ref _isModifying, value))
                {
                    UpdateCommandStates();
                }
            }
        }

        private string _statusMessage;
        public new string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        #endregion

        #region Commandes

        public ICommand GenerateCommand { get; }
        public ICommand GenerateCustomCommand { get; }
        public ICommand ModifyCommand { get; }
        public ICommand SaveModifiedCommand { get; }
        public ICommand CancelModifyCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand PrintCommand { get; }
        public ICommand OpenFileCommand { get; }
        public ICommand ShowInExplorerCommand { get; }
        public ICommand RefreshListCommand { get; }

        #endregion

        #region Constructeur

        public AttestationViewModel(
            AttestationService attestationService,
            PathService pathService)
        {
            _attestationService = attestationService ?? throw new ArgumentNullException(nameof(attestationService));
            _pathService = pathService ?? throw new ArgumentNullException(nameof(pathService));

            // Initialiser les collections
            _availableTemplates = new ObservableCollection<AttestationTemplate>();
            _attestations = new ObservableCollection<AttestationListItem>();
            _attestationMarkdown = string.Empty;
            _statusMessage = string.Empty;

            // Initialiser les commandes
            GenerateCommand = new RelayCommand(async () => await GenerateAttestationAsync(), CanGenerate);
            GenerateCustomCommand = new RelayCommand(async () => await GenerateCustomAttestationAsync(), CanGenerateCustom);
            ModifyCommand = new RelayCommand(StartModify, CanModify);
            SaveModifiedCommand = new RelayCommand(async () => await SaveModifiedAttestationAsync(), CanSaveModified);
            CancelModifyCommand = new RelayCommand(CancelModify, () => IsModifying);
            DeleteCommand = new RelayCommand(async () => await DeleteAttestationAsync(), CanDelete);
            PrintCommand = new RelayCommand(PrintAttestation, CanPrint);
            OpenFileCommand = new RelayCommand(OpenAttestationFile, CanOpenFile);
            ShowInExplorerCommand = new RelayCommand(ShowAttestationInExplorer, CanShowInExplorer);
            RefreshListCommand = new RelayCommand(RefreshAttestationsList, () => CurrentPatient != null);

            // Charger les templates
            LoadTemplates();
        }

        #endregion

        #region Méthodes Privées

        private void LoadTemplates()
        {
            var templates = _attestationService.GetAvailableTemplates();
            AvailableTemplates.Clear();
            
            // Ajouter un placeholder en première position
            AvailableTemplates.Add(new AttestationTemplate
            {
                Type = "",
                DisplayName = "-- Choisir un modèle --",
                Description = "",
                Markdown = "",
                RequiredFields = new System.Collections.Generic.List<string>(),
                OptionalFields = new System.Collections.Generic.List<string>()
            });
            
            foreach (var template in templates)
            {
                AvailableTemplates.Add(template);
            }

            // Sélectionner le placeholder par défaut
            if (AvailableTemplates.Any())
            {
                SelectedTemplate = AvailableTemplates[0];
            }
        }

        private void OnTemplateSelected()
        {
            // Charger l'aperçu du template sélectionné
            if (SelectedTemplate != null && !string.IsNullOrEmpty(SelectedTemplate.Markdown))
            {
                AttestationMarkdown = SelectedTemplate.Markdown;
                RaiseStatusMessage($"Template '{SelectedTemplate.DisplayName}' sélectionné");
                // Déclencher événement pour afficher dans RichTextBox
                AttestationContentLoaded?.Invoke(this, SelectedTemplate.Markdown);
            }
            else
            {
                AttestationMarkdown = string.Empty;
                RaiseStatusMessage(string.Empty);
                AttestationContentLoaded?.Invoke(this, string.Empty);
            }
        }

        private void OnPatientChanged()
        {
            RefreshAttestationsList();
            AttestationMarkdown = string.Empty;
            IsModifying = false;
            UpdateCommandStates();
        }

        private void OnAttestationSelected()
        {
            if (SelectedAttestation != null)
            {
                // Charger le contenu de l'attestation sélectionnée
                try
                {
                    var content = System.IO.File.ReadAllText(SelectedAttestation.MdPath);
                    
                    // Retirer l'en-tête YAML si présent
                    var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                    if (lines.Length > 0 && lines[0].Trim() == "---")
                    {
                        int endIndex = Array.FindIndex(lines, 1, l => l.Trim() == "---");
                        if (endIndex > 0)
                        {
                            content = string.Join(Environment.NewLine, lines.Skip(endIndex + 1));
                        }
                    }

                    AttestationMarkdown = content.Trim();
                    RaiseStatusMessage($"Attestation chargée : {SelectedAttestation.Type}");
                    // Déclencher événement pour afficher dans RichTextBox
                    AttestationContentLoaded?.Invoke(this, content.Trim());
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke(this, ("Erreur", $"Impossible de charger l'attestation : {ex.Message}"));
                }
            }
            else
            {
                AttestationMarkdown = string.Empty;
                AttestationContentLoaded?.Invoke(this, string.Empty);
            }

            IsModifying = false;
            UpdateCommandStates();
        }

        private void RefreshAttestationsList()
        {
            if (CurrentPatient == null)
            {
                Attestations.Clear();
                return;
            }

            var nomComplet = $"{CurrentPatient.Nom}_{CurrentPatient.Prenom}";
            var attestationsList = _attestationService.GetAttestations(nomComplet);

            Attestations.Clear();
            foreach (var (date, type, preview, mdPath, docxPath) in attestationsList)
            {
                Attestations.Add(new AttestationListItem
                {
                    Date = date,
                    Type = type,
                    Preview = preview,
                    MdPath = mdPath,
                    DocxPath = docxPath
                });
            }

            RaiseStatusMessage($"{Attestations.Count} attestation(s) trouvée(s)");
            AttestationListRefreshRequested?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateCommandStates()
        {
            ((RelayCommand)GenerateCommand).RaiseCanExecuteChanged();
            ((RelayCommand)GenerateCustomCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ModifyCommand).RaiseCanExecuteChanged();
            ((RelayCommand)SaveModifiedCommand).RaiseCanExecuteChanged();
            ((RelayCommand)CancelModifyCommand).RaiseCanExecuteChanged();
            ((RelayCommand)DeleteCommand).RaiseCanExecuteChanged();
            ((RelayCommand)PrintCommand).RaiseCanExecuteChanged();
            ((RelayCommand)OpenFileCommand).RaiseCanExecuteChanged();
            ((RelayCommand)ShowInExplorerCommand).RaiseCanExecuteChanged();
            ((RelayCommand)RefreshListCommand).RaiseCanExecuteChanged();
        }

        #endregion

        #region Génération

        private async Task GenerateAttestationAsync()
        {
            if (CurrentPatient == null || SelectedTemplate == null) return;

            IsGenerating = true;
            StatusMessage = "Génération de l'attestation...";

            try
            {
                // Vérifier les champs manquants (y compris le sexe si non renseigné)
                var (missingRequired, missingOptional) = _attestationService.DetectMissingFields(
                    SelectedTemplate.Type,
                    CurrentPatient
                );

                // Collecter les champs de l'utilisateur si nécessaire
                var userFields = new System.Collections.Generic.Dictionary<string, string>();

                if (missingRequired.Any() || missingOptional.Any())
                {
                    var dialog = new AttestationInfoDialog(
                        missingRequired,
                        missingOptional
                    );

                    // Déclencher événement pour ouvrir le dialog
                    bool dialogAccepted = false;
                    AttestationInfoDialogRequested?.Invoke(this, dialog);
                    
                    // Le MainWindow devra gérer l'événement et montrer le dialog
                    // Pour l'instant, on suppose que le dialog a été accepté si CollectedInfo n'est pas null
                    if (dialog.CollectedInfo == null)
                    {
                        RaiseStatusMessage("Génération annulée");
                        return;
                    }

                    userFields = dialog.CollectedInfo;
                }

                // Générer l'attestation
                var (success, markdown, error) = _attestationService.GenerateAttestation(
                    SelectedTemplate.Type,
                    userFields,
                    CurrentPatient
                );

                if (!success)
                {
                    ErrorOccurred?.Invoke(this, ("Erreur de génération", error));
                    RaiseStatusMessage("Erreur lors de la génération");
                    return;
                }

                // Déclencher l'événement pour afficher le preview (pas de sauvegarde automatique)
                AttestationContentLoaded?.Invoke(this, markdown);
                RaiseStatusMessage("Attestation générée - cliquez sur Sauvegarder pour enregistrer");
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ("Erreur", $"Erreur inattendue : {ex.Message}"));
                RaiseStatusMessage("Erreur inattendue");
            }
            finally
            {
                IsGenerating = false;
            }
        }

        private bool CanGenerate()
        {
            return CurrentPatient != null 
                && SelectedTemplate != null 
                && !string.IsNullOrEmpty(SelectedTemplate.Type) // Vérifier que ce n'est pas le placeholder
                && !IsGenerating 
                && !IsModifying;
        }

        private async Task GenerateCustomAttestationAsync()
        {
            if (CurrentPatient == null) return;

            IsGenerating = true;
            StatusMessage = "Génération d'attestation personnalisée...";

            try
            {
                var dialog = new CustomAttestationDialog();
                
                // Déclencher événement pour ouvrir le dialog
                CustomAttestationDialogRequested?.Invoke(this, dialog);
                
                // Le MainWindow devra gérer l'événement et montrer le dialog
                var consigne = dialog.Consigne;
                
                if (string.IsNullOrWhiteSpace(consigne))
                {
                    ErrorOccurred?.Invoke(this, ("Attention", "Veuillez saisir une consigne"));
                    RaiseStatusMessage("Consigne manquante");
                    return;
                }

                RaiseStatusMessage("Génération avec IA en cours...");

                var (success, markdown, error) = await _attestationService.GenerateCustomAttestationAsync(
                    consigne,
                    CurrentPatient
                );

                if (!success)
                {
                    ErrorOccurred?.Invoke(this, ("Erreur IA", error));
                    RaiseStatusMessage("Erreur lors de la génération IA");
                    return;
                }

                // Déclencher l'événement pour afficher le preview (pas de sauvegarde automatique)
                AttestationContentLoaded?.Invoke(this, markdown);
                RaiseStatusMessage("Attestation personnalisée générée - cliquez sur Sauvegarder pour enregistrer");
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ("Erreur", $"Erreur inattendue : {ex.Message}"));
                RaiseStatusMessage("Erreur inattendue");
            }
            finally
            {
                IsGenerating = false;
            }
        }

        private bool CanGenerateCustom()
        {
            return CurrentPatient != null && !IsGenerating && !IsModifying;
        }

        #endregion

        #region Modification

        private void StartModify()
        {
            if (SelectedAttestation == null) return;

            IsModifying = true;
            RaiseStatusMessage("Mode édition activé");
            UpdateCommandStates();
        }

        private bool CanModify()
        {
            return SelectedAttestation != null && !IsModifying && !IsGenerating;
        }

        private async Task SaveModifiedAttestationAsync()
        {
            if (CurrentPatient == null || SelectedAttestation == null) return;

            try
            {
                // Sauvegarder les modifications
                var nomComplet = $"{CurrentPatient.Nom}_{CurrentPatient.Prenom}";
                var (success, message, mdPath, docxPath) = _attestationService.SaveAndExportAttestation(
                    nomComplet,
                    SelectedAttestation.Type,
                    AttestationMarkdown
                );

                if (success && mdPath != null)
                {
                    // NOUVEAU : Enregistrer le poids de pertinence selon le type d'attestation (modification)
                    if (_synthesisWeightTracker != null)
                    {
                        var weight = ContentWeightRules.GetDefaultWeight(SelectedAttestation.Type) ?? 0.2;
                        _synthesisWeightTracker.RecordContentWeight(
                            nomComplet,
                            SelectedAttestation.Type,
                            mdPath,
                            weight,
                            $"Attestation modifiée {SelectedAttestation.Type} (poids: {weight:F1})"
                        );

                        System.Diagnostics.Debug.WriteLine(
                            $"[AttestationViewModel] Poids enregistré (modification): {weight:F1} pour {SelectedAttestation.Type}");
                    }

                    InfoMessageRequested?.Invoke(this, ("Succès", "Attestation modifiée et sauvegardée"));
                    RefreshAttestationsList();
                    IsModifying = false;
                    RaiseStatusMessage("Attestation sauvegardée");
                }
                else
                {
                    ErrorOccurred?.Invoke(this, ("Erreur", message));
                    RaiseStatusMessage("Erreur lors de la sauvegarde");
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ("Erreur", $"Erreur inattendue : {ex.Message}"));
                RaiseStatusMessage("Erreur inattendue");
            }
        }

        private bool CanSaveModified()
        {
            return IsModifying && !string.IsNullOrWhiteSpace(AttestationMarkdown);
        }

        private void CancelModify()
        {
            IsModifying = false;
            OnAttestationSelected(); // Recharger le contenu original
            RaiseStatusMessage("Modification annulée");
        }

        #endregion

        #region Suppression

        private async Task DeleteAttestationAsync()
        {
            if (SelectedAttestation == null) return;

            // Demander confirmation via événement
            bool confirmed = false;
            ConfirmationRequested?.Invoke(this, (
                "Confirmation",
                $"Voulez-vous vraiment supprimer cette attestation ?\n\n{SelectedAttestation.Preview}",
                () => confirmed = true
            ));

            if (!confirmed) return;

            try
            {
                // Supprimer les fichiers directement
                if (System.IO.File.Exists(SelectedAttestation.MdPath))
                {
                    System.IO.File.Delete(SelectedAttestation.MdPath);
                }

                if (!string.IsNullOrEmpty(SelectedAttestation.DocxPath) && System.IO.File.Exists(SelectedAttestation.DocxPath))
                {
                    System.IO.File.Delete(SelectedAttestation.DocxPath);
                }

                // Réinitialiser la sélection
                SelectedAttestation = null;
                AttestationMarkdown = string.Empty;

                // Rafraîchir la liste
                RefreshAttestationsList();

                InfoMessageRequested?.Invoke(this, ("Succès", "Attestation supprimée"));
                RaiseStatusMessage("Attestation supprimée");
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ("Erreur", $"Impossible de supprimer l'attestation : {ex.Message}"));
                RaiseStatusMessage("Erreur lors de la suppression");
            }
        }

        private bool CanDelete()
        {
            return SelectedAttestation != null && !IsModifying && !IsGenerating;
        }

        #endregion

        #region Opérations Fichiers

        private void PrintAttestation()
        {
            if (SelectedAttestation == null) return;

            try
            {
                if (System.IO.File.Exists(SelectedAttestation.DocxPath))
                {
                    FilePrintRequested?.Invoke(this, SelectedAttestation.DocxPath);
                    RaiseStatusMessage("Impression lancée");
                }
                else
                {
                    ErrorOccurred?.Invoke(this, ("Attention", "Le fichier DOCX n'existe pas"));
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ("Erreur", $"Impossible d'imprimer : {ex.Message}"));
            }
        }

        private bool CanPrint()
        {
            return SelectedAttestation != null && !IsModifying;
        }

        private void OpenAttestationFile()
        {
            if (SelectedAttestation == null) return;

            try
            {
                if (System.IO.File.Exists(SelectedAttestation.DocxPath))
                {
                    FileOpenRequested?.Invoke(this, SelectedAttestation.DocxPath);
                    RaiseStatusMessage("Fichier ouvert");
                }
                else
                {
                    ErrorOccurred?.Invoke(this, ("Attention", "Le fichier DOCX n'existe pas"));
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ("Erreur", $"Impossible d'ouvrir le fichier : {ex.Message}"));
            }
        }

        private bool CanOpenFile()
        {
            return SelectedAttestation != null;
        }

        private void ShowAttestationInExplorer()
        {
            if (SelectedAttestation == null) return;

            try
            {
                if (System.IO.File.Exists(SelectedAttestation.DocxPath))
                {
                    ShowInExplorerRequested?.Invoke(this, SelectedAttestation.DocxPath);
                }
                else if (System.IO.File.Exists(SelectedAttestation.MdPath))
                {
                    ShowInExplorerRequested?.Invoke(this, SelectedAttestation.MdPath);
                }
                else
                {
                    ErrorOccurred?.Invoke(this, ("Attention", "Le fichier n'existe pas"));
                }
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(this, ("Erreur", $"Impossible d'afficher dans l'explorateur : {ex.Message}"));
            }
        }

        private bool CanShowInExplorer()
        {
            return SelectedAttestation != null;
        }

        #endregion

        #region Méthodes Helper

        /// <summary>
        /// Helper pour déclencher l'événement StatusMessageChanged
        /// </summary>
        private void RaiseStatusMessage(string message)
        {
            StatusMessage = message;
            StatusMessageChanged?.Invoke(this, message);
        }

        #endregion

        #region Méthodes Publiques

        /// <summary>
        /// Initialise le tracker de poids pour la synthèse
        /// </summary>
        public void InitializeSynthesisWeightTracker(SynthesisWeightTracker tracker)
        {
            _synthesisWeightTracker = tracker;
        }

        /// <summary>
        /// Réinitialise le ViewModel
        /// </summary>
        public void Reset()
        {
            CurrentPatient = null;
            SelectedTemplate = AvailableTemplates.FirstOrDefault();
            SelectedAttestation = null;
            AttestationMarkdown = string.Empty;
            IsModifying = false;
            IsGenerating = false;
            StatusMessage = string.Empty;
            Attestations.Clear();
        }

        /// <summary>
        /// Sauvegarde une attestation générée (appelé depuis le UserControl après preview)
        /// </summary>
        public (bool success, string message, string? mdPath, string? docxPath) SaveGeneratedAttestation(string attestationType, string markdown)
        {
            if (CurrentPatient == null)
            {
                return (false, "Aucun patient sélectionné", null, null);
            }

            if (string.IsNullOrEmpty(markdown))
            {
                return (false, "Aucun contenu à sauvegarder", null, null);
            }

            try
            {
                // Sauvegarder et exporter
                var nomComplet = $"{CurrentPatient.Nom}_{CurrentPatient.Prenom}";
                var result = _attestationService.SaveAndExportAttestation(
                    nomComplet,
                    attestationType,
                    markdown
                );

                if (result.success && result.mdPath != null)
                {
                    // NOUVEAU : Enregistrer le poids de pertinence selon le type d'attestation
                    if (_synthesisWeightTracker != null)
                    {
                        var weight = ContentWeightRules.GetDefaultWeight(attestationType) ?? 0.2; // Défaut 0.2 si type inconnu
                        _synthesisWeightTracker.RecordContentWeight(
                            nomComplet,
                            attestationType,
                            result.mdPath,
                            weight,
                            $"Attestation {attestationType} (poids: {weight:F1})"
                        );

                        System.Diagnostics.Debug.WriteLine(
                            $"[AttestationViewModel] Poids enregistré: {weight:F1} pour attestation {attestationType}");
                    }

                    // Rafraîchir la liste
                    RefreshAttestationsList();
                    RaiseStatusMessage("Attestation sauvegardée avec succès");
                }

                return result;
            }
            catch (Exception ex)
            {
                return (false, $"Erreur lors de la sauvegarde : {ex.Message}", null, null);
            }
        }

        #endregion
    }
}
