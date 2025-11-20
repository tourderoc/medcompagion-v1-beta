using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MedCompanion.Models;
using MedCompanion.Services;

namespace MedCompanion.Dialogs
{
    public partial class EditMCCDialog : Window
    {
        private readonly MCCModel _mcc;
        private readonly MCCLibraryService _mccLibrary;

        public EditMCCDialog(MCCModel mcc, MCCLibraryService mccLibrary)
        {
            InitializeComponent();

            _mcc = mcc ?? throw new ArgumentNullException(nameof(mcc));
            _mccLibrary = mccLibrary ?? throw new ArgumentNullException(nameof(mccLibrary));

            LoadMCCData();
            
            // Surveiller les changements dans les mots-clés
            KeywordsTextBox.TextChanged += KeywordsTextBox_TextChanged;
        }

        /// <summary>
        /// Charge les données du MCC dans le formulaire
        /// </summary>
        private void LoadMCCData()
        {
            // Nom du MCC
            MCCNameText.Text = _mcc.Name;

            // Template
            TemplateTextBox.Text = _mcc.TemplateMarkdown ?? "";
            UpdateTemplateStats();

            // Type de document
            SelectComboBoxItemByTag(DocTypeCombo, _mcc.Semantic?.DocType ?? "courrier");

            // Audience
            SelectComboBoxItemByTag(AudienceCombo, _mcc.Semantic?.Audience ?? "mixte");

            // Ton
            SelectComboBoxItemByTag(ToneCombo, _mcc.Semantic?.Tone ?? "bienveillant");

            // Tranche d'âge
            SelectComboBoxItemByTag(AgeGroupCombo, _mcc.Semantic?.AgeGroup ?? "tous");

            // Mots-clés
            if (_mcc.Keywords != null && _mcc.Keywords.Count > 0)
            {
                KeywordsTextBox.Text = string.Join(", ", _mcc.Keywords);
            }

            UpdateKeywordsHint();
        }

        /// <summary>
        /// Sélectionne un item dans une ComboBox par son Tag
        /// </summary>
        private void SelectComboBoxItemByTag(ComboBox comboBox, string tag)
        {
            if (string.IsNullOrEmpty(tag))
            {
                comboBox.SelectedIndex = 0;
                return;
            }

            foreach (ComboBoxItem item in comboBox.Items)
            {
                if (item.Tag?.ToString() == tag)
                {
                    comboBox.SelectedItem = item;
                    return;
                }
            }

            // Si pas trouvé, sélectionner le premier item
            comboBox.SelectedIndex = 0;
        }

        /// <summary>
        /// Met à jour les stats du template
        /// </summary>
        private void TemplateTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateTemplateStats();
        }

        /// <summary>
        /// Met à jour le hint du nombre de mots-clés
        /// </summary>
        private void KeywordsTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            UpdateKeywordsHint();
        }

        /// <summary>
        /// Met à jour les statistiques du template
        /// </summary>
        private void UpdateTemplateStats()
        {
            var text = TemplateTextBox.Text;
            var lines = text.Split('\n').Length;
            var chars = text.Length;
            
            TemplateStatsText.Text = $"📊 {lines} lignes, {chars} caractères";

            // Compter les variables
            var variables = ExtractVariables(text);
            var varCount = variables.Count;
            
            if (varCount > 0)
            {
                TemplateVariablesText.Text = $"🔤 {varCount} variable(s) : {string.Join(", ", variables.Select(v => $"{{{{{v}}}}}"))}";
            }
            else
            {
                TemplateVariablesText.Text = "🔤 Aucune variable détectée";
            }
        }

        /// <summary>
        /// Extrait les variables du template
        /// </summary>
        private List<string> ExtractVariables(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            var matches = System.Text.RegularExpressions.Regex.Matches(text, @"\{\{([^}]+)\}\}");
            return matches
                .Cast<System.Text.RegularExpressions.Match>()
                .Select(m => m.Groups[1].Value.Trim())
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// Met à jour le texte d'indication pour les mots-clés
        /// </summary>
        private void UpdateKeywordsHint()
        {
            var keywords = ParseKeywords(KeywordsTextBox.Text);
            var count = keywords.Count;

            if (count == 5)
            {
                KeywordsHintText.Text = $"✅ {count} mots-clés (parfait !)";
                KeywordsHintText.Foreground = System.Windows.Media.Brushes.Green;
            }
            else if (count < 5)
            {
                KeywordsHintText.Text = $"⚠️ {count} mots-clés (5 recommandés)";
                KeywordsHintText.Foreground = System.Windows.Media.Brushes.Orange;
            }
            else
            {
                KeywordsHintText.Text = $"ℹ️ {count} mots-clés (5 recommandés, mais plus est acceptable)";
                KeywordsHintText.Foreground = System.Windows.Media.Brushes.DodgerBlue;
            }
        }

        /// <summary>
        /// Parse les mots-clés depuis le texte (séparés par virgules)
        /// </summary>
        private List<string> ParseKeywords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new List<string>();

            return text.Split(',')
                .Select(k => k.Trim())
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .ToList();
        }

        /// <summary>
        /// Récupère le Tag d'un ComboBoxItem sélectionné
        /// </summary>
        private string GetSelectedTag(ComboBox comboBox)
        {
            if (comboBox.SelectedItem is ComboBoxItem item)
            {
                return item.Tag?.ToString() ?? "";
            }
            return "";
        }

        /// <summary>
        /// Valide les données du formulaire
        /// </summary>
        private (bool isValid, string errorMessage) ValidateForm()
        {
            // Vérifier que tous les champs sont remplis
            if (DocTypeCombo.SelectedItem == null)
            {
                return (false, "⚠️ Veuillez sélectionner un type de document");
            }

            if (AudienceCombo.SelectedItem == null)
            {
                return (false, "⚠️ Veuillez sélectionner une audience");
            }

            if (ToneCombo.SelectedItem == null)
            {
                return (false, "⚠️ Veuillez sélectionner un ton");
            }

            if (AgeGroupCombo.SelectedItem == null)
            {
                return (false, "⚠️ Veuillez sélectionner une tranche d'âge");
            }

            // Vérifier les mots-clés
            var keywords = ParseKeywords(KeywordsTextBox.Text);
            if (keywords.Count == 0)
            {
                return (false, "⚠️ Veuillez entrer au moins un mot-clé");
            }

            if (keywords.Count < 3)
            {
                return (false, "⚠️ Veuillez entrer au moins 3 mots-clés (5 recommandés)");
            }

            return (true, string.Empty);
        }

        /// <summary>
        /// Affiche un message de validation
        /// </summary>
        private void ShowValidationMessage(string message)
        {
            ValidationText.Text = message;
            ValidationBorder.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Cache le message de validation
        /// </summary>
        private void HideValidationMessage()
        {
            ValidationBorder.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Bouton Annuler
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// Bouton Sauvegarder
        /// </summary>
        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            // Valider le formulaire
            var (isValid, errorMessage) = ValidateForm();
            
            if (!isValid)
            {
                ShowValidationMessage(errorMessage);
                return;
            }

            HideValidationMessage();

            try
            {
                // Mettre à jour le template
                _mcc.TemplateMarkdown = TemplateTextBox.Text;
                _mcc.LastModified = DateTime.Now;

                // Mettre à jour les métadonnées du MCC
                if (_mcc.Semantic == null)
                {
                    _mcc.Semantic = new SemanticAnalysis();
                }

                _mcc.Semantic.DocType = GetSelectedTag(DocTypeCombo);
                _mcc.Semantic.Audience = GetSelectedTag(AudienceCombo);
                _mcc.Semantic.Tone = GetSelectedTag(ToneCombo);
                _mcc.Semantic.AgeGroup = GetSelectedTag(AgeGroupCombo);

                // Mettre à jour les mots-clés
                _mcc.Keywords = ParseKeywords(KeywordsTextBox.Text);

                // Sauvegarder dans la bibliothèque
                var (success, message) = _mccLibrary.UpdateMCC(_mcc);

                if (!success)
                {
                    ShowValidationMessage($"❌ Erreur lors de la sauvegarde : {message}");
                    return;
                }

                // Succès
                MessageBox.Show(
                    $"✅ Le MCC \"{_mcc.Name}\" a été mis à jour avec succès !",
                    "MCC mis à jour",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                ShowValidationMessage($"❌ Erreur inattendue : {ex.Message}");
            }
        }
    }
}
