using System;
using System.IO;
using MedCompanion.Models;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.IO;
using PdfSharp.Fonts;

namespace MedCompanion.Services
{
    /// <summary>
    /// FontResolver simple pour PDFsharp - Utilise les polices système Windows
    /// </summary>
    internal class SimpleFontResolver : IFontResolver
    {
        public byte[]? GetFont(string faceName)
        {
            // Mapping des polices courantes vers les fichiers Windows
            var fontPath = faceName.ToLowerInvariant() switch
            {
                "arial" => @"C:\Windows\Fonts\arial.ttf",
                "arial bold" => @"C:\Windows\Fonts\arialbd.ttf",
                "times new roman" => @"C:\Windows\Fonts\times.ttf",
                "courier new" => @"C:\Windows\Fonts\cour.ttf",
                "calibri" => @"C:\Windows\Fonts\calibri.ttf",
                _ => @"C:\Windows\Fonts\arial.ttf" // Défaut: Arial
            };

            if (File.Exists(fontPath))
            {
                return File.ReadAllBytes(fontPath);
            }

            // Si le fichier n'existe pas, retourner Arial par défaut
            var defaultPath = @"C:\Windows\Fonts\arial.ttf";
            return File.Exists(defaultPath) ? File.ReadAllBytes(defaultPath) : null;
        }

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
        {
            // Normaliser le nom de la police
            var fontName = familyName.ToLowerInvariant();

            if (bold && italic)
                fontName += " bold italic";
            else if (bold)
                fontName += " bold";
            else if (italic)
                fontName += " italic";

            return new FontResolverInfo(fontName);
        }
    }

    /// <summary>
    /// Service pour remplir automatiquement des champs de formulaires PDF (AcroForms)
    /// </summary>
    public class PDFFormFillerService
    {
        private static bool _fontResolverInitialized = false;

        /// <summary>
        /// Initialise le FontResolver pour PDFsharp (une seule fois)
        /// </summary>
        private static void EnsureFontResolverInitialized()
        {
            if (!_fontResolverInitialized)
            {
                GlobalFontSettings.FontResolver = new SimpleFontResolver();
                _fontResolverInitialized = true;
                System.Diagnostics.Debug.WriteLine("[PDFFormFillerService] FontResolver initialisé");
            }
        }

        public PDFFormFillerService()
        {
            EnsureFontResolverInitialized();
        }
        /// <summary>
        /// Remplit un champ spécifique dans un formulaire PDF
        /// </summary>
        /// <param name="pdfPath">Chemin du PDF à modifier</param>
        /// <param name="fieldName">Nom du champ AcroForm</param>
        /// <param name="value">Valeur à insérer</param>
        /// <returns>Tuple (succès, message d'erreur)</returns>
        public (bool success, string? error) FillFormField(string pdfPath, string fieldName, string value)
        {
            try
            {
                // Vérifier que le fichier existe
                if (!File.Exists(pdfPath))
                {
                    return (false, $"Le fichier PDF n'existe pas: {pdfPath}");
                }

                // Ouvrir le PDF en mode modification
                PdfDocument document;
                try
                {
                    document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);
                }
                catch (Exception ex)
                {
                    return (false, $"Impossible d'ouvrir le PDF: {ex.Message}");
                }

                // Vérifier que le PDF contient un formulaire
                if (document.AcroForm == null)
                {
                    document.Close();
                    return (false, "Le PDF ne contient pas de champs de formulaire (AcroForm)");
                }

                // Chercher le champ
                var field = document.AcroForm.Fields[fieldName];
                if (field == null)
                {
                    document.Close();
                    return (false, $"Le champ '{fieldName}' n'existe pas dans le PDF");
                }

                // Remplir le champ
                field.Value = new PdfString(value);

                // Sauvegarder les modifications
                try
                {
                    document.Save(pdfPath);
                    document.Close();
                    return (true, null);
                }
                catch (Exception ex)
                {
                    document.Close();
                    return (false, $"Erreur lors de la sauvegarde: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                return (false, $"Erreur inattendue: {ex.Message}");
            }
        }

        /// <summary>
        /// Remplit le formulaire MDPH test avec les informations du patient
        /// </summary>
        /// <param name="patient">Patient sélectionné</param>
        /// <param name="templatePath">Chemin du template PDF</param>
        /// <param name="outputPath">Chemin du PDF de sortie</param>
        /// <returns>Tuple (succès, chemin de sortie, message d'erreur)</returns>
        public (bool success, string outputPath, string? error) FillMDPHTestForm(
            PatientIndexEntry patient,
            string templatePath,
            string outputPath)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[PDFFormFillerService] Remplissage MDPH test");
                System.Diagnostics.Debug.WriteLine($"  Template: {templatePath}");
                System.Diagnostics.Debug.WriteLine($"  Output: {outputPath}");
                System.Diagnostics.Debug.WriteLine($"  Patient: {patient.Prenom} {patient.Nom}");

                // Vérifier que le template existe
                if (!File.Exists(templatePath))
                {
                    return (false, "", $"Le template PDF n'existe pas: {templatePath}");
                }

                // Créer le dossier de destination si nécessaire
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(outputDir) && !Directory.Exists(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                    System.Diagnostics.Debug.WriteLine($"  Dossier créé: {outputDir}");
                }

                // Copier le template vers la destination
                try
                {
                    File.Copy(templatePath, outputPath, overwrite: true);
                    System.Diagnostics.Debug.WriteLine($"  Template copié vers destination");
                }
                catch (Exception ex)
                {
                    return (false, "", $"Erreur lors de la copie du template: {ex.Message}");
                }

                // Ouvrir le PDF copié en mode modification
                PdfDocument document;
                try
                {
                    document = PdfReader.Open(outputPath, PdfDocumentOpenMode.Modify);
                }
                catch (Exception ex)
                {
                    return (false, "", $"Impossible d'ouvrir le PDF: {ex.Message}");
                }

                // Vérifier que le formulaire existe
                if (document.AcroForm == null)
                {
                    document.Close();
                    return (false, "", "Le PDF ne contient pas de champs de formulaire (AcroForm)");
                }

                System.Diagnostics.Debug.WriteLine($"  Nombre de champs dans le formulaire: {document.AcroForm.Fields.Count}");

                // Lister tous les champs disponibles (pour debug)
                foreach (var fieldName in document.AcroForm.Fields.Names)
                {
                    System.Diagnostics.Debug.WriteLine($"    Champ disponible: '{fieldName}'");
                }

                // Remplir le champ "nom patient"
                var nomPatientField = document.AcroForm.Fields["nom patient"];
                if (nomPatientField == null)
                {
                    document.Close();
                    return (false, "", "Le champ 'nom patient' n'existe pas dans le PDF");
                }

                string nomComplet = $"{patient.Prenom} {patient.Nom}";
                nomPatientField.Value = new PdfString(nomComplet);
                System.Diagnostics.Debug.WriteLine($"  Champ 'nom patient' rempli avec: {nomComplet}");

                // Sauvegarder les modifications
                try
                {
                    document.Save(outputPath);
                    document.Close();
                    System.Diagnostics.Debug.WriteLine($"  PDF sauvegardé avec succès");
                    return (true, outputPath, null);
                }
                catch (Exception ex)
                {
                    document.Close();
                    return (false, "", $"Erreur lors de la sauvegarde: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PDFFormFillerService] ERREUR: {ex.Message}\n{ex.StackTrace}");
                return (false, "", $"Erreur inattendue: {ex.Message}");
            }
        }

        /// <summary>
        /// Liste tous les champs disponibles dans un PDF (pour diagnostic)
        /// </summary>
        /// <param name="pdfPath">Chemin du PDF à analyser</param>
        /// <returns>Tuple (succès, liste des noms de champs, message d'erreur)</returns>
        public (bool success, string[] fieldNames, string? error) ListFormFields(string pdfPath)
        {
            try
            {
                if (!File.Exists(pdfPath))
                {
                    return (false, Array.Empty<string>(), $"Le fichier PDF n'existe pas: {pdfPath}");
                }

                var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.ReadOnly);

                if (document.AcroForm == null)
                {
                    document.Close();
                    return (false, Array.Empty<string>(), "Le PDF ne contient pas de formulaire");
                }

                var fieldNames = new string[document.AcroForm.Fields.Count];
                for (int i = 0; i < document.AcroForm.Fields.Count; i++)
                {
                    fieldNames[i] = document.AcroForm.Fields.Names[i];
                }

                document.Close();
                return (true, fieldNames, null);
            }
            catch (Exception ex)
            {
                return (false, Array.Empty<string>(), $"Erreur: {ex.Message}");
            }
        }
    }
}
