using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MedCompanion.Models.Evaluations;
using MedCompanion.Models.Restitutions;
using MedCompanion.Services.Evaluations;

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
        private readonly EvaluationPhaseService? _evaluationService;

        // Cache de l'évaluation clôturée lue une fois par patient (pour Cartographie de l'enfant).
        // Évite de relire et reparser le YAML à chaque refresh de la preview live.
        private string? _cachedEvalPatient;
        private EvaluationPhase? _cachedEvalPhase;

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

        public RestitutionHtmlPreviewService(
            PathService pathService,
            EvaluationPhaseService? evaluationService = null)
        {
            _pathService       = pathService;
            _evaluationService = evaluationService;
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
            var blocsHtml = BuildBlocsPages(dossier, coverFields, photoBase64, patientNomComplet);

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
                // 1. Chercher dans le dossier de l'année en cours (là où PatientPhotoService l'enregistre)
                var yearDir   = _pathService.GetPatientYearDirectory(patientNomComplet);
                var photoPath = Path.Combine(yearDir, "photo.jpg");
                if (File.Exists(photoPath))
                    return LoadBase64(photoPath);

                // 2. Fallback sur la racine du patient au cas où
                var patientRoot = _pathService.GetPatientRootDirectory(patientNomComplet);
                photoPath = Path.Combine(patientRoot, "photo.jpg");
                if (File.Exists(photoPath))
                    return LoadBase64(photoPath);

                return "";
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

                // patient.json existe en deux variantes : camelCase (nouveaux) et PascalCase (anciens)
                static string PickStr(JsonElement r, string camel, string pascal)
                {
                    if (r.TryGetProperty(camel,  out var a) && a.ValueKind == JsonValueKind.String) return a.GetString() ?? "";
                    if (r.TryGetProperty(pascal, out var b) && b.ValueKind == JsonValueKind.String) return b.GetString() ?? "";
                    return "";
                }
                result.Nom    = PickStr(root, "nom",   "Nom");
                result.Prenom = PickStr(root, "prenom","Prenom");
                result.Dob    = PickStr(root, "dob",   "Dob");
                result.Ecole  = PickStr(root, "ecole", "Ecole");
                result.Classe = PickStr(root, "classe","Classe");
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

        private string BuildBlocsPages(DossierRestitutionInitial dossier, CoverFields coverFields, string photoBase64, string patientNomComplet)
        {
            var sb = new StringBuilder();
            int pageNumber = 2;

            // Total de pages logiques pour les en-têtes "Page N/total".
            // 1 (couverture) + 1 (restitution 1-page) + 2 (patient & contexte A+B) + 2 (cartographie enfant A+B)
            // + 5 autres blocs (synthese_diag, bilan_final, synthese_globale, projet_therapeutique, conclusion) = 11.
            // À mettre à jour quand on ajoute des pages dédiées.
            int totalPages = 11;

            // Les 5 blocs patient_* sont rendus ensemble sur 2 pages dédiées (A : identification +
            // motif + contexte familial / B : antécédents + situation actuelle). On les capture
            // au passage de la boucle puis on consomme l'ensemble en une seule fois, en sautant
            // les 4 occurrences suivantes pour ne pas générer de pages brouillon parasites.
            var patientBlocs = new Dictionary<string, RestitutionBloc>();
            foreach (var b in dossier.Blocs)
            {
                if (b.Key.StartsWith("patient_", StringComparison.Ordinal))
                    patientBlocs[b.Key] = b;
            }

            // Détecter si la page C (détail parcours de soins) doit être générée
            patientBlocs.TryGetValue("patient_antecedents", out var _atBlocPre);
            var _atPre = ParseAntecedents(_atBlocPre?.ContenuValide ?? "");
            bool hasParcoursDetailPage = HasParcoursDetail(_atPre.ParcoursDetail);
            if (hasParcoursDetailPage) totalPages++;   // page C s'ajoute au total

            bool patientPagesRendered = false;

            foreach (var bloc in dossier.Blocs)
            {
                if (bloc.Key == "couverture") continue;

                if (bloc.Key == "restitution_1page")
                {
                    sb.Append(BuildRestitution1PagePage(bloc, coverFields, photoBase64, pageNumber));
                    pageNumber++;
                    continue;
                }

                if (bloc.Key.StartsWith("patient_", StringComparison.Ordinal))
                {
                    // On rend les pages Patient & Contexte la première fois qu'on rencontre
                    // un bloc patient_*. Les autres sont absorbés silencieusement.
                    if (!patientPagesRendered)
                    {
                        int pageCNumber = pageNumber + 2;
                        sb.Append(BuildPatientContextePageA(patientBlocs, coverFields, pageNumber, totalPages));
                        sb.Append(BuildPatientContextePageB(patientBlocs, pageNumber + 1, totalPages, hasParcoursDetailPage, pageCNumber));
                        pageNumber += 2;
                        if (hasParcoursDetailPage)
                        {
                            sb.Append(BuildPatientContextePageC(patientBlocs, pageNumber, totalPages));
                            pageNumber++;
                        }
                        patientPagesRendered = true;
                    }
                    continue;
                }

                if (bloc.Key == "carto_enfant")
                {
                    // Cartographie de l'enfant : 2 pages (A : sphères 1-4 / B : sphères 5-8).
                    // Sphère par sphère, on bascule les placeholders vers le vrai rendu
                    // (pie SVG + observations + niveau lus depuis l'Étape 3 de l'évaluation).
                    // V0.2 : sphère 1 « Attachement » câblée ; sphères 2-8 restent placeholder.
                    var carto       = LoadLatestCartographieEnfant(patientNomComplet);
                    var perSphere   = ParseCartoEnfantBloc(bloc.ContenuValide ?? "");
                    sb.Append(BuildCartoEnfantPageA(bloc, carto, perSphere, pageNumber,     totalPages));
                    sb.Append(BuildCartoEnfantPageB(bloc, carto, perSphere, pageNumber + 1, totalPages));
                    pageNumber += 2;
                    continue;
                }

                // Fallback brouillon pour tous les autres blocs (synthese_*, projet_*, conclusion).
                var content = string.IsNullOrWhiteSpace(bloc.ContenuValide)
                    ? "<p class='placeholder'><em>(Section à compléter — utilisez le bouton ✨ Suggérer)</em></p>"
                    : MarkdownToHtmlLite(bloc.ContenuValide);

                sb.AppendLine("<div class='page draft-page'>");
                sb.AppendLine($"  <div class='page-num'>Page {pageNumber}/{totalPages}</div>");
                sb.AppendLine($"  <h1 class='draft-title'>{WebUtility.HtmlEncode(bloc.Titre)}</h1>");
                sb.AppendLine($"  <div class='draft-meta'>Voix cible : <strong>{WebUtility.HtmlEncode(bloc.VoixCible)}</strong></div>");
                sb.AppendLine($"  <div class='draft-content'>{content}</div>");
                sb.AppendLine("</div>");
                pageNumber++;
            }
            return sb.ToString();
        }

        // ── Pages Patient & Contexte (A + B) ────────────────────────────────

        /// <summary>
        /// Rend la page A : Identification + Motif + Contexte familial. Header sobre (titre +
        /// filet teal + logo Med + numéro de page) comme demandé — pas de cartouche dégradé
        /// comme la page Restitution Parents, on garde un style clinique sans agression visuelle.
        /// </summary>
        private string BuildPatientContextePageA(
            Dictionary<string, RestitutionBloc> blocs,
            CoverFields f,
            int pageNumber,
            int totalPages)
        {
            blocs.TryGetValue("patient_identification",    out var blocIdent);
            blocs.TryGetValue("patient_motif",             out var blocMotif);
            blocs.TryGetValue("patient_contexte_familial", out var blocCf);

            var identFields = ParseIdentificationFields(blocIdent?.ContenuValide ?? "", f);

            var sb = new StringBuilder();
            sb.AppendLine("<div class='page pc-page'>");
            sb.Append(BuildPcHeader("PATIENT & CONTEXTE", "Informations cliniques initiales", "1/2", pageNumber, totalPages));

            // ── Section 1 : Identification (cartouche teal) ─────────────────
            sb.AppendLine("  <div class='pc-card pc-c-ident'>");
            sb.AppendLine("    <div class='pc-card-hdr'>");
            sb.AppendLine("      <span class='pc-card-num'>1</span>");
            sb.AppendLine("      <h2>IDENTIFICATION</h2>");
            sb.AppendLine("    </div>");
            sb.AppendLine("    <div class='pc-card-body'>");

            // Récit narratif (1 phrase « Il s'agit de... »)
            if (!string.IsNullOrWhiteSpace(identFields.Presentation))
                sb.AppendLine($"      <p class='pc-ident-narrative'>{WebUtility.HtmlEncode(identFields.Presentation)}</p>");
            else
                sb.AppendLine("      <p class='pc-placeholder'><em>Présentation à compléter — cliquez sur ✨ Suggérer.</em></p>");

            // Méta-données structurées (4 lignes)
            sb.AppendLine("      <div class='pc-ident-meta'>");
            sb.Append(BuildPcMetaRow("Période d'évaluation", identFields.PeriodeEvaluation));
            sb.Append(BuildPcMetaRow("Date de restitution", identFields.DateRestitution));
            sb.Append(BuildPcMetaRow("Évaluateur",          identFields.Evaluateur));
            sb.Append(BuildPcMetaRow("Lieu",                identFields.Lieu));
            sb.AppendLine("      </div>");
            sb.AppendLine("    </div>");
            sb.AppendLine("  </div>");

            // ── Section 2 : Motif de consultation (cartouche violet) ────────
            sb.AppendLine("  <div class='pc-card pc-c-motif'>");
            sb.AppendLine("    <div class='pc-card-hdr'>");
            sb.AppendLine("      <span class='pc-card-num'>2</span>");
            sb.AppendLine("      <h2>MOTIF DE CONSULTATION</h2>");
            sb.AppendLine("    </div>");
            sb.AppendLine("    <div class='pc-card-body pc-narrative'>");
            sb.AppendLine(string.IsNullOrWhiteSpace(blocMotif?.ContenuValide)
                ? "      <p class='pc-placeholder'><em>Section à compléter — cliquez sur ✨ Suggérer.</em></p>"
                : MarkdownToHtmlLite(blocMotif!.ContenuValide));
            sb.AppendLine("    </div>");
            sb.AppendLine("  </div>");

            // ── Section 3 : Contexte familial (cartouche vert) ──────────────
            sb.AppendLine("  <div class='pc-card pc-c-famille'>");
            sb.AppendLine("    <div class='pc-card-hdr'>");
            sb.AppendLine("      <span class='pc-card-num'>3</span>");
            sb.AppendLine("      <h2>CONTEXTE FAMILIAL</h2>");
            sb.AppendLine("    </div>");
            sb.AppendLine("    <div class='pc-card-body'>");

            var cf = ParseContexteFamilial(blocCf?.ContenuValide ?? "");
            if (!string.IsNullOrWhiteSpace(cf.Recit))
                sb.AppendLine($"      <div class='pc-narrative'>{MarkdownToHtmlLite(cf.Recit)}</div>");
            else
                sb.AppendLine("      <p class='pc-placeholder'><em>Récit familial à compléter — cliquez sur ✨ Suggérer.</em></p>");

            // 4 colonnes Père / Mère / Fratrie / Autres figures
            sb.AppendLine("      <div class='pc-four-col'>");
            sb.Append(BuildPcMiniCard("PÈRE",              cf.Pere,           "pc-mc-pere"));
            sb.Append(BuildPcMiniCard("MÈRE",              cf.Mere,           "pc-mc-mere"));
            sb.Append(BuildPcMiniCard("FRATRIE",           cf.Fratrie,        "pc-mc-fratrie"));
            sb.Append(BuildPcMiniCard("AUTRES FIGURES",    cf.AutresFigures,  "pc-mc-autres"));
            sb.AppendLine("      </div>");

            // Bandeau pleine largeur : Points à retenir
            var pointsBody = string.IsNullOrWhiteSpace(cf.PointsARetenir)
                ? "<em>—</em>"
                : MarkdownToHtmlLite(cf.PointsARetenir);
            sb.AppendLine($"      <div class='pc-points-banner'><h3>POINTS À RETENIR</h3><div class='pc-points-body'>{pointsBody}</div></div>");
            sb.AppendLine("    </div>");
            sb.AppendLine("  </div>");

            sb.Append(BuildPcFooter(
                "Ces informations constituent le point de départ de la compréhension globale.<br>" +
                "Elles seront précisées et enrichies par l'évaluation clinique, les observations et les bilans réalisés.",
                pageNumber, totalPages));
            sb.AppendLine("</div>");
            return sb.ToString();
        }

        /// <summary>
        /// Rend la page B : Antécédents (4 sous-blocs) + Situation actuelle (5 sous-blocs).
        /// </summary>
        private string BuildPatientContextePageB(
            Dictionary<string, RestitutionBloc> blocs,
            int pageNumber,
            int totalPages,
            bool hasDetailPage = false,
            int detailPageNumber = 0)
        {
            blocs.TryGetValue("patient_antecedents",        out var blocAt);
            blocs.TryGetValue("patient_situation_actuelle", out var blocSa);

            var at = ParseAntecedents(blocAt?.ContenuValide ?? "");
            var sa = ParseSituationActuelle(blocSa?.ContenuValide ?? "");

            var sb = new StringBuilder();
            sb.AppendLine("<div class='page pc-page'>");
            sb.Append(BuildPcHeader("PATIENT & CONTEXTE", "Informations cliniques complémentaires", "2/2", pageNumber, totalPages));

            // ── Section 4 : Antécédents (cartouche bleu) ────────────────────
            sb.AppendLine("  <div class='pc-card pc-c-atcd'>");
            sb.AppendLine("    <div class='pc-card-hdr'>");
            sb.AppendLine("      <span class='pc-card-num'>4</span>");
            sb.AppendLine("      <h2>ANTÉCÉDENTS</h2>");
            sb.AppendLine("    </div>");
            sb.AppendLine("    <div class='pc-card-body'>");
            sb.AppendLine("      <div class='pc-two-col'>");
            sb.Append(BuildPcSubBlock("ANTÉCÉDENTS MÉDICAUX",   at.Medicaux,      "pc-sb-med"));
            sb.Append(BuildPcSubBlock("DÉVELOPPEMENTAUX",       at.Developpement, "pc-sb-dev"));
            sb.AppendLine("      </div>");
            sb.AppendLine("      <div class='pc-two-col'>");
            sb.Append(BuildPcSubBlock("FAMILIAUX", at.Familiaux, "pc-sb-fam"));

            // Parcours compact : 2 sous-sections + lien vers page de détail si applicable
            var suiviBody  = string.IsNullOrWhiteSpace(at.SuiviResume)  ? "<p class='pc-placeholder'><em>—</em></p>" : MarkdownToHtmlLite(at.SuiviResume);
            var bilansBody = string.IsNullOrWhiteSpace(at.BilansResume) ? "<p class='pc-placeholder'><em>—</em></p>" : MarkdownToHtmlLite(at.BilansResume);
            var detailLink = hasDetailPage
                ? $"<div class='pc-parcours-detail-link'>📄 Détail complet → p.{detailPageNumber}</div>"
                : "";
            sb.AppendLine(
                "        <div class='pc-subblock pc-sb-parcours'>" +
                "<h3>PARCOURS DE SOINS</h3>" +
                "<div class='pc-subblock-body'>" +
                  "<div class='pc-parcours-sub'><h4>SUIVI</h4>" + suiviBody + "</div>" +
                  "<div class='pc-parcours-sub'><h4>BILANS</h4>" + bilansBody + "</div>" +
                  detailLink +
                "</div></div>");

            sb.AppendLine("      </div>");
            sb.AppendLine("    </div>");
            sb.AppendLine("  </div>");

            // ── Section 5 : Situation actuelle (cartouche orange) ───────────
            sb.AppendLine("  <div class='pc-card pc-c-situation'>");
            sb.AppendLine("    <div class='pc-card-hdr'>");
            sb.AppendLine("      <span class='pc-card-num'>5</span>");
            sb.AppendLine("      <h2>SITUATION ACTUELLE</h2>");
            sb.AppendLine("    </div>");
            sb.AppendLine("    <div class='pc-card-body'>");
            sb.AppendLine("      <div class='pc-three-col'>");
            sb.Append(BuildPcSubBlock("À L'ÉCOLE",        sa.Ecole,   "pc-sb-ecole"));
            sb.Append(BuildPcSubBlock("À LA MAISON",      sa.Maison,  "pc-sb-maison"));
            sb.Append(BuildPcSubBlock("AVEC LES AUTRES",  sa.Autres,  "pc-sb-autres"));
            sb.AppendLine("      </div>");
            sb.AppendLine("      <div class='pc-two-col'>");
            sb.Append(BuildPcSubBlock("FORCES OBSERVÉES",    sa.Forces,    "pc-sb-forces"));
            sb.Append(BuildPcSubBlock("ACTIVITÉS / INTÉRÊTS", sa.Activites, "pc-sb-activites"));
            sb.AppendLine("      </div>");
            sb.AppendLine("    </div>");
            sb.AppendLine("  </div>");

            sb.Append(BuildPcFooter(
                "Ces éléments seront réévalués régulièrement afin d'ajuster le projet d'accompagnement.<br>" +
                "Ils constituent la base du suivi clinique et du travail en collaboration avec la famille et les partenaires.",
                pageNumber, totalPages));
            sb.AppendLine("</div>");
            return sb.ToString();
        }

        /// <summary>
        /// Rend la page C (conditionnelle) : détail du parcours de soins.
        /// Générée seulement si patient_antecedents contient un contenu substantiel
        /// dans la section "Parcours — détail".
        /// </summary>
        private string BuildPatientContextePageC(
            Dictionary<string, RestitutionBloc> blocs,
            int pageNumber,
            int totalPages)
        {
            blocs.TryGetValue("patient_antecedents", out var blocAt);
            var at = ParseAntecedents(blocAt?.ContenuValide ?? "");

            // Séparer le détail en 2 sections (suivi / bilans) via les marqueurs en gras
            var detailSections = SplitByBoldTitles(at.ParcoursDetail);
            string suiviDetail  = "";
            string bilansDetail = "";
            foreach (var (title, content) in detailSections)
            {
                var t = title.ToLowerInvariant();
                if      (t.Contains("suivi") || t.Contains("antérieur") || t.Contains("anterieur")) suiviDetail  = content;
                else if (t.Contains("bilan") || t.Contains("réalisé")   || t.Contains("realise"))   bilansDetail = content;
            }
            // Si pas de sections séparées, on met tout dans suivi
            if (string.IsNullOrWhiteSpace(suiviDetail) && string.IsNullOrWhiteSpace(bilansDetail))
                suiviDetail = at.ParcoursDetail;

            var sb = new StringBuilder();
            sb.AppendLine("<div class='page pc-page'>");
            sb.Append(BuildPcHeader("PATIENT & CONTEXTE", "Parcours de soins — détail", "annexe", pageNumber, totalPages));

            // Section Suivi antérieur
            sb.AppendLine("  <div class='pc-card pc-c-atcd'>");
            sb.AppendLine("    <div class='pc-card-hdr'>");
            sb.AppendLine("      <span class='pc-card-num'>+</span>");
            sb.AppendLine("      <h2>SUIVI ANTÉRIEUR</h2>");
            sb.AppendLine("    </div>");
            sb.AppendLine("    <div class='pc-card-body'>");
            if (string.IsNullOrWhiteSpace(suiviDetail))
                sb.AppendLine("      <p class='pc-placeholder'><em>Aucun suivi antérieur identifié.</em></p>");
            else
                sb.AppendLine($"      <div class='pc-detail-content'>{MarkdownToHtmlLite(suiviDetail)}</div>");
            sb.AppendLine("    </div>");
            sb.AppendLine("  </div>");

            // Section Bilans réalisés
            sb.AppendLine("  <div class='pc-card pc-c-atcd' style='margin-top:12px'>");
            sb.AppendLine("    <div class='pc-card-hdr'>");
            sb.AppendLine("      <span class='pc-card-num'>+</span>");
            sb.AppendLine("      <h2>BILANS RÉALISÉS</h2>");
            sb.AppendLine("    </div>");
            sb.AppendLine("    <div class='pc-card-body'>");
            if (string.IsNullOrWhiteSpace(bilansDetail))
                sb.AppendLine("      <p class='pc-placeholder'><em>Aucun bilan formel identifié.</em></p>");
            else
                sb.AppendLine($"      <div class='pc-detail-content'>{MarkdownToHtmlLite(bilansDetail)}</div>");
            sb.AppendLine("    </div>");
            sb.AppendLine("  </div>");

            sb.Append(BuildPcFooter(
                "Ces informations sont issues de l'anamnèse et des documents fournis.",
                pageNumber, totalPages));
            sb.AppendLine("</div>");
            return sb.ToString();
        }

        // ── Pages Cartographie de l'enfant (A + B) — V0.1 scaffolding ───────

        /// <summary>
        /// Les 8 sphères du modèle PDF, dans l'ordre d'affichage. V0.1 : structure visuelle
        /// uniquement, sans données réelles ni génération LLM. À chaque itération, on remplacera
        /// le placeholder d'une sphère par son rendu réel (pie/radar + observations + niveau).
        /// </summary>
        private static readonly (int Num, string Title, string Subtitle, string CssClass, bool HasRadar)[] _ceSpheres =
        {
            (1, "ATTACHEMENT",         "et sécurité intérieure",          "ce-s1", false),
            (2, "RÉGULATION",          "émotionnelle",                    "ce-s2", false),
            (3, "LANGAGE",             "",                                "ce-s3", false),
            (4, "TEMPÉRAMENT",         "et adaptabilité",                 "ce-s4", true),
            (5, "PSYCHOMOTRICITÉ",     "(Globale et Fine)",               "ce-s5", true),
            (6, "IMAGINATION ET JEU",  "",                                "ce-s6", false),
            (7, "PENSÉE ET",           "APPRENTISSAGES",                  "ce-s7", false),
            (8, "ATTENTION ET",        "FONCTIONS EXÉCUTIVES",            "ce-s8", true),
        };

        /// <summary>
        /// Charge la dernière cartographie de l'enfant validée pour ce patient. Retourne
        /// null si aucune évaluation clôturée n'existe ou si le service n'est pas injecté.
        /// Mis en cache pour éviter une relecture YAML à chaque refresh de la preview.
        /// </summary>
        private CartographieEnfant? LoadLatestCartographieEnfant(string patientNomComplet)
        {
            if (_evaluationService == null || string.IsNullOrWhiteSpace(patientNomComplet)) return null;
            if (_cachedEvalPatient == patientNomComplet && _cachedEvalPhase != null)
                return _cachedEvalPhase.CartographieEnfant;

            try
            {
                var dir = _pathService.GetPatientRootDirectory(patientNomComplet);
                if (string.IsNullOrEmpty(dir)) return null;

                var phases = _evaluationService.LoadAll(dir);
                // On prend la plus récente clôturée et VALIDÉE pour l'étape 3 (Cartographie enfant).
                var phase = phases
                    .Where(p => !p.IsActive && p.CartographieEnfant.IsValidated)
                    .OrderByDescending(p => p.DateCloture ?? p.DateDerniereModif)
                    .FirstOrDefault();

                // Fallback : la plus récente clôturée même si l'étape 3 n'est pas formellement
                // validée (cas où le médecin coche les items sans cliquer Valider).
                phase ??= phases
                    .Where(p => !p.IsActive)
                    .OrderByDescending(p => p.DateCloture ?? p.DateDerniereModif)
                    .FirstOrDefault();

                _cachedEvalPatient = patientNomComplet;
                _cachedEvalPhase   = phase;
                return phase?.CartographieEnfant;
            }
            catch { return null; }
        }

        /// <summary>
        /// Rend la page A de la Cartographie de l'enfant : sphères 1-4 (Attachement, Régulation,
        /// Langage, Tempérament). Sphère 1 (Attachement) est câblée sur les données réelles
        /// (Étape 3 de la dernière évaluation), les autres restent en placeholder pour V0.2.
        /// </summary>
        private string BuildCartoEnfantPageA(RestitutionBloc bloc, CartographieEnfant? carto, Dictionary<int, CeSphereContent> perSphere, int pageNumber, int totalPages)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<div class='page ce-page'>");
            sb.Append(BuildPcHeader(
                "CARTOGRAPHIE DE L'ENFANT",
                "4.1 Vue d'ensemble — Cette section évalue les sphères de développement,<br>afin de mieux comprendre son fonctionnement interne et ses besoins spécifiques.",
                "1/2", pageNumber, totalPages));

            for (int i = 0; i < 4; i++)
                sb.Append(BuildCeSphereCard(_ceSpheres[i], carto, perSphere));

            sb.Append(BuildCeLegende());
            sb.AppendLine("</div>");
            return sb.ToString();
        }

        /// <summary>
        /// Rend la page B : sphères 5-8 (Psychomotricité, Imagination & Jeu, Pensée &
        /// Apprentissages, Attention & FE).
        /// </summary>
        private string BuildCartoEnfantPageB(RestitutionBloc bloc, CartographieEnfant? carto, Dictionary<int, CeSphereContent> perSphere, int pageNumber, int totalPages)
        {
            var sb = new StringBuilder();
            sb.AppendLine("<div class='page ce-page'>");
            sb.Append(BuildPcHeader(
                "CARTOGRAPHIE DE L'ENFANT",
                "4.1 Vue d'ensemble — Cette section évalue les sphères de développement,<br>afin de mieux comprendre son fonctionnement interne et ses besoins spécifiques.",
                "2/2", pageNumber, totalPages));

            for (int i = 4; i < 8; i++)
                sb.Append(BuildCeSphereCard(_ceSpheres[i], carto, perSphere));

            sb.Append(BuildCeLegende());
            sb.AppendLine("</div>");
            return sb.ToString();
        }

        /// <summary>
        /// Rend une cartouche de sphère. Trois sources de données possibles selon l'état :
        /// (a) Étape 3 cartographie (score + niveau numérique) → alimente la pie SVG ;
        /// (b) Bloc carto_enfant.ContenuValide parsé (observations + niveau clinique phrase) ;
        /// (c) placeholders si rien n'est dispo. Le texte « Niveau clinique » prend la couleur
        /// de la section (cohérence avec le modèle PDF).
        /// </summary>
        private static string BuildCeSphereCard(
            (int Num, string Title, string Subtitle, string CssClass, bool HasRadar) sphere,
            CartographieEnfant? carto,
            Dictionary<int, CeSphereContent> perSphere)
        {
            // (a) Données numériques de l'étape 3 (uniquement câblé pour Attachement en V0.2).
            var segmentData = carto != null ? GetSegmentData(sphere.Num, carto) : null;
            // (b) Contenu rédactionnel de Med pour cette sphère, parsé depuis le bloc.
            perSphere.TryGetValue(sphere.Num, out var medContent);

            var sb = new StringBuilder();
            sb.AppendLine($"  <div class='ce-card {sphere.CssClass}'>");

            // En-tête sphère : badge num + titre + sous-titre
            sb.AppendLine("    <div class='ce-card-hdr'>");
            sb.AppendLine($"      <span class='ce-num'>{sphere.Num}.</span>");
            sb.AppendLine("      <div class='ce-card-title'>");
            sb.AppendLine($"        <strong>{WebUtility.HtmlEncode(sphere.Title)}</strong>");
            if (!string.IsNullOrEmpty(sphere.Subtitle))
                sb.AppendLine($"        <em>{WebUtility.HtmlEncode(sphere.Subtitle)}</em>");
            sb.AppendLine("      </div>");
            sb.AppendLine("    </div>");

            // Corps : chart à gauche, observation + niveau à droite
            sb.AppendLine("    <div class='ce-card-body'>");

            // Zone chart : pie SVG si la sphère est câblée, sinon placeholder
            sb.AppendLine($"      <div class='ce-chart {(sphere.HasRadar ? "ce-chart-radar" : "ce-chart-pie")}'>");
            if (segmentData != null && !sphere.HasRadar)
            {
                sb.AppendLine(BuildCeChenillePieSvg(segmentData.Niveau, segmentData.Score));
            }
            else
            {
                sb.AppendLine(sphere.HasRadar
                    ? "        <span class='ce-chart-placeholder'>(Radar à venir)</span>"
                    : "        <span class='ce-chart-placeholder'>(Pie à venir)</span>");
            }
            sb.AppendLine("      </div>");

            // Zone texte (observations + niveau clinique)
            sb.AppendLine("      <div class='ce-card-text'>");
            sb.AppendLine("        <div class='ce-obs-title'>Observation</div>");
            sb.AppendLine("        <div class='ce-obs-body'>");
            if (medContent != null && !string.IsNullOrWhiteSpace(medContent.Observations))
            {
                sb.AppendLine(MarkdownToHtmlLite(medContent.Observations));
            }
            else
            {
                sb.AppendLine("          <p class='ce-placeholder'><em>Observations à compléter — sphère à venir dans une prochaine itération.</em></p>");
            }
            sb.AppendLine("        </div>");
            sb.AppendLine("        <div class='ce-niveau-title'>Niveau clinique</div>");
            sb.AppendLine("        <div class='ce-niveau-body'>");
            if (medContent != null && !string.IsNullOrWhiteSpace(medContent.NiveauClinique))
            {
                // Texte du niveau clinique en couleur de section (style PDF modèle).
                sb.AppendLine($"          <span class='ce-niveau-text'>{WebUtility.HtmlEncode(medContent.NiveauClinique)}</span>");
            }
            else
            {
                sb.AppendLine("          <span class='ce-placeholder'><em>—</em></span>");
            }
            sb.AppendLine("        </div>");
            sb.AppendLine("      </div>");

            sb.AppendLine("    </div>");
            sb.AppendLine("  </div>");
            return sb.ToString();
        }

        // ── Parsing du bloc carto_enfant ────────────────────────────────────

        /// <summary>Contenu rédigé par Med pour une sphère donnée (parsé depuis bloc.ContenuValide).</summary>
        private class CeSphereContent
        {
            public string Observations    { get; set; } = "";
            public string NiveauClinique  { get; set; } = "";
        }

        /// <summary>
        /// Parse le bloc carto_enfant en sections par sphère. Reconnaît les marqueurs
        /// `## Sphère N — Nom` (séparateurs) et, dans chaque section, les sous-marqueurs
        /// `**Observations**` et `**Niveau clinique**`. Tolère absence d'une sphère :
        /// les non-présentes restent en placeholder.
        /// </summary>
        private static Dictionary<int, CeSphereContent> ParseCartoEnfantBloc(string md)
        {
            var result = new Dictionary<int, CeSphereContent>();
            if (string.IsNullOrWhiteSpace(md)) return result;

            // Découpe par `## Sphère N`. Tolérant aux variations (tiret en/em, accents…).
            var headerRx = new Regex(@"^##\s+Sphère\s+(\d+)\b[^\n]*$", RegexOptions.Multiline);
            var matches  = headerRx.Matches(md);
            if (matches.Count == 0) return result;

            for (int i = 0; i < matches.Count; i++)
            {
                var m = matches[i];
                if (!int.TryParse(m.Groups[1].Value, out var num)) continue;

                var start = m.Index + m.Length;
                var end   = (i + 1 < matches.Count) ? matches[i + 1].Index : md.Length;
                var body  = md.Substring(start, end - start).Trim();

                result[num] = ExtractSphereSubSections(body);
            }
            return result;
        }

        /// <summary>
        /// Extrait les sous-sections d'une sphère : `**Observations**` (bloc multi-ligne)
        /// et `**Niveau clinique** : valeur` (typiquement une seule ligne).
        /// </summary>
        private static CeSphereContent ExtractSphereSubSections(string body)
        {
            var content = new CeSphereContent();

            // Observations : entre `**Observations**` et `**Niveau clinique**` (ou fin).
            var obsMatch = Regex.Match(body,
                @"\*\*Observations\*\*\s*[:\.]?\s*\n?(.*?)(?=\*\*Niveau\s+clinique\*\*|$)",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (obsMatch.Success)
            {
                content.Observations = obsMatch.Groups[1].Value.Trim();
            }

            // Niveau clinique : ligne unique après `**Niveau clinique**`.
            var nivMatch = Regex.Match(body,
                @"\*\*Niveau\s+clinique\*\*\s*[:\.]?\s*(.+?)(?:\r?\n\s*\r?\n|$)",
                RegexOptions.Singleline | RegexOptions.IgnoreCase);
            if (nivMatch.Success)
            {
                var v = nivMatch.Groups[1].Value.Trim();
                // Nettoie les balises markdown éventuelles ajoutées par le LLM.
                v = Regex.Replace(v, @"\*+", "").Trim();
                content.NiveauClinique = v;
            }

            return content;
        }

        // ── Cartographie enfant : helpers données + SVG ─────────────────────

        /// <summary>Couple (score, niveau) extrait pour une sphère donnée.</summary>
        private class CeSegmentData
        {
            public int           Score  { get; init; }
            public NiveauSegment Niveau { get; init; }
        }

        /// <summary>
        /// Mappe le numéro de sphère du modèle PDF (1..8) vers le segment correspondant dans
        /// CartographieEnfant. Sphères avec radar (Tempérament, Psychomot×, Attention) ou non
        /// encore mappées renvoient null pour l'instant. Étendu sphère par sphère.
        /// </summary>
        private static CeSegmentData? GetSegmentData(int sphereNum, CartographieEnfant carto)
        {
            ChenilleSegment? seg = sphereNum switch
            {
                1 => carto.Attachement,
                // 2-8 à câbler dans les itérations suivantes (sphère par sphère).
                _ => null
            };
            if (seg == null) return null;

            var niveau = CartographieScoringService.Calculer(seg.Score, carto.AgeAuMomentDeLaSaisie);
            if (!niveau.HasValue) return null;

            return new CeSegmentData { Score = seg.Score, Niveau = niveau.Value };
        }

        /// <summary>
        /// Construit le SVG d'une pie 6 wedges (un par niveau de la Chenille). Le wedge
        /// correspondant au niveau de l'enfant est légèrement détaché du centre (translation
        /// radiale) et entouré d'un effet de surbrillance (filter glow + stroke épais blanc).
        /// Les 5 autres wedges sont rendus en couleur normale, opacité réduite, pour servir
        /// de référentiel visuel à l'échelle.
        /// </summary>
        private static string BuildCeChenillePieSvg(NiveauSegment niveau, int score)
        {
            // Ordre d'affichage des wedges : du meilleur (en haut, 12h) vers le plus
            // préoccupant (sens horaire). Cohérent avec la chenille : on lit de la ressource
            // vers le besoin d'étayage.
            var order = new[]
            {
                NiveauSegment.VertFonce,
                NiveauSegment.VertClair,
                NiveauSegment.JauneClair,
                NiveauSegment.JauneFonce,
                NiveauSegment.RougeClair,
                NiveauSegment.RougeFonce,
            };

            const double cx = 60, cy = 60, r = 46;
            const double sweep = 2 * Math.PI / 6;     // 60° par wedge
            const double startAngle = -Math.PI / 2;   // commence à 12h
            const double detachOffset = 8;            // distance de détachement du wedge actif

            var sb = new StringBuilder();
            sb.AppendLine("        <svg viewBox='0 0 120 120' class='ce-pie-svg' xmlns='http://www.w3.org/2000/svg'>");
            sb.AppendLine("          <defs>");
            sb.AppendLine("            <filter id='ce-glow' x='-50%' y='-50%' width='200%' height='200%'>");
            sb.AppendLine("              <feGaussianBlur stdDeviation='2.5' result='b'/>");
            sb.AppendLine("              <feMerge><feMergeNode in='b'/><feMergeNode in='SourceGraphic'/></feMerge>");
            sb.AppendLine("            </filter>");
            sb.AppendLine("          </defs>");

            for (int i = 0; i < 6; i++)
            {
                var n = order[i];
                var a0 = startAngle + i * sweep;
                var a1 = a0 + sweep;
                var color = CeNiveauColor(n);
                bool active = n == niveau;

                // Centre du wedge : si actif, on le pousse vers l'extérieur le long de sa bissectrice.
                double dx = 0, dy = 0;
                if (active)
                {
                    var mid = (a0 + a1) / 2;
                    dx = Math.Cos(mid) * detachOffset;
                    dy = Math.Sin(mid) * detachOffset;
                }

                var p0x = cx + dx + r * Math.Cos(a0);
                var p0y = cy + dy + r * Math.Sin(a0);
                var p1x = cx + dx + r * Math.Cos(a1);
                var p1y = cy + dy + r * Math.Sin(a1);

                var path = string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "M {0:F2} {1:F2} L {2:F2} {3:F2} A {4:F2} {4:F2} 0 0 1 {5:F2} {6:F2} Z",
                    cx + dx, cy + dy, p0x, p0y, r, p1x, p1y);

                if (active)
                {
                    // Wedge actif : opacité pleine, contour blanc épais, glow.
                    sb.AppendLine($"          <path d='{path}' fill='{color}' stroke='white' stroke-width='2' filter='url(#ce-glow)'/>");
                }
                else
                {
                    // Wedges inactifs : couleur normale mais opacité réduite pour faire ressortir l'actif.
                    sb.AppendLine($"          <path d='{path}' fill='{color}' fill-opacity='0.32' stroke='white' stroke-width='1'/>");
                }
            }

            // Score au centre — repère pour le médecin (« 4/6 » par ex.)
            sb.AppendLine($"          <circle cx='{cx}' cy='{cy}' r='14' fill='white' stroke='#E2E8F0' stroke-width='1'/>");
            sb.AppendLine($"          <text x='{cx}' y='{cy + 4}' text-anchor='middle' font-size='11' font-weight='800' fill='#1A3A6A' font-family='Segoe UI, Arial, sans-serif'>{score}/6</text>");

            sb.AppendLine("        </svg>");
            return sb.ToString();
        }

        /// <summary>Couleurs hex officielles des 6 niveaux de la Chenille Universelle.</summary>
        private static string CeNiveauColor(NiveauSegment n) => n switch
        {
            NiveauSegment.VertFonce  => "#1E8449",   // Ressource solide
            NiveauSegment.VertClair  => "#82E0AA",   // Satisfaisant
            NiveauSegment.JauneClair => "#F7DC6F",   // À surveiller (bas)
            NiveauSegment.JauneFonce => "#F39C12",   // À surveiller (fort) / Vigilance
            NiveauSegment.RougeClair => "#E74C3C",   // Fragilisé
            NiveauSegment.RougeFonce => "#922B21",   // Très fragilisé
            _ => "#94A3B8"
        };

        /// <summary>
        /// Rend la légende des 5 niveaux cliniques en bas de chaque page (chips colorés
        /// alignés horizontalement). Identique sur page A et B.
        /// </summary>
        private static string BuildCeLegende()
        {
            var levels = new (string Color, string Label, string Sub)[]
            {
                ("#E74C3C", "Très fragilisé", "Besoin majeur"),
                ("#F39C12", "Fragilisé",      "Besoin d'étayage"),
                ("#F1C40F", "À surveiller",   "Équilibrage nécessaire"),
                ("#7DCEA0", "Satisfaisant",   "Niveau correct"),
                ("#27AE60", "Excellent",      "Ressource solide"),
            };

            var sb = new StringBuilder();
            sb.AppendLine("  <div class='ce-legende-title'>ÉCHELLE DE NIVEAU</div>");
            sb.AppendLine("  <div class='ce-legende'>");
            foreach (var (color, label, sub) in levels)
            {
                sb.AppendLine("    <div class='ce-legende-item'>");
                sb.AppendLine($"      <span class='ce-legende-dot' style='background:{color}'></span>");
                sb.AppendLine("      <div class='ce-legende-text'>");
                sb.AppendLine($"        <strong>{WebUtility.HtmlEncode(label)}</strong>");
                sb.AppendLine($"        <span>({WebUtility.HtmlEncode(sub)})</span>");
                sb.AppendLine("      </div>");
                sb.AppendLine("    </div>");
            }
            sb.AppendLine("  </div>");
            sb.AppendLine("  <div class='ce-footer'>");
            sb.AppendLine("    <span class='pc-info-icon'>i</span> Cette cartographie constitue un outil d'aide à la compréhension du fonctionnement interne. " +
                          "Elle sera réévaluée régulièrement afin d'ajuster le projet d'accompagnement.");
            sb.AppendLine("  </div>");
            return sb.ToString();
        }

        // ── Header / footer / helpers de rendu ──────────────────────────────

        /// <summary>
        /// Header sobre (style identique aux brouillons actuels) : grand titre bleu, sous-titre
        /// italique, filet teal, logo MedCompanion à droite + numéro de page. Pas de cartouche
        /// dégradé comme la page Restitution Parents (voix clinique = registre plus sobre).
        /// </summary>
        private string BuildPcHeader(string title, string subtitle, string subPageBadge, int pageNumber, int totalPages)
        {
            var sb = new StringBuilder();
            sb.AppendLine("  <div class='pc-header'>");
            sb.AppendLine("    <div class='pc-header-left'>");
            sb.AppendLine($"      <span class='pc-section-tag'>{WebUtility.HtmlEncode(subPageBadge)}</span>");
            sb.AppendLine($"      <h1>{WebUtility.HtmlEncode(title)}</h1>");
            sb.AppendLine($"      <p class='pc-subtitle'>{WebUtility.HtmlEncode(subtitle)}</p>");
            sb.AppendLine("    </div>");
            sb.AppendLine("    <div class='pc-header-right'>");
            if (!string.IsNullOrEmpty(_medcompanionHeartBase64))
                sb.AppendLine($"      <img src='data:image/png;base64,{_medcompanionHeartBase64}' class='pc-logo-img' alt='MedCompanion'/>");
            sb.AppendLine("      <div class='pc-brand'>");
            sb.AppendLine("        <strong>MedCompanion</strong>");
            sb.AppendLine("        <span>L'INTELLIGENCE AU SERVICE DU SOIN</span>");
            sb.AppendLine("      </div>");
            sb.AppendLine($"      <div class='pc-page-num'>PAGE {pageNumber}/{totalPages}</div>");
            sb.AppendLine("    </div>");
            sb.AppendLine("  </div>");
            sb.AppendLine("  <hr class='pc-header-rule'/>");
            return sb.ToString();
        }

        private static string BuildPcFooter(string textHtml, int pageNumber, int totalPages)
        {
            var sb = new StringBuilder();
            sb.AppendLine("  <div class='pc-footer'>");
            sb.AppendLine("    <div class='pc-footer-info'>");
            sb.AppendLine($"      <span class='pc-info-icon'>i</span> {textHtml}");
            sb.AppendLine("    </div>");
            sb.AppendLine("  </div>");
            return sb.ToString();
        }

        private static string BuildPcMetaRow(string label, string value)
        {
            var v = string.IsNullOrWhiteSpace(value) ? "Non renseigné" : value;
            return $"        <div class='pc-meta-row'><span class='pc-meta-label'>{WebUtility.HtmlEncode(label)}</span><span class='pc-meta-value'>{WebUtility.HtmlEncode(v)}</span></div>\n";
        }

        private static string BuildPcMiniCard(string title, string content, string cssClass)
        {
            var body = string.IsNullOrWhiteSpace(content)
                ? "<p class='pc-placeholder'><em>—</em></p>"
                : MarkdownToHtmlLite(content);
            return
                $"        <div class='pc-mini-card {cssClass}'>" +
                $"<h3>{WebUtility.HtmlEncode(title)}</h3>" +
                $"<div class='pc-mini-body'>{body}</div>" +
                $"</div>\n";
        }

        private static string BuildPcSubBlock(string title, string content, string cssClass)
        {
            var body = string.IsNullOrWhiteSpace(content)
                ? "<p class='pc-placeholder'><em>—</em></p>"
                : MarkdownToHtmlLite(content);
            return
                $"        <div class='pc-subblock {cssClass}'>" +
                $"<h3>{WebUtility.HtmlEncode(title)}</h3>" +
                $"<div class='pc-subblock-body'>{body}</div>" +
                $"</div>\n";
        }

        // ── Parsers des 5 blocs patient_* ───────────────────────────────────

        private class IdentificationFields
        {
            public string Presentation       = "";
            public string PeriodeEvaluation  = "";
            public string DateRestitution    = "";
            public string Evaluateur         = "";
            public string Lieu               = "";
        }

        /// <summary>
        /// Parse les champs labellisés (`**Label** : valeur`) du bloc patient_identification.
        /// Fallback sur les valeurs déjà connues via CoverFields pour DateRestitution.
        /// </summary>
        private IdentificationFields ParseIdentificationFields(string md, CoverFields f)
        {
            var result = new IdentificationFields
            {
                DateRestitution = f.DateRestitution,
                Evaluateur      = "Dr Lassoued Nair, Pédopsychiatre",
            };

            if (string.IsNullOrWhiteSpace(md)) return result;

            string? Pick(params string[] labels)
            {
                foreach (var lbl in labels)
                {
                    var m = Regex.Match(md,
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

            result.Presentation      = Pick("Présentation", "Presentation")                                  ?? result.Presentation;
            result.PeriodeEvaluation = Pick("Période d'évaluation", "Periode d'evaluation", "Période")        ?? result.PeriodeEvaluation;
            result.DateRestitution   = Pick("Date de restitution")                                            ?? result.DateRestitution;
            result.Evaluateur        = Pick("Évaluateur", "Evaluateur")                                       ?? result.Evaluateur;
            result.Lieu              = Pick("Lieu")                                                           ?? result.Lieu;

            return result;
        }

        private class ContexteFamilial
        {
            public string Recit          = "";
            public string Pere           = "";
            public string Mere           = "";
            public string Fratrie        = "";
            public string AutresFigures  = "";
            public string PointsARetenir = "";
        }

        private static ContexteFamilial ParseContexteFamilial(string md)
        {
            var result = new ContexteFamilial();
            if (string.IsNullOrWhiteSpace(md)) return result;

            var sections = SplitByBoldTitles(md);
            foreach (var (title, content) in sections)
            {
                var t = title.ToLowerInvariant();
                if (t.Contains("récit") || t.Contains("recit") || t == "__intro__") result.Recit = content;
                else if (t == "père" || t == "pere")                                 result.Pere = content;
                else if (t == "mère" || t == "mere")                                 result.Mere = content;
                else if (t.Contains("fratrie"))                                       result.Fratrie = content;
                else if (t.Contains("autres") || t.Contains("figure") || t.Contains("attachement")) result.AutresFigures = content;
                else if (t.Contains("retenir") || t.Contains("points"))               result.PointsARetenir = content;
            }
            return result;
        }

        private class Antecedents
        {
            public string Medicaux       = "";
            public string Developpement  = "";
            public string Familiaux      = "";
            public string SuiviResume    = "";
            public string BilansResume   = "";
            public string ParcoursDetail = "";
        }

        private static Antecedents ParseAntecedents(string md)
        {
            var result = new Antecedents();
            if (string.IsNullOrWhiteSpace(md)) return result;

            var sections = SplitByBoldTitles(md);
            foreach (var (title, content) in sections)
            {
                var t = title.ToLowerInvariant();
                if      (t.Contains("médicaux")   || t.Contains("medicaux"))                 result.Medicaux = content;
                else if (t.Contains("développ")   || t.Contains("developp"))                 result.Developpement = content;
                else if (t.Contains("familiaux"))                                             result.Familiaux = content;
                else if (t.Contains("suivi"))                                                 result.SuiviResume = content;
                else if (t.Contains("bilan"))                                                 result.BilansResume = content;
                else if (t.Contains("parcours") || t.Contains("détail") || t.Contains("detail") || t.Contains("soins")) result.ParcoursDetail = content;
            }
            return result;
        }

        /// <summary>Retourne true si le contenu de détail est substantiel (pas juste "Aucun…").</summary>
        private static bool HasParcoursDetail(string detail)
        {
            if (string.IsNullOrWhiteSpace(detail)) return false;
            var d = detail.Trim().ToLowerInvariant();
            return !d.StartsWith("aucun") && d.Length > 20;
        }

        private class SituationActuelle
        {
            public string Ecole     = "";
            public string Maison    = "";
            public string Autres    = "";
            public string Forces    = "";
            public string Activites = "";
        }

        private static SituationActuelle ParseSituationActuelle(string md)
        {
            var result = new SituationActuelle();
            if (string.IsNullOrWhiteSpace(md)) return result;

            var sections = SplitByBoldTitles(md);
            foreach (var (title, content) in sections)
            {
                var t = title.ToLowerInvariant();
                if (t.Contains("école") || t.Contains("ecole"))                               result.Ecole = content;
                else if (t.Contains("maison") || t.Contains("domicile"))                       result.Maison = content;
                else if (t.Contains("autres") || t.Contains("relationnel") || t.Contains("pair")) result.Autres = content;
                else if (t.Contains("forces") || t.Contains("ressources"))                     result.Forces = content;
                else if (t.Contains("activités") || t.Contains("activites") || t.Contains("intérêts") || t.Contains("interets") || t.Contains("loisir")) result.Activites = content;
            }
            return result;
        }

        /// <summary>
        /// Découpe un texte Markdown par titres en gras `**Titre**` sur leur propre ligne, et
        /// renvoie les segments (titre, contenu). Le texte avant le 1er titre est étiqueté
        /// "__intro__" — pratique pour récupérer un récit narratif qui précède une grille.
        /// </summary>
        private static List<(string title, string content)> SplitByBoldTitles(string md)
        {
            var segments = new List<(string, string)>();
            var matches = _sectionTitleRx.Matches(md);
            if (matches.Count == 0) { segments.Add(("__intro__", md.Trim())); return segments; }

            if (matches[0].Index > 0)
            {
                var intro = md.Substring(0, matches[0].Index).Trim();
                if (!string.IsNullOrEmpty(intro)) segments.Add(("__intro__", intro));
            }

            for (int i = 0; i < matches.Count; i++)
            {
                var m         = matches[i];
                var titleText = m.Groups[1].Value.Trim().TrimEnd(':', ' ');
                var start     = m.Index + m.Length;
                var end       = (i + 1 < matches.Count) ? matches[i + 1].Index : md.Length;
                segments.Add((titleText, md.Substring(start, end - start).Trim()));
            }
            return segments;
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

            // ── Bandeau supérieur (Cartouche) ────────────────────────────────
            sb.AppendLine("  <div class='rp-header'>");
            sb.AppendLine("    <div class='rp-logo'>");
            if (!string.IsNullOrEmpty(_medcompanionHeartBase64))
                sb.AppendLine($"      <img src='data:image/png;base64,{_medcompanionHeartBase64}' class='rp-logo-img' />");
            sb.AppendLine("    </div>");
            sb.AppendLine("    <div class='rp-header-title-block'>");
            sb.AppendLine("      <h1 class='rp-main-title'>RESTITUTION AUX PARENTS</h1>");
            sb.AppendLine("      <p class='rp-subtitle'>Comprendre pour mieux accompagner votre enfant</p>");
            sb.AppendLine("    </div>");
            sb.AppendLine("    <div class='rp-badge'>");
            if (!string.IsNullOrEmpty(_iconPedopsychiatrieBase64))
                sb.AppendLine($"      <img src='data:image/png;base64,{_iconPedopsychiatrieBase64}' class='rp-badge-icon' />");
            sb.AppendLine("      <div class='rp-badge-text'><strong>PÉDOPSYCHIATRIE</strong><br/><span>Évaluation · Compréhension · Accompagnement</span></div>");
            sb.AppendLine("    </div>");
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

            // ── Pied de page ─────────────────────────────────────────────────
            sb.AppendLine("  <div class='rp-footer'>");
            sb.AppendLine("    <div class='rp-footer-line'></div>");
            sb.AppendLine("    <div class='rp-footer-content'>");
            sb.AppendLine("      <span>MedCompanion · Document Confidentiel</span>");
            sb.AppendLine($"      <span>Page {pageNumber}/9</span>");
            sb.AppendLine("    </div>");
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
.rp-page {
  display: flex;
  flex-direction: column;
  height: 297mm;
  min-height: 297mm;
  max-height: 297mm;
  padding: 0 0 55px 0;
  justify-content: space-between;
  gap: 8px;
  font-family: 'Segoe UI', Arial, sans-serif;
  font-size: 11px;
  color: #2C3E50;
  position: relative;
}

/* Bandeau haut (Cartouche) */
.rp-header {
  position: relative;
  display: flex;
  justify-content: space-between;
  align-items: center;
  background: linear-gradient(135deg, #3A86C8 0%, #2563EB 100%);
  color: white;
  padding: 10px 30px;
  border-bottom: 3.5px solid #2DC5A2;
  min-height: 60px;
}
.rp-logo { display: flex; align-items: center; }
.rp-logo-img { width: 34px; height: 34px; filter: drop-shadow(0 2px 4px rgba(0,0,0,0.1)); }

.rp-header-title-block {
  position: absolute;
  left: 50%;
  top: 50%;
  transform: translate(-50%, -50%);
  text-align: center;
  width: auto;
  max-width: 50%;
}
.rp-main-title {
  font-size: 18px;
  font-weight: 800;
  color: white;
  margin: 0;
  letter-spacing: 1.5px;
  text-shadow: 0 1px 2px rgba(0,0,0,0.1);
}
.rp-subtitle {
  font-size: 10px;
  color: #E2E8F0;
  margin: 2px 0 0 0;
  font-style: italic;
  opacity: 0.9;
}

.rp-badge { display: flex; align-items: center; gap: 8px; text-align: right; }
.rp-badge-icon { width: 26px; height: 26px; }
.rp-badge-text { text-align: right; line-height: 1.3; }
.rp-badge-text strong { font-size: 10px; letter-spacing: 0.5px; display: block; font-weight: 700; }
.rp-badge-text span { font-size: 8px; opacity: 0.85; }

/* Carte identité */
.rp-identity {
  display: flex;
  align-items: center;
  gap: 30px;
  background: #F8FAFC;
  border: 1px solid #E2E8F0;
  margin: 0 30px;
  padding: 8px 24px;
  border-radius: 10px;
  box-shadow: 0 1px 3px rgba(0,0,0,0.02);
  height: 76px;
}
.rp-photo-wrap {
  flex-shrink: 0;
  width: 60px;
  height: 60px;
}
.rp-photo-img {
  width: 60px;
  height: 60px;
  object-fit: cover;
  border: 2px solid #2DC5A2;
  border-radius: 8px;
}
.rp-photo-placeholder {
  width: 60px;
  height: 60px;
  background: #F1F5F9;
  display: flex;
  align-items: center;
  justify-content: center;
  border: 2px solid #CBD5E1;
  border-radius: 8px;
}
.rp-photo-icon-img { width: 30px; height: 30px; opacity: 0.6; }
.rp-photo-empty { font-size: 26px; }
.rp-identity-details {
  display: flex;
  flex-direction: column;
  justify-content: center;
  flex-grow: 1;
}
.rp-patient-name { font-size: 14px; font-weight: 800; color: #1A3A6A; margin-bottom: 2px; }
.rp-identity-row { font-size: 9.5px; color: #5D6D7E; margin-bottom: 1px; }

/* Sections */
.rp-section {
  margin: 0 30px;
  border-radius: 8px;
  overflow: hidden;
  border: 1px solid rgba(0,0,0,0.05);
  box-shadow: 0 1px 3px rgba(0,0,0,0.02);
}
.rp-two-col { display: flex; gap: 12px; margin: 0 30px; }
.rp-two-col .rp-section { flex: 1; margin: 0; }
.rp-section-hdr { display: flex; align-items: center; gap: 8px; padding: 4px 12px; color: white; }
.rp-section-hdr h2 { margin: 0; font-size: 9.5px; font-weight: 700; letter-spacing: 0.5px; text-transform: uppercase; }
.rp-num { font-size: 13px; font-weight: 800; opacity: 0.85; line-height: 1; flex-shrink: 0; }
.rp-section-body { padding: 8px 12px; font-size: 10px; line-height: 1.45; }
.rp-section-body h1, .rp-section-body h2, .rp-section-body h3 { font-size: 10px; margin: 4px 0 2px 0; }
.rp-section-body ul { margin: 2px 0; padding-left: 14px; }
.rp-section-body li { margin-bottom: 2px; }
.rp-section-body p { margin-bottom: 3px; }
.rp-placeholder { color: #95A5A6; font-style: italic; }

/* Pied de page */
.rp-footer {
  position: absolute;
  bottom: 20px;
  left: 30px;
  right: 30px;
}
.rp-footer-line {
  border-top: 1px solid #E2E8F0;
  margin-bottom: 8px;
}
.rp-footer-content {
  display: flex;
  justify-content: space-between;
  font-size: 9px;
  color: #94A3B8;
  text-transform: uppercase;
  letter-spacing: 0.5px;
}

/* Couleurs par section */
.rp-s1 .rp-section-hdr { background:#2E6DA4; }  .rp-s1 .rp-section-body { background:#EEF5FB; }
.rp-s2 .rp-section-hdr { background:#C87800; }  .rp-s2 .rp-section-body { background:#FEF9EE; }
.rp-s3 .rp-section-hdr { background:#B03020; }  .rp-s3 .rp-section-body { background:#FDEDEC; }
.rp-s4 .rp-section-hdr { background:#6E2F8A; }  .rp-s4 .rp-section-body { background:#F5EEF8; }
.rp-s5 .rp-section-hdr { background:#1A7840; }  .rp-s5 .rp-section-body { background:#EAFAF1; }
.rp-s6 .rp-section-hdr { background:#0E7060; }  .rp-s6 .rp-section-body { background:#E8F8F5; }

/* ── Page 3+4 : Patient & Contexte (voix clinique) ─────────── */
/* Header sobre (pas de cartouche dégradé), texte noir, cartouches pastel. */
.pc-page {
  padding: 28px 40px 60px 40px;
  font-family: 'Segoe UI', Arial, sans-serif;
  font-size: 11px;
  color: #1A1A1A;
  position: relative;
  min-height: 297mm;
}
.pc-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  padding-bottom: 8px;
}
.pc-header-left { flex: 1; }
.pc-header-left h1 {
  font-size: 26px;
  font-weight: 800;
  color: #1A3A6A;
  letter-spacing: 0.5px;
  margin: 4px 0 2px 0;
}
.pc-section-tag {
  display: inline-block;
  background: #1A3A6A;
  color: white;
  font-size: 10px;
  font-weight: 700;
  padding: 3px 8px;
  border-radius: 4px;
  letter-spacing: 0.5px;
  margin-bottom: 4px;
}
.pc-subtitle {
  font-size: 11px;
  color: #555;
  margin: 2px 0 0 0;
  font-weight: 500;
}
.pc-header-right {
  display: flex;
  align-items: center;
  gap: 8px;
  text-align: right;
}
.pc-logo-img { width: 32px; height: 32px; }
.pc-brand { display: flex; flex-direction: column; line-height: 1.1; }
.pc-brand strong { font-size: 13px; color: #1A3A6A; font-weight: 800; }
.pc-brand span    { font-size: 7.5px; color: #00A896; letter-spacing: 0.6px; text-transform: uppercase; }
.pc-page-num {
  font-size: 9.5px;
  color: #94A3B8;
  letter-spacing: 0.8px;
  margin-left: 16px;
  font-weight: 600;
}
.pc-header-rule {
  border: none;
  border-top: 2px solid #00A896;
  margin: 0 0 16px 0;
}

/* Carte de section principale (pastel) */
.pc-card {
  border-radius: 10px;
  margin-bottom: 12px;
  border: 1px solid rgba(0,0,0,0.05);
  overflow: hidden;
}
.pc-card-hdr {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 6px 14px;
  color: white;
}
.pc-card-hdr h2 {
  font-size: 12px;
  font-weight: 800;
  letter-spacing: 0.8px;
  margin: 0;
  text-transform: uppercase;
}
.pc-card-num {
  background: rgba(255,255,255,0.25);
  border-radius: 50%;
  width: 22px;
  height: 22px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  font-size: 12px;
  font-weight: 800;
}
.pc-card-body {
  padding: 10px 14px;
  font-size: 10.5px;
  line-height: 1.5;
  color: #1A1A1A;
}
.pc-card-body p { margin-bottom: 4px; }
.pc-card-body ul { margin: 4px 0; padding-left: 18px; }
.pc-card-body li { margin-bottom: 2px; }
.pc-narrative p { font-size: 11px; line-height: 1.55; }

/* Cartouche Identification : grille méta-données */
.pc-ident-narrative {
  font-size: 11px;
  line-height: 1.55;
  margin-bottom: 10px;
}
.pc-ident-meta {
  border-top: 1px dashed rgba(0,0,0,0.08);
  padding-top: 8px;
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 6px 24px;
}
.pc-meta-row {
  display: flex;
  align-items: baseline;
  gap: 10px;
}
.pc-meta-label {
  font-weight: 700;
  color: #555;
  font-size: 9.5px;
  text-transform: uppercase;
  letter-spacing: 0.3px;
  min-width: 140px;
}
.pc-meta-value {
  font-size: 10.5px;
  color: #1A1A1A;
}

/* Grilles colonnes pour Contexte familial / Antécédents / Situation */
.pc-four-col, .pc-three-col, .pc-two-col {
  display: flex;
  gap: 8px;
  margin-top: 10px;
}
.pc-four-col  > * { flex: 1; min-width: 0; }
.pc-three-col > * { flex: 1; min-width: 0; }
.pc-two-col   > * { flex: 1; min-width: 0; }

/* Mini-cartes (4 colonnes du Contexte familial) */
.pc-mini-card {
  background: white;
  border: 1px solid rgba(0,0,0,0.06);
  border-radius: 6px;
  padding: 6px 8px;
  font-size: 9.5px;
}
.pc-mini-card h3 {
  font-size: 9.5px;
  font-weight: 800;
  color: #1A3A6A;
  margin: 0 0 4px 0;
  letter-spacing: 0.4px;
}
.pc-mini-body ul { margin: 2px 0; padding-left: 14px; }
.pc-mini-body li { margin-bottom: 1px; }
.pc-mini-body p { margin-bottom: 2px; }

/* Sous-blocs (Antécédents / Situation) */
.pc-subblock {
  background: white;
  border: 1px solid rgba(0,0,0,0.06);
  border-radius: 6px;
  padding: 8px 10px;
  font-size: 9.5px;
}
.pc-subblock h3 {
  font-size: 9.5px;
  font-weight: 800;
  color: #1A3A6A;
  margin: 0 0 5px 0;
  letter-spacing: 0.4px;
  text-transform: uppercase;
}
.pc-subblock-body ul { margin: 2px 0; padding-left: 14px; }
.pc-subblock-body li { margin-bottom: 2px; }
.pc-subblock-body p { margin-bottom: 3px; }
.pc-subblock-body strong { color: #1A3A6A; }

/* Footer info */
.pc-footer {
  margin-top: 14px;
  border-top: 1px solid #E2E8F0;
  padding-top: 8px;
  font-size: 9.5px;
  color: #555;
}
.pc-footer-info {
  text-align: center;
  line-height: 1.5;
  font-style: italic;
}
.pc-info-icon {
  display: inline-block;
  width: 14px;
  height: 14px;
  border-radius: 50%;
  background: #00A896;
  color: white;
  font-style: normal;
  font-weight: 800;
  font-size: 9px;
  line-height: 14px;
  text-align: center;
  margin-right: 6px;
}
.pc-placeholder { color: #95A5A6; font-style: italic; }

/* Palette pastel par cartouche principale (header en teinte pleine, body en pastel) */
.pc-c-ident     .pc-card-hdr { background: #00A896; }  .pc-c-ident     .pc-card-body { background: #EAF4F6; }
.pc-c-motif     .pc-card-hdr { background: #6E2F8A; }  .pc-c-motif     .pc-card-body { background: #F5EEF8; }
.pc-c-famille   .pc-card-hdr { background: #1A7840; }  .pc-c-famille   .pc-card-body { background: #EAFAF1; }
.pc-c-atcd      .pc-card-hdr { background: #2E6DA4; }  .pc-c-atcd      .pc-card-body { background: #EEF5FB; }
.pc-c-situation .pc-card-hdr { background: #C87800; }  .pc-c-situation .pc-card-body { background: #FEF3E7; }

/* Petits accents teal sur les titres des mini-cartes pour rappeler le filet du header */
.pc-mc-pere    h3,
.pc-mc-mere    h3,
.pc-mc-fratrie h3 { color: #1A7840; }
.pc-mc-autres  h3 { color: #00A896; }

/* Parcours compact — 2 sous-sections SUIVI / BILANS dans la mini-carte */
.pc-parcours-sub { margin-bottom: 6px; }
.pc-parcours-sub h4 {
  font-size: 9px; font-weight: 700; text-transform: uppercase;
  letter-spacing: .4px; color: #1A5276; margin: 0 0 3px 0;
  border-bottom: 1px solid #D6EAF8; padding-bottom: 2px;
}
.pc-parcours-detail-link {
  margin-top: 6px; font-size: 9px; color: #1A5276;
  font-style: italic; opacity: .8;
}

/* Page C : contenu détaillé suivi / bilans */
.pc-detail-content { font-size: 11px; line-height: 1.55; }
.pc-detail-content p { margin-bottom: 6px; }
.pc-detail-content ul { margin: 4px 0; padding-left: 18px; }
.pc-detail-content li { margin-bottom: 4px; }
.pc-detail-content strong { color: #1A5276; }

/* Bandeau Points à retenir — pleine largeur sous les 4 colonnes */
.pc-points-banner {
  margin-top: 10px;
  background: #EAF7F2;
  border-left: 4px solid #00A896;
  border-radius: 4px;
  padding: 8px 12px;
}
.pc-points-banner h3 {
  font-size: 10px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: .5px;
  color: #00A896;
  margin: 0 0 4px 0;
}
.pc-points-body { font-size: 11px; line-height: 1.5; }

/* ── Pages 7+8 : Cartographie de l'enfant (voix très pro) ──── */
/* Reprend le pattern Patient & Contexte (.pc-page) pour la cohérence visuelle,
   avec 4 cartouches par page + une légende des 5 niveaux en pied. */
.ce-page {
  padding: 28px 40px 30px 40px;
  font-family: 'Segoe UI', Arial, sans-serif;
  font-size: 11px;
  color: #1A1A1A;
  position: relative;
  min-height: 297mm;
}

/* Cartouche d'une sphère */
.ce-card {
  border: 1px solid rgba(0,0,0,0.07);
  border-radius: 10px;
  margin-bottom: 10px;
  background: white;
  overflow: hidden;
  box-shadow: 0 1px 2px rgba(0,0,0,0.02);
}
.ce-card-hdr {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 6px 12px;
  color: white;
  font-weight: 700;
  letter-spacing: 0.4px;
  text-transform: uppercase;
}
.ce-card-hdr .ce-num {
  background: rgba(255,255,255,0.25);
  border-radius: 6px;
  width: 26px;
  height: 26px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  font-size: 13px;
  font-weight: 800;
  flex-shrink: 0;
}
.ce-card-title { line-height: 1.15; }
.ce-card-title strong { display: block; font-size: 12px; }
.ce-card-title em     { font-style: normal; opacity: 0.85; font-weight: 600; font-size: 9.5px; }

.ce-card-body {
  display: flex;
  gap: 14px;
  padding: 10px 14px;
  align-items: stretch;
}
.ce-chart {
  flex-shrink: 0;
  width: 120px;
  height: 120px;
  border-radius: 50%;
  background: #F1F5F9;
  display: flex;
  align-items: center;
  justify-content: center;
  border: 2px dashed #CBD5E1;
}
.ce-chart-radar {
  border-radius: 8px;
  background: #FAFBFC;
}
.ce-chart-placeholder {
  color: #94A3B8;
  font-size: 9.5px;
  font-style: italic;
  text-align: center;
  padding: 0 8px;
}

.ce-card-text { flex: 1; min-width: 0; display: flex; flex-direction: column; gap: 4px; }
.ce-obs-title, .ce-niveau-title {
  font-size: 10px;
  font-weight: 700;
  color: #6E2F8A;
  letter-spacing: 0.4px;
}
.ce-obs-body  { font-size: 10px; line-height: 1.45; color: #1A1A1A; }
.ce-obs-body ul { margin: 2px 0; padding-left: 16px; }
.ce-obs-body li { margin-bottom: 1px; }
.ce-niveau-title { margin-top: 4px; }
.ce-niveau-body  { font-size: 10.5px; color: #1A1A1A; font-weight: 600; }
.ce-placeholder  { color: #95A5A6; font-style: italic; }

/* Pie SVG : retire le placeholder (chart visible), garde le cadre rond */
.ce-chart-pie { background: transparent; border: none; padding: 0; }
.ce-pie-svg   { width: 120px; height: 120px; display: block; }

/* Texte du niveau clinique — couleur de la section (pas la couleur du niveau).
   Style PDF modèle : phrase courte en gras, couleur du header de la cartouche. */
.ce-niveau-text {
  font-size: 11px;
  font-weight: 700;
  letter-spacing: 0.2px;
}

/* Couleur du texte niveau clinique = couleur du header de la sphère.
   Cohérent avec le modèle PDF (Fragilisé en teal pour Attachement, en rouge pour Régulation…). */
.ce-s1 .ce-niveau-text { color: #00A896; }
.ce-s2 .ce-niveau-text { color: #B03020; }
.ce-s3 .ce-niveau-text { color: #2E6DA4; }
.ce-s4 .ce-niveau-text { color: #C87800; }
.ce-s5 .ce-niveau-text { color: #1F4E79; }
.ce-s6 .ce-niveau-text { color: #1A7840; }
.ce-s7 .ce-niveau-text { color: #6E2F8A; }
.ce-s8 .ce-niveau-text { color: #0E7060; }

/* Palette pastel par sphère (header en teinte pleine).
   8 cartouches → 8 teintes distinctes mais cohérentes avec l'ensemble du dossier. */
.ce-s1 .ce-card-hdr { background: #00A896; }  /* Attachement — teal */
.ce-s2 .ce-card-hdr { background: #B03020; }  /* Régulation — rouge sourd */
.ce-s3 .ce-card-hdr { background: #2E6DA4; }  /* Langage — bleu */
.ce-s4 .ce-card-hdr { background: #C87800; }  /* Tempérament — orange */
.ce-s5 .ce-card-hdr { background: #1F4E79; }  /* Psychomotricité — bleu nuit */
.ce-s6 .ce-card-hdr { background: #1A7840; }  /* Imagination — vert */
.ce-s7 .ce-card-hdr { background: #6E2F8A; }  /* Pensée — violet */
.ce-s8 .ce-card-hdr { background: #0E7060; }  /* Attention — teal foncé */

/* Légende ÉCHELLE DE NIVEAU en bas de page */
.ce-legende-title {
  font-size: 10px;
  font-weight: 700;
  color: #1A3A6A;
  text-transform: uppercase;
  letter-spacing: 0.6px;
  margin: 12px 0 6px 0;
}
.ce-legende {
  display: flex;
  justify-content: space-between;
  gap: 6px;
  padding: 8px 10px;
  background: #FAFBFC;
  border: 1px solid #E2E8F0;
  border-radius: 8px;
}
.ce-legende-item {
  display: flex;
  align-items: center;
  gap: 6px;
  flex: 1;
}
.ce-legende-dot {
  display: inline-block;
  width: 14px;
  height: 14px;
  border-radius: 50%;
  flex-shrink: 0;
}
.ce-legende-text {
  display: flex;
  flex-direction: column;
  line-height: 1.1;
}
.ce-legende-text strong { font-size: 9.5px; color: #1A1A1A; font-weight: 700; }
.ce-legende-text span   { font-size: 8.5px; color: #6B7A8D; }

/* Pied de page info */
.ce-footer {
  margin-top: 8px;
  padding: 8px 10px;
  font-size: 9.5px;
  color: #555;
  font-style: italic;
  text-align: center;
  border-top: 1px solid #E2E8F0;
  line-height: 1.5;
}
";
    }
}
