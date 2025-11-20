using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MedCompanion.Models;
using UglyToad.PdfPig;

namespace MedCompanion.Services
{
    /// <summary>
    /// Service de gestion des documents patients
    /// </summary>
    public class DocumentService
    {
        private readonly OpenAIService _aiService;
        private readonly PathService _pathService;
        private const string IndexFileName = "documents-index.json";
        
        public DocumentService(OpenAIService aiService, PathService pathService)
        {
            _aiService = aiService;
            _pathService = pathService;
        }
        
        /// <summary>
        /// Crée la structure de dossiers documents pour un patient
        /// </summary>
        public void EnsureDocumentStructure(string nomComplet)
        {
            var documentsPath = _pathService.GetDocumentsDirectory(nomComplet);
            
            if (!Directory.Exists(documentsPath))
            {
                Directory.CreateDirectory(documentsPath);
            }
            
            // Créer les sous-dossiers pour chaque catégorie
            foreach (var category in DocumentCategories.All)
            {
                var categoryPath = Path.Combine(documentsPath, category);
                if (!Directory.Exists(categoryPath))
                {
                    Directory.CreateDirectory(categoryPath);
                }
            }
        }
        
        /// <summary>
        /// Importe un document et l'analyse avec l'IA
        /// </summary>
        public async Task<(bool success, PatientDocument? document, string message)> ImportDocumentAsync(
            string sourceFilePath, 
            string nomComplet)
        {
            try
            {
                // Vérifier que le fichier existe
                if (!File.Exists(sourceFilePath))
                {
                    return (false, null, "Le fichier source n'existe pas.");
                }
                
                var fileInfo = new FileInfo(sourceFilePath);
                var extension = fileInfo.Extension.ToLowerInvariant();
                
                // Vérifier les extensions supportées
                var supportedExtensions = new[] { ".pdf", ".docx", ".doc", ".jpg", ".jpeg", ".png", ".txt" };
                if (!supportedExtensions.Contains(extension))
                {
                    return (false, null, $"Type de fichier non supporté: {extension}");
                }
                
                // Assurer la structure de dossiers
                EnsureDocumentStructure(nomComplet);
                
                // Extraire le texte du document
                string extractedText = await ExtractTextFromFileAsync(sourceFilePath);
                
                if (string.IsNullOrWhiteSpace(extractedText))
                {
                    return (false, null, "Impossible d'extraire le texte du document.");
                }
                
                // Analyser avec l'IA pour catégorisation et nommage
                var analysis = await AnalyzeDocumentWithAIAsync(extractedText, fileInfo.Name);
                
                // Créer l'objet document
                var document = new PatientDocument
                {
                    FileName = analysis.suggestedName + extension,
                    Category = analysis.category,
                    Summary = analysis.summary,
                    ExtractedText = extractedText,
                    FileSizeBytes = fileInfo.Length,
                    FileExtension = extension,
                    DateAdded = DateTime.Now
                };
                
                // Copier le fichier dans le bon dossier
                var documentsPath = _pathService.GetDocumentsDirectory(nomComplet);
                var targetFolder = Path.Combine(documentsPath, document.Category);
                var targetPath = Path.Combine(targetFolder, document.FileName);
                
                // Gérer les doublons de nom
                int counter = 1;
                string baseFileName = Path.GetFileNameWithoutExtension(document.FileName);
                while (File.Exists(targetPath))
                {
                    document.FileName = $"{baseFileName}_{counter}{extension}";
                    targetPath = Path.Combine(targetFolder, document.FileName);
                    counter++;
                }
                
                // Copier le fichier
                File.Copy(sourceFilePath, targetPath, false);
                document.FilePath = targetPath;
                
                // Sauvegarder dans l'index
                await SaveDocumentToIndexAsync(nomComplet, document);
                
                return (true, document, "Document importé avec succès.");
            }
            catch (Exception ex)
            {
                return (false, null, $"Erreur lors de l'import: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Extrait le texte d'un fichier (OCR si nécessaire)
        /// </summary>
        private async Task<string> ExtractTextFromFileAsync(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            
            try
            {
                switch (extension)
                {
                    case ".txt":
                        return await File.ReadAllTextAsync(filePath);
                    
                    case ".docx":
                        return await ExtractTextFromWordAsync(filePath);
                    
                    case ".pdf":
                        return await ExtractTextFromPdfAsync(filePath);
                    
                    case ".jpg":
                    case ".jpeg":
                    case ".png":
                        // TODO: Implémenter OCR (nécessite Tesseract ou Azure Vision)
                        return "[OCR à implémenter]";
                    
                    default:
                        return string.Empty;
                }
            }
            catch
            {
                return string.Empty;
            }
        }
        
        /// <summary>
        /// Extrait le texte d'un fichier Word (.docx)
        /// </summary>
        private async Task<string> ExtractTextFromWordAsync(string filePath)
        {
            try
            {
                // TODO: Implémenter extraction Word (nécessite DocumentFormat.OpenXml)
                // Pour l'instant, retourne un placeholder
                await Task.Delay(100);
                return "[Extraction Word à implémenter - Installer DocumentFormat.OpenXml]";
            }
            catch
            {
                return string.Empty;
            }
        }
        
        /// <summary>
        /// Extrait le texte d'un fichier PDF avec PdfPig
        /// </summary>
        private async Task<string> ExtractTextFromPdfAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    using (var document = PdfDocument.Open(filePath))
                    {
                        var textBuilder = new StringBuilder();
                        
                        foreach (var page in document.GetPages())
                        {
                            var pageText = page.Text;
                            textBuilder.AppendLine(pageText);
                            textBuilder.AppendLine(); // Séparer les pages
                        }
                        
                        return textBuilder.ToString();
                    }
                }
                catch (Exception ex)
                {
                    // En cas d'erreur, retourner un message descriptif
                    return $"[Erreur extraction PDF: {ex.Message}]";
                }
            });
        }
        
        /// <summary>
        /// Analyse un document avec l'IA pour déterminer catégorie, nom et synthèse
        /// </summary>
        private async Task<(string category, string suggestedName, string summary)> AnalyzeDocumentWithAIAsync(
            string documentText, 
            string originalFileName)
        {
            try
            {
                var prompt = $@"Analyse ce document médical et fournis:
1. CATÉGORIE (un seul mot parmi: bilans, courriers, ordonnances, attestations, radiologies, analyses, autres)
2. NOM_FICHIER (format: AAAA-MM-JJ_type-description sans extension)
3. SYNTHESE (résumé en 2-3 phrases du contenu)

Document:
{documentText.Substring(0, Math.Min(documentText.Length, 2000))}

Réponds uniquement au format:
CATÉGORIE: [catégorie]
NOM: [nom_fichier]
SYNTHESE: [synthèse]";

                var (success, response) = await _aiService.ChatAvecContexteAsync("", prompt);
                
                if (!success)
                {
                    return (DocumentCategories.Autres, 
                            DateTime.Now.ToString("yyyy-MM-dd") + "_" + Path.GetFileNameWithoutExtension(originalFileName), 
                            "Document médical");
                }
                
                // Parser la réponse
                var lines = response.Split('\n');
                string category = DocumentCategories.Autres;
                string suggestedName = DateTime.Now.ToString("yyyy-MM-dd") + "_document";
                string summary = "Document médical";
                
                foreach (var line in lines)
                {
                    if (line.StartsWith("CATÉGORIE:", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("CATEGORIE:", StringComparison.OrdinalIgnoreCase))
                    {
                        var cat = line.Split(':', 2)[1].Trim().ToLowerInvariant();
                        if (DocumentCategories.All.Contains(cat))
                            category = cat;
                    }
                    else if (line.StartsWith("NOM:", StringComparison.OrdinalIgnoreCase))
                    {
                        suggestedName = line.Split(':', 2)[1].Trim();
                    }
                    else if (line.StartsWith("SYNTHESE:", StringComparison.OrdinalIgnoreCase) ||
                             line.StartsWith("SYNTHÈSE:", StringComparison.OrdinalIgnoreCase))
                    {
                        summary = line.Split(':', 2)[1].Trim();
                    }
                }
                
                return (category, suggestedName, summary);
            }
            catch
            {
                // Fallback en cas d'erreur IA
                return (DocumentCategories.Autres, 
                        DateTime.Now.ToString("yyyy-MM-dd") + "_" + Path.GetFileNameWithoutExtension(originalFileName), 
                        "Document médical");
            }
        }
        
        /// <summary>
        /// Sauvegarde un document dans l'index JSON
        /// </summary>
        private async Task SaveDocumentToIndexAsync(string nomComplet, PatientDocument document)
        {
            var documentsPath = _pathService.GetDocumentsDirectory(nomComplet);
            var indexPath = Path.Combine(documentsPath, IndexFileName);
            
            List<PatientDocument> documents;
            if (File.Exists(indexPath))
            {
                var json = await File.ReadAllTextAsync(indexPath);
                documents = JsonSerializer.Deserialize<List<PatientDocument>>(json) ?? new List<PatientDocument>();
            }
            else
            {
                documents = new List<PatientDocument>();
            }
            
            documents.Add(document);
            
            var options = new JsonSerializerOptions { WriteIndented = true };
            var updatedJson = JsonSerializer.Serialize(documents, options);
            await File.WriteAllTextAsync(indexPath, updatedJson);
        }
        
        /// <summary>
        /// Récupère tous les documents d'un patient
        /// </summary>
        public async Task<List<PatientDocument>> GetAllDocumentsAsync(string nomComplet)
        {
            var documentsPath = _pathService.GetDocumentsDirectory(nomComplet);
            var indexPath = Path.Combine(documentsPath, IndexFileName);
            
            if (!File.Exists(indexPath))
                return new List<PatientDocument>();
            
            try
            {
                var json = await File.ReadAllTextAsync(indexPath);
                return JsonSerializer.Deserialize<List<PatientDocument>>(json) ?? new List<PatientDocument>();
            }
            catch
            {
                return new List<PatientDocument>();
            }
        }
        
        /// <summary>
        /// Génère une synthèse globale de tous les documents d'un patient
        /// </summary>
        public async Task<string> GenerateGlobalSynthesisAsync(string nomComplet)
        {
            var documents = await GetAllDocumentsAsync(nomComplet);
            
            if (!documents.Any())
                return "Aucun document disponible pour ce patient.";
            
            try
            {
                // Construire un résumé de tous les documents
                var documentsSummary = string.Join("\n\n", documents.Select(d => 
                    $"[{DocumentCategories.GetDisplayName(d.Category)}] {d.FileName} ({d.DateAddedDisplay})\n{d.Summary}"));
                
                var prompt = $@"Tu es un assistant médical. Génère une synthèse globale structurée de tous ces documents patients.

Documents:
{documentsSummary}

Crée une synthèse en Markdown avec:
# Synthèse Documentaire Globale

## Par Catégorie
[Regrouper par catégorie avec points clés]

## Chronologie
[Événements importants par ordre chronologique]

## Points d'Attention
[Ce qui nécessite un suivi]";

                var (success, synthesis) = await _aiService.ChatAvecContexteAsync("", prompt);
                
                if (!success)
                {
                    return $"Erreur lors de la génération de la synthèse: {synthesis}";
                }
                
                // Sauvegarder la synthèse
                var documentsPath = _pathService.GetDocumentsDirectory(nomComplet);
                var synthesisPath = Path.Combine(documentsPath, "synthese-globale.md");
                await File.WriteAllTextAsync(synthesisPath, synthesis);
                
                return synthesis;
            }
            catch (Exception ex)
            {
                return $"Erreur lors de la génération de la synthèse: {ex.Message}";
            }
        }
        
        /// <summary>
        /// Génère une synthèse d'un seul document spécifique
        /// </summary>
        public async Task<(string synthesis, double relevanceWeight)> GenerateSingleDocumentSynthesisAsync(PatientDocument document)
        {
            if (document == null)
                return ("Aucun document fourni.", 0.0);

            try
            {
                var basePrompt = $@"Tu es un assistant médical. Analyse ce document patient et génère une synthèse détaillée en Markdown.

Document: {document.FileName}
Catégorie: {DocumentCategories.GetDisplayName(document.Category)}
Date: {document.DateAddedDisplay}

Contenu extrait:
{document.ExtractedText}

Crée une synthèse structurée avec:
# Synthèse du Document: {document.FileName}

## Informations Générales
- Type: {DocumentCategories.GetDisplayName(document.Category)}
- Date: {document.DateAddedDisplay}

## Résumé
[Résumé détaillé du contenu]

## Points Clés
[Points importants à retenir]

## Recommandations
[Si applicable, recommandations ou suivi nécessaire]";

                // NOUVEAU : Ajouter évaluation du poids de pertinence
                var prompt = basePrompt + @"

---
ÉVALUATION IMPORTANCE (pour mise à jour synthèse patient) :

Évalue l'importance de ce document pour mettre à jour la synthèse patient (0.0 à 1.0) :

**Poids 0.9-1.0** : Bilan psychologique complet, compte-rendu hospitalisation, diagnostic majeur
**Poids 0.7-0.8** : Bilan médical important (MDPH, orthophoniste), courrier médecin spécialisé
**Poids 0.4-0.6** : Courrier école, compte-rendu consultation externe
**Poids 0.1-0.3** : Document administratif avec infos cliniques mineures

À la fin de ta synthèse, ajoute une ligne :
POIDS_SYNTHESE: X.X
";

                var (success, synthesis) = await _aiService.ChatAvecContexteAsync("", prompt);

                if (!success)
                {
                    return ($"Erreur lors de la génération de la synthèse:\n\n{synthesis}", 0.0);
                }

                // Extraire le poids de la réponse
                double weight = ExtractWeightFromResponse(synthesis);

                // Retirer la ligne POIDS_SYNTHESE
                string cleanedSynthesis = RemoveWeightLine(synthesis);

                return (cleanedSynthesis, weight);
            }
            catch (Exception ex)
            {
                return ($"Erreur lors de la génération de la synthèse:\n\n{ex.Message}", 0.0);
            }
        }

        /// <summary>
        /// Extrait le poids de pertinence depuis la réponse de l'IA
        /// </summary>
        private double ExtractWeightFromResponse(string response)
        {
            try
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    response,
                    @"POIDS_SYNTHESE:\s*(\d+\.?\d*)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                );

                if (match.Success && double.TryParse(match.Groups[1].Value.Replace(',', '.'),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out double weight))
                {
                    return Math.Clamp(weight, 0.0, 1.0);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DocumentService] Erreur extraction poids: {ex.Message}");
            }

            return 0.5; // Valeur par défaut
        }

        /// <summary>
        /// Retire la ligne POIDS_SYNTHESE du texte
        /// </summary>
        private string RemoveWeightLine(string text)
        {
            try
            {
                return System.Text.RegularExpressions.Regex.Replace(
                    text,
                    @"POIDS_SYNTHESE:\s*\d+\.?\d*\s*",
                    "",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase
                ).Trim();
            }
            catch
            {
                return text;
            }
        }
        
        /// <summary>
        /// Vérifie si une synthèse existe déjà pour un document donné
        /// </summary>
        public (bool exists, string? synthesisPath) GetExistingSynthesis(PatientDocument document, string nomComplet)
        {
            try
            {
                // CORRECTION: Utiliser PathService et renommer en syntheses_documents
                var documentsPath = _pathService.GetDocumentsDirectory(nomComplet);
                var synthesisFolder = Path.Combine(documentsPath, "syntheses_documents");
                
                if (!Directory.Exists(synthesisFolder))
                    return (false, null);
                
                // Chercher un fichier qui commence par le nom du document (sans extension) + "_synthese_"
                var documentNameWithoutExt = Path.GetFileNameWithoutExtension(document.FileName);
                var pattern = $"{documentNameWithoutExt}_synthese_*.md";
                
                var matchingFiles = Directory.GetFiles(synthesisFolder, pattern);
                
                if (matchingFiles.Length > 0)
                {
                    // Retourner le fichier le plus récent (le dernier créé)
                    var mostRecent = matchingFiles.OrderByDescending(f => File.GetCreationTime(f)).First();
                    return (true, mostRecent);
                }
                
                return (false, null);
            }
            catch
            {
                return (false, null);
            }
        }
        
        /// <summary>
        /// Charge le contenu d'une synthèse existante
        /// </summary>
        public async Task<string> LoadSynthesisContentAsync(string synthesisPath)
        {
            try
            {
                if (!File.Exists(synthesisPath))
                    return string.Empty;
                
                var content = await File.ReadAllTextAsync(synthesisPath, Encoding.UTF8);
                
                // Supprimer le YAML front matter si présent
                if (content.TrimStart().StartsWith("---"))
                {
                    var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                    int yamlEndIndex = 0;
                    bool inYaml = false;
                    
                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (i == 0 && lines[i].Trim() == "---")
                        {
                            inYaml = true;
                            continue;
                        }
                        if (inYaml && lines[i].Trim() == "---")
                        {
                            yamlEndIndex = i + 1;
                            break;
                        }
                    }
                    
                    if (yamlEndIndex > 0 && yamlEndIndex < lines.Length)
                    {
                        content = string.Join("\n", lines.Skip(yamlEndIndex)).TrimStart();
                    }
                }
                
                return content;
            }
            catch
            {
                return string.Empty;
            }
        }
        
        /// <summary>
        /// Supprime la synthèse d'un document
        /// </summary>
        public (bool success, string message) DeleteSynthesis(string synthesisPath)
        {
            try
            {
                if (!File.Exists(synthesisPath))
                    return (false, "Le fichier de synthèse n'existe pas.");
                
                File.Delete(synthesisPath);
                return (true, "Synthèse supprimée avec succès.");
            }
            catch (Exception ex)
            {
                return (false, $"Erreur lors de la suppression: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Supprime un document (fichier physique + entrée dans l'index + synthèse associée)
        /// </summary>
        public async Task<(bool success, string message)> DeleteDocumentAsync(PatientDocument document, string nomComplet)
        {
            try
            {
                // 1. Vérifier et supprimer la synthèse associée si elle existe
                var (synthesisExists, synthesisPath) = GetExistingSynthesis(document, nomComplet);
                if (synthesisExists && !string.IsNullOrEmpty(synthesisPath) && File.Exists(synthesisPath))
                {
                    File.Delete(synthesisPath);
                }
                
                // 2. Supprimer le fichier physique du document
                if (File.Exists(document.FilePath))
                {
                    File.Delete(document.FilePath);
                }
                
                // 3. Mettre à jour l'index JSON
                var documentsPath = _pathService.GetDocumentsDirectory(nomComplet);
                var indexPath = Path.Combine(documentsPath, IndexFileName);
                
                if (File.Exists(indexPath))
                {
                    var json = await File.ReadAllTextAsync(indexPath);
                    var documents = JsonSerializer.Deserialize<List<PatientDocument>>(json) ?? new List<PatientDocument>();
                    
                    // Retirer le document de la liste (comparaison par FilePath)
                    documents.RemoveAll(d => d.FilePath == document.FilePath);
                    
                    // Réécrire l'index
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var updatedJson = JsonSerializer.Serialize(documents, options);
                    await File.WriteAllTextAsync(indexPath, updatedJson);
                }
                
                return (true, "Document supprimé avec succès.");
            }
            catch (Exception ex)
            {
                return (false, $"Erreur lors de la suppression: {ex.Message}");
            }
        }
    }
}
