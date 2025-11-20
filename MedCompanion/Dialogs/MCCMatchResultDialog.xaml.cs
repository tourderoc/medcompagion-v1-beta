using System;
using System.Windows;
using MedCompanion.Models;
using MedCompanion.Services;

namespace MedCompanion.Dialogs
{
    public partial class MCCMatchResultDialog : Window
    {
        private readonly MCCModel _mccModel;
        private readonly double _matchScore;
        private readonly LetterAnalysisResult _analysisResult;

        public MCCMatchResultDialog(
            MCCModel mccModel,
            double matchScore,
            LetterAnalysisResult analysisResult)
        {
            InitializeComponent();

            _mccModel = mccModel ?? throw new ArgumentNullException(nameof(mccModel));
            _matchScore = matchScore;
            _analysisResult = analysisResult ?? throw new ArgumentNullException(nameof(analysisResult));

            LoadMCCData();
        }

        /// <summary>
        /// Charge et affiche les données du MCC
        /// </summary>
        private void LoadMCCData()
        {
            // ===== SECTION 1 : DONNÉES EXTRAITES DE LA DEMANDE =====
            if (_analysisResult != null)
            {
                // Type demandé
                RequestedDocTypeText.Text = FormatDocType(_analysisResult.DocType);
                
                // Audience détectée
                RequestedAudienceText.Text = FormatAudience(_analysisResult.Audience);
                
                // Ton souhaité
                RequestedToneText.Text = FormatTone(_analysisResult.Tone);
                
                // Âge patient
                RequestedAgeGroupText.Text = FormatAgeGroup(_analysisResult.AgeGroup);
                
                // Mots-clés extraits
                if (_analysisResult.Keywords != null && _analysisResult.Keywords.Count > 0)
                {
                    ExtractedKeywordsText.Text = string.Join(", ", _analysisResult.Keywords);
                }
                else
                {
                    ExtractedKeywordsText.Text = "Aucun mot-clé extrait";
                }
                
                // Confiance de l'analyse
                ConfidenceProgressBar.Value = _analysisResult.ConfidenceScore;
                ConfidenceText.Text = $"{_analysisResult.ConfidenceScore:F0}%";
                
                // Coloriser la confiance
                if (_analysisResult.ConfidenceScore >= 80)
                {
                    ConfidenceText.Foreground = System.Windows.Media.Brushes.Green;
                }
                else if (_analysisResult.ConfidenceScore >= 60)
                {
                    ConfidenceText.Foreground = System.Windows.Media.Brushes.Orange;
                }
                else
                {
                    ConfidenceText.Foreground = System.Windows.Media.Brushes.Red;
                }
            }
            
            // ===== SECTION 2 : DONNÉES DU MCC PROPOSÉ =====
            // Nom du MCC
            MCCNameText.Text = _mccModel.Name;

            // Score de pertinence
            ScoreProgressBar.Value = _matchScore;
            ScoreText.Text = $"{_matchScore:F1}%";

            // Coloriser le score
            if (_matchScore >= 85)
            {
                ScoreText.Foreground = System.Windows.Media.Brushes.Green;
                ScoreProgressBar.Foreground = System.Windows.Media.Brushes.Green;
            }
            else if (_matchScore >= 70)
            {
                ScoreText.Foreground = System.Windows.Media.Brushes.Orange;
                ScoreProgressBar.Foreground = System.Windows.Media.Brushes.Orange;
            }

            // Métadonnées sémantiques
            if (_mccModel.Semantic != null)
            {
                DocTypeText.Text = FormatDocType(_mccModel.Semantic.DocType);
                AudienceText.Text = FormatAudience(_mccModel.Semantic.Audience);
                ToneText.Text = FormatTone(_mccModel.Semantic.Tone);
                AgeGroupText.Text = FormatAgeGroup(_mccModel.Semantic.AgeGroup);
            }
            else
            {
                DocTypeText.Text = "Non défini";
                AudienceText.Text = "Non défini";
                ToneText.Text = "Non défini";
                AgeGroupText.Text = "Non défini";
            }

            // Statistiques d'utilisation
            UsageText.Text = _mccModel.UsageCount > 0 
                ? $"{_mccModel.UsageCount} fois" 
                : "Nouveau modèle";

            // Note moyenne
            if (_mccModel.TotalRatings > 0)
            {
                var stars = new string('⭐', (int)Math.Round(_mccModel.AverageRating));
                RatingText.Text = $"{stars} {_mccModel.AverageRating:F1}/5 ({_mccModel.TotalRatings} avis)";
            }
            else
            {
                RatingText.Text = "Pas encore noté";
            }
        }

        /// <summary>
        /// Formatage convivial du type de document
        /// </summary>
        private string FormatDocType(string docType)
        {
            return docType switch
            {
                "administrative_letter" => "Courrier administratif",
                "school_letter" => "Courrier établissement scolaire",
                "specialist_referral" => "Courrier confrère spécialiste",
                "certificate" => "Certificat médical",
                "report" => "Compte-rendu médical",
                "prescription_explanation" => "Explication d'ordonnance",
                _ => docType
            };
        }

        /// <summary>
        /// Formatage convivial de l'audience
        /// </summary>
        private string FormatAudience(string audience)
        {
            return audience switch
            {
                "school" => "École/établissement scolaire",
                "specialist" => "Médecin spécialiste",
                "administration" => "Administration/institution",
                "family" => "Famille/patient",
                "insurance" => "Assurance/mutuelle",
                _ => audience
            };
        }

        /// <summary>
        /// Formatage convivial du ton
        /// </summary>
        private string FormatTone(string tone)
        {
            return tone switch
            {
                "formal" => "Professionnel formel",
                "medical" => "Technique médical",
                "accessible" => "Accessible au patient",
                "administrative" => "Administratif officiel",
                _ => tone
            };
        }

        /// <summary>
        /// Formatage convivial de la tranche d'âge
        /// </summary>
        private string FormatAgeGroup(string ageGroup)
        {
            return ageGroup switch
            {
                "child" => "Enfant (0-12 ans)",
                "adolescent" => "Adolescent (13-17 ans)",
                "adult" => "Adulte (18-64 ans)",
                "elderly" => "Senior (65+ ans)",
                "all" => "Tous âges",
                _ => ageGroup
            };
        }

        /// <summary>
        /// Retour au dialogue précédent
        /// </summary>
        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        /// <summary>
        /// Confirmation et génération du courrier
        /// </summary>
        private void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
