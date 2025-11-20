using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MedCompanion.Models;

namespace MedCompanion.Dialogs
{
    public partial class OrdonnanceBiologieDialog : Window
    {
        private readonly string _patientNom;
        private readonly string _patientPrenom;
        private readonly string _patientDob;

        private BilanBiologiquePreset? _currentPreset;
        private List<CheckBox> _checkBoxes = new();

        /// <summary>
        /// Résultat de la sélection (null si annulé)
        /// </summary>
        public OrdonnanceBiologie? Result { get; private set; }

        public OrdonnanceBiologieDialog(string patientNom, string patientPrenom, string patientDob)
        {
            InitializeComponent();

            _patientNom = patientNom;
            _patientPrenom = patientPrenom;
            _patientDob = patientDob;

            // Afficher les informations du patient
            PatientInfoTextBlock.Text = $"{patientNom} {patientPrenom} • Né(e) le {patientDob}";

            // Sélectionner le premier preset par défaut
            PresetComboBox.SelectedIndex = 0;
        }

        /// <summary>
        /// Changement de preset - recharger les examens
        /// </summary>
        private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PresetComboBox.SelectedItem is not ComboBoxItem selectedItem)
                return;

            var tag = selectedItem.Tag as string;

            _currentPreset = tag switch
            {
                "standard" => BilanBiologiquePreset.Presets.BilanStandardPediatrique,
                "sansjeune" => BilanBiologiquePreset.Presets.BilanSansJeune,
                "thyroidien" => BilanBiologiquePreset.Presets.BilanThyroidien,
                _ => null
            };

            if (_currentPreset == null)
                return;

            // Afficher la description du preset
            PresetDescriptionTextBlock.Text = _currentPreset.Description ?? "";

            // Afficher la note si elle existe
            if (!string.IsNullOrWhiteSpace(_currentPreset.Note))
            {
                NoteBorder.Visibility = Visibility.Visible;
                NoteTextBlock.Text = _currentPreset.Note;
            }
            else
            {
                NoteBorder.Visibility = Visibility.Collapsed;
            }

            // Générer les cases à cocher
            GenerateCheckBoxes();
        }

        /// <summary>
        /// Génère dynamiquement les cases à cocher pour les examens
        /// </summary>
        private void GenerateCheckBoxes()
        {
            ExamensPanel.Children.Clear();
            _checkBoxes.Clear();

            if (_currentPreset == null)
                return;

            foreach (var examen in _currentPreset.Examens)
            {
                var checkBox = new CheckBox
                {
                    Content = examen.Nom,
                    IsChecked = examen.EstCoche,
                    Tag = examen // Stocker l'objet pour récupération ultérieure
                };

                // Ajouter une indication visuelle pour les examens optionnels
                if (!string.IsNullOrWhiteSpace(examen.Description))
                {
                    var stackPanel = new StackPanel { Orientation = Orientation.Vertical };
                    stackPanel.Children.Add(checkBox);

                    var descriptionTextBlock = new System.Windows.Controls.TextBlock
                    {
                        Text = $"   → {examen.Description}",
                        FontSize = 10,
                        Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(149, 165, 166)),
                        Margin = new Thickness(20, 0, 0, 5)
                    };
                    stackPanel.Children.Add(descriptionTextBlock);

                    ExamensPanel.Children.Add(stackPanel);
                }
                else
                {
                    ExamensPanel.Children.Add(checkBox);
                }

                _checkBoxes.Add(checkBox);
            }
        }

        /// <summary>
        /// Validation et génération de l'ordonnance
        /// </summary>
        private void ValiderButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPreset == null)
            {
                MessageBox.Show("Veuillez sélectionner un modèle de bilan.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Récupérer les examens cochés
            var examensCoches = _checkBoxes
                .Where(cb => cb.IsChecked == true && cb.Tag is ExamenBiologique)
                .Select(cb => cb.Tag as ExamenBiologique)
                .Where(e => e != null)
                .Cast<ExamenBiologique>()
                .ToList();

            if (examensCoches.Count == 0)
            {
                MessageBox.Show("Veuillez sélectionner au moins un examen.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Créer l'ordonnance
            Result = new OrdonnanceBiologie
            {
                PresetNom = _currentPreset.Nom,
                ExamensCoches = examensCoches,
                Note = _currentPreset.Note,
                PatientNom = _patientNom,
                PatientPrenom = _patientPrenom,
                PatientDateNaissance = _patientDob,
                DateCreation = DateTime.Now
            };

            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Annulation
        /// </summary>
        private void AnnulerButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
