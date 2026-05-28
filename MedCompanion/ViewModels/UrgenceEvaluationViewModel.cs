using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using MedCompanion.Commands;
using MedCompanion.Models;
using MedCompanion.Models.Urgences;
using MedCompanion.Services;
using MedCompanion.Services.LLM;
using MedCompanion.Services.Urgence;
using MedCompanion.Services.Urgence.Templates;

namespace MedCompanion.ViewModels
{
    /// <summary>
    /// État du panneau d'évaluation structurée du risque suicidaire (Mode Urgence V0 Étape 4).
    /// Le médecin remplit les sections, sélectionne le niveau de risque, coche le plan d'action,
    /// et enregistre. Sortie : fichier .md immuable à côté du signal source.
    /// </summary>
    public class UrgenceEvaluationViewModel : INotifyPropertyChanged
    {
        private readonly UrgenceSignal _signal;
        private readonly UrgenceLogService _logService;
        private readonly ILLMService? _llmService;
        private readonly int? _patientAge;
        private readonly UrgenceTemplateService.AgeTier _ageTier;
        private readonly Action<string> _onSaved;   // callback (chemin du fichier créé)
        private readonly Action _onCancel;

        public UrgenceSignal Signal => _signal;

        public string PatientName       { get; }
        public string ConsultationLabel { get; }
        public string AgeLabel          { get; }
        public string AgeTierLabel      { get; }

        public ObservableCollection<UrgenceEvaluationSection> Sections { get; } = new();
        public ObservableCollection<UrgenceActionItem>        PlanActions { get; } = new();

        // ── Niveau de risque (radio) ─────────────────────────────────────────

        private UrgenceRiskLevel _riskLevel = UrgenceRiskLevel.NonRenseigne;
        public UrgenceRiskLevel RiskLevel
        {
            get => _riskLevel;
            set
            {
                if (SetProperty(ref _riskLevel, value))
                {
                    OnPropertyChanged(nameof(IsRiskFaible));
                    OnPropertyChanged(nameof(IsRiskModere));
                    OnPropertyChanged(nameof(IsRiskEleve));
                }
            }
        }

        public bool IsRiskFaible { get => RiskLevel == UrgenceRiskLevel.Faible; set { if (value) RiskLevel = UrgenceRiskLevel.Faible; } }
        public bool IsRiskModere { get => RiskLevel == UrgenceRiskLevel.Modere; set { if (value) RiskLevel = UrgenceRiskLevel.Modere; } }
        public bool IsRiskEleve  { get => RiskLevel == UrgenceRiskLevel.Eleve;  set { if (value) RiskLevel = UrgenceRiskLevel.Eleve;  } }

        // ── Revoyure ─────────────────────────────────────────────────────────

        private int _revoyureJours;
        public int RevoyureJours
        {
            get => _revoyureJours;
            set => SetProperty(ref _revoyureJours, value);
        }

        // ── Notes libres ─────────────────────────────────────────────────────

        private string _notesLibres = "";
        public string NotesLibres
        {
            get => _notesLibres;
            set => SetProperty(ref _notesLibres, value);
        }

        // ── Status ───────────────────────────────────────────────────────────

        private string _statusMessage = "";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        // ── Mode restitution (Étape 5) ───────────────────────────────────────

        private bool _isRestitutionMode;
        public bool IsRestitutionMode
        {
            get => _isRestitutionMode;
            set
            {
                if (SetProperty(ref _isRestitutionMode, value))
                    OnPropertyChanged(nameof(IsEvaluationMode));
            }
        }
        public bool IsEvaluationMode => !IsRestitutionMode;

        private string _restitutionIntro = "";
        public string RestitutionIntro { get => _restitutionIntro; set => SetProperty(ref _restitutionIntro, value); }

        private string _restitutionObserve = "";
        public string RestitutionObserve { get => _restitutionObserve; set => SetProperty(ref _restitutionObserve, value); }

        private string _restitutionRessources = "";
        public string RestitutionRessources { get => _restitutionRessources; set => SetProperty(ref _restitutionRessources, value); }

        private bool _isGeneratingRestitution;
        public bool IsGeneratingRestitution
        {
            get => _isGeneratingRestitution;
            set => SetProperty(ref _isGeneratingRestitution, value);
        }

        private string _restitutionStatus = "";
        public string RestitutionStatus { get => _restitutionStatus; set => SetProperty(ref _restitutionStatus, value); }

        private string _generatedPdfPath = "";
        public string GeneratedPdfPath { get => _generatedPdfPath; set => SetProperty(ref _generatedPdfPath, value); }

        public bool HasGeneratedPdf => !string.IsNullOrEmpty(GeneratedPdfPath);

        // ── Commands ─────────────────────────────────────────────────────────

        public ICommand SaveCommand                { get; }
        public ICommand CancelCommand              { get; }
        public ICommand BackToEvaluationCommand    { get; }
        public ICommand GenerateRestitutionCommand { get; }
        public ICommand ExportPdfCommand           { get; }
        public ICommand OpenGeneratedPdfCommand    { get; }

        public UrgenceEvaluationViewModel(
            UrgenceSignal signal,
            int? patientAge,
            string consultationLabel,
            string medecin,
            UrgenceLogService logService,
            Action<string> onSaved,
            Action onCancel,
            ILLMService? llmService = null)
        {
            _signal           = signal;
            _logService       = logService;
            _llmService       = llmService;
            _patientAge       = patientAge;
            _onSaved          = onSaved;
            _onCancel         = onCancel;
            Medecin           = medecin ?? "";
            PatientName       = signal.PatientNomComplet;
            ConsultationLabel = consultationLabel;
            AgeLabel          = patientAge.HasValue ? $"{patientAge.Value} ans" : "âge non confirmé";

            _ageTier = UrgenceTemplateService.TierFor(patientAge);
            AgeTierLabel = _ageTier switch
            {
                UrgenceTemplateService.AgeTier.JeuneEnfant => "< 7 ans — exploration indirecte",
                UrgenceTemplateService.AgeTier.Enfant      => "7-12 ans — version intermédiaire",
                _                                          => "≥ 13 ans — C-SSRS complet"
            };

            foreach (var s in UrgenceTemplateService.BuildSuicideRiskSections(patientAge))
                Sections.Add(s);
            foreach (var a in UrgenceTemplateService.BuildPlanActions())
                PlanActions.Add(a);

            SaveCommand                  = new RelayCommand(async _ => await SaveAsync(), _ => CanSave());
            CancelCommand                = new RelayCommand(_ => _onCancel());
            BackToEvaluationCommand      = new RelayCommand(_ => IsRestitutionMode = false);
            GenerateRestitutionCommand   = new RelayCommand(async _ => await GenerateRestitutionContentAsync(), _ => !IsGeneratingRestitution);
            ExportPdfCommand             = new RelayCommand(async _ => await ExportPdfAsync(),                 _ => !IsGeneratingRestitution && IsRestitutionMode);
            OpenGeneratedPdfCommand      = new RelayCommand(_ => OpenGeneratedPdf(), _ => !string.IsNullOrEmpty(GeneratedPdfPath) && File.Exists(GeneratedPdfPath));
        }

        public string Medecin { get; }

        private bool CanSave() => RiskLevel != UrgenceRiskLevel.NonRenseigne;

        private async Task SaveAsync()
        {
            try
            {
                var path = _logService.WriteEvaluation(
                    _signal, RiskLevel, Sections, PlanActions,
                    RevoyureJours, NotesLibres, Medecin);
                StatusMessage = $"Évaluation enregistrée : {Path.GetFileName(path)}";
                _onSaved(path);

                // Étape 5 : transition automatique en mode "Document parents"
                IsRestitutionMode = true;
                await GenerateRestitutionContentAsync();
            }
            catch (Exception ex)
            {
                StatusMessage = $"Erreur sauvegarde : {ex.Message}";
            }
        }

        // ── Étape 5 : génération LLM du contenu personnalisé ─────────────────

        private async Task GenerateRestitutionContentAsync()
        {
            if (_llmService == null)
            {
                RestitutionIntro      = DefaultIntro();
                RestitutionObserve    = "Précisez ici ce qui a été observé pendant la consultation.";
                RestitutionRessources = "Précisez ici les ressources que vous avez identifiées chez l'enfant.";
                RestitutionStatus     = "Pas de LLM disponible — contenu par défaut, à éditer manuellement.";
                return;
            }

            IsGeneratingRestitution = true;
            RestitutionStatus = "Génération IA en cours...";

            try
            {
                var prompt = BuildRestitutionPrompt();
                var (ok, raw, err) = await _llmService.GenerateTextAsync(prompt, maxTokens: 800);
                if (!ok || string.IsNullOrWhiteSpace(raw))
                {
                    RestitutionStatus     = $"Erreur LLM ({err}) — contenu par défaut.";
                    RestitutionIntro      = DefaultIntro();
                    RestitutionObserve    = "Précisez ici ce qui a été observé.";
                    RestitutionRessources = "Précisez ici les ressources identifiées.";
                    return;
                }

                ParseLlmRestitutionResponse(raw);
                RestitutionStatus = "Contenu généré — relisez et ajustez avant export.";
            }
            catch (Exception ex)
            {
                RestitutionStatus = $"Erreur : {ex.Message}";
            }
            finally
            {
                IsGeneratingRestitution = false;
            }
        }

        private string BuildRestitutionPrompt()
        {
            var checklistLines = new StringBuilder();
            foreach (var s in Sections)
            {
                var sel = s.Choices.FirstOrDefault(c => c.Code == s.SelectedChoiceCode);
                checklistLines.AppendLine($"- {s.Title} : {sel?.Label ?? "(non renseigné)"}");
                if (!string.IsNullOrWhiteSpace(s.FreeText))
                    checklistLines.AppendLine($"  Précisions : {s.FreeText.Trim()}");
            }

            var tierLabel = _ageTier switch
            {
                UrgenceTemplateService.AgeTier.JeuneEnfant => "jeune enfant (< 7 ans)",
                UrgenceTemplateService.AgeTier.Enfant      => "enfant (7-12 ans)",
                _                                          => "adolescent (≥ 13 ans)"
            };

            return $@"Tu es pédopsychiatre. Tu rédiges un document de restitution destiné aux PARENTS d'un {tierLabel}, à l'issue d'une consultation où un risque suicidaire a été évalué.

OBJECTIF : produire un texte court, BIENVEILLANT, SOBRE, qui synthétise ce qui a été observé et les ressources identifiées chez l'enfant. Tu t'adresses aux parents avec chaleur, sans jargon clinique, sans alarmisme inutile mais SANS minimiser non plus.

CONTRAINTES STRICTES :
- Tu ne mentionnes JAMAIS un niveau de risque (faible/modéré/élevé) — c'est en interne.
- Tu ne donnes JAMAIS de conseils techniques (sécurisation domicile, numéros d'urgence) — ils figurent dans des sections fixes du document.
- Vouvoiement des parents.
- Pas de superlatifs (""extrêmement"", ""très inquiétant"").
- Pas d'évocation explicite et brutale du mot ""suicide"" en première phrase de l'intro.

INFORMATIONS À TA DISPOSITION :

Niveau de risque évalué (en interne, ne pas mentionner) : {FormatRiskFr(RiskLevel)}

Checklist remplie par le médecin :
{checklistLines}

Notes libres du médecin :
{(string.IsNullOrWhiteSpace(NotesLibres) ? "(aucune)" : NotesLibres.Trim())}

RÉPONDS UNIQUEMENT PAR UN JSON VALIDE :
{{
  ""intro"": ""2 à 3 phrases d'ouverture chaleureuses, qui posent le cadre du document"",
  ""observe"": ""3 à 4 phrases synthétisant ce qui a été observé pendant la consultation, sans interprétation pesante"",
  ""ressources"": [""ressource 1"", ""ressource 2"", ""ressource 3""]
}}

Le tableau ""ressources"" contient 2 à 4 forces / ressources identifiables chez l'enfant (alliance familiale, intérêts, capacités de dialogue, soutiens, etc.). Si vraiment aucune ressource n'est identifiée, mets une seule entrée : ""À identifier ensemble lors des prochaines consultations.""";
        }

        private void ParseLlmRestitutionResponse(string raw)
        {
            var json = ExtractJsonObject(raw);
            if (string.IsNullOrEmpty(json)) return;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("intro", out var introEl)      && introEl.ValueKind == JsonValueKind.String)
                    RestitutionIntro = introEl.GetString() ?? "";
                if (root.TryGetProperty("observe", out var obsEl)      && obsEl.ValueKind == JsonValueKind.String)
                    RestitutionObserve = obsEl.GetString() ?? "";
                if (root.TryGetProperty("ressources", out var resEl)   && resEl.ValueKind == JsonValueKind.Array)
                {
                    var lines = new List<string>();
                    foreach (var e in resEl.EnumerateArray())
                        if (e.ValueKind == JsonValueKind.String)
                            lines.Add(e.GetString()?.Trim() ?? "");
                    RestitutionRessources = string.Join("\n", lines.Where(l => !string.IsNullOrWhiteSpace(l)));
                }
            }
            catch { /* fallback aux valeurs par défaut */ }
        }

        private static string ExtractJsonObject(string raw)
        {
            raw = System.Text.RegularExpressions.Regex.Replace(raw, @"```(?:json)?\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Replace("```", "");
            int start = raw.IndexOf('{');
            if (start < 0) return "";
            int depth = 0; bool inStr = false; bool esc = false;
            for (int i = start; i < raw.Length; i++)
            {
                var c = raw[i];
                if (esc) { esc = false; continue; }
                if (c == '\\') { esc = true; continue; }
                if (c == '"') { inStr = !inStr; continue; }
                if (inStr) continue;
                if (c == '{') depth++;
                else if (c == '}') { depth--; if (depth == 0) return raw.Substring(start, i - start + 1); }
            }
            return "";
        }

        private string DefaultIntro()
            => "Cette consultation nous a permis d'identifier des éléments qui demandent une vigilance partagée entre vous et nous. Ce document récapitule ce que nous avons observé ensemble et les repères qui peuvent aider à traverser cette période.";

        private static string FormatRiskFr(UrgenceRiskLevel r) => r switch
        {
            UrgenceRiskLevel.Faible => "Faible",
            UrgenceRiskLevel.Modere => "Modéré",
            UrgenceRiskLevel.Eleve  => "Élevé",
            _                       => "Non renseigné"
        };

        // ── Étape 5 : export PDF ─────────────────────────────────────────────

        private async Task ExportPdfAsync()
        {
            IsGeneratingRestitution = true;
            RestitutionStatus = "Génération du PDF...";

            try
            {
                var appDir       = AppDomain.CurrentDomain.BaseDirectory;
                var templatePath = Path.Combine(appDir, "Resources", "Consultation", "restitution_urgence_template.html");
                if (!File.Exists(templatePath))
                {
                    RestitutionStatus = "Template HTML introuvable.";
                    return;
                }
                var template = File.ReadAllText(templatePath, Encoding.UTF8);

                var doctorName = AppSettings.Load().Medecin;
                var html = template
                    .Replace("{{DOCTOR_NAME}}",          System.Net.WebUtility.HtmlEncode(doctorName))
                    .Replace("{{BANNER_URGENCE}}",       BuildBannerUrgence())
                    .Replace("{{INTRO}}",                System.Net.WebUtility.HtmlEncode(RestitutionIntro))
                    .Replace("{{OBSERVE}}",              ParagraphHtml(RestitutionObserve))
                    .Replace("{{RESSOURCES}}",           BuildListHtml(RestitutionRessources))
                    .Replace("{{REPERES}}",              BuildReperesHtml())
                    .Replace("{{PLAN_ACTION}}",          BuildPlanActionHtml())
                    .Replace("{{SECTION_SECURISATION}}", BuildSecurisationSection())
                    .Replace("{{MENTION_LEGALE}}",       BuildMentionLegale());

                // Destination : à côté du signal/note source (dossier notes/)
                var notesDir = Path.GetDirectoryName(_signal.NoteSourcePath) ?? Path.GetTempPath();
                var stamp    = DateTime.Now.ToString("yyyy-MM-dd_HHmmss");
                var htmlPath = Path.Combine(notesDir, $"{stamp}_restitution_urgence.html");
                var pdfPath  = Path.Combine(notesDir, $"{stamp}_restitution_urgence.pdf");
                File.WriteAllText(htmlPath, html, Encoding.UTF8);

                var edge = new EdgeHeadlessPdfService();
                if (edge.IsAvailable)
                {
                    var ok = await edge.ConvertAsync(htmlPath, pdfPath);
                    if (ok)
                    {
                        GeneratedPdfPath = pdfPath;
                        OnPropertyChanged(nameof(HasGeneratedPdf));
                        RestitutionStatus = $"PDF prêt : {Path.GetFileName(pdfPath)}";
                    }
                    else
                    {
                        RestitutionStatus = "Conversion PDF échouée — HTML disponible.";
                    }
                }
                else
                {
                    RestitutionStatus = "Microsoft Edge introuvable — seul le HTML est généré.";
                }
            }
            catch (Exception ex)
            {
                RestitutionStatus = $"Erreur export : {ex.Message}";
            }
            finally
            {
                IsGeneratingRestitution = false;
            }
        }

        private void OpenGeneratedPdf()
        {
            if (string.IsNullOrEmpty(GeneratedPdfPath) || !File.Exists(GeneratedPdfPath)) return;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = GeneratedPdfPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                RestitutionStatus = $"Impossible d'ouvrir le PDF : {ex.Message}";
            }
        }

        // ── Sections fixes / templates de contenu ────────────────────────────

        private string BuildBannerUrgence()
        {
            if (RiskLevel != UrgenceRiskLevel.Eleve) return "";
            return "<div class=\"banner-urgence\"><div class=\"icon\">⚠</div><div class=\"text\">" +
                   "<strong>Ce document est important.</strong> Conservez-le à portée. " +
                   "Les numéros d'urgence figurent en bas de la page." +
                   "</div></div>";
        }

        private string BuildReperesHtml()
        {
            var items = _ageTier switch
            {
                UrgenceTemplateService.AgeTier.JeuneEnfant => new[]
                {
                    "Maintenir un rythme stable et rassurant",
                    "Présence apaisée à proximité, contact corporel quand demandé",
                    "Limiter les écrans et la sur-stimulation",
                    "Favoriser le jeu, le dessin, la lecture partagée",
                    "Veiller au sommeil et aux repas"
                },
                UrgenceTemplateService.AgeTier.Enfant => new[]
                {
                    "Maintenir le dialogue, sans interroger en force",
                    "Limiter l'isolement (chambre fermée des heures durant)",
                    "Garder des repères quotidiens : repas, sommeil, école",
                    "Encourager les activités plaisirs (sport, créativité)",
                    "Présence calme, disponible, non-jugeante"
                },
                _ => new[]
                {
                    "Maintenir le dialogue, même bref, même imparfait",
                    "Respecter son besoin d'intimité tout en gardant un contact régulier",
                    "Préserver les routines : sommeil, repas, école",
                    "Limiter la solitude prolongée en chambre",
                    "Présence calme et non-jugeante",
                    "Vigilance sur les réseaux sociaux, sans intrusion brutale"
                }
            };
            return "<ul>" + string.Join("", items.Select(i => $"<li>{System.Net.WebUtility.HtmlEncode(i)}</li>")) + "</ul>";
        }

        private string BuildPlanActionHtml()
        {
            var cocheList = PlanActions.Where(a => a.IsChecked).Select(a => a.Label).ToList();
            if (RevoyureJours > 0) cocheList.Add($"Revoyure prévue dans {RevoyureJours} jour(s)");
            if (cocheList.Count == 0)
                return "<p><em>Le plan d'action sera précisé avec vous lors du prochain rendez-vous.</em></p>";
            return "<ul>" + string.Join("", cocheList.Select(l => $"<li>{System.Net.WebUtility.HtmlEncode(l)}</li>")) + "</ul>";
        }

        private string BuildSecurisationSection()
        {
            // Affiché uniquement si risque ≥ Modéré
            if (RiskLevel == UrgenceRiskLevel.Faible || RiskLevel == UrgenceRiskLevel.NonRenseigne) return "";

            var items = new[]
            {
                "Médicaments rangés hors de portée (placards verrouillés si possible)",
                "Alcool sécurisé ou tenu à l'écart",
                "Objets tranchants rangés (rasoirs, cutters, ciseaux pointus)",
                "Cordes, ceintures, écharpes longues : à l'écart",
                "Accès à des hauteurs (balcon, fenêtres) : vigilance renforcée"
            };
            return "<div class=\"section-full section-securisation\">" +
                   "<div class=\"section-title\">🏠 Sécurisation du domicile</div>" +
                   "<ul>" + string.Join("", items.Select(i => $"<li>{System.Net.WebUtility.HtmlEncode(i)}</li>")) + "</ul>" +
                   "</div>";
        }

        private string BuildMentionLegale()
            => "<div class=\"legal\">⚖️ Ce document est confidentiel, destiné uniquement aux titulaires de l'autorité parentale. Il ne se substitue pas à une consultation médicale en cas d'aggravation. Conservez-le à portée.</div>";

        private static string ParagraphHtml(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "<p><em>À compléter.</em></p>";
            var paragraphs = text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
            return string.Join("", paragraphs.Select(p => $"<p>{System.Net.WebUtility.HtmlEncode(p.Trim()).Replace("\n", "<br/>")}</p>"));
        }

        private static string BuildListHtml(string multilineText)
        {
            if (string.IsNullOrWhiteSpace(multilineText))
                return "<p><em>À compléter.</em></p>";
            var lines = multilineText.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                                     .Select(l => l.TrimStart('-', '•', '*', ' ', '\t').Trim())
                                     .Where(l => !string.IsNullOrWhiteSpace(l))
                                     .ToList();
            if (lines.Count == 0) return "<p><em>À compléter.</em></p>";
            return "<ul>" + string.Join("", lines.Select(l => $"<li>{System.Net.WebUtility.HtmlEncode(l)}</li>")) + "</ul>";
        }

        // ── INPC ─────────────────────────────────────────────────────────────

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? prop = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(prop));
        private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? prop = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(prop);
            return true;
        }
    }
}
