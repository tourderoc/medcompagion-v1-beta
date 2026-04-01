using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MedCompanion.Commands;
using MedCompanion.Models;
using MedCompanion.Services;
using System.Threading;
using System.Threading.Tasks;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// ViewModel pour la gestion de la synthèse patient
    /// Gère l'affichage, la génération et les indicateurs de mise à jour
    /// </summary>
    public class PatientSynthesisViewModel : ViewModelBase
    {
        private readonly SynthesisService _synthesisService;
        private readonly SynthesisWeightTracker _synthesisWeightTracker;
        private PatientIndexEntry? _currentPatient;

        #region Properties

        private FlowDocument _synthesisDocument = new();
        public FlowDocument SynthesisDocument
        {
            get => _synthesisDocument;
            set => SetProperty(ref _synthesisDocument, value);
        }

        private string _lastUpdateText = "📅 Dernière mise à jour : Jamais";
        public string LastUpdateText
        {
            get => _lastUpdateText;
            set => SetProperty(ref _lastUpdateText, value);
        }

        private bool _isGenerateButtonEnabled;
        public bool IsGenerateButtonEnabled
        {
            get => _isGenerateButtonEnabled;
            set
            {
                if (SetProperty(ref _isGenerateButtonEnabled, value))
                {
                    (GenerateSynthesisCommand as RelayCommand)?.RaiseCanExecuteChanged();
                }
            }
        }

        private string _generateButtonContent = "🔄 Actualiser";
        public string GenerateButtonContent
        {
            get => _generateButtonContent;
            set => SetProperty(ref _generateButtonContent, value);
        }

        private bool _isBadgeVisible;
        public bool IsBadgeVisible
        {
            get => _isBadgeVisible;
            set => SetProperty(ref _isBadgeVisible, value);
        }

        private string _badgeText = "!";
        public string BadgeText
        {
            get => _badgeText;
            set => SetProperty(ref _badgeText, value);
        }

        private string _generateButtonTooltip = "Actualiser la synthèse patient";
        public string GenerateButtonTooltip
        {
            get => _generateButtonTooltip;
            set => SetProperty(ref _generateButtonTooltip, value);
        }

        private double _weightProgressWidth;
        public double WeightProgressWidth
        {
            get => _weightProgressWidth;
            set => SetProperty(ref _weightProgressWidth, value);
        }

        private Brush _weightProgressColor = new SolidColorBrush(Color.FromRgb(46, 204, 113));
        public Brush WeightProgressColor
        {
            get => _weightProgressColor;
            set => SetProperty(ref _weightProgressColor, value);
        }

        private string _weightIndicatorText = "Poids: 0.0/1.0";
        public string WeightIndicatorText
        {
            get => _weightIndicatorText;
            set => SetProperty(ref _weightIndicatorText, value);
        }

        private string? _weightIndicatorTooltip;
        public string? WeightIndicatorTooltip
        {
            get => _weightIndicatorTooltip;
            set => SetProperty(ref _weightIndicatorTooltip, value);
        }

        #endregion

        #region Commands

        public ICommand GenerateSynthesisCommand { get; }
        public ICommand ViewSynthesisCommand { get; }

        #endregion

        #region Events

        /// <summary>
        /// Événement déclenché pour notifier un changement de statut
        /// </summary>
        public event EventHandler<string>? StatusChanged;

        /// <summary>
        /// Événement déclenché pour demander l'ouverture de la vue détaillée
        /// </summary>
        public event EventHandler<string>? OpenDetailedViewRequested;

        #endregion

        public PatientSynthesisViewModel(
            SynthesisService synthesisService,
            SynthesisWeightTracker synthesisWeightTracker)
        {
            _synthesisService = synthesisService ?? throw new ArgumentNullException(nameof(synthesisService));
            _synthesisWeightTracker = synthesisWeightTracker ?? throw new ArgumentNullException(nameof(synthesisWeightTracker));

            GenerateSynthesisCommand = new RelayCommand(
                execute: async () => await GenerateSynthesisAsync(),
                canExecute: () => IsGenerateButtonEnabled
            );

            ViewSynthesisCommand = new RelayCommand(ViewSynthesis);

            // S'abonner aux mises à jour de poids
            _synthesisWeightTracker.WeightUpdated += OnWeightUpdated;

            InitializeDefaultState();
        }

        #region Public Methods

        /// <summary>
        /// Définit le patient courant et charge sa synthèse
        /// </summary>
        public void SetCurrentPatient(PatientIndexEntry? patient)
        {
            _currentPatient = patient;
            LoadPatientSynthesis();
        }

        /// <summary>
        /// Met à jour les indicateurs (badge et poids) (appelé depuis MainWindow)
        /// </summary>
        public void UpdateNotificationBadge()
        {
            UpdateNotificationBadgeInternal();
            UpdateWeightIndicatorInternal();
        }

        /// <summary>
        /// Rafraîchit l'affichage de la synthèse (après modification externe)
        /// </summary>
        public void Refresh()
        {
            System.Diagnostics.Debug.WriteLine("[PatientSynthesisViewModel] Refresh() appelé - rechargement de la synthèse...");
            LoadPatientSynthesis();
            System.Diagnostics.Debug.WriteLine("[PatientSynthesisViewModel] Refresh() terminé");
        }

        private void OnWeightUpdated(object? sender, string patientName)
        {
            // Vérifier si la mise à jour concerne le patient en cours
            if (_currentPatient != null && _currentPatient.NomComplet == patientName)
            {
                // Mettre à jour les indicateurs sur le thread UI
                Application.Current.Dispatcher.Invoke(() =>
                {
                    UpdateNotificationBadgeInternal();
                    UpdateWeightIndicatorInternal();
                });
            }
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Initialise l'état par défaut du ViewModel
        /// </summary>
        private void InitializeDefaultState()
        {
            SynthesisDocument = new FlowDocument();
            LastUpdateText = "📅 Dernière mise à jour : Jamais";
            IsGenerateButtonEnabled = false;
            GenerateButtonContent = "🔄 Actualiser";
            IsBadgeVisible = false;
            WeightProgressWidth = 0;
            WeightIndicatorText = "Poids: 0.0/1.0";
        }

        /// <summary>
        /// Charge la synthèse existante du patient courant
        /// </summary>
        private void LoadPatientSynthesis()
        {
            if (_currentPatient == null)
            {
                InitializeDefaultState();
                return;
            }

            // Activer le bouton (un patient est sélectionné)
            IsGenerateButtonEnabled = true;

            try
            {
                // Vérifier si une synthèse existe
                var synthesisPath = Path.Combine(_currentPatient.DirectoryPath, "synthese", "synthese.md");
                System.Diagnostics.Debug.WriteLine($"[LoadPatientSynthesis] Chemin: {synthesisPath}");

                if (File.Exists(synthesisPath))
                {
                    // Charger et afficher la synthèse
                    var synthesisContent = File.ReadAllText(synthesisPath);
                    System.Diagnostics.Debug.WriteLine($"[LoadPatientSynthesis] Contenu chargé: {synthesisContent.Length} caractères");

                    // Convertir Markdown en FlowDocument
                    try
                    {
                        var newDoc = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(synthesisContent);
                        System.Diagnostics.Debug.WriteLine($"[LoadPatientSynthesis] FlowDocument créé avec {newDoc.Blocks.Count} blocs");

                        // Forcer la mise à jour en créant toujours un nouveau document
                        SynthesisDocument = newDoc;

                        System.Diagnostics.Debug.WriteLine("[LoadPatientSynthesis] SynthesisDocument mis à jour");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LoadPatientSynthesis] Erreur conversion: {ex.Message}");
                        // En cas d'erreur, afficher en texte brut
                        var doc = new FlowDocument();
                        doc.Blocks.Add(new Paragraph(new Run(synthesisContent)));
                        SynthesisDocument = doc;
                    }

                    // Afficher la date de dernière modification
                    var fileInfo = new FileInfo(synthesisPath);
                    LastUpdateText = $"📅 Dernière mise à jour : {fileInfo.LastWriteTime:dd/MM/yyyy HH:mm}";
                    System.Diagnostics.Debug.WriteLine($"[LoadPatientSynthesis] Date mise à jour: {fileInfo.LastWriteTime}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[LoadPatientSynthesis] Fichier synthese.md non trouvé");
                    // Pas de synthèse → Message par défaut
                    var doc = new FlowDocument();
                    var para = new Paragraph(new Run("Aucune synthèse disponible pour ce patient.\n\nCliquez sur 'Générer/Actualiser Synthèse' pour créer une synthèse globale."))
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(149, 165, 166)),
                        FontSize = 13
                    };
                    doc.Blocks.Add(para);
                    SynthesisDocument = doc;

                    LastUpdateText = "📅 Dernière mise à jour : Jamais";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadPatientSynthesis] Erreur: {ex.Message}");
                SynthesisDocument = new FlowDocument();
                LastUpdateText = "📅 Dernière mise à jour : Erreur";
            }

            // Mettre à jour les indicateurs
            UpdateNotificationBadgeInternal();
            UpdateWeightIndicatorInternal();
        }

        /// <summary>
        /// Met à jour le badge de notification si une mise à jour est recommandée
        /// </summary>
        private void UpdateNotificationBadgeInternal()
        {
            if (_currentPatient == null)
            {
                IsBadgeVisible = false;
                return;
            }

            try
            {
                var (shouldUpdate, currentWeight, items) =
                    _synthesisWeightTracker.CheckUpdateNeeded(_currentPatient.NomComplet);

                if (shouldUpdate && items.Count > 0)
                {
                    // Afficher le badge avec le nombre d'items
                    IsBadgeVisible = true;
                    BadgeText = items.Count.ToString();

                    // Tooltip enrichi avec détails
                    var weightDesc = ContentWeightRules.GetWeightDescription(currentWeight);
                    GenerateButtonTooltip =
                        $"⚠️ Mise à jour recommandée\n" +
                        $"Poids accumulé: {currentWeight:F1}/1.0 ({weightDesc})\n" +
                        $"{items.Count} nouveau(x) élément(s) en attente";

                    System.Diagnostics.Debug.WriteLine(
                        $"[PatientSynthesisViewModel] Badge affiché: {items.Count} items, poids {currentWeight:F1}/1.0");
                }
                else
                {
                    // Masquer le badge
                    IsBadgeVisible = false;
                    GenerateButtonTooltip = "Actualiser la synthèse patient";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateNotificationBadge] Erreur: {ex.Message}");
                IsBadgeVisible = false;
            }
        }

        /// <summary>
        /// Met à jour l'indicateur de poids (barre de progression et texte)
        /// </summary>
        private void UpdateWeightIndicatorInternal()
        {
            if (_currentPatient == null)
            {
                // Réinitialiser à zéro si aucun patient
                WeightProgressWidth = 0;
                WeightIndicatorText = "Poids: 0.0/1.0";
                WeightProgressColor = new SolidColorBrush(Color.FromRgb(127, 140, 141));
                WeightIndicatorTooltip = null;
                return;
            }

            try
            {
                var (shouldUpdate, currentWeight, items) =
                    _synthesisWeightTracker.CheckUpdateNeeded(_currentPatient.NomComplet);

                // Mise à jour de la largeur de la barre (max 100px = 1.0)
                WeightProgressWidth = Math.Min(currentWeight * 100, 100);

                // Couleur selon le poids
                Color fillColor = currentWeight switch
                {
                    >= 1.0 => Color.FromRgb(231, 76, 60),    // Rouge #E74C3C
                    >= 0.6 => Color.FromRgb(243, 156, 18),   // Orange #F39C12
                    _ => Color.FromRgb(46, 204, 113)         // Vert #2ECC71
                };
                WeightProgressColor = new SolidColorBrush(fillColor);

                // Texte de l'indicateur
                WeightIndicatorText = currentWeight >= 1.0 && items.Count > 0
                    ? $"Poids: {currentWeight:F1}/1.0 ({items.Count} items)"
                    : $"Poids: {currentWeight:F1}/1.0";

                // Tooltip détaillé avec la liste des items
                if (items.Count > 0)
                {
                    var tooltip = new StringBuilder();
                    tooltip.AppendLine($"Poids accumulé: {currentWeight:F1}/1.0");
                    tooltip.AppendLine($"\nÉléments en attente ({items.Count}):");

                    foreach (var item in items.Take(5))
                    {
                        tooltip.AppendLine($"• {item.ItemType}: {item.RelevanceWeight:F1}");
                    }

                    if (items.Count > 5)
                        tooltip.AppendLine($"... et {items.Count - 5} autre(s)");

                    WeightIndicatorTooltip = tooltip.ToString().Trim();
                }
                else
                {
                    WeightIndicatorTooltip = "Aucun élément en attente";
                }

                System.Diagnostics.Debug.WriteLine(
                    $"[PatientSynthesisViewModel] Indicateur de poids: {currentWeight:F1}/1.0 ({items.Count} items)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateWeightIndicator] Erreur: {ex.Message}");
                WeightProgressWidth = 0;
                WeightIndicatorText = "Poids: 0.0/1.0";
            }
        }

        /// <summary>
        /// Génère ou met à jour la synthèse du patient avec l'IA
        /// </summary>
        private async System.Threading.Tasks.Task GenerateSynthesisAsync()
        {
            if (_currentPatient == null)
            {
                MessageBox.Show("Veuillez d'abord sélectionner un patient.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            FlowDocument? originalDoc = SynthesisDocument;
            try
            {
                // Désactiver le bouton pendant la génération
                IsGenerateButtonEnabled = false;
                GenerateButtonContent = "⏳ Analyse en cours...";

                StatusChanged?.Invoke(this, "⏳ Analyse du dossier patient...");

                // ÉTAPE 1 : Vérifier si une mise à jour est nécessaire
                var (needsUpdate, newItems, lastSynthesisDate) = _synthesisService.CheckForUpdates(_currentPatient.DirectoryPath);

                if (!needsUpdate)
                {
                    // Synthèse déjà à jour
                    MessageBox.Show(
                        "✅ La synthèse est déjà à jour !\n\n" +
                        $"Dernière mise à jour : {lastSynthesisDate:dd/MM/yyyy HH:mm}\n\n" +
                        "Aucun nouveau contenu détecté depuis la dernière génération.",
                        "Synthèse à jour",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information
                    );

                    StatusChanged?.Invoke(this, "✓ Synthèse déjà à jour");
                    return;
                }

                // Message de chargement
                var loadingDoc = new FlowDocument();
                var loadingPara = new Paragraph(new Run(
                    lastSynthesisDate == null
                        ? "⏳ Génération de la synthèse complète du patient...\n\nCela peut prendre quelques instants."
                        : $"⏳ Mise à jour incrémentale de la synthèse...\n\n{newItems.Count} nouveau(x) élément(s) détecté(s).\n\nCela peut prendre quelques instants."
                ))
                {
                    Foreground = new SolidColorBrush(Color.FromRgb(52, 152, 219)),
                    FontSize = 13
                };
                loadingDoc.Blocks.Add(loadingPara);
                SynthesisDocument = loadingDoc;

                string synthesisMarkdown;
                bool success;
                string? error;

                if (lastSynthesisDate == null)
                {
                    // GÉNÉRATION COMPLÈTE (première fois)
                    GenerateButtonContent = "⏳ Génération complète...";
                    StatusChanged?.Invoke(this, "⏳ Génération complète de la synthèse...");

                    (success, synthesisMarkdown, error) = await _synthesisService.GenerateCompleteSynthesisAsync(
                        _currentPatient.NomComplet,
                        _currentPatient.DirectoryPath
                    );
                }
                else
                {
                    // MISE À JOUR INCRÉMENTALE
                    GenerateButtonContent = "⏳ Mise à jour incrémentale...";
                    StatusChanged?.Invoke(this, $"⏳ Mise à jour incrémentale ({newItems.Count} nouveaux éléments)...");

                    // Charger la synthèse existante
                    var synthesisPath = Path.Combine(_currentPatient.DirectoryPath, "synthese", "synthese.md");
                    var existingSynthesis = File.Exists(synthesisPath) ? File.ReadAllText(synthesisPath) : "";

                    (success, synthesisMarkdown, error) = await _synthesisService.UpdateSynthesisIncrementallyAsync(
                        _currentPatient.NomComplet,
                        _currentPatient.DirectoryPath,
                        existingSynthesis,
                        newItems
                    );
                }

                if (success && !string.IsNullOrEmpty(synthesisMarkdown))
                {
                    // Sauvegarder la synthèse
                    var (saveSuccess, saveMessage) = _synthesisService.SaveSynthesis(
                        _currentPatient.DirectoryPath,
                        synthesisMarkdown
                    );

                    if (saveSuccess)
                    {
                        // Afficher la synthèse
                        try
                        {
                            // Nettoyer le YAML avant affichage
                            var cleanedSynthesis = synthesisMarkdown;
                            if (synthesisMarkdown.StartsWith("---"))
                            {
                                var endYamlIndex = synthesisMarkdown.IndexOf("---", 3);
                                if (endYamlIndex > 0)
                                {
                                    cleanedSynthesis = synthesisMarkdown.Substring(endYamlIndex + 3).Trim();
                                }
                            }

                            SynthesisDocument = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(cleanedSynthesis);

                            // Mise à jour du label de date
                            LastUpdateText = $"📅 Dernière mise à jour : {DateTime.Now:dd/MM/yyyy HH:mm}";

                            // Masquer le badge (la synthèse a été mise à jour)
                            IsBadgeVisible = false;
                            GenerateButtonTooltip = "Actualiser la synthèse patient";

                            // Mettre à jour l'indicateur de poids (reset à 0)
                            UpdateWeightIndicatorInternal();

                            StatusChanged?.Invoke(this, "✅ Synthèse générée avec succès");
                        }
                        catch (Exception ex)
                        {
                            // En cas d'erreur de conversion, afficher en texte brut
                            var doc = new FlowDocument();
                            doc.Blocks.Add(new Paragraph(new Run(synthesisMarkdown)));
                            SynthesisDocument = doc;

                            System.Diagnostics.Debug.WriteLine($"Erreur conversion markdown: {ex.Message}");
                        }
                    }
                    else
                    {
                        MessageBox.Show(
                            $"Erreur lors de la sauvegarde : {saveMessage}",
                            "Erreur",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error
                        );
                        StatusChanged?.Invoke(this, "❌ Erreur sauvegarde synthèse");
                    }
                }
                else
                {
                    MessageBox.Show(
                        $"Erreur lors de la génération : {error}",
                        "Erreur",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error
                    );
                    StatusChanged?.Invoke(this, "❌ Erreur génération synthèse");
                }
            }
            catch (OperationCanceledException)
            {
                // Annulation silencieuse demandée par l'utilisateur (via BusyService ou timeout HttpClient)
                System.Diagnostics.Debug.WriteLine("[PatientSynthesisViewModel] Génération annulée ou timeout.");
                StatusChanged?.Invoke(this, "✓ Opération annulée");

                // Restaurer la synthèse précédente
                SynthesisDocument = originalDoc;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Erreur inattendue : {ex.Message}",
                    "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error
                );
                StatusChanged?.Invoke(this, $"❌ Erreur: {ex.Message}");
            }
            finally
            {
                // Réactiver le bouton
                IsGenerateButtonEnabled = true;
                GenerateButtonContent = "🔄 Actualiser";
            }
        }

        /// <summary>
        /// Ouvre la synthèse en vue détaillée
        /// </summary>
        private void ViewSynthesis()
        {
            if (_currentPatient == null)
                return;

            var synthesisPath = Path.Combine(_currentPatient.DirectoryPath, "synthese", "synthese.md");

            if (!File.Exists(synthesisPath))
            {
                MessageBox.Show(
                    "Aucune synthèse disponible.\n\nCliquez sur 'Actualiser' pour générer une synthèse.",
                    "Information",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            OpenDetailedViewRequested?.Invoke(this, synthesisPath);
        }

        #endregion
    }
}
