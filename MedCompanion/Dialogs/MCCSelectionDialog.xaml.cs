using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MedCompanion.Models;
using MedCompanion.Services;

namespace MedCompanion.Dialogs
{
    /// <summary>
    /// Dialogue de sélection parmi les 3 meilleurs MCCs
    /// </summary>
    public partial class MCCSelectionDialog : Window
    {
        public MCCModel SelectedMCC { get; private set; }
        private readonly LetterAnalysisResult _analysis;

        public MCCSelectionDialog(
            List<MCCWithScore> topMatches,
            LetterAnalysisResult analysis)
        {
            InitializeComponent();

            if (topMatches == null || !topMatches.Any())
            {
                throw new ArgumentException("La liste des MCCs ne peut pas être vide");
            }

            _analysis = analysis;

            // Afficher les informations d'analyse
            if (analysis != null)
            {
                AnalysisInfoText.Text = $"📋 Demande : {analysis.DocType} • {analysis.Audience} • {analysis.Tone}";
            }

            SubtitleText.Text = $"{topMatches.Count} modèle(s) trouvé(s) - Cliquez pour sélectionner";

            // Construire les cartes pour chaque MCC
            BuildMCCCards(topMatches);
        }

        /// <summary>
        /// Construit les cartes visuelles pour chaque MCC
        /// </summary>
        private void BuildMCCCards(List<MCCWithScore> topMatches)
        {
            for (int i = 0; i < topMatches.Count; i++)
            {
                var matchItem = topMatches[i];
                var card = CreateMCCCard(matchItem, i + 1);
                MCCCardsPanel.Children.Add(card);
            }
        }

        /// <summary>
        /// Crée une carte visuelle pour un MCC
        /// </summary>
        private Border CreateMCCCard(MCCWithScore matchItem, int rank)
        {
            var mcc = matchItem.MCC;
            var scorePercent = matchItem.NormalizedScore;

            // Container principal
            var card = new Border
            {
                Style = (Style)FindResource("MCCCardStyle"),
                Tag = mcc // Stocker le MCC dans le Tag
            };

            // Ajouter l'événement de clic
            card.MouseLeftButtonDown += (s, e) =>
            {
                SelectedMCC = mcc;
                DialogResult = true;
                Close();
            };

            // Contenu de la carte
            var mainStack = new StackPanel();

            // En-tête : Rang + Nom + Score
            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Colonne gauche : Rang + Nom
            var leftStack = new StackPanel { Orientation = Orientation.Horizontal };

            // Badge de rang
            var rankBadge = new Border
            {
                Background = GetRankColor(rank),
                CornerRadius = new CornerRadius(15),
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 10, 0)
            };
            rankBadge.Child = new TextBlock
            {
                Text = GetRankEmoji(rank),
                FontSize = 18,
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold
            };
            leftStack.Children.Add(rankBadge);

            // Nom du MCC
            var nameText = new TextBlock
            {
                Text = mcc.Name,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                VerticalAlignment = VerticalAlignment.Center
            };
            leftStack.Children.Add(nameText);

            Grid.SetColumn(leftStack, 0);
            headerGrid.Children.Add(leftStack);

            // Colonne droite : Score
            var scoreBadge = new Border
            {
                Style = (Style)FindResource("ScoreBadgeStyle"),
                Background = GetScoreColor(scorePercent)
            };
            scoreBadge.Child = new TextBlock
            {
                Text = $"{scorePercent:F0}%",
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            Grid.SetColumn(scoreBadge, 1);
            headerGrid.Children.Add(scoreBadge);

            mainStack.Children.Add(headerGrid);

            // Métadonnées du MCC
            if (mcc.Semantic != null)
            {
                var metaStack = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 10, 0, 0)
                };

                AddMetaBadge(metaStack, "📄", mcc.Semantic.DocType);
                AddMetaBadge(metaStack, "👥", mcc.Semantic.Audience);
                AddMetaBadge(metaStack, "🎵", mcc.Semantic.Tone);
                if (!string.IsNullOrEmpty(mcc.Semantic.AgeGroup))
                {
                    AddMetaBadge(metaStack, "👶", mcc.Semantic.AgeGroup);
                }

                mainStack.Children.Add(metaStack);
            }

            // Top 3 des critères de scoring
            if (matchItem.ScoreBreakdown != null && matchItem.ScoreBreakdown.Any())
            {
                var scoringText = new TextBlock
                {
                    Text = "📊 Meilleurs critères de matching :",
                    FontSize = 12,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#666")),
                    Margin = new Thickness(0, 10, 0, 5)
                };
                mainStack.Children.Add(scoringText);

                var criteriaStack = new StackPanel { Margin = new Thickness(15, 0, 0, 0) };
                var topCriteria = matchItem.ScoreBreakdown
                    .OrderByDescending(x => x.Value)
                    .Take(3);

                foreach (var (criterion, points) in topCriteria)
                {
                    var criterionText = new TextBlock
                    {
                        Text = $"• {criterion}: {points:F0} pts",
                        FontSize = 11,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#777")),
                        Margin = new Thickness(0, 2, 0, 0)
                    };
                    criteriaStack.Children.Add(criterionText);
                }

                mainStack.Children.Add(criteriaStack);
            }

            // Statistiques d'utilisation
            var statsStack = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 10, 0, 0)
            };

            if (mcc.AverageRating > 0)
            {
                var ratingText = new TextBlock
                {
                    Text = $"⭐ {mcc.AverageRating:F1}/5 ({mcc.TotalRatings} avis)",
                    FontSize = 11,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#666")),
                    Margin = new Thickness(0, 0, 15, 0)
                };
                statsStack.Children.Add(ratingText);
            }

            var usageText = new TextBlock
            {
                Text = $"📈 Utilisé {mcc.UsageCount} fois",
                FontSize = 11,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#666"))
            };
            statsStack.Children.Add(usageText);

            mainStack.Children.Add(statsStack);

            card.Child = mainStack;
            return card;
        }

        /// <summary>
        /// Ajoute un badge de métadonnée
        /// </summary>
        private void AddMetaBadge(StackPanel parent, string emoji, string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            var badge = new Border
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0F0F0")),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(10, 5, 10, 5),
                Margin = new Thickness(0, 0, 8, 0)
            };

            var badgeText = new TextBlock
            {
                Text = $"{emoji} {text}",
                FontSize = 11,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#555"))
            };

            badge.Child = badgeText;
            parent.Children.Add(badge);
        }

        /// <summary>
        /// Retourne la couleur selon le rang
        /// </summary>
        private Brush GetRankColor(int rank)
        {
            return rank switch
            {
                1 => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD700")), // Or
                2 => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C0C0C0")), // Argent
                3 => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CD7F32")), // Bronze
                _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#999999"))
            };
        }

        /// <summary>
        /// Retourne l'emoji selon le rang
        /// </summary>
        private string GetRankEmoji(int rank)
        {
            return rank switch
            {
                1 => "🥇",
                2 => "🥈",
                3 => "🥉",
                _ => $"#{rank}"
            };
        }

        /// <summary>
        /// Retourne la couleur selon le score
        /// </summary>
        private Brush GetScoreColor(double scorePercent)
        {
            if (scorePercent >= 80)
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4CAF50")); // Vert
            else if (scorePercent >= 60)
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF9800")); // Orange
            else
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F44336")); // Rouge
        }

        /// <summary>
        /// Gestion du bouton Annuler
        /// </summary>
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            SelectedMCC = null;
            DialogResult = false;
            Close();
        }
    }
}
