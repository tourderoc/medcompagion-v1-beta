using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MedCompanion.Models.Restitutions;

namespace MedCompanion.Services.Restitutions
{
    /// <summary>
    /// Produit un HTML d'aperçu (preview live) du Dossier de Restitution Clinique pour
    /// affichage dans un WebView2 à droite du panneau d'édition. Le HTML est identique
    /// (à un cosmétique près) à celui qui sera converti en PDF à la validation finale,
    /// ce qui permet au médecin de voir « ce que verront les parents » pendant qu'il édite.
    ///
    /// V1 : page 1 (couverture) basée sur le template restitution_clinique_template.html
    /// + une page simple par bloc avec son contenu Markdown rendu en HTML basique.
    /// Au fur et à mesure que chaque section sera retravaillée, les pages dédiées
    /// remplaceront ces pages-brouillon.
    /// </summary>
    public class RestitutionHtmlPreviewService
    {
        private readonly PathService _pathService;

        // Cache des assets binaires lus une fois (fonts + image arbre + logo) — économise des
        // Mo de lecture disque à chaque refresh de la preview live.
        private string? _treeBase64;
        private string? _caveatLatinBase64;
        private string? _caveatLatinExtBase64;
        private string? _medcompanionHeartBase64;
        private string? _signatureBase64;
        private string? _coverTemplateRaw;

        private string? _iconPatientBase64;
        private string? _iconNaissanceBase64;
        private string? _iconScolariteBase64;
        private string? _iconConsultationBase64;
        private string? _iconEvaluationBase64;
        private string? _iconRestitutionBase64;
        private string? _iconComprehensionBase64;
        private string? _iconReperesBase64;
        private string? _iconObjectifsBase64;
        private string? _iconCollaborationBase64;
        private string? _iconPerspectivesBase64;
        private string? _iconConfidentielBase64;
        private string? _iconPedopsychiatrieBase64;

        public RestitutionHtmlPreviewService(PathService pathService)
        {
            _pathService = pathService;
        }

        public string BuildPreviewHtml(DossierRestitutionInitial dossier, string patientNomComplet)
        {
            EnsureAssetsLoaded();

            // Source 1 : patient.json — identité administrative qui ne dépend pas de Med.
            var patientInfo = LoadPatientInfo(patientNomComplet);

            // Source 2 : bloc 1 « couverture » rédigé par Med — surcharge les champs cliniques
            // (école, classe, motif, dates d'évaluation) issus de la 1ère consultation et des
            // évaluations. Tant que ce bloc n'est pas généré, ces champs restent vides.
            var blocCouverture = dossier.Blocs.FirstOrDefault(b => b.Key == "couverture");
            var coverFields    = ParseCoverFieldsFromBloc(blocCouverture?.ContenuValide ?? "", patientInfo);

            var coverHtml = BuildCoverPage(coverFields);
            var blocsHtml = BuildBlocsPages(dossier);

            var sb = new StringBuilder();
            sb.AppendLine("<!DOCTYPE html><html lang='fr'><head><meta charset='UTF-8'/>");
            sb.AppendLine("<style>");
            sb.AppendLine(BuildWrapperCss());
            sb.AppendLine("</style></head><body>");
            sb.Append(coverHtml);
            sb.Append(blocsHtml);
            sb.AppendLine("</body></html>");
            return sb.ToString();
        }

        // ── Assets ──────────────────────────────────────────────────────────

        private void EnsureAssetsLoaded()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _treeBase64              ??= LoadBase64(Path.Combine(baseDir, "Assets", "PdfGraphics", "cover_tree.png"));
            _caveatLatinBase64       ??= LoadBase64(Path.Combine(baseDir, "Resources", "Consultation", "Fonts", "Caveat-Regular-latin.woff2"));
            _caveatLatinExtBase64    ??= LoadBase64(Path.Combine(baseDir, "Resources", "Consultation", "Fonts", "Caveat-Regular-latin-ext.woff2"));
            _medcompanionHeartBase64 ??= LoadBase64(Path.Combine(baseDir, "Resources", "Consultation", "Icons", "medcompanion_heart.png"));
            _signatureBase64         ??= LoadBase64(Path.Combine(baseDir, "Assets", "signature.png"));
            _coverTemplateRaw        ??= SafeReadAllText(Path.Combine(baseDir, "Resources", "Consultation", "restitution_clinique_template.html"));

            var iconsDir = Path.Combine(baseDir, "Resources", "Consultation", "Icons");
            _iconPatientBase64         ??= LoadBase64(Path.Combine(iconsDir, "mc_patient.png"));
            _iconNaissanceBase64       ??= LoadBase64(Path.Combine(iconsDir, "mc_naissance_calendar.png"));
            _iconScolariteBase64       ??= LoadBase64(Path.Combine(iconsDir, "mc_scolarite_school.png"));
            _iconConsultationBase64    ??= LoadBase64(Path.Combine(iconsDir, "mc_consultation_stethoscope.png"));
            _iconEvaluationBase64      ??= LoadBase64(Path.Combine(iconsDir, "mc_naissance_clipboard.png"));
            _iconRestitutionBase64     ??= LoadBase64(Path.Combine(iconsDir, "mc_restitution_graph.png"));
            _iconComprehensionBase64   ??= LoadBase64(Path.Combine(iconsDir, "mc_bienveillance_hands.png"));
            _iconReperesBase64         ??= LoadBase64(Path.Combine(iconsDir, "mc_reperes_pins.png"));
            _iconObjectifsBase64       ??= LoadBase64(Path.Combine(iconsDir, "mc_resolution_checklist.png"));
            _iconCollaborationBase64   ??= LoadBase64(Path.Combine(iconsDir, "mc_collaboration_people.png"));
            _iconPerspectivesBase64    ??= LoadBase64(Path.Combine(iconsDir, "mc_perspectives_chart.png"));
            _iconConfidentielBase64    ??= LoadBase64(Path.Combine(iconsDir, "mc_confidentialite_shield.png"));
            _iconPedopsychiatrieBase64 ??= LoadBase64(Path.Combine(iconsDir, "mc_medical_head.png"));
        }

        private static string LoadBase64(string path)
        {
            try { return File.Exists(path) ? Convert.ToBase64String(File.ReadAllBytes(path)) : ""; }
            catch { return ""; }
        }

        private static string SafeReadAllText(string path)
        {
            try { return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : ""; }
            catch { return ""; }
        }

        // ── Informations patient (depuis patient.json) ──────────────────────

        private class PatientInfoLite
        {
            public string Nom        = "";
            public string Prenom     = "";
            public string Dob        = "";
            public string Ecole      = "";
            public string Classe     = "";
        }

        /// <summary>
        /// Champs qui alimentent les placeholders de la couverture. Construit à partir de
        /// patient.json (admin) + bloc 1 rédigé par Med (clinique contextuel). Un champ
        /// vide → placeholder neutre dans la preview.
        /// </summary>
        private class CoverFields
        {
            public string NomEnfant         = "";
            public string DateNaissance     = "";
            public string Age               = "";
            public string Classe            = "";
            public string Ecole             = "";
            public string AnneeScolaire     = "2025-2026";
            public string Motif             = "";
            public string DatesEvaluation   = "";
            public string DateRestitution   = "";
        }

        /// <summary>
        /// Parse le contenu Markdown du bloc 1 (généré par Med selon le prompt structuré)
        /// pour extraire les champs labellisés `**Label** : valeur`. Les champs vides ou
        /// non générés tombent en fallback sur patient.json (uniquement pour Nom/DDN/École/Classe).
        /// </summary>
        private CoverFields ParseCoverFieldsFromBloc(string blocContent, PatientInfoLite p)
        {
            var fields = new CoverFields
            {
                // Défauts depuis patient.json — seront éventuellement surchargés par le bloc 1.
                NomEnfant       = $"{p.Prenom} {p.Nom}".Trim(),
                DateNaissance   = FormatDob(p.Dob),
                Age             = ComputeAge(p.Dob),
                Ecole           = p.Ecole,
                Classe          = p.Classe,
                DateRestitution = DateTime.Now.ToString("dd/MM/yyyy"),
            };

            if (string.IsNullOrWhiteSpace(blocContent)) return fields;

            // Recherche `**Label** : valeur` (insensible à la casse pour le label).
            string? Pick(params string[] labels)
            {
                foreach (var lbl in labels)
                {
                    var m = Regex.Match(blocContent,
                        $@"\*\*\s*{Regex.Escape(lbl)}\s*\*\*\s*:\s*(.+?)(?:\r?\n|$)",
                        RegexOptions.IgnoreCase);
                    if (m.Success)
                    {
                        var v = m.Groups[1].Value.Trim();
                        if (!string.IsNullOrWhiteSpace(v) &&
                            !v.Equals("Non renseigné", StringComparison.OrdinalIgnoreCase) &&
                            !v.Equals("Non renseignée", StringComparison.OrdinalIgnoreCase))
                            return v;
                    }
                }
                return null;
            }

            fields.NomEnfant       = Pick("Nom et prénom", "Nom", "Prénom et nom")           ?? fields.NomEnfant;
            fields.DateNaissance   = Pick("Date de naissance", "Naissance")                   ?? fields.DateNaissance;
            fields.Age             = Pick("Âge", "Age")                                       ?? fields.Age;
            fields.Ecole           = Pick("Établissement", "Etablissement", "École", "Ecole") ?? fields.Ecole;
            fields.Classe          = Pick("Classe", "Niveau scolaire")                        ?? fields.Classe;
            fields.AnneeScolaire   = Pick("Année scolaire", "Annee scolaire")                 ?? fields.AnneeScolaire;
            fields.Motif           = Pick("Motif de consultation", "Motif")                   ?? fields.Motif;
            fields.DatesEvaluation = Pick("Dates d'évaluation", "Date d'évaluation",
                                          "Dates d'evaluation", "Évaluations")                ?? fields.DatesEvaluation;

            return fields;
        }

        private PatientInfoLite LoadPatientInfo(string patientNomComplet)
        {
            var result = new PatientInfoLite();
            try
            {
                var jsonPath = _pathService.GetPatientJsonPath(patientNomComplet);
                if (!File.Exists(jsonPath)) return result;
                var raw = File.ReadAllText(jsonPath, Encoding.UTF8);
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;

                if (root.TryGetProperty("Nom", out var nom))       result.Nom    = nom.GetString() ?? "";
                if (root.TryGetProperty("Prenom", out var prenom)) result.Prenom = prenom.GetString() ?? "";
                if (root.TryGetProperty("Dob", out var dob))       result.Dob    = dob.GetString() ?? "";
                if (root.TryGetProperty("Ecole", out var ecole))   result.Ecole  = ecole.GetString() ?? "";
                if (root.TryGetProperty("Classe", out var classe)) result.Classe = classe.GetString() ?? "";
            }
            catch { /* ignore parse errors */ }
            return result;
        }

        private static string FormatDob(string dobIso)
        {
            if (string.IsNullOrWhiteSpace(dobIso)) return "Non renseignée";
            if (DateTime.TryParse(dobIso, out var d)) return d.ToString("dd/MM/yyyy");
            return dobIso;
        }

        private static string ComputeAge(string dobIso)
        {
            if (!DateTime.TryParse(dobIso, out var d)) return "";
            var today = DateTime.Today;
            var age = today.Year - d.Year;
            if (today < d.AddYears(age)) age--;
            return age >= 0 ? $"Âge : {age} ans" : "";
        }

        // ── Page 1 : couverture (template existant + substitutions) ─────────

        private string BuildCoverPage(CoverFields f)
        {
            var html = _coverTemplateRaw ?? "";
            if (string.IsNullOrEmpty(html)) return "<div class='page'>(Template couverture introuvable)</div>";

            // Helper : si la valeur est vide ou "Non renseigné", on affiche un placeholder
            // gris discret pour bien montrer que Med doit générer cette info.
            string Display(string value, string fallback)
                => string.IsNullOrWhiteSpace(value)
                    ? $"<span style='color:#B0B0B0;font-style:italic'>{fallback}</span>"
                    : WebUtility.HtmlEncode(value);

            html = html
                .Replace("{{NOM_ENFANT}}",             Display(f.NomEnfant,       "(à générer)"))
                .Replace("{{DATE_NAISSANCE}}",         Display(f.DateNaissance,   "(à générer)"))
                .Replace("{{AGE}}",                     WebUtility.HtmlEncode(f.Age))
                .Replace("{{CLASSE}}",                  Display(f.Classe,         "(à générer)"))
                .Replace("{{ECOLE}}",                   Display(f.Ecole,          "(à générer)"))
                .Replace("{{ANNEE_SCOLAIRE}}",         "Année scolaire " + WebUtility.HtmlEncode(string.IsNullOrWhiteSpace(f.AnneeScolaire) ? "2025-2026" : f.AnneeScolaire))
                .Replace("{{MOTIF_CONSULTATION}}",      Display(f.Motif,           "(à générer)"))
                .Replace("{{DATES_EVALUATION}}",        Display(f.DatesEvaluation, "(à générer)"))
                .Replace("{{DATE_RESTITUTION}}",        WebUtility.HtmlEncode(f.DateRestitution))
                .Replace("{{TREE_BASE64}}",                _treeBase64 ?? "")
                .Replace("{{CAVEAT_LATIN_BASE64}}",        _caveatLatinBase64 ?? "")
                .Replace("{{CAVEAT_LATIN_EXT_BASE64}}",    _caveatLatinExtBase64 ?? "")
                .Replace("{{MEDCOMPANION_HEART_BASE64}}",  _medcompanionHeartBase64 ?? "")
                .Replace("{{ICON_PATIENT_BASE64}}",         _iconPatientBase64 ?? "")
                .Replace("{{ICON_NAISSANCE_BASE64}}",       _iconNaissanceBase64 ?? "")
                .Replace("{{ICON_SCOLARITE_BASE64}}",       _iconScolariteBase64 ?? "")
                .Replace("{{ICON_CONSULTATION_BASE64}}",    _iconConsultationBase64 ?? "")
                .Replace("{{ICON_EVALUATION_BASE64}}",      _iconEvaluationBase64 ?? "")
                .Replace("{{ICON_RESTITUTION_BASE64}}",     _iconRestitutionBase64 ?? "")
                .Replace("{{ICON_COMPREHENSION_BASE64}}",   _iconComprehensionBase64 ?? "")
                .Replace("{{ICON_REPERES_BASE64}}",         _iconReperesBase64 ?? "")
                .Replace("{{ICON_OBJECTIFS_BASE64}}",       _iconObjectifsBase64 ?? "")
                .Replace("{{ICON_COLLABORATION_BASE64}}",   _iconCollaborationBase64 ?? "")
                .Replace("{{ICON_PERSPECTIVES_BASE64}}",    _iconPerspectivesBase64 ?? "")
                .Replace("{{ICON_CONFIDENTIEL_BASE64}}",    _iconConfidentielBase64 ?? "")
                .Replace("{{ICON_PEDOPSYCHIATRIE_BASE64}}", _iconPedopsychiatrieBase64 ?? "")
                .Replace("{{SIGNATURE_BASE64}}",            _signatureBase64 ?? "");

            // Extraire le <body>…</body> du template pour le coller dans notre wrapper.
            var bodyStart = html.IndexOf("<body>", StringComparison.OrdinalIgnoreCase);
            var bodyEnd   = html.IndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (bodyStart >= 0 && bodyEnd > bodyStart)
            {
                var inner = html.Substring(bodyStart + 6, bodyEnd - bodyStart - 6);

                // Extraire le <style>…</style> du template et SCOPER les sélecteurs `body`
                // vers `.cover-page` — sans ça, les styles A4 (width, padding…) ne
                // s'appliqueraient pas à notre <div> et le rendu s'effondrerait.
                var styleStart = html.IndexOf("<style>", StringComparison.OrdinalIgnoreCase);
                var styleEnd   = html.IndexOf("</style>", StringComparison.OrdinalIgnoreCase);
                var style = "";
                if (styleStart >= 0 && styleEnd > styleStart)
                {
                    var rawCss = html.Substring(styleStart + 7, styleEnd - styleStart - 7);
                    var scopedCss = ScopeBodySelectorToCoverPage(rawCss);
                    style = "<style>" + scopedCss + "</style>";
                }

                return $"<div class='page cover-page'>{style}{inner}</div>";
            }
            return $"<div class='page'>{html}</div>";
        }

        /// <summary>
        /// Remplace toutes les occurrences du sélecteur CSS `body` à la racine
        /// (début de règle) par `.cover-page`. Préserve les @font-face, @page,
        /// les sélecteurs imbriqués (ex: `.foo body` qu'on ne touche pas), et les
        /// noms de propriétés (ex: `font-family` n'est PAS un sélecteur).
        /// </summary>
        private static string ScopeBodySelectorToCoverPage(string css)
        {
            // Remplace au DEBUT d'une règle : `body {` ou `body,` ou `body\n{`
            // mais pas `bodyfoo` ou `.foo body`. On utilise des motifs ancrés sur
            // début de ligne / après accolade / après virgule.
            css = System.Text.RegularExpressions.Regex.Replace(
                css,
                @"(^|[\n\r;,}\s])body(\s*[,{])",
                "$1.cover-page$2",
                System.Text.RegularExpressions.RegexOptions.Multiline);
            return css;
        }

        // ── Pages 2-9 : un bloc par page (brouillon) ────────────────────────

        private string BuildBlocsPages(DossierRestitutionInitial dossier)
        {
            var sb = new StringBuilder();
            int pageNumber = 2;
            foreach (var bloc in dossier.Blocs)
            {
                // Couverture déjà rendue en page 1 — on saute le bloc « couverture ».
                if (bloc.Key == "couverture") continue;

                var content = string.IsNullOrWhiteSpace(bloc.ContenuValide)
                    ? "<p class='placeholder'><em>(Section à compléter — utilisez le bouton ✨ Suggérer)</em></p>"
                    : MarkdownToHtmlLite(bloc.ContenuValide);

                sb.AppendLine("<div class='page draft-page'>");
                sb.AppendLine($"  <div class='page-num'>Page {pageNumber}/9</div>");
                sb.AppendLine($"  <h1 class='draft-title'>{WebUtility.HtmlEncode(bloc.Titre)}</h1>");
                sb.AppendLine($"  <div class='draft-meta'>Voix cible : <strong>{WebUtility.HtmlEncode(bloc.VoixCible)}</strong></div>");
                sb.AppendLine($"  <div class='draft-content'>{content}</div>");
                sb.AppendLine("</div>");
                pageNumber++;
            }
            return sb.ToString();
        }

        /// <summary>
        /// Conversion Markdown → HTML très simple (titres, gras, listes à puces).
        /// V1 : suffisant pour la preview. Si on veut un rendu plus complet, on
        /// utilisera une lib type Markdig.
        /// </summary>
        private static string MarkdownToHtmlLite(string md)
        {
            if (string.IsNullOrWhiteSpace(md)) return "";

            var sb = new StringBuilder();
            var lines = md.Replace("\r\n", "\n").Split('\n');
            bool inList = false;

            foreach (var raw in lines)
            {
                var line = raw.TrimEnd();

                if (line.StartsWith("- ") || line.StartsWith("* "))
                {
                    if (!inList) { sb.AppendLine("<ul>"); inList = true; }
                    sb.AppendLine($"<li>{InlineMd(line.Substring(2))}</li>");
                    continue;
                }
                if (inList) { sb.AppendLine("</ul>"); inList = false; }

                if (line.StartsWith("### "))     sb.AppendLine($"<h3>{InlineMd(line.Substring(4))}</h3>");
                else if (line.StartsWith("## ")) sb.AppendLine($"<h2>{InlineMd(line.Substring(3))}</h2>");
                else if (line.StartsWith("# "))  sb.AppendLine($"<h1>{InlineMd(line.Substring(2))}</h1>");
                else if (string.IsNullOrWhiteSpace(line)) sb.AppendLine("<br/>");
                else                              sb.AppendLine($"<p>{InlineMd(line)}</p>");
            }
            if (inList) sb.AppendLine("</ul>");
            return sb.ToString();
        }

        private static string InlineMd(string s)
        {
            s = WebUtility.HtmlEncode(s);
            // **gras** → <strong>
            s = System.Text.RegularExpressions.Regex.Replace(s, @"\*\*([^*]+)\*\*", "<strong>$1</strong>");
            // *italique* → <em>
            s = System.Text.RegularExpressions.Regex.Replace(s, @"(?<!\*)\*(?!\*)([^*]+)(?<!\*)\*(?!\*)", "<em>$1</em>");
            return s;
        }

        // ── CSS du wrapper de preview ───────────────────────────────────────

        private static string BuildWrapperCss() => @"
* { box-sizing: border-box; }
html, body { margin: 0; padding: 0; background: #DDD; }
body { font-family: 'Nunito', 'Segoe UI', Arial, sans-serif; padding: 20px; }
.page {
  background: white;
  width: 210mm;
  min-height: 297mm;
  margin: 0 auto 20px auto;
  box-shadow: 0 4px 12px rgba(0,0,0,0.15);
  position: relative;
  overflow: hidden;
}
.cover-page { padding: 0; /* le template gère son propre padding */ }
.draft-page {
  padding: 40px 50px;
  color: #0F2D52;
}
.page-num {
  position: absolute;
  top: 20px;
  right: 30px;
  font-size: 11px;
  color: #95A5A6;
  text-transform: uppercase;
  letter-spacing: 1px;
}
.draft-title {
  font-size: 26px;
  font-weight: 800;
  color: #1F618D;
  text-transform: uppercase;
  border-bottom: 3px solid #2DC5A2;
  padding-bottom: 10px;
  margin-bottom: 8px;
}
.draft-meta { font-size: 11px; color: #7F8C8D; font-style: italic; margin-bottom: 24px; }
.draft-content { font-size: 13px; line-height: 1.6; color: #34495E; }
.draft-content h1 { font-size: 18px; margin: 16px 0 8px 0; color: #2C3E50; }
.draft-content h2 { font-size: 15px; margin: 14px 0 6px 0; color: #2C3E50; }
.draft-content h3 { font-size: 13px; margin: 12px 0 4px 0; color: #34495E; font-weight: 700; }
.draft-content ul { margin: 8px 0; padding-left: 24px; }
.draft-content li { margin-bottom: 4px; }
.draft-content p { margin-bottom: 8px; }
.placeholder { color: #95A5A6; }
";
    }
}
