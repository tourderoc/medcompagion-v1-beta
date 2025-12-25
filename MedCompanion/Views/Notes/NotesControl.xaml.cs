using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.ViewModels;

namespace MedCompanion.Views.Notes
{
    /// <summary>
    /// UserControl pour la gestion des notes (brute/structurée + synthèse patient)
    /// </summary>
    public partial class NotesControl : UserControl
    {
        private PatientIndexEntry? _currentPatient;
        private RegenerationService? _regenerationService;

        public event EventHandler<string>? StatusChanged;

        /// <summary>
        /// ViewModel pour la gestion de la synthèse patient
        /// </summary>
        public PatientSynthesisViewModel? SynthesisViewModel { get; private set; }

        /// <summary>
        /// ViewModel pour la gestion des notes
        /// </summary>
        public NoteViewModel? NoteViewModel { get; private set; }

        public NotesControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initialise le contrôle avec les services nécessaires
        /// </summary>
        public void Initialize(SynthesisService synthesisService, SynthesisWeightTracker synthesisWeightTracker, NoteViewModel noteViewModel, RegenerationService? regenerationService = null)
        {
            // Créer le ViewModel de synthèse
            SynthesisViewModel = new PatientSynthesisViewModel(synthesisService, synthesisWeightTracker);

            // Assigner le ViewModel des notes
            NoteViewModel = noteViewModel;

            // Stocker le service de régénération
            _regenerationService = regenerationService;

            // Connecter les événements du ViewModel de synthèse
            SynthesisViewModel.StatusChanged += (s, msg) => StatusChanged?.Invoke(this, msg);
            SynthesisViewModel.OpenDetailedViewRequested += (s, path) =>
                OpenDetailedView(path, Dialogs.ContentType.Synthesis);

            // Définir le DataContext pour les bindings
            this.DataContext = this;
        }

        /// <summary>
        /// Définit le patient courant et charge sa synthèse
        /// </summary>
        public void SetCurrentPatient(PatientIndexEntry? patient)
        {
            _currentPatient = patient;
            SynthesisViewModel?.SetCurrentPatient(patient);
        }

        // Exposer les contrôles pour que MainWindow puisse y accéder (Notes uniquement, pas synthèse)
        public TextBox RawNoteTextBox => RawNoteText;
        public RichTextBox StructuredNoteTextBox => StructuredNoteText;
        public Button StructurerBtn => StructurerButton;
        public Button ValiderSauvegarderBtn => ValiderSauvegarderButton;
        public TextBlock RawNoteLabelBlock => RawNoteLabel;
        public TextBlock StructuredNoteLabelBlock => StructuredNoteLabel;
        public Button FermerConsultationBtn => FermerConsultationButton;

        /// <summary>
        /// Handler pour le bouton "Fermer" en mode consultation
        /// Délégué à la MainWindow via un event ou méthode publique
        /// </summary>
        private void FermerConsultationButton_Click(object sender, RoutedEventArgs e)
        {
            // Ce handler sera connecté depuis MainWindow
            // Pour l'instant, on laisse vide - MainWindow gèrera cet event
        }

        /// <summary>
        /// Handler appelé quand le texte de la note structurée change
        /// Utilisé pour activer le bouton Sauvegarder en mode édition
        /// </summary>
        private void StructuredNoteText_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Si on est en mode édition (pas readonly), activer le bouton Sauvegarder
            // Cette logique sera gérée par le ViewModel via le DataContext
            if (!StructuredNoteText.IsReadOnly && DataContext != null)
            {
                // Le ViewModel gérera l'activation du bouton via le binding
            }
        }

        #region Vue Détaillée

        /// <summary>
        /// Ouvre la note structurée en vue détaillée (plein écran)
        /// </summary>
        private void ViewNoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPatient == null)
                return;

            // Récupérer le NoteViewModel depuis MainWindow
            var mainWindow = Window.GetWindow(this) as MainWindow;
            if (mainWindow?.NoteViewModel?.SelectedNote == null)
            {
                MessageBox.Show(
                    "Aucune note sélectionnée.",
                    "Information",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );
                return;
            }

            string notePath = mainWindow.NoteViewModel.SelectedNote.FilePath;
            if (!File.Exists(notePath))
            {
                MessageBox.Show(
                    "Le fichier de la note n'existe plus.",
                    "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                return;
            }

            OpenDetailedView(notePath, Dialogs.ContentType.Note);
        }

        /// <summary>
        /// Double-clic sur la note structurée pour ouvrir en vue détaillée
        /// </summary>
        private void StructuredNoteText_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            ViewNoteButton_Click(sender, new RoutedEventArgs());
        }

        /// <summary>
        /// Ouvre un fichier dans le dialogue de vue détaillée
        /// </summary>
        private void OpenDetailedView(string filePath, Dialogs.ContentType contentType)
        {
            try
            {
                var dialog = new Dialogs.DetailedViewDialog();
                dialog.LoadContent(filePath, contentType, _currentPatient?.NomComplet ?? "Patient");

                // Initialiser le service de régénération si disponible
                if (_regenerationService != null)
                {
                    // Créer PatientMetadata depuis le patient courant
                    PatientMetadata? patientMeta = null;
                    if (_currentPatient != null)
                    {
                        patientMeta = new PatientMetadata
                        {
                            Nom = _currentPatient.Nom ?? "",
                            Prenom = _currentPatient.Prenom ?? "",
                            Sexe = _currentPatient.Sexe ?? "M"
                        };
                    }
                    dialog.InitializeRegenerationService(_regenerationService, patientMeta);
                }

                // S'abonner à l'événement de sauvegarde pour rafraîchir l'affichage
                dialog.ContentSaved += (s, args) =>
                {
                    // S'assurer que le rafraîchissement se fait sur le thread UI
                    Dispatcher.Invoke(() =>
                    {
                        // Recharger le contenu selon le type
                        if (contentType == Dialogs.ContentType.Synthesis)
                        {
                            SynthesisViewModel?.Refresh();
                            StatusChanged?.Invoke(this, "✅ Synthèse mise à jour");
                        }
                        else if (contentType == Dialogs.ContentType.Note)
                        {
                            // Recharger la note via le ViewModel
                            NoteViewModel?.ReloadCurrentNote();
                            StatusChanged?.Invoke(this, "✅ Note mise à jour");
                        }
                    });
                };

                dialog.Owner = Window.GetWindow(this);
                dialog.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Erreur lors de l'ouverture de la vue détaillée :\n{ex.Message}",
                    "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
            }
        }

        #endregion
    }
}
