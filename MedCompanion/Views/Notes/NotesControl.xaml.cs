using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using MedCompanion.Models;
using MedCompanion.Services;

namespace MedCompanion.Views.Notes
{
    /// <summary>
    /// UserControl pour la gestion des notes (brute/structurée + synthèse patient)
    /// </summary>
    public partial class NotesControl : UserControl
    {
        private SynthesisService? _synthesisService;
        private SynthesisWeightTracker? _synthesisWeightTracker;
        private PatientIndexEntry? _currentPatient;

        public event EventHandler<string>? StatusChanged;

        public NotesControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Initialise le contrôle avec les services nécessaires
        /// </summary>
        public void Initialize(SynthesisService synthesisService, SynthesisWeightTracker synthesisWeightTracker)
        {
            _synthesisService = synthesisService;
            _synthesisWeightTracker = synthesisWeightTracker;
        }

        /// <summary>
        /// Définit le patient courant et charge sa synthèse
        /// </summary>
        public void SetCurrentPatient(PatientIndexEntry? patient)
        {
            _currentPatient = patient;
            LoadPatientSynthesis();
        }

        // Exposer les contrôles pour que MainWindow puisse y accéder
        public RichTextBox SynthesisPreviewTextBox => SynthesisPreviewText;
        public TextBlock LastSynthesisUpdateTextBlock => LastSynthesisUpdateLabel;
        public Button GenerateSynthesisBtn => GenerateSynthesisButton;
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

        #region Synthèse Patient

        /// <summary>
        /// Charge la synthèse existante du patient courant
        /// </summary>
        private void LoadPatientSynthesis()
        {
            if (_currentPatient == null)
            {
                SynthesisPreviewText.Document = new FlowDocument();
                LastSynthesisUpdateLabel.Text = "📅 Dernière mise à jour : Jamais";
                GenerateSynthesisButton.IsEnabled = false;
                return;
            }

            // Activer le bouton (un patient est sélectionné)
            GenerateSynthesisButton.IsEnabled = true;

            try
            {
                // Vérifier si une synthèse existe (dans le sous-dossier synthese/)
                var synthesisPath = Path.Combine(_currentPatient.DirectoryPath, "synthese", "synthese.md");

                if (File.Exists(synthesisPath))
                {
                    // Charger et afficher la synthèse
                    var synthesisContent = File.ReadAllText(synthesisPath);

                    // Convertir Markdown en FlowDocument
                    try
                    {
                        SynthesisPreviewText.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(synthesisContent);
                    }
                    catch
                    {
                        // En cas d'erreur, afficher en texte brut
                        var doc = new FlowDocument();
                        doc.Blocks.Add(new Paragraph(new Run(synthesisContent)));
                        SynthesisPreviewText.Document = doc;
                    }

                    // Afficher la date de dernière modification
                    var fileInfo = new FileInfo(synthesisPath);
                    LastSynthesisUpdateLabel.Text = $"📅 Dernière mise à jour : {fileInfo.LastWriteTime:dd/MM/yyyy HH:mm}";
                }
                else
                {
                    // Pas de synthèse → Message par défaut
                    var doc = new FlowDocument();
                    var para = new Paragraph(new Run("Aucune synthèse disponible pour ce patient.\n\nCliquez sur 'Générer/Actualiser Synthèse' pour créer une synthèse globale."))
                    {
                        Foreground = new SolidColorBrush(Color.FromRgb(149, 165, 166)),
                        FontSize = 13
                    };
                    doc.Blocks.Add(para);
                    SynthesisPreviewText.Document = doc;

                    LastSynthesisUpdateLabel.Text = "📅 Dernière mise à jour : Jamais";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LoadPatientSynthesis] Erreur: {ex.Message}");
                SynthesisPreviewText.Document = new FlowDocument();
                LastSynthesisUpdateLabel.Text = "📅 Dernière mise à jour : Erreur";
            }

            // NOUVEAU : Vérifier et afficher le badge de notification si mise à jour recommandée
            UpdateNotificationBadge();

            // NOUVEAU : Mettre à jour l'indicateur de poids (barre de progression)
            UpdateWeightIndicator();
        }

        /// <summary>
        /// Met à jour le badge de notification si une mise à jour de la synthèse est recommandée
        /// </summary>
        public void UpdateNotificationBadge()
        {
            if (_currentPatient == null || _synthesisWeightTracker == null)
            {
                SynthesisUpdateBadge.Visibility = Visibility.Collapsed;
                return;
            }

            try
            {
                var (shouldUpdate, currentWeight, items) =
                    _synthesisWeightTracker.CheckUpdateNeeded(_currentPatient.NomComplet);

                if (shouldUpdate && items.Count > 0)
                {
                    // Afficher le badge avec le nombre d'items
                    SynthesisUpdateBadge.Visibility = Visibility.Visible;
                    SynthesisUpdateBadgeText.Text = items.Count.ToString();

                    // Tooltip enrichi avec détails
                    var weightDesc = ContentWeightRules.GetWeightDescription(currentWeight);
                    GenerateSynthesisButton.ToolTip =
                        $"⚠️ Mise à jour recommandée\n" +
                        $"Poids accumulé: {currentWeight:F1}/1.0 ({weightDesc})\n" +
                        $"{items.Count} nouveau(x) élément(s) en attente";

                    System.Diagnostics.Debug.WriteLine(
                        $"[NotesControl] Badge affiché: {items.Count} items, poids {currentWeight:F1}/1.0");
                }
                else
                {
                    // Masquer le badge
                    SynthesisUpdateBadge.Visibility = Visibility.Collapsed;
                    GenerateSynthesisButton.ToolTip = "Actualiser la synthèse patient";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateNotificationBadge] Erreur: {ex.Message}");
                SynthesisUpdateBadge.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Met à jour l'indicateur de poids (barre de progression et texte)
        /// </summary>
        public void UpdateWeightIndicator()
        {
            if (_currentPatient == null || _synthesisWeightTracker == null)
            {
                // Réinitialiser à zéro si aucun patient
                WeightProgressFill.Width = 0;
                WeightIndicatorText.Text = "Poids: 0.0/1.0";
                WeightIndicatorText.Foreground = new SolidColorBrush(Color.FromRgb(127, 140, 141)); // #7F8C8D
                WeightIndicatorText.ToolTip = null;
                return;
            }

            try
            {
                var (shouldUpdate, currentWeight, items) =
                    _synthesisWeightTracker.CheckUpdateNeeded(_currentPatient.NomComplet);

                // Mise à jour de la largeur de la barre (max 100px = 1.0)
                double fillWidth = Math.Min(currentWeight * 100, 100);
                WeightProgressFill.Width = fillWidth;

                // Couleur selon le poids
                Color fillColor = currentWeight switch
                {
                    >= 1.0 => Color.FromRgb(231, 76, 60),    // Rouge #E74C3C
                    >= 0.6 => Color.FromRgb(243, 156, 18),   // Orange #F39C12
                    _ => Color.FromRgb(46, 204, 113)         // Vert #2ECC71
                };
                WeightProgressFill.Background = new SolidColorBrush(fillColor);

                // Texte de l'indicateur
                string text = currentWeight >= 1.0 && items.Count > 0
                    ? $"Poids: {currentWeight:F1}/1.0 ({items.Count} items)"
                    : $"Poids: {currentWeight:F1}/1.0";

                WeightIndicatorText.Text = text;
                WeightIndicatorText.Foreground = new SolidColorBrush(fillColor);

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

                    WeightIndicatorText.ToolTip = tooltip.ToString().Trim();
                }
                else
                {
                    WeightIndicatorText.ToolTip = "Aucun élément en attente";
                }

                System.Diagnostics.Debug.WriteLine(
                    $"[NotesControl] Indicateur de poids: {currentWeight:F1}/1.0 ({items.Count} items)");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UpdateWeightIndicator] Erreur: {ex.Message}");
                WeightProgressFill.Width = 0;
                WeightIndicatorText.Text = "Poids: 0.0/1.0";
            }
        }

        /// <summary>
        /// Génère ou met à jour la synthèse du patient avec l'IA
        /// </summary>
        private async void GenerateSynthesisButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPatient == null || _synthesisService == null)
            {
                MessageBox.Show("Veuillez d'abord sélectionner un patient.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // Désactiver le bouton pendant la génération
                GenerateSynthesisButton.IsEnabled = false;
                GenerateSynthesisButton.Content = "⏳ Analyse en cours...";

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
                SynthesisPreviewText.Document = loadingDoc;

                string synthesisMarkdown;
                bool success;
                string? error;

                if (lastSynthesisDate == null)
                {
                    // GÉNÉRATION COMPLÈTE (première fois)
                    GenerateSynthesisButton.Content = "⏳ Génération complète...";
                    StatusChanged?.Invoke(this, "⏳ Génération complète de la synthèse...");

                    (success, synthesisMarkdown, error) = await _synthesisService.GenerateCompleteSynthesisAsync(
                        _currentPatient.NomComplet,
                        _currentPatient.DirectoryPath
                    );
                }
                else
                {
                    // MISE À JOUR INCRÉMENTALE
                    GenerateSynthesisButton.Content = "⏳ Mise à jour incrémentale...";
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

                            SynthesisPreviewText.Document = MarkdownFlowDocumentConverter.MarkdownToFlowDocument(cleanedSynthesis);

                            // Mise à jour du label de date
                            LastSynthesisUpdateLabel.Text = $"📅 Dernière mise à jour : {DateTime.Now:dd/MM/yyyy HH:mm}";

                            // NOUVEAU : Masquer le badge (la synthèse a été mise à jour)
                            SynthesisUpdateBadge.Visibility = Visibility.Collapsed;
                            GenerateSynthesisButton.ToolTip = "Actualiser la synthèse patient";

                            // NOUVEAU : Mettre à jour l'indicateur de poids (reset à 0)
                            UpdateWeightIndicator();

                            StatusChanged?.Invoke(this, "✅ Synthèse générée avec succès");

                            MessageBox.Show(
                                saveMessage,
                                "Synthèse générée",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information
                            );
                        }
                        catch (Exception ex)
                        {
                            // En cas d'erreur de conversion, afficher en texte brut
                            var doc = new FlowDocument();
                            doc.Blocks.Add(new Paragraph(new Run(synthesisMarkdown)));
                            SynthesisPreviewText.Document = doc;

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
                GenerateSynthesisButton.IsEnabled = true;
                GenerateSynthesisButton.Content = "🔄 Générer/Actualiser Synthèse";
            }
        }

        #endregion
    }
}
