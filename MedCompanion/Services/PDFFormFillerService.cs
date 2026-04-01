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
        /// Remplit la page 1 du formulaire MDPH avec les données patient et les réponses IA
        /// </summary>
        /// <param name="patient">Métadonnées du patient</param>
        /// <param name="formData">Données du formulaire générées par l'IA (optionnel pour page 1)</param>
        /// <param name="demandes">Liste des demandes cochées (AESH, AEEH, etc.)</param>
        /// <param name="templatePath">Chemin du template PDF</param>
        /// <param name="outputPath">Chemin du PDF de sortie</param>
        /// <returns>Tuple (succès, chemin de sortie, message d'erreur)</returns>
        public (bool success, string outputPath, string? error) FillMDPHPage1(
            PatientMetadata patient,
            MDPHFormData? formData,
            string demandes,
            string templatePath,
            string outputPath)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[PDFFormFillerService] Remplissage MDPH Page 1");
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

                    // Diagnostiquer les champs problématiques connus
                    if (fieldName.Contains("remarque") || fieldName.Contains("signe") || fieldName.Contains("demandes"))
                    {
                        DiagnoseField(document, fieldName);
                    }
                }

                // ========== REMPLISSAGE PAGE 1 ==========

                // Date de création
                SetFieldValue(document, "date_creation", DateTime.Now.ToString("dd/MM/yyyy"));

                // Identité patient
                SetFieldValue(document, "patient_nom", patient.Nom);
                SetFieldValue(document, "patient_prenom", patient.Prenom);
                SetFieldValue(document, "patient_dob", patient.DobFormatted ?? "");
                SetFieldValue(document, "patient_lieu_naissance", patient.LieuNaissance ?? "");
                SetFieldValue(document, "patient_num_secu", patient.NumeroSecuriteSociale ?? "");

                // Adresse (fusion en un seul champ multiligne)
                var adresseComplete = BuildAdresseComplete(patient);
                SetFieldValue(document, "patient_adresse", adresseComplete);

                // Demandes MDPH (section "À joindre à ce document")
                if (!string.IsNullOrWhiteSpace(demandes))
                {
                    SetFieldValue(document, "demandes_joindre", demandes);
                }

                // Si formData est fourni, remplir aussi les sections médicales de la page 1 (si présentes)
                if (formData != null)
                {
                    SetFieldValue(document, "pathologie_principale", formData.PathologiePrincipale);
                    SetFieldValue(document, "autres_pathologies", formData.AutresPathologies);
                }

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
        /// Helper pour définir la valeur d'un champ (avec gestion d'erreur silencieuse)
        /// </summary>
        private void SetFieldValue(PdfDocument document, string fieldName, string value)
        {
            try
            {
                var field = document.AcroForm?.Fields[fieldName];

                // Si le champ n'est pas trouvé, essayer avec des espaces trailing (bug fréquent dans les PDF)
                if (field == null && document.AcroForm != null)
                {
                    // Chercher un champ qui commence par le nom demandé ou qui a des espaces
                    foreach (var name in document.AcroForm.Fields.Names)
                    {
                        if (name.Trim() == fieldName || name.TrimEnd() == fieldName)
                        {
                            field = document.AcroForm.Fields[name];
                            System.Diagnostics.Debug.WriteLine($"  🔍 Champ '{fieldName}' trouvé sous le nom '{name}' (avec espaces trailing)");
                            break;
                        }
                    }
                }

                if (field != null && !string.IsNullOrWhiteSpace(value))
                {
                    // Configurer le champ comme multiligne si la valeur contient des retours à la ligne
                    if (value.Contains("\n") || value.Contains("\r"))
                    {
                        // Forcer les flags multiligne pour le champ texte
                        if (field.Elements.ContainsKey("/Ff"))
                        {
                            var flags = field.Elements.GetInteger("/Ff");
                            // Flag 0x1000 (4096) = Multiline
                            // Flag 0x2000 (8192) = DoNotScroll (pour forcer l'ajustement)
                            field.Elements.SetInteger("/Ff", flags | 0x1000);
                        }
                        else
                        {
                            // Si le flag n'existe pas, le créer avec Multiline activé
                            field.Elements.SetInteger("/Ff", 0x1000);
                        }

                        // Forcer la taille de police auto pour les champs multiligne longs
                        // Cela permet au texte de s'adapter à la hauteur du champ
                        if (field.Elements.ContainsKey("/DA"))
                        {
                            // Modifier /DA pour utiliser une taille de police automatique (0)
                            var currentDA = field.Elements.GetString("/DA");
                            if (!string.IsNullOrEmpty(currentDA))
                            {
                                // Remplacer la taille de police fixe par 0 (auto)
                                var newDA = System.Text.RegularExpressions.Regex.Replace(
                                    currentDA,
                                    @"\d+(\.\d+)?\s+Tf",
                                    "0 Tf"
                                );
                                field.Elements.SetString("/DA", newDA);
                                System.Diagnostics.Debug.WriteLine($"  ℹ Police auto-ajustée pour '{fieldName}'");
                            }
                        }

                        System.Diagnostics.Debug.WriteLine($"  ℹ Champ '{fieldName}' configuré en multiligne");
                    }

                    field.Value = new PdfString(value);
                    System.Diagnostics.Debug.WriteLine($"  ✓ Champ '{fieldName}' rempli: {value.Substring(0, Math.Min(50, value.Length))}...");
                }
                else if (field == null)
                {
                    System.Diagnostics.Debug.WriteLine($"  ⚠ Champ '{fieldName}' introuvable dans le PDF");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"  ✗ Erreur remplissage champ '{fieldName}': {ex.Message}");
            }
        }

        /// <summary>
        /// Construit l'adresse complète sur plusieurs lignes
        /// </summary>
        private string BuildAdresseComplete(PatientMetadata patient)
        {
            var adresseLines = new List<string>();

            if (!string.IsNullOrWhiteSpace(patient.AdresseRue))
                adresseLines.Add(patient.AdresseRue);

            var codePostalVille = new List<string>();
            if (!string.IsNullOrWhiteSpace(patient.AdresseCodePostal))
                codePostalVille.Add(patient.AdresseCodePostal);
            if (!string.IsNullOrWhiteSpace(patient.AdresseVille))
                codePostalVille.Add(patient.AdresseVille);

            if (codePostalVille.Count > 0)
                adresseLines.Add(string.Join(" ", codePostalVille));

            if (!string.IsNullOrWhiteSpace(patient.AdressePays) && patient.AdressePays != "France")
                adresseLines.Add(patient.AdressePays);

            return string.Join("\n", adresseLines);
        }

        /// <summary>
        /// Remplit TOUTES les pages (1-8) du formulaire MDPH CERFA 15695*01
        /// </summary>
        public (bool success, string outputPath, string? error) FillMDPHComplete(
            PatientMetadata patient,
            MDPHFormData formData,
            string demandes,
            string templatePath,
            string outputPath)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"[PDFFormFillerService] Remplissage MDPH COMPLET (8 pages)");
                System.Diagnostics.Debug.WriteLine($"  Template: {templatePath}");
                System.Diagnostics.Debug.WriteLine($"  Output: {outputPath}");
                System.Diagnostics.Debug.WriteLine($"  Patient: {patient.Prenom} {patient.Nom}");
                System.Diagnostics.Debug.WriteLine($"  📍 AdresseRue: '{patient.AdresseRue ?? "NULL"}'");
                System.Diagnostics.Debug.WriteLine($"  📍 AdresseCodePostal: '{patient.AdresseCodePostal ?? "NULL"}'");
                System.Diagnostics.Debug.WriteLine($"  📍 AdresseVille: '{patient.AdresseVille ?? "NULL"}'");
                System.Diagnostics.Debug.WriteLine($"  🔢 NumeroSecuriteSociale: '{patient.NumeroSecuriteSociale ?? "NULL"}'");

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

                // 🔧 FIX: Forcer le redessin de l'apparence des champs (pour Chrome, Edge, etc.)
                if (document.AcroForm.Elements.ContainsKey("/NeedAppearances"))
                {
                    document.AcroForm.Elements.SetBoolean("/NeedAppearances", true);
                }
                else
                {
                    document.AcroForm.Elements.Add("/NeedAppearances", new PdfBoolean(true));
                }
                System.Diagnostics.Debug.WriteLine($"  Flag '/NeedAppearances' activé");

                System.Diagnostics.Debug.WriteLine($"  Nombre de champs dans le formulaire: {document.AcroForm.Fields.Count}");

                // ========== PAGE 1: IDENTITÉ + DEMANDES ==========
                SetFieldValue(document, "date_creation", DateTime.Now.ToString("dd/MM/yyyy"));
                SetFieldValue(document, "patient_nom", patient.Nom);
                SetFieldValue(document, "patient_prenom", patient.Prenom);
                SetFieldValue(document, "patient_dob", patient.DobFormatted ?? "");
                SetFieldValue(document, "patient_lieu_naissance", patient.LieuNaissance ?? "");
                SetFieldValue(document, "patient_num_secu", patient.NumeroSecuriteSociale ?? "");

                var adresseComplete = BuildAdresseComplete(patient);
                System.Diagnostics.Debug.WriteLine($"  📍 Adresse complète construite: '{adresseComplete}'");
                SetFieldValue(document, "patient_adresse", adresseComplete);

                // Demandes
                if (!string.IsNullOrWhiteSpace(demandes))
                {
                    SetFieldValue(document, "demandes_joindre", demandes);
                }

                // ========== PAGE 2-3: PATHOLOGIES ==========
                SetFieldValue(document, "pathologie_principale", formData.PathologiePrincipale);
                SetFieldValue(document, "autres_pathologies", formData.AutresPathologies);

                // ========== PAGE 3: ÉLÉMENTS ESSENTIELS ==========
                if (formData.ElementsEssentiels != null && formData.ElementsEssentiels.Count > 0)
                {
                    var elementsText = string.Join("\n\n", formData.ElementsEssentiels.Select((e, i) => $"• {e}"));
                    SetFieldValue(document, "elements_essentiels", elementsText);
                }

                // ========== PAGE 3: ANTÉCÉDENTS MÉDICAUX ==========
                if (formData.AntecedentsMedicaux != null && formData.AntecedentsMedicaux.Count > 0)
                {
                    var antecedentsText = string.Join("\n", formData.AntecedentsMedicaux.Select((a, i) => $"• {a}"));
                    SetFieldValue(document, "antecedents_medicaux", antecedentsText);
                }

                // ========== PAGE 3-4: RETARDS DÉVELOPPEMENTAUX ==========
                if (formData.RetardsDeveloppementaux != null && formData.RetardsDeveloppementaux.Count > 0)
                {
                    var retardsText = string.Join("\n", formData.RetardsDeveloppementaux.Select((r, i) => $"• {r}"));
                    SetFieldValue(document, "retards_developpementaux", retardsText);
                }

                // ========== PAGE 4: DESCRIPTION CLINIQUE ==========
                if (formData.DescriptionClinique != null && formData.DescriptionClinique.Count > 0)
                {
                    // Répartir sur les 3 champs disponibles
                    var descLines = formData.DescriptionClinique;
                    
                    // Champ 1 : Premier tiers ou premier élément
                    if (descLines.Count > 0) 
                        SetFieldValue(document, "description_clinique_1", descLines[0]);
                    
                    // Champ 2 : Deuxième tiers ou suite
                    if (descLines.Count > 1) 
                        SetFieldValue(document, "description_clinique_2", string.Join("\n", descLines.Skip(1).Take(Math.Max(1, descLines.Count / 3))));
                    
                    // Champ 3 : Reste
                    if (descLines.Count > 2)
                        SetFieldValue(document, "description_clinique_3", string.Join("\n", descLines.Skip(2)));
                }

                // ========== PAGE 5: TRAITEMENTS ==========
                if (formData.Traitements != null)
                {
                    SetFieldValue(document, "traitements_medicaments", formData.Traitements.Medicaments);
                    SetFieldValue(document, "traitements_effets_indesirables", formData.Traitements.EffetsIndesirables);
                    SetFieldValue(document, "traitements_autres_prises_en_charge", formData.Traitements.AutresPrisesEnCharge);
                }

                // ========== PAGE 5-6: RETENTISSEMENTS FONCTIONNELS ==========
                if (formData.Retentissements != null)
                {
                    SetFieldValue(document, "retentissements_mobilite", formData.Retentissements.Mobilite);
                    SetFieldValue(document, "retentissements_communication", formData.Retentissements.Communication);

                    if (formData.Retentissements.Cognition != null && formData.Retentissements.Cognition.Count > 0)
                    {
                        var cognitionText = string.Join("\n", formData.Retentissements.Cognition.Select(c => $"• {c}"));
                        SetFieldValue(document, "retentissements_cognition", cognitionText);
                    }

                    if (formData.Retentissements.ConduiteEmotionnelle != null && formData.Retentissements.ConduiteEmotionnelle.Count > 0)
                    {
                        var conduiteText = string.Join("\n", formData.Retentissements.ConduiteEmotionnelle.Select(c => $"• {c}"));
                        SetFieldValue(document, "retentissements_conduite_emotionnelle", conduiteText);
                    }

                    SetFieldValue(document, "retentissements_autonomie", formData.Retentissements.Autonomie);
                    SetFieldValue(document, "retentissements_vie_quotidienne", formData.Retentissements.VieQuotidienne); // Probablement pas de champ exact, map vers Autonomie si besoin ou laisser vide si pas de champ
                    SetFieldValue(document, "retentissements_social_scolaire", formData.Retentissements.SocialScolaire);
                }

                // ========== PAGE 7: REMARQUES COMPLÉMENTAIRES ==========
                SetFieldValue(document, "remarques_complementaires", formData.RemarquesComplementaires);

                // ========== PAGE 8: SIGNATURE ==========
                SetFieldValue(document, "date_signature", DateTime.Now.ToString("dd/MM/yyyy"));
                SetFieldValue(document, "medecin_nom", "Dr. [NOM DU MÉDECIN]"); // À personnaliser
                SetFieldValue(document, "lieu_signature", "[VILLE]"); // À personnaliser

                // Sauvegarder les modifications
                try
                {
                    document.Save(outputPath);
                    document.Close();
                    System.Diagnostics.Debug.WriteLine($"  PDF complet sauvegardé avec succès");
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

                var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);

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

        /// <summary>
        /// Diagnostic détaillé d'un champ PDF pour identifier les problèmes d'affichage
        /// </summary>
        public void DiagnoseField(PdfDocument document, string fieldName)
        {
            try
            {
                var field = document.AcroForm?.Fields[fieldName];
                if (field == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] Champ '{fieldName}' introuvable");
                    // LogToFile($"[DIAGNOSTIC] Champ '{fieldName}' introuvable");
                    return;
                }

                var diagLog = $"\n========== DIAGNOSTIC CHAMP: {fieldName} ==========";
                System.Diagnostics.Debug.WriteLine(diagLog);
                // LogToFile(diagLog);

                // Type de champ
                if (field.Elements.ContainsKey("/FT"))
                {
                    var fieldType = field.Elements.GetString("/FT");
                    System.Diagnostics.Debug.WriteLine($"  Type: {fieldType}");
                }

                // Flags du champ
                if (field.Elements.ContainsKey("/Ff"))
                {
                    var flags = field.Elements.GetInteger("/Ff");
                    System.Diagnostics.Debug.WriteLine($"  Flags (/Ff): {flags} (0x{flags:X})");
                    System.Diagnostics.Debug.WriteLine($"    - ReadOnly: {(flags & 0x1) != 0}");
                    System.Diagnostics.Debug.WriteLine($"    - Required: {(flags & 0x2) != 0}");
                    System.Diagnostics.Debug.WriteLine($"    - NoExport: {(flags & 0x4) != 0}");
                    System.Diagnostics.Debug.WriteLine($"    - Multiline: {(flags & 0x1000) != 0}");
                    System.Diagnostics.Debug.WriteLine($"    - Password: {(flags & 0x2000) != 0}");
                    System.Diagnostics.Debug.WriteLine($"    - FileSelect: {(flags & 0x100000) != 0}");
                    System.Diagnostics.Debug.WriteLine($"    - DoNotSpellCheck: {(flags & 0x400000) != 0}");
                    System.Diagnostics.Debug.WriteLine($"    - DoNotScroll: {(flags & 0x800000) != 0}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"  Flags (/Ff): AUCUN");
                }

                // Apparence par défaut (police, taille, couleur)
                if (field.Elements.ContainsKey("/DA"))
                {
                    var da = field.Elements.GetString("/DA");
                    System.Diagnostics.Debug.WriteLine($"  Apparence (/DA): {da}");
                }

                // Rectangle (position et taille)
                if (field.Elements.ContainsKey("/Rect"))
                {
                    var rect = field.Elements.GetArray("/Rect");
                    System.Diagnostics.Debug.WriteLine($"  Rectangle: {rect}");
                    if (rect != null && rect.Elements.Count >= 4)
                    {
                        var width = rect.Elements.GetReal(2) - rect.Elements.GetReal(0);
                        var height = rect.Elements.GetReal(3) - rect.Elements.GetReal(1);
                        System.Diagnostics.Debug.WriteLine($"    - Largeur: {width:F2}");
                        System.Diagnostics.Debug.WriteLine($"    - Hauteur: {height:F2}");
                    }
                }

                // Valeur actuelle
                if (field.Value != null)
                {
                    System.Diagnostics.Debug.WriteLine($"  Valeur: {field.Value.ToString()?.Substring(0, Math.Min(100, field.Value.ToString()?.Length ?? 0))}");
                }

                // Alignement
                if (field.Elements.ContainsKey("/Q"))
                {
                    var align = field.Elements.GetInteger("/Q");
                    var alignText = align switch
                    {
                        0 => "Gauche",
                        1 => "Centre",
                        2 => "Droite",
                        _ => "Inconnu"
                    };
                    System.Diagnostics.Debug.WriteLine($"  Alignement (/Q): {alignText} ({align})");
                }

                System.Diagnostics.Debug.WriteLine($"=================================================\n");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DIAGNOSTIC] Erreur: {ex.Message}");
            }
        }
        /// <summary>
        /// Active le mode "Multi-ligne" sur tous les champs texte du PDF.
        /// </summary>
        public (bool success, string? error) EnableMultilineOnAllFields(string pdfPath)
        {
            try
            {
                if (!File.Exists(pdfPath))
                    return (false, "Fichier PDF introuvable");

                var document = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Modify);

                if (document.AcroForm == null)
                {
                    document.Close();
                    return (false, "Pas de formulaire AcroForm dans le PDF");
                }

                // 🟢 FIX CRITIQUE: Forcer la régénération des apparences par le lecteur PDF
                // Edge et WebView2 en ont besoin pour recalculer le rendu du texte
                if (document.AcroForm.Elements.ContainsKey("/NeedAppearances"))
                {
                    document.AcroForm.Elements.SetBoolean("/NeedAppearances", true);
                }
                else
                {
                    document.AcroForm.Elements.Add("/NeedAppearances", new PdfBoolean(true));
                }

                int modifiedCount = 0;

                foreach (var fieldName in document.AcroForm.Fields.Names)
                {
                    var field = document.AcroForm.Fields[fieldName];
                    if (field == null) continue;

                    // Vérifier si c'est un champ texte (/FT /Tx)
                    bool isTextField = false;
                    if (field.Elements.ContainsKey("/FT"))
                    {
                        var type = field.Elements.GetString("/FT");
                        if (type == "/Tx") isTextField = true;
                    }

                    if (isTextField)
                    {
                        // 1. Ajouter le flag Multiline (Bit 13 = 4096 = 0x1000)
                        if (field.Elements.ContainsKey("/Ff"))
                        {
                            var flags = field.Elements.GetInteger("/Ff");
                            if ((flags & 0x1000) == 0) // Si pas déjà multiline
                            {
                                field.Elements.SetInteger("/Ff", flags | 0x1000);
                                modifiedCount++;
                            }
                        }
                        else
                        {
                            field.Elements.SetInteger("/Ff", 0x1000); // 4096
                            modifiedCount++;
                        }

                        // 2. 🟢 FIX: Forcer une police standard (Helvetica) et taille auto (0)
                        // Cela corrige le texte "bizarre" ou tronqué sur Edge
                        field.Elements.SetString("/DA", "/Helv 0 Tf 0 g");
                    }
                }

                if (modifiedCount > 0)
                {
                    document.Save(pdfPath);
                    System.Diagnostics.Debug.WriteLine($"[PDFFormFiller] {modifiedCount} champs passés en Multi-ligne.");
                }
                
                document.Close();
                return (true, null);
            }
            catch (Exception ex)
            {
                return (false, $"Erreur patching PDF: {ex.Message}");
            }
        }
    }
}
