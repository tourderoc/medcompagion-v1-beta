using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using MedCompanion.Models;

namespace MedCompanion.Services
{
    public class RestitutionPdfTestService
    {
        private readonly EdgeHeadlessPdfService _pdfService;

        public RestitutionPdfTestService(EdgeHeadlessPdfService pdfService)
        {
            _pdfService = pdfService;
        }

        public async Task<string> GenerateTestPdfAsync(PatientMetadata patient, string? photoPath, string outputFolder)
        {
            // 1. Lire le template HTML
            var templatePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Consultation", "restitution_clinique_template.html");
            if (!File.Exists(templatePath))
            {
                throw new FileNotFoundException("Template HTML introuvable.", templatePath);
            }

            var html = File.ReadAllText(templatePath, Encoding.UTF8);

            // 2. Préparer l'image de l'arbre en Base64
            string treeBase64 = string.Empty;
            var treePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "PdfGraphics", "cover_tree.png");
            if (File.Exists(treePath))
            {
                var bytes = File.ReadAllBytes(treePath);
                treeBase64 = Convert.ToBase64String(bytes);
            }

            // 2bis. Embarquer la police Caveat localement (Latin + Latin Extended).
            // Garantit le rendu cursive identique avec ou sans internet.
            var fontsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Consultation", "Fonts");
            string caveatLatinBase64    = LoadFontBase64(Path.Combine(fontsDir, "Caveat-Regular-latin.woff2"));
            string caveatLatinExtBase64 = LoadFontBase64(Path.Combine(fontsDir, "Caveat-Regular-latin-ext.woff2"));

            // 2ter. Logo MedCompanion (cœur PNG fourni par l'utilisateur).
            var iconsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Consultation", "Icons");
            string medcompanionHeartBase64 = LoadFontBase64(Path.Combine(iconsDir, "medcompanion_heart.png"));

            // 2quater. Signature manuscrite du médecin (déjà utilisée pour les courriers).
            string signatureBase64 = LoadFontBase64(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "signature.png"));

            // 3. Remplacer les variables
            string ageStr = patient.Age.HasValue ? $"Âge : {patient.Age} ans" : "";
            
            html = html.Replace("{{NOM_ENFANT}}", patient.NomComplet ?? "");
            html = html.Replace("{{DATE_NAISSANCE}}", patient.DobFormatted ?? "Non renseignée");
            html = html.Replace("{{AGE}}", ageStr);
            html = html.Replace("{{CLASSE}}", patient.Classe ?? "Non renseignée");
            html = html.Replace("{{ECOLE}}", patient.Ecole ?? "Non renseigné");
            html = html.Replace("{{ANNEE_SCOLAIRE}}", "Année scolaire 2025-2026");
            
            // Nouvelles variables de test pour correspondre au design
            html = html.Replace("{{MOTIF_CONSULTATION}}", "Crises de colère,\nénervement fréquent,\nhyperactivité");
            html = html.Replace("{{DATES_EVALUATION}}", "15/05/2026 – 16/05/2026 – 17/05/2026");
            html = html.Replace("{{DATE_RESTITUTION}}", "05/06/2026");

            html = html.Replace("{{TREE_BASE64}}", treeBase64);
            html = html.Replace("{{CAVEAT_LATIN_BASE64}}",         caveatLatinBase64);
            html = html.Replace("{{CAVEAT_LATIN_EXT_BASE64}}",     caveatLatinExtBase64);
            html = html.Replace("{{MEDCOMPANION_HEART_BASE64}}",   medcompanionHeartBase64);
            html = html.Replace("{{SIGNATURE_BASE64}}",            signatureBase64);

            html = html.Replace("{{ICON_PATIENT_BASE64}}",         LoadFontBase64(Path.Combine(iconsDir, "mc_patient.png")));
            html = html.Replace("{{ICON_NAISSANCE_BASE64}}",       LoadFontBase64(Path.Combine(iconsDir, "mc_naissance_calendar.png")));
            html = html.Replace("{{ICON_SCOLARITE_BASE64}}",       LoadFontBase64(Path.Combine(iconsDir, "mc_scolarite_school.png")));
            html = html.Replace("{{ICON_CONSULTATION_BASE64}}",    LoadFontBase64(Path.Combine(iconsDir, "mc_consultation_stethoscope.png")));
            html = html.Replace("{{ICON_EVALUATION_BASE64}}",      LoadFontBase64(Path.Combine(iconsDir, "mc_naissance_clipboard.png")));
            html = html.Replace("{{ICON_RESTITUTION_BASE64}}",     LoadFontBase64(Path.Combine(iconsDir, "mc_restitution_graph.png")));
            html = html.Replace("{{ICON_COMPREHENSION_BASE64}}",   LoadFontBase64(Path.Combine(iconsDir, "mc_bienveillance_hands.png")));
            html = html.Replace("{{ICON_REPERES_BASE64}}",         LoadFontBase64(Path.Combine(iconsDir, "mc_reperes_pins.png")));
            html = html.Replace("{{ICON_OBJECTIFS_BASE64}}",       LoadFontBase64(Path.Combine(iconsDir, "mc_resolution_checklist.png")));
            html = html.Replace("{{ICON_COLLABORATION_BASE64}}",   LoadFontBase64(Path.Combine(iconsDir, "mc_collaboration_people.png")));
            html = html.Replace("{{ICON_PERSPECTIVES_BASE64}}",    LoadFontBase64(Path.Combine(iconsDir, "mc_perspectives_chart.png")));
            html = html.Replace("{{ICON_CONFIDENTIEL_BASE64}}",    LoadFontBase64(Path.Combine(iconsDir, "mc_confidentialite_shield.png")));
            html = html.Replace("{{ICON_PEDOPSYCHIATRIE_BASE64}}", LoadFontBase64(Path.Combine(iconsDir, "mc_medical_head.png")));

            // 4. Sauvegarder le HTML temporaire
            var tmpHtmlPath = Path.Combine(Path.GetTempPath(), $"restitution_temp_{DateTime.Now.Ticks}.html");
            File.WriteAllText(tmpHtmlPath, html, Encoding.UTF8);

            // 5. Convertir en PDF avec Edge Headless (Chrome Engine)
            var pdfPath = Path.Combine(outputFolder, $"Test_Restitution_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
            
            bool success = await _pdfService.ConvertAsync(tmpHtmlPath, pdfPath);
            
            if (!success)
            {
                throw new Exception("Échec de la conversion HTML vers PDF via Edge Headless.");
            }

            // Nettoyage
            try { File.Delete(tmpHtmlPath); } catch { }

            return pdfPath;
        }

        private static string LoadFontBase64(string path)
        {
            if (!File.Exists(path)) return string.Empty;
            return Convert.ToBase64String(File.ReadAllBytes(path));
        }
    }
}
