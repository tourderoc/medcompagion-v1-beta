using System.Windows;
using MedCompanion.Services.Consultation;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Dialogue d'édition du vocabulaire personnalisé Whisper.
    /// </summary>
    public partial class WhisperVocabDialog : Window
    {
        private readonly WhisperVocabService _vocabService;

        public WhisperVocabDialog(WhisperVocabService vocabService)
        {
            InitializeComponent();
            _vocabService = vocabService;

            // Charger le contenu actuel
            _vocabService.Load();
            VocabEditor.Text = _vocabService.RawContent;
            UpdateStats();
        }

        private void UpdateStats()
        {
            var lines = VocabEditor.Text.Split('\n');
            var entryCount = 0;
            var sectionCount = 0;
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#')) continue;
                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    sectionCount++;
                    continue;
                }
                entryCount++;
            }
            StatsText.Text = $"📊 {entryCount} termes dans {sectionCount} sections";
        }

        private void SaveBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _vocabService.Save(VocabEditor.Text);
                MessageBox.Show(
                    $"Vocabulaire sauvegardé : {_vocabService.Count} termes.\n\n" +
                    "Les changements seront appliqués au prochain démarrage de la dictée.",
                    "Vocabulaire sauvegardé",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (System.Exception ex)
            {
                MessageBox.Show(
                    $"Erreur lors de la sauvegarde : {ex.Message}",
                    "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ResetBtn_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Revenir au vocabulaire par défaut ?\nVos modifications personnelles seront perdues.",
                "Réinitialiser le vocabulaire",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                _vocabService.ResetToDefaults();
                VocabEditor.Text = _vocabService.RawContent;
                UpdateStats();
            }
        }
    }
}
