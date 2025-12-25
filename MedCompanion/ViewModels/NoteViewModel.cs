using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Documents;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Services;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// ViewModel pour la gestion des notes cliniques
    /// </summary>
    public class NoteViewModel : ViewModelBase
    {
        private readonly StorageService _storageService;
        private readonly OpenAIService _openAIService;
        private SynthesisWeightTracker? _synthesisWeightTracker;
        private PatientIndexService? _patientIndexService;
        private string? _currentPatientName;
        private string? _currentEditingFilePath;

        // Poids de pertinence de la dernière note structurée (pour mise à jour synthèse)
        private double _lastNoteRelevanceWeight = 0.0;

        // ===== PROPRIÉTÉS =====

        private string _rawNoteText = string.Empty;
        public string RawNoteText
        {
            get => _rawNoteText;
            set
            {
                if (SetProperty(ref _rawNoteText, value))
                {
                    // Activer le bouton Structurer si du texte est présent
                    ((RelayCommand)StructurerCommand).RaiseCanExecuteChanged();
                }
            }
        }

        private FlowDocument _structuredNoteDocument = new FlowDocument();
        public FlowDocument StructuredNoteDocument
        {
            get => _structuredNoteDocument;
            set => SetProperty(ref _structuredNoteDocument, value);
        }

        private bool _isStructuredNoteReadOnly = true;
        public bool IsStructuredNoteReadOnly
        {
            get => _isStructuredNoteReadOnly;
            set => SetProperty(ref _isStructuredNoteReadOnly, value);
        }

        private bool _isModifierButtonVisible = false;
        public bool IsModifierButtonVisible
        {
            get => _isModifierButtonVisible;
            set => SetProperty(ref _isModifierButtonVisible, value);
        }

        private bool _isSupprimerButtonVisible = false;
        public bool IsSupprimerButtonVisible
        {
            get => _isSupprimerButtonVisible;
            set => SetProperty(ref _isSupprimerButtonVisible, value);
        }

        private bool _isSauvegarderButtonEnabled = false;
        public bool IsSauvegarderButtonEnabled
        {
            get => _isSauvegarderButtonEnabled;
            set
            {
                if (SetProperty(ref _isSauvegarderButtonEnabled, value))
                {
                    // Notifier SaveCommand que son CanExecute a changé
                    ((RelayCommand)SaveCommand).RaiseCanExecuteChanged();
                }
            }
        }

        private bool _isStructurerButtonVisible = true;
        public bool IsStructurerButtonVisible
        {
            get => _isStructurerButtonVisible;
            set => SetProperty(ref _isStructurerButtonVisible, value);
        }

        private bool _isSauvegarderButtonVisible = false;
        public bool IsSauvegarderButtonVisible
        {
            get => _isSauvegarderButtonVisible;
            set => SetProperty(ref _isSauvegarderButtonVisible, value);
        }

        private bool _isAnnulerButtonVisible = false;
        public bool IsAnnulerButtonVisible
        {
            get => _isAnnulerButtonVisible;
            set => SetProperty(ref _isAnnulerButtonVisible, value);
        }

        private bool _isRawNoteVisible = true;
        public bool IsRawNoteVisible
        {
            get => _isRawNoteVisible;
            set => SetProperty(ref _isRawNoteVisible, value);
        }

        private ObservableCollection<NoteItem> _notes = new ObservableCollection<NoteItem>();
        public ObservableCollection<NoteItem> Notes
        {
            get => _notes;
            set => SetProperty(ref _notes, value);
        }

        private NoteItem? _selectedNote;
        public NoteItem? SelectedNote
        {
            get => _selectedNote;
            set
            {
                if (SetProperty(ref _selectedNote, value))
                {
                    if (value != null)
                    {
                        LoadNoteContent(value.FilePath);
                    }
                }
            }
        }

        // ===== COMMANDES =====

        public ICommand StructurerCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand EditCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand CancelCommand { get; }

        // ===== ÉVÉNEMENTS =====

        public event EventHandler<string>? StatusMessageChanged;
        public event EventHandler? NoteSaved;
        public event EventHandler? PatientListRefreshRequested; // Nouveau: pour rafraîchir liste patients

        // ===== CONSTRUCTEUR =====

        public NoteViewModel(
            StorageService storageService,
            OpenAIService openAIService)
        {
            _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
            _openAIService = openAIService ?? throw new ArgumentNullException(nameof(openAIService));

            // Initialiser les commandes
            StructurerCommand = new RelayCommand(
                execute: _ => StructurerNoteAsync(),
                canExecute: _ => !string.IsNullOrWhiteSpace(RawNoteText) && !string.IsNullOrEmpty(_currentPatientName)
            );

            SaveCommand = new RelayCommand(
                execute: _ => SaveNote(),
                canExecute: _ => IsSauvegarderButtonEnabled
            );

            EditCommand = new RelayCommand(
                execute: _ => EnterEditMode(),
                canExecute: _ => _currentEditingFilePath != null
            );

            DeleteCommand = new RelayCommand(
                execute: _ => DeleteNote(),
                canExecute: _ => _currentEditingFilePath != null
            );

            CancelCommand = new RelayCommand(
                execute: _ => CancelNote(),
                canExecute: _ => true
            );
        }

        // ===== MÉTHODES PUBLIQUES =====

        /// <summary>
        /// Initialise le tracker de poids pour la synthèse
        /// </summary>
        public void InitializeSynthesisWeightTracker(SynthesisWeightTracker tracker)
        {
            _synthesisWeightTracker = tracker;
        }

        /// <summary>
        /// Charge les notes d'un patient
        /// </summary>
        public void LoadNotes(string patientName, PatientIndexService patientIndex)
        {
            _currentPatientName = patientName;
            _patientIndexService = patientIndex; // STOCKER la référence pour usage futur

            try
            {
                // Récupérer l'ID patient depuis le nom complet
                var patient = patientIndex.GetAllPatients()
                    .FirstOrDefault(p => p.NomComplet == patientName);

                if (patient == null)
                {
                    Notes = new ObservableCollection<NoteItem>();
                    return;
                }

                // Récupérer les notes depuis le disque
                var notes = patientIndex.GetPatientNotes(patient.Id);

                // IMPORTANT: Créer une NOUVELLE ObservableCollection pour forcer la mise à jour de l'UI
                // Simple Clear()/Add() ne suffit pas toujours à rafraîchir l'affichage
                var newNotes = new ObservableCollection<NoteItem>();
                foreach (var note in notes)
                {
                    newNotes.Add(new NoteItem
                    {
                        FilePath = note.filePath,
                        Date = note.date,
                        DateLabel = note.date.ToString("dd/MM/yyyy HH:mm"),
                        Preview = note.preview
                    });
                }
                
                // Remplacer la collection entière (déclenche PropertyChanged)
                Notes = newNotes;
            }
            catch (Exception ex)
            {
                RaiseStatusMessage($"❌ Erreur chargement notes: {ex.Message}");
            }
        }

        /// <summary>
        /// Réinitialise l'interface (nouveau patient ou déselection)
        /// </summary>
        public void Reset()
        {
            RawNoteText = string.Empty;
            StructuredNoteDocument = new FlowDocument();
            IsStructuredNoteReadOnly = true;
            IsModifierButtonVisible = false;
            IsSupprimerButtonVisible = false;
            IsSauvegarderButtonEnabled = false;
            IsSauvegarderButtonVisible = false;
            IsStructurerButtonVisible = true;
            IsRawNoteVisible = true;        // Ré-afficher la zone Note brute
            IsAnnulerButtonVisible = false; // Masquer le bouton Annuler
            _currentEditingFilePath = null;
            _currentPatientName = null;
            Notes.Clear();
            SelectedNote = null;
        }

        // ===== MÉTHODES PRIVÉES =====

        /// <summary>
        /// Charge le contenu d'une note sélectionnée
        /// </summary>
        private void LoadNoteContent(string filePath)
        {
            try
            {
                var content = System.IO.File.ReadAllText(filePath);

                // Nettoyer le YAML
                var cleanedContent = CleanYamlFromMarkdown(content);

                // Stocker le fichier en cours d'édition
                _currentEditingFilePath = filePath;

                // Mode lecture seule
                IsStructuredNoteReadOnly = true;

                // MASQUER la zone Note brute en consultation
                IsRawNoteVisible = false;

                // Afficher Annuler, Modifier et Supprimer en mode consultation
                IsAnnulerButtonVisible = true;
                IsModifierButtonVisible = true;
                IsSupprimerButtonVisible = true;
                IsSauvegarderButtonEnabled = false;
                IsSauvegarderButtonVisible = false;
                IsStructurerButtonVisible = false;

                RaiseStatusMessage("📄 Note chargée");

                // Déclencher un événement pour que MainWindow charge le contenu
                NoteContentLoaded?.Invoke(this, (filePath, cleanedContent));
            }
            catch (Exception ex)
            {
                RaiseStatusMessage($"❌ Erreur lecture: {ex.Message}");
            }
        }

    /// <summary>
    /// Recharge le contenu de la note courante (utile après modification externe)
    /// </summary>
    public void ReloadCurrentNote()
    {
        if (SelectedNote != null && !string.IsNullOrEmpty(SelectedNote.FilePath))
        {
            LoadNoteContent(SelectedNote.FilePath);
        }
    }

        /// <summary>
        /// Événement déclenché quand une note est chargée
        /// </summary>
        public event EventHandler<(string filePath, string content)>? NoteContentLoaded;

        /// <summary>
        /// Structure une note avec l'IA (avec BusyService pour overlay modal)
        /// </summary>
        private async void StructurerNoteAsync()
        {
            if (string.IsNullOrEmpty(_currentPatientName))
                return;

            // Démarrer le BusyService avec étapes détaillées
            var busyService = BusyService.Instance;
            var cancellationToken = busyService.Start("Structuration de la note...", canCancel: true);

            try
            {
                StatusMessageChanged?.Invoke(this, "⏳ Structuration de la note...");
                // ✅ NOUVEAU : Récupérer le sexe du patient
                string sexe = "M";  // Défaut
                if (_patientIndexService != null)
                {
                    var patient = _patientIndexService.GetAllPatients()
                        .FirstOrDefault(p => p.NomComplet == _currentPatientName);
                    sexe = patient?.Sexe ?? "M";
                }

                // Vérifier annulation
                cancellationToken.ThrowIfCancellationRequested();

                busyService.UpdateStep("Anonymisation des données patient...");
                busyService.UpdateProgress(20);

                // Vérifier annulation
                cancellationToken.ThrowIfCancellationRequested();

                busyService.UpdateStep("Appel du modèle IA local...");
                busyService.UpdateProgress(40);

                // ✅ APPEL MODIFIÉ : Passer le CancellationToken
                var (success, result, relevanceWeight) = await _openAIService.StructurerNoteAsync(
                    _currentPatientName,
                    sexe,
                    RawNoteText.Trim(),
                    cancellationToken
                );

                // Vérifier annulation
                cancellationToken.ThrowIfCancellationRequested();

                busyService.UpdateStep("Finalisation...");
                busyService.UpdateProgress(90);

                if (success)
                {
                    // Réinitialiser le fichier (nouvelle note)
                    _currentEditingFilePath = null;

                    // NOUVEAU : Stocker le poids de pertinence pour l'utiliser lors de la sauvegarde
                    _lastNoteRelevanceWeight = relevanceWeight;

                    // Mode édition direct
                    IsStructuredNoteReadOnly = false;

                    // Afficher Annuler et Sauvegarder, masquer les autres
                    IsAnnulerButtonVisible = true;
                    IsModifierButtonVisible = false;
                    IsSupprimerButtonVisible = false;
                    IsSauvegarderButtonEnabled = true;
                    IsSauvegarderButtonVisible = true;
                    IsStructurerButtonVisible = false;

                    busyService.UpdateProgress(100);
                    StatusMessageChanged?.Invoke(this, "✅ Note structurée générée");

                    // Déclencher événement pour que MainWindow affiche le résultat
                    NoteStructured?.Invoke(this, result);
                }
                else
                {
                    RaiseStatusMessage($"❌ {result}");
                }
            }
            catch (OperationCanceledException)
            {
                StatusMessageChanged?.Invoke(this, "⚠️ Structuration annulée");
            }
            catch (Exception ex)
            {
                RaiseStatusMessage($"❌ {ex.Message}");
            }
            finally
            {
                // Toujours arrêter le BusyService
                busyService.Stop();
            }
        }

        /// <summary>
        /// Événement déclenché quand une note est structurée
        /// </summary>
        public event EventHandler<string>? NoteStructured;

        /// <summary>
        /// Sauvegarde la note
        /// </summary>
        private void SaveNote()
        {
            if (string.IsNullOrEmpty(_currentPatientName))
                return;

            try
            {
                // Déclencher événement pour que MainWindow convertisse le FlowDocument en Markdown
                NoteSaveRequested?.Invoke(this, _currentEditingFilePath);
            }
            catch (Exception ex)
            {
                RaiseStatusMessage($"❌ {ex.Message}");
            }
        }

        /// <summary>
        /// Événement déclenché pour demander la sauvegarde
        /// </summary>
        public event EventHandler<string?>? NoteSaveRequested;

        /// <summary>
        /// Événement déclenché pour vider l'aperçu après sauvegarde
        /// </summary>
        public event EventHandler? NoteClearedAfterSave;

        /// <summary>
        /// Complète la sauvegarde après conversion Markdown (appelé par MainWindow)
        /// </summary>
        public void CompleteSave(string markdown)
        {
            if (string.IsNullOrEmpty(_currentPatientName))
                return;

            try
            {
                bool success;
                string message;

                if (_currentEditingFilePath != null)
                {
                    // Mise à jour fichier existant
                    (success, message) = _storageService.UpdateStructuredNote(_currentEditingFilePath, markdown);
                }
                else
                {
                    // Nouveau fichier
                    string? filePath;
                    (success, message, filePath) = _storageService.SaveStructuredNote(_currentPatientName, markdown);

                    if (success && filePath != null)
                    {
                        _currentEditingFilePath = filePath;
                    }
                }

                if (success)
                {
                    // NOUVEAU : Enregistrer le poids de pertinence pour la synthèse
                    if (_synthesisWeightTracker != null && _currentPatientName != null && _currentEditingFilePath != null)
                    {
                        _synthesisWeightTracker.RecordContentWeight(
                            _currentPatientName,
                            "note_clinique",
                            _currentEditingFilePath,
                            _lastNoteRelevanceWeight,
                            $"Note clinique (poids IA: {_lastNoteRelevanceWeight:F1})"
                        );

                        System.Diagnostics.Debug.WriteLine(
                            $"[NoteViewModel] Poids enregistré: {_lastNoteRelevanceWeight:F1} pour note {System.IO.Path.GetFileName(_currentEditingFilePath)}");
                    }

                    // Reset automatique après sauvegarde
                    RawNoteText = string.Empty;
                    StructuredNoteDocument = new FlowDocument();
                    IsStructuredNoteReadOnly = true;
                    IsModifierButtonVisible = false;
                    IsSupprimerButtonVisible = false;
                    IsAnnulerButtonVisible = false;
                    IsSauvegarderButtonEnabled = false;
                    IsSauvegarderButtonVisible = false;
                    IsStructurerButtonVisible = true;
                    IsRawNoteVisible = true;  // Ré-afficher la zone Note brute
                    _currentEditingFilePath = null;
                    SelectedNote = null;

                    RaiseStatusMessage(message + " ✓ Prêt pour une nouvelle note");

                    // Déclencher événement pour forcer MainWindow à vider le RichTextBox
                    NoteClearedAfterSave?.Invoke(this, EventArgs.Empty);

                    // ✅ APPEL DIRECT au lieu d'événement : rafraîchir la liste IMMÉDIATEMENT
                    if (_patientIndexService != null && !string.IsNullOrEmpty(_currentPatientName))
                    {
                        LoadNotes(_currentPatientName, _patientIndexService);
                    }

                    // Notifier que la note a été sauvegardée (pour rafraîchir l'interface courriers)
                    NoteSaved?.Invoke(this, EventArgs.Empty);

                    // ✅ NOUVEAU : Rafraîchir la liste des PATIENTS pour mettre à jour la colonne "Dernière note"
                    PatientListRefreshRequested?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    RaiseStatusMessage($"❌ {message}");
                }
            }
            catch (Exception ex)
            {
                RaiseStatusMessage($"❌ {ex.Message}");
            }
        }

    /// <summary>
    /// Passe en mode édition
    /// </summary>
    private void EnterEditMode()
    {
        IsStructuredNoteReadOnly = false;
        IsAnnulerButtonVisible = true;
        IsModifierButtonVisible = false;
        IsSupprimerButtonVisible = false;
        IsSauvegarderButtonEnabled = true;
        IsSauvegarderButtonVisible = true;
        IsStructurerButtonVisible = false;
        
        // Garder la Note brute masquée en mode édition
        // IsRawNoteVisible reste à false
        
        // IMPORTANT: Forcer la réévaluation du Command même si la valeur était déjà true
        ((RelayCommand)SaveCommand).RaiseCanExecuteChanged();

        RaiseStatusMessage("✏️ Mode édition activé");
    }

        /// <summary>
        /// Supprime la note courante
        /// </summary>
        private void DeleteNote()
        {
            if (_currentEditingFilePath == null)
                return;

            // Déclencher événement pour que MainWindow affiche la confirmation
            NoteDeleteRequested?.Invoke(this, _currentEditingFilePath);
        }

        /// <summary>
        /// Événement déclenché pour demander la suppression
        /// </summary>
        public event EventHandler<string>? NoteDeleteRequested;

        /// <summary>
        /// Annule l'édition en cours et réinitialise l'interface
        /// </summary>
        private void CancelNote()
        {
            // Réinitialiser complètement l'interface
            RawNoteText = string.Empty;
            StructuredNoteDocument = new FlowDocument();
            IsStructuredNoteReadOnly = true;
            IsModifierButtonVisible = false;
            IsSupprimerButtonVisible = false;
            IsAnnulerButtonVisible = false;
            IsSauvegarderButtonEnabled = false;
            IsSauvegarderButtonVisible = false;
            IsStructurerButtonVisible = true;
            IsRawNoteVisible = true;
            _currentEditingFilePath = null;
            SelectedNote = null;

            RaiseStatusMessage("❌ Annulé - Prêt pour une nouvelle note");

            // Déclencher événement pour forcer MainWindow à vider le RichTextBox
            NoteClearedAfterSave?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Complète la suppression (appelé par MainWindow après confirmation)
        /// </summary>
        public void CompleteDelete()
        {
            if (_currentEditingFilePath == null)
                return;

            try
            {
                var (success, message) = _storageService.DeleteStructuredNote(_currentEditingFilePath);

                if (success)
                {
                    // Reset automatique après suppression - Retour à l'état initial
                    RawNoteText = string.Empty;
                    StructuredNoteDocument = new FlowDocument();
                    IsStructuredNoteReadOnly = true;
                    IsModifierButtonVisible = false;
                    IsSupprimerButtonVisible = false;
                    IsAnnulerButtonVisible = false;  // ✅ CORRECTION: Masquer le bouton Annuler
                    IsSauvegarderButtonEnabled = false;
                    IsSauvegarderButtonVisible = false;
                    IsStructurerButtonVisible = true;
                    IsRawNoteVisible = true; // Ré-afficher pour une nouvelle note
                    _currentEditingFilePath = null;
                    SelectedNote = null;

                    RaiseStatusMessage(message + " ✓ Prêt pour une nouvelle note");

                    // Déclencher événement pour forcer MainWindow à vider le RichTextBox
                    NoteClearedAfterSave?.Invoke(this, EventArgs.Empty);

                    // ✅ APPEL DIRECT au lieu d'événement : rafraîchir la liste IMMÉDIATEMENT
                    if (_patientIndexService != null && !string.IsNullOrEmpty(_currentPatientName))
                    {
                        LoadNotes(_currentPatientName, _patientIndexService);
                    }
                }
            }
            catch (Exception ex)
            {
                RaiseStatusMessage($"❌ {ex.Message}");
            }
        }

        /// <summary>
        /// Nettoie le YAML d'un contenu Markdown
        /// </summary>
        private string CleanYamlFromMarkdown(string content)
        {
            if (string.IsNullOrWhiteSpace(content))
                return content;

            if (!content.TrimStart().StartsWith("---"))
                return content;

            var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            bool inYaml = false;
            int yamlEndIndex = 0;

            for (int i = 0; i < lines.Length; i++)
            {
                if (i == 0 && lines[i].Trim() == "---")
                {
                    inYaml = true;
                    continue;
                }
                if (inYaml && lines[i].Trim() == "---")
                {
                    yamlEndIndex = i + 1;
                    break;
                }
            }

            if (yamlEndIndex > 0 && yamlEndIndex < lines.Length)
            {
                return string.Join("\n", lines.Skip(yamlEndIndex)).TrimStart();
            }

            return content;
        }

        /// <summary>
        /// Déclenche un message de statut
        /// </summary>
        private void RaiseStatusMessage(string message)
        {
            StatusMessageChanged?.Invoke(this, message);
        }
    }

    /// <summary>
    /// Représente un élément de note dans la liste
    /// </summary>
    public class NoteItem
    {
        public string FilePath { get; set; } = "";
        public string DateLabel { get; set; } = "";
        public string Preview { get; set; } = "";
        public DateTime Date { get; set; }
    }
}
