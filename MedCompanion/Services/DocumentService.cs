using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MedCompanion.Models;
using UglyToad.PdfPig;
using MedCompanion.Services.LLM; // ✅ AJOUT

namespace MedCompanion.Services
{
    /// <summary>
    /// Service de gestion des documents patients
    /// </summary>
    public class DocumentService
    {
        private readonly LLMGatewayService _llmGatewayService; // ✅ NOUVEAU
        private readonly PathService _pathService;
        private readonly LLMServiceFactory _llmFactory; // ✅ NOUVEAU pour le nettoyage local
        private readonly AppSettings _settings; // ✅ NOUVEAU pour AnonymizationModel
        private const string IndexFileName = "documents-index.json";

        public DocumentService(
            LLMGatewayService llmGatewayService, 
            PathService pathService, 
            LLMServiceFactory llmFactory,
            AppSettings settings)
        {
            _llmGatewayService = llmGatewayService;
            _pathService = pathService;
            _llmFactory = llmFactory;
            _settings = settings;
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
                
                // ÉTAPE 1 : Extraire le texte du document
                string extractedText = await ExtractTextFromFileAsync(sourceFilePath);
                
                if (string.IsNullOrWhiteSpace(extractedText))
                {
                    return (false, null, "Impossible d'extraire le texte du document.");
                }

                // ÉTAPE 1bis : Est-ce NOTRE formulaire de complétion qui revient rempli ?
                //
                // Court-circuit avant tout appel LLM. Ce document n'a rien à faire analyser : son
                // contenu utile est manuscrit, donc absent de la couche texte, et les trois appels
                // qui suivaient (nettoyage, analyse, synthèse détaillée) ne pouvaient produire
                // qu'une description du gabarit vierge. Constaté sur un cas réel : « Le formulaire
                // recueille les informations administratives… », « prévoit des sections pour les
                // coordonnées du père » — zéro information nouvelle, et le document repartait
                // ensuite pondéré à 0,7 dans la Synthèse Initiale.
                //
                // Sa valeur est ailleurs : dans la lecture champ par champ (FormulaireSaisieDialog).
                var (defFormulaire, versionFormulaire) = ReconnaitreFormulaire(extractedText);
                if (defFormulaire != null)
                {
                    var formulaire = new PatientDocument
                    {
                        FileName      = $"{DateTime.Now:yyyy-MM-dd}_formulaire_{defFormulaire.Id.ToLowerInvariant()}_rempli{extension}",
                        Category      = "Formulaires",
                        Summary       = $"{defFormulaire.Libelle} rempli par les parents — à lire champ par champ.",
                        ExtractedText = extractedText,
                        FileSizeBytes = fileInfo.Length,
                        FileExtension = extension,
                        DateAdded     = DateTime.Now,
                        IsFormulaireCompletion = true,
                        FormulaireId      = defFormulaire.Id,
                        FormulaireVersion = versionFormulaire
                    };

                    var dossierFormulaires = Path.Combine(
                        _pathService.GetDocumentsDirectory(nomComplet), formulaire.Category);
                    Directory.CreateDirectory(dossierFormulaires);
                    var destination = Path.Combine(dossierFormulaires, formulaire.FileName);
                    File.Copy(sourceFilePath, destination, overwrite: true);
                    formulaire.FilePath = destination;

                    await SaveDocumentToIndexAsync(nomComplet, formulaire);
                    return (true, formulaire, "Formulaire de complétion reconnu — prêt pour la lecture des champs.");
                }

                // ÉTAPE 2 : Nettoyage local via Ollama (Indépendant du patient, texte brut)
                string cleanedText = await CleanAndStructureOcrViaLocalLLMAsync(extractedText);

                // ÉTAPE 3 : Analyser avec l'IA pour catégorisation et nommage (via Gateway pour anonymisation)
                var analysis = await AnalyzeDocumentWithAIAsync(cleanedText, fileInfo.Name, nomComplet);

                // Créer l'objet document
                var document = new PatientDocument
                {
                    FileName = analysis.suggestedName + extension,
                    Category = analysis.category,
                    Summary = analysis.summary,
                    ExtractedText = cleanedText, // Sauvegarder le texte NETTOYÉ
                    FileSizeBytes = fileInfo.Length,
                    FileExtension = extension,
                    DateAdded = DateTime.Now
                };

                // ÉTAPE 3bis : Si un praticien auteur a été identifié dans le document, l'enregistrer
                // comme intervenant du patient (Admin du dossier bleu) — sauf s'il s'agit du médecin
                // traitant lui-même (ex. mention "J'autorise le Dr Lassoued..." dans un formulaire de
                // consentement, à tort extraite comme auteur du document par le LLM).
                if (analysis.intervenant != null && !string.IsNullOrWhiteSpace(analysis.intervenant.Nom) &&
                    !IsCurrentPractitioner(analysis.intervenant.Nom))
                {
                    analysis.intervenant.SourceDocument = document.FileName;
                    analysis.intervenant.SourceCategory = document.Category;
                    var infoPatientDir = _pathService.GetInfoPatientDirectory(nomComplet);
                    new IntervenantService().Add(infoPatientDir, analysis.intervenant);
                }
                
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
        /// <summary>
        /// Reconnaît le formulaire de complétion à son titre imprimé.
        ///
        /// Vérifié sur un scan réel : le titre ressort de la couche texte sous la forme
        /// « FORMULAIRE DE COMPLÉTION - 1RE CONSULTATION ». Deux pièges constatés sur ce cas, d'où
        /// la forme de ce test :
        ///  • la CASSE diffère du gabarit HTML (le titre y est en minuscules, mis en capitales par
        ///    la feuille de style) — comparaison insensible à la casse ;
        ///  • le titre n'apparaît PAS en tête du texte extrait mais au milieu, l'ordre de lecture du
        ///    PDF ne suivant pas celui de la page — on cherche donc dans tout le texte.
        /// Les accents sont neutralisés : un scan médiocre rend parfois « COMPLETION ».
        ///
        /// Un faux négatif est sans gravité : le document repart dans le circuit habituel.
        /// </summary>
        /// <summary>
        /// Au-delà de cette longueur de texte extrait, le document contient autre chose que le seul
        /// formulaire — typiquement plusieurs pièces passées au scanner en une fois.
        ///
        /// Mesuré sur les 332 documents du fonds : les 18 formulaires seuls tiennent entre 728 et
        /// 2 113 caractères normalisés (leur texte se réduit au gabarit imprimé, le manuscrit
        /// n'atteignant pas la couche texte), tandis qu'un scan mêlant un bilan psychomoteur et le
        /// formulaire atteint 3 208.
        /// </summary>
        private const int LongueurMaxFormulaireSeul = 2600;

        /// <summary>
        /// Reconnaît un formulaire à saisir dans le texte extrait, et avec quelle version de mise
        /// en page il a été imprimé.
        ///
        /// La version compte autant que le type : c'est elle qui désigne la géométrie de lecture.
        /// Un exemplaire remis aux parents il y a trois semaines et rendu aujourd'hui doit être relu
        /// avec le gabarit de son époque, sans quoi on cherche l'adresse là où se trouve désormais
        /// la situation familiale.
        /// </summary>
        internal static (FormulaireDefinition? definition, int version) ReconnaitreFormulaire(string? texteExtrait)
        {
            if (string.IsNullOrWhiteSpace(texteExtrait)) return (null, 0);

            var normalise = NormaliserPourComparaison(texteExtrait);
            var (definition, version) = FormulairesConnus.Reconnaitre(normalise);
            if (definition == null) return (null, 0);

            // Reconnaître ne suffit pas : un scan groupé contient le formulaire ET autre chose. Le
            // court-circuiter ferait perdre la synthèse d'une pièce réellement clinique — cas
            // rencontré en base, un bilan psychomoteur scanné avec le formulaire.
            //
            // L'erreur n'est pas symétrique : renoncer à l'optimisation sur un document ambigu ne
            // coûte que trois appels LLM, alors qu'un court-circuit à tort perd du contenu clinique.
            // On penche donc vers le circuit habituel dès qu'il y a du texte en trop.
            if (normalise.Length > LongueurMaxFormulaireSeul) return (null, 0);

            return (definition, version);
        }

        private static string NormaliserPourComparaison(string texte)
        {
            var decompose = texte.ToLowerInvariant().Normalize(System.Text.NormalizationForm.FormD);
            var sb = new System.Text.StringBuilder(decompose.Length);
            var espacePrecedent = false;

            foreach (var c in decompose)
            {
                if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c)
                    == System.Globalization.UnicodeCategory.NonSpacingMark) continue;

                if (char.IsLetterOrDigit(c)) { sb.Append(c); espacePrecedent = false; }
                else if (!espacePrecedent)   { sb.Append(' '); espacePrecedent = true; }
            }
            return sb.ToString();
        }

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
        /// Nettoie et structure le texte OCR via un LLM local (Ollama)
        /// Utilise le modèle spécifié dans les paramètres de confidentialité.
        /// </summary>
        public async Task<string> CleanAndStructureOcrViaLocalLLMAsync(string rawOcrText)
        {
            if (string.IsNullOrWhiteSpace(rawOcrText)) return string.Empty;

            try
            {
                System.Diagnostics.Debug.WriteLine($"[DocumentService] 🛡️ Nettoyage OCR (Local) - Début (Modèle: {_settings.AnonymizationModel})");

                // 1. Récupérer le modèle local spécifié
                string localModel = _settings.AnonymizationModel;
                
                // 2. Préparer le prompt de nettoyage
                var systemPrompt = @"Tu es un expert en traitement de texte médical. 
Ton rôle est de NETTOYER et de RESTRUCTURER le texte issu d'un OCR bruité.

RÈGLES :
1. Corrige les erreurs de lecture évidentes (ex: 'Patien1' -> 'Patient').
2. Reconstruit les paragraphes et les phrases pour qu'ils soient lisibles.
3. Supprime les artéfacts d'OCR (caractères spéciaux isolés, numéros de page mal placés).
4. CONSERVE l'intégralité des informations cliniques et des noms.
5. NE FAIS AUCUNE SYNTHÈSE, garde le texte intégral mais propre.
6. Retourne UNIQUEMENT le texte nettoyé, sans aucun commentaire.";

                var prompt = $@"Texte OCR brut :
--------------------
{rawOcrText}
--------------------

Tâche : Nettoie et restructure ce texte pour le rendre parfaitement lisible.";

                // 3. Utiliser le provider local via la Factory
                var currentProvider = _llmFactory.GetCurrentProvider();
                
                // Si le provider actuel n'est pas celui de l'anonymisation (local), on peut quand même tenter
                // mais l'objectif est d'utiliser le modèle local configuré.
                // On va simuler un appel "direct" si possible ou passer par le provider s'il est configuré en local.
                
                if (currentProvider is MedCompanion.Services.LLM.OllamaLLMProvider ollama)
                {
                    // Sauvegarder le modèle actuel pour le restaurer après
                    string originalModel = ollama.GetModelName();
                    
                    try 
                    {
                        // Basculer temporairement sur le modèle de nettoyage
                        ollama.SetModel(localModel);
                        
                        var messages = new List<(string role, string content)> 
                        {
                            ("system", systemPrompt),
                            ("user", prompt)
                        };

                        var (success, result, error) = await ollama.ChatAsync(systemPrompt, messages, 2048);
                        
                        if (success)
                        {
                            System.Diagnostics.Debug.WriteLine($"[DocumentService] ✓ Nettoyage OCR terminé ({result.Length} caractères)");
                            return result;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"[DocumentService] ✗ Échec nettoyage OCR: {error}");
                        }
                    }
                    finally
                    {
                        // Restaurer le modèle original
                        ollama.SetModel(originalModel);
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine("[DocumentService] ⚠️ Provider local non actif. Nettoyage OCR ignoré.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DocumentService] ✗ Erreur fatale nettoyage OCR: {ex.Message}");
            }

            return rawOcrText; // Fallback sur le texte brut en cas d'erreur
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
        private async Task<(string category, string suggestedName, string summary, Intervenant? intervenant)> AnalyzeDocumentWithAIAsync(
            string documentText,
            string originalFileName,
            string patientName) // Ajout patientName pour Gateway
        {
            try
            {
                var prompt = $@"Analyse ce document médical et fournis:
1. CATÉGORIE (un seul mot parmi: bilans, courriers, ordonnances, attestations, radiologies, analyses, autres)
2. NOM_FICHIER (format: AAAA-MM-JJ_type-description sans extension)
3. SYNTHESE (résumé en 2-3 phrases du contenu)
4. INTERVENANT : si le document est rédigé/signé par un professionnel identifiable (médecin, psychologue, orthophoniste, psychomotricien...), extrais ses coordonnées telles qu'elles apparaissent dans l'en-tête ou la signature. Sinon laisse les champs vides.

Document:
{documentText.Substring(0, Math.Min(documentText.Length, 2000))}

Réponds uniquement au format:
CATÉGORIE: [catégorie]
NOM: [nom_fichier]
SYNTHESE: [synthèse]
INTERVENANT_NOM: [prénom nom, ou vide]
INTERVENANT_PROFESSION: [ex: Psychologue clinicienne, ou vide]
INTERVENANT_ADRESSE: [adresse, ou vide]
INTERVENANT_CP_VILLE: [code postal et ville, ou vide]
INTERVENANT_TEL: [téléphone, ou vide]
INTERVENANT_EMAIL: [email, ou vide]";

                var messages = new List<(string role, string content)> { ("user", prompt) };
                // Prompt court (2000 caractères max) : une fenêtre de contexte réduite suffit largement
                // et évite le surcoût VRAM/temps d'une fenêtre de 65536 dimensionnée pour la dictée longue.
                var (success, response, error) = await _llmGatewayService.ChatAsync("", messages, patientName, numCtx: 8192);

                if (!success)
                {
                    System.Diagnostics.Debug.WriteLine($"[DocumentService] Erreur analyse IA: {error}");
                    return (DocumentCategories.Autres,
                            DateTime.Now.ToString("yyyy-MM-dd") + "_" + Path.GetFileNameWithoutExtension(originalFileName),
                            "Document médical",
                            null);
                }

                // Parser la réponse
                var lines = response.Split('\n');
                string category = DocumentCategories.Autres;
                string suggestedName = DateTime.Now.ToString("yyyy-MM-dd") + "_document";
                string summary = "Document médical";
                var intervenant = new Intervenant();

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
                    else if (line.StartsWith("INTERVENANT_NOM:", StringComparison.OrdinalIgnoreCase))
                    {
                        intervenant.Nom = CleanIntervenantField(line) ?? "";
                    }
                    else if (line.StartsWith("INTERVENANT_PROFESSION:", StringComparison.OrdinalIgnoreCase))
                    {
                        intervenant.Profession = CleanIntervenantField(line);
                    }
                    else if (line.StartsWith("INTERVENANT_ADRESSE:", StringComparison.OrdinalIgnoreCase))
                    {
                        intervenant.Adresse = CleanIntervenantField(line);
                    }
                    else if (line.StartsWith("INTERVENANT_CP_VILLE:", StringComparison.OrdinalIgnoreCase))
                    {
                        var cpVille = CleanIntervenantField(line);
                        var parts = cpVille?.Split(' ', 2);
                        if (parts != null && parts.Length == 2)
                        {
                            intervenant.CodePostal = parts[0].Trim();
                            intervenant.Ville = parts[1].Trim();
                        }
                        else
                        {
                            intervenant.Ville = cpVille;
                        }
                    }
                    else if (line.StartsWith("INTERVENANT_TEL:", StringComparison.OrdinalIgnoreCase))
                    {
                        intervenant.Telephone = CleanIntervenantField(line);
                    }
                    else if (line.StartsWith("INTERVENANT_EMAIL:", StringComparison.OrdinalIgnoreCase))
                    {
                        intervenant.Email = CleanIntervenantField(line);
                    }
                }

                return (category, suggestedName, summary, string.IsNullOrWhiteSpace(intervenant.Nom) ? null : intervenant);
            }
            catch
            {
                // Fallback en cas d'erreur IA
                return (DocumentCategories.Autres,
                        DateTime.Now.ToString("yyyy-MM-dd") + "_" + Path.GetFileNameWithoutExtension(originalFileName),
                        "Document médical",
                        null);
            }
        }

        /// <summary>
        /// Nettoie un champ INTERVENANT_* extrait de la réponse IA. Retourne null si vide
        /// ou si l'IA a répondu par un placeholder ("vide", "aucun", "n/a"...).
        /// </summary>
        private static string? CleanIntervenantField(string line)
        {
            var value = line.Split(':', 2)[1].Trim();
            if (string.IsNullOrWhiteSpace(value)) return null;

            var placeholders = new[] { "vide", "aucun", "aucune", "n/a", "na", "non identifié", "non identifie", "-" };
            if (placeholders.Contains(value.Trim('[', ']', '.').ToLowerInvariant()))
                return null;

            return value;
        }

        /// <summary>
        /// Vrai si <paramref name="intervenantNom"/> désigne le médecin traitant lui-même
        /// (comparaison sur le nom de famille depuis <c>AppSettings.Medecin</c>, robuste aux
        /// formulations "Dr X", "Dr X Y" ou "X" — l'objectif est d'éviter que le praticien qui
        /// utilise l'application se retrouve listé comme intervenant externe de son propre patient.
        /// </summary>
        private static bool IsCurrentPractitioner(string intervenantNom)
        {
            var medecin = AppSettings.Load().Medecin;
            if (string.IsNullOrWhiteSpace(medecin)) return false;

            var medecinWords = medecin.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => !w.Equals("Dr", StringComparison.OrdinalIgnoreCase) && w.Length > 2);

            return medecinWords.Any(w => intervenantNom.Contains(w, StringComparison.OrdinalIgnoreCase));
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
        /// Enregistre un PDF existant (chemin original conservé, aucune copie) dans l'index documents
        /// du patient pour qu'il apparaisse dans le panel DOCUMENTS sans import manuel.
        /// </summary>
        public async Task RegisterExistingDocumentAsync(string existingPdfPath, string nomComplet, string displayName = "")
        {
            if (!File.Exists(existingPdfPath)) return;
            // Créer le dossier documents/ s'il n'existe pas encore (nouveau patient sans import)
            var documentsPath = _pathService.GetDocumentsDirectory(nomComplet);
            Directory.CreateDirectory(documentsPath);
            var fi = new FileInfo(existingPdfPath);
            var doc = new PatientDocument
            {
                FileName      = string.IsNullOrEmpty(displayName) ? fi.Name : displayName,
                FilePath      = existingPdfPath,
                Category      = "restitutions",
                DateAdded     = fi.CreationTime,
                FileExtension = ".pdf",
                FileSizeBytes = fi.Length,
                Summary       = "Restitution 1er entretien — généré depuis le Mode Consultation."
            };
            await SaveDocumentToIndexAsync(nomComplet, doc);
        }

        /// <summary>
        /// Récupère tous les documents d'un patient
        /// </summary>
        public async Task<List<PatientDocument>> GetAllDocumentsAsync(string nomComplet)
        {
            var result = new List<PatientDocument>();

            try
            {
                var documentFolders = _pathService.GetAllYearDirectories(nomComplet, "documents");

                foreach (var documentsPath in documentFolders)
                {
                    var indexPath = Path.Combine(documentsPath, IndexFileName);

                    if (File.Exists(indexPath))
                    {
                        var json = await File.ReadAllTextAsync(indexPath);
                        var folderDocs = JsonSerializer.Deserialize<List<PatientDocument>>(json);
                        if (folderDocs != null)
                        {
                            result.AddRange(folderDocs);
                        }
                    }
                }

                return result.OrderByDescending(d => d.DateAdded).ToList();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DocumentService] Erreur GetAllDocumentsAsync: {ex.Message}");
                return result;
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

                var messages = new List<(string role, string content)> { ("user", prompt) };
                var (success, synthesis, error) = await _llmGatewayService.ChatAsync("", messages, nomComplet);
                
                if (!success)
                {
                    return $"Erreur lors de la génération de la synthèse: {error ?? synthesis}";
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
        /// Génère une synthèse d'un seul document spécifique avec anonymisation si nécessaire
        /// </summary>
        public async Task<(string synthesis, double relevanceWeight)> GenerateSingleDocumentSynthesisAsync(
            PatientDocument document,
            PatientMetadata? patientData = null)
        {
            if (document == null)
                return ("Aucun document fourni.", 0.0);

            try
            {
                // ✅ ÉTAPE 1 : Nettoyage OCR si non déjà fait (ou optionnel s'il est déjà propre)
                // Pour l'instant on considère que document.ExtractedText est déjà nettoyé s'il vient d'ImportDocumentAsync
                // Plafonné à 8000 caractères : la synthèse (résumé + points clés) n'a pas besoin du
                // document intégral, et un texte non borné pouvait faire exploser le temps de traitement
                // sur les bilans longs (plusieurs pages).
                const int MaxSynthesisInputChars = 8000;
                string fullText = document.ExtractedText ?? "";
                string contentToAnalyze = fullText.Length > MaxSynthesisInputChars
                    ? fullText.Substring(0, MaxSynthesisInputChars) + "\n[...document tronqué pour la synthèse...]"
                    : fullText;

                // ✅ ÉTAPE 2 : Préparer le prompt de synthèse
                var basePrompt = $@"Tu es un assistant médical. Analyse ce document patient et génère une synthèse détaillée en Markdown.

Document: {document.FileName}
Catégorie: {DocumentCategories.GetDisplayName(document.Category)}
Date: {document.DateAddedDisplay}

Contenu extrait:
{contentToAnalyze}

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
[Si applicable, recommandations ou suivi nécessaire]

---
ÉVALUATION IMPORTANCE (pour mise à jour synthèse patient) :

Évalue l'importance de ce document pour mettre à jour la synthèse patient (0.0 à 1.0) :

**Poids 0.9-1.0** : Bilan psychologique complet, compte-rendu hospitalisation, diagnostic majeur
**Poids 0.7-0.8** : Bilan médical important (MDPH, orthophoniste), courrier médecin spécialisé
**Poids 0.4-0.6** : Courrier école, compte-rendu consultation externe
**Poids 0.1-0.3** : Document administratif avec infos cliniques mineures

À la fin de ta synthèse, ajoute une ligne :
POIDS_SYNTHESE: X.X";

                string patientName = patientData != null ? $"{patientData.Prenom} {patientData.Nom}".Trim() : "";

                // ✅ ÉTAPE 3 : Appel Gateway (Anonymisation 3 phases + Chat + Désanonymisation automatique)
                var messages = new List<(string role, string content)> { ("user", basePrompt) };
                var (success, synthesis, error) = await _llmGatewayService.ChatAsync("", messages, patientName, numCtx: 8192);

                if (!success)
                {
                    return ($"Erreur lors de la génération de la synthèse:\n\n{error ?? synthesis}", 0.0);
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
                // Chercher dans tous les répertoires d'années potentiels
                var documentFolders = _pathService.GetAllYearDirectories(nomComplet, "documents");

                foreach (var documentsPath in documentFolders)
                {
                    var synthesisFolder = Path.Combine(documentsPath, "syntheses_documents");

                    if (!Directory.Exists(synthesisFolder))
                        continue;

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
                
                // 3. Mettre à jour l'index JSON (trouver l'index dans le dossier parent du document)
                var docDir = Path.GetDirectoryName(document.FilePath);
                if (docDir == null) return (false, "Impossible de déterminer le dossier du document.");
                
                var parentDir = Directory.GetParent(docDir)?.FullName;
                if (parentDir == null) return (false, "Impossible de déterminer le dossier racine des documents.");
                
                var indexPath = Path.Combine(parentDir, IndexFileName);
                
                if (File.Exists(indexPath))
                {
                    var json = await File.ReadAllTextAsync(indexPath);
                    var documents = JsonSerializer.Deserialize<List<PatientDocument>>(json) ?? new List<PatientDocument>();
                    
                    // Retirer le document de la liste (comparaison par FileName car FilePath peut être relatif ou changer)
                    documents.RemoveAll(d => d.FileName == document.FileName);
                    
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
