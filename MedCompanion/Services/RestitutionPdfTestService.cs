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

            // 3. Remplacer les variables
            string ageStr = patient.Age.HasValue ? $"Âge : {patient.Age} ans" : "";
            
            html = html.Replace("{{NOM_ENFANT}}", patient.NomComplet ?? "");
            html = html.Replace("{{DATE_NAISSANCE}}", patient.DobFormatted ?? "Non renseignée");
            html = html.Replace("{{AGE}}", ageStr);
            html = html.Replace("{{CLASSE}}", patient.Classe ?? "Non renseignée");
            html = html.Replace("{{ECOLE}}", patient.Ecole ?? "Non renseigné");
            
            // Nouvelles variables de test pour correspondre au design
            html = html.Replace("{{MOTIF_CONSULTATION}}", "Crises de colère,\nénervement fréquent,\nhyperactivité");
            html = html.Replace("{{DATES_EVALUATION}}", "15/05/2026 – 16/05/2026 – 17/05/2026");
            html = html.Replace("{{DATE_RESTITUTION}}", "05/06/2026");

            html = html.Replace("{{TREE_BASE64}}", treeBase64);

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
    }
}
