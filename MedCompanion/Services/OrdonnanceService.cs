using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MedCompanion.Models;

namespace MedCompanion.Services;

public class OrdonnanceService
{
    private readonly LetterService _letterService;
    private readonly StorageService _storageService;
    private readonly PathService _pathService;
    private readonly AppSettings _settings;
    private readonly SynthesisWeightTracker _weightTracker;
    private readonly OrdonnancePDFService _pdfService;
    private readonly OrdonnanceDocxService _docxService;
    private readonly DocxToPdfService _docxToPdfService;

    public OrdonnanceService(LetterService letterService, StorageService storageService, PathService pathService)
    {
        _letterService = letterService;
        _storageService = storageService;
        _pathService = pathService;
        _settings = AppSettings.Load();
        _weightTracker = new SynthesisWeightTracker(pathService);
        _pdfService = new OrdonnancePDFService();
        _docxService = new OrdonnanceDocxService();
        _docxToPdfService = new DocxToPdfService();
    }
    
    /// <summary>
    /// Génère le contenu Markdown d'une ordonnance IDE
    /// </summary>
    public string GenerateOrdonnanceIDEMarkdown(OrdonnanceIDE ordonnance)
    {
        var sb = new StringBuilder();
        
        // Titre du document
        sb.AppendLine("# ORDONNANCE DE SOINS INFIRMIERS À DOMICILE");
        sb.AppendLine();
        
        // Informations patient
        sb.AppendLine($"**Date :** {ordonnance.DateCreation:dd/MM/yyyy}");
        sb.AppendLine();
        sb.AppendLine($"Patient : **{ordonnance.Patient}**");
        
        if (!string.IsNullOrEmpty(ordonnance.DateNaissance))
        {
            sb.AppendLine($"Né(e) le : {ordonnance.DateNaissance}");
        }
        
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        
        // Objet
        sb.AppendLine("**Objet :** Prescription de soins infirmiers à domicile – Administration de traitements et surveillance hémodynamique.");
        sb.AppendLine();
        
        // Corps principal
        sb.AppendLine("Je soussigné(e), médecin prescripteur, demande la mise en place des soins infirmiers suivants :");
        sb.AppendLine();
        
        // Soins prescrits
        sb.AppendLine(ordonnance.SoinsPrescrits);
        sb.AppendLine();
        
        // Durée
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine($"**Durée :** {ordonnance.Duree}");
        
        if (!string.IsNullOrEmpty(ordonnance.Renouvelable))
        {
            sb.AppendLine($", renouvelable {ordonnance.Renouvelable}.");
        }
        
        sb.AppendLine();
        sb.AppendLine("**Documents joints :** copie de l'ordonnance médicamenteuse en cours et coordonnées du médecin prescripteur.");
        
        return sb.ToString();
    }

    /// <summary>
    /// Génère le contenu Markdown d'une ordonnance de médicaments
    /// </summary>
    public string GenerateOrdonnanceMedicamentsMarkdown(OrdonnanceMedicaments ordonnance)
    {
        var sb = new StringBuilder();

        // Titre du document
        sb.AppendLine("# ORDONNANCE");
        sb.AppendLine();

        // Informations patient
        sb.AppendLine($"**Date :** {ordonnance.DateCreation:dd/MM/yyyy}");
        sb.AppendLine();
        sb.AppendLine($"Patient : **{ordonnance.PatientNom}**");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        // Liste des médicaments prescrits
        foreach (var medicament in ordonnance.Medicaments)
        {
            // Dénomination du médicament
            sb.AppendLine($"**{medicament.Medicament.Denomination}**");

            // Présentation si disponible
            if (medicament.Presentation != null && !string.IsNullOrEmpty(medicament.Presentation.Libelle))
            {
                sb.AppendLine($"*{medicament.Presentation.Libelle}*");
            }

            sb.AppendLine();

            // Posologie
            sb.AppendLine($"**Posologie :** {medicament.Posologie}");

            // Durée
            sb.AppendLine($"**Durée :** {medicament.Duree}");

            // Quantité
            sb.AppendLine($"**Quantité :** {medicament.Quantite} boîte(s)");

            // Renouvelable
            if (medicament.Renouvelable && medicament.NombreRenouvellements > 0)
            {
                sb.AppendLine($"**Renouvelable :** {medicament.NombreRenouvellements} fois");
            }
            else if (medicament.Renouvelable)
            {
                sb.AppendLine($"**Renouvelable :** Oui");
            }

            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        // Note additionnelle si présente
        if (!string.IsNullOrWhiteSpace(ordonnance.Notes))
        {
            sb.AppendLine("**Note :**");
            sb.AppendLine();
            sb.AppendLine(ordonnance.Notes);
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Génère le contenu Markdown d'une ordonnance de biologie
    /// </summary>
    public string GenerateOrdonnanceBiologieMarkdown(OrdonnanceBiologie ordonnance)
    {
        var sb = new StringBuilder();

        // Titre du document
        sb.AppendLine("# ORDONNANCE DE BIOLOGIE");
        sb.AppendLine();

        // Informations patient (sans date - gérée par mise en page)
        sb.AppendLine($"Patient : **{ordonnance.PatientNom} {ordonnance.PatientPrenom}**");

        if (!string.IsNullOrEmpty(ordonnance.PatientDateNaissance))
        {
            sb.AppendLine($"Né(e) le : {ordonnance.PatientDateNaissance}");
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        // Note additionnelle EN HAUT si présente (ex: "Bilan à jeun")
        if (!string.IsNullOrWhiteSpace(ordonnance.Note))
        {
            sb.AppendLine($"**{ordonnance.Note}**");
            sb.AppendLine();
        }

        // Type de bilan
        sb.AppendLine($"**Type de bilan :** {ordonnance.PresetNom}");
        sb.AppendLine();

        // Liste des examens prescrits
        sb.AppendLine("**Examens prescrits :**");
        sb.AppendLine();

        foreach (var examen in ordonnance.ExamensCoches)
        {
            sb.AppendLine($"- {examen.Nom}");
        }

        // Pas de signature, nom médecin, ni RPPS - géré par la mise en page automatique

        return sb.ToString();
    }

    /// <summary>
    /// Sauvegarde une ordonnance de biologie (Markdown + DOCX + PDF) et retourne les chemins
    /// </summary>
    public (bool success, string message, string? mdPath, string? docxPath, string? pdfPath) SaveOrdonnanceBiologie(
        string patientName,
        OrdonnanceBiologie ordonnance)
    {
        try
        {
            var patientDir = _storageService.GetPatientDirectory(patientName);
            var ordonnancesDir = Path.Combine(patientDir, "ordonnances");

            if (!Directory.Exists(ordonnancesDir))
            {
                Directory.CreateDirectory(ordonnancesDir);
            }

            // Générer nom de fichier avec timestamp
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"BIO_{timestamp}";
            var mdPath = Path.Combine(ordonnancesDir, $"{fileName}.md");

            // Générer le contenu Markdown
            var markdown = GenerateOrdonnanceBiologieMarkdown(ordonnance);

            // Sauvegarder le Markdown
            File.WriteAllText(mdPath, markdown, Encoding.UTF8);

            System.Diagnostics.Debug.WriteLine($"[SaveOrdonnanceBiologie] Markdown sauvegardé: {mdPath}");

            // Exporter en DOCX
            var (exportSuccess, exportMessage, docxPath) = _letterService.ExportToDocx(
                patientName,
                markdown,
                mdPath
            );

            System.Diagnostics.Debug.WriteLine($"[SaveOrdonnanceBiologie] Export DOCX - Success: {exportSuccess}, Message: {exportMessage}, Path: {docxPath ?? "NULL"}");

            // Générer le PDF professionnel
            string? pdfPath = null;
            try
            {
                pdfPath = Path.Combine(ordonnancesDir, $"{fileName}.pdf");

                // Récupérer les métadonnées du patient
                var patientJsonPath = Path.Combine(patientDir, "info_patient", "patient.json");
                PatientMetadata? patientMetadata = null;

                if (File.Exists(patientJsonPath))
                {
                    var patientJson = File.ReadAllText(patientJsonPath, Encoding.UTF8);
                    System.Diagnostics.Debug.WriteLine($"[SaveOrdonnanceMedicaments] JSON brut (premiers 200 chars): {patientJson.Substring(0, Math.Min(200, patientJson.Length))}");

                    patientMetadata = System.Text.Json.JsonSerializer.Deserialize<PatientMetadata>(patientJson, new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    System.Diagnostics.Debug.WriteLine($"[SaveOrdonnanceMedicaments] APRÈS désérialisation:");
                    System.Diagnostics.Debug.WriteLine($"  Nom={patientMetadata?.Nom}, Prenom={patientMetadata?.Prenom}");
                    System.Diagnostics.Debug.WriteLine($"  Dob='{patientMetadata?.Dob}', DobFormatted='{patientMetadata?.DobFormatted}', Age={patientMetadata?.Age}");
                }
                else
                {
                    // Créer un PatientMetadata minimal si le fichier n'existe pas
                    patientMetadata = new PatientMetadata
                    {
                        Nom = patientName.Split('_').FirstOrDefault() ?? "",
                        Prenom = patientName.Split('_').Skip(1).FirstOrDefault() ?? ""
                    };
                }

                if (patientMetadata != null)
                {
                    var pdfSuccess = _pdfService.GenerateOrdonnanceBiologiePDF(ordonnance, patientMetadata, pdfPath);

                    if (pdfSuccess)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveOrdonnanceBiologie] ✅ PDF créé avec succès: {pdfPath}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveOrdonnanceBiologie] ❌ Erreur génération PDF");
                        pdfPath = null;
                    }
                }
            }
            catch (Exception pdfEx)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveOrdonnanceBiologie] Erreur génération PDF: {pdfEx.Message}");
                pdfPath = null;
            }

            // Construire le message de résultat
            var formats = new List<string>();
            if (exportSuccess && !string.IsNullOrEmpty(docxPath)) formats.Add("DOCX");
            if (!string.IsNullOrEmpty(pdfPath)) formats.Add("PDF");

            var formatList = formats.Count > 0 ? string.Join(" + ", formats) : "Markdown";

            if (exportSuccess && !string.IsNullOrEmpty(docxPath))
            {
                System.Diagnostics.Debug.WriteLine($"[SaveOrdonnanceBiologie] ✅ Ordonnance créée: {formatList}");
                return (true, $"✅ Ordonnance biologie générée ({formatList})", mdPath, docxPath, pdfPath);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[SaveOrdonnanceBiologie] ❌ DOCX NON créé - Erreur: {exportMessage}");
                return (true, $"⚠️ Ordonnance sauvegardée mais erreur export DOCX: {exportMessage}", mdPath, null, pdfPath);
            }
        }
        catch (Exception ex)
        {
            return (false, $"Erreur lors de la sauvegarde: {ex.Message}", null, null, null);
        }
    }

    /// <summary>
    /// Sauvegarde une ordonnance de médicaments (Markdown + DOCX + PDF) et retourne les chemins
    /// </summary>
    /// <param name="patientName">Nom complet du patient</param>
    /// <param name="ordonnance">Ordonnance à sauvegarder</param>
    /// <param name="patientMetadata">Métadonnées du patient (optionnel, pour inclure date de naissance/âge)</param>
    /// <param name="metadata">Métadonnées additionnelles pour le système de poids</param>
    public (bool success, string message, string? mdPath, string? docxPath, string? pdfPath) SaveOrdonnanceMedicaments(
        string patientName,
        OrdonnanceMedicaments ordonnance,
        PatientMetadata? patientMetadata = null,
        Dictionary<string, object>? metadata = null)
    {
        try
        {
            var patientDir = _storageService.GetPatientDirectory(patientName);
            var ordonnancesDir = Path.Combine(patientDir, "ordonnances");

            if (!Directory.Exists(ordonnancesDir))
            {
                Directory.CreateDirectory(ordonnancesDir);
            }

            // Générer nom de fichier avec timestamp
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"MED_{timestamp}";
            var mdPath = Path.Combine(ordonnancesDir, $"{fileName}.md");

            // Générer le contenu Markdown
            var markdown = GenerateOrdonnanceMedicamentsMarkdown(ordonnance);

            // Sauvegarder le Markdown
            File.WriteAllText(mdPath, markdown, Encoding.UTF8);

            System.Diagnostics.Debug.WriteLine($"[SaveOrdonnanceMedicaments] Markdown sauvegardé: {mdPath}");

            // Utiliser le PatientMetadata passé en paramètre, sinon lire depuis le fichier JSON
            PatientMetadata? effectivePatientMetadata = patientMetadata;

            if (effectivePatientMetadata == null)
            {
                // Récupérer les métadonnées du patient depuis le fichier JSON si non passées en paramètre
                var patientJsonPath = Path.Combine(patientDir, "info_patient", "patient.json");

                if (File.Exists(patientJsonPath))
                {
                    var patientJson = File.ReadAllText(patientJsonPath, Encoding.UTF8);
                    effectivePatientMetadata = System.Text.Json.JsonSerializer.Deserialize<PatientMetadata>(patientJson, new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                else
                {
                    // Créer un PatientMetadata minimal si le fichier n'existe pas
                    effectivePatientMetadata = new PatientMetadata
                    {
                        Nom = patientName.Split('_').FirstOrDefault() ?? "",
                        Prenom = patientName.Split('_').Skip(1).FirstOrDefault() ?? ""
                    };
                }
            }

            System.Diagnostics.Debug.WriteLine($"[SaveOrdonnanceMedicaments] PatientMetadata - Nom={effectivePatientMetadata?.Nom}, Prenom={effectivePatientMetadata?.Prenom}, Dob={effectivePatientMetadata?.Dob}, Age={effectivePatientMetadata?.Age}");

            // Générer le DOCX professionnel avec images
            string? docxPath = null;
            bool exportSuccess = false;
            string exportMessage = "";

            if (effectivePatientMetadata != null)
            {
                docxPath = Path.Combine(ordonnancesDir, $"{fileName}.docx");
                exportSuccess = _docxService.GenerateOrdonnanceMedicamentsDocx(ordonnance, effectivePatientMetadata, docxPath);
                exportMessage = exportSuccess ? "DOCX créé avec succès" : "Erreur création DOCX";

                System.Diagnostics.Debug.WriteLine($"[SaveOrdonnanceMedicaments] DOCX - Success: {exportSuccess}, Path: {docxPath ?? "NULL"}");
            }

            // Convertir le DOCX en PDF
            string? pdfPath = null;
            if (exportSuccess && !string.IsNullOrEmpty(docxPath) && File.Exists(docxPath))
            {
                try
                {
                    pdfPath = Path.Combine(ordonnancesDir, $"{fileName}.pdf");
                    var pdfSuccess = _docxToPdfService.ConvertDocxToPdf(docxPath, pdfPath);

                    if (pdfSuccess)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveOrdonnanceMedicaments] ✅ PDF créé avec succès: {pdfPath}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveOrdonnanceMedicaments] ❌ Erreur conversion DOCX→PDF");
                        pdfPath = null;
                    }
                }
                catch (Exception pdfEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[SaveOrdonnanceMedicaments] Erreur conversion PDF: {pdfEx.Message}");
                    pdfPath = null;
                }
            }

            // Enregistrer le poids pour la synthèse
            var weight = ContentWeightRules.GetDefaultWeight("ordonnance", metadata) ?? 0.5;
            var justification = metadata != null && metadata.ContainsKey("is_renewal") && (bool)metadata["is_renewal"]
                ? "Renouvellement ordonnance (poids: 0.2)"
                : $"Nouvelle ordonnance (poids: {weight})";

            _weightTracker.RecordContentWeight(
                patientName,
                "ordonnance",
                mdPath,
                weight,
                justification
            );

            System.Diagnostics.Debug.WriteLine($"[SaveOrdonnanceMedicaments] Poids enregistré: {weight} - {justification}");

            // Construire le message de résultat
            var formats = new List<string>();
            if (exportSuccess && !string.IsNullOrEmpty(docxPath)) formats.Add("DOCX");
            if (!string.IsNullOrEmpty(pdfPath)) formats.Add("PDF");

            var formatList = formats.Count > 0 ? string.Join(" + ", formats) : "Markdown";

            if (exportSuccess && !string.IsNullOrEmpty(docxPath))
            {
                System.Diagnostics.Debug.WriteLine($"[SaveOrdonnanceMedicaments] ✅ Ordonnance créée: {formatList}");
                return (true, $"✅ Ordonnance médicaments générée ({formatList})", mdPath, docxPath, pdfPath);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[SaveOrdonnanceMedicaments] ❌ DOCX NON créé - Erreur: {exportMessage}");
                return (true, $"⚠️ Ordonnance sauvegardée mais erreur export DOCX: {exportMessage}", mdPath, null, pdfPath);
            }
        }
        catch (Exception ex)
        {
            return (false, $"Erreur lors de la sauvegarde: {ex.Message}", null, null, null);
        }
    }

    /// <summary>
    /// Sauvegarde une ordonnance IDE (Markdown + DOCX + PDF) et retourne les chemins
    /// </summary>
    public (bool success, string message, string? mdPath, string? docxPath, string? pdfPath) SaveOrdonnanceIDE(
        string patientName,
        OrdonnanceIDE ordonnance)
    {
        try
        {
            // CORRECTION : Utiliser StorageService.GetPatientDirectory() pour avoir le bon format
            var patientDir = _storageService.GetPatientDirectory(patientName);
            var ordonnancesDir = Path.Combine(patientDir, "ordonnances");
            
            if (!Directory.Exists(ordonnancesDir))
            {
                Directory.CreateDirectory(ordonnancesDir);
            }
            
            // Générer nom de fichier avec timestamp
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"IDE_{timestamp}";
            var mdPath = Path.Combine(ordonnancesDir, $"{fileName}.md");
            
            // Générer le contenu Markdown
            var markdown = GenerateOrdonnanceIDEMarkdown(ordonnance);
            
            // Sauvegarder le Markdown
            File.WriteAllText(mdPath, markdown, Encoding.UTF8);
            
            System.Diagnostics.Debug.WriteLine($"[SaveOrdonnanceIDE] Markdown sauvegardé: {mdPath}");
            
            // Exporter en DOCX
            var (exportSuccess, exportMessage, docxPath) = _letterService.ExportToDocx(
                patientName,
                markdown,
                mdPath
            );
            
            System.Diagnostics.Debug.WriteLine($"[SaveOrdonnanceIDE] Export DOCX - Success: {exportSuccess}, Message: {exportMessage}, Path: {docxPath ?? "NULL"}");

            // Générer le PDF professionnel
            string? pdfPath = null;
            try
            {
                pdfPath = Path.Combine(ordonnancesDir, $"{fileName}.pdf");

                // Récupérer les métadonnées du patient
                var patientJsonPath = Path.Combine(patientDir, "info_patient", "patient.json");
                PatientMetadata? patientMetadata = null;

                if (File.Exists(patientJsonPath))
                {
                    var patientJson = File.ReadAllText(patientJsonPath, Encoding.UTF8);
                    System.Diagnostics.Debug.WriteLine($"[SaveOrdonnanceMedicaments] JSON brut (premiers 200 chars): {patientJson.Substring(0, Math.Min(200, patientJson.Length))}");

                    patientMetadata = System.Text.Json.JsonSerializer.Deserialize<PatientMetadata>(patientJson, new System.Text.Json.JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    System.Diagnostics.Debug.WriteLine($"[SaveOrdonnanceMedicaments] APRÈS désérialisation:");
                    System.Diagnostics.Debug.WriteLine($"  Nom={patientMetadata?.Nom}, Prenom={patientMetadata?.Prenom}");
                    System.Diagnostics.Debug.WriteLine($"  Dob='{patientMetadata?.Dob}', DobFormatted='{patientMetadata?.DobFormatted}', Age={patientMetadata?.Age}");
                }
                else
                {
                    // Créer un PatientMetadata minimal si le fichier n'existe pas
                    patientMetadata = new PatientMetadata
                    {
                        Nom = patientName.Split('_').FirstOrDefault() ?? "",
                        Prenom = patientName.Split('_').Skip(1).FirstOrDefault() ?? ""
                    };
                }

                if (patientMetadata != null)
                {
                    var pdfSuccess = _pdfService.GenerateOrdonnanceIDEPDF(ordonnance, patientMetadata, pdfPath);

                    if (pdfSuccess)
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveOrdonnanceIDE] ✅ PDF créé avec succès: {pdfPath}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[SaveOrdonnanceIDE] ❌ Erreur génération PDF");
                        pdfPath = null;
                    }
                }
            }
            catch (Exception pdfEx)
            {
                System.Diagnostics.Debug.WriteLine($"[SaveOrdonnanceIDE] Erreur génération PDF: {pdfEx.Message}");
                pdfPath = null;
            }

            // Construire le message de résultat
            var formats = new List<string>();
            if (exportSuccess && !string.IsNullOrEmpty(docxPath)) formats.Add("DOCX");
            if (!string.IsNullOrEmpty(pdfPath)) formats.Add("PDF");

            var formatList = formats.Count > 0 ? string.Join(" + ", formats) : "Markdown";

            if (exportSuccess && !string.IsNullOrEmpty(docxPath))
            {
                System.Diagnostics.Debug.WriteLine($"[SaveOrdonnanceIDE] ✅ Ordonnance créée: {formatList}");
                return (true, $"✅ Ordonnance IDE générée ({formatList})", mdPath, docxPath, pdfPath);
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[SaveOrdonnanceIDE] ❌ DOCX NON créé - Erreur: {exportMessage}");
                return (true, $"⚠️ Ordonnance sauvegardée mais erreur export DOCX: {exportMessage}", mdPath, null, pdfPath);
            }
        }
        catch (Exception ex)
        {
            return (false, $"Erreur lors de la sauvegarde: {ex.Message}", null, null, null);
        }
    }
    
    /// <summary>
    /// Récupère toutes les ordonnances IDE d'un patient
    /// </summary>
    public List<(DateTime date, string type, string preview, string mdPath, string? docxPath)> GetOrdonnances(string patientName)
    {
        var result = new List<(DateTime, string, string, string, string?)>();
        
        try
        {
            var ordonnanceFolders = _pathService.GetAllYearDirectories(patientName, "ordonnances");
            
            foreach (var ordonnancesDir in ordonnanceFolders)
            {
                // Récupérer tous les fichiers .md
                var mdFiles = Directory.GetFiles(ordonnancesDir, "*.md", SearchOption.TopDirectoryOnly);
            
            foreach (var mdFile in mdFiles)
            {
                var fileName = Path.GetFileName(mdFile);
                var fileInfo = new FileInfo(mdFile);

                // Détecter le type
                string type;
                if (fileName.StartsWith("IDE_", StringComparison.OrdinalIgnoreCase))
                    type = "IDE";
                else if (fileName.StartsWith("BIO_", StringComparison.OrdinalIgnoreCase))
                    type = "Biologie";
                else if (fileName.StartsWith("MED_", StringComparison.OrdinalIgnoreCase))
                    type = "Médicaments";
                else
                {
                    // Pour les anciennes ordonnances sans préfixe, détecter le type par le contenu
                    try
                    {
                        var content = File.ReadAllText(mdFile);
                        if (content.Contains("ORDONNANCE DE SOINS INFIRMIERS"))
                            type = "IDE";
                        else if (content.Contains("ORDONNANCE DE BIOLOGIE"))
                            type = "Biologie";
                        else if (content.Contains("# ORDONNANCE") && (content.Contains("Posologie") || content.Contains("**Durée")))
                            type = "Médicaments";
                        else
                            type = "Autre";
                    }
                    catch
                    {
                        type = "Autre";
                    }
                }

                // Lire le preview avec extraction intelligente du contenu
                string preview = "Ordonnance";
                try
                {
                    var content = File.ReadAllText(mdFile);
                    var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

                    // Chercher des informations significatives
                    string mainTitle = "";
                    string soinsPrescrits = "";
                    string typeBilan = "";
                    var examens = new List<string>();
                    var medicaments = new List<string>();

                    for (int i = 0; i < lines.Length; i++)
                    {
                        var line = lines[i].Trim();

                        // Titre principal (première ligne avec #)
                        if (string.IsNullOrEmpty(mainTitle) && line.StartsWith("# "))
                        {
                            mainTitle = line.Replace("#", "").Trim();
                        }

                        // BIOLOGIE : Chercher le type de bilan
                        if (line.StartsWith("**Type de bilan :**") || line.StartsWith("**Type de bilan:**"))
                        {
                            typeBilan = line.Replace("**Type de bilan :**", "").Replace("**Type de bilan:**", "").Trim();
                        }

                        // BIOLOGIE : Chercher les examens prescrits (après "**Examens prescrits :**")
                        if (line.Contains("Examens prescrits") && i + 2 < lines.Length)
                        {
                            // Lire les 3 premiers examens (lignes commençant par "- ")
                            for (int j = i + 1; j < Math.Min(i + 6, lines.Length); j++)
                            {
                                var nextLine = lines[j].Trim();
                                if (nextLine.StartsWith("- "))
                                {
                                    var examen = nextLine.Substring(2).Trim(); // Retirer "- "
                                    examens.Add(examen);
                                    if (examens.Count >= 3) break; // Limiter à 3 examens
                                }
                            }
                        }

                        // IDE : Chercher les soins prescrits (après "Je soussigné" ou liste de soins)
                        if (line.Contains("soins infirmiers suivants") && i + 2 < lines.Length)
                        {
                            // Prendre les 2-3 lignes suivantes pour avoir un aperçu
                            for (int j = i + 2; j < Math.Min(i + 5, lines.Length); j++)
                            {
                                var nextLine = lines[j].Trim();
                                if (!string.IsNullOrEmpty(nextLine) &&
                                    !nextLine.StartsWith("**") &&
                                    !nextLine.StartsWith("---") &&
                                    !nextLine.StartsWith("#"))
                                {
                                    soinsPrescrits = nextLine;
                                    break;
                                }
                            }
                        }

                        // MÉDICAMENTS : Chercher les noms de médicaments (lignes en gras après le titre)
                        if (line.StartsWith("**") &&
                            !line.Contains("Date :") &&
                            !line.Contains("Posologie") &&
                            !line.Contains("Durée") &&
                            !line.Contains("Quantité") &&
                            !line.Contains("Renouvelable") &&
                            !line.Contains("Note") &&
                            !line.Contains("---"))
                        {
                            var medicament = line.Replace("**", "").Trim();
                            if (!string.IsNullOrEmpty(medicament) && medicament != "ORDONNANCE" && !medicament.StartsWith("Patient"))
                            {
                                medicaments.Add(medicament);
                                if (medicaments.Count >= 3) break; // Limiter à 3 médicaments
                            }
                        }
                    }

                    // Construire le preview selon le type
                    if (type == "Biologie")
                    {
                        // Pour la biologie : afficher type de bilan + examens
                        if (!string.IsNullOrEmpty(typeBilan))
                        {
                            preview = typeBilan;
                            if (examens.Count > 0)
                            {
                                preview += " : " + string.Join(", ", examens.Take(3));
                                if (examens.Count > 3)
                                {
                                    preview += "...";
                                }
                            }
                        }
                        else if (examens.Count > 0)
                        {
                            preview = string.Join(", ", examens.Take(3));
                            if (examens.Count > 3)
                            {
                                preview += "...";
                            }
                        }
                    }
                    else if (type == "IDE")
                    {
                        // Pour IDE : afficher les soins prescrits
                        if (!string.IsNullOrEmpty(soinsPrescrits))
                        {
                            preview = soinsPrescrits;
                            if (preview.Length > 80)
                            {
                                preview = preview.Substring(0, 77) + "...";
                            }
                        }
                    }
                    else if (type == "Médicaments")
                    {
                        // Pour Médicaments : afficher la liste des médicaments
                        if (medicaments.Count > 0)
                        {
                            preview = string.Join(", ", medicaments.Take(3));
                            if (medicaments.Count > 3)
                            {
                                preview += "...";
                            }
                        }
                    }

                    // Fallback si rien trouvé
                    if (preview == "Ordonnance" && !string.IsNullOrEmpty(mainTitle))
                    {
                        preview = mainTitle;
                    }
                }
                catch { }

                // Chercher le DOCX correspondant
                var docxPath = Path.ChangeExtension(mdFile, ".docx");
                if (!File.Exists(docxPath))
                {
                    docxPath = null;
                }

                result.Add((fileInfo.LastWriteTime, type, preview, mdFile, docxPath));
            }
            
            // NOUVEAU : Chercher les .docx orphelins (sans .md correspondant)
            var docxFiles = Directory.GetFiles(ordonnancesDir, "*.docx", SearchOption.TopDirectoryOnly);
            
            foreach (var docxFile in docxFiles)
            {
                var mdFile = Path.ChangeExtension(docxFile, ".md");

                // Si le .md n'existe pas, c'est un orphelin
                if (!File.Exists(mdFile))
                {
                    var fileName = Path.GetFileName(docxFile);
                    var fileInfo = new FileInfo(docxFile);

                    // Détecter le type
                    string type;
                    if (fileName.StartsWith("IDE_", StringComparison.OrdinalIgnoreCase))
                        type = "IDE";
                    else if (fileName.StartsWith("BIO_", StringComparison.OrdinalIgnoreCase))
                        type = "Biologie";
                    else if (fileName.StartsWith("MED_", StringComparison.OrdinalIgnoreCase))
                        type = "Médicaments";
                    else
                    {
                        // Pour les anciennes ordonnances DOCX sans préfixe, essayer de détecter par nom de fichier
                        // (impossible de lire le contenu d'un DOCX facilement sans bibliothèque)
                        // Par défaut, on considère que c'est une ordonnance de médicaments
                        type = "Médicaments";  // Hypothèse: la plupart des ordonnances sont des médicaments
                    }

                    string preview = "Ordonnance (DOCX uniquement - ancien format)";

                    // Utiliser le chemin du .docx comme "mdPath" pour la suppression
                    result.Add((fileInfo.LastWriteTime, type, preview, docxFile, docxFile));
                }
            }
            
            // Trier par date décroissante
            result = result.OrderByDescending(r => r.Item1).ToList();
        }
    }
    catch (Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"[OrdonnanceService] Erreur GetOrdonnances: {ex.Message}");
    }
    
    return result;
}
    
    /// <summary>
    /// Supprime une ordonnance (MD + DOCX + PDF, ou juste DOCX orphelin)
    /// </summary>
    public (bool success, string message) DeleteOrdonnance(string filePath)
    {
        try
        {
            // Déterminer si c'est un .md ou un .docx orphelin
            var extension = Path.GetExtension(filePath).ToLower();
            
            if (extension == ".md")
            {
                // Cas normal : supprimer .md + .docx + .pdf
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                
                var docxPath = Path.ChangeExtension(filePath, ".docx");
                if (File.Exists(docxPath))
                {
                    File.Delete(docxPath);
                }
                
                // Supprimer également le PDF
                var pdfPath = Path.ChangeExtension(filePath, ".pdf");
                if (File.Exists(pdfPath))
                {
                    File.Delete(pdfPath);
                    System.Diagnostics.Debug.WriteLine($"[DeleteOrdonnance] PDF supprimé: {pdfPath}");
                }
            }
            else if (extension == ".docx")
            {
                // Cas orphelin : supprimer uniquement le .docx + .pdf si existe
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }
                
                // Supprimer également le PDF correspondant s'il existe
                var pdfPath = Path.ChangeExtension(filePath, ".pdf");
                if (File.Exists(pdfPath))
                {
                    File.Delete(pdfPath);
                    System.Diagnostics.Debug.WriteLine($"[DeleteOrdonnance] PDF orphelin supprimé: {pdfPath}");
                }
            }
            
            return (true, "✅ Ordonnance supprimée");
        }
        catch (Exception ex)
        {
            return (false, $"Erreur lors de la suppression: {ex.Message}");
        }
    }

    /// <summary>
    /// Parse le contenu Markdown d'une ordonnance pour extraire la liste des médicaments
    /// </summary>
    public List<MedicamentPrescrit> ParseMedicamentsFromMarkdown(string markdownContent)
    {
        var medicaments = new List<MedicamentPrescrit>();

        try
        {
            var lines = markdownContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            MedicamentPrescrit? currentMedicament = null;
            string? currentNom = null;
            string? currentPresentation = null;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();

                // Détection du nom du médicament (ligne en gras: **...**)
                if (trimmedLine.StartsWith("**") && trimmedLine.EndsWith("**") && !trimmedLine.Contains(":"))
                {
                    // Sauvegarder le médicament précédent si existe
                    if (currentMedicament != null && currentNom != null)
                    {
                        medicaments.Add(currentMedicament);
                    }

                    // Commencer un nouveau médicament
                    currentNom = trimmedLine.Trim('*');
                    currentPresentation = null;
                    currentMedicament = new MedicamentPrescrit
                    {
                        Medicament = new Medicament
                        {
                            Denomination = currentNom
                        }
                    };
                }
                // Détection de la présentation (ligne en italique: *...*)
                else if (trimmedLine.StartsWith("*") && trimmedLine.EndsWith("*") && currentMedicament != null)
                {
                    currentPresentation = trimmedLine.Trim('*');
                    currentMedicament.Presentation = new MedicamentPresentation
                    {
                        Libelle = currentPresentation
                    };
                }
                // Extraction des propriétés
                else if (trimmedLine.Contains(":") && currentMedicament != null)
                {
                    var parts = trimmedLine.Split(new[] { ':' }, 2);
                    if (parts.Length == 2)
                    {
                        var key = parts[0].Trim().Trim('*').ToLower();
                        var value = parts[1].Trim().TrimStart('*').Trim(); // Enlever les astérisques en début de valeur

                        switch (key)
                        {
                            case "posologie":
                                currentMedicament.Posologie = value;
                                break;

                            case "durée":
                            case "duree":
                                currentMedicament.Duree = value;
                                break;

                            case "quantité":
                            case "quantite":
                                // Extraire le nombre (ex: "2 boîte(s)" -> 2)
                                var qtyParts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                if (qtyParts.Length > 0 && int.TryParse(qtyParts[0], out int qty))
                                {
                                    currentMedicament.Quantite = qty;
                                }
                                break;

                            case "renouvelable":
                                currentMedicament.Renouvelable = true;

                                // Extraire le nombre de renouvellements si spécifié
                                if (value.Contains("fois"))
                                {
                                    var renouvParts = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                                    if (renouvParts.Length > 0 && int.TryParse(renouvParts[0], out int renouv))
                                    {
                                        currentMedicament.NombreRenouvellements = renouv;
                                    }
                                }
                                break;
                        }
                    }
                }
            }

            // Ajouter le dernier médicament
            if (currentMedicament != null && currentNom != null)
            {
                medicaments.Add(currentMedicament);
            }

            System.Diagnostics.Debug.WriteLine($"[OrdonnanceService] Parsed {medicaments.Count} médicaments from markdown");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OrdonnanceService] Erreur parsing: {ex.Message}");
        }

        return medicaments;
    }

    /// <summary>
    /// Récupère la dernière ordonnance de médicaments pour un patient
    /// </summary>
    public (bool found, List<MedicamentPrescrit> medicaments, string error) GetLastOrdonnanceMedicaments(string patientName)
    {
        try
        {
            // Récupérer toutes les ordonnances du patient
            var ordonnances = GetOrdonnances(patientName);

            // Filtrer pour ne garder que les ordonnances de médicaments avec fichier .md
            // (exclure les anciennes ordonnances DOCX orphelines qui ne peuvent pas être parsées)
            var medicamentsOrdonnances = ordonnances
                .Where(o => o.type == "Médicaments" && o.mdPath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (medicamentsOrdonnances.Count == 0)
            {
                return (false, new List<MedicamentPrescrit>(),
                    "Aucune ordonnance de médicaments trouvée.\n\n" +
                    "💡 Pour utiliser la fonction de renouvellement, créez d'abord une nouvelle ordonnance avec le système actuel.");
            }

            // Prendre la première (déjà triée par date DESC dans GetOrdonnances)
            var lastOrdonnance = medicamentsOrdonnances[0];

            // Lire le contenu du fichier markdown
            if (!File.Exists(lastOrdonnance.mdPath))
            {
                return (false, new List<MedicamentPrescrit>(), "Fichier d'ordonnance introuvable");
            }

            var markdownContent = File.ReadAllText(lastOrdonnance.mdPath, Encoding.UTF8);

            // Parser les médicaments
            var medicaments = ParseMedicamentsFromMarkdown(markdownContent);

            if (medicaments.Count == 0)
            {
                return (false, new List<MedicamentPrescrit>(), "Aucun médicament trouvé dans l'ordonnance");
            }

            System.Diagnostics.Debug.WriteLine(
                $"[OrdonnanceService] Dernière ordonnance trouvée: {medicaments.Count} médicaments, date: {lastOrdonnance.date:dd/MM/yyyy}");

        return (true, medicaments, string.Empty);
    }
    catch (Exception ex)
    {
        return (false, new List<MedicamentPrescrit>(), $"Erreur: {ex.Message}");
    }
}

    /// <summary>
    /// Convertit un fichier Markdown existant en DOCX et PDF
    /// </summary>
    public (bool success, string message, string? docxPath, string? pdfPath) ConvertMarkdownToDocxAndPdf(string patientName, string mdPath)
    {
        try
        {
            if (!File.Exists(mdPath))
            {
                return (false, "Fichier Markdown introuvable", null, null);
            }

            var markdown = File.ReadAllText(mdPath, Encoding.UTF8);
            
            // Exporter en DOCX via LetterService
            var (exportSuccess, exportMessage, docxPath) = _letterService.ExportToDocx(
                patientName,
                markdown,
                mdPath
            );

            if (!exportSuccess || string.IsNullOrEmpty(docxPath))
            {
                return (false, $"Erreur création DOCX: {exportMessage}", null, null);
            }

            // Convertir DOCX en PDF via DocxToPdfService
            string? pdfPath = Path.ChangeExtension(docxPath, ".pdf");
            bool pdfSuccess = _docxToPdfService.ConvertDocxToPdf(docxPath, pdfPath);

            string message = pdfSuccess 
                ? "Documents générés avec succès (DOCX + PDF)" 
                : "DOCX généré avec succès, mais échec PDF";

            return (true, message, docxPath, pdfSuccess ? pdfPath : null);
        }
        catch (Exception ex)
        {
            return (false, $"Erreur conversion: {ex.Message}", null, null);
        }
    }
}
