using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MedCompanion.Models;
using MedCompanion.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MedCompanion.Dialogs
{
    public partial class PAIAssistantDialog : Window
    {
        private readonly PatientIndexEntry _selectedPatient;
        private readonly PatientIndexService _patientIndex;
        private readonly FormulaireAssistantService _formulaireService;
        private readonly SynthesisWeightTracker _synthesisWeightTracker;
        private readonly PathService _pathService = new PathService();

        private WebView2? _webView;
        private bool _webViewInitialized = false;
        private string _pdfPath = "";
        private double _currentZoom = 1.0;

        private string _motif;

        public PAIAssistantDialog(
            PatientIndexEntry selectedPatient,
            PatientIndexService patientIndex,
            FormulaireAssistantService formulaireService,
            SynthesisWeightTracker synthesisWeightTracker,
            string initialMotif = "")
        {
            InitializeComponent();

            _selectedPatient = selectedPatient;
            _patientIndex = patientIndex;
            _formulaireService = formulaireService;
            _synthesisWeightTracker = synthesisWeightTracker;
            _motif = initialMotif;

            // Configurer le bouton de dictée vocale pour cibler le TextBox des instructions
            VoiceButton.TargetTextBox = InstructionTextBox;

            Loaded += PAIAssistantDialog_Loaded;
        }

        private async void PAIAssistantDialog_Loaded(object sender, RoutedEventArgs e)
        {
            LoadPatientInfo();
            InitializeMotif();
            await InitializeWebView2Async();
        }

        private void InitializeMotif()
        {
            if (!string.IsNullOrEmpty(_motif))
            {
                MotifComboBox.Text = _motif;
            }
        }

        private void MotifComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (MotifComboBox.SelectedItem is ComboBoxItem item)
            {
                _motif = item.Content?.ToString() ?? string.Empty;
            }
        }

        private void MotifComboBox_LostFocus(object sender, RoutedEventArgs e)
        {
            _motif = MotifComboBox.Text;
        }

        private void SaveMotifButton_Click(object sender, RoutedEventArgs e)
        {
            _motif = MotifComboBox.Text;
            MessageBox.Show("Le motif a été pris en compte et sera enregistré avec le formulaire.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void LoadPatientInfo()
        {
            var metadata = _patientIndex.GetMetadata(_selectedPatient.Id);
            if (metadata != null)
            {
                PatientPrenomText.Text = metadata.Prenom ?? "Non renseigné";
                PatientNomText.Text = metadata.Nom ?? "Non renseigné";
                
                if (!string.IsNullOrEmpty(metadata.Dob) && DateTime.TryParse(metadata.Dob, out var dob))
                {
                    PatientDobText.Text = dob.ToString("dd/MM/yyyy");
                }
                else
                {
                    PatientDobText.Text = "Non renseignée";
                }
            }
            else
            {
                PatientPrenomText.Text = _selectedPatient.Prenom;
                PatientNomText.Text = _selectedPatient.Nom;
                PatientDobText.Text = "Non renseignée";
            }
        }

        private bool _isSaved = false;
        private string _tempPdfPath = "";

        private async Task InitializeWebView2Async()
        {
            try
            {
                var assetsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "Formulaires");
                var templatePath = Path.Combine(assetsPath, "Dossier PAI.pdf");

                if (!File.Exists(templatePath))
                {
                    PdfFallbackMessage.Text = "❌ PDF PAI introuvable dans Assets/Formulaires";
                    PdfFallbackMessage.Foreground = Brushes.Red;
                    return;
                }

                // Utiliser un fichier temporaire pour l'affichage
                _tempPdfPath = Path.Combine(Path.GetTempPath(), $"preview_pai_{Guid.NewGuid()}.pdf");
                File.Copy(templatePath, _tempPdfPath, overwrite: true);

                _webView = new WebView2();
                PdfViewerContainer.Children.Add(_webView);

                var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Path.GetTempPath(), "MedCompanion_WebView2"));
                await _webView.EnsureCoreWebView2Async(env);

                _webViewInitialized = true;

                // COLLER DANS LES CHAMPS DU PDF — c'est ce qui a cessé de marcher.
                //
                // Le visualiseur PDF d'Edge lit le presse-papier pour honorer un Ctrl+V dans un
                // champ de formulaire. WebView2 gouverne cet accès par une PERMISSION : sans
                // gestionnaire, la demande est refusée en silence, et le collage ne fait
                // simplement rien — aucun message, aucune erreur. Le comportement a changé avec
                // une mise à jour du runtime WebView2, ce qui explique qu'il ait marché avant sans
                // que le code bouge.
                //
                // On autorise la seule lecture du presse-papier, et uniquement elle : les autres
                // permissions (caméra, micro, géolocalisation…) gardent leur refus par défaut.
                _webView.CoreWebView2.PermissionRequested += (_, args) =>
                {
                    if (args.PermissionKind != CoreWebView2PermissionKind.ClipboardRead) return;
                    args.State   = CoreWebView2PermissionState.Allow;
                    args.Handled = true;   // supprime aussi la bannière de demande
                };

                // « Coller » AU CLIC DROIT.
                //
                // Le visualiseur PDF embarqué construit son propre menu contextuel, et il n'y met
                // pas d'entrée Coller — Ctrl+V fonctionne, le clic droit ne propose rien. On ajoute
                // donc l'entrée nous-mêmes, en tête du menu.
                _webView.CoreWebView2.ContextMenuRequested += (_, args) =>
                {
                    // Rien à proposer si le presse-papier est vide : une entrée qui ne fait rien
                    // est pire qu'une entrée absente, elle fait douter du champ visé.
                    if (!LirePressePapier(out var contenu)) return;

                    var apercu = contenu.Length > 28 ? contenu[..28] + "…" : contenu;
                    var item = _webView.CoreWebView2.Environment.CreateContextMenuItem(
                        $"Coller « {apercu} »", null, CoreWebView2ContextMenuItemKind.Command);

                    item.CustomItemSelected += (_, _) => CollerDansLeFormulaire();
                    args.MenuItems.Insert(0, item);
                };

                // URI file:// en bonne et due forme plutôt qu'un chemin Windows brut. Un chemin nu
                // est parfois normalisé par WebView2, parfois interprété comme une recherche —
                // et le visualiseur n'est alors pas dans son mode « document local ».
                _webView.CoreWebView2.Navigate(new Uri(_tempPdfPath).AbsoluteUri);

                PdfFallbackMessage.Visibility = Visibility.Collapsed;
                PdfZoomInButton.IsEnabled = true;
                PdfZoomOutButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                PdfFallbackMessage.Text = $"⚠ Erreur WebView2 : {ex.Message}";
                PdfFallbackMessage.Foreground = Brushes.Orange;
            }
        }

        private void SaveFormButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var formulairesDir = _pathService.GetFormulairesDirectory(_selectedPatient.NomComplet);
                Directory.CreateDirectory(formulairesDir);

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var pdfFileName = $"PAI_{_selectedPatient.Nom}_{_selectedPatient.Prenom}_{timestamp}.pdf";
                _pdfPath = Path.Combine(formulairesDir, pdfFileName);

                File.Copy(_tempPdfPath, _pdfPath, overwrite: true);
                SaveMetadata(formulairesDir, pdfFileName);

                // Enregistrer le poids pour la synthèse (Poids moyen pour un PAI = 0.5)
                _synthesisWeightTracker.RecordContentWeight(
                    _selectedPatient.NomComplet,
                    "Formulaire PAI",
                    _pdfPath,
                    0.5,
                    $"Création d'un PAI ({_motif})"
                );

                _isSaved = true;
                MessageBox.Show("Formulaire enregistré avec succès !", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'enregistrement : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveMetadata(string directory, string pdfFileName)
        {
            try
            {
                var synthesis = new PAISynthesis
                {
                    Type = "PAI",
                    DateCreation = DateTime.Now,
                    Patient = _selectedPatient.NomComplet,
                    Motif = _motif,
                    FileName = pdfFileName
                };

                var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
                var json = System.Text.Json.JsonSerializer.Serialize(synthesis, jsonOptions);
                
                var jsonPath = Path.Combine(directory, Path.ChangeExtension(pdfFileName, ".json"));
                File.WriteAllText(jsonPath, json, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la sauvegarde des métadonnées : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private async void GenerateButton_Click(object sender, RoutedEventArgs e)
        {
            var instruction = InstructionTextBox.Text.Trim();
            if (string.IsNullOrEmpty(instruction))
            {
                MessageBox.Show("Veuillez entrer une instruction pour l'IA.", "Instruction manquante", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                GenerateButton.IsEnabled = false;
                GenerateButton.Content = "⏳ Génération en cours...";
                StatusText.Text = "";
                ResponseTextBox.Text = "";

                var style = (StyleComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Standard";
                
                // On récupère le texte directement car la ComboBox est éditable
                var length = LengthComboBox.Text;
                if (string.IsNullOrWhiteSpace(length))
                {
                    length = "Moyen";
                }

                var metadata = _patientIndex.GetMetadata(_selectedPatient.Id);
                if (metadata == null)
                {
                    MessageBox.Show("Impossible de charger les données du patient.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                var response = await _formulaireService.GenerateCustomContent(metadata, instruction, style, length);

                ResponseTextBox.Text = response;
                CopyResponseButton.IsEnabled = true;
                StatusText.Text = "✅ Réponse générée !";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la génération : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                GenerateButton.IsEnabled = true;
                GenerateButton.Content = "✨ Générer avec l'IA";
            }
        }

        private void CopyResponseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(ResponseTextBox.Text))
            {
                Clipboard.SetText(ResponseTextBox.Text);
                StatusText.Text = "✅ Copié dans le presse-papier !";
                
                Task.Delay(2000).ContinueWith(_ => 
                {
                    Dispatcher.Invoke(() => StatusText.Text = "✅ Réponse générée !");
                });
            }
        }

        // ── Coller dans un champ du PDF ──────────────────────────────────────

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const byte VK_CONTROL      = 0x11;
        private const byte VK_V            = 0x56;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        private static bool LirePressePapier(out string contenu)
        {
            contenu = "";
            try
            {
                if (!Clipboard.ContainsText()) return false;
                contenu = (Clipboard.GetText() ?? "").Trim();
                return contenu.Length > 0;
            }
            catch { return false; }   // presse-papier occupé : on n'ajoute pas l'entrée
        }

        /// <summary>
        /// Rejoue un Ctrl+V dans le visualiseur.
        ///
        /// Détour assumé : le champ à remplir vit à l'intérieur du visualiseur PDF, hors d'atteinte
        /// depuis l'application — on ne peut ni lui écrire, ni lui envoyer une commande. Le seul
        /// chemin qui aboutit est celui que le médecin emprunte à la main, et qui fonctionne.
        ///
        /// La frappe est envoyée APRÈS fermeture du menu et retour du focus au visualiseur : jouée
        /// immédiatement, elle partirait dans le menu contextuel encore ouvert.
        /// </summary>
        private void CollerDansLeFormulaire()
        {
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                _webView?.Focus();
                await Task.Delay(80);

                keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                keybd_event(VK_V, 0, 0, UIntPtr.Zero);
                keybd_event(VK_V, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void CopyPrenomButton_Click(object sender, RoutedEventArgs e)
            => CopierDansPressePapier(PatientPrenomText.Text, "Prénom");

        private void CopyNomButton_Click(object sender, RoutedEventArgs e)
            => CopierDansPressePapier(PatientNomText.Text, "Nom");

        private void CopyDobButton_Click(object sender, RoutedEventArgs e)
            => CopierDansPressePapier(PatientDobText.Text, "Date de naissance");

        /// <summary>
        /// Copie, en réessayant, et en le DISANT.
        ///
        /// <c>Clipboard.SetText</c> échoue par une COMException dès qu'une autre application tient
        /// le presse-papier une fraction de seconde — un gestionnaire de presse-papier, une
        /// session de bureau à distance, un antivirus. L'appel nu laissait alors le médecin cliquer
        /// sur 📋 puis coller l'ancien contenu sans qu'aucun signe ne le prévienne, ce qui, sur un
        /// nom d'enfant recopié dans un document officiel, est exactement l'erreur qu'on ne veut
        /// pas rendre silencieuse.
        /// </summary>
        private void CopierDansPressePapier(string texte, string quoi)
        {
            if (string.IsNullOrWhiteSpace(texte))
            {
                StatusText.Text = $"⚠ {quoi} non renseigné — rien à copier.";
                return;
            }

            for (var essai = 0; essai < 5; essai++)
            {
                try
                {
                    // SetDataObject(copy: true) plutôt que SetText : le contenu survit à la
                    // fermeture de l'application, ce qui compte quand on colle dans un PDF ouvert
                    // à côté.
                    Clipboard.SetDataObject(texte, true);
                    StatusText.Text = $"✅ {quoi} copié — collez dans le champ du PDF (Ctrl+V).";
                    return;
                }
                catch (System.Runtime.InteropServices.COMException)
                {
                    System.Threading.Thread.Sleep(60);
                }
                catch (Exception ex)
                {
                    StatusText.Text = $"❌ Copie impossible : {ex.Message}";
                    return;
                }
            }

            StatusText.Text = "❌ Presse-papier occupé par une autre application — réessayez.";
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // Nettoyage du fichier temporaire si non sauvegardé
            if (!_isSaved && File.Exists(_tempPdfPath))
            {
                try
                {
                    // Libérer le fichier du WebView2 si possible ou attendre fermeture
                    _webView?.Dispose();
                    // Note: Il se peut que le fichier soit verrouillé encore quelques ms
                }
                catch { }
            }
            Close();
        }

        private void PdfZoomInButton_Click(object sender, RoutedEventArgs e)
        {
            if (_webViewInitialized && _webView != null)
            {
                _currentZoom += 0.1;
                _webView.ZoomFactor = _currentZoom;
            }
        }

        private void PdfZoomOutButton_Click(object sender, RoutedEventArgs e)
        {
            if (_webViewInitialized && _webView != null && _currentZoom > 0.2)
            {
                _currentZoom -= 0.1;
                _webView.ZoomFactor = _currentZoom;
            }
        }
    }
}
