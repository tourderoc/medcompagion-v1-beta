using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MedCompanion.Models;
using MedCompanion.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MedCompanion.Dialogs;

public partial class MDPHAssistantDialog : Window
{
    // Services injectés
    private readonly PatientIndexEntry _selectedPatient;
    private readonly PatientIndexService _patientIndex;
    private readonly FormulaireAssistantService _formulaireService;
    private readonly LetterService _letterService;
    private readonly SynthesisWeightTracker _synthesisWeightTracker;
    private readonly PathService _pathService = new PathService();

    // WebView2 pour afficher le PDF
    private WebView2? _webView;
    private bool _webViewInitialized = false;
    private string _pdfPath = "";
    private int _currentZoom = 100;

    // Stockage des sections générées
    private readonly Dictionary<int, string> _generatedSections = new();
    private readonly Dictionary<int, TextBox> _sectionTextBoxes = new();
    private readonly Dictionary<int, Button> _regenerateButtons = new();

    // Section "À joindre à ce document"
    private readonly Dictionary<string, CheckBox> _ajouterCheckboxes = new();
    private TextBox? _autresDemandesTextBox;

    // État de génération
    private bool _isGenerating = false;
    private bool _hasUnsavedChanges = false;
    private MDPHFormData? _generatedFormData = null; // Stockage des données générées pour remplissage PDF

    public MDPHAssistantDialog(
        PatientIndexEntry selectedPatient,
        PatientIndexService patientIndex,
        FormulaireAssistantService formulaireService,
        LetterService letterService,
        SynthesisWeightTracker synthesisWeightTracker)
    {
        InitializeComponent();

        _selectedPatient = selectedPatient;
        _patientIndex = patientIndex;
        _formulaireService = formulaireService;
        _letterService = letterService;
        _synthesisWeightTracker = synthesisWeightTracker;

        // Configuration de la fenêtre
        Loaded += MDPHAssistantDialog_Loaded;
        Closing += MDPHAssistantDialog_Closing;
    }

    private async void MDPHAssistantDialog_Loaded(object sender, RoutedEventArgs e)
    {
        // Afficher les informations du patient
        var metadata = _patientIndex.GetMetadata(_selectedPatient.Id);
        if (metadata != null)
        {
            PatientPrenomText.Text = metadata.Prenom ?? "Non renseigné";
            PatientNomText.Text = metadata.Nom ?? "Non renseigné";

            // Format de la date de naissance : dd/MM/yyyy
            if (!string.IsNullOrEmpty(metadata.Dob) && DateTime.TryParse(metadata.Dob, out var dob))
            {
                PatientDobText.Text = dob.ToString("dd/MM/yyyy");
            }
            else
            {
                PatientDobText.Text = "Non renseignée";
            }

            // Adresse complète
            var adresseParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(metadata.AdresseRue))
                adresseParts.Add(metadata.AdresseRue);
            if (!string.IsNullOrWhiteSpace(metadata.AdresseCodePostal) || !string.IsNullOrWhiteSpace(metadata.AdresseVille))
                adresseParts.Add($"{metadata.AdresseCodePostal} {metadata.AdresseVille}".Trim());

            if (adresseParts.Count > 0)
            {
                PatientAdresseText.Text = string.Join(", ", adresseParts);
            }
            else
            {
                PatientAdresseText.Text = "Non renseignée";
                PatientAdresseText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00)); // Orange pour signaler
            }

            // Numéro de sécurité sociale
            if (!string.IsNullOrWhiteSpace(metadata.NumeroSecuriteSociale))
            {
                // Formater le NIR avec espaces : X XX XX XX XXX XXX XX
                var nir = metadata.NumeroSecuriteSociale;
                if (nir.Length >= 13)
                {
                    PatientNumSecuText.Text = $"{nir.Substring(0, 1)} {nir.Substring(1, 2)} {nir.Substring(3, 2)} {nir.Substring(5, 2)} {nir.Substring(7, 3)} {nir.Substring(10, 3)}" +
                        (nir.Length >= 15 ? $" {nir.Substring(13, 2)}" : "");
                }
                else
                {
                    PatientNumSecuText.Text = nir;
                }
            }
            else
            {
                PatientNumSecuText.Text = "Non renseigné";
                PatientNumSecuText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00)); // Orange pour signaler
            }
        }
        else
        {
            PatientPrenomText.Text = _selectedPatient.Prenom;
            PatientNomText.Text = _selectedPatient.Nom;
            PatientDobText.Text = "Non renseignée";
            PatientAdresseText.Text = "Non renseignée";
            PatientAdresseText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00));
            PatientNumSecuText.Text = "Non renseigné";
            PatientNumSecuText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xA5, 0x00));
        }

        // Créer les sections (vides pour l'instant)
        CreateSectionUI();

        // Initialiser WebView2
        await InitializeWebView2Async();

        // ✅ NE PLUS lancer automatiquement - attendre que l'utilisateur clique sur le bouton
        // await GenerateAllSectionsAsync();
    }

    /// <summary>
    /// Crée l'interface utilisateur pour les 11 sections MDPH.
    /// </summary>
    private void CreateSectionUI()
    {
        // Ajouter la section "À joindre à ce document" EN PREMIER (en haut)
        CreateAjouterSectionUI();

        for (int i = 0; i < MDPHPageMapping.TotalSections; i++)
        {
            int sectionIndex = i; // Capture pour les closures

            // Créer l'Expander
            var expander = new Expander
            {
                Header = MDPHPageMapping.GetSectionTitle(sectionIndex),
                IsExpanded = (sectionIndex == 0), // Première section ouverte par défaut
                Margin = new Thickness(0, 0, 0, 10),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
                BorderThickness = new Thickness(1),
                Background = Brushes.White
            };

            // Contenu de l'Expander
            var contentPanel = new StackPanel { Margin = new Thickness(10) };

            // TextBox pour le contenu de la section
            var textBox = new TextBox
            {
                Text = "⏸ En attente de génération...",
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MinHeight = 40,
                MaxHeight = 600,
                Padding = new Thickness(8),
                FontSize = 13,
                IsReadOnly = false,
                Margin = new Thickness(0, 0, 0, 10),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
                Foreground = new SolidColorBrush(Color.FromRgb(0x95, 0xA5, 0xA6))
            };
            textBox.TextChanged += (s, e) =>
            {
                _hasUnsavedChanges = true;
                AdjustTextBoxHeight(textBox);
            };
            _sectionTextBoxes[sectionIndex] = textBox;
            contentPanel.Children.Add(textBox);

            // Panel de boutons
            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal };

            // Bouton "Copier"
            var copyButton = new Button
            {
                Content = "📋 Copier",
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 10, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x34, 0x98, 0xDB)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                IsEnabled = false
            };
            copyButton.Click += (s, e) => CopyToClipboard(sectionIndex);
            buttonPanel.Children.Add(copyButton);

            // ❌ DÉSACTIVÉ : Boutons "Régénérer" supprimés (architecture old MDPH commentée)
            /*
            // Bouton "Régénérer" (ou "Générer" pour la section Remarques)
            var regenerateButton = new Button
            {
                Content = (sectionIndex == 18) ? "📝 Générer les remarques" : "🔄 Régénérer",
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 10, 0),
                Background = (sectionIndex == 18)
                    ? new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60))  // Vert pour "Générer"
                    : new SolidColorBrush(Color.FromRgb(0xE6, 0x7E, 0x22)), // Orange pour "Régénérer"
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                IsEnabled = (sectionIndex == 18) // Section 18 : bouton activé immédiatement
            };

            // Pour la section 18 (Remarques), appel spécial avec demandes cochées
            if (sectionIndex == 18)
            {
                regenerateButton.Click += async (s, e) => await GenerateRemarquesSectionAsync();
            }
            else
            {
                regenerateButton.Click += async (s, e) => await RegenerateSectionAsync(sectionIndex);
            }

            _regenerateButtons[sectionIndex] = regenerateButton;
            buttonPanel.Children.Add(regenerateButton);
            */

            contentPanel.Children.Add(buttonPanel);
            expander.Content = contentPanel;

            SectionsPanel.Children.Add(expander);
        }
    }

    /// <summary>
    /// Crée la section "À joindre à ce document" avec checkboxes et champ libre.
    /// </summary>
    private void CreateAjouterSectionUI()
    {
        // Créer l'Expander
        var expander = new Expander
        {
            Header = "📎 À joindre à ce document",
            IsExpanded = false,
            Margin = new Thickness(0, 0, 0, 10),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60)),
            BorderThickness = new Thickness(2),
            Background = new SolidColorBrush(Color.FromRgb(0xE8, 0xF8, 0xF5))
        };

        // Contenu de l'Expander
        var contentPanel = new StackPanel { Margin = new Thickness(10) };

        // Texte d'instruction
        var instructionText = new TextBlock
        {
            Text = "Cochez les demandes que vous souhaitez joindre au dossier MDPH :",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap
        };
        contentPanel.Children.Add(instructionText);

        // Liste des demandes fréquentes
        var demandesList = new[] {
            ("AESH", "Accompagnant d'Élève en Situation de Handicap"),
            ("AEEH", "Allocation d'Éducation de l'Enfant Handicapé"),
            ("PCH", "Prestation de Compensation du Handicap"),
            ("MPA", "Matériel Pédagogique Adapté"),
            ("RQTH", "Reconnaissance de la Qualité de Travailleur Handicapé"),
            ("Aménagements scolaires", "Aménagements scolaires")
        };

        // Créer les CheckBox
        foreach (var (key, label) in demandesList)
        {
            var checkbox = new CheckBox
            {
                Content = label,
                FontSize = 13,
                Margin = new Thickness(0, 5, 0, 5),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            checkbox.Checked += (s, e) => _hasUnsavedChanges = true;
            checkbox.Unchecked += (s, e) => _hasUnsavedChanges = true;
            _ajouterCheckboxes[key] = checkbox;
            contentPanel.Children.Add(checkbox);
        }

        // Séparateur
        var separator = new Border
        {
            Height = 1,
            Background = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            Margin = new Thickness(0, 10, 0, 10)
        };
        contentPanel.Children.Add(separator);

        // Champ "Autres demandes"
        var autresLabel = new TextBlock
        {
            Text = "Autres demandes :",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 5)
        };
        contentPanel.Children.Add(autresLabel);

        _autresDemandesTextBox = new TextBox
        {
            Text = "",
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MinHeight = 60,
            MaxHeight = 150,
            Padding = new Thickness(8),
            FontSize = 13,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xDD, 0xDD)),
            Margin = new Thickness(0, 0, 0, 10)
        };
        _autresDemandesTextBox.TextChanged += (s, e) => _hasUnsavedChanges = true;
        contentPanel.Children.Add(_autresDemandesTextBox);

        // Bouton "Copier la liste"
        var copyListeButton = new Button
        {
            Content = "📋 Copier la liste",
            Padding = new Thickness(12, 6, 12, 6),
            Background = new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 5, 0, 0)
        };
        copyListeButton.Click += CopyAjouterListeButton_Click;
        contentPanel.Children.Add(copyListeButton);

        expander.Content = contentPanel;
        SectionsPanel.Children.Add(expander);
    }

    /// <summary>
    /// Initialise WebView2 et charge le PDF MDPH.
    /// </summary>
    private async Task InitializeWebView2Async()
    {
        try
        {
            // Trouver le template PDF MDPH dans Assets
            var assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Formulaires");
            
            // ✅ UTILISER UNIQUEMENT LE TEMPLATE 8 PAGES
            var templatePath = Path.Combine(assetsPath, "MDPH_Template_8pages.pdf");
            
            // Log pour debug
            System.Diagnostics.Debug.WriteLine($"[MDPHAssistantDialog] Template path: {templatePath}");
            System.Diagnostics.Debug.WriteLine($"[MDPHAssistantDialog] Exists: {File.Exists(templatePath)}");

            if (string.IsNullOrEmpty(templatePath))
            {
                PdfFallbackMessage.Text = "❌ PDF MDPH introuvable dans Assets/Formulaires";
                PdfFallbackMessage.Foreground = Brushes.Red;
                return;
            }

            // Créer une copie du template dans le dossier patient
            var formulairesDir = _pathService.GetFormulairesDirectory(_selectedPatient.NomComplet);
            Directory.CreateDirectory(formulairesDir);

            // Générer le nom du fichier avec timestamp (millisecondes pour unicité)
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
            var pdfFileName = $"MDPH_{timestamp}.pdf";
            _pdfPath = Path.Combine(formulairesDir, pdfFileName);

            // Copier le template vers le dossier patient
            File.Copy(templatePath, _pdfPath, overwrite: true);
            System.Diagnostics.Debug.WriteLine($"[MDPHAssistantDialog] PDF copié vers: {_pdfPath}");

            // 🟢 PATCH: Activer le mode Multi-ligne sur tous les champs
            var filler = new PDFFormFillerService();
            filler.EnableMultilineOnAllFields(_pdfPath);

            // Créer WebView2
            _webView = new WebView2();
            PdfViewerContainer.Children.Add(_webView);

            // Initialiser WebView2
            var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Path.GetTempPath(), "MedCompanion_WebView2"));
            await _webView.EnsureCoreWebView2Async(env);

            _webViewInitialized = true;

            // Charger le PDF
            _webView.CoreWebView2.Navigate(_pdfPath);

            // Masquer le message de fallback
            PdfFallbackMessage.Visibility = Visibility.Collapsed;

            // Activer les boutons de navigation
            PdfPreviousPageButton.IsEnabled = true;
            PdfNextPageButton.IsEnabled = true;
            PdfZoomInButton.IsEnabled = true;
            PdfZoomOutButton.IsEnabled = true;

            // Événement de navigation terminée
            _webView.CoreWebView2.NavigationCompleted += (s, e) =>
            {
                if (e.IsSuccess)
                {
                    UpdatePdfPageIndicator();
                }
            };
        }
        catch (Exception ex)
        {
            PdfFallbackMessage.Text = $"⚠ Erreur WebView2 : {ex.Message}\n\nLe PDF s'ouvrira dans l'application externe.";
            PdfFallbackMessage.Foreground = Brushes.Orange;

            // Désactiver les boutons
            PdfPreviousPageButton.IsEnabled = false;
            PdfNextPageButton.IsEnabled = false;
            PdfZoomInButton.IsEnabled = false;
            PdfZoomOutButton.IsEnabled = false;
        }
    }

    /// <summary>
    /// Handler du bouton "Commencer la génération"
    /// </summary>
    private async void StartGenerationButton_Click(object sender, RoutedEventArgs e)
    {
        // Masquer le bouton et afficher la barre de progression
        StartGenerationButton.Visibility = Visibility.Collapsed;
        GenerationProgressBar.Visibility = Visibility.Visible;

        // Lancer la génération
        await GenerateAllSectionsAsync();
    }

    /// <summary>
    /// Génère toutes les sections MDPH avec l'IA en UN SEUL appel.
    /// NOUVELLE ARCHITECTURE : 1 prompt → 19 sections en JSON → Parsing
    /// </summary>
    private async Task GenerateAllSectionsAsync()
    {
        _isGenerating = true;
        StatusText.Text = "⏳ Génération du formulaire complet en cours...";
        GenerationProgressBar.Value = 0;

        try
        {
            // 1. Construire la liste des demandes cochées
            var demandesList = new List<string>();
            foreach (var kvp in _ajouterCheckboxes)
            {
                if (kvp.Value.IsChecked == true)
                {
                    demandesList.Add(kvp.Value.Content?.ToString() ?? "");
                }
            }

            // Ajouter les autres demandes (texte libre)
            string autresDemandes = _autresDemandesTextBox?.Text ?? "";
            if (!string.IsNullOrWhiteSpace(autresDemandes))
            {
                demandesList.Add($"Autres demandes : {autresDemandes}");
            }

            string demandes = string.Join("\n- ", demandesList);
            if (!string.IsNullOrWhiteSpace(demandes))
            {
                demandes = "- " + demandes; // Ajouter le premier tiret
            }

            // Vérifier qu'au moins une demande est cochée
            if (string.IsNullOrWhiteSpace(demandes))
            {
                var result = MessageBox.Show(
                    "Aucune demande n'a été cochée dans la section 'À joindre à ce document'.\n\n" +
                    "Voulez-vous continuer quand même ?",
                    "Demandes manquantes",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    return;
                }

                demandes = "Aucune demande spécifique";
            }

            StatusText.Text = "⏳ Appel au LLM pour générer toutes les sections...";
            GenerationProgressBar.Value = 10;

            // 2. Appel unique au service (18x plus rapide !)
            var formData = await _formulaireService.GenerateCompleteFormAsync(
                _selectedPatient.NomComplet,
                demandes
            );

            // Stocker les données pour le remplissage PDF ultérieur
            _generatedFormData = formData;

            StatusText.Text = "⏳ Remplissage des sections...";
            GenerationProgressBar.Value = 50;

            // 3. Remplir les TextBoxes avec les données parsées
            FillSectionFromFormData(0, formData.PathologiePrincipale);
            FillSectionFromFormData(1, formData.AutresPathologies);
            FillSectionFromFormData(2, string.Join("\n", formData.ElementsEssentiels.Select(e => $"- {e}")));
            FillSectionFromFormData(3, string.Join("\n", formData.AntecedentsMedicaux.Select(a => $"- {a}")));
            FillSectionFromFormData(4, string.Join("\n", formData.RetardsDeveloppementaux.Select(r => $"- {r}")));

            // Description clinique (3 lignes)
            for (int i = 0; i < Math.Min(3, formData.DescriptionClinique.Count); i++)
            {
                FillSectionFromFormData(5 + i, formData.DescriptionClinique[i]);
            }

            // Traitements
            FillSectionFromFormData(8, formData.Traitements.Medicaments);
            FillSectionFromFormData(9, formData.Traitements.EffetsIndesirables);
            FillSectionFromFormData(10, formData.Traitements.AutresPrisesEnCharge);

            // Retentissements
            FillSectionFromFormData(11, formData.Retentissements.Mobilite);
            FillSectionFromFormData(12, formData.Retentissements.Communication);
            FillSectionFromFormData(13, string.Join("\n", formData.Retentissements.Cognition.Select(c => $"- {c}")));
            FillSectionFromFormData(14, string.Join("\n", formData.Retentissements.ConduiteEmotionnelle.Select(c => $"- {c}")));
            FillSectionFromFormData(15, formData.Retentissements.Autonomie);
            FillSectionFromFormData(16, formData.Retentissements.VieQuotidienne);
            FillSectionFromFormData(17, formData.Retentissements.SocialScolaire);

            // Remarques complémentaires
            FillSectionFromFormData(18, formData.RemarquesComplementaires);

            StatusText.Text = "✅ Formulaire complet généré avec succès ! (1 appel LLM au lieu de 19)";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60));
            GenerationProgressBar.Value = 100;

            SaveDocxButton.Content = "💾 Sauvegarder et Terminer";
            SaveDocxButton.IsEnabled = true;
            FillPdfButton.IsEnabled = true; // Activer le bouton de remplissage PDF
            _hasUnsavedChanges = false; // Reset car c'est la génération initiale
        }
        catch (Exception ex)
        {
            StatusText.Text = $"❌ Erreur lors de la génération : {ex.Message}";
            StatusText.Foreground = Brushes.Red;
            MessageBox.Show(
                $"Une erreur est survenue lors de la génération :\n\n{ex.Message}",
                "Erreur",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isGenerating = false;
        }
    }

    /// <summary>
    /// Remplit une section avec le contenu généré et active les boutons
    /// </summary>
    private void FillSectionFromFormData(int sectionIndex, string content)
    {
        _generatedSections[sectionIndex] = content;

        // Mettre à jour l'UI
        if (_sectionTextBoxes.TryGetValue(sectionIndex, out var textBox))
        {
            textBox.Text = content;
            textBox.IsReadOnly = false;
            textBox.Foreground = Brushes.Black; // Remettre la couleur normale
            AdjustTextBoxHeight(textBox);

            // Activer les boutons
            var copyButton = ((textBox.Parent as StackPanel)?.Children[1] as StackPanel)?.Children[0] as Button;
            if (copyButton != null) copyButton.IsEnabled = true;

            // ❌ DÉSACTIVÉ : Plus de boutons "Régénérer"
            // if (_regenerateButtons.TryGetValue(sectionIndex, out var regenButton))
            //     regenButton.IsEnabled = true;
        }
    }

    /*❌ DÉSACTIVÉ : Méthodes old architecture MDPH (commentées dans FormulaireAssistantService)
    /// <summary>
    /// Génère le contenu d'une section spécifique.
    /// </summary>
    private async Task<string> GenerateSectionContentAsync(int sectionIndex, PatientMetadata metadata)
    {
        // Mapping vers les méthodes du FormulaireAssistantService
        return sectionIndex switch
        {
            0 => await _formulaireService.GeneratePathologieSection(metadata),
            1 => await _formulaireService.GenerateAutresPathologiesSection(metadata),
            2 => await _formulaireService.GenerateElementsEssentielsSection(metadata),
            3 => await _formulaireService.GenerateAntecedentsMedicauxSection(metadata),
            4 => await _formulaireService.GenerateRetardsDeveloppementauxSection(metadata),
            5 => await _formulaireService.GenerateDescriptionClinique1Section(metadata),
            6 => await _formulaireService.GenerateDescriptionClinique2Section(metadata),
            7 => await _formulaireService.GenerateDescriptionClinique3Section(metadata),
            8 => await _formulaireService.GenerateTraitements1Section(metadata),
            9 => await _formulaireService.GenerateTraitements2Section(metadata),
            10 => await _formulaireService.GenerateTraitements3Section(metadata),
            11 => await _formulaireService.GenerateRetentissementMobiliteSection(metadata),
            12 => await _formulaireService.GenerateRetentissementCommunicationSection(metadata),
            13 => await _formulaireService.GenerateRetentissementCognitionSection(metadata),
            14 => await _formulaireService.GenerateConduiteEmotionnelleSection(metadata),
            15 => await _formulaireService.GenerateRetentissementAutonomieSection(metadata),
            16 => await _formulaireService.GenerateRetentissementVieQuotidienneSection(metadata),
            17 => await _formulaireService.GenerateRetentissementSocialScolaireSection(metadata),
            18 => await _formulaireService.GenerateRemarquesComplementairesSection(metadata),
            _ => "Section invalide"
        };
    }*/

    /*❌ DÉSACTIVÉ : Old architecture MDPH (boutons Régénérer supprimés)
    /// <summary>
    /// Régénère une section spécifique.
    /// </summary>
    private async Task RegenerateSectionAsync(int sectionIndex)
    {
        if (_isGenerating) return;

        var button = _regenerateButtons[sectionIndex];
        var originalContent = button.Content;
        button.Content = "⏳ En cours...";
        button.IsEnabled = false;

        try
        {
            var metadata = _patientIndex.GetMetadata(_selectedPatient.Id);
            if (metadata == null) return;

            string newContent = await GenerateSectionContentAsync(sectionIndex, metadata);

            _generatedSections[sectionIndex] = newContent;
            if (_sectionTextBoxes.TryGetValue(sectionIndex, out var textBox))
            {
                textBox.Text = newContent;
                AdjustTextBoxHeight(textBox);
            }

            _hasUnsavedChanges = true;

            // Notification toast
            StatusText.Text = $"✅ Section {sectionIndex + 1} régénérée !";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60));
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Erreur lors de la régénération :\n\n{ex.Message}",
                "Erreur",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            button.Content = originalContent;
            button.IsEnabled = true;
        }
    }*/

    /*❌ DÉSACTIVÉ : Old architecture MDPH (bouton Générer remarques supprimé)
    /// <summary>
    /// Génère la section "Remarques complémentaires" (section 18) avec les demandes cochées.
    /// </summary>
    private async Task GenerateRemarquesSectionAsync()
    {
        const int sectionIndex = 18;

        if (_isGenerating) return;

        var button = _regenerateButtons[sectionIndex];
        var originalContent = button.Content;
        button.Content = "⏳ Génération...";
        button.IsEnabled = false;

        try
        {
            var metadata = _patientIndex.GetMetadata(_selectedPatient.Id);
            if (metadata == null)
            {
                MessageBox.Show(
                    "Impossible de récupérer les métadonnées du patient.",
                    "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Récupérer les demandes cochées
            string demandes = BuildAjouterListeTexte();

            if (string.IsNullOrWhiteSpace(demandes))
            {
                var result = MessageBox.Show(
                    "Aucune demande n'a été cochée dans la section \"À joindre à ce document\".\n\n" +
                    "Voulez-vous générer les remarques complémentaires sans demandes spécifiques ?",
                    "Aucune demande cochée",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.No)
                {
                    return;
                }
            }

            // Générer le contenu avec les demandes
            string newContent = await _formulaireService.GenerateRemarquesComplementairesSection(metadata, demandes);

            _generatedSections[sectionIndex] = newContent;
            if (_sectionTextBoxes.TryGetValue(sectionIndex, out var textBox))
            {
                textBox.Text = newContent;
                AdjustTextBoxHeight(textBox);
            }

            _hasUnsavedChanges = true;

            // Notification toast
            StatusText.Text = "✅ Remarques complémentaires générées avec succès !";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60));
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Erreur lors de la génération des remarques :\n\n{ex.Message}",
                "Erreur",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            button.Content = originalContent;
            button.IsEnabled = true;
        }
    }*/

    /// <summary>
    /// Copie le contenu d'une section dans le presse-papier.
    /// </summary>
    private void CopyToClipboard(int sectionIndex)
    {
        if (!_sectionTextBoxes.TryGetValue(sectionIndex, out var textBox))
            return;

        try
        {
            Clipboard.SetText(textBox.Text);

            // Notification visuelle
            StatusText.Text = $"✅ Section {sectionIndex + 1} copiée dans le presse-papier !";
            StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60));

            // Reset après 3 secondes
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            timer.Tick += (s, e) =>
            {
                if (!_isGenerating)
                {
                    StatusText.Text = "✅ Toutes les sections ont été générées avec succès !";
                }
                timer.Stop();
            };
            timer.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Erreur lors de la copie :\n\n{ex.Message}",
                "Erreur",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Copie le prénom du patient dans le presse-papier.
    /// </summary>
    private void CopyPrenomButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(PatientPrenomText.Text);
            ShowCopyConfirmation("Prénom copié !");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors de la copie : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Copie le nom du patient dans le presse-papier.
    /// </summary>
    private void CopyNomButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(PatientNomText.Text);
            ShowCopyConfirmation("Nom copié !");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors de la copie : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Copie la date de naissance du patient dans le presse-papier.
    /// </summary>
    private void CopyDobButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(PatientDobText.Text);
            ShowCopyConfirmation("Date de naissance copiée !");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors de la copie : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Copie l'adresse du patient dans le presse-papier.
    /// </summary>
    private void CopyAdresseButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (PatientAdresseText.Text != "Non renseignée")
            {
                Clipboard.SetText(PatientAdresseText.Text);
                ShowCopyConfirmation("Adresse copiée !");
            }
            else
            {
                MessageBox.Show("L'adresse n'est pas renseignée.\n\nVeuillez compléter les informations du patient.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors de la copie : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Copie le numéro de sécurité sociale du patient dans le presse-papier.
    /// </summary>
    private void CopyNumSecuButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (PatientNumSecuText.Text != "Non renseigné")
            {
                // Copier sans espaces pour le NIR
                var nirClean = PatientNumSecuText.Text.Replace(" ", "");
                Clipboard.SetText(nirClean);
                ShowCopyConfirmation("N° Sécurité Sociale copié !");
            }
            else
            {
                MessageBox.Show("Le numéro de sécurité sociale n'est pas renseigné.\n\nVeuillez compléter les informations du patient.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors de la copie : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Copie la liste des demandes cochées dans le presse-papier.
    /// </summary>
    private void CopyAjouterListeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string liste = BuildAjouterListeTexte();
            if (string.IsNullOrEmpty(liste))
            {
                MessageBox.Show("Aucune demande n'a été cochée.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Clipboard.SetText(liste);
            ShowCopyConfirmation("Liste des demandes copiée !");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur lors de la copie : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>
    /// Affiche une confirmation temporaire de copie.
    /// </summary>
    private void ShowCopyConfirmation(string message)
    {
        StatusText.Text = $"✅ {message}";
        StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60));

        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        timer.Tick += (s, e) =>
        {
            if (!_isGenerating)
            {
                StatusText.Text = "✅ Toutes les sections ont été générées avec succès !";
            }
            timer.Stop();
        };
        timer.Start();
    }

    /// <summary>
    /// Navigue vers la page PDF correspondant à une section.
    /// </summary>
    private void JumpToPdfPage(int sectionIndex)
    {
        if (!_webViewInitialized || _webView?.CoreWebView2 == null) return;

        int pageNumber = MDPHPageMapping.GetPageForSection(sectionIndex);

        try
        {
            // Naviguer vers la page avec le fragment #page=N
            string pageUrl = $"{_pdfPath}#page={pageNumber}";
            _webView.CoreWebView2.Navigate(pageUrl);

            UpdatePdfPageIndicator();
        }
        catch (Exception ex)
        {
            // Silent fail - la navigation PDF peut échouer silencieusement
            System.Diagnostics.Debug.WriteLine($"PDF navigation error: {ex.Message}");
        }
    }

    /// <summary>
    /// Met à jour l'indicateur de page du PDF.
    /// </summary>
    private void UpdatePdfPageIndicator()
    {
        // Affiche le nom du fichier PDF chargé
        PdfPageIndicator.Text = !string.IsNullOrEmpty(_pdfPath) ? Path.GetFileName(_pdfPath) : "PDF non chargé";
    }

    // ========== GESTION NAVIGATION PDF ==========

    private void PdfPreviousPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_webViewInitialized || _webView?.CoreWebView2 == null) return;

        try
        {
            // Tenter de faire défiler vers le haut (simulation page précédente)
            _webView.CoreWebView2.ExecuteScriptAsync("window.scrollBy(0, -window.innerHeight);");
        }
        catch { }
    }

    private void PdfNextPageButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_webViewInitialized || _webView?.CoreWebView2 == null) return;

        try
        {
            // Tenter de faire défiler vers le bas (simulation page suivante)
            _webView.CoreWebView2.ExecuteScriptAsync("window.scrollBy(0, window.innerHeight);");
        }
        catch { }
    }

    private void PdfZoomInButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_webViewInitialized || _webView?.CoreWebView2 == null) return;

        _currentZoom += 10;
        if (_currentZoom > 200) _currentZoom = 200;

        try
        {
            _webView.CoreWebView2.ExecuteScriptAsync($"document.body.style.zoom = '{_currentZoom}%';");
        }
        catch { }
    }

    private void PdfZoomOutButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_webViewInitialized || _webView?.CoreWebView2 == null) return;

        _currentZoom -= 10;
        if (_currentZoom < 50) _currentZoom = 50;

        try
        {
            _webView.CoreWebView2.ExecuteScriptAsync($"document.body.style.zoom = '{_currentZoom}%';");
        }
        catch { }
    }

    // ========== HELPER METHODS ==========

    /// <summary>
    /// Ajuste automatiquement la hauteur d'une TextBox en fonction de son contenu.
    /// </summary>
    private void AdjustTextBoxHeight(TextBox textBox)
    {
        if (string.IsNullOrEmpty(textBox.Text))
        {
            textBox.Height = textBox.MinHeight;
            return;
        }

        // Compter le nombre de lignes
        int lineCount = textBox.Text.Split('\n').Length;

        // Calculer la hauteur approximative (hauteur de ligne * nombre de lignes + padding)
        double lineHeight = textBox.FontSize * 1.5; // 1.5 est l'interligne approximatif
        double calculatedHeight = (lineCount * lineHeight) + textBox.Padding.Top + textBox.Padding.Bottom + 10;

        // Appliquer les contraintes min/max
        if (calculatedHeight < textBox.MinHeight)
            textBox.Height = textBox.MinHeight;
        else if (calculatedHeight > textBox.MaxHeight)
            textBox.Height = textBox.MaxHeight;
        else
            textBox.Height = calculatedHeight;
    }

    // ========== SAUVEGARDE ET FERMETURE ==========

    /// <summary>
    /// Construit la liste des demandes à joindre au document (format texte séparé par des virgules).
    /// </summary>
    private string BuildAjouterListeTexte()
    {
        var demandes = new List<string>();

        // Ajouter les demandes cochées
        foreach (var kvp in _ajouterCheckboxes)
        {
            if (kvp.Value.IsChecked == true)
            {
                demandes.Add(kvp.Key);
            }
        }

        // Ajouter les "Autres demandes" si renseignées
        if (_autresDemandesTextBox != null && !string.IsNullOrWhiteSpace(_autresDemandesTextBox.Text))
        {
            demandes.Add(_autresDemandesTextBox.Text.Trim());
        }

        return demandes.Count > 0 ? string.Join(", ", demandes) : "";
    }

    private async void SaveDocxButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveDocxButton.IsEnabled = false;
            SaveDocxButton.Content = "⏳ Sauvegarde en cours...";

            // Construire le document Markdown
            var markdown = new StringBuilder();
            markdown.AppendLine($"# Formulaire MDPH - {_selectedPatient.NomComplet}");
            markdown.AppendLine($"**Date de génération:** {DateTime.Now:dd/MM/yyyy HH:mm}");
            markdown.AppendLine();
            markdown.AppendLine("---");
            markdown.AppendLine();

            for (int i = 0; i < MDPHPageMapping.TotalSections; i++)
            {
                if (_sectionTextBoxes.TryGetValue(i, out var textBox))
                {
                    markdown.AppendLine($"## {MDPHPageMapping.GetSectionTitle(i)}");
                    markdown.AppendLine();
                    markdown.AppendLine(textBox.Text);
                    markdown.AppendLine();
                }
            }

            // Ajouter la section "À joindre à ce document"
            string ajouterTexte = BuildAjouterListeTexte();
            if (!string.IsNullOrEmpty(ajouterTexte))
            {
                markdown.AppendLine("## 📎 À joindre à ce document");
                markdown.AppendLine();
                markdown.AppendLine(ajouterTexte);
                markdown.AppendLine();
            }

            // Sauvegarder le fichier Markdown
            var formulairesDir = _pathService.GetFormulairesDirectory(_selectedPatient.NomComplet);
            Directory.CreateDirectory(formulairesDir);

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string mdFilePath = Path.Combine(formulairesDir, $"MDPH_{timestamp}.md");
            var markdownContent = markdown.ToString();
            await File.WriteAllTextAsync(mdFilePath, markdownContent, Encoding.UTF8);

            // Exporter vers DOCX
            var (success, message, docxPath) = _letterService.ExportToDocx(
                _selectedPatient.NomComplet,
                markdownContent,
                mdFilePath
            );

            _hasUnsavedChanges = false;

            // Ajout du poids au système de synthèse
            if (!string.IsNullOrEmpty(_pdfPath)) 
            {
                _synthesisWeightTracker.RecordContentWeight(
                    _selectedPatient.NomComplet,
                    "formulaire_mdph",
                    _pdfPath,
                    0.8
                );
            }

            // ---------------------------------------------------------
            // NOUVEAU : Sauvegarder la synthèse JSON pour l'affichage dans la liste
            // ---------------------------------------------------------
            try
            {
                var demandes = new List<string>();
                foreach (var kvp in _ajouterCheckboxes)
                {
                    if (kvp.Value.IsChecked == true)
                    {
                        var content = kvp.Value.Content?.ToString();
                        if (!string.IsNullOrEmpty(content))
                        {
                            demandes.Add(content);
                        }
                    }
                }

                var synthesis = new MDPHSynthesis
                {
                    Patient = _selectedPatient.NomComplet,
                    DateCreation = DateTime.Now,
                    Demandes = demandes,
                    AutresDemandes = _autresDemandesTextBox?.Text ?? "",
                    FileName = Path.GetFileName(_pdfPath)
                };

                // Sauvegarder le JSON avec le même nom de base que le PDF
                // _pdfPath est ex: .../MDPH_20251121_101500.pdf
                // jsonPath sera: .../MDPH_20251121_101500.json
                if (!string.IsNullOrEmpty(_pdfPath))
                {
                    var jsonPath = Path.ChangeExtension(_pdfPath, ".json");
                    var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                    var jsonString = System.Text.Json.JsonSerializer.Serialize(synthesis, jsonOptions);
                    File.WriteAllText(jsonPath, jsonString);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur sauvegarde synthèse JSON : {ex.Message}");
                // On ne bloque pas la sauvegarde principale pour ça
            }

            if (success && !string.IsNullOrEmpty(docxPath))
            {
                // Message de succès plus clair pour le flux PDF manuel
                var result = MessageBox.Show(
                    $"✅ Dossier MDPH sauvegardé !\n\n" +
                    $"Le fichier PDF a été enregistré dans le dossier du patient.\n" +
                    $"Les réponses IA ont également été exportées (DOCX/MD).\n\n" +
                    $"Le formulaire apparaîtra maintenant dans la liste des formulaires sauvegardés.",
                    "Sauvegarde réussie",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    $"⚠️ Dossier sauvegardé mais erreur lors de l'export DOCX :\n\n{message}",
                    "Avertissement",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            // Important : définir le résultat sur true pour que la fenêtre parente rafraîchisse la liste
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Erreur lors de la sauvegarde :\n\n{ex.Message}",
                "Erreur",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SaveDocxButton.Content = "💾 Sauvegarder et Terminer";
            SaveDocxButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Remplit automatiquement le PDF MDPH avec les données générées par l'IA
    /// </summary>
    private async void FillPdfButton_Click(object sender, RoutedEventArgs e)
    {
        if (_generatedFormData == null)
        {
            MessageBox.Show(
                "Aucune donnée générée disponible.\n\nVeuillez d'abord générer le formulaire.",
                "Données manquantes",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        try
        {
            FillPdfButton.IsEnabled = false;
            FillPdfButton.Content = "⏳ Remplissage du PDF en cours...";

            // Trouver le template PDF MDPH dans Assets
            var assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Formulaires");
            
            // ✅ UTILISER UNIQUEMENT LE TEMPLATE 8 PAGES
            // On retire les fallbacks pour éviter toute confusion
            var templatePath = Path.Combine(assetsPath, "MDPH_Template_8pages.pdf");

            if (!File.Exists(templatePath))
            {
                MessageBox.Show(
                    $"Template MDPH introuvable !\n\nChemin attendu:\n{templatePath}\n\n" +
                    "Veuillez placer votre PDF avec champs AcroForm dans ce dossier.",
                    "Template manquant",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            // 2. Récupérer les métadonnées du patient
            var metadata = _patientIndex.GetMetadata(_selectedPatient.Id);
            if (metadata == null)
            {
                MessageBox.Show("Impossible de charger les métadonnées du patient.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // DEBUG: Afficher les données chargées pour le diagnostic
            System.Diagnostics.Debug.WriteLine($"=== DEBUG METADATA ===");
            System.Diagnostics.Debug.WriteLine($"  AdresseRue: '{metadata.AdresseRue ?? "NULL"}'");
            System.Diagnostics.Debug.WriteLine($"  AdresseCodePostal: '{metadata.AdresseCodePostal ?? "NULL"}'");
            System.Diagnostics.Debug.WriteLine($"  AdresseVille: '{metadata.AdresseVille ?? "NULL"}'");
            System.Diagnostics.Debug.WriteLine($"  NumeroSecuriteSociale: '{metadata.NumeroSecuriteSociale ?? "NULL"}'");

            // 3. Préparer le chemin de sortie
            var formulairesDir = _pathService.GetFormulairesDirectory(_selectedPatient.NomComplet);
            Directory.CreateDirectory(formulairesDir);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var outputPath = Path.Combine(formulairesDir, $"MDPH_Rempli_{timestamp}.pdf");

            // 4. Construire la liste des demandes
            var demandesList = new List<string>();
            foreach (var kvp in _ajouterCheckboxes)
            {
                if (kvp.Value.IsChecked == true)
                {
                    demandesList.Add(kvp.Value.Content?.ToString() ?? "");
                }
            }

            string autresDemandes = _autresDemandesTextBox?.Text ?? "";
            if (!string.IsNullOrWhiteSpace(autresDemandes))
            {
                demandesList.Add($"Autres demandes : {autresDemandes}");
            }

            string demandes = string.Join("\n", demandesList.Select(d => $"☑ {d}"));

            // 5. D'abord, lister tous les champs disponibles dans le PDF pour diagnostic
            var pdfFiller = new PDFFormFillerService();
            var (listSuccess, fieldNames, listError) = pdfFiller.ListFormFields(templatePath);

            if (listSuccess)
            {
                // Écrire dans le Debug
                System.Diagnostics.Debug.WriteLine($"========== CHAMPS DISPONIBLES DANS LE PDF ==========");
                System.Diagnostics.Debug.WriteLine($"Total : {fieldNames.Length} champs");

                // Écrire aussi dans un fichier texte pour que l'utilisateur puisse le voir facilement
                var logPath = Path.Combine(formulairesDir, "MDPH_Champs_PDF.txt");
                var logContent = new StringBuilder();
                logContent.AppendLine($"========== CHAMPS DISPONIBLES DANS LE PDF ==========");
                logContent.AppendLine($"Template : {templatePath}");
                logContent.AppendLine($"Date : {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                logContent.AppendLine($"Total : {fieldNames.Length} champs");
                logContent.AppendLine();

                foreach (var fieldName in fieldNames)
                {
                    // Afficher la longueur pour détecter espaces invisibles
                    System.Diagnostics.Debug.WriteLine($"  - '{fieldName}' (len={fieldName.Length})");
                    logContent.AppendLine($"  - {fieldName}");
                }

                // Chercher spécifiquement les champs patient_num_secu et patient_adresse
                var numSecuField = fieldNames.FirstOrDefault(f => f.Contains("num_secu"));
                var adresseField = fieldNames.FirstOrDefault(f => f.Contains("adresse"));
                System.Diagnostics.Debug.WriteLine($"  🔍 Champ num_secu trouvé: '{numSecuField}' (len={numSecuField?.Length ?? 0})");
                System.Diagnostics.Debug.WriteLine($"  🔍 Champ adresse trouvé: '{adresseField}' (len={adresseField?.Length ?? 0})");

                System.Diagnostics.Debug.WriteLine($"====================================================");
                logContent.AppendLine($"====================================================");

                File.WriteAllText(logPath, logContent.ToString(), Encoding.UTF8);
                System.Diagnostics.Debug.WriteLine($"Liste des champs sauvegardée dans : {logPath}");
            }

            // 6. Appeler le service de remplissage PDF
            StatusText.Text = "⏳ Remplissage du PDF avec les données générées...";

            var (success, filledPath, error) = pdfFiller.FillMDPHComplete(
                metadata,
                _generatedFormData,
                demandes,
                templatePath,
                outputPath
            );

            if (success)
            {
                StatusText.Text = "✅ PDF rempli avec succès !";
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60));

                // -------------------------------------------------------------
                // 🔄 SWAP: Remplacer le PDF vide par le PDF rempli dans la vue
                // -------------------------------------------------------------
                try
                {
                    // 1. Sauvegarder l'ancien chemin pour suppression
                    var oldPath = _pdfPath;

                    // 2. Mettre à jour le chemin officiel
                    _pdfPath = filledPath;

                    // 3. Afficher le PDF rempli dans WebView2
                    if (_webView != null && _webView.CoreWebView2 != null)
                    {
                        _webView.CoreWebView2.Navigate(_pdfPath);
                    }

                    // 4. Supprimer l'ancien fichier (le "blank")
                    // On attend un peu que WebView2 libère le fichier (si nécessaire)
                    await Task.Delay(500);
                    if (File.Exists(oldPath) && oldPath != filledPath)
                    {
                        try 
                        { 
                            File.Delete(oldPath);
                            System.Diagnostics.Debug.WriteLine($"[MDPHAssistantDialog] Ancien PDF supprimé: {oldPath}");
                            
                            // Supprimer aussi le JSON associé s'il existe (celui du blank)
                            var oldJson = Path.ChangeExtension(oldPath, ".json");
                            if (File.Exists(oldJson)) File.Delete(oldJson);
                        } 
                        catch (Exception exDelete)
                        {
                            System.Diagnostics.Debug.WriteLine($"[MDPHAssistantDialog] Impossible de supprimer l'ancien PDF (lock?): {exDelete.Message}");
                        }
                    }

                    // 5. Mettre à jour l'indicateur
                    UpdatePdfPageIndicator();

                    // Notification non-intrusive
                    MessageBox.Show(
                        "Le PDF a été rempli et chargé dans la visionneuse ci-contre.\n\n" +
                        "L'ancien fichier vide a été supprimé.",
                        "Remplissage Effectué",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                }
                catch (Exception exSwap)
                {
                    MessageBox.Show($"Le PDF est rempli mais impossible de l'afficher : {exSwap.Message}", "Info", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            else
            {
                StatusText.Text = $"❌ Erreur lors du remplissage du PDF";
                StatusText.Foreground = Brushes.Red;

                MessageBox.Show(
                    $"❌ Erreur lors du remplissage du PDF:\n\n{error}\n\n" +
                    "Vérifiez que les noms des champs AcroForm correspondent au mapping attendu.",
                    "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "❌ Erreur inattendue";
            StatusText.Foreground = Brushes.Red;

            MessageBox.Show(
                $"Erreur inattendue:\n\n{ex.Message}",
                "Erreur",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            FillPdfButton.Content = "📄 Remplir PDF automatiquement";
            FillPdfButton.IsEnabled = true;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MDPHAssistantDialog_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_hasUnsavedChanges)
        {
            var result = MessageBox.Show(
                "Vous avez des modifications non sauvegardées.\n\nVoulez-vous vraiment fermer sans sauvegarder ?",
                "Modifications non sauvegardées",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result == MessageBoxResult.No)
            {
                e.Cancel = true;
                return; // Ne pas continuer si l'utilisateur annule
            }
        }

        // ✅ Si le formulaire n'a pas été sauvegardé, supprimer le PDF et JSON temporaires
        if (DialogResult != true && !string.IsNullOrEmpty(_pdfPath))
        {
            try
            {
                // Supprimer le PDF
                if (File.Exists(_pdfPath))
                {
                    File.Delete(_pdfPath);
                    System.Diagnostics.Debug.WriteLine($"[MDPHAssistantDialog] PDF temporaire supprimé: {_pdfPath}");
                }

                // Supprimer le JSON associé s'il existe
                var jsonPath = Path.ChangeExtension(_pdfPath, ".json");
                if (File.Exists(jsonPath))
                {
                    File.Delete(jsonPath);
                    System.Diagnostics.Debug.WriteLine($"[MDPHAssistantDialog] JSON temporaire supprimé: {jsonPath}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MDPHAssistantDialog] Erreur suppression fichiers temporaires: {ex.Message}");
                // Ne pas propager l'erreur - c'est un nettoyage, pas critique
            }
        }

        // Nettoyer WebView2
        _webView?.Dispose();
    }
}
