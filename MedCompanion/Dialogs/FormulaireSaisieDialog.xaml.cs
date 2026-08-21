using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using MedCompanion.Models;
using MedCompanion.Services;
using MedCompanion.Services.Consultation;
using MedCompanion.Services.LLM;
using MedCompanion.Services.Vision;
using Microsoft.Web.WebView2.Core;
using SkiaSharp;

namespace MedCompanion.Dialogs
{
    public partial class FormulaireSaisieDialog : Window
    {
        private readonly string? _pdfPath;
        private readonly string? _patientDir;
        private readonly FormulaireDataService _service = new();
        private readonly GlmOcrService _ocr = new(AppSettings.Load());

        /// <summary>Lecture des cases à cocher uniquement (voir <see cref="AntecedentsSchema"/>) — GLM-OCR
        /// échoue systématiquement sur cette tâche, testé fiable avec ce modèle vision.</summary>
        private readonly LlamaCppProvider _vision = new();

        /// <summary>Nom de famille de l'enfant — sert à résoudre les cases « Même nom que
        /// l'enfant » du père/mère sans jamais faire lire ces cases par l'OCR.</summary>
        private string _enfantNom = "";

        /// <summary>
        /// patient.json est écrit tantôt en camelCase (création via <c>PatientIndexService.Upsert</c>),
        /// tantôt en PascalCase (<c>PatientInfoDialog</c>) — sans casse insensible à la lecture, les
        /// champs ne se lient pas et <see cref="PatientMetadata"/> revient avec Nom/Prénom vides, ce
        /// qui les réécrirait au moment de fusionner et ferait disparaître le patient de la recherche
        /// (l'index exclut toute fiche sans Nom/Prénom).
        /// </summary>
        private static readonly JsonSerializerOptions PatientJsonReadOptions =
            new() { PropertyNameCaseInsensitive = true };

        /// <summary>
        /// Position verticale (mm, page A4) d'un bloc du template — sert à découper le scan avant
        /// de l'envoyer à l'OCR (voir <see cref="ComputeBlockBoundsAsync"/>).
        /// </summary>
        private record BlockBounds(double PereY0, double PereY1, double MereY0, double MereY1,
            double AdresseY0, double AdresseY1, double AntecedentsY0, double AntecedentsY1,
            double SituationY0, double SituationY1, double AutorisationsY0, double AutorisationsY1);

        /// <summary>
        /// Carte de coordonnées du template calculée une seule fois par session (Edge headless,
        /// ~1,7 s) puis réutilisée par tous les clics « Remplissage automatique ».
        /// </summary>
        private static readonly Lazy<Task<BlockBounds?>> BlockBoundsTask = new(ComputeBlockBoundsAsync);

        public FormulaireSaisieDialog(string? pdfPath, string? patientDir)
        {
            InitializeComponent();
            _pdfPath = pdfPath;
            _patientDir = patientDir;
            PopulateVisionModels();
        }

        /// <summary>
        /// Alimente le sélecteur de modèle de vision. Seuls les modèles dont le projecteur ET le
        /// GGUF sont réellement présents sont listés : proposer un modèle absent ne produirait
        /// qu'un échec au moment de lire le formulaire.
        /// </summary>
        private void PopulateVisionModels()
        {
            _suspendVisionChange = true;
            try
            {
                VisionModelCombo.Items.Clear();
                var current = LlamaCppProfiles.VisionProfile;

                foreach (var profile in LlamaCppProfiles.VisionCapable)
                {
                    var item = new System.Windows.Controls.ComboBoxItem
                    {
                        Content = profile.ShortName,
                        Tag     = profile
                    };
                    VisionModelCombo.Items.Add(item);
                    if (profile.Id == current.Id) VisionModelCombo.SelectedItem = item;
                }

                // Aucun modèle de vision installé : masquer plutôt qu'afficher une liste vide.
                VisionModelCombo.Visibility = VisionModelCombo.Items.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                if (VisionModelCombo.SelectedItem == null && VisionModelCombo.Items.Count > 0)
                    VisionModelCombo.SelectedIndex = 0;
            }
            finally { _suspendVisionChange = false; }
        }

        private bool _suspendVisionChange;

        private void VisionModelCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_suspendVisionChange) return;
            if (VisionModelCombo.SelectedItem is not System.Windows.Controls.ComboBoxItem item) return;
            if (item.Tag is not LlamaCppModelProfile profile) return;

            // WPF peut lever cet événement après la construction, une fois le ComboBox mis en page :
            // la garde posée pendant le remplissage est alors déjà levée. On ne réagit donc qu'à un
            // changement réel, sinon un simple ouverture de formulaire afficherait un message.
            if (profile.Id == LlamaCppProfiles.VisionProfile.Id) return;

            LlamaCppProfiles.SetVisionProfile(profile);
            AutoFillStatus.Text = $"Vision : {profile.ShortName} (appliqué à la prochaine lecture).";
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _enfantNom = LoadEnfantNom(_patientDir);

            foreach (var radio in new[] { SitEnsemble, SitSepares, SitDivorces, SitGardeAlt, SitRecomposee, SitAutre })
                radio.Checked += (_, _) => UpdateAdresse2Visibility();

            // Load existing data
            if (!string.IsNullOrEmpty(_patientDir))
                PopulateForm(_service.Load(_patientDir));

            UpdateAdresse2Visibility();

            // Init PDF viewer
            if (!string.IsNullOrEmpty(_pdfPath) && File.Exists(_pdfPath))
            {
                try
                {
                    var env = await CoreWebView2Environment.CreateAsync();
                    await PdfWebView.EnsureCoreWebView2Async(env);
                    PdfWebView.CoreWebView2.Navigate("file:///" + _pdfPath.Replace('\\', '/'));
                    PdfWebView.Visibility = Visibility.Visible;
                    PdfFallback.Visibility = Visibility.Collapsed;
                }
                catch
                {
                    PdfFallback.Text = "Impossible d'afficher le PDF dans le visualiseur.";
                }
            }
            else
            {
                PdfFallback.Text = "Aucun PDF associé à ce document.";
            }
        }

        // ── Nom du parent = nom de l'enfant / adresse unique si parents ensemble ──

        private static string LoadEnfantNom(string? patientDir)
        {
            if (string.IsNullOrEmpty(patientDir)) return "";
            try
            {
                var path = Path.Combine(patientDir, "info_patient", "patient.json");
                if (!File.Exists(path)) return "";
                var meta = JsonSerializer.Deserialize<PatientMetadata>(File.ReadAllText(path, Encoding.UTF8), PatientJsonReadOptions);
                return meta?.Nom ?? "";
            }
            catch
            {
                return "";
            }
        }

        private void PereNomMemeCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool same = PereNomMemeCheckBox.IsChecked == true;
            PereNom.IsEnabled = !same;
            if (same) PereNom.Text = _enfantNom;
        }

        private void MereNomMemeCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
        {
            bool same = MereNomMemeCheckBox.IsChecked == true;
            MereNom.IsEnabled = !same;
            if (same) MereNom.Text = _enfantNom;
        }

        /// <summary>Parents ensemble (bloc 4) ⇒ une seule adresse suffit : la 2e adresse est
        /// masquée. Tout autre statut (séparés, divorcés, garde alternée, recomposée, autre) ou
        /// statut pas encore renseigné ⇒ la 2e adresse reste disponible.</summary>
        private void UpdateAdresse2Visibility()
        {
            bool ensemble = SitEnsemble.IsChecked == true;
            Adresse2Panel.Visibility = ensemble ? Visibility.Collapsed : Visibility.Visible;
            Adresse2Hint.Visibility = ensemble ? Visibility.Visible : Visibility.Collapsed;
        }

        // ── Remplissage automatique (OCR) ────────────────────────
        //
        // GLM-OCR (1,1B, spécialisé OCR) tient mal l'attention sur une page entière à
        // plusieurs blocs : mesuré — envoyé la page complète (ou même les 3 blocs recadrés
        // ensemble), le modèle ne renseigne que le premier bloc puis referme son JSON.
        // Recadré sur un seul bloc à la fois, il lit correctement. On découpe donc le scan
        // en 3 bandes (père / mère / adresse) à partir de la carte de coordonnées du
        // template (déjà utilisée en Phase 4b) et on fait un appel OCR par bande.
        //
        // Les cases à cocher (antécédents familiaux, situation familiale, autorisations, photo) sont
        // lues séparément par un modèle VISION (Qwen3.8-27B, llama.cpp) plutôt que par GLM-OCR :
        // testé — GLM-OCR échoue systématiquement sur cette tâche (recopie le schéma tel quel, ou
        // coche plusieurs cases à la fois sur une ligne isolée). La densité d'encre par case (approche
        // pixel, essayée avant) ne discriminait pas non plus de façon fiable sur un scan réel. Le
        // modèle vision, lui, a été testé fiable : 8/8 cases correctement identifiées sur un
        // formulaire réel, avec un raisonnement ligne par ligne cohérent.

        /// <summary>Marge ajoutée de part et d'autre de chaque bloc (mm) pour absorber un léger
        /// décalage de calage sans couper le texte.</summary>
        private const double BlockPaddingMm = 4;

        private async void BtnAutoFill_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_pdfPath) || !File.Exists(_pdfPath))
            {
                AutoFillStatus.Text = "Aucun scan à lire.";
                return;
            }

            BtnAutoFill.IsEnabled = false;

            try
            {
                var bounds = await BlockBoundsTask.Value;
                var pageImage = await Task.Run(() => RenderFirstPageToPng(_pdfPath!));

                if (bounds == null)
                {
                    // Repli : carte de coordonnées indisponible (Edge absent...) → un seul
                    // appel pleine page. Moins fiable, mieux que rien.
                    AutoFillStatus.Text = "Lecture OCR en cours...";
                    var (ok, json, error) = await _ocr.ExtractJsonAsync(pageImage, FullPageSchema, FullPageConsigne);
                    if (!ok) { AutoFillStatus.Text = $"Échec OCR : {error}"; return; }
                    ApplyPereJson(json);
                    ApplyMereJson(json);
                    ApplyAdresseJson(json, "adresse1", Adresse, CodePostal, Ville);
                    ApplyAdresseJson(json, "adresse2", Adresse2, CodePostal2, Ville2);
                    AutoFillStatus.Text = "Champs texte pré-remplis (page entière) — vérifiez avant de valider.";
                    return;
                }

                double mmToPx = ImageOps.GetSize(pageImage).height / 297.0;

                AutoFillStatus.Text = "Lecture des coordonnées du père...";
                var pereCrop = CropBlock(pageImage, bounds.PereY0, bounds.PereY1, mmToPx);
                var (pereOk, pereJson, pereErr) = await _ocr.ExtractJsonAsync(pereCrop, PereSchema, PereConsigne);
                if (pereOk) ApplyPereJson(pereJson);

                AutoFillStatus.Text = "Lecture des coordonnées de la mère...";
                var mereCrop = CropBlock(pageImage, bounds.MereY0, bounds.MereY1, mmToPx);
                var (mereOk, mereJson, mereErr) = await _ocr.ExtractJsonAsync(mereCrop, MereSchema, MereConsigne);
                if (mereOk) ApplyMereJson(mereJson);

                AutoFillStatus.Text = "Lecture de l'adresse...";
                var adresseCrop = CropBlock(pageImage, bounds.AdresseY0, bounds.AdresseY1, mmToPx);
                var (adrOk, adrJson, adrErr) = await _ocr.ExtractJsonAsync(adresseCrop, AdresseSchema, AdresseConsigne);
                if (adrOk)
                {
                    ApplyAdresseJson(adrJson, "adresse1", Adresse, CodePostal, Ville);
                    ApplyAdresseJson(adrJson, "adresse2", Adresse2, CodePostal2, Ville2);
                }

                // Cases à cocher : modèle vision, pas GLM-OCR — voir note en tête de section.
                // Best-effort : n'empêche jamais le reste du pré-remplissage. Les 3 blocs partagent
                // le même serveur en mode vision une fois démarré (coût de rechargement payé une
                // seule fois, pas à chaque bloc).
                var visionResults = new List<(string label, bool ok, string? error)>();

                async Task<bool> ReadVisionBlock(string label, double y0, double y1,
                    Func<string, bool> apply, string prompt)
                {
                    if (y1 <= y0) return false;
                    AutoFillStatus.Text = $"Lecture {label} (vision)...";
                    var crop = CropBlock(pageImage, y0, y1, mmToPx);
                    var (visOk, visJson, visErr) = await _vision.AnalyzeImageAsync(prompt, crop, maxTokens: 600);
                    bool applied = visOk && apply(visJson);
                    visionResults.Add((label, applied, visOk ? (applied ? null : "réponse illisible") : visErr));
                    return applied;
                }

                bool atcdOk = await ReadVisionBlock("des antécédents familiaux",
                    bounds.AntecedentsY0, bounds.AntecedentsY1, ApplyAntecedentsJson, BuildAntecedentsPrompt());
                bool sitOk = await ReadVisionBlock("de la situation familiale",
                    bounds.SituationY0, bounds.SituationY1, ApplySituationJson, BuildSituationPrompt());
                bool autorOk = await ReadVisionBlock("des autorisations",
                    bounds.AutorisationsY0, bounds.AutorisationsY1, ApplyAutorisationsJson, BuildAutorisationsPrompt());

                bool anyVisionOk = atcdOk || sitOk || autorOk;

                if (!pereOk && !mereOk && !adrOk && !anyVisionOk)
                {
                    var firstErr = pereErr ?? mereErr ?? adrErr ?? visionResults.FirstOrDefault(r => r.error != null).error;
                    AutoFillStatus.Text = $"Échec OCR : {firstErr}";
                }
                else
                {
                    var failed = visionResults.Where(r => !r.ok).Select(r => $"{r.label} ({r.error})").ToList();
                    AutoFillStatus.Text = "Champs pré-remplis — vérifiez avant de valider." +
                        (failed.Count > 0 ? $" Non lus : {string.Join(", ", failed)}." : "");
                }
            }
            catch (Exception ex)
            {
                AutoFillStatus.Text = $"Erreur : {ex.Message}";
            }
            finally
            {
                BtnAutoFill.IsEnabled = true;
            }
        }

        private static byte[] CropBlock(byte[] pageImage, double yMm0, double yMm1, double mmToPx)
        {
            var (width, height) = ImageOps.GetSize(pageImage);
            int y0 = Math.Max(0, (int)((yMm0 - BlockPaddingMm) * mmToPx));
            int y1 = Math.Min(height, (int)((yMm1 + BlockPaddingMm) * mmToPx));
            return ImageOps.Crop(pageImage, new Int32Rect(0, y0, width, Math.Max(1, y1 - y0)));
        }

        /// <summary>
        /// Calcule, une fois pour la session, la position verticale (mm) des blocs père/mère/
        /// adresse à partir de la carte de coordonnées du template (Phase 4b). La géométrie du
        /// scan étant celle du template imprimé, ces bornes restent valables tant que le
        /// template n'est pas modifié.
        /// </summary>
        private static async Task<BlockBounds?> ComputeBlockBoundsAsync()
        {
            try
            {
                var templatePath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "Resources", "Formulaires", "formulaire_completion.html");
                if (!File.Exists(templatePath)) return null;

                var pdf = new EdgeHeadlessPdfService();
                if (!pdf.IsAvailable) return null;

                var (ok, json, _) = await pdf.ExtractCoordMapAsync(templatePath);
                if (!ok) return null;

                using var doc = JsonDocument.Parse(json);
                var fields = doc.RootElement.GetProperty("fields");

                (double y0, double y1) Bounds(params string[] prefixes)
                {
                    double min = double.MaxValue, max = double.MinValue;
                    foreach (var f in fields.EnumerateArray())
                    {
                        var name = f.GetProperty("name").GetString() ?? "";
                        if (!prefixes.Any(p => name.StartsWith(p, StringComparison.Ordinal))) continue;
                        var rect = f.GetProperty("rect");
                        var y = rect.GetProperty("y").GetDouble();
                        var h = rect.GetProperty("h").GetDouble();
                        if (y < min) min = y;
                        if (y + h > max) max = y + h;
                    }
                    return min <= max ? (min, max) : (0, 0);
                }

                var (pY0, pY1) = Bounds("pere_");
                var (mY0, mY1) = Bounds("mere_");
                var (aY0, aY1) = Bounds("adresse1_", "adresse2_");
                var (atY0, atY1) = Bounds("atcd_");
                var (siY0, siY1) = Bounds("situation_", "garde_");
                var (auY0, auY1) = Bounds("photo_", "consent_");

                return new BlockBounds(pY0, pY1, mY0, mY1, aY0, aY1, atY0, atY1, siY0, siY1, auY0, auY1);
            }
            catch
            {
                return null; // le repli page entière prend le relais
            }
        }

        private static byte[] RenderFirstPageToPng(string pdfPath)
        {
            var pdfBytes = File.ReadAllBytes(pdfPath);
            using var bitmap = PDFtoImage.Conversion.ToImage(pdfBytes, 0);
            using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }

        // ── Schémas OCR (un par bloc — voir note en tête de section) ─────

        private const string PereSchema =
            """{ "pere_prenom": null, "pere_nom": null, "pere_telephone": null, "pere_email": null }""";
        private const string PereConsigne =
            "Ce bloc montre les coordonnées du père : prénom et nom en lettres capitales dans des " +
            "cases, téléphone portable en chiffres dans des cases, email en écriture libre.";

        private const string MereSchema =
            """{ "mere_prenom": null, "mere_nom": null, "mere_telephone": null, "mere_email": null }""";
        private const string MereConsigne =
            "Ce bloc montre les coordonnées de la mère : prénom et nom en lettres capitales dans des " +
            "cases, téléphone portable en chiffres dans des cases, email en écriture libre.";

        private const string AdresseSchema =
            """
            {
              "adresse1_rue": null, "adresse1_cp_ville": null,
              "adresse2_rue": null, "adresse2_cp_ville": null
            }
            """;
        private const string AdresseConsigne =
            "Ce bloc montre l'adresse du milieu de vie : rue en lettres/chiffres capitales dans des " +
            "cases (adresse 1, à gauche), puis code postal et ville dans des cases juste en dessous. " +
            "Une adresse 2 facultative (garde alternée) peut apparaître à droite, même structure. " +
            "Si une zone est vide, laisse la valeur à null.";

        // Lecture des cases à cocher (antécédents familiaux) : GLM-OCR échoue systématiquement sur
        // cette tâche (testé — soit il recopie le schéma tel quel, soit il coche plusieurs cases à
        // la fois sur une ligne isolée sans ambiguïté). Le modèle vision Qwen3.8-27B (llama.cpp,
        // mmproj) a été testé fiable sur ce même formulaire réel : 8/8 cases correctement identifiées,
        // avec un raisonnement ligne par ligne cohérent — voir LlamaCppProvider.
        private const string AntecedentsSchema =
            """
            {
              "tdah": "oui|non|nsp", "dyslexie": "oui|non|nsp", "tsa": "oui|non|nsp",
              "anxiete": "oui|non|nsp", "depression": "oui|non|nsp", "bipolarite": "oui|non|nsp",
              "addictions": "oui|non|nsp", "suicide": "oui|non|nsp"
            }
            """;
        private const string AntecedentsConsigne =
            "Ce tableau liste 8 antécédents familiaux, chacun avec 3 cases à cocher : Oui, Non, Ne sait " +
            "pas. Pour chaque ligne, examine attentivement laquelle des 3 cases contient une croix ou " +
            "une coche visible, et réponds par \"oui\", \"non\" ou \"nsp\" selon la colonne cochée. " +
            "Une seule case doit être cochée par ligne ; si aucune ne l'est, réponds null.";

        private const string SituationSchema =
            """
            {
              "situation": "ensemble|separes|divorces|garde_alternee|recomposee|autre",
              "garde": "parents|mere|pere|autre"
            }
            """;
        private const string SituationConsigne =
            "Ce bloc montre deux groupes de cases à cocher. Le premier (\"Situation familiale\") a 6 " +
            "options : Parents ensemble, Parents séparés, Divorcés, Garde alternée, Famille recomposée, " +
            "Autre — une seule est cochée. Le second (\"Mode de garde principal de l'enfant\") a 4 " +
            "options : Parents, Mère, Père, Autre — une seule est cochée. Examine attentivement laquelle " +
            "case contient une croix ou une coche visible dans chaque groupe.";

        private const string AutorisationsSchema =
            """{ "photo": "oui|non", "infos": "oui|non", "sms": "oui|non", "email": "oui|non" }""";
        private const string AutorisationsConsigne =
            "Ce bloc montre 4 questions, chacune avec 2 cases à cocher (Oui/Non ou Autorise/N'autorise " +
            "pas) : autorisation de photographie de l'enfant, autorisation d'utiliser les informations du " +
            "formulaire, autorisation d'envoi de SMS, autorisation d'envoi d'emails. Pour chacune, examine " +
            "attentivement laquelle des 2 cases contient une croix ou une coche visible.";

        private const string FullPageSchema =
            """
            {
              "pere_prenom": null, "pere_nom": null, "pere_telephone": null, "pere_email": null,
              "mere_prenom": null, "mere_nom": null, "mere_telephone": null, "mere_email": null,
              "adresse1_rue": null, "adresse1_cp_ville": null,
              "adresse2_rue": null, "adresse2_cp_ville": null
            }
            """;
        private const string FullPageConsigne =
            "Ce formulaire comporte un bloc 1 (coordonnées du père), un bloc 2 (coordonnées de la " +
            "mère) et un bloc 3 (adresse). Les noms/prénoms/téléphones sont écrits en lettres " +
            "capitales dans des cases individuelles.";

        private void ApplyPereJson(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            SetIfPresent(root, "pere_prenom", PerePrenom);
            SetIfPresent(root, "pere_nom", PereNom);
            SetIfPresent(root, "pere_telephone", PereTel);
            SetIfPresent(root, "pere_email", PereEmail);
        }

        private void ApplyMereJson(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            SetIfPresent(root, "mere_prenom", MerePrenom);
            SetIfPresent(root, "mere_nom", MereNom);
            SetIfPresent(root, "mere_telephone", MereTel);
            SetIfPresent(root, "mere_email", MereEmail);
        }

        /// <summary>Répartit un champ « rue » + un champ « CP + Ville » combiné (une seule
        /// rangée de cases sur le papier) vers les trois zones de saisie séparées de l'écran.</summary>
        private void ApplyAdresseJson(string json, string prefix,
            System.Windows.Controls.TextBox rueBox,
            System.Windows.Controls.TextBox cpBox,
            System.Windows.Controls.TextBox villeBox)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            SetIfPresent(root, $"{prefix}_rue", rueBox);

            if (!root.TryGetProperty($"{prefix}_cp_ville", out var cpVilleEl) ||
                cpVilleEl.ValueKind != JsonValueKind.String) return;

            var raw = cpVilleEl.GetString()?.Trim();
            if (string.IsNullOrWhiteSpace(raw)) return;

            var match = Regex.Match(raw, @"^(\d{4,5})\s+(.+)$");
            if (match.Success)
            {
                cpBox.Text = match.Groups[1].Value;
                villeBox.Text = match.Groups[2].Value.Trim();
            }
            else
            {
                villeBox.Text = raw;
            }
        }

        private static string BuildAntecedentsPrompt() =>
            AntecedentsConsigne +
            "\n\nRéponds UNIQUEMENT avec un objet JSON valide, sans commentaire ni texte autour, selon ce schéma :\n" +
            AntecedentsSchema;

        /// <summary>Applique la lecture vision des 8 antécédents aux boutons radio correspondants.
        /// Retourne false si la réponse n'est pas exploitable (rien n'est modifié dans ce cas).</summary>
        private bool ApplyAntecedentsJson(string raw)
        {
            try
            {
                var match = Regex.Match(raw, @"\{[\s\S]*\}");
                if (!match.Success) return false;

                using var doc = JsonDocument.Parse(match.Value);
                var root = doc.RootElement;

                void Apply(string key,
                    System.Windows.Controls.RadioButton oui,
                    System.Windows.Controls.RadioButton non,
                    System.Windows.Controls.RadioButton nsp)
                {
                    if (!root.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.String) return;
                    var v = el.GetString();
                    if (v == "oui" || v == "non" || v == "nsp") SetOuiNonNsp(v, oui, non, nsp);
                }

                Apply("tdah",       TdahOui, TdahNon, TdahNsp);
                Apply("dyslexie",   DyslexieOui, DyslexieNon, DyslexieNsp);
                Apply("tsa",        TsaOui, TsaNon, TsaNsp);
                Apply("anxiete",    AnxieuxOui, AnxieuxNon, AnxieuxNsp);
                Apply("depression", DepOui, DepNon, DepNsp);
                Apply("bipolarite", BipoOui, BipoNon, BipoNsp);
                Apply("addictions", AddOui, AddNon, AddNsp);
                Apply("suicide",    TsOui, TsNon, TsNsp);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string BuildSituationPrompt() =>
            SituationConsigne +
            "\n\nRéponds UNIQUEMENT avec un objet JSON valide, sans commentaire ni texte autour, selon ce schéma :\n" +
            SituationSchema;

        private bool ApplySituationJson(string raw)
        {
            try
            {
                var match = Regex.Match(raw, @"\{[\s\S]*\}");
                if (!match.Success) return false;

                using var doc = JsonDocument.Parse(match.Value);
                var root = doc.RootElement;

                if (root.TryGetProperty("situation", out var sitEl) && sitEl.ValueKind == JsonValueKind.String)
                    SetRadio(sitEl.GetString() ?? "", ("ensemble", SitEnsemble), ("separes", SitSepares),
                        ("divorces", SitDivorces), ("garde_alternee", SitGardeAlt),
                        ("recomposee", SitRecomposee), ("autre", SitAutre));

                if (root.TryGetProperty("garde", out var gardeEl) && gardeEl.ValueKind == JsonValueKind.String)
                    SetRadio(gardeEl.GetString() ?? "", ("parents", GardeParents), ("mere", GardeMere),
                        ("pere", GardePere), ("autre", GardeAutre));

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string BuildAutorisationsPrompt() =>
            AutorisationsConsigne +
            "\n\nRéponds UNIQUEMENT avec un objet JSON valide, sans commentaire ni texte autour, selon ce schéma :\n" +
            AutorisationsSchema;

        private bool ApplyAutorisationsJson(string raw)
        {
            try
            {
                var match = Regex.Match(raw, @"\{[\s\S]*\}");
                if (!match.Success) return false;

                using var doc = JsonDocument.Parse(match.Value);
                var root = doc.RootElement;

                void Apply(string key,
                    System.Windows.Controls.RadioButton oui,
                    System.Windows.Controls.RadioButton non)
                {
                    if (!root.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.String) return;
                    var v = el.GetString();
                    if (v == "oui" || v == "non") SetOuiNon(v, oui, non);
                }

                Apply("photo", PhotoAutoriseOui, PhotoAutoriseNon);
                Apply("infos", AutorInfosOui, AutorInfosNon);
                Apply("sms",   AutorSmsOui, AutorSmsNon);
                Apply("email", AutorEmailOui, AutorEmailNon);

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static void SetIfPresent(JsonElement root, string key, System.Windows.Controls.TextBox target)
        {
            if (!root.TryGetProperty(key, out var value) || value.ValueKind != JsonValueKind.String)
                return;

            var text = value.GetString();
            if (!string.IsNullOrWhiteSpace(text))
                target.Text = text.Trim();
        }

        private void PopulateForm(FormulaireData d)
        {
            PerePrenom.Text  = d.PerePrenom;
            PereNom.Text     = d.PereNom;
            PereNomMemeCheckBox.IsChecked = d.PereNomMeme; // déclenche PereNomMemeCheckBox_CheckedChanged
            PereTel.Text     = d.PereTel;
            PereEmail.Text   = d.PereEmail;

            MerePrenom.Text  = d.MerePrenom;
            MereNom.Text     = d.MereNom;
            MereNomMemeCheckBox.IsChecked = d.MereNomMeme; // déclenche MereNomMemeCheckBox_CheckedChanged
            MereTel.Text     = d.MereTel;
            MereEmail.Text   = d.MereEmail;

            Adresse.Text    = d.Adresse;
            CodePostal.Text = d.CodePostal;
            Ville.Text      = d.Ville;
            SetRadio(d.GardeAdresse1, ("mere", GardeAdr1Mere), ("pere", GardeAdr1Pere), ("autre", GardeAdr1Autre));

            Adresse2.Text    = d.Adresse2;
            CodePostal2.Text = d.CodePostal2;
            Ville2.Text      = d.Ville2;
            SetRadio(d.GardeAdresse2, ("mere", GardeAdr2Mere), ("pere", GardeAdr2Pere), ("autre", GardeAdr2Autre));

            SetRadio(d.SituationFamiliale, ("ensemble", SitEnsemble), ("separes", SitSepares),
                ("divorces", SitDivorces), ("garde_alternee", SitGardeAlt),
                ("recomposee", SitRecomposee), ("autre", SitAutre));

            SetRadio(d.GardePrincipale, ("parents", GardeParents), ("mere", GardeMere),
                ("pere", GardePere), ("autre", GardeAutre));

            SetOuiNonNsp(d.Tdah,            TdahOui, TdahNon, TdahNsp);
            SetOuiNonNsp(d.Dyslexie,        DyslexieOui, DyslexieNon, DyslexieNsp);
            SetOuiNonNsp(d.Tsa,             TsaOui, TsaNon, TsaNsp);
            SetOuiNonNsp(d.TroublesAnxieux, AnxieuxOui, AnxieuxNon, AnxieuxNsp);
            SetOuiNonNsp(d.Depression,      DepOui, DepNon, DepNsp);
            SetOuiNonNsp(d.Bipolarite,      BipoOui, BipoNon, BipoNsp);
            SetOuiNonNsp(d.Addictions,      AddOui, AddNon, AddNsp);
            SetOuiNonNsp(d.TentativeSuicide, TsOui, TsNon, TsNsp);

            AntecedentsAutreLabel.Text = d.AntecedentsAutreLabel;
            SetOuiNonNsp(d.AntecedentsAutre, AutreAtcdOui, AutreAtcdNon, AutreAtcdNsp);

            SetOuiNon(d.PhotoAutorise,   PhotoAutoriseOui, PhotoAutoriseNon);
            SetOuiNon(d.AutorUsageInfos, AutorInfosOui, AutorInfosNon);
            SetOuiNon(d.AutorSms,        AutorSmsOui,   AutorSmsNon);
            SetOuiNon(d.AutorEmail,      AutorEmailOui, AutorEmailNon);
        }

        private FormulaireData CollectForm() => new()
        {
            PerePrenom  = PerePrenom.Text.Trim(),
            PereNom     = PereNomMemeCheckBox.IsChecked == true ? _enfantNom : PereNom.Text.Trim(),
            PereNomMeme = PereNomMemeCheckBox.IsChecked == true,
            PereTel     = PereTel.Text.Trim(),
            PereEmail   = PereEmail.Text.Trim(),

            MerePrenom  = MerePrenom.Text.Trim(),
            MereNom     = MereNomMemeCheckBox.IsChecked == true ? _enfantNom : MereNom.Text.Trim(),
            MereNomMeme = MereNomMemeCheckBox.IsChecked == true,
            MereTel     = MereTel.Text.Trim(),
            MereEmail   = MereEmail.Text.Trim(),

            Adresse       = Adresse.Text.Trim(),
            CodePostal    = CodePostal.Text.Trim(),
            Ville         = Ville.Text.Trim(),
            GardeAdresse1 = GetRadio(("mere", GardeAdr1Mere), ("pere", GardeAdr1Pere), ("autre", GardeAdr1Autre)),

            Adresse2      = Adresse2.Text.Trim(),
            CodePostal2   = CodePostal2.Text.Trim(),
            Ville2        = Ville2.Text.Trim(),
            GardeAdresse2 = GetRadio(("mere", GardeAdr2Mere), ("pere", GardeAdr2Pere), ("autre", GardeAdr2Autre)),

            SituationFamiliale = GetRadio(("ensemble", SitEnsemble), ("separes", SitSepares),
                ("divorces", SitDivorces), ("garde_alternee", SitGardeAlt),
                ("recomposee", SitRecomposee), ("autre", SitAutre)),

            GardePrincipale = GetRadio(("parents", GardeParents), ("mere", GardeMere),
                ("pere", GardePere), ("autre", GardeAutre)),

            Tdah             = GetOuiNonNsp(TdahOui, TdahNon, TdahNsp),
            Dyslexie         = GetOuiNonNsp(DyslexieOui, DyslexieNon, DyslexieNsp),
            Tsa              = GetOuiNonNsp(TsaOui, TsaNon, TsaNsp),
            TroublesAnxieux  = GetOuiNonNsp(AnxieuxOui, AnxieuxNon, AnxieuxNsp),
            Depression       = GetOuiNonNsp(DepOui, DepNon, DepNsp),
            Bipolarite       = GetOuiNonNsp(BipoOui, BipoNon, BipoNsp),
            Addictions       = GetOuiNonNsp(AddOui, AddNon, AddNsp),
            TentativeSuicide       = GetOuiNonNsp(TsOui, TsNon, TsNsp),
            AntecedentsAutreLabel  = AntecedentsAutreLabel.Text.Trim(),
            AntecedentsAutre       = GetOuiNonNsp(AutreAtcdOui, AutreAtcdNon, AutreAtcdNsp),

            PhotoAutorise   = GetOuiNon(PhotoAutoriseOui, PhotoAutoriseNon),
            AutorUsageInfos = GetOuiNon(AutorInfosOui, AutorInfosNon),
            AutorSms        = GetOuiNon(AutorSmsOui, AutorSmsNon),
            AutorEmail      = GetOuiNon(AutorEmailOui, AutorEmailNon),

            LinkedDocumentPath = _pdfPath,
        };

        private async void BtnValider_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_patientDir)) { Close(); return; }

            BtnValider.IsEnabled = false;
            BtnAnnuler.IsEnabled = false;
            StatusLabel.Text = "Enregistrement...";

            var data = CollectForm();
            _service.Save(_patientDir, data);

            MergeIntoPatientMetadata(_patientDir, data);

            var (atcdMerged, atcdError) = await MergeAntecedentsFamiliauxAsync(_patientDir, data);

            StatusLabel.Text = atcdMerged
                ? "Enregistré ✓ — contacts, autorisations et antécédents familiaux intégrés au dossier."
                : $"Enregistré ✓ — contacts et autorisations intégrés. Antécédents familiaux non intégrés ({atcdError}).";

            DialogResult = true;
            Close();
        }

        // ── Intégration au dossier (Phase 5) ─────────────────────
        //
        // « Med propose → le médecin valide » : ces fusions ne se déclenchent qu'au clic sur
        // Valider, jamais automatiquement pendant la relecture.

        /// <summary>
        /// Écrit les contacts parents, l'adresse et les autorisations dans patient.json — un champ
        /// non renseigné dans le formulaire ne remplace pas une valeur déjà connue du dossier.
        /// </summary>
        private static void MergeIntoPatientMetadata(string patientDir, FormulaireData data)
        {
            var path = Path.Combine(patientDir, "info_patient", "patient.json");
            if (!File.Exists(path)) return;

            PatientMetadata? meta;
            try
            {
                meta = JsonSerializer.Deserialize<PatientMetadata>(File.ReadAllText(path, Encoding.UTF8), PatientJsonReadOptions);
            }
            catch
            {
                return; // patient.json corrompu : on ne touche à rien plutôt que d'écraser
            }
            if (meta == null) return;

            void SetIfGiven(string value, Action<string> setter)
            {
                if (!string.IsNullOrWhiteSpace(value)) setter(value.Trim());
            }

            SetIfGiven(data.PerePrenom, v => meta.PerePrenom = v);
            SetIfGiven(data.PereNom, v => meta.PereNom = v);
            SetIfGiven(data.PereTel, v => meta.PereTelephone = v);
            SetIfGiven(data.PereEmail, v => meta.PereEmail = v);

            SetIfGiven(data.MerePrenom, v => meta.MerePrenom = v);
            SetIfGiven(data.MereNom, v => meta.MereNom = v);
            SetIfGiven(data.MereTel, v => meta.MereTelephone = v);
            SetIfGiven(data.MereEmail, v => meta.MereEmail = v);

            SetIfGiven(data.Adresse, v => meta.AdresseRue = v);
            SetIfGiven(data.CodePostal, v => meta.AdresseCodePostal = v);
            SetIfGiven(data.Ville, v => meta.AdresseVille = v);

            if (!string.IsNullOrWhiteSpace(data.PhotoAutorise))
                meta.ConsentementPhoto = data.PhotoAutorise == "oui";

            if (!string.IsNullOrWhiteSpace(data.AutorUsageInfos))
                meta.AutorisationUsageInfos = data.AutorUsageInfos == "oui";
            if (!string.IsNullOrWhiteSpace(data.AutorSms))
                meta.AutorisationSms = data.AutorSms == "oui";
            if (!string.IsNullOrWhiteSpace(data.AutorEmail))
                meta.AutorisationEmail = data.AutorEmail == "oui";

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            File.WriteAllText(path, JsonSerializer.Serialize(meta, options), Encoding.UTF8);
        }

        /// <summary>
        /// Ajoute un paragraphe « Antécédents familiaux » dans la section « ATCDs médicaux » de la
        /// note de 1ère consultation la plus récente du patient (frontmatter JSON + corps markdown).
        /// Best-effort : si aucune 1ère consultation n'est trouvée, on prévient sans bloquer la
        /// sauvegarde du formulaire (elle reste dans formulaire_data.json de toute façon).
        /// </summary>
        private static async Task<(bool ok, string? error)> MergeAntecedentsFamiliauxAsync(
            string patientDir, FormulaireData data)
        {
            var lines = BuildAntecedentsFamiliauxLines(data);
            if (lines.Count == 0) return (true, null); // rien à ajouter, pas un échec

            var mdPath = FindLatestPremiereConsultationFile(patientDir);
            if (mdPath == null)
                return (false, "aucune 1ère consultation trouvée dans le dossier");

            try
            {
                var content = await File.ReadAllTextAsync(mdPath, Encoding.UTF8);

                var addendum = "\n\n**Antécédents familiaux (déclarés par les parents, formulaire du " +
                    $"{DateTime.Now:dd/MM/yyyy}) :**\n" + string.Join("\n", lines);

                var updated = InjectIntoBlocksJson(content, "atcds", addendum) ?? content;
                updated = InjectIntoMarkdownSection(updated, "ATCDs médicaux", addendum);

                await File.WriteAllTextAsync(mdPath, updated, Encoding.UTF8);
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private static List<string> BuildAntecedentsFamiliauxLines(FormulaireData d)
        {
            var items = new (string label, string value)[]
            {
                ("TDAH / troubles de l'attention", d.Tdah),
                ("Dyslexie / troubles des apprentissages", d.Dyslexie),
                ("Troubles du spectre de l'autisme (TSA)", d.Tsa),
                ("Troubles anxieux", d.TroublesAnxieux),
                ("Dépression", d.Depression),
                ("Bipolarité", d.Bipolarite),
                ("Addictions", d.Addictions),
                ("Tentative de suicide", d.TentativeSuicide),
            };

            var lines = items
                .Where(i => !string.IsNullOrWhiteSpace(i.value))
                .Select(i => $"- {i.label} : {LabelOuiNonNsp(i.value)}")
                .ToList();

            if (!string.IsNullOrWhiteSpace(d.AntecedentsAutre) && !string.IsNullOrWhiteSpace(d.AntecedentsAutreLabel))
                lines.Add($"- {d.AntecedentsAutreLabel.Trim()} : {LabelOuiNonNsp(d.AntecedentsAutre)}");

            return lines;
        }

        private static string LabelOuiNonNsp(string v) => v switch
        {
            "oui" => "oui",
            "non" => "non",
            "nsp" => "ne sait pas",
            _ => v
        };

        /// <summary>Trouve la note de 1ère consultation la plus récente (frontmatter
        /// <c>type: "consultation-premiere"</c>) dans les sous-dossiers {année}/notes du patient.</summary>
        private static string? FindLatestPremiereConsultationFile(string patientDir)
        {
            if (!Directory.Exists(patientDir)) return null;

            var candidates = new List<(DateTime date, string path)>();
            foreach (var yearDir in Directory.GetDirectories(patientDir))
            {
                var notesDir = Path.Combine(yearDir, "notes");
                if (!Directory.Exists(notesDir)) continue;

                foreach (var file in Directory.GetFiles(notesDir, "*_consultation.md"))
                {
                    var text = File.ReadAllText(file, Encoding.UTF8);
                    if (!text.Contains("type: \"consultation-premiere\"", StringComparison.Ordinal)) continue;

                    var dateMatch = Regex.Match(text, @"date:\s*""([^""]+)""");
                    var date = dateMatch.Success && DateTime.TryParse(dateMatch.Groups[1].Value, out var d)
                        ? d : File.GetLastWriteTime(file);
                    candidates.Add((date, file));
                }
            }

            return candidates.OrderByDescending(c => c.date).Select(c => c.path).FirstOrDefault();
        }

        /// <summary>
        /// Met à jour le <c>FreeText</c> du bloc <paramref name="blockKey"/> dans le JSON compact
        /// embarqué sous <c>interrogatoire_blocks_json: |</c> (frontmatter YAML) — pour que la note
        /// reste ré-éditable telle quelle si elle est rouverte.
        /// </summary>
        private static string? InjectIntoBlocksJson(string content, string blockKey, string addendum)
        {
            const string marker = "interrogatoire_blocks_json: |";
            var markerIdx = content.IndexOf(marker, StringComparison.Ordinal);
            if (markerIdx < 0) return null;

            var jsonLineStart = content.IndexOf('\n', markerIdx) + 1;
            if (jsonLineStart <= 0) return null;
            var jsonLineEnd = content.IndexOf('\n', jsonLineStart);
            if (jsonLineEnd < 0) return null;

            var jsonText = content.Substring(jsonLineStart, jsonLineEnd - jsonLineStart).TrimStart(' ');

            List<Dictionary<string, string>>? blocks;
            try
            {
                blocks = JsonSerializer.Deserialize<List<Dictionary<string, string>>>(jsonText);
            }
            catch
            {
                return null;
            }
            if (blocks == null) return null;

            var block = blocks.FirstOrDefault(b => b.TryGetValue("Key", out var k) && k == blockKey);
            if (block == null) return null;

            block["FreeText"] = (block.GetValueOrDefault("FreeText") ?? "").TrimEnd() + addendum;

            var newJsonLine = "  " + JsonSerializer.Serialize(blocks);
            return content.Substring(0, jsonLineStart) + newJsonLine + content.Substring(jsonLineEnd);
        }

        /// <summary>
        /// Insère <paramref name="addendum"/> dans le corps markdown, juste avant la fin de la
        /// section <c>## {sectionTitle}</c> — ou crée la section (avant "## Observations cliniques"
        /// si présent) si le bloc était vide et donc absent de la note.
        /// </summary>
        private static string InjectIntoMarkdownSection(string content, string sectionTitle, string addendum)
        {
            var heading = $"## {sectionTitle}";
            var idx = content.IndexOf(heading, StringComparison.Ordinal);

            if (idx < 0)
            {
                var obsIdx = content.IndexOf("## Observations cliniques", StringComparison.Ordinal);
                var insertion = $"{heading}\n{addendum.TrimStart()}\n\n";
                return obsIdx >= 0
                    ? content.Substring(0, obsIdx) + insertion + content.Substring(obsIdx)
                    : content.TrimEnd() + "\n\n" + insertion;
            }

            var searchStart = idx + heading.Length;
            var nextHeadingIdx = content.IndexOf("\n## ", searchStart, StringComparison.Ordinal);
            var insertPos = nextHeadingIdx >= 0 ? nextHeadingIdx : content.Length;
            return content.Substring(0, insertPos) + addendum + "\n" + content.Substring(insertPos);
        }

        private void BtnAnnuler_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ── Helpers ──────────────────────────────────────────────

        private static void SetRadio(string value, params (string key, System.Windows.Controls.RadioButton rb)[] pairs)
        {
            foreach (var (key, rb) in pairs)
                rb.IsChecked = key == value;
        }

        private static string GetRadio(params (string key, System.Windows.Controls.RadioButton rb)[] pairs)
        {
            foreach (var (key, rb) in pairs)
                if (rb.IsChecked == true) return key;
            return "";
        }

        private static void SetOuiNonNsp(string value,
            System.Windows.Controls.RadioButton oui,
            System.Windows.Controls.RadioButton non,
            System.Windows.Controls.RadioButton nsp)
        {
            oui.IsChecked = value == "oui";
            non.IsChecked = value == "non";
            nsp.IsChecked = value == "nsp";
        }

        private static string GetOuiNonNsp(
            System.Windows.Controls.RadioButton oui,
            System.Windows.Controls.RadioButton non,
            System.Windows.Controls.RadioButton nsp)
            => oui.IsChecked == true ? "oui" : non.IsChecked == true ? "non" : nsp.IsChecked == true ? "nsp" : "";

        private static void SetOuiNon(string value,
            System.Windows.Controls.RadioButton oui,
            System.Windows.Controls.RadioButton non)
        {
            oui.IsChecked = value == "oui";
            non.IsChecked = value == "non";
        }

        private static string GetOuiNon(
            System.Windows.Controls.RadioButton oui,
            System.Windows.Controls.RadioButton non)
            => oui.IsChecked == true ? "oui" : non.IsChecked == true ? "non" : "";
    }
}
