using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MedCompanion.Models;

namespace MedCompanion.Dialogs
{
    public partial class RateLetterDialog : Window
    {
        private int _selectedRating = 0;
        private readonly Button[] _starButtons;

        public LetterRating? Rating { get; private set; }

        public RateLetterDialog(string letterPath, string? mccId = null, string? mccName = null)
        {
            InitializeComponent();

            _starButtons = new[] { Star1Button, Star2Button, Star3Button, Star4Button, Star5Button };

            // Initialiser l'objet rating
            Rating = new LetterRating
            {
                LetterPath = letterPath,
                MCCId = mccId,
                MCCName = mccName
            };

            // Si une évaluation existe déjà, la charger
            // (sera géré par le code appelant qui peut pré-remplir)
        }

        /// <summary>
        /// Pré-remplit le dialogue avec une évaluation existante
        /// </summary>
        public void LoadExistingRating(LetterRating existingRating)
        {
            if (existingRating != null)
            {
                _selectedRating = existingRating.Rating;
                CommentTextBox.Text = existingRating.Comment ?? string.Empty;
                UpdateStarDisplay();
                ValidateButton.IsEnabled = true;
                
                Title = "⭐ Modifier l'évaluation";
            }
        }

        /// <summary>
        /// Gestion du clic sur une étoile
        /// </summary>
        private void StarButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string tagStr)
            {
                if (int.TryParse(tagStr, out int rating))
                {
                    _selectedRating = rating;
                    UpdateStarDisplay();
                    ValidateButton.IsEnabled = true;
                }
            }
        }

        /// <summary>
        /// Met à jour l'affichage des étoiles
        /// </summary>
        private void UpdateStarDisplay()
        {
            for (int i = 0; i < _starButtons.Length; i++)
            {
                if (i < _selectedRating)
                {
                    // Étoile pleine
                    _starButtons[i].Content = "★";
                    _starButtons[i].Foreground = new SolidColorBrush(Color.FromRgb(255, 193, 7)); // Orange doré
                }
                else
                {
                    // Étoile vide
                    _starButtons[i].Content = "☆";
                    _starButtons[i].Foreground = new SolidColorBrush(Color.FromRgb(200, 200, 200)); // Gris
                }
            }

            // Mettre à jour le texte d'indication
            RatingHintText.Text = GetRatingHintText(_selectedRating);
            RatingHintText.Foreground = new SolidColorBrush(GetRatingColor(_selectedRating));
        }

        /// <summary>
        /// Retourne le texte d'indication selon la note
        /// </summary>
        private string GetRatingHintText(int rating)
        {
            return rating switch
            {
                1 => "⚠️ Très insuffisant",
                2 => "⚠️ Insuffisant",
                3 => "🔧 Acceptable",
                4 => "✅ Bon",
                5 => "⭐ Excellent",
                _ => "Cliquez sur une étoile pour noter"
            };
        }

        /// <summary>
        /// Retourne la couleur selon la note
        /// </summary>
        private Color GetRatingColor(int rating)
        {
            return rating switch
            {
                1 or 2 => Color.FromRgb(244, 67, 54),  // Rouge
                3 => Color.FromRgb(255, 152, 0),        // Orange
                4 or 5 => Color.FromRgb(76, 175, 80),   // Vert
                _ => Color.FromRgb(153, 153, 153)       // Gris
            };
        }

        /// <summary>
        /// Validation de l'évaluation
        /// </summary>
        private void ValidateButton_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedRating == 0)
            {
                MessageBox.Show(
                    "Veuillez sélectionner une note.",
                    "Note requise",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning
                );
                return;
            }

            // Mettre à jour l'objet Rating
            if (Rating != null)
            {
                Rating.Rating = _selectedRating;
                Rating.Comment = string.IsNullOrWhiteSpace(CommentTextBox.Text) 
                    ? null 
                    : CommentTextBox.Text.Trim();
            }

            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Annulation
        /// </summary>
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
