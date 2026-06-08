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

        // Détecte les titres de sections Markdown gras : **Titre** ou **Titre :**
        private static readonly Regex _sectionTitleRx = new Regex(
            @"^\s*\*\*([^*\n]{3,120})\*\*\s*$",
            RegexOptions.Multiline | RegexOptions.Compiled);

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
            var photoBase64 = LoadPatientPhotoBase64(patientNomComplet);
            var blocsHtml = BuildBlocsPages(dossier, coverFields, photoBase64);

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

        private string LoadPatientPhotoBase64(string patientNomComplet)
        {
            try
            {
                var patientRoot = _pathService.GetPatientRootDirectory(patientNomComplet);
                var photoPath   = Path.Combine(patientRoot, "photo.jpg");
                return File.Exists(photoPath) ? LoadBase64(photoPath) : "";
            }
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
            public string Prenom            = ""; // prénom brut depuis patient.json (jamais écrasé par le LLM)
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
                Prenom          = p.Prenom, // source fiable, jamais écrasée par le LLM
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

        // ── Pages 2-9 ────────────────────────────────────────────────────────

        private string BuildBlocsPages(DossierRestitutionInitial dossier, CoverFields coverFields, string photoBase64)
        {
            var sb = new StringBuilder();
            int pageNumber = 2;
            foreach (var bloc in dossier.Blocs)
            {
                if (bloc.Key == "couverture") continue;

                if (bloc.Key == "restitution_1page")
                {
                    sb.Append(BuildRestitution1PagePage(bloc, coverFields, photoBase64, pageNumber));
                }
                else
                {
                    var content = string.IsNullOrWhiteSpace(bloc.ContenuValide)
                        ? "<p class='placeholder'><em>(Section à compléter — utilisez le bouton ✨ Suggérer)</em></p>"
                        : MarkdownToHtmlLite(bloc.ContenuValide);

                    sb.AppendLine("<div class='page draft-page'>");
                    sb.AppendLine($"  <div class='page-num'>Page {pageNumber}/9</div>");
                    sb.AppendLine($"  <h1 class='draft-title'>{WebUtility.HtmlEncode(bloc.Titre)}</h1>");
                    sb.AppendLine($"  <div class='draft-meta'>Voix cible : <strong>{WebUtility.HtmlEncode(bloc.VoixCible)}</strong></div>");
                    sb.AppendLine($"  <div class='draft-content'>{content}</div>");
                    sb.AppendLine("</div>");
                }
                pageNumber++;
            }
            return sb.ToString();
        }

        // ── Page 2 : Restitution 1-page parents ─────────────────────────────

        private string BuildRestitution1PagePage(RestitutionBloc bloc, CoverFields f, string photoBase64, int pageNumber)
        {
            var sections = ParseRestitution1PageSections(bloc.ContenuValide ?? "");

            // Prénom depuis patient.json (fiable) — fallback sur premier mot de NomEnfant
            var parts  = (f.NomEnfant ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var prenom = (!string.IsNullOrWhiteSpace(f.Prenom) ? f.Prenom
                          : parts.Length > 0 ? parts[0] : "l'enfant").ToUpperInvariant();

            // Photo patient ou icône placeholder
            string photoHtml;
            if (!string.IsNullOrEmpty(photoBase64))
                photoHtml = $"<img src='data:image/jpeg;base64,{photoBase64}' class='rp-photo-img' />";
            else if (!string.IsNullOrEmpty(_iconPatientBase64))
                photoHtml = $"<div class='rp-photo-placeholder'><img src='data:image/png;base64,{_iconPatientBase64}' class='rp-photo-icon-img' /></div>";
            else
                photoHtml = "<div class='rp-photo-placeholder rp-photo-empty'><span>👤</span></div>";

            var sb = new StringBuilder();
            sb.AppendLine("<div class='page rp-page'>");
            sb.AppendLine($"  <div class='page-num'>Page {pageNumber}/9</div>");

            // ── Bandeau supérieur ────────────────────────────────────────────
            sb.AppendLine("  <div class='rp-header'>");
            sb.AppendLine("    <div class='rp-logo'>");
            if (!string.IsNullOrEmpty(_medcompanionHeartBase64))
                sb.AppendLine($"      <img src='data:image/png;base64,{_medcompanionHeartBase64}' class='rp-logo-img' />");
            sb.AppendLine("      <div class='rp-logo-text'><strong>MedCompanion</strong><br/><span>L'intelligence au service du soin</span></div>");
            sb.AppendLine("    </div>");
            sb.AppendLine("    <div class='rp-badge'>");
            if (!string.IsNullOrEmpty(_iconPedopsychiatrieBase64))
                sb.AppendLine($"      <img src='data:image/png;base64,{_iconPedopsychiatrieBase64}' class='rp-badge-icon' />");
            sb.AppendLine("      <div><strong>PÉDOPSYCHIATRIE</strong><br/><span>Évaluation · Compréhension · Accompagnement</span></div>");
            sb.AppendLine("    </div>");
            sb.AppendLine("  </div>");

            // ── Titre principal ──────────────────────────────────────────────
            sb.AppendLine("  <div class='rp-title-block'>");
            sb.AppendLine("    <h1 class='rp-main-title'>RESTITUTION AUX PARENTS</h1>");
            sb.AppendLine("    <p class='rp-subtitle'>Comprendre pour mieux accompagner votre enfant</p>");
            sb.AppendLine("  </div>");

            // ── Carte identité patient ───────────────────────────────────────
            sb.AppendLine("  <div class='rp-identity'>");
            sb.AppendLine($"    <div class='rp-photo-wrap'>{photoHtml}</div>");
            sb.AppendLine("    <div class='rp-identity-details'>");
            sb.AppendLine($"      <div class='rp-patient-name'>{WebUtility.HtmlEncode(f.NomEnfant)}</div>");
            if (!string.IsNullOrEmpty(f.DateNaissance))
            {
                var ageStr = string.IsNullOrEmpty(f.Age) ? "" : $" — <strong>{WebUtility.HtmlEncode(f.Age)}</strong>";
                sb.AppendLine($"      <div class='rp-identity-row'>Né(e) le <strong>{WebUtility.HtmlEncode(f.DateNaissance)}</strong>{ageStr}</div>");
            }
            if (!string.IsNullOrEmpty(f.Ecole))
            {
                var classeStr = string.IsNullOrEmpty(f.Classe) ? "" : $", {WebUtility.HtmlEncode(f.Classe)}";
                sb.AppendLine($"      <div class='rp-identity-row'>{WebUtility.HtmlEncode(f.Ecole)}{classeStr}</div>");
            }
            if (!string.IsNullOrEmpty(f.AnneeScolaire))
                sb.AppendLine($"      <div class='rp-identity-row'>Année scolaire {WebUtility.HtmlEncode(f.AnneeScolaire)}</div>");
            sb.AppendLine("    </div>");
            sb.AppendLine("  </div>");

            // ── Section 1 pleine largeur ─────────────────────────────────────
            sb.Append(RenderRpSection("1", "CE QUE NOUS AVONS COMPRIS", sections.Comprehension, "rp-s1"));

            // ── Sections 2+3 côte-à-côte ─────────────────────────────────────
            sb.AppendLine("  <div class='rp-two-col'>");
            sb.Append(RenderRpSection("2", $"LES FORCES DE {prenom}", sections.Forces, "rp-s2"));
            sb.Append(RenderRpSection("3", "LES DIFFICULTÉS ACTUELLEMENT OBSERVÉES", sections.Difficultes, "rp-s3"));
            sb.AppendLine("  </div>");

            // ── Section 4 pleine largeur ─────────────────────────────────────
            sb.Append(RenderRpSection("4", $"CE QUI PEUT AIDER {prenom}", sections.Aide, "rp-s4"));

            // ── Sections 5+6 côte-à-côte ─────────────────────────────────────
            sb.AppendLine("  <div class='rp-two-col'>");
            sb.Append(RenderRpSection("5", "NOTRE FEUILLE DE ROUTE", sections.FeuilleDeRoute, "rp-s5"));
            sb.Append(RenderRpSection("6", "SON ENVIRONNEMENT : POINTS CLÉS", sections.Environnement, "rp-s6"));
            sb.AppendLine("  </div>");

            sb.AppendLine("</div>");
            return sb.ToString();
        }

        private static string RenderRpSection(string num, string title, string mdContent, string cssClass)
        {
            var body = string.IsNullOrWhiteSpace(mdContent)
                ? "<p class='rp-placeholder'><em>Section à compléter — cliquez sur ✨ Suggérer</em></p>"
                : MarkdownToHtmlLite(mdContent);
            return
                $"<div class='rp-section {cssClass}'>" +
                $"<div class='rp-section-hdr'><span class='rp-num'>{WebUtility.HtmlEncode(num)}</span>" +
                $"<h2>{WebUtility.HtmlEncode(title)}</h2></div>" +
                $"<div class='rp-section-body'>{body}</div>" +
                $"</div>\n";
        }

        // Modèle interne pour les 6 sections de la page parents
        private class Restitution1PageSections
        {
            public string Comprehension  = "";
            public string Forces         = "";
            public string Difficultes    = "";
            public string Aide           = "";
            public string FeuilleDeRoute = "";
            public string Environnement  = "";
        }

        private static Restitution1PageSections ParseRestitution1PageSections(string md)
        {
            var result = new Restitution1PageSections();
            if (string.IsNullOrWhiteSpace(md)) return result;

            var matches = _sectionTitleRx.Matches(md);
            if (matches.Count == 0) { result.Comprehension = md; return result; }

            // Construit les segments (titre, contenu)
            var segments = new List<(string title, string content)>();

            // Texte d'introduction avant le premier titre gras
            if (matches[0].Index > 0)
            {
                var intro = md.Substring(0, matches[0].Index).Trim();
                if (!string.IsNullOrEmpty(intro)) segments.Add(("__intro__", intro));
            }

            for (int i = 0; i < matches.Count; i++)
            {
                var m = matches[i];
                var titleText = m.Groups[1].Value.Trim().TrimEnd(':', ' ');
                var start     = m.Index + m.Length;
                var end       = (i + 1 < matches.Count) ? matches[i + 1].Index : md.Length;
                segments.Add((titleText, md.Substring(start, end - start).Trim()));
            }

            // Classifie chaque segment dans l'une des 6 cases
            static void Append(ref string field, string title, string content, bool includeTitle)
            {
                var prefix = (includeTitle && title != "__intro__") ? $"**{title}**\n\n" : "";
                field += (field.Length > 0 ? "\n\n" : "") + prefix + content;
            }

            foreach (var (title, content) in segments)
            {
                var t = title.ToLowerInvariant();

                if (t == "__intro__" || t.Contains("compris") || t.Contains("compréhension") || t.Contains("portrait"))
                    Append(ref result.Comprehension, title, content, t != "__intro__");
                else if (t.Contains("forces") || t.Contains("réussites") || t.Contains("fonctionne bien") || t.Contains("atouts"))
                    Append(ref result.Forces, title, content, false);
                else if (t.Contains("défis") || t.Contains("difficultés") || t.Contains("vigilance") || t.Contains("surveiller"))
                    Append(ref result.Difficultes, title, content, false);
                else if (t.Contains("aider") || t.Contains("outils") || t.Contains("concrètement") || t.Contains("aide concr"))
                    Append(ref result.Aide, title, content, false);
                else if (t.Contains("feuille de route") || t.Contains("prochaines étapes") || t.Contains("prochaine"))
                    Append(ref result.FeuilleDeRoute, title, content, false);
                else if (t.Contains("environnement") || t.Contains("entourage") || t.Contains("points clés") || t.Contains("contexte"))
                    Append(ref result.Environnement, title, content, false);
                else
                    // Non classifié → annexé à la compréhension en fallback
                    Append(ref result.Comprehension, title, content, true);
            }

            return result;
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

/* ── Rendu PDF (Edge headless --print-to-pdf = contexte print) ── */
@media print {
  html, body { background: white !important; padding: 0 !important; margin: 0 !important; }
  .page {
    box-shadow: none !important;
    margin: 0 !important;
    page-break-after: always;
    break-after: page;
    height: 297mm;
    overflow: hidden;
  }
  .page:last-child { page-break-after: avoid; break-after: avoid; }
}

/* ── Page 2 : Restitution parents ────────────────────────── */
.rp-page { padding: 0; font-family: 'Segoe UI', Arial, sans-serif; font-size: 11px; color: #2C3E50; }

/* Bandeau haut */
.rp-header { display:flex; justify-content:space-between; align-items:center;
  background: linear-gradient(135deg,#1A3A6A 0%,#2D5FA6 100%);
  color:white; padding:8px 18px; }
.rp-logo { display:flex; align-items:center; gap:8px; }
.rp-logo-img { width:30px; height:30px; }
.rp-logo-text { font-size:12px; line-height:1.3; }
.rp-logo-text strong { font-size:13px; }
.rp-logo-text span { font-size:9px; opacity:0.8; }
.rp-badge { display:flex; align-items:center; gap:8px; text-align:right; }
.rp-badge-icon { width:26px; height:26px; }
.rp-badge strong { font-size:10px; letter-spacing:0.5px; display:block; }
.rp-badge span { font-size:8px; opacity:0.8; }

/* Titre principal */
.rp-title-block { padding:7px 18px 5px 18px; border-bottom:2.5px solid #2DC5A2; background:white; }
.rp-main-title { font-size:17px; font-weight:900; color:#1A3A6A; margin:0; letter-spacing:1px; }
.rp-subtitle { font-size:9px; color:#7F8C8D; margin:2px 0 0 0; font-style:italic; }

/* Carte identité */
.rp-identity { display:flex; align-items:center; gap:12px;
  background:#F7F9FC; border:1px solid #DDE8F5;
  margin:7px 14px 4px 14px; padding:9px 12px; border-radius:7px; }
.rp-photo-wrap { flex-shrink:0; }
.rp-photo-img { width:58px; height:58px; border-radius:50%; object-fit:cover; border:2.5px solid #2DC5A2; }
.rp-photo-placeholder { width:58px; height:58px; border-radius:50%; background:#DDE8F5;
  display:flex; align-items:center; justify-content:center; border:2px solid #B0C8E8; }
.rp-photo-icon-img { width:34px; height:34px; opacity:0.65; }
.rp-photo-empty { font-size:26px; }
.rp-patient-name { font-size:15px; font-weight:800; color:#1A3A6A; margin-bottom:3px; }
.rp-identity-row { font-size:10px; color:#5D6D7E; margin-bottom:2px; }

/* Sections */
.rp-section { margin:3px 10px; border-radius:6px; overflow:hidden; border:1px solid rgba(0,0,0,0.07); }
.rp-two-col { display:flex; }
.rp-two-col .rp-section { flex:1; margin:3px 6px; }
.rp-two-col .rp-section:first-child { margin-left:10px; }
.rp-two-col .rp-section:last-child  { margin-right:10px; }
.rp-section-hdr { display:flex; align-items:center; gap:7px; padding:5px 11px; color:white; }
.rp-section-hdr h2 { margin:0; font-size:9.5px; font-weight:800; letter-spacing:0.4px; text-transform:uppercase; }
.rp-num { font-size:16px; font-weight:900; opacity:0.9; line-height:1; flex-shrink:0; }
.rp-section-body { padding:7px 11px; font-size:10px; line-height:1.5; }
.rp-section-body h1,.rp-section-body h2,.rp-section-body h3 { font-size:10px; margin:5px 0 3px 0; }
.rp-section-body ul { margin:3px 0; padding-left:14px; }
.rp-section-body li { margin-bottom:3px; }
.rp-section-body p  { margin-bottom:5px; }
.rp-placeholder { color:#95A5A6; font-style:italic; }

/* Couleurs par section */
.rp-s1 .rp-section-hdr { background:#2E6DA4; }  .rp-s1 .rp-section-body { background:#EEF5FB; }
.rp-s2 .rp-section-hdr { background:#C87800; }  .rp-s2 .rp-section-body { background:#FEF9EE; }
.rp-s3 .rp-section-hdr { background:#B03020; }  .rp-s3 .rp-section-body { background:#FDEDEC; }
.rp-s4 .rp-section-hdr { background:#6E2F8A; }  .rp-s4 .rp-section-body { background:#F5EEF8; }
.rp-s5 .rp-section-hdr { background:#1A7840; }  .rp-s5 .rp-section-body { background:#EAFAF1; }
.rp-s6 .rp-section-hdr { background:#0E7060; }  .rp-s6 .rp-section-body { background:#E8F8F5; }
";
    }
}
